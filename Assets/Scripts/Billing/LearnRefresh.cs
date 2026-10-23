using System;
using System.Collections;
using UnityEngine;

/// <summary>What <see cref="LearnRefresh.Decide"/> answers for one learning send.</summary>
public enum LearnRefreshStep
{
    /// <summary>The minute is free: re-read now (after <see cref="LearnRefresh.DelaySeconds"/>) and start a new minute.</summary>
    FetchNow,

    /// <summary>Inside the minute, nothing pending: one trailing re-read the moment the minute ends.</summary>
    ScheduleTrailing,

    /// <summary>Inside the minute, a trailing re-read is already pending — it will count this send too.</summary>
    Skip,
}

/// <summary>
/// Keeps the learning percent moving while the owner answers clients (spec 2026-10-22 §4.1 «Обновление во время работы»).
///
/// The «Авто» chips paint the percent that Get Usage reports (<c>UsageStore</c> → <c>AutoReadinessPolicy.StateFor</c>), and
/// the app reads Get Usage at boot and when a few screens open — never while the owner sits in a chat picking a card and
/// sending it, which is exactly when the percent grows. After a SUCCESSFUL send that carried a shown suggestion set
/// (<c>SendOrigin.kind != None</c>; wired in <c>ChatManager.PostTextMessageRoutine</c>, which a Telegram reply and a retry
/// from the outbox go through too) this re-reads the snapshot <see cref="DelaySeconds"/> later — the proxy records the send
/// AFTER it has answered the app (≈ 2 s on prod), so an immediate read would not count it — and never more than once per
/// <see cref="MinIntervalMs"/> for the whole app (<see cref="ShouldFetch"/>, the <see cref="ChannelHealth.ShouldRun"/> rule).
/// A burst inside the minute is not dropped: the first send of the burst that lands inside the minute schedules ONE trailing
/// re-read for the moment the minute ends, later ones change nothing while it is pending, and when it fires it stamps the
/// throttle like any read — so the answers of the minute show right after it, still at most one Get Usage a minute. The whole
/// decision is the pure <see cref="Decide"/>.
///
/// The request is hosted on <see cref="Manager.Instance"/>, never on a component of the chat: Unity stops a coroutine whose
/// host goes inactive, and Manager is the always-active host that already runs the boot fetch
/// (<c>PaywallController.RefetchUsage</c> hosts its re-read the same way). Every wait is a <c>WaitForSecondsRealtime</c>, the
/// codebase's idiom. Applying the snapshot, the single-flight guard and the failure handling all stay in
/// <see cref="UsageClient.FetchRoutine"/>. Pinned by LearnRefreshTests.
/// </summary>
public static class LearnRefresh
{
    /// <summary>At most one re-read a minute for the whole app.</summary>
    public const long MinIntervalMs = 60L * 1000;

    /// <summary>The pause before the request, so the server has recorded the send that the snapshot is meant to count.</summary>
    public const float DelaySeconds = 5f;

    /// <summary>
    /// How long past its due time a pending trailing re-read still counts as pending (2 × <see cref="DelaySeconds"/>). A late
    /// one is about to fire; one older than that lost its coroutine and must not block the re-reads forever.
    /// </summary>
    public const long TrailingGraceMs = (long)(2 * DelaySeconds * 1000);

    // When the last re-read was scheduled, Unix ms — the throttle's stamp. In memory only (the ChannelHealth stamp's rule): a
    // new launch reads usage at boot anyway, and 0 means «never».
    private static long _lastScheduledMs;

    // When the pending trailing re-read is due, Unix ms; 0 = none pending.
    private static long _trailingDueMs;

    /// <summary>
    /// Never twice within <see cref="MinIntervalMs"/>; a stamp from the future (the device clock moved back) never blocks.
    /// </summary>
    public static bool ShouldFetch(long nowMs, long lastMs)
        => lastMs <= 0 || nowMs - lastMs >= MinIntervalMs || nowMs < lastMs;

    /// <summary>
    /// The throttle's whole decision for one learning send at <paramref name="nowMs"/>, given the last re-read's stamp and the
    /// pending trailing re-read's due time (0 = none). A pending one answers <see cref="LearnRefreshStep.Skip"/> — it will count
    /// this send. Otherwise <see cref="ShouldFetch"/> decides: a free minute (never read, a minute later, the clock moved back)
    /// is <see cref="LearnRefreshStep.FetchNow"/>, an occupied one schedules the trailing re-read at <c>lastMs +</c>
    /// <see cref="MinIntervalMs"/> — <c>dueMs</c>, 0 for the other two steps.
    /// </summary>
    public static (LearnRefreshStep step, long dueMs) Decide(long nowMs, long lastMs, long trailingDueMs)
    {
        if (TrailingPending(nowMs, trailingDueMs)) return (LearnRefreshStep.Skip, 0L);
        return ShouldFetch(nowMs, lastMs)
            ? (LearnRefreshStep.FetchNow, 0L)
            : (LearnRefreshStep.ScheduleTrailing, lastMs + MinIntervalMs);
    }

    /// <summary>
    /// Call once a send that carried a shown suggestion set has been acknowledged. Throttled by <see cref="Decide"/>; every
    /// request runs <see cref="DelaySeconds"/> after its moment on the Manager, so this returns at once.
    /// </summary>
    public static void AfterLearningSend()
    {
        if (Manager.Instance == null) return;   // EditMode / early boot: no host to run it, so the minute is not spent either
        long now = NowMs();
        (LearnRefreshStep step, long dueMs) = Decide(now, _lastScheduledMs, _trailingDueMs);
        if (step == LearnRefreshStep.Skip) return;
        if (step == LearnRefreshStep.FetchNow) _lastScheduledMs = now;   // the minute starts with this read
        _trailingDueMs = dueMs;   // the trailing read's due time; a read now forgets any stale one
        Manager.Instance.StartCoroutine(ReadAfter(dueMs));
    }

    // A pending trailing read: due within the next minute (a due further ahead means the clock moved back) and not past its grace.
    private static bool TrailingPending(long nowMs, long trailingDueMs)
        => trailingDueMs > 0 && trailingDueMs - nowMs <= MinIntervalMs && nowMs <= trailingDueMs + TrailingGraceMs;

    // trailingDueMs = 0: a read now. Otherwise wait for the minute to end first, and only the read still pending fires — one that
    // outlived its grace was replaced, and must not make a second request.
    private static IEnumerator ReadAfter(long trailingDueMs)
    {
        if (trailingDueMs > 0)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, (trailingDueMs - NowMs()) / 1000f));
            if (_trailingDueMs != trailingDueMs) yield break;
            _trailingDueMs = 0;
            _lastScheduledMs = NowMs();   // it is the new minute's read: the throttle counts from here
        }
        yield return new WaitForSecondsRealtime(DelaySeconds);
        yield return UsageClient.FetchRoutine();
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
