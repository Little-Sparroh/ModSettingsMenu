using UnityEngine;

/// <summary>
/// Forces an unlocked, visible mouse cursor while any ModSettingsMenu UI is open.
/// Stacks with the game's PlayerInput cursor ref-count so closing MSM while the
/// in-game Menu is still open does not steal the cursor.
/// Re-applies every frame (including LateUpdate) so other systems cannot stomp it.
/// Reference-counted so config GUI + reposition mode can stack safely.
/// </summary>
public static class FreeCursor
{
    private static int _holders;
    private static bool _usedPlayerInput;
    private static FreeCursorDriver _driver;

    public static bool IsHeld => _holders > 0;

    /// <summary>Begin forcing free cursor. Call once when opening UI.</summary>
    public static void Acquire()
    {
        EnsureDriver();

        if (_holders == 0)
            UnlockViaGame();

        _holders++;
        Apply();
    }

    /// <summary>Stop forcing free cursor for one holder. Restores when count hits zero.</summary>
    public static void Release()
    {
        if (_holders <= 0)
            return;

        _holders--;
        if (_holders == 0)
            LockViaGame();
    }

    /// <summary>Force free cursor now (safe to call every frame while held).</summary>
    public static void Apply()
    {
        if (_holders <= 0)
            return;

        Cursor.lockState = PlayerInput.CursorMenuMode;
        Cursor.visible = true;
    }

    private static void UnlockViaGame()
    {
        try
        {
            PlayerInput.UnlockCursor();
            _usedPlayerInput = true;
        }
        catch
        {
            // PlayerInput may not be ready very early; fall back to direct cursor control.
            _usedPlayerInput = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private static void LockViaGame()
    {
        if (_usedPlayerInput)
        {
            try
            {
                // Stacks with Menu: if enableCursor stays > 0, cursor remains free.
                PlayerInput.LockCursor();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _usedPlayerInput = false;
            }
        }

        // Safety: if the game menu (or anything else) still wants a free cursor, keep it free.
        // PlayerInput.IsMenuEnabled covers the in-game Menu path.
        try
        {
            if (PlayerInput.IsMenuEnabled)
            {
                Cursor.lockState = PlayerInput.CursorMenuMode;
                Cursor.visible = true;
                return;
            }
        }
        catch
        {
            // ignore
        }

        // Also respect Menu.Instance if PlayerInput flag is somehow out of sync.
        try
        {
            if (Menu.Instance != null && Menu.Instance.IsOpen)
            {
                Cursor.lockState = PlayerInput.CursorMenuMode;
                Cursor.visible = true;
                return;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void EnsureDriver()
    {
        if (_driver != null)
            return;

        var go = new GameObject("ModSettingsMenu_FreeCursor");
        Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<FreeCursorDriver>();
    }

    private sealed class FreeCursorDriver : MonoBehaviour
    {
        private void Update() => Apply();

        private void LateUpdate() => Apply();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
                Apply();
        }
    }
}
