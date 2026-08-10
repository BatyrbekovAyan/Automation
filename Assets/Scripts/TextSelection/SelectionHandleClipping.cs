using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Works out the screen area a field's selection pins may draw in, so
/// SelectionOverlay can confine them to it with a RectMask2D.
///
/// The pins render on the selection overlay canvas, NOT inside the field, so
/// none of the field's own clipping applies to them: scroll a selection towards
/// the edge of a text card and the text is clipped away while the pins carry on
/// drawing at its position — a dot hanging over whatever sits above or below the
/// card.
/// </summary>
public static class SelectionHandleClipping
{
    private static readonly Vector3[] Corners = new Vector3[4];

    public static Rect Intersect(Rect a, Rect b) =>
        Rect.MinMaxRect(
            Mathf.Max(a.xMin, b.xMin),
            Mathf.Max(a.yMin, b.yMin),
            Mathf.Min(a.xMax, b.xMax),
            Mathf.Min(a.yMax, b.yMax));

    /// Empty (or inside-out, which is the shape a disjoint intersection
    /// produces when the field has scrolled off its page).
    public static bool IsEmpty(Rect rect) => rect.width <= 0f || rect.height <= 0f;

    /// <summary>
    /// The field's own box, narrowed by every RectMask2D ABOVE it — the card's
    /// clip and the page's, so a card scrolled off the tab takes its pins with
    /// it.
    ///
    /// Deliberately starts at the input, NOT at the text component: TMP's Text
    /// Area is inset from the field (40/32 on the Bot Settings cards) and
    /// carries its own RectMask2D, so measuring from the text cuts the pins
    /// well inside the visible card. The pins belong to the field, so the
    /// field's bounds are what may cut them.
    /// </summary>
    public static Rect VisibleScreenRect(TMP_InputField field)
    {
        if (field == null) return Rect.zero;

        var clip = ScreenRect((RectTransform)field.transform);

        for (var t = field.transform.parent; t != null; t = t.parent)
        {
            if (t.GetComponent<RectMask2D>() == null) continue;

            clip = Intersect(clip, ScreenRect((RectTransform)t));
            if (IsEmpty(clip)) return Rect.zero;
        }

        return IsEmpty(clip) ? Rect.zero : clip;
    }

    private static Rect ScreenRect(RectTransform rt)
    {
        rt.GetWorldCorners(Corners);
        var min = RectTransformUtility.WorldToScreenPoint(null, Corners[0]);
        var max = RectTransformUtility.WorldToScreenPoint(null, Corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}
