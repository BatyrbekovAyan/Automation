/// <summary>
/// When the suggestions panel is allowed to take the keyboard slot relative to the CHAT-OPEN
/// choreography (Prep → Slide → Populate → Idle, see <see cref="ChatManager.ChatOpenPhase"/>).
///
/// <para>Why this exists: <c>ChatManager.SelectChat</c> fires <c>OnChatSelected</c> synchronously
/// on the row tap, and <c>SuggestionsController.RestoreForActiveChat</c> claims the slot right
/// there — a full 300 ms of Prep plus the ~290 ms horizontal slide before the chat screen is even
/// active. The claim is invisible while it is made (MessagesPanel is deactivated, so
/// <c>KeyboardAwarePanel.Update</c> never runs and its OnDisable already reset the applied rise to
/// rest) and then lands ALL AT ONCE on the slide's first frame: the composer + panel animate
/// upward over exactly the same frames the chat travels right-to-left, which reads as the chat
/// opening diagonally (device report 2026-08-18).</para>
///
/// <para>So the rise is not "started too early" — it is APPLIED at the wrong moment. The fix is to
/// hold the claim until the open has finished, which is what these rules describe.</para>
/// </summary>
public static class SuggestionSlotOpenTiming
{
    /// <summary>
    /// Beat between the chat-open animation finishing and the panel starting to rise. Not merely
    /// cosmetic separation: <c>PopulateBubbles</c> runs inside the slide's own onComplete, so the
    /// thread's first batch is instantiated and laid out on the very frames right after the slide
    /// ends — starting the slot tween into that would both stutter the rise and put two motions on
    /// screen at once, which is the complaint this whole seam answers.
    /// </summary>
    public const float SettleDelaySeconds = 0.12f;

    /// <summary>
    /// The chat-open animation is over. Mirrors the park condition in
    /// <c>ChatManager.SyncLatestMessages</c> — Populate and Idle are both "settled" (Populate is
    /// only ever reached with the panel already at rest), and the slide gate is checked separately
    /// because <c>SwipeToBack.IsSliding</c> is lowered AFTER the onComplete that advances the phase.
    /// </summary>
    public static bool ChatOpenSettled(ChatManager.ChatOpenPhase phase, bool isSliding)
        => !isSliding
           && (phase == ChatManager.ChatOpenPhase.Populate || phase == ChatManager.ChatOpenPhase.Idle);

    /// <summary>
    /// May the panel take the slot now? <paramref name="secondsSinceSettled"/> is negative while
    /// the open is still in flight (no settle instant recorded yet) and must never be read as an
    /// elapsed beat — a sentinel that passed the delay check would defeat the whole gate.
    /// </summary>
    public static bool MayTakeSlot(ChatManager.ChatOpenPhase phase, bool isSliding, float secondsSinceSettled)
        => secondsSinceSettled >= 0f
           && ChatOpenSettled(phase, isSliding)
           && secondsSinceSettled >= SettleDelaySeconds;
}
