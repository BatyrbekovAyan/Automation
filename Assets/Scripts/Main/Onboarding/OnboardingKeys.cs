/// <summary>
/// The three GLOBAL PlayerPrefs keys the first-run onboarding flow reads/writes.
/// These sit OUTSIDE the per-bot "BotN…" namespace (verified collision-free —
/// RESEARCH §Runtime State Inventory) and are wiped by PlayerPrefs.DeleteAll()
/// in the «Удалить все данные» flow by design.
///
/// Centralised as const strings (analog: Bot.UnauthedProfileSentinel) so callers
/// never hand-type a key literal.
/// </summary>
public static class OnboardingKeys
{
    /// <summary>Set to 1 once the first-run carousel has been seen (or auto-flagged for existing users).</summary>
    public const string Seen = "OnboardingSeen";

    /// <summary>Set to 1 once the «Первые шаги» checklist reaches 4/4 (never resurfaces after).</summary>
    public const string ChecklistDone = "OnboardingChecklistDone";

    /// <summary>Set to 1 once the bot has sent its first reply (global latch; isIncoming==false proxy).</summary>
    public const string FirstBotReplySeen = "FirstBotReplySeen";
}
