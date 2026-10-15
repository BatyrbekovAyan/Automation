using System;
using NUnit.Framework;

/// <summary>
/// Pins every user-facing string and state decision of the Профиль → «Подписка»
/// page (Task 14b). Same discipline as PaywallRowsTests: NBSP is written as a
/// \u00A0 ESCAPE, never as the raw byte — an edit round-trip has silently degraded
/// a typed NBSP to a plain space in this repo before (Task 4's lesson).
/// </summary>
public class SubscriptionPageRowsTests
{
    // Real payload shape: Postgres timestamptz round-tripped through JSON.
    const string PeriodEndUtc = "2026-08-26T09:00:00Z";

    [SetUp]
    public void Seams()
    {
        // Identity localiser: a real device converts UTC→local, which would make the
        // rendered DAY depend on the machine's timezone and turn these asserts flaky.
        SubscriptionPageRows.LocalizeUtc = d => d;
    }

    [TearDown]
    public void Reset() => SubscriptionPageRows.ResetSeamsForTests();

    // A purchased plan decides on its own — the trial inputs are noise for the formatting
    // tests below, so they pass the quiet defaults (clock never started, server silent).
    private static SubscriptionStatusLine Paid(PlanTier tier, string periodEndIso, string interval = null)
        => SubscriptionPageRows.StatusLine(tier, trialStarted: false, trialDaysLeft: PlanCatalog.TrialDays,
            serverSaysExpired: false, periodEndIso: periodEndIso, interval: interval);

    // ── StatusLine ───────────────────────────────────────────────────────────

    [Test]
    public void Active_business_shows_price_and_renewal_date()
    {
        var line = Paid(PlanTier.Business, PeriodEndUtc);

        Assert.AreEqual(SubscriptionState.Active, line.State);
        Assert.AreEqual("Бизнес", line.Title);
        Assert.AreEqual("Активна", line.PillText);
        Assert.AreEqual(ThemeRole.PositiveBg, line.PillBg);
        Assert.AreEqual(ThemeRole.PositiveInk, line.PillInk);
        Assert.AreEqual("19\u00A0990\u00A0₸/мес · продлится 26 августа", line.Subline);
    }

    [Test]
    public void An_annual_subscriber_sees_the_year_price_and_period()
    {
        var line = Paid(PlanTier.Business, "2026-09-03T09:00:00Z",
            interval: SubscriptionPageRows.IntervalYear);

        Assert.AreEqual(SubscriptionState.Active, line.State);
        Assert.AreEqual("Бизнес", line.Title);
        Assert.AreEqual("198\u00A0990\u00A0₸/год · продлится 3 сентября", line.Subline);
    }

    [Test]
    public void An_explicit_month_interval_is_the_unchanged_monthly_line()
    {
        var line = Paid(PlanTier.Business, PeriodEndUtc,
            interval: SubscriptionPageRows.IntervalMonth);
        Assert.AreEqual("19\u00A0990\u00A0₸/мес · продлится 26 августа", line.Subline);
    }

    [Test]
    public void An_unknown_or_missing_interval_falls_back_to_the_month_line()
    {
        // Server said null (unrecognised SKU suffix), said nothing at all (pre-15a payload),
        // or answered something we do not model — all three are «период неизвестен», and the
        // KNOWN DEFAULT is monthly. Never guess the annual figure: it is 10× the monthly one.
        foreach (string interval in new[] { null, "", "   ", "week", "YEARLY" })
            Assert.AreEqual("19\u00A0990\u00A0₸/мес · продлится 26 августа",
                Paid(PlanTier.Business, PeriodEndUtc, interval).Subline,
                $"interval={interval ?? "null"}");
    }

    [Test]
    public void The_year_price_is_read_from_the_catalog_for_every_tier()
    {
        Assert.AreEqual("99\u00A0000\u00A0₸/год",
            SubscriptionPageRows.ActiveSubline(PlanTier.Start, null, SubscriptionPageRows.IntervalYear));
        Assert.AreEqual("399\u00A0990\u00A0₸/год",
            SubscriptionPageRows.ActiveSubline(PlanTier.Network, null, SubscriptionPageRows.IntervalYear));
    }

    [Test]
    public void Active_start_tier_carries_its_own_price()
    {
        var line = Paid(PlanTier.Start, PeriodEndUtc);
        Assert.AreEqual("Старт", line.Title);
        Assert.AreEqual("9\u00A0990\u00A0₸/мес · продлится 26 августа", line.Subline);
    }

    [Test]
    public void Active_without_a_usable_period_end_drops_the_renewal_clause()
    {
        foreach (string iso in new[] { null, "", "   ", "not-a-date" })
        {
            var line = Paid(PlanTier.Network, iso);
            Assert.AreEqual("39\u00A0900\u00A0₸/мес", line.Subline, $"iso={iso ?? "null"}");
        }
    }

    [Test]
    public void An_offset_timestamp_normalises_to_utc_before_the_seam_sees_it()
    {
        // Not every producer stamps Z. 10:00+05:00 IS 05:00Z, so with the seam set to identity
        // this must land on 3 сентября on every machine — if ParsePeriodEnd ever stopped
        // normalising, the day would start following the test runner's own timezone.
        var line = Paid(PlanTier.Business, "2026-09-03T10:00:00+05:00");
        StringAssert.EndsWith("продлится 3 сентября", line.Subline);

        // Same instant, other side of midnight UTC: 01:00+05:00 is the PREVIOUS day in UTC.
        Assert.AreEqual(new DateTime(2026, 9, 2, 20, 0, 0, DateTimeKind.Utc),
            SubscriptionPageRows.ParsePeriodEnd("2026-09-03T01:00:00+05:00"));
    }

    [Test]
    public void Trial_with_days_left_shows_the_countdown()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, trialStarted: true, trialDaysLeft: 3,
            serverSaysExpired: false, periodEndIso: null);

        Assert.AreEqual(SubscriptionState.Trial, line.State);
        Assert.AreEqual("Пробный", line.Title);
        Assert.AreEqual("Пробный", line.PillText);
        // Hairline, NOT Surface: the pill sits on a Surface card and would vanish into it.
        Assert.AreEqual(ThemeRole.Hairline, line.PillBg);
        Assert.AreEqual(ThemeRole.InkTertiary, line.PillInk);
        Assert.AreEqual("Пробный · осталось 3 дн.", line.Subline);
    }

    [Test]
    public void A_never_started_clock_is_a_trial_while_the_server_is_silent()
    {
        // Pre-auth grace: TrialLedger.DaysLeft() returns the full TrialDays before the clock
        // has ever started. That alone says nothing about the account — only the server can
        // say a reinstall has hidden a spent one (see the incident test below) — so «not
        // started» is a trial exactly as long as the server has not said «expired».
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, trialStarted: false,
            trialDaysLeft: PlanCatalog.TrialDays, serverSaysExpired: false, periodEndIso: null);
        Assert.AreEqual(SubscriptionState.Trial, line.State);
        Assert.AreEqual("Пробный · осталось 5 дн.", line.Subline);
        Assert.AreEqual(SubscriptionState.Trial, SubscriptionPageRows.State(PlanTier.None,
            trialStarted: false, trialDaysLeft: PlanCatalog.TrialDays, serverSaysExpired: false));
    }

    [Test]
    public void No_pill_ever_paints_itself_the_colour_of_the_card_beneath_it()
    {
        foreach (var line in new[]
                 {
                     Paid(PlanTier.Business, PeriodEndUtc),
                     SubscriptionPageRows.StatusLine(PlanTier.None, true, 3, false, null),    // running trial
                     SubscriptionPageRows.StatusLine(PlanTier.None, true, 0, false, null),    // spent trial
                     SubscriptionPageRows.StatusLine(PlanTier.None, false, PlanCatalog.TrialDays, true, null), // server-expired
                 })
            Assert.AreNotEqual(ThemeRole.Surface, line.PillBg, $"pill «{line.PillText}» исчезнет на карточке");
    }

    [Test]
    public void Trial_last_day_still_counts_one()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, trialStarted: true, trialDaysLeft: 1,
            serverSaysExpired: false, periodEndIso: null);
        Assert.AreEqual("Пробный · осталось 1 дн.", line.Subline);
    }

    [Test]
    public void Spent_trial_reads_as_no_subscription()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, trialStarted: true, trialDaysLeft: 0,
            serverSaysExpired: false, periodEndIso: null);

        Assert.AreEqual(SubscriptionState.Expired, line.State);
        Assert.AreEqual("Без подписки", line.Title);
        Assert.AreEqual("Истекла", line.PillText);
        Assert.AreEqual(ThemeRole.Hairline, line.PillBg);
        Assert.AreEqual(ThemeRole.InkTertiary, line.PillInk);
        Assert.AreEqual("Подписка не оформлена", line.Subline);
    }

    [Test]
    public void A_purchase_outranks_a_spent_trial_clock_and_the_servers_expired()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.Business, trialStarted: true, trialDaysLeft: 0,
            serverSaysExpired: true, periodEndIso: PeriodEndUtc);
        Assert.AreEqual(SubscriptionState.Active, line.State);
        Assert.AreEqual("Бизнес", line.Title);
    }

    [Test]
    public void Negative_days_left_never_renders_as_a_countdown()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, trialStarted: true, trialDaysLeft: -4,
            serverSaysExpired: false, periodEndIso: null);
        Assert.AreEqual(SubscriptionState.Expired, line.State);
    }

    // ── The state is the gate's verdict (EntitlementPolicy.EffectiveTier), cell by cell ──────

    // Nothing purchased. The ledger is the CLIENT's clock; the server's word can only TAKE an
    // unstarted trial (the reinstall incident of 2026-08-26: the RevenueCat id survived, the local
    // ledger did not) — it cannot take a running one — and EntitlementGate.CurrentTier agrees
    // with every row, which is the whole point: the page must never contradict the gate.
    [TestCase(false, 5, false, SubscriptionState.Trial)]     // pre-auth grace: the first auth starts the clock
    [TestCase(false, 5, true, SubscriptionState.Expired)]    // reinstall wiped the ledger, the server knows
    [TestCase(true, 3, false, SubscriptionState.Trial)]
    [TestCase(true, 3, true, SubscriptionState.Trial)]       // a running local trial lives by its own clock
    [TestCase(true, 1, true, SubscriptionState.Trial)]
    [TestCase(true, 0, false, SubscriptionState.Expired)]
    [TestCase(true, 0, true, SubscriptionState.Expired)]
    [TestCase(true, -4, false, SubscriptionState.Expired)]
    public void Without_a_purchase_the_state_follows_the_clock_and_only_then_the_servers_word(
        bool trialStarted, int daysLeft, bool serverSaysExpired, SubscriptionState expected)
        => Assert.AreEqual(expected,
            SubscriptionPageRows.State(PlanTier.None, trialStarted, daysLeft, serverSaysExpired));

    [TestCase(PlanTier.Start)]
    [TestCase(PlanTier.Business)]
    [TestCase(PlanTier.Network)]
    public void A_purchase_is_active_whatever_the_clock_and_the_server_say(PlanTier purchased)
    {
        foreach (bool started in new[] { false, true })
        foreach (int days in new[] { PlanCatalog.TrialDays, 0, -4 })
        foreach (bool serverExpired in new[] { false, true })
            Assert.AreEqual(SubscriptionState.Active,
                SubscriptionPageRows.State(purchased, started, days, serverExpired),
                $"started={started} days={days} serverExpired={serverExpired}");
    }

    [Test]
    public void A_reinstall_that_wiped_the_trial_ledger_still_reads_expired_when_the_server_says_so()
    {
        // The contradiction this task closes: after the reinstall TrialLedger.DaysLeft() reports
        // the FULL trial — the page used to print «Пробный · осталось 5 дн.» — while the gate
        // (EntitlementGate.CurrentTier) was already None and the server knew the account was spent.
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, trialStarted: false,
            trialDaysLeft: PlanCatalog.TrialDays, serverSaysExpired: true, periodEndIso: null);

        Assert.AreEqual(SubscriptionState.Expired, line.State);
        Assert.AreEqual("Без подписки", line.Title);
        Assert.AreEqual("Истекла", line.PillText);
        Assert.AreEqual(ThemeRole.Hairline, line.PillBg);
        Assert.AreEqual(ThemeRole.InkTertiary, line.PillInk);
        Assert.AreEqual("Подписка не оформлена", line.Subline);
        StringAssert.DoesNotContain("осталось", line.Subline);
    }

    [Test]
    public void A_running_local_trial_keeps_its_countdown_when_the_server_disagrees()
    {
        // EffectiveTier: a started trial lives by its OWN clock — the page follows the gate here too
        // (a headline of «Истекла» over a gate that still lets the owner create bots would be the
        // contradiction again, mirrored).
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, trialStarted: true, trialDaysLeft: 3,
            serverSaysExpired: true, periodEndIso: null);

        Assert.AreEqual(SubscriptionState.Trial, line.State);
        Assert.AreEqual("Пробный · осталось 3 дн.", line.Subline);
    }

    [Test]
    public void The_state_is_the_gates_verdict_for_every_reachable_combination()
    {
        // TrialLedger's own invariants make the domain finite: a clock that never started reads the
        // FULL trial and is never «expired»; a started one reads what is left and is expired at <= 0.
        var clocks = new[]
        {
            (started: false, days: PlanCatalog.TrialDays),
            (started: true, days: PlanCatalog.TrialDays),
            (started: true, days: 3),
            (started: true, days: 1),
            (started: true, days: 0),
            (started: true, days: -4),
        };

        foreach (PlanTier purchased in new[] { PlanTier.None, PlanTier.Start, PlanTier.Business, PlanTier.Network })
        foreach (var clock in clocks)
        foreach (bool serverExpired in new[] { false, true })
        {
            bool trialExpired = clock.started && clock.days <= 0;     // TrialLedger.IsExpired
            PlanTier gate = EntitlementPolicy.EffectiveTier(purchased, clock.started, trialExpired, serverExpired);
            SubscriptionState want = gate == PlanTier.None ? SubscriptionState.Expired
                                   : gate == PlanTier.Trial ? SubscriptionState.Trial
                                   : SubscriptionState.Active;

            Assert.AreEqual(want,
                SubscriptionPageRows.State(purchased, clock.started, clock.days, serverExpired),
                $"purchased={purchased} started={clock.started} days={clock.days} serverExpired={serverExpired} → gate={gate}");
        }
    }

    // ── UsageLine ────────────────────────────────────────────────────────────

    [Test]
    public void Usage_under_the_warn_threshold_is_ok()
    {
        var line = SubscriptionPageRows.UsageLine(412, 1000, 0);
        Assert.AreEqual("412 из 1\u00A0000", line.Text);
        Assert.AreEqual(QuotaState.Ok, line.State);
    }

    [Test]
    public void Usage_at_eighty_percent_warns()
    {
        var line = SubscriptionPageRows.UsageLine(800, 1000, 0);
        Assert.AreEqual("800 из 1\u00A0000", line.Text);
        Assert.AreEqual(QuotaState.Warn, line.State);
    }

    [Test]
    public void Usage_at_the_cap_is_over()
    {
        var line = SubscriptionPageRows.UsageLine(1000, 1000, 0);
        Assert.AreEqual(QuotaState.Over, line.State);
    }

    [Test]
    public void The_denominator_is_always_the_base_quota_and_the_reserve_only_moves_the_state()
    {
        // Резерв не расширяет месячную квоту (решение владельца 2026-08-26) — он тратится
        // ПОСЛЕ неё. Раньше знаменатель был quota + topup, и он сползал на каждом
        // израсходованном диалоге резерва.
        var line = SubscriptionPageRows.UsageLine(1000, 1000, 500);
        Assert.AreEqual("1\u00A0000 из 1\u00A0000", line.Text);
        Assert.AreEqual(QuotaState.Reserve, line.State, "квота исчерпана, но резерв ещё платит");

        // Перерасход печатается честно — числитель не подрезается под знаменатель.
        var spending = SubscriptionPageRows.UsageLine(1240, 1000, 260);
        Assert.AreEqual("1\u00A0240 из 1\u00A0000", spending.Text);
        Assert.AreEqual(QuotaState.Reserve, spending.State);

        Assert.AreEqual(QuotaState.Over, SubscriptionPageRows.UsageLine(1500, 1000, 0).State);
    }

    [Test]
    public void An_unread_snapshot_shows_a_dash_not_a_fake_zero()
    {
        var line = SubscriptionPageRows.UnknownUsageLine(1000);
        Assert.AreEqual("— из 1\u00A0000", line.Text);
        Assert.AreEqual(QuotaState.Ok, line.State);
    }

    [Test]
    public void Fill_fraction_tracks_usage_and_clamps()
    {
        Assert.AreEqual(0f, SubscriptionPageRows.FillFraction(0, 1000), 0.0001f);
        Assert.AreEqual(0.412f, SubscriptionPageRows.FillFraction(412, 1000), 0.0001f);
        Assert.AreEqual(1f, SubscriptionPageRows.FillFraction(2500, 1000), 0.0001f, "перерасход не рисует полосу шире дорожки");
        Assert.AreEqual(0f, SubscriptionPageRows.FillFraction(5, 0), 0.0001f, "нет тарифа — полоса ПУСТАЯ, не полная (и не деление на ноль)");
        Assert.AreEqual(0f, SubscriptionPageRows.FillFraction(-3, 1000), 0.0001f);
    }

    [Test]
    public void Fill_fraction_measures_the_same_denominator_the_line_prints()
    {
        // Полоса и число под ней не могут говорить о разных знаменателях: резерв тратится
        // ЗА концом дорожки, а не удлиняет её.
        Assert.AreEqual(1f, SubscriptionPageRows.FillFraction(1000, 1000), 0.0001f);
        Assert.AreEqual(1f, SubscriptionPageRows.FillFraction(1240, 1000), 0.0001f);
    }

    [Test]
    public void Fill_role_escalates_with_the_quota_state()
    {
        Assert.AreEqual(ThemeRole.AccentFill, SubscriptionPageRows.FillRole(QuotaState.Ok));
        Assert.AreEqual(ThemeRole.StatusOwnerNeeded, SubscriptionPageRows.FillRole(QuotaState.Warn));
        Assert.AreEqual(ThemeRole.StatusOwnerNeeded, SubscriptionPageRows.FillRole(QuotaState.Reserve),
            "резерв ещё платит: не красная стена, но и не спокойный синий");
        Assert.AreEqual(ThemeRole.Destructive, SubscriptionPageRows.FillRole(QuotaState.Over));
    }

    [Test]
    public void Meters_are_hidden_when_there_is_no_plan_to_measure_against()
    {
        // PlanCatalog.Get(None) is 0/0/0 — a visible meter would read «2 из 0».
        Assert.IsFalse(SubscriptionPageRows.MetersVisible(PlanTier.None));
        Assert.IsTrue(SubscriptionPageRows.MetersVisible(PlanTier.Trial));
        Assert.IsTrue(SubscriptionPageRows.MetersVisible(PlanTier.Start));
        Assert.IsTrue(SubscriptionPageRows.MetersVisible(PlanTier.Business));
        Assert.IsTrue(SubscriptionPageRows.MetersVisible(PlanTier.Network));
    }

    // ── ReserveLine ──────────────────────────────────────────────────────────

    [Test]
    public void A_purchased_reserve_is_named_under_the_meter_while_it_is_unspent()
    {
        // Sandbox pass 2026-09-06: four top-ups had landed server-side (topupBalance 2000) and
        // the page still read «0 из 1 000» — the reserve was invisible until the quota ran out,
        // because the denominator is the BASE quota by design (2026-08-26). This line is where
        // the paid reserve becomes visible without touching that denominator.
        Assert.AreEqual("Резерв: 2\u00A0000 диалогов — спишутся после квоты",
            SubscriptionPageRows.ReserveLine(2000, PlanTier.Business, frozen: false));
        Assert.AreEqual("Резерв: 500 диалогов — спишутся после квоты",
            SubscriptionPageRows.ReserveLine(PlanCatalog.TopUpDialogs, PlanTier.Business, frozen: false));
    }

    [Test]
    public void An_empty_reserve_yields_no_line_so_the_label_hides()
    {
        foreach (PlanTier tier in new[] { PlanTier.None, PlanTier.Trial, PlanTier.Start, PlanTier.Business, PlanTier.Network })
        foreach (bool frozen in new[] { false, true })
        {
            Assert.IsNull(SubscriptionPageRows.ReserveLine(0, tier, frozen), $"tier={tier} frozen={frozen}");
            Assert.IsNull(SubscriptionPageRows.ReserveLine(-5, tier, frozen),
                $"a negative wire value is «nothing to show», never «-5 диалогов» (tier={tier} frozen={frozen})");
        }
    }

    [Test]
    public void The_reserve_line_agrees_noun_and_verb_with_the_count()
    {
        Assert.AreEqual("Резерв: 1 диалог — спишется после квоты", SubscriptionPageRows.ReserveLine(1, PlanTier.Start, false));
        Assert.AreEqual("Резерв: 2 диалога — спишутся после квоты", SubscriptionPageRows.ReserveLine(2, PlanTier.Start, false));
        Assert.AreEqual("Резерв: 5 диалогов — спишутся после квоты", SubscriptionPageRows.ReserveLine(5, PlanTier.Start, false));
        Assert.AreEqual("Резерв: 11 диалогов — спишутся после квоты", SubscriptionPageRows.ReserveLine(11, PlanTier.Start, false));
        Assert.AreEqual("Резерв: 21 диалог — спишется после квоты", SubscriptionPageRows.ReserveLine(21, PlanTier.Start, false));
    }

    [Test]
    public void Every_live_tier_keeps_the_spendable_wording()
    {
        // The trial counts as live: PlanCatalog gives it a quota, and a top-up is spent after it.
        foreach (PlanTier tier in new[] { PlanTier.Trial, PlanTier.Start, PlanTier.Business, PlanTier.Network })
            Assert.AreEqual("Резерв: 500 диалогов — спишутся после квоты",
                SubscriptionPageRows.ReserveLine(500, tier, frozen: false), $"tier={tier}");
    }

    // ── ReserveLine without a plan: visible, and honest that it is frozen ────────────────────

    [Test]
    public void Without_a_plan_the_reserve_is_shown_as_frozen_not_as_spendable()
    {
        // The balance is paid for and survives the lapse (the server keeps it and stops spending
        // it while the account is not active/trialing). The page used to hide the whole meters
        // block at PlanTier.None — the reserve with it — so a lapsed owner saw nothing for the
        // packs they bought. Hidden is a lie of omission; «спишутся после квоты» would be a
        // promise the server does not keep; «заморожен» is the truth.
        Assert.AreEqual("Резерв: 500 диалогов — заморожен до возобновления подписки",
            SubscriptionPageRows.ReserveLine(500, PlanTier.None, frozen: false));
        Assert.AreEqual("Резерв: 2\u00A0000 диалогов — заморожен до возобновления подписки",
            SubscriptionPageRows.ReserveLine(2000, PlanTier.None, frozen: true));
    }

    [Test]
    public void The_frozen_flag_alone_freezes_the_reserve_on_any_tier()
    {
        // `tier` and `frozen` are two spellings of one fact on the page; a caller that has only one
        // of them must never get the spendable promise for an account the server will not spend for.
        foreach (PlanTier tier in new[] { PlanTier.None, PlanTier.Trial, PlanTier.Start, PlanTier.Business, PlanTier.Network })
            Assert.AreEqual("Резерв: 500 диалогов — заморожен до возобновления подписки",
                SubscriptionPageRows.ReserveLine(500, tier, frozen: true), $"tier={tier}");
    }

    [Test]
    public void The_frozen_line_agrees_the_noun_with_the_count_and_keeps_the_participle_singular()
    {
        // «заморожен» agrees with «Резерв» (masculine singular), not with the count — unlike
        // «спишется/спишутся», which agrees with «диалоги». Only the noun moves (RuPlural 1/2/5).
        Assert.AreEqual("Резерв: 1 диалог — заморожен до возобновления подписки", SubscriptionPageRows.ReserveLine(1, PlanTier.None, false));
        Assert.AreEqual("Резерв: 2 диалога — заморожен до возобновления подписки", SubscriptionPageRows.ReserveLine(2, PlanTier.None, false));
        Assert.AreEqual("Резерв: 5 диалогов — заморожен до возобновления подписки", SubscriptionPageRows.ReserveLine(5, PlanTier.None, false));
        Assert.AreEqual("Резерв: 11 диалогов — заморожен до возобновления подписки", SubscriptionPageRows.ReserveLine(11, PlanTier.None, false));
        Assert.AreEqual("Резерв: 21 диалог — заморожен до возобновления подписки", SubscriptionPageRows.ReserveLine(21, PlanTier.None, false));
    }

    // ── The meters block outlives its rows ───────────────────────────────────────────────────

    [Test]
    public void The_meters_block_stays_for_a_reserve_line_when_the_rows_are_hidden()
    {
        // MetersVisible is about the ROWS («2 из 0» is not a limit). The reserve line lives in the
        // same block, so a block hidden by the rows' rule alone took the line with it — the
        // reserve is visible when there is a line, independent of MetersVisible.
        Assert.IsFalse(SubscriptionPageRows.MetersVisible(PlanTier.None), "the rows' own rule is unchanged");

        Assert.IsFalse(SubscriptionPageRows.MetersBlockVisible(PlanTier.None, null), "no plan, no reserve: nothing at all");
        Assert.IsFalse(SubscriptionPageRows.MetersBlockVisible(PlanTier.None, ""), "an empty line is no line");
        Assert.IsTrue(SubscriptionPageRows.MetersBlockVisible(PlanTier.None,
            SubscriptionPageRows.ReserveLine(500, PlanTier.None, false)), "no plan, a paid reserve: the block hosts its line");

        foreach (PlanTier tier in new[] { PlanTier.Trial, PlanTier.Start, PlanTier.Business, PlanTier.Network })
        {
            Assert.IsTrue(SubscriptionPageRows.MetersBlockVisible(tier, null), $"{tier}: the rows always show");
            Assert.IsTrue(SubscriptionPageRows.MetersBlockVisible(tier, "Резерв: 500 диалогов — спишутся после квоты"), $"{tier}");
        }
    }

    // ── Count rows / actions ─────────────────────────────────────────────────

    [Test]
    public void Count_line_reads_current_out_of_limit()
    {
        Assert.AreEqual("2 из 3", SubscriptionPageRows.CountLine(2, 3));
        Assert.AreEqual("0 из 1", SubscriptionPageRows.CountLine(0, 1));
        Assert.AreEqual("1\u00A0000 из 1\u00A0000", SubscriptionPageRows.CountLine(1000, 1000));
    }

    [Test]
    public void Top_up_row_names_the_pack_and_its_price()
    {
        Assert.AreEqual("Купить 500 диалогов — 3\u00A0900\u00A0₸", SubscriptionPageRows.TopUpRowText());
        // Store-localized top-up price wins when the fetch has landed (Apple 3.1.2);
        // an empty store string keeps the KZT fallback.
        Assert.AreEqual("Купить 500 диалогов — $9.99", SubscriptionPageRows.TopUpRowText("$9.99"));
        Assert.AreEqual("Купить 500 диалогов — 3\u00A0900\u00A0₸", SubscriptionPageRows.TopUpRowText(""));
    }

    [Test]
    public void Cancel_is_offered_only_to_someone_who_bought_something()
    {
        Assert.IsFalse(SubscriptionPageRows.CancelVisible(PlanTier.None));
        Assert.IsFalse(SubscriptionPageRows.CancelVisible(PlanTier.Trial), "триал нечего отменять — карты нет");
        Assert.IsTrue(SubscriptionPageRows.CancelVisible(PlanTier.Start));
        Assert.IsTrue(SubscriptionPageRows.CancelVisible(PlanTier.Business));
        Assert.IsTrue(SubscriptionPageRows.CancelVisible(PlanTier.Network));
    }

    [Test]
    public void Fixed_row_copy_is_pinned()
    {
        Assert.AreEqual("Подписка", SubscriptionPageRows.PageTitle);
        Assert.AreEqual("Изменить тариф", SubscriptionPageRows.ChangePlanRow);
        Assert.AreEqual("Восстановить покупки", SubscriptionPageRows.RestoreRow);
        Assert.AreEqual("Отменить подписку", SubscriptionPageRows.CancelRow);
        // 2.3.10: the SEED must stay store-neutral (it ships baked in the scene of
        // BOTH platforms' binaries); the store names live only in the per-store
        // variants stamped at runtime.
        Assert.AreEqual("Управление подпиской — в настройках магазина приложений",
            SubscriptionPageRows.CancelCaption);
        Assert.AreEqual("Управление подпиской — в настройках App Store",
            SubscriptionPageRows.CancelCaptionText(iosStore: true));
        Assert.AreEqual("Управление подпиской — в настройках Google Play",
            SubscriptionPageRows.CancelCaptionText(iosStore: false));
        StringAssert.DoesNotContain("Google Play", SubscriptionPageRows.CancelCaptionText(iosStore: true));
        Assert.AreEqual("Диалоги ИИ", SubscriptionPageRows.DialogsTitle);
        Assert.AreEqual("Боты", SubscriptionPageRows.BotsTitle);
        Assert.AreEqual("Каналы", SubscriptionPageRows.ChannelsTitle);
    }

    [Test]
    public void Notices_are_pinned_so_a_reword_is_a_one_place_edit()
    {
        Assert.AreEqual("Диалоги начислены", SubscriptionPageRows.TopUpDoneNotice);
        Assert.AreEqual("Не удалось купить диалоги. Попробуйте ещё раз.", SubscriptionPageRows.TopUpFailedNotice);
    }

    // ── RU date seam this page introduced ────────────────────────────────────

    [Test]
    public void Genitive_months_cover_the_year()
    {
        string[] expected =
        {
            "января", "февраля", "марта", "апреля", "мая", "июня",
            "июля", "августа", "сентября", "октября", "ноября", "декабря",
        };
        for (int m = 1; m <= 12; m++)
            Assert.AreEqual(expected[m - 1], RuDateFormat.MonthGenitive(m), $"месяц {m}");
    }

    [Test]
    public void Day_month_reads_as_a_russian_date()
    {
        Assert.AreEqual("26 августа", RuDateFormat.DayMonth(new DateTime(2026, 8, 26)));
        Assert.AreEqual("1 января", RuDateFormat.DayMonth(new DateTime(2026, 1, 1)));
        Assert.AreEqual("31 декабря", RuDateFormat.DayMonth(new DateTime(2026, 12, 31)));
    }
}
