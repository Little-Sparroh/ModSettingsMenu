using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

/// <summary>
/// Public API for mods to register HUD elements for click-and-drag repositioning.
/// Consumer mods should call Register after creating their HUD and Unregister on destroy.
/// Soft-dependency friendly: call via reflection if you do not want a hard assembly reference.
/// </summary>
public static class HudRepositionAPI
{
    public sealed class HudElement
    {
        public string Id { get; internal set; }
        public string DisplayName { get; internal set; }
        public RectTransform Rect { get; internal set; }
        public ConfigEntry<float> AnchorX { get; internal set; }
        public ConfigEntry<float> AnchorY { get; internal set; }
        public bool IsAutoDetected { get; internal set; }
    }

    private static readonly Dictionary<string, HudElement> Elements =
        new Dictionary<string, HudElement>(StringComparer.OrdinalIgnoreCase);

    public static bool IsRepositionModeActive => HudRepositionMode.IsActive;

    /// <summary>
    /// Register a HUD element so it can be moved in reposition mode.
    /// Anchors are expected to be normalized 0-1 values (anchorMin = anchorMax).
    /// </summary>
    public static void Register(
        string id,
        string displayName,
        RectTransform rect,
        ConfigEntry<float> anchorX,
        ConfigEntry<float> anchorY)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("id is required", nameof(id));
        if (rect == null)
            throw new ArgumentNullException(nameof(rect));
        if (anchorX == null)
            throw new ArgumentNullException(nameof(anchorX));
        if (anchorY == null)
            throw new ArgumentNullException(nameof(anchorY));

        Elements[id] = new HudElement
        {
            Id = id,
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName,
            Rect = rect,
            AnchorX = anchorX,
            AnchorY = anchorY,
            IsAutoDetected = false
        };

        SparrohPlugin.Logger?.LogInfo($"[HudReposition] Registered '{displayName}' ({id})");
    }

    /// <summary>
    /// Remove a previously registered HUD element.
    /// </summary>
    public static void Unregister(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        if (Elements.Remove(id))
            SparrohPlugin.Logger?.LogInfo($"[HudReposition] Unregistered '{id}'");
    }

    /// <summary>
    /// Returns a snapshot of currently registered elements (including auto-detected ones while mode is active).
    /// </summary>
    public static IReadOnlyList<HudElement> GetRegistered()
    {
        CleanupDestroyed();
        return Elements.Values.ToList();
    }

    public static void ToggleRepositionMode() => HudRepositionMode.Toggle();

    public static void EnterRepositionMode() => HudRepositionMode.Enter();

    public static void ExitRepositionMode() => HudRepositionMode.Exit();

    internal static void RegisterAutoDetected(
        string id,
        string displayName,
        RectTransform rect,
        ConfigEntry<float> anchorX,
        ConfigEntry<float> anchorY)
    {
        if (Elements.ContainsKey(id))
            return;

        Elements[id] = new HudElement
        {
            Id = id,
            DisplayName = displayName,
            Rect = rect,
            AnchorX = anchorX,
            AnchorY = anchorY,
            IsAutoDetected = true
        };
    }

    internal static void ClearAutoDetected()
    {
        var toRemove = Elements.Where(kv => kv.Value.IsAutoDetected).Select(kv => kv.Key).ToList();
        foreach (var key in toRemove)
            Elements.Remove(key);
    }

    internal static void CleanupDestroyed()
    {
        var toRemove = Elements
            .Where(kv => kv.Value.Rect == null)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in toRemove)
            Elements.Remove(key);
    }
}
