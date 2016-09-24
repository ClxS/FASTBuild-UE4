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
                if (Type == CompilerTypes.Msvc || Type == CompilerTypes.Rc)
                {
                    return "(?<=(/Fo \"|/Fo\"))(.*?)(?=\")";
                }
                else
                {
                    return "(?<=(-o \"|-o\"))(.*?)(?=\")";
                }
            }
        }
        public override string PchOutputRegex
        {
            get
            {
                if (Type == CompilerTypes.Msvc || Type == CompilerTypes.Rc)
                {
                    return "(?<=(/Fp \"|/Fp\"))(.*?)(?=\")";
                }
                return "(?<=(/Fp \"|/Fp\"))(.*?)(?=\")";
            }
        }
        public string Alias;

        public string GetBffArguments(string arguments)
        {
            var output = new StringBuilder();
            output.AppendFormat(" .CompilerOptions\t = '{0}'\r\n", arguments);
            return output.ToString();
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("Compiler('{0}')\n{{\n", Alias);
            sb.AppendFormat("\t.Executable\t\t            = '{0}' \n " +
                            "\t.ExtraFiles\t\t  = {{ {1} }}\n",
                            ExecPath,
                            string.Join("\n\t\t\t", GetExtraFiles().Select(e => "\t\t'" + e + "'")));

            sb.Append("}\n");
            return sb.ToString();
        }
        private IEnumerable<string> GetExtraFiles()
        {
            if (Type != CompilerTypes.Msvc) return Enumerable.Empty<string>();

            var output = new List<string>();

            var msvcVer = "";
            switch (WindowsPlatform.Compiler)
            {
                case WindowsCompiler.VisualStudio2013:
                    msvcVer = "120";
                    break;
                case WindowsCompiler.VisualStudio2015:
                    msvcVer = "140";
                    break;
            }

            var compilerDir = Path.GetDirectoryName(ExecPath);


            output.Add(compilerDir + "\\1033\\clui.dll");
            if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2013)
            {
                output.Add(compilerDir + "\\c1ast.dll");
                output.Add(compilerDir + "\\c1xxast.dll");
            }
            output.Add(compilerDir + "\\c1xx.dll");
            output.Add(compilerDir + "\\c2.dll");
            output.Add(compilerDir + "\\c1.dll");
            
            output.Add(compilerDir + "\\msobj" + msvcVer + ".dll");
            output.Add(compilerDir + "\\mspdb" + msvcVer + ".dll");
            output.Add(compilerDir + "\\mspdbsrv.exe");
            output.Add(compilerDir + "\\mspdbcore.dll");
            output.Add(compilerDir + "\\mspft" + msvcVer + ".dll");
            return output;
        }
        private void LocaliseCompilerPath()
        {
            var compilerPath = "";
            if (ExecPath.Contains("cl.exe"))
            {
                var compilerPathComponents = ExecPath.Replace('\\', '/').Split('/');
                var startIndex = Array.FindIndex(compilerPathComponents, row => row == "VC");
                if (startIndex > 0)
                {
                    Type = CompilerTypes.Msvc;
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
                Type = CompilerTypes.Rc;
                var compilerPathComponents = ExecPath.Replace('\\', '/').Split('/');
                compilerPath = "$WindowsSDKBasePath$";
                var startIndex = Array.FindIndex(compilerPathComponents, row => row == "8.1");
                if (startIndex > 0)
                {
                    for (var i = startIndex + 1; i < compilerPathComponents.Length; ++i)
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
