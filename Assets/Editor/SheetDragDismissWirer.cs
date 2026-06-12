#if UNITY_EDITOR
using Automation.BotSettingsUI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Surgically adds a drag-to-dismiss zone over the grabber strip of both
/// bottom sheets (Sheet_BotSwitcher and AttachSheet) without rebuilding them.
/// Idempotent — re-run after any sheet rebuild (Tools/Bot Switcher/Build Sheet
/// or the attach sheet builder), which drops the zone.
/// Spec: docs/superpowers/specs/2026-06-12-sheet-drag-dismiss-design.md
/// </summary>
public static class SheetDragDismissWirer
{
    private const string ZoneName = "DragZone";
    private const string GrabberName = "Grabber";
    private const string BotSettingsPrefabPath = "Assets/Prefabs/BotSettings.prefab";

    // Zone heights in 1080x1920 reference units: the full dead strip above
    // each sheet's content (grabber area + title / grabber area + row gap).
    private const float BotSwitcherZoneHeight = 172f;
    private const float AttachZoneHeight = 96f;
    // ItemEditSheet: grabber strip (60, pre-existing padding above the title)
    // + title block (100); the Fields container starts at 190.
    private const float ItemEditZoneHeight = 160f;

    // Grabber pill added to the edit sheets — same metrics as the bot
    // switcher's and attach sheet's pills.
    private const float GrabberWidth = 108f;
    private const float GrabberHeight = 12f;
    private const float GrabberTopOffset = 24f;
    private static readonly Color GrabberColor = new Color(0.78f, 0.78f, 0.80f);

    [MenuItem("Tools/Sheets/Wire Drag Dismiss")]
    public static void Wire()
    {
        int wired = 0;

        var botSheet = Object.FindFirstObjectByType<BotSwitcherSheet>(FindObjectsInactive.Include);
        if (botSheet != null)
        {
            var so = new SerializedObject(botSheet);
            var panel = so.FindProperty("sheetPanel").objectReferenceValue as RectTransform;
            var backdrop = so.FindProperty("backdropGroup").objectReferenceValue as CanvasGroup;
            if (panel != null)
            {
                WireZone(panel, backdrop, BotSwitcherZoneHeight, botSheet.Close);
                EditorSceneManager.MarkSceneDirty(botSheet.gameObject.scene);
                wired++;
            }
            else
            {
                Debug.LogError("[SheetDragDismissWirer] BotSwitcherSheet.sheetPanel is not wired — run Tools/Bot Switcher/Build Sheet first.");
            }
        }
        else
        {
            Debug.LogError("[SheetDragDismissWirer] No BotSwitcherSheet in the scene — run Tools/Bot Switcher/Build Sheet first.");
        }

        var attachSheet = Object.FindFirstObjectByType<AttachSheet>(FindObjectsInactive.Include);
        if (attachSheet != null)
        {
            var so = new SerializedObject(attachSheet);
            var backdrop = so.FindProperty("backdropGroup").objectReferenceValue as CanvasGroup;
            WireZone((RectTransform)attachSheet.transform, backdrop, AttachZoneHeight, attachSheet.Close);
            EditorSceneManager.MarkSceneDirty(attachSheet.gameObject.scene);
            wired++;
        }
        else
        {
            Debug.LogError("[SheetDragDismissWirer] No AttachSheet in the scene — run its builder first.");
        }

        wired += WireItemEditSheetsInPrefab();

        Debug.Log($"[SheetDragDismissWirer] Wired drag-to-dismiss on {wired}/4 sheet(s).");
    }

    /// <summary>
    /// Product/Service edit sheets live inside BotSettings.prefab, not the
    /// scene — edit the prefab asset so every instance inherits the zone.
    /// These sheets have no grabber pill at all, so one is added into the
    /// pre-existing 60-unit padding above the title. onDismiss targets
    /// Dismiss() (not Hide()) to keep the discard-new-card semantics of a
    /// scrim tap.
    /// </summary>
    private static int WireItemEditSheetsInPrefab()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(BotSettingsPrefabPath);
        if (contents == null)
        {
            Debug.LogError($"[SheetDragDismissWirer] Could not load {BotSettingsPrefabPath}.");
            return 0;
        }

        int wired = 0;
        try
        {
            foreach (var editSheet in contents.GetComponentsInChildren<ItemEditSheet>(includeInactive: true))
            {
                var so = new SerializedObject(editSheet);
                var panel = so.FindProperty("sheetRoot").objectReferenceValue as RectTransform;
                var backdrop = so.FindProperty("scrimBehindGroup").objectReferenceValue as CanvasGroup;
                if (panel == null)
                {
                    Debug.LogError($"[SheetDragDismissWirer] {editSheet.name}.sheetRoot is not wired — skipped.");
                    continue;
                }

                AddGrabberPill(panel);
                WireZone(panel, backdrop, ItemEditZoneHeight, editSheet.Dismiss);
                wired++;
            }

            if (wired > 0) PrefabUtility.SaveAsPrefabAsset(contents, BotSettingsPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        return wired;
    }

    private static void AddGrabberPill(RectTransform panel)
    {
        Transform existing = panel.Find(GrabberName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var pill = new GameObject(GrabberName, typeof(RectTransform), typeof(Image));
        pill.layer = panel.gameObject.layer;
        pill.transform.SetParent(panel, false);

        var rt = pill.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -GrabberTopOffset);
        rt.sizeDelta = new Vector2(GrabberWidth, GrabberHeight);

        var image = pill.GetComponent<Image>();
        image.color = GrabberColor;
        image.raycastTarget = false;

        var rounded = pill.AddComponent<Nobi.UiRoundedCorners.ImageWithRoundedCorners>();
        rounded.radius = GrabberHeight * 0.5f;
        rounded.Validate();
        rounded.Refresh();
    }

    /// <summary>
    /// Creates the transparent DragZone as the panel's LAST child so it wins
    /// raycasts over the grabber/title beneath it. LayoutElement.ignoreLayout
    /// keeps the bot switcher panel's VerticalLayoutGroup from flowing it.
    /// </summary>
    private static void WireZone(RectTransform panel, CanvasGroup backdrop, float zoneHeight, UnityAction closeAction)
    {
        Transform existing = panel.Find(ZoneName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var zone = new GameObject(ZoneName,
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        zone.layer = panel.gameObject.layer;
        zone.transform.SetParent(panel, false);
        zone.transform.SetAsLastSibling();

        zone.GetComponent<LayoutElement>().ignoreLayout = true;

        var rt = zone.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, zoneHeight);

        var image = zone.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;

        var drag = zone.AddComponent<SheetDragDismiss>();
        var so = new SerializedObject(drag);
        so.FindProperty("panel").objectReferenceValue = panel;
        so.FindProperty("backdropGroup").objectReferenceValue = backdrop;
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddVoidPersistentListener(drag.onDismiss, closeAction);
        EditorUtility.SetDirty(zone);
    }
}
#endif
