using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// While ModSettingsMenu UI or HUD reposition mode is open, suppress combat left-click
/// so the player does not fire weapons while interacting with the UI.
/// </summary>
public static class InputBlocker
{
    private static bool _patched;
    private static Harmony _harmony;

    public static bool ShouldBlockGameplayInput =>
        FreeCursor.IsHeld || HudRepositionMode.IsActive || ModConfigGUI.IsVisible;

    public static void EnsurePatched(Harmony harmony)
    {
        if (_patched)
            return;

        _harmony = harmony;
        try
        {
            // Soft-block: intercept common Gun fire entry points if present
            var gunType = AccessTools.TypeByName("Gun") ??
                          AccessTools.TypeByName("Pigeon.Gun") ??
                          FindTypeBySimpleName("Gun");

            if (gunType != null)
            {
                foreach (var method in gunType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.IsSpecialName || method.IsGenericMethod)
                        continue;

                    string n = method.Name;
                    if (n.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    // Skip obvious non-fire helpers
                    if (n.IndexOf("OnFired", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    try
                    {
                        var prefix = new HarmonyMethod(typeof(InputBlocker), nameof(BlockIfUiOpen));
                        harmony.Patch(method, prefix: prefix);
                        SparrohPlugin.Logger.LogInfo($"[InputBlocker] Patched {gunType.Name}.{method.Name}");
                    }
                    catch (Exception ex)
                    {
                        SparrohPlugin.Logger.LogDebug($"[InputBlocker] Skip {method.Name}: {ex.Message}");
                    }
                }
            }

            // Also try Player weapon input methods
            var playerType = AccessTools.TypeByName("Pigeon.Movement.Player") ??
                             AccessTools.TypeByName("Player");
            if (playerType != null)
            {
                foreach (var method in playerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    string n = method.Name;
                    if (n.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    try
                    {
                        var prefix = new HarmonyMethod(typeof(InputBlocker), nameof(BlockIfUiOpen));
                        harmony.Patch(method, prefix: prefix);
                        SparrohPlugin.Logger.LogInfo($"[InputBlocker] Patched {playerType.Name}.{method.Name}");
                    }
                    catch
                    {
                    }
                }
            }

            _patched = true;
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogWarning($"[InputBlocker] Patch setup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Harmony prefix: return false to skip original when UI owns input.
    /// </summary>
    public static bool BlockIfUiOpen()
    {
        return !ShouldBlockGameplayInput;
    }

    /// <summary>
    /// Soften mouse left button state for any code reading Input System this frame.
    /// Call from LateUpdate while UI is open.
    /// </summary>
    public static void SoftSuppressMouseFire()
    {
        if (!ShouldBlockGameplayInput)
            return;

        // Cannot fully rewrite device state safely every frame without side effects;
        // Harmony patches are the primary block. This is a no-op placeholder for future
        // InputAction map disabling if we locate the player's action asset.
    }

    private static Type FindTypeBySimpleName(string simpleName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetTypes().FirstOrDefault(x => x.Name == simpleName);
                if (t != null)
                    return t;
            }
            catch
            {
            }
        }

        return null;
    }
}
