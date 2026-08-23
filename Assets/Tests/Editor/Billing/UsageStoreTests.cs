using NUnit.Framework;

public class UsageStoreTests
{
    [SetUp]
    public void Seams() => UsageStore.ResetSeamsForTests();

    [TearDown]
    public void Reset() => UsageStore.ResetSeamsForTests();

    private const string ValidJson =
        "{\"success\":true,\"plan\":\"business\",\"status\":\"active\",\"quota\":1000,\"used\":7," +
        "\"topupBalance\":500,\"botsRegistered\":1,\"channelsConnected\":2,\"periodEnd\":\"2026-09-20T10:43:02.757Z\"}";

    // --- Parse -----------------------------------------------------------------------------

    [Test] public void Parse_valid_json_round_trips_every_field_exactly()
    {
        var snapshot = UsageStore.Parse(ValidJson);

        Assert.IsNotNull(snapshot);
        Assert.IsTrue(snapshot.success);
        Assert.AreEqual("business", snapshot.plan);
        Assert.AreEqual("active", snapshot.status);
        Assert.AreEqual(1000, snapshot.quota);
        Assert.AreEqual(7, snapshot.used);
        Assert.AreEqual(500, snapshot.topupBalance);
        Assert.AreEqual(1, snapshot.botsRegistered);
        Assert.AreEqual(2, snapshot.channelsConnected);
        Assert.AreEqual("2026-09-20T10:43:02.757Z", snapshot.periodEnd);
    }

    [Test] public void Parse_valid_json_with_null_period_end_round_trips_null()
    {
        var snapshot = UsageStore.Parse(
            "{\"success\":true,\"plan\":\"trial\",\"status\":\"trialing\",\"quota\":150,\"used\":0," +
            "\"topupBalance\":0,\"botsRegistered\":0,\"channelsConnected\":0,\"periodEnd\":null}");

        Assert.IsNotNull(snapshot);
        Assert.AreEqual("trial", snapshot.plan);
        Assert.IsNull(snapshot.periodEnd);
    }

    // Task 15a: the annual-interval fields. `interval` is derived server-side from the SKU
    // suffix; the client only carries it (and the raw productId, for diagnosis).
    [Test] public void Parse_carries_the_billing_interval_and_product_id()
    {
        var snapshot = UsageStore.Parse(
            "{\"success\":true,\"plan\":\"business\",\"status\":\"active\",\"quota\":1000,\"used\":7," +
            "\"topupBalance\":500,\"botsRegistered\":1,\"channelsConnected\":2," +
            "\"periodEnd\":\"2026-09-03T10:00:00Z\",\"productId\":\"sub.business.year\",\"interval\":\"year\"}");

        Assert.IsNotNull(snapshot);
        Assert.AreEqual("year", snapshot.interval);
        Assert.AreEqual("sub.business.year", snapshot.productId);
    }

    [Test] public void Parse_tolerates_a_payload_without_the_interval_fields()
    {
        // An older Get Usage deployment (or a response from before Task 15a) simply omits
        // them — that must land as null, not throw and not lose the rest of the snapshot.
        var snapshot = UsageStore.Parse(ValidJson);

        Assert.IsNotNull(snapshot);
        Assert.IsNull(snapshot.interval);
        Assert.IsNull(snapshot.productId);
        Assert.AreEqual(1000, snapshot.quota, "остальные поля не пострадали");
    }

    [Test] public void Parse_explicit_null_interval_round_trips_null()
    {
        var snapshot = UsageStore.Parse(
            "{\"success\":true,\"plan\":\"start\",\"status\":\"active\",\"quota\":300,\"used\":0," +
            "\"topupBalance\":0,\"botsRegistered\":0,\"channelsConnected\":0,\"periodEnd\":null," +
            "\"productId\":\"legacy.grandfathered\",\"interval\":null}");

        Assert.IsNotNull(snapshot);
        Assert.IsNull(snapshot.interval);
        Assert.AreEqual("legacy.grandfathered", snapshot.productId);
    }

    [Test] public void Parse_garbage_returns_null_without_throwing()
    {
        UsageSnapshot snapshot = null;
        Assert.DoesNotThrow(() => snapshot = UsageStore.Parse("not json at all { garbage"));
        Assert.IsNull(snapshot);
    }

    [Test] public void Parse_empty_string_returns_null_without_throwing()
    {
        UsageSnapshot snapshot = null;
        Assert.DoesNotThrow(() => snapshot = UsageStore.Parse(""));
        Assert.IsNull(snapshot);
    }

    [Test] public void Parse_null_returns_null_without_throwing()
    {
        UsageSnapshot snapshot = null;
        Assert.DoesNotThrow(() => snapshot = UsageStore.Parse(null));
        Assert.IsNull(snapshot);
    }

    // --- Apply -----------------------------------------------------------------------------

    [Test] public void Apply_sets_current_and_fires_event()
    {
        var snapshot = UsageStore.Parse(ValidJson);
        UsageSnapshot seenAtFire = null;
        int fireCount = 0;
        UsageStore.OnUsageChanged += () => { fireCount++; seenAtFire = UsageStore.Current; };

        UsageStore.Apply(snapshot);

        Assert.AreEqual(1, fireCount);
        Assert.AreSame(snapshot, UsageStore.Current);
        Assert.AreSame(snapshot, seenAtFire, "Current must already be updated by the time subscribers see the event");
    }

    [Test] public void Apply_null_is_a_no_op_and_does_not_fire()
    {
        var first = UsageStore.Parse(ValidJson);
        UsageStore.Apply(first);
        bool fired = false;
        UsageStore.OnUsageChanged += () => fired = true;

        UsageStore.Apply(null);

        Assert.IsFalse(fired);
        Assert.AreSame(first, UsageStore.Current, "a null Apply must never clear an already-cached snapshot");
    }

    // --- Seams -----------------------------------------------------------------------------

    [Test] public void ResetSeamsForTests_clears_current_and_subscribers()
    {
        UsageStore.Apply(UsageStore.Parse(ValidJson));
        bool called = false;
        UsageStore.OnUsageChanged += () => called = true;

        UsageStore.ResetSeamsForTests();

        Assert.IsNull(UsageStore.Current);
        UsageStore.Apply(UsageStore.Parse(ValidJson));   // would fire the leaked subscriber if not cleared
        Assert.IsFalse(called, "a subscriber surviving ResetSeamsForTests would leak into other tests");
    }
}
