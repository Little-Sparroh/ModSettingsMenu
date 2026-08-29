using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemStepScrollHandler : MonoBehaviour, IScrollHandler, IBeginDragHandler, IEndDragHandler
{
    private const float MinSnapChildHeight = 8f;
    private const float SnapPassThreshold = 3f;
    private readonly Vector3[] _corners = new Vector3[4];

    private ScrollRect _scrollRect;
    private StickyModTitleController _sticky;

    public void OnBeginDrag(PointerEventData eventData)
    {
        ModConfigGUI.ClearActiveEditing();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (_scrollRect == null || !_scrollRect.vertical)
            return;

        ModConfigGUI.ClearActiveEditing();

        var scrollDelta = eventData.scrollDelta.y;
        if (Mathf.Approximately(scrollDelta, 0f))
            return;

        var content = _scrollRect.content;
        var viewport = _scrollRect.viewport != null
            ? _scrollRect.viewport
            : (RectTransform)_scrollRect.transform;

        if (content == null || viewport == null)
            return;

        var contentHeight = content.rect.height;
        var viewportHeight = viewport.rect.height;
        var scrollable = Mathf.Max(0f, contentHeight - viewportHeight);
        if (scrollable <= 0f)
            return;

        var scrollUp = scrollDelta > 0f;
        var inset = _sticky != null ? _sticky.SnapTopInset : 0f;

        if (!TryFindSnapChild(content, viewport, inset, scrollUp, out var target))
        {
            _scrollRect.velocity = Vector2.zero;
            _scrollRect.verticalNormalizedPosition = scrollUp ? 1f : 0f;
            eventData.Use();
            return;
        }

        SnapChildToInset(content, viewport, target, inset, scrollable);
        eventData.Use();
    }

    public void Initialize(ScrollRect scrollRect, StickyModTitleController sticky = null)
    {
        _scrollRect = scrollRect;
        _sticky = sticky;
    }

    private bool TryFindSnapChild(
        RectTransform content,
        RectTransform viewport,
        float inset,
        bool scrollUp,
        out RectTransform target)
    {
        target = null;
        var bestDist = scrollUp ? float.MinValue : float.MaxValue;
        ConsiderChildren(content, viewport, inset, scrollUp, ref bestDist, ref target);
        return target != null;
    }

    private void ConsiderChildren(
        RectTransform parent,
        RectTransform viewport,
        float inset,
        bool scrollUp,
        ref float bestDist,
        ref RectTransform target)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            // Nested vertical layout groups (mod blocks / bodies) are containers,
            // not rows — recurse so each entry/title/section snaps individually.
            if (IsVerticalLayoutContainer(child))
            {
                ConsiderChildren(child, viewport, inset, scrollUp, ref bestDist, ref target);
                continue;
            }

            var h = child.rect.height;
            if (h < MinSnapChildHeight)
                continue;

            var cg = child.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.5f)
                continue;

            var topDist = DistanceBelowViewportTop(viewport, child);

            if (scrollUp)
            {
                if (topDist < inset - SnapPassThreshold && topDist > bestDist)
                {
                    bestDist = topDist;
                    target = child;
                }
            }
            else
            {
                if (topDist > inset + SnapPassThreshold && topDist < bestDist)
                {
                    bestDist = topDist;
                    target = child;
                }
            }
        }
    }

    private static bool IsVerticalLayoutContainer(RectTransform rt)
    {
        if (rt.childCount == 0)
            return false;

        var vlg = rt.GetComponent<VerticalLayoutGroup>();
        return vlg != null && vlg.enabled;
    }

    private void SnapChildToInset(
        RectTransform content,
        RectTransform viewport,
        RectTransform child,
        float inset,
        float scrollable)
    {
        var topDist = DistanceBelowViewportTop(viewport, child);

        var deltaPixels = topDist - inset;
        var next = Mathf.Clamp01(
            _scrollRect.verticalNormalizedPosition - deltaPixels / scrollable);

        _scrollRect.velocity = Vector2.zero;
        _scrollRect.verticalNormalizedPosition = next;
    }

    private float DistanceBelowViewportTop(RectTransform viewport, RectTransform rt)
    {
        rt.GetWorldCorners(_corners);
        var topLocal = viewport.InverseTransformPoint(_corners[1]);
        return viewport.rect.yMax - topLocal.y;
    }
}
