# ModSettingsMenu

A BepInEx mod for MycoPunk that provides an in-game GUI for editing mod configuration files, plus a quality-of-life *
*HUD Reposition Mode** for click-and-drag layout of mod HUD elements.

## Description

This mod lets players edit configuration settings for other installed mods directly in-game, without manually editing
`.cfg` files. It parses BepInEx configuration files and presents them in an organized, themed GUI (via SparrohUILib).

It also includes **HUD Reposition Mode** so compatible mods can expose draggable HUD elements whose positions write back
to each mod's AnchorX/AnchorY config.

Adapted from ToeKneeRED's MycoModList.

## Features

* In-game GUI for editing mod configuration files (SparrohUILib themed UI)
* Default **F10** keybind to toggle the config menu (rebindable in-game)
* "Mod Config" button in the main menu when the menu is open
* Support for boolean toggles, integer/float inputs, text strings, dropdown selections, and optional ranged float
  sliders (SparrohUILib)

* Configuration entries organized by file sections
* Changes write to the plugin's live `ConfigEntry` (so `SettingChanged` fires and mods can hot-reload) and save to
  `.cfg` files
* Visual indicators for sandbox mods
* Multi-mod support displaying all installed mods with configs
* **Sticky mod titles** — while scrolling, the current mod's title pins to the top of the viewport
* **Sticky-aware row snap** — wheel scroll snaps one content row at a time and aligns under the pinned title
* **Mod list toolbar** — search by name/GUID, sort (A–Z, Z–A, load order, sandbox first, has-config first), hide mods
  without config, and All/Sandbox/Client-side filter chips
* **Collapsible toolbar filters** — +/- beside the search bar hides sort/filter/options rows (search stays visible);
  state persisted
* **Collapse / expand** — click mod titles (or the sticky pinned header) to fold settings; Expand/Collapse all; optional
  group-by-author (GUID prefix)
* **HUD Reposition Mode** (default **F9**) — click and drag registered HUD elements; positions write back to each mod's
  AnchorX/AnchorY config

## Dependencies

* MycoPunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) — version 5.4.2403 or compatible
* [SparrohUILib](https://thunderstore.io/c/mycopunk/p/Sparroh/SparrohUILib/) — shared themed UI (**1.1.6+** required)
* [HarmonyLib](https://github.com/pardeike/Harmony) (included via BepInEx)

## Installation

1. Install BepInEx for MycoPunk
2. Download the mod from Thunderstore, or place the `.dll` in `<MycoPunk Directory>/BepInEx/plugins/`
3. Launch the game — the mod loads automatically through BepInEx

## Usage

* Press **F10** (or your configured key) to open the mod configuration GUI
* Alternatively, open the main menu and click the "Mod Config" button
* Use the toolbar to search, sort, filter, and collapse mods as needed
* Select a mod from the list to view and edit its configuration options
* Changes are applied and saved when modified

### HUD Reposition Mode

* Press **F9** (or your configured key), or click **Reposition HUDs** in the main menu / Mod Config title bar
* Drag highlighted HUD elements to the desired position
* Coordinates (0–1 anchors) update live and are saved when you release the mouse
* Press **Esc** or the toggle key again to exit

Compatible HUD mods register themselves via the API. Unregistered mods that expose `*AnchorX` / `*AnchorY` config pairs
may still be auto-detected when their HUD objects exist under the reticle.

## Configuration

The mod itself has configurable settings (BepInEx config entry names):

### Keybinds

* **Toggle Config GUI** — key to open the config menu (default: `F10`)
* **Toggle Hud Reposition** — key to enter HUD reposition mode (default: `F9`)

### UI

* **Mod Sort Mode** — list order: `Alphabetical` (default), `AlphabeticalDesc`, `LoadOrder`, `SandboxFirst`,
  `HasConfigFirst`
* **Hide Mods Without Config** — hide mods with no matching `.cfg` (default: `true`)
* **Mod List Filter** — `All` (default), `Sandbox`, or `ClientSide`
* **Group Mods By Author** — group list by GUID author prefix (default: `false`)
* **Collapsed Mods** — comma-separated mod keys that are collapsed (empty = all expanded)
* **Toolbar Filters Collapsed** — hide toolbar filter/sort/options rows; search stays visible (default: `false`)
* **Use Float Sliders** — when enabled, float settings with a defined min/max range render as SparrohUILib sliders
  instead of text fields (default: `false`)

Keybinds can be rebound in-game by clicking the input field in the Mod Config GUI. Sort, hide-empty, filter chips,
grouping, and collapse state also update from the toolbar.

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

* **Mod not loading?** Ensure BepInEx is installed and the `.dll` is in the correct plugins folder
* **Configs not showing?** The mod only displays mods that have `.cfg` files in the BepInEx config directory (unless "
  Hide Mods Without Config" is off)
* **Keybind not working?** Check for conflicts with other mods or rebind it in the GUI
* **GUI not appearing?** Verify the game is running and try toggling with the menu button
* **Dropdowns unusable / no caret in fields?** Update SparrohUILib to **1.1.6+**
* **HUD not draggable?** Ensure the HUD is visible (in-mission), registered via the API, or has matching AnchorX/Y
  config keys

## Authors

- Sparroh

## License

This project is licensed under the MIT License — see the LICENSE file for details.
