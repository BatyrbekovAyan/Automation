#if UNITY_EDITOR
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Store-compliance feature-list restamp (2026-08-31, App Store audit §05 — Apple
/// 2.1/3.1.2). The «Во всех тарифах» checklist is BAKED into the scene by
/// PaywallBuilder (Feature0..N rows, never re-rendered at runtime), and it advertised
/// four Block-2 features the submitted binary does not have — while the review notes
/// invite a sandbox purchase, so the reviewer can falsify them in-app.
///
/// ADDITIVE: restamps the labels of the first <see cref="PaywallRows.AllPlansFeatures"/>
/// rows from the (trimmed) seam and DEACTIVATES the surplus rows — never destroys
/// nodes, so the Block-2 update can reactivate them by re-running this after restoring
/// the seam's lines. Do NOT re-run the destructive full Tools/Billing/Build Paywall
/// for this. The card's VerticalLayoutGroup excludes inactive children, so the card
/// shrinks on its own.
/// </summary>
public static class PaywallFeatureTrimWirer
{
    [MenuItem("Tools/Store Compliance/Trim Paywall Feature List")]
    public static void Run()
    {
        RunInternal();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[PaywallFeatureTrimWirer] Restamped — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod PaywallFeatureTrimWirer.RunHeadless -quit
    public static void RunHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        RunInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PaywallFeatureTrimWirer] Headless restamp + save complete");
    }

    private static void RunInternal()
    {
        var controller = Object.FindFirstObjectByType<PaywallController>(FindObjectsInactive.Include);
        if (controller == null)
            throw new System.InvalidOperationException(
                "[PaywallFeatureTrimWirer] PaywallController not found — is Main.unity open?");

        var featureRows = controller.GetComponentsInChildren<Transform>(true)
            .Where(t => Regex.IsMatch(t.name, @"^Feature\d+$"))
            .OrderBy(t => int.Parse(t.name.Substring("Feature".Length)))
            .ToArray();

        string[] features = PaywallRows.AllPlansFeatures;
        if (featureRows.Length < features.Length)
            throw new System.InvalidOperationException(
                $"[PaywallFeatureTrimWirer] Scene has {featureRows.Length} Feature rows but the seam lists " +
                $"{features.Length} — run Tools/Billing/Build Paywall first (it bakes one row per line).");

        for (int i = 0; i < featureRows.Length; i++)
        {
            bool kept = i < features.Length;
            featureRows[i].gameObject.SetActive(kept);
            EditorUtility.SetDirty(featureRows[i].gameObject);
            if (!kept) continue;

            var label = featureRows[i].Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (label == null)
                throw new System.InvalidOperationException(
                    $"[PaywallFeatureTrimWirer] {featureRows[i].name} has no Label TMP.");
            label.text = features[i];
            EditorUtility.SetDirty(label);
        }

        Debug.Log($"[PaywallFeatureTrimWirer] {features.Length} rows restamped, " +
                  $"{featureRows.Length - features.Length} surplus rows deactivated.");
    }
}
#endif
