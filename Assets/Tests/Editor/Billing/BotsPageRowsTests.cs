using System;
using NUnit.Framework;

/// <summary>
/// Pins every user-facing string and state decision of the «Боты» billing surface
/// (Task 14c, spec §6): the header trial pill, the account dialog meter strip and
/// the «+ бот» card's remaining-count subtext.
///
/// Same discipline as PaywallRowsTests / SubscriptionPageRowsTests: NBSP is written
/// as a \u00A0 ESCAPE, never as the raw byte — an edit round-trip has silently
/// degraded a typed NBSP to a plain space in this repo before (Task 4's lesson).
/// </summary>
public class BotsPageRowsTests
{
    // ── Trial pill ───────────────────────────────────────────────────────────

    [Test]
    public void A_started_trial_shows_the_pill_with_the_day_count()
    {
        var pill = BotsPageRows.TrialPill(PlanTier.Trial, trialStarted: true, daysLeft: 3);

        Assert.IsTrue(pill.Visible);
        Assert.AreEqual("Пробный · 3 дн.", pill.Text);
        Assert.AreEqual(ThemeRole.AccentSoft, pill.Bg);
        Assert.AreEqual(ThemeRole.AccentText, pill.Ink);
    }

    [Test]
    public void The_pre_auth_grace_shows_no_pill_because_the_clock_has_not_started()
    {
        // EntitlementPolicy grants Trial before the first channel auth (spec §3), but
        // there is no countdown to advertise yet — TrialLedger.DaysLeft() would report
        // the full TrialDays forever.
        var pill = BotsPageRows.TrialPill(PlanTier.Trial, trialStarted: false, daysLeft: PlanCatalog.TrialDays);
        Assert.IsFalse(pill.Visible);
    }

    [Test]
    public void No_pill_on_any_tier_other_than_trial()
    {
        foreach (PlanTier tier in new[] { PlanTier.None, PlanTier.Start, PlanTier.Business, PlanTier.Network })
            Assert.IsFalse(BotsPageRows.TrialPill(tier, true, 3).Visible, $"tier={tier}");
    }

    [Test]
    public void The_last_day_switches_the_pill_to_the_urgent_tint()
    {
        var pill = BotsPageRows.TrialPill(PlanTier.Trial, true, BotsPageRows.UrgentDaysLeft);

        Assert.IsTrue(pill.Visible);
        Assert.AreEqual("Пробный · 1 дн.", pill.Text);
        Assert.AreEqual(ThemeRole.DestructiveSoft, pill.Bg);
        Assert.AreEqual(ThemeRole.Destructive, pill.Ink);
    }

    [Test]
    public void Two_days_left_is_still_the_calm_tint()
    {
        var pill = BotsPageRows.TrialPill(PlanTier.Trial, true, 2);
        Assert.AreEqual(ThemeRole.AccentSoft, pill.Bg);
        Assert.AreEqual(ThemeRole.AccentText, pill.Ink);
    }

    [Test]
    public void A_spent_or_rolled_back_clock_never_renders_a_negative_countdown()
    {
        // Reachable inside BillingService's resolve-window grace: CurrentTier is Trial
        // while the local clock already reads zero (or worse, after a clock rollback).
        foreach (int days in new[] { 0, -1, -400 })
        {
            var pill = BotsPageRows.TrialPill(PlanTier.Trial, true, days);
            Assert.AreEqual("Пробный · 0 дн.", pill.Text, $"days={days}");
            Assert.AreEqual(ThemeRole.DestructiveSoft, pill.Bg, $"days={days}");
        }
    }

    [Test]
    public void No_pill_state_paints_itself_the_colour_of_the_bar_it_sits_on()
    {
        // The pill lives in NavHeader, which is ThemeRole.Surface in both themes — a
        // Surface pill would simply vanish (the 14b status-pill regression).
        foreach (int days in new[] { 5, 3, 1, 0 })
        {
            var pill = BotsPageRows.TrialPill(PlanTier.Trial, true, days);
            Assert.AreNotEqual(ThemeRole.Surface, pill.Bg, $"days={days}");
            Assert.AreNotEqual(pill.Bg, pill.Ink, $"days={days}");
        }
    }

    // ── Meter title ──────────────────────────────────────────────────────────

    [Test]
    public void Meter_title_names_the_current_month_in_the_nominative()
    {
        Assert.AreEqual("Диалоги ИИ · август", BotsPageRows.MeterTitle(new DateTime(2026, 8, 22)));
    }

    [Test]
    public void Meter_title_covers_every_month()
    {
        string[] expected =
        {
            "январь", "февраль", "март", "апрель", "май", "июнь",
            "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь",
        };
        for (int m = 1; m <= 12; m++)
            Assert.AreEqual("Диалоги ИИ · " + expected[m - 1],
                BotsPageRows.MeterTitle(new DateTime(2026, m, 15)), $"month={m}");
    }

    // ── Meter hint ───────────────────────────────────────────────────────────

    [Test]
    public void Below_the_warn_threshold_there_is_no_hint_line()
    {
        // The mockup's own numbers: 214 of 300 is 71 % — comfortable, nothing to say.
        Assert.IsNull(BotsPageRows.MeterHint(214, 300, 0));
        Assert.IsNull(BotsPageRows.MeterHint(239, 300, 0));   // 79 % — last quiet step
    }

    [Test]
    public void The_warn_threshold_is_exactly_eighty_percent()
    {
        Assert.IsNull(BotsPageRows.MeterHint(239, 300, 0));
        Assert.IsNotNull(BotsPageRows.MeterHint(240, 300, 0));   // 80 % on the nose
    }

    [Test]
    public void The_warn_hint_counts_what_is_left_and_offers_the_top_up()
    {
        Assert.AreEqual("Осталось 60 — докупить 500 за 3\u00A0900\u00A0₸",
            BotsPageRows.MeterHint(240, 300, 0));
    }

    [Test]
    public void The_warn_hint_agrees_with_the_number_it_counts()
    {
        // «остался 1» vs «осталось 86» — the verb takes the one/many split, and 11..14
        // are "many" despite ending in 1..4 (RuPlural's whole reason for existing).
        Assert.AreEqual("Остался 1 — докупить 500 за 3\u00A0900\u00A0₸",
            BotsPageRows.MeterHint(299, 300, 0));
        Assert.AreEqual("Осталось 86 — докупить 500 за 3\u00A0900\u00A0₸",
            BotsPageRows.MeterHint(914, 1000, 0));
        Assert.AreEqual("Осталось 11 — докупить 500 за 3\u00A0900\u00A0₸",
            BotsPageRows.MeterHint(989, 1000, 0));   // 11..14 are "many" despite the 1
        Assert.AreEqual("Остался 21 — докупить 500 за 3\u00A0900\u00A0₸",
            BotsPageRows.MeterHint(979, 1000, 0));   // …but 21 is not
        Assert.AreEqual("Осталось 22 — докупить 500 за 3\u00A0900\u00A0₸",
            BotsPageRows.MeterHint(978, 1000, 0));   // 2..4 take the neuter too
    }

    [Test]
    public void An_exhausted_quota_says_what_the_bot_does_now()
    {
        Assert.AreEqual("Лимит исчерпан — бот отвечает в режиме «Вместе»",
            BotsPageRows.MeterHint(300, 300, 0));
        Assert.AreEqual("Лимит исчерпан — бот отвечает в режиме «Вместе»",
            BotsPageRows.MeterHint(4000, 300, 0));
    }

    [Test]
    public void A_top_up_raises_the_ceiling_the_hint_measures_against()
    {
        // Exhausting the BASE quota is already Warn even with credits in hand — that is
        // QuotaMath's rule, shared with the «Подписка» bar, and it is honest: the plan's
        // own allowance is gone. What is LEFT, though, counts the top-up, so the line
        // offers a real number instead of a wall.
        Assert.AreEqual("Осталось 500 — докупить 500 за 3\u00A0900\u00A0₸",
            BotsPageRows.MeterHint(300, 300, 500));
        Assert.AreEqual("Осталось 40 — докупить 500 за 3\u00A0900\u00A0₸",
            BotsPageRows.MeterHint(760, 300, 500));
        // Over only once the credits are spent too.
        Assert.AreEqual(BotsPageRows.OverHint, BotsPageRows.MeterHint(800, 300, 500));
    }

    [Test]
    public void The_hint_line_only_turns_red_when_the_quota_is_actually_spent()
    {
        Assert.AreEqual(ThemeRole.InkSecondary, BotsPageRows.HintRole(QuotaState.Ok));
        Assert.AreEqual(ThemeRole.InkSecondary, BotsPageRows.HintRole(QuotaState.Warn));
        Assert.AreEqual(ThemeRole.Destructive, BotsPageRows.HintRole(QuotaState.Over));
    }

    // ── Meter visibility ─────────────────────────────────────────────────────

    [Test]
    public void No_meter_without_a_plan()
    {
        // Delegates to SubscriptionPageRows.MetersVisible — PlanTier.None has no
        // allowances at all, so a meter would read «— из 0».
        Assert.IsFalse(BotsPageRows.MeterVisible(PlanTier.None, 2));
    }

    [Test]
    public void No_meter_before_the_first_bot_exists()
    {
        // Zero bots is the EmptyState's screen; an account dialog meter over it is noise.
        Assert.IsFalse(BotsPageRows.MeterVisible(PlanTier.Business, 0));
    }

    [Test]
    public void Every_real_tier_with_a_bot_shows_the_meter()
    {
        foreach (PlanTier tier in new[] { PlanTier.Trial, PlanTier.Start, PlanTier.Business, PlanTier.Network })
            Assert.IsTrue(BotsPageRows.MeterVisible(tier, 1), $"tier={tier}");
    }

    // ── Add-bot subtext ──────────────────────────────────────────────────────

    [Test]
    public void Add_bot_subtext_counts_the_slots_left_in_the_plan()
    {
        Assert.AreEqual("Ещё 3 бота в тарифе", BotsPageRows.AddBotSubtext(PlanTier.Business, 0));
        Assert.AreEqual("Ещё 2 бота в тарифе", BotsPageRows.AddBotSubtext(PlanTier.Business, 1));
        Assert.AreEqual("Ещё 1 бот в тарифе", BotsPageRows.AddBotSubtext(PlanTier.Business, 2));
        Assert.AreEqual("Ещё 5 ботов в тарифе", BotsPageRows.AddBotSubtext(PlanTier.Network, 0));
        Assert.AreEqual("Ещё 1 бот в тарифе", BotsPageRows.AddBotSubtext(PlanTier.Start, 0));
    }

    [Test]
    public void At_the_limit_the_subtext_says_so_instead_of_counting_zero()
    {
        Assert.AreEqual("Лимит ботов тарифа", BotsPageRows.AddBotSubtext(PlanTier.Business, 3));
        Assert.AreEqual("Лимит ботов тарифа", BotsPageRows.AddBotSubtext(PlanTier.Start, 1));
        Assert.AreEqual("Лимит ботов тарифа", BotsPageRows.AddBotSubtext(PlanTier.Network, 5));
        Assert.AreEqual("Лимит ботов тарифа", BotsPageRows.AddBotSubtext(PlanTier.None, 0));
    }

    [Test]
    public void More_bots_than_the_plan_allows_never_counts_backwards()
    {
        // Reachable after a downgrade: the bots stay, the allowance shrinks.
        Assert.AreEqual("Лимит ботов тарифа", BotsPageRows.AddBotSubtext(PlanTier.Start, 4));
    }

    [Test]
    public void The_trial_grants_the_business_slot_count()
    {
        Assert.AreEqual("Ещё 3 бота в тарифе", BotsPageRows.AddBotSubtext(PlanTier.Trial, 0));
    }

    // ── The one fixed string that is also a layout decision ──────────────────

    [Test]
    public void The_add_bot_card_title_is_stable()
    {
        Assert.AreEqual("Добавить бота", BotsPageRows.AddBotTitle);
    }

    // ── Auto-open gate (Task 14d) ────────────────────────────────────────────

    [TestCase(PlanTier.Trial)]
    [TestCase(PlanTier.Start)]
    [TestCase(PlanTier.Business)]
    [TestCase(PlanTier.Network)]
    public void A_plan_with_room_still_auto_opens_the_wizard_at_zero_bots(PlanTier tier)
    {
        // The zero-bots happy path, unchanged: every tier with MaxBots > 0 keeps opening the
        // first-bot wizard on arrival.
        Assert.IsTrue(BotsPageRows.ShouldAutoOpenWizard(tier, 0));
    }

    [Test]
    public void No_subscription_never_auto_opens_a_wizard_it_would_refuse()
    {
        // PlanTier.None allows 0 bots, so the auto-open would refuse — and a refusal now raises
        // the limit sheet, which would mean a modal on every arrival at «Боты». The empty state
        // keeps the screen; its CTA is the tap that earns the sheet.
        Assert.IsFalse(BotsPageRows.ShouldAutoOpenWizard(PlanTier.None, 0));
    }

    [TestCase(PlanTier.Trial, 1)]
    [TestCase(PlanTier.Business, 2)]
    [TestCase(PlanTier.Network, 5)]
    [TestCase(PlanTier.None, 3)]
    public void A_page_that_already_has_bots_never_auto_opens(PlanTier tier, int liveBots)
    {
        // Belt and braces: the call site only reaches this inside !hasBots, but the rule reads
        // «no bots AND room», not «room».
        Assert.IsFalse(BotsPageRows.ShouldAutoOpenWizard(tier, liveBots));
    }

    [TestCase(PlanTier.None)]
    [TestCase(PlanTier.Trial)]
    [TestCase(PlanTier.Start)]
    [TestCase(PlanTier.Business)]
    [TestCase(PlanTier.Network)]
    public void The_auto_open_gate_agrees_with_the_bot_gate_at_zero_bots(PlanTier tier)
    {
        // The auto-open must never disagree with what StartNewBot would do — that disagreement
        // IS the unwanted modal (auto-open true, gate false).
        Assert.AreEqual(EntitlementPolicy.CanCreateBot(tier, 0),
                        BotsPageRows.ShouldAutoOpenWizard(tier, 0));
    }

    // The UsageClient in-flight guard moved to UsageClientTests when Task 14d gave it a
    // staleness arm — it was never about this screen's rows.
}
