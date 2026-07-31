using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

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
            var gunType = AccessTools.TypeByName("Gun") ??
                          AccessTools.TypeByName("Pigeon.Gun") ??
                          FindTypeBySimpleName("Gun");

            if (gunType != null)
                foreach (var method in gunType.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                          BindingFlags.NonPublic))
                {
                    if (method.IsSpecialName || method.IsGenericMethod)
                        continue;

                    var n = method.Name;
                    if (n.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;


                    if (n.IndexOf("OnFired", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    try
                    {
                        var prefix = new HarmonyMethod(typeof(InputBlocker), nameof(BlockIfUiOpen));
                        harmony.Patch(method, prefix);
                        SparrohPlugin.Logger.LogInfo($"[InputBlocker] Patched {gunType.Name}.{method.Name}");
                    }
                    catch (Exception ex)
                    {
                        SparrohPlugin.Logger.LogDebug($"[InputBlocker] Skip {method.Name}: {ex.Message}");
                    }
                }


            var playerType = AccessTools.TypeByName("Pigeon.Movement.Player") ??
                             AccessTools.TypeByName("Player");
            if (playerType != null)
                foreach (var method in playerType.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                             BindingFlags.NonPublic))
                {
                    var n = method.Name;
                    if (n.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    try
                    {
                        var prefix = new HarmonyMethod(typeof(InputBlocker), nameof(BlockIfUiOpen));
                        harmony.Patch(method, prefix);
                        SparrohPlugin.Logger.LogInfo($"[InputBlocker] Patched {playerType.Name}.{method.Name}");
                    }
                    catch
                    {
                    }
                }

            _patched = true;
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogWarning($"[InputBlocker] Patch setup failed: {ex.Message}");
        }
    }


    public static bool BlockIfUiOpen()
    {
        return !ShouldBlockGameplayInput;
    }


    public static void SoftSuppressMouseFire()
    {
        if (!ShouldBlockGameplayInput)
            return;
    }

    private static Type FindTypeBySimpleName(string simpleName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            try
            {
                var t = asm.GetTypes().FirstOrDefault(x => x.Name == simpleName);
                if (t != null)
                    return t;
            }
            catch
            {
            }

        return null;
    }
}