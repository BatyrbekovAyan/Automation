/// <summary>What is currently occupying the bottom slot (sketch-005 winner E).</summary>
public enum SuggestionSlotState
{
    /// <summary>
    /// Slot height 0, composer flush to the screen bottom. No TAP ever reaches it — only a downward
    /// drag of the handle, or the two events that end the suggestions surface itself (AnsweredRun,
    /// ReplyModeOff).
    /// </summary>
    Collapsed,

    /// <summary>The suggestions panel at the standard (measured keyboard) height — the slot's DEFAULT tenant.</summary>
    Panel,

    /// <summary>The suggestions panel grown until every card is visible.</summary>
    Expanded,

    /// <summary>The native keyboard — it exists only while the composer field is focused.</summary>
    Keyboard
}

/// <summary>
/// Everything that can ask the slot to change tenant. Drags are not here — they resolve through
/// <see cref="SuggestionSlotStateMachine.AfterDrag"/>.
/// </summary>
public enum SuggestionSlotInput
{
    /// <summary>The owner tapped the composer input field.</summary>
    FieldTap,

    /// <summary>The owner tapped the message thread above the composer.</summary>
    ThreadTap,

    /// <summary>The owner tapped the morphing destination key at the field's end.</summary>
    KeyTap,

    /// <summary>The owner picked a suggestion card.</summary>
    Pick,

    /// <summary>The owner's own answer went out (the outgoing echo closes the run).</summary>
    AnsweredRun,

    /// <summary>A new client message arrived for the open chat.</summary>
    IncomingMessage,

    /// <summary>The native keyboard went away (platform callback or our own blur).</summary>
    KeyboardDismissed,

    /// <summary>The chat switched into «Вместе» — the suggestions surface becomes available.</summary>
    ReplyModeOn,

    /// <summary>The chat switched into «Авто» — the suggestions surface is suppressed entirely.</summary>
    ReplyModeOff
}

/// <summary>
/// The slot's transition table (sketch-005 winner E). The bottom slot holds exactly one tenant and
/// the suggestions panel — not the keyboard — is its DEFAULT one: the native keyboard exists only
/// while the composer field is focused, and no TAP ever collapses the slot — Collapsed is reached by
/// a downward drag of the handle (see <see cref="AfterDrag"/>) or by the two events that end the
/// suggestions surface itself, AnsweredRun and ReplyModeOff. This seam protects the rule a
/// controller keeps breaking by hand: a tap NEVER hides an open panel — raising is the only thing
/// taps do. A FieldTap focuses the field (and so summons the keyboard) from EVERY state, Collapsed
/// included: the owner revised the original two-step entry away on 2026-08-14, so the panel-raise
/// affordances from Collapsed are the thread tap, the ✦ key and the incoming-message auto-raise.
/// Pure: no MonoBehaviour, no input types, no clock — the controller feeds state + intent and
/// applies the returned flags.
/// </summary>
public static class SuggestionSlotStateMachine
{
    /// <summary>
    /// Resolve one intent against the current tenant. In «Авто» (<paramref name="semiAutoOn"/> false)
    /// there is no panel at all, so a Panel/Expanded state handed in is illegal input and is
    /// normalised to Collapsed rather than propagated.
    /// </summary>
    public static SlotTransition Resolve(SuggestionSlotState current, SuggestionSlotInput input, bool semiAutoOn)
    {
        SuggestionSlotState state = Normalise(current, semiAutoOn);
        return semiAutoOn ? ResolveSemiAuto(state, input) : ResolveAuto(state, input);
    }

    /// <summary>
    /// The tenant a finished drag leaves behind — the detent IS the state (Standard means the panel
    /// at keyboard height). An unrecognised detent resolves to the STANDARD one, never to Collapsed:
    /// SuggestionSlotDetents.HeightFor sizes that same value at the standard height, so collapsing
    /// here would leave the machine calling a standard-height slot "collapsed" — a disagreement
    /// nothing downstream can see. Garbage in this model always resolves upward (the Snap tie-break
    /// rule); only a deliberate drag may collapse.
    /// </summary>
    public static SuggestionSlotState AfterDrag(SlotDetent snapped)
    {
        switch (snapped)
        {
            case SlotDetent.Collapsed: return SuggestionSlotState.Collapsed;
            case SlotDetent.Expanded: return SuggestionSlotState.Expanded;
            default: return SuggestionSlotState.Panel;   // Standard + any out-of-range cast
        }
    }

    // --- «Вместе» -----------------------------------------------------------

    private static SlotTransition ResolveSemiAuto(SuggestionSlotState state, SuggestionSlotInput input)
    {
        switch (input)
        {
            case SuggestionSlotInput.FieldTap:
                // Owner revision 2026-08-14: tapping the field means «I want to type» from every
                // state, Collapsed included — the keyboard opens directly. (The original two-step
                // entry raised the panel first; it was dropped.)
                return ToFocused(SuggestionSlotState.Keyboard);

            case SuggestionSlotInput.ThreadTap:
                // An open panel NEVER hides from a thread tap; over a keyboard the tap is a dismiss.
                if (state == SuggestionSlotState.Keyboard) return ToBlurred(SuggestionSlotState.Panel);
                return To(state == SuggestionSlotState.Collapsed ? SuggestionSlotState.Panel : state);

            case SuggestionSlotInput.KeyTap:
                // The key shows the DESTINATION, so it always moves: panel ⇄ keyboard, or raise.
                if (state == SuggestionSlotState.Keyboard) return ToBlurred(SuggestionSlotState.Panel);
                if (state == SuggestionSlotState.Collapsed) return To(SuggestionSlotState.Panel);
                return ToFocused(SuggestionSlotState.Keyboard);

            case SuggestionSlotInput.Pick:
                // Locked 2026-08-11 flow: the panel stays open so a re-clustered variant is one tap
                // away, and a pick NEVER opens the keyboard. The panel leaves on the outgoing echo.
                return To(state);

            case SuggestionSlotInput.AnsweredRun:
                // Collapse, so the next incoming message can auto-raise the panel again.
                return To(SuggestionSlotState.Collapsed);

            case SuggestionSlotInput.IncomingMessage:
                // The ONLY auto-raise. Anything already up merely re-renders — nothing moves under
                // the owner's finger, least of all while they are typing.
                return state == SuggestionSlotState.Collapsed
                    ? To(SuggestionSlotState.Panel)
                    : RefreshOnly(state);

            case SuggestionSlotInput.KeyboardDismissed:
                // The panel is the slot's default tenant, so any blur hands the slot back to it.
                return To(state == SuggestionSlotState.Keyboard ? SuggestionSlotState.Panel : state);

            case SuggestionSlotInput.ReplyModeOn:
                return To(SuggestionSlotState.Panel);

            case SuggestionSlotInput.ReplyModeOff:
                return To(SuggestionSlotState.Collapsed);

            default:
                return To(state);   // out-of-range cast: inert, never becomes state
        }
    }

    // --- «Авто» (no panel exists; only Collapsed and Keyboard are reachable) --

    private static SlotTransition ResolveAuto(SuggestionSlotState state, SuggestionSlotInput input)
    {
        switch (input)
        {
            case SuggestionSlotInput.FieldTap:
                // Normal focus, ALWAYS — the two-step entry belongs to the panel, which is absent here.
                return ToFocused(SuggestionSlotState.Keyboard);

            case SuggestionSlotInput.ThreadTap:
                return state == SuggestionSlotState.Keyboard
                    ? ToBlurred(SuggestionSlotState.Collapsed)
                    : To(state);

            case SuggestionSlotInput.KeyTap:
                return To(state);   // the key is hidden in «Авто» — a stray call must be inert

            case SuggestionSlotInput.KeyboardDismissed:
                return To(state == SuggestionSlotState.Keyboard ? SuggestionSlotState.Collapsed : state);

            case SuggestionSlotInput.ReplyModeOn:
                return To(SuggestionSlotState.Panel);   // leaving «Авто» hands the slot to the panel

            case SuggestionSlotInput.ReplyModeOff:
                return To(SuggestionSlotState.Collapsed);

            default:
                // Pick / AnsweredRun / IncomingMessage have no surface to act on, and so does any
                // out-of-range cast.
                return To(state);
        }
    }

    /// <summary>
    /// Panel/Expanded cannot exist in «Авто», and an out-of-range cast is not a tenant — both read as
    /// Collapsed. Deliberately the opposite of <see cref="AfterDrag"/>'s upward fallback: a detent is
    /// a drag the owner just performed, so it must land on something, whereas an unknown tenant is a
    /// corrupt read — "nothing is up" is the honest answer, and raising a panel nobody asked for
    /// would put a surface over the thread on the strength of a bad value.
    /// </summary>
    private static SuggestionSlotState Normalise(SuggestionSlotState state, bool semiAutoOn)
    {
        switch (state)
        {
            case SuggestionSlotState.Collapsed:
            case SuggestionSlotState.Keyboard:
                return state;
            case SuggestionSlotState.Panel:
            case SuggestionSlotState.Expanded:
                return semiAutoOn ? state : SuggestionSlotState.Collapsed;
            default:
                return SuggestionSlotState.Collapsed;
        }
    }

    private static SlotTransition To(SuggestionSlotState state)
        => new SlotTransition(state, false, false, false);

    private static SlotTransition ToFocused(SuggestionSlotState state)
        => new SlotTransition(state, true, false, false);

    private static SlotTransition ToBlurred(SuggestionSlotState state)
        => new SlotTransition(state, false, true, false);

    private static SlotTransition RefreshOnly(SuggestionSlotState state)
        => new SlotTransition(state, false, false, true);
}

/// <summary>
/// One resolved slot move: where the slot ends up plus the side effects the controller owns.
/// The flags are mutually exclusive by construction — nothing focuses and blurs in the same move,
/// and a content refresh happens only where the tenant did not change.
/// </summary>
public readonly struct SlotTransition
{
    /// <summary>The tenant after the move (equal to the current one when the input was a no-op).</summary>
    public readonly SuggestionSlotState State;

    /// <summary>The controller must ActivateInputField — the keyboard is the destination.</summary>
    public readonly bool FocusField;

    /// <summary>The controller must dismiss the field so the keyboard leaves the slot.</summary>
    public readonly bool BlurField;

    /// <summary>State unchanged: re-render the cards in place and move nothing.</summary>
    public readonly bool ContentRefreshOnly;

    public SlotTransition(SuggestionSlotState state, bool focusField, bool blurField, bool contentRefreshOnly)
    {
        State = state;
        FocusField = focusField;
        BlurField = blurField;
        ContentRefreshOnly = contentRefreshOnly;
    }
}
