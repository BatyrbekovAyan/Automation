using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using static SourceGuard;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.3, §5.4; sketch 009 screens 1, 3, 4): the «Личные» folder driven for real over a bare app
/// (PersonalChatsFolderFixture), its rows the real ChatItem.prefab.
///   • Browse — every personal chat, newest first, the ones that are not people included and saying what they are; a person's
///     row swipes «В рабочие» (and «Удалить» on WhatsApp) through the chat list's own RequestMove / RequestDelete — each row
///     subscribed once, however often the folder redraws; a tap opens the chat.
///   • «Изменить» — checkboxes instead of the swipe; «Сделать рабочими · N» moves the marked chats with a counted toast and
///     goes back; nothing marked says so. «Отмена» goes back without moving anything.
///   • «Кто из них клиенты?» — the loader while the channel's list is empty, the soft note when chats came and none is a
///     contact.
///   • The folder belongs to one bot and one channel: a switch to another closes it, a switch naming its own leaves it open.
/// In EditMode the queue sends nothing (no ChatAudienceSyncService) — a move is observed through the toast the chat list shows.
/// </summary>
public class PersonalChatsFolderViewTests
{
    private const string Bot = "PersonalChatsFolderViewTestBot";
    private const string Profile = "folder-wa-profile";
    private const string Saule = "77010000021@c.us";
    private const string Mama = "77010000022@c.us";
    private const string Family = "120363000000000021@g.us";
    private const string NewClient = "77010000023@c.us";
    private static readonly long MomentMs = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly long BeforeSec = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    private static readonly long AfterSec = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private readonly List<GameObject> hosts = new List<GameObject>();
    private string dir;
    private Manager previousManager;
    private ChatManager previousChatManager;

    [SetUp]
    public void SetUp()
    {
        AudienceToast.ResetForTests();
        PersonalChatsFolderView.ResetForTests();
        dir = Path.Combine(Path.GetTempPath(), "PersonalChatsFolderViewTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(dir, ChatAudienceStore.CacheFileName));
        ChatAudienceStore.ApplyConnected(Profile, MomentMs);
        previousManager = Manager.Instance;
        previousChatManager = ChatManager.Instance;
        PersonalChatsOnboarding.Clear(Bot, telegram: false);
    }

    [TearDown]
    public void TearDown()
    {
        PersonalChatsOnboarding.Clear(Bot, telegram: false);
        Manager.Instance = previousManager;
        ChatManager.Instance = previousChatManager;
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        PersonalChatsFolderView.ResetForTests();
        AudienceToast.ResetForTests();
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private FolderHarness Folder()
    {
        FolderHarness h = PersonalChatsFolderFixture.Build(Bot, Profile, hosts);
        h.AddChat(Mama, "Мама", BeforeSec + 10, contact: true);
        h.AddChat(Family, "Семья", BeforeSec + 30);
        h.AddChat(NewClient, "Новый клиент", AfterSec);
        h.AddChat(Saule, "Сауле", BeforeSec + 20);
        return h;
    }

    // ---- Browse ----

    [Test]
    public void Browse_ListsThePersonalChats_NewestFirst_AndWhatTheGroupIs()
    {
        FolderHarness h = Folder();
        PersonalChatsFolderView.OpenFromTheList();   // PersonalChatsRowView.Tapped

        Assert.IsTrue(h.Folder.IsOpen);
        Assert.AreEqual(FolderMode.Browse, h.Folder.Mode);
        CollectionAssert.AreEqual(new[] { Family, Saule, Mama }, h.Folder.ShownChatIdsForTests,
            "the personal chats newest first — the group included, the new client (working) not");
        Assert.AreEqual(PersonalChatsCopy.NonHumanGroup, h.Folder.RowForTests(Family).lastMessageText.text, "a group says what it is");
        Assert.AreEqual(PersonalChatsCopy.FolderTitle, h.Title.text);
        Assert.IsTrue(h.Edit.gameObject.activeSelf, "«Изменить»: there are people to mark");
        Assert.IsTrue(h.Footer.activeSelf, "the swipe hint");
        Assert.IsFalse(h.BottomBar.activeSelf, "no bottom button in the folder itself");
        Assert.IsTrue(h.Swipe.gameObject.activeSelf, "the page swipes back");
        Assert.IsTrue(h.BackChevron.activeSelf);
        Assert.IsFalse(h.BackLabel.activeSelf);

        ChatItemView saule = h.Folder.RowForTests(Saule);
        TextMeshProUGUI word = saule.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.transform.parent.name == PersonalChatsBuilder.MoveButtonName);
        Assert.AreEqual(PersonalChatsCopy.SwipeToBusiness, word.text, "a person's row swipes «В рабочие»");
        Assert.IsTrue(saule.swipeToDelete.enabled, "…and swipes at all");
    }

    [Test]
    public void TheRowsMoveAndDelete_GoToTheChatListsOwnRequests_EachRowSubscribedOnce()
    {
        FolderHarness h = Folder();
        h.Folder.Open(FolderMode.Browse);
        h.Folder.RefreshNowForTests();
        h.Folder.RefreshNowForTests();

        ChatItemView saule = h.Folder.RowForTests(Saule);
        foreach (string evt in new[] { "MoveRequested", "DeleteRequested", "SelectToggled", "ChatUpdated", "OrderChanged" })
            Assert.AreEqual(1, Handlers(saule, evt), $"{evt}: subscribed once per row, however often the folder redraws");

        Button(saule, PersonalChatsBuilder.MoveButtonName).onClick.Invoke();
        Assert.AreEqual(PersonalChatsCopy.MovedToBusiness, AudienceToast.LastText,
            "the swipe's «В рабочие» went through ChatListView.RequestMove — it moved a personal chat to work");

        string folder = Read("Scripts/UI/PersonalChatsFolderView.cs");
        StringAssert.Contains("chatList.RequestMove(", MethodBody(folder, "private void HandleRowMove("));
        StringAssert.Contains("chatList.RequestDelete(", MethodBody(folder, "private void HandleRowDelete("),
            "«Удалить» goes the chat list's way too — its confirm card");
    }

    [Test]
    public void AChatTurningWorking_LeavesTheFolder()
    {
        FolderHarness h = Folder();
        h.Folder.Open(FolderMode.Browse);
        ChatAudienceStore.ApplyDesire(Profile, Saule, AudienceKind.Business);
        h.Folder.RefreshNowForTests();
        CollectionAssert.AreEqual(new[] { Family, Mama }, h.Folder.ShownChatIdsForTests, "a chat made working is no longer personal");
        Assert.IsFalse(h.Folder.RowForTests(Saule).gameObject.activeSelf);
    }

    // ---- «Изменить» ----

    [Test]
    public void Edit_ShowsCheckboxes_NoSwipe_AndMovesTheMarkedChats()
    {
        FolderHarness h = Folder();
        h.Folder.Open(FolderMode.Browse);
        h.Edit.onClick.Invoke();

        Assert.AreEqual(FolderMode.Edit, h.Folder.Mode);
        CollectionAssert.AreEqual(new[] { Saule, Mama }, h.Folder.ShownChatIdsForTests, "only people can be marked");
        Assert.AreEqual(PersonalChatsCopy.EditTitle, h.Title.text);
        Assert.IsTrue(h.BackLabel.activeSelf, "«Отмена» on the left");
        Assert.IsFalse(h.BackChevron.activeSelf);
        Assert.IsFalse(h.Edit.gameObject.activeSelf);
        Assert.IsTrue(h.BottomBar.activeSelf);
        Assert.IsFalse(h.Secondary.gameObject.activeSelf, "no «Отмечу позже» here");
        Assert.IsFalse(h.Swipe.gameObject.activeSelf, "a selection list does not swipe back");
        Assert.AreEqual("Сделать рабочими · 0", h.PrimaryLabel.text);

        ChatItemView saule = h.Folder.RowForTests(Saule);
        Assert.IsTrue(Check(saule).activeSelf, "a checkbox…");
        Assert.IsFalse(Checked(saule).activeSelf, "…unmarked");
        Assert.IsFalse(saule.swipeToDelete.enabled, "rows in a selection list do not swipe");

        saule.button.onClick.Invoke();
        h.Folder.RowForTests(Mama).button.onClick.Invoke();
        Assert.IsTrue(Checked(saule).activeSelf, "a tap marks");
        Assert.AreEqual("Сделать рабочими · 2", h.PrimaryLabel.text);
        h.Folder.RowForTests(Mama).button.onClick.Invoke();
        Assert.AreEqual("Сделать рабочими · 1", h.PrimaryLabel.text, "a second tap unmarks");
        h.Folder.RowForTests(Mama).button.onClick.Invoke();

        h.Primary.onClick.Invoke();
        Assert.AreEqual("2 чата стали рабочими", AudienceToast.LastText, "two chats moved — the toast counts them");
        Assert.AreEqual(FolderMode.Browse, h.Folder.Mode, "back to the folder");
        Assert.IsTrue(h.Folder.IsOpen);
        Assert.IsFalse(Check(saule).activeSelf, "the checkboxes go");
        Assert.IsTrue(saule.swipeToDelete.enabled, "…and the swipe comes back");
    }

    [Test]
    public void Edit_WithNothingMarked_SaysSo_AndCancelMovesNothing()
    {
        FolderHarness h = Folder();
        h.Folder.Open(FolderMode.Edit);
        h.Primary.onClick.Invoke();
        Assert.AreEqual(PersonalChatsCopy.SelectAtLeastOne, AudienceToast.LastText, "«Отметьте хотя бы один чат»");
        Assert.AreEqual(FolderMode.Edit, h.Folder.Mode, "still selecting");

        AudienceToast.ResetForTests();
        h.Folder.RowForTests(Saule).button.onClick.Invoke();
        h.Back.onClick.Invoke();   // «Отмена»
        Assert.AreEqual(FolderMode.Browse, h.Folder.Mode, "«Отмена» goes back to the folder");
        Assert.IsTrue(h.Folder.IsOpen, "…the folder stays");
        Assert.IsNull(AudienceToast.LastText, "nothing moved");
        h.Edit.onClick.Invoke();
        Assert.AreEqual("Сделать рабочими · 0", h.PrimaryLabel.text, "the marks did not survive the cancel");
    }

    // ---- «Кто из них клиенты?» ----

    [Test]
    public void Onboarding_LoadsWhileTheListIsEmpty_NotesWhenNoChatIsAContact()
    {
        FolderHarness h = PersonalChatsFolderFixture.Build(Bot, Profile, hosts);
        h.Folder.Open(FolderMode.Onboarding);

        Assert.AreEqual(PersonalChatsCopy.OnboardingTitle, h.Title.text);
        Assert.IsTrue(h.StatusLine.activeSelf, "«WhatsApp подключён»");
        Assert.IsTrue(h.SectionHeader.activeSelf, "«НЕДАВНИЕ ЧАТЫ»");
        Assert.IsTrue(h.Secondary.gameObject.activeSelf, "«Отмечу позже»");
        Assert.IsFalse(h.Back.gameObject.activeSelf, "no way back but its own two buttons");
        Assert.IsTrue(h.LoadingLabel.gameObject.activeSelf, "the channel's list is empty: loading");
        Assert.AreEqual(PersonalChatsCopy.OnboardingLoading(telegram: false), h.LoadingLabel.text);
        Assert.IsFalse(h.NoContactsNote.activeSelf, "nothing has arrived — no conclusion yet");

        h.AddChat(NewClient, "Новый клиент", AfterSec);
        h.Folder.RefreshNowForTests();
        Assert.IsFalse(h.LoadingLabel.gameObject.activeSelf, "a chat arrived — the list is loaded, even with no personal row in it");
        Assert.IsTrue(h.NoContactsNote.activeSelf, "…and not one chat is a contact");

        h.AddChat(Saule, "Сауле", BeforeSec);
        h.Folder.RefreshNowForTests();
        Assert.AreEqual(PersonalChatsCopy.SublineHistory, h.Folder.RowForTests(Saule).lastMessageText.text, "«не в контактах, писал раньше»");
        Assert.IsTrue(h.NoContactsNote.activeSelf, "a conversation from before is no contact");

        h.AddChat(Mama, "Мама", BeforeSec, contact: true);
        h.Folder.RefreshNowForTests();
        Assert.AreEqual(PersonalChatsCopy.SublineContact, h.Folder.RowForTests(Mama).lastMessageText.text, "«в контактах»");
        Assert.IsFalse(h.NoContactsNote.activeSelf, "a contact — the phone sees the address book");

        string folder = Read("Scripts/UI/PersonalChatsFolderView.cs");
        Assert.AreEqual(10f, PersonalChatsFolderView.OnboardingPollSeconds, "the list is asked again every 10 s while the screen is up");
        StringAssert.Contains("RefreshActiveBotChats()", MethodBody(folder, "private IEnumerator PollChats("));
    }

    // ---- one bot, one channel ----

    [Test]
    public void ASwitchToItsOwnBotOrChannel_KeepsItOpen_AnotherClosesIt()
    {
        FolderHarness h = Folder();
        h.Folder.Open(FolderMode.Browse);

        Raise(h.Chats, "OnActiveBotChanged", Bot);
        Assert.IsTrue(h.Folder.IsOpen, "a switch announcing the folder's own bot (the settings open it right after switching)");
        Raise(h.Chats, "OnActiveChannelChanged", ChatChannel.WhatsApp);
        Assert.IsTrue(h.Folder.IsOpen, "…or its own channel leaves it open");

        Raise(h.Chats, "OnActiveChannelChanged", ChatChannel.Telegram);
        Assert.IsFalse(h.Folder.IsOpen, "another channel closes it");

        h.Folder.Open(FolderMode.Browse);
        Raise(h.Chats, "OnActiveBotChanged", "SomeOtherBot");
        Assert.IsFalse(h.Folder.IsOpen, "another bot closes it");
    }

    [Test]
    public void TheSwipeBack_ClosesTheFolder()
    {
        FolderHarness h = Folder();
        h.Folder.Open(FolderMode.Browse);
        Assert.IsNotNull(h.Swipe.OnCommitted, "the strip's commit is the folder's");
        h.Swipe.OnCommitted();
        Assert.IsFalse(h.Folder.IsOpen, "the page slid off: closed");
        Assert.IsFalse(PersonalChatsFolderView.IsShowing);
    }

    // ---- helpers ----

    private static GameObject Check(ChatItemView row) => (GameObject)new UnityEditor.SerializedObject(row).FindProperty("selectCheck").objectReferenceValue;

    private static GameObject Checked(ChatItemView row) => (GameObject)new UnityEditor.SerializedObject(row).FindProperty("selectChecked").objectReferenceValue;

    private static UnityEngine.UI.Button Button(ChatItemView row, string name) =>
        row.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(b => b.name == name);

    // A field-like event's handlers — the compiler's private backing field of the same name.
    private static int Handlers(object target, string evt)
    {
        FieldInfo field = target.GetType().GetField(evt, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"{target.GetType().Name}.{evt} is not a field-like event any more");
        return (field.GetValue(target) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    // Raises a ChatManager event the way ChatManager does — through its backing delegate.
    private static void Raise<T>(ChatManager chats, string evt, T value)
    {
        FieldInfo field = typeof(ChatManager).GetField(evt, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"ChatManager.{evt} moved");
        (field.GetValue(chats) as Action<T>)?.Invoke(value);
    }
}
