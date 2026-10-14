using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// A3 «защита платящих» (2026-10-14): WHEN the client asks the server to heal its billing mirror after a reinstall
/// (POST /webhook/VerifyIdentity). <see cref="IdentityHealTrigger.ShouldCall"/> is the whole decision — a RevenueCat merge
/// (CustomerInfo.OriginalAppUserId ≠ the current app user id), a token that carries a billing identity, once per pair per
/// launch — and the wiring guards below pin where Manager takes its inputs and how it sends (EditMode cannot run the
/// coroutine against a server: the transport contract is pinned at the source, as in AuthWiringGuardTests).
/// </summary>
public class IdentityHealTriggerTests
{
    private const string Current = "$RCAnonymousID:a3a3a3a3c4c4c4c4a3a3a3a3c4c4c4c4";
    private const string Original = "$RCAnonymousID:b5b5b5b5d6d6d6d6b5b5b5b5d6d6d6d6";

    [SetUp]
    public void Seams()
    {
        BillingService.ResetSeamsForTests();
        EntitlementGate.ResetSeamsForTests();
    }

    [TearDown]
    public void Reset()
    {
        BillingService.ResetSeamsForTests();
        EntitlementGate.ResetSeamsForTests();
    }

    // --- the decision ---------------------------------------------------------------------------------------------

    [Test]
    public void A_merge_with_a_billing_identity_is_called()
        => Assert.IsTrue(IdentityHealTrigger.ShouldCall(Current, Original, false, null));

    [Test]
    public void No_merge_is_never_called()
        => Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, Current, false, null));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void An_unknown_current_id_is_never_called(string current)
        => Assert.IsFalse(IdentityHealTrigger.ShouldCall(current, Original, false, null));

    /// <summary>No CustomerInfo has landed yet (the backend reports no original id): nothing is known about a merge.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void An_unknown_original_id_is_never_called(string original)
        => Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, original, false, null));

    /// <summary>billingConflict: the token carries NO billing identity — nothing to heal INTO; the gate would refuse.</summary>
    [Test]
    public void A_billing_conflict_is_never_called()
        => Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, Original, true, null));

    [Test]
    public void The_same_pair_is_called_once_per_launch()
    {
        string latch = IdentityHealTrigger.PairOf(Current, Original);
        Assert.IsFalse(IdentityHealTrigger.ShouldCall(Current, Original, false, latch));
    }

    /// <summary>A new merge in the same launch (another restore) is a new pair — it is called.</summary>
    [Test]
    public void Another_pair_after_a_call_is_called()
    {
        string latch = IdentityHealTrigger.PairOf(Current, Original);
        const string newer = "$RCAnonymousID:c6c6c6c6e7e7e7e7c6c6c6c6e7e7e7e7";
        Assert.IsTrue(IdentityHealTrigger.ShouldCall(newer, Original, false, latch));
        Assert.IsTrue(IdentityHealTrigger.ShouldCall(Current, newer, false, latch));
    }

    /// <summary>The pair is ordered (original → current): a swapped pair is another merge, never the latched one.</summary>
    [Test]
    public void The_pair_key_is_ordered()
        => Assert.AreNotEqual(IdentityHealTrigger.PairOf(Current, Original), IdentityHealTrigger.PairOf(Original, Current));

    /// <summary>Ids are opaque: compared ordinally, never case-folded.</summary>
    [Test]
    public void Ids_are_compared_ordinally()
        => Assert.IsTrue(IdentityHealTrigger.ShouldCall(Current, Current.ToUpperInvariant(), false, null));

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
        Assert.IsTrue(IdentityHealTrigger.ShouldCall(BillingService.AppUserId, BillingService.OriginalAppUserId, false, null));
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
    public void The_handler_decides_by_the_seam_and_latches_the_pair_before_calling()
    {
        string handler = Body(Source("Main/Manager.cs"), "private void OnEntitlementChangedHealIdentity(PlanTier _)");
        StringAssert.Contains("string current = BillingIdentity.AppUserId;", handler, "current = the id the session asks to bind (the token's u)");
        StringAssert.Contains("string original = BillingService.OriginalAppUserId;", handler);
        StringAssert.Contains("AuthClient.Existing.BillingConflict", handler);
        int decide = handler.IndexOf("IdentityHealTrigger.ShouldCall(current, original, conflict, _identityHealLastPair)", System.StringComparison.Ordinal);
        int latch = handler.IndexOf("_identityHealLastPair = IdentityHealTrigger.PairOf(current, original);", System.StringComparison.Ordinal);
        int start = handler.IndexOf("StartCoroutine(VerifyIdentityRoutine(current, original));", System.StringComparison.Ordinal);
        Assert.IsTrue(decide >= 0 && latch > decide && start > latch, "decide → latch the pair → start the call (a repeat event never calls twice)");
        StringAssert.Contains("catch (Exception e)", handler, "it runs inside RevenueCat's callback multicast: it must never throw");
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
