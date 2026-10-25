using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.2, §5.8): the working chat list shows only working chats; personal ones stay behind one
/// «Личные» row with a grey counter of the personal chats that have unread messages, and come back — with a lock — only
/// while a search is on. The tab counters count working chats only. Pure: <see cref="PersonalChatsListModel"/> reads the
/// <see cref="AudienceView"/> the store draws a chat by; a missing view reads as personal (unknown → the safe side).
/// </summary>
public class PersonalChatsListModelTests
{
    private static AudienceView Working(NonHumanKind nonHuman = NonHumanKind.None) =>
        new AudienceView { Kind = AudienceKind.Business, Reason = AudienceReason.New, NonHuman = nonHuman };

    private static AudienceView Personal(AudienceReason reason = AudienceReason.Contact, NonHumanKind nonHuman = NonHumanKind.None) =>
        new AudienceView { Kind = AudienceKind.Personal, Reason = reason, NonHuman = nonHuman };

    [Test]
    public void Visible_WorkingAlways_PersonalOnlyInSearch()
    {
        Assert.IsTrue(PersonalChatsListModel.Visible(Working(), searchActive: false), "a working chat is in the working list");
        Assert.IsTrue(PersonalChatsListModel.Visible(Working(), searchActive: true), "…and in the search results");
        Assert.IsTrue(PersonalChatsListModel.Visible(new AudienceView { Kind = AudienceKind.Business, Reason = AudienceReason.Owner, Pending = true },
            searchActive: false), "a chat the owner is moving to the working list is drawn there at once");

        Assert.IsFalse(PersonalChatsListModel.Visible(Personal(), searchActive: false), "a personal chat stays behind the «Личные» row");
        Assert.IsFalse(PersonalChatsListModel.Visible(Personal(AudienceReason.NonHuman, NonHumanKind.Group), searchActive: false),
            "a group is personal like every non-person");
        Assert.IsFalse(PersonalChatsListModel.Visible(Personal(AudienceReason.Unknown), searchActive: false),
            "a chat nothing is known about is personal");
        Assert.IsFalse(PersonalChatsListModel.Visible(null, searchActive: false), "no view at all reads as personal");

        Assert.IsTrue(PersonalChatsListModel.Visible(Personal(), searchActive: true), "the search finds personal chats too");
        Assert.IsTrue(PersonalChatsListModel.Visible(Personal(AudienceReason.NonHuman, NonHumanKind.Channel), searchActive: true));
    }

    [Test]
    public void ShowLock_OnlySearchAndPersonal()
    {
        Assert.IsTrue(PersonalChatsListModel.ShowLock(Personal(), searchActive: true), "a personal chat in the search results wears the lock");
        Assert.IsTrue(PersonalChatsListModel.ShowLock(Personal(AudienceReason.NonHuman, NonHumanKind.Bot), searchActive: true));
        Assert.IsTrue(PersonalChatsListModel.ShowLock(null, searchActive: true), "no view at all reads as personal");

        Assert.IsFalse(PersonalChatsListModel.ShowLock(Working(), searchActive: true), "a working chat never wears it");
        Assert.IsFalse(PersonalChatsListModel.ShowLock(Working(), searchActive: false));
        Assert.IsFalse(PersonalChatsListModel.ShowLock(Personal(), searchActive: false),
            "without a search a personal chat is not in the list at all — nothing to mark");
    }

    [Test]
    public void RowVisible_NeedsPersonalChatsAndNoSearch()
    {
        Assert.IsTrue(PersonalChatsListModel.RowVisible(personalCount: 1, searchActive: false));
        Assert.IsTrue(PersonalChatsListModel.RowVisible(personalCount: 40, searchActive: false));

        Assert.IsFalse(PersonalChatsListModel.RowVisible(personalCount: 0, searchActive: false), "no personal chats — no row");
        Assert.IsFalse(PersonalChatsListModel.RowVisible(personalCount: 3, searchActive: true),
            "a search shows the personal chats themselves, so the row steps aside");
        Assert.IsFalse(PersonalChatsListModel.RowVisible(personalCount: 0, searchActive: true));
    }

    [Test]
    public void PersonalCount_CountsEveryPersonalChat_NonPersonsIncluded()
    {
        var chats = new List<(AudienceView v, int unread)>
        {
            (Personal(), 0),
            (Personal(AudienceReason.NonHuman, NonHumanKind.Group), 5),
            (Working(), 2),
            (null, 0),
        };
        Assert.AreEqual(3, PersonalChatsListModel.PersonalCount(chats), "a contact, a group and a chat with no view");
        Assert.AreEqual(0, PersonalChatsListModel.PersonalCount(new List<(AudienceView v, int unread)>()));
        Assert.AreEqual(0, PersonalChatsListModel.PersonalCount(null));
    }

    [Test]
    public void PersonalUnreadChats_CountsChatsNotMessages()
    {
        var chats = new List<(AudienceView v, int unread)>
        {
            (Personal(), 7),                                                   // a contact with seven unread messages — one chat
            (Personal(AudienceReason.History), 1),
            (Personal(AudienceReason.NonHuman, NonHumanKind.Group), 30),       // a group is personal too
            (Personal(AudienceReason.Owner), 0),                               // read
            (Working(), 4),                                                    // working — the tab counter's business
            (Personal(), -3),                                                  // a broken count is no unread
        };
        Assert.AreEqual(3, PersonalChatsListModel.PersonalUnreadChats(chats), "chats with something unread, not their messages");
        Assert.AreEqual(0, PersonalChatsListModel.PersonalUnreadChats(new List<(AudienceView v, int unread)> { (Working(), 9) }));
        Assert.AreEqual(0, PersonalChatsListModel.PersonalUnreadChats(null));
    }

    [Test]
    public void WorkingUnreadTotal_SkipsPersonal()
    {
        var chats = new List<(AudienceView v, int unread)>
        {
            (Working(), 2),
            (Working(), 3),
            (Working(), -1),                                                   // a broken count adds nothing
            (Personal(), 7),
            (Personal(AudienceReason.NonHuman, NonHumanKind.Channel), 12),
            (Personal(AudienceReason.Unknown), 1),
            (null, 6),                                                         // no view — personal
        };
        Assert.AreEqual(5, PersonalChatsListModel.WorkingUnreadTotal(chats), "messages of working chats only");
        Assert.AreEqual(0, PersonalChatsListModel.WorkingUnreadTotal(new List<(AudienceView v, int unread)> { (Personal(), 3) }));
        Assert.AreEqual(0, PersonalChatsListModel.WorkingUnreadTotal(null));
    }

    [Test]
    public void IsPersonal_IsTheViewsKind_AndNoViewIsPersonal()
    {
        Assert.IsTrue(PersonalChatsListModel.IsPersonal(Personal()));
        Assert.IsTrue(PersonalChatsListModel.IsPersonal(null));
        Assert.IsFalse(PersonalChatsListModel.IsPersonal(Working()));
    }
}
