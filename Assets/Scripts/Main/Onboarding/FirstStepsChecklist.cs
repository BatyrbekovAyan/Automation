/// <summary>
/// Pure derivation for the «Первые шаги» checklist card: channel label, per-step
/// states, and 4/4 completion (analog: DashboardMetrics + ChatRowSwipePolicy).
/// Takes primitive facts and returns primitives — no persisted-state reads, no MonoBehaviour —
/// so the whole card logic stays EditMode-unit-testable.
/// </summary>
public static class FirstStepsChecklist
{
    /// <summary>
    /// The four checklist step states in copy-deck order:
    /// create bot · connect channel · upload price list · first reply.
    /// </summary>
    public static bool[] StepStates(bool botExists, bool channelAuthed,
                                    bool hasFiles, bool firstReplySeen)
        => new[] { botExists, channelAuthed, hasFiles, firstReplySeen };

    /// <summary>
    /// True when every step is done. Uses <c>Array.TrueForAll</c> semantics, so an
    /// empty array is vacuously true — the card only latches
    /// <c>OnboardingKeys.ChecklistDone</c> at a real 4/4.
    /// </summary>
    public static bool AllDone(bool[] steps) => System.Array.TrueForAll(steps, s => s);
}
