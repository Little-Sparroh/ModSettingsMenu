using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using System;
using HarmonyLib;
using Pigeon;


[MycoMod(null, ModFlags.IsClientSide)]
[BepInPlugin(PLUGINGUID, PLUGINNAME, PLUGINVERSION)]
public class Plugin : BaseUnityPlugin
{
    public const string PLUGINGUID = "sparroh.modsettingsmenu";
    public const string PLUGINNAME = "ModSettingsMenu";
    public const string PLUGINVERSION = "1.0.0";
    
    public new static ManualLogSource Logger;

    private ConfigEntry<int> _intEntry;
    private ConfigEntry<bool> _boolEntry;
    private ConfigEntry<string> _stringEntry;
    public static ConfigEntry<string> ToggleKey { get; private set; }
    public static bool IsRebinding { get; set; } = false;

    private void Awake()
    {
        Logger = base.Logger;

        ToggleKey = Config.Bind("Keybinds", "ToggleModConfigGUI", "F10", "Key to toggle mod config GUI");

        var harmony = new Harmony(PLUGINGUID);
        harmony.PatchAll(typeof(Plugin));
        harmony.PatchAll(typeof(MenuPatches));

        Config.Save();
        Logger.LogInfo($"{PLUGINGUID} loaded!");
    }

    private void Update()
    {
        if (!IsRebinding)
        {
            try
            {
                var key = (Key)Enum.Parse(typeof(Key), ToggleKey.Value, true);
                if (Keyboard.current != null && key != Key.None && Keyboard.current[key].wasPressedThisFrame)
                {
                    ModConfigGUI.Toggle();
                }
            }
            catch
            {
            }
        }

        if (IsRebinding && Keyboard.current != null)
        {
            foreach (var k in Keyboard.current.allKeys)
            {
                if (k.wasPressedThisFrame)
                {
                    ToggleKey.Value = k.name;
                    Config.Save();
                    IsRebinding = false;
                    if (ModConfigGUI.KeyBindInput != null)
                    {
                        ModConfigGUI.KeyBindInput.text = ToggleKey.Value;
                        ModConfigGUI.KeyBindInput.interactable = true;
                    }
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
                    break;
                }
            }
        }
    }

    void OnGUI()
    {
        if (MenuPatches.IsMenuOpen && GUI.Button(new Rect(Screen.width - 280, 10, 160, 30), "Mod Config"))
        {
            ModConfigGUI.Toggle();
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
