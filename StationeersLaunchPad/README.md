<p align="center"><img src="docs/SLP_logo.png" /></p>

StationeersLaunchPad

A Stationeers mod loader that allows you to edit mod configuration at game startup. This is compatible with BepInEx and StationeersMods mods installed locally in the home folder or downloaded from Steam Workshop.

Visit the StationeersLaunchPad docs for more info on StationeersLaunchPad and modding Stationeers.

Installation

Fresh

Install BepInEx into the game folder

Download BepInEx 5.4 and extract it into the game folder (right click the game in Steam, Manage -> Browse local files).

You should end up with a BepInEx folder and doorstop_config.ini in the same folder as rocketstation.exe.

Run the game once to create the folder structure.

Linux/Steam Deck only: If running through Proton/Wine, follow the additional BepInEx installation steps.

Since Stationeers is running as a Windows executable through Proton, use the Windows version of BepInEx linked above.

Install StationeersLaunchPad

Download the latest client zip from Releases.

Extract it into the BepInEx/plugins folder.

Switch from StationeersMods

StationeersLaunchPad and StationeersMods can't be installed together. Both mod loaders serve the same purpose and having both installed can cause issues. StationeersLaunchPad was created to replace and upgrade StationeersMods.

Remove the StationeersMods folder from BepInEx/plugins in the game folder.

To open the game folder: right click the game in Steam, Manage -> Browse local files.

Install StationeersLaunchPad

Download the latest client zip from Releases.

Extract it into the BepInEx/plugins folder.

Verify Installation

If StationeersLaunchPad is installed correctly, you should see the LaunchPad window at the bottom of the loading screen.

<img width="640" height="360" alt="image" src="https://github.com/user-attachments/assets/a184a340-d461-4f99-bbe3-8699101fe15e" />

Usage

Install mods into %HOME%/documents/my games/stationeers/mods or download them from Steam Workshop.

Start the game. Mods will automatically load.

If you want to reorder or enable/disable mods, click the loading window at the bottom when the game first opens.

To resume loading and step through stages, click the highlighted stage in the upper-left of the loading window.

Mods will auto-update unless otherwise chosen in configuration.

Mod Profiles

Mod Profiles are optional saved lists of enabled mods and their load order. A profile can include Local, Workshop, and Repo mods, and the selected profile is applied automatically on startup.

<details>
<summary><b>How to create and use a profile</b></summary>

Open the LaunchPad menu during startup by clicking the SLP box that appears on the game's "Boot Screen" (Splash Screen) and select the Mod Profiles tab.

Enable and disable the mods in the list on the left using the checkboxes.

Enter a name under Create from the current mod list, then select Create Profile.

Use Save Changes after changing the enabled mods or their order. Use Revert Changes to restore the saved profile.

Select another profile from the profile picker when needed.

Select the built-in Vanilla profile to launch without any mods, or select Disable Profiles to return to the normal, classic workflow.

</details>

<details>
<summary><b>How to share a profile</b></summary>

Only profiles that exclusively contain mods from the Workshop can be shared as compact SLP1 codes (for now):

Save the profile, then open SLP Window > Mod Profiles > Share Profile.

Select Copy SLP1 Code and send the code to another user.

To import one, paste it into the same tab and select Load SLP1 Code.

SLP downloads missing Workshop items and applies the shared load order. Enter a name to save the imported list as a profile.

SLP1 codes contain only Workshop IDs, load order, and an integrity checksum. They do not contain mod files, local or Repo mods, or mod configuration data.

</details>

<details>
<summary><b>Profile FAQ</b></summary>

Do I have to use profiles?

No. Profiles are opt-in, and disabling them leaves the current mod list unchanged.

What do "Unsaved changes" and "Revert Changes" mean?

The mod list on the left is the working copy. Save Changes updates the selected profile; Revert Changes restores its saved enabled mods and order.

What happens when a profile is missing mods?

Loading pauses and lists the missing entries. Workshop items can be resubscribed directly; Local and Repo mods must be restored manually or removed from the profile.

Where are profiles stored?

Profiles are stored as separate XML files under the LaunchPad save path - usually Documents\My Games\Stationeers.

</details>

Dedicated Server

Install BepInEx into the dedicated server folder.

Download BepInEx 5.4 and extract it into the dedicated server folder (<steamcmd>/steamapps/common/Stationeers Dedicated Server).

You should end up with a BepInEx folder and doorstop_config.ini in the same folder as rocketstation_DedicatedServer.exe.

Install StationeersLaunchPad.

Download the latest server zip from Releases.

Extract it into the BepInEx/plugins folder.

In the game client, click the loading window at the bottom on startup to open configuration.

Enable/disable and reorder mods to match what you want installed on the server.

On the LaunchPad Configuration tab, click Export Mod Package to create a zip file containing the enabled mods and config file.

Extract the zip file into the dedicated server folder. It should create modconfig.xml and a mods folder in the same folder as rocketstation_DedicatedServer.exe.

Modding

Information on modding Stationeers can be found at:

Stationeers Modding Docs (work in progress)

Stationeers Discord #modding channel

Stationeers Modding Discord

LaunchPadBooster library

Default entrypoint lifecycle

The existing supported OnLoaded(...) parameter signatures are unchanged. An OnLoaded(...) method may return either void or bool.

Returning bool allows a mod to report whether initialization succeeded:

public bool OnLoaded(List<GameObject> prefabs, ConfigFile config)
{
// Initialize the mod.
return true;
}

Returning false tells StationeersLaunchPad that initialization failed and the mod should be cleaned up when it is safe to do so.

Existing mods using void OnLoaded(...) remain supported.

Unloading

A reversible default entrypoint can provide a parameterless void OnUnloaded() method:

public void OnUnloaded()
{
// Undo Harmony patches, event subscriptions,
// registrations, and other changes made by the mod.
}

OnUnloaded() can also be used while cleaning up a partially initialized mod, so implementations should tolerate partial initialization.

Mods may optionally provide a parameterless bool CanUnload() method:

public bool CanUnload()
{
return true;
}

CanUnload() is checked before a normal/manual unload. Returning false prevents the unload at that time. If the method is absent, it defaults to true.

StationeersLaunchPad checks all initialized entrypoints before beginning a manual unload so one entrypoint cannot veto after cleanup has already started.

Current unload support is intended for the StationeersLaunchPad startup/loading workflow. Unloading while a world is running is not part of the supported lifecycle.

Safe Mode

Safe Mode only loads default entrypoints that advertise a reversible lifecycle.

A default entrypoint is Safe Mode compatible when:

it uses one of the existing supported OnLoaded(...) parameter signatures;

OnLoaded(...) returns bool;

it provides a parameterless void OnUnloaded() method.

CanUnload() is optional and controls whether a compatible mod may be unloaded at a particular moment.

Existing mods using void OnLoaded(...) remain supported during normal loading, but are not considered Safe Mode compatible.

Cleanup responsibilities

StationeersLaunchPad cleans resources that it owns, including loaded AssetBundles, entrypoint GameObjects, configuration handlers, and its internal mod/assembly references.

StationeersLaunchPad does not physically unload CLR/Mono assemblies. Static state in a mod assembly can therefore remain alive for the lifetime of the game process.

Mods are responsible for reversing changes they make themselves, such as Harmony patches, event subscriptions, and direct changes to game state.

Resources registered through LaunchPadBooster should be removed through LaunchPadBooster's corresponding removal APIs from OnUnloaded().

Linking against StationeersLaunchPad

Linking against StationeersLaunchPad (using any class in the StationeersLaunchPad namespace or nested namespaces) is unsupported. These classes are not a stable API, and will change without notice. The code is MIT licensed so you are free to copy any utilities you want to use into your own mod. LaunchPadBooster also contains utilities with a stable API that can be used by mods. If there is something you need that only StationeersLaunchPad has, reach out on GitHub or Discord and it can be added as an injected parameter to the Default Entrypoint.

v0.2.25 Update Error

StationeersLaunchPad v0.2.25 contains a bug that prevents the auto-update from functioning.

Please follow the Manual Update Instructions if you are encountering this error.