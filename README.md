# ModSettingsMenu

A BepInEx mod for MycoPunk that provides an in-game GUI for editing mod configuration files.

## Description

This mod allows players to edit configuration settings for other installed mods directly within the game interface, without needing to manually edit .cfg files. It parses BepInEx configuration files and presents them in an organized GUI.

Adapted from ToeKneeRED's MycoModList.

## Features

* In-game GUI for editing mod configuration files
* Default F10 keybind to toggle the config menu (rebindable in-game)
* "Mod Config" button in the main menu when the menu is open
* Support for editing boolean toggles, integer/float inputs, text strings, and dropdown selections
* Configuration entries organized by file sections
* Automatic saving of changes to .cfg files
* Visual indicators for sandbox mods
* Multi-mod support displaying all installed mods with configs

## Dependencies

* MycoPunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) - Version 5.4.2403 or compatible
* .NET Framework 4.8
* [HarmonyLib](https://github.com/pardeike/Harmony) (included via NuGet)

## Installation

1. Install BepInEx for MycoPunk
2. Download the mod from Thunderstore or place the .dll file in `<MycoPunk Directory>/BepInEx/plugins/`
3. Launch the game - the mod loads automatically through BepInEx

## Usage

* Press F10 (or your configured key) to open the mod configuration GUI
* Alternatively, open the main menu and click the "Mod Config" button
* Select a mod from the list to view and edit its configuration options
* Changes are saved automatically when modified

## Configuration

The mod itself has one configurable setting:
* Toggle Key: Key to open the config menu (default: F10) - can be rebound in-game by clicking the input field

## Help

* **Mod not loading?** Ensure BepInEx is installed and the .dll is in the correct plugins folder
* **Configs not showing?** The mod only displays mods that have .cfg files in the BepInEx config directory
* **Keybind not working?** Check for conflicts with other mods or rebind it in the GUI
* **GUI not appearing?** Verify the game is running and try toggling with the menu button

## Authors

- Sparroh
- ToeKneeRED (original MycoModList)
- funlennysub (BepInEx template)
- [@DomPizzie](https://twitter.com/dompizzie) (README template)

## License

This project is licensed under the MIT License - see the LICENSE file for details
