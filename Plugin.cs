using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[MycoMod(null, ModFlags.IsClientSide)]
[BepInPlugin(PLUGINGUID, PLUGINNAME, PLUGINVERSION)]
[BepInDependency("sparroh.uilibrary")]
public class SparrohPlugin : BaseUnityPlugin
{
    public const string PLUGINGUID = "sparroh.modsettingsmenu";
    public const string PLUGINNAME = "ModSettingsMenu";
    public const string PLUGINVERSION = "2.1.0";

    public new static ManualLogSource Logger;


    public static ConfigEntry<string> ToggleKey => ConfigManager.ToggleKey;
    public static ConfigEntry<string> RepositionKey => ConfigManager.RepositionKey;
    public static ConfigEntry<string> ModSortMode => ConfigManager.ModSortMode;
    public static ConfigEntry<bool> HideModsWithoutConfig => ConfigManager.HideModsWithoutConfig;
    public static ConfigEntry<string> ModListFilter => ConfigManager.ModListFilter;
    public static ConfigEntry<bool> GroupModsByAuthor => ConfigManager.GroupModsByAuthor;
    public static ConfigEntry<string> CollapsedMods => ConfigManager.CollapsedMods;
    public static ConfigEntry<bool> ToolbarFiltersCollapsed => ConfigManager.ToolbarFiltersCollapsed;
    public static ConfigEntry<bool> UseFloatSliders => ConfigManager.UseFloatSliders;

    public static bool IsRebinding { get; set; }
    public static bool IsRebindingReposition { get; set; }

    private void Awake()
    {
        Logger = base.Logger;

        ConfigManager.Initialize(Config, Logger);

        var harmony = new Harmony(PLUGINGUID);
        harmony.PatchAll(typeof(SparrohPlugin));
        harmony.PatchAll(typeof(MenuPatches));
        InputBlocker.EnsurePatched(harmony);

        HudRepositionMode.EnsureExists();

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
                    ModConfigGUI.Toggle();
            }
            catch (Exception e)
            {
                Logger.LogError($"Error parsing toggle key '{ToggleKey.Value}': {e.Message}");
            }

            try
            {
                var repoKey = (Key)Enum.Parse(typeof(Key), RepositionKey.Value, true);
                if (Keyboard.current != null && repoKey != Key.None && Keyboard.current[repoKey].wasPressedThisFrame)
                    HudRepositionMode.Toggle();
            }
            catch (Exception e)
            {
                Logger.LogError($"Error parsing reposition key '{RepositionKey.Value}': {e.Message}");
            }
        }

        if ((IsRebinding || IsRebindingReposition) && Keyboard.current != null)
            foreach (var k in Keyboard.current.allKeys)
                if (k.wasPressedThisFrame)
                {
                    if (IsRebinding)
                    {
                        ToggleKey.Value = k.name;
                        ConfigManager.Save();
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
                        ConfigManager.Save();
                        IsRebindingReposition = false;
                        if (ModConfigGUI.RepositionKeyBindInput != null)
                        {
                            ModConfigGUI.RepositionKeyBindInput.text = RepositionKey.Value;
                            ModConfigGUI.RepositionKeyBindInput.interactable = true;
                        }
                    }

                    EventSystem.current.SetSelectedGameObject(null);
                    break;
                }
    }
}