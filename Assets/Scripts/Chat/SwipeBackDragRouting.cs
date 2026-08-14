/// <summary>
/// Where a NON-horizontal drag that starts on the chat screen's left-edge back-swipe strip
/// belongs.
///
/// The strip is a transparent full-height band that renders ABOVE the thread, the composer and
/// the suggestions slot, so it wins the pointer raycast over whatever the user actually aimed at
/// — nothing beneath it ever sees the gesture. Forwarding every non-horizontal drag straight to
/// the message ScrollRect (what SwipeToBack used to do) was correct only while the strip covered
/// the thread alone: the moment it also spans the slot, a vertical drag on the suggestion cards
/// scrolls the MESSAGE LIST behind them instead of the cards.
///
/// Same class of bug and same fix as SwipeToBackBotSettings.ResolveVerticalTarget — extracted
/// here as a pure seam because that one lives in an untested inline branch, and generalised from
/// "first ScrollRect" to "first drag gesture": the slot's 42u grab strip
/// (SuggestionSlotDragHandle) and a sheet's drag zone (SheetDragDismiss) are plain drag handlers,
/// not ScrollRects, and a ScrollRect-only rule would leave both dead under the band.
/// </summary>
public static class SwipeBackDragRouting
{
    public enum VerticalTarget
    {
        /// <summary>Nothing under the finger at all — an unwired or degenerate scene (and the
        /// EditMode case, where an unrendered canvas returns no hits). Keep the pre-strip
        /// behaviour and drive the thread, rather than silently killing list scrolling.</summary>
        ThreadFallback,

        /// <summary>Forward to whatever the finger is actually on: the thread ScrollRect, the
        /// suggestion cards' ScrollRect, the slot's grab handle, a sheet's drag zone.</summary>
        UnderFinger,

        /// <summary>The finger is on a surface that owns no drag gesture — the composer bar, the
        /// panel's header. It must not scroll, and the thread hidden BEHIND it must not scroll in
        /// its place: uGUI's RaycastAll has no occlusion, so every layer under the finger is in
        /// the hit list whether or not an opaque graphic covers it. Consulting only the FIRST
        /// foreign hit is what keeps "this surface doesn't scroll" from silently becoming "scroll
        /// something the user cannot even see".</summary>
        None
    }

    /// <param name="hasForeignHit">The raycast found at least one hit that is not the strip itself.</param>
    /// <param name="topHitOwnsADragGesture">That top-most foreign hit resolves to a drag handler.</param>
    public static VerticalTarget Resolve(bool hasForeignHit, bool topHitOwnsADragGesture)
    {
        if (!hasForeignHit) return VerticalTarget.ThreadFallback;
        return topHitOwnsADragGesture ? VerticalTarget.UnderFinger : VerticalTarget.None;
    }
}
