using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [MenuItem] that attaches <see cref="SuggestionsController"/> to the composer host and wires its
/// serialized refs (panel, toggle, MessagesBottomPanel) via SerializedObject; also stamps the
/// sheet grab-handle's controller ref and repurposes the parked composer MicButton as the
/// suggestions-sheet toggle (owner request 2026-08-07). Build-time only, idempotent (reuses an
/// existing controller). Pure Editor wiring tool — no networking.
/// </summary>
public static class SuggestionsControllerWirer
{
    [MenuItem("Tools/UI/Wire Suggestions Controller")]
    public static void Wire()
    {
        // The panel/toggle live under the (inactive) WhatsApp screen — include inactive in the search.
        var panel = Object.FindFirstObjectByType<SuggestionsPanel>(FindObjectsInactive.Include);
        var toggle = Object.FindFirstObjectByType<SemiAutoToggle>(FindObjectsInactive.Include);
        var bottomPanel = Object.FindFirstObjectByType<MessagesBottomPanel>(FindObjectsInactive.Include);

        if (panel == null || toggle == null || bottomPanel == null)
        {
            Debug.LogError("SuggestionsControllerWirer: missing dependency — " +
                           $"panel:{panel != null} toggle:{toggle != null} bottomPanel:{bottomPanel != null}. " +
                           "Run 'Tools/UI/Build Suggestions Panel' first.");
            return;
        }

        // The composer's ExpandableInput lives on the same GameObject as MessagesBottomPanel —
        // the controller drives it so the messages make room + the panel rides the composer top.
        var expandable = bottomPanel.GetComponent<ExpandableInput>();
        if (expandable == null)
        {
            Debug.LogError("SuggestionsControllerWirer: MessagesBottomPanel has no ExpandableInput component.");
            return;
        }

        // Host the controller on the composer it drives (active while a chat is open, so OnEnable/
        // OnDisable track the live-message subscription correctly). Reuse an existing one.
        var controller = Object.FindFirstObjectByType<SuggestionsController>(FindObjectsInactive.Include);
        if (controller == null)
            controller = bottomPanel.gameObject.AddComponent<SuggestionsController>();

        var so = new SerializedObject(controller);
        so.FindProperty("_panel").objectReferenceValue = panel;
        so.FindProperty("_toggle").objectReferenceValue = toggle;
        so.FindProperty("_bottomPanel").objectReferenceValue = bottomPanel;
        so.FindProperty("_expandableInput").objectReferenceValue = expandable;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Grab-handle close routes through the controller (so the message-list floor follows).
        var dragHandle = panel.GetComponentInChildren<SheetDragHandle>(true);
        if (dragHandle != null)
        {
            var hso = new SerializedObject(dragHandle);
            hso.FindProperty("controller").objectReferenceValue = controller;
            hso.ApplyModifiedPropertiesWithoutUndo();
        }
        else Debug.LogWarning("SuggestionsControllerWirer: no SheetDragHandle under the panel — " +
                              "run 'Tools/UI/Build Suggestions Panel' with the current builder first.");

        WireSheetToggleButton(controller, bottomPanel.transform);

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = controller.gameObject;
        Debug.Log("SuggestionsControllerWirer: wired SuggestionsController (panel, toggle, bottomPanel, " +
                  "grab handle, sheet-toggle button).");
    }

    // Repurpose the parked (inactive) composer MicButton: activate it, swap its glyph for the
    // suggestions ✦ sparkle (InkSecondary via ThemedColor), and wire onClick → ToggleSheet as a
    // persistent listener. Idempotent: skips the listener if already wired.
    private static void WireSheetToggleButton(SuggestionsController controller, Transform bottomPanel)
    {
        Transform mic = FindChildRecursive(bottomPanel, "MicButton");
        if (mic == null)
        {
            Debug.LogWarning("SuggestionsControllerWirer: MicButton not found under MessagesBottomPanel — " +
                             "sheet-toggle button not wired.");
            return;
        }

        mic.gameObject.SetActive(true);
        Button button = mic.GetComponent<Button>();
        if (button == null) button = mic.gameObject.AddComponent<Button>();

        // Deepest Image is the glyph (root-first order from GetComponentsInChildren).
        Image[] images = mic.GetComponentsInChildren<Image>(true);
        Image glyph = images.Length > 0 ? images[images.Length - 1] : null;
        if (glyph != null)
        {
            var sparkle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Suggestions/suggest_sparkle.png");
            if (sparkle != null) glyph.sprite = sparkle;
            glyph.preserveAspect = true;
            var themed = glyph.GetComponent<ThemedColor>();
            if (themed == null) themed = glyph.gameObject.AddComponent<ThemedColor>();
            themed.Configure(ThemeRole.InkSecondary, glyph);
            glyph.color = new Color(Theme.Color(ThemeRole.InkSecondary).r, Theme.Color(ThemeRole.InkSecondary).g,
                Theme.Color(ThemeRole.InkSecondary).b, glyph.color.a);   // show the design without play mode
            EditorUtility.SetDirty(themed);
        }

        bool alreadyWired = false;
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            if (button.onClick.GetPersistentMethodName(i) == nameof(SuggestionsController.ToggleSheet))
                alreadyWired = true;
        if (!alreadyWired)
            UnityEventTools.AddVoidPersistentListener(button.onClick, controller.ToggleSheet);

        EditorUtility.SetDirty(mic.gameObject);
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child != parent && child.name == name) return child;
        return null;
    }
}
