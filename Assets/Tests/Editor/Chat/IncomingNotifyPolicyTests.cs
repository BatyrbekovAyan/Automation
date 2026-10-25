using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

// Contract for the sound/vibration cue on chat-list syncs: fires only for a
// genuinely new, incoming, unread message in a WORKING chat the user is NOT
// currently reading. Each guard is exercised by flipping exactly one input off
// the passing baseline.
public class IncomingNotifyPolicyTests
{
    private static bool Notify(
        bool isInitialLoad = false,
        bool lastIdChanged = true,
        bool lastMessageIsMine = false,
        int unreadCount = 2,
        string chatId = "111@c.us",
        string openChatId = "222@c.us",
        bool chatPanelVisible = false,
        bool chatIsPersonal = false)
        => IncomingNotifyPolicy.ShouldNotify(
            isInitialLoad, lastIdChanged, lastMessageIsMine, unreadCount, chatId, openChatId, chatPanelVisible, chatIsPersonal);

    [Test]
    public void Baseline_NewIncomingUnread_Notifies() => Assert.IsTrue(Notify());

    [Test]
    public void InitialCacheLoad_NeverNotifies() => Assert.IsFalse(Notify(isInitialLoad: true));

    [Test]
    public void UnchangedLastMessage_DoesNotNotify() => Assert.IsFalse(Notify(lastIdChanged: false));

    [Test]
    public void OwnOutgoingEcho_DoesNotNotify() => Assert.IsFalse(Notify(lastMessageIsMine: true));

    [Test]
    public void NoUnread_DoesNotNotify() => Assert.IsFalse(Notify(unreadCount: 0));

    [Test]
    public void OpenChatOnScreen_Suppressed()
    {
        Assert.IsFalse(Notify(chatId: "111@c.us", openChatId: "111@c.us", chatPanelVisible: true));
    }

    [Test]
    public void SameChatButPanelHidden_StillNotifies()
    {
        // currentChatId is never cleared on back-navigation, so the stale id
        // must not suppress cues while the user is on the chat list.
        Assert.IsTrue(Notify(chatId: "111@c.us", openChatId: "111@c.us", chatPanelVisible: false));
    }

    [Test]
    public void OtherChatWhilePanelVisible_Notifies()
    {
        Assert.IsTrue(Notify(chatId: "111@c.us", openChatId: "222@c.us", chatPanelVisible: true));
    }

    // «Личные и рабочие чаты» (spec §5.8): a personal chat never plays the cue — not even a genuinely new, incoming, unread
    // message in a chat nobody is reading — and the passing baseline proves that is the only thing that silenced it.
    [Test]
    public void Personal_NeverNotifies()
    {
        Assert.IsFalse(Notify(chatIsPersonal: true));
        Assert.IsFalse(Notify(chatIsPersonal: true, unreadCount: 40, chatPanelVisible: true));
        Assert.IsTrue(Notify(chatIsPersonal: false), "the same message in a working chat notifies");
    }

    // The policy is only as good as what ChatManager hands it: BOTH sites in ParseChatsJson (a merged row and a new one) must
    // pass the chat's audience as the list draws it — the store's view, never a literal. A merged row passes the verdict
    // before the message OR after it (the message itself moves an undecided chat's history estimate); a new row has no
    // «before». PersonalChatCueTests drives the merged path for real.
    [Test]
    public void ParseChatsJson_HandsEveryNotifySiteTheChatsAudience()
    {
        string source = SourceGuard.Read("Scripts/Main/ChatManager.cs");
        string parse = SourceGuard.MethodBody(source, "void ParseChatsJson(");
        List<string> calls = SourceGuard.Calls(parse, "IncomingNotifyPolicy.ShouldNotify(");
        Assert.AreEqual(2, calls.Count, "ParseChatsJson asks the policy for a merged row and for a new one");
        Assert.IsTrue(Regex.IsMatch(calls[0], @"chatIsPersonal:\s*personalBefore\s*\|\|\s*IsPersonalChat\("),
            $"the merged row passes the verdict before the message or after it:\n{calls[0]}");
        Assert.IsTrue(Regex.IsMatch(calls[1], @"chatIsPersonal:\s*IsPersonalChat\("),
            $"a new row passes its verdict:\n{calls[1]}");
        Assert.AreEqual(2, Regex.Matches(source, @"IncomingNotifyPolicy\.ShouldNotify\(").Count, "no other notify site in ChatManager");

        int before = parse.IndexOf("bool personalBefore = IsPersonalChat(", System.StringComparison.Ordinal);
        int update = parse.IndexOf("existingVm.UpdateLastMessage(", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(before, 0, "the merged row takes its verdict before the message");
        Assert.Less(before, update, "…BEFORE UpdateLastMessage moves the last message past the connection moment");

        string helper = SourceGuard.MethodBody(source, "bool IsPersonalChat(");
        StringAssert.Contains("ChatAudienceStore.ViewOf(", helper, "the verdict the list draws the chat by");
        StringAssert.Contains("AudienceKind.Personal", helper);
    }

    // An incoming message in a chat the app holds no verdict for asks for a list read soon (PersonalChatCueTests drives that
    // call site for real). The service makes a burst of such requests ONE read: one timer that later requests share, the
    // read forced, about ListSoonSeconds after the request; the other triggers keep their once-a-minute throttle.
    [Test]
    public void TheReadSoon_IsOneTimerForABurst_AndLeavesTheThrottleAlone()
    {
        string source = SourceGuard.Read("Scripts/Chat/ChatAudienceSyncService.cs");
        Assert.AreEqual(3f, ChatAudienceSyncService.ListSoonSeconds, "time for the server to decide the chat on the message");

        string schedule = SourceGuard.MethodBody(source, "void ScheduleListSoon(");
        StringAssert.Contains("if (_listSoon != null", schedule, "a request while a read is on its way shares it — not one timer per message");
        StringAssert.Contains("_listSoon = StartCoroutine(ListSoonRoutine())", schedule);

        string routine = SourceGuard.MethodBody(source, "IEnumerator ListSoonRoutine(");
        int wait = routine.IndexOf("WaitForSecondsRealtime(ListSoonSeconds)", System.StringComparison.Ordinal);
        int free = routine.IndexOf("_listSoon = null", System.StringComparison.Ordinal);
        int read = routine.IndexOf("ReadList(force: true)", System.StringComparison.Ordinal);
        Assert.IsTrue(wait >= 0 && wait < free && free < read,
            "wait, free the timer (a request after it starts the next read), then read forced:\n" + routine);

        StringAssert.Contains("ListFocusGapMs", SourceGuard.MethodBody(source, "void ReadList("), "an unforced read still waits out the minute");
    }
}
