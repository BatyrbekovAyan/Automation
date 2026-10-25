#if UNITY_EDITOR
using System;
using Automation.BotSettingsUI;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static BuilderSceneUtil;
using Object = UnityEngine.Object;

/// <summary>
/// The bot settings' «ЛИЧНЫЕ ЧАТЫ» section (spec §5.7, sketch 009 screen 7) — BotSettings.prefab, the Основное tab, right above
/// «РАСПИСАНИЕ РАБОТЫ», built the way BotScheduleBuilder builds the schedule: every anchor through BotSettings' serialized
/// references (never a node name), the hint measured on its own label inside the loaded prefab (no probe object — the open
/// scene is never touched), every value compared before it is written, nothing deleted.
///
///   SectionHeader_ЛИЧНЫЕ ЧАТЫ  a clone of the tab's first SectionHeader, «ЛИЧНЫЕ ЧАТЫ»
///   PersonalChatsRow           the channel rows' card (BotSettings.whatsappRow: its fill, corners, shadow and height) as one
///                              button pressed like «Удалить ИИ-сотрудника» (BotSettings.deleteBotButton) — [Label «Личные
///                              чаты» in the channel rows' label style][Count, InkSecondary][Chevron «›», InkTertiary — the
///                              «Проверить ИИ-сотрудника» row's]; BotSettings.personalChatsRow and personalChatsCount
///   PersonalChatsHint          PersonalChatsCopy.SettingsHint in the schedule hint's style, its box its measured height
///
/// The block stands right before the schedule's header — the header right above BotSettings.scheduleRow — so the Основное tab
/// reads «… КАНАЛЫ · ЛИЧНЫЕ ЧАТЫ · РАСПИСАНИЕ РАБОТЫ · Удалить». The authored colours are the light theme's; every colour is a
/// ThemedColor binding. Geometry is in 1080×1920 canvas reference units.
/// </summary>
public static partial class PersonalChatsBuilder
{
    public const string SettingsPrefabPath = "Assets/Prefabs/BotSettings.prefab";
    public const string SettingsHeaderName = "SectionHeader_ЛИЧНЫЕ ЧАТЫ";
    public const string SettingsRowName = "PersonalChatsRow";
    public const string SettingsHintName = "PersonalChatsHint";

    /// <summary>The number's box, right-aligned against the chevron: «12345» at 36u is about 110u.</summary>
    public const float SettingsCountWidth = 200f;
    /// <summary>The number's size: the label's 40u less a step, InkSecondary (sketch: 13 against the title's 14).</summary>
    public const float SettingsCountSize = 36f;
    /// <summary>Between the number and its neighbours.</summary>
    public const float SettingsCountGap = 16f;
    /// <summary>The chevron's box, size and optical lift — the «Проверить ИИ-сотрудника» row's «›» (BotSettings.Test).</summary>
    public const float SettingsChevronWidth = 40f, SettingsChevronSize = 48f, SettingsChevronLift = 4f;
    /// <summary>The hint's size — the schedule hint's.</summary>
    public const float SettingsHintSize = 30f;

    private const string SettingsLabelName = "Label";
    private const string SettingsCountPreview = "0";   // BotSettings.RefreshPersonalChatsCount writes the bot's number
    private const float ReferenceWidth = 1080f;
    private const float HintMaxHeight = 600f;           // ten lines: a taller measurement is a broken one

    private static readonly Vector2 RightMiddle = new Vector2(1f, 0.5f);

    /// <summary>The «ЛИЧНЫЕ ЧАТЫ» section on a loaded BotSettings prefab (see the summary); true when something was changed.</summary>
    public static bool EnsureSettingsSection(BotSettings settings)
    {
        bool modified = false;
        EnsureSettingsSection(settings, ref modified);
        return modified;
    }

    // BotSettings.prefab, saved only when something in it changed.
    private static void WireSettingsPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SettingsPrefabPath);
        try
        {
            var settings = root.GetComponent<BotSettings>();
            if (settings == null) throw new InvalidOperationException($"{Tag} {SettingsPrefabPath}: no BotSettings on the prefab root.");
            bool modified = false;
            EnsureSettingsSection(settings, ref modified);
            if (!modified)
            {
                Debug.Log($"{Tag} {SettingsPrefabPath}: already applied — not saved.");
                return;
            }
            PrefabUtility.SaveAsPrefabAsset(root, SettingsPrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException($"{Tag} Could not save {SettingsPrefabPath}.");
            Debug.Log($"{Tag} {SettingsPrefabPath}: the «ЛИЧНЫЕ ЧАТЫ» section updated and saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureSettingsSection(BotSettings settings, ref bool modified)
    {
        SettingsAnchors anchors = SettingsAnchors.Of(settings);
        SectionHeader header = EnsureSettingsHeader(anchors, ref modified);
        (Button row, TextMeshProUGUI count) = EnsureSettingsRow(anchors, ref modified);
        TextMeshProUGUI hint = EnsureSettingsHint(anchors, ref modified);

        // header · row · hint, right before «РАСПИСАНИЕ РАБОТЫ» (each placed right before the next).
        modified |= PlaceBefore(hint.transform, anchors.ScheduleHeader);
        modified |= PlaceBefore(row.transform, hint.transform);
        modified |= PlaceBefore(header.transform, row.transform);

        var refs = new SerializedObject(settings);
        modified |= SetRef(refs, "personalChatsRow", row, Tag);
        modified |= SetRef(refs, "personalChatsCount", count, Tag);
    }

    // ---- the parts ----

    private static SectionHeader EnsureSettingsHeader(SettingsAnchors anchors, ref bool modified)
    {
        Transform existing = anchors.Content.Find(SettingsHeaderName);
        SectionHeader header = existing != null ? existing.GetComponent<SectionHeader>() : null;
        if (existing != null && header == null)
            throw new InvalidOperationException($"{Tag} {SettingsHeaderName} has no SectionHeader — fix it by hand, this tool deletes nothing.");
        if (header == null)
        {
            header = Object.Instantiate(anchors.SourceHeader, anchors.Content);
            header.gameObject.name = SettingsHeaderName;
            modified = true;
        }
        if (header.Text != PersonalChatsCopy.SettingsHeader)
        {
            header.Text = PersonalChatsCopy.SettingsHeader;
            modified = true;
        }
        return header;
    }

    // The channel rows' card as one button; inside it, left to right: the label, the number, the chevron.
    private static (Button row, TextMeshProUGUI count) EnsureSettingsRow(SettingsAnchors anchors, ref bool modified)
    {
        RectTransform row = EnsureChild(anchors.Content, SettingsRowName, ref modified, typeof(CanvasRenderer), typeof(Image),
            typeof(ImageWithRoundedCorners), typeof(Button), typeof(ThemedColor));
        if (anchors.CardShadow != null) AddMissing(row, ref modified, typeof(Shadow));
        modified |= SetHeight(row, anchors.CardHeight);   // the tab's list controls the width, never the height

        var card = row.GetComponent<Image>();
        modified |= SetImage(card, null, ThemeRole.Surface, raycast: true);
        modified |= EnsureRounded(card, anchors.CardRadius);
        modified |= BindTheme(row.GetComponent<ThemedColor>(), ThemeRole.Surface, card);
        if (anchors.CardShadow != null) modified |= SetShadow(row.GetComponent<Shadow>(), anchors.CardShadow);
        var button = row.GetComponent<Button>();
        modified |= SetButton(button, card, anchors.Press);

        float inset = anchors.LabelInset;
        float countX = -(inset + SettingsChevronWidth + SettingsCountGap);
        float labelEnd = countX - SettingsCountWidth - SettingsCountGap;   // the label's right edge, from the card's

        TextMeshProUGUI label = EnsureRowText(row, SettingsLabelName, 0, PersonalChatsCopy.SettingsRow, anchors.LabelFont,
            anchors.LabelSize, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis, ThemeRole.InkPrimary, ref modified);
        modified |= SetWeight(label, anchors.LabelWeight);
        modified |= SetRect((RectTransform)label.transform, new Vector2(0f, 0.5f), RightMiddle, LeftMiddle, new Vector2(inset, 0f),
            new Vector2(labelEnd - inset, anchors.LabelHeight));

        TextMeshProUGUI count = EnsureRowText(row, CountName, 1, SettingsCountPreview, anchors.LabelFont, SettingsCountSize,
            TextAlignmentOptions.Right, TextOverflowModes.Overflow, ThemeRole.InkSecondary, ref modified);
        modified |= SetRect((RectTransform)count.transform, RightMiddle, RightMiddle, RightMiddle, new Vector2(countX, 0f),
            new Vector2(SettingsCountWidth, anchors.LabelHeight));

        TextMeshProUGUI chevron = EnsureRowText(row, ChevronName, 2, PersonalChatsCopy.SettingsChevron, anchors.ChevronFont,
            SettingsChevronSize, TextAlignmentOptions.Center, TextOverflowModes.Overflow, ThemeRole.InkTertiary, ref modified);
        modified |= SetRect((RectTransform)chevron.transform, RightMiddle, RightMiddle, RightMiddle,
            new Vector2(-inset, SettingsChevronLift), new Vector2(SettingsChevronWidth, anchors.LabelHeight));

        return (button, count);
    }

    // A one-line text of the row: its slot among the row's children, its copy, font, size, alignment and role.
    private static TextMeshProUGUI EnsureRowText(RectTransform row, string name, int index, string text, TMP_FontAsset font, float size,
                                                 TextAlignmentOptions alignment, TextOverflowModes overflow, ThemeRole role, ref bool modified)
    {
        RectTransform rect = EnsureChild(row, name, ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ThemedColor));
        modified |= SetIndex(rect, index);
        var label = rect.GetComponent<TextMeshProUGUI>();
        modified |= SetLabel(label, text, font, size, alignment, overflow, role);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), role, label);
        return label;
    }

    private static TextMeshProUGUI EnsureSettingsHint(SettingsAnchors anchors, ref bool modified)
    {
        RectTransform rect = EnsureChild(anchors.Content, SettingsHintName, ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI),
            typeof(ThemedColor));
        var hint = rect.GetComponent<TextMeshProUGUI>();
        modified |= SetParagraph(hint, PersonalChatsCopy.SettingsHint, anchors.NoteFont, SettingsHintSize, TextAlignmentOptions.TopLeft,
            ThemeRole.InkTertiary);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), ThemeRole.InkTertiary, hint);
        modified |= SetHeight(rect, MeasuredHintHeight(hint, anchors.ListWidth));
        return hint;
    }

    // The hint wraps in the tab list's width; its box is its measured height. Measured on the hint itself, inside the loaded
    // prefab — the static SF Pro Regular atlas measures headless — so no probe object ever touches the open scene. A measurement
    // outside one line .. ten lines is a broken one (a never-laid-out TMP can report a tenth of the truth): stop, save nothing.
    private static float MeasuredHintHeight(TextMeshProUGUI hint, float width)
    {
        float measured = hint.GetPreferredValues(hint.text, width, 0f).y;
        if (measured < hint.fontSize || measured > HintMaxHeight)
            throw new InvalidOperationException($"{Tag} the settings hint measured {measured:0.##}u at width {width} — not a plausible height.");
        return Mathf.Ceil(measured);
    }

    // ---- setters: each compares first and reports whether it wrote ----

    private static bool SetWeight(TMP_Text label, FontWeight weight)
    {
        if (label.fontWeight == weight) return false;
        label.fontWeight = weight;
        return true;
    }

    private static bool SetShadow(Shadow shadow, Shadow source)
    {
        if (shadow.effectColor == source.effectColor && shadow.effectDistance == source.effectDistance
            && shadow.useGraphicAlpha == source.useGraphicAlpha)
            return false;
        shadow.effectColor = source.effectColor;
        shadow.effectDistance = source.effectDistance;
        shadow.useGraphicAlpha = source.useGraphicAlpha;
        return true;
    }

    // ---- what the Основное tab offers, through BotSettings' own references ----

    private sealed class SettingsAnchors
    {
        public RectTransform Content { get; private set; }
        public float ListWidth { get; private set; }
        public SectionHeader SourceHeader { get; private set; }
        public Transform ScheduleHeader { get; private set; }
        public float CardHeight { get; private set; }
        public float CardRadius { get; private set; }
        public Shadow CardShadow { get; private set; }
        public ColorBlock Press { get; private set; }
        public float LabelInset { get; private set; }
        public float LabelHeight { get; private set; }
        public TMP_FontAsset LabelFont { get; private set; }
        public float LabelSize { get; private set; }
        public FontWeight LabelWeight { get; private set; }
        public TMP_FontAsset ChevronFont { get; private set; }
        public TMP_FontAsset NoteFont { get; private set; }

        public static SettingsAnchors Of(BotSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var refs = new SerializedObject(settings);
            RectTransform content = ContentOf(settings, refs);
            ToggleRow channelRow = Required(settings.whatsappRow, "BotSettings.whatsappRow (the channel rows' card)");
            var cardLabel = Required(new SerializedObject(channelRow).FindProperty("labelText")?.objectReferenceValue as TextMeshProUGUI,
                "the channel row's label (ToggleRow.labelText)");
            var labelRect = (RectTransform)cardLabel.transform;
            var deleteButton = Required(refs.FindProperty("deleteBotButton")?.objectReferenceValue as Button,
                "BotSettings.deleteBotButton (the tab's button press)");
            var title = Required(refs.FindProperty("headerTitle")?.objectReferenceValue as TextMeshProUGUI,
                "BotSettings.headerTitle (the chevron's font, as the «Проверить ИИ-сотрудника» row's)");
            var note = Required(settings.BotNameField.GetComponentInChildren<TextMeshProUGUI>(true), "a label in BotNameField (the notes' font)");
            var list = Required(content.GetComponent<VerticalLayoutGroup>(), "the Основное tab's list layout");
            return new SettingsAnchors
            {
                Content = content,
                ListWidth = ReferenceWidth - list.padding.left - list.padding.right,
                SourceHeader = Required(content.GetComponentInChildren<SectionHeader>(true), "a SectionHeader in the Основное tab"),
                ScheduleHeader = ScheduleHeaderOf(settings, content),
                CardHeight = Positive(((RectTransform)channelRow.transform).sizeDelta.y, "the channel row's height"),
                CardRadius = Required(channelRow.GetComponent<ImageWithRoundedCorners>(), "the channel row's corners").radius,
                CardShadow = channelRow.GetComponent<Shadow>(),
                Press = deleteButton.colors,
                LabelInset = labelRect.anchoredPosition.x,
                LabelHeight = Positive(labelRect.sizeDelta.y, "the channel row's label height"),
                LabelFont = Required(cardLabel.font, "the channel row's label font"),
                LabelSize = cardLabel.fontSize,
                LabelWeight = cardLabel.fontWeight,
                ChevronFont = Required(title.font, "the header title's font"),
                NoteFont = Required(note.font, "BotNameField's label font"),
            };
        }

        // The Основное tab's list: BotNameField's parent, which General's ScrollRect scrolls (BotScheduleBuilder's check).
        private static RectTransform ContentOf(BotSettings settings, SerializedObject refs)
        {
            Required(settings.BotNameField, "BotSettings.BotNameField");
            var general = refs.FindProperty("General")?.objectReferenceValue as GameObject;
            var content = settings.BotNameField.transform.parent as RectTransform;
            ScrollRect scroll = general != null ? general.GetComponent<ScrollRect>() : null;
            if (content == null || scroll == null || scroll.content != content)
                throw new InvalidOperationException($"{Tag} Unexpected Основное tab hierarchy (BotNameField is not in General's scroll content).");
            return content;
        }

        // «РАСПИСАНИЕ РАБОТЫ»: the header right above the schedule's toggle (BotSettings.scheduleRow).
        private static Transform ScheduleHeaderOf(BotSettings settings, RectTransform content)
        {
            Transform toggle = settings.ScheduleRow != null ? settings.ScheduleRow.transform : null;
            if (toggle == null || toggle.parent != content)
                throw new InvalidOperationException($"{Tag} BotSettings.scheduleRow is not in the Основное tab — run Tools/BotSettings/Add Schedule Section first.");
            int index = toggle.GetSiblingIndex();
            Transform header = index > 0 ? content.GetChild(index - 1) : null;
            if (header == null || header.GetComponent<SectionHeader>() == null)
                throw new InvalidOperationException($"{Tag} No SectionHeader right above BotSettings.scheduleRow.");
            return header;
        }

        private static T Required<T>(T value, string what) where T : Object =>
            value != null ? value : throw new InvalidOperationException($"{Tag} BotSettings.prefab: {what} is missing.");

        private static float Positive(float value, string what) =>
            value > 0f ? value : throw new InvalidOperationException($"{Tag} BotSettings.prefab: {what} is {value}.");
    }
}
#endif
