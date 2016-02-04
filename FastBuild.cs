using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Xml;
using System.Text.RegularExpressions;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Management;
using System.Text;

namespace UnrealBuildTool
{
    public class FASTBuild
    {
        static private int MaxActionsToExecuteInParallel = 0;
        static private int JobNumber;

        static private bool UseCache
        {
            get
            {
                return true;
            }
        }
        static private bool? _allowCacheWrite = null;
        static public bool EnableCacheGenerationMode
        {
            get
            {
                if (!_allowCacheWrite.HasValue)
                {
                    //Add your own way of checking here. I check whether it's running on the CI machine in my case.
                }
                return _allowCacheWrite.Value;
            }

        }
        static public bool IsDistrbuted
        {
            get
            {
                return true;
            }
        }
        private struct FastBuildCommon
        {
            public static string EntryArguments
            {
                get
                {
                    string EntryArguments = ";-------------------------------------------------------------------------------\r\n";
                    EntryArguments += "; Windows Platform\r\n";
                    EntryArguments += ";-------------------------------------------------------------------------------\r\n";
                    if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2013)
                    {
                        EntryArguments += ".VSBasePath          = '../Extras/FASTBuild/External/VS13.4/VC'\r\n";
                    }
                    else if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2015)
                    {
                        EntryArguments += ".VSBasePath          = '../Extras/FASTBuild/External/VS15.0/VC'\r\n";
                    }
                    EntryArguments += ".ClangBasePath           = '../Extras/FASTBuild/External/LLVM'\r\n";
                    EntryArguments += ".WindowsSDKBasePath  = '../Extras/FASTBuild/External/Windows8.1'\r\n";
                    EntryArguments += ".OrbisSDK            = '../Extras/FASTBuild/External/Orbis'\r\n";

                    EntryArguments += ";-------------------------------------------------------------------------------\r\n";
                    EntryArguments += "; Base (library) includes\r\n";
                    EntryArguments += ";-------------------------------------------------------------------------------\r\n";
                    EntryArguments += ".BaseIncludePaths        = ' /I\"$VSBasePath$/include/\"'\r\n";
                    EntryArguments += "                         + ' /I\"$VSBasePath$/atlmfc/include/\"'\r\n";
                    EntryArguments += "                         + ' /I\"$WindowsSDKBasePath$/include/um/\"'\r\n";
                    EntryArguments += "                         + ' /I\"$WindowsSDKBasePath$/include/shared/\"'\r\n";
                    EntryArguments += "                         + ' /I\"$WindowsSDKBasePath$/include/winrt/\"'\r\n";                   

                    return EntryArguments;
                }
            }
            public static string GetAliasTag(string alias, List<string> targets)
            {
                string output = "";
                int targetCount = targets.Count;
                output += "Alias( '" + alias + "' )\r\n";
                output += "{\r\n";
                output += " .Targets = {";
                for (int i = 0; i < targetCount; ++i)
                {
                    output += "'" + targets[i] + "'";
                    if (i < targetCount - 1)
                    {
                        output += ",";
                    }
                }
                output += " }\r\n";
                output += "}\r\n";
                return output;

            }
        }
        public enum ExecutionResult
        {
            Unavailable,
            TasksFailed,
            TasksSucceeded,
        }
        private enum BuildStep
        {
            CompileObjects,
            Link
        }
        private enum CompilerType
        {
            MSVC,
            RC
        }
        private class Compiler : Linker
        {
            public Compiler(string exePath)
                : base(exePath)
            {
                LocaliseCompilerPath();
            }
            public override string InputFileRegex
            {
                get
                {
                    return "(?<= \")(.*?)(?=\")";
                }
            }
            public override string OutputFileRegex
            {
                get
                {
                    if (Type == CompilerType.MSVC || Type == CompilerType.RC)
                    {
                        return "(?<=(/Fo \"|/Fo\"))(.*?)(?=\")";
                    }
                    else
                    {
                        return "(?<=(-o \"|-o\"))(.*?)(?=\")";
                    }
                }
            }
            public override string PCHOutputRegex
            {
                get
                {
                    if (Type == CompilerType.MSVC || Type == CompilerType.RC)
                    {
                        return "(?<=(/Fp \"|/Fp\"))(.*?)(?=\")";
                    }
                    else
                    {
                        return "(?<=(/Fp \"|/Fp\"))(.*?)(?=\")";
                    }
                }
            }
            public string Alias;

            public string GetBffArguments(string arguments)
            {
                StringBuilder output = new StringBuilder();
                output.AppendFormat(" .CompilerOptions\t = '{0}'\r\n", arguments);                
                return output.ToString();
            }
            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Compiler('{0}')\n{{\n", Alias);
                sb.AppendFormat("\t.Executable\t\t            = '{0}' \n " +
                                "\t.ExtraFiles\t\t  = {{ {1} }}\n",
                                ExecPath,
                                string.Join("\n\t\t\t", GetExtraFiles().Select(e => "\t\t'" + e + "'")));

                sb.Append("}\n");
                return sb.ToString();
            }
            private List<string> GetExtraFiles()
            {
                List<string> output = new List<string>();

                string msvcVer = "";
                if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2013)
                {
                    msvcVer = "120";
                }
                else if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2015)
                {
                    msvcVer = "140";
                }

                string compilerDir = Path.GetDirectoryName(ExecPath);
                string msIncludeDir;

                if (Type == CompilerType.MSVC)
                {
                    output.Add(compilerDir + "\\1033\\clui.dll");
                    if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2013)
                    {
                        output.Add(compilerDir + "\\c1ast.dll");
                        output.Add(compilerDir + "\\c1xxast.dll");
                    }
                    output.Add(compilerDir + "\\c1xx.dll");
                    output.Add(compilerDir + "\\c2.dll");
                    output.Add(compilerDir + "\\c1.dll");

                    if (compilerDir.Contains("x86_amd64") || compilerDir.Contains("amd64_x86"))
                    {
                        msIncludeDir = "$VSBasePath$\\bin"; //We need to include the x86 version of the includes
                    }
                    else
                    {
                        msIncludeDir = compilerDir;
                    }

                    output.Add(compilerDir + "\\msobj" + msvcVer + ".dll");
                    output.Add(compilerDir + "\\mspdb" + msvcVer + ".dll");
                    output.Add(compilerDir + "\\mspdbsrv.exe");
                    output.Add(compilerDir + "\\mspdbcore.dll");
                    output.Add(compilerDir + "\\mspft" + msvcVer + ".dll");
                }
                return output;
            }
            private void LocaliseCompilerPath()
            {
                string compilerPath = "";
                if (ExecPath.Contains("cl.exe"))
                {
                    string[] compilerPathComponents = ExecPath.Replace('\\', '/').Split('/');
                    int startIndex = Array.FindIndex(compilerPathComponents, row => row == "VC");
                    if (startIndex > 0)
                    {
                        Type = CompilerType.MSVC;
                        compilerPath = "$VSBasePath$";
                        for (int i = startIndex + 1; i < compilerPathComponents.Length; ++i)
                        {
                            compilerPath += "/" + compilerPathComponents[i];
                        }
                    }
                    ExecPath = compilerPath;
                }
                else if (ExecPath.Contains("rc.exe"))
                {
                    Type = CompilerType.RC;
                    string[] compilerPathComponents = ExecPath.Replace('\\', '/').Split('/');
                    compilerPath = "$WindowsSDKBasePath$";
                    int startIndex = Array.FindIndex(compilerPathComponents, row => row == "8.1");
                    if (startIndex > 0)
                    {
                        for (int i = startIndex + 1; i < compilerPathComponents.Length; ++i)
                        {
                            compilerPath += "/" + compilerPathComponents[i];
                        }
                    }
                    ExecPath = compilerPath;
                }
            }
        }
        private class Linker
        {
            public Linker(string execPath)
            {
                ExecPath = execPath;
                LocaliseLinkerPath();
            }

            private void LocaliseLinkerPath()
            {
                string compilerPath = "";
                if (ExecPath.Contains("link.exe") || ExecPath.Contains("lib.exe"))
                {
                    string[] compilerPathComponents = ExecPath.Replace('\\', '/').Split('/');
                    int startIndex = Array.FindIndex(compilerPathComponents, row => row == "VC");
                    if (startIndex > 0)
                    {
                        Type = CompilerType.MSVC;
                        compilerPath = "$VSBasePath$";
                        for (int i = startIndex + 1; i < compilerPathComponents.Length; ++i)
                        {
                            compilerPath += "/" + compilerPathComponents[i];
                        }
                    }
                    ExecPath = compilerPath;
                }
            }

            public static bool IsKnownLinker(string args)
            {
                return args.Contains("lib.exe") || args.Contains("link.exe") || args.Contains("orbis-clang.exe") || args.Contains("orbis-snarl.exe");

            }
            public string ExecPath { get; set; }
            public CompilerType Type;

            private List<string> _allowedInputTypes;
            public virtual List<string> AllowedInputTypes
            {
                get
                {
                    if (_allowedInputTypes == null)
                    {
                        if (Type == CompilerType.MSVC)
                        {
                            _allowedInputTypes = new List<string>() { ".response", ".lib", ".obj" };
                        }
                    }
                    return _allowedInputTypes;
                }
            }

            private List<string> _allowedOutputTypes;
            public virtual List<string> AllowedOutputTypes
            {
                get
                {
                    if (_allowedOutputTypes == null)
                    {
                        if (Type == CompilerType.MSVC)
                        {
                            _allowedOutputTypes = new List<string>() { ".dll", ".lib", ".exe" };
                        }
                    }
                    return _allowedOutputTypes;
                }
            }

            public virtual string InputFileRegex
            {
                get
                {                    
                    return "(?<=@\")(.*?)(?=\")";
                }
            }
            public virtual string OutputFileRegex
            {
                get
                {
                    if (Type == CompilerType.MSVC || Type == CompilerType.RC)
                    {
                        return "(?<=(/OUT: \"|/OUT:\"))(.*?)(?=\")";
                    }
                    return "";
                }
            }
            public virtual string PCHOutputRegex
            {
                get
                {
                    if (Type == CompilerType.MSVC || Type == CompilerType.RC)
                    {
                        return "(?<=(/Fp \"|/Fp\"))(.*?)(?=\")";
                    }
                    else
                    {
                        return "(?<=(/Fp \"|/Fp\"))(.*?)(?=\")";
                    }
                }
            }

        }

        private abstract class FastbuildAction
        {
            public FastbuildAction()
            {
                Dependencies = new List<FastbuildAction>();
            }
            public Action Action { get; set; }
            public string NodeType { get; set; }
            public int AliasIndex { get; set; }
            public List<FastbuildAction> Dependencies { get; set; }

            public string Alias
            {
                get
                {
                    return NodeType + "-" + AliasIndex;
                }
            }
        }

        private class ExecAction : FastbuildAction
        {
            public ExecAction()
            {
                NodeType = "Exec";
            }
            public Linker Linker;
            public string Arguments { get; set; }

            public override string ToString()
            {
                //Carry on here. Need to strip input/output out, or change to not require in/out
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Exec('{0}')\n{{\n", Alias);
                sb.AppendFormat("\t.ExecExecutable\t\t  = '{0}' \n " +
                                "\t.ExecArguments\t\t  = '{1}'\n" +
                                "\t.DoNotUseOutput = true\n" +
                                "\t.ExecOutput\t\t = '{2}-{0}'\n",
                                Linker.ExecPath,
                                Arguments, Alias);

                if (Dependencies.Any())
                {
                    sb.AppendFormat("\t.PreBuildDependencies\t\t= {{ {0} }} \n ",
                        string.Join("\n\t\t\t", Dependencies.Select(d => "'" + d.Alias + "'")));
                }
                sb.Append("}\n");

                return sb.ToString();
            }
        }

        private class LinkAction : ExecAction
        {
            public LinkAction()
            {
                NodeType = "DLL";
            }

            public string OutputLibrary { get; set; }
            public List<string> Inputs { get; set; }
            public bool LocalOnly { get; set; }

            public override string ToString()
            {
                string linkerArgs = Arguments;

                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("DLL('{0}')\n{{\n", Alias);
                sb.AppendFormat("\t.Linker\t\t            = '{0}' \n " +
                                "\t.LinkerOutput\t\t  =  '{1}' \n",
                                Linker.ExecPath,
                                OutputLibrary);

                if (Dependencies.Any())
                {
                    linkerArgs += " %1";
                    sb.AppendFormat("\t.Libraries\t\t   = {{ {0} }} \n ",
                        string.Join("\n\t\t\t", Dependencies.Select(d => "'" + d.Alias + "'")));
                }
                else
                {
                    if (Inputs.Count == 1)
                    {
                        linkerArgs = linkerArgs.Replace(Inputs[0], "%1");
                        sb.AppendFormat("\t.Libraries\t\t   = '{0}' \n ",
                            Inputs[0]);
                    }
                    else
                    {
                        var inputs = Inputs.Where(n => !n.EndsWith(".response")).ToList();
                        linkerArgs = linkerArgs.Replace(inputs[0], "%1");
                        foreach (var lib in inputs)
                        {
                            lib.Replace(lib, "");
                        }
                        sb.AppendFormat("\t.Libraries\t\t   = {{ {0} }} \n ",
                        string.Join("\n\t\t\t", inputs.Select(d => "'" + d + "'")));
                    }
                }
                linkerArgs = linkerArgs.Replace(OutputLibrary, "%2");
                sb.AppendFormat("\t.LinkerOptions\t\t = '{0}'\n", linkerArgs);

                sb.Append("}\n");

                return sb.ToString();
            }
        }
        private class ObjectGroup : FastbuildAction
        {
            public ObjectGroup()
            {
                ActionInputs = new Dictionary<Action, string>();
                NodeType = "ObjG";
            }
            public Dictionary<Action, string> ActionInputs { get; set; }
            public string MatchHash { get; set; }
            public string CompilerArguments { get; set; }
            public Compiler ObjectCompiler;
            public string OutputPath { get; set; }
            public string OutputExt { get; set; }
            public bool LocalOnly { get; set; }
            public PCHOptions PchOptions { get; set; }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendFormat("ObjectList('{0}')\n{{\n", Alias);

                sb.AppendFormat("\t.Compiler\t\t            = '{0}' \n " +
                                "\t.CompilerInputFiles\t\t  = {{ {1} }}\n" +
                                "\t.CompilerOutputPath\t\t  = '{2}'\n" +
                                "\t.CompilerOutputExtension\t\t  = '{3}'\n" +
                                "\t{4}\n",
                                ObjectCompiler.Alias,
                                string.Join("\n\t\t\t", ActionInputs.Select(a => "'" + a.Value + "'")),
                                OutputPath,
                                OutputExt,
                                ObjectCompiler.GetBffArguments(CompilerArguments));

                if (PchOptions != null)
                {
                    sb.AppendFormat("\t.PCHInputFile\t\t            = '{0}' \n " +
                                    "\t.PCHOutputFile\t\t            = '{1}' \n " +
                                    "\t.PCHOptions\t\t            = '{2}' \n ",
                                    PchOptions.Input,
                                    PchOptions.Output,
                                    PchOptions.Options);
                }

                if (Dependencies.Any())
                {
                    sb.AppendFormat("\t.PreBuildDependencies\t\t   = {{ {0} }} \n ",
                        string.Join("\n\t\t\t", Dependencies.Select(d => "'" + d.Alias + "'")));
                }

                sb.Append("}\n");

                return sb.ToString();
            }
        }
        private class PCHOptions
        {
            public string Options;
            public string Input;
            public string Output;
        }

        static private Dictionary<string, Compiler> Compilers = new Dictionary<string, Compiler>();
        static private Dictionary<string, Linker> Linkers = new Dictionary<string, Linker>();

        /**
         * Used when debugging Actions outputs all action return values to debug out
         * 
         * @param   sender      Sending object
         * @param   e           Event arguments (In this case, the line of string output)
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

        public static ExecutionResult ExecuteActions(List<Action> Actions)
        {
            ExecutionResult FASTBuildResult = ExecutionResult.TasksSucceeded;
            if (Actions.Count > 0)
            {
                if (IsAvailable() == false)
                {
                    return ExecutionResult.Unavailable;
                }

                List<Action> UnassignedObjects = new List<Action>();
                List<Action> UnassignedLinks = new List<Action>();
                List<Action> MiscActions = Actions.Where(a => a.ActionType != ActionType.Compile && a.ActionType != ActionType.Link).ToList();
                List<FastbuildAction> ObjectGroups = GatherActionsObjects(Actions.Where(a => a.ActionType == ActionType.Compile), ref UnassignedObjects);
                List<FastbuildAction> LinkGroups = GatherActionsLink(Actions.Where(a => a.ActionType == ActionType.Link), ref UnassignedLinks);

                Log.TraceInformation("Actions: {0}", Actions.Count, UnassignedObjects.Count);
                Log.TraceInformation("Misc Actions - FBuild: {0}", MiscActions.Count);
                Log.TraceInformation("Objects - FBuild: {0} -- Local: {1}", ObjectGroups.Count, UnassignedObjects.Count);
                Log.TraceInformation("Link - FBuild: {0} -- Local: {1}", LinkGroups != null ? LinkGroups.Count : 0, UnassignedLinks.Count);

                if (UnassignedLinks.Count > 0)
                {
                    throw new Exception("Error, unaccounted for lib! Cannot guarantee there will be no prerequisite issues. Fix it");
                }
                if (UnassignedObjects.Count > 0)
                {
                    Dictionary<Action, ActionThread> ActionThreadDictionary = new Dictionary<Action, ActionThread>();
                    ExecuteLocalActions(UnassignedObjects, ActionThreadDictionary, UnassignedObjects.Count);
                }

                if (ObjectGroups.Any())
                {
                    FASTBuildResult = RunFBuild(BuildStep.CompileObjects, GenerateFBuildFileString(BuildStep.CompileObjects, ObjectGroups));
                }
                if (FASTBuildResult == ExecutionResult.TasksSucceeded)
                {
                    if (LinkGroups.Any())
                    {
                        FASTBuildResult = RunFBuild(BuildStep.Link, GenerateFBuildFileString(BuildStep.Link, LinkGroups));                        
                    }
                }
            }
            return FASTBuildResult;
        }

        private static string GenerateFBuildFileString(BuildStep step, List<FastbuildAction> actions)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FastBuildCommon.EntryArguments);
            if (step == BuildStep.CompileObjects)
            {
                sb.Append(GenerateFBuildCompilers());
            }

            if (actions.Count > 0)
            {
                sb.Append(GenerateFBuildNodeList(step == BuildStep.CompileObjects ? "ObjectsListsAlias" : "DLLListAlias", actions));
            }
            sb.Append(GenerateFBuildTargets(step == BuildStep.CompileObjects ? actions.Any() : false,
                                                    step == BuildStep.Link ? actions.Any() : false));
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

        private static string GenerateFBuildNodeList(string aliasName, List<FastbuildAction> ObjectGroups)
        {
            StringBuilder sb = new StringBuilder();
            List<string> aliases = new List<string>();

            foreach (var objectGroup in ObjectGroups)
            {
                aliases.Add(objectGroup.Alias);
                sb.Append(objectGroup.ToString());
            }

            if (aliases.Any())
            {
                sb.Append(FastBuildCommon.GetAliasTag(aliasName, aliases));
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
                return FastBuildCommon.GetAliasTag("all", aliases);
            }
            else
            {
                return "";
            }
        }

        private static List<FastbuildAction> GatherActionsObjects(IEnumerable<Action> CompileActions, ref List<Action> UnassignedActions)
        {
            //Debugger.Launch();
            List<ObjectGroup> objectGroup = new List<ObjectGroup>();
            Dictionary<Action, ObjectGroup> actionLinks = new Dictionary<Action, ObjectGroup>();
            int objectCount = 0;
            foreach (var action in CompileActions)
            {
                ObjectGroup obj = ParseCompilerAction(action);
                if (obj != null)
                {
                    var group = objectGroup.FirstOrDefault(n => n.MatchHash == obj.MatchHash);
                    if (group != null)
                    {
                        group.ActionInputs[action] = obj.ActionInputs.FirstOrDefault().Value;
                    }
                    else
                    {
                        obj.AliasIndex = objectCount++;
                        objectGroup.Add(obj);
                    }
                    actionLinks[action] = obj;
                }
                else
                {
                    Log.TraceInformation("Local Action - \n-Path:{0}\n-Args:{1}", action.CommandPath, action.CommandArguments);
                    UnassignedActions.Add(action);
                }
            }

            foreach (var actionPair in actionLinks)
            {
                var prerequisites = actionPair.Key.PrerequisiteItems.Where(n => n.ProducingAction != null && n.ProducingAction.ActionType == ActionType.Compile);
                foreach (var prerequisite in prerequisites)
                {
                    if (actionLinks.ContainsKey(prerequisite.ProducingAction))
                    {
                        actionPair.Value.Dependencies.Add(actionLinks[prerequisite.ProducingAction]);
                    }
                }
            }

            return objectGroup.Cast<FastbuildAction>().ToList();
        }

        private static List<FastbuildAction> GatherActionsLink(IEnumerable<Action> LocalLinkActions, ref List<Action> UnassignedActions)
        {
            List<ExecAction> linkActions = new List<ExecAction>();
            Dictionary<Action, ExecAction> actionLinks = new Dictionary<Action, ExecAction>();

            foreach (var action in LocalLinkActions)
            {
                var linkAction = ParseLinkAction(action);
                if (linkAction != null)
                {
                    linkActions.Add(linkAction);
                    linkAction.AliasIndex = linkActions.Count;
                    actionLinks[action] = linkAction;
                }
                else
                {
                    Log.TraceInformation("Local Action - \n-Path:{0}\n-Args:{1}", action.CommandPath, action.CommandArguments);
                    UnassignedActions.Add(action);
                }
            }

            //Resolve dependencies
            foreach (var action in LocalLinkActions)
            {
                if (actionLinks.ContainsKey(action) == false) throw new Exception("FASTBuild: Not all link actions processed!");

                ExecAction linkAction = actionLinks[action];
                var prerequisites = action.PrerequisiteItems.Where(n => n.ProducingAction != null && n.ProducingAction.ActionType == ActionType.Link);
                foreach (var prerequisite in prerequisites)
                {
                    if (actionLinks.ContainsKey(prerequisite.ProducingAction))
                    {
                        linkAction.Dependencies.Add(actionLinks[prerequisite.ProducingAction]);
                    }
                }
            }
            return linkActions.Cast<FastbuildAction>().ToList();
        }

        private static string LocaliseFilePath(string filepath)
        {
            Uri inputUri = new Uri(filepath);
            Uri currentUri = new Uri(Directory.GetCurrentDirectory());
            Uri relativeInputPath = currentUri.MakeRelativeUri(inputUri);
            return "../" + relativeInputPath.ToString();
        }

        private static ObjectGroup ParseCompilerAction(Action action)
        {
            string[] compatableExtensions = { ".c", ".cpp", ".rc", ".inl" };
            ObjectGroup outputAction = null;
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

            if (inputMatches.Count > 0 && outputMatch.Success)
            {
                string input = "";
                string outputPath = "";
                string outputExt = "";
                string matchHash = "";
                string args = action.CommandArguments;
                PCHOptions pchOptions = null;

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
                    pchOptions = new PCHOptions();
                    outputMatch = Regex.Match(args, compiler.PCHOutputRegex, RegexOptions.IgnoreCase);
                    pchOptions.Input = LocaliseFilePath(input);
                    pchOptions.Output = LocaliseFilePath(outputMatch.Value);
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

                input = LocaliseFilePath(input);
                outputPath = LocaliseFilePath(outputPath);

                if (pchOptions != null)
                    matchHash = args + outputPath + outputExt + action.CommandPath + pchOptions.Options + pchOptions.Input + pchOptions.Output;
                else
                    matchHash = args + outputPath + outputExt + action.CommandPath;

                outputAction = new ObjectGroup()
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

        private static ExecAction ParseLinkAction(Action action)
        {
            //Debugger.Launch();
            ExecAction output = null;
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
                var outputMatchesRegex = Regex.Matches(action.CommandArguments, linker.OutputFileRegex, RegexOptions.IgnoreCase);
                var inputMatches = inputMatchesRegex.Cast<Match>().Where(n => linker.AllowedInputTypes.Where(a => n.Value.EndsWith(a)).Any()).ToList();
                var outputMatches = outputMatchesRegex.Cast<Match>().Where(n => linker.AllowedOutputTypes.Where(a => n.Value.EndsWith(a)).Any()).ToList();

                if (inputMatches.Count > 0 && outputMatches.Count == 1)
                {
                    linkerFound = true;
                    output = new LinkAction()
                    {
                        Action = action,
                        Linker = linker,
                        Arguments = action.CommandArguments,
                        OutputLibrary = outputMatches[0].Value,
                        Inputs = inputMatches.Select(n => n.Value).ToList(),
                        LocalOnly = !action.bCanExecuteRemotely
                    };
                }
            }

            if (!linkerFound)
            {
                output = new ExecAction()
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
                Log.TraceInformation((step == BuildStep.CompileObjects ? "Object Compilation" : "Object Linking") + " Finished. Execution Time: {0}", elapsedMs);
                
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
                + (IsDistrbuted ? "-dist " : "")
                + (UseCache ? (EnableCacheGenerationMode ? "-cache " : "-cacheread ") : "")
               + " -config " + BuildConfiguration.BaseIntermediatePath + "/fbuild.bff");

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
                // TODO - hard coded FBuild.exe name, might be okay...?
                string FBExecutable = Path.Combine(FBRoot, "FBuild.exe");
                string CompilerDir = Path.Combine(FBRoot, "External/VS15.0");
                string SdkDir = Path.Combine(FBRoot, "External/Windows8.1");

                // Check that FASTBuild is available
                bFBExists = File.Exists(FBExecutable);
                if (bFBExists)
                {
                    if (!Directory.Exists(CompilerDir) || !Directory.Exists(SdkDir))
                    {
                        bFBExists = false;
                    }
                }
            }
            return bFBExists;
        }

    }
}
