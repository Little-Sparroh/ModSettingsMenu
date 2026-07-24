using System;
using System.Collections.Generic;
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

    // Slightly larger than theme defaults for config readability.
    private const float FontTitleRef = 26f;
    private const float FontModTitleRef = 22f;
    private const float FontSectionRef = 18f;
    private const float FontLabelRef = 17f;
    private const float FontBodyRef = 16f;
    private const float FontSmallRef = 14f;

    private static UIWindow _window;
    private static bool _visible;
    private static bool _cursorHeld;
    private static TMP_InputField _activeInput;
    private static readonly List<UIDropdown> _openDropdowns = new List<UIDropdown>();
    private static StickyModTitleController _stickyTitles;

    // Toolbar (fixed above scroll content)
    private static RectTransform _toolbarRoot;
    private static RectTransform _toolbarFiltersBody;
    private static RectTransform _toolbarScrollRt;
    private static UIInputField _searchField;
    private static UIDropdown _sortDropdown;
    private static UIToggle _hideEmptyToggle;
    private static UIToggle _groupByAuthorToggle;
    private static UIButton _expandCollapseAllBtn;
    private static UIButton _toolbarFiltersToggleBtn;
    private static readonly List<UIButton> _filterChipButtons = new List<UIButton>();
    private static string _searchQuery = "";
    private static bool _suppressToolbarCallbacks;
    private static bool _toolbarFiltersCollapsed;
    private static float _toolbarHExpanded;
    private static float _toolbarHCollapsed;
    private static string[] _cfgFilesCache;


    // Collapse state (keys = GUID or Name). Empty set = all expanded (default).
    private static readonly HashSet<string> _collapsedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ModBlockState> _modBlocks = new List<ModBlockState>();

    private sealed class ModBlockState
    {
        public string Key;
        public GameObject Body;
        public TextMeshProUGUI Chevron;
        public RectTransform TitleRect;
        public string TitleRichText;
        public bool IsExpanded;
    }


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
    public static bool IsVisible => _visible;

    private static Dictionary<string, Dictionary<string, string[]>> _cachedOptions =
        new Dictionary<string, Dictionary<string, string[]>>();


    /// <summary>
    /// Unfocus inputs, close dropdowns, and clear EventSystem selection so scroll/pan
    /// never commits or continues an edit.
    /// </summary>
    internal static void ClearActiveEditing()
    {
        if (_activeInput != null)
        {
            _activeInput.DeactivateInputField();
            _activeInput = null;
        }

        for (int i = _openDropdowns.Count - 1; i >= 0; i--)
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
        input.ActivateInputField();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(input.gameObject);
    }

    public static void Toggle()
    {
        if (_window == null || _window.GameObject == null)
        {
            try
            {
                CreateGUI();
            }
            catch (Exception e)
            {
                SparrohPlugin.Logger.LogError($"Error creating GUI: {e}");
                return;
            }
        }

        if (_visible)
            Hide();
        else
            Show();
    }

    public static void Show()
    {
        if (_window == null || _window.GameObject == null)
        {
            try
            {
                CreateGUI();
            }
            catch (Exception e)
            {
                SparrohPlugin.Logger.LogError($"Error creating GUI: {e}");
                return;
            }
        }

        if (_visible)
            return;

        // Opening config should not stack with HUD reposition mode.
        if (HudRepositionMode.IsActive)
            HudRepositionMode.Exit();

        _visible = true;
        _window.Show();
        HoldCursor();

        // Search is session-only; clear each open so the full list is visible.
        _searchQuery = "";
        if (_searchField != null)
        {
            _suppressToolbarCallbacks = true;
            _searchField.Text = "";
            _suppressToolbarCallbacks = false;
        }

        SyncToolbarFromConfig();
        RefreshMods(resetScroll: true);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }


    /// <summary>Close the config window without toggling (e.g. when entering reposition mode).</summary>
    public static void Hide()
    {
        if (!_visible)
            return;

        _visible = false;
        ClearActiveEditing();
        if (_window != null)
            _window.Hide(invokeClose: false);
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
            scrollable: true,
            closeButton: true,
            sortingOrder: UITheme.WindowSortingOrder + 10);


        _window.OnClose(() =>
        {
            // Close button path — keep state in sync.
            if (!_visible)
                return;
            _visible = false;
            ClearActiveEditing();
            ReleaseCursor();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        });

        // Enlarge + bold the window title; left-align so it doesn't collide with right-side controls.
        if (_window.TitleText != null)
        {
            _window.TitleText.fontSize = UITheme.S(FontTitleRef);
            _window.TitleText.fontStyle = FontStyles.Bold;
            _window.TitleText.color = UIColors.TextPrimary;
            _window.TitleText.alignment = TextAlignmentOptions.Left;
            // Leave room on the right for Reposition + close (X).
            UIHelpers.SetFillParent(_window.TitleText.rectTransform, UITheme.S(8f));
            _window.TitleText.rectTransform.offsetMax = new Vector2(-UITheme.S(200f), -UITheme.S(4f));
            _window.TitleText.rectTransform.offsetMin = new Vector2(UITheme.S(10f), UITheme.S(4f));
        }

        // Reposition HUDs button on the title bar (right side, left of close).
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
                    preferredHeight: UITheme.S(28f));

                var crt = repoBtn.Rect;
                crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
                crt.pivot = new Vector2(1f, 0.5f);
                crt.sizeDelta = new Vector2(UITheme.S(150f), UITheme.S(28f));
                // Leave room for the close (X) button.
                crt.anchoredPosition = new Vector2(-UITheme.S(48f), 0f);

                var le = repoBtn.GameObject.GetComponent<LayoutElement>();
                if (le != null)
                    UnityEngine.Object.Destroy(le);

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


        // Wheel snaps one content row at a time (sticky-aware); clear edits when scrolling/dragging.
        if (_window.ScrollView != null)
        {
            var scroll = _window.ScrollView.ScrollRect;
            scroll.scrollSensitivity = 0f;

            // Sticky mod titles: floating bar over the viewport, pinned while scrolling a mod's settings.
            _stickyTitles = CreateStickyModTitleOverlay(_window.ScrollView);

            var stepScroll = _window.ScrollView.GameObject.AddComponent<ItemStepScrollHandler>();
            stepScroll.Initialize(scroll, _stickyTitles);

            // Fixed toolbar above the scroll viewport (search / sort / filters).
            CreateToolbar(_window.ScrollView);
        }

        // Start hidden until Show/Toggle.
        _window.Hide(invokeClose: false);
        _visible = false;
    }

    /// <summary>
    /// Builds a non-scrolling toolbar (search always visible; sort/filters collapsible)
    /// pinned to the top of the window body, and insets the scroll view below it.
    /// </summary>
    private static void CreateToolbar(UIScrollView scrollView)
    {
        var body = scrollView.Rect.parent as RectTransform;
        if (body == null)
            return;

        float pad = UITheme.S(4f);
        float searchH = UITheme.S(34f);
        float rowH = UITheme.S(30f);
        float gap = UITheme.S(5f);
        // Expanded: search | sort+hide | chips | group+expand
        // Collapsed: search only
        _toolbarHExpanded = pad + searchH + gap + rowH + gap + rowH + gap + rowH + pad;
        _toolbarHCollapsed = pad + searchH + pad;

        _toolbarScrollRt = scrollView.Rect;
        _toolbarFiltersCollapsed = SparrohPlugin.ToolbarFiltersCollapsed?.Value ?? false;
        float initialH = _toolbarFiltersCollapsed ? _toolbarHCollapsed : _toolbarHExpanded;

        // Inset scroll view under the toolbar.
        _toolbarScrollRt.offsetMax = new Vector2(_toolbarScrollRt.offsetMax.x, -initialH);

        var toolbarBg = UIFactory.CreateImage("Toolbar", body, UIColors.Surface, raycast: true);
        UIFactory.ApplyWhiteSprite(toolbarBg);
        _toolbarRoot = toolbarBg.rectTransform;
        UIHelpers.SetTopStretch(_toolbarRoot, initialH, left: 0f, right: 0f, top: 0f);
        _toolbarRoot.SetAsLastSibling();

        // Accent under toolbar (separates from list) — ignore layout so it doesn't steal a row.
        var accent = UIFactory.CreateImage("ToolbarAccent", _toolbarRoot, UIColors.BorderAccent, raycast: false);
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
            UITheme.ScaledPadding(8, 8, 6, 8),
            TextAnchor.UpperLeft,
            controlChildHeight: true,
            expandHeight: false,
            controlChildWidth: true,
            expandWidth: true);

        // ── Row 1: search + filters collapse toggle ──────────────────────
        var searchRow = UIFactory.CreateRect("SearchRow", _toolbarRoot);
        UIHelpers.EnsureLayoutElement(searchRow.gameObject, preferredHeight: searchH, minHeight: searchH);
        UIFactory.AddHorizontalLayout(
            searchRow.gameObject,
            UITheme.S(6f),
            new RectOffset(0, 0, 0, 0),
            TextAnchor.MiddleLeft,
            controlChildWidth: true,
            expandWidth: false,
            controlChildHeight: true,
            expandHeight: true);

        _searchField = UIInputField.Create(
            searchRow,
            "",
            "Search mods…",
            onChanged: query =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                _searchQuery = query ?? "";
                RefreshMods(resetScroll: true, preserveSearchFocus: true);
            },
            name: "SearchField");
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
            onClick: () =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                SetToolbarFiltersCollapsed(!_toolbarFiltersCollapsed, persist: true);
            },
            style: UIButtonStyle.Default,
            name: "ToolbarFiltersToggle",
            preferredHeight: searchH);
        var toggleLe = UIHelpers.EnsureLayoutElement(_toolbarFiltersToggleBtn.GameObject,
            preferredWidth: searchH,
            preferredHeight: searchH,
            minHeight: searchH);
        toggleLe.flexibleWidth = 0f;
        if (_toolbarFiltersToggleBtn.Label != null)
        {
            _toolbarFiltersToggleBtn.Label.fontSize = UITheme.S(FontModTitleRef);
            _toolbarFiltersToggleBtn.Label.fontStyle = FontStyles.Bold;
        }

        // ── Collapsible body: sort / chips / group ───────────────────────
        _toolbarFiltersBody = UIFactory.CreateRect("FiltersBody", _toolbarRoot);
        float filtersBodyH = rowH + gap + rowH + gap + rowH;
        UIHelpers.EnsureLayoutElement(_toolbarFiltersBody.gameObject,
            preferredHeight: filtersBodyH,
            minHeight: filtersBodyH);
        UIFactory.AddVerticalLayout(
            _toolbarFiltersBody.gameObject,
            gap,
            new RectOffset(0, 0, 0, 0),
            TextAnchor.UpperLeft,
            controlChildHeight: true,
            expandHeight: false,
            controlChildWidth: true,
            expandWidth: true);

        // ── Row 2: sort + hide empty ─────────────────────────────────────
        var row2 = UIFactory.CreateRect("SortRow", _toolbarFiltersBody);
        UIHelpers.EnsureLayoutElement(row2.gameObject, preferredHeight: rowH, minHeight: rowH);
        UIFactory.AddHorizontalLayout(
            row2.gameObject,
            UITheme.S(8f),
            new RectOffset(0, 0, 0, 0),
            TextAnchor.MiddleLeft,
            controlChildWidth: true,
            expandWidth: false,
            controlChildHeight: true,
            expandHeight: true);

        int initialSort = IndexOfSortMode(SparrohPlugin.ModSortMode?.Value);
        _sortDropdown = UIDropdown.Create(
            row2,
            SortModeLabels,
            initialSort,
            onChanged: null,
            name: "SortDropdown");
        var sortLe = UIHelpers.EnsureLayoutElement(_sortDropdown.GameObject,
            preferredWidth: UITheme.S(160f),
            preferredHeight: rowH,
            minHeight: rowH);
        sortLe.flexibleWidth = 1f;
        if (_sortDropdown.Label != null)
        {
            _sortDropdown.Label.fontSize = UITheme.S(FontSmallRef);
            _sortDropdown.Label.fontStyle = FontStyles.Bold;
        }

        // Explicit open/close so we track the dropdown like config fields.
        // UIDropdown.OpenList reparents + elevates the list above siblings / scroll masks.
        var sortMainBtn = _sortDropdown.GameObject.GetComponentInChildren<Button>();
        if (sortMainBtn != null)
        {
            sortMainBtn.onClick.RemoveAllListeners();
            sortMainBtn.onClick.AddListener(() =>
            {
                // Use IsOpen — List is reparented to the root canvas while open, so Find("List") fails.
                bool wasOpen = _sortDropdown.IsOpen;
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
            string mode = SortModeIds[Mathf.Clamp(idx, 0, SortModeIds.Length - 1)];
            if (SparrohPlugin.ModSortMode != null && SparrohPlugin.ModSortMode.Value != mode)
            {
                SparrohPlugin.ModSortMode.Value = mode;
                SparrohPlugin.ModSortMode.ConfigFile.Save();
            }
            UnregisterDropdown(_sortDropdown);
            RefreshMods(resetScroll: true);
        });

        _hideEmptyToggle = UIToggle.Create(
            row2,
            "Hide empty",
            SparrohPlugin.HideModsWithoutConfig?.Value ?? true,
            onChanged: val =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                if (SparrohPlugin.HideModsWithoutConfig != null &&
                    SparrohPlugin.HideModsWithoutConfig.Value != val)
                {
                    SparrohPlugin.HideModsWithoutConfig.Value = val;
                    SparrohPlugin.HideModsWithoutConfig.ConfigFile.Save();
                }
                RefreshMods(resetScroll: true);
            },
            name: "HideEmptyToggle");
        var hideLe = UIHelpers.EnsureLayoutElement(_hideEmptyToggle.GameObject,
            preferredWidth: UITheme.S(120f),
            preferredHeight: rowH);
        hideLe.flexibleWidth = 0f;
        if (_hideEmptyToggle.Label != null)
            _hideEmptyToggle.Label.fontSize = UITheme.S(FontSmallRef);

        // ── Row 3: filter chips ──────────────────────────────────────────
        var row3 = UIFactory.CreateRect("ChipRow", _toolbarFiltersBody);
        UIHelpers.EnsureLayoutElement(row3.gameObject, preferredHeight: rowH, minHeight: rowH);
        UIFactory.AddHorizontalLayout(
            row3.gameObject,
            UITheme.S(6f),
            new RectOffset(0, 0, 0, 0),
            TextAnchor.MiddleLeft,
            controlChildWidth: true,
            expandWidth: true,
            controlChildHeight: true,
            expandHeight: true);

        _filterChipButtons.Clear();
        string activeFilter = NormalizeFilter(SparrohPlugin.ModListFilter?.Value);
        for (int i = 0; i < FilterChipIds.Length; i++)
        {
            string chipId = FilterChipIds[i];
            string chipLabel = FilterChipLabels[i];
            bool selected = string.Equals(chipId, activeFilter, StringComparison.OrdinalIgnoreCase);
            var chip = UIButton.Create(
                row3,
                chipLabel,
                onClick: null,
                style: selected ? UIButtonStyle.Active : UIButtonStyle.Default,
                name: "Filter_" + chipId,
                preferredHeight: rowH);
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

            string capturedId = chipId;
            chip.OnClick(() =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                ApplyFilterChip(capturedId);
            });
            _filterChipButtons.Add(chip);
        }

        // ── Row 4: group by author + expand/collapse all ─────────────────
        var row4 = UIFactory.CreateRect("DensityRow", _toolbarFiltersBody);
        UIHelpers.EnsureLayoutElement(row4.gameObject, preferredHeight: rowH, minHeight: rowH);
        UIFactory.AddHorizontalLayout(
            row4.gameObject,
            UITheme.S(8f),
            new RectOffset(0, 0, 0, 0),
            TextAnchor.MiddleLeft,
            controlChildWidth: true,
            expandWidth: false,
            controlChildHeight: true,
            expandHeight: true);

        _groupByAuthorToggle = UIToggle.Create(
            row4,
            "Group by author",
            SparrohPlugin.GroupModsByAuthor?.Value ?? false,
            onChanged: val =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                if (SparrohPlugin.GroupModsByAuthor != null &&
                    SparrohPlugin.GroupModsByAuthor.Value != val)
                {
                    SparrohPlugin.GroupModsByAuthor.Value = val;
                    SparrohPlugin.GroupModsByAuthor.ConfigFile.Save();
                }
                RefreshMods(resetScroll: true);
            },
            name: "GroupByAuthorToggle");
        var groupLe = UIHelpers.EnsureLayoutElement(_groupByAuthorToggle.GameObject,
            preferredWidth: UITheme.S(150f),
            preferredHeight: rowH);
        groupLe.flexibleWidth = 1f;
        if (_groupByAuthorToggle.Label != null)
            _groupByAuthorToggle.Label.fontSize = UITheme.S(FontSmallRef);

        _expandCollapseAllBtn = UIButton.Create(
            row4,
            "Collapse all",
            onClick: () =>
            {
                if (_suppressToolbarCallbacks)
                    return;
                ToggleExpandCollapseAll();
            },
            style: UIButtonStyle.Default,
            name: "ExpandCollapseAll",
            preferredHeight: rowH);
        var expLe = UIHelpers.EnsureLayoutElement(_expandCollapseAllBtn.GameObject,
            preferredWidth: UITheme.S(120f),
            preferredHeight: rowH);
        expLe.flexibleWidth = 0f;
        if (_expandCollapseAllBtn.Label != null)
        {
            _expandCollapseAllBtn.Label.fontSize = UITheme.S(FontSmallRef);
            _expandCollapseAllBtn.Label.fontStyle = FontStyles.Bold;
        }

        // Apply initial collapsed/expanded chrome (heights already set above).
        ApplyToolbarFiltersCollapsed(persist: false);
    }

    /// <summary>Show or hide toolbar filter/sort rows; search stays visible.</summary>
    private static void SetToolbarFiltersCollapsed(bool collapsed, bool persist)
    {
        _toolbarFiltersCollapsed = collapsed;
        ApplyToolbarFiltersCollapsed(persist);
    }

    private static void ApplyToolbarFiltersCollapsed(bool persist)
    {
        if (_toolbarFiltersBody != null)
            _toolbarFiltersBody.gameObject.SetActive(!_toolbarFiltersCollapsed);

        float h = _toolbarFiltersCollapsed ? _toolbarHCollapsed : _toolbarHExpanded;
        if (_toolbarRoot != null)
            UIHelpers.SetTopStretch(_toolbarRoot, h, left: 0f, right: 0f, top: 0f);

        if (_toolbarScrollRt != null)
            _toolbarScrollRt.offsetMax = new Vector2(_toolbarScrollRt.offsetMax.x, -h);

        if (_toolbarFiltersToggleBtn != null)
            _toolbarFiltersToggleBtn.SetText(_toolbarFiltersCollapsed ? "+" : "-");

        // Closing filters should also close any open sort dropdown.
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
                int idx = IndexOfSortMode(SparrohPlugin.ModSortMode?.Value);
                _sortDropdown.Select(idx, notify: false);
            }

            if (_hideEmptyToggle != null && SparrohPlugin.HideModsWithoutConfig != null)
                _hideEmptyToggle.IsOn = SparrohPlugin.HideModsWithoutConfig.Value;

            if (_groupByAuthorToggle != null && SparrohPlugin.GroupModsByAuthor != null)
                _groupByAuthorToggle.IsOn = SparrohPlugin.GroupModsByAuthor.Value;

            RefreshFilterChipStyles(NormalizeFilter(SparrohPlugin.ModListFilter?.Value));
            UpdateExpandCollapseAllLabel();

            bool collapsed = SparrohPlugin.ToolbarFiltersCollapsed?.Value ?? false;
            if (collapsed != _toolbarFiltersCollapsed)
                SetToolbarFiltersCollapsed(collapsed, persist: false);
            else
                ApplyToolbarFiltersCollapsed(persist: false);
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
        RefreshMods(resetScroll: true);
    }

    private static void RefreshFilterChipStyles(string activeFilter)
    {
        activeFilter = NormalizeFilter(activeFilter);
        for (int i = 0; i < _filterChipButtons.Count && i < FilterChipIds.Length; i++)
        {
            bool on = string.Equals(FilterChipIds[i], activeFilter, StringComparison.OrdinalIgnoreCase);
            _filterChipButtons[i].SetStyle(on ? UIButtonStyle.Active : UIButtonStyle.Default);
        }
    }

    private static int IndexOfSortMode(string mode)
    {
        if (string.IsNullOrEmpty(mode))
            return 0;
        for (int i = 0; i < SortModeIds.Length; i++)
        {
            if (string.Equals(SortModeIds[i], mode, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    private static string NormalizeFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return "All";
        for (int i = 0; i < FilterChipIds.Length; i++)
        {
            if (string.Equals(FilterChipIds[i], filter, StringComparison.OrdinalIgnoreCase))
                return FilterChipIds[i];
        }
        return "All";
    }

    // ── Collapse / group helpers ─────────────────────────────────────────


    private static string GetModKey(ModInfo mod)
    {
        if (!string.IsNullOrEmpty(mod.GUID))
            return mod.GUID;
        return mod.Name ?? "";
    }

    /// <summary>GUID prefix before first '.' (e.g. sparroh.mod → sparroh), else Other.</summary>
    private static string GetAuthorGroup(ModInfo mod)
    {
        string guid = mod.GUID;
        if (string.IsNullOrEmpty(guid))
            return "Other";
        int dot = guid.IndexOf('.');
        if (dot <= 0)
            return "Other";
        return guid.Substring(0, dot);
    }

    private static void LoadCollapsedFromConfig()
    {
        _collapsedMods.Clear();
        string raw = SparrohPlugin.CollapsedMods?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return;
        foreach (string part in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string key = part.Trim();
            if (key.Length > 0)
                _collapsedMods.Add(key);
        }
    }

    private static void SaveCollapsedToConfig()
    {
        if (SparrohPlugin.CollapsedMods == null)
            return;
        string value = _collapsedMods.Count == 0
            ? ""
            : string.Join(",", _collapsedMods);
        if (SparrohPlugin.CollapsedMods.Value == value)
            return;
        SparrohPlugin.CollapsedMods.Value = value;
        SparrohPlugin.CollapsedMods.ConfigFile.Save();
    }

    private static bool IsModExpanded(string key) => !_collapsedMods.Contains(key);

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

    /// <summary>Toggle one mod block (in-list title or sticky header).</summary>
    private static void ToggleModBlock(ModBlockState block)
    {
        if (block == null)
            return;
        ClearActiveEditing();
        ApplyBlockExpanded(block, !block.IsExpanded, persist: true);
        _stickyTitles?.Refresh();
        UpdateExpandCollapseAllLabel();
    }

    private static ModBlockState FindModBlock(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        for (int i = 0; i < _modBlocks.Count; i++)
        {
            var block = _modBlocks[i];
            if (block != null && string.Equals(block.Key, key, StringComparison.OrdinalIgnoreCase))
                return block;
        }
        return null;
    }

    /// <summary>Called by sticky header click for the currently pinned mod.</summary>
    internal static void ToggleModBlockByKey(string key)
    {
        ToggleModBlock(FindModBlock(key));
    }

    private static void ToggleExpandCollapseAll()
    {
        // If any visible block is expanded → collapse all; else expand all.
        bool anyExpanded = false;
        for (int i = 0; i < _modBlocks.Count; i++)
        {
            if (_modBlocks[i] != null && _modBlocks[i].IsExpanded)
            {
                anyExpanded = true;
                break;
            }
        }

        bool expand = !anyExpanded;
        for (int i = 0; i < _modBlocks.Count; i++)
        {
            var block = _modBlocks[i];
            if (block == null)
                continue;
            ApplyBlockExpanded(block, expand, persist: false);
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
        bool anyExpanded = false;
        if (_modBlocks.Count == 0)
        {
            // Before first refresh, assume default all-expanded.
            anyExpanded = _collapsedMods.Count == 0;
        }
        else
        {
            for (int i = 0; i < _modBlocks.Count; i++)
            {
                if (_modBlocks[i] != null && _modBlocks[i].IsExpanded)
                {
                    anyExpanded = true;
                    break;
                }
            }
        }

        _expandCollapseAllBtn.SetText(anyExpanded ? "Collapse all" : "Expand all");
    }

    private static void CreateGroupHeader(Transform parent, string groupName)
    {
        var bar = UIFactory.CreateImage("Group_" + groupName, parent, UIColors.SectionBar, raycast: false);
        UIFactory.ApplyWhiteSprite(bar);
        UIHelpers.EnsureLayoutElement(bar.gameObject,
            preferredHeight: UITheme.S(28f),
            minHeight: UITheme.S(28f));

        string label = string.IsNullOrEmpty(groupName) ? "Other" : groupName;
        var tmp = UIFactory.CreateTmp(
            "Text",
            bar.rectTransform,
            RichText.Bold(label),
            UITheme.S(FontSectionRef),
            UIColors.TextSecondary,
            TextAlignmentOptions.MidlineLeft);
        UIHelpers.SetFillParent(tmp.rectTransform, UITheme.S(8f));
    }




    /// <summary>
    /// Builds a floating mod-title bar as a child of the scroll viewport (masked on push-off)
    /// and attaches the controller that keeps it in sync with scroll position.
    /// </summary>
    private static StickyModTitleController CreateStickyModTitleOverlay(UIScrollView scrollView)
    {
        float headerH = UITheme.S(44f);

        // Raycast on so the pinned title can collapse/expand like the in-list header.
        var stickyImg = UIFactory.CreateImage(
            "StickyModTitle",
            scrollView.Viewport,
            UIColors.TitleBar,
            raycast: true);
        UIFactory.ApplyWhiteSprite(stickyImg);

        var stickyRt = stickyImg.rectTransform;
        UIHelpers.SetTopStretch(stickyRt, headerH, left: 0f, right: 0f, top: 0f);
        stickyRt.SetAsLastSibling();

        // Accent line under the sticky bar (matches window title chrome).
        var accent = UIFactory.CreateImage("Accent", stickyRt, UIColors.BorderAccent, raycast: false);
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

        // Whole-bar click toggles the pinned mod (drag-pan safe).
        var stickyClick = stickyImg.gameObject.AddComponent<ClickVsDragToggle>();
        stickyClick.Initialize(
            true,
            ClickDragThresholdPx,
            _ => controller.ToggleActiveMod());

        return controller;
    }

    public static void RefreshMods() => RefreshMods(resetScroll: false, preserveSearchFocus: false);

    /// <param name="resetScroll">Snap list to top after rebuild (sort/filter/search changes).</param>
    /// <param name="preserveSearchFocus">Keep the search field focused after rebuild (live typing).</param>
    public static void RefreshMods(bool resetScroll, bool preserveSearchFocus = false)
    {
        if (_window == null || _window.Content == null)
            return;

        // Don't steal focus from the search box while the user is typing.
        if (preserveSearchFocus)
        {
            // Close dropdowns only; leave search caret alone.
            for (int i = _openDropdowns.Count - 1; i >= 0; i--)
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

        // Fresh cfg scan once per refresh (used by filter, sort, and CreateModConfig).
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

            bool groupByAuthor = SparrohPlugin.GroupModsByAuthor?.Value ?? false;
            bool first = true;

            if (groupByAuthor)

            {
                // Stable group order: sort groups A–Z, keep within-group order from visible.
                var byGroup = new SortedDictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < visible.Count; i++)
                {
                    string g = GetAuthorGroup(visible[i]);
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
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
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
            }
            else
            {
                foreach (var mod in visible)
                {
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
        _activeInput = _searchField.Input;
        _searchField.Input.ActivateInputField();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_searchField.Input.gameObject);
    }

    /// <summary>Filter → sort pipeline for the mod list.</summary>
    private static List<ModInfo> BuildVisibleMods(IReadOnlyList<ModInfo> source)
    {
        string query = (_searchQuery ?? "").Trim();
        bool hideEmpty = SparrohPlugin.HideModsWithoutConfig?.Value ?? true;
        string filter = NormalizeFilter(SparrohPlugin.ModListFilter?.Value);
        string sortMode = SparrohPlugin.ModSortMode?.Value ?? "Alphabetical";

        var list = new List<ModInfo>(source.Count);
        for (int i = 0; i < source.Count; i++)
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
            return; // already in ModManager / chainloader order after filter

        if (string.Equals(mode, "AlphabeticalDesc", StringComparison.OrdinalIgnoreCase))
        {
            list.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (string.Equals(mode, "SandboxFirst", StringComparison.OrdinalIgnoreCase))
        {
            list.Sort((a, b) =>
            {
                int sandbox = b.IsSandbox.CompareTo(a.IsSandbox); // true first
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
                bool ha = ModHasConfig(a);
                bool hb = ModHasConfig(b);
                int cfg = hb.CompareTo(ha); // has-config first
                if (cfg != 0)
                    return cfg;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return;
        }

        // Alphabetical (default)
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

    /// <summary>Find a .cfg whose filename contains the mod display name (case-insensitive).</summary>
    private static bool TryFindConfigPath(ModInfo mod, out string configPath)
    {
        configPath = null;
        if (string.IsNullOrEmpty(mod.Name))
            return false;

        string nameLower = mod.Name.ToLowerInvariant();
        foreach (string file in GetCfgFiles())
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName != null && fileName.ToLowerInvariant().Contains(nameLower))
            {
                configPath = file;
                return true;
            }
        }

        return false;
    }

    private static bool ModHasConfig(ModInfo mod) => TryFindConfigPath(mod, out _);

    /// <summary>
    /// Write a setting to the plugin's live ConfigEntry (fires SettingChanged) and save.
    /// Falls back to a detached ConfigFile write if the live entry cannot be resolved.
    /// </summary>
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

        // Detached file-only write (no in-memory plugin update).
        if (fileFallback != null)
        {
            fileFallback.Value = value ?? "";
            fileFallback.ConfigFile.Save();
        }
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
            BaseUnityPlugin plugin = FindPluginForConfig(mod, configPath);
            if (plugin?.Config == null)
                return false;

            ConfigEntryBase live = FindLiveEntry(plugin.Config, section, key);
            if (live == null)
                return false;

            // Prefer serialized set so bool/int/float/string all round-trip correctly.
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
        {
            return fromGuid;
        }

        if (Chainloader.PluginInfos == null || string.IsNullOrEmpty(configPath))
            return null;

        foreach (var kv in Chainloader.PluginInfos)
        {
            if (kv.Value?.Instance is not BaseUnityPlugin plugin || plugin.Config == null)
                continue;

            string path = plugin.Config.ConfigFilePath;
            if (!string.IsNullOrEmpty(path) &&
                string.Equals(path, configPath, StringComparison.OrdinalIgnoreCase))
            {
                return plugin;
            }
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
            BaseUnityPlugin plugin = FindPluginForConfig(mod, configPath);
            ConfigEntryBase live = plugin != null
                ? FindLiveEntry(plugin.Config, section, key)
                : null;
            if (live != null)
            {
                string serialized = live.GetSerializedValue();
                if (serialized != null)
                    return serialized;
            }
        }
        catch
        {
            // fall through to file value
        }

        return fileValue ?? "";
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
                            options = comment.Substring("Acceptable values:".Length).Split(',')
                                .Select(s => s.Trim())
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
            string modKey = GetModKey(modLocal);
            bool expanded = IsModExpanded(modKey);

            // ── Mod block: title + collapsible body ───────────────────────
            var blockRoot = UIFactory.CreateRect("ModBlock_" + modLocal.Name, parent);
            UIFactory.AddVerticalLayout(
                blockRoot.gameObject,
                UITheme.S(UITheme.SpacingTight),
                new RectOffset(0, 0, 0, 0),
                TextAnchor.UpperLeft,
                controlChildHeight: true,
                expandHeight: false,
                controlChildWidth: true,
                expandWidth: true);
            // Let content size the block.
            UIFactory.AddContentSizeFitter(blockRoot.gameObject,
                ContentSizeFitter.FitMode.Unconstrained,
                ContentSizeFitter.FitMode.PreferredSize);

            // Title bar (clickable)
            var titleBar = UIFactory.CreateImage(modLocal.Name + " Title", blockRoot, UIColors.TitleBar, raycast: true);
            UIFactory.ApplyWhiteSprite(titleBar);
            UIHelpers.EnsureLayoutElement(titleBar.gameObject,
                preferredHeight: UITheme.S(44f),
                minHeight: UITheme.S(44f));

            UIFactory.AddHorizontalLayout(
                titleBar.gameObject,
                UITheme.S(6f),
                UITheme.ScaledPadding(10, 10, 4, 4),
                TextAnchor.MiddleLeft,
                controlChildWidth: true,
                expandWidth: false,
                controlChildHeight: true,
                expandHeight: true);

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
                preferredWidth: UITheme.S(28f),
                preferredHeight: UITheme.S(36f));
            chevLe.flexibleWidth = 0f;

            string title = RichText.Bold(modLocal.Name);
            if (modLocal.IsSandbox)
            {
                title += " " + RichText.Size(
                    "[" + RichText.Italic(RichText.Colorize("Sandbox", UIColors.Rose)) + "]",
                    55);
            }

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

            // Body holds sections / no-config message
            var bodyRt = UIFactory.CreateRect("Body", blockRoot);
            UIFactory.AddVerticalLayout(
                bodyRt.gameObject,
                UITheme.S(UITheme.SpacingTight),
                new RectOffset(0, 0, 0, 0),
                TextAnchor.UpperLeft,
                controlChildHeight: true,
                expandHeight: false,
                controlChildWidth: true,
                expandWidth: true);
            UIFactory.AddContentSizeFitter(bodyRt.gameObject,
                ContentSizeFitter.FitMode.Unconstrained,
                ContentSizeFitter.FitMode.PreferredSize);
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

            // Sticky shows chevron + name so collapsed state is visible while pinned.
            _stickyTitles?.Register(titleBar.rectTransform, FormatStickyTitle(blockState), modKey);

            // Click title to toggle (drag-pan safe) — same path as sticky header.
            var titleClick = titleBar.gameObject.AddComponent<ClickVsDragToggle>();
            titleClick.Initialize(
                expanded,
                ClickDragThresholdPx,
                _ => ToggleModBlock(blockState));

            Transform bodyParent = bodyRt;


            // ── Locate config file ─────────────────────────────────────────
            if (!TryFindConfigPath(modLocal, out string configPath) || !File.Exists(configPath))
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
                // Section header
                var sectionLabel = UIWindow.CreateSectionHeader(bodyParent, section.Key);

                if (sectionLabel?.Tmp != null)
                {
                    sectionLabel.Tmp.fontSize = UITheme.S(FontSectionRef);
                    sectionLabel.Tmp.fontStyle = FontStyles.Bold;
                }

                foreach (var (fullEntry, _) in section.Value)
                {
                    string key = fullEntry.Substring(0, fullEntry.IndexOf('=')).Trim();
                    string value = fullEntry.Substring(fullEntry.IndexOf('=') + 1).Trim();
                    string sectionName = section.Key.Trim('[', ']');
                    // Detached bind kept only as disk fallback if live plugin entry is missing.
                    var configEntry = configFile.Bind(sectionName, key, value);

                    string[] options = null;
                    string cacheKey = sectionName + "." + key;
                    if (_cachedOptions.ContainsKey(configPath) &&
                        _cachedOptions[configPath].ContainsKey(cacheKey))
                    {
                        options = _cachedOptions[configPath][cacheKey];
                    }

                    // Prefer the plugin's in-memory value so the UI matches live state.
                    string rawEntryValue = GetLiveOrFileValue(modLocal, configPath, sectionName, key, value);
                    Type entryType = typeof(string);

                    if (bool.TryParse(rawEntryValue, out _))
                        entryType = typeof(bool);
                    else if (int.TryParse(rawEntryValue, out _))
                        entryType = typeof(int);
                    else if (float.TryParse(rawEntryValue, out _))
                        entryType = typeof(float);


                    // Entry row: dark surface with label + control
                    float rowH = UITheme.S(58f);
                    var entryBg = UIFactory.CreateImage("Entry_" + key, bodyParent, UIColors.EntryBg, raycast: true);

                    UIFactory.ApplyWhiteSprite(entryBg);
                    UIHelpers.EnsureLayoutElement(entryBg.gameObject,
                        preferredHeight: rowH,
                        minHeight: rowH);

                    // controlChildWidth must be true or flexibleWidth is ignored (content-sized kids).
                    UIFactory.AddHorizontalLayout(
                        entryBg.gameObject,
                        UITheme.S(UITheme.SpacingNormal),
                        UITheme.ScaledPadding(12, 12, 6, 6),
                        TextAnchor.MiddleLeft,
                        controlChildWidth: true,
                        expandWidth: true,
                        controlChildHeight: true,
                        expandHeight: true);

                    // Label | value — equal halves of the row.
                    var labelTmp = UIFactory.CreateTmp(
                        "Label",
                        entryBg.rectTransform,
                        key,
                        UITheme.S(FontLabelRef),
                        UIColors.TextPrimary,
                        TextAlignmentOptions.MidlineLeft,
                        wrap: true);
                    labelTmp.fontStyle = FontStyles.Bold;
                    var labelLe = UIHelpers.EnsureLayoutElement(labelTmp.gameObject,
                        preferredWidth: UITheme.S(200f),
                        preferredHeight: rowH - UITheme.S(12f));
                    labelLe.flexibleWidth = 1f;
                    labelLe.minWidth = 0f;
                    labelLe.preferredWidth = -1f; // let flex share decide width

                    var valueRt = UIFactory.CreateRect("Value", entryBg.rectTransform);
                    var valueLe = UIHelpers.EnsureLayoutElement(valueRt.gameObject,
                        preferredWidth: UITheme.S(200f),
                        preferredHeight: UITheme.S(36f),
                        minHeight: UITheme.S(32f));
                    valueLe.flexibleWidth = 1f;
                    valueLe.minWidth = 0f;
                    valueLe.preferredWidth = -1f;




                    if (entryType == typeof(bool))
                    {
                        bool isOn = rawEntryValue.Equals("true", StringComparison.OrdinalIgnoreCase);

                        // Status text (ON/OFF) — click-vs-drag gated so pan doesn't flip.
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

                        string boolSection = sectionName;
                        string boolKey = key;
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
                        int initial = Array.FindIndex(options,
                            o => string.Equals(o, rawEntryValue, StringComparison.OrdinalIgnoreCase));
                        if (initial < 0)
                            initial = 0;

                        // Build dropdown without the default main-button toggle so we can
                        // clear other edits first, then open/close explicitly.
                        var dropdown = UIDropdown.Create(
                            valueRt,
                            options,
                            initial,
                            onChanged: null,
                            "Dropdown_" + key);

                        string ddSection = sectionName;
                        string ddKey = key;
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
                                // Use IsOpen — List is reparented to the root canvas while open.
                                bool wasOpen = dropdown.IsOpen;

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
                        bool isConfigToggleKey = sectionName == "Keybinds" && key == "ToggleModConfigGUI";
                        bool isRepositionKey = sectionName == "Keybinds" && key == "ToggleHudReposition";

                        var field = UIInputField.Create(
                            valueRt,
                            rawEntryValue,
                            "",
                            name: "Input_" + key);

                        UIHelpers.SetFillParent(field.Rect);

                        // Darker input already from theme; enlarge + bold text.
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
                            // Key rebind: click arms capture mode.
                            field.Input.interactable = false;
                            var eventTrigger = field.GameObject.AddComponent<EventTrigger>();
                            var triggerEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                            bool bindConfig = isConfigToggleKey;
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
                            // Select-to-edit: first clean click arms the field; save on end edit.
                            field.Input.interactable = false;
                            var armClick = field.GameObject.AddComponent<SelectToEditInput>();
                            armClick.Initialize(field.Input, ClickDragThresholdPx, () => ActivateInput(field.Input));

                            string inputSection = sectionName;
                            string inputKey = key;
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
}

/// <summary>
/// Pins the active mod's title to the top of the config scroll viewport while its
/// settings are in view. The next mod title pushes the sticky bar off (iOS-style).
/// </summary>
public class StickyModTitleController : MonoBehaviour
{
    private struct ModTitleEntry
    {
        public RectTransform TitleRect;
        public string TitleRichText;
        public string ModKey;
        public CanvasGroup CanvasGroup;
    }

    private ScrollRect _scrollRect;
    private RectTransform _viewport;
    private RectTransform _stickyRoot;
    private TextMeshProUGUI _stickyLabel;
    private float _headerHeight;
    private readonly List<ModTitleEntry> _entries = new List<ModTitleEntry>();
    private readonly Vector3[] _corners = new Vector3[4];
    private bool _visible;
    private string _currentTitle;
    private int _hiddenTitleIndex = -1;
    private int _activeIndex = -1;

    /// <summary>True while the floating sticky title is shown.</summary>
    public bool IsStickyActive => _visible;

    /// <summary>
    /// Pixels from the viewport top to the bottom edge of the sticky bar (0 when hidden).
    /// Wheel snap aligns rows to this line so nothing rests half-under the header.
    /// </summary>
    public float SnapTopInset
    {
        get
        {
            if (!_visible || _stickyRoot == null || !_stickyRoot.gameObject.activeSelf)
                return 0f;

            // During push-off, stickyY > 0 slides the bar up; visible height shrinks.
            float pushed = Mathf.Max(0f, _stickyRoot.anchoredPosition.y);
            return Mathf.Clamp(_headerHeight - pushed, 0f, _headerHeight);
        }
    }

    public void Initialize(
        ScrollRect scrollRect,
        RectTransform stickyRoot,
        TextMeshProUGUI stickyLabel,
        float headerHeight)
    {
        _scrollRect = scrollRect;
        _viewport = scrollRect != null ? scrollRect.viewport : null;
        _stickyRoot = stickyRoot;
        _stickyLabel = stickyLabel;
        _headerHeight = Mathf.Max(1f, headerHeight);

        if (_scrollRect != null)
            _scrollRect.onValueChanged.AddListener(OnScrollChanged);

        SetStickyVisible(false);
    }

    private void OnDestroy()
    {
        if (_scrollRect != null)
            _scrollRect.onValueChanged.RemoveListener(OnScrollChanged);

        RestoreHiddenTitle();
    }

    public void Clear()
    {
        RestoreHiddenTitle();
        _entries.Clear();
        _currentTitle = null;
        _activeIndex = -1;
        SetStickyVisible(false);
    }

    public void Register(RectTransform titleRect, string titleRichText, string modKey = null)
    {
        if (titleRect == null)
            return;

        var cg = titleRect.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = titleRect.gameObject.AddComponent<CanvasGroup>();
        // Keep raycasts on so title click can collapse/expand; only disable while sticky-hidden.
        cg.alpha = 1f;
        cg.blocksRaycasts = true;
        cg.interactable = true;

        _entries.Add(new ModTitleEntry
        {
            TitleRect = titleRect,
            TitleRichText = titleRichText ?? string.Empty,
            ModKey = modKey ?? string.Empty,
            CanvasGroup = cg
        });
    }

    /// <summary>Collapse/expand the mod currently represented by the sticky bar.</summary>
    public void ToggleActiveMod()
    {
        if (!_visible || _activeIndex < 0 || _activeIndex >= _entries.Count)
            return;

        string key = _entries[_activeIndex].ModKey;
        if (string.IsNullOrEmpty(key))
            return;

        ModConfigGUI.ToggleModBlockByKey(key);
    }

    /// <summary>Update sticky label text for a registered title (e.g. chevron after collapse).</summary>
    public void UpdateTitle(RectTransform titleRect, string titleRichText)
    {
        if (titleRect == null)
            return;

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].TitleRect != titleRect)
                continue;

            var e = _entries[i];
            e.TitleRichText = titleRichText ?? string.Empty;
            _entries[i] = e;

            // If this title is the active sticky, update the floater immediately.
            if (_visible && _activeIndex == i && _stickyLabel != null)
            {
                _stickyLabel.text = e.TitleRichText;
                _currentTitle = e.TitleRichText;
            }
            else if (_hiddenTitleIndex == i)
            {
                // Force sticky label refresh on next UpdateSticky.
                _currentTitle = null;
            }

            return;
        }
    }


    /// <summary>Rebuild layout then recompute sticky state (call after RefreshMods).</summary>
    public void Refresh()
    {
        if (_scrollRect != null && _scrollRect.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);

        Canvas.ForceUpdateCanvases();
        UpdateSticky();
    }

    private void OnScrollChanged(Vector2 _)
    {
        UpdateSticky();
    }

    private void LateUpdate()
    {
        // Keep in sync during inertial coasting and layout settles.
        if (_entries.Count > 0 && _scrollRect != null && isActiveAndEnabled)
            UpdateSticky();
    }

    private void UpdateSticky()
    {
        if (_stickyRoot == null || _viewport == null || _entries.Count == 0)
        {
            RestoreHiddenTitle();
            _activeIndex = -1;
            SetStickyVisible(false);
            return;
        }

        // Last header whose top edge has reached/passed the viewport top becomes sticky.
        int activeIndex = -1;
        for (int i = 0; i < _entries.Count; i++)
        {
            var titleRt = _entries[i].TitleRect;
            if (titleRt == null)
                continue;

            float topDist = DistanceBelowViewportTop(titleRt);
            if (topDist <= 0.5f)
                activeIndex = i;
            else if (activeIndex >= 0)
                break;
        }

        if (activeIndex < 0)
        {
            RestoreHiddenTitle();
            _activeIndex = -1;
            SetStickyVisible(false);
            return;
        }

        // Push-off: top-stretch sticky uses positive Y to move UP (out of viewport).
        // When the next mod title enters the sticky zone, slide the floater up with it.
        float stickyY = 0f;
        if (activeIndex + 1 < _entries.Count)
        {
            var nextRt = _entries[activeIndex + 1].TitleRect;
            if (nextRt != null)
            {
                float nextTop = DistanceBelowViewportTop(nextRt);
                if (nextTop < _headerHeight)
                    stickyY = _headerHeight - nextTop;
            }
        }

        // Fully pushed off — next title owns the top; hide floater until activeIndex advances.
        if (stickyY >= _headerHeight - 0.5f)
        {
            RestoreHiddenTitle();
            _activeIndex = -1;
            SetStickyVisible(false);
            return;
        }

        _activeIndex = activeIndex;

        string title = _entries[activeIndex].TitleRichText;
        if (!string.Equals(_currentTitle, title, StringComparison.Ordinal))
        {
            _currentTitle = title;
            if (_stickyLabel != null)
                _stickyLabel.text = title;
        }

        _stickyRoot.anchoredPosition = new Vector2(_stickyRoot.anchoredPosition.x, stickyY);
        SetStickyVisible(true);

        // Hide the in-list title while the floater represents it (avoids double/clipped headers).
        SetHiddenTitle(activeIndex);
    }

    /// <summary>
    /// Distance from the viewport's top edge down to the title's top edge, in viewport local units.
    /// 0 = flush with top; positive = still below (visible lower); negative = scrolled above.
    /// </summary>
    private float DistanceBelowViewportTop(RectTransform titleRt)
    {
        titleRt.GetWorldCorners(_corners);
        // corners: 0=BL, 1=TL, 2=TR, 3=BR — use top-left
        Vector3 titleTopLocal = _viewport.InverseTransformPoint(_corners[1]);
        return _viewport.rect.yMax - titleTopLocal.y;
    }

    private void SetHiddenTitle(int index)
    {
        if (_hiddenTitleIndex == index)
            return;

        RestoreHiddenTitle();

        if (index < 0 || index >= _entries.Count)
            return;

        var cg = _entries[index].CanvasGroup;
        if (cg != null)
        {
            cg.alpha = 0f;
            // Floater represents this title — don't steal clicks under the sticky bar.
            cg.blocksRaycasts = false;
            cg.interactable = false;
        }

        _hiddenTitleIndex = index;
    }

    private void RestoreHiddenTitle()
    {
        if (_hiddenTitleIndex < 0)
            return;

        if (_hiddenTitleIndex < _entries.Count)
        {
            var cg = _entries[_hiddenTitleIndex].CanvasGroup;
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }
        }

        _hiddenTitleIndex = -1;
    }


    private void SetStickyVisible(bool visible)
    {
        if (_visible == visible && _stickyRoot != null && _stickyRoot.gameObject.activeSelf == visible)
            return;

        _visible = visible;
        if (_stickyRoot != null)
            _stickyRoot.gameObject.SetActive(visible);

        if (!visible)
        {
            _currentTitle = null;
            _activeIndex = -1;
        }
    }
}


/// <summary>
/// ScrollRect wheel handler: each notch snaps the next/previous content row to the
/// sticky-aware top line (viewport top, or just under the pinned mod title).
/// Also clears active editing when the user scrolls or begins drag-panning.
/// </summary>
public class ItemStepScrollHandler : MonoBehaviour, IScrollHandler, IBeginDragHandler, IEndDragHandler
{
    /// <summary>Ignore separators / hairlines when picking snap targets.</summary>
    private const float MinSnapChildHeight = 8f;

    /// <summary>How far past the snap line a row must be to count as "next" / "previous".</summary>
    private const float SnapPassThreshold = 3f;

    private ScrollRect _scrollRect;
    private StickyModTitleController _sticky;
    private readonly Vector3[] _corners = new Vector3[4];

    public void Initialize(ScrollRect scrollRect, StickyModTitleController sticky = null)
    {
        _scrollRect = scrollRect;
        _sticky = sticky;
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (_scrollRect == null || !_scrollRect.vertical)
            return;

        ModConfigGUI.ClearActiveEditing();

        float scrollDelta = eventData.scrollDelta.y;
        if (Mathf.Approximately(scrollDelta, 0f))
            return;

        RectTransform content = _scrollRect.content;
        RectTransform viewport = _scrollRect.viewport != null
            ? _scrollRect.viewport
            : (RectTransform)_scrollRect.transform;

        if (content == null || viewport == null)
            return;

        float contentHeight = content.rect.height;
        float viewportHeight = viewport.rect.height;
        float scrollable = Mathf.Max(0f, contentHeight - viewportHeight);
        if (scrollable <= 0f)
            return;

        // Wheel up (positive) → reveal content above; wheel down → content below.
        bool scrollUp = scrollDelta > 0f;
        float inset = _sticky != null ? _sticky.SnapTopInset : 0f;

        if (!TryFindSnapChild(content, viewport, inset, scrollUp, out RectTransform target))
        {
            // Already at end — nudge fully to top/bottom.
            _scrollRect.velocity = Vector2.zero;
            _scrollRect.verticalNormalizedPosition = scrollUp ? 1f : 0f;
            eventData.Use();
            return;
        }

        SnapChildToInset(content, viewport, target, inset, scrollable);
        eventData.Use();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        ModConfigGUI.ClearActiveEditing();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
    }

    private bool TryFindSnapChild(
        RectTransform content,
        RectTransform viewport,
        float inset,
        bool scrollUp,
        out RectTransform target)
    {
        target = null;
        float bestDist = scrollUp ? float.MinValue : float.MaxValue;

        for (int i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i) as RectTransform;
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            float h = child.rect.height;
            if (h < MinSnapChildHeight)
                continue;

            // Skip the in-list title currently represented by the sticky floater.
            var cg = child.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.5f)
                continue;

            float topDist = DistanceBelowViewportTop(viewport, child);

            if (scrollUp)
            {
                // Previous row: top is above the snap line.
                if (topDist < inset - SnapPassThreshold && topDist > bestDist)
                {
                    bestDist = topDist;
                    target = child;
                }
            }
            else
            {
                // Next row: top is below the snap line.
                if (topDist > inset + SnapPassThreshold && topDist < bestDist)
                {
                    bestDist = topDist;
                    target = child;
                }
            }
        }

        return target != null;
    }

    private void SnapChildToInset(
        RectTransform content,
        RectTransform viewport,
        RectTransform child,
        float inset,
        float scrollable)
    {
        float topDist = DistanceBelowViewportTop(viewport, child);
        // Positive delta → child is too low → move content up → decrease normalized pos.
        float deltaPixels = topDist - inset;
        float next = Mathf.Clamp01(
            _scrollRect.verticalNormalizedPosition - deltaPixels / scrollable);

        _scrollRect.velocity = Vector2.zero;
        _scrollRect.verticalNormalizedPosition = next;
    }

    private float DistanceBelowViewportTop(RectTransform viewport, RectTransform rt)
    {
        rt.GetWorldCorners(_corners);
        Vector3 topLocal = viewport.InverseTransformPoint(_corners[1]);
        return viewport.rect.yMax - topLocal.y;
    }
}


/// <summary>
/// Bool control: only flips on a clean click (pointer moved less than threshold).
/// Does not implement IDragHandler so ScrollRect drag-pan still works from this control.
/// </summary>
public class ClickVsDragToggle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private bool _value;
    private float _threshold;
    private Action<bool> _onChanged;
    private Vector2 _pressPos;
    private bool _pressed;

    public void Initialize(bool initialValue, float dragThresholdPx, Action<bool> onChanged)
    {
        _value = initialValue;
        _threshold = dragThresholdPx;
        _onChanged = onChanged;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
        _pressed = true;
        _pressPos = eventData.position;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_pressed || eventData.button != PointerEventData.InputButton.Left)
            return;
        _pressed = false;

        if ((eventData.position - _pressPos).sqrMagnitude > _threshold * _threshold)
            return;

        _value = !_value;
        _onChanged?.Invoke(_value);
    }
}

/// <summary>
/// Text/number field: first clean click arms the TMP_InputField for editing.
/// Does not implement IDragHandler so ScrollRect drag-pan still works from this control.
/// </summary>
public class SelectToEditInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private TMP_InputField _input;
    private float _threshold;
    private Action _onSelect;
    private Vector2 _pressPos;
    private bool _pressed;

    public void Initialize(TMP_InputField input, float dragThresholdPx, Action onSelect)
    {
        _input = input;
        _threshold = dragThresholdPx;
        _onSelect = onSelect;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
        _pressed = true;
        _pressPos = eventData.position;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_pressed || eventData.button != PointerEventData.InputButton.Left)
            return;
        _pressed = false;

        if ((eventData.position - _pressPos).sqrMagnitude > _threshold * _threshold)
            return;

        if (_input != null && _input.interactable && _input.isFocused)
            return;

        _onSelect?.Invoke();
    }
}
