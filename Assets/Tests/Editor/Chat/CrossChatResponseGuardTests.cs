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

    // --- The chat's @lid code («Личные и рабочие чаты»: the live check found WhatsApp chats the list names by their
    // --- number while every messages/get row carries the chat's code) -------------------------------------------

    private const string Number = "77001112233@c.us";
    private const string ChatCode = "100000000000123@lid";
    private const string OtherCode = "100000000000999@lid";
    private const string TelegramChat = "5550001112";

    private static ChatIdentity Whatsapp(string code) => ChatIdentity.For(ChatChannel.WhatsApp, Number, code);

    [Test]
    public void IsForDifferentChat_EveryRowUnderTheChatsCode_IsItsPage()
    {
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(Window(ChatCode, 50), Whatsapp(ChatCode)),
            "the chat's own page: its history, its live messages, the panel's input");
    }

    [Test]
    public void IsForDifferentChat_RowsUnderTheNumberAndUnderTheCode_IsItsPage()
    {
        var mixed = new List<RawMessage> { Msg(ChatCode, "a"), Msg(Number, "b"), Msg(null, "c"), Msg(ChatCode, "d") };
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(mixed, Whatsapp(ChatCode)));
    }

    [Test]
    public void IsForDifferentChat_APageUnderAnotherCode_IsStillForeign()
    {
        Assert.IsTrue(CrossChatResponseGuard.IsForDifferentChat(Window(OtherCode, 50), Whatsapp(ChatCode)),
            "another code names another chat — a crossed answer");
        Assert.IsTrue(CrossChatResponseGuard.IsForDifferentChat(Window("77009998877@c.us", 50), Whatsapp(ChatCode)),
            "so does another number");
        var untagged = new List<RawMessage> { Msg(OtherCode, "a"), Msg(null, "b"), Msg("", "c") };
        Assert.IsTrue(CrossChatResponseGuard.IsForDifferentChat(untagged, Whatsapp(ChatCode)),
            "rows without a chatId are ignored, as before");
    }

    [Test]
    public void IsForDifferentChat_ACodeThatDoesNotCount_ChangesNothing()
    {
        // A code without @lid, an empty one, one equal to the chat id: the chat is known by its id alone — exactly as before.
        foreach (string notACode in new[] { null, "", "100000000000123", "@lid", "100000000000123@LID", "77009998877@c.us", Number })
        {
            ChatIdentity chat = Whatsapp(notACode);
            Assert.IsTrue(CrossChatResponseGuard.IsForDifferentChat(Window(ChatCode, 5), chat),
                $"code '{notACode}': a page under an @lid code the list never named is foreign");
            Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(Window(Number, 5), chat),
                $"code '{notACode}': the page under the chat's id is its page");
            if (!string.IsNullOrEmpty(notACode) && notACode != Number)
                Assert.IsTrue(CrossChatResponseGuard.IsForDifferentChat(Window(notACode, 5), chat),
                    $"'{notACode}' is no code: a page under it is foreign");
        }
    }

    [Test]
    public void IsForDifferentChat_Telegram_IsUnchanged()
    {
        ChatIdentity chat = ChatIdentity.For(ChatChannel.Telegram, TelegramChat, ChatCode);
        Assert.IsTrue(CrossChatResponseGuard.IsForDifferentChat(Window(ChatCode, 5), chat), "Telegram has no code");
        Assert.IsFalse(CrossChatResponseGuard.IsForDifferentChat(Window(TelegramChat, 5), chat));
    }

    [Test]
    public void IsForDifferentChat_AnIdentityWithoutACode_AnswersAsTheIdAlone()
    {
        // The identity overload is the id overload whenever no code counts — byte for byte on every page shape above.
        var pages = new List<List<RawMessage>>
        {
            Window(ChatA, 5), Window(ChatB, 5), Window(ChatCode, 5), new List<RawMessage> { Msg(null), Msg("") },
            new List<RawMessage> { Msg(ChatB, "a"), Msg(null, "b") }, new List<RawMessage>(), null,
        };
        foreach (string requested in new[] { ChatA, ChatB, "", null })
            foreach (List<RawMessage> page in pages)
                Assert.AreEqual(CrossChatResponseGuard.IsForDifferentChat(page, requested),
                    CrossChatResponseGuard.IsForDifferentChat(page, ChatIdentity.For(ChatChannel.WhatsApp, requested, null)),
                    $"requested '{requested}', page of {page?.Count.ToString() ?? "null"} rows");
    }
}
