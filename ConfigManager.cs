using BepInEx.Configuration;
using BepInEx.Logging;

public static class ConfigManager
{
    private static ConfigFile _config;
    public static ConfigEntry<string> ToggleKey { get; private set; }
    public static ConfigEntry<string> RepositionKey { get; private set; }
    public static ConfigEntry<string> ModSortMode { get; private set; }
    public static ConfigEntry<bool> HideModsWithoutConfig { get; private set; }
    public static ConfigEntry<string> ModListFilter { get; private set; }
    public static ConfigEntry<bool> GroupModsByAuthor { get; private set; }
    public static ConfigEntry<string> CollapsedMods { get; private set; }
    public static ConfigEntry<bool> ToolbarFiltersCollapsed { get; private set; }
    public static ConfigEntry<bool> UseFloatSliders { get; private set; }

    public static void Initialize(ConfigFile config, ManualLogSource log)
    {
        _config = config;

        ToggleKey = config.Bind("Keybinds", "Toggle Config GUI", "F10", "Key to toggle mod config GUI");
        RepositionKey = config.Bind("Keybinds", "Toggle Hud Reposition", "F9",
            "Key to toggle HUD reposition mode (click-and-drag HUD elements)");

        ModSortMode = config.Bind(
            "UI",
            "Mod Sort Mode",
            "Alphabetical",
            "How to order mods in the config list: Alphabetical, AlphabeticalDesc, LoadOrder, SandboxFirst, HasConfigFirst");
        HideModsWithoutConfig = config.Bind(
            "UI",
            "Hide Mods Without Config",
            true,
            "When enabled, mods without a matching .cfg file are hidden from the config list");
        ModListFilter = config.Bind(
            "UI",
            "Mod List Filter",
            "All",
            "Filter chips for the mod list: All, Sandbox, ClientSide");
        GroupModsByAuthor = config.Bind(
            "UI",
            "Group Mods By Author",
            false,
            "When enabled, group the mod list by GUID author prefix (e.g. sparroh)");
        CollapsedMods = config.Bind(
            "UI",
            "Collapsed Mods",
            "",
            "Comma-separated mod keys (GUID or name) whose settings are collapsed; empty means all expanded");
        ToolbarFiltersCollapsed = config.Bind(
            "UI",
            "Toolbar Filters Collapsed",
            false,
            "When enabled, hide toolbar filter/sort/options rows (search bar stays visible)");
        UseFloatSliders = config.Bind(
            "UI",
            "Use Float Sliders",
            false,
            "When enabled, float settings with a defined min/max range are shown as sliders instead of text fields");

        config.Save();
        log?.LogDebug("ConfigManager initialized.");
    }

    public static void Save()
    {
        _config?.Save();
    }
}