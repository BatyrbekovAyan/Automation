using TMPro;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot, idempotent restructure of ChatItem.prefab for swipe-to-delete:
///   ChatItem (root: RectTransform + ChatItemView)
///     ├─ DeleteButton (sibling 0, behind) — red, pinned right, width = RevealWidth
///     └─ SwipeContent (sibling 1, front)  — carries the moved bg Image + tap Button +
///                                            HorizontalLayoutGroup + Avatar + TextBlock,
///                                            plus the SwipeToDelete gesture.
/// Edits the prefab asset via PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset so the
/// prefab GUID is preserved and ChatItemView's serialized refs are rewired in code.
/// Validates structure first and ABORTS WITHOUT SAVING on any surprise. Re-running is a no-op.
/// </summary>
public static class ChatItemSwipeLayerBuilder
{
    const string PrefabPath = "Assets/Prefabs/ChatItem.prefab";
    const float RevealWidth = 150f;

    [MenuItem("Tools/Chat/Add Swipe-to-Delete Layer to ChatItem")]
    public static void Build()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("[ChatItemSwipeLayerBuilder] Could not load " + PrefabPath);
            return;
        }

        bool save = false;
        try
        {
            if (root.transform.Find("SwipeContent") != null)
            {
                Debug.LogWarning("[ChatItemSwipeLayerBuilder] SwipeContent already present — already applied; aborting (no change).");
                return;
            }

            var rootImage = root.GetComponent<Image>();
            var rootButton = root.GetComponent<Button>();
            var rootHlg = root.GetComponent<HorizontalLayoutGroup>();
            var avatar = root.transform.Find("Avatar");
            var textBlock = root.transform.Find("TextBlock");
            var civ = root.GetComponent<ChatItemView>();

            if (rootImage == null || rootButton == null || rootHlg == null ||
                avatar == null || textBlock == null || civ == null)
            {
                Debug.LogError($"[ChatItemSwipeLayerBuilder] Unexpected structure — image={rootImage != null} " +
                               $"button={rootButton != null} hlg={rootHlg != null} avatar={avatar != null} " +
                               $"textBlock={textBlock != null} chatItemView={civ != null}. Prefab NOT modified.");
                return;
            }

            // 1) SwipeContent — stretch-fill the row; will carry the sliding content.
            var swipe = new GameObject("SwipeContent", typeof(RectTransform));
            var swipeRt = (RectTransform)swipe.transform;
            swipeRt.SetParent(root.transform, false);
            swipeRt.anchorMin = Vector2.zero;
            swipeRt.anchorMax = Vector2.one;
            swipeRt.offsetMin = Vector2.zero;
            swipeRt.offsetMax = Vector2.zero;
            swipeRt.pivot = new Vector2(0.5f, 0.5f);

            // Copy the bg Image, tap Button, and HorizontalLayoutGroup from root → SwipeContent.
            ComponentUtility.CopyComponent(rootImage);
            ComponentUtility.PasteComponentAsNew(swipe);
            ComponentUtility.CopyComponent(rootHlg);
            ComponentUtility.PasteComponentAsNew(swipe);
            ComponentUtility.CopyComponent(rootButton);
            ComponentUtility.PasteComponentAsNew(swipe);

            var swipeImage = swipe.GetComponent<Image>();
            var swipeButton = swipe.GetComponent<Button>();
            var swipeHlg = swipe.GetComponent<HorizontalLayoutGroup>();
            if (swipeImage == null || swipeButton == null || swipeHlg == null)
            {
                Debug.LogError($"[ChatItemSwipeLayerBuilder] Component copy failed — image={swipeImage != null} " +
                               $"button={swipeButton != null} hlg={swipeHlg != null}. Prefab NOT modified.");
                return;
            }
            swipeButton.targetGraphic = swipeImage;

            // 2) Move the existing content under SwipeContent (preserves ChatItemView's child refs).
            avatar.SetParent(swipeRt, false);
            textBlock.SetParent(swipeRt, false);

            // 3) Remove the originals from the root (it becomes a plain container).
            Object.DestroyImmediate(rootButton);
            Object.DestroyImmediate(rootHlg);
            Object.DestroyImmediate(rootImage);

            // 4) DeleteButton — behind, pinned to the right edge, width == RevealWidth.
            var del = new GameObject("DeleteButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var delRt = (RectTransform)del.transform;
            delRt.SetParent(root.transform, false);
            delRt.anchorMin = new Vector2(1f, 0f);
            delRt.anchorMax = new Vector2(1f, 1f);
            delRt.pivot = new Vector2(1f, 0.5f);
            delRt.sizeDelta = new Vector2(RevealWidth, 0f);
            delRt.anchoredPosition = Vector2.zero;
            var delImg = del.GetComponent<Image>();
            delImg.color = new Color(0.886f, 0.282f, 0.282f, 1f); // red
            del.GetComponent<Button>().targetGraphic = delImg;

            var label = new GameObject("Label", typeof(RectTransform));
            var labelRt = (RectTransform)label.transform;
            labelRt.SetParent(delRt, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var tmp = label.AddComponent<TextMeshProUGUI>();
            tmp.text = "Удалить";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 30f;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;

            // 5) Z-order: DeleteButton behind (index 0), SwipeContent in front (index 1).
            delRt.SetSiblingIndex(0);
            swipeRt.SetSiblingIndex(1);

            // 6) Gesture component on the sliding layer.
            var swipeComp = swipe.AddComponent<SwipeToDelete>();
            var soSwipe = new SerializedObject(swipeComp);
            var rw = soSwipe.FindProperty("revealWidth");
            if (rw != null)
            {
                rw.floatValue = RevealWidth;
                soSwipe.ApplyModifiedProperties();
            }

            // 7) Rewire ChatItemView's serialized refs (public fields).
            civ.button = swipeButton;
            civ.deleteButton = del.GetComponent<Button>();
            civ.swipeToDelete = swipeComp;
            EditorUtility.SetDirty(civ);

            save = true;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[ChatItemSwipeLayerBuilder] DONE — SwipeContent + DeleteButton added, ChatItemView rewired, prefab saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            if (!save)
                Debug.Log("[ChatItemSwipeLayerBuilder] Finished without saving (no-op or abort).");
        }
    }
}
