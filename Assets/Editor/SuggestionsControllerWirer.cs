using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// [MenuItem] that attaches <see cref="SuggestionsController"/> to the composer host and wires its
/// serialized refs via SerializedObject: panel, toggle, MessagesBottomPanel, the MovingArea's
/// <see cref="KeyboardAwarePanel"/> (the slot-inset target since sketch-003), the composer's
/// <see cref="ComposerSlotKey"/> and the panel's <see cref="SuggestionSlotDragHandle"/> (the 42u grab
/// strip of sketch-005 E). Build-time only, idempotent (reuses an existing controller).
/// Run AFTER 'Tools/UI/Build Suggestions Panel'. Pure Editor wiring tool — no networking.
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

        // The slot swap drives the MovingArea's rider — the composer's own parent chain owns it
        // (the AttachmentPreviewScreen has a separate KeyboardAwarePanel; parent lookup can
        // never confuse the two).
        var keyboardMover = bottomPanel.GetComponentInParent<KeyboardAwarePanel>(true);
        if (keyboardMover == null)
        {
            Debug.LogError("SuggestionsControllerWirer: no KeyboardAwarePanel above MessagesBottomPanel (MovingArea).");
            return;
        }

        // The ✦⇄⌨ key lives inside the composer's input field (built by SuggestionsPanelBuilder).
        var slotKey = bottomPanel.GetComponentInChildren<ComposerSlotKey>(true);
        if (slotKey == null)
        {
            Debug.LogError("SuggestionsControllerWirer: no ComposerSlotKey under MessagesBottomPanel — " +
                           "run 'Tools/UI/Build Suggestions Panel' with the current builder first.");
            return;
        }

        // The 42u grab strip lives in the panel's chrome (built by SuggestionsPanelBuilder). Searched
        // with includeInactive — the builder parks the rebuilt panel INACTIVE, so an active-only walk
        // finds nothing.
        //
        // Deliberately NOT fatal, unlike every dependency above it: this wirer runs straight after the
        // builder has already destroyed and rebuilt the panel, so a hard return here would leave
        // _panel/_slotKey at {fileID: 0} — and the controller's null guards make that failure SILENT at
        // play time. A scene whose panel predates the drag-handle chrome must still come out fully
        // wired; only the handle is missing, and the warning says so.
        var dragHandle = panel.GetComponentInChildren<SuggestionSlotDragHandle>(true);
        if (dragHandle == null)
        {
            Debug.LogWarning("SuggestionsControllerWirer: no SuggestionSlotDragHandle under the suggestions " +
                             "panel — the 3-detent grab strip will be dead (sketch-005 E rule 6). Re-run " +
                             "'Tools/UI/Build Suggestions Panel' with the current builder. Wiring the rest anyway.");
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
        so.FindProperty("_keyboardMover").objectReferenceValue = keyboardMover;
        so.FindProperty("_slotKey").objectReferenceValue = slotKey;
        // Written even when null: a rebuild that dropped the strip must clear the stale ref, never
        // leave the controller pointing at a handle the scene no longer has.
        //
        // Guarded exactly like the builder's own tintCircle stamp: FindProperty returns null for a
        // field the COMPILED class does not have, and the two halves of this ref live in SEPARATE
        // runtime files (SuggestionSlotDragHandle.cs carries the type this Editor script compiles
        // against; SuggestionsController.cs carries the _dragHandle field), so a partial checkout /
        // revert can leave the type present and the field gone. This script would still compile, and
        // an unguarded NRE on the line below would abort BEFORE ApplyModifiedPropertiesWithoutUndo —
        // stranding _panel/_slotKey and every ref above it at {fileID: 0}, which is precisely the
        // silent play-time failure the non-fatal handle lookup above exists to prevent.
        SerializedProperty handleProp = so.FindProperty("_dragHandle");
        if (handleProp != null)
            handleProp.objectReferenceValue = dragHandle;
        else
            Debug.LogWarning("SuggestionsControllerWirer: the compiled SuggestionsController has no " +
                             "_dragHandle field (pre-005-E runtime) — the grab strip stays unwired. " +
                             "Everything else was stamped.");
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = controller.gameObject;
        // The ref list names only what was actually written — a handle that was not found (or a field
        // that does not exist) must not be reported as wired.
        bool handleStamped = handleProp != null && dragHandle != null;
        Debug.Log("SuggestionsControllerWirer: wired SuggestionsController (panel, toggle, bottomPanel, " +
                  $"keyboardMover, slotKey{(handleStamped ? ", dragHandle" : "")}).");
    }
}
