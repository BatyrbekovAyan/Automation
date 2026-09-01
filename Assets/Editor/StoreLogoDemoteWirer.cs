#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Store-compliance logo demotion (2026-08-31, App Store audit §03 — Apple 5.2.1 /
/// Meta brand guidelines / Play Impersonation). ADDITIVE: swaps sprites and tints on
/// EXISTING nodes, deactivates two overlays — creates and destroys nothing.
///
/// The official WhatsApp/Telegram marks were painted on decorative hero surfaces —
/// the strongest 5.2.1 hook a reviewer gets, doubly dangerous because the WhatsApp
/// variant («WhatsApp Glowing.png») is a MODIFIED mark, which Meta's guidelines
/// forbid outright. Demoted here:
///
///   A) Chats empty-state hero (EmptyStateView.iconImage): glowing WA logo → the
///      in-house nav-chats bubble glyph, channel-accent tinted. telegramIcon → null,
///      so the Telegram channel no longer swaps in the official roundel (the runtime
///      already recolors the disc/CTA per channel — identity survives without the
///      mark; with a null ref the authored sprite stays and TG forces it white on
///      the Telegram-blue disc).
///
///   B) Onboarding channel cards (WhatsappCard/TelegramCard → IconSquare/Logo):
///      official marks → the same neutral glyph, channel-tinted. The cards' NAME
///      labels («WhatsApp» / «Telegram») carry the channel — nominative text use.
///      OnboardingScreenBuilder was updated in the same change, so a rebuild
///      authors the identical neutral state.
///
///   C) Auth-screen logo overlays (Logo nodes near the WA/TG QR status): sprite →
///      null AND GameObject deactivated — the QR panel is the reviewer's mandatory
///      test path, and the QR needs no logo. Sprite refs are dropped (not just
///      hidden) so the binary stops shipping the mark from these surfaces.
///
/// KEPT deliberately (defensible nominative use — do not strip): the small channel
/// glyphs on bot cards (Bot.prefab) and the wizard channel picker, where the mark
/// identifies which service an account is connected to.
///
/// After running, «WhatsApp Glowing.png» has zero references and is deleted from
/// the repo — never re-add a modified official mark.
/// </summary>
public static class StoreLogoDemoteWirer
{
    private const string GlyphPath = "Assets/Images/Nav/nav_chats_filled.png";

    // Channel accents over the pale discs/squares (white-on-pale is invisible):
    // empty state keeps EmptyStateViewBuilder's authored Brand green; the runtime
    // Telegram branch forces white on the Telegram-blue disc.
    private static readonly Color EmptyStateTint = Hex("#25D366");
    private static readonly Color WaCardTint = Hex("#1F8A46");   // OnboardingAuthBlocksBuilder.LockTint
    private static readonly Color TgCardTint = Hex("#2AABEE");   // ChannelAccent.TelegramBlue

    // The nav glyph inks nearly its full canvas, unlike the old logo art whose padding
    // made it read smaller — at the authored 260×260 it looked gigantic in the disc
    // (owner check 2026-09-01). 150 ≈ the About page's robot-in-disc proportion.
    private const float EmptyStateIconSize = 150f;

    [MenuItem("Tools/Store Compliance/Demote Official Channel Logos")]
    public static void Run()
    {
        RunInternal();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[StoreLogoDemoteWirer] Demoted — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod StoreLogoDemoteWirer.RunHeadless -quit
    public static void RunHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        RunInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[StoreLogoDemoteWirer] Headless demote + save complete");
    }

    private static void RunInternal()
    {
        var glyph = AssetDatabase.LoadAssetAtPath<Sprite>(GlyphPath);
        if (glyph == null)
            throw new System.InvalidOperationException(
                $"[StoreLogoDemoteWirer] Neutral glyph not importable as Sprite: {GlyphPath}");

        RestampEmptyState(glyph);
        RestampOnboardingCard("WhatsappCard", glyph, WaCardTint);
        RestampOnboardingCard("TelegramCard", glyph, TgCardTint);
        DisableAuthLogo("WhatsappQRStatusText");
        DisableAuthLogo("TelegramQRStatusText");

        Debug.Log("[StoreLogoDemoteWirer] Empty-state hero + 2 onboarding cards restamped, 2 auth logo overlays dropped.");
    }

    private static void RestampEmptyState(Sprite glyph)
    {
        var view = Object.FindFirstObjectByType<EmptyStateView>(FindObjectsInactive.Include);
        if (view == null)
            throw new System.InvalidOperationException(
                "[StoreLogoDemoteWirer] EmptyStateView not found — is Main.unity open?");

        var so = new SerializedObject(view);
        var iconImage = so.FindProperty("iconImage").objectReferenceValue as Image;
        if (iconImage == null)
            throw new System.InvalidOperationException(
                "[StoreLogoDemoteWirer] EmptyStateView.iconImage is not assigned.");

        iconImage.sprite = glyph;
        iconImage.color = EmptyStateTint;
        iconImage.rectTransform.sizeDelta = new Vector2(EmptyStateIconSize, EmptyStateIconSize);
        EditorUtility.SetDirty(iconImage);

        so.FindProperty("telegramIcon").objectReferenceValue = null;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(view);
    }

    private static void RestampOnboardingCard(string cardName, Sprite glyph, Color tint)
    {
        var card = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name == cardName);
        if (card == null)
            throw new System.InvalidOperationException(
                $"[StoreLogoDemoteWirer] {cardName} not found — run Tools/Onboarding builders first.");

        // Cards are bordered: root → Fill → IconSquare → Logo. Search the card's own
        // subtree so the border level can't strand the lookup.
        var logo = card.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "Logo" && t.parent != null && t.parent.name == "IconSquare");
        var image = logo != null ? logo.GetComponent<Image>() : null;
        if (image == null)
            throw new System.InvalidOperationException(
                $"[StoreLogoDemoteWirer] {cardName} → IconSquare/Logo Image not found.");

        image.sprite = glyph;
        image.color = tint;
        EditorUtility.SetDirty(image);
    }

    private static void DisableAuthLogo(string statusTextField)
    {
        var manager = Object.FindFirstObjectByType<Manager>(FindObjectsInactive.Include);
        if (manager == null)
            throw new System.InvalidOperationException(
                "[StoreLogoDemoteWirer] Manager not found — is Main.unity open?");

        var statusGo = new SerializedObject(manager).FindProperty(statusTextField)
            .objectReferenceValue as GameObject;
        if (statusGo == null)
            throw new System.InvalidOperationException(
                $"[StoreLogoDemoteWirer] Manager.{statusTextField} is not assigned.");

        // The Logo overlay sits under the status label or beside it under the QR
        // image — search the parent's subtree so both layouts resolve.
        var scope = statusGo.transform.parent != null ? statusGo.transform.parent : statusGo.transform;
        var logo = scope.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "Logo");
        if (logo == null)
            throw new System.InvalidOperationException(
                $"[StoreLogoDemoteWirer] No 'Logo' node found near {statusTextField}.");

        var image = logo.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = null;    // drop the asset reference, not just the pixels
            EditorUtility.SetDirty(image);
        }
        logo.gameObject.SetActive(false);
        EditorUtility.SetDirty(logo.gameObject);
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        return c;
    }
}
#endif
