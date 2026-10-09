using NUnit.Framework;
using UnityEngine;

/// <summary>
/// M4 (follow-ups 2026-10-09): the retired-channel marker — the device's memory that the server retired one channel of a
/// bot. Per-bot PlayerPrefs keys ({bot}{Whatsapp|Telegram}Retired{ProfileId|Reason}); it holds for its own id only, yields
/// to the token, and goes with the bot. Runs against the real PlayerPrefs under a test-only bot key, removed afterwards.
/// </summary>
public class ProfileRetirementTests
{
    private const string BotKey = "M4RetirementTestBot";

    private static InstallSession.TokenClaims Token(params string[] profiles) =>
        new InstallSession.TokenClaims { InstallId = "i", Profiles = profiles, Workflows = new string[0], Exp = 1 };

    [TearDown]
    public void TearDown()
    {
        foreach (var suffix in ProfileRetirement.KeySuffixes)
            PlayerPrefs.DeleteKey(BotKey + suffix);
    }

    [Test]
    public void KeySuffixes_FollowThePerBotConvention()
    {
        CollectionAssert.AreEquivalent(
            new[] { "WhatsappRetiredProfileId", "WhatsappRetiredReason", "TelegramRetiredProfileId", "TelegramRetiredReason" },
            ProfileRetirement.KeySuffixes);
        Assert.AreEqual("TelegramRetiredProfileId", ProfileRetirement.ProfileIdSuffix(ChatChannel.Telegram));
        Assert.AreEqual("WhatsappRetiredReason", ProfileRetirement.ReasonSuffix(ChatChannel.WhatsApp));
    }

    [Test]
    public void Mark_RemembersTheIdAndTheReason_PerChannel()
    {
        string notified = null;
        System.Action<string> listener = key => notified = key;
        ProfileRetirement.Changed += listener;
        try
        {
            ProfileRetirement.Mark(BotKey, ChatChannel.Telegram, "tg-1", "churn_grace");
        }
        finally
        {
            ProfileRetirement.Changed -= listener;
        }
        Assert.AreEqual(BotKey, notified, "the bot's card repaints");
        Assert.AreEqual("tg-1", ProfileRetirement.MarkerId(BotKey, ChatChannel.Telegram));
        Assert.AreEqual("churn_grace", ProfileRetirement.Reason(BotKey, ChatChannel.Telegram));
        Assert.AreEqual("", ProfileRetirement.MarkerId(BotKey, ChatChannel.WhatsApp), "the other channel is untouched");
        Assert.AreEqual("churn_grace", ProfileRetirement.ActiveReason(BotKey, ChatChannel.Telegram, "tg-1", Token()));
        Assert.IsNull(ProfileRetirement.ActiveReason(BotKey, ChatChannel.Telegram, "tg-2", Token()), "a new connection: another id");
        Assert.IsNull(ProfileRetirement.ActiveReason(BotKey, ChatChannel.Telegram, "tg-1", Token("tg-1")), "the token wins");
        Assert.IsNull(ProfileRetirement.ActiveReason(BotKey, ChatChannel.WhatsApp, "wa-1", Token()));
    }

    [Test]
    public void Mark_WithoutAReason_StillCountsAsRetired()
    {
        ProfileRetirement.Mark(BotKey, ChatChannel.WhatsApp, "wa-1", null);   // a row this install does not own: 410, no reason
        Assert.AreEqual("", ProfileRetirement.ActiveReason(BotKey, ChatChannel.WhatsApp, "wa-1", null),
            "\"\" = retired, reason unknown («Канал отключён»); null would mean «not retired»");
    }

    [Test]
    public void Mark_IgnoresTheSentinelAndAMissingBot()
    {
        ProfileRetirement.Mark(BotKey, ChatChannel.WhatsApp, Bot.UnauthedProfileSentinel, "trial_expiry");
        ProfileRetirement.Mark("", ChatChannel.WhatsApp, "wa-1", "trial_expiry");
        Assert.IsFalse(PlayerPrefs.HasKey(BotKey + ProfileRetirement.WhatsappProfileIdSuffix));
        Assert.IsFalse(PlayerPrefs.HasKey(ProfileRetirement.WhatsappProfileIdSuffix), "no bot key → nothing written under a bare suffix");
    }

    [Test]
    public void Clear_RemovesAllFourKeys()
    {
        ProfileRetirement.Mark(BotKey, ChatChannel.WhatsApp, "wa-1", "trial_expiry");
        ProfileRetirement.Mark(BotKey, ChatChannel.Telegram, "tg-1", "app_delete");
        ProfileRetirement.Clear(BotKey);
        foreach (var suffix in ProfileRetirement.KeySuffixes)
            Assert.IsFalse(PlayerPrefs.HasKey(BotKey + suffix), $"{suffix} survived the bot's deletion");
    }
}
