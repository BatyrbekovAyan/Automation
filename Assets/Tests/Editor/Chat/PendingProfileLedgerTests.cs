using NUnit.Framework;
using UnityEngine;

// Contract tests for the pending-Wappi-profile ledger backing quit-time and
// next-launch orphan cleanup. The ledger's keys are global PlayerPrefs (not
// per-bot, and deliberately identical to the pre-ledger raw keys), so each
// test snapshots whatever real state the editor had and restores it after.
public class PendingProfileLedgerTests
{
    private const string WaIdKey = "lastCreatedWhatsappProfileId";
    private const string WaSavedKey = "lastCreatedWhatsappProfileIdSaved";
    private const string TgIdKey = "lastCreatedTelegramProfileId";
    private const string TgSavedKey = "lastCreatedTelegramProfileIdSaved";

    private string _savedWaId, _savedTgId;
    private int _savedWaSaved, _savedTgSaved;

    [SetUp]
    public void SetUp()
    {
        _savedWaId = PlayerPrefs.GetString(WaIdKey, "-1");
        _savedWaSaved = PlayerPrefs.GetInt(WaSavedKey, 1);
        _savedTgId = PlayerPrefs.GetString(TgIdKey, "-1");
        _savedTgSaved = PlayerPrefs.GetInt(TgSavedKey, 1);

        PlayerPrefs.DeleteKey(WaIdKey);
        PlayerPrefs.DeleteKey(WaSavedKey);
        PlayerPrefs.DeleteKey(TgIdKey);
        PlayerPrefs.DeleteKey(TgSavedKey);
    }

    [TearDown]
    public void TearDown()
    {
        PlayerPrefs.SetString(WaIdKey, _savedWaId);
        PlayerPrefs.SetInt(WaSavedKey, _savedWaSaved);
        PlayerPrefs.SetString(TgIdKey, _savedTgId);
        PlayerPrefs.SetInt(TgSavedKey, _savedTgSaved);
    }

    [Test]
    public void TryGetPending_False_OnFreshState()
    {
        Assert.IsFalse(PendingProfileLedger.TryGetPendingWhatsapp(out string id));
        Assert.AreEqual("-1", id);
    }

    [Test]
    public void MarkPending_ThenTryGet_ReturnsProfileId()
    {
        PendingProfileLedger.MarkWhatsappPending("WA-123");

        Assert.IsTrue(PendingProfileLedger.TryGetPendingWhatsapp(out string id));
        Assert.AreEqual("WA-123", id);
    }

    [Test]
    public void MarkPending_IgnoresSentinelAndEmpty()
    {
        PendingProfileLedger.MarkWhatsappPending("-1");
        PendingProfileLedger.MarkWhatsappPending("");
        PendingProfileLedger.MarkWhatsappPending(null);

        Assert.IsFalse(PendingProfileLedger.TryGetPendingWhatsapp(out _));
    }

    [Test]
    public void MarkClaimed_ClearsPendingState()
    {
        PendingProfileLedger.MarkWhatsappPending("WA-123");
        PendingProfileLedger.MarkWhatsappClaimed();

        Assert.IsFalse(PendingProfileLedger.TryGetPendingWhatsapp(out _));
    }

    [Test]
    public void ClearIfMatches_WrongId_KeepsPendingEntry()
    {
        PendingProfileLedger.MarkWhatsappPending("WA-123");
        PendingProfileLedger.ClearWhatsappIfMatches("WA-OTHER");

        Assert.IsTrue(PendingProfileLedger.TryGetPendingWhatsapp(out string id));
        Assert.AreEqual("WA-123", id);
    }

    [Test]
    public void ClearIfMatches_RightId_ResetsToSentinelAndClaimed()
    {
        PendingProfileLedger.MarkWhatsappPending("WA-123");
        PendingProfileLedger.ClearWhatsappIfMatches("WA-123");

        Assert.IsFalse(PendingProfileLedger.TryGetPendingWhatsapp(out _));
        Assert.AreEqual("-1", PlayerPrefs.GetString(WaIdKey));
        Assert.AreEqual(1, PlayerPrefs.GetInt(WaSavedKey));
    }

    // Locks the key names: pending state written by pre-ledger builds (raw
    // PlayerPrefs writes) must still be visible after an app update.
    [Test]
    public void TryGetPending_SeesLegacyRawKeys()
    {
        PlayerPrefs.SetString(WaIdKey, "WA-LEGACY");
        PlayerPrefs.SetInt(WaSavedKey, 0);

        Assert.IsTrue(PendingProfileLedger.TryGetPendingWhatsapp(out string id));
        Assert.AreEqual("WA-LEGACY", id);
    }

    // Matches the historical sweep guard: Saved==0 with the "-1" sentinel id
    // must not report a pending profile.
    [Test]
    public void TryGetPending_False_WhenSavedZeroButIdIsSentinel()
    {
        PlayerPrefs.SetString(WaIdKey, "-1");
        PlayerPrefs.SetInt(WaSavedKey, 0);

        Assert.IsFalse(PendingProfileLedger.TryGetPendingWhatsapp(out _));
    }

    [Test]
    public void Channels_AreIndependent()
    {
        PendingProfileLedger.MarkWhatsappPending("WA-123");
        PendingProfileLedger.MarkTelegramPending("TG-456");

        PendingProfileLedger.MarkWhatsappClaimed();

        Assert.IsFalse(PendingProfileLedger.TryGetPendingWhatsapp(out _));
        Assert.IsTrue(PendingProfileLedger.TryGetPendingTelegram(out string tgId));
        Assert.AreEqual("TG-456", tgId);
    }

    [Test]
    public void Telegram_MarkPending_ThenClearIfMatches_Settles()
    {
        PendingProfileLedger.MarkTelegramPending("TG-456");
        PendingProfileLedger.ClearTelegramIfMatches("TG-456");

        Assert.IsFalse(PendingProfileLedger.TryGetPendingTelegram(out _));
        Assert.AreEqual("-1", PlayerPrefs.GetString(TgIdKey));
        Assert.AreEqual(1, PlayerPrefs.GetInt(TgSavedKey));
    }
}
