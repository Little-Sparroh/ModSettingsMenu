using HarmonyLib;

public static class MenuPatches
{
    public static bool IsMenuOpen;

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