using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Idempotent prefab tweak for the swipe-to-delete reveal: makes the DeleteButton SQUARE
/// (width = row height), parks it off-screen to the right at rest, and wires it to
/// SwipeToDelete so it slides in alongside the swipe. Run after the reveal layer exists.
/// </summary>
public static class ChatItemDeleteButtonTweakBuilder
{
    const string PrefabPath = "Assets/Prefabs/ChatItem.prefab";

    [MenuItem("Tools/Chat/Tweak Delete Button (square + slide-in)")]
    public static void Build()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("[ChatItemDeleteButtonTweakBuilder] Could not load " + PrefabPath);
            return;
        }

        bool save = false;
        try
        {
            var swipe = root.transform.Find("SwipeContent");
            var del = root.transform.Find("DeleteButton") as RectTransform;
            var swipeComp = swipe != null ? swipe.GetComponent<SwipeToDelete>() : null;
            if (swipe == null || del == null || swipeComp == null)
            {
                Debug.LogError($"[ChatItemDeleteButtonTweakBuilder] Unexpected structure — " +
                               $"swipeContent={swipe != null} deleteButton={del != null} swipeToDelete={swipeComp != null}. " +
                               "Prefab NOT modified.");
                return;
            }

            // Row height — drives the square size.
            float rowH = ((RectTransform)root.transform).rect.height;
            if (rowH < 1f) rowH = ((RectTransform)root.transform).sizeDelta.y;
            var rootLE = root.GetComponent<LayoutElement>();
            if (rowH < 1f && rootLE != null) rowH = rootLE.preferredHeight;
            if (rowH < 1f) rowH = 200f;
            float square = rowH;

            // Square, full-height, pinned to the right edge, parked off-screen to the right at rest.
            del.anchorMin = new Vector2(1f, 0f);
            del.anchorMax = new Vector2(1f, 1f);
            del.pivot = new Vector2(1f, 0.5f);
            del.sizeDelta = new Vector2(square, 0f);
            del.anchoredPosition = new Vector2(square, 0f);

            var so = new SerializedObject(swipeComp);
            so.FindProperty("revealWidth").floatValue = square;
            so.FindProperty("deleteButton").objectReferenceValue = del;
            so.ApplyModifiedProperties();

            save = true;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[ChatItemDeleteButtonTweakBuilder] DONE — square={square}px, off-screen at rest, " +
                      "deleteButton wired to SwipeToDelete; prefab saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            if (!save) Debug.Log("[ChatItemDeleteButtonTweakBuilder] Finished without saving (abort).");
        }
    }
}
