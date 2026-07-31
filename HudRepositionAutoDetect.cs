using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

public static class HudRepositionAutoDetect
{
    private static readonly Regex AnchorPairRegex =
        new(@"^(?<prefix>.+)AnchorX$", RegexOptions.IgnoreCase | RegexOptions.Compiled);


    public static void Run(Dictionary<string, ConfigFile> configFiles)
    {
        if (configFiles == null)
            throw new ArgumentNullException(nameof(configFiles));

        try
        {
            var pairs = DiscoverAnchorPairs();
            var hudRoots = FindHudRoots();

            SparrohPlugin.Logger.LogInfo(
                $"[HudReposition] Auto-detect: {pairs.Count} anchor pair(s), {hudRoots.Count} HUD root(s)");

            foreach (var pair in pairs)
            {
                var id = $"auto::{Path.GetFileNameWithoutExtension(pair.ConfigPath)}::{pair.Prefix}";

                var alreadyRegistered = HudRepositionAPI.GetRegistered()
                    .Any(e =>
                        e.AnchorX != null &&
                        string.Equals(e.AnchorX.Definition.Key, pair.KeyX, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.AnchorY.Definition.Key, pair.KeyY, StringComparison.OrdinalIgnoreCase));
                if (alreadyRegistered)
                    continue;

                var match = hudRoots.FirstOrDefault(r => NameMatchesPrefix(r.name, pair.Prefix));
                if (match == null && hudRoots.Count > 0)
                    match = hudRoots.FirstOrDefault(r =>
                        r.name.IndexOf(pair.Prefix.Substring(0, Math.Min(4, pair.Prefix.Length)),
                            StringComparison.OrdinalIgnoreCase) >= 0);

                if (match == null)
                    continue;

                var rect = match.GetComponent<RectTransform>();
                if (rect == null)
                    continue;

                if (!configFiles.TryGetValue(pair.ConfigPath, out var cfg))
                {
                    cfg = new ConfigFile(pair.ConfigPath, true);
                    configFiles[pair.ConfigPath] = cfg;
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
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogWarning($"[HudReposition] Auto-detect failed: {ex.Message}");
        }
    }

    public static List<AnchorPair> DiscoverAnchorPairs()
    {
        var result = new List<AnchorPair>();
        if (!Directory.Exists(Paths.ConfigPath))
            return result;

        foreach (var file in Directory.GetFiles(Paths.ConfigPath, "*.cfg"))
            try
            {
                var section = "";
                var floats = new Dictionary<string, (string section, float value)>(StringComparer.OrdinalIgnoreCase);

                foreach (var raw in File.ReadLines(file))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2);
                        continue;
                    }

                    var eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    var key = line.Substring(0, eq).Trim();
                    var val = line.Substring(eq + 1).Trim();
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ||
                        float.TryParse(val, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                        floats[key] = (section, f);
                }

                foreach (var kv in floats)
                {
                    var m = AnchorPairRegex.Match(kv.Key);
                    if (!m.Success)
                        continue;

                    var prefix = m.Groups["prefix"].Value;
                    var keyY = prefix + "AnchorY";
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

        return result;
    }

    public static List<GameObject> FindHudRoots()
    {
        var roots = new List<GameObject>();
        var seen = new HashSet<int>();

        void Add(GameObject go)
        {
            if (go == null)
                return;
            var id = go.GetInstanceID();
            if (seen.Add(id))
                roots.Add(go);
        }

        try
        {
            var playerType = Type.GetType("Pigeon.Movement.Player, Assembly-CSharp") ??
                             AppDomain.CurrentDomain.GetAssemblies()
                                 .SelectMany(a =>
                                 {
                                     try
                                     {
                                         return a.GetTypes();
                                     }
                                     catch
                                     {
                                         return Type.EmptyTypes;
                                     }
                                 })
                                 .FirstOrDefault(t => t.Name == "Player" && t.Namespace != null &&
                                                      t.Namespace.Contains("Pigeon"));

            if (playerType != null)
            {
                var localProp = playerType.GetProperty("LocalPlayer",
                    BindingFlags.Public | BindingFlags.Static);
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
                                if (child.GetComponent<RectTransform>() != null)
                                    Add(child.gameObject);

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

        try
        {
            foreach (var rt in Object.FindObjectsOfType<RectTransform>())
            {
                var n = rt.gameObject.name;
                if (n.IndexOf("HUD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Meter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Speedometer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Altimeter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Carnometer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Tracker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("GunStats", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("BossTimer", StringComparison.OrdinalIgnoreCase) >= 0)
                    Add(rt.gameObject);
            }
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogDebug($"[HudReposition] Global HUD scan failed: {ex.Message}");
        }

        return roots;
    }

    public static bool NameMatchesPrefix(string goName, string prefix)
    {
        if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(prefix))
            return false;

        var compactGo = goName.Replace(" ", "").Replace("_", "");
        var compactPrefix = prefix.Replace(" ", "").Replace("_", "");

        if (compactGo.IndexOf(compactPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (compactPrefix.StartsWith("Gun", StringComparison.OrdinalIgnoreCase) &&
            compactGo.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (compactPrefix.Length >= 4)
        {
            var head = compactPrefix.Substring(0, Math.Min(8, compactPrefix.Length));
            if (compactGo.IndexOf(head, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    public sealed class AnchorPair
    {
        public string ConfigPath;
        public float DefaultX;
        public float DefaultY;
        public string KeyX;
        public string KeyY;
        public string Prefix;
        public string Section;
    }
}