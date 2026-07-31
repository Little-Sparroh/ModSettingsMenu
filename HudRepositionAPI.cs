using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

public static class HudRepositionAPI
{
    private static readonly Dictionary<string, HudElement> Elements = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsRepositionModeActive => HudRepositionMode.IsActive;


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


    public static void Unregister(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        if (Elements.Remove(id))
            SparrohPlugin.Logger?.LogInfo($"[HudReposition] Unregistered '{id}'");
    }


    public static IReadOnlyList<HudElement> GetRegistered()
    {
        CleanupDestroyed();
        return Elements.Values.ToList();
    }

    public static void ToggleRepositionMode()
    {
        HudRepositionMode.Toggle();
    }

    public static void EnterRepositionMode()
    {
        HudRepositionMode.Enter();
    }

    public static void ExitRepositionMode()
    {
        HudRepositionMode.Exit();
    }

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

    public sealed class HudElement
    {
        public string Id { get; internal set; }
        public string DisplayName { get; internal set; }
        public RectTransform Rect { get; internal set; }
        public ConfigEntry<float> AnchorX { get; internal set; }
        public ConfigEntry<float> AnchorY { get; internal set; }
        public bool IsAutoDetected { get; internal set; }
    }
}