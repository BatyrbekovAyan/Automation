using NUnit.Framework;

// EditMode coverage for the effective-«Вместе» override (final-review I-1, Task 17b R1).
// Pure decision seam — no Unity objects, no network, no PlayerPrefs.
public class QuotaFallbackPolicyTests
{
    private static UsageSnapshot Snap(string status = "active", int quota = 1000,
        int used = 1000, int topup = 0, bool success = true)
        => new UsageSnapshot
        {
            success = success, plan = "business", status = status,
            quota = quota, used = used, topupBalance = topup,
        };

    // --- the state the override exists for ---------------------------------

    [Test]
    public void Over_quota_with_an_empty_reserve_falls_back()
        => Assert.IsTrue(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(used: 1000, topup: 0)));

    [Test]
    public void Past_the_quota_still_falls_back()
        => Assert.IsTrue(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(used: 1420, topup: 0)));

    // --- states that must NOT override the owner's stored mode --------------

    [Test]
    public void Inside_the_quota_does_not_fall_back()
        => Assert.IsFalse(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(used: 999)));

    [Test]
    public void A_remaining_reserve_does_not_fall_back()
    {
        // The reserve still pays for auto-replies (owner decision 2026-08-26), so the bot is
        // answering by itself — there is nothing to fall back FROM.
        Assert.IsFalse(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(used: 1000, topup: 500)));
        Assert.IsFalse(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(used: 1499, topup: 1)));
    }

    [Test]
    public void Unknown_usage_never_falls_back()
    {
        // A cold boot has no snapshot yet: «not known» must not read as «zero quota, all spent»,
        // or every «Авто» chat would open the panel before the first GetUsage read lands.
        Assert.IsFalse(QuotaFallbackPolicy.ShouldFallBackToSemi(null));
        Assert.IsFalse(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(success: false)),
            "an unusable body is not a usage reading");
    }

    [Test]
    public void No_plan_never_falls_back()
    {
        // PlanTier.None reads back as quota 0 / used 0 — «0 из 0» is a missing plan, not a
        // spent one, and the bots are not running at all.
        Assert.IsFalse(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(quota: 0, used: 0)));
        Assert.IsFalse(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(quota: 0, used: 12)));
    }

    // --- subscription gate parity (Task 17a's Suggest_Replies gate) ---------

    [TestCase("active", true)]
    [TestCase("trialing", true)]
    [TestCase("grace", true)]
    [TestCase("expired", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    [TestCase("something_new", false)]
    public void Only_a_serviceable_status_may_fall_back(string status, bool expected)
        => Assert.AreEqual(expected,
            QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(status: status, used: 1000, topup: 0)),
            "the server refuses suggestions for an expired/unknown account — raising the panel " +
            "there would replace silence with an error card");

    [Test]
    public void Status_matching_is_case_insensitive()
        => Assert.IsTrue(QuotaFallbackPolicy.ShouldFallBackToSemi(Snap(status: "ACTIVE")));

    [Test]
    public void Status_constants_match_the_wire_values()
    {
        // Duplicated in the subscribers table and in every workflow that writes it — a rename
        // on either side has to fail here rather than silently disabling the fallback.
        Assert.AreEqual("active", QuotaFallbackPolicy.StatusActive);
        Assert.AreEqual("trialing", QuotaFallbackPolicy.StatusTrialing);
        Assert.AreEqual("grace", QuotaFallbackPolicy.StatusGrace);
        Assert.AreEqual("expired", QuotaFallbackPolicy.StatusExpired);
    }

    // --- the scalar core ----------------------------------------------------

    [Test]
    public void Negative_reserve_is_treated_as_empty()
        => Assert.IsTrue(QuotaFallbackPolicy.ShouldFallBackToSemi("active", 300, 300, -1));
}
