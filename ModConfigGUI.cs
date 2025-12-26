using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private static Dictionary<string, Dictionary<string, string[]>> _cachedOptions =
        new Dictionary<string, Dictionary<string, string[]>>();

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
                SparrohPlugin.Logger.LogError($"Error creating GUI: {e.Message}");
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

        var canvas = new GameObject("ModConfigCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.transform.SetParent(null, false);
        var can = canvas.GetComponent<Canvas>();
        if (can == null)
        {
        }
        else
        {
            can.renderMode = RenderMode.ScreenSpaceOverlay;
            can.sortingOrder = 100;
        }

        var panel = new GameObject("ModConfigPanel", typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(800, 600);

        var image = panel.GetComponent<Image>();
        image.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);

        var titleObj = new GameObject("Title", typeof(Image));
        titleObj.transform.SetParent(panel.transform, false);
        var titleImg = titleObj.GetComponent<Image>();
        titleImg.color = new Color(0.4f, 0.4f, 0.5f, 1f);
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
        titleText.color = new Color(1f, 1f, 1f, 1f);
        titleText.alignment = TextAnchor.MiddleCenter;

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
        scrollImg.color = new Color(0.1f, 0.1f, 0.15f, 0.8f);

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
        scrollComponent.scrollSensitivity = 0.1f;

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
        foreach (Transform child in _content.transform)
        {
            Destroy(child.gameObject);
        }

        foreach (Transform child in _modWindow.transform.parent)
        {
            if (child.name == "List")
            {
                Destroy(child.gameObject);
            }
        }

        try
        {
            var mods = ModManager.Mods;
            if (mods == null) return;
            foreach (var mod in mods)
            {
                try
                {
                    CreateModConfig(mod, _content.transform);
                    GameObject separator = new GameObject("Separator", typeof(Image));
                    separator.transform.SetParent(_content.transform, false);
                    var sepImg = separator.GetComponent<Image>();
                    sepImg.color = new Color(0.5f, 0.5f, 0.6f, 0.5f);
                    var sepLayout = separator.AddComponent<LayoutElement>();
                    sepLayout.preferredWidth = 760;
                    sepLayout.preferredHeight = 3;
                }
                catch (Exception e)
                {
                    SparrohPlugin.Logger.LogError($"Error creating config for mod {mod.Name}: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            SparrohPlugin.Logger.LogError($"Error refreshing mods list: {e.Message}");
        }
    }

    private static Dictionary<string, List<(string entry, string[] options)>> ParseModConfig(string configPath)
    {
        try
        {
            var entries = new Dictionary<string, List<(string, string[])>>();
            string currentSection = "";
            List<string> pendingComments = new List<string>();

            foreach (var line in File.ReadLines(configPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("#"))
                {
                    pendingComments.Add(line.TrimStart('#').Trim());
                    continue;
                }

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Trim();
                    if (!entries.ContainsKey(currentSection))
                        entries[currentSection] = new List<(string, string[])>();
                    pendingComments.Clear();
                }
                else if (line.Contains("="))
                {
                    if (!entries.ContainsKey(currentSection))
                        entries[currentSection] = new List<(string, string[])>();

                    string[] options = null;
                    foreach (var comment in pendingComments)
                    {
                        if (comment.StartsWith("Acceptable values:"))
                        {
                            options = comment.Substring("Acceptable values:".Length).Split(',').Select(s => s.Trim())
                                .ToArray();
                            break;
                        }
                    }

                    entries[currentSection].Add((line.Trim(), options));
                    pendingComments.Clear();
                }
            }

            return entries;
        }
        catch (Exception e)
        {
            SparrohPlugin.Logger.LogError($"Error parsing config file '{configPath}': {e.Message}");
            return new Dictionary<string, List<(string, string[])>>();
        }
    }

    private static void CreateModConfig(ModInfo mod, Transform parent)
    {
        try
        {
            ModInfo modLocal = mod;
            GameObject titleObj = new GameObject(modLocal.Name + " Title", typeof(Image));
            titleObj.transform.SetParent(parent, false);
            var titleImg = titleObj.GetComponent<Image>();
            titleImg.color = new Color(0.6f, 0.6f, 0.7f, 1f);
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
            titleText.color = new Color(0.1f, 0.1f, 0.1f, 1f);
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
            if (!_cachedOptions.ContainsKey(configPath))
            {
                var parsed = ParseModConfig(configPath);
                _cachedOptions[configPath] = new Dictionary<string, string[]>();
                foreach (var sect in parsed)
                {
                    string sectKey = sect.Key.Trim('[', ']');
                    foreach (var (entry, opts) in sect.Value)
                    {
                        string k = entry.Substring(0, entry.IndexOf('=')).Trim();
                        _cachedOptions[configPath][sectKey + "." + k] = opts;
                    }
                }
            }

            foreach (var section in ParseModConfig(configPath))
            {
                GameObject sectionLabel = new GameObject($"Section: {section.Key}", typeof(Image));
                sectionLabel.transform.SetParent(parent, false);
                var sectionImg = sectionLabel.GetComponent<Image>();
                sectionImg.color = new Color(0.3f, 0.4f, 0.5f, 1f);
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

                foreach (var (fullEntry, _) in section.Value)
                {
                    string key = fullEntry.Substring(0, fullEntry.IndexOf('=')).Trim();
                    string value = fullEntry.Substring(fullEntry.IndexOf('=') + 1).Trim();
                    var configEntry = configFile.Bind(section.Key.Trim('[', ']'), key, value);
                    string[] options = null;
                    string cacheKey = section.Key.Trim('[', ']') + "." + key;
                    if (_cachedOptions.ContainsKey(configPath) && _cachedOptions[configPath].ContainsKey(cacheKey))
                    {
                        options = _cachedOptions[configPath][cacheKey];
                    }

                    string rawEntryValue = configEntry.Value ?? "";
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
                    entryImg.color = new Color(0.2f, 0.2f, 0.25f, 0.8f);
                    var entryLayout = entryObj.AddComponent<LayoutElement>();
                    entryLayout.preferredWidth = 760;
                    entryLayout.minHeight = 55;

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

                    GameObject valueObj = new GameObject("Value", typeof(RectTransform));
                    valueObj.transform.SetParent(entryObj.transform, false);
                    var valueRect = valueObj.GetComponent<RectTransform>();
                    valueRect.anchorMin = new Vector2(0.45f, 0);
                    valueRect.anchorMax = new Vector2(1, 1);
                    valueRect.sizeDelta = new Vector2(-10, 0);

                    if (entryType == typeof(bool))
                    {
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
                            statusComp.text = val ? "ON" : "OFF";
                            statusComp.color = val ? Color.green : Color.red;
                        });
                        var toggleImg = valueObj.AddComponent<Image>();
                        toggleImg.color = new Color(0.7f, 0.7f, 0.8f, 0.9f);
                        toggle.targetGraphic = toggleImg;
                    }
                    else if (options != null)
                    {
                        var mainButton = valueObj.AddComponent<Button>();
                        mainButton.targetGraphic = valueObj.AddComponent<Image>();
                        mainButton.targetGraphic.color = new Color(0.8f, 0.8f, 0.9f, 0.9f);
                        var mainTextObj = new GameObject("MainText", typeof(Text));
                        mainTextObj.transform.SetParent(valueObj.transform, false);
                        var mainTextRect = mainTextObj.GetComponent<RectTransform>();
                        mainTextRect.anchorMin = Vector2.zero;
                        mainTextRect.anchorMax = Vector2.one;
                        mainTextRect.offsetMin = Vector2.zero;
                        mainTextRect.offsetMax = Vector2.zero;
                        var mainText = mainTextObj.GetComponent<Text>();
                        mainText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                        mainText.fontSize = 16;
                        mainText.color = Color.black;
                        mainText.alignment = TextAnchor.MiddleCenter;
                        mainText.text = value;

                        var listObj = new GameObject("List", typeof(Image));
                        listObj.transform.SetParent(_modWindow.transform.parent, false);
                        var listRect = listObj.GetComponent<RectTransform>();
                        listRect.anchorMin = new Vector2(0.5f, 0.5f);
                        listRect.anchorMax = new Vector2(0.5f, 0.5f);
                        listRect.pivot = new Vector2(0.5f, 1);
                        listRect.sizeDelta = new Vector2(200, 30 * options.Length);
                        listRect.anchoredPosition = new Vector2(0, 0);
                        var listImg = listObj.GetComponent<Image>();
                        listImg.color = new Color(0.9f, 0.9f, 0.95f, 1f);
                        var listCanvas = listObj.AddComponent<Canvas>();
                        listCanvas.sortingOrder = 200;
                        listObj.AddComponent<GraphicRaycaster>();
                        listObj.SetActive(false);

                        for (int i = 0; i < options.Length; i++)
                        {
                            var opt = options[i];
                            var optionObj = new GameObject("Option " + i, typeof(Button), typeof(Image));
                            optionObj.transform.SetParent(listObj.transform, false);
                            var optionRect = optionObj.GetComponent<RectTransform>();
                            optionRect.anchorMin = new Vector2(0, 1);
                            optionRect.anchorMax = new Vector2(1, 1);
                            optionRect.pivot = new Vector2(0.5f, 1);
                            optionRect.sizeDelta = new Vector2(0, 30);
                            optionRect.anchoredPosition = new Vector2(0, -i * 30);
                            var optionImg = optionObj.GetComponent<Image>();
                            optionImg.color = new Color(0.8f, 0.8f, 0.9f, 1f);
                            var optionButton = optionObj.GetComponent<Button>();
                            optionButton.targetGraphic = optionImg;
                            var optionTextObj = new GameObject("Text", typeof(Text));
                            optionTextObj.transform.SetParent(optionObj.transform, false);
                            var optionTextRect = optionTextObj.GetComponent<RectTransform>();
                            optionTextRect.anchorMin = new Vector2(0, 0);
                            optionTextRect.anchorMax = new Vector2(1, 1);
                            optionTextRect.offsetMin = new Vector2(20, 0);
                            optionTextRect.offsetMax = new Vector2(-4, 0);
                            var optionText = optionTextObj.GetComponent<Text>();
                            optionText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                            optionText.fontSize = 14;
                            optionText.color = Color.black;
                            optionText.alignment = TextAnchor.MiddleLeft;
                            optionText.text = opt;
                            int index = i;
                            optionButton.onClick.AddListener(() =>
                            {
                                configEntry.Value = options[index];
                                configEntry.ConfigFile.Save();
                                mainText.text = options[index];
                                listObj.SetActive(false);
                            });
                        }

                        mainButton.onClick.AddListener(() =>
                        {
                            if (!listObj.activeSelf)
                            {
                                Vector3[] corners = new Vector3[4];
                                valueObj.GetComponent<RectTransform>().GetWorldCorners(corners);
                                Vector3 center = (corners[0] + corners[2]) / 2;
                                Vector2 localPoint;
                                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                    _modWindow.transform.parent.GetComponent<RectTransform>(), center, null,
                                    out localPoint);
                                listRect.anchoredPosition = localPoint + new Vector2(0,
                                    -valueObj.GetComponent<RectTransform>().rect.height / 2 - 5);
                            }

                            listObj.SetActive(!listObj.activeSelf);
                        });
                    }
                    else
                    {
                        var input = valueObj.AddComponent<InputField>();
                        input.text = value;
                        if (section.Key.Trim('[', ']') == "Keybinds" && key == "ToggleModConfigGUI")
                        {
                            var eventTrigger = input.gameObject.AddComponent<EventTrigger>();
                            var triggerEntry = new EventTrigger.Entry();
                            triggerEntry.eventID = EventTriggerType.PointerClick;
                            triggerEntry.callback.AddListener((data) =>
                            {
                                if (!SparrohPlugin.IsRebinding)
                                {
                                    input.text = "Press new key...";
                                    input.interactable = false;
                                    SparrohPlugin.IsRebinding = true;
                                    KeyBindInput = input;
                                }
                            });
                            eventTrigger.triggers.Add(triggerEntry);
                        }

                        input.onValueChanged.AddListener(newVal =>
                        {
                            if (!SparrohPlugin.IsRebinding ||
                                (section.Key.Trim('[', ']') != "Keybinds" || key != "ToggleModConfigGUI"))
                            {
                                configEntry.Value = newVal;
                                configEntry.ConfigFile.Save();
                            }
                        });
                        var inputImg = valueObj.AddComponent<Image>();
                        inputImg.color = new Color(0.8f, 0.8f, 0.9f, 0.9f);
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
                        input.textComponent.color = Color.black;
                        input.textComponent.alignment = TextAnchor.MiddleCenter;
                        input.targetGraphic = inputImg;
                    }
                }
            }
        }
        catch (Exception e)
        {
            SparrohPlugin.Logger.LogError($"Error creating mod config for {mod.Name}: {e.Message}");
        }
    }
}