using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// A4 «защита платящих» (2026-10-14; spec §2.2, §3 A4): the device's «Канал отключён» mark. A paying owner's channel that
/// logged out is MARKED from the proxy's <c>status</c> answer — never deleted, logged out or switched off. Pins the verdict
/// matrix (<see cref="ChannelHealth.Judge(long, string)"/>: only a 2xx with the BOOLEAN authorized:false marks; anything
/// else keeps the mark as it is), which channels are checked, the throttle, the mark store (per-bot keys, saved at once,
/// one event per change), the card's condition, and — at the source, since EditMode cannot run the coroutine against a
/// server — the wiring: launch after the session and the claim loop, a return to the app, the proxy's status op only, no
/// destructive call, the marks cleared by a re-authorisation and with the bot.
/// </summary>
public class ChannelHealthTests
{
    private const string BotKey = "A4ChannelHealthTestBot";
    private const string Wa = "prof-wa-1";
    private const string Tg = "prof-tg-1";

    private Dictionary<string, int> _prefs;
    private int _saves;
    private List<string> _events;

    [SetUp]
    public void Seams()
    {
        _prefs = new Dictionary<string, int>();
        _saves = 0;
        _events = new List<string>();
        ChannelHealth.ResetSeamsForTests();
        ChannelHealth.GetInt = (key, fallback) => _prefs.TryGetValue(key, out int v) ? v : fallback;
        ChannelHealth.SetInt = (key, value) => _prefs[key] = value;
        ChannelHealth.DeleteKey = key => _prefs.Remove(key);
        ChannelHealth.Save = () => _saves++;
        ChannelHealth.Changed += botKey => _events.Add(botKey);
    }

    [TearDown]
    public void Reset() => ChannelHealth.ResetSeamsForTests();

    // --- the verdict ----------------------------------------------------------------------------------------------

    [TestCase(200L, "{\"authorized\":false,\"profile_id\":\"prof-wa-1\"}")]
    [TestCase(200L, "{\"profile_id\":\"prof-wa-1\",\"authorized\":false,\"phone\":null}")]
    [TestCase(201L, "{\"authorized\":false}")]
    [TestCase(299L, "{ \"authorized\" : false }")]
    public void Only_a_2xx_with_the_boolean_false_marks(long status, string body)
        => Assert.AreEqual(ChannelHealthVerdict.Unauthorized, ChannelHealth.Judge(status, body));

    [TestCase(200L, "{\"authorized\":true,\"profile_id\":\"prof-wa-1\"}")]
    [TestCase(204L, "{\"authorized\":true}")]
    public void A_2xx_with_the_boolean_true_unmarks(long status, string body)
        => Assert.AreEqual(ChannelHealthVerdict.Authorized, ChannelHealth.Judge(status, body));

    [TestCase(0L, "{\"authorized\":false}", TestName = "Keep: no answer (status 0)")]
    [TestCase(300L, "{\"authorized\":false}", TestName = "Keep: 300")]
    [TestCase(400L, "{\"authorized\":false}", TestName = "Keep: 400")]
    [TestCase(401L, "{\"authorized\":false}", TestName = "Keep: 401 (no session)")]
    [TestCase(403L, "{\"success\":false,\"error\":\"forbidden\",\"detail\":\"profile_id\"}", TestName = "Keep: 403 (a retired profile)")]
    [TestCase(403L, "{\"authorized\":false}", TestName = "Keep: 403 even with a false flag")]
    [TestCase(404L, "{\"authorized\":false}", TestName = "Keep: 404")]
    [TestCase(500L, "{\"authorized\":false}", TestName = "Keep: 500")]
    [TestCase(502L, "{\"status\":\"error\"}", TestName = "Keep: 502")]
    [TestCase(503L, "{\"authorized\":false}", TestName = "Keep: 503")]
    [TestCase(200L, "", TestName = "Keep: empty body")]
    [TestCase(200L, null, TestName = "Keep: no body")]
    [TestCase(200L, "   ", TestName = "Keep: whitespace body")]
    [TestCase(200L, "<html><body>Bad gateway</body></html>", TestName = "Keep: HTML")]
    [TestCase(200L, "not json", TestName = "Keep: not JSON")]
    [TestCase(200L, "[{\"authorized\":false}]", TestName = "Keep: a JSON array")]
    [TestCase(200L, "false", TestName = "Keep: a bare JSON value")]
    [TestCase(200L, "{\"authorized\":\"false\"}", TestName = "Keep: the flag as a string")]
    [TestCase(200L, "{\"authorized\":0}", TestName = "Keep: the flag as a number")]
    [TestCase(200L, "{\"authorized\":null}", TestName = "Keep: the flag null")]
    [TestCase(200L, "{\"profile_id\":\"prof-wa-1\"}", TestName = "Keep: no flag")]
    [TestCase(200L, "{\"status\":{\"authorized\":false}}", TestName = "Keep: a nested flag")]
    [TestCase(200L, "{\"authorized\":false", TestName = "Keep: truncated JSON")]
    public void Anything_else_keeps_the_mark_as_it_is(long status, string body)
        => Assert.AreEqual(ChannelHealthVerdict.Keep, ChannelHealth.Judge(status, body));

    [Test]
    public void Another_profiles_status_proves_nothing_about_this_channel()
    {
        Assert.AreEqual(ChannelHealthVerdict.Keep, ChannelHealth.Judge(200, "{\"authorized\":false,\"profile_id\":\"other\"}", Wa),
            "Wappi crosses concurrent answers — a status naming another profile is never this channel's");
        Assert.AreEqual(ChannelHealthVerdict.Keep, ChannelHealth.Judge(200, "{\"authorized\":true,\"profile_id\":\"other\"}", Wa));
        Assert.AreEqual(ChannelHealthVerdict.Unauthorized, ChannelHealth.Judge(200, "{\"authorized\":false,\"profile_id\":\"prof-wa-1\"}", Wa));
        Assert.AreEqual(ChannelHealthVerdict.Unauthorized, ChannelHealth.Judge(200, "{\"authorized\":false}", Wa), "no echo: judged as is");
        Assert.AreEqual(ChannelHealthVerdict.Unauthorized, ChannelHealth.Judge(200, "{\"authorized\":false,\"profile_id\":null}", Wa));
        Assert.AreEqual(ChannelHealthVerdict.Keep, ChannelHealth.Judge(500, "{\"authorized\":false,\"profile_id\":\"prof-wa-1\"}", Wa));
    }

    // --- which channels, how often ----------------------------------------------------------------------------------

    [Test]
    public void Only_connected_channels_the_token_lists_are_checked_in_card_order()
    {
        var bots = new[]
        {
            new ChannelHealth.BotChannels("Bot0", Wa, Tg),
            new ChannelHealth.BotChannels("Bot1", "prof-wa-2", Bot.UnauthedProfileSentinel),
            new ChannelHealth.BotChannels("Bot2", "", null),
            new ChannelHealth.BotChannels("Bot3", "prof-wa-unlisted", "prof-tg-3"),
            new ChannelHealth.BotChannels("", "prof-wa-4", null),
        };
        var targets = ChannelHealth.Targets(bots, new[] { Wa, Tg, "prof-wa-2", "prof-tg-3", "prof-wa-4" });
        var got = new List<string>();
        foreach (var t in targets) got.Add($"{t.BotKey}:{t.Channel}:{t.ProfileId}");
        CollectionAssert.AreEqual(new[] { "Bot0:WhatsApp:prof-wa-1", "Bot0:Telegram:prof-tg-1", "Bot1:WhatsApp:prof-wa-2", "Bot3:Telegram:prof-tg-3" }, got,
            "a sentinel / empty id, a profile the token does not list (retired, another install's) and a nameless bot are never asked");
        Assert.IsEmpty(ChannelHealth.Targets(bots, new string[0]), "a token without profiles asks nothing");
        Assert.IsEmpty(ChannelHealth.Targets(bots, null));
        Assert.IsEmpty(ChannelHealth.Targets(null, new[] { Wa }));
    }

    [TestCase(0L, 1_000_000L, true, TestName = "Throttle: never run before")]
    [TestCase(1_000_000L, 1_000_000L + 59_999L, false, TestName = "Throttle: within a minute — no")]
    [TestCase(1_000_000L, 1_000_000L + 60_000L, true, TestName = "Throttle: a minute later — yes")]
    [TestCase(1_000_000L, 999_000L, true, TestName = "Throttle: the clock moved back — never blocks")]
    public void A_launch_and_a_return_check_at_most_once_a_minute(long lastRunMs, long nowMs, bool expected)
        => Assert.AreEqual(expected, ChannelHealth.ShouldRun(nowMs, lastRunMs));

    // --- the mark ---------------------------------------------------------------------------------------------------

    [Test]
    public void The_mark_lives_under_the_bots_own_keys()
    {
        Assert.AreEqual("WhatsappUnauthorized", ChannelHealth.Suffix(ChatChannel.WhatsApp));
        Assert.AreEqual("TelegramUnauthorized", ChannelHealth.Suffix(ChatChannel.Telegram));
        ChannelHealth.Apply(BotKey, ChatChannel.WhatsApp, ChannelHealthVerdict.Unauthorized);
        Assert.AreEqual(1, _prefs[BotKey + "WhatsappUnauthorized"], "int 1 under {bot}WhatsappUnauthorized");
        Assert.IsFalse(_prefs.ContainsKey(BotKey + "TelegramUnauthorized"), "the other channel is not touched");
        Assert.IsTrue(ChannelHealth.IsMarked(BotKey, ChatChannel.WhatsApp));
        Assert.IsFalse(ChannelHealth.IsMarked(BotKey, ChatChannel.Telegram));
    }

    [Test]
    public void A_change_is_saved_at_once_and_announced_once()
    {
        ChannelHealth.Apply(BotKey, ChatChannel.Telegram, ChannelHealthVerdict.Unauthorized);
        ChannelHealth.Apply(BotKey, ChatChannel.Telegram, ChannelHealthVerdict.Unauthorized);
        Assert.AreEqual(1, _saves, "a repeated verdict writes nothing");
        CollectionAssert.AreEqual(new[] { BotKey }, _events, "one Changed per real change, naming the bot");

        ChannelHealth.Apply(BotKey, ChatChannel.Telegram, ChannelHealthVerdict.Keep);
        Assert.IsTrue(ChannelHealth.IsMarked(BotKey, ChatChannel.Telegram), "Keep changes nothing");
        Assert.AreEqual(1, _saves);

        ChannelHealth.Unmark(BotKey, ChatChannel.Telegram);
        Assert.IsFalse(ChannelHealth.IsMarked(BotKey, ChatChannel.Telegram), "authorized:true / a re-authorisation removes it");
        Assert.IsFalse(_prefs.ContainsKey(BotKey + "TelegramUnauthorized"), "removed, not stored as 0");
        Assert.AreEqual(2, _saves);
        Assert.AreEqual(2, _events.Count);

        ChannelHealth.Unmark(BotKey, ChatChannel.Telegram);
        Assert.AreEqual(2, _saves, "unmarking an unmarked channel writes nothing");
    }

    [Test]
    public void A_deleted_bot_takes_both_marks_with_it()
    {
        ChannelHealth.Apply(BotKey, ChatChannel.WhatsApp, ChannelHealthVerdict.Unauthorized);
        ChannelHealth.Apply(BotKey, ChatChannel.Telegram, ChannelHealthVerdict.Unauthorized);
        ChannelHealth.Clear(BotKey);
        Assert.IsFalse(ChannelHealth.IsMarked(BotKey, ChatChannel.WhatsApp));
        Assert.IsFalse(ChannelHealth.IsMarked(BotKey, ChatChannel.Telegram));
        Assert.IsEmpty(_prefs);
    }

    [Test]
    public void A_nameless_bot_is_never_marked()
    {
        ChannelHealth.Apply("", ChatChannel.WhatsApp, ChannelHealthVerdict.Unauthorized);
        ChannelHealth.Apply(null, ChatChannel.WhatsApp, ChannelHealthVerdict.Unauthorized);
        Assert.IsEmpty(_prefs);
        Assert.IsFalse(ChannelHealth.IsMarked(null, ChatChannel.WhatsApp));
    }

    [Test]
    public void The_card_shows_the_mark_only_on_a_connected_channel_the_server_did_not_retire()
    {
        Assert.IsTrue(ChannelHealth.CardShowsUnauthorized(true, Wa, null));
        Assert.IsFalse(ChannelHealth.CardShowsUnauthorized(false, Wa, null), "no mark");
        Assert.IsFalse(ChannelHealth.CardShowsUnauthorized(true, Bot.UnauthedProfileSentinel, null), "no profile any more — the mark is inert");
        Assert.IsFalse(ChannelHealth.CardShowsUnauthorized(true, "", null));
        Assert.IsFalse(ChannelHealth.CardShowsUnauthorized(true, Wa, "liveness"), "a retired channel's own subline outranks it");
        Assert.IsFalse(ChannelHealth.CardShowsUnauthorized(true, Wa, ""), "retired without a reason is still retired");
    }

    [Test]
    public void The_settings_own_status_answers_only_ever_unmark_the_bot_that_holds_the_checked_profile()
    {
        var root = new GameObject("A4ChannelHealthRoot");
        try
        {
            var botObject = new GameObject(BotKey);
            botObject.transform.SetParent(root.transform);
            var bot = botObject.AddComponent<Bot>();
            bot.whatsappProfileId = Wa;
            bot.telegramProfileId = Tg;
            ChannelHealth.Apply(BotKey, ChatChannel.WhatsApp, ChannelHealthVerdict.Unauthorized);
            ChannelHealth.Apply(BotKey, ChatChannel.Telegram, ChannelHealthVerdict.Unauthorized);

            ChannelHealth.UnmarkIfAuthorized(root.transform, ChatChannel.WhatsApp, Wa, 200, "{\"authorized\":false,\"profile_id\":\"prof-wa-1\"}");
            Assert.IsTrue(ChannelHealth.IsMarked(BotKey, ChatChannel.WhatsApp), "authorized:false changes nothing here — the settings act on it themselves");
            ChannelHealth.UnmarkIfAuthorized(root.transform, ChatChannel.WhatsApp, Wa, 200, "{\"authorized\":true,\"profile_id\":\"other\"}");
            Assert.IsTrue(ChannelHealth.IsMarked(BotKey, ChatChannel.WhatsApp), "a crossed answer (another profile's status) proves nothing");
            ChannelHealth.UnmarkIfAuthorized(root.transform, ChatChannel.WhatsApp, Wa, 500, "{\"authorized\":true}");
            Assert.IsTrue(ChannelHealth.IsMarked(BotKey, ChatChannel.WhatsApp), "a non-2xx proves nothing");
            ChannelHealth.UnmarkIfAuthorized(root.transform, ChatChannel.WhatsApp, Wa, 200, "{\"authorized\":\"true\"}");
            Assert.IsTrue(ChannelHealth.IsMarked(BotKey, ChatChannel.WhatsApp), "the flag as a string proves nothing (the routine's strict rule)");
            ChannelHealth.UnmarkIfAuthorized(root.transform, ChatChannel.WhatsApp, "prof-wa-old", 200, "{\"authorized\":true,\"profile_id\":\"prof-wa-old\"}");
            Assert.IsTrue(ChannelHealth.IsMarked(BotKey, ChatChannel.WhatsApp), "a profile the bot no longer holds heals nothing");
            Assert.AreEqual(2, _saves, "nothing above wrote a thing");

            ChannelHealth.UnmarkIfAuthorized(root.transform, ChatChannel.WhatsApp, Wa, 200, "{\"authorized\":true,\"profile_id\":\"prof-wa-1\"}");
            Assert.IsFalse(ChannelHealth.IsMarked(BotKey, ChatChannel.WhatsApp), "authorized:true for the held profile heals the card at once");
            Assert.IsTrue(ChannelHealth.IsMarked(BotKey, ChatChannel.Telegram), "the other channel is not touched");
            ChannelHealth.UnmarkIfAuthorized(root.transform, ChatChannel.Telegram, Tg, 200, "{\"authorized\":true}");
            Assert.IsFalse(ChannelHealth.IsMarked(BotKey, ChatChannel.Telegram), "no echo: judged as is (the routine's rule)");
            CollectionAssert.AreEqual(new[] { BotKey, BotKey, BotKey, BotKey }, _events, "two marks, two heals — one event each");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void The_copy_is_one_line_in_russian()
        => Assert.AreEqual("Канал отключён · переподключите в настройках", ChannelHealthCopy.UnauthorizedSubline);

    // --- the wiring (source guards) ---------------------------------------------------------------------------------

    private static string Source(string relative) =>
        Regex.Replace(File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", relative)), @"//[^\n]*", "");

    private static string Body(string source, string signature)
    {
        int at = source.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(at, 0, $"{signature} not found");
        int open = source.IndexOf('{', at), depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"{signature}: unbalanced braces");
        return "";
    }

    [Test]
    public void The_routine_runs_after_the_session_and_the_claim_loop_and_asks_only_the_proxys_status_op()
    {
        string partial = Source("Main/Manager.ChannelHealth.cs");
        string routine = Body(partial, "private IEnumerator ChannelHealthRoutine()");
        int ensure = routine.IndexOf("yield return auth.EnsureSession();", System.StringComparison.Ordinal);
        int claims = routine.IndexOf("while (ProfileClaimFlow.IsRunning) yield return null;", System.StringComparison.Ordinal);
        int targets = routine.IndexOf("ChannelHealth.Targets(ChannelHealthBots(), claims.Profiles)", System.StringComparison.Ordinal);
        Assert.IsTrue(ensure >= 0 && claims > ensure && targets > claims, "session → the claim loop is over → the token's profiles");
        StringAssert.Contains("_channelHealthRunning = false;", Body(routine, "finally"), "the single-flight latch is released however the run ends");

        string check = Body(partial, "private IEnumerator CheckChannelHealth(ChannelHealth.Target target)");
        StringAssert.Contains("WappiProxy.Send(WappiProxy.Op.Status, target.Channel, new { profile_id = target.ProfileId }", check);
        StringAssert.Contains("ChannelHealth.Judge(www != null ? www.responseCode : 0, www?.downloadHandler?.text, target.ProfileId)", check,
            "the verdict comes from the seam, with the profile-echo guard");
        StringAssert.Contains("ChannelHealth.Apply(target.BotKey, target.Channel, verdict);", check);
        Assert.IsFalse(Regex.IsMatch(partial, @"\.SendWebRequest\(\)"), "never a direct send — the proxy carries the session token");
    }

    [Test]
    public void Nothing_in_the_check_is_destructive()
    {
        string partial = Source("Main/Manager.ChannelHealth.cs");
        foreach (var forbidden in new[] { "ProfileDelete", "ProfileLogout", "GetDelete", "DeleteWhatsappProfile", "DeleteTelegramProfile",
                                          "isOnWhatsapp", "isOnTelegram", "SetWorkflowActive", "DeleteBot", "ProfileRetirement.Mark", "PlayerPrefs.Set" })
            Assert.IsFalse(partial.Contains(forbidden), $"the channel check must never delete, log out or switch anything off ({forbidden})");
        Assert.AreEqual(1, Regex.Matches(partial, @"WappiProxy\.Send\(").Count, "one proxy call: the status op");
    }

    [Test]
    public void Launch_and_a_return_to_the_app_start_it()
    {
        string load = Body(Source("Main/Manager.cs"), "public IEnumerator LoadBots()");
        int claimLoop = load.IndexOf("StartCoroutine(ProfileClaimFlow.RunAtBoot(BotsRoot));", System.StringComparison.Ordinal);
        int health = load.IndexOf("StartChannelHealthAtLaunch();", System.StringComparison.Ordinal);
        Assert.IsTrue(claimLoop >= 0 && health > claimLoop, "at launch, after the claim loop is started (the routine waits for it)");
        string partial = Source("Main/Manager.ChannelHealth.cs");
        string launch = Body(partial, "private void StartChannelHealthAtLaunch()");
        int arm = launch.IndexOf("_channelHealthArmed = true;", System.StringComparison.Ordinal);
        int start = launch.IndexOf("TryStartChannelHealth();", System.StringComparison.Ordinal);
        Assert.IsTrue(arm >= 0 && start > arm, "the launch arms the focus trigger, then starts the check");
        StringAssert.Contains("if (hasFocus && _channelHealthArmed) TryStartChannelHealth();", Body(partial, "private void OnApplicationFocus(bool hasFocus)"),
            "a focus before LoadBots built the cards must not spend the throttle on an empty run");
        StringAssert.Contains("if (_channelHealthRunning || !ChannelHealth.ShouldRun(now, _channelHealthLastRunMs)) return;",
            Body(partial, "private void TryStartChannelHealth()"));
    }

    [Test]
    public void A_reauthorisation_and_the_bots_deletion_clear_the_mark()
    {
        string manager = Source("Main/Manager.cs");
        StringAssert.Contains("ChannelHealth.UnmarkProfile(BotsRoot, ChatChannel.WhatsApp, whatsappProfileId);",
            Body(manager, "private IEnumerator GetWhatsappProfileStatus()"), "the WhatsApp pairing confirmation (wizard + settings) clears it");
        StringAssert.Contains("ChannelHealth.UnmarkProfile(BotsRoot, ChatChannel.Telegram, telegramProfileId);",
            Body(manager, "private IEnumerator GetTelegramProfileStatus()"), "the Telegram one too");
        StringAssert.Contains("ChannelHealth.Clear(transform.name);", Body(Source("Main/Bot.cs"), "public void DeleteBotLocally()"));
    }

    [Test]
    public void The_settings_own_status_checks_heal_the_card_with_the_profile_they_asked_about()
    {
        // A4 fix round 2: switching a channel on and opening the settings both read the channel's status — an authorised one
        // must clear «Канал отключён» there and then, not at the next launch / return to the app.
        string auth = Source("Main/BotSettings.Auth.cs");
        foreach (var (method, channel) in new[]
                 {
                     ("private IEnumerator CheckWhatsappAuthorization()", "ChatChannel.WhatsApp"),
                     ("private IEnumerator CheckWhatsappUnauthorizationOutsideApp()", "ChatChannel.WhatsApp"),
                     ("private IEnumerator CheckTelegramAuthorization()", "ChatChannel.Telegram"),
                     ("private IEnumerator CheckTelegramUnauthorizationOutsideApp()", "ChatChannel.Telegram"),
                 })
        {
            string body = Body(auth, method);
            StringAssert.Contains($"WappiProxy.Send(WappiProxy.Op.Status, {channel}, new {{ profile_id = checkedProfileId }}", body,
                $"{method}: the status is asked for the very profile the heal names");
            int read = body.IndexOf("string response = www.downloadHandler.text;", System.StringComparison.Ordinal);
            int heal = body.IndexOf($"ChannelHealth.UnmarkIfAuthorized(Manager.Instance.BotsRoot, {channel}, checkedProfileId, www.responseCode, response);",
                System.StringComparison.Ordinal);
            Assert.IsTrue(read >= 0 && heal > read, $"{method}: an authorised status must heal the card's «Канал отключён» at once");
        }
    }

    [Test]
    public void The_card_repaints_on_a_change_and_wears_the_copy()
    {
        string bot = Source("Main/Bot.cs");
        StringAssert.Contains("ChannelHealth.Changed += HandleChannelHealthChanged;", Body(bot, "private void OnEnable()"));
        StringAssert.Contains("ChannelHealth.Changed -= HandleChannelHealthChanged;", Body(bot, "private void OnDisable()"));
        StringAssert.Contains("BotSublineKind.Unauthorized => ChannelHealthCopy.UnauthorizedSubline,", Body(bot, "private void PaintSubline(BotSublineKind kind, string retiredText)"));
    }
}
