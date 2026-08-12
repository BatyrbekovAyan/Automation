/// <summary>
/// Pure decision rules for the suggestions-panel ⇄ keyboard slot swap (sketch-003 variant A).
/// The slot is ONE region at the bottom of the screen with exactly one tenant — the native
/// keyboard or the suggestions panel — and the composer sits on whichever is up. These rules
/// keep the composer from moving during a tenant change (the "no-dip" invariant): the
/// controller holds the MovingArea inset (KeyboardAwarePanel.VirtualBottomInset) through the
/// handoff and releases it only when the incoming keyboard has actually taken over.
/// </summary>
public static class SuggestionSlotSwap
{
    /// <summary>The keyboard counts as "arrived" once it covers this fraction of the held inset.</summary>
    public const float ReleaseFraction = 0.95f;

    /// <summary>
    /// A keyboard SHORTER than the held slot never reaches the fraction — after this long the
    /// hold releases anyway and the composer settles down onto the real keyboard (a one-time
    /// correction; the measurement watcher then makes the next slot match exactly).
    /// </summary>
    public const float ReleaseTimeoutSeconds = 0.7f;

    /// <summary>
    /// Slot height for a panel open. Over a LIVE keyboard the slot must equal the live keyboard
    /// height — any other value moves the composer mid-swap; with no keyboard up, the remembered
    /// measurement (or its fallback) is the best stand-in.
    /// </summary>
    public static float SlotForOpen(bool keyboardVisible, float keyboardCanvasPx, float rememberedCanvasPx)
        => keyboardVisible && SuggestionSlotHeight.IsValid(keyboardCanvasPx)
            ? keyboardCanvasPx
            : rememberedCanvasPx;

    /// <summary>
    /// While the panel yields the slot to a rising keyboard, decide when the held inset may drop
    /// to the keyboard's own tracking. Never while the keyboard is absent — a keyboard that
    /// bounced away mid-handoff means the panel reinstates (controller rule), not that the
    /// composer falls.
    /// </summary>
    public static bool ShouldReleaseHold(
        bool keyboardVisible, float keyboardCanvasPx, float heldCanvasPx, float yieldingSeconds)
    {
        if (!keyboardVisible) return false;
        if (heldCanvasPx <= 0f) return true;
        if (keyboardCanvasPx >= heldCanvasPx * ReleaseFraction) return true;
        return yieldingSeconds >= ReleaseTimeoutSeconds;
    }
}
