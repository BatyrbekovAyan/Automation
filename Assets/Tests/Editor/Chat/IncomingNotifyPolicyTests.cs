using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

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
    // pass the chat's audience as the list draws it — the store's view, never a literal.
    [Test]
    public void ParseChatsJson_HandsEveryNotifySiteTheChatsAudience()
    {
        string source = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/ChatManager.cs"));
        string parse = MethodBody(source, "void ParseChatsJson(");
        List<string> calls = Calls(parse, "IncomingNotifyPolicy.ShouldNotify(");
        Assert.AreEqual(2, calls.Count, "ParseChatsJson asks the policy for a merged row and for a new one");
        foreach (string call in calls)
            Assert.IsTrue(Regex.IsMatch(call, @"chatIsPersonal:\s*IsPersonalChat\("),
                $"a notify site must pass the chat's audience (chatIsPersonal: IsPersonalChat(...)):\n{call}");
        Assert.AreEqual(2, Regex.Matches(source, @"IncomingNotifyPolicy\.ShouldNotify\(").Count, "no other notify site in ChatManager");

        string helper = MethodBody(source, "bool IsPersonalChat(");
        StringAssert.Contains("ChatAudienceStore.ViewOf(", helper, "the verdict the list draws the chat by");
        StringAssert.Contains("AudienceKind.Personal", helper);
    }

    // ---- source helpers ----

    // The body of the method whose signature starts with marker, braces matched.
    private static string MethodBody(string source, string marker)
    {
        int at = source.IndexOf(marker, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(at, 0, $"«{marker}» not found in ChatManager.cs");
        int open = source.IndexOf('{', at);
        int arrow = source.IndexOf("=>", at, System.StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
        {
            int end = source.IndexOf(';', arrow);
            return source.Substring(arrow, end - arrow + 1);
        }
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"«{marker}»: unbalanced braces");
        return null;
    }

    // Every call that starts with marker, its parentheses matched.
    private static List<string> Calls(string body, string marker)
    {
        var calls = new List<string>();
        int from = 0;
        while ((from = body.IndexOf(marker, from, System.StringComparison.Ordinal)) >= 0)
        {
            int depth = 0;
            int end = from + marker.Length - 1;
            for (; end < body.Length; end++)
            {
                if (body[end] == '(') depth++;
                else if (body[end] == ')' && --depth == 0) break;
            }
            calls.Add(body.Substring(from, end - from + 1));
            from = end;
        }
        return calls;
    }
}
