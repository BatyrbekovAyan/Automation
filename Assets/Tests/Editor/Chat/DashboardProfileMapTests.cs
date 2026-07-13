using System.Collections.Generic;
using NUnit.Framework;

public class DashboardProfileMapTests
{
    // Factory (named Bp, not Bot, to avoid shadowing the Bot MonoBehaviour type).
    private static BotProfiles Bp(string name, string wa, string tg) =>
        new BotProfiles { botName = name, whatsappProfileId = wa, telegramProfileId = tg };

    [Test] public void AuthedProfiles_CollectsBothChannels()
    {
        var bots = new[] { Bp("Bot0", "wa1", "tg1") };
        CollectionAssert.AreEqual(new[] { "wa1", "tg1" }, DashboardProfileMap.AuthedProfiles(bots));
    }

    [Test] public void AuthedProfiles_SkipsSentinelAndEmpty()
    {
        var bots = new[] { Bp("Bot0", Bot.UnauthedProfileSentinel, "") };
        Assert.IsEmpty(DashboardProfileMap.AuthedProfiles(bots));
        Assert.IsFalse(DashboardProfileMap.ProfileToBot(bots).ContainsKey(Bot.UnauthedProfileSentinel));
        Assert.IsEmpty(DashboardProfileMap.BotChips(bots));
    }

    [Test] public void BotChips_DualChannelBotProducesOneChipCoveringBothProfiles()
    {
        var chips = DashboardProfileMap.BotChips(new[] { Bp("Bot0", "wa1", "tg1") });
        Assert.AreEqual(1, chips.Count);
        Assert.AreEqual("Bot0", chips[0].botName);
        Assert.IsTrue(chips[0].profileIds.SetEquals(new[] { "wa1", "tg1" }));
    }

    [Test] public void ProfileToBot_ResolvesChannelPerMatchedId()
    {
        var map = DashboardProfileMap.ProfileToBot(new[] { Bp("Bot0", "wa1", "tg1") });
        Assert.AreEqual("Bot0", map["wa1"].botName);
        Assert.AreEqual(ChatChannel.WhatsApp, map["wa1"].channel);
        Assert.AreEqual("Bot0", map["tg1"].botName);
        Assert.AreEqual(ChatChannel.Telegram, map["tg1"].channel);
    }

    [Test] public void TelegramOnlyBot_CollectsMapsAndResolvesTelegram()
    {
        var bots = new[] { Bp("Bot0", Bot.UnauthedProfileSentinel, "tg1") };
        CollectionAssert.AreEqual(new[] { "tg1" }, DashboardProfileMap.AuthedProfiles(bots));

        var chips = DashboardProfileMap.BotChips(bots);
        Assert.AreEqual(1, chips.Count);
        Assert.IsTrue(chips[0].profileIds.SetEquals(new[] { "tg1" }));

        var map = DashboardProfileMap.ProfileToBot(bots);
        Assert.IsTrue(DashboardProfileMap.TryResolve(map, "tg1", out var botName, out var channel));
        Assert.AreEqual("Bot0", botName);
        Assert.AreEqual(ChatChannel.Telegram, channel);
    }

    [Test] public void TryResolve_MissOrNullReturnsFalse()
    {
        var map = DashboardProfileMap.ProfileToBot(new[] { Bp("Bot0", "wa1", Bot.UnauthedProfileSentinel) });
        Assert.IsFalse(DashboardProfileMap.TryResolve(map, "forged", out _, out _));
        Assert.IsFalse(DashboardProfileMap.TryResolve(map, null, out _, out _));
        Assert.IsFalse(DashboardProfileMap.TryResolve(null, "wa1", out _, out _));
    }
}
