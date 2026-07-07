using NUnit.Framework;

public class SupportMessageComposerTests
{
    [Test]
    public void FullMessage_WithContact_AppendsContactAndMeta()
    {
        string result = SupportMessageComposer.Compose(
            "Бот не отвечает", "+7 700 000 00 00", "1.0", "Android", "samsung SM-G991B");

        Assert.AreEqual(
            "Бот не отвечает\nКонтакт: +7 700 000 00 00\n— Automation v1.0 · Android · samsung SM-G991B",
            result);
    }

    [Test]
    public void EmptyContact_OmitsContactLine()
    {
        string result = SupportMessageComposer.Compose("Вопрос", "", "1.0", "IPhonePlayer", "iPhone14,5");
        Assert.AreEqual("Вопрос\n— Automation v1.0 · IPhonePlayer · iPhone14,5", result);
    }

    [Test]
    public void WhitespaceContact_OmitsContactLine()
    {
        string result = SupportMessageComposer.Compose("Вопрос", "   ", "1.0", "Android", "Pixel");
        Assert.IsFalse(result.Contains("Контакт"));
    }

    [Test]
    public void NullContact_OmitsContactLine()
    {
        string result = SupportMessageComposer.Compose("Вопрос", null, "1.0", "Android", "Pixel");
        Assert.IsFalse(result.Contains("Контакт"));
    }

    [Test]
    public void MessageIsTrimmed()
    {
        string result = SupportMessageComposer.Compose("  Вопрос  ", null, "1.0", "Android", "Pixel");
        Assert.IsTrue(result.StartsWith("Вопрос\n"));
    }

    [Test]
    public void EmptyMessage_ReturnsEmpty() => Assert.AreEqual("", SupportMessageComposer.Compose("", "x", "1.0", "a", "b"));

    [Test]
    public void WhitespaceMessage_ReturnsEmpty() => Assert.AreEqual("", SupportMessageComposer.Compose("   ", "x", "1.0", "a", "b"));

    [Test]
    public void NullMessage_ReturnsEmpty() => Assert.AreEqual("", SupportMessageComposer.Compose(null, "x", "1.0", "a", "b"));
}
