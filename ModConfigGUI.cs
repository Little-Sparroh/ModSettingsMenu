using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using BepInEx;
using BepInEx.Configuration;

public class ModConfigGUI : MonoBehaviour
{
    private static GameObject _modWindow;
    private static GameObject _scrollView;
    private static GameObject _content;
    private static VerticalLayoutGroup _layoutGroup;
    private static bool _visible = false;
    public static InputField KeyBindInput;

    public static void Toggle()
    {
        if (_modWindow == null)
        {
            try
            {
                CreateGUI();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Error creating GUI: " + e);
                return;
            }
        }
        _visible = !_visible;
        _modWindow.SetActive(_visible);
        if (_visible) RefreshMods();
        UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
    }

    private static void CreateGUI()
    {
        Plugin.Logger.LogInfo("Creating GUI Start");

        // Create canvas
        var canvas = new GameObject("ModConfigCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.transform.SetParent(null, false);
        var can = canvas.GetComponent<Canvas>();
        if (can == null) Plugin.Logger.LogError("Canvas component null");
        else {
            can.renderMode = RenderMode.ScreenSpaceOverlay;
            can.sortingOrder = 100;
            Plugin.Logger.LogInfo("Canvas created");
        }

        // Panel background
        var panel = new GameObject("ModConfigPanel", typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(800, 600);

        var image = panel.GetComponent<Image>();
        image.color = new Color(0.15f, 0.15f, 0.2f, 0.95f); // Dark blue-gray background
        Plugin.Logger.LogInfo("Panel created");

        // Title
        var titleObj = new GameObject("Title", typeof(Image));
        titleObj.transform.SetParent(panel.transform, false);
        var titleImg = titleObj.GetComponent<Image>();
        titleImg.color = new Color(0.4f, 0.4f, 0.5f, 1f); // Header background
        var titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.offsetMin = new Vector2(10, -50);
        titleRect.offsetMax = new Vector2(-10, -10);

        var titleTextObj = new GameObject("TitleText", typeof(Text));
        titleTextObj.transform.SetParent(titleObj.transform, false);
        var titleTextRect = titleTextObj.GetComponent<RectTransform>();
        titleTextRect.anchorMin = Vector2.zero;
        titleTextRect.anchorMax = Vector2.one;
        titleTextRect.offsetMin = Vector2.zero;
        titleTextRect.offsetMax = Vector2.zero;
        var titleText = titleTextObj.GetComponent<Text>();
        titleText.text = "Mod Configurations";
        titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        titleText.fontSize = 26;
        titleText.color = new Color(1f, 1f, 1f, 1f); // Bright white
        titleText.alignment = TextAnchor.MiddleCenter;
        Plugin.Logger.LogInfo("Title created");

        // Scroll view
        var scrollObj = new GameObject("ScrollView", typeof(ScrollRect), typeof(Image), typeof(UnityEngine.UI.Mask));
        scrollObj.transform.SetParent(panel.transform, false);
        var mask = scrollObj.GetComponent<UnityEngine.UI.Mask>();
        mask.showMaskGraphic = false;
        var scrollRect = scrollObj.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = new Vector2(1, 1);
        scrollRect.offsetMin = new Vector2(20, 20);
        scrollRect.offsetMax = new Vector2(-20, -60);

        var scrollImg = scrollObj.GetComponent<Image>();
        scrollImg.color = new Color(0.1f, 0.1f, 0.15f, 0.8f); // Darker scroll background

        var scrollComponent = scrollObj.GetComponent<ScrollRect>();
        var scrollContent = new GameObject("Content", typeof(VerticalLayoutGroup));
        scrollContent.transform.SetParent(scrollObj.transform, false);
        var contentRect = scrollContent.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0, 1);
        contentRect.sizeDelta = new Vector2(0, 0);

        scrollComponent.content = contentRect;
        scrollComponent.vertical = true;
        scrollComponent.horizontal = false;
        scrollComponent.scrollSensitivity = 0.1f; // Slower scroll wheel

        var layout = scrollContent.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 15;
        layout.padding = new RectOffset(20, 20, 15, 20);

        var fitter = scrollContent.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _modWindow = panel;
        _content = scrollContent;
        _layoutGroup = layout;
    }

    public static void RefreshMods()
    {
        // Clear existing entries
        foreach (Transform child in _content.transform)
        {
            Destroy(child.gameObject);
        }

        // Assuming ModManager.Mods is accessible
        try
        {
            var mods = ModManager.Mods;
            if (mods == null) return;
            foreach (var mod in mods)
            {
                try
                {
                    CreateModConfig(mod, _content.transform);
                    // Add separator between mods
                    GameObject separator = new GameObject("Separator", typeof(Image));
                    separator.transform.SetParent(_content.transform, false);
                    var sepImg = separator.GetComponent<Image>();
                    sepImg.color = new Color(0.5f, 0.5f, 0.6f, 0.5f); // Subtle separator line
                    var sepLayout = separator.AddComponent<LayoutElement>();
                    sepLayout.preferredWidth = 760;
                    sepLayout.preferredHeight = 3; // Thin line
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError("Error creating config for mod " + mod.Name + ": " + e);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Error in PopulateMods: " + e);
        }
    }

    private static Dictionary<string, List<string>> ParseModConfig(string configPath)
    {
        var entries = new Dictionary<string, List<string>>();
        string currentSection = "";

        foreach (var line in File.ReadLines(configPath))
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                currentSection = line.Trim();
                if (!entries.ContainsKey(currentSection))
                    entries[currentSection] = new List<string>();
            }
            else if (line.Contains("="))
            {
                if (!entries.ContainsKey(currentSection))
                    entries[currentSection] = new List<string>();

                entries[currentSection].Add(line.Trim());
            }
        }

        return entries;
    }

    private static void CreateModConfig(ModInfo mod, Transform parent)
    {
        ModInfo modLocal = mod;
        GameObject titleObj = new GameObject(modLocal.Name + " Title", typeof(Image));
        titleObj.transform.SetParent(parent, false);
        var titleImg = titleObj.GetComponent<Image>();
        titleImg.color = new Color(0.6f, 0.6f, 0.7f, 1f); // Lighter blue for mod headers
        var titleLayout = titleObj.AddComponent<LayoutElement>();
        titleLayout.preferredWidth = 760;
        titleLayout.minHeight = 40;

        GameObject titleTextObj = new GameObject("Text", typeof(Text));
        titleTextObj.transform.SetParent(titleObj.transform, false);
        var titleTextRect = titleTextObj.GetComponent<RectTransform>();
        titleTextRect.anchorMin = Vector2.zero;
        titleTextRect.anchorMax = Vector2.one;
        titleTextRect.offsetMin = Vector2.zero;
        titleTextRect.offsetMax = Vector2.zero;
        var titleText = titleTextObj.GetComponent<Text>();
        titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        titleText.text = $"<b>{modLocal.Name}</b>";
        if (modLocal.IsSandbox)
            titleText.text += $" <size=50%>[<i><color=#FF0000>Sandbox</color></i>]</size>";
        titleText.fontSize = 22;
        titleText.color = new Color(0.1f, 0.1f, 0.1f, 1f); // Dark text for contrast
        titleText.alignment = TextAnchor.MiddleCenter;

        string[] cfgFiles = Directory.GetFiles(Paths.ConfigPath, "*.cfg");
        string configPath = null;
        foreach (string file in cfgFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
    if (fileName.ToLower().Contains(modLocal.Name.ToLower()))
            {
                configPath = file;
                break;
            }
        }
        Plugin.Logger.LogInfo($"Checking config for mod {modLocal.Name}: {configPath ?? "not found"}");
        if (configPath == null || !File.Exists(configPath))
        {
            GameObject noConfigObj = new GameObject("NoConfig", typeof(Text));
            noConfigObj.transform.SetParent(parent, false);
            var noConfigText = noConfigObj.GetComponent<Text>();
            noConfigText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            noConfigText.text = "(No config found)";
            noConfigText.fontSize = 18;
            noConfigText.color = Color.gray;
            noConfigText.alignment = TextAnchor.MiddleCenter;
            var layoutElem = noConfigObj.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 760;
            layoutElem.preferredHeight = 28;
            return;
        }
        var configFile = new ConfigFile(configPath, true);
        foreach (var section in ParseModConfig(configPath))
        {
            GameObject sectionLabel = new GameObject($"Section: {section.Key}", typeof(Image));
            sectionLabel.transform.SetParent(parent, false);
            var sectionImg = sectionLabel.GetComponent<Image>();
            sectionImg.color = new Color(0.3f, 0.4f, 0.5f, 1f); // Section header background
            sectionLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(760, -1);
            var layoutElem = sectionLabel.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 760;
            layoutElem.minHeight = 35;

            GameObject sectionTextObj = new GameObject("Text", typeof(Text));
            sectionTextObj.transform.SetParent(sectionLabel.transform, false);
            var sectionTextRect = sectionTextObj.GetComponent<RectTransform>();
            sectionTextRect.anchorMin = Vector2.zero;
            sectionTextRect.anchorMax = Vector2.one;
            sectionTextRect.offsetMin = Vector2.zero;
            sectionTextRect.offsetMax = Vector2.zero;
            var sectionText = sectionTextObj.GetComponent<Text>();
            sectionText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            sectionText.text = section.Key;
            sectionText.fontSize = 20;
            sectionText.color = Color.white;
            sectionText.alignment = TextAnchor.MiddleCenter;

            foreach (var entry in section.Value)
            {
                // Simplified version of config entry creation
                // Use similar logic as original but adapted to new layout
                string key = entry.Substring(0, entry.LastIndexOf('=')).Trim();
                string value = entry.Substring(entry.LastIndexOf('=') + 1).Trim();
                var configEntry = configFile.Bind(section.Key.Trim('[', ']'), key, value);
                    string rawEntryValue = configEntry.Value;
                    Type entryType = typeof(string);

                    if (bool.TryParse(rawEntryValue, out var _))
                        entryType = typeof(bool);
                    else if (int.TryParse(rawEntryValue, out var _))
                        entryType = typeof(int);
                    else if (float.TryParse(rawEntryValue, out var _))
                        entryType = typeof(float);

                    GameObject entryObj = new GameObject("Entry " + key);
                    entryObj.transform.SetParent(parent, false);
                    var entryImg = entryObj.AddComponent<Image>();
                    entryImg.color = new Color(0.2f, 0.2f, 0.25f, 0.8f); // Entry panel background
                    var entryLayout = entryObj.AddComponent<LayoutElement>();
                    entryLayout.preferredWidth = 760;
                    entryLayout.minHeight = 55;

                    // Label
                    GameObject labelObj = new GameObject("Label", typeof(Text));
                    labelObj.transform.SetParent(entryObj.transform, false);
                    var labelText = labelObj.GetComponent<Text>();
                    labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    labelText.text = key;
                    labelText.fontSize = 16;
                    labelText.color = Color.white;
                    labelText.alignment = TextAnchor.MiddleCenter;
                    var labelRect = labelObj.GetComponent<RectTransform>();
                    labelRect.anchorMin = new Vector2(0, 0.5f);
                    labelRect.anchorMax = new Vector2(0.4f, 0.5f);

                    // Value field
                    GameObject valueObj = new GameObject("Value", typeof(RectTransform));
                    valueObj.transform.SetParent(entryObj.transform, false);
                    var valueRect = valueObj.GetComponent<RectTransform>();
                    valueRect.anchorMin = new Vector2(0.45f, 0);
                    valueRect.anchorMax = new Vector2(1, 1);
                    valueRect.sizeDelta = new Vector2(-10, 0);

                    if (entryType == typeof(bool))
                    {
                        // Toggle for bool
                        valueObj.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);

                        var toggle = valueObj.AddComponent<Toggle>();
                        toggle.isOn = rawEntryValue.ToLower() == "true";
                        var statusText = new GameObject("Status", typeof(Text));
                        statusText.transform.SetParent(valueObj.transform, false);
                        var statusComp = statusText.GetComponent<Text>();
                        statusComp.text = toggle.isOn ? "ON" : "OFF";
                        statusComp.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                        statusComp.fontSize = 16;
                        statusComp.color = toggle.isOn ? Color.green : Color.red;
                        statusComp.alignment = TextAnchor.MiddleCenter;
                        var statusRect = statusText.GetComponent<RectTransform>();
                        statusRect.anchorMin = Vector2.zero;
                        statusRect.anchorMax = Vector2.one;
                        statusRect.offsetMin = Vector2.zero;
                        statusRect.offsetMax = Vector2.zero;

                        toggle.onValueChanged.AddListener(val =>
                        {
                            configEntry.Value = val ? "True" : "False";
                            configEntry.ConfigFile.Save();
                            Plugin.Logger.LogInfo($"Saved {section.Key} {key} = {configEntry.Value}");
                            statusComp.text = val ? "ON" : "OFF";
                            statusComp.color = val ? Color.green : Color.red;
                        });
                        var toggleImg = valueObj.AddComponent<Image>();
                        toggleImg.color = new Color(0.7f, 0.7f, 0.8f, 0.9f); // Light gray for toggle background
                        toggle.targetGraphic = toggleImg;
                    }
                    else
                    {
                        // Input field for others
                        var input = valueObj.AddComponent<InputField>();
                        input.text = value;
                        if (section.Key.Trim('[', ']') == "Keybinds" && key == "ToggleModConfigGUI")
                        {
                            var eventTrigger = input.gameObject.AddComponent<EventTrigger>();
                            var triggerEntry = new EventTrigger.Entry();
                            triggerEntry.eventID = EventTriggerType.PointerClick;
                            triggerEntry.callback.AddListener((data) =>
                            {
                                if (!Plugin.IsRebinding)
                                {
                                    input.text = "Press new key...";
                                    input.interactable = false;
                                    Plugin.IsRebinding = true;
                                    KeyBindInput = input;
                                }
                            });
                            eventTrigger.triggers.Add(triggerEntry);
                        }
                        input.onValueChanged.AddListener(newVal =>
                        {
                            if (!Plugin.IsRebinding || (section.Key.Trim('[', ']') != "Keybinds" || key != "ToggleModConfigGUI"))
                            {
                                configEntry.Value = newVal;
                                configEntry.ConfigFile.Save();
                                Plugin.Logger.LogInfo($"Saved {section.Key} {key} = {configEntry.Value}");
                            }
                        });
                        var inputImg = valueObj.AddComponent<Image>();
                        inputImg.color = new Color(0.8f, 0.8f, 0.9f, 0.9f); // Light blue-gray for input background
                        var textChild = new GameObject("Text", typeof(Text));
                        textChild.transform.SetParent(valueObj.transform, false);
                        var textChildRect = textChild.GetComponent<RectTransform>();
                        textChildRect.anchorMin = new Vector2(0.5f, 0.5f);
                        textChildRect.anchorMax = new Vector2(0.5f, 0.5f);
                        textChildRect.anchoredPosition = Vector2.zero;
                        textChildRect.pivot = new Vector2(0.5f, 0.5f);
                        textChildRect.sizeDelta = new Vector2(300, 30);
                        input.textComponent = textChild.GetComponent<Text>();
                        input.textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                        input.textComponent.fontSize = 16;
                        input.textComponent.color = Color.black; // Dark text for contrast in input field
                        input.textComponent.alignment = TextAnchor.MiddleCenter;
                        input.targetGraphic = inputImg;
                    }
                }
            }
        }
    }
