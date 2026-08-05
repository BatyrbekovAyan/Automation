using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 3: the approved chats-row type-hierarchy fix, applied to ChatItem.prefab.
///
/// Measured problem: the timestamp was 38u BOLD — the same size as the message
/// preview and heavier than the contact name, so the least important text on the
/// row was drawn the loudest. Name-to-preview was only 44/38 = 1.16x, too little
/// to read as hierarchy.
///
/// Applied here (type only — no geometry, no colour):
///   Name     44u regular -> 46u Bold
///   Message  38u         -> 36u
///   Time     38u Bold    -> 30u regular
/// giving a 1.28x name/preview ratio and a clearly recessive timestamp.
///
/// NOT done here, deliberately:
///   • Divider inset — it is ALREADY inset. Divider's parent is TextBlock, which
///     sits beside Avatar in SwipeContent's horizontal LayoutGroup, so it starts
///     at the text column today. Only its colour changes, via the Hairline token.
///   • Unread badge fill/ink — those live on Theme.Fixed / the theme assets.
///   • Badge height 48u -> 52u — UnreadBadge carries a ContentSizeFitter, so its
///     height is layout-driven; hand-setting sizeDelta would be overwritten.
///
/// Semibold is not available: the SF Pro Text SDF asset ships an EMPTY font-weight
/// table (10 slots, 0 populated), so m_fontWeight is inert and TMP offers only
/// regular or (faux) Bold. Bold is what the approved mock's 600 maps onto here.
/// </summary>
public static class ChatsListRedesign
{
    private const string PrefabPath = "Assets/Prefabs/ChatItem.prefab";

    private static readonly (string path, float size, FontStyles style)[] TypeSpec =
    {
        ("SwipeContent/TextBlock/TopRow/Name",      46f, FontStyles.Bold),
        ("SwipeContent/TextBlock/Message",          36f, FontStyles.Normal),
        ("SwipeContent/TextBlock/TopRow/Time/Text", 30f, FontStyles.Normal),
    };

    [MenuItem("Tools/Theme/Chats List/Redesign — Audit (dry run)")]
    public static void Audit() => Run(apply: false);

    [MenuItem("Tools/Theme/Chats List/Redesign — Apply")]
    public static void Apply() => Run(apply: true);

    private static void Run(bool apply)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[ChatsListRedesign] Could not load {PrefabPath}");
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[ChatsListRedesign] {(apply ? "APPLY" : "AUDIT")} — {PrefabPath}");
            sb.AppendLine($"{"element",-16}{"size",-18}{"style",-22}");
            sb.AppendLine(new string('-', 60));

            bool missing = false;
            foreach (var (path, size, style) in TypeSpec)
            {
                var t = root.transform.Find(path);
                var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
                if (tmp == null)
                {
                    sb.AppendLine($"{System.IO.Path.GetFileName(path),-16}MISSING");
                    missing = true;
                    continue;
                }

                sb.AppendLine($"{tmp.name,-16}{tmp.fontSize,-6} -> {size,-8}{tmp.fontStyle,-10} -> {style}");
                if (!apply) continue;

                var so = new SerializedObject(tmp);
                so.FindProperty("m_fontSize").floatValue = size;
                so.FindProperty("m_fontSizeBase").floatValue = size;
                so.FindProperty("m_fontStyle").intValue = (int)style;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            Debug.Log(sb.ToString());

            if (!apply) return;
            if (missing)
            {
                Debug.LogError("[ChatsListRedesign] Aborted: a spec path was not found. Fix the spec.");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[ChatsListRedesign] Applied. Type only — no geometry, colour or hierarchy touched.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
