#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stamps the single serialized ref a runtime script can't resolve on its own:
/// <see cref="EmptyStateView"/>.telegramIcon ← the Telegram logo sprite (guid
/// cee100f5bfdd848ccb770f9e96e64a5a). With that ref in place, EmptyStateView shows the
/// Telegram logo UNTINTED on the Telegram channel (05-12); the icon-color swap and the
/// parent-disc recolor are done at runtime, so this builder wires ONLY the asset ref.
///
/// The logo PNG must be imported as a SINGLE sprite (its .meta spriteMode: 1) for
/// LoadAssetAtPath&lt;Sprite&gt; to return an assignable sprite — see 05-12.
///
/// Mirrors ChannelSwitcherBuilder's headless idiom (FindFirstObjectByType include-inactive,
/// SerializedObject SetRef, OpenScene→mutate→SaveScene). Idempotent — a re-run re-stamps the
/// same ref. Two entry points:
///   • Editor OPEN   → run "Tools/Empty State/Stamp Telegram Icon", then SAVE (Cmd+S).
///   • Editor CLOSED → Tools/run-editor-builder.sh EmptyStateTelegramIconBuilder.StampHeadless
/// </summary>
public static class EmptyStateTelegramIconBuilder
{
    private const string TelegramLogoGuid = "cee100f5bfdd848ccb770f9e96e64a5a";

    // ── Entry points ────────────────────────────────────────────────────────

    [MenuItem("Tools/Empty State/Stamp Telegram Icon")]
    public static void Stamp()
    {
        EmptyStateView view = StampInternal();
        if (view == null) return;
        Selection.activeGameObject = view.gameObject;
        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        Debug.Log("[EmptyStateTelegramIconBuilder] telegramIcon stamped. SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod EmptyStateTelegramIconBuilder.StampHeadless -quit
    public static void StampHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        StampInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[EmptyStateTelegramIconBuilder] Headless build + save complete: telegramIcon ref stamped.");
    }

    // ── Stamp ───────────────────────────────────────────────────────────────

    private static EmptyStateView StampInternal()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<EmptyStateView>(FindObjectsInactive.Include);
        if (view == null)
        {
            Debug.LogError("[EmptyStateTelegramIconBuilder] EmptyStateView not found — open Main.unity.");
            return null;
        }

        string path = AssetDatabase.GUIDToAssetPath(TelegramLogoGuid);
        var sprite = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            Debug.LogError($"[EmptyStateTelegramIconBuilder] Telegram logo sprite not found at guid {TelegramLogoGuid} " +
                           "(path: '" + path + "'). Is the PNG imported as a Single sprite (.meta spriteMode: 1)?");
            return null;
        }

        var so = new SerializedObject(view);
        var prop = so.FindProperty("telegramIcon");
        if (prop == null)
        {
            Debug.LogError("[EmptyStateTelegramIconBuilder] 'telegramIcon' property not found on EmptyStateView — recompile first.");
            return null;
        }
        prop.objectReferenceValue = sprite;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(view);

        Debug.Log($"[EmptyStateTelegramIconBuilder] Stamped telegramIcon ← '{path}'.");
        return view;
    }
}
#endif
