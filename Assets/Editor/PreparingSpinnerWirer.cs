#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click wiring for VideoController.preparingSpinner. Steals the exact spinner subtree the chat
/// bubbles use (MessageItemView.loadingSpinner — the "Loading" card + its animated "Spinner" child)
/// from MessageTextIncoming.prefab, reparents it under the fullscreen VideoPlayerPanel, centres it,
/// disables raycasts, sets it inactive (the VideoController code owns the toggle), and assigns the
/// serialized field. Self-cleaning + idempotent: removes any prior PreparingSpinner and any stray
/// bubble (a child carrying MessageItemView) accidentally parented under the panel, then rebuilds.
/// </summary>
public static class PreparingSpinnerWirer
{
    private const string PrefabPath = "Assets/Prefabs/MessageTextIncoming.prefab";
    private const string ChildName  = "PreparingSpinner";

    [MenuItem("Tools/Chat/Wire Video Preparing Spinner")]
    public static void Wire()
    {
        var controller = Object.FindFirstObjectByType<VideoController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            Debug.LogError("[SpinnerWirer] No VideoController in the open scene — open Main.unity first.");
            return;
        }
        Transform panel = controller.transform;

        // --- Clean up any prior/failed run: existing spinner + any stray bubble under the panel. ---
        for (int i = panel.childCount - 1; i >= 0; i--)
        {
            Transform c = panel.GetChild(i);
            bool isOldSpinner = c.name == ChildName;
            bool isStrayBubble = c.GetComponent<MessageItemView>() != null
                              || c.GetComponentInChildren<MessageItemView>(true) != null;
            if (isOldSpinner || isStrayBubble)
            {
                Debug.Log($"[SpinnerWirer] Cleaning up '{c.name}' under {panel.name}.");
                Object.DestroyImmediate(c.gameObject);
            }
        }

        // --- Instantiate the WHOLE prefab (root), steal its loadingSpinner subtree, drop the rest. ---
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError($"[SpinnerWirer] Prefab not found: {PrefabPath}"); return; }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance == null) { Debug.LogError("[SpinnerWirer] Failed to instantiate prefab."); return; }
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        var miv = instance.GetComponent<MessageItemView>() ?? instance.GetComponentInChildren<MessageItemView>(true);
        GameObject spinner = miv != null ? miv.loadingSpinner : null;
        if (spinner == null)
        {
            Debug.LogError("[SpinnerWirer] Could not resolve MessageItemView.loadingSpinner on the prefab instance.");
            Object.DestroyImmediate(instance);
            return;
        }

        spinner.transform.SetParent(panel, false);   // move the spinner out of the bubble
        spinner.name = ChildName;
        Object.DestroyImmediate(instance);            // discard the rest of the bubble
        spinner.layer = controller.gameObject.layer;

        // The bubble spinner's white card exists only to cover the download-button placeholder; over
        // the black fullscreen video it reads as an ugly white box. Drop the root card Image (and its
        // now-inert LayoutElement) so only the animated Spinner child shows over the video.
        var cardImage = spinner.GetComponent<Image>();
        if (cardImage != null) Object.DestroyImmediate(cardImage);
        var cardLayout = spinner.GetComponent<LayoutElement>();
        if (cardLayout != null) Object.DestroyImmediate(cardLayout);

        // The ring sprite is white but the bubble tints it black to sit on the white card; over the
        // black fullscreen video a black ring is invisible. Tint it white here (this clone only —
        // the bubble prefab keeps its black ring).
        var ring = spinner.transform.Find("Spinner");
        if (ring != null && ring.TryGetComponent<Image>(out var ringImage))
            ringImage.color = Color.white;
        else
            Debug.LogWarning("[SpinnerWirer] 'Spinner' ring child not found — fullscreen ring keeps the bubble tint.");

        // --- Centre over the (black-while-loading) VideoScreen, render on top. ---
        var rt = spinner.GetComponent<RectTransform>();
        if (rt == null) rt = spinner.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.SetAsLastSibling();

        // Never eat taps — Close button + swipe-to-close must stay reachable.
        foreach (var g in spinner.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

        spinner.SetActive(false); // PlayVideo shows it; OnPrepareCompleted/FailPlayback/CloseVideo hide it.

        // --- Assign the private [SerializeField] preparingSpinner. ---
        var so = new SerializedObject(controller);
        var prop = so.FindProperty("preparingSpinner");
        if (prop == null) { Debug.LogError("[SpinnerWirer] VideoController has no serialized 'preparingSpinner' field."); return; }
        prop.objectReferenceValue = spinner;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(spinner);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = spinner;
        Debug.Log($"[SpinnerWirer] OK — '{ChildName}' wired under '{panel.name}'; VideoController.preparingSpinner assigned. Save the scene to persist.");
    }
}
#endif
