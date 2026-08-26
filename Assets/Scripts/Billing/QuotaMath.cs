using System;

/// <summary>
/// What the dialog meter is saying. <see cref="Reserve"/> is APPENDED rather than slotted between
/// <see cref="Warn"/> and <see cref="Over"/> so the ordinals stay stable (the ThemeRole append-only
/// habit — nothing serialises this enum today, and nothing should start by accident).
/// </summary>
public enum QuotaState
{
    Ok,
    Warn,
    /// <summary>Ceiling spent: monthly quota gone AND the reserve empty. The only real wall.</summary>
    Over,
    /// <summary>
    /// The monthly quota is spent and the purchased reserve is paying (owner decision 2026-08-26).
    /// The bot still answers by itself, so this must never be painted as <see cref="Over"/>.
    /// </summary>
    Reserve,
}

/// <summary>
/// Dialog-allowance arithmetic under RESERVE semantics (owner decision 2026-08-26, spec §2 add-on
/// note): a top-up is not added to the monthly quota — it is a reserve spent one dialog at a time
/// once the quota is gone, and it never expires.
///
/// Two facts about the wire numbers this file is built on (Get Usage's Shape Response):
/// <c>quota</c> is always the BASE plan quota, and <c>used</c> counts EVERY dialog this month,
/// including the ones the reserve paid for — so over-quota usage raises <c>used</c> and lowers
/// <c>topupBalance</c> at the same time.
/// </summary>
public static class QuotaMath
{
    public static int Percent(int used, int quota)
        => quota <= 0 ? 100 : Math.Min(100, (int)Math.Floor(used * 100.0 / quota));

    /// <summary>
    /// Dialogs left before the wall: whatever is left of the monthly quota, plus the whole
    /// remaining reserve.
    ///
    /// NOT <c>quota + reserve - used</c>: those two move in OPPOSITE directions once the quota is
    /// gone, so that expression closed the gap twice per dialog and reported an empty tank with
    /// half the reserve still unspent (quota 1 000 + reserve 500 read as exhausted at used 1 250).
    /// </summary>
    public static int Remaining(int used, int quota, int reserve)
        => Math.Max(0, quota - used) + Math.Max(0, reserve);

    /// <param name="reserve">Remaining top-up balance (<see cref="UsageSnapshot.topupBalance"/>).</param>
    public static QuotaState State(int used, int quota, int reserve)
    {
        if (used >= quota) return reserve > 0 ? QuotaState.Reserve : QuotaState.Over;
        return Percent(used, quota) >= PlanCatalog.WarnThresholdPercent
            ? QuotaState.Warn
            : QuotaState.Ok;
    }
}
