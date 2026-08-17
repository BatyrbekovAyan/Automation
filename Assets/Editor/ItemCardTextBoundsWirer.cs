using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the product/service list card's title and description inside their own
/// column so a long name can never paint over the price.
///
/// The bug: NameDesc's VerticalLayoutGroup shipped with childControlWidth OFF,
/// which makes uGUI position the children but leave their authored width alone
/// — Name and Desc kept the 720/740 units baked into the prefab while their
/// container is only as wide as the card minus the 224-unit price lane. The
/// text therefore ellipsised hundreds of units to the RIGHT of the column, on
/// top of the price (Price is a later sibling, so the two simply collided).
///
/// The container geometry itself was already right, so the fix is to let the
/// group drive the width and to truncate the single-line title instead of
/// wrapping it into a second line the 60-unit box cannot show.
///
/// ADDITIVE AND IDEMPOTENT: it flips flags on existing components only. Do NOT
/// "fix" this by re-running Tools/Rebuild Bot Settings Prefabs — that builder
/// has long diverged from these prefabs (it authors 78-unit cards against the
/// shipped 200) and is destructive.
/// </summary>
public static class ItemCardTextBoundsWirer
{
    private static readonly string[] CardPrefabPaths =
    {
        "Assets/Prefabs/Product.prefab",
        "Assets/Prefabs/Service.prefab",
    };

    private const string ColumnName = "NameDesc";
    private const string TitleName = "Name";

    // Reports through the Console rather than EditorUtility.DisplayDialog: a
    // modal blocks the Editor when the entry is driven from the terminal over
    // the mcp-unity bridge, and the Console is already open next to the menu.
    [MenuItem("Tools/BotSettings/Fix Item Card Text Bounds")]
    public static void Fix()
    {
        int cards = Run();
        Debug.Log($"[ItemCardTextBoundsWirer] {cards} card prefab(s) updated — a long product " +
                  "name now truncates at its own column instead of running over the price.");
    }

    /// <summary>Batch entry: Tools/run-editor-builder.sh ItemCardTextBoundsWirer.BuildHeadless</summary>
    public static void BuildHeadless()
    {
        int cards = Run();
        Debug.Log($"[ItemCardTextBoundsWirer] Bounded {cards} card prefab(s).");
        Debug.Log("[ItemCardTextBoundsWirer] Headless build + save complete");
    }

    private static int Run()
    {
        int touched = 0;
        foreach (var path in CardPrefabPaths)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                WireCard(contents, path);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                touched++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        return touched;
    }

    private static void WireCard(GameObject card, string path)
    {
        var column = FindDeep(card.transform, ColumnName);
        if (column == null)
            throw new InvalidOperationException($"{path}: no '{ColumnName}' column — card structure changed.");

        var group = column.GetComponent<VerticalLayoutGroup>();
        if (group == null)
            throw new InvalidOperationException($"{path}: '{ColumnName}' has no VerticalLayoutGroup.");

        // THE fix. Without it the group only aligns the children and their
        // authored width wins, however narrow the column actually is.
        group.childControlWidth = true;
        group.childForceExpandWidth = true;

        var title = FindDeep(column, TitleName)?.GetComponent<TextMeshProUGUI>();
        if (title == null)
            throw new InvalidOperationException($"{path}: no '{TitleName}' label under the column.");

        // One-line title: the box is 60 units tall, so a wrapped second line is
        // unrenderable anyway — truncating fills the line instead of breaking
        // early at a word boundary.
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
