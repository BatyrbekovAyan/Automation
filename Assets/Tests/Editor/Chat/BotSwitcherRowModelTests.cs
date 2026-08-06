using NUnit.Framework;

// Covers BotSwitcherRowModel — the pure decisions of one Sheet_BotSwitcher row
// (compact А2): auto-chip visibility (connected bots only) and the RU-plural
// subline «N чатов[ · M новых]» / «Не подключён» / «Нет чатов».
public class BotSwitcherRowModelTests
{
    // --- chip visibility --------------------------------------------------

    [Test]
    public void AutoChipVisible_AnyConnectedChannel()
    {
        Assert.IsTrue(BotSwitcherRowModel.AutoChipVisible(waConnected: true, tgConnected: false));
        Assert.IsTrue(BotSwitcherRowModel.AutoChipVisible(waConnected: false, tgConnected: true));
        Assert.IsTrue(BotSwitcherRowModel.AutoChipVisible(waConnected: true, tgConnected: true));
    }

    [Test]
    public void AutoChipVisible_HiddenWhenUnconnected()
    {
        Assert.IsFalse(BotSwitcherRowModel.AutoChipVisible(waConnected: false, tgConnected: false),
            "An unconnected bot cannot reply — the autopilot chip would be a lie");
    }

    // --- subline ----------------------------------------------------------

    [Test]
    public void Subline_Unconnected_IgnoresCounts()
    {
        Assert.AreEqual("Не подключён", BotSwitcherRowModel.Subline(false, 12, 3));
    }

    [Test]
    public void Subline_ConnectedNoChats()
    {
        Assert.AreEqual("Нет чатов", BotSwitcherRowModel.Subline(true, 0, 0));
    }

    [Test]
    public void Subline_ChatsOnly_NoUnread()
    {
        Assert.AreEqual("3 чата", BotSwitcherRowModel.Subline(true, 3, 0));
    }

    [Test]
    public void Subline_ChatsAndUnread()
    {
        Assert.AreEqual("6 чатов · 4 новых", BotSwitcherRowModel.Subline(true, 6, 4));
        Assert.AreEqual("21 чат · 1 новый", BotSwitcherRowModel.Subline(true, 21, 1));
    }

    // --- RU plurals -------------------------------------------------------

    [Test]
    public void RuPlural_ChatForms()
    {
        Assert.AreEqual("чат", BotSwitcherRowModel.RuPlural(1, "чат", "чата", "чатов"));
        Assert.AreEqual("чата", BotSwitcherRowModel.RuPlural(2, "чат", "чата", "чатов"));
        Assert.AreEqual("чата", BotSwitcherRowModel.RuPlural(4, "чат", "чата", "чатов"));
        Assert.AreEqual("чатов", BotSwitcherRowModel.RuPlural(5, "чат", "чата", "чатов"));
        Assert.AreEqual("чатов", BotSwitcherRowModel.RuPlural(11, "чат", "чата", "чатов"));
        Assert.AreEqual("чатов", BotSwitcherRowModel.RuPlural(14, "чат", "чата", "чатов"));
        Assert.AreEqual("чат", BotSwitcherRowModel.RuPlural(21, "чат", "чата", "чатов"));
        Assert.AreEqual("чата", BotSwitcherRowModel.RuPlural(22, "чат", "чата", "чатов"));
        Assert.AreEqual("чатов", BotSwitcherRowModel.RuPlural(111, "чат", "чата", "чатов"));
    }
}
