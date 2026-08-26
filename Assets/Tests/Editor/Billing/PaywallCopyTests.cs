using NUnit.Framework;

public class PaywallCopyTests
{
    [TestCase(9900, "9 900 ₸")]
    [TestCase(199000, "199 000 ₸")]
    [TestCase(500, "500 ₸")]
    [TestCase(-199000, "-199 000 ₸")]
    public void Kzt_groups_thousands_with_nbsp(int v, string s) => Assert.AreEqual(s, PaywallCopy.Kzt(v));

    [TestCase(1, "1 диалог")]
    [TestCase(22, "22 диалога")]
    [TestCase(300, "300 диалогов")]
    [TestCase(11, "11 диалогов")]
    public void Dialog_plural(int n, string s) => Assert.AreEqual(s, PaywallCopy.Dialogs(n));

    [Test] public void Trial_cta_is_five_days() => StringAssert.Contains("5 дней", PaywallCopy.TrialCta());
    [Test] public void Year_line_carries_the_savings_claim() => StringAssert.Contains("выгода до 17%", PaywallCopy.YearLine(PlanCatalog.Get(PlanTier.Start)));
    [Test] public void YearSavingBadge_is_pinned() => Assert.AreEqual("до -17%", PaywallCopy.YearSavingBadge);
    [Test] public void PerMonth_appends_suffix() => Assert.AreEqual("9 900 ₸/мес", PaywallCopy.PerMonth(9900));
    [Test] public void TrialPill_formats_days() => Assert.AreEqual("Пробный · 3 дн.", PaywallCopy.TrialPill(3));
}
