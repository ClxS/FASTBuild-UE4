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
                if (_allowedInputTypes != null) return _allowedInputTypes;

                switch (Type)
                {
                    case CompilerTypes.Msvc:
                        _allowedInputTypes = new List<string>() { ".response", ".lib", ".obj" };
                        break;
                    case CompilerTypes.OrbisClang:
                    case CompilerTypes.OrbisSnarl:
                        _allowedInputTypes = new List<string>() { ".response", ".a" };
                        break;
                    case CompilerTypes.Rc:
                    case CompilerTypes.Clang:
                    default:
                        break;
                }
                return _allowedInputTypes;
            }
        }

        private List<string> _allowedOutputTypes;
        public virtual List<string> AllowedOutputTypes
        {
            get
            {
                if (_allowedOutputTypes != null) return _allowedOutputTypes;

                switch (Type)
                {
                    case CompilerTypes.Msvc:
                        _allowedOutputTypes = new List<string>() { ".dll", ".lib", ".exe" };
                        break;
                    case CompilerTypes.OrbisClang:
                    case CompilerTypes.OrbisSnarl:
                        _allowedOutputTypes = new List<string>() { ".self", ".a", ".so" };
                        break;
                    case CompilerTypes.Rc:
                    case CompilerTypes.Clang:
                    default:
                        break;
                }
                return _allowedOutputTypes;
            }
        }

        public virtual string InputFileRegex
        {
            get
            {
                return Type == CompilerTypes.OrbisClang ? "(?<=\")(.*?)(?=\")" : "(?<=@\")(.*?)(?=\")";
            }
        }
        public virtual string OutputFileRegex
        {
            get
            {
                switch (Type)
                {
                    case CompilerTypes.Msvc:
                    case CompilerTypes.Rc:
                        return "(?<=(/OUT: \"|/OUT:\"))(.*?)(?=\")";
                    case CompilerTypes.Clang:
                    case CompilerTypes.OrbisClang:
                        return "(?<=(-o \"|-o\"))(.*?)(?=\")";
                    case CompilerTypes.OrbisSnarl:
                        return "(?<=\")(.*?.a)(?=\")";
                    default:
                        break;
                }
                return "";
            }
        }
        public virtual string ImportLibraryRegex
        {
            get
            {
                if (Type == CompilerTypes.Msvc || Type == CompilerTypes.Rc)
                {
                    return "(?<=(/IMPLIB: \"|/IMPLIB:\"))(.*?)(?=\")";
                }
                return "";
            }
        }
        public virtual string PchOutputRegex
        {
            get
            {
                if (Type == CompilerTypes.Msvc || Type == CompilerTypes.Rc)
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
            var compilerPath = "";
            if (ExecPath.Contains("link.exe") || ExecPath.Contains("lib.exe"))
            {
                var compilerPathComponents = ExecPath.Replace('\\', '/').Split('/');
                var startIndex = Array.FindIndex(compilerPathComponents, row => row == "VC");
                if (startIndex > 0)
                {
                    Type = CompilerTypes.Msvc;
                    compilerPath = "$VSBasePath$";
                    for (var i = startIndex + 1; i < compilerPathComponents.Length; ++i)
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
