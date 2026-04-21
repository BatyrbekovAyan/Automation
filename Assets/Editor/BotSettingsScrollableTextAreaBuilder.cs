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
///
/// Skips anything already converted; safe to re-run.
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

        if (field.GetComponent<ScrollableTextArea>() != null)
            return false; // Already converted; idempotent skip.

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

        // 1. Delete the inner Label.
        if (labelProp != null && labelProp.objectReferenceValue is TextMeshProUGUI label)
        {
            Object.DestroyImmediate(label.gameObject);
            labelProp.objectReferenceValue = null;
            fieldSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // 2. Configure content RT (top-stretch, pivot top).
        var contentRt = textComponent.rectTransform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);

        // 3. Add ScrollRect on the TMP_InputField GameObject.
        var inputGo = input.gameObject;
        var scroll = inputGo.GetComponent<ScrollRect>();
        if (scroll == null)
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

        // 4. Attach ScrollableTextArea on the field root and wire refs.
        var sta = field.gameObject.AddComponent<ScrollableTextArea>();
        var staSo = new SerializedObject(sta);
        staSo.FindProperty("scrollRect").objectReferenceValue = scroll;
        staSo.FindProperty("inputField").objectReferenceValue = input;
        staSo.FindProperty("content").objectReferenceValue = contentRt;
        staSo.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[BotSettings] Converted {fieldName} to ScrollableTextArea.");
        return true;
    }
}
#endif
