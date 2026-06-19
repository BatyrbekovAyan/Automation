using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

// Covers the cache-layer defense against Wappi's concurrent messages/get response crossing:
// a foreign chat's messages persisted into this chat's cache file (by an older session, before
// the network guard existed) must never load or re-save. The chat renders cache-first, so this
// is what actually clears the poison a user is still seeing after the network fix.
public class ChatHistoryCacheForeignStripTests
{
    private const string Own = "77472714618@c.us";        // a 1:1 chat
    private const string Foreign = "120363000000000001@g.us"; // a group chat

    private string dir;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "ChatHistoryCacheForeignStripTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private static MessageViewModel Msg(string id, string chatId) =>
        new MessageViewModel { messageId = id, chatId = chatId, type = MessageType.Chat, text = id };

    private void Save(params MessageViewModel[] messages) =>
        ChatHistoryCache.SaveHistory(dir, Own, new List<MessageViewModel>(messages));

    private List<MessageViewModel> Load() => ChatHistoryCache.LoadHistory(dir, Own);

    [Test]
    public void Load_DropsForeignChatMessages_KeepsOwn()
    {
        Save(Msg("a", Own), Msg("poison1", Foreign), Msg("b", Own), Msg("poison2", Foreign));

        var loaded = Load();

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, loaded.ConvertAll(m => m.messageId));
        Assert.IsFalse(loaded.Exists(m => m.chatId == Foreign), "foreign entries must not load");
    }

    [Test]
    public void Save_DoesNotPersistForeignMessages()
    {
        Save(Msg("a", Own), Msg("poison", Foreign));

        // Read the raw file directly — even bypassing LoadHistory's strip, the poison is gone.
        string raw = File.ReadAllText(Path.Combine(dir, "messages", $"{Own}.json"));
        StringAssert.DoesNotContain(Foreign, raw);
        StringAssert.Contains("\"a\"", raw);
    }

    [Test]
    public void Strip_KeepsEntriesWithEmptyOrMissingChatId()
    {
        // Legacy entries (written before chatId was reliably populated) must survive.
        Save(Msg("legacy", ""), Msg("nullid", null), Msg("own", Own), Msg("poison", Foreign));

        var loaded = Load();

        CollectionAssert.AreEquivalent(new[] { "legacy", "nullid", "own" }, loaded.ConvertAll(m => m.messageId));
    }

    [Test]
    public void Load_AllOwnChat_Unchanged()
    {
        Save(Msg("a", Own), Msg("b", Own), Msg("c", Own));
        var loaded = Load();
        CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, loaded.ConvertAll(m => m.messageId));
    }
}
