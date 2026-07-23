/// <summary>
/// The «Первые шаги» card shows only when at least one bot exists and the
/// checklist has not been permanently completed. Zero bots ⇒ the EmptyState is the
/// step-1 guidance, so the card stays hidden (D1). 4/4 done ⇒ hidden forever.
///
/// Pure boolean predicate — no UnityEngine, no MonoBehaviour — so it stays
/// EditMode-unit-testable (analog: <see cref="OnboardingGate"/> /
/// <see cref="FirstStepsChecklist"/>). The MonoBehaviour supplies the facts
/// (<c>botsParent.childCount &gt; 0</c>, <c>PlayerPrefs OnboardingKeys.ChecklistDone</c>)
/// and acts on the verdict via a CanvasGroup.
/// </summary>
public static class FirstStepsCardVisibility
{
    public static bool ShouldShow(bool hasBots, bool checklistDone) => hasBots && !checklistDone;
}
