using TMPro;
using UnityEngine;

/// <summary>
/// Applies <see cref="ConfirmCardLayout"/> to a live confirm card: measures the
/// title and body at their real text width and writes the solved rects back.
///
/// Deliberately NOT a MonoBehaviour and NOT a scene object. The chats popups are
/// hand-tuned nodes in Main.unity and the project's rule is that the scene is
/// the source of truth — adding a component there would be a scene mutation a
/// future builder run could silently revert, and it buys nothing: every caller
/// already holds, or can resolve, the references the fit needs. So the whole fix
/// ships as code.
///
/// The TITLE IS OPTIONAL. ChatDeleteConfirm serializes only its body, because
/// its title is the fixed «Удалить чат?» — one line in a 70u box, in every
/// locale this app ships (one). A null title contributes zero authored height
/// and zero measured height, so the solve reduces to body-only growth and the
/// two «Авто» popups, which do pass a title, are unaffected.
///
/// TWO ORDERING RULES, both load-bearing:
///
///   1. FIT AFTER THE POPUP IS ACTIVE. TMP resolves its text into
///      m_TextProcessingArray inside ParseInputText, and a component whose
///      GameObject has never been active has not run its own initialisation —
///      CalculatePreferredValues then takes the "no text to generate" early
///      return and reports 0. Every caller therefore shows the popup first and
///      fit immediately afterwards, in the same frame and well before the first
///      render, so nothing is ever seen at the wrong size.
///   2. MEASURE AT AN EXPLICIT WIDTH. TMP's parameterless preferredHeight falls
///      back to an unbounded width whenever its own m_marginWidth is still 0,
///      which is exactly the state on the activation frame — it would report a
///      single unwrapped line and defeat the whole fit. The width is read from
///      the text's own RectTransform, which is valid without a layout pass in
///      both shapes this fit meets: the chats popup stretch-anchors its texts,
///      so the rect derives its width from the card rather than from a layout
///      group, and the delete popup gives them a fixed 760u sizeDelta.x. What
///      would NOT be safe is a text sized by a layout group.
/// </summary>
public static class ConfirmCardFitter
{
    /// <summary>
    /// TMP's own "unbounded" margin value (TMP_Math.FLOAT_MAX). Passed as the
    /// measurement height so the vertical margin can never truncate the result.
    /// </summary>
    private const float Unbounded = 32767f;

    /// <summary>
    /// The card's authored geometry, captured from the scene/builder on the
    /// first fit and re-used forever after. Callers own one of these per card
    /// and pass it by ref — the solve must always start from the ORIGINAL
    /// values, never from the previous result, or growth would compound.
    /// </summary>
    public struct Baseline
    {
        internal bool captured;
        internal float cardHeight;
        internal float titleHeight;
        internal float bodyY;
        internal float bodyHeight;

        /// <summary>True once the authored geometry has been read off the card.</summary>
        public bool Captured => captured;
    }

    /// <summary>
    /// Read the card's authored geometry into <paramref name="baseline"/>, once.
    /// Call this while the card is still untouched — at wire/build time, before
    /// any <see cref="Fit"/> can have grown it. <see cref="Fit"/> falls back to
    /// capturing on its first run, but that fallback would read a GROWN card if
    /// the owner of the baseline were ever recreated while the card outlived it,
    /// and the card would then ratchet a little larger on every subsequent show.
    /// </summary>
    public static void Capture(RectTransform card, TextMeshProUGUI title, TextMeshProUGUI body,
        ref Baseline baseline)
    {
        if (baseline.captured) return;
        if (card == null || body == null) return;

        baseline.cardHeight = card.sizeDelta.y;
        // A card whose title is fixed copy needs no title reference at all, and
        // ChatDeleteConfirm deliberately has none: «Удалить чат?» is 304u wide
        // in a 760u column, so it is one 54.12u line inside a 70u box forever.
        // Zero authored height makes Solve's title term vanish arithmetically
        // (Grown(0, 0) is 0, so titleGrowth is 0) rather than needing a branch.
        baseline.titleHeight = title != null ? title.rectTransform.sizeDelta.y : 0f;
        baseline.bodyY = body.rectTransform.anchoredPosition.y;
        baseline.bodyHeight = body.rectTransform.sizeDelta.y;
        baseline.captured = true;
    }

    /// <summary>
    /// Measure <paramref name="title"/> and <paramref name="body"/> and resize
    /// the card so a wrapped title pushes the body down instead of drawing over
    /// it. Safe to call on every show: the solve runs from the captured
    /// baseline, so it is idempotent. A null argument is a no-op.
    /// </summary>
    public static void Fit(RectTransform card, TextMeshProUGUI title, TextMeshProUGUI body,
        ref Baseline baseline)
    {
        if (card == null || body == null) return;

        RectTransform bodyRt = body.rectTransform;

        Capture(card, title, body, ref baseline);

        ConfirmCardLayout.Geometry solved = ConfirmCardLayout.Solve(
            baseline.cardHeight,
            baseline.titleHeight, PreferredHeight(title),
            baseline.bodyY, baseline.bodyHeight, PreferredHeight(body));

        // Width is never touched — only the y components move. The chats popup
        // insets its texts from the card's edges (a stretch anchor, so
        // sizeDelta.x is negative); the delete popup gives them a fixed 760u
        // width. Both are authored, and neither is this fit's business.
        if (title != null)
        {
            RectTransform titleRt = title.rectTransform;
            titleRt.sizeDelta = new Vector2(titleRt.sizeDelta.x, solved.TitleHeight);
        }

        bodyRt.sizeDelta = new Vector2(bodyRt.sizeDelta.x, solved.BodyHeight);
        bodyRt.anchoredPosition = new Vector2(bodyRt.anchoredPosition.x, solved.BodyY);

        // Last, so the texts are already sized when the card's resize notifies
        // its children. The card's ImageWithRoundedCorners repaints itself from
        // OnRectTransformDimensionsChange, and this card is not under a stencil
        // Mask, so no RoundedCornerMaskSync is needed here.
        card.sizeDelta = new Vector2(card.sizeDelta.x, solved.CardHeight);
    }

    /// <summary>
    /// TMP's preferred height for the text it currently holds, wrapped at its
    /// own rect width. Returns 0 (read by the seam as "no measurement", hence
    /// no growth) when there is nothing to measure or the rect has no usable
    /// width yet.
    ///
    /// Deliberately the 2-arg overload. The 3-arg `GetPreferredValues(string, …)`
    /// routes through SetTextInternal, which repopulates TMP's processing arrays
    /// from the string it was handed and never puts the old content back — so a
    /// future caller who measured a string BEFORE assigning it would leave the
    /// component holding text it is not showing. The 2-arg form re-derives from
    /// m_text via ParseInputText, so the arrays can never disagree with the text.
    /// </summary>
    private static float PreferredHeight(TextMeshProUGUI tmp)
    {
        if (tmp == null || string.IsNullOrEmpty(tmp.text)) return 0f;

        // A text whose GameObject is not active has never initialised, and TMP then reports a
        // small positive number (≈ one tenth of the real height) rather than 0 — enough to pass
        // IsMeasured and leave the card silently un-fitted (review, 2026-09-05). The state is
        // exactly "Fit ran before PopupUI.Show", so refuse it and say so.
        if (!tmp.isActiveAndEnabled)
            return WarnUnmeasured(tmp, "its GameObject is not active — fit after PopupUI.Show");

        float width = tmp.rectTransform.rect.width;
        if (width <= 1f) return WarnUnmeasured(tmp, "its rect has no width yet");

        float preferred = tmp.GetPreferredValues(width, Unbounded).y;
        if (!ConfirmCardLayout.IsMeasured(preferred))
            return WarnUnmeasured(tmp, "TextMeshPro returned no preferred height");

        return preferred;
    }

    /// <summary>
    /// A failed measurement fails OPEN — the card stays exactly as authored,
    /// which is also precisely what the overlap bug looks like. That makes the
    /// two states indistinguishable on screen, so say so in the editor and in
    /// development builds rather than letting the fix quietly stop working.
    /// </summary>
    private static float WarnUnmeasured(TextMeshProUGUI tmp, string why)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning($"[ConfirmCardFitter] Could not measure '{tmp.name}' ({why}) — " +
                         "the confirm card stays at its authored size, so long copy may overlap. " +
                         "The fit must run AFTER PopupUI.Show has activated the popup.", tmp);
#endif
        return 0f;
    }
}
