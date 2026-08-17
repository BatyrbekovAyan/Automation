using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Guards the product/service list card against the title-over-price collision:
/// NameDesc's group must SIZE its children, not merely align them. With
/// childControlWidth off, Name and Desc keep the width baked into the prefab
/// (720/740) while their column is only the card minus the 224-unit price lane,
/// so the title ellipsised on top of the price — the defect reported on device
/// 2026-08-14.
///
/// The assertion is on the flag rather than on the serialized child width: the
/// width is only correct once a layout pass runs, which does not happen while
/// the prefab sits on disk.
/// </summary>
public class ItemCardTextBoundsTests
{
    private static readonly string[] CardPrefabPaths =
    {
        "Assets/Prefabs/Product.prefab",
        "Assets/Prefabs/Service.prefab",
    };

    [Test]
    public void CardTitleColumn_SizesItsChildrenToTheColumn()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(card, $"{path} not found");

            var column = card.transform.Find("Info/NameDesc");
            Assert.IsNotNull(column, $"{path}: the Info/NameDesc column is gone.");

            var group = column.GetComponent<VerticalLayoutGroup>();
            Assert.IsNotNull(group, $"{path}: NameDesc lost its VerticalLayoutGroup.");
            Assert.IsTrue(group.childControlWidth,
                $"{path}: childControlWidth is off — Name/Desc keep their authored width " +
                "and a long title paints over the price.");
        }
    }

    [Test]
    public void CardTitle_TruncatesOnOneLine()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var title = card.transform.Find("Info/NameDesc/Name")?.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(title, $"{path}: no Name label.");

            Assert.AreEqual(TextWrappingModes.NoWrap, title.textWrappingMode,
                $"{path}: the title box is one line tall — wrapping hides the overflow instead of marking it.");
            Assert.AreEqual(TextOverflowModes.Ellipsis, title.overflowMode,
                $"{path}: a clipped title must end in an ellipsis, not a hard cut.");
        }
    }

    // The column reserves exactly the price lane on its right. If that reserve
    // is ever dropped the collision returns even with childControlWidth on.
    [Test]
    public void CardTitleColumn_StopsAtThePriceLane()
    {
        foreach (var path in CardPrefabPaths)
        {
            var card = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var column = (RectTransform)card.transform.Find("Info/NameDesc");
            var price = (RectTransform)card.transform.Find("Info/Price");
            Assert.IsNotNull(price, $"{path}: no Price label.");

            // Column: right edge inset from the parent's right by -offsetMax.x.
            // Price: right-anchored, so its left edge sits at -anchoredPosition.x + width.
            float columnRightInset = -column.offsetMax.x;
            float priceLeftInset = -price.anchoredPosition.x + price.sizeDelta.x;
            Assert.GreaterOrEqual(columnRightInset, priceLeftInset,
                $"{path}: the text column reaches into the price lane " +
                $"(column stops {columnRightInset} from the right, price starts at {priceLeftInset}).");
        }
    }
}
