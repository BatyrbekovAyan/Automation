#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds the in-app legal surface to Профиль → «О приложении» (2026-09-01, App Store
/// audit §04 — Apple 5.1.1(i) / Play User Data: the privacy policy must be easily
/// reachable in-app, and a link living only inside the paywall was a weak answer;
/// plus the non-affiliation disclaimer, the standard mitigation for Apple 5.2.1 /
/// Play Impersonation).
///
/// ADDITIVE + idempotent: CLONES the existing «Лицензии открытого ПО» row twice
/// (clone keeps fonts, icon squircle, chevron and ThemedColor bindings — the
/// SubscriptionPageBuilder clone-the-row idiom), retitles them, inserts dividers
/// cloned from any existing profile divider, stamps ProfileSubPages'
/// privacyPolicyButton/termsOfUseButton refs (runtime wires the clicks to
/// LegalLinks), and rewrites the footer FinePrint to the disclaimer + operator ©.
/// ProfileSubPagesBuilder was updated in the same change, so a destructive rebuild
/// authors the identical state. Re-run this after that builder runs.
/// </summary>
public static class AboutLegalWirer
{
    private const string PrivacyRowName = "Row_Политика конфиденциальности";
    private const string TermsRowName = "Row_Условия использования";

    private const string FooterText =
        "Choose Reply не связан с WhatsApp (Meta Platforms) и Telegram и не одобрен ими.\n" +
        "WhatsApp и Telegram — товарные знаки их владельцев.\n\n" +
        "© 2026 ТОО «Synergy Expert Group»\nСделано для бизнеса в Казахстане и СНГ";

    [MenuItem("Tools/Store Compliance/Add About Legal Rows")]
    public static void Run()
    {
        RunInternal();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[AboutLegalWirer] Added — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod AboutLegalWirer.RunHeadless -quit
    public static void RunHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        RunInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[AboutLegalWirer] Headless add + save complete");
    }

    private static void RunInternal()
    {
        var profile = Object.FindFirstObjectByType<ProfileSubPages>(FindObjectsInactive.Include);
        if (profile == null)
            throw new System.InvalidOperationException(
                "[AboutLegalWirer] ProfileSubPages not found — is Main.unity open?");

        var so = new SerializedObject(profile);
        var licensesButton = so.FindProperty("licensesButton").objectReferenceValue as Button;
        if (licensesButton == null)
            throw new System.InvalidOperationException(
                "[AboutLegalWirer] ProfileSubPages.licensesButton is not assigned — " +
                "run Tools/Profile Sub-Pages/Build first.");

        Transform licensesRow = licensesButton.transform;
        Transform docsCard = licensesRow.parent;

        // Idempotent teardown of a previous run's clones (and their leading dividers).
        DestroyRowAndLeadingDivider(docsCard, PrivacyRowName);
        DestroyRowAndLeadingDivider(docsCard, TermsRowName);

        int licensesIndex = licensesRow.GetSiblingIndex();
        var privacyRow = CloneRow(licensesRow, docsCard, PrivacyRowName,
            "Политика конфиденциальности", licensesIndex);
        var termsRow = CloneRow(licensesRow, docsCard, TermsRowName,
            "Условия использования", privacyRow.GetSiblingIndex() + 1);
        InsertDividerAfter(docsCard, privacyRow);
        InsertDividerAfter(docsCard, termsRow);

        so.FindProperty("privacyPolicyButton").objectReferenceValue =
            privacyRow.GetComponent<Button>();
        so.FindProperty("termsOfUseButton").objectReferenceValue =
            termsRow.GetComponent<Button>();
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);

        RewriteFooter(docsCard);

        Debug.Log("[AboutLegalWirer] 2 legal rows + dividers added, footer disclaimer/© rewritten.");
    }

    private static void DestroyRowAndLeadingDivider(Transform card, string rowName)
    {
        var row = card.Find(rowName);
        if (row == null) return;
        int index = row.GetSiblingIndex();
        // A divider directly AFTER the row belongs to this clone pair (InsertDividerAfter).
        if (index + 1 < card.childCount && card.GetChild(index + 1).name == "Divider")
            Object.DestroyImmediate(card.GetChild(index + 1).gameObject);
        Object.DestroyImmediate(row.gameObject);
    }

    private static Transform CloneRow(Transform template, Transform card, string name,
        string labelText, int siblingIndex)
    {
        var clone = Object.Instantiate(template.gameObject, card);
        clone.name = name;
        clone.transform.SetSiblingIndex(siblingIndex);

        var label = clone.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (label == null)
            throw new System.InvalidOperationException(
                $"[AboutLegalWirer] Cloned row {name} has no Label TMP.");
        label.text = labelText;

        // The clone carries the template's persistent Button state but no runtime
        // listeners — ProfileSubPages.WireAbout adds the LegalLinks handlers.
        var button = clone.GetComponent<Button>();
        button.onClick.RemoveAllListeners();

        EditorUtility.SetDirty(clone);
        return clone.transform;
    }

    private static void InsertDividerAfter(Transform card, Transform row)
    {
        // Clone any existing profile divider so ThemedColor bindings survive; the
        // Screen_Profile subtree always has one (every multi-row card uses them).
        var screenRoot = card.GetComponentInParent<ProfileSubPages>(true).transform;
        Transform template = null;
        foreach (var t in screenRoot.GetComponentsInChildren<Transform>(true))
            if (t.name == "Divider") { template = t; break; }
        if (template == null)
            throw new System.InvalidOperationException(
                "[AboutLegalWirer] No Divider found under Screen_Profile to clone.");

        var divider = Object.Instantiate(template.gameObject, card);
        divider.name = "Divider";
        divider.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);
        EditorUtility.SetDirty(divider);
    }

    private static void RewriteFooter(Transform docsCard)
    {
        var content = docsCard.parent;
        var finePrint = content.Find("FinePrint");
        var tmp = finePrint != null ? finePrint.GetComponent<TextMeshProUGUI>() : null;
        if (tmp == null)
            throw new System.InvalidOperationException(
                "[AboutLegalWirer] FinePrint label not found beside DocsCard.");
        tmp.text = FooterText;
        EditorUtility.SetDirty(tmp);
    }
}
#endif
