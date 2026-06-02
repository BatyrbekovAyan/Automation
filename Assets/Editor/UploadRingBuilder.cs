using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot builder that adds the outgoing video upload progress ring + cancel (X)
/// under playOverlay in MessageTextOutgoing.prefab and wires the MessageItemView
/// serialized refs (uploadRing, cancelButton, cancelIcon). The play triangle is
/// playOverlay's own Image, toggled at runtime — no separate icon ref needed.
/// Idempotent: re-running first removes the prior generated children.
/// Run via: Tools ▸ Chat ▸ Build Upload Ring (Outgoing).
/// </summary>
public static class UploadRingBuilder
{
    private const string PrefabPath = "Assets/Prefabs/MessageTextOutgoing.prefab";

    [MenuItem("Tools/Chat/Build Upload Ring (Outgoing)")]
    public static void Build()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) { Debug.LogError($"[UploadRingBuilder] could not load {PrefabPath}"); return; }

        try
        {
            var view = root.GetComponent<MessageItemView>();
            if (view == null) { Debug.LogError("[UploadRingBuilder] no MessageItemView on prefab root"); return; }

            var so = new SerializedObject(view);
            var overlayGo = so.FindProperty("playOverlay").objectReferenceValue as GameObject;
            if (overlayGo == null) { Debug.LogError("[UploadRingBuilder] playOverlay ref is null on prefab"); return; }
            Transform overlay = overlayGo.transform;

            // Idempotent re-run: drop any previously generated nodes.
            DestroyIfExists(overlay, "UploadRing");
            DestroyIfExists(overlay, "CancelButton");

            // --- radial ring ---
            var ringGo = new GameObject("UploadRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ringGo.transform.SetParent(overlay, false);
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.sizeDelta        = new Vector2(150f, 150f);
            ringRt.anchoredPosition = Vector2.zero;
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.color         = Color.white;
            ringImg.raycastTarget = false;
            ringImg.type          = Image.Type.Filled;
            ringImg.fillMethod    = Image.FillMethod.Radial360;
            ringImg.fillOrigin    = (int)Image.Origin360.Top;
            ringImg.fillClockwise = true;
            ringImg.fillAmount    = 0f;
            ringGo.SetActive(false);   // runtime shows it when a send starts

            // --- cancel button (transparent hit area) + X icon child ---
            var cancelGo = new GameObject("CancelButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            cancelGo.transform.SetParent(overlay, false);
            var cancelRt = (RectTransform)cancelGo.transform;
            cancelRt.sizeDelta        = new Vector2(88f, 88f);   // ≥44dp touch target
            cancelRt.anchoredPosition = Vector2.zero;
            var cancelHit = cancelGo.GetComponent<Image>();
            cancelHit.color         = new Color(0f, 0f, 0f, 0f);  // invisible but raycast-able
            cancelHit.raycastTarget = true;

            var iconGo = new GameObject("CancelIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(cancelGo.transform, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.sizeDelta        = new Vector2(44f, 44f);
            iconRt.anchoredPosition = Vector2.zero;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.color         = Color.white;
            iconImg.raycastTarget = false;
            cancelGo.SetActive(false);

            // --- wire serialized refs ---
            so.FindProperty("uploadRing").objectReferenceValue   = ringImg;
            so.FindProperty("cancelButton").objectReferenceValue = cancelGo.GetComponent<Button>();
            so.FindProperty("cancelIcon").objectReferenceValue   = iconImg;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[UploadRingBuilder] UploadRing + CancelButton added and wired on MessageTextOutgoing.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void DestroyIfExists(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }
}
