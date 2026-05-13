using System.IO;
using NUnit.Framework;

public class OutboxStoreTests
{
    private string _tempRoot;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "OutboxStoreTests_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private OutboxStore MakeStore() => new OutboxStore(() => _tempRoot);

    private OutboxStore.OutboxEntry MakeEntry(string tempId = "t1", string chatId = "+15551@c.us")
        => new OutboxStore.OutboxEntry
        {
            tempId = tempId,
            chatId = chatId,
            text = "hi",
            timestamp = 12345,
            attemptCount = 1,
            profileId = "profileX"
        };

    [Test]
    public void GetFor_EmptyChat_ReturnsEmptyList()
    {
        var store = MakeStore();
        var entries = store.GetFor("+15551@c.us");
        Assert.IsNotNull(entries);
        Assert.AreEqual(0, entries.Count);
    }

    [Test]
    public void Add_ThenGetFor_ReturnsOneEntry()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);

        var entries = store.GetFor(entry.chatId);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("t1", entries[0].tempId);
        Assert.AreEqual("hi", entries[0].text);
    }

    [Test]
    public void Add_PersistsToDisk()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);

        // A second store instance against the same root must see the entry.
        var store2 = MakeStore();
        var entries = store2.GetFor(entry.chatId);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("t1", entries[0].tempId);
    }

    [Test]
    public void Remove_DeletesTheEntry()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);
        store.Remove("t1");

        Assert.AreEqual(0, store.GetFor(entry.chatId).Count);
    }

    [Test]
    public void Remove_PersistsToDisk()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);
        store.Remove("t1");

        // A second store on the same root must also see zero entries.
        var store2 = MakeStore();
        Assert.AreEqual(0, store2.GetFor(entry.chatId).Count);
    }

    [Test]
    public void Find_ReturnsMatchingEntry()
    {
        var store = MakeStore();
        store.Add(MakeEntry(tempId: "t1"));
        store.Add(MakeEntry(tempId: "t2"));

        var found = store.Find("t2");
        Assert.IsNotNull(found);
        Assert.AreEqual("t2", found.tempId);
    }

    [Test]
    public void Find_MissingTempId_ReturnsNull()
    {
        var store = MakeStore();
        store.Add(MakeEntry(tempId: "t1"));
        Assert.IsNull(store.Find("missing"));
    }

    [Test]
    public void Update_BumpsAttemptCount_AndPersists()
    {
        var store = MakeStore();
        var entry = MakeEntry();
        store.Add(entry);

        entry.attemptCount = 5;
        store.Update(entry);

        var store2 = MakeStore();
        var reloaded = store2.GetFor(entry.chatId)[0];
        Assert.AreEqual(5, reloaded.attemptCount);
    }

    [Test]
    public void Update_NonExistentTempId_DoesNotThrowOrCorruptList()
    {
        var store = MakeStore();
        store.Add(MakeEntry(tempId: "t1"));

        // Update an entry that doesn't exist — must not throw, must not corrupt the list.
        Assert.DoesNotThrow(() => store.Update(MakeEntry(tempId: "missing")));

        var entries = store.GetFor("+15551@c.us");
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("t1", entries[0].tempId);
    }

    [Test]
    public void CorruptedJsonFile_GetForReturnsEmpty()
    {
        // Use Add to create the on-disk file (avoids coupling to the internal sanitization scheme),
        // then overwrite it with garbage.
        var chatId = "+15551@c.us";
        var seedStore = MakeStore();
        seedStore.Add(MakeEntry(chatId: chatId));

        var files = Directory.GetFiles(_tempRoot, "outbox_*.json");
        Assert.AreEqual(1, files.Length);
        File.WriteAllText(files[0], "{ this is not valid json ");

        // A fresh store reading the corrupted file should degrade to empty.
        var freshStore = MakeStore();
        Assert.AreEqual(0, freshStore.GetFor(chatId).Count);
    }

    [Test]
    public void ChatIdWithSpecialChars_IsSanitizedForFilename()
    {
        // Group chat ids contain '@g.us' and a hyphen — the file system must accept them.
        var store = MakeStore();
        var entry = MakeEntry(chatId: "12345-67890@g.us");
        store.Add(entry);

        var entries = store.GetFor("12345-67890@g.us");
        Assert.AreEqual(1, entries.Count);
    }

    [Test]
    public void RemoveAt_TargetsSpecificCacheRoot_NotJustCurrentBot()
    {
        // Simulate the bot-switch-mid-retry scenario: store A's outbox file
        // exists, but the in-memory _byChatId would point at store B if we
        // built a store with a different getCacheRoot. RemoveAt should still
        // clear store A's file by path.
        var storeForBotA = MakeStore();
        storeForBotA.Add(MakeEntry(tempId: "tempA1"));

        // Build a "different bot" store that does not know about store A's entry.
        var differentRoot = Path.Combine(Path.GetTempPath(), "OutboxStoreTests_otherBot_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(differentRoot);
        var storeForBotB = new OutboxStore(() => differentRoot);

        // Calling RemoveAt on storeForBotB but pointing at storeForBotA's
        // cacheRoot should clear storeForBotA's file.
        storeForBotB.RemoveAt(_tempRoot, "+15551@c.us", "tempA1");

        // Verify the entry is gone from disk by loading via a fresh store on the
        // original root.
        var storeAReread = MakeStore();
        Assert.AreEqual(0, storeAReread.GetFor("+15551@c.us").Count);

        // Cleanup the second temp dir.
        if (Directory.Exists(differentRoot)) Directory.Delete(differentRoot, recursive: true);
    }

}
