# StationeersLaunchPad

## How it works

`LaunchPadPlugin` Bepinex plugin patches the `SplashBehaviour` so it doesn't start loading the game immediately, then calls `LaunchPadConfig.Run`. The patch to `SplashBehavour.Draw` draws a mod loading window (`LaunchPadGUI`) instead of the normal progress bar.

`LaunchPadConfig` loads the list of local and workshop mods, then the `modconfig.xml` file and sets up a tentantive runtime mod configuration. If the user doesn't click the loading window in a fixed time after startup (currently 3 seconds), it will automatically start loading the mod assemblies and resources, and then the game (handled in `LaunchPadConfig.Run`). 

If the user clicks the loading window (`LaunchPadGUI.DrawAutoLoad`), it opens a larger configuration editor allowing changes to enabled mods and order before load (`LaunchPadGUI.DrawManualLoad`).

The loading is currently done one mod at a time (`LinearLoadStrategy` in `ModLoader.cs`), but could be parallelized later based on dependencies if load time is an issue.

## Code Organization

### [LaunchPadPlugin.cs](LaunchPadPlugin.cs)
Bepinex plugin entrypoint to the mod loader.

### [LaunchPadConfig.cs](LaunchPadConfig.cs)
Main workflow logic of loading mod configuration and mods.

### [LaunchPadGUI.cs](LaunchPadGUI.cs)
IMGui mod configuration window.

### [LaunchPadUpdater.cs](LaunchPadUpdater.cs)
Main class for handling automatic plugin updating.

### [ModLoader.cs](ModLoader.cs)
Contains the workflow for loading enabled mods into the game, currently just a simple linear load.
Also contains the logic for finding Bepinex and StationeersMods entry points in the mod files.

### [ModInfo.cs](ModInfo.cs)
Information about an installed mod (name, folder, about.xml).

### [LoadedMod.cs](LoadedMod.cs)
Contains the implementations of loading in mod files, and information about load status.

### [Logger.cs](Logger.cs)
A logger that can be scoped to a specific mod, as well as a log handler that associates log messages with a specific mod. This works, but the scoped logs can only be viewed during startup for now.

### [AssemblyInfo.cs](AssemblyInfo.cs)
Simple structure holding assembly name and the names of other assemblies it directly references. Part of a prototype of adding implicit dependencies based on assembly references. Currently this information is loaded but unused.