using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// A4 «защита платящих» (2026-10-14; spec §2.2, §3 A4) — the device half of «канал отключился».
//
// A paying owner's WhatsApp/Telegram logs out (a re-paired phone) and the bot falls silent while the card still says
// «connected». At launch — after the session and the boot claim loop — and on every return to the app (at most once a
// minute) this partial asks the Wappi Proxy's `status` op for every profile the session token lists, one at a time, and
// records the answer as the channel's mark (ChannelHealth): a positive authorized:false → «Канал отключён · переподключите
// в настройках» on the card, authorized:true → the mark goes. NON-DESTRUCTIVE by design: nothing here deletes, logs out or
// switches off a profile or a channel — the existing check on opening the bot's settings stays the reconnect path.
// Pinned by ChannelHealthTests (the decisions + these wiring guards).
public partial class Manager
{
    private bool _channelHealthRunning;
    private bool _channelHealthArmed;       // LoadBots has instantiated the bots — before that there is nothing to check
    private long _channelHealthLastRunMs;

    /// <summary>
    /// A return to the app re-checks the channels (≤ 1/min — <see cref="ChannelHealth.ShouldRun"/>). Only once the launch
    /// check was armed: Unity may report focus before LoadBots has built the cards, and a run then would find no bot and still
    /// spend the throttle, skipping the launch check.
    /// </summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && _channelHealthArmed) TryStartChannelHealth();
    }

    /// <summary>The launch check (LoadBots, right after the boot claim loop is started; the routine waits for both).</summary>
    private void StartChannelHealthAtLaunch()
    {
        _channelHealthArmed = true;
        TryStartChannelHealth();
    }

    /// <summary>Launch and focus entry: single-flight, throttled.</summary>
    private void TryStartChannelHealth()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_channelHealthRunning || !ChannelHealth.ShouldRun(now, _channelHealthLastRunMs)) return;
        _channelHealthRunning = true;
        _channelHealthLastRunMs = now;
        try
        {
            StartCoroutine(ChannelHealthRoutine());
        }
        catch (Exception e)
        {
            _channelHealthRunning = false;
            Debug.LogError($"[ChannelHealth] could not start — continuing: {e}");
        }
    }

    private IEnumerator ChannelHealthRoutine()
    {
        try
        {
            var auth = AuthClient.Instance;
            if (auth == null) yield break;
            yield return auth.EnsureSession();
            // after the boot claim loop: it may still bind profiles (the token then lists them) or move retired ids aside
            while (ProfileClaimFlow.IsRunning) yield return null;
            var claims = auth.Claims;
            if (claims == null) yield break;
            foreach (var target in ChannelHealth.Targets(ChannelHealthBots(), claims.Profiles))
                yield return CheckChannelHealth(target);
        }
        finally
        {
            _channelHealthRunning = false;
        }
    }

    /// <summary>One channel: the proxy's status op, then the mark — only while the bot still holds that profile there.</summary>
    private IEnumerator CheckChannelHealth(ChannelHealth.Target target)
    {
        UnityWebRequest www = null;
        yield return WappiProxy.Send(WappiProxy.Op.Status, target.Channel, new { profile_id = target.ProfileId }, r => www = r, 20);
        using var wwwScope = www;
        var verdict = ChannelHealth.Judge(www != null ? www.responseCode : 0, www?.downloadHandler?.text, target.ProfileId);
        if (verdict == ChannelHealthVerdict.Keep) yield break;
        Bot bot = ChannelHealthBot(target.BotKey);
        string held = bot == null ? null : target.Channel == ChatChannel.Telegram ? bot.telegramProfileId : bot.whatsappProfileId;
        if (string.Equals(held, target.ProfileId, StringComparison.Ordinal))   // a deleted / re-paired bot is left alone
            ChannelHealth.Apply(target.BotKey, target.Channel, verdict);
    }

    private IEnumerable<ChannelHealth.BotChannels> ChannelHealthBots()
    {
        var root = BotsRoot;
        if (root == null) yield break;
        for (int i = 0; i < root.childCount; i++)
        {
            var bot = root.GetChild(i).GetComponent<Bot>();
            if (bot != null) yield return new ChannelHealth.BotChannels(bot.name, bot.whatsappProfileId, bot.telegramProfileId);
        }
    }

    private Bot ChannelHealthBot(string botKey)
    {
        var root = BotsRoot;
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            var bot = root.GetChild(i).GetComponent<Bot>();
            if (bot != null && bot.name == botKey) return bot;
        }
        return null;
    }
}
