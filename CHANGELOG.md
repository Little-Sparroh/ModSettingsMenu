# Changelog

## 2.0.3

### Fixes
* **Dropdown menus unusable** — option lists no longer draw behind later rows or get clipped by the scroll mask (requires SparrohUILib 1.1.5+; lists reparent to the window canvas while open)

## 2.0.2


### Features
* **Collapsible toolbar filters** — +/- button to the right of the search bar hides sort, filter chips, and options rows while keeping search visible, freeing vertical space for settings; preference persisted as `ToolbarFiltersCollapsed`

## 2.0.1

### Fixes
* Config toggles/inputs now write the **plugin's live ConfigEntry** (via Chainloader) so `SettingChanged` fires and mods hot-reload immediately — previously MSM only wrote a detached string ConfigFile, so in-memory settings never updated until restart


## 2.0.0


### Features
* **Sticky mod titles** — while scrolling the config list, the current mod's title pins to the top of the viewport until the next mod pushes it off (so you always know which mod you're editing)
* **Sticky-aware row snap** — wheel scroll snaps one full content row at a time (any height) and aligns under the pinned title so rows don't rest half-clipped
* **Mod list toolbar** — fixed bar above the scroll list with search, sort, hide-empty, and filter chips
* **Search mods** — filter the list by mod name or GUID as you type (clears each time the menu opens)
* **Sort modes** — A–Z, Z–A, load order, sandbox first, has-config first (persisted in config)
* **Hide empty** — optionally hide mods with no matching `.cfg` (default on; persisted)
* **Filter chips** — All / Sandbox / Client-side (single-select; persisted)
* **Collapse / expand mods** — click a mod title (+/−) to fold its settings; default all expanded; state persisted
* **Sticky title collapse** — click the pinned sticky header to collapse/expand the active mod (same as the in-list title)
* **Expand / Collapse all** — toolbar button toggles every visible mod section
* **Group by author** — optional grouping by GUID prefix (e.g. `sparroh.*`); off by default



### Changes

* **SparrohUILib GUI** — config menu rebuilt with `UIWindow`, themed widgets, TMP text, and the Mycopunk teal/slate palette (was still using hand-rolled legacy Unity UI)
* **Readability** — darker surfaces under all text (`PanelBg` / `EntryBg` / `InputBg`); larger bold titles, section headers, and labels
* **Layout polish** — narrower window (~520px), title shortened to "Mod Configs", wider value/editor controls per row; opening Mod Config (F10) exits HUD Reposition mode


### Fixes
* **Scroll wheel** — each wheel notch now advances the config list by one item (was nearly unusable at `0.1` sensitivity)
* **Accidental edits** — toggles only flip on a clean click (not drag-pan); text fields require an explicit click to start editing and save on end-edit; scrolling or drag-panning clears focus and closes dropdowns
* **Cursor lost with menu open** — closing ModSettingsMenu while the in-game Menu is open no longer locks/hides the cursor; FreeCursor now stacks with `PlayerInput.UnlockCursor` / `LockCursor`


## 1.2.0 (2026-07-10)


### Features
* **HUD Reposition Mode** — click-and-drag HUD elements instead of typing X/Y anchors
* Default **F9** keybind to toggle reposition mode (rebindable in-game)
* "Reposition HUDs" button in the main menu and Mod Config title bar
* Public `HudRepositionAPI` for other mods to register draggable HUD elements
* Optional auto-detect of `*AnchorX` / `*AnchorY` config pairs under the reticle
* Live coordinate labels while dragging; positions saved to BepInEx configs on release

### Technical
* Soft-dependency friendly registration API (`HudRepositionAPI.Register` / `Unregister`)
* Example soft-dependency client helper: `HudRepositionClient.cs.example`

1.1.1 (2026-01-09)
Changes

    Button Adjustments
    Lost this update, it never made it to github.

## 1.1.0 (2025-12-24)

### Features
* Support for dropdown selections for configuration entries with predefined acceptable values

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
