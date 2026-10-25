using System;
using NUnit.Framework;

/// <summary>
/// Pins ChatRowSwipePolicy — which actions a chat row's swipe offers (spec §5.2, §5.3):
/// <list type="bullet">
///   <item>the working list: a working chat offers «В личные» and, on WhatsApp only, today's «Удалить» (Telegram has no
///   chat/delete endpoint, so a Telegram row never carries a delete);</item>
///   <item>the «Личные» folder: «В рабочие» and nothing else;</item>
///   <item>a chat that is not a person (a group, a channel, a bot, the service account, «Избранное») never offers a move — in
///   the folder its row has no swipe at all;</item>
///   <item>the working list's search results: a personal chat found there swipes as it would in the folder
///   (<see cref="ChatRowSwipePolicy.InFolder"/>), with the folder's word (<see cref="ChatRowSwipePolicy.MoveLabel"/>).</item>
/// </list>
/// </summary>
public class ChatRowSwipePolicyTests
{
    private const bool List = false;
    private const bool Folder = true;

    // ---- the matrix: both channels × the list and the folder × a person and every kind of non-person ----

    [TestCase(ChatChannel.WhatsApp, List, NonHumanKind.None, true, true)]
    [TestCase(ChatChannel.WhatsApp, List, NonHumanKind.Group, false, true)]
    [TestCase(ChatChannel.WhatsApp, List, NonHumanKind.Channel, false, true)]
    [TestCase(ChatChannel.WhatsApp, List, NonHumanKind.Bot, false, true)]
    [TestCase(ChatChannel.WhatsApp, List, NonHumanKind.Service, false, true)]
    [TestCase(ChatChannel.WhatsApp, List, NonHumanKind.Self, false, true)]
    [TestCase(ChatChannel.Telegram, List, NonHumanKind.None, true, false)]
    [TestCase(ChatChannel.Telegram, List, NonHumanKind.Group, false, false)]
    [TestCase(ChatChannel.Telegram, List, NonHumanKind.Channel, false, false)]
    [TestCase(ChatChannel.Telegram, List, NonHumanKind.Bot, false, false)]
    [TestCase(ChatChannel.Telegram, List, NonHumanKind.Service, false, false)]
    [TestCase(ChatChannel.Telegram, List, NonHumanKind.Self, false, false)]
    [TestCase(ChatChannel.WhatsApp, Folder, NonHumanKind.None, true, false)]
    [TestCase(ChatChannel.WhatsApp, Folder, NonHumanKind.Group, false, false)]
    [TestCase(ChatChannel.WhatsApp, Folder, NonHumanKind.Channel, false, false)]
    [TestCase(ChatChannel.WhatsApp, Folder, NonHumanKind.Bot, false, false)]
    [TestCase(ChatChannel.WhatsApp, Folder, NonHumanKind.Service, false, false)]
    [TestCase(ChatChannel.WhatsApp, Folder, NonHumanKind.Self, false, false)]
    [TestCase(ChatChannel.Telegram, Folder, NonHumanKind.None, true, false)]
    [TestCase(ChatChannel.Telegram, Folder, NonHumanKind.Group, false, false)]
    [TestCase(ChatChannel.Telegram, Folder, NonHumanKind.Channel, false, false)]
    [TestCase(ChatChannel.Telegram, Folder, NonHumanKind.Bot, false, false)]
    [TestCase(ChatChannel.Telegram, Folder, NonHumanKind.Service, false, false)]
    [TestCase(ChatChannel.Telegram, Folder, NonHumanKind.Self, false, false)]
    public void Actions_Matrix(ChatChannel channel, bool inFolder, NonHumanKind nonHuman, bool move, bool delete)
    {
        Assert.AreEqual((move, delete), ChatRowSwipePolicy.Actions(channel, inFolder, nonHuman));
    }

    [Test]
    public void Actions_EveryKindOfNonPerson_NeverMoves()
    {
        foreach (NonHumanKind kind in Enum.GetValues(typeof(NonHumanKind)))
        {
            if (kind == NonHumanKind.None) continue;
            foreach (ChatChannel channel in Enum.GetValues(typeof(ChatChannel)))
            {
                Assert.IsFalse(ChatRowSwipePolicy.Actions(channel, List, kind).Move, $"{kind} on {channel}, the list");
                Assert.IsFalse(ChatRowSwipePolicy.Actions(channel, Folder, kind).Move, $"{kind} on {channel}, the folder");
            }
        }
    }

    // ---- the working list's search results (a personal chat swipes as in the folder) ----

    [Test]
    public void SearchResults_APersonalRow_SwipesAsInTheFolder()
    {
        var personal = new AudienceView { Kind = AudienceKind.Personal, Reason = AudienceReason.Contact, NonHuman = NonHumanKind.None };
        bool place = ChatRowSwipePolicy.InFolder(personal);

        Assert.IsTrue(place, "a personal chat found by the search stands in the folder's place");
        Assert.AreEqual((true, false), ChatRowSwipePolicy.Actions(ChatChannel.WhatsApp, place, personal.NonHuman),
            "«В рабочие» and no «Удалить» — the folder's rule, on WhatsApp too");
        Assert.AreEqual((true, false), ChatRowSwipePolicy.Actions(ChatChannel.Telegram, place, personal.NonHuman));
        Assert.AreEqual(PersonalChatsCopy.SwipeToBusiness, ChatRowSwipePolicy.MoveLabel(place));
    }

    [Test]
    public void SearchResults_ANonPerson_GetsNoSwipe()
    {
        foreach (NonHumanKind kind in new[] { NonHumanKind.Group, NonHumanKind.Channel, NonHumanKind.Bot, NonHumanKind.Service, NonHumanKind.Self })
        {
            var view = new AudienceView { Kind = AudienceKind.Personal, Reason = AudienceReason.NonHuman, NonHuman = kind };
            bool place = ChatRowSwipePolicy.InFolder(view);
            Assert.AreEqual((false, false), ChatRowSwipePolicy.Actions(ChatChannel.WhatsApp, place, kind), $"{kind} on WhatsApp");
            Assert.AreEqual((false, false), ChatRowSwipePolicy.Actions(ChatChannel.Telegram, place, kind), $"{kind} on Telegram");
        }
    }

    [Test]
    public void WorkingList_AWorkingRow_OffersToPersonal_AndTodaysDelete()
    {
        var working = new AudienceView { Kind = AudienceKind.Business, Reason = AudienceReason.New, NonHuman = NonHumanKind.None };
        bool place = ChatRowSwipePolicy.InFolder(working);

        Assert.IsFalse(place, "a working chat stands in the list's place");
        Assert.AreEqual((true, true), ChatRowSwipePolicy.Actions(ChatChannel.WhatsApp, place, working.NonHuman));
        Assert.AreEqual((true, false), ChatRowSwipePolicy.Actions(ChatChannel.Telegram, place, working.NonHuman),
            "Telegram has no chat/delete endpoint");
        Assert.AreEqual(PersonalChatsCopy.SwipeToPersonal, ChatRowSwipePolicy.MoveLabel(place));
    }

    [Test]
    public void InFolder_FollowsTheKindShownNow_AnOwnersMoveIncluded()
    {
        // ViewOf draws the owner's move at once (Pending): the row's swipe follows the kind the chat is moving to.
        Assert.IsFalse(ChatRowSwipePolicy.InFolder(new AudienceView { Kind = AudienceKind.Business, Reason = AudienceReason.Owner, Pending = true }));
        Assert.IsTrue(ChatRowSwipePolicy.InFolder(new AudienceView { Kind = AudienceKind.Personal, Reason = AudienceReason.Owner, Pending = true }));
        Assert.IsFalse(ChatRowSwipePolicy.InFolder(null), "no verdict — the list's place");
    }

    [Test]
    public void MoveLabel_NamesWhereTheChatGoes()
    {
        Assert.AreEqual(PersonalChatsCopy.SwipeToPersonal, ChatRowSwipePolicy.MoveLabel(List));
        Assert.AreEqual(PersonalChatsCopy.SwipeToBusiness, ChatRowSwipePolicy.MoveLabel(Folder));
    }
}
