#if UNITY_EDITOR
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor maintenance for BotSettings.prefab.
///
/// For each of BusinessField and PromptField:
///   1. Deletes the inner Label GameObject (redundant with SectionHeader).
///   2. Wraps the TMP_InputField's textViewport + textComponent in a
///      ScrollRect so the fixed-height card supports touch-drag scrolling.
///   3. Attaches ScrollableTextArea with wired references for runtime
///      content-size sync.
///   4. Inserts a DragShield overlay (same script the chat outgoing input
///      uses) as the last child of the TMP_InputField. The shield
///      distinguishes tap (place caret) from drag (scroll) instead of
///      letting TMP_InputField interpret every drag as text selection.
///
/// Each step is individually idempotent — safe to re-run on a prefab
/// that has already been partially converted.
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

        var modified = false;

        // 1. Delete the inner Label (idempotent: skipped if already cleared).
        if (labelProp != null && labelProp.objectReferenceValue is TextMeshProUGUI label)
        {
            Object.DestroyImmediate(label.gameObject);
            labelProp.objectReferenceValue = null;
            fieldSo.ApplyModifiedPropertiesWithoutUndo();
            modified = true;
        }

        // 2. Configure content RT (top-stretch, pivot top). Idempotent by
        // equality check so re-runs don't mark the prefab dirty.
        var contentRt = textComponent.rectTransform;
        var targetMin = new Vector2(0f, 1f);
        var targetMax = new Vector2(1f, 1f);
        var targetPivot = new Vector2(0.5f, 1f);
        if (contentRt.anchorMin != targetMin ||
            contentRt.anchorMax != targetMax ||
            contentRt.pivot != targetPivot)
        {
            contentRt.anchorMin = targetMin;
            contentRt.anchorMax = targetMax;
            contentRt.pivot = targetPivot;
            modified = true;
        }

        // 3. Add ScrollRect on the TMP_InputField GameObject (idempotent).
        var inputGo = input.gameObject;
        var scroll = inputGo.GetComponent<ScrollRect>();
        if (scroll == null)
        {
            scroll = inputGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 1f;
            scroll.viewport = viewport;
            scroll.content = contentRt;
            modified = true;
        }

        // 3b. Revert any prior ScrollableInputField swap to stock
        // TMP_InputField. DragShield supersedes the subclass so the prefab
        // should reference only the stock class going forward.
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
            else
            {
                Debug.LogWarning(
                    "[BotSettings] Could not locate TMP_InputField MonoScript; " +
                    "leaving input component class unchanged.");
            }
        }

        // 4. Attach ScrollableTextArea on the field root and wire refs
        // (idempotent: skipped if already attached).
        var sta = field.GetComponent<ScrollableTextArea>();
        if (sta == null)
        {
            sta = field.gameObject.AddComponent<ScrollableTextArea>();
            var staSo = new SerializedObject(sta);
            staSo.FindProperty("scrollRect").objectReferenceValue = scroll;
            staSo.FindProperty("inputField").objectReferenceValue = input;
            staSo.FindProperty("content").objectReferenceValue = contentRt;
            staSo.ApplyModifiedPropertiesWithoutUndo();
            modified = true;
        }

        // 5. Insert DragShield overlay as the last child of the TMP_InputField
        // GameObject so it captures pointer events before the input field
        // does. This is the same shield the chat outgoing-message input uses:
        // tap places caret, drag scrolls the parent ScrollRect, long-press
        // double-tap selects a word. Idempotent — reuses the existing shield.
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

        // Ensure DragShield is front-most sibling so raycasts hit it first.
        var lastIndex = inputGo.transform.childCount - 1;
        if (shield.transform.GetSiblingIndex() != lastIndex)
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
