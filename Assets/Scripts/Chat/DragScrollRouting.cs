/// <summary>
/// Which ScrollRect owns a drag that starts inside a text card (DragShield).
///
/// A card whose text overflows keeps the gesture for itself; a card with
/// nothing hidden hands it to the page, so a full-width card never swallows
/// page scrolling. Extracted from DragShield as a pure seam so the policy is
/// unit-testable — the routing bug it guards (drags over the text reaching
/// the page instead of the card's own text) was invisible to every layer of
/// review because it lived in an untested inline branch.
/// </summary>
public static class DragScrollRouting
{
    /// <summary>
    /// Where a drag starting on <paramref name="from"/> should scroll: the
    /// nearest vertical ScrollRect above it that actually has something
    /// hidden, else the next one above that. A card whose text already fits
    /// must not swallow the page's gesture. Null when nothing above scrolls.
    /// </summary>
    public static UnityEngine.UI.ScrollRect ResolveTarget(UnityEngine.Transform from)
    {
        UnityEngine.UI.ScrollRect fallback = null;
        var scroll = from != null ? from.GetComponentInParent<UnityEngine.UI.ScrollRect>() : null;

        while (scroll != null)
        {
            if (scroll.vertical)
            {
                if (CanScrollVertically(scroll)) return scroll;
                if (fallback == null) fallback = scroll;
            }

            var parent = scroll.transform.parent;
            scroll = parent != null ? parent.GetComponentInParent<UnityEngine.UI.ScrollRect>() : null;
        }

        return fallback;
    }

    public static bool CanScrollVertically(UnityEngine.UI.ScrollRect scroll)
    {
        if (scroll == null || !scroll.vertical || scroll.content == null) return false;
        var viewport = scroll.viewport != null
            ? scroll.viewport
            : (UnityEngine.RectTransform)scroll.transform;
        return HasHiddenText(scroll.content.rect.height, viewport.rect.height);
    }

    public enum Target
    {
        None,
        InnerText,
        Page
    }

    /// Sub-pixel slack: layout rounding routinely leaves content a fraction
    /// taller than the viewport with nothing actually hidden.
    public const float OverflowEpsilon = 1f;

    public static bool HasHiddenText(float contentHeight, float viewportHeight) =>
        contentHeight > viewportHeight + OverflowEpsilon;

    public static Target Resolve(bool hasInnerScroll, bool hasHiddenText, bool hasPageScroll)
    {
        if (hasInnerScroll && hasHiddenText) return Target.InnerText;
        if (hasPageScroll) return Target.Page;
        return hasInnerScroll ? Target.InnerText : Target.None;
    }
}
