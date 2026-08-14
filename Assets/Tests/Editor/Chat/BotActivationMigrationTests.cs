using NUnit.Framework;

/// <summary>
/// Pins the paused-bot rescue matrix for the 2026-08 unification (bots-page
/// capsule → ReplyMode): only explicitly-paused bots migrate, a stored Auto
/// mode is pinned to Semi, suppression rows are planned for every real
/// profile id regardless of mode, and workflows re-activate only for
/// channels that are toggled on AND hold a real id.
/// </summary>
public class BotActivationMigrationTests
{
    private const string Sentinel = "-1";

    private static BotActivationMigration.MigrationPlan Plan(
        int master = 0, int mode = 1,
        bool waEnabled = true, bool tgEnabled = true,
        string waProfile = "wa-profile", string tgProfile = "tg-profile",
        string waWorkflow = "wa-wf", string tgWorkflow = "tg-wf") =>
        BotActivationMigration.Plan(master, mode, waEnabled, tgEnabled,
            waProfile, tgProfile, waWorkflow, tgWorkflow);

    [Test]
    public void NotPaused_NoMigration()
    {
        var plan = Plan(master: 1);
        Assert.IsFalse(plan.NeedsMigration);
        Assert.IsEmpty(plan.SuppressProfileIds);
        Assert.IsEmpty(plan.ActivateWorkflowIds);
    }

    [Test]
    public void Paused_WithStoredAuto_PinsSemi()
    {
        var plan = Plan(mode: 0);
        Assert.IsTrue(plan.NeedsMigration);
        Assert.IsTrue(plan.ForceSemi, "a paused bot must never resurrect auto-replying");
    }

    [Test]
    public void Paused_WithSemiOrDefault_KeepsModeButStillSuppresses()
    {
        // Absence of a '*' row reads as «reply» on the server — a default-Semi
        // bot may have NO row, so suppression is planned regardless of mode.
        var plan = Plan(mode: 1);
        Assert.IsFalse(plan.ForceSemi);
        Assert.AreEqual(new[] { "wa-profile", "tg-profile" }, plan.SuppressProfileIds);
    }

    [Test]
    public void SentinelAndEmptyProfileIds_NeverSuppressed()
    {
        var plan = Plan(waProfile: Sentinel, tgProfile: "");
        Assert.IsEmpty(plan.SuppressProfileIds, "\"-1\"/\"\" are not profiles");
    }

    [Test]
    public void Activation_RequiresToggleOnAndRealWorkflowId()
    {
        var both = Plan();
        Assert.AreEqual(new[] { "wa-wf", "tg-wf" }, both.ActivateWorkflowIds);

        var waOff = Plan(waEnabled: false);
        Assert.AreEqual(new[] { "tg-wf" }, waOff.ActivateWorkflowIds,
            "a channel the owner toggled off stays inactive — that gate survives the unification");

        var tgUnauthed = Plan(tgWorkflow: Sentinel);
        Assert.AreEqual(new[] { "wa-wf" }, tgUnauthed.ActivateWorkflowIds);

        var neverCreated = Plan(waWorkflow: "", tgWorkflow: Sentinel);
        Assert.IsEmpty(neverCreated.ActivateWorkflowIds);
    }

    [Test]
    public void Paused_FullyUnauthedBot_MigratesWithNoNetworkWork()
    {
        var plan = Plan(waProfile: Sentinel, tgProfile: Sentinel,
            waWorkflow: Sentinel, tgWorkflow: Sentinel);
        Assert.IsTrue(plan.NeedsMigration, "the dead master key still needs clearing");
        Assert.IsEmpty(plan.SuppressProfileIds);
        Assert.IsEmpty(plan.ActivateWorkflowIds);
    }
}
