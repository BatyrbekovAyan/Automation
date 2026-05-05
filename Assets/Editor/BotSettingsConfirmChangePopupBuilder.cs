#if UNITY_EDITOR
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor maintenance for BotSettings.prefab:
///   1. Bakes ConfirmChangeWhatsappNumberPopup / ConfirmChangeTelegramNumberPopup
///      with confirm+cancel buttons wired to the serialized references.
///   2. Replaces the WhatsappNumberField / TelegramNumberField EditableFields
///      (which contain a TMP_InputField) with NumberDisplayField components
///      — a card-shaped Button showing a centered bold number label and
///      nothing else.
///
/// Skips anything already converted; safe to re-run.
/// </summary>
public static class BotSettingsConfirmChangePopupBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";

    [MenuItem("Tools/BotSettings/Build Confirm-Change Popups")]
    public static void Build() => Run(force: false);

    [MenuItem("Tools/BotSettings/Rebuild Confirm-Change Popups (Overwrite)")]
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

            modified |= BuildPopup(so, prefabRoot, force,
                popupProp: "ConfirmChangeWhatsappNumberPopup",
                confirmBtnProp: "ConfirmChangeWhatsappNumberButton",
                cancelBtnProp: "CancelChangeWhatsappNumberButton",
                goName: "ConfirmChangeWhatsappNumberPopup",
                title: "Сменить номер WhatsApp?",
                body: "Текущий номер будет отключён от аккаунта.");

            modified |= BuildPopup(so, prefabRoot, force,
                popupProp: "ConfirmChangeTelegramNumberPopup",
                confirmBtnProp: "ConfirmChangeTelegramNumberButton",
                cancelBtnProp: "CancelChangeTelegramNumberButton",
                goName: "ConfirmChangeTelegramNumberPopup",
                title: "Сменить номер Telegram?",
                body: "Текущий номер будет отключён от аккаунта.");

            modified |= ConvertNumberFieldIfNeeded(so, "WhatsappNumberField");
            modified |= ConvertNumberFieldIfNeeded(so, "TelegramNumberField");

            if (modified)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log($"[BotSettings] Prefab updated at {PrefabPath}");
            }
            else
            {
                Debug.Log("[BotSettings] Nothing to do — prefab already up to date.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // Swaps the EditableField on a number-field GameObject for a
    // NumberDisplayField, strips the Label + TMP_InputField children, adds
    // a centered TMP number label, and ensures the card has a Button.
    private static bool ConvertNumberFieldIfNeeded(SerializedObject settingsSo, string propertyName)
    {
        var fieldProp = settingsSo.FindProperty(propertyName);
        if (fieldProp == null || fieldProp.objectReferenceValue == null) return false;

        // Already a NumberDisplayField — nothing to do.
        if (fieldProp.objectReferenceValue is NumberDisplayField) return false;

        var existing = fieldProp.objectReferenceValue as EditableField;
        if (existing == null) return false;

        var go = existing.gameObject;

        // Drop Label + Input children; NumberDisplayField shows the value
        // through its own displayText, nothing else.
        for (int i = go.transform.childCount - 1; i >= 0; i--)
        {
            var child = go.transform.GetChild(i);
            if (child.name == "Label" || child.name == "Input")
                Object.DestroyImmediate(child.gameObject);
        }

        // Replace EditableField with NumberDisplayField (same GO, same
        // serialized reference slot).
        Object.DestroyImmediate(existing);
        var display = go.AddComponent<NumberDisplayField>();

        // Centered bold number label.
        var textGo = new GameObject("NumberLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, worldPositionStays: false);
        var trt = (RectTransform)textGo.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 17f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.11f, 0.11f, 0.12f);
        tmp.raycastTarget = false;
        tmp.text = "";

        // Wire the displayText SerializedProperty on the new component.
        var displaySo = new SerializedObject(display);
        displaySo.FindProperty("displayText").objectReferenceValue = tmp;
        displaySo.ApplyModifiedPropertiesWithoutUndo();

        // Card-level Button. BotSettings.Start wires onClick at runtime to
        // OpenConfirmChange{Whatsapp|Telegram}NumberPopup.
        var cardImg = go.GetComponent<Image>();
        if (cardImg != null) cardImg.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        if (btn == null) btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        btn.targetGraphic = cardImg;
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        btn.colors = colors;

        // Point the BotSettings serialized field at the new component.
        fieldProp.objectReferenceValue = display;
        return true;
    }

    private static bool BuildPopup(SerializedObject so, GameObject prefabRoot, bool force,
        string popupProp, string confirmBtnProp, string cancelBtnProp,
        string goName, string title, string body)
    {
        var popupSp = so.FindProperty(popupProp);
        if (popupSp == null)
        {
            Debug.LogError($"[BotSettings] SerializedProperty '{popupProp}' not found.");
            return false;
        }

        var existing = popupSp.objectReferenceValue as GameObject;
        if (existing != null)
        {
            if (!force) return false;
            Object.DestroyImmediate(existing);
            popupSp.objectReferenceValue = null;
        }

        // Also sweep any stray GameObject with the same name under the prefab
        // root, in case a previous build lost its SerializedField reference.
        for (int i = prefabRoot.transform.childCount - 1; i >= 0; i--)
        {
            var child = prefabRoot.transform.GetChild(i);
            if (child.name == goName) Object.DestroyImmediate(child.gameObject);
        }

        var (popup, confirmBtn, cancelBtn) = CreatePopupGameObject(prefabRoot, goName, title, body);
        popupSp.objectReferenceValue = popup;
        so.FindProperty(confirmBtnProp).objectReferenceValue = confirmBtn;
        so.FindProperty(cancelBtnProp).objectReferenceValue = cancelBtn;
        return true;
    }

    private static (GameObject popup, Button confirm, Button cancel) CreatePopupGameObject(
        GameObject prefabRoot, string goName, string title, string body)
    {
        var popup = new GameObject(goName, typeof(RectTransform), typeof(Image), typeof(Button));
        popup.transform.SetParent(prefabRoot.transform, worldPositionStays: false);
        var prt = (RectTransform)popup.transform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        prt.SetAsLastSibling();

        // Backdrop starts fully transparent; PopupUI.Show fades it in to 0.5.
        popup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        // Card — must be named "Content" so PopupUI.Show locates it.
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

        // EventAbsorber prevents card taps from dismissing the backdrop.
        card.AddComponent<EventAbsorber>();

        AddText(card, "Title", title, 54f, bold: true,
            color: new Color(0.11f, 0.11f, 0.12f),
            anchorTopPivot: new Vector2(0.5f, 1f),
            anchoredPosition: new Vector2(0f, -72f),
            sizeDelta: new Vector2(-96f, 84f));

        AddText(card, "Body", body, 42f, bold: false,
            color: new Color(0.39f, 0.39f, 0.4f),
            anchorTopPivot: new Vector2(0.5f, 1f),
            anchoredPosition: new Vector2(0f, -204f),
            sizeDelta: new Vector2(-96f, 180f));

        var cancelBtn = BuildButton(card, "CancelButton", "Отмена",
            isPrimary: false, anchorX: 0.28f);
        var confirmBtn = BuildButton(card, "ConfirmButton", "Сменить",
            isPrimary: true, anchorX: 0.72f);

        popup.SetActive(false);
        return (popup, confirmBtn, cancelBtn);
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
            ? new Color(0.92f, 0.27f, 0.27f)
            : new Color(0.94f, 0.94f, 0.95f);

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
