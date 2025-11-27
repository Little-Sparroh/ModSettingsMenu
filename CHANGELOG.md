# Changelog

## 1.0.0 (2025-08-19)

### Features
* In-game GUI for editing mod configuration files
* Default F10 keybind to toggle config menu (rebindable in-game)
* "Mod Config" button in main menu when menu is open
* Support for editing boolean toggles, integer/float inputs, and text strings
* Organized display by config file sections
* Automatic saving of changes to .cfg files
* Sandbox mod indicators in the GUI
* Multi-mod support showing all installed mods with configs
* Adapted from ToeKneeRED's MycoModList

### Technical
* Adapted from ToeKneeRED's MycoModList
* Built with BepInEx framework and HarmonyLib
* Parses standard BepInEx .cfg configuration files
* GUI created as Unity overlay compatible with MycoPunk
* Key rebinding functionality with real-time feedback

### Tech
* Initial mod template setup with BepInEx framework
* Add MinVer for version management
* Add thunderstore.toml configuration for mod publishing
* Add LICENSE.md and CHANGELOG.md template files
* Basic plugin structure with HarmonyLib integration
* Placeholder for mod-specific functionality
