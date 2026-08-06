#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot ADDITIVE restyle of the chats-list top bar + bot switcher sheet to
/// the locked 2026-08 spec (docs/design/ui-restyle/chats-topbar-spec.md):
///
///   • TopBar 250→400 — two tiers: identity + «Авто» button above, a full-width
///     recessed channel segment below. Scroll content top padding 260→410.
///   • Old ModeToggle (sliding knob) destroyed; new AutoButton pill built into
///     RightZone and wired to the reworked ReplyModeToggleBinder.
///   • ChannelSwitcher pill → Background-token well with Surface-card cells,
///     brand dots and an unread count; ChannelSwitcherView refs restamped.
///   • ReplyModeConfirmPopup restyled (palette tokens, enable-only copy) and
///     moved above the sheet in sibling order.
///   • Sheet_BotSwitcher: Background panel, «Ваши боты» + subtitle, and the
///     BotSwitcherRow.prefab rebuilt to the compact А2 row (152u, dots subline,
///     per-bot auto chip, ring+rail selection).
///
/// NEVER re-run the superseded geometry builders (ChannelSwitcherBuilder,
/// ReplyModeToggleBuilder, BotSwitcherSheetBuilder) — the scene contains
/// hand-tuning they would clobber. This builder touches only what it owns,
/// is idempotent (safe to re-run), and saves the scene + prefab itself.
/// </summary>
public static class ChatsTopBarRestyleBuilder
{
    private const string RowPrefabPath = "Assets/Prefabs/BotSwitcherRow.prefab";
    private const string HeaderFontGuid = "a2b0b38b6764047da9250bcff1b0f432";

    // Geometry (1080×1920 canvas units)
    private const float BarHeight = 400f;
    private const float ContentTopPadding = 410f;
    private const float Tier1Y = -100f;
    private const float Tier1Height = 156f;
    private const float Tier2WellY = -280f;
    private const float WellHeight = 96f;
    private const float SheetPanelHeight = 1020f;
    private const float RowHeight = 152f;

    // Theme_Light literal values — runtime repaints via Theme/ThemedColor; these
    // make the scene look correct in the editor before play mode.
    private static readonly Color LBackground = Hex("#F4F8F8");
    private static readonly Color LSurface = Hex("#FFFFFF");
    private static readonly Color LHairline = Hex("#E3EDED");
    private static readonly Color LBorder = Hex("#C4D6D7");
    private static readonly Color LInkPrimary = Hex("#08181B");
    private static readonly Color LInkSecondary = Hex("#4C6265");
    private static readonly Color LInkTertiary = Hex("#64797C");
    private static readonly Color LAccentFill = Hex("#243A7A");
    private static readonly Color LWaGreen = Hex("#25D366");
    private static readonly Color LTgBlue = Hex("#2AABEE");

    private static Type cachedRoundedType;

    [MenuItem("Tools/Chats Top Bar/Restyle (Two Tiers + Auto Button)")]
    public static void Build()
    {
        var switcherView = UnityEngine.Object.FindFirstObjectByType<ChannelSwitcherView>(FindObjectsInactive.Include);
        if (switcherView == null)
        {
            Debug.LogError("[ChatsTopBarRestyle] ChannelSwitcherView not found — open Main.unity first.");
            return;
        }

        Transform channelSwitcher = switcherView.transform;
        Transform centerZone = channelSwitcher.parent;
        Transform topBar = centerZone != null ? centerZone.parent : null;
        Transform chatsPanel = topBar != null ? topBar.parent : null;
        if (topBar == null || chatsPanel == null)
        {
            Debug.LogError("[ChatsTopBarRestyle] Unexpected hierarchy above ChannelSwitcher — aborting.");
            return;
        }

        TMP_FontAsset font = LoadHeaderFont();

        RestructureBar(topBar);
        RetuneIdentity(topBar);
        GameObject popup = RestyleConfirmPopup(chatsPanel);
        BuildAutoButton(topBar, popup, font);
        RebuildChannelWell(channelSwitcher, centerZone, switcherView, font);
        RaiseContentPadding(chatsPanel);
        RestyleSheet(chatsPanel, font);
        RestyleRowPrefab(font);

        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(topBar.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[ChatsTopBarRestyle] Done: bar 400u two-tier, AutoButton wired, well rebuilt, " +
                  "popup restyled, sheet + row prefab compacted. Check EmptyState/SyncingState offsets visually.");
    }

    // ---- Bar structure ---------------------------------------------------

    private static void RestructureBar(Transform topBar)
    {
        var barRt = (RectTransform)topBar;
        barRt.sizeDelta = new Vector2(barRt.sizeDelta.x, BarHeight);

        // Tier 1 — identity (left) + auto button (right).
        var left = topBar.Find("LeftZone") as RectTransform;
        if (left != null)
        {
            left.anchorMin = left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 1f);
            left.anchoredPosition = new Vector2(0f, Tier1Y);
            left.sizeDelta = new Vector2(640f, Tier1Height);
        }

        var right = topBar.Find("RightZone") as RectTransform;
        if (right != null)
        {
            right.anchorMin = right.anchorMax = new Vector2(1f, 1f);
            right.pivot = new Vector2(1f, 1f);
            right.anchoredPosition = new Vector2(0f, Tier1Y);
            right.sizeDelta = new Vector2(340f, Tier1Height);
        }

        // Tier 2 — the channel well's home, full width.
        var center = topBar.Find("CenterZone") as RectTransform;
        if (center != null)
        {
            center.gameObject.SetActive(true);   // an old builder pass hid it
            center.anchorMin = new Vector2(0f, 1f);
            center.anchorMax = new Vector2(1f, 1f);
            center.pivot = new Vector2(0.5f, 1f);
            center.sizeDelta = new Vector2(-80f, WellHeight);   // 40u inset each side
            center.anchoredPosition = new Vector2(0f, Tier2WellY);
        }

        // Bar background keeps its full-stretch Image; make sure it is themed.
        for (int i = 0; i < topBar.childCount; i++)
        {
            var child = (RectTransform)topBar.GetChild(i);
            if (child.anchorMin == Vector2.zero && child.anchorMax == Vector2.one &&
                child.GetComponent<Image>() != null)
            {
                EnsureThemed(child.gameObject, ThemeRole.Surface);
                break;
            }
        }
    }

    private static void RetuneIdentity(Transform topBar)
    {
        Transform title = topBar.Find("LeftZone/BotSwitcherTitle");
        if (title == null)
        {
            Debug.LogWarning("[ChatsTopBarRestyle] BotSwitcherTitle not found — identity untouched.");
            return;
        }

        var titleRt = (RectTransform)title;
        titleRt.sizeDelta = new Vector2(titleRt.sizeDelta.x, 96f);

        Transform avatar = title.Find("Avatar");
        if (avatar != null)
        {
            var le = avatar.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = 88f;
                le.preferredHeight = 88f;
            }
            SetRoundedRadius(avatar.gameObject, 44f);
            if (avatar.childCount > 0)
                ((RectTransform)avatar.GetChild(0)).sizeDelta = new Vector2(41f, 41f);
        }

        var name = title.Find("BotName")?.GetComponent<TextMeshProUGUI>();
        if (name != null)
        {
            name.fontSize = 46f;
            name.color = LInkPrimary;
            EnsureThemed(name.gameObject, ThemeRole.InkPrimary);
        }
    }

    // ---- Auto button -----------------------------------------------------

    private static void BuildAutoButton(Transform topBar, GameObject popup, TMP_FontAsset font)
    {
        Transform rightZone = topBar.Find("RightZone");
        if (rightZone == null)
        {
            Debug.LogError("[ChatsTopBarRestyle] RightZone not found — AutoButton skipped.");
            return;
        }

        // The knob toggle this button replaces + any earlier run of ourselves.
        DestroyAllByName(topBar, "ModeToggle");
        DestroyAllByName(topBar, "AutoButton");

        var root = new GameObject("AutoButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(rightZone, false);
        root.transform.SetAsFirstSibling();

        var rootLe = root.GetComponent<LayoutElement>();
        rootLe.preferredWidth = 190f;
        rootLe.preferredHeight = 96f;

        // RightZone's HLG does not control child sizes — the rect must carry
        // the real dimensions itself (the LayoutElement is a no-op there).
        ((RectTransform)root.transform).sizeDelta = new Vector2(190f, 96f);

        var hitImage = root.GetComponent<Image>();
        hitImage.color = new Color(0f, 0f, 0f, 0f);   // invisible 96u hit target
        hitImage.raycastTarget = true;

        var button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hitImage;

        // Visual pill — 76u inside the 96u hit rect.
        var pill = NewUiChild(root.transform, "Pill", typeof(RectTransform));
        var pillRt = (RectTransform)pill.transform;
        pillRt.anchorMin = new Vector2(0f, 0.5f);
        pillRt.anchorMax = new Vector2(1f, 0.5f);
        pillRt.pivot = new Vector2(0.5f, 0.5f);
        pillRt.sizeDelta = new Vector2(0f, 76f);
        pillRt.anchoredPosition = Vector2.zero;

        Image ring = BuildStretchedImage(pill.transform, "Ring", LBorder, 38f, Vector2.zero);
        Image fill = BuildStretchedImage(pill.transform, "Fill", LSurface, 35f, new Vector2(3f, 3f));

        var content = NewUiChild(pill.transform, "Content", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Stretch((RectTransform)content.transform, Vector2.zero);
        var hlg = content.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 14f;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        (Image dotRing, Image dotCore) = BuildLamp(content.transform, 18f, 4f, LInkTertiary, LSurface);

        TextMeshProUGUI label = BuildTmp(content.transform, "Label", "Авто", 30f, FontStyles.Bold,
            LInkSecondary, font, new Vector2(96f, 40f));

        // Wire the reworked binder — popup refs resolved from the restyled popup.
        var binder = root.AddComponent<ReplyModeToggleBinder>();
        var so = new SerializedObject(binder);
        SetRef(so, "toggleButton", button);
        SetRef(so, "ringImage", ring);
        SetRef(so, "fillImage", fill);
        SetRef(so, "label", label);
        SetRef(so, "dotRing", dotRing);
        SetRef(so, "dotCore", dotCore);
        if (popup != null)
        {
            SetRef(so, "confirmPopup", popup);
            SetRef(so, "confirmTitle", popup.transform.Find("Content/Title")?.GetComponent<TextMeshProUGUI>());
            SetRef(so, "confirmBody", popup.transform.Find("Content/Body")?.GetComponent<TextMeshProUGUI>());
            SetRef(so, "confirmButton", popup.transform.Find("Content/ConfirmButton")?.GetComponent<Button>());
            SetRef(so, "cancelButton", popup.transform.Find("Content/CancelButton")?.GetComponent<Button>());
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- Confirm popup (existing object, restyled in place) --------------

    private static GameObject RestyleConfirmPopup(Transform chatsPanel)
    {
        // The popup may already have been lifted to the screen root (see below) —
        // look in both homes so re-runs stay idempotent.
        Transform popup = chatsPanel.Find("ReplyModeConfirmPopup");
        if (popup == null && chatsPanel.parent != null)
            popup = chatsPanel.parent.Find("ReplyModeConfirmPopup");
        if (popup == null)
        {
            Debug.LogWarning("[ChatsTopBarRestyle] ReplyModeConfirmPopup not found — binder falls back to instant commits.");
            return null;
        }

        // Lift the popup to the screen root: as a ChatsPanel child it would render
        // BEHIND MessagesPanel, and the per-chat SemiAutoToggle confirms from the
        // open conversation. Last sibling ⇒ above both panels and the bot sheet.
        if (chatsPanel.parent != null && popup.parent != chatsPanel.parent)
            popup.SetParent(chatsPanel.parent, worldPositionStays: false);
        popup.SetAsLastSibling();

        Transform card = popup.Find("Content");
        if (card != null)
        {
            var cardImage = card.GetComponent<Image>();
            if (cardImage != null) cardImage.color = LSurface;
            EnsureThemed(card.gameObject, ThemeRole.Surface);
        }

        StyleTmp(popup.Find("Content/Title"), "Включить авто-режим?", LInkPrimary, ThemeRole.InkPrimary);
        StyleTmp(popup.Find("Content/Body"),
            "Бот будет отвечать клиентам сам. Выключить можно в любой момент — этой же кнопкой.",
            LInkSecondary, ThemeRole.InkSecondary);

        Transform cancel = popup.Find("Content/CancelButton");
        if (cancel != null)
        {
            var image = cancel.GetComponent<Image>();
            if (image != null) image.color = LBackground;
            EnsureThemed(cancel.gameObject, ThemeRole.Background);
            StyleTmp(cancel.Find("Label"), "Отмена", LInkSecondary, ThemeRole.InkSecondary);
        }

        Transform confirm = popup.Find("Content/ConfirmButton");
        if (confirm != null)
        {
            var image = confirm.GetComponent<Image>();
            if (image != null) image.color = LAccentFill;
            EnsureThemed(confirm.gameObject, ThemeRole.AccentFill);
            StyleTmp(confirm.Find("Label"), "Включить", Color.white, ThemeRole.AccentOnFill);
        }

        return popup.gameObject;
    }

    // ---- Conversation-screen per-chat chip -------------------------------

    [MenuItem("Tools/Chats Top Bar/Restyle Conversation Auto Chip")]
    public static void BuildConversationChip()
    {
        var toggle = UnityEngine.Object.FindFirstObjectByType<SemiAutoToggle>(FindObjectsInactive.Include);
        if (toggle == null)
        {
            Debug.LogError("[ChatsTopBarRestyle] SemiAutoToggle not found — open Main.unity first.");
            return;
        }

        TMP_FontAsset font = LoadHeaderFont();
        GameObject go = toggle.gameObject;

        // The old sliding knob was 220×60; the chip keeps that footprint's slot
        // but grows the hit target to 88 (sheet-chip metrics: 60u visual pill).
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(170f, 88f);

        var hit = go.GetComponent<Image>();
        if (hit == null) hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        var button = go.GetComponent<Button>();
        if (button == null) button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        // The knob's Thumb/Faint* children retire wholesale.
        for (int i = go.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);

        // The root's old rounded-track component would clip the transparent hit
        // rect oddly — retune it to the new rect (harmless if absent).
        SetRoundedRadius(go, 44f);

        var pill = NewUiChild(go.transform, "Pill", typeof(RectTransform));
        var pillRt = (RectTransform)pill.transform;
        pillRt.anchorMin = new Vector2(0f, 0.5f);
        pillRt.anchorMax = new Vector2(1f, 0.5f);
        pillRt.pivot = new Vector2(0.5f, 0.5f);
        pillRt.sizeDelta = new Vector2(0f, 60f);
        pillRt.anchoredPosition = Vector2.zero;

        Image ring = BuildStretchedImage(pill.transform, "Ring", LBorder, 30f, Vector2.zero);
        Image fill = BuildStretchedImage(pill.transform, "Fill", LSurface, 27f, new Vector2(3f, 3f));

        var content = NewUiChild(pill.transform, "Content", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Stretch((RectTransform)content.transform, Vector2.zero);
        var hlg = content.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 10f;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        (Image dotRing, Image dotCore) = BuildLamp(content.transform, 14f, 3f, LInkTertiary, LSurface);
        TextMeshProUGUI label = BuildTmp(content.transform, "Label", "Авто", 26f, FontStyles.Bold,
            LInkSecondary, font, new Vector2(70f, 34f));

        var so = new SerializedObject(toggle);
        SetRef(so, "toggleButton", button);
        SetRef(so, "ringImage", ring);
        SetRef(so, "fillImage", fill);
        SetRef(so, "label", label);
        SetRef(so, "dotRing", dotRing);
        SetRef(so, "dotCore", dotCore);
        so.ApplyModifiedPropertiesWithoutUndo();

        // The chip confirms from the open conversation — the shared popup must
        // sit above MessagesPanel. Locate ChatsPanel via the channel switcher.
        var switcherView = UnityEngine.Object.FindFirstObjectByType<ChannelSwitcherView>(FindObjectsInactive.Include);
        Transform chatsPanel = switcherView != null ? switcherView.transform.parent?.parent?.parent : null;
        if (chatsPanel != null) RestyleConfirmPopup(chatsPanel);

        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[ChatsTopBarRestyle] Conversation SemiAutoToggle rebuilt as the «Авто» chip and rewired.");
    }

    // ---- Channel well ----------------------------------------------------

    private static void RebuildChannelWell(Transform channelSwitcher, Transform centerZone,
        ChannelSwitcherView view, TMP_FontAsset font)
    {
        // The switcher stretches to fill the tier-2 zone.
        var rt = (RectTransform)channelSwitcher;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Root image becomes the hairline ring of the well.
        var wellRing = channelSwitcher.GetComponent<Image>();
        if (wellRing != null) wellRing.color = LHairline;
        EnsureThemed(channelSwitcher.gameObject, ThemeRole.Hairline);
        SetRoundedRadius(channelSwitcher.gameObject, 48f);

        // Recessed fill inset by the 2u ring.
        DestroyExisting(channelSwitcher, "WellFill");
        Image wellFill = BuildStretchedImage(channelSwitcher, "WellFill", LBackground, 46f, new Vector2(2f, 2f));
        wellFill.transform.SetAsFirstSibling();
        EnsureThemed(wellFill.gameObject, ThemeRole.Background);

        Transform waChip = channelSwitcher.Find("WaChip");
        Transform tgChip = channelSwitcher.Find("TgChip");
        if (waChip == null || tgChip == null)
        {
            Debug.LogError("[ChatsTopBarRestyle] WaChip/TgChip not found — well cells skipped.");
            return;
        }
        waChip.SetAsLastSibling();
        tgChip.SetAsLastSibling();

        CellRefs wa = RebuildCell(waChip, "WhatsApp", LWaGreen, selectedByDefault: true, font);
        CellRefs tg = RebuildCell(tgChip, "Telegram", LTgBlue, selectedByDefault: false, font);

        var so = new SerializedObject(view);
        SetRef(so, "waChipButton", waChip.GetComponent<Button>());
        SetRef(so, "tgChipButton", tgChip.GetComponent<Button>());
        SetRef(so, "waChipFill", wa.Card);
        SetRef(so, "tgChipFill", tg.Card);
        SetRef(so, "waLabel", wa.Label);
        SetRef(so, "tgLabel", tg.Label);
        SetRef(so, "waDot", wa.Dot);
        SetRef(so, "tgDot", tg.Dot);
        SetRef(so, "waCount", wa.Count);
        SetRef(so, "tgCount", tg.Count);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private struct CellRefs
    {
        public Image Card;
        public Image Dot;
        public TextMeshProUGUI Label;
        public TextMeshProUGUI Count;
    }

    private static CellRefs RebuildCell(Transform chip, string labelText, Color brand,
        bool selectedByDefault, TMP_FontAsset font)
    {
        bool isLeft = chip.name == "WaChip";
        var rt = (RectTransform)chip;
        rt.anchorMin = new Vector2(isLeft ? 0f : 0.5f, 0f);
        rt.anchorMax = new Vector2(isLeft ? 0.5f : 1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(isLeft ? 5f : 0f, 5f);
        rt.offsetMax = new Vector2(isLeft ? 0f : -5f, -5f);

        // The chip's own Image stays the invisible hit target for its Button.
        var hit = chip.GetComponent<Image>();
        if (hit != null)
        {
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
        }

        // Cells are rebuilt from scratch — the old Fill/Label pill parts retire.
        for (int i = chip.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(chip.GetChild(i).gameObject);

        Image card = BuildStretchedImage(chip, "CellCard", LSurface, 43f, Vector2.zero);
        Color cardColor = card.color;
        cardColor.a = selectedByDefault ? 1f : 0f;
        card.color = cardColor;

        var content = NewUiChild(card.transform, "Content", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Stretch((RectTransform)content.transform, Vector2.zero);
        var hlg = content.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 14f;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        Image dot = BuildCircle(content.transform, "Dot", 20f, brand);
        if (!selectedByDefault)
        {
            Color faded = dot.color;
            faded.a = 0.4f;
            dot.color = faded;
        }

        TextMeshProUGUI label = BuildTmp(content.transform, "Label", labelText, 32f, FontStyles.Bold,
            selectedByDefault ? LInkPrimary : LInkTertiary, font, new Vector2(200f, 44f));

        TextMeshProUGUI count = BuildTmp(content.transform, "Count", "", 28f, FontStyles.Bold,
            LInkTertiary, font, new Vector2(74f, 40f));
        count.gameObject.SetActive(false);   // ChannelSwitcherView shows it when unread > 0

        return new CellRefs { Card = card, Dot = dot, Label = label, Count = count };
    }

    private static void RaiseContentPadding(Transform chatsPanel)
    {
        Transform content = chatsPanel.Find("Scroll/Viewport/Content");
        var vlg = content != null ? content.GetComponent<VerticalLayoutGroup>() : null;
        if (vlg == null)
        {
            Debug.LogWarning("[ChatsTopBarRestyle] Scroll content VLG not found — top padding unchanged.");
            return;
        }
        RectOffset padding = vlg.padding;
        padding.top = (int)ContentTopPadding;
        vlg.padding = padding;
    }

    // ---- Sheet -----------------------------------------------------------

    private static void RestyleSheet(Transform chatsPanel, TMP_FontAsset font)
    {
        Transform sheet = chatsPanel.Find("Sheet_BotSwitcher");
        Transform panel = sheet != null ? sheet.Find("Panel") : null;
        if (panel == null)
        {
            Debug.LogWarning("[ChatsTopBarRestyle] Sheet_BotSwitcher/Panel not found — sheet untouched.");
            return;
        }

        var panelRt = (RectTransform)panel;
        panelRt.sizeDelta = new Vector2(panelRt.sizeDelta.x, SheetPanelHeight);

        var panelImage = panel.GetComponent<Image>();
        if (panelImage != null) panelImage.color = LBackground;
        EnsureThemed(panel.gameObject, ThemeRole.Background);

        Transform pill = panel.Find("GrabberArea/Pill");
        if (pill != null)
        {
            var pillImage = pill.GetComponent<Image>();
            if (pillImage != null) pillImage.color = LBorder;
            EnsureThemed(pill.gameObject, ThemeRole.Border);
        }

        Transform title = panel.Find("Title");
        var titleTmp = title != null ? title.GetComponent<TextMeshProUGUI>() : null;
        if (titleTmp == null)
        {
            Debug.LogWarning("[ChatsTopBarRestyle] Sheet Title not found — texts unchanged.");
            return;
        }

        // Subtitle first (cloned from Title so alignment/margins carry over),
        // then style both — the clone must not inherit Title's ThemedColor role.
        Transform subtitle = panel.Find("Subtitle");
        if (subtitle == null)
        {
            GameObject clone = UnityEngine.Object.Instantiate(title.gameObject, panel);
            clone.name = "Subtitle";
            clone.transform.SetSiblingIndex(title.GetSiblingIndex() + 1);
            subtitle = clone.transform;
            var stale = clone.GetComponent<ThemedColor>();
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale);
        }

        titleTmp.text = "Ваши боты";
        titleTmp.color = LInkPrimary;
        EnsureThemed(title.gameObject, ThemeRole.InkPrimary);
        var titleLe = title.GetComponent<LayoutElement>();
        if (titleLe != null) titleLe.preferredHeight = 76f;

        var subTmp = subtitle.GetComponent<TextMeshProUGUI>();
        if (subTmp != null)
        {
            subTmp.text = "Чаты и авто-режим";
            subTmp.fontSize = 28f;
            subTmp.fontStyle = FontStyles.Normal;
            subTmp.color = LInkTertiary;
            EnsureThemed(subTmp.gameObject, ThemeRole.InkTertiary);
        }
        var subLe = subtitle.GetComponent<LayoutElement>();
        if (subLe != null) subLe.preferredHeight = 56f;
    }

    // ---- Row prefab ------------------------------------------------------

    private static void RestyleRowPrefab(TMP_FontAsset font)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RowPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[ChatsTopBarRestyle] Could not load {RowPrefabPath}.");
            return;
        }

        try
        {
            Transform cardBg = root.transform.Find("CardBg");
            Transform avatar = cardBg != null ? cardBg.Find("Avatar") : null;
            Transform stack = cardBg != null ? cardBg.Find("Stack") : null;
            var view = root.GetComponent<BotSwitcherRowView>();
            if (cardBg == null || avatar == null || stack == null || view == null)
            {
                Debug.LogError("[ChatsTopBarRestyle] Row prefab structure unexpected — prefab untouched.");
                return;
            }

            // Root: compact height, ring radius for the 4u inset.
            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(rootRt.sizeDelta.x, RowHeight);
            var rootLe = root.GetComponent<LayoutElement>();
            if (rootLe != null) rootLe.preferredHeight = RowHeight;
            SetRoundedRadius(root, 44f);

            // Card: 4u ring inset (was 12), token surface, tighter paddings.
            var cardRt = (RectTransform)cardBg;
            cardRt.offsetMin = new Vector2(4f, 4f);
            cardRt.offsetMax = new Vector2(-4f, -4f);
            SetRoundedRadius(cardBg.gameObject, 40f);
            EnsureThemed(cardBg.gameObject, ThemeRole.Surface);
            var cardHlg = cardBg.GetComponent<HorizontalLayoutGroup>();
            if (cardHlg != null)
            {
                RectOffset padding = cardHlg.padding;
                padding.left = 32;
                padding.right = 24;
                cardHlg.padding = padding;
                cardHlg.spacing = 28f;
            }

            // Selection rail (idempotent rebuild), sits over the card's left edge.
            DestroyExisting(root.transform, "Rail");
            var rail = NewUiChild(root.transform, "Rail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var railRt = (RectTransform)rail.transform;
            railRt.anchorMin = new Vector2(0f, 0f);
            railRt.anchorMax = new Vector2(0f, 1f);
            railRt.pivot = new Vector2(0f, 0.5f);
            railRt.sizeDelta = new Vector2(10f, -8f);
            railRt.anchoredPosition = new Vector2(4f, 0f);
            var railImage = rail.GetComponent<Image>();
            railImage.color = LAccentFill;
            railImage.raycastTarget = false;
            SetRoundedRadius(rail, 5f);
            rail.SetActive(false);

            // Avatar 144→100.
            var avatarLe = avatar.GetComponent<LayoutElement>();
            if (avatarLe != null)
            {
                avatarLe.preferredWidth = 100f;
                avatarLe.preferredHeight = 100f;
                avatarLe.minWidth = 100f;
                avatarLe.minHeight = 100f;
            }
            SetRoundedRadius(avatar.gameObject, 50f);
            Transform avatarIcon = avatar.Find("IconSprite");
            if (avatarIcon != null) ((RectTransform)avatarIcon).sizeDelta = new Vector2(65f, 65f);

            // Stack flexes between avatar and chip; name 40 + themed.
            var stackLe = stack.GetComponent<LayoutElement>() ?? stack.gameObject.AddComponent<LayoutElement>();
            stackLe.flexibleWidth = 1f;
            var stackVlg = stack.GetComponent<VerticalLayoutGroup>();
            if (stackVlg != null) stackVlg.spacing = 8f;

            var nameTmp = stack.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (nameTmp != null)
            {
                nameTmp.fontSize = 40f;
                nameTmp.color = LInkPrimary;
                nameTmp.enableWordWrapping = false;
                nameTmp.overflowMode = TextOverflowModes.Ellipsis;
                EnsureThemed(nameTmp.gameObject, ThemeRole.InkPrimary);
            }

            // ChipRow → SubRow: brand dots + «N чатов · M новых».
            Transform subRow = stack.Find("SubRow") ?? stack.Find("ChipRow");
            if (subRow == null)
            {
                Debug.LogError("[ChatsTopBarRestyle] ChipRow/SubRow not found in prefab — aborting prefab pass.");
                return;
            }
            subRow.name = "SubRow";
            for (int i = subRow.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(subRow.GetChild(i).gameObject);

            var subHlg = subRow.GetComponent<HorizontalLayoutGroup>();
            if (subHlg != null)
            {
                subHlg.spacing = 12f;
                subHlg.childAlignment = TextAnchor.MiddleLeft;
                subHlg.childControlWidth = true;
                subHlg.childControlHeight = true;
                subHlg.childForceExpandWidth = false;
                subHlg.childForceExpandHeight = false;
            }

            Image waDot = BuildCircle(subRow, "WaDot", 16f, LWaGreen);
            AddLayoutSize(waDot.gameObject, 16f, 16f);
            Image tgDot = BuildCircle(subRow, "TgDot", 16f, LTgBlue);
            AddLayoutSize(tgDot.gameObject, 16f, 16f);

            TextMeshProUGUI subLabel = BuildTmp(subRow, "SubLabel", "", 28f, FontStyles.Normal,
                LInkTertiary, font, new Vector2(520f, 36f));
            subLabel.alignment = TextAlignmentOptions.MidlineLeft;
            subLabel.overflowMode = TextOverflowModes.Ellipsis;
            var subLabelLe = subLabel.gameObject.AddComponent<LayoutElement>();
            subLabelLe.preferredHeight = 36f;
            subLabelLe.flexibleWidth = 1f;
            EnsureThemed(subLabel.gameObject, ThemeRole.InkTertiary);

            // The per-bot «Авто» chip — the header button, replicated.
            DestroyExisting(cardBg, "AutoChip");
            ChipRefs chip = BuildAutoChip(cardBg, font);

            // The corner badge retires — selection is ring + rail now.
            DestroyExisting(root.transform, "SelectedBadge");

            // Rewire the view.
            var so = new SerializedObject(view);
            SetRef(so, "ringImage", root.GetComponent<Image>());
            SetRef(so, "railObject", rail);
            SetRef(so, "railImage", railImage);
            SetRef(so, "canvasGroup", root.GetComponent<CanvasGroup>());
            SetRef(so, "rowButton", root.GetComponent<Button>());
            SetRef(so, "avatarImage", avatar.GetComponent<Image>());
            SetRef(so, "avatarIcon", avatarIcon != null ? avatarIcon.GetComponent<Image>() : null);
            SetRef(so, "nameLabel", nameTmp);
            SetRef(so, "waDot", waDot);
            SetRef(so, "tgDot", tgDot);
            SetRef(so, "subLabel", subLabel);
            SetRef(so, "chipRoot", chip.Root);
            SetRef(so, "chipButton", chip.Button);
            SetRef(so, "chipRing", chip.Ring);
            SetRef(so, "chipFill", chip.Fill);
            SetRef(so, "chipDotRing", chip.DotRing);
            SetRef(so, "chipDotCore", chip.DotCore);
            SetRef(so, "chipLabel", chip.Label);
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, RowPrefabPath, out bool success);
            Debug.Log(success
                ? "[ChatsTopBarRestyle] BotSwitcherRow.prefab compacted + rewired."
                : "[ChatsTopBarRestyle] FAILED to save BotSwitcherRow.prefab!");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private struct ChipRefs
    {
        public GameObject Root;
        public Button Button;
        public Image Ring;
        public Image Fill;
        public Image DotRing;
        public Image DotCore;
        public TextMeshProUGUI Label;
    }

    private static ChipRefs BuildAutoChip(Transform cardBg, TMP_FontAsset font)
    {
        var root = NewUiChild(cardBg, "AutoChip",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        var le = root.GetComponent<LayoutElement>();
        le.preferredWidth = 170f;
        le.preferredHeight = 88f;
        le.minWidth = 170f;

        var hit = root.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        var button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        var pill = NewUiChild(root.transform, "Pill", typeof(RectTransform));
        var pillRt = (RectTransform)pill.transform;
        pillRt.anchorMin = new Vector2(0f, 0.5f);
        pillRt.anchorMax = new Vector2(1f, 0.5f);
        pillRt.pivot = new Vector2(0.5f, 0.5f);
        pillRt.sizeDelta = new Vector2(0f, 60f);
        pillRt.anchoredPosition = Vector2.zero;

        Image ring = BuildStretchedImage(pill.transform, "Ring", LBorder, 30f, Vector2.zero);
        Image fill = BuildStretchedImage(pill.transform, "Fill", LSurface, 27f, new Vector2(3f, 3f));

        var content = NewUiChild(pill.transform, "Content", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Stretch((RectTransform)content.transform, Vector2.zero);
        var hlg = content.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 10f;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        (Image dotRing, Image dotCore) = BuildLamp(content.transform, 14f, 3f, LInkTertiary, LSurface);
        TextMeshProUGUI label = BuildTmp(content.transform, "Label", "Авто", 26f, FontStyles.Bold,
            LInkSecondary, font, new Vector2(70f, 34f));

        return new ChipRefs
        {
            Root = root,
            Button = button,
            Ring = ring,
            Fill = fill,
            DotRing = dotRing,
            DotCore = dotCore,
            Label = label,
        };
    }

    // ---- Shared build helpers -------------------------------------------

    private static GameObject NewUiChild(Transform parent, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt, Vector2 inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = inset;
        rt.offsetMax = -inset;
    }

    private static Image BuildStretchedImage(Transform parent, string name, Color color,
        float radius, Vector2 inset)
    {
        var go = NewUiChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Stretch((RectTransform)go.transform, inset);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        AddRounded(go, radius);
        return image;
    }

    private static Image BuildCircle(Transform parent, string name, float size, Color color)
    {
        var go = NewUiChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(size, size);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        AddRounded(go, size / 2f);
        return image;
    }

    /// <summary>The state lamp: an outer circle with an inset core (the "hole" of the hollow look).</summary>
    private static (Image ring, Image core) BuildLamp(Transform parent, float size, float coreInset,
        Color ringColor, Color coreColor)
    {
        Image ring = BuildCircle(parent, "Lamp", size, ringColor);
        var core = NewUiChild(ring.transform, "Core", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Stretch((RectTransform)core.transform, new Vector2(coreInset, coreInset));
        var coreImage = core.GetComponent<Image>();
        coreImage.color = coreColor;
        coreImage.raycastTarget = false;
        AddRounded(core, (size - coreInset * 2f) / 2f);
        return (ring, coreImage);
    }

    private static TextMeshProUGUI BuildTmp(Transform parent, string name, string text, float fontSize,
        FontStyles style, Color color, TMP_FontAsset font, Vector2 sizeDelta)
    {
        var go = NewUiChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        ((RectTransform)go.transform).sizeDelta = sizeDelta;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.characterSpacing = -2f;   // project text standard
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void StyleTmp(Transform target, string text, Color color, ThemeRole role)
    {
        var tmp = target != null ? target.GetComponent<TextMeshProUGUI>() : null;
        if (tmp == null) return;
        tmp.text = text;
        tmp.color = color;
        EnsureThemed(tmp.gameObject, role);
    }

    private static void AddLayoutSize(GameObject go, float width, float height)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;
        le.minWidth = width;
        le.minHeight = height;
    }

    /// <summary>
    /// Attach (or retarget) a ThemedColor binding for a STATIC-role graphic.
    /// State-driven graphics (cells, lamps, rails) are deliberately left
    /// unthemed — their views repaint them from Theme on every state change.
    /// </summary>
    private static void EnsureThemed(GameObject go, ThemeRole role)
    {
        var themed = go.GetComponent<ThemedColor>() ?? go.AddComponent<ThemedColor>();
        var so = new SerializedObject(themed);
        SerializedProperty roleProp = so.FindProperty("role");
        if (roleProp != null) roleProp.enumValueIndex = (int)role;
        SerializedProperty targetProp = so.FindProperty("target");
        if (targetProp != null) targetProp.objectReferenceValue = go.GetComponent<Graphic>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void DestroyExisting(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
    }

    private static void DestroyAllByName(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t != null && t != root && t.name == name)
                UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
    }

    private static void SetRef(SerializedObject so, string property, UnityEngine.Object value)
    {
        SerializedProperty prop = so.FindProperty(property);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[ChatsTopBarRestyle] Serialized property '{property}' not found.");
    }

    private static TMP_FontAsset LoadHeaderFont()
    {
        string path = AssetDatabase.GUIDToAssetPath(HeaderFontGuid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font != null) return font;

        var anyTmp = UnityEngine.Object.FindFirstObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        return anyTmp != null ? anyTmp.font : null;
    }

    // RoundedCorners ships in its OWN UPM assembly — scan loaded assemblies
    // (project memory: Type.GetType against Assembly-CSharp silently fails).
    private static Type ResolveRoundedType()
    {
        if (cachedRoundedType != null) return cachedRoundedType;

        const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = asm.GetType(fullName);
            if (type != null)
            {
                cachedRoundedType = type;
                return type;
            }
        }
        return null;
    }

    private static void AddRounded(GameObject go, float radius)
    {
        Type type = ResolveRoundedType();
        if (type == null) return;
        var rc = go.GetComponent(type) ?? go.AddComponent(type);
        type.GetField("radius")?.SetValue(rc, radius);
        type.GetField("image")?.SetValue(rc, go.GetComponent<Image>());
    }

    private static void SetRoundedRadius(GameObject go, float radius)
    {
        Type type = ResolveRoundedType();
        if (type == null) return;
        var rc = go.GetComponent(type);
        if (rc == null) rc = go.AddComponent(type);
        type.GetField("radius")?.SetValue(rc, radius);
        type.GetField("image")?.SetValue(rc, go.GetComponent<Image>());
    }

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}
#endif
