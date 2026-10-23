using System;
using System.Collections;
using UnityEngine;

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
/// A send inside the minute is not followed by a request of its own: the percent catches up at the next one, or at any of
/// the older triggers.
///
/// The request is hosted on <see cref="Manager.Instance"/>, never on a component of the chat: Unity stops a coroutine whose
/// host goes inactive, and Manager is the always-active host that already runs the boot fetch
/// (<c>PaywallController.RefetchUsage</c> hosts its re-read the same way). Applying the snapshot, the single-flight guard
/// and the failure handling all stay in <see cref="UsageClient.FetchRoutine"/>. Pinned by LearnRefreshTests.
/// </summary>
public static class LearnRefresh
{
    /// <summary>At most one re-read a minute for the whole app.</summary>
    public const long MinIntervalMs = 60L * 1000;

    /// <summary>The pause before the request, so the server has recorded the send that the snapshot is meant to count.</summary>
    public const float DelaySeconds = 5f;

    // When the last re-read was scheduled, Unix ms. In memory only (the ChannelHealth stamp's rule): a new launch reads
    // usage at boot anyway, and 0 means «never».
    private static long _lastScheduledMs;

    /// <summary>
    /// Never twice within <see cref="MinIntervalMs"/>; a stamp from the future (the device clock moved back) never blocks.
    /// </summary>
    public static bool ShouldFetch(long nowMs, long lastMs)
        => lastMs <= 0 || nowMs - lastMs >= MinIntervalMs || nowMs < lastMs;

    /// <summary>
    /// Call once a send that carried a shown suggestion set has been acknowledged. Throttled by <see cref="ShouldFetch"/>;
    /// the re-read itself runs <see cref="DelaySeconds"/> later on the Manager, so this returns at once.
    /// </summary>
    public static void AfterLearningSend()
    {
        if (Manager.Instance == null) return;   // EditMode / early boot: no host to run it, so the minute is not spent either
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!ShouldFetch(now, _lastScheduledMs)) return;
        _lastScheduledMs = now;
        Manager.Instance.StartCoroutine(FetchAfterDelay());
    }

    private static IEnumerator FetchAfterDelay()
    {
        yield return new WaitForSeconds(DelaySeconds);
        yield return UsageClient.FetchRoutine();
    }
}
