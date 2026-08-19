using UnityEngine;

/// <summary>
/// Where the suggestions panel's empty / error block sits inside the card area.
/// <para>
/// The block is a fixed-size stack (heading + body + «Обновить»), and the area it lives in shrinks
/// all the way to nothing as the slot collapses. Those two facts cannot both be served by centring:
/// once the area is shorter than the block, a centred block hangs half of itself ABOVE the area —
/// over the panel's header and then over the composer, because nothing masks it (the RectMask2D
/// lives on the card Viewport and these overlays are its SIBLINGS, not its children).
/// </para>
/// <para>
/// So the rule has two regimes: centred while the block fits, pinned to the area's TOP once it does
/// not. Pinning is what makes a collapse read correctly — the block keeps its layout and slides down
/// with the panel, leaving through the screen bottom, instead of climbing over the chrome.
/// </para>
/// <para>
/// This replaces letting the block's own VerticalLayoutGroup absorb the shrink. It cannot: with
/// childControlHeight the group lerps its children from preferred toward MIN, and the two labels are
/// plain TMP with a min of 0 — so their rects collapsed to nothing while the glyphs kept drawing, and
/// «Нет предложений» and «Напишите ответ вручную» printed on top of each other (device 2026-08-19).
/// The «Обновить» pill was the only child that survived, because it is the only one carrying a
/// LayoutElement.minHeight.
/// </para>
/// </summary>
public static class SuggestionStateLayout
{
    /// <summary>
    /// Distance from the card area's TOP edge down to the block's top edge, canvas units.
    /// Never negative: that is half the point — the block may leave through the bottom, never
    /// through the top.
    /// <para>
    /// The other half is what counts as a MEASUREMENT. A block height that is zero or negative is
    /// not a short block, it is a rect whose ContentSizeFitter has not run yet — and a negative one
    /// is the normal state for exactly one frame, because converting the overlay off stretch anchors
    /// leaves the old inset sum (a negative number) sitting in sizeDelta.y until the fitter
    /// overwrites it. Centring against that pushes the block BELOW the area's middle, which shipped
    /// as "on first chat open the empty state sits almost at the bottom" (device 2026-08-20).
    /// So an unmeasured block pins to the top: wrong for one frame at the safe end, where the
    /// caller's next placement corrects it, instead of wrong at the end nobody expects.
    /// </para>
    /// </summary>
    public static float TopOffset(float areaHeightCanvasPx, float blockHeightCanvasPx)
    {
        if (!float.IsFinite(areaHeightCanvasPx) || !float.IsFinite(blockHeightCanvasPx)) return 0f;
        if (blockHeightCanvasPx <= 0f) return 0f;   // not measured yet — never "centre on nothing"
        return Mathf.Max(0f, (areaHeightCanvasPx - blockHeightCanvasPx) * 0.5f);
    }
}
