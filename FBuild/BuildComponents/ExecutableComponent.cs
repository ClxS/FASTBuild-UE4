using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild.BuildComponents
{
    public class ExecutableComponent : BuildComponent
    {        
        public Linker Linker;

        public string Arguments { get; set; }

        public ExecutableComponent()
        {
            NodeType = "Exec";
        }

        public override string ToString()
        {
            //Carry on here. Need to strip input/output out, or change to not require in/out
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Exec('{0}')\n{{\n", Alias);
            sb.AppendFormat("\t.ExecExecutable\t\t  = '{0}' \n " +
                            "\t.ExecArguments\t\t  = '{1}'\n" +
                            "\t.DoNotUseOutput = true\n" +
                            "\t.ExecOutput\t\t = '{2}-{0}'\n",
                            Linker.ExecPath,
                            Arguments, Alias);

            if (Dependencies.Any())
            {
                sb.AppendFormat("\t.PreBuildDependencies\t\t= {{ {0} }} \n ",
                    string.Join("\n\t\t\t", Dependencies.Select(d => "'" + d.Alias + "'").Distinct()));
            }
            sb.Append("}\n");

            return sb.ToString();
        }
    }

}
