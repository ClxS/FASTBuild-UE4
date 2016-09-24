There's been a bit of interest in all the steps required to get fastbuild to compile Unreal Engine 4, so I've repository together with all the steps required. For reference I'm using the unmodified 4.13 branch from GitHub, and an unmodified v0.91 fastbuild. This has only currently been tested on Windows builds with caching (without distribution) enabled. Full console support will come in the future.

# Setting up FASTBuild

Due to how fastbuild works you can pretty much place it where you want. I keep mine within the engine, with the following file structure.
* UE4
* * Engine
* * * Extras
* * * * FASTBuild
* * * * * FBuild.exe
* * * * * SDK 

Inside your SDK, setup junction links to your Windows SDK folder, as well as your Visual Studio 2015 folder.

# Modifying the Engine

There are a few files in the UnrealBuildTool project we need to modify, these are:
- Configuration/BuildConfiguration.cs
- Configuration/UEBuildPlatform.cs
- System/ActionGraph.cs
- Windows/UEBuildWindows.cs

We also need to add a new file to generate a BFF file from the provided actions list. For this, I'm only going to focus on the Windows platform.

### Configuration/BuildConfiguration.cs

- Add the following properties to the top of the file.

```
 // --> FASTBuild

/// <summary>
/// Whether FASTBuild may be used.
/// </summary>
[XmlConfig]
public static bool bAllowFastbuild;

/// <summary>
/// Whether linking should be disabled. Useful for cache 
/// generation builds
/// </summary>
[XmlConfig]
public static bool bFastbuildNoLinking;

/// <summary>
/// Whether the build should continue despite errors. 
/// Useful for cache generation builds
/// </summary>
[XmlConfig]
public static bool bFastbuildContinueOnError;

// <-- FASTBuild
```

- Add the following to the very bottom of the LoadDefaults method.

```
// --> FASTBuild

bAllowFastbuild = true;
bUsePDBFiles = false; //Only required if you're using MSVC

// <-- FASTBuild
```

Finally, add this to the ValidateConfiguration method near similar lines for XGE/Distcc/SNDBS

```
// --> FASTBuild
if(!BuildPlatform.CanUseFastbuild())
{
    bAllowFastbuild = false;
}
// <-- FASTBuild
```

### Configuration/UEBuildPlatform.cs

Near similar lines for XGE/Distcc/SNDBS, add the following method.

```
// --> FASTBuild
public virtual bool CanUseFastbuild()
{
    return false;
}
// <-- FASTBuild
```

### System/ActionGraph.cs

Alter the ExecuteActions method to run this check prior to the XGE one:
```
 // --> FASTBuild
if (BuildConfiguration.bAllowFastbuild)
{
    ExecutorName = "Fastbuild";

    FASTBuild.ExecutionResult FastBuildResult = FASTBuild.ExecuteActions(ActionsToExecute);
    if (FastBuildResult != FASTBuild.ExecutionResult.Unavailable)
    {
        ExecutorName = "FASTBuild";
        Result = (FastBuildResult == FASTBuild.ExecutionResult.TasksSucceeded);
        bUsedXGE = true;
    }
}
// <-- FASTBuild
```

And alter the XGE if to check for !bUseXGE, like this:
```
From:
if (BuildConfiguration.bAllowXGE || BuildConfiguration.bXGEExport)
To:
if (!bUsedXGE && (BuildConfiguration.bAllowXGE || BuildConfiguration.bXGEExport))
```

### Windows/UEBuildWindows

Near similar lines for SNDBS, add the following method:

```
// --> FASTBuild
public override bool CanUseFastbuild()
{
    return true;
}
// <-- FASTBuild
```

# Enabling Cache Generation

FASTBuildCOnfiguration.cs contains the EnableCacheGenerationMode property. By default this checks for whether an environment variable ("UE-FB-CACHE-WRITE") contains the value TRUE.

# Warnings as errors

Enabling warnings as errors will cause issues with certain things being falsely detected as digraphs, and will need to be fixed manually by adding a space to separate the "<::X"