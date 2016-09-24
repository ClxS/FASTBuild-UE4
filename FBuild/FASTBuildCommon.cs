using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild
{
    public static class FASTBuildCommon
    {
        public static string EntryArguments
        {
            get
            {
                string EntryArguments = "";
                if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2013)
                {
                    EntryArguments += ".VSBasePath     	        = '../Extras/FASTBuild/External/VS13.4/VC'\r\n";
                }
                else if (WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2015)
                {
                    EntryArguments += ".VSBasePath     	        = '../Extras/FASTBuild/External/VS15.0/VC'\r\n";
                }
                EntryArguments += ".ClangBasePath			= '../Extras/FASTBuild/External/ClangForWindows/3.6'\r\n";
                EntryArguments += ".WindowsSDKBasePath      = 'C:/Program Files (x86)/Windows Kits/8.1'\r\n";
                
                EntryArguments += ";-------------------------------------------------------------------------------\r\n";
                EntryArguments += "; Settings\r\n";
                EntryArguments += ";-------------------------------------------------------------------------------\r\n";
                EntryArguments += "Settings\r\n";
                EntryArguments += "{\r\n";
                EntryArguments += "    .CachePath = '" + FASTBuildConfiguration.CachePath + "'\r\n";
                EntryArguments += "}\r\n";

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
}
