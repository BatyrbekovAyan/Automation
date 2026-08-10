using UnityEngine;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Pure sizing math for ScrollableTextArea, split out so the measure
    /// width — the input that decides whether a card believes it has
    /// anything to scroll — is pinned by tests.
    /// </summary>
    public static class ScrollableTextAreaMetrics
    {
        /// A RectTransform inside a ScrollRect reports a ~2px width until
        /// layout settles; anything below this is pre-layout noise, not a
        /// real column width. (Guard at 100, not >1 — a couple of pixels of
        /// width measures a 6000px-tall paragraph.)
        public const float MinMeasurableWidth = 100f;

        public static bool WidthSettled(float layoutWidth) =>
            layoutWidth >= MinMeasurableWidth;

        /// <summary>
        /// Width to wrap-measure the text at. This must be the width the TEXT
        /// lays out in, NOT the card's: TMP's Text Area is inset from the card
        /// (40px per side on the Bot Settings cards), so measuring at the card
        /// width fits more characters per line than the field really does,
        /// under-counts wrapped lines, and leaves the scroll content shorter
        /// than the text. The tail is then unreachable AND the card reports
        /// "nothing hidden", so DragShield hands the drag to the page.
        /// Falls back to the card width while the text rect is still
        /// pre-layout, which is what the old code always used.
        /// </summary>
        public static float MeasureWidth(float textLayoutWidth, float viewportWidth) =>
            WidthSettled(textLayoutWidth) ? textLayoutWidth : viewportWidth;

        /// <summary>
        /// How tall the scroll content (the TMP_InputField's own rect) must be.
        ///
        /// <paramref name="chromeHeight"/> is the vertical space inside the
        /// content that is NOT text column — TMP's Text Area is inset from the
        /// input (32 top + 32 bottom on the Bot Settings cards). Leave it out
        /// and the text viewport ends up that much shorter than the text, so
        /// TMP starts scrolling the text INTERNALLY to keep the caret in view:
        /// the first row is then pushed above the viewport and no amount of
        /// scrolling our ScrollRect brings it back, because the offset lives on
        /// the text component, not on the content.
        ///
        /// Content is never shorter than the viewport either — a short text
        /// must still fill the card so DragShield covers the whole visible area.
        /// </summary>
        public static float ContentHeight(
            float measuredTextHeight, float chromeHeight, float bottomPadding, float viewportHeight) =>
            Mathf.Max(viewportHeight, measuredTextHeight + Mathf.Max(0f, chromeHeight) + bottomPadding);
    }
}
