using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild
{
    public static class FASTBuildConfiguration
    {
        public static bool UseCache => true;

        public static bool IsDistrbuted => true;

        public static bool UseSinglePassCompilation => false;

        public static string AdditionalArguments => "";

        public static string CachePath => "";

        public static bool EnableCacheGenerationMode
        {
            get
            {
                var writeFastBuildString = Environment.GetEnvironmentVariable("UE-FB-CACHE-WRITE");
                if (writeFastBuildString == null) return false;

                return string.Compare("TRUE", writeFastBuildString, StringComparison.OrdinalIgnoreCase) == 0;
            }
        }
    }
}
