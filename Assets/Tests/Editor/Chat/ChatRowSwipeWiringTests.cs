using System;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Pins how a chat row's swipe follows its chat (spec §5.2, §5.3; a personal chat found by the working list's search swipes
/// as in the «Личные» folder). ChatItemView applies <see cref="ChatRowSwipePolicy"/> on the bind and applies it again — never
/// snapping an open row shut unless the actions changed — whenever its place changes (ChatListView.ApplyMatchToItem, on every
/// visibility pass: the store's OnChanged and every row update) or its facts do (the row's OnUpdated); its move button hands
/// the chat to whoever made the row. The list's side, which EditMode cannot run (it needs ChatManager), is a source guard.
///
/// The row is built in code with an empty title and preview, so no text is measured and nothing starts a tween.
/// </summary>
public class ChatRowSwipeWiringTests
{
    private const string PersonId = "77010000000@c.us";

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyWhatTheTestBuilt()
    {
        foreach (Object created in _created)
            if (created != null) Object.DestroyImmediate(created);
        _created.Clear();
    }

    // ---- the row ----

    [Test]
    public void TheRow_FollowsItsPlace_WorkingToPersonalAndBack()
    {
        Row row = BuildRow();
        ChatViewModel chat = Chat(Person());
        row.View.SetSwipeContext(false);   // ChatListView sets the place before the bind (AddChat)
        row.View.Bind(chat);
        AssertSwipe(row, true, true, PersonalChatsCopy.SwipeToPersonal, "a working WhatsApp chat");

        row.View.SetSwipeContext(true);    // the chat turned personal while the search keeps it on the screen
        AssertSwipe(row, true, false, PersonalChatsCopy.SwipeToBusiness, "a personal chat swipes as in the folder");
        ChatViewModel moved = null;
        row.View.MoveRequested += c => moved = c;
        row.Move.onClick.Invoke();
        Assert.AreSame(chat, moved, "the move button still answers after the actions changed");

        row.View.SetSwipeContext(false);
        AssertSwipe(row, true, true, PersonalChatsCopy.SwipeToPersonal, "working again");
    }

    [Test]
    public void ABindWithoutAPlace_IsTheWorkingList()
    {
        Row row = BuildRow();
        row.View.Bind(Chat(Person()));
        AssertSwipe(row, true, true, PersonalChatsCopy.SwipeToPersonal, "the default place");
    }

    [Test]
    public void ANonPerson_GetsNoSwipe_AndTheRowFollowsItsFacts()
    {
        Row row = BuildRow();
        ChatViewModel chat = Chat(Person());
        row.View.SetSwipeContext(true);
        row.View.Bind(chat);
        Assert.IsTrue(row.Swipe.enabled, "a person: «В рабочие»");

        chat.UpdateFacts(Person(bot: true));   // a fresh chat-list row says it is a bot — the row's OnUpdated
        Assert.IsFalse(row.Swipe.enabled, "nothing to reveal — no swipe at all");
        Assert.IsFalse(row.Move.gameObject.activeSelf, "no move for a non-person");
        Assert.IsFalse(row.Delete.gameObject.activeSelf, "and no «Удалить» in the folder's place");
        ChatViewModel moved = null;
        row.View.MoveRequested += c => moved = c;
        row.Move.onClick.Invoke();
        Assert.IsNull(moved, "the hidden move button answers nothing");
    }

    [Test]
    public void TheSamePlace_LeavesAnOpenRowOpen()
    {
        Row row = BuildRow();
        row.View.SetSwipeContext(false);
        row.View.Bind(Chat(Person()));
        float open = -row.Swipe.RevealWidth;
        Assert.Less(open, 0f, "the row has something to reveal");
        row.Content.anchoredPosition = new Vector2(open, 0f);   // the owner swiped it open

        row.View.SetSwipeContext(false);   // a visibility pass that changed nothing for this row
        Assert.AreEqual(open, row.Content.anchoredPosition.x, "a refresh never snaps an open row shut");

        row.View.SetSwipeContext(true);    // the actions change
        Assert.AreEqual(0f, row.Content.anchoredPosition.x, "new actions snap it shut first");
    }

    [Test]
    public void TheMoveButton_HandsTheChatToWhoeverMadeTheRow()
    {
        Row row = BuildRow();
        ChatViewModel chat = Chat(Person());
        row.View.Bind(chat);
        ChatViewModel moved = null;
        row.View.MoveRequested += c => moved = c;

        row.Move.onClick.Invoke();

        Assert.AreSame(chat, moved);
    }

    // ---- the list (source guards: ChatListView needs ChatManager) ----

    [Test]
    public void TheList_GivesEveryRowItsPlace_OnEveryVisibilityPass()
    {
        string list = SourceGuard.Read("Scripts/UI/ChatListView.cs");
        StringAssert.Contains("item.SetSwipeContext(ChatRowSwipePolicy.InFolder(view));",
            SourceGuard.MethodBody(list, "private void ApplyMatchToItem("),
            "a row's place is its chat's kind now — a personal chat found by the search swipes as in the folder");
        StringAssert.Contains("ApplyMatchToItem(item, item.Vm, view, searchActive);", SourceGuard.MethodBody(list, "private void ApplyVisibility("),
            "every visibility pass re-reads every row's place");
        StringAssert.Contains("ChatAudienceStore.OnChanged += RequestVisibilityRefresh;", SourceGuard.MethodBody(list, "void Start("),
            "a decision or a move starts a pass");

        string row = SourceGuard.Read("Scripts/UI/ChatItemView.cs");
        string updated = SourceGuard.MethodBody(row, "void OnVmUpdated(");
        StringAssert.Contains("parentList.RequestVisibilityRefresh();", updated, "a row update starts a pass");
        StringAssert.Contains("ApplySwipeActions();", updated, "and re-reads the row's own facts (a non-person never moves)");
    }

    [Test]
    public void TheList_TakesTheRowsMove_AndMovesByTheChatsKind()
    {
        string list = SourceGuard.Read("Scripts/UI/ChatListView.cs");
        StringAssert.Contains("item.MoveRequested += RequestMove;", SourceGuard.MethodBody(list, "void AddChat("));

        string move = SourceGuard.MethodBody(list, "public void RequestMove(");
        StringAssert.Contains("if (!view.CanMove) return;", move, "a chat that is not a person never moves");
        int place = move.IndexOf("if (ChatRowSwipePolicy.InFolder(view))", StringComparison.Ordinal);
        int business = move.IndexOf("ChatAudienceSyncService.MakeBusiness(", StringComparison.Ordinal);
        int businessToast = move.IndexOf("AudienceToast.Show(PersonalChatsCopy.MovedToBusiness);", StringComparison.Ordinal);
        int personal = move.IndexOf("ChatAudienceSyncService.MakePersonal(", StringComparison.Ordinal);
        int personalToast = move.IndexOf("AudienceToast.Show(PersonalChatsCopy.MovedToPersonal);", StringComparison.Ordinal);
        Assert.IsTrue(place >= 0 && place < business && business < businessToast && businessToast < personal && personal < personalToast,
            "the verdict that chose the button's word decides the move: personal → working with «Чат стал рабочим», working → «Личные» with «Чат перенесён в «Личные»»:\n" + move);
    }

    // ---- helpers ----

    private sealed class Row
    {
        public ChatItemView View;
        public SwipeToDelete Swipe;
        public RectTransform Content;
        public Button Move;
        public Button Delete;
        public TextMeshProUGUI MoveLabel;
    }

    // ChatItem's shape and the references ChatItemView.Bind reads: the two buttons behind (pinned to the right edge), the
    // sliding content in front with the tap button, the name, the preview and the avatars.
    private Row BuildRow()
    {
        var root = new GameObject("ChatItem", typeof(RectTransform));
        _created.Add(root);
        var view = root.AddComponent<ChatItemView>();

        Button delete = ActionButton(root.transform, "DeleteButton");
        Button move = ActionButton(root.transform, "MoveButton");
        var label = Child(move.transform, "Label").gameObject.AddComponent<TextMeshProUGUI>();

        RectTransform content = Child(root.transform, "SwipeContent");
        content.gameObject.AddComponent<Image>();
        view.button = content.gameObject.AddComponent<Button>();
        var swipe = content.gameObject.AddComponent<SwipeToDelete>();
        view.titleText = Child(content, "Name").gameObject.AddComponent<TextMeshProUGUI>();
        view.lastMessageText = Child(content, "Message").gameObject.AddComponent<TextMeshProUGUI>();
        view.avatarImage = Child(content, "Avatar").gameObject.AddComponent<Image>();
        view.defaultAvatar = Child(content, "Default").gameObject.AddComponent<Image>();
        view.deleteButton = delete;
        view.swipeToDelete = swipe;

        var rowRefs = new SerializedObject(view);
        rowRefs.FindProperty("moveButton").objectReferenceValue = move;
        rowRefs.FindProperty("moveLabel").objectReferenceValue = label;
        rowRefs.ApplyModifiedPropertiesWithoutUndo();
        var swipeRefs = new SerializedObject(swipe);
        swipeRefs.FindProperty("deleteButton").objectReferenceValue = delete.transform;
        swipeRefs.FindProperty("moveButton").objectReferenceValue = move.transform;
        swipeRefs.ApplyModifiedPropertiesWithoutUndo();

        return new Row { View = view, Swipe = swipe, Content = content, Move = move, Delete = delete, MoveLabel = label };
    }

    private static Button ActionButton(Transform parent, string name)
    {
        RectTransform rect = Child(parent, name);
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(200f, 0f);
        rect.gameObject.AddComponent<Image>();
        return rect.gameObject.AddComponent<Button>();
    }

    private static RectTransform Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static AudienceFacts Person(bool bot = false) => new AudienceFacts(false, PersonId, null, bot, false, false, false);

    private static ChatViewModel Chat(AudienceFacts facts) => new ChatViewModel(PersonId, "", "", "", 0, facts: facts);

    private static void AssertSwipe(Row row, bool move, bool delete, string word, string what)
    {
        Assert.AreEqual(move, row.Move.gameObject.activeSelf, $"{what}: the move button");
        Assert.AreEqual(delete, row.Delete.gameObject.activeSelf, $"{what}: «Удалить»");
        Assert.AreEqual(word, row.MoveLabel.text, $"{what}: the move button's word");
        Assert.AreEqual(move || delete, row.Swipe.enabled, $"{what}: the swipe");
    }
}
