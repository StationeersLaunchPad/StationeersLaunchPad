<p align="center"><img src="docs/SLP_logo.png" /></p>

# StationeersLaunchPad

A Stationeers mod loader that allows you to edit mod configuration at game startup. This is compatible with Bepinex and StationeersMods mods installed locally in the home folder or downloaded from steam workshop.

Visit the [StationeersLaunchPad docs](https://stationeerslaunchpad.github.io/docs/) for more info on StationeersLaunchPad and modding Stationeers.

## Installation

### Fresh
- Install BepInEx into game folder
  - [Download BepInEx 5.4](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip) and extract into game folder (right click game in steam, `Manage->Browse local files`).
  - should end up with `BepInEx` folder and `doorstop_config.ini` file in same folder as rocketstation.exe
  - run game once to create folder structure
  - __Linux/Steam Deck Only:__ If running on Linux/Steam Deck via Proton/Wine, you will need to [perform some additional installation steps](https://docs.bepinex.dev/articles/advanced/proton_wine.html)
    - Since you are running a windows executable via Proton, make sure to use the Windows version of BepInEx linked above.
- Install StationeersLaunchPad
  - download latest client zip from [Releases](https://github.com/StationeersLaunchPad/StationeersLaunchPad/releases)
  - extract into `BepInEx/plugins` folder

### Switch from StationeersMods
__StationeersLaunchPad and StationeersMods can't be installed together__. Both mod loaders serve the same purpose and having both installed can cause issues. StationeersLaunchPad was created to replace and upgrade StationeersMods.

- Remove `StationeersMods` folder from `Bepinex/plugins` in game folder
  - to open game folder: right click game in steam, `Manage->Browse local files`
- Install StationeersLaunchPad
  - download latest client zip from [Releases](https://github.com/StationeersLaunchPad/StationeersLaunchPad/releases)
  - extract into `BepInEx/plugins` folder

### Verify Installation
- If StationeersLaunchPad is installed correctly, you should see the LaunchPad window at the bottom of the loading screen
<img width="640" height="360" alt="image" src="https://github.com/user-attachments/assets/a184a340-d461-4f99-bbe3-8699101fe15e" />

## Usage

- Install mods into `%HOME%/documents/my games/stationeers/mods` or download them from steam workshop.
- Start game. Mods will automatically load
- If you want to reorder or enable/disable mods, click the loading window at the bottom when the game first opens
  - To resume loading and step through stages, click the highlighted stage upper-left of the loading window
- Mod will auto be updated unless otherwise chosen in configuration.

## Mod Profiles

Mod Profiles are optional saved lists of enabled mods and their load order. A profile can include Local, Workshop, and Repo mods, and the selected profile is applied automatically on startup.

<details>
<summary><b>How to create and use a profile</b></summary>

1. Open the LaunchPad menu during startup by clicking the SLP box that appears on the games "Boot Screen" (Splash Screen) and select the **Mod Profiles** tab.
2. Enable and disable the mods in the list on the left using the checkboxes
3. Enter a name under **Create from the current mod list**, then select **Create Profile**.
4. Use **Save Changes** after changing the enabled mods or their order. Use **Revert Changes** to restore the saved profile.
5. Select another profile from the profile picker when needed.

Select the built-in **Vanilla** profile to launch without any mods, or select **Disable Profiles** to return to the normal, classic workflow.

</details>

<details>
<summary><b>How to share a profile</b></summary>

Only profiles that exclusively contain mods from the workshop can be shared as compact `SLP1` codes (for now):

1. Save the profile, then open **SLP Window > Mod Profiles > Share Profile**.
2. Select **Copy SLP1 Code** and send the code to another user.
3. To import one, paste it into the same tab and select **Load SLP1 Code**.
4. SLP downloads missing Workshop items and applies the shared load order. Enter a name to save the imported list as a profile.

SLP1 codes contain only Workshop IDs, load order, and an integrity checksum. They do not contain mod files, local or Repo mods, or mod configuration data.

</details>

<details>
<summary><b>Profile FAQ</b></summary>

**Do I have to use profiles?**

No. Profiles are opt-in, and disabling them leaves the current mod list unchanged.

**What do "Unsaved changes" and "Revert Changes" mean?**

The mod list on the left is the working copy. **Save Changes** updates the selected profile; **Revert Changes** restores its saved enabled mods and order.

**What happens when a profile is missing mods?**

Loading pauses and lists the missing entries. Workshop items can be resubscribed directly; Local and Repo mods must be restored manually or removed from the profile.

**Where are profiles stored?**

Profiles are stored as separate XML files under the LaunchPad save path - usually `Documents\My Games\Stationeers`

</details>

## Dedicated Server

- Install Bepinex into dedicated server folder
  - [Download BepInEx 5.4](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip) and extract into dedicated server folder (`<steamcmd>/steamapps/common/Stationeers Dedicated Server`)
  - should end up with `BepInEx` folder and `doorstop_config.ini` file in same folder as `rocketstation_DedicatedServer.exe`
- Install StationeersLaunchPad
  - download latest server zip from [Releases](https://github.com/StationeersLaunchPad/StationeersLaunchPad/releases)
  - extract into `BepInEx/plugins` folder
- In the game client, click the loading window at the bottom on startup to open configuration
  - enable/disable and reorder mods to match what you want installed on the server
  - on the Launchpad Configuration tab, click `Export Mod Package` to create a zip file containing the enabled mods and config file
  - extract the zip file into the dedicated server folder (should create `modconfig.xml` and `mods` folder in same folder as `rocketstation_DedicatedServer.exe`)

## Modding
Information on modding stationeers can be found at:
- [Stationeers Modding Docs](https://stationeerslaunchpad.github.io/docs/) (work in progress)
- [Stationeers Discord](https://discord.gg/stationeers) #modding channel
- [Stationeers Modding Discord](https://discord.gg/5qZbPVTw2U)
- [LaunchPadBooster library](https://github.com/StationeersLaunchPad/LaunchPadBooster)

### Linking against StationeersLaunchPad
Linking against StationeersLaunchPad (using any class in the `StationeersLaunchPad` namespace or nested namespaces) is unsupported. These classes are not a stable API, and will change without notice. The code is MIT licensed so you are free to copy any utilities you want to use into your own mod. LaunchPadBooster also contains utilities with a stable API that can be used by mods. If there is something you need that only StationeersLaunchPad has, reach out on github or discord and it can be added as an injected parameter to the Default Entrypoint.

## v0.2.25 Update Error
StationeersLaunchPad v0.2.25 contains a bug that prevents the auto-update from functioning.

Please follow the [Manual Update Instructions](https://stationeerslaunchpad.github.io/docs/slp/update_error_0.2.25/) if you are encountering this error.

![Update Error](https://stationeerslaunchpad.github.io/docs/img/update_error_0.2.25.png)
