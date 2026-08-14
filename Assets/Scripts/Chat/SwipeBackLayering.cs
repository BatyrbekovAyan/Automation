/// <summary>
/// Where the chat screen's left-edge back-swipe strip has to sit in MessagesPanel's child order.
///
/// uGUI awards a pointer to the LATER sibling, so the strip only ever receives a gesture over a
/// surface it out-orders. It used to live INSIDE MovingArea at index 4, below both the composer
/// (BottomPanel at index 5, whose Background is an opaque raycast-target skirt) and the
/// suggestions slot (SuggestionsPanel, a later sibling of MovingArea itself) — which is exactly
/// why a back-swipe starting on either of them did nothing: over the composer no drag handler
/// existed above it at all and the gesture died, over the slot the cards' ScrollRect claimed it
/// and refused a horizontal drag.
///
/// Raising it inside MovingArea would not fix it either: MovingArea rides the keyboard/slot inset
/// (KeyboardAwarePanel), so an open slot translates it — and any strip parented to it — clear of
/// the very region that has to be covered. The strip belongs to MessagesPanel, immediately after
/// the last content layer, and BEFORE the chrome (TopBar) and the modal overlays (photo/video
/// viewer, attachment preview, emoji picker, reaction bar) whose own gestures it must never
/// shadow.
///
/// Pure index math so the contract is unit-testable and shared by the wirer that applies it.
/// </summary>
public static class SwipeBackLayering
{
    /// <summary>Sibling index reported for a layer the scene does not have.</summary>
    public const int NotPresent = -1;

    /// <summary>
    /// The strip's index: one past the last content layer it must out-order. A scene whose
    /// suggestions panel has not been built yet (NotPresent) still gets a valid answer — one past
    /// MovingArea — and re-running this after the panel builder lands moves it up again.
    /// </summary>
    public static int TargetSiblingIndex(int movingAreaIndex, int suggestionsPanelIndex) =>
        (movingAreaIndex > suggestionsPanelIndex ? movingAreaIndex : suggestionsPanelIndex) + 1;

    /// <summary>True when the strip renders above every surface a back-swipe must work over.</summary>
    public static bool OutranksContent(int stripIndex, int movingAreaIndex, int suggestionsPanelIndex) =>
        stripIndex > movingAreaIndex &&
        (suggestionsPanelIndex == NotPresent || stripIndex > suggestionsPanelIndex);

    /// <summary>
    /// True when the strip still renders below the screen's chrome and modal overlays. Those own
    /// gestures of their own — the top bar's controls, SwipeToClose on the media viewers, the
    /// emoji sheet, the reaction bar — and a strip above them would swallow the left edge of all
    /// of it.
    ///
    /// Pass the LOWEST index among the chrome/overlay layers AS OBSERVED AFTER the strip has been
    /// placed. Inserting the strip pushes every later sibling one index down, so checking an
    /// intended index against where the chrome used to sit reads a correct placement as a
    /// collision and refuses it.
    /// </summary>
    public static bool StaysBelowChrome(int stripIndex, int lowestChromeIndex) =>
        lowestChromeIndex == NotPresent || stripIndex < lowestChromeIndex;
}
