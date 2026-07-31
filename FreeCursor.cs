using UnityEngine;

public static class FreeCursor
{
    private static int _holders;
    private static bool _usedPlayerInput;
    private static FreeCursorDriver _driver;

    public static bool IsHeld => _holders > 0;


    public static void Acquire()
    {
        EnsureDriver();

        if (_holders == 0)
            UnlockViaGame();

        _holders++;
        Apply();
    }


    public static void Release()
    {
        if (_holders <= 0)
            return;

        _holders--;
        if (_holders == 0)
            LockViaGame();
    }


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
            _usedPlayerInput = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private static void LockViaGame()
    {
        if (_usedPlayerInput)
            try
            {
                PlayerInput.LockCursor();
            }
            catch
            {
            }
            finally
            {
                _usedPlayerInput = false;
            }


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
        }


        try
        {
            if (Menu.Instance != null && Menu.Instance.IsOpen)
            {
                Cursor.lockState = PlayerInput.CursorMenuMode;
                Cursor.visible = true;
            }
        }
        catch
        {
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
        private void Update()
        {
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
                Apply();
        }
    }
}