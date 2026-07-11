using UnityEngine;

/// <summary>
/// Forces an unlocked, visible mouse cursor while any ModSettingsMenu UI is open.
/// Re-applies every frame (including LateUpdate) so game FPS lock cannot steal it back.
/// Reference-counted so config GUI + reposition mode can stack safely.
/// </summary>
public static class FreeCursor
{
    private static int _holders;
    private static bool _stored;
    private static CursorLockMode _previousLockState;
    private static bool _previousCursorVisible;
    private static FreeCursorDriver _driver;

    public static bool IsHeld => _holders > 0;

    /// <summary>Begin forcing free cursor. Call once when opening UI.</summary>
    public static void Acquire()
    {
        EnsureDriver();

        if (_holders == 0)
            Store();

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
            Restore();
    }

    /// <summary>Force free cursor now (safe to call every frame while held).</summary>
    public static void Apply()
    {
        if (_holders <= 0)
            return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static void Store()
    {
        if (_stored)
            return;

        _previousLockState = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _stored = true;
    }

    private static void Restore()
    {
        if (!_stored)
            return;

        Cursor.lockState = _previousLockState;
        Cursor.visible = _previousCursorVisible;
        _stored = false;
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
