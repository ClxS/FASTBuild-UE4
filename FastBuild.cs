using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Xml;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using UnrealBuildTool.FBuild.BuildComponents;
using UnrealBuildTool.FBuild;

namespace UnrealBuildTool
{
    public class FASTBuild
    {     
        private static int MaxActionsToExecuteInParallel
        {
            get
            {
                return 0;
            }
        }
        private static int JobNumber { get; set; }
        private static int AliasBase { get; set; } = 1;

        /** The possible result of executing tasks with SN-DBS. */
        public enum ExecutionResult
        {
            Unavailable,
            TasksFailed,
            TasksSucceeded,
        }
        private enum BuildStep
        {
            CompileObjects,
            Link,
            CompileAndLink
        }
        
                
        private static readonly Dictionary<string, Compiler> Compilers = new Dictionary<string, Compiler>();
        private static readonly Dictionary<string, Linker> Linkers = new Dictionary<string, Linker>();

        /**
         * Used when debugging Actions outputs all action return values to debug out
         * 
         * @param	sender		Sending object
         * @param	e			Event arguments (In this case, the line of string output)
         */
        protected static void ActionDebugOutput(object sender, DataReceivedEventArgs e)
        {
            var Output = e.Data;
            if (Output == null)
            {
                return;
            }

            Log.TraceInformation(Output);
        }

        internal static ExecutionResult ExecuteLocalActions(List<Action> InLocalActions, Dictionary<Action, ActionThread> InActionThreadDictionary, int TotalNumJobs)
        {
            // Time to sleep after each iteration of the loop in order to not busy wait.
            const float loopSleepTime = 0.1f;

            ExecutionResult localActionsResult = ExecutionResult.TasksSucceeded;

            while (true)
            {
                // Count the number of pending and still executing actions.
                int NumUnexecutedActions = 0;
                int NumExecutingActions = 0;
                foreach (Action Action in InLocalActions)
                {
                    ActionThread ActionThread = null;
                    bool bFoundActionProcess = InActionThreadDictionary.TryGetValue(Action, out ActionThread);
                    if (bFoundActionProcess == false)
                    {
                        NumUnexecutedActions++;
                    }
                    else if (ActionThread != null)
                    {
                        if (ActionThread.bComplete == false)
                        {
                            NumUnexecutedActions++;
                            NumExecutingActions++;
                        }
                    }
                }

                // If there aren't any pending actions left, we're done executing.
                if (NumUnexecutedActions == 0)
                {
                    break;
                }

                // If there are fewer actions executing than the maximum, look for pending actions that don't have any outdated
                // prerequisites.
                foreach (Action Action in InLocalActions)
                {
                    ActionThread ActionProcess = null;
                    bool bFoundActionProcess = InActionThreadDictionary.TryGetValue(Action, out ActionProcess);
                    if (bFoundActionProcess == false)
                    {
                        if (NumExecutingActions < Math.Max(1, MaxActionsToExecuteInParallel))
                        {
                            // Determine whether there are any prerequisites of the action that are outdated.
                            bool bHasOutdatedPrerequisites = false;
                            bool bHasFailedPrerequisites = false;
                            foreach (FileItem PrerequisiteItem in Action.PrerequisiteItems)
                            {
                                if (PrerequisiteItem.ProducingAction != null && InLocalActions.Contains(PrerequisiteItem.ProducingAction))
                                {
                                    ActionThread PrerequisiteProcess = null;
                                    bool bFoundPrerequisiteProcess = InActionThreadDictionary.TryGetValue(PrerequisiteItem.ProducingAction, out PrerequisiteProcess);
                                    if (bFoundPrerequisiteProcess == true)
                                    {
                                        if (PrerequisiteProcess == null)
                                        {
                                            bHasFailedPrerequisites = true;
                                        }
                                        else if (PrerequisiteProcess.bComplete == false)
                                        {
                                            bHasOutdatedPrerequisites = true;
                                        }
                                        else if (PrerequisiteProcess.ExitCode != 0)
                                        {
                                            bHasFailedPrerequisites = true;
                                        }
                                    }
                                    else
                                    {
                                        bHasOutdatedPrerequisites = true;
                                    }
                                }
                            }

                            // If there are any failed prerequisites of this action, don't execute it.
                            if (bHasFailedPrerequisites)
                            {
                                // Add a null entry in the dictionary for this action.
                                InActionThreadDictionary.Add(Action, null);
                            }
                            // If there aren't any outdated prerequisites of this action, execute it.
                            else if (!bHasOutdatedPrerequisites)
                            {
                                ActionThread ActionThread = new ActionThread(Action, JobNumber, TotalNumJobs);
                                ActionThread.Run();

                                InActionThreadDictionary.Add(Action, ActionThread);

                                NumExecutingActions++;
                                JobNumber++;
                            }
                        }
                    }
                }

                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(loopSleepTime));
            }

            return localActionsResult;
        }

        public static List<BuildComponent> CompilationActions { get; set; }
        public static List<BuildComponent> LinkerActions { get; set; }
        public static List<BuildComponent> AllActions => CompilationActions.Concat(LinkerActions).ToList();

        private static void DeresponsifyActions(List<Action> actions)
        {
            // UE4.13 started to shove the entire argument into response files. This does not work
            // well with FASTBuild so we'll have to undo it.
            foreach(var action in actions)
            {
                if (!action.CommandArguments.StartsWith(" @\"") || !action.CommandArguments.EndsWith("\"") ||
                    action.CommandArguments.Count(f => f == '"') != 2) continue;

                var file = Regex.Match(action.CommandArguments, "(?<=@\")(.*?)(?=\")");
                if (!file.Success) continue;

                var arg = File.ReadAllText(file.Value);
                action.CommandArguments = arg;
            }
        }

        public static ExecutionResult ExecuteActions(List<Action> actions)
        {
            ExecutionResult fastBuildResult = ExecutionResult.TasksSucceeded;
            CompilationActions = null;
            LinkerActions = null;

            if (actions.Count <= 0)
            {
                return fastBuildResult;
            }

            if (IsAvailable() == false)
            {
                return ExecutionResult.Unavailable;
            }

            List<Action> unassignedObjects = new List<Action>();
            List<Action> unassignedLinks = new List<Action>();
            List<Action> miscActions = actions.Where(a => a.ActionType != ActionType.Compile && a.ActionType != ActionType.Link).ToList();
            Dictionary<Action, BuildComponent> fastbuildActions = new Dictionary<Action, BuildComponent>();

            DeresponsifyActions(actions);

            CompilationActions = GatherActionsObjects(actions.Where(a => a.ActionType == ActionType.Compile), ref unassignedObjects, ref fastbuildActions);
            LinkerActions = GatherActionsLink(actions.Where(a => a.ActionType == ActionType.Link), ref unassignedLinks, ref fastbuildActions);
            ResolveDependencies(CompilationActions, LinkerActions, fastbuildActions);

            Log.TraceInformation("Actions: "+actions.Count+" - Unassigned: "+unassignedObjects.Count);
            Log.TraceInformation("Misc Actions - FBuild: "+miscActions.Count);
            Log.TraceInformation("Objects - FBuild: "+ CompilationActions.Count+" -- Local: "+unassignedObjects.Count);
            var lg = 0;
            if (LinkerActions != null) lg = LinkerActions.Count;
            Log.TraceInformation("Link - FBuild: " + lg + " -- Local: " + unassignedLinks.Count);

            if (unassignedLinks.Count > 0)
            {
                throw new Exception("Error, unaccounted for lib! Cannot guarantee there will be no prerequisite issues. Fix it");
            }
            if (unassignedObjects.Count > 0)
            {
                var actionThreadDictionary = new Dictionary<Action, ActionThread>();
                ExecuteLocalActions(unassignedObjects, actionThreadDictionary, unassignedObjects.Count);
            }

            if (FASTBuildConfiguration.UseSinglePassCompilation)
            {
                RunFBuild(BuildStep.CompileAndLink, GenerateFBuildFileString(BuildStep.CompileAndLink, CompilationActions.Concat(LinkerActions).ToList()));
            }
            else
            {
                if (CompilationActions.Any())
                {
                    fastBuildResult = RunFBuild(BuildStep.CompileObjects, GenerateFBuildFileString(BuildStep.CompileObjects, CompilationActions));
                }
                if (fastBuildResult == ExecutionResult.TasksSucceeded)
                {
                    if (LinkerActions != null && LinkerActions.Any())
                    {
                        if (!BuildConfiguration.bFastbuildNoLinking)
                        {
                            fastBuildResult = RunFBuild(BuildStep.Link, GenerateFBuildFileString(BuildStep.Link, LinkerActions));
                        }
                    }
                }
            }
            return fastBuildResult;
        }

        private static void ResolveDependencies(List<BuildComponent> objectGroups, List<BuildComponent> linkGroups, Dictionary<Action, BuildComponent> fastbuildActions)
        {
            var actions = objectGroups.Concat(linkGroups);

            foreach (var action in fastbuildActions)
            {
                var prerequisites = action.Key.PrerequisiteItems
                    .Where(n => n.ProducingAction != null && (n.ProducingAction.ActionType == action.Key.ActionType));

                foreach (var prerequisite in prerequisites)
                {
                    if (fastbuildActions.ContainsKey(prerequisite.ProducingAction))
                    {
                        if (!action.Value.Dependencies.Contains(fastbuildActions[prerequisite.ProducingAction]))
                        {
                            action.Value.Dependencies.Add(fastbuildActions[prerequisite.ProducingAction]);
                        }
                    }
                }
            }
        }

        private static string GenerateFBuildFileString(BuildStep step, List<BuildComponent> actions)
        {
            var sb = new StringBuilder();
            sb.Append(FASTBuildCommon.EntryArguments);
            if (step == BuildStep.CompileObjects || step == BuildStep.CompileAndLink)
            {
                sb.Append(GenerateFBuildCompilers());
            }

            if (step == BuildStep.CompileAndLink)
            {
                sb.Append(GenerateFBuildNodeList("DLLListAlias", actions));
                sb.Append(GenerateFBuildTargets(false, actions.Any()));
            }
            else
            {
                if (actions.Count > 0)
                {
                    sb.Append(GenerateFBuildNodeList(step == BuildStep.CompileObjects ? "ObjectsListsAlias" : "DLLListAlias", actions));
                }
                sb.Append(GenerateFBuildTargets(step == BuildStep.CompileObjects && actions.Any(),
                                                        step == BuildStep.Link && actions.Any()));
            }
            return sb.ToString();
        }

        private static string GenerateFBuildCompilers()
        {
            StringBuilder sb = new StringBuilder();

            int objectCount = 0;
            foreach (var compiler in Compilers)
            {
                compiler.Value.Alias = "Compiler-" + objectCount;
                sb.Append(compiler.Value.ToString());
                objectCount++;
            }

            return sb.ToString();
        }

        private static string GenerateFBuildNodeList(string aliasName, IList<BuildComponent> objectGroups)
        {
            var sb = new StringBuilder();
            var aliases = new List<string>();

            // Ensure nodes are placed before nodes which depend on them.
            bool changed;
            do
            {
                changed = false;
                for (var i = 0; i < objectGroups.Count; ++i)
                {
                    if (!objectGroups[i].Dependencies.Any()) continue;

                    var highest = objectGroups[i].Dependencies.Select(objectGroups.IndexOf).OrderBy(n => n).Last();
                    if (highest > i)
                    {
                        var thisObject = objectGroups[i];
                        objectGroups.RemoveAt(i);
                        objectGroups.Insert(highest, thisObject);
                        changed = true;
                    }
                }
            } while (changed);

            foreach (var objectGroup in objectGroups)
            {
                aliases.Add(objectGroup.Alias);
                sb.Append(objectGroup.ToString());
            }

            if (aliases.Any())
            {
                sb.Append(FASTBuildCommon.GetAliasTag(aliasName, aliases));
            }
            return sb.ToString();
        }

        private static string GenerateFBuildTargets(bool anyObjects, bool anyLibs)
        {
            var aliases = new List<string>();
            if (anyObjects)
            {
                aliases.Add("ObjectsListsAlias");
            }
            if (anyLibs)
            {
                aliases.Add("DLLListAlias");
            }
            if (anyObjects || anyLibs)
            {
                return FASTBuildCommon.GetAliasTag("all", aliases);
            }
            else
            {
                return "";
            }
        }
        
        private static List<BuildComponent> GatherActionsObjects(IEnumerable<Action> compileActions, ref List<Action> unassignedActions, ref Dictionary<Action, BuildComponent> actionLinks)
        {
            var objectGroup = new List<ObjectGroupComponent>();
            
            foreach (var action in compileActions)
            {
                var obj = ParseCompilerAction(action);
                if (obj != null)
                {
                    var group = objectGroup.FirstOrDefault(n => n.MatchHash == obj.MatchHash);
                    if (group != null && !FASTBuildConfiguration.UseSinglePassCompilation)
                    {
                        group.ActionInputs[action] = obj.ActionInputs.FirstOrDefault().Value;
                    }
                    else
                    {
                        obj.AliasIndex = AliasBase++;
                        objectGroup.Add(obj);
                    }
                    actionLinks[action] = obj;
                }
                else
                {
                    Log.TraceInformation("Local Action - \n-Path:"+action.CommandPath+"\n-Args:"+action.CommandArguments);
                    unassignedActions.Add(action);
                }
            }
            return objectGroup.Cast<BuildComponent>().ToList();
        }

        private static List<BuildComponent> GatherActionsLink(IEnumerable<Action> localLinkActions, ref List<Action> unassignedActions, ref Dictionary<Action, BuildComponent> actionLinks)
        {
            var linkActions = new List<ExecutableComponent>();

            foreach (var action in localLinkActions)
            {
                var linkAction = ParseLinkAction(action);
                if (linkAction != null)
                {
                    linkActions.Add(linkAction);
                    linkAction.AliasIndex = AliasBase++;
                    actionLinks[action] = linkAction;
                }
                else
                {
                    Log.TraceInformation("Local Action - \n-Path:"+action.CommandPath+"\n-Args:"+action.CommandArguments);
                    unassignedActions.Add(action);
                }
            }
            return linkActions.Cast<BuildComponent>().ToList();
        }

        private static string LocaliseFilePath(string filepath)
        {
            Uri inputUri = new Uri(filepath);
            Uri currentUri = new Uri(Directory.GetCurrentDirectory());
            Uri relativeInputPath = currentUri.MakeRelativeUri(inputUri);
            return "../" + relativeInputPath.ToString();
        }

        private static ObjectGroupComponent ParseCompilerAction(Action action)
        {
            string[] compatableExtensions = { ".c", ".cpp", ".rc", ".inl" };
            ObjectGroupComponent outputAction = null;
            Compiler compiler;

            if (Compilers.ContainsKey(action.CommandPath))
            {
                compiler = Compilers[action.CommandPath];
            }
            else
            {
                compiler = new Compiler(action.CommandPath);
                Compilers[action.CommandPath] = compiler;
            }

            var inputMatches = Regex.Matches(action.CommandArguments, compiler.InputFileRegex, RegexOptions.IgnoreCase);
            var outputMatch = Regex.Match(action.CommandArguments, compiler.OutputFileRegex, RegexOptions.IgnoreCase);
            var usingPch = action.CommandArguments.Contains("/Yc");

            if (inputMatches.Count > 0)
            {
                var input = "";
                var outputPath = "";
                var outputExt = "";
                var matchHash = "";
                var args = action.CommandArguments;
                PchOptions pchOptions = null;

                foreach (Match inputMatch in inputMatches)
                {
                    if (compatableExtensions.Any(ext => inputMatch.Value.EndsWith(ext)))
                    {
                        input = inputMatch.Value;
                    }
                    if (!string.IsNullOrWhiteSpace(input))
                        break;
                }

                var output = outputMatch.Value;

                if (usingPch)
                {
                    pchOptions = new PchOptions();
                    outputMatch = Regex.Match(args, compiler.PchOutputRegex, RegexOptions.IgnoreCase);
                    pchOptions.Input = input; 
                    pchOptions.Output = outputMatch.Value;
                    pchOptions.Options = args;

                    pchOptions.Options = pchOptions.Options.Replace(input, "%1");
                    pchOptions.Options = pchOptions.Options.Replace(outputMatch.Value, "%2");

                    args = args.Replace("/Yc", "/Yu");
                }
                args = args.Replace(" c ", "");
                args = args.Replace(output, "%2");
                args = args.Replace(input, "%1");

                var pathExtSplit = output.Split('.');
                outputPath = Path.GetDirectoryName(output) + Path.DirectorySeparatorChar;
                outputExt = "." + pathExtSplit[pathExtSplit.Length - 2] + "." + pathExtSplit[pathExtSplit.Length - 1];

                if (outputExt == ".h.obj")
                {
                    outputExt = ".obj";
                }
                
                var dependencies = action.PrerequisiteItems.OrderBy(n => n.AbsolutePath).Select(n => n.AbsolutePath);

                if (pchOptions != null)
                    matchHash = args + outputPath + outputExt + action.CommandPath + pchOptions.Options + pchOptions.Input + pchOptions.Output + dependencies;
                else
                    matchHash = args + outputPath + outputExt + action.CommandPath + dependencies;

                outputAction = new ObjectGroupComponent()
                {
                    MatchHash = matchHash,
                    CompilerArguments = args,
                    OutputPath = outputPath,
                    OutputExt = outputExt,
                    LocalOnly = !action.bCanExecuteRemotely,
                    PchOptions = pchOptions,
                    ObjectCompiler = compiler
                };
                outputAction.ActionInputs[action] = input;
            }
            return outputAction;
        }

        private static ExecutableComponent ParseLinkAction(Action action)
        {
            ExecutableComponent output = null;
            Linker linker;

            if (Linkers.ContainsKey(action.CommandPath))
            {
                linker = Linkers[action.CommandPath];
            }
            else
            {
                linker = new Linker(action.CommandPath);
                Linkers[action.CommandPath] = linker;
            }

            var linkerFound = false;
            if (Linker.IsKnownLinker(action.CommandPath))
            {
                var inputMatchesRegex = Regex.Matches(action.CommandArguments, linker.InputFileRegex, RegexOptions.IgnoreCase);
                var importLibMatchesRegex = Regex.Matches(action.CommandArguments, linker.ImportLibraryRegex, RegexOptions.IgnoreCase);                 
                var outputMatchesRegex = Regex.Matches(action.CommandArguments, linker.OutputFileRegex, RegexOptions.IgnoreCase);
                var inputMatches = inputMatchesRegex.Cast<Match>().Where(n => linker.AllowedInputTypes.Any(a => n.Value.EndsWith(a))).ToList();
                var outputMatches = outputMatchesRegex.Cast<Match>().Where(n => linker.AllowedOutputTypes.Any(a => n.Value.EndsWith(a))).ToList();
                var importMatches = importLibMatchesRegex.Cast<Match>().ToList();

                if (inputMatches.Count > 0 && outputMatches.Count == 1)
                {
                    linkerFound = true;
                    output = new LinkerComponent()
                    {
                        Action = action,
                        Linker = linker,
                        Arguments = action.CommandArguments,
                        OutputLibrary = outputMatches[0].Value,
                        ImportLibrary = importMatches.Count > 0 ? importMatches[0].Value : null,
                        Inputs = inputMatches.Select(n => n.Value).ToList(),
                        LocalOnly = !action.bCanExecuteRemotely
                    };
                }
            }

            if (!linkerFound)
            {
                output = new ExecutableComponent()
                {
                    Action = action,
                    Arguments = action.CommandArguments,
                    Linker = linker
                };
            }
            return output;
        }

        private static ExecutionResult RunFBuild(BuildStep step, string bffString)
        {
            ExecutionResult result;
            try
            {
                var watch = Stopwatch.StartNew();
                Log.TraceInformation(step == BuildStep.CompileObjects ? "Building Objects" : "Linking Objects");

                var distScriptFilename = Path.Combine(BuildConfiguration.BaseIntermediatePath, "fbuild.bff");
                var distScriptFileStream = new FileStream(distScriptFilename, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

                var scriptFile = new StreamWriter(distScriptFileStream)
                {
                    AutoFlush = true
                };
                scriptFile.WriteLine(ActionThread.ExpandEnvironmentVariables(bffString));
                scriptFile.Flush();
                scriptFile.Close();
                scriptFile.Dispose();
                scriptFile = null;

                result = DispatchFBuild();
                watch.Stop();
                var elapsedMs = watch.ElapsedMilliseconds;
                Log.TraceInformation((step == BuildStep.CompileObjects ? "Object Compilation" : "Object Linking") + " Finished. Execution Time: "+elapsedMs);                
            }
            catch (Exception)
            {
                result = ExecutionResult.TasksFailed;
            }

            return result;
        }

        private static ExecutionResult DispatchFBuild()
        {
            ProcessStartInfo PSI = new ProcessStartInfo(Path.GetFullPath("../Extras/FASTBuild/") + "FBuild.exe", ""
                + (FASTBuildConfiguration.IsDistrbuted ? "-dist " : "")
                + (FASTBuildConfiguration.UseCache ? (FASTBuildConfiguration.EnableCacheGenerationMode ? "-cache " : "-cacheread ") : "")
                + (BuildConfiguration.bFastbuildContinueOnError ? "-nostoponerror " : "")
                + FASTBuildConfiguration.AdditionalArguments
                + " -clean"
                + " -config " + BuildConfiguration.BaseIntermediatePath + "/fbuild.bff");
            Log.TraceInformation("Arguments: "+PSI.Arguments);
            Log.TraceInformation("Cache Path: "+FASTBuildConfiguration.CachePath);
            Log.TraceInformation("Use Cache: " + (FASTBuildConfiguration.UseCache ? "Yes" : "No"));
            Log.TraceInformation("Is Distributed: " + (FASTBuildConfiguration.IsDistrbuted ? "Yes" : "No"));

            PSI.RedirectStandardOutput = true;
            PSI.RedirectStandardError = true;
            PSI.UseShellExecute = false;
            PSI.CreateNoWindow = true;
            PSI.WorkingDirectory = Path.GetFullPath(".");
            var newProcess = new Process
            {
                StartInfo = PSI
            };
            var output = new DataReceivedEventHandler(ActionDebugOutput);
            newProcess.OutputDataReceived += output;
            newProcess.ErrorDataReceived += output;
            newProcess.Start();
            newProcess.BeginOutputReadLine();
            newProcess.BeginErrorReadLine();
            newProcess.WaitForExit();
            newProcess.OutputDataReceived -= output;
            newProcess.ErrorDataReceived -= output;

            return newProcess.ExitCode == 0 ? ExecutionResult.TasksSucceeded : ExecutionResult.TasksFailed;
        }

        public static bool IsAvailable()
        {
            var fbRoot = Path.GetFullPath("../Extras/FASTBuild/");

            var fbExists = false;

            if (fbRoot == null) return false;

            var fbExecutable = Path.Combine(fbRoot, "FBuild.exe");
            var compilerDir = Path.Combine(fbRoot, "External/VS15.0");
            var sdkDir = Path.Combine(fbRoot, "External/Windows8.1");

            // Check that FASTBuild is available
            fbExists = File.Exists(fbExecutable);
            if (fbExists)
            {
                if (!Directory.Exists(compilerDir))
                {
                    fbExists = false;
                }
                if (!Directory.Exists(sdkDir))
                {
                    fbExists = false;
                }
            }
            return fbExists;
        }

    }
}
