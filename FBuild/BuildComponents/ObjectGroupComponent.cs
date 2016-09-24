using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild.BuildComponents
{
    public class ObjectGroupComponent : BuildComponent
    {        
        public Dictionary<Action, string> ActionInputs { get; set; }
        public IEnumerable<string> ActionOutputs
        {
            get
            {
                return ActionInputs.Select(n => OutputPath + Path.GetFileNameWithoutExtension(n.Value) + OutputExt);
            }
        }

        public string MatchHash { get; set; }

        public string CompilerArguments { get; set; }

        public string OutputPath { get; set; }

        public string OutputExt { get; set; }

        public Compiler ObjectCompiler;

        public PchOptions PchOptions { get; set; }

        public bool LocalOnly { get; set; }
        
        public ObjectGroupComponent()
        {
            ActionInputs = new Dictionary<Action, string>();
            NodeType = "ObjG";
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendFormat("ObjectList('{0}')\n{{\n", Alias);

            sb.AppendFormat("\t.Compiler\t\t            = '{0}' \n " +
                            "\t.CompilerInputFiles\t\t  = {{ {1} }}\n" +
                            "\t.CompilerOutputPath\t\t  = '{2}'\n" +
                            "\t.CompilerOutputExtension\t\t  = '{3}'\n" +
                            "\t.CompilerOptions\t = '{4}'\n",
                            ObjectCompiler.Alias,
                            string.Join("\n\t\t\t", ActionInputs.Select(a => "'" + a.Value + "'")),
                            OutputPath,
                            OutputExt,
                            CompilerArguments);
            if (PchOptions != null)
            {
                sb.AppendFormat("\t.PCHInputFile\t\t          = '{0}' \n " +
                                "\t.PCHOutputFile\t\t         = '{1}' \n " +
                                "\t.PCHOptions\t\t            = '{2}' \n ",
                                PchOptions.Input,
                                PchOptions.Output,
                                PchOptions.Options);
            }

            var dependencyString = "";
            if (Dependencies.Any())
            {                
                foreach(var dependency in Dependencies)
                {
                    dependencyString += "\n\t\t\t '" + dependency.Alias + "'";
                    //if(dependency is ObjectGroupComponent)
                    //{
                    //    if(((ObjectGroupComponent)dependency).PchOptions != null)
                    //    {
                    //        //dependencyString += "\n\t\t\t '" + dependency.Alias + "-PCH'";
                    //    }
                    //}
                }
            }

            if(!string.IsNullOrEmpty(dependencyString))
            {
                sb.AppendFormat("\t.PreBuildDependencies\t\t   = {{ {0} }} \n ",
                    dependencyString);
            }

            sb.Append("}\n");
            
            return sb.ToString();
        }
    }

}
