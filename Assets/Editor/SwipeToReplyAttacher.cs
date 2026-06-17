#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds the <see cref="SwipeToReply"/> drag handler to the Bubble GameObject of both message
/// prefabs (the same object that carries the bubble background Image and MessageBubbleLongPress),
/// so swipe-right-to-reply works on every spawned bubble. Idempotent.
/// </summary>
public static class SwipeToReplyAttacher
{
    private static readonly string[] Prefabs =
    {
        "Assets/Prefabs/MessageTextIncoming.prefab",
        "Assets/Prefabs/MessageTextOutgoing.prefab",
    };

    [MenuItem("Tools/Chat/Attach Swipe-To-Reply To Both Bubbles")]
    public static void Attach()
    {
        foreach (string path in Prefabs) AttachToPrefab(path);
    }

    private static void AttachToPrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view = root.GetComponent<MessageItemView>();
            if (view == null || view.bubbleBackground == null)
            {
                Debug.LogError($"[SwipeToReply] {path}: no MessageItemView / bubbleBackground — skipped.");
                return;
            }

            GameObject bubbleGo = view.bubbleBackground.gameObject;
            if (bubbleGo.GetComponent<SwipeToReply>() == null)
            {
                bubbleGo.AddComponent<SwipeToReply>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[SwipeToReply] attached to '{bubbleGo.name}' in {path}.");
            }
            else
            {
                Debug.Log($"[SwipeToReply] already present on '{bubbleGo.name}' in {path} — unchanged.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
