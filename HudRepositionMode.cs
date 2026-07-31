using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

public class HudRepositionMode : MonoBehaviour
{
    private const float MinHitWidth = 180f;
    private const float MinHitHeight = 36f;

    private readonly Dictionary<string, ConfigFile> _autoConfigFiles = new(StringComparer.OrdinalIgnoreCase);


    private bool _cursorHeld;

    private HudRepositionAPI.HudElement _dragging;
    private Vector2 _dragGrabOffset;
    public static HudRepositionMode Instance { get; private set; }
    public static bool IsActive { get; private set; }

    private void Update()
    {
        if (!IsActive)
            return;

        FreeCursor.Apply();
        InputBlocker.SoftSuppressMouseFire();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Exit();
            return;
        }


        var mouse = Mouse.current;
        if (mouse == null)
            return;

        var mousePos = mouse.position.ReadValue();
        ProcessDragInput(
            mousePos,
            mouse.leftButton.wasPressedThisFrame,
            mouse.leftButton.isPressed,
            mouse.leftButton.wasReleasedThisFrame);
    }

    private void OnGUI()
    {
        if (!IsActive)
            return;


        var e = Event.current;
        if (e != null)
        {
            var guiMouse = e.mousePosition;
            var screenMouse = new Vector2(guiMouse.x, Screen.height - guiMouse.y);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                ProcessDragInput(screenMouse, true, true, false);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                ProcessDragInput(screenMouse, false, true, false);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                ProcessDragInput(screenMouse, false, false, true);
                e.Use();
            }
        }

        DrawOverlay();
    }

    public static void EnsureExists()
    {
        if (Instance != null)
            return;

        var go = new GameObject("HudRepositionMode");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<HudRepositionMode>();
    }

    public static void Toggle()
    {
        EnsureExists();
        if (IsActive)
            Exit();
        else
            Enter();
    }

    public static void Enter()
    {
        EnsureExists();
        if (IsActive)
            return;

        IsActive = true;
        Instance.HoldCursor();
        HudRepositionAutoDetect.Run(Instance._autoConfigFiles);
        HudRepositionAPI.CleanupDestroyed();


        var count = HudRepositionAPI.GetRegistered().Count;
        SparrohPlugin.Logger.LogInfo(
            $"[HudReposition] Reposition mode ON — {count} element(s). Drag to move, Esc/F9 to exit.");
    }

    public static void Exit()
    {
        if (!IsActive)
            return;

        if (Instance != null)
        {
            if (Instance._dragging != null)
            {
                Instance.CommitDrag(Instance._dragging);
                Instance._dragging = null;
            }

            Instance.ReleaseCursor();
            HudRepositionAPI.ClearAutoDetected();
            Instance._autoConfigFiles.Clear();
        }

        IsActive = false;
        SparrohPlugin.Logger.LogInfo("[HudReposition] Reposition mode OFF");
    }

    private void ProcessDragInput(Vector2 screenPos, bool pressed, bool held, bool released)
    {
        HudRepositionAPI.CleanupDestroyed();
        var elements = HudRepositionAPI.GetRegistered();

        if (pressed)
        {
            _dragging = FindElementAtScreenPos(elements, screenPos);
            if (_dragging != null && _dragging.Rect != null)
            {
                if (TryScreenToNormalizedParent(_dragging.Rect, screenPos, out var mouseNorm))
                    _dragGrabOffset = _dragging.Rect.anchorMin - mouseNorm;
                else
                    _dragGrabOffset = Vector2.zero;

                SparrohPlugin.Logger.LogInfo($"[HudReposition] Drag start: {_dragging.DisplayName}");
            }
        }

        if (_dragging != null && _dragging.Rect != null && held)
            if (TryScreenToNormalizedParent(_dragging.Rect, screenPos, out var mouseNorm))
            {
                var target = mouseNorm + _dragGrabOffset;
                target.x = Mathf.Clamp01(target.x);
                target.y = Mathf.Clamp01(target.y);
                ApplyAnchorsLive(_dragging.Rect, target);
            }

        if (released && _dragging != null)
        {
            CommitDrag(_dragging);
            _dragging = null;
        }
    }

    private void DrawOverlay()
    {
        var barRect = new Rect(0, 0, Screen.width, 36);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(barRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var elements = HudRepositionAPI.GetRegistered();
        var header = elements.Count == 0
            ? "HUD Reposition Mode — no HUD elements found (is the HUD visible in-mission?) · Esc/F9 to exit"
            : $"HUD Reposition Mode — {elements.Count} element(s) · drag boxes to move · Esc/F9 to exit";

        var headerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        GUI.Label(barRect, header, headerStyle);

        foreach (var element in elements)
        {
            if (element.Rect == null)
                continue;


            if (!TryGetScreenRect(element.Rect, out var screenRect))
                continue;

            screenRect = EnsureMinHitSize(ExpandRect(screenRect, 10f, 8f));

            var isDrag = _dragging != null && _dragging.Id == element.Id;
            var boxColor = isDrag
                ? new Color(0.2f, 0.85f, 0.4f, 0.4f)
                : new Color(0.2f, 0.55f, 1f, 0.35f);
            var borderColor = isDrag
                ? new Color(0.2f, 1f, 0.4f, 1f)
                : new Color(0.5f, 0.85f, 1f, 1f);

            GUI.color = boxColor;
            GUI.DrawTexture(screenRect, Texture2D.whiteTexture);
            GUI.color = borderColor;
            DrawBorder(screenRect, 2f);
            GUI.color = Color.white;

            var anchors = element.Rect.anchorMin;
            var label = $"{element.DisplayName}  ({anchors.x:F3}, {anchors.y:F3})";
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            var labelRect = new Rect(screenRect.x, Mathf.Max(40f, screenRect.y - 18f),
                Mathf.Max(screenRect.width, 240f), 18f);
            var shadow = new GUIStyle(labelStyle) { normal = { textColor = Color.black } };
            GUI.Label(new Rect(labelRect.x + 1, labelRect.y + 1, labelRect.width, labelRect.height), label, shadow);
            GUI.Label(labelRect, label, labelStyle);
        }
    }

    private void CommitDrag(HudRepositionAPI.HudElement element)
    {
        if (element?.Rect == null)
            return;

        var anchors = element.Rect.anchorMin;
        var x = Mathf.Clamp01(anchors.x);
        var y = Mathf.Clamp01(anchors.y);

        try
        {
            var changed = false;
            if (!Mathf.Approximately(element.AnchorX.Value, x))
            {
                element.AnchorX.Value = x;
                changed = true;
            }

            if (!Mathf.Approximately(element.AnchorY.Value, y))
            {
                element.AnchorY.Value = y;
                changed = true;
            }

            if (changed)
            {
                element.AnchorX.ConfigFile.Save();
                SparrohPlugin.Logger.LogInfo(
                    $"[HudReposition] Saved '{element.DisplayName}' → ({x:F4}, {y:F4})");
            }
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogError($"[HudReposition] Failed to save '{element.DisplayName}': {ex.Message}");
        }

        ApplyAnchorsLive(element.Rect, new Vector2(x, y));
    }

    private static void ApplyAnchorsLive(RectTransform rect, Vector2 anchors)
    {
        rect.anchorMin = anchors;
        rect.anchorMax = anchors;

        rect.anchoredPosition = Vector2.zero;
    }

    private static HudRepositionAPI.HudElement FindElementAtScreenPos(
        IReadOnlyList<HudRepositionAPI.HudElement> elements,
        Vector2 screenPos)
    {
        HudRepositionAPI.HudElement best = null;
        var bestArea = float.MaxValue;

        foreach (var element in elements)
        {
            if (element.Rect == null)
                continue;

            if (!TryGetScreenRectRaw(element.Rect, out var raw))
                continue;

            raw = EnsureMinHitSize(ExpandRect(raw, 10f, 8f));
            if (raw.Contains(screenPos))
            {
                var area = raw.width * raw.height;
                if (area < bestArea)
                {
                    bestArea = area;
                    best = element;
                }
            }
        }

        return best;
    }

    private static bool TryGetScreenRectRaw(RectTransform rect, out Rect screenRect)
    {
        screenRect = default;
        if (rect == null)
            return false;

        Camera cam = null;
        var canvas = rect.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera ?? Camera.main;

        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);


        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (var i = 0; i < 4; i++)
        {
            Vector2 sp;
            if (cam != null)
                sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            else
                sp = new Vector2(corners[i].x, corners[i].y);

            minX = Mathf.Min(minX, sp.x);
            maxX = Mathf.Max(maxX, sp.x);
            minY = Mathf.Min(minY, sp.y);
            maxY = Mathf.Max(maxY, sp.y);
        }


        if (maxX - minX < 1f || maxY - minY < 1f)
        {
            var anchorScreen = AnchorToScreenPoint(rect);
            screenRect = new Rect(anchorScreen.x - MinHitWidth * 0.5f, anchorScreen.y - MinHitHeight * 0.5f,
                MinHitWidth, MinHitHeight);
            return true;
        }

        screenRect = new Rect(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    private static Vector2 AnchorToScreenPoint(RectTransform rect)
    {
        var parent = rect.parent as RectTransform;
        if (parent == null)
            return new Vector2(Screen.width * rect.anchorMin.x, Screen.height * rect.anchorMin.y);

        var pr = parent.rect;
        var local = new Vector2(
            pr.xMin + rect.anchorMin.x * pr.width,
            pr.yMin + rect.anchorMin.y * pr.height);
        var world = parent.TransformPoint(local);

        Camera cam = null;
        var canvas = rect.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera ?? Camera.main;

        if (cam != null)
            return RectTransformUtility.WorldToScreenPoint(cam, world);

        return new Vector2(world.x, world.y);
    }

    private static bool TryGetScreenRect(RectTransform rect, out Rect guiRect)
    {
        guiRect = default;
        if (!TryGetScreenRectRaw(rect, out var raw))
            return false;

        guiRect = new Rect(raw.x, Screen.height - raw.y - raw.height, raw.width, raw.height);
        return true;
    }

    private static Rect ExpandRect(Rect r, float padX, float padY)
    {
        return new Rect(r.x - padX, r.y - padY, r.width + padX * 2f, r.height + padY * 2f);
    }

    private static Rect EnsureMinHitSize(Rect r)
    {
        var w = Mathf.Max(r.width, MinHitWidth);
        var h = Mathf.Max(r.height, MinHitHeight);
        var cx = r.x + r.width * 0.5f;
        var cy = r.y + r.height * 0.5f;
        return new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
    }

    private static void DrawBorder(Rect r, float thickness)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), Texture2D.whiteTexture);
    }

    private static bool TryScreenToNormalizedParent(RectTransform rect, Vector2 screenPos, out Vector2 normalized)
    {
        normalized = Vector2.zero;
        if (rect == null)
            return false;

        var parent = rect.parent as RectTransform;
        if (parent == null)
        {
            normalized = new Vector2(
                Mathf.Clamp01(screenPos.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(screenPos.y / Mathf.Max(1f, Screen.height)));
            return true;
        }

        Camera cam = null;
        var canvas = rect.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera ?? Camera.main;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, cam, out var localPoint))
        {
            normalized = new Vector2(
                Mathf.Clamp01(screenPos.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(screenPos.y / Mathf.Max(1f, Screen.height)));
            return true;
        }

        var parentRect = parent.rect;
        if (parentRect.width <= 0f || parentRect.height <= 0f)
            return false;

        normalized = new Vector2(
            (localPoint.x - parentRect.xMin) / parentRect.width,
            (localPoint.y - parentRect.yMin) / parentRect.height);
        return true;
    }

    private void HoldCursor()
    {
        if (_cursorHeld)
            return;
        FreeCursor.Acquire();
        _cursorHeld = true;
    }

    private void ReleaseCursor()
    {
        if (!_cursorHeld)
            return;
        FreeCursor.Release();
        _cursorHeld = false;
    }
}