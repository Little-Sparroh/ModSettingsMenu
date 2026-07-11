using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Click-and-drag HUD reposition mode. Toggle with the configured key (default F9) or via Mod Config GUI.
/// </summary>
public class HudRepositionMode : MonoBehaviour
{
    public static HudRepositionMode Instance { get; private set; }
    public static bool IsActive { get; private set; }

    private HudRepositionAPI.HudElement _dragging;
    private Vector2 _dragGrabOffset;
    private bool _cursorHeld;

    private readonly Dictionary<string, ConfigFile> _autoConfigFiles =
        new Dictionary<string, ConfigFile>(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex AnchorPairRegex =
        new Regex(@"^(?<prefix>.+)AnchorX$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const float MinHitWidth = 180f;
    private const float MinHitHeight = 36f;

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
        Instance.RunAutoDetect();
        HudRepositionAPI.CleanupDestroyed();

        int count = HudRepositionAPI.GetRegistered().Count;
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

        // Input System path (backup to IMGUI)
        var mouse = Mouse.current;
        if (mouse == null)
            return;

        Vector2 mousePos = mouse.position.ReadValue();
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

        // IMGUI mouse path — often more reliable when cursor is unlocked
        Event e = Event.current;
        if (e != null)
        {
            // Event mouse is top-left origin; convert to bottom-left (screen)
            Vector2 guiMouse = e.mousePosition;
            Vector2 screenMouse = new Vector2(guiMouse.x, Screen.height - guiMouse.y);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                ProcessDragInput(screenMouse, pressed: true, held: true, released: false);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                ProcessDragInput(screenMouse, pressed: false, held: true, released: false);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                ProcessDragInput(screenMouse, pressed: false, held: false, released: true);
                e.Use();
            }
        }

        DrawOverlay();
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
                    _dragGrabOffset = (Vector2)_dragging.Rect.anchorMin - mouseNorm;
                else
                    _dragGrabOffset = Vector2.zero;

                SparrohPlugin.Logger.LogInfo($"[HudReposition] Drag start: {_dragging.DisplayName}");
            }
        }

        if (_dragging != null && _dragging.Rect != null && held)
        {
            if (TryScreenToNormalizedParent(_dragging.Rect, screenPos, out var mouseNorm))
            {
                Vector2 target = mouseNorm + _dragGrabOffset;
                target.x = Mathf.Clamp01(target.x);
                target.y = Mathf.Clamp01(target.y);
                ApplyAnchorsLive(_dragging.Rect, target);
            }
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
        string header = elements.Count == 0
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

            // Draw even if inactive so user can still see/find them when possible
            if (!TryGetScreenRect(element.Rect, out Rect screenRect))
                continue;

            screenRect = EnsureMinHitSize(ExpandRect(screenRect, 10f, 8f));

            bool isDrag = _dragging != null && _dragging.Id == element.Id;
            Color boxColor = isDrag
                ? new Color(0.2f, 0.85f, 0.4f, 0.4f)
                : new Color(0.2f, 0.55f, 1f, 0.35f);
            Color borderColor = isDrag
                ? new Color(0.2f, 1f, 0.4f, 1f)
                : new Color(0.5f, 0.85f, 1f, 1f);

            GUI.color = boxColor;
            GUI.DrawTexture(screenRect, Texture2D.whiteTexture);
            GUI.color = borderColor;
            DrawBorder(screenRect, 2f);
            GUI.color = Color.white;

            Vector2 anchors = element.Rect.anchorMin;
            string label = $"{element.DisplayName}  ({anchors.x:F3}, {anchors.y:F3})";
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

        Vector2 anchors = element.Rect.anchorMin;
        float x = Mathf.Clamp01(anchors.x);
        float y = Mathf.Clamp01(anchors.y);

        try
        {
            bool changed = false;
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
        // Keep pixel offset zero so position is purely anchor-based
        rect.anchoredPosition = Vector2.zero;
    }

    private static HudRepositionAPI.HudElement FindElementAtScreenPos(
        IReadOnlyList<HudRepositionAPI.HudElement> elements,
        Vector2 screenPos)
    {
        HudRepositionAPI.HudElement best = null;
        float bestArea = float.MaxValue;

        foreach (var element in elements)
        {
            if (element.Rect == null)
                continue;

            if (!TryGetScreenRectRaw(element.Rect, out Rect raw))
                continue;

            raw = EnsureMinHitSize(ExpandRect(raw, 10f, 8f));
            if (raw.Contains(screenPos))
            {
                float area = raw.width * raw.height;
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

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        // Convert each corner to screen space
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 sp;
            if (cam != null)
                sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            else
                sp = new Vector2(corners[i].x, corners[i].y); // Overlay: already screen-ish

            minX = Mathf.Min(minX, sp.x);
            maxX = Mathf.Max(maxX, sp.x);
            minY = Mathf.Min(minY, sp.y);
            maxY = Mathf.Max(maxY, sp.y);
        }

        // Fallback: if degenerate, use anchor position as a point hit target
        if (maxX - minX < 1f || maxY - minY < 1f)
        {
            Vector2 anchorScreen = AnchorToScreenPoint(rect);
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

        Rect pr = parent.rect;
        Vector2 local = new Vector2(
            pr.xMin + rect.anchorMin.x * pr.width,
            pr.yMin + rect.anchorMin.y * pr.height);
        Vector3 world = parent.TransformPoint(local);

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
        if (!TryGetScreenRectRaw(rect, out Rect raw))
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
        float w = Mathf.Max(r.width, MinHitWidth);
        float h = Mathf.Max(r.height, MinHitHeight);
        float cx = r.x + r.width * 0.5f;
        float cy = r.y + r.height * 0.5f;
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
            // Fallback: normalize against full screen
            normalized = new Vector2(
                Mathf.Clamp01(screenPos.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(screenPos.y / Mathf.Max(1f, Screen.height)));
            return true;
        }

        Camera cam = null;
        var canvas = rect.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera ?? Camera.main;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, cam, out Vector2 localPoint))
        {
            // Fallback screen normalize
            normalized = new Vector2(
                Mathf.Clamp01(screenPos.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(screenPos.y / Mathf.Max(1f, Screen.height)));
            return true;
        }

        Rect parentRect = parent.rect;
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

    #region Auto-detect

    private void RunAutoDetect()
    {
        try
        {
            var pairs = DiscoverAnchorPairs();
            var hudRoots = FindHudRoots();

            SparrohPlugin.Logger.LogInfo(
                $"[HudReposition] Auto-detect: {pairs.Count} anchor pair(s), {hudRoots.Count} HUD root(s)");

            // Match config pairs to GameObjects
            foreach (var pair in pairs)
            {
                string id = $"auto::{Path.GetFileNameWithoutExtension(pair.ConfigPath)}::{pair.Prefix}";

                bool alreadyRegistered = HudRepositionAPI.GetRegistered()
                    .Any(e =>
                        e.AnchorX != null &&
                        string.Equals(e.AnchorX.Definition.Key, pair.KeyX, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.AnchorY.Definition.Key, pair.KeyY, StringComparison.OrdinalIgnoreCase));
                if (alreadyRegistered)
                    continue;

                GameObject match = hudRoots.FirstOrDefault(r => NameMatchesPrefix(r.name, pair.Prefix));
                // Broader fallback: any HUD root if only one pair left unmatched for this prefix family
                if (match == null && hudRoots.Count > 0)
                {
                    match = hudRoots.FirstOrDefault(r =>
                        r.name.IndexOf(pair.Prefix.Substring(0, Math.Min(4, pair.Prefix.Length)),
                            StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (match == null)
                    continue;

                var rect = match.GetComponent<RectTransform>();
                if (rect == null)
                    continue;

                if (!_autoConfigFiles.TryGetValue(pair.ConfigPath, out var cfg))
                {
                    cfg = new ConfigFile(pair.ConfigPath, true);
                    _autoConfigFiles[pair.ConfigPath] = cfg;
                }

                var anchorX = cfg.Bind(pair.Section, pair.KeyX, pair.DefaultX);
                var anchorY = cfg.Bind(pair.Section, pair.KeyY, pair.DefaultY);

                HudRepositionAPI.RegisterAutoDetected(
                    id,
                    string.IsNullOrEmpty(pair.Prefix) ? match.name : pair.Prefix,
                    rect,
                    anchorX,
                    anchorY);

                SparrohPlugin.Logger.LogInfo(
                    $"[HudReposition] Auto-detected '{match.name}' ← {pair.KeyX}/{pair.KeyY}");
            }

            // Last resort: any *HUD under reticle with no config still gets a temporary drag
            // (won't persist unless config pair exists — skip for now)
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogWarning($"[HudReposition] Auto-detect failed: {ex.Message}");
        }
    }

    private sealed class AnchorPair
    {
        public string ConfigPath;
        public string Section;
        public string Prefix;
        public string KeyX;
        public string KeyY;
        public float DefaultX;
        public float DefaultY;
    }

    private static List<AnchorPair> DiscoverAnchorPairs()
    {
        var result = new List<AnchorPair>();
        if (!Directory.Exists(Paths.ConfigPath))
            return result;

        foreach (string file in Directory.GetFiles(Paths.ConfigPath, "*.cfg"))
        {
            try
            {
                string section = "";
                var floats = new Dictionary<string, (string section, float value)>(StringComparer.OrdinalIgnoreCase);

                foreach (var raw in File.ReadLines(file))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2);
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ||
                        float.TryParse(val, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                    {
                        floats[key] = (section, f);
                    }
                }

                foreach (var kv in floats)
                {
                    var m = AnchorPairRegex.Match(kv.Key);
                    if (!m.Success)
                        continue;

                    string prefix = m.Groups["prefix"].Value;
                    string keyY = prefix + "AnchorY";
                    if (!floats.TryGetValue(keyY, out var yEntry))
                        continue;

                    result.Add(new AnchorPair
                    {
                        ConfigPath = file,
                        Section = string.IsNullOrEmpty(kv.Value.section) ? "HUD Positioning" : kv.Value.section,
                        Prefix = prefix,
                        KeyX = kv.Key,
                        KeyY = keyY,
                        DefaultX = kv.Value.value,
                        DefaultY = yEntry.value
                    });
                }
            }
            catch (Exception ex)
            {
                SparrohPlugin.Logger.LogDebug($"[HudReposition] Could not parse {file}: {ex.Message}");
            }
        }

        return result;
    }

    private static List<GameObject> FindHudRoots()
    {
        var roots = new List<GameObject>();
        var seen = new HashSet<int>();

        void Add(GameObject go)
        {
            if (go == null)
                return;
            int id = go.GetInstanceID();
            if (seen.Add(id))
                roots.Add(go);
        }

        // Prefer reticle children
        try
        {
            var playerType = Type.GetType("Pigeon.Movement.Player, Assembly-CSharp") ??
                             AppDomain.CurrentDomain.GetAssemblies()
                                 .SelectMany(a =>
                                 {
                                     try { return a.GetTypes(); }
                                     catch { return Type.EmptyTypes; }
                                 })
                                 .FirstOrDefault(t => t.Name == "Player" && t.Namespace != null &&
                                                      t.Namespace.Contains("Pigeon"));

            if (playerType != null)
            {
                var localProp = playerType.GetProperty("LocalPlayer",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var local = localProp?.GetValue(null);
                if (local != null)
                {
                    var lookProp = local.GetType().GetProperty("PlayerLook") ??
                                   local.GetType().GetProperty("playerLook");
                    var look = lookProp?.GetValue(local);
                    if (look != null)
                    {
                        var reticleProp = look.GetType().GetProperty("Reticle") ??
                                          look.GetType().GetProperty("reticle");
                        var reticle = reticleProp?.GetValue(look) as Transform;
                        if (reticle != null)
                        {
                            foreach (Transform child in reticle)
                            {
                                if (child.GetComponent<RectTransform>() != null)
                                    Add(child.gameObject);
                            }

                            SparrohPlugin.Logger.LogInfo(
                                $"[HudReposition] Found reticle with {reticle.childCount} child(ren)");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogDebug($"[HudReposition] Reticle scan failed: {ex.Message}");
        }

        // Always also scan for *HUD names
        try
        {
            foreach (var rt in UnityEngine.Object.FindObjectsOfType<RectTransform>())
            {
                string n = rt.gameObject.name;
                if (n.IndexOf("HUD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Meter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Speedometer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Altimeter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Carnometer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Tracker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("GunStats", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("BossTimer", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Prefer root-ish containers (has RectTransform, not deep text leaf only)
                    Add(rt.gameObject);
                }
            }
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogDebug($"[HudReposition] Global HUD scan failed: {ex.Message}");
        }

        return roots;
    }

    private static bool NameMatchesPrefix(string goName, string prefix)
    {
        if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(prefix))
            return false;

        string compactGo = goName.Replace(" ", "").Replace("_", "");
        string compactPrefix = prefix.Replace(" ", "").Replace("_", "");

        if (compactGo.IndexOf(compactPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // SpeedometerAnchor prefix "Speedometer" vs GO "SpeedometerHUD"
        // GunDisplay vs GunStatsHUD
        if (compactPrefix.StartsWith("Gun", StringComparison.OrdinalIgnoreCase) &&
            compactGo.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (compactPrefix.Length >= 4)
        {
            string head = compactPrefix.Substring(0, Math.Min(8, compactPrefix.Length));
            if (compactGo.IndexOf(head, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    #endregion
}
