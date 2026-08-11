using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Covers OnboardingProgressReset — the «Первые шаги» latch teardown Bot.DeleteBot runs.
// The latches are GLOBAL PlayerPrefs keys (outside the per-bot "BotN…" namespace), so the
// side-effecting tests snapshot whatever real state the editor had and restore it after
// (same convention as PendingProfileLedgerTests).
public class OnboardingProgressResetTests
{
    private int _savedChannel, _savedPriceList, _savedFirstReply, _savedDone, _savedSeen;

    [SetUp]
    public void SetUp()
    {
        _savedChannel = PlayerPrefs.GetInt(OnboardingKeys.ChannelConnectedSeen, 0);
        _savedPriceList = PlayerPrefs.GetInt(OnboardingKeys.PriceListUploadedSeen, 0);
        _savedFirstReply = PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0);
        _savedDone = PlayerPrefs.GetInt(OnboardingKeys.ChecklistDone, 0);
        _savedSeen = PlayerPrefs.GetInt(OnboardingKeys.Seen, 0);
    }

    [TearDown]
    public void TearDown()
    {
        PlayerPrefs.SetInt(OnboardingKeys.ChannelConnectedSeen, _savedChannel);
        PlayerPrefs.SetInt(OnboardingKeys.PriceListUploadedSeen, _savedPriceList);
        PlayerPrefs.SetInt(OnboardingKeys.FirstBotReplySeen, _savedFirstReply);
        PlayerPrefs.SetInt(OnboardingKeys.ChecklistDone, _savedDone);
        PlayerPrefs.SetInt(OnboardingKeys.Seen, _savedSeen);
    }

    private static void LatchEverything()
    {
        PlayerPrefs.SetInt(OnboardingKeys.ChannelConnectedSeen, 1);
        PlayerPrefs.SetInt(OnboardingKeys.PriceListUploadedSeen, 1);
        PlayerPrefs.SetInt(OnboardingKeys.FirstBotReplySeen, 1);
        PlayerPrefs.SetInt(OnboardingKeys.ChecklistDone, 1);
        PlayerPrefs.SetInt(OnboardingKeys.Seen, 1);
    }

    // ── The pure rule ─────────────────────────────────────────────────────────

    [Test]
    public void ShouldReset_LastBotDeleted_True()
        => Assert.IsTrue(OnboardingProgressReset.ShouldReset(0),
            "No bots left ⇒ the next bot starts onboarding from scratch.");

    [Test]
    public void ShouldReset_OtherBotsRemain_False()
    {
        Assert.IsFalse(OnboardingProgressReset.ShouldReset(1),
            "A remaining bot still owns the checklist — its progress must not be wiped.");
        Assert.IsFalse(OnboardingProgressReset.ShouldReset(5));
    }

    [Test]
    public void ShouldReset_NegativeCount_TreatedAsEmpty()
        => Assert.IsTrue(OnboardingProgressReset.ShouldReset(-1),
            "Defensive: a bogus count must never leave stale latches behind.");

    // ── The key list (the contract Bot.DeleteBot depends on) ──────────────────

    [Test]
    public void Keys_CoverAllThreeProgressLatches()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                OnboardingKeys.ChannelConnectedSeen,
                OnboardingKeys.PriceListUploadedSeen,
                OnboardingKeys.FirstBotReplySeen,
            },
            OnboardingProgressReset.Keys,
            "Rows 2-4 are the per-bot-earned milestones; row 1 is derived live from the bot count.");
    }

    [Test]
    public void Keys_ExcludeCarouselSeenAndChecklistDone()
    {
        Assert.IsFalse(OnboardingProgressReset.Keys.Contains(OnboardingKeys.Seen),
            "The welcome carousel is once-per-install (OnboardingGate.ShouldAutoFlagSeen) — never resurface it.");
        Assert.IsFalse(OnboardingProgressReset.Keys.Contains(OnboardingKeys.ChecklistDone),
            "Spec: the card never resurfaces after a real 4/4 completion.");
    }

    // ── The side effect ───────────────────────────────────────────────────────

    [Test]
    public void Clear_DeletesEveryProgressLatch()
    {
        LatchEverything();

        OnboardingProgressReset.Clear();

        Assert.AreEqual(0, PlayerPrefs.GetInt(OnboardingKeys.ChannelConnectedSeen, 0));
        Assert.AreEqual(0, PlayerPrefs.GetInt(OnboardingKeys.PriceListUploadedSeen, 0));
        Assert.AreEqual(0, PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0));
    }

    [Test]
    public void Clear_LeavesCarouselSeenAndChecklistDoneAlone()
    {
        LatchEverything();

        OnboardingProgressReset.Clear();

        Assert.AreEqual(1, PlayerPrefs.GetInt(OnboardingKeys.Seen, 0));
        Assert.AreEqual(1, PlayerPrefs.GetInt(OnboardingKeys.ChecklistDone, 0));
    }

    [Test]
    public void OnBotDeleted_LastBot_ClearsLatches()
    {
        LatchEverything();

        OnboardingProgressReset.OnBotDeleted(remainingBots: 0);

        Assert.AreEqual(0, PlayerPrefs.GetInt(OnboardingKeys.ChannelConnectedSeen, 0));
        Assert.AreEqual(0, PlayerPrefs.GetInt(OnboardingKeys.PriceListUploadedSeen, 0));
        Assert.AreEqual(0, PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0));
    }

    [Test]
    public void OnBotDeleted_OtherBotsRemain_KeepsLatches()
    {
        LatchEverything();

        OnboardingProgressReset.OnBotDeleted(remainingBots: 1);

        Assert.AreEqual(1, PlayerPrefs.GetInt(OnboardingKeys.ChannelConnectedSeen, 0));
        Assert.AreEqual(1, PlayerPrefs.GetInt(OnboardingKeys.PriceListUploadedSeen, 0));
        Assert.AreEqual(1, PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0),
            "Row 4 is a global, non-derivable fact — deleting a secondary bot must not regress it.");
    }

    // ── The bug this fixes, end to end at the policy level ────────────────────

    [Test]
    public void DeleteOnlyBotThenCreateAnother_ChecklistStartsAtOneOfFour()
    {
        LatchEverything();                                    // bot A got to 4/4-worth of progress
        PlayerPrefs.SetInt(OnboardingKeys.ChecklistDone, 0);   // …but never latched completion

        OnboardingProgressReset.OnBotDeleted(remainingBots: 0);

        // Bot B exists; every other fact is false for a brand-new bot.
        bool[] steps = FirstStepsChecklist.StepStates(
            botExists: true,
            channelAuthed: FirstStepsChecklist.Milestone(
                PlayerPrefs.GetInt(OnboardingKeys.ChannelConnectedSeen, 0) == 1, liveFact: false),
            hasFiles: FirstStepsChecklist.Milestone(
                PlayerPrefs.GetInt(OnboardingKeys.PriceListUploadedSeen, 0) == 1, liveFact: false),
            firstReplySeen: PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0) == 1);

        Assert.AreEqual(new[] { true, false, false, false }, steps,
            "The new bot's checklist shows 1 из 4 — not the deleted bot's checked rows.");
    }
}
