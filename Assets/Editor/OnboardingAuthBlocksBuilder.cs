#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Injects the onboarding auth-step reassurance UI into the EXISTING hand-built
/// auth panels (Manager-owned), following the <see cref="NavRestructureBuilder"/>
/// idiom (idempotent delete-and-rebuild, Image+sprite icons, RoundedCorners bake,
/// SerializedObject field stamping). It builds:
///
///   A) «Это безопасно» trust cards (ONB-02) — one appended as the LAST child of
///      the WhatsApp code panel AND one appended as the LAST child of the Telegram
///      phone/code panel. Both panels lay their children out with a
///      VerticalLayoutGroup, so the card lands at the bottom of the stack. Appending
///      last is index-critical: Manager addresses the code-entry children by the
///      hardcoded sibling indices GetChild(3)/(4)/(5) (WhatsApp) and GetChild(3)
///      (Telegram) — inserting anywhere but last would shift them and break the
///      real auth code flow. Channel-specific verbatim copy, green-tinted card, a
///      lock icon (Image+sprite, never a TMP glyph). No linked-device-code text.
///
///   B) Per-channel success sheets (ONB-03 scene half) — TWO independent
///      «Загрузить прайс-лист» / «Позже» clusters, one built into the WhatsApp
///      SuccessOverlay and a second into the Telegram SuccessOverlay (separate
///      GameObjects in separate hierarchies — a shared label/button cannot child
///      both, so Plan 04 declared per-channel wa*/tg* field sets). Each sheet has an
///      animated green check (SuccessCheckPop → DOScale 0.9→1 OutBack), title, body,
///      a full-width Primary CTA and a ghost «Позже» button. The clusters are stamped
///      onto Manager's waSuccess* and tgSuccess* fields via SerializedObject.
///
/// All sizes in 1080×1920 canvas reference units. Save the scene after running
/// (the headless entry saves automatically).
/// </summary>
public static class OnboardingAuthBlocksBuilder
{
    // ── Design tokens ─────────────────────────────────────────────────────────
    private const float CardRadius = 40f;
    private const float BorderWidth = 3f;
    private const float PanelWidth = 970f;          // WhatsApp/Telegram code-panel width

    private static readonly Color Ink = Hex("#1A1A2E");
    private static readonly Color Muted = Hex("#65676B");
    private static readonly Color Primary = Hex("#1B7CEB");

    // Trust card (spec §Auth trust blocks).
    private static readonly Color TrustBg = Hex("#F2F8F2");
    private static readonly Color TrustBorder = Hex("#DCEDDD");
    private static readonly Color TrustTitle = Hex("#15633A");   // deep green, cohesive with the lock
    private static readonly Color TrustDisc = Hex("#E3F1E4");     // pale-green icon disc
    private static readonly Color LockTint = Hex("#1F8A46");      // white lock PNG tinted green

    // Success sheet.
    private static readonly Color SheetBg = Color.white;
    private static readonly Color CheckDisc = Hex("#E8F8EE");
    private static readonly Color GhostBg = Hex("#F0F2F5");

    // Fonts by GUID (default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
    private const string BoldGuid = "1cd715823fef34be4a3d3f3c5572594c";

    private const string LockIconPath = "Assets/Images/Icons/Lock.png";
    private const string CheckIconPath = "Assets/Images/Icons/[CITYPNG.COM]HD Green Check True Tick Mark Icon Sign PNG - 3000x3000.png";

    // Verbatim copy deck (owner-approved — do not paraphrase).
    private const string TrustTitleText = "Это безопасно";
    private const string TrustBodyWhatsapp =
        "Работает через официальные «Связанные устройства» WhatsApp. Переписка остаётся у вас, отключить бота можно в любой момент.";
    private const string TrustBodyTelegram =
        "Официальный вход Telegram: код приходит в само приложение. Переписка остаётся у вас, отключить бота можно в любой момент.";
    private const string SuccessTitleText = "Бот подключён!";
    private const string SuccessBodyText =
        "Осталось научить бота вашим ценам — загрузите прайс-лист, и он будет отвечать по вашим товарам";
    private const string SuccessPrimaryText = "Загрузить прайс-лист";
    private const string SuccessLaterText = "Позже";

    private static TMP_FontAsset _regular, _semibold, _bold;
    private static Sprite _lock, _check;
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    // ── Entry points ─────────────────────────────────────────────────────────

    [MenuItem("Tools/Onboarding/Build Auth Blocks")]
    public static void Build()
    {
        BuildInternal();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[OnboardingAuthBlocksBuilder] Built auth trust blocks + per-channel success CTAs — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod OnboardingAuthBlocksBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[OnboardingAuthBlocksBuilder] Headless build + save complete");
    }

    // ── Main build ───────────────────────────────────────────────────────────

    private static void BuildInternal()
    {
        AssetDatabase.Refresh();
        EnsureIconImportSettings();
        LoadAssets();
        _roundedToRefresh.Clear();

        var manager = Object.FindFirstObjectByType<Manager>(FindObjectsInactive.Include);
        if (manager == null)
            throw new System.InvalidOperationException(
                "[OnboardingAuthBlocksBuilder] Manager not found — is Main.unity open?");

        var so = new SerializedObject(manager);
        var waCodePanel = so.FindProperty("WhatsappCodePanel").objectReferenceValue as GameObject;
        var tgCodePanel = so.FindProperty("TelegramCodePanel").objectReferenceValue as GameObject;
        var waSuccessPanel = so.FindProperty("WhatsappAuthSuccessPanel").objectReferenceValue as GameObject;
        var tgSuccessPanel = so.FindProperty("TelegramAuthSuccessPanel").objectReferenceValue as GameObject;

        if (waCodePanel == null || tgCodePanel == null || waSuccessPanel == null || tgSuccessPanel == null)
            throw new System.InvalidOperationException(
                "[OnboardingAuthBlocksBuilder] Manager auth-panel refs are not all assigned — cannot inject blocks.");

        // A) Trust cards — channel-specific copy, appended LAST (index-safe).
        BuildTrustCard(waCodePanel, TrustBodyWhatsapp);
        BuildTrustCard(tgCodePanel, TrustBodyTelegram);

        // B) Per-channel success sheets.
        var wa = BuildSuccessSheet(waSuccessPanel);
        var tg = BuildSuccessSheet(tgSuccessPanel);

        // Stamp both clusters onto Manager's per-channel success-moment fields
        // (the [SerializeField] private field NAMES are the builder↔component contract).
        so.FindProperty("waSuccessTitleLabel").objectReferenceValue = wa.Title;
        so.FindProperty("waSuccessBodyLabel").objectReferenceValue = wa.Body;
        so.FindProperty("waSuccessPrimaryButton").objectReferenceValue = wa.PrimaryButton;
        so.FindProperty("waSuccessPrimaryLabel").objectReferenceValue = wa.PrimaryLabel;
        so.FindProperty("waSuccessLaterButton").objectReferenceValue = wa.LaterButton;
        so.FindProperty("tgSuccessTitleLabel").objectReferenceValue = tg.Title;
        so.FindProperty("tgSuccessBodyLabel").objectReferenceValue = tg.Body;
        so.FindProperty("tgSuccessPrimaryButton").objectReferenceValue = tg.PrimaryButton;
        so.FindProperty("tgSuccessPrimaryLabel").objectReferenceValue = tg.PrimaryLabel;
        so.FindProperty("tgSuccessLaterButton").objectReferenceValue = tg.LaterButton;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);

        // C) Bake RoundedCorners now that every rect is sized.
        Canvas.ForceUpdateCanvases();
        foreach (var rounded in _roundedToRefresh)
            RefreshRounded(rounded);

        Debug.Log("[OnboardingAuthBlocksBuilder] Trust cards (WhatsApp + Telegram) + two per-channel success sheets built; Manager wa*/tg* fields stamped.");
    }

    // ── A) Trust card ──────────────────────────────────────────────────────────

    // Appends a green «Это безопасно» card as the LAST child of a code panel. The
    // panel uses a VerticalLayoutGroup, so the card renders at the bottom of the
    // stack; SetAsLastSibling keeps Manager's GetChild(3/4/5) / GetChild(3) indices
    // intact (Pitfall 2).
    private static void BuildTrustCard(GameObject codePanel, string bodyText)
    {
        const float cardHeight = 300f;

        DestroyAllByName(codePanel.transform, "TrustBlock");

        // Bordered card root (border rect + inner fill). Sized to the panel width so
        // the VLG (ChildForceExpandWidth) lays it flush; a LayoutElement guards against
        // whichever ControlWidth/Height flags the panel uses.
        var root = NewChild(codePanel, "TrustBlock", out var rootRt);
        rootRt.anchorMin = new Vector2(0f, 1f);
        rootRt.anchorMax = new Vector2(0f, 1f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.sizeDelta = new Vector2(PanelWidth, cardHeight);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = PanelWidth;
        le.minHeight = cardHeight;
        le.preferredHeight = cardHeight;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
        root.AddComponent<Image>().color = TrustBorder;
        AddRounded(root, CardRadius);

        var fill = NewChild(root, "Fill", out var fillRt);
        StretchFill(fillRt, BorderWidth);
        fill.AddComponent<Image>().color = TrustBg;
        AddRounded(fill, CardRadius - BorderWidth);

        // Lock icon on a pale-green disc, vertically centred at the card's left.
        var disc = NewChild(fill, "LockDisc", out var discRt);
        SetAnchors(discRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f));
        discRt.anchoredPosition = new Vector2(80f, 0f);
        discRt.sizeDelta = new Vector2(112f, 112f);
        disc.AddComponent<Image>().color = TrustDisc;
        AddRounded(disc, 56f);
        var lockGo = NewChild(disc, "Lock", out var lockRt);
        StretchFill(lockRt, 26f);
        AddIconImage(lockGo, _lock, LockTint);

        // Title «Это безопасно».
        var titleGo = NewChild(fill, "Title", out var titleRt);
        SetAnchors(titleRt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        titleRt.anchoredPosition = new Vector2(160f, -44f);
        titleRt.sizeDelta = new Vector2(770f, 64f);
        var titleTmp = AddText(titleGo, TrustTitleText, 48f, _semibold, TrustTitle);
        titleTmp.alignment = TextAlignmentOptions.TopLeft;

        // Channel-specific body.
        var bodyGo = NewChild(fill, "Body", out var bodyRt);
        SetAnchors(bodyRt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        bodyRt.anchoredPosition = new Vector2(160f, -120f);
        bodyRt.sizeDelta = new Vector2(770f, 160f);
        var bodyTmp = AddText(bodyGo, bodyText, 34f, _regular, Muted);
        bodyTmp.alignment = TextAlignmentOptions.TopLeft;
        bodyTmp.lineSpacing = 4f;

        // Index-safe: the card MUST be the last child so GetChild indices don't shift.
        root.transform.SetAsLastSibling();
    }

    // ── B) Success sheet ────────────────────────────────────────────────────────

    private struct SuccessRefs
    {
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Body;
        public Button PrimaryButton;
        public TextMeshProUGUI PrimaryLabel;
        public Button LaterButton;
    }

    // Builds one independent «Бот подключён!» sheet into a SuccessOverlay panel. The
    // sheet is a centred opaque card (larger than the small 520² panel it lives in;
    // the panel has no mask) with an animated check, title, body, Primary CTA and a
    // ghost «Позже». Idempotent via DestroyAllByName.
    private static SuccessRefs BuildSuccessSheet(GameObject successPanel)
    {
        DestroyAllByName(successPanel.transform, "SuccessCta");

        var sheet = NewChild(successPanel, "SuccessCta", out var sheetRt);
        SetAnchors(sheetRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        sheetRt.sizeDelta = new Vector2(880f, 1160f);
        sheetRt.anchoredPosition = Vector2.zero;
        var sheetBg = sheet.AddComponent<Image>();
        sheetBg.color = SheetBg;
        sheetBg.raycastTarget = true; // opaque sheet — covers the panel's vestigial content
        AddRounded(sheet, 48f);

        // Animated green check on a pale disc.
        var disc = NewChild(sheet, "CheckDisc", out var discRt);
        SetAnchors(discRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        discRt.anchoredPosition = new Vector2(0f, -72f);
        discRt.sizeDelta = new Vector2(168f, 168f);
        disc.AddComponent<Image>().color = CheckDisc;
        AddRounded(disc, 84f);
        disc.AddComponent<SuccessCheckPop>(); // DOScale 0.9→1 OutBack on every show
        var checkGo = NewChild(disc, "Check", out var checkRt);
        StretchFill(checkRt, 40f);
        AddIconImage(checkGo, _check, Color.white); // check PNG is already green

        // Title «Бот подключён!» (Manager also sets this at runtime).
        var titleGo = NewChild(sheet, "Title", out var titleRt);
        SetAnchors(titleRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        titleRt.anchoredPosition = new Vector2(0f, -292f);
        titleRt.sizeDelta = new Vector2(780f, 74f);
        var titleTmp = AddText(titleGo, SuccessTitleText, 56f, _bold, Ink);
        titleTmp.alignment = TextAlignmentOptions.Center;

        // Body.
        var bodyGo = NewChild(sheet, "Body", out var bodyRt);
        SetAnchors(bodyRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        bodyRt.anchoredPosition = new Vector2(0f, -388f);
        bodyRt.sizeDelta = new Vector2(720f, 230f);
        var bodyTmp = AddText(bodyGo, SuccessBodyText, 34f, _regular, Muted);
        bodyTmp.alignment = TextAlignmentOptions.Top;
        bodyTmp.lineSpacing = 6f;

        // Primary CTA «Загрузить прайс-лист».
        var primary = NewChild(sheet, "PrimaryButton", out var primaryRt);
        SetAnchors(primaryRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        primaryRt.anchoredPosition = new Vector2(0f, 184f);
        primaryRt.sizeDelta = new Vector2(760f, 132f);
        var primaryBg = primary.AddComponent<Image>();
        primaryBg.color = Primary;
        AddRounded(primary, CardRadius);
        var primaryButton = primary.AddComponent<Button>();
        primaryButton.targetGraphic = primaryBg;
        var primaryLabelGo = NewChild(primary, "Label", out var primaryLabelRt);
        StretchFill(primaryLabelRt);
        var primaryLabel = AddText(primaryLabelGo, SuccessPrimaryText, 40f, _semibold, Color.white);
        primaryLabel.alignment = TextAlignmentOptions.Center;

        // Ghost «Позже».
        var later = NewChild(sheet, "LaterButton", out var laterRt);
        SetAnchors(laterRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        laterRt.anchoredPosition = new Vector2(0f, 60f);
        laterRt.sizeDelta = new Vector2(760f, 104f);
        var laterBg = later.AddComponent<Image>();
        laterBg.color = GhostBg;
        AddRounded(later, CardRadius);
        var laterButton = later.AddComponent<Button>();
        laterButton.targetGraphic = laterBg;
        var laterLabelGo = NewChild(later, "Label", out var laterLabelRt);
        StretchFill(laterLabelRt);
        var laterLabel = AddText(laterLabelGo, SuccessLaterText, 36f, _semibold, Muted);
        laterLabel.alignment = TextAlignmentOptions.Center;

        sheet.transform.SetAsLastSibling();

        return new SuccessRefs
        {
            Title = titleTmp,
            Body = bodyTmp,
            PrimaryButton = primaryButton,
            PrimaryLabel = primaryLabel,
            LaterButton = laterButton,
        };
    }

    // ── Asset loading / import settings ─────────────────────────────────────────

    private static void EnsureIconImportSettings()
    {
        foreach (string path in new[] { LockIconPath, CheckIconPath })
        {
            if (!File.Exists(path)) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool dirty = importer.textureType != TextureImporterType.Sprite
                         || importer.spriteImportMode != SpriteImportMode.Single
                         || importer.mipmapEnabled
                         || importer.filterMode != FilterMode.Bilinear
                         || importer.wrapMode != TextureWrapMode.Clamp
                         || !importer.alphaIsTransparency;
            if (!dirty) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    private static void LoadAssets()
    {
        _regular = LoadFont(RegularGuid);
        _semibold = LoadFont(SemiboldGuid);
        _bold = LoadFont(BoldGuid);
        _lock = LoadSprite(LockIconPath);
        _check = LoadSprite(CheckIconPath);
    }

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[OnboardingAuthBlocksBuilder] Font missing for GUID {guid}");
        return font;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[OnboardingAuthBlocksBuilder] Sprite missing: {path}");
        return sprite;
    }

    // ── Low-level helpers (verbatim NavRestructureBuilder idiom) ───────────────

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    private static GameObject NewChild(GameObject parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
    }

    private static void StretchFill(RectTransform rt, float uniformInset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(uniformInset, uniformInset);
        rt.offsetMax = new Vector2(-uniformInset, -uniformInset);
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float size, TMP_FontAsset font, Color color)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        if (font != null) tmp.font = font;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        // This project's TMP default is NO wrapping — without this, long bodies
        // render one line tall and spill off the card.
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddIconImage(GameObject go, Sprite sprite, Color tint)
    {
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = tint;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private static void AddRounded(GameObject go, float radius)
    {
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = radius;
        _roundedToRefresh.Add(rounded);
    }

    private static void RefreshRounded(Component rounded)
    {
        if (rounded == null) return;
        switch (rounded)
        {
            case ImageWithRoundedCorners simple:
                simple.Validate();
                simple.Refresh();
                break;
            case ImageWithIndependentRoundedCorners independent:
                independent.Validate();
                independent.Refresh();
                break;
        }
    }

    private static void DestroyAllByName(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t != root && t.name == name)
                Object.DestroyImmediate(t.gameObject);
        }
    }
}
#endif
