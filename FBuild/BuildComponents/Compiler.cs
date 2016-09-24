using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild.BuildComponents
{
    public class Compiler : Linker
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
                return "(?<=( \")|(@\"))(.*?)(?=\")";
            }
        }
        public override string OutputFileRegex
        {
            get
            {
                if (Type == CompilerTypes.MSVC || Type == CompilerTypes.RC)
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
                if (Type == CompilerTypes.MSVC || Type == CompilerTypes.RC)
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

            if (Type == CompilerTypes.MSVC)
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
                    Type = CompilerTypes.MSVC;
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
                Type = CompilerTypes.RC;
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
            else if (ExecPath.Contains("orbis-clang.exe"))
            {
                Type = CompilerTypes.OrbisClang;
            }
        }
    }
}
