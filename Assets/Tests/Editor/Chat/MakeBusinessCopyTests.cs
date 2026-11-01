using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.5, §4.5): the «Сделать чат рабочим?» sheet says what the assistant will do once the chat
/// is working. With «Авто» on and the client's message waiting it answers that message — but only while it is fresh
/// (<see cref="ChatAudienceRule.FreshHours"/>), so the small note under the text explains that an older one is skipped — and
/// only when the server hands that message on at all: it replays the types of its REPLAY_TYPES
/// (Tools/n8n/build-chat-audience.py, compared here through the project root), never a voice message or a reaction. With
/// «Авто» on and the owner's message last it simply answers what comes next; with the bot's channel switched off in its
/// settings nobody answers by itself; with «Авто» off it only suggests.
/// </summary>
public class MakeBusinessCopyTests
{
    private static readonly string ChatAudienceBuilderPath =
        Path.Combine(Application.dataPath, "..", "Tools", "n8n", "build-chat-audience.py");

    [Test]
    public void For_AutoFresh()
    {
        (string body, string note) = MakeBusinessCopy.For(autoOn: true, channelOn: true, lastIsClient: true, lastReplayable: true, lastAgeHours: 2);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoFresh, body, "the waiting message is answered right away");
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessStaleNote, note, "…and the note says why an older one would not be");
    }

    [Test]
    public void For_AutoStaleHasTheNote()
    {
        (string body, string note) = MakeBusinessCopy.For(autoOn: true, channelOn: true, lastIsClient: true, lastReplayable: true, lastAgeHours: 30);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh, body, "a message older than a day is not answered — new ones are");
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessStaleNote, note, "the note explains the waiting message is skipped");
    }

    [Test]
    public void For_AutoOwnerLastNoNote()
    {
        (string body, string note) = MakeBusinessCopy.For(autoOn: true, channelOn: true, lastIsClient: false, lastReplayable: true, lastAgeHours: 1);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh, body, "nobody waits for an answer — the assistant takes the next message");
        Assert.IsNull(note, "nothing is skipped, so there is nothing to explain");
    }

    [TestCase(1.0)]
    [TestCase(30.0)]
    public void For_AWaitingMessageTheServerNeverHandsOn_PromisesNoAnswerToIt(double age)
    {
        (string body, string note) = MakeBusinessCopy.For(autoOn: true, channelOn: true, lastIsClient: true, lastReplayable: false, lastAgeHours: age);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh, body,
            "a voice message or a reaction is never handed to the assistant again — it answers the new messages");
        Assert.IsNull(note, "its age is not why it goes unanswered — no note about the age");
    }

    [Test]
    public void For_AChannelSwitchedOff_PromisesNoAutomaticAnswer()
    {
        foreach (bool lastIsClient in new[] { true, false })
        foreach (bool replayable in new[] { true, false })
        foreach (double age in new[] { 0.5, 30.0 })
        {
            (string body, string note) = MakeBusinessCopy.For(autoOn: true, channelOn: false, lastIsClient, replayable, age);
            Assert.AreEqual(PersonalChatsCopy.MakeBusinessChannelOff, body,
                "the bot's settings switched the channel off: nobody answers there by itself, whatever the chat's mode");
            Assert.IsNull(note, "nothing is answered or skipped");
        }
    }

    [Test]
    public void For_Manual()
    {
        foreach (bool channelOn in new[] { true, false })
        foreach (bool lastIsClient in new[] { true, false })
        foreach (double age in new[] { 0.5, 30.0 })
        {
            (string body, string note) = MakeBusinessCopy.For(autoOn: false, channelOn, lastIsClient, lastReplayable: true, age);
            Assert.AreEqual(PersonalChatsCopy.MakeBusinessManual, body,
                "«Авто» is off: the assistant suggests, the owner sends — the panel does not depend on the channel switch");
            Assert.IsNull(note, "the owner answers himself — no message is answered or skipped");
        }
    }

    [Test]
    public void For_TheLineBetweenFreshAndStale_IsFreshHours()
    {
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoFresh,
            MakeBusinessCopy.For(true, true, true, true, ChatAudienceRule.FreshHours).Body, "a message exactly a day old is still answered");
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh,
            MakeBusinessCopy.For(true, true, true, true, ChatAudienceRule.FreshHours + 0.01).Body, "a moment older is not");
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh,
            MakeBusinessCopy.For(true, true, true, true, double.NaN).Body, "an age nobody could compute is never fresh");
    }

    [Test]
    public void AgeHours_FromTheLastMessage_AndAnUnknownTimeIsNeverFresh()
    {
        const long now = 1_800_000_000;
        Assert.AreEqual(2.0, MakeBusinessCopy.AgeHours(now - 2 * 3600, now), 1e-9);
        Assert.AreEqual(0.5, MakeBusinessCopy.AgeHours(now - 1800, now), 1e-9);
        Assert.AreEqual(double.PositiveInfinity, MakeBusinessCopy.AgeHours(0, now), "the list gave no time for the last message");
        Assert.AreEqual(double.PositiveInfinity, MakeBusinessCopy.AgeHours(-5, now));
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessAutoNoFresh,
            MakeBusinessCopy.For(true, true, true, true, MakeBusinessCopy.AgeHours(0, now)).Body, "no time → no promise of an answer");
    }

    // ── the types the server hands on ─────────────────────────────────────────────────────────────────────

    [Test]
    public void ReplayTypes_AreTheServersList()
    {
        string module = File.ReadAllText(ChatAudienceBuilderPath);
        Match line = Regex.Match(module, @"^REPLAY_TYPES[ \t]*=[ \t]*\(([^)\r\n]*)\)", RegexOptions.Multiline);
        Assert.IsTrue(line.Success, "REPLAY_TYPES is gone from build-chat-audience.py — the app can no longer be compared with it");
        string[] server = Regex.Matches(line.Groups[1].Value, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        Assert.IsNotEmpty(server, "REPLAY_TYPES is no longer a tuple of string literals");
        CollectionAssert.AreEquivalent(server, MakeBusinessCopy.ReplayTypes,
            "the app promises an answer to exactly the types the server hands to the assistant again");
        CollectionAssert.AllItemsAreUnique(MakeBusinessCopy.ReplayTypes);
    }

    private static ChatViewModel Row(bool mine, string type, long time) =>
        new ChatViewModel("77000000901@c.us", "Сауле", null, "…", time, lastMessageType: type, isLastMessageMine: mine);

    private static MessageViewModel Live(bool incoming, MessageType type, long time) =>
        new MessageViewModel { messageId = "m" + time, type = type, isIncoming = incoming, timestamp = time };

    private static (bool, bool, long) Facts(MakeBusinessCopy.LastMessageFacts f) => (f.IsClient, f.Replayable, f.UnixSec);

    [Test]
    public void LastMessage_IsTheNewerOfTheRowAndTheOpenChat()
    {
        // The chat list's row follows the list's syncs only; the open chat's live list also holds the app's own sends (put in
        // at once) and what its live poll found — the owner's phone messages, the client's replies. The newer of the two wins.
        const long now = 1_800_000_000;
        Assert.AreEqual((false, true, now - 60),
            Facts(MakeBusinessCopy.LastMessage(Row(false, "chat", now - 600), Live(false, MessageType.Chat, now - 60))),
            "the owner wrote in the open chat after the client — the row still says the client");
        Assert.AreEqual((true, true, now - 30),
            Facts(MakeBusinessCopy.LastMessage(Row(true, "chat", now - 3600), Live(true, MessageType.Image, now - 30))),
            "the client wrote while the chat was open — the row still says the owner");
        Assert.AreEqual((true, false, now - 30),
            Facts(MakeBusinessCopy.LastMessage(Row(true, "chat", now - 3600), Live(true, MessageType.Voice, now - 30))),
            "a voice message the open chat found is never handed on");
        Assert.AreEqual((true, false, now - 600),
            Facts(MakeBusinessCopy.LastMessage(Row(false, "reaction", now - 600), Live(true, MessageType.Chat, now - 900))),
            "the row is newer (a reaction the list saw after the open chat's newest message): the row");
        Assert.AreEqual((false, true, now - 600),
            Facts(MakeBusinessCopy.LastMessage(Row(true, "chat", now - 600), Live(false, MessageType.Chat, now - 600))),
            "the same second: the open chat's, which saw it last");
        Assert.AreEqual((true, true, now - 600), Facts(MakeBusinessCopy.LastMessage(Row(false, "chat", now - 600), null)),
            "no open chat: the row");
    }

    [Test]
    public void IsReplayable_TheOpenChatsMessage_ByItsType()
    {
        foreach (MessageType type in new[] { MessageType.Chat, MessageType.Image, MessageType.Video, MessageType.Audio, MessageType.Sticker,
                                             MessageType.Document })
            Assert.IsTrue(MakeBusinessCopy.IsReplayable(type), $"{type} is handed to the assistant again");
        foreach (MessageType type in new[] { MessageType.Voice, MessageType.Reaction, MessageType.Unknown })
            Assert.IsFalse(MakeBusinessCopy.IsReplayable(type), $"{type} is not");
    }

    [Test]
    public void IsReplayable_ReadsTheChatListsWord_ForTheLastMessage()
    {
        // The chat list names a message by the words of a messages/get row for every type the server replays: WhatsApp text
        // «chat», Telegram text «text», the media by their own names.
        foreach (string word in MakeBusinessCopy.ReplayTypes)
            Assert.IsTrue(MakeBusinessCopy.IsReplayable(word), $"«{word}» is handed to the assistant again");

        // A voice message — «ptt» in a messages/get row on both channels, and the chat-list preview reads «voice» as one too —
        // is never handed on: the bot transcribes the file of the incoming webhook, which a messages/get row does not carry.
        foreach (string voice in new[] { "ptt", "voice" })
            Assert.IsFalse(MakeBusinessCopy.IsReplayable(voice), $"«{voice}»: a voice message is never handed on");
        foreach (string reaction in new[] { "reaction", "reaction_remove" })
            Assert.IsFalse(MakeBusinessCopy.IsReplayable(reaction), $"«{reaction}»: a reaction is no client waiting for an answer");
        foreach (string other in new[] { "poll", "poll_vote", "incoming_call", "missed_call", "call_terminate", "buttons", "list", "system" })
            Assert.IsFalse(MakeBusinessCopy.IsReplayable(other), $"«{other}» is not handed on");

        // A word the server's list does not name promises nothing — the preview's other names («photo», «contact») included —
        // and every word is matched exactly, like the server's own comparison.
        foreach (string unknown in new[] { "photo", "contact", "", null, "Chat", "IMAGE", " chat", "text " })
            Assert.IsFalse(MakeBusinessCopy.IsReplayable(unknown), $"«{unknown ?? "null"}» promises no answer");
    }
}
