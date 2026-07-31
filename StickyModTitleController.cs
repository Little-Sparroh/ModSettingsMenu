using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StickyModTitleController : MonoBehaviour
{
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<ModTitleEntry> _entries = new();
    private int _activeIndex = -1;
    private string _currentTitle;
    private float _headerHeight;
    private int _hiddenTitleIndex = -1;

    private ScrollRect _scrollRect;
    private TextMeshProUGUI _stickyLabel;
    private RectTransform _stickyRoot;
    private RectTransform _viewport;

    public bool IsStickyActive { get; private set; }

    public float SnapTopInset
    {
        get
        {
            if (!IsStickyActive || _stickyRoot == null || !_stickyRoot.gameObject.activeSelf)
                return 0f;

            var pushed = Mathf.Max(0f, _stickyRoot.anchoredPosition.y);
            return Mathf.Clamp(_headerHeight - pushed, 0f, _headerHeight);
        }
    }

    private void LateUpdate()
    {
        if (_entries.Count > 0 && _scrollRect != null && isActiveAndEnabled)
            UpdateSticky();
    }

    private void OnDestroy()
    {
        if (_scrollRect != null)
            _scrollRect.onValueChanged.RemoveListener(OnScrollChanged);

        RestoreHiddenTitle();
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

    public void ToggleActiveMod()
    {
        if (!IsStickyActive || _activeIndex < 0 || _activeIndex >= _entries.Count)
            return;

        var key = _entries[_activeIndex].ModKey;
        if (string.IsNullOrEmpty(key))
            return;

        ModConfigGUI.ToggleModBlockByKey(key);
    }

    public void UpdateTitle(RectTransform titleRect, string titleRichText)
    {
        if (titleRect == null)
            return;

        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].TitleRect != titleRect)
                continue;

            var e = _entries[i];
            e.TitleRichText = titleRichText ?? string.Empty;
            _entries[i] = e;

            if (IsStickyActive && _activeIndex == i && _stickyLabel != null)
            {
                _stickyLabel.text = e.TitleRichText;
                _currentTitle = e.TitleRichText;
            }
            else if (_hiddenTitleIndex == i)
            {
                _currentTitle = null;
            }

            return;
        }
    }

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

    private void UpdateSticky()
    {
        if (_stickyRoot == null || _viewport == null || _entries.Count == 0)
        {
            RestoreHiddenTitle();
            _activeIndex = -1;
            SetStickyVisible(false);
            return;
        }

        var activeIndex = -1;
        for (var i = 0; i < _entries.Count; i++)
        {
            var titleRt = _entries[i].TitleRect;
            if (titleRt == null)
                continue;

            var topDist = DistanceBelowViewportTop(titleRt);
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

        var stickyY = 0f;
        if (activeIndex + 1 < _entries.Count)
        {
            var nextRt = _entries[activeIndex + 1].TitleRect;
            if (nextRt != null)
            {
                var nextTop = DistanceBelowViewportTop(nextRt);
                if (nextTop < _headerHeight)
                    stickyY = _headerHeight - nextTop;
            }
        }

        if (stickyY >= _headerHeight - 0.5f)
        {
            RestoreHiddenTitle();
            _activeIndex = -1;
            SetStickyVisible(false);
            return;
        }

        _activeIndex = activeIndex;

        var title = _entries[activeIndex].TitleRichText;
        if (!string.Equals(_currentTitle, title, StringComparison.Ordinal))
        {
            _currentTitle = title;
            if (_stickyLabel != null)
                _stickyLabel.text = title;
        }

        _stickyRoot.anchoredPosition = new Vector2(_stickyRoot.anchoredPosition.x, stickyY);
        SetStickyVisible(true);

        SetHiddenTitle(activeIndex);
    }

    private float DistanceBelowViewportTop(RectTransform titleRt)
    {
        titleRt.GetWorldCorners(_corners);

        var titleTopLocal = _viewport.InverseTransformPoint(_corners[1]);
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
        if (IsStickyActive == visible && _stickyRoot != null && _stickyRoot.gameObject.activeSelf == visible)
            return;

        IsStickyActive = visible;
        if (_stickyRoot != null)
            _stickyRoot.gameObject.SetActive(visible);

        if (!visible)
        {
            _currentTitle = null;
            _activeIndex = -1;
        }
    }

    private struct ModTitleEntry
    {
        public RectTransform TitleRect;
        public string TitleRichText;
        public string ModKey;
        public CanvasGroup CanvasGroup;
    }
}