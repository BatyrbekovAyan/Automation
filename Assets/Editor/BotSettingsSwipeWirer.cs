#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Editor-only: wire the BotSettings prefab with an iOS-style left-edge swipe
// detector that drives SwipeToBackBotSettings.
//
// The gesture component cannot live on the BotSettings prefab root — the root
// is the panel that slides, it has no raycast target, and children (ScrollRects,
// TMP_InputFields) would consume drag events before they bubble up. We mirror
// the chat setup (Assets/Scenes/Main.unity: GameObject "SwipeBack" inside the
// chat's MovingArea) by creating a narrow left-edge child GameObject that sits
// on top of the tab content and owns the drag handlers.
//
// Child layout: 150px wide, left-anchored, stretched vertically, invisible
// Image with raycastTarget=true. Sibling index = last so it renders above tab
// content and catches drags first.
//
// BotsPage is intentionally NOT wired here — it lives in Main.unity and cannot
// be serialized on a prefab asset. SwipeToBackBotSettings resolves it at
// runtime via the BotsPage singleton.
//
// Menu: Tools/BotSettings/Wire Swipe Back
public static class BotSettingsSwipeWirer
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string MenuPath = "Tools/BotSettings/Wire Swipe Back";
    private const string SwipeChildName = "SwipeBack";
    private const float SwipeStripWidth = 150f;
    private const float SwipeStripInsetX = 100f;

    [MenuItem(MenuPath)]
    public static void Wire()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogError($"[SwipeWirer] Prefab not found at {PrefabPath}");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var rootRect = root.GetComponent<RectTransform>();
            if (rootRect == null)
            {
                Debug.LogError("[SwipeWirer] BotSettings prefab root has no RectTransform.");
                return;
            }

            // Migration: earlier versions of this wirer put SwipeToBackBotSettings
            // directly on the prefab root. Remove it — the gesture needs the
            // dedicated edge-strip child to receive drag events reliably.
            var rootComponent = root.GetComponent<SwipeToBackBotSettings>();
            if (rootComponent != null)
            {
                Object.DestroyImmediate(rootComponent, true);
                Debug.Log("[SwipeWirer] Removed stale SwipeToBackBotSettings from prefab root.");
            }

            var stripGo = FindOrCreateSwipeStrip(root);
            var stripRect = stripGo.GetComponent<RectTransform>();
            ConfigureStripRect(stripRect);
            ConfigureStripImage(stripGo);

            var component = stripGo.GetComponent<SwipeToBackBotSettings>();
            if (component == null) component = stripGo.AddComponent<SwipeToBackBotSettings>();

            var serialized = new SerializedObject(component);
            serialized.FindProperty("botSettingsPanelToSlide").objectReferenceValue = rootRect;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Ensure the strip renders above tab content so the raycast hits
            // it before the ScrollRect viewport underneath.
            stripGo.transform.SetAsLastSibling();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[SwipeWirer] Wired SwipeToBackBotSettings on {PrefabPath} " +
                      $"(edge-strip child '{SwipeChildName}' @ {SwipeStripWidth}px wide, left-anchored).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        EditorSceneManager.MarkAllScenesDirty();
    }

    private static GameObject FindOrCreateSwipeStrip(GameObject root)
    {
        var existing = root.transform.Find(SwipeChildName);
        if (existing != null) return existing.gameObject;

        var go = new GameObject(SwipeChildName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(root.transform, worldPositionStays: false);
        return go;
    }

    // 150px-wide column anchored to the left edge, stretched vertically.
    // AnchorMin (0,0) / AnchorMax (0,1) with sizeDelta.x = 150 means the strip
    // is always exactly 150px wide regardless of screen size.
    private static void ConfigureStripRect(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(SwipeStripInsetX, 0f);
        rect.sizeDelta = new Vector2(SwipeStripWidth, 0f);
        rect.localScale = Vector3.one;
    }

    // Invisible raycast target. color.a = 0 so nothing renders; raycastTarget
    // = true so the EventSystem still dispatches drag events to this object.
    private static void ConfigureStripImage(GameObject go)
    {
        var image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);
        image.raycastTarget = true;
    }
}
#endif
