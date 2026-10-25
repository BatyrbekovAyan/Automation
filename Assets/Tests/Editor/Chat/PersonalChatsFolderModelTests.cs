using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.3, §5.4; sketch 009 screens 1, 3, 4): the «Личные» folder's rules, pure.
///   • Browse lists every personal chat — the ones that are not people included — newest first; Edit and «Кто из них
///     клиенты?» list only the chats that can be made working. A working chat is never in the folder.
///   • The search keeps a chat whose name matches, or whose number does when the query is a number.
///   • The line under the name: a non-person says what it is; in the onboarding a person says why it is personal; otherwise
///     the chat's own last message (null).
///   • The bottom button counts the marks; the onboarding's soft note shows only once chats have arrived and none is a contact.
///   • A confirmed selection moves only what the folder lists and the server takes; the toast counts the chats it moved.
///   • A bot or channel switch takes the folder away only when it names another bot or channel than the folder's own.
/// </summary>
public class PersonalChatsFolderModelTests
{
    private const string Saule = "77010000001@c.us";
    private const string Mama = "77010000002@c.us";
    private const string Dauren = "77010000003@c.us";
    private const string Family = "120363000000000001@g.us";
    private const string Client = "77010000004@c.us";

    // ---- the rows ----

    [Test]
    public void Rows_Browse_IncludesNonHumans_EditOmitsThem()
    {
        var chats = new List<(ChatViewModel, AudienceView)>
        {
            (Chat(Saule, "Сауле", 500), Personal(AudienceReason.Contact)),
            (Chat(Family, "Семья", 400), Personal(AudienceReason.NonHuman, NonHumanKind.Group)),
            (Chat(Client, "Клиент", 300), Working()),
            (Chat(Dauren, "Даурен", 200), Personal(AudienceReason.History)),
        };

        CollectionAssert.AreEqual(new[] { Saule, Family, Dauren }, Ids(PersonalChatsFolderModel.Rows(chats, FolderMode.Browse, "")),
            "the folder: every personal chat, the group included; the working chat is not there");
        foreach (FolderMode mode in new[] { FolderMode.Edit, FolderMode.Onboarding })
            CollectionAssert.AreEqual(new[] { Saule, Dauren }, Ids(PersonalChatsFolderModel.Rows(chats, mode, "")),
                $"{mode}: only the chats that can be made working");
    }

    [Test]
    public void Rows_NewestFirst_AndTheSelectionModesCarryCheckboxes()
    {
        var chats = new List<(ChatViewModel, AudienceView)>
        {
            (Chat(Mama, "Мама", 100), Personal(AudienceReason.Contact)),
            (Chat(Saule, "Сауле", 900), Personal(AudienceReason.Contact)),
            (Chat(Dauren, "Даурен", 500), Personal(AudienceReason.History)),
        };

        IReadOnlyList<FolderRow> browse = PersonalChatsFolderModel.Rows(chats, FolderMode.Browse, null);
        CollectionAssert.AreEqual(new[] { Saule, Dauren, Mama }, Ids(browse), "new on top");
        Assert.IsTrue(browse.All(row => !row.Selectable), "the folder itself has no checkboxes");
        Assert.IsTrue(PersonalChatsFolderModel.Rows(chats, FolderMode.Edit, "").All(row => row.Selectable), "«Изменить»: every row selects");
        Assert.IsTrue(PersonalChatsFolderModel.Rows(chats, FolderMode.Onboarding, "").All(row => row.Selectable), "the onboarding too");
        Assert.AreSame(chats[1].Item1, browse[0].Chat);
        Assert.AreSame(chats[1].Item2, browse[0].View);
    }

    [Test]
    public void Rows_AChatWithoutAView_IsPersonal_ButNeverSelectable()
    {
        var chats = new List<(ChatViewModel, AudienceView)> { (Chat(Saule, "Сауле", 1), null) };
        Assert.AreEqual(1, PersonalChatsFolderModel.Rows(chats, FolderMode.Browse, "").Count, "what is not known is personal");
        Assert.AreEqual(0, PersonalChatsFolderModel.Rows(chats, FolderMode.Edit, "").Count, "…and is never moved");
        Assert.AreEqual(0, PersonalChatsFolderModel.Rows(null, FolderMode.Browse, "").Count);
    }

    [Test]
    public void Rows_Query_FiltersByTitleAndNumber()
    {
        var chats = new List<(ChatViewModel, AudienceView)>
        {
            (Chat(Saule, "Сауле Клиент", 3), Personal(AudienceReason.Contact)),
            (Chat("77015552011@c.us", "", 2), Personal(AudienceReason.History)),
            (Chat(Mama, "Мама", 1), Personal(AudienceReason.Contact)),
        };

        CollectionAssert.AreEqual(new[] { Saule }, Ids(PersonalChatsFolderModel.Rows(chats, FolderMode.Edit, "сауле")),
            "the name, whatever the case");
        CollectionAssert.AreEqual(new[] { Saule }, Ids(PersonalChatsFolderModel.Rows(chats, FolderMode.Edit, "  клиент ")),
            "…anywhere in it, the query trimmed");
        CollectionAssert.AreEqual(new[] { "77015552011@c.us" }, Ids(PersonalChatsFolderModel.Rows(chats, FolderMode.Edit, "+7 701 555-20-11")),
            "a number written any way finds the chat's phone number");
        CollectionAssert.AreEqual(new[] { "77015552011@c.us" }, Ids(PersonalChatsFolderModel.Rows(chats, FolderMode.Edit, "5552")),
            "…part of it too");
        Assert.AreEqual(0, PersonalChatsFolderModel.Rows(chats, FolderMode.Edit, "Мама 2").Count,
            "a query that is not a number never matches by digits");
        Assert.AreEqual(3, PersonalChatsFolderModel.Rows(chats, FolderMode.Edit, " ").Count, "an empty query keeps everything");
        Assert.IsFalse(PersonalChatsFolderModel.Matches(Chat(Family, "Семья", 1), "120363"),
            "a group id is no phone number");
    }

    // ---- the line under the name ----

    [Test]
    public void Subline_NonHumanAndOnboardingMarks()
    {
        Assert.AreEqual(PersonalChatsCopy.NonHumanGroup,
            PersonalChatsFolderModel.Subline(Personal(AudienceReason.NonHuman, NonHumanKind.Group), FolderMode.Browse),
            "«Группа · ИИ-сотрудник в группах не отвечает»");
        Assert.AreEqual(PersonalChatsCopy.NonHumanBot,
            PersonalChatsFolderModel.Subline(Personal(AudienceReason.NonHuman, NonHumanKind.Bot), FolderMode.Browse));
        Assert.AreEqual(PersonalChatsCopy.SublineContact,
            PersonalChatsFolderModel.Subline(Personal(AudienceReason.Contact), FolderMode.Onboarding), "«в контактах»");
        Assert.AreEqual(PersonalChatsCopy.SublineHistory,
            PersonalChatsFolderModel.Subline(Personal(AudienceReason.History), FolderMode.Onboarding), "«не в контактах, писал раньше»");

        Assert.IsNull(PersonalChatsFolderModel.Subline(Personal(AudienceReason.Contact), FolderMode.Browse),
            "the folder itself shows the chat's last message");
        Assert.IsNull(PersonalChatsFolderModel.Subline(Personal(AudienceReason.History), FolderMode.Edit), "«Изменить» too");
        Assert.IsNull(PersonalChatsFolderModel.Subline(Personal(AudienceReason.Owner), FolderMode.Onboarding),
            "a chat the owner made personal has no reason to name — its last message");
        Assert.IsNull(PersonalChatsFolderModel.Subline(Personal(AudienceReason.Unknown), FolderMode.Onboarding));
        Assert.IsNull(PersonalChatsFolderModel.Subline(null, FolderMode.Onboarding));

        IReadOnlyList<FolderRow> rows = PersonalChatsFolderModel.Rows(
            new List<(ChatViewModel, AudienceView)> { (Chat(Saule, "Сауле", 1), Personal(AudienceReason.Contact)) }, FolderMode.Onboarding, "");
        Assert.AreEqual(PersonalChatsCopy.SublineContact, rows[0].Subline, "the rows carry their line");
    }

    // ---- the bottom button, the soft note ----

    [Test]
    public void PrimaryLabel_UsesTheCount()
    {
        Assert.AreEqual("Сделать рабочими · 2", PersonalChatsFolderModel.PrimaryLabel(FolderMode.Edit, 2));
        Assert.AreEqual("Сделать рабочими · 0", PersonalChatsFolderModel.PrimaryLabel(FolderMode.Edit, 0));
        Assert.AreEqual("Готово · клиентов: 0", PersonalChatsFolderModel.PrimaryLabel(FolderMode.Onboarding, 0));
        Assert.AreEqual("Готово · клиентов: 3", PersonalChatsFolderModel.PrimaryLabel(FolderMode.Onboarding, 3));
        Assert.IsNull(PersonalChatsFolderModel.PrimaryLabel(FolderMode.Browse, 2), "the folder itself has no bottom button");
    }

    [Test]
    public void NoContactsNote_OnlyWhenLoadedAndNoContact()
    {
        var history = new List<FolderRow> { Row(Dauren, AudienceReason.History), Row(Mama, AudienceReason.History) };
        Assert.IsTrue(PersonalChatsFolderModel.ShowNoContactsNote(history, loaded: true),
            "chats arrived and not one is a contact — the phone's messenger, it seems, does not see the address book");
        Assert.IsTrue(PersonalChatsFolderModel.ShowNoContactsNote(new List<FolderRow>(), loaded: true),
            "a list with chats but no personal rows says it too");
        Assert.IsFalse(PersonalChatsFolderModel.ShowNoContactsNote(history, loaded: false), "nothing has arrived yet — the loader speaks");
        Assert.IsFalse(PersonalChatsFolderModel.ShowNoContactsNote(null, loaded: false));

        history.Add(Row(Saule, AudienceReason.Contact));
        Assert.IsFalse(PersonalChatsFolderModel.ShowNoContactsNote(history, loaded: true), "one contact is enough");

        var ownerMarked = new List<FolderRow>
        {
            new FolderRow { Chat = Chat(Saule, "Сауле", 1, contact: true), View = Personal(AudienceReason.Owner) },
        };
        Assert.IsFalse(PersonalChatsFolderModel.ShowNoContactsNote(ownerMarked, loaded: true),
            "a contact the owner already decided is still a contact the phone sees");
    }

    // ---- what a confirmed selection moves ----

    [Test]
    public void ChatsToMove_OnlyTheSelectedRowsTheServerTakes()
    {
        var rows = new List<FolderRow>
        {
            Row(Saule, AudienceReason.Contact),
            new FolderRow { Chat = Chat(Family, "Семья", 1), View = Personal(AudienceReason.NonHuman, NonHumanKind.Group) },
            Row(Dauren, AudienceReason.History),
            Row("*", AudienceReason.History),
        };
        var selected = new HashSet<string> { Dauren, Family, Saule, "*", "77019999999@c.us" };

        CollectionAssert.AreEqual(new[] { Saule, Dauren }, PersonalChatsFolderModel.ChatsToMove(rows, selected),
            "in the rows' order; never a non-person, the reply mode's wildcard or a chat the folder does not list");
        Assert.IsEmpty(PersonalChatsFolderModel.ChatsToMove(rows, new HashSet<string>()), "nothing marked — nothing moves");
        Assert.IsEmpty(PersonalChatsFolderModel.ChatsToMove(null, selected));
    }

    [Test]
    public void MovedToast_CountsTheChats()
    {
        Assert.IsNull(PersonalChatsFolderModel.MovedToast(0), "nothing moved — nothing to say");
        Assert.AreEqual("Чат стал рабочим", PersonalChatsFolderModel.MovedToast(1));
        Assert.AreEqual("2 чата стали рабочими", PersonalChatsFolderModel.MovedToast(2));
        Assert.AreEqual("5 чатов стали рабочими", PersonalChatsFolderModel.MovedToast(5));
        Assert.AreEqual("11 чатов стали рабочими", PersonalChatsFolderModel.MovedToast(11));
        Assert.AreEqual("21 чат стал рабочим", PersonalChatsFolderModel.MovedToast(21));
    }

    [Test]
    public void AnyMovable_NeedsAPerson()
    {
        Assert.IsFalse(PersonalChatsFolderModel.AnyMovable(new List<FolderRow>
        {
            new FolderRow { Chat = Chat(Family, "Семья", 1), View = Personal(AudienceReason.NonHuman, NonHumanKind.Group) },
        }), "a folder of groups has nothing to make working");
        Assert.IsTrue(PersonalChatsFolderModel.AnyMovable(new List<FolderRow> { Row(Saule, AudienceReason.Contact) }));
        Assert.IsFalse(PersonalChatsFolderModel.AnyMovable(null));
    }

    // ---- a bot or channel switch ----

    [Test]
    public void StaysOpen_OnlyForTheFoldersOwnBotAndChannel()
    {
        Assert.IsTrue(PersonalChatsFolderModel.StaysOpen("Bot1", ChatChannel.WhatsApp, "Bot1", ChatChannel.WhatsApp),
            "a switch naming the folder's own bot and channel leaves it open (the settings open it right after switching)");
        Assert.IsFalse(PersonalChatsFolderModel.StaysOpen("Bot1", ChatChannel.WhatsApp, "Bot2", ChatChannel.WhatsApp), "another bot");
        Assert.IsFalse(PersonalChatsFolderModel.StaysOpen("Bot1", ChatChannel.WhatsApp, "Bot1", ChatChannel.Telegram), "another channel");
        Assert.IsFalse(PersonalChatsFolderModel.StaysOpen("Bot1", ChatChannel.WhatsApp, "bot1", ChatChannel.WhatsApp), "bot keys are exact");
    }

    // ---- helpers ----

    private static ChatViewModel Chat(string id, string title, long lastSec, bool contact = false) =>
        new ChatViewModel(id, title, null, "", lastSec, facts: new AudienceFacts(false, id, "", false, false, false, contact));

    private static AudienceView Personal(AudienceReason reason, NonHumanKind nonHuman = NonHumanKind.None) =>
        new AudienceView { Kind = AudienceKind.Personal, Reason = reason, NonHuman = nonHuman };

    private static AudienceView Working() => new AudienceView { Kind = AudienceKind.Business, Reason = AudienceReason.New };

    private static FolderRow Row(string id, AudienceReason reason) =>
        new FolderRow { Chat = Chat(id, id, 1), View = Personal(reason), Selectable = true };

    private static string[] Ids(IReadOnlyList<FolderRow> rows) => rows.Select(row => row.Chat.ChatId).ToArray();
}
