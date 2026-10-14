using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// A3 «защита платящих» (2026-10-14): WHEN the client asks the server to heal its billing mirror after a reinstall
/// (POST /webhook/VerifyIdentity). <see cref="IdentityHealTrigger.ShouldCall"/> decides — a RevenueCat merge
/// (CustomerInfo.OriginalAppUserId ≠ the current app user id), a token that carries a billing identity, and the PERSISTED
/// latch (fix round: a pair the server HEALED is asked again only after 24 h; a new pair always; only a 2xx success:true
/// stamps — <see cref="IdentityHealTrigger.ShouldStamp"/>). The wiring guards pin where Manager takes its inputs, the
/// per-launch attempt guard and how it sends (EditMode cannot run the coroutine against a server: the transport contract
/// is pinned at the source, as in AuthWiringGuardTests).
/// </summary>
public class IdentityHealTriggerTests
{
    private const string Current = "$RCAnonymousID:a3a3a3a3c4c4c4c4a3a3a3a3c4c4c4c4";
    private const string Original = "$RCAnonymousID:b5b5b5b5d6d6d6d6b5b5b5b5d6d6d6d6";

    private const long Now = 1_800_000_000_000L;              // 2027-01-15 — any fixed instant
    private const long Hour = 60L * 60 * 1000;
    private static readonly string Healed = IdentityHealTrigger.PairOf(Current, Original);

    private System.Collections.Generic.Dictionary<string, string> _prefs;
    private int _saves;

    [SetUp]
    public void Seams()
    {
        BillingService.ResetSeamsForTests();
        EntitlementGate.ResetSeamsForTests();
        _prefs = new System.Collections.Generic.Dictionary<string, string>();
        _saves = 0;
        IdentityHealLatch.GetString = (key, fallback) => _prefs.TryGetValue(key, out var v) ? v : fallback;
        IdentityHealLatch.SetString = (key, value) => _prefs[key] = value;
        IdentityHealLatch.Save = () => _saves++;
    }

    [TearDown]
    public void Reset()
    {
        BillingService.ResetSeamsForTests();
        EntitlementGate.ResetSeamsForTests();
        IdentityHealLatch.ResetSeamsForTests();
    }

    // --- the decision ---------------------------------------------------------------------------------------------

    [Test]
    public void A_merge_with_a_billing_identity_and_no_latch_is_called()
        => Assert.IsTrue(IdentityHealTrigger.ShouldCall(Current, Original, false, "", 0, Now));

    [Test]
    public void No_merge_is_never_called()
        => Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, Current, false, "", 0, Now));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void An_unknown_current_id_is_never_called(string current)
        => Assert.IsFalse(IdentityHealTrigger.ShouldCall(current, Original, false, "", 0, Now));

    /// <summary>No CustomerInfo has landed yet (the backend reports no original id): nothing is known about a merge.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void An_unknown_original_id_is_never_called(string original)
        => Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, original, false, "", 0, Now));

    /// <summary>billingConflict: the token carries NO billing identity — nothing to heal INTO; the gate would refuse.</summary>
    [Test]
    public void A_billing_conflict_is_never_called()
    {
        Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, Original, true, "", 0, Now));
        Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, Original, true, Healed, Now - 48 * Hour, Now), "not even past the 24 h window");
    }

    /// <summary>A pair the server HEALED stays quiet for 24 h — across launches (the latch is persisted).</summary>
    [TestCase(1L)]
    [TestCase(60L * 60 * 1000)]
    [TestCase(24L * 60 * 60 * 1000 - 1)]
    public void The_healed_pair_inside_24h_is_not_called(long agoMs)
        => Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, Original, false, Healed, Now - agoMs, Now));

    /// <summary>After 24 h the same pair is asked again: something may have landed under the old id since.</summary>
    [TestCase(24L * 60 * 60 * 1000)]
    [TestCase(25L * 60 * 60 * 1000)]
    [TestCase(400L * 24 * 60 * 60 * 1000)]
    public void The_healed_pair_after_24h_is_called(long agoMs)
        => Assert.IsTrue(IdentityHealTrigger.ShouldCall(Current, Original, false, Healed, Now - agoMs, Now));

    /// <summary>A NEW merge (another reinstall / restore) is a new pair — always called, whatever the latch says.</summary>
    [Test]
    public void A_new_pair_is_always_called()
    {
        const string newer = "$RCAnonymousID:c6c6c6c6e7e7e7e7c6c6c6c6e7e7e7e7";
        Assert.IsTrue(IdentityHealTrigger.ShouldCall(newer, Original, false, Healed, Now - 1, Now));
        Assert.IsTrue(IdentityHealTrigger.ShouldCall(Current, newer, false, Healed, Now - 1, Now));
        Assert.IsTrue(IdentityHealTrigger.ShouldCall(Original, Current, false, Healed, Now - 1, Now), "the swapped pair is another merge");
    }

    /// <summary>A stamp from the future (the device clock moved back) never blocks the heal.</summary>
    [Test]
    public void A_stamp_from_the_future_never_blocks()
        => Assert.IsTrue(IdentityHealTrigger.ShouldCall(Current, Original, false, Healed, Now + Hour, Now));

    /// <summary>The pair is ordered (original → current): a swapped pair is another merge, never the latched one.</summary>
    [Test]
    public void The_pair_key_is_ordered()
        => Assert.AreNotEqual(IdentityHealTrigger.PairOf(Current, Original), IdentityHealTrigger.PairOf(Original, Current));

    /// <summary>Ids are opaque: compared ordinally, never case-folded.</summary>
    [Test]
    public void Ids_are_compared_ordinally()
        => Assert.IsTrue(IdentityHealTrigger.ShouldCall(Current, Current.ToUpperInvariant(), false, "", 0, Now));

    // --- what stamps the latch: ONLY the server's positive answer ---------------------------------------------------

    [TestCase(200, "{\"success\":true,\"aliases\":1,\"consolidated\":1,\"rekeyed\":2,\"truncated\":false}")]
    [TestCase(200, "{\"success\":true,\"aliases\":1,\"consolidated\":0,\"rekeyed\":0,\"truncated\":false}")]   // a repeat: still healed
    public void A_2xx_success_stamps(long code, string body)
        => Assert.IsTrue(IdentityHealTrigger.ShouldStamp(code, body));

    [TestCase(200, "{\"success\":false,\"reason\":\"hint_not_alias\"}")]
    [TestCase(200, "{\"success\":false,\"reason\":\"rc_mismatch\"}")]
    [TestCase(200, "{\"success\":false,\"reason\":\"no_aliases\"}")]
    [TestCase(502, "{\"success\":false,\"reason\":\"rc_unavailable\"}")]
    [TestCase(503, "{\"success\":false,\"reason\":\"db_unavailable\"}")]
    [TestCase(400, "{\"success\":false,\"error\":\"bad_request\"}")]
    [TestCase(401, "{\"success\":false,\"error\":\"unauthorized\",\"reason\":\"unauthorized:missing\"}")]
    [TestCase(403, "{\"success\":false,\"error\":\"unauthorized\",\"reason\":\"forbidden:appUserId\"}")]
    [TestCase(500, "{\"success\":true}")]                           // not a 2xx
    [TestCase(0, null)]                                              // no answer at all
    [TestCase(200, "")]
    [TestCase(200, "<html>gateway</html>")]
    [TestCase(200, "{\"success\":\"true\"}")]                        // a string is not the server's boolean
    [TestCase(200, "[true]")]
    public void Anything_else_never_stamps(long code, string body)
        => Assert.IsFalse(IdentityHealTrigger.ShouldStamp(code, body));

    // --- the persisted latch ----------------------------------------------------------------------------------------

    [Test]
    public void The_latch_keys_are_pinned()
    {
        Assert.AreEqual("CrIdentityHeal.LastPair", IdentityHealLatch.LastPairKey);
        Assert.AreEqual("CrIdentityHeal.LastHealedAtMs", IdentityHealLatch.LastHealedAtMsKey);
    }

    [Test]
    public void An_empty_store_holds_no_latch()
    {
        Assert.AreEqual("", IdentityHealLatch.LastPair);
        Assert.AreEqual(0L, IdentityHealLatch.LastHealedAtMs);
    }

    [Test]
    public void A_stamp_is_read_back_and_saved_once()
    {
        IdentityHealLatch.Stamp(Healed, Now);
        Assert.AreEqual(Healed, IdentityHealLatch.LastPair);
        Assert.AreEqual(Now, IdentityHealLatch.LastHealedAtMs);
        Assert.AreEqual(1, _saves, "flushed right away — the OS may kill the app after backgrounding");
    }

    [Test]
    public void An_unreadable_stamp_counts_as_none()
    {
        _prefs[IdentityHealLatch.LastHealedAtMsKey] = "not-a-number";
        Assert.AreEqual(0L, IdentityHealLatch.LastHealedAtMs);
    }

    /// <summary>The sequence the coordinator asked for, end to end on the pure parts: a healed pair is quiet for 24 h
    /// across launches; a refusal stamps nothing (the next launch asks again); a new pair always asks.</summary>
    [Test]
    public void Latch_sequence_across_launches()
    {
        bool Ask(long now) => IdentityHealTrigger.ShouldCall(Current, Original, false, IdentityHealLatch.LastPair, IdentityHealLatch.LastHealedAtMs, now);
        void Answer(long code, string body, long now)
        {
            if (IdentityHealTrigger.ShouldStamp(code, body)) IdentityHealLatch.Stamp(IdentityHealTrigger.PairOf(Current, Original), now);
        }

        Assert.IsTrue(Ask(Now), "launch 1: no latch");
        Answer(200, "{\"success\":false,\"reason\":\"rc_unavailable\"}", Now);
        Assert.IsTrue(Ask(Now + Hour), "launch 2: the refusal stamped nothing — asked again");
        Answer(200, "{\"success\":true,\"aliases\":1,\"consolidated\":1,\"rekeyed\":1,\"truncated\":false}", Now + Hour);
        Assert.IsFalse(Ask(Now + 2 * Hour), "launch 3: healed an hour ago");
        Assert.IsFalse(Ask(Now + 24 * Hour), "launch 4: still inside 24 h of the heal");
        Assert.IsTrue(Ask(Now + 25 * Hour), "launch 5: 24 h after the heal — asked again");
        Assert.IsTrue(IdentityHealTrigger.ShouldCall("$RCAnonymousID:c6c6c6c6e7e7e7e7c6c6c6c6e7e7e7e7", Original, false,
            IdentityHealLatch.LastPair, IdentityHealLatch.LastHealedAtMs, Now + 2 * Hour), "a new pair inside the window");
    }

    // --- the input: CustomerInfo.OriginalAppUserId through the backend seam ----------------------------------------

    [Test]
    public void BillingService_reports_no_original_id_before_Initialize()
        => Assert.AreEqual("", BillingService.OriginalAppUserId);

    [Test]
    public void BillingService_reports_the_backends_original_id()
    {
        var fake = new FakeBillingBackend { AppUserId = Current, OriginalAppUserId = Original };
        BillingService.BackendFactory = () => fake;
        BillingService.Initialize();

        Assert.AreEqual(Original, BillingService.OriginalAppUserId);
        Assert.AreEqual(Current, BillingService.AppUserId);
        Assert.IsTrue(IdentityHealTrigger.ShouldCall(BillingService.AppUserId, BillingService.OriginalAppUserId, false, "", 0, Now));
    }

    // --- the wiring (source guards) --------------------------------------------------------------------------------

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

    /// <summary>The first CustomerInfo — the one that reveals a reinstall's merge — must not slip past the handler.</summary>
    [Test]
    public void Manager_subscribes_before_billing_initializes_and_unsubscribes_on_destroy()
    {
        string manager = Source("Main/Manager.cs");
        string boot = Body(manager, "private IEnumerator PreloadSecretsThenInitBilling()");
        int sub = boot.IndexOf("BillingService.OnEntitlementChanged += OnEntitlementChangedHealIdentity;", System.StringComparison.Ordinal);
        int init = boot.IndexOf("BillingService.Initialize();", System.StringComparison.Ordinal);
        Assert.IsTrue(sub >= 0 && init > sub, "subscribe to OnEntitlementChanged BEFORE BillingService.Initialize()");
        StringAssert.Contains("BillingService.OnEntitlementChanged -= OnEntitlementChangedHealIdentity;", Body(manager, "private void OnDestroy()"),
            "OnEntitlementChanged is static: a destroyed Manager must unsubscribe");
    }

    [Test]
    public void The_handler_decides_by_the_seam_and_the_persisted_latch_and_guards_the_launch()
    {
        string handler = Body(Source("Main/Manager.cs"), "private void OnEntitlementChangedHealIdentity(PlanTier _)");
        StringAssert.Contains("string current = BillingIdentity.AppUserId;", handler, "current = the id the session asks to bind (the token's u)");
        StringAssert.Contains("string original = BillingService.OriginalAppUserId;", handler);
        StringAssert.Contains("AuthClient.Existing.BillingConflict", handler);
        int decide = handler.IndexOf("IdentityHealTrigger.ShouldCall(current, original, conflict, IdentityHealLatch.LastPair, IdentityHealLatch.LastHealedAtMs, now)", System.StringComparison.Ordinal);
        int guard = handler.IndexOf("if (string.Equals(_identityHealAttemptedPair, pair, StringComparison.Ordinal)) return;", System.StringComparison.Ordinal);
        int mark = handler.IndexOf("_identityHealAttemptedPair = pair;", System.StringComparison.Ordinal);
        int start = handler.IndexOf("StartCoroutine(VerifyIdentityRoutine(current, original));", System.StringComparison.Ordinal);
        Assert.IsTrue(decide >= 0 && guard > decide && mark > guard && start > mark,
            "decide by the persisted latch → skip a pair already tried this launch → mark it → start (no duplicate in flight, a refusal waits for the next launch)");
        StringAssert.Contains("catch (Exception e)", handler, "it runs inside RevenueCat's callback multicast: it must never throw");
    }

    /// <summary>Only the server's positive answer stamps the persisted latch — from exactly one place.</summary>
    [Test]
    public void Only_a_positive_answer_stamps_the_latch()
    {
        string manager = Source("Main/Manager.cs");
        Assert.AreEqual(1, Regex.Matches(manager, @"IdentityHealLatch\.Stamp\(").Count, "one stamping site");
        string routine = Body(manager, "private IEnumerator VerifyIdentityRoutine(string appUserId, string originalAppUserId)");
        int gate = routine.IndexOf("if (IdentityHealTrigger.ShouldStamp(www.responseCode, body))", System.StringComparison.Ordinal);
        int stamp = routine.IndexOf("IdentityHealLatch.Stamp(IdentityHealTrigger.PairOf(appUserId, originalAppUserId), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());", System.StringComparison.Ordinal);
        Assert.IsTrue(gate >= 0 && stamp > gate && stamp - gate < 200, "the stamp sits directly under ShouldStamp(responseCode, body)");
    }

    [Test]
    public void The_call_goes_through_AuthorizedRequest_and_skips_a_billing_conflict()
    {
        string routine = Body(Source("Main/Manager.cs"), "private IEnumerator VerifyIdentityRoutine(string appUserId, string originalAppUserId)");
        int ensure = routine.IndexOf("yield return auth.EnsureSession();", System.StringComparison.Ordinal);
        int skip = routine.IndexOf("if (auth != null && auth.BillingConflict) yield break;", System.StringComparison.Ordinal);
        int send = routine.IndexOf("AuthorizedRequest.Send(", System.StringComparison.Ordinal);
        Assert.IsTrue(ensure >= 0 && skip > ensure && send > skip, "the session is fresh, a billing conflict skips the call, then it is sent");
        StringAssert.Contains("AuthorizedRequest.JsonPost($\"{n8nBaseUrl}{VerifyIdentityRequest.Path}\", VerifyIdentityRequest.ComposeBody(appUserId, originalAppUserId), 30)", routine);
        Assert.IsFalse(routine.Contains(".SendWebRequest()"), "never a direct send — AuthorizedRequest stamps the token");
        StringAssert.Contains("VerifyIdentityRequest.HealedSomething(", routine, "only a heal that moved something re-reads usage");
    }

    [Test]
    public void RevenueCatBackend_takes_the_original_id_from_CustomerInfo()
        => StringAssert.Contains("if (!string.IsNullOrEmpty(info.OriginalAppUserId)) OriginalAppUserId = info.OriginalAppUserId;",
            Body(Source("Billing/RevenueCatBackend.cs"), "private void Apply(Purchases.CustomerInfo info)"));
}
