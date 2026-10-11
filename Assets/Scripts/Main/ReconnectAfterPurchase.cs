using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F2 (plan 2026-10-11 §5.2, task T9): what happens right after a purchase. The store's answer reaches every surface
/// through <see cref="BillingService.OnEntitlementChanged"/>; when it is a PURCHASE (no paid plan → a paid one — never the
/// boot resolution of a plan the owner already had, <see cref="ReconnectSettlePolicy.IsPurchaseTransition"/>):
/// <list type="number">
/// <item>the session token is refreshed — it lists only live rows, and a 12-hour token may still list a profile the
/// sweep retired after it was issued, which the claim loop would then never ask about;</item>
/// <item>the boot claim loop runs again (<see cref="ProfileClaimFlow.RunAtBoot"/>): a profile the sweep retired after the
/// app started answers 410 and its channel is moved aside (at most one claim.start per profile per run — M4);</item>
/// <item>the channels waiting for their reconnect are listed (<see cref="ProfileRetirement.ReconnectCandidates"/>) and,
/// when there are any, «Подключите ботов снова» is offered (<see cref="ReconnectBotsSheet"/>) once the paywall is gone.</item>
/// </list>
/// A row's «Подключить» opens that bot's settings and starts the channel's fresh path (gate → server wait → profile.add →
/// pairing → Create naming the retired profile). The gate's paywall-request event is deliberately NOT touched —
/// <c>PaywallController</c> stays its only subscriber (a second one would open the paywall and this sheet together).
/// </summary>
public static class ReconnectAfterPurchase
{
    /// <summary>The tier of the last entitlement answer this session (null until the first — the boot resolution).</summary>
    private static PlanTier? lastTier;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        lastTier = null;
        BillingService.OnEntitlementChanged -= HandleEntitlementChanged;
        BillingService.OnEntitlementChanged += HandleEntitlementChanged;
    }

    private static void HandleEntitlementChanged(PlanTier tier)
    {
        PlanTier? before = lastTier;
        lastTier = tier;
        if (!ReconnectSettlePolicy.IsPurchaseTransition(before, tier)) return;

        // Runs inside RevenueCat's callback multicast: nothing here may throw into the other subscribers.
        var host = Manager.Instance;
        if (host == null) return;
        host.StartCoroutine(AfterPurchase(host.BotsRoot));
    }

    private static IEnumerator AfterPurchase(Transform botsRoot)
    {
        if (botsRoot == null) yield break;
        var auth = AuthClient.Instance;
        if (auth != null) yield return auth.ForceRefresh();

        // A run still going (the boot one) would make ours return at once — wait for it, then ask again.
        while (ProfileClaimFlow.IsRunning) yield return null;
        yield return ProfileClaimFlow.RunAtBoot(botsRoot);

        var claims = AuthClient.Existing != null ? AuthClient.Existing.Claims : null;
        List<RetiredChannelPolicy.Candidate> rows = ProfileRetirement.ReconnectCandidates(botsRoot, claims);
        if (rows.Count == 0) yield break;

        // Over a clear screen: the paywall closes itself after the purchase, a gate sheet may still be leaving.
        while (PaywallController.Instance != null && PaywallController.Instance.IsOpen) yield return null;
        while (BillingGateSheet.IsOpen) yield return null;

        var paywall = PaywallController.Instance;
        ReconnectBotsSheet.Show(rows, paywall != null ? paywall.SemiboldFont : null, paywall != null ? paywall.RegularFont : null,
            row => OpenReconnect(botsRoot, row));
    }

    /// <summary>The Bots tab, the bot's settings (unless they are already the open ones), then that channel's fresh path.</summary>
    private static void OpenReconnect(Transform botsRoot, RetiredChannelPolicy.Candidate row)
    {
        Bot bot = FindBot(botsRoot, row.BotKey);
        if (bot == null) return;
        bool alreadyOpen = Manager.openBot == bot.gameObject && Manager.openBotSettings != null
                           && Manager.openBotSettings.gameObject.activeInHierarchy;
        if (!alreadyOpen)
        {
            var tabs = Object.FindFirstObjectByType<BottomTabManager>(FindObjectsInactive.Include);
            if (tabs != null) tabs.SwitchTab(BottomTabManager.BotsTabIndex);
            bot.OpenSettingsAtGeneralTab();
        }
        if (Manager.openBotSettings != null) Manager.openBotSettings.BeginReconnect(row.Channel);
    }

    private static Bot FindBot(Transform botsRoot, string botKey)
    {
        if (botsRoot == null || string.IsNullOrEmpty(botKey)) return null;
        for (int i = 0; i < botsRoot.childCount; i++)
        {
            var bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot != null && bot.name == botKey) return bot;
        }
        return null;
    }
}
