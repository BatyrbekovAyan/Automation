using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Enforces the invariant that BOTH auth pages carry <see cref="LayoutRebuildOnEnable"/>.
///
/// The auth pages are a two-level nested ContentSizeFitter chain (Content[VLG+CSF] →
/// QRPanel/Divider/CodePanel, each itself VLG-or-HLG + CSF). Unity resolves nested CSF+LG
/// chains across several frames, so on first activation the children settle one frame late —
/// on WhatsApp that showed as a vertically collapsed page whose «или» divider overlapped the
/// «Войти по номеру» header, silently correcting itself seconds later when the QR arrived and
/// Manager.OpenWhatsappQRPanel called ForceRebuildLayout.
///
/// TelegramAuth already had the component; WhatsappAuth was simply missed, which is why only
/// the WhatsApp page showed the pop. Rather than patch the one page, this fixer asserts the
/// invariant on both so a future auth page can't regress the same way.
///
/// Idempotent (ensure-component, never destroys), Edit-Mode only, saves the scene.
/// </summary>
public static class AuthPageLayoutRebuildFixer
{
    [MenuItem("Tools/Auth Pages/Ensure Layout Rebuild On Enable")]
    public static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException(
                "Exit Play Mode first — scene edits made in Play Mode are discarded on exit.");

        var manager = Object.FindFirstObjectByType<Manager>(FindObjectsInactive.Include);
        if (manager == null)
            throw new System.InvalidOperationException("Manager not found — is Main.unity open?");

        var so = new SerializedObject(manager);
        bool changed = false;
        changed |= EnsureOn(so, "WhatsappAuth");
        changed |= EnsureOn(so, "TelegramAuth");

        if (!changed)
        {
            Debug.Log("[AuthPageLayoutRebuildFixer] Both auth pages already carry LayoutRebuildOnEnable — nothing to do.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveScene(manager.gameObject.scene);
        Debug.Log("[AuthPageLayoutRebuildFixer] Done — scene saved.");
    }

    /// <summary>Adds LayoutRebuildOnEnable to the page behind <paramref name="fieldName"/> if absent.</summary>
    private static bool EnsureOn(SerializedObject managerSo, string fieldName)
    {
        var property = managerSo.FindProperty(fieldName);
        if (property == null)
            throw new System.InvalidOperationException($"Manager has no serialized field '{fieldName}'.");

        if (property.objectReferenceValue is not GameObject page)
            throw new System.InvalidOperationException($"Manager.{fieldName} is unwired.");

        if (page.GetComponent<LayoutRebuildOnEnable>() != null)
        {
            Debug.Log($"[AuthPageLayoutRebuildFixer] {page.name} already has LayoutRebuildOnEnable — skipped.");
            return false;
        }

        Undo.AddComponent<LayoutRebuildOnEnable>(page);
        Debug.Log($"[AuthPageLayoutRebuildFixer] Added LayoutRebuildOnEnable to {page.name}.");
        return true;
    }
}
