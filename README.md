<p align="center"><img src="docs/SLP_logo.png" /></p>

# StationeersLaunchPad

A simple mod loader for Stationeers that allows you to edit mod configuration at game startup. This is compatible with Bepinex and StationeersMods mods installed locally in the home folder or downloaded from steam workshop.

## Installation

### Fresh
- Install BepInEx into game folder
  - [Download BepInEx 5.4](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip) and extract into game folder (right click game in steam, `Manage->Browse local files`).
  - should end up with `BepInEx` folder and `doorstop_config.ini` file in same folder as rocketstation.exe
- Install StationeersLaunchPad
  - download latest zip from [Releases](https://github.com/StationeersLaunchPad/StationeersLaunchPad/releases)
  - extract into `BepInEx/plugins` folder

### Switch from StationeersMods
- Remove `StationeersMods` folder from `Bepinex/plugins` in game folder
  - to open game folder: right click game in steam, `Manage->Browse local files`
- Install StationeersLaunchPad
  - download latest client zip from [Releases](https://github.com/StationeersLaunchPad/StationeersLaunchPad/releases)
  - extract into `BepInEx/plugins` folder

## Usage

- Install mods into `%HOME%/documents/my games/stationeers/mods` or download them from steam workshop.
- Start game. Mods will automatically load
- If you want to reorder or enable/disable mods, click the loading window at the bottom when the game first opens
- Mod will auto be updated unless otherwise chosen in configuration.

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