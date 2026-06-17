using NUnit.Framework;

public class ChatViewModelReactionTests
{
    [Test]
    public void SetReactionPreview_RefreshesRow_WithoutReordering()
    {
        var vm = new ChatViewModel("c1", "Title", "", "old", 100);
        bool updated = false, reordered = false;
        vm.OnUpdated += _ => updated = true;
        vm.OnLastMessageChanged += _ => reordered = true;

        vm.SetReactionPreview("❤️", true, "Hello", "chat");

        Assert.IsTrue(updated, "should refresh the row");
        Assert.IsFalse(reordered, "must not reorder the chat list");
        Assert.AreEqual("❤️", vm.LastMessage);
        Assert.AreEqual("reaction", vm.LastMessageType);
        Assert.IsTrue(vm.IsLastMessageMine);
        Assert.AreEqual("Hello", vm.ReactionTargetText);
        Assert.AreEqual("chat", vm.ReactionTargetType);
    }

    [Test]
    public void UpdateReactionContext_NoChange_FiresNoEvent()
    {
        var vm = new ChatViewModel("c1", "Title", "", "old", 100);
        int updates = 0;
        vm.OnUpdated += _ => updates++;

        vm.UpdateReactionContext(null, null); // already null

        Assert.AreEqual(0, updates);
    }

    [Test]
    public void UpdateReactionContext_Clears_PreviouslySetText()
    {
        var vm = new ChatViewModel("c1", "Title", "", "old", 100);
        vm.SetReactionPreview("❤️", true, "Hello", "chat");

        vm.UpdateReactionContext(null, null);

        Assert.IsNull(vm.ReactionTargetText);
        Assert.IsNull(vm.ReactionTargetType);
    }

    [Test]
    public void IsGroup_DerivedFromChatIdSuffix()
    {
        Assert.IsTrue(new ChatViewModel("123456789@g.us", "G", "", "x", 1).IsGroup);
        Assert.IsFalse(new ChatViewModel("123456789@c.us", "C", "", "x", 1).IsGroup);
        Assert.IsFalse(new ChatViewModel("", "C", "", "x", 1).IsGroup);
    }

    [Test]
    public void Constructor_PersistsSenderName()
    {
        var vm = new ChatViewModel("123456789@g.us", "G", "", "x", 1, lastMessageSenderName: "Aliya");
        Assert.AreEqual("Aliya", vm.LastMessageSenderName);
    }

    [Test]
    public void SetLastMessageSenderName_Sets()
    {
        var vm = new ChatViewModel("123456789@g.us", "G", "", "x", 1);
        vm.SetLastMessageSenderName("Aliya");
        Assert.AreEqual("Aliya", vm.LastMessageSenderName);
    }

    [Test]
    public void ApplyResolvedRowDetails_IgnoresEmptyName_ThenSetsNonEmpty()
    {
        var vm = new ChatViewModel("123456789@g.us", "G", "", "x", 1, lastMessageSenderName: "Bumer");
        vm.ApplyResolvedRowDetails("", "", "");          // resolver found nothing → keep existing
        Assert.AreEqual("Bumer", vm.LastMessageSenderName);
        vm.ApplyResolvedRowDetails("", "", "Alibek");    // resolver found a name → adopt it
        Assert.AreEqual("Alibek", vm.LastMessageSenderName);
    }

    [Test]
    public void ResolvedSenderName_DrivesGroupRowPrefix()
    {
        // LID group participant: chat-list pushname arrives empty → no author shown, until the
        // backfill resolves it from the messages endpoint and it must drive the row prefix.
        var vm = new ChatViewModel("123456789@g.us", "G", "", "Hello", 100,
                                   lastMessageType: "chat", lastMessageSenderName: "");
        Assert.IsTrue(vm.IsGroup);
        Assert.AreEqual("Hello", FormatRow(vm), "empty author → bare body, no dangling prefix");

        vm.ApplyResolvedRowDetails("", "", "Alibek"); // backfill resolves the author

        Assert.AreEqual("Alibek: Hello", FormatRow(vm), "backfilled name must drive the row prefix");
    }

    private static string FormatRow(ChatViewModel vm) =>
        ChatPreviewFormatter.Format(vm.LastMessage, vm.LastMessageType, vm.LastMessageDeliveryStatus,
            vm.IsLastMessageMine, vm.ReactionTargetText, vm.ReactionTargetType,
            vm.LastMessageSenderName, vm.IsGroup);

    [Test]
    public void SetReactionPreview_CarriesReactorName()
    {
        var vm = new ChatViewModel("123456789@g.us", "G", "", "x", 1);
        vm.SetReactionPreview("❤️", false, "Hi", "chat", "Aliya");
        Assert.AreEqual("Aliya", vm.LastMessageSenderName);
    }
}
