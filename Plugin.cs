using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using System;
using HarmonyLib;


[MycoMod(null, ModFlags.IsClientSide)]
[BepInPlugin(PLUGINGUID, PLUGINNAME, PLUGINVERSION)]
[BepInDependency("sparroh.uilibrary")]
public class SparrohPlugin : BaseUnityPlugin

{
    public const string PLUGINGUID = "sparroh.modsettingsmenu";
    public const string PLUGINNAME = "ModSettingsMenu";
    public const string PLUGINVERSION = "1.2.0";
    
    public new static ManualLogSource Logger;

    public static ConfigEntry<string> ToggleKey { get; private set; }
    public static ConfigEntry<string> RepositionKey { get; private set; }

    /// <summary>Alphabetical | AlphabeticalDesc | LoadOrder | SandboxFirst | HasConfigFirst</summary>
    public static ConfigEntry<string> ModSortMode { get; private set; }

    /// <summary>When true, mods without a matching .cfg are hidden from the list.</summary>
    public static ConfigEntry<bool> HideModsWithoutConfig { get; private set; }

    /// <summary>All | Sandbox | ClientSide</summary>
    public static ConfigEntry<string> ModListFilter { get; private set; }

    /// <summary>When true, mods are grouped by GUID author prefix (e.g. sparroh.*).</summary>
    public static ConfigEntry<bool> GroupModsByAuthor { get; private set; }

    /// <summary>
    /// Comma-separated mod keys (GUID or name) that are collapsed.
    /// Empty = all expanded (default).
    /// </summary>
    public static ConfigEntry<string> CollapsedMods { get; private set; }

    public static bool IsRebinding { get; set; } = false;
    public static bool IsRebindingReposition { get; set; } = false;

    private void Awake()
    {
        Logger = base.Logger;

        ToggleKey = Config.Bind("Keybinds", "ToggleModConfigGUI", "F10", "Key to toggle mod config GUI");
        RepositionKey = Config.Bind("Keybinds", "ToggleHudReposition", "F9",
            "Key to toggle HUD reposition mode (click-and-drag HUD elements)");

        ModSortMode = Config.Bind(
            "UI",
            "ModSortMode",
            "Alphabetical",
            "How to order mods in the config list: Alphabetical, AlphabeticalDesc, LoadOrder, SandboxFirst, HasConfigFirst");
        HideModsWithoutConfig = Config.Bind(
            "UI",
            "HideModsWithoutConfig",
            true,
            "When enabled, mods without a matching .cfg file are hidden from the config list");
        ModListFilter = Config.Bind(
            "UI",
            "ModListFilter",
            "All",
            "Filter chips for the mod list: All, Sandbox, ClientSide");
        GroupModsByAuthor = Config.Bind(
            "UI",
            "GroupModsByAuthor",
            false,
            "When enabled, group the mod list by GUID author prefix (e.g. sparroh)");
        CollapsedMods = Config.Bind(
            "UI",
            "CollapsedMods",
            "",
            "Comma-separated mod keys (GUID or name) whose settings are collapsed; empty means all expanded");


        var harmony = new Harmony(PLUGINGUID);
        harmony.PatchAll(typeof(SparrohPlugin));
        harmony.PatchAll(typeof(MenuPatches));
        InputBlocker.EnsurePatched(harmony);

        HudRepositionMode.EnsureExists();

        Config.Save();
        Logger.LogInfo($"{PLUGINGUID} v{PLUGINVERSION} loaded!");
    }



    private void Update()
    {
        if (!IsRebinding && !IsRebindingReposition)
        {
            try
            {
                var key = (Key)Enum.Parse(typeof(Key), ToggleKey.Value, true);
                if (Keyboard.current != null && key != Key.None && Keyboard.current[key].wasPressedThisFrame)
                {
                    ModConfigGUI.Toggle();
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Error parsing toggle key '{ToggleKey.Value}': {e.Message}");
            }

            try
            {
                var repoKey = (Key)Enum.Parse(typeof(Key), RepositionKey.Value, true);
                if (Keyboard.current != null && repoKey != Key.None && Keyboard.current[repoKey].wasPressedThisFrame)
                {
                    HudRepositionMode.Toggle();
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Error parsing reposition key '{RepositionKey.Value}': {e.Message}");
            }
        }

        if ((IsRebinding || IsRebindingReposition) && Keyboard.current != null)
        {
            foreach (var k in Keyboard.current.allKeys)
            {
                if (k.wasPressedThisFrame)
                {
                    if (IsRebinding)
                    {
                        ToggleKey.Value = k.name;
                        Config.Save();
                        IsRebinding = false;
                        if (ModConfigGUI.KeyBindInput != null)
                        {
                            ModConfigGUI.KeyBindInput.text = ToggleKey.Value;
                            ModConfigGUI.KeyBindInput.interactable = true;
                        }
                    }
                    else if (IsRebindingReposition)
                    {
                        RepositionKey.Value = k.name;
                        Config.Save();
                        IsRebindingReposition = false;
                        if (ModConfigGUI.RepositionKeyBindInput != null)
                        {
                            ModConfigGUI.RepositionKeyBindInput.text = RepositionKey.Value;
                            ModConfigGUI.RepositionKeyBindInput.interactable = true;
                        }
                    }

                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
                    break;
                }
            }
        }
    }


}


public static class MenuPatches
{
    public static bool IsMenuOpen = false;

    [HarmonyPatch(typeof(Menu), "Open")]
    public static void Prefix(Menu __instance)
    {
        IsMenuOpen = true;
    }

    [HarmonyPatch(typeof(Menu), "Close")]
    public static void Prefix()
    {
        IsMenuOpen = false;
    }
}
