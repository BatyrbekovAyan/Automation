using UnityEngine;

/// <summary>
/// Pure geometry seam for the app's three confirm cards — the shared chats
/// «Авто» popup (Screen_Messanger/ReplyModeConfirmPopup), the bots-page twin
/// built by <see cref="BotActivationConfirm"/>, and DeleteChatConfirmPanel
/// (<see cref="ChatDeleteConfirm"/>).
///
/// THE RULE PRESERVES AUTHORED GAPS, SO THE AUTHORED GAPS MUST BE RIGHT. This is
/// not a caveat, it is the precondition — and DeleteChatConfirmPanel is what
/// proved it. That card shipped with its Body box at -150..-290 against buttons
/// whose top edge is at -280: ten units of overlap, authored. Grow-by-overflow
/// would have reproduced that overlap faithfully at every size (a four-line body
/// grows the box to 158 and the card to 478, and the text still lands ~10u over
/// the buttons), so calling Fit on it would have looked like a fix and been
/// none. The scene was corrected first — Body height 140 → 86, which is two
/// 39.36u lines plus slack and leaves the same 44u clearance the chats popup was
/// authored with — and only then was the fit applied. If a fourth card ever
/// joins, check its authored clearance BEFORE wiring it up; ConfirmCardScenePremiseTests
/// is where that check belongs.
///
/// All three were authored at a FIXED height with the title, body and buttons
/// at absolute offsets: title top-anchored at a fixed y with a fixed box, body
/// top-anchored below it, buttons bottom-anchored. That is correct exactly as
/// long as every string fits the box it was measured for. It stopped being
/// correct when the per-chat copy arrived: «Включить авто-режим в этом чате?»
/// is 825u wide at 42pt Bold in a 640u column, so it wraps to two lines
/// (2 × 50.12u = 100.24u) inside a 64u box whose TMP overflow mode is Overflow —
/// the second line simply drew over the body, which never moved
/// (device 2026-09-04, iPhone 17 Pro Max).
///
/// The rule here is GROW BY OVERFLOW, not re-layout: each text box keeps its
/// authored height as a MINIMUM and only grows by however much its text
/// actually overflows; the body slides down by the title's growth and the card
/// grows by the sum. Two consequences worth keeping:
///
///   • A string that already fits produces ZERO movement, so every copy that
///     looks right today is byte-identical afterwards. The short header title
///     (546u ⇒ 1 line ⇒ 50.12u ≤ 64u) and both 3-line bodies (121.72u ≤ 130u)
///     are in that set — but the per-chat body only just: its longest wrapped
///     line measures 638.9u in a 640u column, 1.1u of margin. Editing that
///     sentence by one word tips it to four lines and the card legitimately
///     grows to 473u. That is the rule working, not a regression.
///   • The authored gaps survive. The card is centre-pivoted while the texts
///     are top-anchored and the buttons bottom-anchored, so growing the card by
///     the same amount the body moved down keeps the body-to-button clearance
///     exactly as authored (44u on the chats popup, and 44u on the delete popup
///     once its box was corrected — at one line and at five alike).
///
/// Flat Assets/Scripts/UI/ pure-seam style (AutoButtonModel / ChannelSwitcherModel
/// precedent): no namespace, no MonoBehaviour, so the matrix is EditMode-testable.
/// The Unity-side applier is <see cref="ConfirmCardFitter"/>.
/// </summary>
public static class ConfirmCardLayout
{
    /// <summary>Solved absolute geometry — all values are final, not deltas.</summary>
    public readonly struct Geometry
    {
        /// <summary>Height for the title's RectTransform (top-anchored box).</summary>
        public readonly float TitleHeight;
        /// <summary>anchoredPosition.y for the body's RectTransform.</summary>
        public readonly float BodyY;
        /// <summary>Height for the body's RectTransform.</summary>
        public readonly float BodyHeight;
        /// <summary>Height for the card's RectTransform.</summary>
        public readonly float CardHeight;

        public Geometry(float titleHeight, float bodyY, float bodyHeight, float cardHeight)
        {
            TitleHeight = titleHeight;
            BodyY = bodyY;
            BodyHeight = bodyHeight;
            CardHeight = cardHeight;
        }
    }

    /// <summary>
    /// Solve the card's geometry for one pair of measured texts.
    ///
    /// Every "authored" argument is the value the scene/builder shipped — the
    /// caller must pass the ORIGINAL baseline every time, never the previously
    /// solved output, or repeated shows would compound the growth. Passing the
    /// baseline also makes the solve idempotent: same inputs ⇒ same outputs.
    ///
    /// A preferred height that is not a usable positive number (0 from an empty
    /// string, or NaN/Infinity from a TMP call made before the component was
    /// initialised) is read as "no measurement" and produces no growth — the
    /// card then simply stays exactly as authored, which is the safe direction.
    /// </summary>
    public static Geometry Solve(
        float authoredCardHeight,
        float authoredTitleHeight, float titlePreferredHeight,
        float authoredBodyY, float authoredBodyHeight, float bodyPreferredHeight)
    {
        float titleHeight = Grown(authoredTitleHeight, titlePreferredHeight);
        float bodyHeight = Grown(authoredBodyHeight, bodyPreferredHeight);

        float titleGrowth = titleHeight - authoredTitleHeight;
        float bodyGrowth = bodyHeight - authoredBodyHeight;

        // The body is top-anchored, so pushing it down means a MORE negative y.
        float bodyY = authoredBodyY - titleGrowth;

        // The card is centre-pivoted: growing it by the total pushed-down
        // distance keeps the bottom-anchored buttons the same distance below
        // the body as they were authored.
        float cardHeight = authoredCardHeight + titleGrowth + bodyGrowth;

        return new Geometry(titleHeight, bodyY, bodyHeight, cardHeight);
    }

    /// <summary>
    /// The authored box height, grown to whole units when the measured text
    /// overflows it. Ceil rather than the raw float so a sub-unit measurement
    /// difference between platforms cannot leave a hairline of the last line
    /// clipped, and so the applied values stay stable across shows.
    /// </summary>
    private static float Grown(float authoredHeight, float preferredHeight)
    {
        if (!IsMeasured(preferredHeight)) return authoredHeight;
        return Mathf.Max(authoredHeight, Mathf.Ceil(preferredHeight));
    }

    /// <summary>True when a TMP preferred height is a usable measurement.</summary>
    public static bool IsMeasured(float preferredHeight) =>
        preferredHeight > 0f && !float.IsNaN(preferredHeight) && !float.IsInfinity(preferredHeight);
}
