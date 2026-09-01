#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Store-compliance text restamp (2026-08-31, App Store audit §02/§05). ADDITIVE:
/// only rewrites the text of EXISTING TMP labels — creates and destroys nothing,
/// so the hand-tuned scene stays intact (never re-run the full builders for this).
///
///   A) Auth trust cards (both channels): the old deck claimed «официальные
///      „Связанные устройства"» and «Переписка остаётся у вас» under an
///      «Это безопасно» header — overclaims the app's own review notes contradict
///      (Play Deceptive Behavior / Apple 2.3.1). Restamped to the honest deck in
///      <see cref="OnboardingAuthBlocksBuilder"/> (updated in the same change, per
///      the RU rule: label and its authoring builder move together).
///
///   B) Profile → «Подписка» cancel caption: the baked seed said «App Store /
///      Google Play» — a Google Play mention inside the iOS binary (Apple 2.3.10).
///      Reseeded to the store-neutral <see cref="SubscriptionPageRows.CancelCaption"/>;
///      the per-store wording is stamped at runtime by RefreshSubscriptionPage.
///
/// Targets resolve through the owning components' serialized refs (Manager's
/// auth-panel fields, ProfileSubPages.subCancelCaption) — never by path guessing.
/// </summary>
public static class StoreClaimsFixWirer
{
    [MenuItem("Tools/Store Compliance/Restamp Auth Claims + Subscription Caption")]
    public static void Run()
    {
        RunInternal();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[StoreClaimsFixWirer] Restamped — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod StoreClaimsFixWirer.RunHeadless -quit
    public static void RunHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        RunInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[StoreClaimsFixWirer] Headless restamp + save complete");
    }

    private static void RunInternal()
    {
        var manager = Object.FindFirstObjectByType<Manager>(FindObjectsInactive.Include);
        if (manager == null)
            throw new System.InvalidOperationException(
                "[StoreClaimsFixWirer] Manager not found — is Main.unity open?");

        var managerSo = new SerializedObject(manager);
        RestampTrustCard(managerSo, "WhatsappCodePanel", OnboardingAuthBlocksBuilder.TrustBodyWhatsapp);
        RestampTrustCard(managerSo, "TelegramCodePanel", OnboardingAuthBlocksBuilder.TrustBodyTelegram);

        RestampCancelCaption();

        Debug.Log("[StoreClaimsFixWirer] Trust cards (WA+TG) + subscription caption restamped.");
    }

    private static void RestampTrustCard(SerializedObject managerSo, string panelField, string bodyText)
    {
        var panel = managerSo.FindProperty(panelField).objectReferenceValue as GameObject;
        if (panel == null)
            throw new System.InvalidOperationException(
                $"[StoreClaimsFixWirer] Manager.{panelField} is not assigned.");

        var block = panel.transform.Find("TrustBlock");
        var fill = block != null ? block.Find("Fill") : null;
        if (fill == null)
            throw new System.InvalidOperationException(
                $"[StoreClaimsFixWirer] TrustBlock/Fill not found under {panelField} — " +
                "run Tools/Onboarding/Build Auth Blocks first.");

        // The 5.1.2(i) consent sentence made the deck 5 lines — grow the card to the
        // builder's geometry so the last line is not clipped (both sources move together).
        var blockRt = (RectTransform)block;
        blockRt.sizeDelta = new Vector2(blockRt.sizeDelta.x, OnboardingAuthBlocksBuilder.TrustCardHeight);
        var layout = block.GetComponent<UnityEngine.UI.LayoutElement>();
        if (layout != null)
        {
            layout.minHeight = OnboardingAuthBlocksBuilder.TrustCardHeight;
            layout.preferredHeight = OnboardingAuthBlocksBuilder.TrustCardHeight;
            EditorUtility.SetDirty(layout);
        }
        var bodyRt = fill.Find("Body") as RectTransform;
        if (bodyRt != null)
        {
            bodyRt.sizeDelta = new Vector2(bodyRt.sizeDelta.x, OnboardingAuthBlocksBuilder.TrustBodyHeight);
            EditorUtility.SetDirty(bodyRt);
        }
        EditorUtility.SetDirty(blockRt);

        SetLabel(fill.Find("Title"), OnboardingAuthBlocksBuilder.TrustTitleText, panelField + " Title");
        SetLabel(fill.Find("Body"), bodyText, panelField + " Body");
    }

    private static void RestampCancelCaption()
    {
        var profile = Object.FindFirstObjectByType<ProfileSubPages>(FindObjectsInactive.Include);
        if (profile == null)
            throw new System.InvalidOperationException(
                "[StoreClaimsFixWirer] ProfileSubPages not found — is Main.unity open?");

        var profileSo = new SerializedObject(profile);
        var captionGo = profileSo.FindProperty("subCancelCaption").objectReferenceValue as GameObject;
        if (captionGo == null)
            throw new System.InvalidOperationException(
                "[StoreClaimsFixWirer] ProfileSubPages.subCancelCaption is not assigned — " +
                "run Tools/Billing/Build Subscription Page first.");

        var tmp = captionGo.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp == null)
            throw new System.InvalidOperationException(
                "[StoreClaimsFixWirer] subCancelCaption carries no TMP label.");

        tmp.text = SubscriptionPageRows.CancelCaption;
        EditorUtility.SetDirty(tmp);
    }

    private static void SetLabel(Transform node, string text, string what)
    {
        var tmp = node != null ? node.GetComponent<TextMeshProUGUI>() : null;
        if (tmp == null)
            throw new System.InvalidOperationException(
                $"[StoreClaimsFixWirer] TMP label missing for {what}.");
        tmp.text = text;
        EditorUtility.SetDirty(tmp);
    }
}
#endif
