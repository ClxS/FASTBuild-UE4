using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild.BuildComponents
{
    public class Linker
    {
        public string ExecPath { get; set; }
        public CompilerTypes Type;

        private List<string> _allowedInputTypes;
        public virtual List<string> AllowedInputTypes
        {
            get
            {
                if (_allowedInputTypes == null)
                {
                    if (Type == CompilerTypes.MSVC)
                    {
                        _allowedInputTypes = new List<string>() { ".response", ".lib", ".obj" };
                    }
                    else if (Type == CompilerTypes.OrbisClang || Type == CompilerTypes.OrbisSnarl)
                    {
                        _allowedInputTypes = new List<string>() { ".response", ".a" };
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
                    if (Type == CompilerTypes.MSVC)
                    {
                        _allowedOutputTypes = new List<string>() { ".dll", ".lib", ".exe" };
                    }
                    else if (Type == CompilerTypes.OrbisClang || Type == CompilerTypes.OrbisSnarl)
                    {
                        _allowedOutputTypes = new List<string>() { ".self", ".a", ".so" };
                    }
                }
                return _allowedOutputTypes;
            }
        }

        public virtual string InputFileRegex
        {
            get
            {
                if (Type == CompilerTypes.OrbisClang)
                {
                    return "(?<=\")(.*?)(?=\")";
                }

                return "(?<=@\")(.*?)(?=\")";
            }
        }
        public virtual string OutputFileRegex
        {
            get
            {
                if (Type == CompilerTypes.MSVC || Type == CompilerTypes.RC)
                {
                    return "(?<=(/OUT: \"|/OUT:\"))(.*?)(?=\")";
                }
                else if (Type == CompilerTypes.OrbisClang)
                {
                    return "(?<=(-o \"|-o\"))(.*?)(?=\")";
                }
                else if (Type == CompilerTypes.OrbisSnarl)
                {
                    return "(?<=\")(.*?.a)(?=\")";
                }
                return "";
            }
        }
        public virtual string ImportLibraryRegex
        {
            get
            {
                if (Type == CompilerTypes.MSVC || Type == CompilerTypes.RC)
                {
                    return "(?<=(/IMPLIB: \"|/IMPLIB:\"))(.*?)(?=\")";
                }
                return "";
            }
        }
        public virtual string PCHOutputRegex
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
                    Type = CompilerTypes.MSVC;
                    compilerPath = "$VSBasePath$";
                    for (int i = startIndex + 1; i < compilerPathComponents.Length; ++i)
                    {
                        compilerPath += "/" + compilerPathComponents[i];
                    }
                }
                ExecPath = compilerPath;
            }
            else if (ExecPath.Contains("orbis-snarl.exe"))
            {
                Type = CompilerTypes.OrbisSnarl;
            }
            else if (ExecPath.Contains("orbis-clang.exe"))
            {
                Type = CompilerTypes.OrbisClang;
            }
        }

        public static bool IsKnownLinker(string args)
        {
            return args.Contains("lib.exe") || args.Contains("link.exe") || args.Contains("orbis-clang.exe") || args.Contains("orbis-snarl.exe");
        }
    }

}
