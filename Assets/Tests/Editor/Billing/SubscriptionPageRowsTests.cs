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

    // ── StatusLine ───────────────────────────────────────────────────────────

    [Test]
    public void Active_business_shows_price_and_renewal_date()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.Business, true, 0, PeriodEndUtc);

        Assert.AreEqual(SubscriptionState.Active, line.State);
        Assert.AreEqual("Бизнес", line.Title);
        Assert.AreEqual("Активна", line.PillText);
        Assert.AreEqual(ThemeRole.PositiveBg, line.PillBg);
        Assert.AreEqual(ThemeRole.PositiveInk, line.PillInk);
        Assert.AreEqual("19\u00A0900\u00A0₸/мес · продлится 26 августа", line.Subline);
    }

    [Test]
    public void Active_start_tier_carries_its_own_price()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.Start, true, 0, PeriodEndUtc);
        Assert.AreEqual("Старт", line.Title);
        Assert.AreEqual("9\u00A0900\u00A0₸/мес · продлится 26 августа", line.Subline);
    }

    [Test]
    public void Active_without_a_usable_period_end_drops_the_renewal_clause()
    {
        foreach (string iso in new[] { null, "", "   ", "not-a-date" })
        {
            var line = SubscriptionPageRows.StatusLine(PlanTier.Network, true, 0, iso);
            Assert.AreEqual("39\u00A0900\u00A0₸/мес", line.Subline, $"iso={iso ?? "null"}");
        }
    }

    [Test]
    public void Renewal_date_survives_an_offset_timestamp()
    {
        // Not every producer stamps Z; a +05:00 offset must still resolve to a real date.
        var line = SubscriptionPageRows.StatusLine(PlanTier.Business, true, 0, "2026-09-03T10:00:00+05:00");
        StringAssert.EndsWith("продлится 3 сентября", line.Subline);
    }

    [Test]
    public void Trial_with_days_left_shows_the_countdown()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, true, 3, null);

        Assert.AreEqual(SubscriptionState.Trial, line.State);
        Assert.AreEqual("Пробный", line.Title);
        Assert.AreEqual("Пробный", line.PillText);
        Assert.AreEqual(ThemeRole.Surface, line.PillBg);
        Assert.AreEqual(ThemeRole.InkTertiary, line.PillInk);
        Assert.AreEqual("Пробный · осталось 3 дн.", line.Subline);
    }

    [Test]
    public void Trial_that_never_started_is_still_a_trial()
    {
        // Pre-auth grace (EntitlementPolicy.EffectiveTier(None,false,false) == Trial):
        // the clock starts at first channel auth, so «not started» must not read as expired.
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, false, PlanCatalog.TrialDays, null);
        Assert.AreEqual(SubscriptionState.Trial, line.State);
        Assert.AreEqual("Пробный · осталось 5 дн.", line.Subline);
    }

    [Test]
    public void Trial_last_day_still_counts_one()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, true, 1, null);
        Assert.AreEqual("Пробный · осталось 1 дн.", line.Subline);
    }

    [Test]
    public void Spent_trial_reads_as_no_subscription()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, true, 0, null);

        Assert.AreEqual(SubscriptionState.Expired, line.State);
        Assert.AreEqual("Без подписки", line.Title);
        Assert.AreEqual("Истекла", line.PillText);
        Assert.AreEqual(ThemeRole.InkTertiary, line.PillInk);
        Assert.AreEqual("Подписка не оформлена", line.Subline);
    }

    [Test]
    public void A_purchase_outranks_a_spent_trial_clock()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.Business, true, 0, PeriodEndUtc);
        Assert.AreEqual(SubscriptionState.Active, line.State);
    }

    [Test]
    public void Negative_days_left_never_renders_as_a_countdown()
    {
        var line = SubscriptionPageRows.StatusLine(PlanTier.None, true, -4, null);
        Assert.AreEqual(SubscriptionState.Expired, line.State);
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
    public void A_top_up_extends_both_the_denominator_and_the_state()
    {
        var line = SubscriptionPageRows.UsageLine(1000, 1000, 500);
        Assert.AreEqual("1\u00A0000 из 1\u00A0500", line.Text);
        Assert.AreEqual(QuotaState.Warn, line.State, "докупленные диалоги ещё не потрачены");

        Assert.AreEqual(QuotaState.Over, SubscriptionPageRows.UsageLine(1500, 1000, 500).State);
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
        Assert.AreEqual(0f, SubscriptionPageRows.FillFraction(0, 1000, 0), 0.0001f);
        Assert.AreEqual(0.412f, SubscriptionPageRows.FillFraction(412, 1000, 0), 0.0001f);
        Assert.AreEqual(1f, SubscriptionPageRows.FillFraction(2500, 1000, 0), 0.0001f, "перерасход не рисует полосу шире дорожки");
        Assert.AreEqual(1f, SubscriptionPageRows.FillFraction(5, 0, 0), 0.0001f, "нулевая квота — полоса полная, не деление на ноль");
        Assert.AreEqual(0f, SubscriptionPageRows.FillFraction(-3, 1000, 0), 0.0001f);
    }

    [Test]
    public void Fill_role_escalates_with_the_quota_state()
    {
        Assert.AreEqual(ThemeRole.AccentFill, SubscriptionPageRows.FillRole(QuotaState.Ok));
        Assert.AreEqual(ThemeRole.StatusOwnerNeeded, SubscriptionPageRows.FillRole(QuotaState.Warn));
        Assert.AreEqual(ThemeRole.Destructive, SubscriptionPageRows.FillRole(QuotaState.Over));
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
        Assert.AreEqual("Управление подпиской — в настройках App Store / Google Play",
            SubscriptionPageRows.CancelCaption);
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
