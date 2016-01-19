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
    public class FastBuild
    {
        static private bool UseCache { get { return true; } }
        static private bool UseCacheWrite { get { return false; } }
        static private bool IsDistrbuted { get { return true; } }
        
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
                        EntryArguments += ".VSBasePath      = '../Extras/FASTBuild/SDK/VS13.4/VC'\r\n";
                    }
                    else if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2015)
                    {
                        EntryArguments += ".VSBasePath      = '../Extras/FASTBuild/SDK/VS15.0/VC'\r\n";
                    }
                    EntryArguments += ".ClangBasePath       = '../Extras/FASTBuild/SDK/LLVM'\r\n";
                    EntryArguments += ".WindowsSDKBasePath  = 'C:\\Program Files (x86)\\Windows Kits\\8.1'\r\n";

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
            Link
        }

        private enum CompilerType
        {
            MSVC,
            RC,
            Clang
        }

        private class Compiler
        {
            public string Alias;
            public string CompilerPath;
            public CompilerType CompilerType;

            public string GetBffArguments(string arguments)
            {
                StringBuilder output = new StringBuilder();
                if (CompilerType == CompilerType.RC)
                {
                    output.Append(" .CompilerOptions\t = .BaseIncludePaths\r\n");
                    output.AppendFormat(" .CompilerOptions\t + '{0}'\r\n", arguments);
                }
                else if (CompilerType == CompilerType.MSVC)
                {
                    output.AppendFormat(" .CompilerOptions\t = '{0}'\r\n", arguments);
                    output.Append(" .CompilerOptions\t + .BaseIncludePaths\r\n");
                }
                else
                {
                    output.AppendFormat(" .CompilerOptions\t = '{0}'\r\n", arguments);
                }
                return output.ToString();
            }
            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Compiler('{0}')\n{{\n", Alias);
                sb.AppendFormat("\t.Executable\t\t            = '{0}' \n " +
                                "\t.ExtraFiles\t\t  = {{ {1} }}\n",
                                CompilerPath,
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
                
                string compilerDir = Path.GetDirectoryName(CompilerPath);
                string msIncludeDir;

                if (CompilerType == CompilerType.MSVC)
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
        }

        private class LinkAction
        {
            public LinkAction()
            {
                LinkDependencies = new List<LinkAction>();
            }
            public string Alias { get; set; }
            public List<LinkAction> LinkDependencies { get; set; }
            public Action Action { get; set; }
            public string LinkerArguments { get; set; }
            public Compiler Linker;
            public string OutputLibrary { get; set; }
            public string Input { get; set; }
            public bool LocalOnly { get; set; }

            public override string ToString()
            {
                string linkerArgs = LinkerArguments.Replace(OutputLibrary, "%2");

                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("DLL('{0}')\n{\n", Alias);
                sb.AppendFormat("\t.Linker\t\t            = '{0}' \n " +
                                "\t.LinkerOutput\t\t  = { {1} }\n" +
                                "\t.LinkerOptions\t\t  = { {2} }\n",
                                Linker.CompilerPath,
                                OutputLibrary,
                                linkerArgs);

                if (LinkDependencies.Any())
                {
                    sb.AppendFormat("\t.Libraries\t\t   = { {0} } \n ",
                        string.Join("\n\t\t\t", LinkDependencies.Select(d => "'" + d.Alias + "'")));
                }
                else
                {
                    sb.AppendFormat("\t.Libraries\t\t   = { {0} } \n ",
                        Input);
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
        private class ObjectGroup
        {
            public ObjectGroup()
            {
                ActionInputs = new Dictionary<Action, string>();
                BuildDependencies = new List<ObjectGroup>();
            }
            public string Alias { get; set; }
            public List<ObjectGroup> BuildDependencies { get; set; }
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

                if (BuildDependencies.Any())
                {
                    sb.AppendFormat("\t.PreBuildDependencies\t\t   = {{ {0} }} \n ",
                        string.Join("\n\t\t\t", BuildDependencies.Select(d => "'" + d.Alias + "'")));
                }

                sb.Append("}\n");   

                return sb.ToString();
            }
        }

        static private Dictionary<string, Compiler> Compilers = new Dictionary<string, Compiler>();
                
        static protected void ActionDebugOutput(object sender, DataReceivedEventArgs e)
        {
            var Output = e.Data;
            if (Output == null)
            {
                return;
            }

            Log.TraceInformation(Output);
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
                List<ObjectGroup> ObjectGroups = GatherActionsObjects(Actions, ref UnassignedObjects);
                List<LinkAction> LinkGroups = LinkGroups = GatherActionsLink(Actions, ref UnassignedLinks);

                LocaliseCompilerPaths();

                if (UnassignedLinks.Count > 0)
                {
                    throw new Exception("Error, unaccounted for libs! Cannot guarantee there will be no prerequisite issues. Fix it");
                }
                if (UnassignedObjects.Count > 0)
                {
                    Dictionary<Action, ActionThread> ActionThreadDictionary = new Dictionary<Action, ActionThread>();
                    throw new Exception("Error, unaccounted for objects.");
                }

                if (ObjectGroups.Any())
                {
                    FASTBuildResult = RunFBuild(BuildStep.CompileObjects, GenerateFBuildFileString(BuildStep.CompileObjects, ObjectGroups, LinkGroups));
                }
                if (FASTBuildResult == ExecutionResult.TasksSucceeded)
                {
                    if (LinkGroups.Any())
                    {
                        FASTBuildResult = RunFBuild(BuildStep.Link, GenerateFBuildFileString(BuildStep.Link, ObjectGroups, LinkGroups));
                    }
                }
            }
            return FASTBuildResult;
        }

        private static string GenerateFBuildFileString(BuildStep step, List<ObjectGroup> ObjectGroups, List<LinkAction> LinkGroups)
        {
            switch (step)
            {
                case BuildStep.CompileObjects:
                    {
                        string objectsBffFile = FastBuildCommon.EntryArguments;
                        objectsBffFile += GenerateFBuildCompilers();
                        if (ObjectGroups.Count > 0)
                        {
                            objectsBffFile += GenerateFBuildObjectsList(ObjectGroups);
                        }
                        objectsBffFile += GenerateFBuildTargets(ObjectGroups.Any(), false);
                        return objectsBffFile;
                    }
                case BuildStep.Link:
                    {
                        string linkBffFile = FastBuildCommon.EntryArguments;
                        if (LinkGroups.Count > 0)
                        {
                            linkBffFile += GenerateFBuildLinkerList(LinkGroups);
                        }
                        linkBffFile += GenerateFBuildTargets(false, LinkGroups.Any());
                        return linkBffFile;
                    }
                default: return "";
            }
        }

        private static string GenerateFBuildCompilers()
        {
            string output = "";

            int objectCount = 0;
            foreach (var compiler in Compilers)
            {
                compiler.Value.Alias = "Compiler-" + objectCount.ToString();
                output += compiler.Value.ToString();//; FastBuildCommon.GetCompilerTag(compiler.Value);
                objectCount++;
            }

            return output;
        }

        private static string GenerateFBuildObjectsList(List<ObjectGroup> ObjectGroups)
        {
            string output = "";
            List<string> aliases = new List<string>();

            foreach (var objectGroup in ObjectGroups)
            {
                aliases.Add(objectGroup.Alias);
                output += objectGroup.ToString();//FastBuildCommon.GetObjectList(objectGroup);
            }

            if (aliases.Any())
            {
                output += FastBuildCommon.GetAliasTag("ObjectsListsAlias", aliases);
            }
            return output;
        }

        private static string GenerateFBuildLinkerList(List<LinkAction> LinkerActions)
        {
            string output = "";
            List<string> aliases = new List<string>();

            foreach (var lnkAction in LinkerActions)
            {
                string alias = lnkAction.Alias;
                aliases.Add(alias);
                output += lnkAction.ToString();// FastBuildCommon.GetLinkerTag(alias, objectGroup);
            }

            if (aliases.Any())
            {
                output += FastBuildCommon.GetAliasTag("DLLListAlias", aliases);
            }
            return output;
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

        private static List<ObjectGroup> GatherActionsObjects(List<Action> LocalActions, ref List<Action> UnassignedActions)
        {
            List<ObjectGroup> objectGroup = new List<ObjectGroup>();
            Dictionary<Action, ObjectGroup> actionLinks = new Dictionary<Action, ObjectGroup>();
            foreach (var action in LocalActions.Where(a => a.ActionType == ActionType.Compile))
            {
                string input, outputPath, outputExt, args;
                PCHOptions pchOptions;
                args = action.CommandArguments;
                if (ReadCommand(ActionType.Compile, ref args, out input, out outputPath, out outputExt, out pchOptions))
                {
                    string matchHash;
                    if (pchOptions != null)
                        matchHash = args + outputPath + outputExt + action.CommandPath + pchOptions.Options + pchOptions.Input + pchOptions.Output;
                    else
                        matchHash = args + outputPath + outputExt + action.CommandPath;
                    var group = objectGroup.FirstOrDefault(n => n.MatchHash == matchHash);
                    if (group != null)
                    {
                        group.ActionInputs[action] = input;
                    }
                    else
                    {
                        group = new ObjectGroup()
                        {
                            MatchHash = matchHash,
                            CompilerArguments = args,
                            OutputPath = outputPath,
                            OutputExt = outputExt,
                            LocalOnly = !action.bCanExecuteRemotely,
                            PchOptions = pchOptions,
                            Alias = "ObjG-" + objectGroup.Count
                        };

                        group.ActionInputs[action] = input;
                        if (Compilers.ContainsKey(action.CommandPath))
                        {
                            group.ObjectCompiler = Compilers[action.CommandPath];
                        }
                        else
                        {
                            group.ObjectCompiler = new Compiler() { CompilerPath = action.CommandPath };
                            Compilers[action.CommandPath] = group.ObjectCompiler;
                        }
                        objectGroup.Add(group);
                    }
                    actionLinks[action] = group;
                }
                else
                {
                    Log.TraceInformation("Local Action - \n-Path:{0}\n-Args:{1}", action.CommandPath, action.CommandArguments);
                    UnassignedActions.Add(action);
                }
            }

            //Resolve dependencies
            foreach (var action in LocalActions.Where(a => a.ActionType == ActionType.Compile))
            {
                var buildAction = actionLinks[action];
                var prerequisites = action.PrerequisiteItems.Where(n => n.ProducingAction != null && n.ProducingAction.ActionType == ActionType.Compile);
                foreach (var prerequisite in prerequisites)
                {
                    if (actionLinks.ContainsKey(prerequisite.ProducingAction))
                    {
                        buildAction.BuildDependencies.Add(actionLinks[prerequisite.ProducingAction]);
                    }
                }
            }
            return objectGroup;
        }

        private static List<LinkAction> GatherActionsLink(List<Action> LocalActions, ref List<Action> UnassignedActions)
        {
            List<LinkAction> linkActions = new List<LinkAction>();
            Dictionary<Action, LinkAction> actionLinks = new Dictionary<Action, LinkAction>();
            foreach (var action in LocalActions.Where(a => a.ActionType == ActionType.Link))
            {
                string input, outputPath, outputExt, args;
                PCHOptions pchOptions;
                args = action.CommandArguments;
                if (ReadCommand(ActionType.Link, ref args, out input, out outputPath, out outputExt, out pchOptions))
                {
                    var group = new LinkAction()
                    {
                        Alias = "DllG-" + linkActions.Count,
                        Action = action,
                        LinkerArguments = args,
                        OutputLibrary = outputPath,
                        Input = input,
                        LocalOnly = !action.bCanExecuteRemotely
                    };
                    if (Compilers.ContainsKey(action.CommandPath))
                    {
                        group.Linker = Compilers[action.CommandPath];
                    }
                    else
                    {
                        group.Linker = new Compiler() { CompilerPath = action.CommandPath };
                        Compilers[action.CommandPath] = group.Linker;
                    }
                    linkActions.Add(group);
                    actionLinks[action] = group;
                }
                else
                {
                    Log.TraceInformation("Local Action - \n-Path:{0}\n-Args:{1}", action.CommandPath, action.CommandArguments);
                    UnassignedActions.Add(action);
                }
            }

            //Resolve dependencies
            foreach (var action in LocalActions.Where(a => a.ActionType == ActionType.Link))
            {
                LinkAction linkAction = actionLinks[action];
                var prerequisites = action.PrerequisiteItems.Where(n => n.ProducingAction != null && n.ProducingAction.ActionType == ActionType.Link);
                foreach (var prerequisite in prerequisites)
                {
                    if (actionLinks.ContainsKey(prerequisite.ProducingAction))
                    {
                        linkAction.LinkDependencies.Add(actionLinks[prerequisite.ProducingAction]);
                    }
                }
            }
            return linkActions;
        }

        private static bool ReadCommand(ActionType actionType, ref string args, out string input, out string outputPath, out string outputExt,
            out PCHOptions pchOptions)
        {
            bool success = true;
            string compilerInputRegex = "";
            string compilerOutputRegex = "";
            string compilerPchRegex = "";
            pchOptions = null;
            input = "";
            outputPath = "";
            outputExt = "";

            if (actionType == ActionType.Compile)
            {
                compilerInputRegex = "(?<= \")(.*?)(?=\")";
                compilerOutputRegex = "(?<=(/Fo \"|/Fo\"))(.*?)(?=\")";
                compilerPchRegex = "(/Yc \"|/Yc\")(.*?)(\")";
            }
            else if (actionType == ActionType.Link)
            {
                compilerInputRegex = "(?<=@\")(.*?)(?=\")";
                compilerOutputRegex = "(?<=(/OUT: \"|/OUT:\"))(.*?)(?=\")";
                compilerPchRegex = "";
            }

            var inputMatches = Regex.Matches(args, compilerInputRegex, RegexOptions.IgnoreCase);
            var outputMatch = Regex.Match(args, compilerOutputRegex, RegexOptions.IgnoreCase);
            var PCHMatch = Regex.Match(args, compilerPchRegex, RegexOptions.IgnoreCase);

            if (inputMatches.Count > 0 && outputMatch.Success)
            {
                if (actionType == ActionType.Compile)
                {
                    string[] compatableExtensions = { ".c", ".cpp", ".rc", ".inl" };
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

                    if (PCHMatch.Success)
                    {
                        pchOptions = new PCHOptions();
                        compilerPchRegex = "(?<=(/Fp \"|/Fp\"))(.*?)(?=\")";
                        outputMatch = Regex.Match(args, compilerPchRegex, RegexOptions.IgnoreCase);
                        pchOptions.Input = input;
                        pchOptions.Output = outputMatch.Value;

                        // pchOptions.Options = args.Replace(output, "%3");
                        pchOptions.Options = args;

                        pchOptions.Options = pchOptions.Options.Replace(pchOptions.Input, "%1");
                        pchOptions.Options = pchOptions.Options.Replace(pchOptions.Output, "%2");

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

                    Uri inputUri = new Uri(input);
                    Uri outputUri = new Uri(outputPath);
                    Uri currentUri = new Uri(System.IO.Directory.GetCurrentDirectory());
                    Uri relativeInputPath = currentUri.MakeRelativeUri(inputUri);
                    Uri relativeOutputPath = currentUri.MakeRelativeUri(outputUri);

                    input = "../" + relativeInputPath.ToString();
                    outputPath = "../" + relativeOutputPath.ToString();
                }
                else if (actionType == ActionType.Link)
                {
                    input = inputMatches[0].Value;

                    string output = outputMatch.Value;
                    //args = args.Replace(input, "%1");
                    //args += " %1";
                    outputPath = output;
                }
            }
            else
            {
                success = false;
            }

            return success;
        }

        private static void LocaliseCompilerPaths()
        {
            foreach (var compiler in Compilers)
            {
                string compilerPath = "";
                if (compiler.Value.CompilerPath.Contains("cl.exe") || compiler.Value.CompilerPath.Contains("link.exe") || compiler.Value.CompilerPath.Contains("lib.exe"))
                {
                    string[] compilerPathComponents = compiler.Value.CompilerPath.Replace('\\', '/').Split('/');
                    int startIndex = Array.FindIndex(compilerPathComponents, row => row == "VC");
                    if (startIndex > 0)
                    {
                        compiler.Value.CompilerType = CompilerType.MSVC;
                        compilerPath = "$VSBasePath$";
                        for (int i = startIndex + 1; i < compilerPathComponents.Length; ++i)
                        {
                            compilerPath += "/" + compilerPathComponents[i];
                        }
                    }
                    else
                    {
                        startIndex = Array.FindIndex(compilerPathComponents, row => row == "LLVM");
                        if (startIndex > 0)
                        {
                            compiler.Value.CompilerType = CompilerType.Clang;
                            compilerPath = "$ClangBasePath$";
                            for (int i = startIndex + 1; i < compilerPathComponents.Length; ++i)
                            {
                                compilerPath += "/" + compilerPathComponents[i];
                            }
                        }
                    }
                    compiler.Value.CompilerPath = compilerPath;
                }
                else if (compiler.Value.CompilerPath.Contains("rc.exe"))
                {
                    string[] compilerPathComponents = compiler.Value.CompilerPath.Replace('\\', '/').Split('/');
                    compilerPath = "$WindowsSDKBasePath$";
                    int startIndex = Array.FindIndex(compilerPathComponents, row => row == "8.1");
                    if (startIndex > 0)
                    {
                        for (int i = startIndex + 1; i < compilerPathComponents.Length; ++i)
                        {
                            compilerPath += "/" + compilerPathComponents[i];
                        }
                    }
                    compiler.Value.CompilerPath = compilerPath;
                    compiler.Value.CompilerType = CompilerType.RC;
                }
            }
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
                + (UseCache ? (UseCacheWrite ? "-cache " : "-cacheread ") : "")
                + "-config " + BuildConfiguration.BaseIntermediatePath + "/fbuild.bff");
            Log.TraceWarning(PSI.Arguments);
            Log.TraceWarning(Path.GetFullPath("."));
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
            // TODO - Move this to an env variable with FBuild location
            string FBRoot = Path.GetFullPath("../Extras/FASTBuild/");

            bool bFBExists = false;
            if (FBRoot != null)
            {
                string FBExecutable = Path.Combine(FBRoot, "FBuild.exe");
                string CompilerDir = Path.Combine(FBRoot, "SDK/VS15.0");
                string SdkDir = Path.Combine(FBRoot, "SDK/Windows8.1");

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
