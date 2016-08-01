### NOTE:
- This project requires a small modification to FASTBuild to work properly. This incompatibility is due to using DLL nodes where Library nodes should have been used. The upcoming update to this script will resolve this issue, until then, you'll need to use the workaround under the 'Modifying Fastbuild' heading

There's been a bit of interest in all the steps required to get fastbuild to compile Unreal Engine 4, so I've repository together with all the steps required. For reference I'm using the unmodified 4.10 branch from GitHub, and a slightly modified v0.88 fastbuild.

These are just the basic steps to get it building. A lot of improvements can be made.

# Setting up FASTBuild

Due to how fastbuild works you can pretty much place it where you want. I keep mine within the engine, with the following file structure.
* UE4
* * Engine
* * * Extras
* * * * FASTBuild
* * * * * FBuild.exe
* * * * * SDK 

Inside your SDK, setup junction lines to your Windows SDK folder, as well as your Visual Studio 2015 folder.

# Modifying fastbuild

Until a proper fix is implemented, I've used a small workaround to fix a bug with Fastbuild where DLL nodes using LIB.exe get reported as EXE_NODEs, so cannot be used as dependencies. To fix this, go to FunctionExecutable::DependOnNode, and add the following block.

```
// an external executable ?
if (node->GetType() == Node::EXE_NODE)
{
	// depend on ndoe - will use exe output at build time
	nodes.Append(Dependency(node));
	return true;
}
```

# Modifying the Engine

There are a few files we need to modify, these are:
- ActionGraph.cs
- BuildConfiguration.cs
- UEBuildPlatform.cs
- UEBuildWindows.cs

We also need to add a new file to generate a BFF file from the provided actions list. For this, I'm only going to focus on the Windows platform.

### UEBuildPlatform.cs

In IUEBuildPlatform, add a new function
```
bool CanUseFastbuild();
```

In UEBuildPlatform, add a new function
```
public virtual bool CanUseFastbuild() { return false; }
```

### UEBuildWindows.cs

In WindowsPlatform, add a new function
```
public override bool CanUseFastbuild() { return true; }
```

### ActionGraph.cs

In ActionGraph::ExecuteActions, add the following bit of code. I've added it between the check for XGE and Distcc.

```
if (!bUsedXGE && BuildConfiguration.bAllowFastbuild)
{
    ExecutorName = "Fastbuild";

    FastBuild.ExecutionResult FastBuildResult = FastBuild.ExecuteActions(ActionsToExecute);
    if (FastBuildResult != FastBuild.ExecutionResult.Unavailable)
    {
        ExecutorName = "XGE";
        Result = (FastBuildResult == FastBuild.ExecutionResult.TasksSucceeded);
        // don't do local compilation
        bUsedXGE = true;
    }
}
```

### BuildConfiguration.cs

Near similar lines for Distcc/SNDBS, add:
```
[XmlConfig]
public static bool bAllowFastbuild;
```

Inside LoadDefaults(), set

```
bAllowFastbuild = true;
bUsePDBFiles = false; //Only required if you're using MSVC
bUsePCHFiles = false; //temporary until compilation issue fixed
```

Inside ValidateConfiguration(), add

```
if (!BuildPlatform.CanUseFastbuild()) 
{
	bAllowFastbuild = false;
}
```

### FastBuild.cs

** NOTE: This is still a work in progress and hasn't been heavily tested. Let me know if something does not work by submitting an issue [here](https://github.com/ClxS/fastbuild-ue4/issues) **

https://github.com/ClxS/fastbuild-ue4/blob/master/FastBuild.cs
This is a quick example for how to generate a BFF from the Actions provided by the Unreal Build Tool. There are still a few major improvements to make which will be improved in later revisions, such as:
- Improve ReadCommand()
- Support platforms other than Windows/VS2015
- Change FBuild location to use an environment variable, rather than hard coded paths
- Test which ExtraFiles VS2015 actually needs. I just grabbed the common ones.
- Add PreBuildDependencies node to DLL, so that it'll be possible to combine both object compilation and linking into a single pass.
- It will likely require an addition to fastbuild, but we would see performance improvements if we wrote to a an already parsed binary format, and read that rather than parsing the BFF as they can be quite large. (10,000 lines for UE4Editor)

A few things of note:
- It uses the DLL node for all linking steps. This is because FBuild's node types aren't strictly for certain steps, but just describe behaviour. The DLL node is the only one which fits our requirements. (I think?)

# Fixing Compilation Errors

Unreal has a fair few compilation errors which need to be resolved before using FASTBuild with certain settings. Most of them are include issues, so Whether or not you hit them depends on your project as the unity build system covers many of them up. 
Other issues include a (apparently unreported) bug with how VC++ compiles from preprocessed source, which becomes apparent when forcing Warnings as Errors on.
