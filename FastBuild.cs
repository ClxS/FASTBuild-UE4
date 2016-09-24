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
        static private int MaxActionsToExecuteInParallel
        {
            get
            {
                return 0;
            }
        }
        static private int JobNumber { get; set; }

        private static int _AliasBase=1;
        private static int AliasBase
        {
            get
            {
                return _AliasBase;
            }
            set
            {
                _AliasBase = value;
            }
        } 

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
        
                
        static private Dictionary<string, Compiler> Compilers = new Dictionary<string, Compiler>();
        static private Dictionary<string, Linker> Linkers = new Dictionary<string, Linker>();

        /**
         * Used when debugging Actions outputs all action return values to debug out
         * 
         * @param	sender		Sending object
         * @param	e			Event arguments (In this case, the line of string output)
         */
        static protected void ActionDebugOutput(object sender, DataReceivedEventArgs e)
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
            const float LoopSleepTime = 0.1f;

            ExecutionResult LocalActionsResult = ExecutionResult.TasksSucceeded;

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

                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(LoopSleepTime));
            }

            return LocalActionsResult;
        }

        public static List<BuildComponent> CompilationActions { get; set; }
        public static List<BuildComponent> LinkerActions { get; set; }
        public static List<BuildComponent> AllActions
        {
            get
            {
                return CompilationActions.Concat(LinkerActions).ToList();
            }
        }

        private static void DeresponsifyActions(List<Action> Actions)
        {
            // UE4.13 started to shove the entire argument into response files. This does not work
            // well with FASTBuild so we'll have to undo it.
            foreach(var action in Actions)
            {
                if(action.CommandArguments.StartsWith(" @\"") && action.CommandArguments.EndsWith("\"") &&
                    action.CommandArguments.Count(f => f == '"') == 2)
                {
                    var file = Regex.Match(action.CommandArguments, "(?<=@\")(.*?)(?=\")");
                    if(file.Success)
                    {
                        var arg = File.ReadAllText(file.Value);
                        action.CommandArguments = arg;
                    }
                }
            }
        }

        public static ExecutionResult ExecuteActions(List<Action> Actions)
        {
            ExecutionResult FASTBuildResult = ExecutionResult.TasksSucceeded;
            CompilationActions = null;
            LinkerActions = null;

            if (Actions.Count > 0)
            {
                if (IsAvailable() == false)
                {
                    return ExecutionResult.Unavailable;
                }

                List<Action> UnassignedObjects = new List<Action>();
                List<Action> UnassignedLinks = new List<Action>();
                List<Action> MiscActions = Actions.Where(a => a.ActionType != ActionType.Compile && a.ActionType != ActionType.Link).ToList();
                Dictionary<Action, BuildComponent> FastbuildActions = new Dictionary<Action, BuildComponent>();

                DeresponsifyActions(Actions);

                CompilationActions = GatherActionsObjects(Actions.Where(a => a.ActionType == ActionType.Compile), ref UnassignedObjects, ref FastbuildActions);
                LinkerActions = GatherActionsLink(Actions.Where(a => a.ActionType == ActionType.Link), ref UnassignedLinks, ref FastbuildActions);
                ResolveDependencies(CompilationActions, LinkerActions, FastbuildActions);

                Log.TraceInformation("Actions: "+Actions.Count+" - Unassigned: "+UnassignedObjects.Count);
                Log.TraceInformation("Misc Actions - FBuild: "+MiscActions.Count);
                Log.TraceInformation("Objects - FBuild: "+ CompilationActions.Count+" -- Local: "+UnassignedObjects.Count);
                int lg = 0;
                if (LinkerActions != null) lg = LinkerActions.Count;
                Log.TraceInformation("Link - FBuild: "+lg+" -- Local: "+UnassignedLinks.Count);

                if (UnassignedLinks.Count > 0)
                {
                    throw new Exception("Error, unaccounted for lib! Cannot guarantee there will be no prerequisite issues. Fix it");
                }
                if (UnassignedObjects.Count > 0)
                {
                    Dictionary<Action, ActionThread> ActionThreadDictionary = new Dictionary<Action, ActionThread>();
                    ExecuteLocalActions(UnassignedObjects, ActionThreadDictionary, UnassignedObjects.Count);
                }

                if (FASTBuildConfiguration.UseSinglePassCompilation)
                {
                    RunFBuild(BuildStep.CompileAndLink, GenerateFBuildFileString(BuildStep.CompileAndLink, CompilationActions.Concat(LinkerActions).ToList()));
                }
                else
                {
                    if (CompilationActions.Any())
                    {
                        FASTBuildResult = RunFBuild(BuildStep.CompileObjects, GenerateFBuildFileString(BuildStep.CompileObjects, CompilationActions));
                    }
                    if (FASTBuildResult == ExecutionResult.TasksSucceeded)
                    {
                        if (LinkerActions.Any())
                        {
                            if (!BuildConfiguration.bFastbuildNoLinking)
                            {
                                FASTBuildResult = RunFBuild(BuildStep.Link, GenerateFBuildFileString(BuildStep.Link, LinkerActions));
                            }
                        }
                    }
                }


            }
            return FASTBuildResult;
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
            StringBuilder sb = new StringBuilder();
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
                sb.Append(GenerateFBuildTargets(step == BuildStep.CompileObjects ? actions.Any() : false,
                                                        step == BuildStep.Link ? actions.Any() : false));
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

        private static string GenerateFBuildNodeList(string aliasName, List<BuildComponent> ObjectGroups)
        {
            StringBuilder sb = new StringBuilder();
            List<string> aliases = new List<string>();

            // Ensure nodes are placed before nodes which depend on them.
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < ObjectGroups.Count; ++i)
                {
                    if (ObjectGroups[i].Dependencies.Any())
                    {
                        var highest = ObjectGroups[i].Dependencies.Select(n => ObjectGroups.IndexOf(n)).OrderBy(n => n).Last();
                        if (highest > i)
                        {
                            var thisObject = ObjectGroups[i];
                            ObjectGroups.RemoveAt(i);
                            ObjectGroups.Insert(highest, thisObject);
                            changed = true;
                        }
                    }
                }
            } while (changed);

            foreach (var objectGroup in ObjectGroups)
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
            List<string> aliases = new List<string>();
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
        
        private static List<BuildComponent> GatherActionsObjects(IEnumerable<Action> CompileActions, ref List<Action> UnassignedActions, ref Dictionary<Action, BuildComponent> actionLinks)
        {
            //Debugger.Launch();
            List<ObjectGroupComponent> objectGroup = new List<ObjectGroupComponent>();
            
            foreach (var action in CompileActions)
            {
                ObjectGroupComponent obj = ParseCompilerAction(action);
                if (obj != null)
                {
                    var group = objectGroup.FirstOrDefault(n => n.MatchHash == obj.MatchHash);
                    //if (group != null)
                    //{
                    //    group.ActionInputs[action] = obj.ActionInputs.FirstOrDefault().Value;
                    //}
                    //else
                    //{
                        obj.AliasIndex = AliasBase++;
                        objectGroup.Add(obj);
                    //}
                    actionLinks[action] = obj;
                }
                else
                {
                    Log.TraceInformation("Local Action - \n-Path:"+action.CommandPath+"\n-Args:"+action.CommandArguments);
                    UnassignedActions.Add(action);
                }
            }
            return objectGroup.Cast<BuildComponent>().ToList();
        }

        private static List<BuildComponent> GatherActionsLink(IEnumerable<Action> LocalLinkActions, ref List<Action> UnassignedActions, ref Dictionary<Action, BuildComponent> actionLinks)
        {
            List<ExecutableComponent> linkActions = new List<ExecutableComponent>();

            foreach (var action in LocalLinkActions)
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
                    UnassignedActions.Add(action);
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
            //var PCHMatch = Regex.Match(action.CommandArguments, "", RegexOptions.IgnoreCase);
            var usingPch = action.CommandArguments.Contains("/Yc");

            if (inputMatches.Count > 0)
            {
                string input = "";
                string outputPath = "";
                string outputExt = "";
                string matchHash = "";
                string args = action.CommandArguments;
                PchOptions pchOptions = null;

                foreach (Match inputMatch in inputMatches)
                {
                    foreach (var ext in compatableExtensions)
                    {
                        if (inputMatch.Value.EndsWith(ext))
                        {
                            input = inputMatch.Value;
                            break;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(input))
                        break;
                }

                string output = outputMatch.Value;

                if (usingPch)
                {
                    pchOptions = new PchOptions();
                    outputMatch = Regex.Match(args, compiler.PCHOutputRegex, RegexOptions.IgnoreCase);
                    pchOptions.Input = input; // LocaliseFilePath(input);
                    pchOptions.Output = outputMatch.Value; // LocaliseFilePath(outputMatch.Value);
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

                //TODO[CJ] This probably isn't needed?
                //input = LocaliseFilePath(input);
                //outputPath = LocaliseFilePath(outputPath);

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
            //Debugger.Launch();
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

            bool linkerFound = false;
            if (Linker.IsKnownLinker(action.CommandPath))
            {
                var inputMatchesRegex = Regex.Matches(action.CommandArguments, linker.InputFileRegex, RegexOptions.IgnoreCase);
                var importLibMatchesRegex = Regex.Matches(action.CommandArguments, linker.ImportLibraryRegex, RegexOptions.IgnoreCase);                 
                var outputMatchesRegex = Regex.Matches(action.CommandArguments, linker.OutputFileRegex, RegexOptions.IgnoreCase);
                var inputMatches = inputMatchesRegex.Cast<Match>().Where(n => linker.AllowedInputTypes.Where(a => n.Value.EndsWith(a)).Any()).ToList();
                var outputMatches = outputMatchesRegex.Cast<Match>().Where(n => linker.AllowedOutputTypes.Where(a => n.Value.EndsWith(a)).Any()).ToList();
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

                StreamWriter ScriptFile;
                string distScriptFilename = Path.Combine(BuildConfiguration.BaseIntermediatePath, "fbuild.bff");
                FileStream distScriptFileStream = new FileStream(distScriptFilename, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                ScriptFile = new StreamWriter(distScriptFileStream);
                ScriptFile.AutoFlush = true;
                ScriptFile.WriteLine(ActionThread.ExpandEnvironmentVariables(bffString));
                ScriptFile.Flush();
                ScriptFile.Close();
                ScriptFile.Dispose();
                ScriptFile = null;

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
            Process NewProcess = new Process();
            NewProcess.StartInfo = PSI;
            var output = new DataReceivedEventHandler(ActionDebugOutput);
            NewProcess.OutputDataReceived += output;
            NewProcess.ErrorDataReceived += output;
            NewProcess.Start();
            NewProcess.BeginOutputReadLine();
            NewProcess.BeginErrorReadLine();
            NewProcess.WaitForExit();
            NewProcess.OutputDataReceived -= output;
            NewProcess.ErrorDataReceived -= output;

            return NewProcess.ExitCode == 0 ? ExecutionResult.TasksSucceeded : ExecutionResult.TasksFailed;
        }

        public static bool IsAvailable()
        {
            string FBRoot = Path.GetFullPath("../Extras/FASTBuild/");

            bool bFBExists = false;

            if (FBRoot != null)
            {
                string FBExecutable = Path.Combine(FBRoot, "FBuild.exe");
                string CompilerDir = Path.Combine(FBRoot, "External/VS15.0");
                string SdkDir = Path.Combine(FBRoot, "External/Windows8.1");

                // Check that FASTBuild is available
                bFBExists = File.Exists(FBExecutable);
                if (bFBExists)
                {
                    if (!Directory.Exists(CompilerDir))
                    {
                        bFBExists = false;
                    }
                    if (!Directory.Exists(SdkDir))
                    {
                        bFBExists = false;
                    }
                }
            }
            return bFBExists;
        }

    }
}
