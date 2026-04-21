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
///   3. Swaps the TMP_InputField's script to ScrollableInputField so
///      drag gestures forward to the ScrollRect instead of selecting text.
///   4. Attaches ScrollableTextArea with wired references for runtime
///      content-size sync.
///
/// Each step is individually idempotent — safe to re-run on a prefab
/// that has already been partially converted.
/// </summary>
public static class BotSettingsScrollableTextAreaBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string ScrollableInputFieldScriptPath =
        "Assets/Scripts/Main/BotSettings/ScrollableInputField.cs";

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

        // 4. Swap TMP_InputField -> ScrollableInputField so drag gestures
        // forward to the ScrollRect. Idempotent: skipped if already swapped.
        if (!(input is ScrollableInputField))
        {
            var scrollableScript =
                AssetDatabase.LoadAssetAtPath<MonoScript>(ScrollableInputFieldScriptPath);
            if (scrollableScript == null)
            {
                Debug.LogError(
                    $"[BotSettings] ScrollableInputField script not found at {ScrollableInputFieldScriptPath}; " +
                    "skipping InputField swap.");
            }
            else
            {
                var inputSo = new SerializedObject(input);
                inputSo.FindProperty("m_Script").objectReferenceValue = scrollableScript;
                inputSo.ApplyModifiedPropertiesWithoutUndo();
                modified = true;
            }
        }

        // 5. Attach ScrollableTextArea on the field root and wire refs
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

        if (modified)
            Debug.Log($"[BotSettings] Converted {fieldName}.");
        return modified;
    }
}
#endif
