#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds Профиль → «Подписка» (Task 14b, spec §6): a seventh ProfileSubPages panel
/// plus the list row that opens it.
///
/// ADDITIVE by construction. <c>ProfileSubPagesBuilder</c> is a destroy-and-rebuild
/// builder that still writes the pre-theme LIGHT literals, so re-running it to add a
/// page would wipe the owner's hand-tuning and every ThemedColor binding in
/// Screen_Profile (see «Scene is source of truth»). This builder instead:
///
///  • CLONES the list row out of Section1 — the row already carries the correct 150u
///    height, HLG padding, squircle geometry and theme bindings, all adjusted by hand
///    after the original builder ran (the ProfileThemeToggleBuilder idiom);
///  • destroys and rebuilds ONLY its own <c>PanelSubscription</c>, whose shell is
///    cloned from a sibling panel so it inherits the same hand-tuned header/scroll/
///    swipe chrome, then filled with freshly built, theme-bound content;
///  • touches nothing else in the scene.
///
/// Idempotent: run it twice and the scene is byte-equivalent. All sizes are 1080×1920
/// canvas reference units. Save the scene after running (the headless entry saves).
/// </summary>
public static class SubscriptionPageBuilder
{
    // ── Design tokens (reference units) ──────────────────────────────────────
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string PanelName = "PanelSubscription";
    private const string RowName = "ПодпискаRow";
    private const string SectionPath = "ScrollView/Viewport/Content/Section1";

    private const float CardRadius = 40f;
    private const float RowHeight = 150f;
    private const float IconSize = 100f;
    private const float IconRadius = 28f;
    private const float IconGlyphInset = 24f;
    private const float BarHeight = 18f;
    private const float PillHeight = 56f;
    private const float PillRadius = 28f;

    // Fonts by GUID (the default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string MediumGuid = "d091b0cad5d964a53a41de97ba932a27";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";

    private const string IconsDir = "Assets/Images/ProfileSubPages";
    private const string CardIconPath = IconsDir + "/PS_Card.png";
    private const string DialogIconPath = IconsDir + "/PS_Bubble.png";
    private const string RestoreIconPath = "Assets/Images/Chat/Relaod.png";
    private const string CancelIconPath = "Assets/Images/New/Close.png";
    private const string ChevronRightPath = "Assets/Images/Chat/chevron-right.png";

    // Decorative squircle hues, literal like five of the six rows already in Section1
    // (only «Аккаунт» is theme-bound). Teal is the one family hue not yet spent.
    private static readonly Color RowIconTeal = Hex("#00897B");
    private static readonly Color IconBlue = Hex("#1B7CEB");
    private static readonly Color IconGreen = Hex("#4CAF50");
    private static readonly Color IconSlate = Hex("#607D8B");

    private static TMP_FontAsset _regular, _medium, _semibold;
    private static Sprite _cardIcon, _dialogIcon, _restoreIcon, _cancelIcon, _chevronRight;
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    // ── Entry points ─────────────────────────────────────────────────────────

    [MenuItem("Tools/Billing/Build Subscription Page")]
    public static void Build()
    {
        GameObject panel = BuildInternal();
        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(panel.scene);
        Debug.Log("[SubscriptionPageBuilder] Build complete: PanelSubscription + ПодпискаRow. SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod SubscriptionPageBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SubscriptionPageBuilder] Headless build + save complete.");
    }

    // ── Main build ───────────────────────────────────────────────────────────

    private static GameObject BuildInternal()
    {
        AssetDatabase.Refresh();
        EnsureIconImportSettings();
        LoadAssets();
        _roundedToRefresh.Clear();

        var profilePage = Object.FindFirstObjectByType<ProfilePage>(FindObjectsInactive.Include);
        if (profilePage == null)
            throw new System.InvalidOperationException("[SubscriptionPageBuilder] ProfilePage not found — is Main.unity open?");
        Transform screenProfile = profilePage.transform;

        var subPages = screenProfile.GetComponentInChildren<ProfileSubPages>(true);
        if (subPages == null)
            throw new System.InvalidOperationException("[SubscriptionPageBuilder] ProfileSubPages root not found — run Tools/Profile Sub-Pages/Build first.");

        GameObject panel = BuildPanel(subPages.transform);
        var so = new SerializedObject(subPages);
        BuildContent(panel, so);
        StampPage(so, panel);
        so.ApplyModifiedPropertiesWithoutUndo();

        BuildListRow(screenProfile, profilePage);

        // Radius bake needs sized rects; do it while the panel is still active.
        Canvas.ForceUpdateCanvases();
        foreach (Component rounded in _roundedToRefresh)
            RefreshRounded(rounded);

        panel.SetActive(false);
        return panel;
    }

    // ── Panel shell (cloned from a hand-tuned sibling) ───────────────────────

    private static GameObject BuildPanel(Transform subPages)
    {
        // Idempotent: destroy only the panel this builder owns.
        for (int i = subPages.childCount - 1; i >= 0; i--)
            if (subPages.GetChild(i).name == PanelName)
                Object.DestroyImmediate(subPages.GetChild(i).gameObject);

        Transform template = subPages.Cast<Transform>()
            .FirstOrDefault(t => t.name == "PanelPrivacy" || t.name == "PanelAbout");
        if (template == null)
            throw new System.InvalidOperationException("[SubscriptionPageBuilder] No sibling panel to clone the shell from.");

        var panel = Object.Instantiate(template.gameObject, subPages);
        panel.name = PanelName;
        panel.SetActive(true);   // content build needs live rects; put away at the end

        // Panels must stay BEFORE ConfirmPopup, which has to draw over any of them.
        Transform confirm = subPages.Cast<Transform>().FirstOrDefault(t => t.name == "ConfirmPopup");
        panel.transform.SetSiblingIndex(confirm != null ? confirm.GetSiblingIndex() : subPages.childCount - 1);

        var title = panel.transform.Find("Header/Title")?.GetComponent<TextMeshProUGUI>();
        if (title != null) title.text = SubscriptionPageRows.PageTitle;

        // The clone's SwipeToBackPanel still points at the TEMPLATE's rect/scroll.
        var swipe = panel.GetComponentInChildren<SwipeToBackPanel>(true);
        var scroll = panel.transform.Find("ScrollView")?.GetComponent<ScrollRect>();
        if (swipe != null)
        {
            var swipeSo = new SerializedObject(swipe);
            swipeSo.FindProperty("panelToSlide").objectReferenceValue = (RectTransform)panel.transform;
            swipeSo.FindProperty("contentScrollRect").objectReferenceValue = scroll;
            swipeSo.ApplyModifiedPropertiesWithoutUndo();
        }
        var passthrough = panel.GetComponentInChildren<ClickPassthrough>(true);
        if (passthrough != null) passthrough.allowedPanel = panel.transform;

        // Wipe the cloned page's content; the shell (header/scroll/swipe) is what we keep.
        Transform content = panel.transform.Find("ScrollView/Viewport/Content");
        if (content == null)
            throw new System.InvalidOperationException("[SubscriptionPageBuilder] Cloned panel has no Content column.");
        for (int i = content.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(content.GetChild(i).gameObject);

        // …and anything the template hung outside the scroll column (e.g. the support sheet).
        foreach (string stray in new[] { "SupportCta", "SupportSheet" })
        {
            Transform found = panel.transform.Find(stray);
            if (found != null) Object.DestroyImmediate(found.gameObject);
        }

        return panel;
    }

    // ── Page content ─────────────────────────────────────────────────────────

    private static void BuildContent(GameObject panel, SerializedObject so)
    {
        GameObject content = panel.transform.Find("ScrollView/Viewport/Content").gameObject;

        MakeCaption(content, SubscriptionPageRows.PlanCaption);
        BuildPlanCard(content, so);

        MakeCaption(content, SubscriptionPageRows.ActionsCaption);
        BuildActionsCard(content, so);

        var notice = NewChild(content, "Notice", out _);
        var noticeTmp = AddText(notice, "", 30f, _regular, ThemeRole.InkTertiary);
        noticeTmp.alignment = TextAlignmentOptions.TopLeft;
        noticeTmp.margin = new Vector4(16f, -8f, 16f, 0f);
        notice.SetActive(false);
        so.FindProperty("subNotice").objectReferenceValue = noticeTmp;

        BuildCancelCard(content, so);
    }

    private static void BuildPlanCard(GameObject parent, SerializedObject so)
    {
        GameObject card = MakeCard(parent, "PlanCard");

        // Head: title + status pill, then the subline.
        var head = NewChild(card, "Head", out _);
        AddVerticalGroup(head, new RectOffset(44, 44, 40, 32), 12f);

        var titleRow = NewChild(head, "TitleRow", out _);
        AddHorizontalGroup(titleRow, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.MiddleLeft);

        var titleGo = NewChild(titleRow, "Title", out _);
        var title = AddText(titleGo, "Бизнес", 44f, _semibold, ThemeRole.InkPrimary);
        var titleLe = titleGo.AddComponent<LayoutElement>();
        titleLe.preferredWidth = 0f;    // mask the label's appetite so the pill is never squeezed
        titleLe.flexibleWidth = 1f;

        var pill = NewChild(titleRow, "StatusPill", out _);
        var pillLe = pill.AddComponent<LayoutElement>();
        pillLe.minHeight = PillHeight;
        pillLe.preferredHeight = PillHeight;
        pillLe.flexibleWidth = 0f;
        var pillBg = pill.AddComponent<Image>();
        pillBg.color = Color.white;
        pillBg.raycastTarget = false;
        AddRounded(pill, PillRadius);
        var pillBgTheme = pill.AddComponent<ThemedColor>();
        pillBgTheme.Configure(ThemeRole.PositiveBg, pillBg);
        AddHorizontalGroup(pill, new RectOffset(24, 24, 0, 0), 0f, TextAnchor.MiddleCenter);

        var pillLabelGo = NewChild(pill, "Label", out _);
        var pillLabel = AddText(pillLabelGo, SubscriptionPageRows.PillActive, 28f, _semibold, null);
        pillLabel.alignment = TextAlignmentOptions.Midline;
        pillLabel.textWrappingMode = TextWrappingModes.NoWrap;
        var pillInkTheme = pillLabelGo.AddComponent<ThemedColor>();
        pillInkTheme.Configure(ThemeRole.PositiveInk, pillLabel);

        var sublineGo = NewChild(head, "Subline", out _);
        var subline = AddText(sublineGo, "", 36f, _regular, ThemeRole.InkSecondary);

        MakeDivider(card);

        // Meters.
        var meters = NewChild(card, "Meters", out _);
        AddVerticalGroup(meters, new RectOffset(44, 44, 36, 40), 20f);

        var dialogsValue = MakeMeterRow(meters, SubscriptionPageRows.DialogsTitle, out _);

        var bar = NewChild(meters, "Bar", out _);
        SetPreferredHeight(bar, BarHeight);
        var barImg = bar.AddComponent<Image>();
        barImg.color = Color.white;
        barImg.raycastTarget = false;
        Themed(bar, ThemeRole.Hairline);
        AddRounded(bar, BarHeight / 2f);

        var fill = NewChild(bar, "Fill", out RectTransform fillRt);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0.5f, 1f);   // rewritten every render from the live fraction
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = Color.white;
        fillImg.raycastTarget = false;
        var fillTheme = fill.AddComponent<ThemedColor>();
        fillTheme.Configure(ThemeRole.AccentFill, fillImg);
        AddRounded(fill, BarHeight / 2f);

        var botsValue = MakeMeterRow(meters, SubscriptionPageRows.BotsTitle, out _);
        var channelsValue = MakeMeterRow(meters, SubscriptionPageRows.ChannelsTitle, out _);

        so.FindProperty("subPlanTitle").objectReferenceValue = title;
        so.FindProperty("subPlanSubline").objectReferenceValue = subline;
        so.FindProperty("subPillLabel").objectReferenceValue = pillLabel;
        so.FindProperty("subPillBgTheme").objectReferenceValue = pillBgTheme;
        so.FindProperty("subPillInkTheme").objectReferenceValue = pillInkTheme;
        so.FindProperty("subDialogsValue").objectReferenceValue = dialogsValue;
        so.FindProperty("subQuotaFill").objectReferenceValue = fillRt;
        so.FindProperty("subQuotaFillTheme").objectReferenceValue = fillTheme;
        so.FindProperty("subBotsValue").objectReferenceValue = botsValue;
        so.FindProperty("subChannelsValue").objectReferenceValue = channelsValue;
    }

    private static void BuildActionsCard(GameObject parent, SerializedObject so)
    {
        GameObject card = MakeCard(parent, "ActionsCard");

        Button change = MakeActionRow(card, "Row_ChangePlan", SubscriptionPageRows.ChangePlanRow, _cardIcon, IconBlue, out _);
        MakeDivider(card);
        Button topUp = MakeActionRow(card, "Row_TopUp", SubscriptionPageRows.TopUpRowText(), _dialogIcon, IconGreen, out var topUpLabel);
        MakeDivider(card);
        Button restore = MakeActionRow(card, "Row_Restore", SubscriptionPageRows.RestoreRow, _restoreIcon, IconSlate, out _);

        so.FindProperty("subChangePlanButton").objectReferenceValue = change;
        so.FindProperty("subTopUpButton").objectReferenceValue = topUp;
        so.FindProperty("subTopUpLabel").objectReferenceValue = topUpLabel;
        so.FindProperty("subRestoreButton").objectReferenceValue = restore;
    }

    private static void BuildCancelCard(GameObject parent, SerializedObject so)
    {
        GameObject card = MakeCard(parent, "CancelCard");
        Button cancel = MakeActionRow(card, "Row_Cancel", SubscriptionPageRows.CancelRow, _cancelIcon,
            Color.white, out TextMeshProUGUI label, iconBgRole: ThemeRole.DestructiveSoft,
            glyphRole: ThemeRole.Destructive, labelRole: ThemeRole.Destructive);

        var caption = NewChild(parent, "CancelCaption", out _);
        var captionTmp = AddText(caption, SubscriptionPageRows.CancelCaption, 30f, _regular, ThemeRole.InkTertiary);
        captionTmp.alignment = TextAlignmentOptions.TopLeft;
        captionTmp.margin = new Vector4(16f, -8f, 16f, 0f);
        captionTmp.lineSpacing = 8f;

        so.FindProperty("subCancelCard").objectReferenceValue = card;
        so.FindProperty("subCancelCaption").objectReferenceValue = caption;
        so.FindProperty("subCancelButton").objectReferenceValue = cancel;
        if (label == null) Debug.LogWarning("[SubscriptionPageBuilder] Cancel row has no label.");
    }

    private static void StampPage(SerializedObject so, GameObject panel)
    {
        SerializedProperty pages = so.FindProperty("pages");
        int index = (int)ProfileSubPages.Page.Subscription;
        if (pages.arraySize <= index) pages.arraySize = index + 1;

        SerializedProperty entry = pages.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("panel").objectReferenceValue = (RectTransform)panel.transform;
        entry.FindPropertyRelative("backButton").objectReferenceValue =
            panel.transform.Find("Header/BackButton")?.GetComponent<Button>();
        entry.FindPropertyRelative("swipe").objectReferenceValue =
            panel.GetComponentInChildren<SwipeToBackPanel>(true);
    }

    // ── Profile list row (cloned, ProfileThemeToggleBuilder idiom) ───────────

    private static void BuildListRow(Transform screenProfile, ProfilePage profilePage)
    {
        Transform section = screenProfile.Find(SectionPath);
        if (section == null)
        {
            Debug.LogError($"[SubscriptionPageBuilder] {SectionPath} not found — row skipped.");
            return;
        }

        Transform existing = section.Find(RowName);
        GameObject row;
        if (existing != null)
        {
            row = existing.gameObject;
        }
        else
        {
            // «Аккаунт» is the row this one belongs beside; cloning it inherits the
            // section's tuned 150u height, HLG padding and theme bindings.
            Transform template = section.Cast<Transform>().FirstOrDefault(t => t.name.EndsWith("Row"));
            if (template == null)
            {
                Debug.LogError("[SubscriptionPageBuilder] Section1 has no row to clone.");
                return;
            }

            int insertAt = template.GetSiblingIndex() + 1;
            Transform dividerTemplate = section.Cast<Transform>().FirstOrDefault(t => t.name == "Divider");
            if (dividerTemplate != null)
            {
                var divider = Object.Instantiate(dividerTemplate.gameObject, section);
                divider.name = "Divider";
                divider.transform.SetSiblingIndex(insertAt++);
            }

            var clone = Object.Instantiate(template.gameObject, section);
            clone.name = RowName;
            clone.transform.SetSiblingIndex(insertAt);
            row = clone;
        }

        // Re-stamp the row's identity every run so a re-build repairs a hand-edit drift.
        var label = row.transform.Find("Label/Text")?.GetComponent<TextMeshProUGUI>();
        if (label != null) label.text = SubscriptionPageRows.PageTitle;

        Transform iconBg = row.transform.Find("IconBg");
        if (iconBg != null)
        {
            // The «Аккаунт» template binds its squircle to AccentFill; this row carries a
            // literal hue like the other five, so that binding must not survive the clone
            // (it would repaint the teal on every theme event).
            var stale = iconBg.GetComponent<ThemedColor>();
            if (stale != null) Object.DestroyImmediate(stale);

            var bg = iconBg.GetComponent<Image>();
            if (bg != null) bg.color = RowIconTeal;

            var glyph = iconBg.Find("Icon")?.GetComponent<Image>();
            if (glyph != null)
            {
                glyph.sprite = _cardIcon;
                glyph.color = Color.white;
                glyph.preserveAspect = true;
            }
        }

        var button = row.GetComponent<Button>();
        var pageSo = new SerializedObject(profilePage);
        SerializedProperty prop = pageSo.FindProperty("subscriptionButton");
        if (prop == null)
        {
            Debug.LogError("[SubscriptionPageBuilder] ProfilePage.subscriptionButton missing — compile first.");
            return;
        }
        prop.objectReferenceValue = button;
        pageSo.ApplyModifiedPropertiesWithoutUndo();

        ResizeSection(section);
    }

    /// <summary>
    /// Section1's VerticalLayoutGroup has childControlHeight OFF, so it sizes itself from
    /// the children's own sizeDelta — and the section's rect + LayoutElement must both be
    /// re-derived by hand or Content packs Section2 straight over the new row.
    /// </summary>
    private static void ResizeSection(Transform section)
    {
        var vlg = section.GetComponent<VerticalLayoutGroup>();
        float height = vlg != null ? vlg.padding.vertical : 0f;
        int visible = 0;
        foreach (Transform child in section)
        {
            if (!child.gameObject.activeSelf) continue;
            height += ((RectTransform)child).sizeDelta.y;
            visible++;
        }
        if (vlg != null) height += vlg.spacing * Mathf.Max(0, visible - 1);

        var rt = (RectTransform)section;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        var le = section.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.minHeight = height;
            le.preferredHeight = height;
        }
        Debug.Log($"[SubscriptionPageBuilder] Section1 re-sized to {height} over {visible} children.");
    }

    // ── Content primitives (ProfileSubPagesBuilder metrics, theme-bound) ─────

    private static GameObject MakeCard(GameObject parent, string name)
    {
        var card = NewChild(parent, name, out _);
        var img = card.AddComponent<Image>();
        img.color = Color.white;
        Themed(card, ThemeRole.Surface);
        AddRounded(card, CardRadius);
        // Zero padding: rows carry their own, which is what makes dividers full-bleed.
        AddVerticalGroup(card, new RectOffset(0, 0, 0, 0), 0f);
        return card;
    }

    private static void MakeCaption(GameObject parent, string text)
    {
        var go = NewChild(parent, "Caption", out _);
        var tmp = AddText(go, text, 30f, _semibold, ThemeRole.InkSecondary);
        tmp.characterSpacing = 6f;
        tmp.margin = new Vector4(12f, 24f, 12f, 0f);
    }

    private static void MakeDivider(GameObject card)
    {
        var divider = NewChild(card, "Divider", out _);
        SetPreferredHeight(divider, 2f);
        var img = divider.AddComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = false;
        Themed(divider, ThemeRole.Hairline);
    }

    /// <summary>«Диалоги ИИ» ————— «412 из 1 000». Returns the VALUE label.</summary>
    private static TextMeshProUGUI MakeMeterRow(GameObject parent, string label, out TextMeshProUGUI labelTmp)
    {
        var row = NewChild(parent, "Meter_" + label, out _);
        AddHorizontalGroup(row, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.MiddleLeft);

        var labelGo = NewChild(row, "Label", out _);
        labelTmp = AddText(labelGo, label, 38f, _regular, ThemeRole.InkSecondary);
        var labelLe = labelGo.AddComponent<LayoutElement>();
        labelLe.preferredWidth = 0f;
        labelLe.flexibleWidth = 1f;

        var valueGo = NewChild(row, "Value", out _);
        var value = AddText(valueGo, "", 38f, _semibold, ThemeRole.InkPrimary);
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.textWrappingMode = TextWrappingModes.NoWrap;
        valueGo.AddComponent<LayoutElement>().flexibleWidth = 0f;
        return value;
    }

    /// <summary>Tappable row: squircle icon + label + chevron. Returns its Button.</summary>
    private static Button MakeActionRow(GameObject card, string name, string text, Sprite glyph,
        Color iconColor, out TextMeshProUGUI label, ThemeRole? iconBgRole = null,
        ThemeRole? glyphRole = null, ThemeRole labelRole = ThemeRole.InkPrimary)
    {
        // Stable node name, not the label: the top-up row's text is re-stamped from the
        // seam at render time and would otherwise leave the name lying about its content.
        var row = NewChild(card, name, out _);
        SetPreferredHeight(row, RowHeight);
        var hit = row.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        var button = row.AddComponent<Button>();
        button.targetGraphic = hit;
        AddHorizontalGroup(row, new RectOffset(44, 54, 27, 27), 40f, TextAnchor.MiddleLeft);

        var iconBg = NewChild(row, "IconBg", out _);
        SetFixedSize(iconBg, IconSize, IconSize);
        var iconImg = iconBg.AddComponent<Image>();
        iconImg.color = iconColor;
        iconImg.raycastTarget = false;
        if (iconBgRole.HasValue) Themed(iconBg, iconBgRole.Value);
        AddRounded(iconBg, IconRadius);

        var icon = NewChild(iconBg, "Icon", out RectTransform iconRt);
        StretchFill(iconRt, IconGlyphInset);
        var glyphImg = icon.AddComponent<Image>();
        glyphImg.sprite = glyph;
        glyphImg.color = Color.white;
        glyphImg.preserveAspect = true;
        glyphImg.raycastTarget = false;
        if (glyphRole.HasValue) Themed(icon, glyphRole.Value);

        var labelGo = NewChild(row, "Label", out _);
        label = AddText(labelGo, text, 42f, _medium, labelRole);
        var labelLe = labelGo.AddComponent<LayoutElement>();
        labelLe.preferredWidth = 0f;
        labelLe.flexibleWidth = 1f;

        var chevron = NewChild(row, "Chevron", out _);
        SetFixedSize(chevron, 32f, 32f);
        var chevronImg = chevron.AddComponent<Image>();
        chevronImg.sprite = _chevronRight;
        chevronImg.color = Color.white;
        chevronImg.preserveAspect = true;
        chevronImg.raycastTarget = false;
        Themed(chevron, ThemeRole.InkSecondary);

        return button;
    }

    // ── Assets ───────────────────────────────────────────────────────────────

    private static void LoadAssets()
    {
        _regular = LoadFont(RegularGuid);
        _medium = LoadFont(MediumGuid);
        _semibold = LoadFont(SemiboldGuid);
        _cardIcon = LoadSprite(CardIconPath);
        _dialogIcon = LoadSprite(DialogIconPath);
        _restoreIcon = LoadSprite(RestoreIconPath);
        _cancelIcon = LoadSprite(CancelIconPath);
        _chevronRight = LoadSprite(ChevronRightPath);
    }

    /// <summary>Same contract ProfileSubPagesBuilder applies to this folder — a freshly
    /// added glyph would otherwise import as a plain Texture and load as a null Sprite.</summary>
    private static void EnsureIconImportSettings()
    {
        if (!Directory.Exists(IconsDir)) return;

        foreach (string path in Directory.GetFiles(IconsDir, "*.png"))
        {
            string assetPath = path.Replace('\\', '/');
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer)) continue;

            bool dirty = importer.textureType != TextureImporterType.Sprite
                         || importer.spriteImportMode != SpriteImportMode.Single
                         || importer.mipmapEnabled
                         || !importer.alphaIsTransparency;
            if (!dirty) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }
    }

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[SubscriptionPageBuilder] Font missing for GUID {guid}");
        return font;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[SubscriptionPageBuilder] Sprite missing: {path}");
        return sprite;
    }

    // ── Low-level helpers (PaywallBuilder idiom) ────────────────────────────

    private static GameObject NewChild(GameObject parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static void StretchFill(RectTransform rt, float uniformInset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(uniformInset, uniformInset);
        rt.offsetMax = new Vector2(-uniformInset, -uniformInset);
    }

    private static void AddVerticalGroup(GameObject go, RectOffset padding, float spacing)
    {
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = padding;
        vlg.spacing = spacing;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
    }

    private static void AddHorizontalGroup(GameObject go, RectOffset padding, float spacing, TextAnchor alignment)
    {
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = padding;
        hlg.spacing = spacing;
        hlg.childAlignment = alignment;
        hlg.childControlWidth = true;
        // childControlHeight TRUE everywhere: a group that sizes itself from a child's stale
        // sizeDelta ships permanently one layout pass behind (the bubble-row lesson).
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
    }

    private static void SetPreferredHeight(GameObject go, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleHeight = 0f;
    }

    private static void SetFixedSize(GameObject go, float width, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.minHeight = height;
        le.preferredWidth = width;
        le.preferredHeight = height;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float size,
        TMP_FontAsset font, ThemeRole? role)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        if (font != null) tmp.font = font;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        // This project's TMP default is NO wrapping — without this, long lines render one
        // line tall and spill off-screen.
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        if (role.HasValue) Themed(go, role.Value);
        return tmp;
    }

    private static void Themed(GameObject go, ThemeRole role)
    {
        var graphic = go.GetComponent<Graphic>();
        if (graphic == null)
        {
            Debug.LogWarning($"[SubscriptionPageBuilder] Themed('{go.name}') found no Graphic — binding skipped.");
            return;
        }
        go.AddComponent<ThemedColor>().Configure(role, graphic, keepAlpha: true);
    }

    private static void AddRounded(GameObject go, float radius)
    {
        // Nobi radius is 1:1 with the VISUAL radius — do NOT halve or double it.
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

    private static Color Hex(string hex)
        => ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
}
#endif
