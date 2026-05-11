#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class BotSwitcherSheetBuilder
{
    private const string SheetName = "Sheet_BotSwitcher";
    private const string RowName = "BotSwitcherRow";
    private const string PrefabHolderName = "BotSwitcherRowPrefabHolder";

    // Sheet shell
    private const float SheetHeight = 640f;
    private const float TopCornerRadius = 24f;

    // Drag handle
    private const float HandleAreaHeight = 24f;
    private const float HandleWidth = 36f;
    private const float HandleHeight = 4f;

    // Header + divider
    private const float HeaderHeight = 56f;
    private const float HeaderFontSize = 18f;
    private const float DividerHeight = 1f;

    // Row
    private const float RowHeight = 72f;
    private const float AvatarSize = 48f;
    private const float TrailingCheckSize = 20f;
    private const float SelectedBgRadius = 12f;
    private const float NameFontSize = 16f;
    private const float SubFontSize = 13f;
    private const float StatusDotSize = 8f;

    // Palette — kept inline so the look-and-feel is auditable from one file.
    private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 1f);
    private static readonly Color PanelColor = Color.white;
    private static readonly Color HandleColor = new Color(0.82f, 0.82f, 0.84f);
    private static readonly Color HeaderTextColor = new Color(0.10f, 0.10f, 0.12f);
    private static readonly Color DividerColor = new Color(0f, 0f, 0f, 0.06f);

    private static readonly Color RowNameColor = new Color(0.10f, 0.10f, 0.12f);
    private static readonly Color RowSubColor = new Color(0.45f, 0.45f, 0.48f);
    private static readonly Color SelectedTint = new Color(0.13f, 0.78f, 0.42f, 0.10f);
    private static readonly Color AccentGreen = new Color(0.13f, 0.78f, 0.42f);
    private static readonly Color StatusDisconnected = new Color(0.6f, 0.6f, 0.62f);
    private static readonly Color AvatarPlaceholder = new Color(0.85f, 0.85f, 0.85f);

    /// <summary>
    /// Constructs Sheet_BotSwitcher and the BotSwitcherRow prefab template under
    /// the Canvas. RUN FIRST — Screen_WhatsappHeaderRebuilder's title binder finds
    /// this sheet at runtime. After this, also re-run "Tools/Bot Switcher/Rebuild
    /// Row Avatar" so the Avatar picks up its 48px circular styling against the
    /// new row size.
    /// </summary>
    [MenuItem("Tools/Bot Switcher/Build Sheet")]
    public static void Build()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[BotSwitcherSheetBuilder] No Canvas found in scene. Open the Main scene first.");
            return;
        }

        Transform existing = canvas.transform.Find(SheetName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        GameObject sheet = BuildSheetRoot(canvas);
        GameObject backdrop = BuildBackdrop(sheet);
        GameObject panel = BuildPanel(sheet);
        BuildDragHandle(panel);
        BuildHeader(panel);
        BuildDivider(panel);
        RectTransform contentRT = BuildRowScroll(panel);

        GameObject row = BuildRowPrefab(canvas);

        var controller = sheet.GetComponent<BotSwitcherSheet>();
        var so = new SerializedObject(controller);
        so.FindProperty("backdropGroup").objectReferenceValue = backdrop.GetComponent<CanvasGroup>();
        so.FindProperty("backdropButton").objectReferenceValue = backdrop.GetComponent<Button>();
        so.FindProperty("sheetPanel").objectReferenceValue = panel.GetComponent<RectTransform>();
        so.FindProperty("rowContainer").objectReferenceValue = contentRT;
        so.FindProperty("rowPrefab").objectReferenceValue = row.GetComponent<BotSwitcherRowView>();
        so.ApplyModifiedPropertiesWithoutUndo();

        sheet.SetActive(false);
        Debug.Log(
            $"[BotSwitcherSheetBuilder] Built {SheetName} under {canvas.name}. " +
            $"Next: drag {RowName} from the hidden {PrefabHolderName} into Assets/Prefabs/ to overwrite BotSwitcherRow.prefab, " +
            $"re-wire BotSwitcherSheet.rowPrefab to the saved asset, " +
            $"then run 'Tools/Bot Switcher/Rebuild Row Avatar' so the Avatar picks up the new {AvatarSize}px circular styling.");
        Selection.activeGameObject = sheet;
        EditorSceneManager.MarkSceneDirty(sheet.scene);
    }

    private static GameObject BuildSheetRoot(Canvas canvas)
    {
        GameObject sheet = new GameObject(SheetName, typeof(RectTransform));
        sheet.transform.SetParent(canvas.transform, false);
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

        backdrop.GetComponent<Image>().color = BackdropColor; // alpha controlled by CanvasGroup
        var group = backdrop.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        return backdrop;
    }

    /// <summary>
    /// Bottom-anchored sheet panel (pivot Y = 0, anchor Y = 0) per the
    /// BotSwitcherSheet contract — the controller slides this up by its own
    /// height. Rounded only on the top corners so the bottom can sit flush
    /// against the safe-area edge without a visible gap.
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
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Simple;
        image.color = PanelColor;
        image.raycastTarget = true;

        // Top-only rounded corners. Vector4 mapping: x=TL, y=TR, z=BR, w=BL.
        var rounded = panel.AddComponent<ImageWithIndependentRoundedCorners>();
        rounded.r = new Vector4(TopCornerRadius, TopCornerRadius, 0f, 0f);
        rounded.Validate();
        rounded.Refresh();

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 16);
        layout.spacing = 0;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        return panel;
    }

    private static void BuildDragHandle(GameObject panel)
    {
        GameObject area = new GameObject("DragHandleArea",
            typeof(RectTransform), typeof(LayoutElement));
        area.transform.SetParent(panel.transform, false);
        var le = area.GetComponent<LayoutElement>();
        le.minHeight = HandleAreaHeight;
        le.preferredHeight = HandleAreaHeight;

        GameObject pill = new GameObject("Pill", typeof(RectTransform), typeof(Image));
        pill.transform.SetParent(area.transform, false);
        var pillRT = pill.GetComponent<RectTransform>();
        pillRT.anchorMin = new Vector2(0.5f, 0.5f);
        pillRT.anchorMax = new Vector2(0.5f, 0.5f);
        pillRT.pivot = new Vector2(0.5f, 0.5f);
        pillRT.sizeDelta = new Vector2(HandleWidth, HandleHeight);
        pillRT.anchoredPosition = Vector2.zero;

        var pillImage = pill.GetComponent<Image>();
        pillImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        pillImage.type = Image.Type.Simple;
        pillImage.color = HandleColor;
        pillImage.raycastTarget = false;

        var pillRounded = pill.AddComponent<ImageWithRoundedCorners>();
        pillRounded.radius = HandleHeight * 0.5f;
        pillRounded.Validate();
        pillRounded.Refresh();
    }

    private static void BuildHeader(GameObject panel)
    {
        GameObject header = new GameObject("Header",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        header.transform.SetParent(panel.transform, false);

        var text = header.GetComponent<TextMeshProUGUI>();
        text.text = "Select bot";
        text.fontSize = HeaderFontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = HeaderTextColor;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        var le = header.GetComponent<LayoutElement>();
        le.minHeight = HeaderHeight;
        le.preferredHeight = HeaderHeight;
    }

    private static void BuildDivider(GameObject panel)
    {
        GameObject divider = new GameObject("Divider",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        divider.transform.SetParent(panel.transform, false);

        var image = divider.GetComponent<Image>();
        image.color = DividerColor;
        image.raycastTarget = false;

        var le = divider.GetComponent<LayoutElement>();
        le.minHeight = DividerHeight;
        le.preferredHeight = DividerHeight;
    }

    /// <summary>
    /// Scrollable row container. flexibleHeight = 1 makes it absorb the
    /// remaining vertical space inside the panel below the header/divider.
    /// </summary>
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
        contentLayout.spacing = 4;
        contentLayout.padding = new RectOffset(8, 8, 8, 8);
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
    /// Hidden template under the Canvas; BotSwitcherSheet instantiates it at
    /// runtime. SelectedBackground = soft green pill behind the row when
    /// selected. The runtime view's "selectedAccentBar" SerializedField is
    /// repurposed to drive the trailing checkmark: BotSwitcherRowView only
    /// SetActives the GameObject on selection — any Image works there.
    /// </summary>
    private static GameObject BuildRowPrefab(Canvas canvas)
    {
        Transform existingHolder = canvas.transform.Find(PrefabHolderName);
        if (existingHolder != null) Object.DestroyImmediate(existingHolder.gameObject);

        GameObject holder = new GameObject(PrefabHolderName, typeof(RectTransform));
        holder.transform.SetParent(canvas.transform, false);
        holder.SetActive(false);

        GameObject row = new GameObject(RowName,
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(holder.transform, false);

        row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, RowHeight);
        row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // transparent base — clicks still hit
        var rowLE = row.GetComponent<LayoutElement>();
        rowLE.minHeight = RowHeight;
        rowLE.preferredHeight = RowHeight;

        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(16, 16, 12, 12);
        rowLayout.spacing = 12;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;

        Image selectedBg = BuildSelectedBackground(row);
        Image avatarImage = BuildAvatar(row);
        (TextMeshProUGUI nameText, TextMeshProUGUI subText, Image statusDot) = BuildTextStack(row);
        Image trailingCheck = BuildTrailingCheck(row);

        var rowView = row.AddComponent<BotSwitcherRowView>();
        var so = new SerializedObject(rowView);
        so.FindProperty("avatarImage").objectReferenceValue = avatarImage;
        so.FindProperty("nameLabel").objectReferenceValue = nameText;
        so.FindProperty("subLineLabel").objectReferenceValue = subText;
        so.FindProperty("statusDot").objectReferenceValue = statusDot;
        so.FindProperty("selectedBackground").objectReferenceValue = selectedBg;
        so.FindProperty("selectedAccentBar").objectReferenceValue = trailingCheck;
        so.FindProperty("rowButton").objectReferenceValue = row.GetComponent<Button>();
        so.ApplyModifiedPropertiesWithoutUndo();

        return row;
    }

    /// <summary>
    /// Full-row tinted pill that sits BEHIND avatar/stack/check when the row
    /// is selected. Anchored full-stretch with insets — but the row's
    /// HorizontalLayoutGroup would otherwise treat it as a flow child and
    /// shove everything else to its right. LayoutElement.ignoreLayout opts
    /// it out so the anchor-based positioning wins.
    /// </summary>
    private static Image BuildSelectedBackground(GameObject row)
    {
        GameObject bg = new GameObject("SelectedBackground",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        bg.transform.SetParent(row.transform, false);

        bg.GetComponent<LayoutElement>().ignoreLayout = true;

        var rt = bg.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8f, 4f);
        rt.offsetMax = new Vector2(-8f, -4f);

        var image = bg.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Simple;
        image.color = SelectedTint;
        image.raycastTarget = false;

        var rounded = bg.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = SelectedBgRadius;
        rounded.Validate();
        rounded.Refresh();

        bg.transform.SetAsFirstSibling();
        bg.SetActive(false);
        return image;
    }

    private static Image BuildAvatar(GameObject row)
    {
        GameObject avatar = new GameObject("Avatar",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        avatar.transform.SetParent(row.transform, false);

        avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(AvatarSize, AvatarSize);
        var le = avatar.GetComponent<LayoutElement>();
        le.preferredWidth = AvatarSize;
        le.preferredHeight = AvatarSize;
        le.minWidth = AvatarSize;
        le.minHeight = AvatarSize;

        // Final circular masking + IconSprite child are applied by
        // BotSwitcherRowAvatarRebuilder against the saved prefab asset.
        var image = avatar.GetComponent<Image>();
        image.color = AvatarPlaceholder;
        return image;
    }

    private static (TextMeshProUGUI nameText, TextMeshProUGUI subText, Image statusDot) BuildTextStack(GameObject row)
    {
        GameObject stack = new GameObject("Stack",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        stack.transform.SetParent(row.transform, false);

        var stackLayout = stack.GetComponent<VerticalLayoutGroup>();
        stackLayout.spacing = 2;
        stackLayout.childAlignment = TextAnchor.MiddleLeft;
        stackLayout.childForceExpandWidth = true;
        stackLayout.childForceExpandHeight = false;
        stackLayout.childControlWidth = true;
        stackLayout.childControlHeight = true;

        var stackLE = stack.GetComponent<LayoutElement>();
        stackLE.flexibleWidth = 1f;

        GameObject nameGO = new GameObject("Name",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(stack.transform, false);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = "Bot";
        nameText.fontSize = NameFontSize;
        nameText.fontStyle = FontStyles.Normal;
        nameText.color = RowNameColor;
        nameText.raycastTarget = false;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.enableWordWrapping = false;

        GameObject subRow = new GameObject("SubLine",
            typeof(RectTransform), typeof(HorizontalLayoutGroup));
        subRow.transform.SetParent(stack.transform, false);
        var subLayout = subRow.GetComponent<HorizontalLayoutGroup>();
        subLayout.spacing = 6;
        subLayout.childAlignment = TextAnchor.MiddleLeft;
        subLayout.childForceExpandWidth = false;
        subLayout.childForceExpandHeight = false;
        subLayout.childControlWidth = true;
        subLayout.childControlHeight = true;

        GameObject dotGO = new GameObject("StatusDot",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        dotGO.transform.SetParent(subRow.transform, false);
        dotGO.GetComponent<RectTransform>().sizeDelta = new Vector2(StatusDotSize, StatusDotSize);
        var dotLE = dotGO.GetComponent<LayoutElement>();
        dotLE.preferredWidth = StatusDotSize;
        dotLE.preferredHeight = StatusDotSize;
        dotLE.minWidth = StatusDotSize;
        dotLE.minHeight = StatusDotSize;

        var dotImage = dotGO.GetComponent<Image>();
        dotImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        dotImage.type = Image.Type.Simple;
        dotImage.color = StatusDisconnected;
        dotImage.raycastTarget = false;

        var dotRounded = dotGO.AddComponent<ImageWithRoundedCorners>();
        dotRounded.radius = StatusDotSize * 0.5f;
        dotRounded.Validate();
        dotRounded.Refresh();

        GameObject subTextGO = new GameObject("SubText",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        subTextGO.transform.SetParent(subRow.transform, false);
        var subText = subTextGO.GetComponent<TextMeshProUGUI>();
        subText.text = "WhatsApp not connected";
        subText.fontSize = SubFontSize;
        subText.color = RowSubColor;
        subText.raycastTarget = false;
        subText.overflowMode = TextOverflowModes.Ellipsis;
        subText.enableWordWrapping = false;
        var subLE = subTextGO.GetComponent<LayoutElement>();
        subLE.flexibleWidth = 1f;

        return (nameText, subText, dotImage);
    }

    /// <summary>
    /// Wired into BotSwitcherRowView.selectedAccentBar — the row view toggles
    /// this GameObject active when the row represents the active bot. Uses
    /// Unity's built-in checkmark sprite tinted with the accent green.
    /// </summary>
    private static Image BuildTrailingCheck(GameObject row)
    {
        GameObject check = new GameObject("TrailingCheck",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        check.transform.SetParent(row.transform, false);

        check.GetComponent<RectTransform>().sizeDelta = new Vector2(TrailingCheckSize, TrailingCheckSize);
        var le = check.GetComponent<LayoutElement>();
        le.preferredWidth = TrailingCheckSize;
        le.preferredHeight = TrailingCheckSize;
        le.minWidth = TrailingCheckSize;
        le.minHeight = TrailingCheckSize;

        var image = check.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        image.color = AccentGreen;
        image.preserveAspect = true;
        image.raycastTarget = false;

        check.SetActive(false);
        return image;
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
