# ModSettingsMenu

A BepInEx mod for MycoPunk that provides an in-game GUI for editing mod configuration files, plus a quality-of-life **HUD Reposition Mode** for click-and-drag layout of mod HUD elements.

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
* **Mod list toolbar** — search by name/GUID, sort (A–Z, load order, sandbox first, …), hide mods without config, and All/Sandbox/Client-side filter chips
* **Collapse / expand** — click mod titles (or the sticky pinned header) to fold settings; Expand/Collapse all; optional group-by-author (GUID prefix)


* **HUD Reposition Mode** (default F9) — click and drag registered HUD elements; positions write back to each mod's AnchorX/AnchorY config


## Dependencies

* MycoPunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) - Version 5.4.2403 or compatible
* [SparrohUILib](https://thunderstore.io/c/mycopunk/p/Sparroh/SparrohUILib/) — shared themed UI (required)
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

### HUD Reposition Mode

* Press **F9** (or your configured key), or click **Reposition HUDs** in the main menu / Mod Config title bar
* Drag highlighted HUD elements to the desired position
* Coordinates (0–1 anchors) update live and are saved when you release the mouse
* Press **Esc** or the toggle key again to exit

Compatible HUD mods register themselves via the API. Unregistered mods that expose `*AnchorX` / `*AnchorY` config pairs may still be auto-detected when their HUD objects exist under the reticle.

## Configuration

The mod itself has configurable settings:

* **ToggleModConfigGUI**: Key to open the config menu (default: F10)
* **ToggleHudReposition**: Key to enter HUD reposition mode (default: F9)
* **ModSortMode**: List order — `Alphabetical` (default), `AlphabeticalDesc`, `LoadOrder`, `SandboxFirst`, `HasConfigFirst`
* **HideModsWithoutConfig**: Hide mods with no matching `.cfg` (default: true)
* **ModListFilter**: `All` (default), `Sandbox`, or `ClientSide`
* **GroupModsByAuthor**: Group list by GUID author prefix (default: false)
* **CollapsedMods**: Comma-separated mod keys that are collapsed (empty = all expanded)

Keybinds can be rebound in-game by clicking the input field in the Mod Config GUI. Sort, hide-empty, filter chips, grouping, and collapse state also update from the toolbar.


## For mod authors — HudRepositionAPI

Register your HUD after creating its `RectTransform`, and unregister on destroy:

```csharp
// Soft dependency (recommended) — copy HudRepositionClient.cs.example into your project
HudRepositionClient.Register(
    id: "your.mod.guid",
    displayName: "My HUD",
    rect: containerRect,
    anchorX: myAnchorX,   // ConfigEntry<float> 0-1
    anchorY: myAnchorY);

// On destroy / when HUD is destroyed:
HudRepositionClient.Unregister("your.mod.guid");
```

Or call `HudRepositionAPI` directly if you reference `ModSettingsMenu.dll`.

Convention:

* Section: `[HUD Positioning]`
* Keys: `{Name}AnchorX` / `{Name}AnchorY` as floats in **0–1** (anchorMin = anchorMax)
* Parent under the player reticle (or any screen-space canvas)
* Listen to `SettingChanged` on the config entries to apply anchors live

## Help

* **Mod not loading?** Ensure BepInEx is installed and the .dll is in the correct plugins folder
* **Configs not showing?** The mod only displays mods that have .cfg files in the BepInEx config directory
* **Keybind not working?** Check for conflicts with other mods or rebind it in the GUI
* **GUI not appearing?** Verify the game is running and try toggling with the menu button
* **HUD not draggable?** Ensure the HUD is visible (in-mission), registered via the API, or has matching AnchorX/Y config keys

## Authors

- Sparroh

## License

This project is licensed under the MIT License - see the LICENSE file for details
