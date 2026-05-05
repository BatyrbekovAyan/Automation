#if UNITY_EDITOR
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor maintenance for BotSettings.prefab.
///
/// For each of BusinessField and PromptField, the target layout mirrors
/// the chat outgoing-message input wiring:
///
///   BusinessField (ScrollRect + RectMask2D + ScrollableTextArea)
///     Background
///     TMP_InputField                       ← ScrollRect.content
///       (TMP's own Text Area / Text / Placeholder left untouched)
///       DragShield (transparent raycast-target Image, last child)
///
/// The ScrollRect lives on the BusinessField root; its content is the
/// TMP_InputField's OWN RectTransform. TMP_InputField grows with text
/// via ScrollableTextArea writing to its RectTransform.sizeDelta — TMP
/// keeps managing its internal textViewport / textComponent / caret,
/// which avoids caret-graphic rebuild re-entry during layout.
/// DragShield on the root captures pointer events before the input field
/// does so tap places caret and drag scrolls the ScrollRect.
///
/// Each step is individually idempotent — safe to re-run on a prefab
/// already converted by earlier (incorrect) versions of this tool.
/// </summary>
public static class BotSettingsScrollableTextAreaBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";

    [MenuItem("Tools/BotSettings/Build Scrollable Business+Prompt")]
    public static void Build()
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

            var modified = false;
            modified |= ConvertField(settings.BusinessField, nameof(settings.BusinessField));
            modified |= ConvertField(settings.PromptField, nameof(settings.PromptField));

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log($"[BotSettings] Prefab updated at {PrefabPath}");
            }
            else
            {
                Debug.Log("[BotSettings] Nothing to do — already converted.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool ConvertField(EditableTextArea field, string fieldName)
    {
        if (field == null)
        {
            Debug.LogWarning($"[BotSettings] {fieldName} not wired on BotSettings prefab.");
            return false;
        }

        var fieldSo = new SerializedObject(field);
        var labelProp = fieldSo.FindProperty("labelText");
        var inputProp = fieldSo.FindProperty("input");
        if (inputProp == null || inputProp.objectReferenceValue == null)
        {
            Debug.LogError($"[BotSettings] {fieldName}.input not wired; aborting.");
            return false;
        }

        var input = (TMP_InputField)inputProp.objectReferenceValue;
        var viewport = input.textViewport;
        var textComponent = input.textComponent;
        if (viewport == null || textComponent == null)
        {
            Debug.LogError($"[BotSettings] {fieldName} TMP_InputField missing textViewport/textComponent.");
            return false;
        }

        var fieldRt = (RectTransform)field.transform;
        var inputGo = input.gameObject;
        var inputRt = (RectTransform)inputGo.transform;
        var modified = false;

        // 1. Delete the inner Label (idempotent).
        if (labelProp != null && labelProp.objectReferenceValue is TextMeshProUGUI label)
        {
            Object.DestroyImmediate(label.gameObject);
            labelProp.objectReferenceValue = null;
            fieldSo.ApplyModifiedPropertiesWithoutUndo();
            modified = true;
        }

        // 2. Revert any prior ScrollableInputField swap back to stock
        // TMP_InputField. DragShield supersedes the subclass.
        if (input.GetType() != typeof(TMP_InputField))
        {
            var tmpScript = GetMonoScriptForType(typeof(TMP_InputField));
            if (tmpScript != null)
            {
                var inputSo = new SerializedObject(input);
                inputSo.FindProperty("m_Script").objectReferenceValue = tmpScript;
                inputSo.ApplyModifiedPropertiesWithoutUndo();
                modified = true;
            }
        }

        // 3. Remove any ScrollRect left over on the TMP_InputField GameObject
        // from earlier (incorrect) builder versions — the ScrollRect now
        // lives on the field root. Also strip the RectMask2D that the old
        // variant could have attached here.
        var staleScroll = inputGo.GetComponent<ScrollRect>();
        if (staleScroll != null)
        {
            Object.DestroyImmediate(staleScroll);
            modified = true;
        }

        // 4. Reset textComponent RT to TMP defaults. Earlier builder
        // versions set it to top-stretch; TMP expects the text component
        // to fill its textViewport (stretch-stretch, pivot center).
        var textCompRt = textComponent.rectTransform;
        var tmpMin = Vector2.zero;
        var tmpMax = Vector2.one;
        var tmpPivot = new Vector2(0.5f, 0.5f);
        if (textCompRt.anchorMin != tmpMin ||
            textCompRt.anchorMax != tmpMax ||
            textCompRt.pivot != tmpPivot ||
            textCompRt.sizeDelta != Vector2.zero ||
            textCompRt.anchoredPosition != Vector2.zero)
        {
            textCompRt.anchorMin = tmpMin;
            textCompRt.anchorMax = tmpMax;
            textCompRt.pivot = tmpPivot;
            textCompRt.sizeDelta = Vector2.zero;
            textCompRt.anchoredPosition = Vector2.zero;
            modified = true;
        }

        // 5. Configure TMP_InputField RT as scroll content: top-stretch,
        // pivot top-center. Its sizeDelta.y is driven by ScrollableTextArea
        // at runtime; leave whatever height is currently on the prefab.
        var contentMin = new Vector2(0f, 1f);
        var contentMax = new Vector2(1f, 1f);
        var contentPivot = new Vector2(0.5f, 1f);
        if (inputRt.anchorMin != contentMin ||
            inputRt.anchorMax != contentMax ||
            inputRt.pivot != contentPivot)
        {
            inputRt.anchorMin = contentMin;
            inputRt.anchorMax = contentMax;
            inputRt.pivot = contentPivot;
            modified = true;
        }

        // 6. Add ScrollRect on the field root (idempotent). Viewport is the
        // field root itself (clipped by RectMask2D below); content is the
        // TMP_InputField's own RectTransform.
        var scroll = field.gameObject.GetComponent<ScrollRect>();
        if (scroll == null)
        {
            scroll = field.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 1f;
            modified = true;
        }
        if (scroll.viewport != fieldRt || scroll.content != inputRt)
        {
            scroll.viewport = fieldRt;
            scroll.content = inputRt;
            modified = true;
        }

        // 7. RectMask2D on the field root clips the input when it scrolls
        // past the card bounds.
        if (field.gameObject.GetComponent<RectMask2D>() == null)
        {
            field.gameObject.AddComponent<RectMask2D>();
            modified = true;
        }

        // 8. Attach ScrollableTextArea and (re)wire refs to the correct
        // content — the TMP_InputField's RectTransform, NOT textComponent's.
        var sta = field.GetComponent<ScrollableTextArea>();
        if (sta == null)
        {
            sta = field.gameObject.AddComponent<ScrollableTextArea>();
            modified = true;
        }
        var staSo = new SerializedObject(sta);
        var staScrollProp = staSo.FindProperty("scrollRect");
        var staInputProp = staSo.FindProperty("inputField");
        var staContentProp = staSo.FindProperty("content");
        if (staScrollProp.objectReferenceValue != scroll ||
            staInputProp.objectReferenceValue != input ||
            staContentProp.objectReferenceValue != inputRt)
        {
            staScrollProp.objectReferenceValue = scroll;
            staInputProp.objectReferenceValue = input;
            staContentProp.objectReferenceValue = inputRt;
            staSo.ApplyModifiedPropertiesWithoutUndo();
            modified = true;
        }

        // 9. Delete any stale DragShield left on the BusinessField root by a
        // previous (incorrect) builder version. Placing the shield there
        // put TMP_InputField outside its ancestor chain, so every tap
        // triggered EventSystem.DeselectIfSelectionChanged and unfocused
        // the input — firing onEndEdit → Blur → scrim.Hide on every tap.
        var staleRootShield = field.transform.Find("DragShield");
        if (staleRootShield != null)
        {
            Object.DestroyImmediate(staleRootShield.gameObject);
            modified = true;
        }

        // 10. Insert DragShield as the last child of the TMP_InputField
        // GameObject (matches the chat outgoing-input layout). Being inside
        // TMP_InputField makes the field itself the nearest ISelectHandler
        // ancestor during EventSystem's select-change check, so the field
        // stays focused across taps and the scrim does not flicker.
        // ScrollableTextArea keeps the TMP_InputField at least as tall as
        // the viewport, so DragShield always covers the full visible card
        // interior; RectMask2D on the field root clips any overscroll.
        var shieldTransform = inputGo.transform.Find("DragShield");
        DragShield shield;
        if (shieldTransform == null)
        {
            var shieldGo = new GameObject(
                "DragShield", typeof(RectTransform), typeof(Image));
            shieldGo.transform.SetParent(inputGo.transform, worldPositionStays: false);

            var shieldRt = (RectTransform)shieldGo.transform;
            shieldRt.anchorMin = Vector2.zero;
            shieldRt.anchorMax = Vector2.one;
            shieldRt.sizeDelta = Vector2.zero;
            shieldRt.anchoredPosition = Vector2.zero;

            var shieldImage = shieldGo.GetComponent<Image>();
            shieldImage.color = new Color(0f, 0f, 0f, 0f);
            shieldImage.raycastTarget = true;

            shield = shieldGo.AddComponent<DragShield>();
            modified = true;
        }
        else
        {
            shield = shieldTransform.GetComponent<DragShield>();
            if (shield == null)
            {
                shield = shieldTransform.gameObject.AddComponent<DragShield>();
                modified = true;
            }
        }

        // Ensure DragShield is front-most sibling of TMP_InputField so
        // raycasts hit it before TMP's textComponent / placeholder.
        var lastSibling = inputGo.transform.childCount - 1;
        if (shield.transform.GetSiblingIndex() != lastSibling)
        {
            shield.transform.SetAsLastSibling();
            modified = true;
        }

        // Wire DragShield refs (idempotent by equality check).
        if (shield.parentScrollRect != scroll || shield.inputField != input)
        {
            var shieldSo = new SerializedObject(shield);
            shieldSo.FindProperty("parentScrollRect").objectReferenceValue = scroll;
            shieldSo.FindProperty("inputField").objectReferenceValue = input;
            shieldSo.ApplyModifiedPropertiesWithoutUndo();
            modified = true;
        }

        if (modified)
            Debug.Log($"[BotSettings] Converted {fieldName}.");
        return modified;
    }

    // TMP_InputField lives in a compiled assembly, so its MonoScript isn't
    // reachable via AssetDatabase.LoadAssetAtPath. Scan the runtime
    // MonoScript registry to find it.
    private static MonoScript GetMonoScriptForType(System.Type type)
    {
        foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
        {
            if (script != null && script.GetClass() == type)
                return script;
        }
        return null;
    }
}
#endif
