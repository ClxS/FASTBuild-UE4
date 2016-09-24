using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild
{
    public static class FASTBuildConfiguration
    {
        static public bool UseCache
        {
            get
            {
                return true;
            }
        }
        static public bool IsDistrbuted
        {
            get
            {
                return true;
            }
        }        
        static public bool UseSinglePassCompilation
        {
            get
            {
                return false;
            }
        }
        static public string AdditionalArguments
        {
            get
            {
                return "";
            }
        }        
        public static string CachePath
        {
            get
            {
                return "";
            }
        }
        static public bool EnableCacheGenerationMode
        {
            get
            {
                bool allowCacheWrite = false;
                string writeFastBuildString = Environment.GetEnvironmentVariable("UE-FB-CACHE-WRITE");
                if (writeFastBuildString != null)
                {
                    if (string.Compare("TRUE", writeFastBuildString, true) == 0)
                    {
                        allowCacheWrite = true;
                    }
                }
                return allowCacheWrite;
            }
        }
    }
}
