#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bakes the bot-level DeleteConfirmPopup into BotSettings.prefab and wires
/// the serialized delete* fields on the BotSettings component:
///   - deleteBotButton   : auto-resolved from the existing "DeleteBotButton"
///                         GameObject under General > Viewport > Content.
///   - deleteConfirmPopup: newly created sibling of the prefab root, named
///                         "DeleteBotConfirmPopup" to avoid collision with
///                         the ProductEditSheet / ServiceEditSheet popups
///                         already called "DeleteConfirmPopup".
///   - deleteConfirmButton / deleteCancelButton.
///
/// Mirrors BotSettingsConfirmChangePopupBuilder — safe to re-run.
/// </summary>
public static class BotSettingsDeleteBotPopupBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string PopupGoName = "DeleteBotConfirmPopup";

    [MenuItem("Tools/BotSettings/Build Delete-Bot Popup")]
    public static void Build() => Run(force: false);

    [MenuItem("Tools/BotSettings/Rebuild Delete-Bot Popup (Overwrite)")]
    public static void Rebuild() => Run(force: true);

    private static void Run(bool force)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[BotSettings] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var settings = prefabRoot.GetComponent<BotSettings>();
            if (settings == null)
            {
                Debug.LogError("[BotSettings] BotSettings component not found on prefab root.");
                return;
            }

            var so = new SerializedObject(settings);
            bool modified = false;

            modified |= WireDeleteBotButton(so, prefabRoot);
            modified |= BuildDeleteConfirmPopup(so, prefabRoot, force);

            if (modified)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log($"[BotSettings] Delete-Bot popup baked into {PrefabPath}");
            }
            else
            {
                Debug.Log("[BotSettings] Delete-Bot popup already up to date.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // Locate the existing "DeleteBotButton" GameObject and assign it to the
    // deleteBotButton serialized slot if empty.
    private static bool WireDeleteBotButton(SerializedObject so, GameObject prefabRoot)
    {
        var sp = so.FindProperty("deleteBotButton");
        if (sp == null)
        {
            Debug.LogError("[BotSettings] SerializedProperty 'deleteBotButton' not found.");
            return false;
        }
        if (sp.objectReferenceValue != null) return false;

        var found = FindInChildren(prefabRoot.transform, "DeleteBotButton");
        if (found == null)
        {
            Debug.LogWarning("[BotSettings] 'DeleteBotButton' GameObject not found in prefab — skipping.");
            return false;
        }
        var btn = found.GetComponent<Button>();
        if (btn == null) btn = found.gameObject.AddComponent<Button>();
        sp.objectReferenceValue = btn;
        return true;
    }

    private static bool BuildDeleteConfirmPopup(SerializedObject so, GameObject prefabRoot, bool force)
    {
        var popupSp = so.FindProperty("deleteConfirmPopup");
        var confirmSp = so.FindProperty("deleteConfirmButton");
        var cancelSp = so.FindProperty("deleteCancelButton");
        if (popupSp == null || confirmSp == null || cancelSp == null)
        {
            Debug.LogError("[BotSettings] Delete popup SerializedProperties not found. " +
                           "Check that BotSettings.cs has [SerializeField] private fields named " +
                           "deleteConfirmPopup, deleteConfirmButton, deleteCancelButton.");
            return false;
        }

        var existing = popupSp.objectReferenceValue as GameObject;
        if (existing != null)
        {
            if (!force) return false;
            Object.DestroyImmediate(existing);
            popupSp.objectReferenceValue = null;
        }

        // Sweep any stray "DeleteBotConfirmPopup" sibling that may have been
        // orphaned by a previous build losing its SerializedField reference.
        for (int i = prefabRoot.transform.childCount - 1; i >= 0; i--)
        {
            var child = prefabRoot.transform.GetChild(i);
            if (child.name == PopupGoName) Object.DestroyImmediate(child.gameObject);
        }

        var (popup, confirmBtn, cancelBtn) = CreatePopup(prefabRoot);
        popupSp.objectReferenceValue = popup;
        confirmSp.objectReferenceValue = confirmBtn;
        cancelSp.objectReferenceValue = cancelBtn;
        return true;
    }

    private static (GameObject popup, Button confirm, Button cancel) CreatePopup(GameObject prefabRoot)
    {
        var popup = new GameObject(PopupGoName, typeof(RectTransform), typeof(Image), typeof(Button));
        popup.transform.SetParent(prefabRoot.transform, worldPositionStays: false);
        var prt = (RectTransform)popup.transform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        prt.SetAsLastSibling();

        // Backdrop starts transparent; PopupUI.Show fades it in.
        popup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        // Card must be named "Content" so PopupUI.Show finds it for the
        // fade/scale animation.
        var card = new GameObject("Content", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(popup.transform, worldPositionStays: false);
        var crt = (RectTransform)card.transform;
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(960f, 660f);
        crt.anchoredPosition = Vector2.zero;
        card.GetComponent<Image>().color = Color.white;

        TryAddRoundedCorners(card, radius: 48f);
        card.AddComponent<EventAbsorber>();

        AddText(card, "Title", "Удалить бота?", 54f, bold: true,
            color: new Color(0.11f, 0.11f, 0.12f),
            anchorTopPivot: new Vector2(0.5f, 1f),
            anchoredPosition: new Vector2(0f, -72f),
            sizeDelta: new Vector2(-96f, 84f));

        AddText(card, "Body", "Это действие необратимо. Все данные бота будут удалены.",
            42f, bold: false,
            color: new Color(0.39f, 0.39f, 0.4f),
            anchorTopPivot: new Vector2(0.5f, 1f),
            anchoredPosition: new Vector2(0f, -204f),
            sizeDelta: new Vector2(-96f, 220f));

        var cancelBtn = BuildButton(card, "CancelButton", "Отмена",
            isPrimary: false, anchorX: 0.28f);
        var confirmBtn = BuildButton(card, "ConfirmButton", "Удалить",
            isPrimary: true, anchorX: 0.72f);

        popup.SetActive(false);
        return (popup, confirmBtn, cancelBtn);
    }

    private static Transform FindInChildren(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindInChildren(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static void AddText(GameObject parent, string goName, string text,
        float fontSize, bool bold, Color color,
        Vector2 anchorTopPivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent.transform, worldPositionStays: false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, anchorTopPivot.y);
        rt.anchorMax = new Vector2(1f, anchorTopPivot.y);
        rt.pivot = anchorTopPivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;
    }

    private static Button BuildButton(GameObject parent, string goName, string label,
        bool isPrimary, float anchorX)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, worldPositionStays: false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(anchorX, 0f);
        rt.anchorMax = new Vector2(anchorX, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 60f);
        rt.sizeDelta = new Vector2(384f, 144f);

        var img = go.GetComponent<Image>();
        img.color = isPrimary
            ? new Color(0.92f, 0.27f, 0.27f)    // destructive red
            : new Color(0.94f, 0.94f, 0.95f);   // neutral

        TryAddRoundedCorners(go, radius: 36f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, worldPositionStays: false);
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 45f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = isPrimary ? Color.white : new Color(0.11f, 0.11f, 0.12f);
        tmp.raycastTarget = false;

        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        return btn;
    }

    private static void TryAddRoundedCorners(GameObject target, float radius)
    {
        var rcType = System.Type.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners, Assembly-CSharp")
                     ?? System.Type.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners");
        if (rcType == null) return;

        var rc = target.AddComponent(rcType) as MonoBehaviour;
        if (rc == null) return;

        var radiusField = rcType.GetField("radius");
        if (radiusField != null) radiusField.SetValue(rc, radius);
        var imgField = rcType.GetField("image");
        if (imgField != null) imgField.SetValue(rc, target.GetComponent<Image>());
    }
}
#endif
