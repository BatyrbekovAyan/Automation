using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

/// <summary>
/// Pins the facts about the two confirm cards in Main.unity —
/// Screen_Messanger/ReplyModeConfirmPopup («Авто») and
/// Screen_Messanger/ChatsPanel/DeleteChatConfirmPanel (delete chat) — that
/// ConfirmCardFitter's grow-by-overflow rule silently depends on.
///
/// The rule is: grow each text box by its overflow, slide the body down by the
/// title's growth, grow the card by the sum. That preserves the authored
/// body-to-button clearance for exactly one reason — the buttons hang off the
/// card's BOTTOM edge, so growing a centre-pivoted card moves them away from the
/// body by the same amount the body moved down. Re-anchor a button (one
/// full-width Confirm, a button pinned to the card top, a layout group over the
/// row) and the fitter keeps growing the card while the buttons no longer move:
/// a long title then reproduces the 2026-09-04 overlap against the BUTTONS
/// instead of against the body, with every other test still green.
///
/// AND — the lesson the delete card taught — the rule preserves whatever gap
/// the scene authored, INCLUDING a negative one. DeleteChatConfirmPanel shipped
/// with its Body box running 10u into the buttons' band, so fitting it would
/// have preserved the overlap at every size. BodyBox_ClearsTheButtons is
/// therefore the load-bearing test in this file: it checks the authored geometry
/// itself, which is the one thing no runtime fit can repair.
///
/// The seam tests exercise arithmetic and the fitter tests build their own card,
/// so neither can see the scene. This one reads Main.unity directly, in the style
/// of BotsHeaderIconsLayoutTests.Scene_HeaderIcons_carries_a_horizontal_ContentSizeFitter.
/// </summary>
public class ConfirmCardScenePremiseTests
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    /// <summary>The clearance both cards were authored with, and that growth preserves.</summary>
    private const float Clearance = 44f;

    /// <summary>Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset — what the delete card measures at.</summary>
    private const string SfProRegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";

    private const string AutoPopup = "ReplyModeConfirmPopup";
    private const string DeletePopup = "DeleteChatConfirmPanel";

    private static Dictionary<string, string> _docs;
    private static Dictionary<string, CardNodes> _cards;

    /// <summary>One popup's card and the children the fitter and its binder resolve by name.</summary>
    private sealed class CardNodes
    {
        public string Popup;
        public string[] Buttons;
        public Dictionary<string, string> Children;   // child GameObject name -> its RectTransform id

        public string Require(string childName)
        {
            Assert.IsTrue(Children.ContainsKey(childName),
                $"{Popup}/Content/{childName} is gone — ConfirmCardFitter and its caller both expect it.");
            return Children[childName];
        }

        public override string ToString() => Popup;
    }

    [OneTimeSetUp]
    public void ParseScene()
    {
        _docs = ParseDocuments(File.ReadAllText(ScenePath));
        Assert.Greater(_docs.Count, 1000, "Main.unity did not parse — every assertion below would be a false green");

        _cards = new Dictionary<string, CardNodes>
        {
            [AutoPopup] = Read(AutoPopup, "CancelButton", "ConfirmButton"),
            [DeletePopup] = Read(DeletePopup, "CancelButton", "DeleteButton"),
        };
    }

    /// <summary>
    /// The parsed card for one popup. Tests take the popup NAME rather than the
    /// CardNodes itself because NUnit builds a [TestCase]'s arguments at discovery,
    /// before [OneTimeSetUp] has parsed anything — passing the object would pass null.
    /// </summary>
    private static CardNodes Card(string popup)
    {
        Assert.IsNotNull(_cards, "Scene parse did not run");
        return _cards[popup];
    }

    // --- the baseline the fitter captures ---------------------------------

    [Test]
    public void AutoCard_And_Texts_KeepTheAuthoredGeometryTheTestsAssume()
    {
        AssertRect(Card(AutoPopup), "Content", sizeDelta: "{x: 720, y: 440}");
        AssertRect(Card(AutoPopup), "Title", anchoredPosition: "{x: 0, y: -52}", sizeDelta: "{x: -80, y: 64}");
        AssertRect(Card(AutoPopup), "Body", anchoredPosition: "{x: 0, y: -118}", sizeDelta: "{x: -80, y: 130}");
    }

    [Test]
    public void DeleteCard_And_Texts_KeepTheAuthoredGeometryTheTestsAssume()
    {
        AssertRect(Card(DeletePopup), "Content", sizeDelta: "{x: 820, y: 460}");
        AssertRect(Card(DeletePopup), "Title", anchoredPosition: "{x: 0, y: -60}", sizeDelta: "{x: 760, y: 70}");

        // 86, not the 140 this card shipped with. See BodyBox_ClearsTheButtons.
        AssertRect(Card(DeletePopup), "Body", anchoredPosition: "{x: 0, y: -150}", sizeDelta: "{x: 760, y: 86}");
    }

    // --- the premise grow-by-overflow rests on ----------------------------

    /// <summary>
    /// The authored gap between the body box and the buttons, computed from the
    /// scene's own numbers rather than restated. Both cards are 44u — and the
    /// delete card was -10u until 2026-09-04, which is the whole reason this test
    /// exists: grow-by-overflow would have carried that -10u to every size, so
    /// wiring up ConfirmCardFitter would have looked like a fix and been none.
    ///
    /// Fixing a future overlap by making a text box TALLER moves this number the
    /// wrong way and fails here. The lever is the card height or the box's y.
    /// </summary>
    [Test]
    [TestCase(AutoPopup)]
    [TestCase(DeletePopup)]
    public void BodyBox_ClearsTheButtons(string popup)
    {
        CardNodes card = Card(popup);

        float cardHeight = Vec(card.Require("Content"), "m_SizeDelta").y;
        float bodyTop = -Vec(card.Require("Body"), "m_AnchoredPosition").y;
        float bodyHeight = Vec(card.Require("Body"), "m_SizeDelta").y;

        // Buttons are bottom-anchored, so each owns (its y + its height) of the
        // card's bottom edge; the taller reach wins.
        float buttonBlock = 0f;
        foreach (string button in card.Buttons)
        {
            string rect = card.Require(button);
            float reach = Vec(rect, "m_AnchoredPosition").y + Vec(rect, "m_SizeDelta").y;
            if (reach > buttonBlock) buttonBlock = reach;
        }

        float buttonTop = cardHeight - buttonBlock;
        float gap = buttonTop - (bodyTop + bodyHeight);

        Assert.Greater(gap, 0f,
            $"{card}: the body box reaches into the buttons' band. ConfirmCardFitter CANNOT fix this — " +
            "it preserves authored gaps by design, so it would carry the overlap to every size. " +
            "Correct the card's authored rects.");
        Assert.AreEqual(Clearance, gap, 0.001f,
            $"{card}: both confirm cards are authored with {Clearance}u between the body box and the buttons.");
    }

    [Test]
    [TestCase(AutoPopup)]
    [TestCase(DeletePopup)]
    public void Buttons_HangOffTheCardsBottomEdge(string popup)
    {
        CardNodes card = Card(popup);

        foreach (string button in card.Buttons)
        {
            string rect = card.Require(button);

            StringAssert.Contains("y: 0}", Field(rect, "m_AnchorMin"),
                $"{card}/{button}: anchorMin.y must be 0 — growing the card only moves the buttons away " +
                "from the body while they are anchored to its bottom edge.");
            StringAssert.Contains("y: 0}", Field(rect, "m_AnchorMax"),
                $"{card}/{button}: anchorMax.y must be 0 (see anchorMin).");
            StringAssert.Contains("y: 0}", Field(rect, "m_Pivot"),
                $"{card}/{button}: pivot.y must be 0, so its distance to the card's bottom edge is fixed.");
        }
    }

    [Test]
    [TestCase(AutoPopup)]
    [TestCase(DeletePopup)]
    public void Texts_HangFromTheCardsTopEdge(string popup)
    {
        CardNodes card = Card(popup);

        foreach (string text in new[] { "Title", "Body" })
        {
            string rect = card.Require(text);

            StringAssert.Contains("y: 1}", Field(rect, "m_AnchorMin"),
                $"{card}/{text}: must stay top-anchored — the fit writes an absolute anchoredPosition.y " +
                "measured from the card's top edge.");
            StringAssert.Contains("y: 1}", Field(rect, "m_AnchorMax"), $"{card}/{text}: see anchorMin.");
            StringAssert.Contains("y: 1}", Field(rect, "m_Pivot"),
                $"{card}/{text}: pivot.y must be 1, so growing its height extends it DOWNWARD and line one never moves.");
        }
    }

    [Test]
    [TestCase(AutoPopup)]
    [TestCase(DeletePopup)]
    public void Card_IsCentrePivoted(string popup)
    {
        CardNodes card = Card(popup);
        string rect = card.Require("Content");

        StringAssert.Contains("y: 0.5}", Field(rect, "m_Pivot"),
            $"{card}: the card grows about its centre, which is what keeps a bottom-anchored button " +
            "the same distance below the body after the card is made taller.");
    }

    [Test]
    [TestCase(AutoPopup)]
    [TestCase(DeletePopup)]
    public void Card_HasNoLayoutGroupOrFitterThatWouldFightTheWrittenRects(string popup)
    {
        CardNodes card = Card(popup);

        foreach (string component in ComponentBlocks(GameObjectOf(card.Require("Content"))))
        {
            StringAssert.DoesNotContain("LayoutGroup", component,
                $"{card}: a layout group on the card would overwrite the rects ConfirmCardFitter writes.");
            StringAssert.DoesNotContain("ContentSizeFitter", component,
                $"{card}: a ContentSizeFitter on the card would fight the solved card height.");
        }
    }

    [Test]
    [TestCase(AutoPopup)]
    [TestCase(DeletePopup)]
    public void Texts_WrapAndOverflow_SoTheMeasurementMatchesWhatIsDrawn(string popup)
    {
        CardNodes card = Card(popup);

        foreach (string text in new[] { "Title", "Body" })
        {
            string tmp = TmpBlock(GameObjectOf(card.Require(text)));
            Assert.IsNotNull(tmp, $"{card}/{text} lost its TextMeshProUGUI");

            StringAssert.Contains("m_TextWrappingMode: 1", tmp,
                $"{card}/{text}: wrapping must stay ON — the fit measures a wrapped height and would " +
                "otherwise size the box for text that renders on one long line.");
            StringAssert.Contains("m_enableAutoSizing: 0", tmp,
                $"{card}/{text}: auto-sizing would shrink the text instead of growing the card, and the two " +
                "mechanisms would fight over the same overflow.");
        }
    }

    /// <summary>
    /// ChatDeleteConfirmCardTests measures against SFProText-Regular by path. If the
    /// scene's Body is repointed at another weight, every line count in that file
    /// silently describes a card that no longer ships.
    /// </summary>
    [Test]
    public void DeleteCard_Body_UsesTheFontItsFitterTestsMeasureWith()
    {
        string tmp = TmpBlock(GameObjectOf(Card(DeletePopup).Require("Body")));

        StringAssert.Contains(SfProRegularGuid, FieldIn(tmp, "m_fontAsset"),
            "DeleteChatConfirmPanel/Content/Body no longer uses SFProText-Regular SDF — the 39.36u line " +
            "height in ChatDeleteConfirmCardTests was derived from that asset's own metrics.");
    }

    // --- raw-YAML helpers -------------------------------------------------

    private static CardNodes Read(string popupName, params string[] buttons)
    {
        string popupGo = FindGameObjectByName(popupName);
        Assert.IsNotNull(popupGo, $"{popupName} is gone from Main.unity.");

        string cardRect = ChildRectByName(RectOf(popupGo), "Content");
        Assert.IsNotNull(cardRect, $"{popupName}/Content is gone — PopupUI.FindCard resolves the card by that name.");

        var children = new Dictionary<string, string>();
        foreach (string childRect in ChildRects(cardRect))
            children[NameOf(GameObjectOf(childRect))] = childRect;
        children["Content"] = cardRect;

        return new CardNodes { Popup = popupName, Buttons = buttons, Children = children };
    }

    private static void AssertRect(CardNodes card, string childName,
        string anchoredPosition = null, string sizeDelta = null)
    {
        string rect = card.Require(childName);

        if (anchoredPosition != null)
            Assert.AreEqual(anchoredPosition, Field(rect, "m_AnchoredPosition"),
                $"{card}/{childName}.anchoredPosition changed. The fitter reads this as the authored baseline, " +
                "so the app still behaves — but the measured constants in the seam tests no longer describe it.");

        if (sizeDelta != null)
            Assert.AreEqual(sizeDelta, Field(rect, "m_SizeDelta"),
                $"{card}/{childName}.sizeDelta changed. Enlarging a text box is NOT how a new overlap is fixed — " +
                "it only raises the fixed minimum, and it eats the clearance above the buttons. See ConfirmCardLayout.");
    }

    /// <summary>The {x: .., y: ..} value of <paramref name="field"/>, as numbers.</summary>
    private static (float x, float y) Vec(string docId, string field)
    {
        Match m = Regex.Match(Field(docId, field), @"x:\s*(-?[\d.]+),\s*y:\s*(-?[\d.]+)");
        Assert.IsTrue(m.Success, $"{field} on document {docId} is not a vector: '{Field(docId, field)}'");
        return (float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, string> ParseDocuments(string scene)
    {
        var docs = new Dictionary<string, string>();
        var matches = Regex.Matches(scene, @"^--- !u!\d+ &(\d+)(?: stripped)?\s*$", RegexOptions.Multiline);

        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index + matches[i].Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : scene.Length;
            docs[matches[i].Groups[1].Value] = scene.Substring(start, end - start);
        }
        return docs;
    }

    private static string FindGameObjectByName(string name)
    {
        foreach (var kv in _docs)
            if (kv.Value.Contains("\nGameObject:") && Regex.IsMatch(kv.Value, $@"^\s+m_Name: {Regex.Escape(name)}\s*$", RegexOptions.Multiline))
                return kv.Key;
        return null;
    }

    private static IEnumerable<string> ComponentBlocks(string gameObjectId)
    {
        foreach (Match m in Regex.Matches(_docs[gameObjectId], @"- component: \{fileID: (\d+)\}"))
            if (_docs.TryGetValue(m.Groups[1].Value, out string block))
                yield return block;
    }

    private static string RectOf(string gameObjectId)
    {
        foreach (Match m in Regex.Matches(_docs[gameObjectId], @"- component: \{fileID: (\d+)\}"))
            if (_docs.TryGetValue(m.Groups[1].Value, out string block) && block.Contains("\nRectTransform:"))
                return m.Groups[1].Value;
        return null;
    }

    private static string GameObjectOf(string rectId) =>
        Regex.Match(_docs[rectId], @"m_GameObject: \{fileID: (\d+)\}").Groups[1].Value;

    private static string NameOf(string gameObjectId) =>
        Regex.Match(_docs[gameObjectId], @"^\s+m_Name: (.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();

    private static IEnumerable<string> ChildRects(string rectId)
    {
        string body = _docs[rectId];
        int start = body.IndexOf("m_Children:", System.StringComparison.Ordinal);
        int end = body.IndexOf("m_Father:", System.StringComparison.Ordinal);
        if (start < 0 || end < start) yield break;

        foreach (Match m in Regex.Matches(body.Substring(start, end - start), @"\{fileID: (\d+)\}"))
            yield return m.Groups[1].Value;
    }

    private static string ChildRectByName(string parentRectId, string name)
    {
        foreach (string child in ChildRects(parentRectId))
            if (NameOf(GameObjectOf(child)) == name) return child;
        return null;
    }

    /// <summary>The TextMeshProUGUI block on a GameObject, as raw YAML.</summary>
    private static string TmpBlock(string gameObjectId)
    {
        foreach (string block in ComponentBlocks(gameObjectId))
            if (block.Contains("m_fontAsset:")) return block;
        return null;
    }

    private static string Field(string docId, string field) => FieldIn(_docs[docId], field);

    /// <summary>Field lookup inside an already-extracted YAML block.</summary>
    private static string FieldIn(string block, string field) =>
        Regex.Match(block, $@"^\s+{field}: (.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();
}
