using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnrealBuildTool.FBuild.BuildComponents
{
    public enum LinkerType
    {
        Static,
        Dynamic
    }

    public class LinkerComponent : ExecutableComponent
    {
        public string OutputLibrary { get; set; }

        public string ImportLibrary { get; set; }

        public List<string> Inputs { get; set; }

        public bool LocalOnly { get; set; }

        public LinkerType LinkerType
        {
            get
            {
                return OutputLibrary.EndsWith(".lib") ? LinkerType.Static : LinkerType.Dynamic;
            }
        }

        public LinkerComponent()
        {
            NodeType = "DLL";
        }


        public override string ToString()
        {
            if(LinkerType == LinkerType.Static)
            {
                return ToLibString();
            }
            else
            {
                return ToDllString();
            }
        }

        private string ToLibString()
        {
            string linkerArgs = Arguments;
            
            List<string> libraryInputs = null;
            List<string> systemInputs = null;
            
            if (Inputs.Count == 1)
            {
                string responseFile = Inputs[0];
                linkerArgs = linkerArgs.Replace(Inputs[0], "%1");
                linkerArgs = linkerArgs.Replace(OutputLibrary, "%2");
                GetResponseFileInputs(ref responseFile, FASTBuildConfiguration.UseSinglePassCompilation ? FASTBuild.AllActions : FASTBuild.LinkerActions, out libraryInputs, out systemInputs);
                                
                //If we found resolvable references in the response file, use those as the inputs and re-add the response file.
                //If not, use the response file itself just to stop fbuild erroring.
                if (libraryInputs.Any())
                {
                    linkerArgs = linkerArgs.Replace("%1", responseFile + "\" \"%1");
                }
                else
                {
                    libraryInputs.Add(responseFile);
                }
            }
            else
            {
                throw new Exception("Don't know what to do here yet");
            }


            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Library('{0}')\n{{\n", Alias);

            //We do not provide file inputs (we use separate ObjectLists) for simplicity, so we do not need to specify an actually defined compiler.
            sb.AppendFormat("\t.Compiler\t\t = 'Null'\n"); 
            sb.AppendFormat("\t.CompilerOptions\t\t = '%1 %2 %3'\n");
            sb.AppendFormat("\t.CompilerOutputPath\t\t = ' '\n");
            sb.AppendFormat("\t.Librarian\t\t            = '{0}' \n " +
                            "\t.LibrarianOutput\t\t  =  '{1}' \n",
                            Linker.ExecPath,
                            OutputLibrary);
            sb.AppendFormat("\t.LibrarianOptions\t\t = '{0}'\n", linkerArgs);
            if (systemInputs.Any())
            {
                sb.AppendFormat("\t.LibrarianOptions\t\t   {0} \n ",
                string.Join("\n\t\t\t", systemInputs.Select(d => "+ ' " + d + "'")));
            }
            if(libraryInputs.Any())
            {
                sb.AppendFormat("\t.LibrarianAdditionalInputs\t\t   = {{ {0} }} \n ",
                                    string.Join("\n\t\t\t", libraryInputs.Select(d => "'" + d + "'")));
            }
            //sb.AppendFormat("\t.LibrarianAdditionalInputs\t\t   = {{ {0} }} \n ",
            //                        string.Join("\n\t\t\t", Inputs.Select(d => "'" + d + "'")));


            sb.Append("}\n");

            return sb.ToString();
        }
        
        private string ToDllString()
        {
            string linkerArgs = Arguments;

            List<string> libraryInputs = null;
            List<string> systemInputs = null;
            
            if (Inputs.Count == 1)
            {
                string responseFile = Inputs[0];
                linkerArgs = linkerArgs.Replace(Inputs[0], "%1");
                linkerArgs = linkerArgs.Replace(OutputLibrary, "%2");
                GetResponseFileInputs(ref responseFile, FASTBuildConfiguration.UseSinglePassCompilation ? FASTBuild.AllActions : FASTBuild.LinkerActions, out libraryInputs, out systemInputs);

                //If we found resolvable references in the response file, use those as the inputs and re-add the response file.
                //If not, use the response file itself just to stop fbuild erroring.
                if (libraryInputs.Any())
                {
                    linkerArgs = linkerArgs.Replace("%1", responseFile + "\" \"%1");
                }
                else
                {
                    libraryInputs.Add(responseFile);
                }
            }
            else
            {
                throw new Exception("Don't know what to do here yet");
            }
            
            linkerArgs = linkerArgs.Replace(OutputLibrary, "%2");
            
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("DLL('{0}')\n{{\n", Alias);
            sb.AppendFormat("\t.LinkerLinkObjects\t\t = false\n");
            sb.AppendFormat("\t.Linker\t\t            = '{0}' \n " +
                            "\t.LinkerOutput\t\t  =  '{1}' \n",
                            Linker.ExecPath,
                            OutputLibrary);
            sb.AppendFormat("\t.LinkerOptions\t\t = '{0}'\n", linkerArgs);
            if (systemInputs.Any())
            {
                //sb.AppendFormat("\t.LinkerOptions\t\t   {0} \n ",
                //string.Join("\n\t\t\t", systemInputs.Select(d => "+ ' " + d + "'")));
            }
            sb.AppendFormat("\t.Libraries\t\t   = {{ {0} }} \n ",
                                    string.Join("\n\t\t\t", libraryInputs.Select(d => "'" + d + "'")));


            sb.Append("}\n");

            return sb.ToString();
        }

        private void GetResponseFileInputs(ref string file, List<BuildComponent> resolvableDependencies, out List<string> inputs, out List<string> systemInputs)
        {
            var lines = File.ReadAllLines(file);

            inputs = new List<string>();
            systemInputs = new List<string>();
            var unresolvedLines = lines.Select(n => n).ToList();
            
            for(int i = 0; i < lines.Length; ++i)
            {
                //Nasty, but fixes FASTBuild not letting explicitly provide the output file path to work with UE4s PCH name formatting
                if (lines[i].Contains("SharedPCHs"))
                {
                    //lines[i] = lines[i].Replace("SharedPCHs\\", "SharedPCHs\\PCH.");

                    //Also fixup the unresolved line, just in case
                    //unresolvedLines[i] = lines[i];
                }

                // Strip the additional quotes from the response file
                var resolvedLine = lines[i].Replace("\"", "");                

                // UE4 Provides response outputs to project files as absolute paths.
                // A quick way to check if something is a system include is whether it is a rooted path or not
                if (Path.IsPathRooted(resolvedLine))
                {
                    // We should resolve project includes to see if we're building the node for that this pass as well
                    BuildComponent matchingDependency = null;

                    foreach(var dependency in resolvableDependencies)
                    {
                        if(dependency is LinkerComponent)
                        {
                            var linkDependency = (LinkerComponent)dependency;
                            if(linkDependency.ImportLibrary == resolvedLine || linkDependency.OutputLibrary == resolvedLine)
                            {
                                matchingDependency = dependency;
                                break;
                            }
                        }
                        else if(dependency is ObjectGroupComponent)
                        {
                            var objectDependency = (ObjectGroupComponent)dependency;
                            if (objectDependency.ActionOutputs.Contains(resolvedLine) ||
                                (objectDependency.PchOptions != null && 
                                Path.ChangeExtension(objectDependency.PchOptions.Output, objectDependency.OutputExt) == resolvedLine))
                            {
                                matchingDependency = dependency;
                                break;
                            }
                        }
                    }

                    //if (FASTBuildConfiguration.UseSinglePassCompilation || matchingDependency != null)
                    {
                        unresolvedLines.Remove(lines[i]);
                        if (matchingDependency != null)
                        {
                            resolvedLine = matchingDependency.Alias;
                        }
                        inputs.Add(resolvedLine);
                    }
                }
            }

            file += ".fbuild";
            File.WriteAllLines(file, unresolvedLines);
        }
    }
}
