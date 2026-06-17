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
}
