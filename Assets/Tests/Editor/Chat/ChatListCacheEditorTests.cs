using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class ChatListCacheEditorTests
{
    private static string TwoChatJson()
    {
        var resp = new ChatsResponse
        {
            status = "done",
            dialogs = new List<ChatDialog>
            {
                new ChatDialog { id = "a@c.us", name = "Alpha" },
                new ChatDialog { id = "b@c.us", name = "Bravo" },
            }
        };
        return JsonUtility.ToJson(resp);
    }

    [Test] public void RemovesTheNamedChat()
    {
        string outJson = ChatListCacheEditor.RemoveChat(TwoChatJson(), "a@c.us");
        var parsed = JsonUtility.FromJson<ChatsResponse>(outJson);
        Assert.AreEqual(1, parsed.dialogs.Count);
        Assert.AreEqual("b@c.us", parsed.dialogs[0].id);
    }

    [Test] public void UnknownChatLeavesJsonUnchanged()
    {
        string input = TwoChatJson();
        Assert.AreEqual(input, ChatListCacheEditor.RemoveChat(input, "zzz@c.us"));
    }

    [Test] public void NullOrEmptyInputsAreSafe()
    {
        Assert.IsNull(ChatListCacheEditor.RemoveChat(null, "a@c.us"));
        Assert.AreEqual("", ChatListCacheEditor.RemoveChat("", "a@c.us"));
        Assert.AreEqual("{}", ChatListCacheEditor.RemoveChat("{}", null));
    }

    [Test] public void GarbageJsonReturnedUnchanged()
        => Assert.AreEqual("not json", ChatListCacheEditor.RemoveChat("not json", "a@c.us"));
}
