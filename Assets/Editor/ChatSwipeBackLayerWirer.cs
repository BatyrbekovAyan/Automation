using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Raises the chat screen's left-edge back-swipe strip (the <see cref="SwipeToBack"/> GameObject)
/// out of MovingArea and into MessagesPanel, immediately after the last content layer.
///
/// WHY: uGUI awards a pointer to the LATER sibling. The strip shipped as MovingArea child [4],
/// under the composer (BottomPanel [5], whose Background is an opaque raycast-target skirt) and
/// under the suggestions slot (SuggestionsPanel, a later sibling of MovingArea itself) — so a
/// back-swipe that began on either of them never reached the strip: over the composer no drag
/// handler existed above it at all and the gesture died; over the slot the cards' ScrollRect
/// claimed it and refused the horizontal direction.
///
/// Raising it INSIDE MovingArea would not fix it. MovingArea rides the keyboard/slot inset
/// (KeyboardAwarePanel), so an open slot translates it — and any strip parented to it — clear of
/// the region that has to be covered. The strip has to be static, which means it has to be a
/// child of MessagesPanel.
///
/// It deliberately stops there: TopBar and the modal overlays (photo/video viewer, attachment
/// preview, emoji picker, reaction bar) stay ABOVE the strip so their own gestures — the header's
/// controls, SwipeToClose, the sheets — are never shadowed. That invariant is asserted, not
/// assumed: a scene whose chrome would end up under the strip is left untouched with an error.
///
/// It also seats the band flush against the screen edge (left edge to x = 0, right edge kept where
/// the author put it). The strip shipped starting 25u in, which is dead space exactly where an
/// iOS-style edge pan begins — and it is the only object that can start a back-swipe at all.
///
/// Additive and idempotent: it moves ONE object and changes only its horizontal band. It creates
/// nothing, destroys nothing, and re-running it after 'Tools/UI/Build Suggestions Panel' (which
/// re-inserts the panel at MovingArea+1) is both safe and the correct thing to do.
///
/// Ordering rule lives in <see cref="SwipeBackLayering"/> so it is unit-tested rather than
/// re-derived here.
/// </summary>
public static class ChatSwipeBackLayerWirer
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    /// <summary>Layers that must keep rendering ABOVE the strip, identified by the component that
    /// owns each one rather than by name — a rename must not silently drop a layer from the
    /// safety check and let the strip swallow the left edge of a media viewer.</summary>
    private static readonly Type[] ChromeAndOverlayMarkers =
    {
        typeof(MessageHeaderView),          // TopBar
        typeof(PhotoViewer),                // PhotoViewerPanel
        typeof(VideoController),            // VideoPlayerPanel
        typeof(AttachmentPreviewScreen),    // AttachmentPreviewScreen
        typeof(EmojiPickerController),      // EmojiPickerOverlay
        typeof(ReactionBarController)       // ReactionBarOverlay
    };

    [MenuItem("Tools/Chat/Raise Swipe-Back Strip Above Composer + Panel")]
    public static void Raise()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[ChatSwipeBackLayerWirer] Edit mode only — a play-mode move is discarded on exit.");
            return;
        }

        var swipe = UnityEngine.Object.FindFirstObjectByType<SwipeToBack>(FindObjectsInactive.Include);
        if (swipe == null)
        {
            Debug.LogError("[ChatSwipeBackLayerWirer] No SwipeToBack in the open scene.");
            return;
        }

        // The panel the strip drives IS the panel it must live in — one serialized ref, no name
        // lookup, and it is already wired because the gesture needs it to slide anything at all.
        var messagesPanel = swipe.chatPanelToSlide;
        if (messagesPanel == null)
        {
            Debug.LogError("[ChatSwipeBackLayerWirer] SwipeToBack.chatPanelToSlide is unwired — " +
                           "nothing identifies the panel the strip belongs to.");
            return;
        }

        var strip = (RectTransform)swipe.transform;

        var movingAreaIndex = DirectChildIndex(messagesPanel, ComposerHost(messagesPanel));
        if (movingAreaIndex == SwipeBackLayering.NotPresent)
        {
            Debug.LogError("[ChatSwipeBackLayerWirer] No MessagesBottomPanel under the chat panel — " +
                           "cannot tell which layer carries the composer.");
            return;
        }

        var panel = UnityEngine.Object.FindFirstObjectByType<SuggestionsPanel>(FindObjectsInactive.Include);
        var panelIndex = panel != null
            ? DirectChildIndex(messagesPanel, panel.transform)
            : SwipeBackLayering.NotPresent;

        // Snapshot BEFORE the move. SetParent(worldPositionStays: false) keeps the serialized
        // anchors, but both parents are full-rect stretches of the same panel, so writing the
        // values back afterwards makes the strip's geometry provably identical either way.
        var anchorMin = strip.anchorMin;
        var anchorMax = strip.anchorMax;
        var anchoredPosition = strip.anchoredPosition;
        var sizeDelta = strip.sizeDelta;
        var pivot = strip.pivot;

        var originalParent = strip.parent;
        var originalIndex = strip.GetSiblingIndex();

        // No Undo grouping, by project convention for scene builders: an Undo-wrapped structural
        // edit is what makes a re-run non-idempotent, and this must be safe to run headlessly.
        var reparented = originalParent != messagesPanel;
        if (reparented)
        {
            strip.SetParent(messagesPanel, worldPositionStays: false);
            strip.anchorMin = anchorMin;
            strip.anchorMax = anchorMax;
            strip.anchoredPosition = anchoredPosition;
            strip.sizeDelta = sizeDelta;
            strip.pivot = pivot;
            strip.localScale = Vector3.one;
            strip.localRotation = Quaternion.identity;
        }

        // Seat the band flush against the screen edge. The strip is the ONLY thing that can start
        // a back-swipe, and it shipped 25u short of the edge — dead space exactly where an
        // iOS-style edge pan begins, so a finger landing on the bezel went to whatever was behind
        // it. Only the LEFT edge moves: the right edge is preserved so widening can never reach
        // further in and newly shadow a composer control.
        var alignedBand = false;
        if (Mathf.Approximately(strip.anchorMin.x, 0f) && Mathf.Approximately(strip.anchorMax.x, 0f))
        {
            SwipeBackLayering.EdgeAlignedBand(
                strip.anchoredPosition.x, strip.sizeDelta.x, strip.pivot.x,
                out var bandX, out var bandWidth);

            if (bandWidth > 0f &&
                (!Mathf.Approximately(bandX, strip.anchoredPosition.x) ||
                 !Mathf.Approximately(bandWidth, strip.sizeDelta.x)))
            {
                strip.anchoredPosition = new Vector2(bandX, strip.anchoredPosition.y);
                strip.sizeDelta = new Vector2(bandWidth, strip.sizeDelta.y);
                alignedBand = true;
            }
        }
        else
        {
            Debug.LogWarning("[ChatSwipeBackLayerWirer] The strip is x-STRETCHED rather than left-anchored, " +
                             "so sizeDelta.x is an inset and not a width — edge alignment skipped. Check by " +
                             "hand that the band still reaches x = 0.");
        }

        // Recomputed after the reparent: it appends the strip last, so any index read before the
        // move describes a hierarchy that no longer exists.
        movingAreaIndex = DirectChildIndex(messagesPanel, ComposerHost(messagesPanel));
        panelIndex = panel != null
            ? DirectChildIndex(messagesPanel, panel.transform)
            : SwipeBackLayering.NotPresent;

        strip.SetSiblingIndex(SwipeBackLayering.TargetSiblingIndex(movingAreaIndex, panelIndex));

        // Verify AFTER the move, never against the pre-move indices: inserting the strip pushes
        // every later sibling — the chrome included — one index down, so a guard that compared the
        // intended index against where the top bar USED to be would refuse the correct placement.
        var stripIndex = strip.GetSiblingIndex();
        movingAreaIndex = DirectChildIndex(messagesPanel, ComposerHost(messagesPanel));
        panelIndex = panel != null
            ? DirectChildIndex(messagesPanel, panel.transform)
            : SwipeBackLayering.NotPresent;
        var lowestChrome = LowestChromeIndex(messagesPanel, strip);

        if (!SwipeBackLayering.OutranksContent(stripIndex, movingAreaIndex, panelIndex) ||
            !SwipeBackLayering.StaysBelowChrome(stripIndex, lowestChrome))
        {
            // Put it back exactly where it was — a half-applied structural edit in a hand-tuned
            // scene is worse than no edit at all.
            strip.SetParent(originalParent, worldPositionStays: false);
            strip.anchorMin = anchorMin;
            strip.anchorMax = anchorMax;
            strip.anchoredPosition = anchoredPosition;
            strip.sizeDelta = sizeDelta;
            strip.pivot = pivot;
            strip.SetSiblingIndex(originalIndex);

            Debug.LogError($"[ChatSwipeBackLayerWirer] ABORTED and reverted: the strip landed at {stripIndex}, " +
                           $"which does not sit above MovingArea [{movingAreaIndex}] and the suggestions panel " +
                           $"[{panelIndex}] while staying below the first chrome/overlay layer [{lowestChrome}]. " +
                           "The scene's child order is not what this expects — fix the order first, then re-run.");
            return;
        }

        EditorUtility.SetDirty(strip);
        EditorSceneManager.MarkSceneDirty(strip.gameObject.scene);
        Selection.activeGameObject = strip.gameObject;

        Debug.Log($"[ChatSwipeBackLayerWirer] '{strip.name}' is now {messagesPanel.name} child " +
                  $"[{strip.GetSiblingIndex()}] — above MovingArea [{movingAreaIndex}] and " +
                  $"SuggestionsPanel [{(panelIndex == SwipeBackLayering.NotPresent ? "absent" : panelIndex.ToString())}], " +
                  $"below the first chrome/overlay layer [{lowestChrome}]. " +
                  $"{(reparented ? "Reparented out of MovingArea." : "Already parented correctly; index re-checked.")} " +
                  $"Band x ∈ [{SwipeBackLayering.BandLeftEdge(strip.anchoredPosition.x, strip.sizeDelta.x, strip.pivot.x):0.##}, " +
                  $"{SwipeBackLayering.BandRightEdge(strip.anchoredPosition.x, strip.sizeDelta.x, strip.pivot.x):0.##}]" +
                  $"{(alignedBand ? " (re-seated flush to the screen edge)." : " (already flush).")} " +
                  "Save the scene.");
    }

    /// <summary>
    /// Headless entry:
    ///   Unity -batchmode -projectPath . -executeMethod ChatSwipeBackLayerWirer.RunHeadless -quit
    /// Exits non-zero on failure so scripts can gate on it.
    /// </summary>
    public static void RunHeadless()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[ChatSwipeBackLayerWirer] {ScenePath} failed to open.");
            EditorApplication.Exit(1);
            return;
        }

        Raise();

        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError("[ChatSwipeBackLayerWirer] scene save FAILED.");
            EditorApplication.Exit(1);
            return;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[ChatSwipeBackLayerWirer] raise + save complete.");
    }

    /// <summary>The composer's own transform — the layer that rides the keyboard inset is whichever
    /// direct child of the chat panel contains it.</summary>
    private static Transform ComposerHost(Transform messagesPanel)
    {
        var bottomPanel = messagesPanel.GetComponentInChildren<MessagesBottomPanel>(true);
        return bottomPanel != null ? bottomPanel.transform : null;
    }

    /// <summary>Index of the direct child of <paramref name="root"/> that contains
    /// <paramref name="descendant"/>, or <see cref="SwipeBackLayering.NotPresent"/>.</summary>
    private static int DirectChildIndex(Transform root, Transform descendant)
    {
        if (root == null || descendant == null) return SwipeBackLayering.NotPresent;

        var walk = descendant;
        while (walk != null && walk.parent != root) walk = walk.parent;
        return walk != null ? walk.GetSiblingIndex() : SwipeBackLayering.NotPresent;
    }

    /// <summary>Lowest sibling index among the chrome/overlay layers the strip must stay under.</summary>
    private static int LowestChromeIndex(Transform messagesPanel, Transform strip)
    {
        var lowest = SwipeBackLayering.NotPresent;

        for (var i = 0; i < messagesPanel.childCount; i++)
        {
            var child = messagesPanel.GetChild(i);
            if (child == strip) continue;

            foreach (var marker in ChromeAndOverlayMarkers)
            {
                if (child.GetComponentInChildren(marker, true) == null) continue;

                var index = child.GetSiblingIndex();
                if (lowest == SwipeBackLayering.NotPresent || index < lowest) lowest = index;
                break;
            }
        }

        return lowest;
    }
}
