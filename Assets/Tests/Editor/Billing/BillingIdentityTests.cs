using NUnit.Framework;

public class BillingIdentityTests
{
    [TearDown] public void Reset() => BillingIdentity.ResetSeamsForTests();

    [Test] public void Default_source_is_non_empty()
    {
        Assert.IsFalse(string.IsNullOrEmpty(BillingIdentity.AppUserId));
    }

    [Test] public void Stable_across_two_reads()
    {
        var first = BillingIdentity.AppUserId;
        var second = BillingIdentity.AppUserId;
        Assert.AreEqual(first, second);
    }

    [Test] public void Seam_override_is_honored()
    {
        BillingIdentity.Source = () => "fixed-test-app-user-id";
        Assert.AreEqual("fixed-test-app-user-id", BillingIdentity.AppUserId);
    }
}
