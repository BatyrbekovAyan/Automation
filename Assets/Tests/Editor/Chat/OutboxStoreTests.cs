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
    public void CorruptedJsonFile_GetForReturnsEmpty()
    {
        var chatId = "+15551@c.us";
        var path = Path.Combine(_tempRoot, "outbox_" + SanitizeForTest(chatId) + ".json");
        File.WriteAllText(path, "{ this is not valid json ");

        var store = MakeStore();
        Assert.AreEqual(0, store.GetFor(chatId).Count);
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

    // Mirrors the SanitizeChatId used inside the SUT — keep in sync.
    private static string SanitizeForTest(string chatId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(chatId.Length);
        foreach (var c in chatId)
            sb.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
