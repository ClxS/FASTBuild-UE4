using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild.BuildComponents
{
    public abstract class BuildComponent
    {        
        public Action Action { get; set; }

        public string NodeType { get; set; }

        public int AliasIndex { get; set; }

        public List<BuildComponent> Dependencies { get; set; }

        public string Alias => NodeType + "-" + AliasIndex;

        protected BuildComponent()
        {
            Dependencies = new List<BuildComponent>();
        }
    }

}
