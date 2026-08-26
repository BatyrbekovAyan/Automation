using System;

/// <summary>
/// «Кончилась квота ⇒ бот переходит в «Вместе», а не молчит» — the client half of final-review
/// finding I-1 (owner decision 2026-08-26, spec §5.3).
///
/// The server already stops auto-replying once the monthly quota AND the purchased reserve are
/// spent: <c>Count Dialog</c>'s false branch dead-ends, so nothing is sent. Without this policy the
/// client showed nothing either — the suggestions panel renders only for chats whose stored reply
/// mode is «Вместе» — and the owner's bot simply went quiet, while the «Боты» meter's
/// <see cref="BotsPageRows.OverHint"/> promised the opposite in the same breath.
///
/// This is an EFFECTIVE-mode override, never a stored one: nothing here writes
/// <see cref="SemiAutoStore"/>, <c>ReplyModeToggleBinder</c> or the server's Set_Reply_Mode row.
/// The owner's chosen mode has to survive the quota reset (and a top-up) untouched — the account is
/// borrowing the panel for as long as it cannot pay for auto-replies, not being switched over.
///
/// Pure and Unity-free (<see cref="UsageSnapshot"/> is a plain data class), so every arm is
/// EditMode-testable — the house pattern for every billing decision in this milestone.
/// </summary>
public static class QuotaFallbackPolicy
{
    // Wire values of UsageSnapshot.status (the subscribers table's own column, echoed verbatim by
    // Get Usage's Shape Response). Kept beside the only seam that reads them, like
    // SubscriptionPageRows.IntervalMonth/IntervalYear.
    public const string StatusActive = "active";
    public const string StatusTrialing = "trialing";
    public const string StatusGrace = "grace";
    public const string StatusExpired = "expired";

    /// <summary>
    /// Whether the open chat should behave as «Вместе» regardless of its stored reply mode.
    /// <c>null</c> (no GetUsage read has landed yet) is UNKNOWN, never «zero usage» — a cold boot
    /// must not put every «Авто» chat into the fallback on the strength of a number we do not have.
    /// </summary>
    public static bool ShouldFallBackToSemi(UsageSnapshot usage)
        => usage != null
        && usage.success
        && ShouldFallBackToSemi(usage.status, usage.quota, usage.used, usage.topupBalance);

    /// <param name="reserve">
    /// <see cref="UsageSnapshot.topupBalance"/> — the REMAINING top-up reserve (owner decision
    /// 2026-08-26: a top-up is a reserve consumed one dialog at a time only once the monthly quota
    /// is gone, and it never expires). While any of it is left the bot still answers by itself, so
    /// there is nothing to fall back from.
    /// </param>
    public static bool ShouldFallBackToSemi(string status, int quota, int used, int reserve)
        => IsServiceable(status)
        && quota > 0            // no plan ⇒ no allowance to be "over"; «0 из 0» is a missing plan, not a wall
        && used >= quota
        && reserve <= 0;

    /// <summary>
    /// Whether the server will actually answer a suggestions request for this account. The
    /// Suggest_Replies gate (Task 17a) serves <see cref="StatusActive"/>/<see cref="StatusTrialing"/>/
    /// <see cref="StatusGrace"/> and refuses <see cref="StatusExpired"/> or an unknown id — so
    /// raising the panel for an expired account would buy the owner an error card instead of the
    /// silence it was meant to replace. An empty/unrecognised status is treated the same way: we
    /// only override a mode the owner chose when we are sure the substitute works.
    /// </summary>
    public static bool IsServiceable(string status)
        => Is(status, StatusActive) || Is(status, StatusTrialing) || Is(status, StatusGrace);

    private static bool Is(string value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
