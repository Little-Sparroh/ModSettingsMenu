using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Sparroh.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ModConfigGUI : MonoBehaviour
{
    private const float ClickDragThresholdPx = 8f;

    private const float FontTitleRef = 26f;
    private const float FontModTitleRef = 22f;
    private const float FontSectionRef = 18f;
    private const float FontLabelRef = 17f;
    private const float FontBodyRef = 16f;
    private const float FontSmallRef = 14f;

    private static UIWindow _window;
    private static bool _cursorHeld;
    private static TMP_InputField _activeInput;
    private static readonly List<UIDropdown> _openDropdowns = new();
    private static StickyModTitleController _stickyTitles;

    private static RectTransform _toolbarRoot;
    private static RectTransform _toolbarFiltersBody;
    private static RectTransform _toolbarScrollRt;
    private static UIInputField _searchField;
    private static UIDropdown _sortDropdown;
    private static UIToggle _hideEmptyToggle;
    private static UIToggle _groupByAuthorToggle;
    private static UIButton _expandCollapseAllBtn;
    private static UIButton _toolbarFiltersToggleBtn;
    private static readonly List<UIButton> _filterChipButtons = new();
    private static string _searchQuery = "";
    private static bool _suppressToolbarCallbacks;
    private static bool _toolbarFiltersCollapsed;
    private static float _toolbarHExpanded;
    private static float _toolbarHCollapsed;
    private static string[] _cfgFilesCache;

    private static readonly HashSet<string> _collapsedMods = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ModBlockState> _modBlocks = new();

    private static readonly string[] SortModeIds =
    {
        "Alphabetical",
        "AlphabeticalDesc",
        "LoadOrder",
        "SandboxFirst",
        "HasConfigFirst"
    };

    private static readonly string[] SortModeLabels =
    {
        "A–Z",
        "Z–A",
        "Load order",
        "Sandbox first",
        "Has config first"
    };

    private static readonly string[] FilterChipIds = { "All", "Sandbox", "ClientSide" };
    private static readonly string[] FilterChipLabels = { "All", "Sandbox", "Client-side" };

    public static TMP_InputField KeyBindInput;
    public static TMP_InputField RepositionKeyBindInput;

    private static readonly Dictionary<string, Dictionary<string, EntryMeta>> _cachedMeta = new();

    public static bool IsVisible { get; private set; }

    internal static void ClearActiveEditing()
    {
        if (_activeInput != null)
        {
            _activeInput.DeactivateInputField();
            _activeInput = null;
        }

        for (var i = _openDropdowns.Count - 1; i >= 0; i--)
        {
            var dd = _openDropdowns[i];
            if (dd != null)
                dd.CloseList();
        }

        _openDropdowns.Clear();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private static void RegisterOpenDropdown(UIDropdown dropdown)
    {
        if (dropdown == null)
            return;
        if (!_openDropdowns.Contains(dropdown))
            _openDropdowns.Add(dropdown);
    }

    private static void UnregisterDropdown(UIDropdown dropdown)
    {
        _openDropdowns.Remove(dropdown);
    }

    private static void ActivateInput(TMP_InputField input)
    {
        if (input == null)
            return;

        if (_activeInput != null && _activeInput != input)
            _activeInput.DeactivateInputField();

        _activeInput = input;
        input.interactable = true;

        input.customCaretColor = true;
        if (input.caretWidth < 2)
            input.caretWidth = 2;
        if (input.caretBlinkRate <= 0f)
            input.caretBlinkRate = 0.85f;

        input.ActivateInputField();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(input.gameObject);

        input.MoveTextEnd(false);
        input.ForceLabelUpdate();
    }

    public static void Toggle()
    {
        if (_window == null || _window.GameObject == null)
            try
            {
                CreateGUI();
            }
            catch (Exception e)
            {
                SparrohPlugin.Logger.LogError($"Error creating GUI: {e}");
                return;
            }

        if (IsVisible)
            Hide();
        else
            Show();
    }

    public static void Show()
    {
        if (_window == null || _window.GameObject == null)
            try
            {
                CreateGUI();
            }
            catch (Exception e)
            {
                SparrohPlugin.Logger.LogError($"Error creating GUI: {e}");
                return;
            }

        if (IsVisible)
            return;

        if (HudRepositionMode.IsActive)
            HudRepositionMode.Exit();

        IsVisible = true;
        _window.Show();
        HoldCursor();

        _searchQuery = "";
        if (_searchField != null)
        {
            _suppressToolbarCallbacks = true;
            _searchField.Text = "";
            _suppressToolbarCallbacks = false;
        }

        SyncToolbarFromConfig();
        RefreshMods(true);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public static void Hide()
    {
        if (!IsVisible)
            return;

        IsVisible = false;
        ClearActiveEditing();
        if (_window != null)
            _window.Hide(false);
        ReleaseCursor();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private static void HoldCursor()
    {
        if (_cursorHeld)
            return;
        FreeCursor.Acquire();
        _cursorHeld = true;
    }

    private static void ReleaseCursor()
    {
        if (!_cursorHeld)
            return;
        FreeCursor.Release();
        _cursorHeld = false;
    }

    private static void CreateGUI()
    {
        UITheme.Initialize();

        _window = UIWindow.Create(
            "ModConfig",
            new Vector2(520f, 640f),
            "Mod Configs",
            true,
            true,
            UITheme.WindowSortingOrder + 10);

        _window.OnClose(() =>
        {
            if (!IsVisible)
                return;
            IsVisible = false;
            ClearActiveEditing();
            ReleaseCursor();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        });

        if (_window.TitleText != null)
        {
            _window.TitleText.fontSize = UITheme.S(FontTitleRef);
            _window.TitleText.fontStyle = FontStyles.Bold;
            _window.TitleText.color = UIColors.TextPrimary;
            _window.TitleText.alignment = TextAlignmentOptions.Left;

            UIHelpers.SetFillParent(_window.TitleText.rectTransform, UITheme.S(8f));
            _window.TitleText.rectTransform.offsetMax = new Vector2(-UITheme.S(200f), -UITheme.S(4f));
            _window.TitleText.rectTransform.offsetMin = new Vector2(UITheme.S(10f), UITheme.S(4f));
        }

        try
        {
            var titleBar = _window.TitleText != null
                ? _window.TitleText.transform.parent as RectTransform
                : null;
            if (titleBar != null)
            {
                var repoBtn = UIButton.Create(
                    titleBar,
                    "Reposition HUDs",
                    () =>
                    {
                        Hide();
                        HudRepositionMode.Enter();
                    },
                    UIButtonStyle.Primary,
                    "RepositionButton",
                    UITheme.S(28f));

                var crt = repoBtn.Rect;
                crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
                crt.pivot = new Vector2(1f, 0.5f);
                crt.sizeDelta = new Vector2(UITheme.S(150f), UITheme.S(28f));
                crt.anchoredPosition = new Vector2(-UITheme.S(48f), 0f);

                var le = repoBtn.GameObject.GetComponent<LayoutElement>();
                if (le != null)
                    Destroy(le);

                if (repoBtn.Label != null)
                {
                    repoBtn.Label.fontSize = UITheme.S(FontSmallRef);
                    repoBtn.Label.fontStyle = FontStyles.Bold;
                }
            }
        }
        catch (Exception e)
        {
            SparrohPlugin.Logger.LogWarning($"Could not add Reposition button to title bar: {e.Message}");
        }

        if (_window.ScrollView != null)
        {
            var scroll = _window.ScrollView.ScrollRect;
            scroll.scrollSensitivity = 0f;

            _stickyTitles = CreateStickyModTitleOverlay(_window.ScrollView);

            var stepScroll = _window.ScrollView.GameObject.AddComponent<ItemStepScrollHandler>();
            stepScroll.Initialize(scroll, _stickyTitles);

            CreateToolbar(_window.ScrollView);
        }

        _window.Hide(false);
        IsVisible = false;
    }

    private static void CreateToolbar(UIScrollView scrollView)
    {
        var body = scrollView.Rect.parent as RectTransform;
        if (body == null)
            return;

        var pad = UITheme.S(4f);
        var searchH = UITheme.S(34f);
        var rowH = UITheme.S(30f);
        var gap = UITheme.S(5f);

        _toolbarHExpanded = pad + searchH + gap + rowH + gap + rowH + gap + rowH + pad;
        _toolbarHCollapsed = pad + searchH + pad;

        _toolbarScrollRt = scrollView.Rect;
        _toolbarFiltersCollapsed = SparrohPlugin.ToolbarFiltersCollapsed?.Value ?? false;
        var initialH = _toolbarFiltersCollapsed ? _toolbarHCollapsed : _toolbarHExpanded;

        _toolbarScrollRt.offsetMax = new Vector2(_toolbarScrollRt.offsetMax.x, -initialH);

        var toolbarBg = UIFactory.CreateImage("Toolbar", body, UIColors.Surface);
        UIFactory.ApplyWhiteSprite(toolbarBg);
        _toolbarRoot = toolbarBg.rectTransform;
        UIHelpers.SetTopStretch(_toolbarRoot, initialH);
        _toolbarRoot.SetAsLastSibling();

        var accent = UIFactory.CreateImage("ToolbarAccent", _toolbarRoot, UIColors.BorderAccent, false);
        UIFactory.ApplyWhiteSprite(accent);
        var accentRt = accent.rectTransform;
        accentRt.anchorMin = new Vector2(0f, 0f);
        accentRt.anchorMax = new Vector2(1f, 0f);
        accentRt.pivot = new Vector2(0.5f, 0f);
        accentRt.sizeDelta = new Vector2(0f, UITheme.S(2f));
        accentRt.anchoredPosition = Vector2.zero;
        var accentLe = accent.gameObject.AddComponent<LayoutElement>();
        accentLe.ignoreLayout = true;

        UIFactory.AddVerticalLayout(
            toolbarBg.gameObject,
            gap,
            UITheme.ScaledPadding(8, 8, 6, 8));

        var searchRow = UIFactory.CreateRect("SearchRow", _toolbarRoot);
        UIHelpers.EnsureLayoutElement(searchRow.gameObject, preferredHeight: searchH, minHeight: searchH);
        UIFactory.AddHorizontalLayout(
            searchRow.gameObject,
            UITheme.S(6f),
            new RectOffset(0, 0, 0, 0));

        _searchField = UIInputField.Create(
            searchRow,
            "",
            "Search mods…",
            query =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                _searchQuery = query ?? "";
                RefreshMods(true, true);
            },
            "SearchField");
        var searchLe = UIHelpers.EnsureLayoutElement(_searchField.GameObject,
            preferredHeight: searchH,
            minHeight: searchH);
        searchLe.flexibleWidth = 1f;
        if (_searchField.TextComponent != null)
        {
            _searchField.TextComponent.fontSize = UITheme.S(FontBodyRef);
            _searchField.TextComponent.alignment = TextAlignmentOptions.Left;
        }

        if (_searchField.Placeholder != null)
        {
            _searchField.Placeholder.fontSize = UITheme.S(FontBodyRef);
            _searchField.Placeholder.alignment = TextAlignmentOptions.Left;
        }

        _toolbarFiltersToggleBtn = UIButton.Create(
            searchRow,
            _toolbarFiltersCollapsed ? "+" : "-",
            () =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                SetToolbarFiltersCollapsed(!_toolbarFiltersCollapsed, true);
            },
            UIButtonStyle.Default,
            "ToolbarFiltersToggle",
            searchH);
        var toggleLe = UIHelpers.EnsureLayoutElement(_toolbarFiltersToggleBtn.GameObject,
            searchH,
            searchH,
            searchH);
        toggleLe.flexibleWidth = 0f;
        if (_toolbarFiltersToggleBtn.Label != null)
        {
            _toolbarFiltersToggleBtn.Label.fontSize = UITheme.S(FontModTitleRef);
            _toolbarFiltersToggleBtn.Label.fontStyle = FontStyles.Bold;
        }

        _toolbarFiltersBody = UIFactory.CreateRect("FiltersBody", _toolbarRoot);
        var filtersBodyH = rowH + gap + rowH + gap + rowH;
        UIHelpers.EnsureLayoutElement(_toolbarFiltersBody.gameObject,
            preferredHeight: filtersBodyH,
            minHeight: filtersBodyH);
        UIFactory.AddVerticalLayout(
            _toolbarFiltersBody.gameObject,
            gap,
            new RectOffset(0, 0, 0, 0));

        var row2 = UIFactory.CreateRect("SortRow", _toolbarFiltersBody);
        UIHelpers.EnsureLayoutElement(row2.gameObject, preferredHeight: rowH, minHeight: rowH);
        UIFactory.AddHorizontalLayout(
            row2.gameObject,
            UITheme.S(8f),
            new RectOffset(0, 0, 0, 0));

        var initialSort = IndexOfSortMode(SparrohPlugin.ModSortMode?.Value);
        _sortDropdown = UIDropdown.Create(
            row2,
            SortModeLabels,
            initialSort,
            null,
            "SortDropdown");
        var sortLe = UIHelpers.EnsureLayoutElement(_sortDropdown.GameObject,
            UITheme.S(160f),
            rowH,
            rowH);
        sortLe.flexibleWidth = 1f;
        if (_sortDropdown.Label != null)
        {
            _sortDropdown.Label.fontSize = UITheme.S(FontSmallRef);
            _sortDropdown.Label.fontStyle = FontStyles.Bold;
        }

        var sortMainBtn = _sortDropdown.GameObject.GetComponentInChildren<Button>();
        if (sortMainBtn != null)
        {
            sortMainBtn.onClick.RemoveAllListeners();
            sortMainBtn.onClick.AddListener(() =>
            {
                var wasOpen = _sortDropdown.IsOpen;
                ClearActiveEditing();
                if (!wasOpen)
                {
                    _sortDropdown.OpenList();
                    RegisterOpenDropdown(_sortDropdown);
                }
            });
        }

        _sortDropdown.OnChanged((idx, _) =>
        {
            if (_suppressToolbarCallbacks)
                return;
            var mode = SortModeIds[Mathf.Clamp(idx, 0, SortModeIds.Length - 1)];
            if (SparrohPlugin.ModSortMode != null && SparrohPlugin.ModSortMode.Value != mode)
            {
                SparrohPlugin.ModSortMode.Value = mode;
                SparrohPlugin.ModSortMode.ConfigFile.Save();
            }

            UnregisterDropdown(_sortDropdown);
            RefreshMods(true);
        });

        _hideEmptyToggle = UIToggle.Create(
            row2,
            "Hide empty",
            SparrohPlugin.HideModsWithoutConfig?.Value ?? true,
            val =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                if (SparrohPlugin.HideModsWithoutConfig != null &&
                    SparrohPlugin.HideModsWithoutConfig.Value != val)
                {
                    SparrohPlugin.HideModsWithoutConfig.Value = val;
                    SparrohPlugin.HideModsWithoutConfig.ConfigFile.Save();
                }

                RefreshMods(true);
            },
            "HideEmptyToggle");
        var hideLe = UIHelpers.EnsureLayoutElement(_hideEmptyToggle.GameObject,
            UITheme.S(120f),
            rowH);
        hideLe.flexibleWidth = 0f;
        if (_hideEmptyToggle.Label != null)
            _hideEmptyToggle.Label.fontSize = UITheme.S(FontSmallRef);

        var row3 = UIFactory.CreateRect("ChipRow", _toolbarFiltersBody);
        UIHelpers.EnsureLayoutElement(row3.gameObject, preferredHeight: rowH, minHeight: rowH);
        UIFactory.AddHorizontalLayout(
            row3.gameObject,
            UITheme.S(6f),
            new RectOffset(0, 0, 0, 0),
            TextAnchor.MiddleLeft,
            true,
            true);

        _filterChipButtons.Clear();
        var activeFilter = NormalizeFilter(SparrohPlugin.ModListFilter?.Value);
        for (var i = 0; i < FilterChipIds.Length; i++)
        {
            var chipId = FilterChipIds[i];
            var chipLabel = FilterChipLabels[i];
            var selected = string.Equals(chipId, activeFilter, StringComparison.OrdinalIgnoreCase);
            var chip = UIButton.Create(
                row3,
                chipLabel,
                null,
                selected ? UIButtonStyle.Active : UIButtonStyle.Default,
                "Filter_" + chipId,
                rowH);
            var chipLe = UIHelpers.EnsureLayoutElement(chip.GameObject,
                preferredHeight: rowH,
                minHeight: rowH);
            chipLe.flexibleWidth = 1f;
            chipLe.preferredWidth = -1f;
            if (chip.Label != null)
            {
                chip.Label.fontSize = UITheme.S(FontSmallRef);
                chip.Label.fontStyle = FontStyles.Bold;
            }

            var capturedId = chipId;
            chip.OnClick(() =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                ApplyFilterChip(capturedId);
            });
            _filterChipButtons.Add(chip);
        }

        var row4 = UIFactory.CreateRect("DensityRow", _toolbarFiltersBody);
        UIHelpers.EnsureLayoutElement(row4.gameObject, preferredHeight: rowH, minHeight: rowH);
        UIFactory.AddHorizontalLayout(
            row4.gameObject,
            UITheme.S(8f),
            new RectOffset(0, 0, 0, 0));

        _groupByAuthorToggle = UIToggle.Create(
            row4,
            "Group by author",
            SparrohPlugin.GroupModsByAuthor?.Value ?? false,
            val =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                if (SparrohPlugin.GroupModsByAuthor != null &&
                    SparrohPlugin.GroupModsByAuthor.Value != val)
                {
                    SparrohPlugin.GroupModsByAuthor.Value = val;
                    SparrohPlugin.GroupModsByAuthor.ConfigFile.Save();
                }

                RefreshMods(true);
            },
            "GroupByAuthorToggle");
        var groupLe = UIHelpers.EnsureLayoutElement(_groupByAuthorToggle.GameObject,
            UITheme.S(150f),
            rowH);
        groupLe.flexibleWidth = 1f;
        if (_groupByAuthorToggle.Label != null)
            _groupByAuthorToggle.Label.fontSize = UITheme.S(FontSmallRef);

        _expandCollapseAllBtn = UIButton.Create(
            row4,
            "Collapse all",
            () =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                ToggleExpandCollapseAll();
            },
            UIButtonStyle.Default,
            "ExpandCollapseAll",
            rowH);
        var expLe = UIHelpers.EnsureLayoutElement(_expandCollapseAllBtn.GameObject,
            UITheme.S(120f),
            rowH);
        expLe.flexibleWidth = 0f;
        if (_expandCollapseAllBtn.Label != null)
        {
            _expandCollapseAllBtn.Label.fontSize = UITheme.S(FontSmallRef);
            _expandCollapseAllBtn.Label.fontStyle = FontStyles.Bold;
        }

        ApplyToolbarFiltersCollapsed(false);
    }

    private static void SetToolbarFiltersCollapsed(bool collapsed, bool persist)
    {
        _toolbarFiltersCollapsed = collapsed;
        ApplyToolbarFiltersCollapsed(persist);
    }

    private static void ApplyToolbarFiltersCollapsed(bool persist)
    {
        if (_toolbarFiltersBody != null)
            _toolbarFiltersBody.gameObject.SetActive(!_toolbarFiltersCollapsed);

        var h = _toolbarFiltersCollapsed ? _toolbarHCollapsed : _toolbarHExpanded;
        if (_toolbarRoot != null)
            UIHelpers.SetTopStretch(_toolbarRoot, h);

        if (_toolbarScrollRt != null)
            _toolbarScrollRt.offsetMax = new Vector2(_toolbarScrollRt.offsetMax.x, -h);

        if (_toolbarFiltersToggleBtn != null)
            _toolbarFiltersToggleBtn.SetText(_toolbarFiltersCollapsed ? "+" : "-");

        if (_toolbarFiltersCollapsed && _sortDropdown != null)
        {
            _sortDropdown.CloseList();
            UnregisterDropdown(_sortDropdown);
        }

        if (persist && SparrohPlugin.ToolbarFiltersCollapsed != null &&
            SparrohPlugin.ToolbarFiltersCollapsed.Value != _toolbarFiltersCollapsed)
        {
            SparrohPlugin.ToolbarFiltersCollapsed.Value = _toolbarFiltersCollapsed;
            SparrohPlugin.ToolbarFiltersCollapsed.ConfigFile.Save();
        }
    }

    private static void SyncToolbarFromConfig()
    {
        _suppressToolbarCallbacks = true;
        try
        {
            if (_sortDropdown != null)
            {
                var idx = IndexOfSortMode(SparrohPlugin.ModSortMode?.Value);
                _sortDropdown.Select(idx, false);
            }

            if (_hideEmptyToggle != null && SparrohPlugin.HideModsWithoutConfig != null)
                _hideEmptyToggle.IsOn = SparrohPlugin.HideModsWithoutConfig.Value;

            if (_groupByAuthorToggle != null && SparrohPlugin.GroupModsByAuthor != null)
                _groupByAuthorToggle.IsOn = SparrohPlugin.GroupModsByAuthor.Value;

            RefreshFilterChipStyles(NormalizeFilter(SparrohPlugin.ModListFilter?.Value));
            UpdateExpandCollapseAllLabel();

            var collapsed = SparrohPlugin.ToolbarFiltersCollapsed?.Value ?? false;
            if (collapsed != _toolbarFiltersCollapsed)
                SetToolbarFiltersCollapsed(collapsed, false);
            else
                ApplyToolbarFiltersCollapsed(false);
        }
        finally
        {
            _suppressToolbarCallbacks = false;
        }
    }

    private static void ApplyFilterChip(string filterId)
    {
        filterId = NormalizeFilter(filterId);
        if (SparrohPlugin.ModListFilter != null &&
            !string.Equals(SparrohPlugin.ModListFilter.Value, filterId, StringComparison.OrdinalIgnoreCase))
        {
            SparrohPlugin.ModListFilter.Value = filterId;
            SparrohPlugin.ModListFilter.ConfigFile.Save();
        }

        RefreshFilterChipStyles(filterId);
        RefreshMods(true);
    }

    private static void RefreshFilterChipStyles(string activeFilter)
    {
        activeFilter = NormalizeFilter(activeFilter);
        for (var i = 0; i < _filterChipButtons.Count && i < FilterChipIds.Length; i++)
        {
            var on = string.Equals(FilterChipIds[i], activeFilter, StringComparison.OrdinalIgnoreCase);
            _filterChipButtons[i].SetStyle(on ? UIButtonStyle.Active : UIButtonStyle.Default);
        }
    }

    private static int IndexOfSortMode(string mode)
    {
        if (string.IsNullOrEmpty(mode))
            return 0;
        for (var i = 0; i < SortModeIds.Length; i++)
            if (string.Equals(SortModeIds[i], mode, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    private static string NormalizeFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return "All";
        for (var i = 0; i < FilterChipIds.Length; i++)
            if (string.Equals(FilterChipIds[i], filter, StringComparison.OrdinalIgnoreCase))
                return FilterChipIds[i];
        return "All";
    }

    private static string GetModKey(ModInfo mod)
    {
        if (!string.IsNullOrEmpty(mod.GUID))
            return mod.GUID;
        return mod.Name ?? "";
    }

    private static string GetAuthorGroup(ModInfo mod)
    {
        var guid = mod.GUID;
        if (string.IsNullOrEmpty(guid))
            return "Other";
        var dot = guid.IndexOf('.');
        if (dot <= 0)
            return "Other";
        return guid.Substring(0, dot);
    }

    private static void LoadCollapsedFromConfig()
    {
        _collapsedMods.Clear();
        var raw = SparrohPlugin.CollapsedMods?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return;
        foreach (var part in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var key = part.Trim();
            if (key.Length > 0)
                _collapsedMods.Add(key);
        }
    }

    private static void SaveCollapsedToConfig()
    {
        if (SparrohPlugin.CollapsedMods == null)
            return;
        var value = _collapsedMods.Count == 0
            ? ""
            : string.Join(",", _collapsedMods);
        if (SparrohPlugin.CollapsedMods.Value == value)
            return;
        SparrohPlugin.CollapsedMods.Value = value;
        SparrohPlugin.CollapsedMods.ConfigFile.Save();
    }

    private static bool IsModExpanded(string key)
    {
        return !_collapsedMods.Contains(key);
    }

    private static void SetModExpanded(string key, bool expanded)
    {
        if (string.IsNullOrEmpty(key))
            return;
        if (expanded)
            _collapsedMods.Remove(key);
        else
            _collapsedMods.Add(key);
        SaveCollapsedToConfig();
    }

    private static void ApplyBlockExpanded(ModBlockState block, bool expanded, bool persist)
    {
        if (block == null)
            return;
        block.IsExpanded = expanded;
        if (block.Body != null)
            block.Body.SetActive(expanded);
        if (block.Chevron != null)
            block.Chevron.text = expanded ? "-" : "+";

        if (persist)
            SetModExpanded(block.Key, expanded);

        SyncStickyTitleForBlock(block);
    }

    private static string FormatStickyTitle(ModBlockState block)
    {
        if (block == null)
            return "";
        return (block.IsExpanded ? "- " : "+ ") + (block.TitleRichText ?? "");
    }

    private static void SyncStickyTitleForBlock(ModBlockState block)
    {
        if (block == null || block.TitleRect == null)
            return;
        _stickyTitles?.UpdateTitle(block.TitleRect, FormatStickyTitle(block));
    }

    private static void ToggleModBlock(ModBlockState block)
    {
        if (block == null)
            return;
        ClearActiveEditing();
        ApplyBlockExpanded(block, !block.IsExpanded, true);
        _stickyTitles?.Refresh();
        UpdateExpandCollapseAllLabel();
    }

    private static ModBlockState FindModBlock(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        for (var i = 0; i < _modBlocks.Count; i++)
        {
            var block = _modBlocks[i];
            if (block != null && string.Equals(block.Key, key, StringComparison.OrdinalIgnoreCase))
                return block;
        }

        return null;
    }

    internal static void ToggleModBlockByKey(string key)
    {
        ToggleModBlock(FindModBlock(key));
    }

    private static void ToggleExpandCollapseAll()
    {
        var anyExpanded = false;
        for (var i = 0; i < _modBlocks.Count; i++)
            if (_modBlocks[i] != null && _modBlocks[i].IsExpanded)
            {
                anyExpanded = true;
                break;
            }

        var expand = !anyExpanded;
        for (var i = 0; i < _modBlocks.Count; i++)
        {
            var block = _modBlocks[i];
            if (block == null)
                continue;
            ApplyBlockExpanded(block, expand, false);
            if (expand)
                _collapsedMods.Remove(block.Key);
            else
                _collapsedMods.Add(block.Key);
        }

        SaveCollapsedToConfig();
        UpdateExpandCollapseAllLabel();
        _stickyTitles?.Refresh();
    }

    private static void UpdateExpandCollapseAllLabel()
    {
        if (_expandCollapseAllBtn?.Label == null)
            return;
        var anyExpanded = false;
        if (_modBlocks.Count == 0)
            anyExpanded = _collapsedMods.Count == 0;
        else
            for (var i = 0; i < _modBlocks.Count; i++)
                if (_modBlocks[i] != null && _modBlocks[i].IsExpanded)
                {
                    anyExpanded = true;
                    break;
                }

        _expandCollapseAllBtn.SetText(anyExpanded ? "Collapse all" : "Expand all");
    }

    private static void CreateGroupHeader(Transform parent, string groupName)
    {
        var bar = UIFactory.CreateImage("Group_" + groupName, parent, UIColors.SectionBar, false);
        UIFactory.ApplyWhiteSprite(bar);
        UIHelpers.EnsureLayoutElement(bar.gameObject,
            preferredHeight: UITheme.S(28f),
            minHeight: UITheme.S(28f));

        var label = string.IsNullOrEmpty(groupName) ? "Other" : groupName;
        var tmp = UIFactory.CreateTmp(
            "Text",
            bar.rectTransform,
            RichText.Bold(label),
            UITheme.S(FontSectionRef),
            UIColors.TextSecondary,
            TextAlignmentOptions.MidlineLeft);
        UIHelpers.SetFillParent(tmp.rectTransform, UITheme.S(8f));
    }

    private static StickyModTitleController CreateStickyModTitleOverlay(UIScrollView scrollView)
    {
        var headerH = UITheme.S(44f);

        var stickyImg = UIFactory.CreateImage(
            "StickyModTitle",
            scrollView.Viewport,
            UIColors.TitleBar);
        UIFactory.ApplyWhiteSprite(stickyImg);

        var stickyRt = stickyImg.rectTransform;
        UIHelpers.SetTopStretch(stickyRt, headerH);
        stickyRt.SetAsLastSibling();

        var accent = UIFactory.CreateImage("Accent", stickyRt, UIColors.BorderAccent, false);
        UIFactory.ApplyWhiteSprite(accent);
        var accentRt = accent.rectTransform;
        accentRt.anchorMin = new Vector2(0f, 0f);
        accentRt.anchorMax = new Vector2(1f, 0f);
        accentRt.pivot = new Vector2(0.5f, 0f);
        accentRt.sizeDelta = new Vector2(0f, UITheme.S(2f));
        accentRt.anchoredPosition = Vector2.zero;

        var stickyTmp = UIFactory.CreateTmp(
            "Text",
            stickyRt,
            "",
            UITheme.S(FontModTitleRef),
            UIColors.TextPrimary,
            TextAlignmentOptions.MidlineLeft);
        stickyTmp.fontStyle = FontStyles.Bold;
        stickyTmp.raycastTarget = false;
        UIHelpers.SetFillParent(stickyTmp.rectTransform, UITheme.S(10f));

        stickyImg.gameObject.SetActive(false);

        var controller = scrollView.GameObject.AddComponent<StickyModTitleController>();
        controller.Initialize(scrollView.ScrollRect, stickyRt, stickyTmp, headerH);

        var stickyClick = stickyImg.gameObject.AddComponent<ClickVsDragToggle>();
        stickyClick.Initialize(
            true,
            ClickDragThresholdPx,
            _ => controller.ToggleActiveMod());

        return controller;
    }

    public static void RefreshMods()
    {
        RefreshMods(false);
    }

    public static void RefreshMods(bool resetScroll, bool preserveSearchFocus = false)
    {
        if (_window == null || _window.Content == null)
            return;

        if (preserveSearchFocus)
        {
            for (var i = _openDropdowns.Count - 1; i >= 0; i--)
            {
                var dd = _openDropdowns[i];
                if (dd != null)
                    dd.CloseList();
            }

            _openDropdowns.Clear();
        }
        else
        {
            ClearActiveEditing();
        }

        _stickyTitles?.Clear();
        _modBlocks.Clear();
        UIHelpers.DestroyChildren(_window.Content);

        _cfgFilesCache = null;
        LoadCollapsedFromConfig();

        try
        {
            var source = ModManager.Mods;
            if (source == null)
            {
                ShowEmptyState("No mods loaded");
                _stickyTitles?.Refresh();
                UpdateExpandCollapseAllLabel();
                if (resetScroll)
                    ResetScrollToTop();
                return;
            }

            var visible = BuildVisibleMods(source);
            if (visible.Count == 0)
            {
                ShowEmptyState("No mods match");
                _stickyTitles?.Refresh();
                UpdateExpandCollapseAllLabel();
                if (resetScroll)
                    ResetScrollToTop();
                if (preserveSearchFocus)
                    RestoreSearchFocus();
                return;
            }

            var groupByAuthor = SparrohPlugin.GroupModsByAuthor?.Value ?? false;
            var first = true;

            if (groupByAuthor)
            {
                var byGroup = new SortedDictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < visible.Count; i++)
                {
                    var g = GetAuthorGroup(visible[i]);
                    if (!byGroup.TryGetValue(g, out var bucket))
                    {
                        bucket = new List<ModInfo>();
                        byGroup[g] = bucket;
                    }

                    bucket.Add(visible[i]);
                }

                foreach (var kvp in byGroup)
                {
                    if (!first)
                        UISeparator.Create(_window.Content);
                    first = false;

                    CreateGroupHeader(_window.Content, kvp.Key);
                    for (var i = 0; i < kvp.Value.Count; i++)
                        try
                        {
                            if (i > 0)
                                UISeparator.Create(_window.Content);
                            CreateModConfig(kvp.Value[i], _window.Content);
                        }
                        catch (Exception e)
                        {
                            SparrohPlugin.Logger.LogError(
                                $"Error creating config for mod {kvp.Value[i].Name}: {e.Message}");
                        }
                }
            }
            else
            {
                foreach (var mod in visible)
                    try
                    {
                        if (!first)
                            UISeparator.Create(_window.Content);
                        first = false;

                        CreateModConfig(mod, _window.Content);
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

        _stickyTitles?.Refresh();
        UpdateExpandCollapseAllLabel();

        if (resetScroll)
            ResetScrollToTop();

        if (preserveSearchFocus)
            RestoreSearchFocus();
    }

    private static void ShowEmptyState(string message)
    {
        var empty = UIText.Create(
            _window.Content,
            "EmptyState",
            message,
            UITheme.S(FontBodyRef),
            UIColors.TextMuted,
            TextAlignmentOptions.Center);
        UIHelpers.EnsureLayoutElement(empty.GameObject,
            preferredHeight: UITheme.S(48f),
            minHeight: UITheme.S(48f));
    }

    private static void ResetScrollToTop()
    {
        if (_window?.ScrollView?.ScrollRect == null)
            return;
        var sr = _window.ScrollView.ScrollRect;
        sr.velocity = Vector2.zero;
        sr.verticalNormalizedPosition = 1f;
    }

    private static void RestoreSearchFocus()
    {
        if (_searchField?.Input == null)
            return;
        ActivateInput(_searchField.Input);
    }

    private static List<ModInfo> BuildVisibleMods(IReadOnlyList<ModInfo> source)
    {
        var query = (_searchQuery ?? "").Trim();
        var hideEmpty = SparrohPlugin.HideModsWithoutConfig?.Value ?? true;
        var filter = NormalizeFilter(SparrohPlugin.ModListFilter?.Value);
        var sortMode = SparrohPlugin.ModSortMode?.Value ?? "Alphabetical";

        var list = new List<ModInfo>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var mod = source[i];

            if (!ModMatchesSearch(mod, query))
                continue;

            if (filter == "Sandbox" && !mod.IsSandbox)
                continue;
            if (filter == "ClientSide" && !mod.IsClientSide)
                continue;

            if (hideEmpty && !ModHasConfig(mod))
                continue;

            list.Add(mod);
        }

        SortMods(list, sortMode);
        return list;
    }

    private static bool ModMatchesSearch(ModInfo mod, string query)
    {
        if (string.IsNullOrEmpty(query))
            return true;

        if (!string.IsNullOrEmpty(mod.Name) &&
            mod.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (!string.IsNullOrEmpty(mod.GUID) &&
            mod.GUID.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private static void SortMods(List<ModInfo> list, string mode)
    {
        if (list == null || list.Count <= 1)
            return;

        mode = mode ?? "Alphabetical";

        if (string.Equals(mode, "LoadOrder", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(mode, "AlphabeticalDesc", StringComparison.OrdinalIgnoreCase))
        {
            list.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (string.Equals(mode, "SandboxFirst", StringComparison.OrdinalIgnoreCase))
        {
            list.Sort((a, b) =>
            {
                var sandbox = b.IsSandbox.CompareTo(a.IsSandbox);
                if (sandbox != 0)
                    return sandbox;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return;
        }

        if (string.Equals(mode, "HasConfigFirst", StringComparison.OrdinalIgnoreCase))
        {
            list.Sort((a, b) =>
            {
                var ha = ModHasConfig(a);
                var hb = ModHasConfig(b);
                var cfg = hb.CompareTo(ha);
                if (cfg != 0)
                    return cfg;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return;
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetCfgFiles()
    {
        if (_cfgFilesCache != null)
            return _cfgFilesCache;

        try
        {
            if (Directory.Exists(Paths.ConfigPath))
                _cfgFilesCache = Directory.GetFiles(Paths.ConfigPath, "*.cfg");
            else
                _cfgFilesCache = Array.Empty<string>();
        }
        catch (Exception e)
        {
            SparrohPlugin.Logger.LogWarning($"Could not enumerate config files: {e.Message}");
            _cfgFilesCache = Array.Empty<string>();
        }

        return _cfgFilesCache;
    }

    private static bool TryFindConfigPath(ModInfo mod, out string configPath)
    {
        configPath = null;
        if (string.IsNullOrEmpty(mod.Name))
            return false;

        var nameLower = mod.Name.ToLowerInvariant();
        foreach (var file in GetCfgFiles())
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName != null && fileName.ToLowerInvariant().Contains(nameLower))
            {
                configPath = file;
                return true;
            }
        }

        return false;
    }

    private static bool ModHasConfig(ModInfo mod)
    {
        return TryFindConfigPath(mod, out _);
    }

    private static void ApplySettingValue(
        ModInfo mod,
        string configPath,
        string section,
        string key,
        string value,
        ConfigEntry<string> fileFallback)
    {
        if (TrySetLiveConfigValue(mod, configPath, section, key, value))
            return;

        if (fileFallback != null)
        {
            fileFallback.Value = value ?? "";
            fileFallback.ConfigFile.Save();
        }
    }

    private static void ApplySettingValueLiveOnly(
        ModInfo mod,
        string configPath,
        string section,
        string key,
        string value,
        ConfigEntry<string> fileFallback)
    {
        try
        {
            var plugin = FindPluginForConfig(mod, configPath);
            if (plugin?.Config != null)
            {
                var live = FindLiveEntry(plugin.Config, section, key);
                if (live != null)
                {
                    live.SetSerializedValue(value ?? "");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogWarning(
                $"Live config set failed for [{section}] {key}: {ex.Message}");
        }

        if (fileFallback != null)
            fileFallback.Value = value ?? "";
    }

    private static bool TrySetLiveConfigValue(
        ModInfo mod,
        string configPath,
        string section,
        string key,
        string value)
    {
        try
        {
            var plugin = FindPluginForConfig(mod, configPath);
            if (plugin?.Config == null)
                return false;

            var live = FindLiveEntry(plugin.Config, section, key);
            if (live == null)
                return false;

            live.SetSerializedValue(value ?? "");
            plugin.Config.Save();
            return true;
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogWarning(
                $"Live config set failed for [{section}] {key}: {ex.Message}");
            return false;
        }
    }

    private static BaseUnityPlugin FindPluginForConfig(ModInfo mod, string configPath)
    {
        if (!string.IsNullOrEmpty(mod.GUID) &&
            Chainloader.PluginInfos != null &&
            Chainloader.PluginInfos.TryGetValue(mod.GUID, out var byGuid) &&
            byGuid?.Instance is BaseUnityPlugin fromGuid)
            return fromGuid;

        if (Chainloader.PluginInfos == null || string.IsNullOrEmpty(configPath))
            return null;

        foreach (var kv in Chainloader.PluginInfos)
        {
            if (kv.Value?.Instance is not BaseUnityPlugin plugin || plugin.Config == null)
                continue;

            var path = plugin.Config.ConfigFilePath;
            if (!string.IsNullOrEmpty(path) &&
                string.Equals(path, configPath, StringComparison.OrdinalIgnoreCase))
                return plugin;
        }

        return null;
    }

    private static ConfigEntryBase FindLiveEntry(ConfigFile config, string section, string key)
    {
        if (config == null || string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key))
            return null;

        foreach (var kv in config)
        {
            if (kv.Key == null || kv.Value == null)
                continue;
            if (!string.Equals(kv.Key.Section, section, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(kv.Key.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;
            return kv.Value;
        }

        return null;
    }

    private static string GetLiveOrFileValue(
        ModInfo mod,
        string configPath,
        string section,
        string key,
        string fileValue)
    {
        try
        {
            var plugin = FindPluginForConfig(mod, configPath);
            var live = plugin != null
                ? FindLiveEntry(plugin.Config, section, key)
                : null;
            if (live != null)
            {
                var serialized = live.GetSerializedValue();
                if (serialized != null)
                    return serialized;
            }
        }
        catch
        {
        }

        return fileValue ?? "";
    }

    private static bool TryParseFloat(string s, out float value)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               || float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseAcceptableRangeComment(string comment, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (string.IsNullOrEmpty(comment))
            return false;


        const string prefix = "Acceptable value range:";
        if (!comment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = comment.Substring(prefix.Length).Trim();
        var fromIdx = rest.IndexOf("From ", StringComparison.OrdinalIgnoreCase);
        var toIdx = rest.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        if (fromIdx < 0 || toIdx < 0 || toIdx <= fromIdx)
            return false;

        var minStr = rest.Substring(fromIdx + 5, toIdx - (fromIdx + 5)).Trim();
        var maxStr = rest.Substring(toIdx + 4).Trim();
        if (!TryParseFloat(minStr, out min) || !TryParseFloat(maxStr, out max))
            return false;
        if (min > max)
        {
            var tmp = min;
            min = max;
            max = tmp;
        }

        return !Mathf.Approximately(min, max);
    }

    private static bool TryGetFloatRange(
        ModInfo mod,
        string configPath,
        string section,
        string key,
        EntryMeta fileMeta,
        out float min,
        out float max)
    {
        min = 0f;
        max = 0f;

        try
        {
            var plugin = FindPluginForConfig(mod, configPath);
            var live = plugin != null ? FindLiveEntry(plugin.Config, section, key) : null;
            if (live?.Description?.AcceptableValues != null)
            {
                var av = live.Description.AcceptableValues;
                var avType = av.GetType();
                if (avType.IsGenericType &&
                    avType.GetGenericTypeDefinition() == typeof(AcceptableValueRange<>))
                {
                    var minObj = avType.GetProperty("MinValue")?.GetValue(av);
                    var maxObj = avType.GetProperty("MaxValue")?.GetValue(av);
                    if (minObj != null && maxObj != null)
                    {
                        min = Convert.ToSingle(minObj, CultureInfo.InvariantCulture);
                        max = Convert.ToSingle(maxObj, CultureInfo.InvariantCulture);
                        if (min > max)
                        {
                            var tmp = min;
                            min = max;
                            max = tmp;
                        }

                        return !Mathf.Approximately(min, max);
                    }
                }
            }
        }
        catch
        {
        }

        if (fileMeta?.RangeMin != null && fileMeta.RangeMax != null)
        {
            min = fileMeta.RangeMin.Value;
            max = fileMeta.RangeMax.Value;
            return !Mathf.Approximately(min, max);
        }

        return false;
    }

    private static Dictionary<string, List<(string entry, EntryMeta meta)>> ParseModConfig(string configPath)
    {
        try
        {
            var entries = new Dictionary<string, List<(string, EntryMeta)>>();
            var currentSection = "";
            var pendingComments = new List<string>();

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
                        entries[currentSection] = new List<(string, EntryMeta)>();
                    pendingComments.Clear();
                }
                else if (line.Contains("="))
                {
                    if (!entries.ContainsKey(currentSection))
                        entries[currentSection] = new List<(string, EntryMeta)>();

                    var meta = new EntryMeta();
                    foreach (var comment in pendingComments)
                        if (comment.StartsWith("Acceptable values:", StringComparison.OrdinalIgnoreCase))
                        {
                            meta.Options = comment.Substring("Acceptable values:".Length).Split(',')
                                .Select(s => s.Trim())
                                .Where(s => s.Length > 0)
                                .ToArray();
                        }
                        else if (TryParseAcceptableRangeComment(comment, out var rMin, out var rMax))
                        {
                            meta.RangeMin = rMin;
                            meta.RangeMax = rMax;
                        }

                    entries[currentSection].Add((line.Trim(), meta));
                    pendingComments.Clear();
                }
            }

            return entries;
        }
        catch (Exception e)
        {
            SparrohPlugin.Logger.LogError($"Error parsing config file '{configPath}': {e.Message}");
            return new Dictionary<string, List<(string, EntryMeta)>>();
        }
    }


    private static void CreateFloatSlider(
        RectTransform valueRt,
        float min,
        float max,
        float initial,
        string key,
        ModInfo modLocal,
        string configPath,
        string sectionName,
        ConfigEntry<string> configEntry)
    {
        initial = Mathf.Clamp(initial, min, max);


        var slider = UISlider.Create(
            valueRt,
            " ",
            min,
            max,
            initial,
            null,
            "Slider_" + key,
            false,
            "G4");

        UIHelpers.SetFillParent(slider.Rect);

        if (slider.Label != null)
            slider.Label.gameObject.SetActive(false);

        if (slider.ValueLabel != null)
        {
            slider.ValueLabel.fontSize = UITheme.S(FontSmallRef);
            slider.ValueLabel.fontStyle = FontStyles.Bold;
            slider.ValueLabel.alignment = TextAlignmentOptions.Right;
        }

        var sliderSection = sectionName;
        var sliderKey = key;
        var lastLive = initial;

        slider.OnChanged(v =>
        {
            lastLive = v;
            ApplySettingValueLiveOnly(
                modLocal,
                configPath,
                sliderSection,
                sliderKey,
                v.ToString(CultureInfo.InvariantCulture),
                configEntry);
        });

        var saveOnUp = slider.Slider.gameObject.AddComponent<SliderSaveOnPointerUp>();
        saveOnUp.Initialize(() =>
        {
            ApplySettingValue(
                modLocal,
                configPath,
                sliderSection,
                sliderKey,
                lastLive.ToString(CultureInfo.InvariantCulture),
                configEntry);
        });
    }

    private static void CreateModConfig(ModInfo mod, Transform parent)
    {
        try
        {
            var modLocal = mod;
            var modKey = GetModKey(modLocal);
            var expanded = IsModExpanded(modKey);

            var blockRoot = UIFactory.CreateRect("ModBlock_" + modLocal.Name, parent);
            UIFactory.AddVerticalLayout(
                blockRoot.gameObject,
                UITheme.S(UITheme.SpacingTight),
                new RectOffset(0, 0, 0, 0));

            UIFactory.AddContentSizeFitter(blockRoot.gameObject);

            var titleBar = UIFactory.CreateImage(modLocal.Name + " Title", blockRoot, UIColors.TitleBar);
            UIFactory.ApplyWhiteSprite(titleBar);
            UIHelpers.EnsureLayoutElement(titleBar.gameObject,
                preferredHeight: UITheme.S(44f),
                minHeight: UITheme.S(44f));

            UIFactory.AddHorizontalLayout(
                titleBar.gameObject,
                UITheme.S(6f),
                UITheme.ScaledPadding(10, 10, 4, 4));

            var chevronTmp = UIFactory.CreateTmp(
                "Chevron",
                titleBar.rectTransform,
                expanded ? "-" : "+",
                UITheme.S(FontModTitleRef),
                UIColors.TextSecondary,
                TextAlignmentOptions.Center);
            chevronTmp.fontStyle = FontStyles.Bold;
            chevronTmp.raycastTarget = false;
            var chevLe = UIHelpers.EnsureLayoutElement(chevronTmp.gameObject,
                UITheme.S(28f),
                UITheme.S(36f));
            chevLe.flexibleWidth = 0f;

            var title = RichText.Bold(modLocal.Name);
            if (modLocal.IsSandbox)
                title += " " + RichText.Size(
                    "[" + RichText.Italic(RichText.Colorize("Sandbox", UIColors.Rose)) + "]",
                    55);

            var titleTmp = UIFactory.CreateTmp(
                "Text",
                titleBar.rectTransform,
                title,
                UITheme.S(FontModTitleRef),
                UIColors.TextPrimary,
                TextAlignmentOptions.MidlineLeft);
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.raycastTarget = false;
            var titleLe = UIHelpers.EnsureLayoutElement(titleTmp.gameObject,
                preferredHeight: UITheme.S(36f));
            titleLe.flexibleWidth = 1f;

            var bodyRt = UIFactory.CreateRect("Body", blockRoot);
            UIFactory.AddVerticalLayout(
                bodyRt.gameObject,
                UITheme.S(UITheme.SpacingTight),
                new RectOffset(0, 0, 0, 0));
            UIFactory.AddContentSizeFitter(bodyRt.gameObject);
            bodyRt.gameObject.SetActive(expanded);

            var blockState = new ModBlockState
            {
                Key = modKey,
                Body = bodyRt.gameObject,
                Chevron = chevronTmp,
                TitleRect = titleBar.rectTransform,
                TitleRichText = title,
                IsExpanded = expanded
            };
            _modBlocks.Add(blockState);

            _stickyTitles?.Register(titleBar.rectTransform, FormatStickyTitle(blockState), modKey);

            var titleClick = titleBar.gameObject.AddComponent<ClickVsDragToggle>();
            titleClick.Initialize(
                expanded,
                ClickDragThresholdPx,
                _ => ToggleModBlock(blockState));

            Transform bodyParent = bodyRt;

            if (!TryFindConfigPath(modLocal, out var configPath) || !File.Exists(configPath))
            {
                var noConfig = UIText.Create(
                    bodyParent,
                    "NoConfig",
                    "(No config found)",
                    UITheme.S(FontBodyRef),
                    UIColors.TextMuted,
                    TextAlignmentOptions.Center);
                UIHelpers.EnsureLayoutElement(noConfig.GameObject,
                    preferredHeight: UITheme.S(28f),
                    minHeight: UITheme.S(28f));
                return;
            }

            var configFile = new ConfigFile(configPath, true);
            if (!_cachedMeta.ContainsKey(configPath))
            {
                var parsed = ParseModConfig(configPath);
                _cachedMeta[configPath] = new Dictionary<string, EntryMeta>();
                foreach (var sect in parsed)
                {
                    var sectKey = sect.Key.Trim('[', ']');
                    foreach (var (entry, meta) in sect.Value)
                    {
                        var k = entry.Substring(0, entry.IndexOf('=')).Trim();
                        _cachedMeta[configPath][sectKey + "." + k] = meta;
                    }
                }
            }

            foreach (var section in ParseModConfig(configPath))
            {
                var sectionLabel = UIWindow.CreateSectionHeader(bodyParent, section.Key);

                if (sectionLabel?.Tmp != null)
                {
                    sectionLabel.Tmp.fontSize = UITheme.S(FontSectionRef);
                    sectionLabel.Tmp.fontStyle = FontStyles.Bold;
                }

                foreach (var (fullEntry, entryMeta) in section.Value)
                {
                    var key = fullEntry.Substring(0, fullEntry.IndexOf('=')).Trim();
                    var value = fullEntry.Substring(fullEntry.IndexOf('=') + 1).Trim();
                    var sectionName = section.Key.Trim('[', ']');

                    var configEntry = configFile.Bind(sectionName, key, value);

                    var meta = entryMeta;
                    var cacheKey = sectionName + "." + key;
                    if (_cachedMeta.ContainsKey(configPath) &&
                        _cachedMeta[configPath].ContainsKey(cacheKey))
                        meta = _cachedMeta[configPath][cacheKey];

                    var options = meta?.Options;

                    var rawEntryValue = GetLiveOrFileValue(modLocal, configPath, sectionName, key, value);
                    var entryType = typeof(string);

                    if (bool.TryParse(rawEntryValue, out _))
                        entryType = typeof(bool);
                    else if (int.TryParse(rawEntryValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                             || int.TryParse(rawEntryValue, out _))
                        entryType = typeof(int);
                    else if (TryParseFloat(rawEntryValue, out _))
                        entryType = typeof(float);

                    var wantSlider = entryType == typeof(float)
                                     && (SparrohPlugin.UseFloatSliders?.Value ?? false);
                    var sliderMin = 0f;
                    var sliderMax = 1f;
                    var useFloatSlider = false;
                    if (wantSlider &&
                        TryGetFloatRange(modLocal, configPath, sectionName, key, meta,
                            out sliderMin, out sliderMax))
                        useFloatSlider = true;


                    var rowH = UITheme.S(useFloatSlider ? 72f : 58f);

                    var entryBg = UIFactory.CreateImage("Entry_" + key, bodyParent, UIColors.EntryBg);

                    UIFactory.ApplyWhiteSprite(entryBg);
                    UIHelpers.EnsureLayoutElement(entryBg.gameObject,
                        preferredHeight: rowH,
                        minHeight: rowH);

                    UIFactory.AddHorizontalLayout(
                        entryBg.gameObject,
                        UITheme.S(UITheme.SpacingNormal),
                        UITheme.ScaledPadding(12, 12, 6, 6),
                        TextAnchor.MiddleLeft,
                        true,
                        true);

                    var labelTmp = UIFactory.CreateTmp(
                        "Label",
                        entryBg.rectTransform,
                        key,
                        UITheme.S(FontLabelRef),
                        UIColors.TextPrimary,
                        TextAlignmentOptions.MidlineLeft,
                        true);
                    labelTmp.fontStyle = FontStyles.Bold;
                    var labelLe = UIHelpers.EnsureLayoutElement(labelTmp.gameObject,
                        UITheme.S(200f),
                        rowH - UITheme.S(12f));
                    labelLe.flexibleWidth = 1f;
                    labelLe.minWidth = 0f;
                    labelLe.preferredWidth = -1f;

                    var valueRt = UIFactory.CreateRect("Value", entryBg.rectTransform);
                    var valueLe = UIHelpers.EnsureLayoutElement(valueRt.gameObject,
                        UITheme.S(200f),
                        useFloatSlider ? rowH - UITheme.S(12f) : UITheme.S(36f),
                        useFloatSlider ? UITheme.S(40f) : UITheme.S(32f));
                    valueLe.flexibleWidth = 1f;
                    valueLe.minWidth = 0f;
                    valueLe.preferredWidth = -1f;

                    if (useFloatSlider)
                    {
                        TryParseFloat(rawEntryValue, out var initialF);
                        CreateFloatSlider(
                            valueRt,
                            sliderMin,
                            sliderMax,
                            initialF,
                            key,
                            modLocal,
                            configPath,
                            sectionName,
                            configEntry);
                    }
                    else if (entryType == typeof(bool))
                    {
                        var isOn = rawEntryValue.Equals("true", StringComparison.OrdinalIgnoreCase);

                        var statusTmp = UIFactory.CreateTmp(
                            "Status",
                            valueRt,
                            isOn ? "ON" : "OFF",
                            UITheme.S(FontLabelRef),
                            isOn ? UIColors.Success : UIColors.Error,
                            TextAlignmentOptions.Center);
                        statusTmp.fontStyle = FontStyles.Bold;
                        UIHelpers.SetFillParent(statusTmp.rectTransform);

                        var toggleImg = valueRt.gameObject.AddComponent<Image>();
                        UIFactory.ApplyWhiteSprite(toggleImg);
                        toggleImg.color = isOn ? UIColors.ToggleOn : UIColors.ToggleOff;
                        toggleImg.raycastTarget = true;

                        var boolSection = sectionName;
                        var boolKey = key;
                        var entryToggle = valueRt.gameObject.AddComponent<ClickVsDragToggle>();
                        entryToggle.Initialize(
                            isOn,
                            ClickDragThresholdPx,
                            val =>
                            {
                                ApplySettingValue(
                                    modLocal,
                                    configPath,
                                    boolSection,
                                    boolKey,
                                    val ? "true" : "false",
                                    configEntry);
                                statusTmp.text = val ? "ON" : "OFF";
                                statusTmp.color = val ? UIColors.Success : UIColors.Error;
                                toggleImg.color = val ? UIColors.ToggleOn : UIColors.ToggleOff;
                            });
                    }
                    else if (options != null && options.Length > 0)
                    {
                        var initial = Array.FindIndex(options,
                            o => string.Equals(o, rawEntryValue, StringComparison.OrdinalIgnoreCase));
                        if (initial < 0)
                            initial = 0;

                        var dropdown = UIDropdown.Create(
                            valueRt,
                            options,
                            initial,
                            null,
                            "Dropdown_" + key);

                        var ddSection = sectionName;
                        var ddKey = key;
                        dropdown.OnChanged((idx, selected) =>
                        {
                            ApplySettingValue(
                                modLocal,
                                configPath,
                                ddSection,
                                ddKey,
                                selected,
                                configEntry);
                            UnregisterDropdown(dropdown);
                        });

                        UIHelpers.SetFillParent(dropdown.Rect);

                        var mainBtn = dropdown.GameObject.GetComponentInChildren<Button>();
                        if (mainBtn != null)
                        {
                            mainBtn.onClick.RemoveAllListeners();
                            mainBtn.onClick.AddListener(() =>
                            {
                                var wasOpen = dropdown.IsOpen;

                                ClearActiveEditing();

                                if (!wasOpen)
                                {
                                    dropdown.OpenList();
                                    RegisterOpenDropdown(dropdown);
                                }
                            });
                        }

                        if (dropdown.Label != null)
                        {
                            dropdown.Label.fontSize = UITheme.S(FontBodyRef);
                            dropdown.Label.fontStyle = FontStyles.Bold;
                        }
                    }
                    else
                    {
                        var isConfigToggleKey = sectionName == "Keybinds" && key == "Toggle Config GUI";
                        var isRepositionKey = sectionName == "Keybinds" && key == "Toggle Hud Reposition";

                        var field = UIInputField.Create(
                            valueRt,
                            rawEntryValue,
                            name: "Input_" + key);

                        UIHelpers.SetFillParent(field.Rect);

                        if (field.TextComponent != null)
                        {
                            field.TextComponent.fontSize = UITheme.S(FontBodyRef);
                            field.TextComponent.fontStyle = FontStyles.Bold;
                            field.TextComponent.color = UIColors.InputText;
                            field.TextComponent.alignment = TextAlignmentOptions.Center;
                        }

                        if (field.Placeholder != null)
                            field.Placeholder.alignment = TextAlignmentOptions.Center;

                        field.Input.pointSize = UITheme.S(FontBodyRef);

                        if (isConfigToggleKey || isRepositionKey)
                        {
                            field.Input.interactable = false;
                            var eventTrigger = field.GameObject.AddComponent<EventTrigger>();
                            var triggerEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                            var bindConfig = isConfigToggleKey;
                            triggerEntry.callback.AddListener(_ =>
                            {
                                if (!SparrohPlugin.IsRebinding && !SparrohPlugin.IsRebindingReposition)
                                {
                                    ClearActiveEditing();
                                    field.Input.interactable = false;
                                    field.Input.text = "Press new key...";
                                    if (bindConfig)
                                    {
                                        SparrohPlugin.IsRebinding = true;
                                        KeyBindInput = field.Input;
                                    }
                                    else
                                    {
                                        SparrohPlugin.IsRebindingReposition = true;
                                        RepositionKeyBindInput = field.Input;
                                    }
                                }
                            });
                            eventTrigger.triggers.Add(triggerEntry);
                        }
                        else
                        {
                            field.Input.interactable = false;
                            var armClick = field.GameObject.AddComponent<SelectToEditInput>();
                            armClick.Initialize(field.Input, ClickDragThresholdPx, () => ActivateInput(field.Input));

                            var inputSection = sectionName;
                            var inputKey = key;
                            field.Input.onEndEdit.AddListener(newVal =>
                            {
                                ApplySettingValue(
                                    modLocal,
                                    configPath,
                                    inputSection,
                                    inputKey,
                                    newVal,
                                    configEntry);
                                field.Input.interactable = false;
                                if (_activeInput == field.Input)
                                    _activeInput = null;
                                if (EventSystem.current != null)
                                    EventSystem.current.SetSelectedGameObject(null);
                            });
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            SparrohPlugin.Logger.LogError($"Error creating mod config for {mod.Name}: {e.Message}");
        }
    }

    private sealed class EntryMeta
    {
        public string[] Options;
        public float? RangeMax;
        public float? RangeMin;
    }

    private sealed class ModBlockState
    {
        public GameObject Body;
        public TextMeshProUGUI Chevron;
        public bool IsExpanded;
        public string Key;
        public RectTransform TitleRect;
        public string TitleRichText;
    }
}