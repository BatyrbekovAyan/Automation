using Automation.BotSettingsUI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Guards the drag routing of the Bot Settings scrollable cards (Описание,
/// Промпт): DragShield is what forwards a drag to the card's own ScrollRect,
/// so it has to cover the whole input — any strip of the card it misses falls
/// through to the page instead.
///
/// Note this is only half the story. The SwipeBack strip is a full-height band
/// over the left ~200px that sits ABOVE the tab content, so inside that band no
/// card graphic wins the raycast at all; SwipeToBackBotSettings resolves the
/// target there (see ResolveVerticalTarget).
/// </summary>
public class BotSettingsDragRoutingTests
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";

    private static GameObject LoadPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "BotSettings.prefab not found");
        return prefab;
    }

    [Test]
    public void EveryScrollableCard_HasAShieldCoveringItsWholeInput()
    {
        var cards = LoadPrefab().GetComponentsInChildren<ScrollableTextArea>(true);
        Assert.IsNotEmpty(cards, "No ScrollableTextArea cards on the prefab.");

        foreach (var card in cards)
        {
            var input = card.GetComponentInChildren<TMP_InputField>(true);
            Assert.IsNotNull(input, $"{card.name}: no TMP_InputField.");

            var shield = input.GetComponentInChildren<DragShield>(true);
            Assert.IsNotNull(shield,
                $"{card.name}: no DragShield — nothing would route drags to the card's ScrollRect.");

            // Last sibling, so the shield sits above TMP's text in the canvas
            // draw order rather than racing it.
            Assert.AreEqual(input.transform.childCount - 1, shield.transform.GetSiblingIndex(),
                $"{card.name}: DragShield must be the front-most child of the input.");

            var shieldRt = (RectTransform)shield.transform;
            Assert.AreEqual(Vector2.zero, shieldRt.anchorMin, $"{card.name}: shield must stretch to the input.");
            Assert.AreEqual(Vector2.one, shieldRt.anchorMax, $"{card.name}: shield must stretch to the input.");
            Assert.AreEqual(Vector2.zero, shieldRt.sizeDelta,
                $"{card.name}: an inset shield leaves a strip of the card falling through to the page.");
        }
    }

    [Test]
    public void EveryScrollableCard_ShieldIsARaycastTarget()
    {
        foreach (var card in LoadPrefab().GetComponentsInChildren<ScrollableTextArea>(true))
        {
            var shield = card.GetComponentInChildren<DragShield>(true);
            var image = shield.GetComponent<Image>();
            Assert.IsTrue(image.raycastTarget,
                $"{card.name}: DragShield's Image must be a raycast target or it receives nothing.");
        }
    }

    // TMP's Text Area is inset from the input vertically too, and that inset
    // must be added back when sizing the scroll content. Without it the text
    // column ends up shorter than the text, TMP starts scrolling the text
    // internally to keep the caret visible, and the FIRST ROW can never be
    // scrolled back — the offset lives on the text component, not on the
    // content our ScrollRect moves.
    [Test]
    public void TextAreaVerticalInset_LeavesRoomForTheWholeText()
    {
        foreach (var card in LoadPrefab().GetComponentsInChildren<ScrollableTextArea>(true))
        {
            var input = card.GetComponentInChildren<TMP_InputField>(true);
            var chrome = -input.textViewport.sizeDelta.y;   // stretch-anchored: sizeDelta.y is the negative inset
            Assert.Greater(chrome, 0f, $"{card.name}: expected Text Area to be inset from the input.");

            const float textHeight = 787f;   // device-measured long description
            var content = ScrollableTextAreaMetrics.ContentHeight(textHeight, chrome, 8f, 360f);

            Assert.GreaterOrEqual(content - chrome, textHeight,
                $"{card.name}: the text column must fit the text, or TMP takes over scrolling.");
        }
    }

    // Pins the geometry that makes the measure-width fix necessary: TMP's text
    // column is genuinely narrower than the card, so measuring wrapped height
    // at the card width under-counts lines.
    [Test]
    public void TextColumn_IsNarrowerThanTheCard()
    {
        foreach (var card in LoadPrefab().GetComponentsInChildren<ScrollableTextArea>(true))
        {
            var input = card.GetComponentInChildren<TMP_InputField>(true);
            var textArea = input.textViewport;
            Assert.IsNotNull(textArea, $"{card.name}: no textViewport.");
            Assert.Less(textArea.sizeDelta.x, 0f,
                $"{card.name}: Text Area is expected to be inset from the card; if that ever changes, " +
                "ScrollableTextArea's measure width should be revisited.");
        }
    }
}
