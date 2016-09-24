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
                var entryArguments = "";
                switch (WindowsPlatform.Compiler)
                {
                    case WindowsCompiler.VisualStudio2013:
                        entryArguments += ".VSBasePath     	        = '../Extras/FASTBuild/External/VS13.4/VC'\r\n";
                        break;
                    case WindowsCompiler.VisualStudio2015:
                        entryArguments += ".VSBasePath     	        = '../Extras/FASTBuild/External/VS15.0/VC'\r\n";
                        break;
                }
                entryArguments += ".ClangBasePath			= '../Extras/FASTBuild/External/ClangForWindows/3.6'\r\n";
                entryArguments += ".WindowsSDKBasePath      = 'C:/Program Files (x86)/Windows Kits/8.1'\r\n";
                
                entryArguments += ";-------------------------------------------------------------------------------\r\n";
                entryArguments += "; Settings\r\n";
                entryArguments += ";-------------------------------------------------------------------------------\r\n";
                entryArguments += "Settings\r\n";
                entryArguments += "{\r\n";
                entryArguments += "    .CachePath = '" + FASTBuildConfiguration.CachePath + "'\r\n";
                entryArguments += "}\r\n";

                return entryArguments;
            }
        }
        public static string GetAliasTag(string alias, List<string> targets)
        {
            var output = "";
            var targetCount = targets.Count;
            output += "Alias( '" + alias + "' )\r\n";
            output += "{\r\n";
            output += " .Targets = {";
            for (var i = 0; i < targetCount; ++i)
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
