using System.Collections.Generic;
using NUnit.Framework;

public class CrossChatResponseGuardTests
{
    private const string ChatA = "120363000000000001@g.us";
    private const string ChatB = "77472714618@c.us";

    private static RawMessage Msg(string chatId, string id = "m") => new RawMessage { id = id, chatId = chatId };

    private static List<RawMessage> Window(string chatId, int count)
    {
        var list = new List<RawMessage>();
        for (int i = 0; i < count; i++) list.Add(Msg(chatId, "m" + i));
        return list;
    }

    // --- The crossing case: every message names a foreign chat -----------------

    [Test]
    public void IsForDifferentChat_AllMessagesForeign_ReturnsTrue()
    {
        var crossed = Window(ChatB, 50);
        Assert.IsTrue(CrossChatResponseGuard.IsForDifferentChat(crossed, ChatA));
    }

    [Test]
    public void IsForDifferentChat_AllMessagesMatch_ReturnsFalse()
    {
        var clean = Window(ChatA, 50);
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(clean, ChatA));
    }

    // --- Conservative: keep the page if a single message confirms the chat -----

    [Test]
    public void IsForDifferentChat_OneAnomalousForeignMessage_KeepsPage()
    {
        var mostlyClean = Window(ChatA, 49);
        mostlyClean.Add(Msg(ChatB, "weird")); // a lone foreign-tagged entry must not drop the page
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(mostlyClean, ChatA));
    }

    // --- Never discard on missing data -----------------------------------------

    [Test]
    public void IsForDifferentChat_AllChatIdsEmpty_ReturnsFalse()
    {
        var noIds = new List<RawMessage> { Msg(null), Msg(""), Msg(null) };
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(noIds, ChatA));
    }

    [Test]
    public void IsForDifferentChat_ForeignMessagesButSomeUntagged_ReturnsTrue()
    {
        // A crossed window where a couple of entries lack a chatId — the tagged ones are all
        // foreign and none match, so it is still a crossed response.
        var crossed = new List<RawMessage> { Msg(ChatB, "a"), Msg(null, "b"), Msg(ChatB, "c") };
        Assert.IsTrue(CrossChatResponseGuard.IsForDifferentChat(crossed, ChatA));
    }

    // --- Edge cases: empty / null inputs are not "crossed" ----------------------

    [Test]
    public void IsForDifferentChat_EmptyOrNullInputs_ReturnFalse()
    {
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(null, ChatA));
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(new List<RawMessage>(), ChatA));
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(Window(ChatB, 3), null));
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(Window(ChatB, 3), ""));
    }
}
