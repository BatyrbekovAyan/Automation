#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds Sheet_BotSwitcher (status-card design) under the Canvas and saves the
/// row template directly to Assets/Prefabs/BotSwitcherRow.prefab — one menu
/// item, no manual prefab drag or follow-up avatar rebuild.
/// Spec: docs/superpowers/specs/2026-06-12-bot-switcher-status-cards-design.md
/// </summary>
public static class BotSwitcherSheetBuilder
{
    private const string SheetName = "Sheet_BotSwitcher";
    private const string RowName = "BotSwitcherRow";
    private const string RowPrefabPath = "Assets/Prefabs/BotSwitcherRow.prefab";
    private const string LegacyHolderName = "BotSwitcherRowPrefabHolder";
    // The sheet lives inside the chats-list panel (the screen it serves), the
    // same way AttachSheet lives inside MessagesPanel — not at canvas root.
    private const string ParentName = "ChatsPanel";
    private const string WaSpritePath = "Assets/Images/Icons/WhatsApp.svg.png";
    private const string TgSpritePath = "Assets/Images/Icons/Telegram_2019_Logo.svg.png";

    // All sizes in 1080x1920 canvas reference units (1 dp ~= 3 units).
    private const float SheetHeight = 1180f;
    private const float TopCornerRadius = 60f;

    private const float GrabberAreaHeight = 72f;
    private const float GrabberWidth = 108f;
    private const float GrabberHeight = 12f;

    private const float TitleHeight = 100f;
    private const float TitleFontSize = 44f;

    private const int ListSidePadding = 48;
    private const int ListTopPadding = 12;
    // Bottom padding includes home-indicator allowance — safe zones are baked
    // into sizes in this project, never read from Screen.safeArea at runtime.
    private const int ListBottomPadding = 96;
    private const float CardSpacing = 24f;

    private const float CardHeight = 228f;
    private const float CardRadius = 48f;
    private const float RingRadius = 54f;
    private const float RingInset = 6f;
    private const int CardPaddingX = 36;
    private const float CardContentSpacing = 36f;

    private const float AvatarSize = 144f;
    private const float AvatarIconSize = 92f;

    private const float NameFontSize = 42f;
    private const float StackSpacing = 12f;

    private const float ChipSpacing = 16f;
    private const float ChipHeight = 66f;
    private const float ChipRadius = 33f;
    private const int ChipPaddingX = 24;
    private const float ChipInnerGap = 12f;
    private const float ChipIconSize = 36f;
    private const float ChipFontSize = 28f;

    private const float BadgeSize = 60f;
    private const float BadgeCheckSize = 36f;
    // Nudge toward the card center so the badge sits on the rounded corner arc.
    private const float BadgeCornerInset = 16f;

    private static readonly Color BackdropColor = Color.black;
    private static readonly Color PanelColor = new Color(0.941f, 0.949f, 0.961f);
    private static readonly Color GrabberColor = new Color(0.78f, 0.78f, 0.80f);
    private static readonly Color TitleColor = new Color(0.102f, 0.102f, 0.180f);
    private static readonly Color CardColor = Color.white;
    private static readonly Color NameColor = new Color(0.102f, 0.102f, 0.180f);
    private static readonly Color AccentBlue = new Color(0.106f, 0.486f, 0.922f);
    private static readonly Color AvatarPlaceholder = new Color(0.85f, 0.85f, 0.85f);
    private static readonly Color ChipNeutralBg = new Color(0.925f, 0.925f, 0.933f);
    private static readonly Color ChipNeutralLabel = new Color(0.557f, 0.557f, 0.576f);

    [MenuItem("Tools/Bot Switcher/Build Sheet")]
    public static void Build()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[BotSwitcherSheetBuilder] No Canvas found in scene. Open the Main scene first.");
            return;
        }

        Sprite waSprite = AssetDatabase.LoadAssetAtPath<Sprite>(WaSpritePath);
        Sprite tgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TgSpritePath);
        if (waSprite == null || tgSprite == null)
        {
            Debug.LogError("[BotSwitcherSheetBuilder] Brand sprite missing or not imported as " +
                $"Sprite (2D and UI): {WaSpritePath} / {TgSpritePath}. Fix import settings, re-run.");
            return;
        }

        // Resolve the WhatsApp ChatsPanel via the title binder that opens this
        // sheet — it uniquely lives in that panel's TopBar, so walking up from
        // it can't land on another screen's panel (e.g. Telegram's).
        var titleBinder = Object.FindFirstObjectByType<BotSwitcherTitleBinder>(FindObjectsInactive.Include);
        Transform parent = titleBinder != null ? titleBinder.transform : null;
        while (parent != null && parent.name != ParentName) parent = parent.parent;
        if (parent == null)
        {
            Debug.LogError($"[BotSwitcherSheetBuilder] Could not resolve '{ParentName}' above BotSwitcherTitleBinder. Is the WhatsApp header built?");
            return;
        }

        // Idempotent: remove pre-existing sheets anywhere under the canvas
        // (covers the old canvas-root placement) plus the legacy row holder.
        foreach (var old in canvas.GetComponentsInChildren<BotSwitcherSheet>(includeInactive: true))
            Object.DestroyImmediate(old.gameObject);
        Transform legacyHolder = canvas.transform.Find(LegacyHolderName);
        if (legacyHolder != null) Object.DestroyImmediate(legacyHolder.gameObject);

        GameObject sheet = BuildSheetRoot(parent);
        GameObject backdrop = BuildBackdrop(sheet);
        GameObject panel = BuildPanel(sheet);
        BuildGrabber(panel);
        BuildTitle(panel);
        RectTransform contentRT = BuildRowScroll(panel);

        BotSwitcherRowView rowPrefab = BuildAndSaveRowPrefab(waSprite, tgSprite);
        if (rowPrefab == null)
        {
            Object.DestroyImmediate(sheet);
            return;
        }

        var controller = sheet.GetComponent<BotSwitcherSheet>();
        var so = new SerializedObject(controller);
        so.FindProperty("backdropGroup").objectReferenceValue = backdrop.GetComponent<CanvasGroup>();
        so.FindProperty("backdropButton").objectReferenceValue = backdrop.GetComponent<Button>();
        so.FindProperty("sheetPanel").objectReferenceValue = panel.GetComponent<RectTransform>();
        so.FindProperty("rowContainer").objectReferenceValue = contentRT;
        so.FindProperty("rowPrefab").objectReferenceValue = rowPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();

        sheet.SetActive(false);
        Selection.activeGameObject = sheet;
        EditorSceneManager.MarkSceneDirty(sheet.scene);
        Debug.Log($"[BotSwitcherSheetBuilder] Built {SheetName} and saved {RowPrefabPath}. No further manual steps.");
    }

    private static GameObject BuildSheetRoot(Transform parent)
    {
        GameObject sheet = new GameObject(SheetName, typeof(RectTransform));
        sheet.transform.SetParent(parent, false);
        sheet.transform.SetAsLastSibling();
        StretchFill(sheet.GetComponent<RectTransform>());
        sheet.AddComponent<BotSwitcherSheet>();
        return sheet;
    }

    private static GameObject BuildBackdrop(GameObject sheet)
    {
        GameObject backdrop = new GameObject("Backdrop",
            typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Button));
        backdrop.transform.SetParent(sheet.transform, false);
        StretchFill(backdrop.GetComponent<RectTransform>());

        backdrop.GetComponent<Image>().color = BackdropColor;
        var group = backdrop.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        return backdrop;
    }

    /// <summary>
    /// Bottom-anchored panel (pivot Y = 0, anchor Y = 0) per the BotSwitcherSheet
    /// contract — the controller slides it up by its own height.
    /// </summary>
    private static GameObject BuildPanel(GameObject sheet)
    {
        GameObject panel = new GameObject("Panel",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(sheet.transform, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, SheetHeight);

        var image = panel.GetComponent<Image>();
        image.color = PanelColor;
        image.raycastTarget = true;

        var rounded = panel.AddComponent<ImageWithIndependentRoundedCorners>();
        rounded.r = new Vector4(TopCornerRadius, TopCornerRadius, 0f, 0f);
        rounded.Validate();
        rounded.Refresh();

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 0;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        return panel;
    }

    private static void BuildGrabber(GameObject panel)
    {
        GameObject area = new GameObject("GrabberArea",
            typeof(RectTransform), typeof(LayoutElement));
        area.transform.SetParent(panel.transform, false);
        var le = area.GetComponent<LayoutElement>();
        le.minHeight = GrabberAreaHeight;
        le.preferredHeight = GrabberAreaHeight;

        GameObject pill = new GameObject("Pill", typeof(RectTransform), typeof(Image));
        pill.transform.SetParent(area.transform, false);
        var pillRT = pill.GetComponent<RectTransform>();
        pillRT.anchorMin = new Vector2(0.5f, 0.5f);
        pillRT.anchorMax = new Vector2(0.5f, 0.5f);
        pillRT.pivot = new Vector2(0.5f, 0.5f);
        pillRT.sizeDelta = new Vector2(GrabberWidth, GrabberHeight);

        var pillImage = pill.GetComponent<Image>();
        pillImage.color = GrabberColor;
        pillImage.raycastTarget = false;

        AddRoundedCorners(pill, GrabberHeight * 0.5f);
    }

    private static void BuildTitle(GameObject panel)
    {
        GameObject title = new GameObject("Title",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        title.transform.SetParent(panel.transform, false);

        var text = title.GetComponent<TextMeshProUGUI>();
        text.text = "Switch bot";
        text.fontSize = TitleFontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = TitleColor;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        var le = title.GetComponent<LayoutElement>();
        le.minHeight = TitleHeight;
        le.preferredHeight = TitleHeight;
    }

    private static RectTransform BuildRowScroll(GameObject panel)
    {
        GameObject scroll = new GameObject("RowScroll",
            typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(LayoutElement));
        scroll.transform.SetParent(panel.transform, false);

        scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        var le = scroll.GetComponent<LayoutElement>();
        le.minHeight = 200f;
        le.flexibleHeight = 1f;

        var scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.135f;

        GameObject viewport = new GameObject("Viewport",
            typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scroll.transform, false);
        StretchFill(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);

        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;

        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = CardSpacing;
        contentLayout.padding = new RectOffset(
            ListSidePadding, ListSidePadding, ListTopPadding, ListBottomPadding);
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.content = contentRT;
        return contentRT;
    }

    /// <summary>
    /// The row root image IS the selection ring: a rounded rect that stays
    /// Color.clear until BotSwitcherRowView paints it with the accent. The
    /// white card body (CardBg) is a child inset RingInset on all sides —
    /// RoundedCorners has no border mode and children always render above
    /// their parent, so ring-as-root is the only clean single-hierarchy way.
    /// A clear Image still raycasts, so the root keeps the Button.
    /// </summary>
    private static BotSwitcherRowView BuildAndSaveRowPrefab(Sprite waSprite, Sprite tgSprite)
    {
        GameObject row = new GameObject(RowName,
            typeof(RectTransform), typeof(Image), typeof(Button),
            typeof(CanvasGroup), typeof(LayoutElement));

        row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, CardHeight);
        var rowLE = row.GetComponent<LayoutElement>();
        rowLE.minHeight = CardHeight;
        rowLE.preferredHeight = CardHeight;

        var ringImage = row.GetComponent<Image>();
        ringImage.color = Color.clear;
        ringImage.raycastTarget = true;
        AddRoundedCorners(row, RingRadius);

        row.GetComponent<Button>().targetGraphic = ringImage;

        GameObject cardBg = BuildCardBg(row);
        Image avatarImage = BuildAvatar(cardBg, out Image avatarIcon);
        (TextMeshProUGUI nameText, var waChip, var tgChip) =
            BuildTextStack(cardBg, waSprite, tgSprite);
        GameObject badge = BuildSelectedBadge(row);

        var rowView = row.AddComponent<BotSwitcherRowView>();
        var so = new SerializedObject(rowView);
        so.FindProperty("ringImage").objectReferenceValue = ringImage;
        so.FindProperty("selectedBadge").objectReferenceValue = badge;
        so.FindProperty("canvasGroup").objectReferenceValue = row.GetComponent<CanvasGroup>();
        so.FindProperty("avatarImage").objectReferenceValue = avatarImage;
        so.FindProperty("avatarIcon").objectReferenceValue = avatarIcon;
        so.FindProperty("nameLabel").objectReferenceValue = nameText;
        so.FindProperty("waChipBg").objectReferenceValue = waChip.bg;
        so.FindProperty("waChipIcon").objectReferenceValue = waChip.icon;
        so.FindProperty("waChipLabel").objectReferenceValue = waChip.label;
        so.FindProperty("tgChipBg").objectReferenceValue = tgChip.bg;
        so.FindProperty("tgChipIcon").objectReferenceValue = tgChip.icon;
        so.FindProperty("tgChipLabel").objectReferenceValue = tgChip.label;
        so.FindProperty("rowButton").objectReferenceValue = row.GetComponent<Button>();
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(row, RowPrefabPath, out bool success);
        Object.DestroyImmediate(row);
        if (!success || saved == null)
        {
            Debug.LogError($"[BotSwitcherSheetBuilder] Failed to save {RowPrefabPath}.");
            return null;
        }
        return saved.GetComponent<BotSwitcherRowView>();
    }

    private static GameObject BuildCardBg(GameObject row)
    {
        GameObject cardBg = new GameObject("CardBg",
            typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        cardBg.transform.SetParent(row.transform, false);

        var rt = cardBg.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(RingInset, RingInset);
        rt.offsetMax = new Vector2(-RingInset, -RingInset);

        var image = cardBg.GetComponent<Image>();
        image.color = CardColor;
        image.raycastTarget = false;
        AddRoundedCorners(cardBg, CardRadius);

        var layout = cardBg.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(CardPaddingX, CardPaddingX, 0, 0);
        layout.spacing = CardContentSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        return cardBg;
    }

    private static Image BuildAvatar(GameObject cardBg, out Image avatarIcon)
    {
        GameObject avatar = new GameObject("Avatar",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        avatar.transform.SetParent(cardBg.transform, false);

        var le = avatar.GetComponent<LayoutElement>();
        le.preferredWidth = AvatarSize;
        le.preferredHeight = AvatarSize;
        le.minWidth = AvatarSize;
        le.minHeight = AvatarSize;

        var image = avatar.GetComponent<Image>();
        image.color = AvatarPlaceholder;
        image.raycastTarget = false;
        AddRoundedCorners(avatar, AvatarSize * 0.5f);

        GameObject iconGO = new GameObject("IconSprite",
            typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(avatar.transform, false);
        var iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.sizeDelta = new Vector2(AvatarIconSize, AvatarIconSize);

        avatarIcon = iconGO.GetComponent<Image>();
        avatarIcon.raycastTarget = false;
        avatarIcon.preserveAspect = true;
        avatarIcon.enabled = false;
        return image;
    }

    private static (TextMeshProUGUI nameText,
        (Image bg, Image icon, TextMeshProUGUI label) waChip,
        (Image bg, Image icon, TextMeshProUGUI label) tgChip)
        BuildTextStack(GameObject cardBg, Sprite waSprite, Sprite tgSprite)
    {
        GameObject stack = new GameObject("Stack",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        stack.transform.SetParent(cardBg.transform, false);

        var stackLayout = stack.GetComponent<VerticalLayoutGroup>();
        stackLayout.spacing = StackSpacing;
        stackLayout.childAlignment = TextAnchor.MiddleLeft;
        stackLayout.childForceExpandWidth = true;
        stackLayout.childForceExpandHeight = false;
        stackLayout.childControlWidth = true;
        stackLayout.childControlHeight = true;
        stack.GetComponent<LayoutElement>().flexibleWidth = 1f;

        GameObject nameGO = new GameObject("Name",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(stack.transform, false);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = "Bot";
        nameText.fontSize = NameFontSize;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = NameColor;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        nameText.raycastTarget = false;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.enableWordWrapping = false;

        GameObject chipRow = new GameObject("ChipRow",
            typeof(RectTransform), typeof(HorizontalLayoutGroup));
        chipRow.transform.SetParent(stack.transform, false);
        var chipRowLayout = chipRow.GetComponent<HorizontalLayoutGroup>();
        chipRowLayout.spacing = ChipSpacing;
        chipRowLayout.childAlignment = TextAnchor.MiddleLeft;
        chipRowLayout.childForceExpandWidth = false;
        chipRowLayout.childForceExpandHeight = false;
        chipRowLayout.childControlWidth = true;
        chipRowLayout.childControlHeight = true;

        var waChip = BuildChip(chipRow, "Chip_WhatsApp", waSprite, "WhatsApp");
        var tgChip = BuildChip(chipRow, "Chip_Telegram", tgSprite, "Telegram");

        return (nameText, waChip, tgChip);
    }

    private static (Image bg, Image icon, TextMeshProUGUI label) BuildChip(
        GameObject chipRow, string name, Sprite brandSprite, string labelText)
    {
        GameObject chip = new GameObject(name,
            typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        chip.transform.SetParent(chipRow.transform, false);

        var le = chip.GetComponent<LayoutElement>();
        le.minHeight = ChipHeight;
        le.preferredHeight = ChipHeight;

        var bg = chip.GetComponent<Image>();
        bg.color = ChipNeutralBg;
        bg.raycastTarget = false;
        AddRoundedCorners(chip, ChipRadius);

        var layout = chip.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(ChipPaddingX, ChipPaddingX, 0, 0);
        layout.spacing = ChipInnerGap;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        GameObject iconGO = new GameObject("Icon",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        iconGO.transform.SetParent(chip.transform, false);
        var iconLE = iconGO.GetComponent<LayoutElement>();
        iconLE.preferredWidth = ChipIconSize;
        iconLE.preferredHeight = ChipIconSize;
        iconLE.minWidth = ChipIconSize;
        iconLE.minHeight = ChipIconSize;
        var icon = iconGO.GetComponent<Image>();
        icon.sprite = brandSprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        GameObject labelGO = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(chip.transform, false);
        var label = labelGO.GetComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = ChipFontSize;
        label.color = ChipNeutralLabel;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.enableWordWrapping = false;

        return (bg, icon, label);
    }

    private static GameObject BuildSelectedBadge(GameObject row)
    {
        GameObject badge = new GameObject("SelectedBadge",
            typeof(RectTransform), typeof(Image));
        badge.transform.SetParent(row.transform, false);

        var rt = badge.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-BadgeCornerInset, -BadgeCornerInset);
        rt.sizeDelta = new Vector2(BadgeSize, BadgeSize);

        var image = badge.GetComponent<Image>();
        image.color = AccentBlue;
        image.raycastTarget = false;
        AddRoundedCorners(badge, BadgeSize * 0.5f);

        GameObject check = new GameObject("Check", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(badge.transform, false);
        var checkRT = check.GetComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0.5f, 0.5f);
        checkRT.anchorMax = new Vector2(0.5f, 0.5f);
        checkRT.pivot = new Vector2(0.5f, 0.5f);
        checkRT.sizeDelta = new Vector2(BadgeCheckSize, BadgeCheckSize);

        var checkImage = check.GetComponent<Image>();
        checkImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        checkImage.color = Color.white;
        checkImage.preserveAspect = true;
        checkImage.raycastTarget = false;

        badge.SetActive(false);
        return badge;
    }

    private static void AddRoundedCorners(GameObject go, float radius)
    {
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = radius;
        rounded.Validate();
        rounded.Refresh();
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
#endif
