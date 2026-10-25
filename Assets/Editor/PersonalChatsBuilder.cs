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
/// and .prefab, ChatItemView's and MessageHeaderView's own fields), finds its own objects again by their names, and compares every value before it
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
/// The swipe (spec §5.2, §5.3):
///   • ChatItem.prefab — <see cref="EnsureChatItemMoveButton"/>: MoveButton, the swipe's «В личные» / «В рабочие», 200u
///     (<see cref="MoveButtonWidth"/>) right on the left of DeleteButton and right before it among the siblings, so both stay
///     behind the sliding SwipeContent; «Удалить»'s twin in shape (anchors, height, press, word font and size) in AccentFill
///     with an AccentOnFill word. Authored hidden — SwipeToDelete.SetActions shows it when the row offers a move — and wired:
///     SwipeToDelete.moveButton, ChatItemView.moveButton / moveLabel. Anchored through SwipeToDelete.deleteButton.
///
/// The open chat's header (spec §5.5, §5.6, sketch 009 screens 5–6):
///   • Main.unity — <see cref="EnsureHeaderParts"/>: PersonalPill, the «Личный» pill, right after the «Авто» chip in its zone,
///     as tall as the chip and <see cref="PersonalPillWidth"/> wide: the chip's transparent tap box over the chip's
///     ring-and-fill pill (a Border ring, a Hairline fill) holding the lock (Lock.png) and «Личный» in the chip's word font,
///     both InkSecondary. Hidden — MessageHeaderView
///     swaps it with the chip in a personal chat. NameTap, a transparent button over the avatar and the name column, the
///     zone's last child and ignored by its layout group, from the avatar's left edge — never over the back button.
///     Anchored through MessageHeaderView's own fields (avatarImage, nameText) and the chip the SuggestionsController drives;
///     MessageHeaderView's semiAutoToggle, personalPill, pillLock, nameTap and suggestions are stamped.
///
///   ChatItem (SwipeContent/TextBlock/TopRow):  [AudienceLock][Name ……][Time]
///   ChatItem (behind SwipeContent):            … row's right edge | [MoveButton/Label][DeleteButton/Label]  (parked at rest)
///   PersonalChatsRow (Button, HLG):            [Avatar/Circle/Lock] [Title ……] [Counter/Count] [Chevron/Icon]  + Divider
///   TopBar/LeftZone:                           [BackButton] [Avatar] [Text/Name ……]  + NameTap (over Avatar..Text)
///   TopBar/RightZone:                          [SemiAutoToggle] [PersonalPill/Pill(Ring, Fill, Content[Lock][Label])]
///
/// The «Личные» folder (spec §5.3, §5.4; sketch 009 screens 1, 3, 4) — PersonalChatsBuilder.Folder.cs:
///   • ChatItem.prefab — <see cref="EnsureChatItemSelectCheck"/>: SelectCheck, the checkbox of the folder's selection modes,
///     right before the avatar; hidden; ChatItemView.selectCheck / selectChecked;
///   • Main.unity — <see cref="EnsureFolderPanel"/>: Screen_Messanger/PersonalChatsPanel, right after the chat list and before
///     the open chat, over the tab bar like it; hidden; every PersonalChatsFolderView part wired. The chat list's delete card
///     moves up beside it, over it — a folder row's «Удалить» asks through it.
///
/// Colours are ThemedColor bindings (the glyphs are white sprites tinted by role, never baked); the authored colours are
/// the light theme's, like the rest of the scene. Geometry is in 1080×1920 canvas reference units.
/// </summary>
public static partial class PersonalChatsBuilder
{
    public const string RowName = "PersonalChatsRow";
    public const string LockName = "AudienceLock";
    public const string MoveButtonName = "MoveButton";
    public const string PersonalPillName = "PersonalPill";
    public const string NameTapName = "NameTap";

    /// <summary>The pill's lock: its ink (74 % of the sprite's height) is about the cap height of the chip's 26u word.</summary>
    public const float PillLockSize = 26f;
    /// <summary>Between the lock and «Личный»; the sprite's own transparent margin adds ≈ 3u — the chip's lamp-to-word rhythm.</summary>
    public const float PillSpacing = 8f;
    /// <summary>The least room the pill keeps on each side of the lock and the word (pinned by the premise tests).</summary>
    public const float PillMinPadding = 20f;
    /// <summary>
    /// The pill's width — the chip's height, but wider than the chip's 170u: «Личный» in the chip's word font measures ≈ 111u,
    /// so the lock and the word need ≈ 145u and the pill keeps ≈ 27u on each side. Its zone's layout group seats it on the
    /// zone's right edge, where the chip stands.
    /// </summary>
    public const float PersonalPillWidth = 200f;
    /// <summary>
    /// How far the name's tap reaches above and below its 100u zone: 132u in all, the house touch floor. Above the zone is the
    /// bar's status strip, below it the bar's own bottom edge — nothing there takes a tap.
    /// </summary>
    public const float NameTapBleed = 16f;

    /// <summary>The swipe's move button: as wide as «Удалить» (200u), right on its left.</summary>
    public const float MoveButtonWidth = 200f;

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
    private const string MoveLabelName = "Label";
    private const string PillFaceName = "Pill", RingName = "Ring", FillName = "Fill", ContentName = "Content", PillWordName = "Label";
    private const float PillRingWidth = 3f;   // the chip's: its Fill sits 3u inside its Ring
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

    /// <summary>The swipe's move button in a chat row (see the class summary); true when something was changed.</summary>
    public static bool EnsureChatItemMoveButton(ChatItemView item)
    {
        bool modified = false;
        EnsureChatItemMoveButton(item, ref modified);
        return modified;
    }

    /// <summary>The «Личные» row in the list (see the class summary); true when something was changed.</summary>
    public static bool EnsureListRow(ChatListView list)
    {
        bool modified = false;
        EnsureListRow(list, ref modified);
        return modified;
    }

    /// <summary>
    /// The open chat's header parts — the «Личный» pill and the name's tap (see the class summary); true when something was
    /// changed. <paramref name="suggestions"/> — the scene's SuggestionsController, stamped on the header (null: left as is).
    /// </summary>
    public static bool EnsureHeaderParts(MessageHeaderView header, SuggestionsController suggestions)
    {
        bool modified = false;
        EnsureHeaderParts(header, suggestions, ref modified);
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
        EnsureHeaderParts(Single<MessageHeaderView>(scene, Tag), Single<SuggestionsController>(scene, Tag), ref modified);
        GameObject thread = Single<ChatManager>(scene, Tag).MessageListPanel;
        if (thread == null) throw new InvalidOperationException($"{Tag} ChatManager.MessageListPanel is not wired.");
        EnsureFolderPanel(list, thread.transform, ref modified);
        if (!modified)
        {
            Debug.Log($"{Tag} {ScenePath}: already applied — not saved.");
            return;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"{Tag} Could not save {ScenePath}.");
        Debug.Log($"{Tag} {ScenePath}: the «Личные» row, the open chat's header parts and the «Личные» folder updated and saved.");
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
            EnsureChatItemMoveButton(item, ref modified);
            EnsureChatItemSelectCheck(item, ref modified);
            if (!modified)
            {
                Debug.Log($"{Tag} {path}: already applied — not saved.");
                return;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved) throw new InvalidOperationException($"{Tag} Could not save {path}.");
            Debug.Log($"{Tag} {path}: the audience lock, the swipe's move button and the checkbox updated and saved.");
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

    // ---- ChatItem: the swipe's move button, right on «Удалить»'s left ----

    // «Удалить»'s twin in shape — pinned to the row's right edge, as tall, «Удалить»'s press and word style — in AccentFill under
    // an AccentOnFill word, right on its left. Hidden: SwipeToDelete.SetActions shows it when the row offers a move.
    private static void EnsureChatItemMoveButton(ChatItemView item, ref bool modified)
    {
        (SerializedObject swipeRefs, RectTransform delete, TextMeshProUGUI deleteWord) = DeleteButtonOf(item);

        RectTransform move = FindOrCreateMoveButton(delete, swipeRefs, ref modified);
        modified |= PlaceBefore(move, delete);
        modified |= SetRect(move, delete.anchorMin, delete.anchorMax, delete.pivot,
            new Vector2(delete.anchoredPosition.x - MoveButtonWidth, delete.anchoredPosition.y),
            new Vector2(MoveButtonWidth, delete.sizeDelta.y));
        (Button button, TextMeshProUGUI word) = EnsureMoveFace(move, item.deleteButton.colors, deleteWord, ref modified);
        modified |= SetActive(move.gameObject, false);

        modified |= SetRef(swipeRefs, "moveButton", move, Tag);
        var view = new SerializedObject(item);
        modified |= SetRef(view, "moveButton", button, Tag);
        modified |= SetRef(view, "moveLabel", word, Tag);
    }

    // «Удалить» as the row's swipe holds it (SwipeToDelete.deleteButton — the same button as ChatItemView.deleteButton) and
    // its word, which the move button's word matches.
    private static (SerializedObject swipeRefs, RectTransform delete, TextMeshProUGUI word) DeleteButtonOf(ChatItemView item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        SwipeToDelete swipe = item.swipeToDelete;
        if (swipe == null) throw new InvalidOperationException($"{Tag} {item.name}: ChatItemView.swipeToDelete is not wired.");
        var swipeRefs = new SerializedObject(swipe);
        var delete = swipeRefs.FindProperty("deleteButton")?.objectReferenceValue as RectTransform;
        if (delete == null || delete.parent == null)
            throw new InvalidOperationException($"{Tag} {item.name}: SwipeToDelete.deleteButton is not wired into the row.");
        if (item.deleteButton == null || item.deleteButton.transform != delete)
            throw new InvalidOperationException($"{Tag} {item.name}: ChatItemView.deleteButton is not SwipeToDelete's «Удалить».");
        TextMeshProUGUI word = delete.GetComponentInChildren<TextMeshProUGUI>(true);
        if (word == null) throw new InvalidOperationException($"{Tag} {item.name}: «Удалить» has no TMP word to match.");
        return (swipeRefs, delete, word);
    }

    // The fill — AccentFill, the tap target, pressed like «Удалить» — and the word: «В личные» (the working list's; the row
    // rewrites it per place), «Удалить»'s font and size, AccentOnFill, filling the button.
    private static (Button button, TextMeshProUGUI word) EnsureMoveFace(RectTransform move, ColorBlock press, TextMeshProUGUI deleteWord,
                                                                        ref bool modified)
    {
        var fill = move.GetComponent<Image>();
        modified |= SetImage(fill, null, ThemeRole.AccentFill, raycast: true);
        modified |= BindTheme(move.GetComponent<ThemedColor>(), ThemeRole.AccentFill, fill);
        var button = move.GetComponent<Button>();
        modified |= SetButton(button, fill, press);

        RectTransform wordRect = EnsureChild(move, MoveLabelName, ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI),
            typeof(ThemedColor));
        var word = wordRect.GetComponent<TextMeshProUGUI>();
        modified |= SetLabel(word, PersonalChatsCopy.SwipeToPersonal, deleteWord.font, deleteWord.fontSize, TextAlignmentOptions.Center,
            TextOverflowModes.Overflow, ThemeRole.AccentOnFill);
        modified |= BindTheme(wordRect.GetComponent<ThemedColor>(), ThemeRole.AccentOnFill, word);
        modified |= SetRect(wordRect, Vector2.zero, Vector2.one, Centre, Vector2.zero, Vector2.zero);
        return (button, word);
    }

    // The button the swipe already holds, else the one beside «Удалить» by its name, else a new one — built HIDDEN from its
    // first moment, so nothing under it wakes while it is authored.
    private static RectTransform FindOrCreateMoveButton(RectTransform delete, SerializedObject swipeRefs, ref bool modified)
    {
        var wired = swipeRefs.FindProperty("moveButton")?.objectReferenceValue as RectTransform;
        var move = wired != null && wired.parent == delete.parent ? wired : delete.parent.Find(MoveButtonName) as RectTransform;
        if (move == null)
        {
            var go = new GameObject(MoveButtonName, typeof(RectTransform)) { layer = delete.parent.gameObject.layer };
            go.SetActive(false);
            move = (RectTransform)go.transform;
            move.SetParent(delete.parent, false);
            modified = true;
        }
        AddMissing(move, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(ThemedColor));
        return move;
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

    // ---- the open chat's header: the «Личный» pill and the name's tap ----

    private static void EnsureHeaderParts(MessageHeaderView header, SuggestionsController suggestions, ref bool modified)
    {
        if (header == null) throw new ArgumentNullException(nameof(header));
        var view = new SerializedObject(header);
        SemiAutoToggle chip = ChipOf(header, view, suggestions);
        (Button pill, Image pillLock) = EnsurePersonalPill(chip, view.FindProperty("personalPill")?.objectReferenceValue as Button, ref modified);
        Button nameTap = EnsureNameTap(header, view.FindProperty("nameTap")?.objectReferenceValue as Button, ref modified);

        modified |= SetRef(view, "semiAutoToggle", chip, Tag);
        modified |= SetRef(view, "personalPill", pill, Tag);
        modified |= SetRef(view, "pillLock", pillLock, Tag);
        modified |= SetRef(view, "nameTap", nameTap, Tag);
        if (suggestions != null) modified |= SetRef(view, "suggestions", suggestions, Tag);
    }

    // The «Авто» chip: the header's own reference once stamped, else the one chip in the header — and it must be the very chip
    // the suggestions controller paints (the header hides what the controller drives).
    private static SemiAutoToggle ChipOf(MessageHeaderView header, SerializedObject view, SuggestionsController suggestions)
    {
        var chip = view.FindProperty("semiAutoToggle")?.objectReferenceValue as SemiAutoToggle;
        if (chip == null)
        {
            SemiAutoToggle[] chips = header.GetComponentsInChildren<SemiAutoToggle>(true);
            if (chips.Length != 1)
                throw new InvalidOperationException($"{Tag} Expected one SemiAutoToggle under {header.name}, found {chips.Length}.");
            chip = chips[0];
        }
        if (chip.transform.parent == null) throw new InvalidOperationException($"{Tag} The «Авто» chip has no zone.");
        if (suggestions != null)
        {
            var driven = new SerializedObject(suggestions).FindProperty("_toggle")?.objectReferenceValue as SemiAutoToggle;
            if (driven != null && driven != chip)
                throw new InvalidOperationException($"{Tag} The header's «Авто» chip is not SuggestionsController._toggle.");
        }
        return chip;
    }

    // «Личный» in the chip's place: the chip's anchors and height (its width is <see cref="PersonalPillWidth"/>), its
    // transparent tap box, its ring-and-fill pill (a Border ring, a Hairline fill) and in it the lock and the word in the
    // chip's word font, InkSecondary. Hidden — MessageHeaderView shows it in a personal chat. Right after the chip in its
    // zone, so the zone's layout group seats it on the zone's right edge, where the chip stands.
    private static (Button pill, Image lockImage) EnsurePersonalPill(SemiAutoToggle chip, Button wired, ref bool modified)
    {
        var chipRect = (RectTransform)chip.transform;
        var chipRefs = new SerializedObject(chip);
        var chipRing = chipRefs.FindProperty("ringImage")?.objectReferenceValue as Image;
        var chipWord = chipRefs.FindProperty("label")?.objectReferenceValue as TextMeshProUGUI;
        if (chipRing == null || chipRing.transform.parent == null)
            throw new InvalidOperationException($"{Tag} SemiAutoToggle.ringImage is not wired into the chip's pill.");
        if (chipWord == null) throw new InvalidOperationException($"{Tag} SemiAutoToggle.label is not wired.");
        var chipFace = (RectTransform)chipRing.transform.parent;

        RectTransform pill = FindOrCreatePill(chipRect.parent, wired, ref modified);
        modified |= PlaceAfter(pill, chipRect);
        modified |= SetRect(pill, chipRect.anchorMin, chipRect.anchorMax, chipRect.pivot, chipRect.anchoredPosition,
            new Vector2(PersonalPillWidth, chipRect.sizeDelta.y));
        var hit = pill.GetComponent<Image>();
        modified |= SetHitBox(hit);
        modified |= SetTapButton(pill.GetComponent<Button>(), hit);
        modified |= SetActive(pill.gameObject, false);

        RectTransform face = EnsureChild(pill, PillFaceName, ref modified);
        modified |= SetRect(face, chipFace.anchorMin, chipFace.anchorMax, chipFace.pivot, chipFace.anchoredPosition, chipFace.sizeDelta);
        float radius = chipFace.sizeDelta.y / 2f;
        EnsurePillLayer(face, RingName, 0, ThemeRole.Border, radius, Vector2.zero, ref modified);
        EnsurePillLayer(face, FillName, 1, ThemeRole.Hairline, radius - PillRingWidth, Vector2.one * (-2f * PillRingWidth), ref modified);

        RectTransform content = EnsureChild(face, ContentName, ref modified, typeof(HorizontalLayoutGroup));
        modified |= SetIndex(content, 2);
        modified |= SetRect(content, Vector2.zero, Vector2.one, Centre, Vector2.zero, Vector2.zero);
        modified |= SetRowLayout(content.GetComponent<HorizontalLayoutGroup>(), 0, 0, PillSpacing, TextAnchor.MiddleCenter);

        RectTransform lockRect = EnsureChild(content, RowLockName, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement),
            typeof(ThemedColor));
        modified |= SetIndex(lockRect, 0);
        var lockImage = lockRect.GetComponent<Image>();
        modified |= SetGlyph(lockImage, LoadSprite(LockIconPath), ThemeRole.InkSecondary);
        modified |= BindTheme(lockRect.GetComponent<ThemedColor>(), ThemeRole.InkSecondary, lockImage);
        modified |= SetLayout(lockRect.GetComponent<LayoutElement>(), PillLockSize, PillLockSize);

        RectTransform wordRect = EnsureChild(content, PillWordName, ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ThemedColor));
        modified |= SetIndex(wordRect, 1);
        var word = wordRect.GetComponent<TextMeshProUGUI>();
        modified |= SetPillWord(word, chipWord);
        modified |= BindTheme(wordRect.GetComponent<ThemedColor>(), ThemeRole.InkSecondary, word);
        return (pill.GetComponent<Button>(), lockImage);
    }

    // One layer of the pill's face: a rounded, theme-tinted fill stretched over the face (inset by sizeDelta).
    private static void EnsurePillLayer(RectTransform face, string name, int index, ThemeRole role, float radius, Vector2 inset,
                                        ref bool modified)
    {
        RectTransform layer = EnsureChild(face, name, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(ImageWithRoundedCorners),
            typeof(ThemedColor));
        modified |= SetIndex(layer, index);
        var image = layer.GetComponent<Image>();
        modified |= SetImage(image, null, role, raycast: false);
        modified |= EnsureRounded(image, radius);
        modified |= BindTheme(layer.GetComponent<ThemedColor>(), role, image);
        modified |= SetRect(layer, Vector2.zero, Vector2.one, Centre, Vector2.zero, inset);
    }

    // The pill the header already holds, else the one beside the chip by its name, else a new one — built HIDDEN from its
    // first moment, so nothing under it (Nobi's material, TMP) wakes while it is authored.
    private static RectTransform FindOrCreatePill(Transform zone, Button wired, ref bool modified)
    {
        var pill = wired != null && wired.transform.parent == zone ? (RectTransform)wired.transform : zone.Find(PersonalPillName) as RectTransform;
        if (pill == null)
        {
            var go = new GameObject(PersonalPillName, typeof(RectTransform)) { layer = zone.gameObject.layer };
            go.SetActive(false);
            pill = (RectTransform)go.transform;
            pill.SetParent(zone, false);
            modified = true;
        }
        AddMissing(pill, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(Button));
        return pill;
    }

    // A transparent button over the avatar and the name: from the avatar's left edge to the name column's right edge, the
    // zone's height and <see cref="NameTapBleed"/> above and below it — the back button, left of the avatar, stays clear of
    // it. The zone's layout group ignores it (the avatar and the name never move) and it is the zone's last child, so it wins
    // the tap over both.
    private static Button EnsureNameTap(MessageHeaderView header, Button wired, ref bool modified)
    {
        (RectTransform avatar, RectTransform text) = NameColumnsOf(header);
        var zone = (RectTransform)avatar.parent;
        var tap = wired != null && wired.transform.parent == zone ? (RectTransform)wired.transform : zone.Find(NameTapName) as RectTransform;
        if (tap == null)
        {
            var go = new GameObject(NameTapName, typeof(RectTransform)) { layer = zone.gameObject.layer };
            tap = (RectTransform)go.transform;
            tap.SetParent(zone, false);
            modified = true;
        }
        AddMissing(tap, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        modified |= SetIndex(tap, zone.childCount - 1);
        modified |= SetIgnoreLayout(tap.GetComponent<LayoutElement>());
        float left = HorizontalEdges(avatar, zone).left;
        float right = HorizontalEdges(text, zone).right;
        modified |= SetRect(tap, Vector2.zero, Vector2.up, LeftMiddle, new Vector2(left, 0f), new Vector2(right - left, 2f * NameTapBleed));
        var hit = tap.GetComponent<Image>();
        modified |= SetHitBox(hit);
        modified |= SetTapButton(tap.GetComponent<Button>(), hit);
        modified |= SetActive(tap.gameObject, true);
        return tap.GetComponent<Button>();
    }

    // The avatar's column and the name's column (MessageHeaderView.avatarImage / .nameText sit inside them), one zone's two.
    private static (RectTransform avatar, RectTransform text) NameColumnsOf(MessageHeaderView header)
    {
        var avatar = header.avatarImage != null ? header.avatarImage.transform.parent as RectTransform : null;
        var text = header.nameText != null ? header.nameText.transform.parent as RectTransform : null;
        if (avatar == null || text == null || avatar.parent == null || avatar.parent != text.parent)
            throw new InvalidOperationException($"{Tag} {header.name}: MessageHeaderView.avatarImage and .nameText are not two columns of one zone.");
        return (avatar, text);
    }

    // A child's horizontal edges, from its parent rect's left edge, read off its anchors, pivot, position and size — no layout
    // pass needed (a headless build runs none).
    private static (float left, float right) HorizontalEdges(RectTransform child, RectTransform parent)
    {
        float parentWidth = parent.rect.width;
        float anchorLeft = child.anchorMin.x * parentWidth;
        float anchorRight = child.anchorMax.x * parentWidth;
        float width = anchorRight - anchorLeft + child.sizeDelta.x;
        float left = anchorLeft + (anchorRight - anchorLeft) * child.pivot.x + child.anchoredPosition.x - child.pivot.x * width;
        return (left, left + width);
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

    // The pill's word: «Личный» in the «Авто» chip's word font, size, style and spacing, so the two read as one control.
    private static bool SetPillWord(TextMeshProUGUI word, TextMeshProUGUI chipWord)
    {
        Color ink = Theme.Light.Resolve(ThemeRole.InkSecondary);
        if (word.text == PersonalChatsCopy.Pill && word.font == chipWord.font && Mathf.Approximately(word.fontSize, chipWord.fontSize)
            && word.fontStyle == chipWord.fontStyle && Mathf.Approximately(word.characterSpacing, chipWord.characterSpacing)
            && word.alignment == TextAlignmentOptions.Center && word.textWrappingMode == TextWrappingModes.NoWrap
            && word.overflowMode == TextOverflowModes.Overflow && !word.enableAutoSizing && !word.raycastTarget && word.color == ink)
            return false;
        word.text = PersonalChatsCopy.Pill;
        word.font = chipWord.font;
        word.fontSize = chipWord.fontSize;
        word.fontStyle = chipWord.fontStyle;
        word.characterSpacing = chipWord.characterSpacing;
        word.alignment = TextAlignmentOptions.Center;
        word.textWrappingMode = TextWrappingModes.NoWrap;
        word.overflowMode = TextOverflowModes.Overflow;
        word.enableAutoSizing = false;
        word.raycastTarget = false;   // only the pill's box takes the tap
        word.color = ink;             // opaque — the ThemedColor binding keeps the authored alpha
        return true;
    }

    // A tap target that draws nothing: the «Авто» chip's own transparent box.
    private static bool SetHitBox(Image image)
    {
        var clear = new Color(0f, 0f, 0f, 0f);
        if (image.sprite == null && image.type == Image.Type.Simple && image.raycastTarget && image.maskable && image.color == clear)
            return false;
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.raycastTarget = true;
        image.maskable = true;
        image.color = clear;
        return true;
    }

    // No tint on press — the header punches the pill in code, as the chip does; the name's tap shows nothing at all.
    private static bool SetTapButton(Button button, Graphic target)
    {
        if (button.transition == Selectable.Transition.None && button.targetGraphic == target) return false;
        button.transition = Selectable.Transition.None;
        button.targetGraphic = target;
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
