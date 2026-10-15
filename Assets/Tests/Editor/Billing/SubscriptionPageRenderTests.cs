using System;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

/// <summary>
/// The «Подписка» page as the owner sees it: driven through the REAL
/// <c>ProfileSubPages.RefreshSubscriptionPage</c> on a harness shaped like the scene's PlanCard
/// (Meters block holding the rows and the reserve line, the divider above it, the headline labels).
///
/// The two defects pinned here (A6, 2026-10-15) lived in the page's COMPOSITION, not in any one
/// string: (1) the headline decided from the local trial clock alone, so after a reinstall wiped the
/// ledger it read «Пробный · осталось 5 дн.» while <see cref="EntitlementGate.CurrentTier"/> was None
/// and the server knew the account was spent; (2) the whole Meters block was hidden at PlanTier.None,
/// and the paid top-up reserve — which lives in that block — went with it. A pure-seam test cannot see
/// either (the seam returned the right answer; the page asked it the wrong question and then hid the
/// result), which is why this drives the page itself.
///
/// EditMode does not run Awake/OnEnable on a plain MonoBehaviour, so the component is inert until the
/// test calls the (private) render directly — the EmptyStateViewLifecycleTests idiom.
/// </summary>
public class SubscriptionPageRenderTests
{
    // A fixed "now" keeps TrialLedger.DaysLeft() independent of the day the suite runs.
    private static readonly DateTime Now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private const string FrozenReserve500 = "Резерв: 500 диалогов — заморожен до возобновления подписки";

    private GameObject _root;
    private ProfileSubPages _page;
    private TextMeshProUGUI _title, _subline, _pill, _dialogs, _reserve, _bots, _channels;
    private GameObject _block, _divider;
    private GameObject[] _rows;   // the meter rows: everything in the block except the reserve line

    [SetUp]
    public void SetUp()
    {
        ResetSeams();
        BuildHarness();
    }

    [TearDown]
    public void TearDown()
    {
        if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        ResetSeams();
    }

    private static void ResetSeams()
    {
        EntitlementGate.ResetSeamsForTests();
        BillingService.ResetSeamsForTests();
        UsageStore.ResetSeamsForTests();
        TrialLedger.ResetSeamsForTests();
        TrialLedger.UtcNow = () => Now;
        TrialLedger.Save = (_, __) => { };
        SubscriptionPageRows.ResetSeamsForTests();
        SubscriptionPageRows.LocalizeUtc = d => d;
    }

    // ── the world the page reads ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Entitlements resolved to <paramref name="entitlements"/> (none = nothing purchased). A fake
    /// backend is "known" the instant it is wired; without this CurrentTier answers the
    /// resolve-window grace (Trial) for everyone, which is not the state under test.
    /// </summary>
    private static void Purchased(params string[] entitlements)
    {
        var fake = new FakeBillingBackend();
        fake.SetActiveEntitlements(entitlements);
        BillingService.BackendFactory = () => fake;
        BillingService.Initialize();
    }

    /// <summary>A reinstall wiped the ledger: the clock has never started.</summary>
    private static void ClockNeverStarted() => TrialLedger.Load = _ => "";

    /// <summary>A started clock with <paramref name="daysLeft"/> days on it (0 = spent).</summary>
    private static void ClockStarted(int daysLeft)
    {
        string started = Now.AddDays(-(PlanCatalog.TrialDays - daysLeft)).ToString("o", CultureInfo.InvariantCulture);
        TrialLedger.Load = _ => started;
    }

    private static void ServerSays(string status, int topupBalance, int quota = 0, int used = 0)
        => UsageStore.Apply(new UsageSnapshot
        {
            success = true,
            plan = status == "expired" ? "none" : "business",
            status = status,
            quota = quota,
            used = used,
            topupBalance = topupBalance,
        });

    // ── the harness (mirrors Main.unity: PlanCard/Meters) ────────────────────────────────────

    private void BuildHarness()
    {
        _root = new GameObject("SubscriptionPageHarness", typeof(RectTransform));
        _page = _root.AddComponent<ProfileSubPages>();

        _title = Label(_root.transform, "Title");
        _subline = Label(_root.transform, "Subline");
        _pill = Label(_root.transform, "PillLabel");

        _divider = Child(_root.transform, "Divider");
        _block = Child(_root.transform, "Meters");

        GameObject dialogsRow = Child(_block.transform, "Meter_Диалоги ИИ");
        _dialogs = Label(dialogsRow.transform, "Value");
        GameObject bar = Child(_block.transform, "Bar");
        RectTransform fill = Child(bar.transform, "Fill").GetComponent<RectTransform>();
        _reserve = Label(_block.transform, "ReserveLine");
        _reserve.gameObject.SetActive(false);   // authored inactive: the runtime owns its visibility
        GameObject botsRow = Child(_block.transform, "Meter_Боты");
        _bots = Label(botsRow.transform, "Value");
        GameObject channelsRow = Child(_block.transform, "Meter_Каналы");
        _channels = Label(channelsRow.transform, "Value");

        _rows = new[] { dialogsRow, bar, botsRow, channelsRow };

        Set("subPlanTitle", _title);
        Set("subPlanSubline", _subline);
        Set("subPillLabel", _pill);
        Set("subMetersBlock", _block);
        Set("subMetersDivider", _divider);
        Set("subDialogsValue", _dialogs);
        Set("subQuotaFill", fill);
        Set("subReserveLine", _reserve);
        Set("subBotsValue", _bots);
        Set("subChannelsValue", _channels);
    }

    private static GameObject Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI Label(Transform parent, string name)
        => Child(parent, name).AddComponent<TextMeshProUGUI>();

    private void Set(string field, object value)
    {
        FieldInfo f = typeof(ProfileSubPages).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(f, $"ProfileSubPages.{field} not found — update SubscriptionPageRenderTests");
        f.SetValue(_page, value);
    }

    private void Render()
    {
        MethodInfo m = typeof(ProfileSubPages).GetMethod("RefreshSubscriptionPage",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(m, "ProfileSubPages.RefreshSubscriptionPage not found — update SubscriptionPageRenderTests");
        m.Invoke(_page, null);
    }

    private static bool Shown(Component c) => c.gameObject.activeInHierarchy;
    private static bool Shown(GameObject go) => go.activeInHierarchy;

    private void AssertRows(bool visible, string why)
    {
        foreach (GameObject row in _rows)
            Assert.AreEqual(visible, Shown(row), $"{row.name}: {why}");
    }

    // ── defect 1: the headline against a wiped trial ledger ──────────────────────────────────

    [Test]
    public void A_wiped_ledger_with_a_server_that_says_expired_reads_expired_not_a_fresh_trial()
    {
        Purchased();
        ClockNeverStarted();
        ServerSays("expired", topupBalance: 0);

        Render();

        Assert.AreEqual("Без подписки", _title.text);
        Assert.AreEqual("Истекла", _pill.text);
        Assert.AreEqual("Подписка не оформлена", _subline.text);
        Assert.AreEqual(PlanTier.None, EntitlementGate.CurrentTier, "sanity: the gate already said None — the page must agree");
        Assert.IsFalse(Shown(_block), "no plan, no reserve: nothing under the headline");
        Assert.IsFalse(Shown(_divider), "the divider goes with the block, or the card ends on a hairline");
    }

    [Test]
    public void A_wiped_ledger_with_a_silent_server_is_still_the_pre_auth_trial()
    {
        Purchased();
        ClockNeverStarted();
        // no snapshot at all: a cold start knows nothing, and «nothing» never locks a fresh install

        Render();

        Assert.AreEqual("Пробный", _title.text);
        Assert.AreEqual("Пробный · осталось 5 дн.", _subline.text);
        Assert.IsTrue(Shown(_block));
        AssertRows(true, "a live trial shows its meters");
    }

    // ── defect 2: the reserve on a plan-less account ─────────────────────────────────────────

    [Test]
    public void A_paid_reserve_stays_visible_and_frozen_after_the_trial_lapsed()
    {
        Purchased();
        ClockStarted(daysLeft: 0);
        ServerSays("expired", topupBalance: 500);

        Render();

        Assert.AreEqual("Истекла", _pill.text);
        Assert.IsTrue(Shown(_block), "the block hosts the reserve line even though its rows are hidden");
        Assert.IsTrue(Shown(_divider), "the same hairline the live tiers have between the headline and the meters");
        Assert.IsTrue(Shown(_reserve), "the reserve is paid for — hiding it is a lie of omission");
        Assert.AreEqual(FrozenReserve500, _reserve.text);
        AssertRows(false, "«2 из 0» is not a limit: the rows stay hidden without a plan");
    }

    [Test]
    public void The_reserve_survives_the_reinstall_incident_too_wiped_ledger_server_expired_balance_kept()
    {
        Purchased();
        ClockNeverStarted();
        ServerSays("expired", topupBalance: 2000);

        Render();

        Assert.AreEqual("Истекла", _pill.text);
        Assert.IsTrue(Shown(_reserve));
        Assert.AreEqual("Резерв: 2\u00A0000 диалогов — заморожен до возобновления подписки", _reserve.text);
        AssertRows(false, "no plan");
    }

    [Test]
    public void A_lapsed_account_with_no_reserve_shows_no_meters_at_all()
    {
        Purchased();
        ClockStarted(daysLeft: 0);
        ServerSays("expired", topupBalance: 0);

        Render();

        Assert.IsFalse(Shown(_block));
        Assert.IsFalse(Shown(_divider));
        Assert.IsFalse(Shown(_reserve));
    }

    [Test]
    public void No_snapshot_yet_says_nothing_about_a_reserve()
    {
        Purchased();
        ClockStarted(daysLeft: 0);   // spent locally, the server has not been heard from

        Render();

        Assert.AreEqual("Истекла", _pill.text);
        Assert.IsFalse(Shown(_reserve), "«Резерв: 0» would be a statement about a number we do not have");
        Assert.IsFalse(Shown(_block));
    }

    // ── the live tiers keep what they had ────────────────────────────────────────────────────

    [Test]
    public void An_active_plan_shows_its_rows_and_the_spendable_reserve_under_the_bar()
    {
        Purchased("tier_business");
        ClockNeverStarted();
        ServerSays("active", topupBalance: 2000, quota: 1000, used: 412);

        Render();

        Assert.AreEqual("Бизнес", _title.text);
        Assert.AreEqual("Активна", _pill.text);
        Assert.IsTrue(Shown(_block));
        Assert.IsTrue(Shown(_divider));
        AssertRows(true, "a plan has meters");
        Assert.AreEqual("412 из 1\u00A0000", _dialogs.text);
        Assert.IsTrue(Shown(_reserve));
        Assert.AreEqual("Резерв: 2\u00A0000 диалогов — спишутся после квоты", _reserve.text);
    }

    [Test]
    public void A_running_trial_with_a_reserve_shows_the_spendable_wording()
    {
        Purchased();
        ClockStarted(daysLeft: 3);
        ServerSays("trialing", topupBalance: 500, quota: 300);

        Render();

        Assert.AreEqual("Пробный · осталось 3 дн.", _subline.text);
        Assert.IsTrue(Shown(_reserve));
        Assert.AreEqual("Резерв: 500 диалогов — спишутся после квоты", _reserve.text);
        AssertRows(true, "a live trial shows its meters");
    }

    [Test]
    public void Buying_a_plan_after_a_lapse_brings_the_rows_back_and_unfreezes_the_reserve()
    {
        Purchased();
        ClockStarted(daysLeft: 0);
        ServerSays("expired", topupBalance: 500);
        Render();
        AssertRows(false, "lapsed");
        Assert.AreEqual(FrozenReserve500, _reserve.text);

        // The purchase lands: the store reports the entitlement, the server mirror follows.
        ResetSeams();
        Purchased("tier_start");
        ClockStarted(daysLeft: 0);
        ServerSays("active", topupBalance: 500, quota: 300, used: 10);
        Render();

        AssertRows(true, "a hidden row must come back when the plan does");
        Assert.AreEqual("Резерв: 500 диалогов — спишутся после квоты", _reserve.text);
        Assert.AreEqual("Активна", _pill.text);
    }

    // ── the headline and the reserve line never disagree ─────────────────────────────────────

    [Test]
    public void The_reserve_is_frozen_exactly_when_the_headline_says_expired_for_every_world()
    {
        foreach (string entitlement in new[] { null, "tier_business" })
        foreach (int clock in new[] { -1 /* never started */, 3, 0 })
        foreach (string server in new[] { "trialing", "expired" })
        {
            ResetSeams();
            if (entitlement == null) Purchased(); else Purchased(entitlement);
            if (clock < 0) ClockNeverStarted(); else ClockStarted(clock);
            ServerSays(server, topupBalance: 500, quota: 1000);

            Render();

            string world = $"entitlement={entitlement ?? "none"} clock={clock} server={server}";
            bool expired = _pill.text == SubscriptionPageRows.PillExpired;
            bool gateIsNone = EntitlementGate.CurrentTier == PlanTier.None;

            Assert.AreEqual(gateIsNone, expired, $"headline vs gate — {world}");
            Assert.IsTrue(Shown(_reserve), $"a paid reserve is always visible — {world}");
            Assert.AreEqual(expired, _reserve.text.Contains("заморожен"), $"frozen wording vs headline — {world}");
            Assert.AreEqual(!expired, _reserve.text.Contains("спишутся"), $"spendable wording vs headline — {world}");
            Assert.AreEqual(!gateIsNone, Shown(_rows[0]), $"rows follow the plan — {world}");
            Assert.IsTrue(Shown(_block), $"the block hosts the line — {world}");
        }
    }
}
