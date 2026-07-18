/// <summary>
/// Pure first-run gate for the onboarding carousel plus the existing-user
/// auto-flag rule. Booleans-in, boolean-out — no UnityEngine, no MonoBehaviour —
/// so it stays EditMode-unit-testable (analog: ChatRowSwipePolicy). The
/// MonoBehaviour supplies the facts (<c>botsParent.childCount &gt; 0</c>,
/// <c>PlayerPrefs.GetInt(OnboardingKeys.Seen, 0) == 1</c>) and acts on the verdict.
/// </summary>
public static class OnboardingGate
{
    /// <summary>
    /// First run only: show the carousel when there are NO bots AND the seen flag
    /// is unset. Any bot present ⇒ never (existing users skip onboarding entirely).
    /// </summary>
    public static bool ShouldShowCarousel(bool hasBots, bool seen) => !hasBots && !seen;

    /// <summary>
    /// Existing-user auto-flag: a user who already has bots but never saw the
    /// carousel should be flagged as seen (called at the end of Manager.LoadBots),
    /// so deleting all their bots later in the session never resurfaces it.
    /// </summary>
    public static bool ShouldAutoFlagSeen(bool hasBots, bool seen) => hasBots && !seen;
}
