# Bot Switcher Status Cards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild `Sheet_BotSwitcher` as a status-card bottom sheet (gray sheet, white card per bot, WhatsApp + Telegram connection chips, blue ring + check badge on the active bot) at correct 1080×1920 reference-unit sizes.

**Architecture:** Three files change. `BotSwitcherRowView.cs` swaps the status-dot/subline fields for two platform chips plus ring/badge selection. `BotSwitcherSheet.cs` gains a per-card cascade fade. `BotSwitcherSheetBuilder.cs` is rewritten with calibrated sizes and now saves `BotSwitcherRow.prefab` directly (one menu item, no manual steps). `BotSwitcherRowAvatarRebuilder.cs` is deleted as superseded. Spec: `docs/superpowers/specs/2026-06-12-bot-switcher-status-cards-design.md`.

**Tech Stack:** Unity 6 uGUI, TMPro, DOTween, Nobi.UiRoundedCorners, editor `[MenuItem]` builder pattern.

**Testing note (deviation from TDD):** The user-approved spec adds no new tests — the only logic added is color/alpha mapping; everything else is editor-built UI. Verification = compile + existing EditMode suite green (Unity test bridge) + visual check in Game view at 1080×2400 + user GREEN on device. Unity code can't compile per-task outside the Editor, so commits land after verification, not after each task.

**Unity environment rules (from project memory):** No git worktrees. Stage `.cs` together with its `.meta`. Commit/push only with user consent. Verify via the test bridge (Editor open: drop `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json`; Editor closed: `Tools/run-tests-headless.sh`).

---

### Task 1: Rework `BotSwitcherRowView.cs`

**Files:**
- Modify: `Assets/Scripts/UI/BotSwitcherRowView.cs` (full replacement)

- [ ] **Step 1: Replace the file contents with:**

```csharp
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

/// <summary>
/// One bot card inside Sheet_BotSwitcher. Shows the business-tint avatar, the
/// bot name, and one connection chip per platform (WhatsApp / Telegram). The
/// active bot gets an accent ring (the row's root image) and a corner badge.
/// All references are wired by BotSwitcherSheetBuilder into the saved prefab.
/// </summary>
public class BotSwitcherRowView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private Image ringImage;
    [SerializeField] private GameObject selectedBadge;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image avatarIcon;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private Image waChipBg;
    [SerializeField] private Image waChipIcon;
    [SerializeField] private TextMeshProUGUI waChipLabel;
    [SerializeField] private Image tgChipBg;
    [SerializeField] private Image tgChipIcon;
    [SerializeField] private TextMeshProUGUI tgChipLabel;
    [SerializeField] private Button rowButton;

    [Header("Style")]
    [SerializeField] private Color accentColor = new Color(0.106f, 0.486f, 0.922f);
    [SerializeField] private Color waConnectedBg = new Color(0.914f, 0.969f, 0.937f);
    [SerializeField] private Color waConnectedLabel = new Color(0.059f, 0.431f, 0.337f);
    [SerializeField] private Color tgConnectedBg = new Color(0.902f, 0.945f, 0.984f);
    [SerializeField] private Color tgConnectedLabel = new Color(0.094f, 0.373f, 0.647f);
    [SerializeField] private Color disconnectedBg = new Color(0.925f, 0.925f, 0.933f);
    [SerializeField] private Color disconnectedLabel = new Color(0.557f, 0.557f, 0.576f);
    [SerializeField, Range(0f, 1f)] private float disconnectedIconAlpha = 0.35f;

    public CanvasGroup CanvasGroup => canvasGroup;

    private string botId;
    private System.Action<string> onTap;

    public void Bind(Bot bot, bool isSelected, System.Action<string> tapHandler)
    {
        if (bot == null) return;

        botId = bot.transform.name;
        onTap = tapHandler;

        if (nameLabel != null)
            nameLabel.text = PlayerPrefs.GetString(botId + "Name", botId);

        if (avatarImage != null) avatarImage.color = bot.GetBusinessIconTint();
        if (avatarIcon != null)
        {
            Sprite sprite = bot.GetBusinessIconSprite();
            avatarIcon.sprite = sprite;
            avatarIcon.enabled = sprite != null;
        }

        ApplyChip(waChipBg, waChipIcon, waChipLabel,
            IsConnected(bot.whatsappProfileId), waConnectedBg, waConnectedLabel);
        ApplyChip(tgChipBg, tgChipIcon, tgChipLabel,
            IsConnected(bot.telegramProfileId), tgConnectedBg, tgConnectedLabel);

        if (ringImage != null) ringImage.color = isSelected ? accentColor : Color.clear;
        if (selectedBadge != null) selectedBadge.SetActive(isSelected);

        if (rowButton != null)
        {
            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(HandleTap);
        }
    }

    private static bool IsConnected(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// Brand logos are full-color sprites, so the disconnected state fades
    /// alpha instead of tinting — multiplying a colored logo by gray goes muddy.
    /// </summary>
    private void ApplyChip(Image bg, Image icon, TextMeshProUGUI label,
        bool connected, Color onBg, Color onLabel)
    {
        if (bg != null) bg.color = connected ? onBg : disconnectedBg;
        if (label != null) label.color = connected ? onLabel : disconnectedLabel;
        if (icon != null) icon.color = new Color(1f, 1f, 1f, connected ? 1f : disconnectedIconAlpha);
    }

    private void HandleTap()
    {
        if (string.IsNullOrEmpty(botId)) return;

        transform.DOPunchScale(Vector3.one * 0.04f, 0.18f, 1, 0.5f);
        onTap?.Invoke(botId);
    }

    private void OnDestroy()
    {
        if (rowButton != null) rowButton.onClick.RemoveAllListeners();
    }
}
```

Color values are the spec's hex palette converted to 0–1 floats: accent `#1B7CEB`, WA chip `#E9F7EF`/`#0F6E56`, TG chip `#E6F1FB`/`#185FA5`, disconnected `#ECECEE`/`#8E8E93`.

Removed vs the old file: `subLineLabel`, `statusDot`, `selectedBackground`, `selectedAccentBar`, `statusConnectedColor`, `statusDisconnectedColor`, and the bold/normal name toggle (ring + badge carry selection now; the name is always styled by the prefab).

- [ ] **Step 2: Confirm `validate-cs.sh` hook output is clean** (runs automatically on the Edit/Write). No commit yet — Unity code is verified in Task 4 before anything is committed.

### Task 2: Cascade stagger in `BotSwitcherSheet.cs`

**Files:**
- Modify: `Assets/Scripts/UI/BotSwitcherSheet.cs` (`PopulateRows` only, around line 124)

- [ ] **Step 1: Add two constants** next to the existing `FallbackPanelHeight` constant:

```csharp
    private const float CascadeFadeSeconds = 0.2f;
    private const float CascadeStaggerSeconds = 0.05f;
```

- [ ] **Step 2: Replace the `for` loop body in `PopulateRows`** (currently `Bot bot = ...` through `row.Bind(...)`) with:

```csharp
        int spawned = 0;
        for (int i = 0; i < botsRoot.childCount; i++)
        {
            Bot bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot == null) continue;

            var row = Instantiate(rowPrefab, rowContainer);
            row.transform.localScale = Vector3.one;
            row.Bind(bot, isSelected: bot.transform.name == activeBotId, tapHandler: HandleRowTap);

            CanvasGroup rowGroup = row.CanvasGroup;
            if (rowGroup != null)
            {
                // SetLink kills the tween if PopulateRows destroys the row
                // mid-fade on a quick close/reopen.
                rowGroup.alpha = 0f;
                rowGroup.DOFade(1f, CascadeFadeSeconds)
                    .SetDelay(spawned * CascadeStaggerSeconds)
                    .SetLink(row.gameObject);
            }
            spawned++;
        }
```

Note the `spawned` counter: `i` indexes all children of `BotsRoot` including non-Bot transforms, so using `i` for the stagger delay would leave gaps in the cascade.

- [ ] **Step 3: Confirm the rest of the file is untouched** — `Awake`, `Open`, `Close`, backdrop handling all stay as-is.

### Task 3: Rewrite `BotSwitcherSheetBuilder.cs`, delete `BotSwitcherRowAvatarRebuilder.cs`

**Files:**
- Modify: `Assets/Editor/BotSwitcherSheetBuilder.cs` (full replacement)
- Delete: `Assets/Editor/BotSwitcherRowAvatarRebuilder.cs` + `Assets/Editor/BotSwitcherRowAvatarRebuilder.cs.meta`

The only reference to `BotSwitcherRowAvatarRebuilder` outside its own file is a comment in the old builder being replaced (verified by grep 2026-06-12), so the deletion is safe. `BotSwitcherTitleAvatarRebuilder.cs` (title avatar in the WhatsApp header) stays.

- [ ] **Step 1: Replace `BotSwitcherSheetBuilder.cs` contents with:**

```csharp
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

        Transform existing = canvas.transform.Find(SheetName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        Transform legacyHolder = canvas.transform.Find(LegacyHolderName);
        if (legacyHolder != null) Object.DestroyImmediate(legacyHolder.gameObject);

        GameObject sheet = BuildSheetRoot(canvas);
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
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Simple;
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
        pillImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        pillImage.type = Image.Type.Simple;
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
        ringImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        ringImage.type = Image.Type.Simple;
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
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Simple;
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
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Simple;
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
        bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        bg.type = Image.Type.Simple;
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
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Simple;
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
```

- [ ] **Step 2: Delete the superseded rebuilder** (file + meta together, per project convention):

```bash
rm "Assets/Editor/BotSwitcherRowAvatarRebuilder.cs" "Assets/Editor/BotSwitcherRowAvatarRebuilder.cs.meta"
```

### Task 4: Compile + existing tests green

**Files:** none (verification)

- [ ] **Step 1: Determine Editor state.** If the Unity Editor has the project open, use the test bridge; if closed, use the headless script. (`Tools/run-tests-headless.sh` refuses to run while the Editor holds the project lock.)

- [ ] **Step 2 (Editor open):**

```bash
mkdir -p Temp/claude && touch Temp/claude/run-tests.trigger
```

Wait for `Temp/claude/test-summary.json` (the Editor must be focused), then read it. Expected: compile succeeds, all existing EditMode tests pass, zero failures.

- [ ] **Step 2 (Editor closed, alternative):**

```bash
Tools/run-tests-headless.sh
```

Expected: exit 0, NUnit result in `Tools/test-output/` shows 0 failures. Compile errors abort the run and appear in the log — fix before proceeding.

### Task 5: Run the builder in the Editor

**Files (generated, not hand-edited):**
- Regenerates: `Assets/Prefabs/BotSwitcherRow.prefab`
- Modifies: `Assets/Scenes/Main.unity` (`Sheet_BotSwitcher` subtree)

- [ ] **Step 1: Run the menu item** `Tools/Bot Switcher/Build Sheet`. With the Editor open and the Unity MCP server started this can be done from the terminal via `mcp__mcp-unity__execute_menu_item`; otherwise ask the user to click it.

- [ ] **Step 2: Check the Console** (or `mcp__mcp-unity__get_console_logs`). Expected: `[BotSwitcherSheetBuilder] Built Sheet_BotSwitcher and saved Assets/Prefabs/BotSwitcherRow.prefab. No further manual steps.` and no errors. If the brand-sprite error fires instead, set both PNGs' Texture Type to "Sprite (2D and UI)" in import settings and re-run.

- [ ] **Step 3: Save the scene** (Cmd+S or `mcp__mcp-unity__save_scene`).

### Task 6: Visual verification (user GREEN)

- [ ] **Step 1: Game view at 1080×2400**, open the WhatsApp screen, tap the title to open the switcher. Check against the spec table:
  - Sheet slides up, gray panel, grabber, "Switch bot" title at a size consistent with other screens.
  - Cards: white, properly large (228 units ≈ same scale as BotSettings cards), business-tint avatar with icon, bold name.
  - Chips: connected platform tinted with full-color logo; disconnected gray with faded logo. Verify with one bot that has WhatsApp connected and one with nothing connected.
  - Active bot: blue ring + blue corner check badge, badge straddling the card's top-right rounded corner.
  - Cascade: cards fade in staggered on open; tap a card → punch scale → sheet closes → header title updates.
- [ ] **Step 2: User confirms on-device look (GREEN).** Do not proceed to commit without it.

### Task 7: Commit (with user consent)

- [ ] **Step 1: Stage everything the feature touched** — scripts with their `.meta` files unchanged (`.cs` edits don't change metas, but the deletion removes one), plus regenerated assets:

```bash
git add Assets/Scripts/UI/BotSwitcherRowView.cs \
        Assets/Scripts/UI/BotSwitcherSheet.cs \
        Assets/Editor/BotSwitcherSheetBuilder.cs \
        Assets/Prefabs/BotSwitcherRow.prefab \
        Assets/Scenes/Main.unity
git rm --cached --ignore-unmatch Assets/Editor/BotSwitcherRowAvatarRebuilder.cs \
        Assets/Editor/BotSwitcherRowAvatarRebuilder.cs.meta 2>/dev/null
git add -u Assets/Editor/
```

Expect a large `Main.unity` diff — layout-driven RectTransform churn and RoundedCorners material regeneration are benign in this project; sanity-check the `Sheet_BotSwitcher` fileIDs, not line counts.

- [ ] **Step 2: Commit:**

```bash
git commit -m "feat(ui): rebuild bot switcher as status-card sheet

Calibrated reference-unit sizing (was raw iPhone points at ~40% scale),
WhatsApp + Telegram connection chips per bot, accent ring + corner badge
on the active bot, cascade open animation. Builder now saves
BotSwitcherRow.prefab directly; row avatar rebuilder deleted as superseded.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Self-review

- **Spec coverage:** sheet shell table → Task 3 constants; card table → Task 3 `BuildAndSaveRowPrefab`/`BuildCardBg`/`BuildAvatar`/`BuildTextStack`/`BuildChip`/`BuildSelectedBadge`; chip states + sentinel rule → Task 1 `ApplyChip`/`IsConnected`; ring-root architecture → Task 3 (documented on `BuildAndSaveRowPrefab`); cascade → Task 2; one-shot prefab save → Task 3; rebuilder deletion → Task 3 Step 2; verification section → Tasks 4–6. No gaps.
- **Placeholder scan:** all code complete; no TBDs.
- **Type consistency:** `row.CanvasGroup` (Task 2) matches the `public CanvasGroup CanvasGroup` property (Task 1). Builder `FindProperty` names match Task 1's serialized fields one-to-one (`ringImage`, `selectedBadge`, `canvasGroup`, `avatarImage`, `avatarIcon`, `nameLabel`, `waChipBg/Icon/Label`, `tgChipBg/Icon/Label`, `rowButton`). `Bind` signature unchanged from the current `BotSwitcherSheet` call site.
