using NUnit.Framework;

/// <summary>
/// F2 (plan 2026-10-11 §5.1, task T9): the reconnect screens' copy. The texts are the plan's, word for word (the owner
/// reads them before the release); the glyphs are checked against the SF Pro atlases by FontGlyphCoverageTests.
/// </summary>
public class RetiredChannelCopyTests
{
    [Test]
    public void TheBanner_NamesWhatEnded()
    {
        StringAssert.StartsWith("Пробный период закончился, и канал отключили.", RetiredChannelCopy.BannerBody("trial_expiry"));
        StringAssert.StartsWith("Подписка закончилась, и канал отключили.", RetiredChannelCopy.BannerBody("churn_grace"));
        foreach (string other in new[] { "liveness", "channel_limit", "", null })
            Assert.AreEqual(RetiredChannelCopy.BannerBodyOther, RetiredChannelCopy.BannerBody(other), $"«{other ?? "null"}»");
        foreach (string reason in new[] { "trial_expiry", "churn_grace", "liveness" })
            StringAssert.EndsWith("Настройки, прайс-листы и переписка сохранены — после оплаты подключите канал снова.",
                RetiredChannelCopy.BannerBody(reason), "the promise F2 keeps: the data is moved to the new connection");
        Assert.AreEqual("Сотрудник отключён", RetiredChannelCopy.BannerTitle);
        Assert.AreEqual("Выбрать тариф", RetiredChannelCopy.BannerCta);
    }

    [TestCase("77071234567", "Ранее: +77071234567")]
    [TestCase("+7 707 123 45 67", "Ранее: +7 707 123 45 67")]
    [TestCase(" 77071234567 ", "Ранее: +77071234567")]
    [TestCase("@my_shop", "Ранее: @my_shop")]
    public void PreviousNumber_GetsItsPlus_OnlyWhenItIsBareDigits(string stored, string shown)
    {
        Assert.AreEqual(shown, RetiredChannelCopy.PreviousNumber(stored));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void PreviousNumber_NoneStored_ShowsNoLine(string stored)
    {
        Assert.IsNull(RetiredChannelCopy.PreviousNumber(stored));
    }

    [Test]
    public void TheSheetRow_NamesTheBotAndItsChannel()
    {
        Assert.AreEqual("Цветы · WhatsApp", RetiredChannelCopy.SheetRow("Цветы", ChatChannel.WhatsApp));
        Assert.AreEqual("Цветы · Telegram", RetiredChannelCopy.SheetRow("Цветы", ChatChannel.Telegram));
    }

    [Test]
    public void ThePlansTexts_WordForWord()
    {
        Assert.AreEqual("Отключён · нажмите, чтобы подключить снова", RetiredChannelCopy.RowHint);
        Assert.AreEqual("Подключить", RetiredChannelCopy.ConnectPill);
        Assert.AreEqual("Сотрудник отключён", RetiredChannelCopy.EmptyTitle);
        Assert.AreEqual("Подключите канал снова в настройках сотрудника", RetiredChannelCopy.EmptyBody);
        Assert.AreEqual("Подтверждаем оплату…", RetiredChannelCopy.Settling);
        Assert.AreEqual("Оплата ещё не дошла до сервера. Попробуйте через минуту.", RetiredChannelCopy.SettleTimedOut);
        Assert.AreEqual("Подключите сотрудников снова", RetiredChannelCopy.SheetTitle);
        Assert.AreEqual("Тариф активен. Настройки, прайс-листы и переписка на месте — осталось заново подключить WhatsApp или Telegram.",
            RetiredChannelCopy.SheetBody);
        Assert.AreEqual("Позже", RetiredChannelCopy.SheetLater);
    }
}
