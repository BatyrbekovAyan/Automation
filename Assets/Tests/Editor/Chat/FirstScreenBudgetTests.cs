using System;
using System.Collections.Generic;
using NUnit.Framework;

public class FirstScreenBudgetTests
{
    private static MessageViewModel Msg(MessageType type, float aspect = 1f, float rotation = 0f)
        => new MessageViewModel { type = type, aspectRatio = aspect, videoRotation = rotation };

    private static MessageViewModel Portrait() => Msg(MessageType.Image, 0.5625f);   // 9:16
    private static MessageViewModel Landscape() => Msg(MessageType.Image, 1.7778f);   // 16:9
    private static MessageViewModel Square() => Msg(MessageType.Image, 1.0f);
    private static MessageViewModel Text() => Msg(MessageType.Chat);
    private static MessageViewModel Document() => Msg(MessageType.Document);

    // --- Per-type / orientation weights -------------------------------------

    [Test] public void Weight_PortraitImage_Is9() => Assert.AreEqual(9f, FirstScreenBudget.Weight(Portrait()), 0.001f);
    [Test] public void Weight_LandscapeImage_Is5() => Assert.AreEqual(5f, FirstScreenBudget.Weight(Landscape()), 0.001f);
    [Test] public void Weight_SquareImage_Is8() => Assert.AreEqual(8f, FirstScreenBudget.Weight(Square()), 0.001f);
    [Test] public void Weight_Sticker_Is4point5() => Assert.AreEqual(4.5f, FirstScreenBudget.Weight(Msg(MessageType.Sticker)), 0.001f);
    [Test] public void Weight_Audio_Is2() => Assert.AreEqual(2f, FirstScreenBudget.Weight(Msg(MessageType.Audio)), 0.001f);
    [Test] public void Weight_Voice_Is2() => Assert.AreEqual(2f, FirstScreenBudget.Weight(Msg(MessageType.Voice)), 0.001f);
    [Test] public void Weight_Document_Is2() => Assert.AreEqual(2f, FirstScreenBudget.Weight(Document()), 0.001f);
    [Test] public void Weight_Text_Is1() => Assert.AreEqual(1f, FirstScreenBudget.Weight(Text()), 0.001f);
    [Test] public void Weight_Unknown_Is1() => Assert.AreEqual(1f, FirstScreenBudget.Weight(Msg(MessageType.Unknown)), 0.001f);

    [Test]
    public void Weight_RotatedLandscapeVideo_CountsAsPortrait()
    {
        // Phone portrait clip stored as a landscape frame + 90° rotation -> displays portrait.
        var rotated = Msg(MessageType.Video, 1.7778f, 90f);
        Assert.AreEqual(9f, FirstScreenBudget.Weight(rotated), 0.001f);
    }

    [Test]
    public void Weight_MissingDimensions_CountsAsSquare()
    {
        // Aspect normalizes to 1.0 upstream when the API omits width/height.
        Assert.AreEqual(8f, FirstScreenBudget.Weight(Msg(MessageType.Image, 1.0f)), 0.001f);
    }

    // --- Coverage rule (the reported bug) -----------------------------------

    [Test]
    public void Regression_DocumentThenTwoPortraits_ShowsAllThree()
    {
        // The originally-reported scenario: a document above two portrait images.
        // document(2) + portrait(9) + portrait(9) = 20, so all three are spawned
        // instead of stopping short and leaving an empty band at the top.
        var list = new List<MessageViewModel> { Document(), Portrait(), Portrait() };
        Assert.AreEqual(3, FirstScreenBudget.MessageCount(list));
    }

    [Test]
    public void ThreePortraits_AllShown_ThirdCoversGap()
    {
        // portrait = 9, so two portraits (18pts) leave the screen uncovered (< 20).
        // The coverage rule spawns the third to fill the gap (reaching 27); a naive
        // "fit within budget" rule would have stopped at two. This is the fix.
        var list = new List<MessageViewModel> { Portrait(), Portrait(), Portrait() };
        Assert.AreEqual(3, FirstScreenBudget.MessageCount(list));
    }

    [Test]
    public void FourPortraits_QueuesTheFourth()
    {
        // Three portraits (27pts) cover the screen; the fourth stays in the queue.
        var list = new List<MessageViewModel> { Portrait(), Portrait(), Portrait(), Portrait() };
        Assert.AreEqual(3, FirstScreenBudget.MessageCount(list));
    }

    [Test]
    public void TextThenPortrait_IncludesCrossingMessage()
    {
        // 19 text (19pts) leaves a 1pt gap; the portrait that crosses the budget
        // is spawned so no empty band remains at the top.
        var list = new List<MessageViewModel>();
        for (int i = 0; i < 19; i++) list.Add(Text());
        list.Add(Portrait());
        Assert.AreEqual(20, FirstScreenBudget.MessageCount(list));
    }

    [Test]
    public void TwentyText_FillsExactly()
    {
        var list = new List<MessageViewModel>();
        for (int i = 0; i < 30; i++) list.Add(Text());
        Assert.AreEqual(20, FirstScreenBudget.MessageCount(list));
    }

    [Test]
    public void SingleMessage_AlwaysShown()
    {
        var list = new List<MessageViewModel> { Portrait() };
        Assert.AreEqual(1, FirstScreenBudget.MessageCount(list));
    }

    [Test]
    public void EmptyOrNull_ReturnsZero()
    {
        Assert.AreEqual(0, FirstScreenBudget.MessageCount(new List<MessageViewModel>()));
        Assert.AreEqual(0, FirstScreenBudget.MessageCount(null));
    }

    // --- Cache-aware media weight (download card vs full bubble) -------------

    private static readonly Func<string, bool> NoneCached = _ => false;
    private static readonly Func<string, bool> AllCached  = _ => true;

    private static MessageViewModel UndownloadedPortrait(int i = 0) => new MessageViewModel
    {
        type = MessageType.Image, aspectRatio = 0.5625f, mediaUrl = $"http://p/{i}.jpg", messageId = $"m{i}"
    };

    [Test]
    public void Weight_UndownloadedPortrait_CostsCardWeight()
    {
        // Nothing cached and no thumbnail -> paints the fixed download card, so it
        // costs the card weight (3), not a full portrait (9).
        Assert.AreEqual(3f, FirstScreenBudget.Weight(UndownloadedPortrait(), NoneCached), 0.001f);
    }

    [Test]
    public void Weight_CachedPortrait_KeepsFullWeight()
    {
        // HD bytes already on disk -> instant full-size render -> full portrait weight.
        Assert.AreEqual(9f, FirstScreenBudget.Weight(UndownloadedPortrait(), AllCached), 0.001f);
    }

    [Test]
    public void Weight_PortraitWithBase64Thumbnail_KeepsFullWeight()
    {
        // A renderable (inline base64) thumbnail pre-grows the bubble to media size
        // even before the HD download, so it is weighed full despite no cached bytes.
        var vm = new MessageViewModel
        {
            type = MessageType.Image,
            aspectRatio = 0.5625f,
            mediaUrl = "http://p/x.jpg",
            messageId = "m",
            thumbnailUrl = "base64://AAAA"
        };
        Assert.AreEqual(9f, FirstScreenBudget.Weight(vm, NoneCached), 0.001f);
    }

    [Test]
    public void Weight_NullProbe_KeepsLegacyFullWeight()
    {
        // No probe supplied -> legacy behaviour, media always weighed at full height.
        Assert.AreEqual(9f, FirstScreenBudget.Weight(UndownloadedPortrait(), null), 0.001f);
        Assert.AreEqual(9f, FirstScreenBudget.Weight(UndownloadedPortrait()), 0.001f);
    }

    [Test]
    public void MessageCount_UndownloadedPortraits_FillScreenWithMore()
    {
        // The reported bug: a few undownloaded portraits only paint ~284px cards each,
        // leaving the viewport half empty. At card weight (3) the budget keeps pulling
        // messages until the screen is actually covered: 6 cards = 18pts (uncovered),
        // the 7th crosses 20 and is included -> 7 shown (vs. 3 under the old weights).
        var list = new List<MessageViewModel>();
        for (int i = 0; i < 10; i++) list.Add(UndownloadedPortrait(i));
        Assert.AreEqual(7, FirstScreenBudget.MessageCount(list, NoneCached));
    }

    [Test]
    public void MessageCount_CachedPortraits_UnchangedFromLegacy()
    {
        // When the same portraits are already cached they paint full-size, so the
        // legacy count stands: three cover the screen (27pts), the rest queue.
        var list = new List<MessageViewModel>();
        for (int i = 0; i < 10; i++) list.Add(UndownloadedPortrait(i));
        Assert.AreEqual(3, FirstScreenBudget.MessageCount(list, AllCached));
        Assert.AreEqual(3, FirstScreenBudget.MessageCount(list)); // null probe = legacy
    }
}
