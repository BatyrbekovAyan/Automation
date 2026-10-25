using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты»: a chat row the list hides (a personal chat outside a search, a chat moved to «Личные», a search
/// toggled, the chats panel itself) must still get its avatar once it is shown. Hiding a GameObject stops its coroutines for
/// good — the avatar download included — and a stopped coroutine never reaches the end of its `using`. So
/// ChatItemView.OnDisable aborts and disposes the cut-off request and owes the load again (OnEnable starts it), and the list
/// decides a new row's visibility BEFORE binding it, so a row born hidden never starts a download at all. EditMode can run
/// neither a coroutine nor a download: the wiring is pinned at the source, the «still owed» rule on its own.
/// </summary>
public class ChatRowAvatarTests
{
    private const string Url = "https://example.invalid/avatar.jpg";   // never fetched: AvatarLoadOwed is pure

    [Test]
    public void AnAvatarIsOwed_OnlyWhileItHasAUrlAndNoSprite()
    {
        Assert.IsTrue(ChatItemView.AvatarLoadOwed(new ChatViewModel("c1", "Title", Url, "", 0)));
        Assert.IsFalse(ChatItemView.AvatarLoadOwed(new ChatViewModel("c2", "Title", "", "", 0)), "no URL — nothing to load");
        Assert.IsFalse(ChatItemView.AvatarLoadOwed(new ChatViewModel("c3", "Title", null, "", 0)));
        Assert.IsFalse(ChatItemView.AvatarLoadOwed(null));

        var texture = new Texture2D(2, 2);
        try
        {
            var loaded = new ChatViewModel("c4", "Title", Url, "", 0)
            {
                AvatarSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero),
            };
            Assert.IsFalse(ChatItemView.AvatarLoadOwed(loaded), "already loaded — a hide owes nothing");
            Object.DestroyImmediate(loaded.AvatarSprite);
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void OnDisable_OwesACutOffDownloadAgain_AndLetsItsRequestGo()
    {
        string source = SourceGuard.Read("Scripts/UI/ChatItemView.cs");
        string onDisable = SourceGuard.MethodBody(source, "void OnDisable(");
        int inFlight = onDisable.IndexOf("if (avatarLoadCoroutine == null) return;", System.StringComparison.Ordinal);
        int dispose = onDisable.IndexOf("DisposeAvatarRequest();", System.StringComparison.Ordinal);
        int owed = onDisable.IndexOf("if (AvatarLoadOwed(vm)) pendingAvatarLoad = true;", System.StringComparison.Ordinal);
        Assert.IsTrue(inFlight >= 0 && inFlight < dispose && dispose < owed,
            "a download cut off by the hide is let go and owed again — OnEnable starts it:\n" + onDisable);

        string release = SourceGuard.MethodBody(source, "void DisposeAvatarRequest(");
        StringAssert.Contains("avatarRequest.Abort();", release);
        StringAssert.Contains("avatarRequest.Dispose();", release, "a stopped coroutine never runs its `using`");

        string onEnable = SourceGuard.MethodBody(source, "void OnEnable(");
        StringAssert.Contains("if (!AvatarLoadOwed(vm)) return;", onEnable);
        StringAssert.Contains("avatarLoadCoroutine = StartCoroutine(LoadAvatar(vm));", onEnable);
    }

    [Test]
    public void TheDownload_IsInFlightUntilAnswered_AndARebindReplacesIt()
    {
        string source = SourceGuard.Read("Scripts/UI/ChatItemView.cs");
        string load = SourceGuard.MethodBody(source, "IEnumerator LoadAvatar(");
        int track = load.IndexOf("avatarRequest = req;", System.StringComparison.Ordinal);
        int send = load.IndexOf("yield return req.SendWebRequest();", System.StringComparison.Ordinal);
        int done = load.IndexOf("avatarLoadCoroutine = null;", System.StringComparison.Ordinal);
        Assert.IsTrue(track >= 0 && track < send && send < done,
            "the request is tracked while it is out and the load stops being in flight once it is answered:\n" + load);

        StringAssert.Contains("CancelAvatarLoad();", SourceGuard.MethodBody(source, "public void Bind("),
            "a rebind lets the previous download go before it starts its own");
    }

    [Test]
    public void AddChat_DecidesVisibility_BeforeTheBind()
    {
        string addChat = SourceGuard.MethodBody(SourceGuard.Read("Scripts/UI/ChatListView.cs"), "void AddChat(");
        int match = addChat.IndexOf("ApplyMatchToItem(item, vm,", System.StringComparison.Ordinal);
        int bind = addChat.IndexOf("item.Bind(vm);", System.StringComparison.Ordinal);
        Assert.IsTrue(match >= 0 && match < bind,
            "a row hidden on arrival binds inactive, so its avatar waits for the row instead of a download cut off at once:\n" + addChat);
    }
}
