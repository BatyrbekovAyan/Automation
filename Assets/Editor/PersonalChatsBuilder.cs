#if UNITY_EDITOR
using System;
using System.Linq;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static BuilderSceneUtil;
using Object = UnityEngine.Object;

/// <summary>
/// «Личные и рабочие чаты» (spec §5, sketch 009): the scene and prefab parts of the feature. ADDITIVE and idempotent — it
/// authors the objects it owns and nothing else, resolves every anchor through a serialized reference (ChatListView.content
/// and .prefab, ChatItemView's own fields), finds its own objects again by their names, and compares every value before it
/// writes it: a second run changes nothing and saves nothing (a scene save re-mints every Nobi material in Main.unity). The
/// building blocks it shares with AutoChipLearnLayerBuilder live in BuilderSceneUtil.
/// Run it with the Editor closed: Tools/run-editor-builder.sh PersonalChatsBuilder.BuildHeadless.
///
/// This step (the working chat list, spec §5.2):
///   • ChatItem.prefab — <see cref="EnsureChatItemLock"/>: AudienceLock, the lock a personal chat wears in the search
///     results, in the name's row right before the name; hidden; ChatItemView.audienceLock;
///   • Main.unity — <see cref="EnsureListRow"/>: PersonalChatsRow, the «Личные» row, in the list's Content right after
///     ChatsSearchBar (ChatListView.ApplyOrderNow keeps it there — it owns only its chat rows' slots). A chat row's twin:
///     its height, its paddings and avatar column, the lock (Lock.png) in an avatar-sized circle, «Личные» in the name's
///     font, a grey counter shaped like the unread badge, the bot card's chevron, the inset divider. Hidden — ChatListView
///     shows it — and built hidden, so nothing under it wakes while it is authored; ChatListView.personalRow.
///
///   ChatItem (SwipeContent/TextBlock/TopRow):  [AudienceLock][Name ……][Time]
///   PersonalChatsRow (Button, HLG):            [Avatar/Circle/Lock] [Title ……] [Counter/Count] [Chevron/Icon]  + Divider
///
/// Colours are ThemedColor bindings (the glyphs are white sprites tinted by role, never baked); the authored colours are
/// the light theme's, like the rest of the scene. Geometry is in 1080×1920 canvas reference units.
/// </summary>
public static class PersonalChatsBuilder
{
    public const string RowName = "PersonalChatsRow";
    public const string LockName = "AudienceLock";

    /// <summary>The lock before a chat's name: its ink (74 % of the sprite's height) is about the 44u name's cap height.</summary>
    public const float LockGlyphSize = 42f;
    /// <summary>Added to the glyph's slot; with the sprite's own transparent margin the ink stands ≈ 14u off the name.</summary>
    public const float LockGap = 4f;
    /// <summary>The lock inside the «Личные» row's circle.</summary>
    public const float RowLockSize = 64f;
    /// <summary>The chevron's slot and the glyph's inset in it — the bot card's (Bot.prefab BotArrow).</summary>
    public const float ChevronSlot = 44f, ChevronInset = 6f;

    private const string Tag = "[PersonalChatsBuilder]";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string LockIconPath = "Assets/Images/Icons/Lock.png";
    private const string ChevronIconPath = "Assets/Images/Chat/chevron-right.png";
    private const string IconName = "Icon";
    private const string AvatarName = "Avatar", CircleName = "Circle", RowLockName = "Lock", TitleName = "Title";
    private const string CounterName = "Counter", CountName = "Count", ChevronName = "Chevron", DividerName = "Divider";
    private const float DividerThickness = 1f;
    private const string CountPreview = "1";   // authored hidden; PersonalChatsRowView.Bind writes the number

    private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 LeftMiddle = new Vector2(0f, 0.5f);

    // ---- entry points ----

    [MenuItem("Tools/Personal Chats/Build")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Edit Mode only — leave Play mode first.");
            return;
        }
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded)
        {
            Debug.LogError($"{Tag} Open {ScenePath} first.");
            return;
        }
        Apply(scene);
        Debug.Log($"{Tag} Build complete.");
    }

    /// <summary>Batch entry (Editor closed): Tools/run-editor-builder.sh PersonalChatsBuilder.BuildHeadless</summary>
    public static void BuildHeadless()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath);
        Apply(scene);
        Debug.Log($"{Tag} Headless build + save complete.");
    }

    /// <summary>The lock in a chat row (see the class summary); true when something was changed.</summary>
    public static bool EnsureChatItemLock(ChatItemView item)
    {
        bool modified = false;
        EnsureChatItemLock(item, ref modified);
        return modified;
    }

    /// <summary>The «Личные» row in the list (see the class summary); true when something was changed.</summary>
    public static bool EnsureListRow(ChatListView list)
    {
        bool modified = false;
        EnsureListRow(list, ref modified);
        return modified;
    }

    // ---- the two places ----

    // The chat row prefab first, then the scene; each is saved only when something in it changed.
    private static void Apply(Scene scene)
    {
        ChatListView list = Single<ChatListView>(scene, Tag);
        if (list.prefab == null) throw new InvalidOperationException($"{Tag} ChatListView.prefab is not wired.");
        WireItemPrefab(AssetDatabase.GetAssetPath(list.prefab));

        bool modified = false;
        EnsureListRow(list, ref modified);
        if (!modified)
        {
            Debug.Log($"{Tag} {ScenePath}: already applied — not saved.");
            return;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"{Tag} Could not save {ScenePath}.");
        Debug.Log($"{Tag} {ScenePath}: the «Личные» row updated and saved.");
    }

    private static void WireItemPrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var item = root.GetComponent<ChatItemView>();
            if (item == null) throw new InvalidOperationException($"{Tag} {path}: no ChatItemView on the prefab root.");
            bool modified = false;
            EnsureChatItemLock(item, ref modified);
            if (!modified)
            {
                Debug.Log($"{Tag} {path}: already applied — not saved.");
                return;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved) throw new InvalidOperationException($"{Tag} Could not save {path}.");
            Debug.Log($"{Tag} {path}: the audience lock updated and saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- ChatItem: the lock before the name ----

    private static void EnsureChatItemLock(ChatItemView item, ref bool modified)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        TextMeshProUGUI name = item.titleText;
        if (name == null || name.transform.parent == null)
            throw new InvalidOperationException($"{Tag} {item.name}: ChatItemView.titleText is not wired into a row.");

        RectTransform slot = EnsureChild(name.transform.parent, LockName, ref modified, typeof(LayoutElement));
        modified |= PlaceBefore(slot, name.transform);
        modified |= SetLayout(slot.GetComponent<LayoutElement>(), LockGlyphSize + LockGap, LockGlyphSize);

        RectTransform icon = EnsureChild(slot, IconName, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(ThemedColor));
        var glyph = icon.GetComponent<Image>();
        modified |= SetGlyph(glyph, LoadSprite(LockIconPath), ThemeRole.InkTertiary);
        modified |= BindTheme(icon.GetComponent<ThemedColor>(), ThemeRole.InkTertiary, glyph);
        modified |= SetRect(icon, LeftMiddle, LeftMiddle, LeftMiddle, Vector2.zero, Vector2.one * LockGlyphSize);

        modified |= SetActive(slot.gameObject, false);   // ChatListView shows it on a personal chat in the search results
        modified |= SetRef(new SerializedObject(item), "audienceLock", slot.gameObject, Tag);
    }

    // ---- the list: the «Личные» row ----

    private static void EnsureListRow(ChatListView list, ref bool modified)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        if (list.content == null) throw new InvalidOperationException($"{Tag} ChatListView.content is not wired.");
        if (list.prefab == null) throw new InvalidOperationException($"{Tag} ChatListView.prefab is not wired.");
        RowMetrics metrics = RowMetrics.Of(list.prefab);
        var owner = new SerializedObject(list);

        RectTransform row = FindOrCreateRow(list.content, owner, ref modified);
        modified |= PlaceAfter(row, SearchBarOf(list.content));
        modified |= SetActive(row.gameObject, false);   // ChatListView shows it while there are personal chats

        var background = row.GetComponent<Image>();
        var button = row.GetComponent<Button>();
        modified |= SetLayout(row.GetComponent<LayoutElement>(), -1f, metrics.Height, -1f, metrics.Height);
        modified |= SetImage(background, null, ThemeRole.Surface, raycast: true);
        modified |= BindTheme(row.GetComponent<ThemedColor>(), ThemeRole.Surface, background);
        modified |= SetButton(button, background, metrics.Press);
        modified |= SetRowLayout(row.GetComponent<HorizontalLayoutGroup>(), metrics.PadLeft, metrics.PadRight, metrics.Spacing,
            TextAnchor.MiddleLeft);

        EnsureLockColumn(row, metrics, ref modified);
        EnsureTitle(row, metrics, ref modified);
        (GameObject counter, TextMeshProUGUI count) = EnsureCounter(row, metrics, ref modified);
        EnsureChevron(row, ref modified);
        EnsureDivider(row, metrics, ref modified);

        var view = new SerializedObject(row.GetComponent<PersonalChatsRowView>());
        modified |= SetRef(view, "button", button, Tag);
        modified |= SetRef(view, "counter", counter, Tag);
        modified |= SetRef(view, "counterText", count, Tag);
        modified |= SetRef(owner, "personalRow", row.GetComponent<PersonalChatsRowView>(), Tag);
    }

    // The row ChatListView already holds, else the one in Content by its name, else a new one — built HIDDEN from its
    // first moment, so no OnEnable under it (Nobi's material, PixelSnapLine's snapped thickness) runs while it is authored.
    private static RectTransform FindOrCreateRow(Transform content, SerializedObject owner, ref bool modified)
    {
        var wired = owner.FindProperty("personalRow")?.objectReferenceValue as PersonalChatsRowView;
        var row = wired != null && wired.transform.parent == content ? (RectTransform)wired.transform : content.Find(RowName) as RectTransform;
        if (row == null)
        {
            var go = new GameObject(RowName, typeof(RectTransform)) { layer = content.gameObject.layer };
            go.SetActive(false);
            row = (RectTransform)go.transform;
            row.SetParent(content, false);
            modified = true;
        }
        AddMissing(row, ref modified, typeof(LayoutElement), typeof(CanvasRenderer), typeof(Image), typeof(Button),
            typeof(HorizontalLayoutGroup), typeof(ThemedColor), typeof(PersonalChatsRowView));
        return row;
    }

    private static Transform SearchBarOf(Transform content)
    {
        Transform[] bars = content.Cast<Transform>().Where(child => child.GetComponent<ChatSearchBar>() != null).ToArray();
        if (bars.Length != 1)
            throw new InvalidOperationException($"{Tag} Expected one ChatSearchBar right under the list's Content, found {bars.Length}.");
        return bars[0];
    }

    // The avatar column of a chat row; in it an avatar-sized circle, and the lock in the circle.
    private static void EnsureLockColumn(RectTransform row, RowMetrics metrics, ref bool modified)
    {
        RectTransform column = EnsureChild(row, AvatarName, ref modified, typeof(LayoutElement));
        modified |= SetIndex(column, 0);
        modified |= SetLayout(column.GetComponent<LayoutElement>(), metrics.AvatarColumn.x, metrics.AvatarColumn.y);

        RectTransform circle = EnsureChild(column, CircleName, ref modified, typeof(CanvasRenderer), typeof(Image),
            typeof(ImageWithRoundedCorners), typeof(ThemedColor));
        var disc = circle.GetComponent<Image>();
        modified |= SetImage(disc, null, ThemeRole.Hairline, raycast: false);
        modified |= EnsureRounded(disc, metrics.Circle.x / 2f);
        modified |= BindTheme(circle.GetComponent<ThemedColor>(), ThemeRole.Hairline, disc);
        modified |= SetRect(circle, Centre, Centre, Centre, Vector2.zero, metrics.Circle);

        RectTransform lockRect = EnsureChild(circle, RowLockName, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(ThemedColor));
        var glyph = lockRect.GetComponent<Image>();
        modified |= SetGlyph(glyph, LoadSprite(LockIconPath), ThemeRole.InkTertiary);
        modified |= BindTheme(lockRect.GetComponent<ThemedColor>(), ThemeRole.InkTertiary, glyph);
        modified |= SetRect(lockRect, Centre, Centre, Centre, Vector2.zero, Vector2.one * RowLockSize);
    }

    // «Личные» in the chat name's font — it takes the row's free width, like the name does.
    private static void EnsureTitle(RectTransform row, RowMetrics metrics, ref bool modified)
    {
        RectTransform rect = EnsureChild(row, TitleName, ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI),
            typeof(LayoutElement), typeof(ThemedColor));
        modified |= SetIndex(rect, 1);
        var title = rect.GetComponent<TextMeshProUGUI>();
        modified |= SetLabel(title, PersonalChatsCopy.RowTitle, metrics.TitleFont, metrics.TitleSize, TextAlignmentOptions.Left,
            TextOverflowModes.Ellipsis, ThemeRole.InkPrimary);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), ThemeRole.InkPrimary, title);
        modified |= SetLayout(rect.GetComponent<LayoutElement>(), 0f, -1f, 0f, -1f, flexibleWidth: 1f);
    }

    // The grey counter: the unread badge's shape (height, radius, paddings, font), InkTertiary instead of the channel's
    // colour. Hidden — PersonalChatsRowView.Bind shows it when a personal chat has something unread.
    private static (GameObject, TextMeshProUGUI) EnsureCounter(RectTransform row, RowMetrics metrics, ref bool modified)
    {
        RectTransform pill = EnsureChild(row, CounterName, ref modified, typeof(CanvasRenderer), typeof(Image),
            typeof(ImageWithRoundedCorners), typeof(HorizontalLayoutGroup), typeof(LayoutElement), typeof(ThemedColor));
        modified |= SetIndex(pill, 2);
        var fill = pill.GetComponent<Image>();
        modified |= SetImage(fill, null, ThemeRole.InkTertiary, raycast: false);
        modified |= EnsureRounded(fill, metrics.BadgeRadius);
        modified |= BindTheme(pill.GetComponent<ThemedColor>(), ThemeRole.InkTertiary, fill);
        modified |= SetRowLayout(pill.GetComponent<HorizontalLayoutGroup>(), metrics.BadgePadLeft, metrics.BadgePadRight, 0f,
            TextAnchor.MiddleCenter);
        modified |= SetLayout(pill.GetComponent<LayoutElement>(), metrics.BadgeHeight, metrics.BadgeHeight, -1f, metrics.BadgeHeight);
        modified |= SetActive(pill.gameObject, false);

        RectTransform rect = EnsureChild(pill, CountName, ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI),
            typeof(LayoutElement), typeof(ThemedColor));
        var count = rect.GetComponent<TextMeshProUGUI>();
        modified |= SetLabel(count, CountPreview, metrics.CountFont, metrics.CountSize, TextAlignmentOptions.Center,
            TextOverflowModes.Overflow, ThemeRole.AccentOnFill);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), ThemeRole.AccentOnFill, count);
        // A floor only (the badge's own): the number's width is TMP's, so «12» widens the pill.
        modified |= SetLayout(rect.GetComponent<LayoutElement>(), metrics.CountMinWidth, -1f, -1f, -1f);
        return (pill.gameObject, count);
    }

    private static void EnsureChevron(RectTransform row, ref bool modified)
    {
        RectTransform slot = EnsureChild(row, ChevronName, ref modified, typeof(LayoutElement));
        modified |= SetIndex(slot, 3);
        modified |= SetLayout(slot.GetComponent<LayoutElement>(), ChevronSlot, ChevronSlot);

        RectTransform icon = EnsureChild(slot, IconName, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(ThemedColor));
        var glyph = icon.GetComponent<Image>();
        modified |= SetGlyph(glyph, LoadSprite(ChevronIconPath), ThemeRole.InkTertiary);
        modified |= BindTheme(icon.GetComponent<ThemedColor>(), ThemeRole.InkTertiary, glyph);
        modified |= SetRect(icon, Vector2.zero, Vector2.one, Centre, Vector2.zero, Vector2.one * (-2f * ChevronInset));
    }

    // A chat row's divider: the row's bottom edge, from the text column to the right edge. Its thickness belongs to
    // PixelSnapLine (it snaps it to whole pixels at runtime), so it is written once, when the line is made.
    private static void EnsureDivider(RectTransform row, RowMetrics metrics, ref bool modified)
    {
        bool fresh = row.Find(DividerName) == null;
        RectTransform line = EnsureChild(row, DividerName, ref modified, typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement), typeof(PixelSnapLine), typeof(ThemedColor));
        modified |= SetIndex(line, 4);
        var image = line.GetComponent<Image>();
        modified |= SetImage(image, null, ThemeRole.Hairline, raycast: false);
        modified |= BindTheme(line.GetComponent<ThemedColor>(), ThemeRole.Hairline, image);
        modified |= SetIgnoreLayout(line.GetComponent<LayoutElement>());
        modified |= SetSnap(line.GetComponent<PixelSnapLine>());

        float inset = metrics.TextColumnX;
        float thickness = fresh ? DividerThickness : line.sizeDelta.y;
        modified |= SetRect(line, Vector2.zero, Vector2.right, new Vector2(0.5f, 0f), new Vector2(inset / 2f, 0f),
            new Vector2(-inset, thickness));
    }

    // ---- what a chat row looks like (ChatItem.prefab, through ChatItemView's own references) ----

    private sealed class RowMetrics
    {
        public float Height { get; private set; }
        public int PadLeft { get; private set; }
        public int PadRight { get; private set; }
        public float Spacing { get; private set; }
        public Vector2 AvatarColumn { get; private set; }
        public Vector2 Circle { get; private set; }
        public TMP_FontAsset TitleFont { get; private set; }
        public float TitleSize { get; private set; }
        public float BadgeHeight { get; private set; }
        public float BadgeRadius { get; private set; }
        public int BadgePadLeft { get; private set; }
        public int BadgePadRight { get; private set; }
        public TMP_FontAsset CountFont { get; private set; }
        public float CountSize { get; private set; }
        public float CountMinWidth { get; private set; }
        public ColorBlock Press { get; private set; }

        /// <summary>Where a chat row's text column — and its divider — begins.</summary>
        public float TextColumnX => PadLeft + AvatarColumn.x + Spacing;

        public static RowMetrics Of(ChatItemView item)
        {
            var row = Required(item.button != null ? item.button.GetComponent<HorizontalLayoutGroup>() : null, "the row's layout (button)");
            var avatarColumn = Required(item.avatarImage != null ? item.avatarImage.transform.parent.GetComponent<LayoutElement>() : null,
                "the avatar column (avatarImage)");
            var badge = Required(item.unreadBadge, "the unread badge (unreadBadge)");
            var countText = Required(item.unreadCountText, "the unread count (unreadCountText)");
            return new RowMetrics
            {
                Height = Positive(Required(item.GetComponent<LayoutElement>(), "the row's height").preferredHeight, "the row's height"),
                PadLeft = row.padding.left,
                PadRight = row.padding.right,
                Spacing = row.spacing,
                AvatarColumn = new Vector2(avatarColumn.preferredWidth, avatarColumn.preferredHeight),
                Circle = item.avatarImage.rectTransform.sizeDelta,
                TitleFont = Required(item.titleText, "the name (titleText)").font,
                TitleSize = item.titleText.fontSize,
                BadgeHeight = Positive(((RectTransform)badge.transform).sizeDelta.y, "the unread badge's height"),
                BadgeRadius = Required(badge.GetComponent<ImageWithRoundedCorners>(), "the unread badge's corners").radius,
                BadgePadLeft = Required(badge.GetComponent<HorizontalLayoutGroup>(), "the unread badge's layout").padding.left,
                BadgePadRight = badge.GetComponent<HorizontalLayoutGroup>().padding.right,
                CountFont = countText.font,
                CountSize = countText.fontSize,
                CountMinWidth = countText.GetComponent<LayoutElement>() != null ? countText.GetComponent<LayoutElement>().minWidth : -1f,
                Press = Required(item.button, "the row's button (button)").colors,
            };
        }

        private static T Required<T>(T value, string what) where T : Object =>
            value != null ? value : throw new InvalidOperationException($"{Tag} ChatItem.prefab: {what} is missing — a chat row's metrics come from it.");

        private static float Positive(float value, string what) =>
            value > 0f ? value : throw new InvalidOperationException($"{Tag} ChatItem.prefab: {what} is {value}.");
    }

    // ---- setters: each compares first and reports whether it wrote ----

    private static bool SetLabel(TextMeshProUGUI label, string text, TMP_FontAsset font, float size, TextAlignmentOptions alignment,
                                 TextOverflowModes overflow, ThemeRole role)
    {
        Color ink = Theme.Light.Resolve(role);
        if (label.text == text && label.font == font && Mathf.Approximately(label.fontSize, size) && label.fontStyle == FontStyles.Normal
            && label.alignment == alignment && label.textWrappingMode == TextWrappingModes.NoWrap && label.overflowMode == overflow
            && !label.enableAutoSizing && !label.raycastTarget && label.color == ink)
            return false;
        label.text = text;
        label.font = font;
        label.fontSize = size;
        label.fontStyle = FontStyles.Normal;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = overflow;
        label.enableAutoSizing = false;
        label.raycastTarget = false;   // only the row's background takes a tap
        label.color = ink;             // opaque — the ThemedColor binding keeps the authored alpha
        return true;
    }

    // A white glyph sprite, tinted by its role at runtime; never a tap target.
    private static bool SetGlyph(Image image, Sprite sprite, ThemeRole role)
    {
        bool changed = SetImage(image, sprite, role, raycast: false);
        if (image.preserveAspect) return changed;
        image.preserveAspect = true;
        return true;
    }

    private static bool SetImage(Image image, Sprite sprite, ThemeRole role, bool raycast)
    {
        Color color = Theme.Light.Resolve(role);
        if (image.sprite == sprite && image.type == Image.Type.Simple && image.raycastTarget == raycast && image.maskable
            && image.color == color)
            return false;
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.raycastTarget = raycast;
        image.maskable = true;
        image.color = color;
        return true;
    }

    private static bool SetButton(Button button, Graphic target, ColorBlock press)
    {
        if (button.transition == Selectable.Transition.ColorTint && button.targetGraphic == target && button.colors == press)
            return false;
        button.transition = Selectable.Transition.ColorTint;   // a chat row's press, from ChatItem's own button
        button.targetGraphic = target;
        button.colors = press;
        return true;
    }

    private static bool SetRowLayout(HorizontalLayoutGroup group, int padLeft, int padRight, float spacing, TextAnchor alignment)
    {
        RectOffset padding = group.padding;
        if (padding.left == padLeft && padding.right == padRight && padding.top == 0 && padding.bottom == 0
            && Mathf.Approximately(group.spacing, spacing) && group.childAlignment == alignment
            && group.childControlWidth && group.childControlHeight && !group.childForceExpandWidth && !group.childForceExpandHeight
            && !group.childScaleWidth && !group.childScaleHeight && !group.reverseArrangement)
            return false;
        group.padding = new RectOffset(padLeft, padRight, 0, 0);
        group.spacing = spacing;
        group.childAlignment = alignment;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        group.childScaleWidth = group.childScaleHeight = false;
        group.reverseArrangement = false;
        return true;
    }

    // A fixed slot: min = preferred on both axes (-1 leaves an axis to the content).
    private static bool SetLayout(LayoutElement element, float width, float height) =>
        SetLayout(element, width, height, width, height);

    private static bool SetLayout(LayoutElement element, float minWidth, float minHeight, float preferredWidth, float preferredHeight,
                                  float flexibleWidth = -1f)
    {
        if (!element.ignoreLayout && Same(element.minWidth, minWidth) && Same(element.minHeight, minHeight)
            && Same(element.preferredWidth, preferredWidth) && Same(element.preferredHeight, preferredHeight)
            && Same(element.flexibleWidth, flexibleWidth) && Same(element.flexibleHeight, -1f))
            return false;
        element.ignoreLayout = false;
        element.minWidth = minWidth;
        element.minHeight = minHeight;
        element.preferredWidth = preferredWidth;
        element.preferredHeight = preferredHeight;
        element.flexibleWidth = flexibleWidth;
        element.flexibleHeight = -1f;
        return true;
    }

    private static bool SetIgnoreLayout(LayoutElement element)
    {
        if (element.ignoreLayout) return false;
        element.ignoreLayout = true;
        return true;
    }

    private static bool SetSnap(PixelSnapLine snap)
    {
        var line = new SerializedObject(snap);
        SerializedProperty axis = line.FindProperty("axis");
        SerializedProperty thickness = line.FindProperty("designThicknessUnits");
        if (axis.enumValueIndex == (int)PixelSnapLine.SnapAxis.Height && Mathf.Approximately(thickness.floatValue, DividerThickness))
            return false;
        axis.enumValueIndex = (int)PixelSnapLine.SnapAxis.Height;
        thickness.floatValue = DividerThickness;
        line.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // Both fields through one SerializedObject — "image" is private, and Nobi's own Validate sets it only on enable.
    // The radius is 1:1 with the visual radius — never halved or doubled (CLAUDE.md).
    private static bool EnsureRounded(Image image, float radius)
    {
        var rounded = new SerializedObject(image.GetComponent<ImageWithRoundedCorners>());
        SerializedProperty radiusProp = rounded.FindProperty("radius");
        SerializedProperty imageProp = rounded.FindProperty("image");
        if (Mathf.Approximately(radiusProp.floatValue, radius) && imageProp.objectReferenceValue == image) return false;
        radiusProp.floatValue = radius;
        imageProp.objectReferenceValue = image;
        rounded.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static bool SetActive(GameObject gameObject, bool active)
    {
        if (gameObject.activeSelf == active) return false;
        gameObject.SetActive(active);
        return true;
    }

    // ---- hierarchy helpers (the shared ones — EnsureChild, AddMissing, PlaceAfter, SetRect, BindTheme, SetRef, Single —
    // are BuilderSceneUtil's) ----

    private static bool SetIndex(Transform item, int index)
    {
        if (item.GetSiblingIndex() == index) return false;
        item.SetSiblingIndex(index);
        return true;
    }

    // Moves item to right before anchor (both children of one parent).
    private static bool PlaceBefore(Transform item, Transform anchor)
    {
        int index = item.GetSiblingIndex();
        int target = anchor.GetSiblingIndex() - (index < anchor.GetSiblingIndex() ? 1 : 0);
        if (index == target) return false;
        item.SetSiblingIndex(target);
        return true;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        return sprite != null ? sprite : throw new InvalidOperationException($"{Tag} No sprite at {path}.");
    }

    private static bool Same(float a, float b) => Mathf.Approximately(a, b);
}
#endif
