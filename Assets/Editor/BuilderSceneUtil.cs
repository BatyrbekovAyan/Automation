#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// The idempotent building blocks of the additive scene and prefab builders (AutoChipLearnLayerBuilder,
/// PersonalChatsBuilder): each compares before it writes and reports whether it wrote — through its return value or
/// <c>ref modified</c> — so a second run of a builder changes nothing and saves nothing (a scene save re-mints every Nobi
/// material in Main.unity). Errors carry the calling builder's log tag.
/// </summary>
internal static class BuilderSceneUtil
{
    /// <summary>The child named <paramref name="name"/> under <paramref name="parent"/>, made when missing, with every listed component.</summary>
    public static RectTransform EnsureChild(Transform parent, string name, ref bool modified, params Type[] components)
    {
        var child = parent.Find(name) as RectTransform;
        if (child == null)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            child = (RectTransform)go.transform;
            child.SetParent(parent, false);
            modified = true;
        }
        AddMissing(child, ref modified, components);
        return child;
    }

    /// <summary>Adds each listed component the owner's GameObject does not carry yet.</summary>
    public static void AddMissing(Component owner, ref bool modified, params Type[] components)
    {
        foreach (Type component in components)
        {
            if (owner.GetComponent(component) != null) continue;
            owner.gameObject.AddComponent(component);
            modified = true;
        }
    }

    /// <summary>Moves item to right after anchor (both children of one parent).</summary>
    public static bool PlaceAfter(Transform item, Transform anchor)
    {
        int index = item.GetSiblingIndex();
        int target = anchor.GetSiblingIndex() + (index < anchor.GetSiblingIndex() ? 0 : 1);
        if (index == target) return false;
        item.SetSiblingIndex(target);
        return true;
    }

    public static bool SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        if (rect.anchorMin == anchorMin && rect.anchorMax == anchorMax && rect.pivot == pivot
            && rect.anchoredPosition == position && rect.sizeDelta == size)
            return false;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return true;
    }

    /// <summary>
    /// Role, target and alpha through SerializedObject only — ThemedColor.Configure would repaint at once from whatever theme
    /// this editor happens to be in.
    /// </summary>
    public static bool BindTheme(ThemedColor themed, ThemeRole role, Graphic target)
    {
        var binding = new SerializedObject(themed);
        SerializedProperty roleProp = binding.FindProperty("role");
        SerializedProperty targetProp = binding.FindProperty("target");
        SerializedProperty alphaProp = binding.FindProperty("preserveAlpha");
        if (roleProp.enumValueIndex == (int)role && targetProp.objectReferenceValue == target && alphaProp.boolValue) return false;
        roleProp.enumValueIndex = (int)role;
        targetProp.objectReferenceValue = target;
        alphaProp.boolValue = true;
        binding.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    /// <summary>Points the owner's serialized reference <paramref name="field"/> at <paramref name="value"/>.</summary>
    public static bool SetRef(SerializedObject owner, string field, Object value, string tag)
    {
        SerializedProperty property = owner.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException($"{tag} {owner.targetObject.GetType().Name} has no serialized field '{field}'.");
        if (property.objectReferenceValue == value) return false;
        property.objectReferenceValue = value;
        owner.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    /// <summary>The scene's one <typeparamref name="T"/>, inactive objects included.</summary>
    public static T Single<T>(Scene scene, string tag) where T : Component
    {
        T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(component => component.gameObject.scene == scene).ToArray();
        if (found.Length != 1)
            throw new InvalidOperationException($"{tag} Expected exactly one {typeof(T).Name} in {scene.path}, found {found.Length}.");
        return found[0];
    }
}
#endif
