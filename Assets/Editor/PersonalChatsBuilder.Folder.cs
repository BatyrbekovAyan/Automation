#if UNITY_EDITOR
using System;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static BuilderSceneUtil;
using Object = UnityEngine.Object;

/// <summary>
/// The «Личные» folder's parts (spec §5.3, §5.4; sketch 009 screens 1, 3, 4) — see the class summary in PersonalChatsBuilder.cs.
///
///   ChatItem (SwipeContent):        [SelectCheck(Ring, Hole, Checked(Fill, Tick))] [Avatar] [TextBlock …]   (SelectCheck hidden)
///   Screen_Messanger:               ChatsPanel · PersonalChatsPanel · DeleteChatConfirmPanel · MessagesPanel · …
///   PersonalChatsPanel (VLG, the open chat's full screen, hidden):
///     Header (284u, the open chat's bar)   [Back: Icon ‹ | Label «Отмена»] [Title] [Edit: Label «Изменить»]  + Line
///     StatusLine     [Icon ✓] [Text «WhatsApp подключён»]                                   (onboarding)
///     Body/Card      [Icon lock] [Text — the hint / what the screen is for]                  (browse, onboarding)
///     Search         the chat list's own search bar, cloned                                  (edit, onboarding)
///     SectionHeader  «НЕДАВНИЕ ЧАТЫ»                                                         (onboarding)
///     LoadingLabel   «Загружаем чаты из WhatsApp…»                                           (onboarding, empty list)
///     Scroll/Viewport(RectMask2D)/Content   the rows (ChatItem.prefab, made at runtime)
///     NoContactsNote/Text                                                                    (onboarding)
///     BottomBar      [Primary «Сделать рабочими · N» / «Готово · клиентов: N»] [Secondary «Отмечу позже»]
///     Footer/Text    «Свайп по чату — сделать его рабочим»                                   (browse)
///     SwipeBack      the left-edge strip: SwipeToBackPanel + ClickPassthrough, ignored by the layout (browse)
/// What each mode shows is PersonalChatsFolderView's (it toggles the parts); the authored state is the folder itself.
/// Fonts come from the chat row (its name's Semibold, its preview's Regular) — never Medium or Bold.
/// </summary>
public static partial class PersonalChatsBuilder
{
    public const string FolderPanelName = "PersonalChatsPanel";
    public const string SelectCheckName = "SelectCheck";

    /// <summary>The checkbox — the sketch's 20dp box.</summary>
    public const float SelectCheckSize = 60f;

    /// <summary>The header's two side buttons — the chevron or «Отмена», and «Изменить» — each this wide; the title sits between.</summary>
    public const float HeaderSideWidth = 240f;

    private const string SearchName = "Search";
    private const string ChevronLeftPath = "Assets/Images/Chat/chevron-left.png";
    private const string TickPath = "Assets/Images/Icons/Tick.png";

    private const float FolderHeaderHeight = 284f;   // the open chat's bar: the status strip baked in, its row at 158..258
    private const float NavRowY = 76f;               // that row's centre above the header's bottom edge
    private const float NavHitHeight = 132f;         // the house touch floor
    private const float Gutter = 40f;                // a chat row's own side padding
    private const float BackGlyph = 60f;
    private const float TitleSize = 48f, TitleHeight = 80f, NavWordSize = 42f;
    private const float BodySize = 36f, NoteSize = 32f, OverlineSize = 32f, OverlineSpacing = 5f;
    private const float StatusGlyph = 36f, CardGlyph = 44f, CardRadius = 36f;
    private const float LoadingHeight = 200f;
    private const float PrimaryHeight = 132f, PrimarySize = 42f, SecondaryHeight = 108f, SecondarySize = 38f;
    private const float StripWidth = 150f;           // ProfileSubPagesBuilder's swipe-back strip
    private const float CheckRadius = 18f, CheckRing = 4f, TickInset = 12f;

    // ---- entry points ----

    /// <summary>The checkbox in a chat row (see the class summary); true when something was changed.</summary>
    public static bool EnsureChatItemSelectCheck(ChatItemView item)
    {
        bool modified = false;
        EnsureChatItemSelectCheck(item, ref modified);
        return modified;
    }

    /// <summary>
    /// The «Личные» folder beside <paramref name="list"/> — right after it, before <paramref name="messagesPanel"/> (the open
    /// chat) — and the list's delete card moved over it (see the class summary); true when something was changed.
    /// </summary>
    public static bool EnsureFolderPanel(ChatListView list, Transform messagesPanel)
    {
        bool modified = false;
        EnsureFolderPanel(list, messagesPanel, ref modified);
        return modified;
    }

    // ---- ChatItem: the checkbox ----

    // Right before the avatar in the row's layout, hidden — the folder's selection modes show it; its marked face hidden too.
    private static void EnsureChatItemSelectCheck(ChatItemView item, ref bool modified)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        Transform avatar = item.avatarImage != null ? item.avatarImage.transform.parent : null;
        if (avatar == null || avatar.parent == null)
            throw new InvalidOperationException($"{Tag} {item.name}: ChatItemView.avatarImage is not wired into the row's layout.");
        var refs = new SerializedObject(item);
        RectTransform check = FindOrCreateHidden(avatar.parent, SelectCheckName, refs.FindProperty("selectCheck")?.objectReferenceValue as GameObject,
            ref modified);
        AddMissing(check, ref modified, typeof(LayoutElement));
        modified |= PlaceBefore(check, avatar);
        modified |= SetLayout(check.GetComponent<LayoutElement>(), SelectCheckSize, SelectCheckSize);
        modified |= SetHeight(check, SelectCheckSize);   // the row's layout sets the width, never the height
        modified |= SetActive(check.gameObject, false);
        EnsurePillLayer(check, "Ring", 0, ThemeRole.InputBorder, CheckRadius, Vector2.zero, ref modified);
        EnsurePillLayer(check, "Hole", 1, ThemeRole.Surface, CheckRadius - CheckRing, Vector2.one * (-2f * CheckRing), ref modified);
        RectTransform marked = EnsureCheckMark(check, ref modified);
        modified |= SetRef(refs, "selectCheck", check.gameObject, Tag);
        modified |= SetRef(refs, "selectChecked", marked.gameObject, Tag);
    }

    // The marked face: the accent fill and the white tick over the ring.
    private static RectTransform EnsureCheckMark(RectTransform check, ref bool modified)
    {
        RectTransform marked = EnsureChild(check, "Checked", ref modified);
        modified |= SetIndex(marked, 2);
        modified |= SetRect(marked, Vector2.zero, Vector2.one, Centre, Vector2.zero, Vector2.zero);
        modified |= SetActive(marked.gameObject, false);
        EnsurePillLayer(marked, "Fill", 0, ThemeRole.AccentFill, CheckRadius, Vector2.zero, ref modified);
        RectTransform tick = EnsureChild(marked, "Tick", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(ThemedColor));
        modified |= SetIndex(tick, 1);
        var glyph = tick.GetComponent<Image>();
        modified |= SetGlyph(glyph, LoadSprite(TickPath), ThemeRole.AccentOnFill);
        modified |= BindTheme(tick.GetComponent<ThemedColor>(), ThemeRole.AccentOnFill, glyph);
        modified |= SetRect(tick, Vector2.zero, Vector2.one, Centre, Vector2.zero, Vector2.one * (-2f * TickInset));
        return marked;
    }

    // ---- Main.unity: the folder ----

    private static void EnsureFolderPanel(ChatListView list, Transform messagesPanel, ref bool modified)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        if (list.prefab == null) throw new InvalidOperationException($"{Tag} ChatListView.prefab is not wired.");
        if (list.content == null) throw new InvalidOperationException($"{Tag} ChatListView.content is not wired.");
        Transform screen = list.transform.parent;
        if (screen == null || messagesPanel == null || messagesPanel.parent != screen)
            throw new InvalidOperationException($"{Tag} The chat list and the open chat are not two panels of one screen.");
        var fonts = new FolderFonts(list.prefab);

        RectTransform panel = FindOrCreateFolderPanel(screen, ref modified);
        modified |= PlaceAfter(panel, list.transform);
        modified |= SetRect(panel, ((RectTransform)messagesPanel).anchorMin, ((RectTransform)messagesPanel).anchorMax,
            ((RectTransform)messagesPanel).pivot, ((RectTransform)messagesPanel).anchoredPosition, ((RectTransform)messagesPanel).sizeDelta);
        modified |= SetActive(panel.gameObject, false);   // PersonalChatsFolderView shows itself
        EnsurePageSurface(panel, ref modified);

        var refs = new SerializedObject(panel.GetComponent<PersonalChatsFolderView>());
        EnsureFolderHeader(panel, fonts, refs, ref modified);
        EnsureStatusLine(panel, fonts, refs, ref modified);
        EnsureBodyCard(panel, fonts, refs, ref modified);
        EnsureFolderSearch(panel, list, refs, ref modified);
        EnsureSectionHeader(panel, fonts, refs, ref modified);
        EnsureLoadingLabel(panel, fonts, refs, ref modified);
        ScrollRect scroll = EnsureFolderRows(panel, list, refs, ref modified);
        EnsureNoContactsNote(panel, fonts, refs, ref modified);
        EnsureBottomBar(panel, fonts, list.prefab.button.colors, refs, ref modified);
        EnsureFooter(panel, fonts, refs, ref modified);
        EnsureFolderSwipeBack(panel, scroll, refs, ref modified);
        modified |= SetRef(refs, "chatList", list, Tag);
        modified |= SetRef(refs, "rowPrefab", list.prefab, Tag);

        EnsureDeleteCardOverFolder(list, panel, ref modified);
    }

    // The panel the screen holds by its name, else a new one — built HIDDEN from its first moment, so nothing under it wakes
    // while it is authored.
    private static RectTransform FindOrCreateFolderPanel(Transform screen, ref bool modified)
    {
        RectTransform panel = FindOrCreateHidden(screen, FolderPanelName, null, ref modified);
        AddMissing(panel, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup), typeof(ThemedColor),
            typeof(PersonalChatsFolderView));
        return panel;
    }

    // The page: the chat list's ground (Surface), taking every tap so nothing reaches the list below; its parts stacked.
    private static void EnsurePageSurface(RectTransform panel, ref bool modified)
    {
        var ground = panel.GetComponent<Image>();
        modified |= SetImage(ground, null, ThemeRole.Surface, raycast: true);
        modified |= BindTheme(panel.GetComponent<ThemedColor>(), ThemeRole.Surface, ground);
        modified |= SetGroup(panel.GetComponent<VerticalLayoutGroup>(), new RectOffset(0, 0, 0, 0), 0f, TextAnchor.UpperCenter, expandWidth: true);
    }

    // ---- the header: [‹ / «Отмена»] [title] [«Изменить»] on the open chat's bar ----

    private static void EnsureFolderHeader(RectTransform panel, FolderFonts fonts, SerializedObject refs, ref bool modified)
    {
        RectTransform header = EnsureChild(panel, "Header", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(ThemedColor));
        modified |= SetIndex(header, 0);
        var ground = header.GetComponent<Image>();
        modified |= SetImage(ground, null, ThemeRole.Surface, raycast: false);
        modified |= BindTheme(header.GetComponent<ThemedColor>(), ThemeRole.Surface, ground);
        modified |= SetLayout(header.GetComponent<LayoutElement>(), -1f, FolderHeaderHeight, -1f, FolderHeaderHeight);
        EnsureHeaderLine(header, ref modified);
        EnsureHeaderBack(header, fonts, refs, ref modified);

        RectTransform titleRect = EnsureChild(header, "Title", ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ThemedColor));
        modified |= SetIndex(titleRect, 2);
        var title = titleRect.GetComponent<TextMeshProUGUI>();
        modified |= SetLabel(title, PersonalChatsCopy.FolderTitle, fonts.Semibold, TitleSize, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis,
            ThemeRole.InkPrimary);
        modified |= BindTheme(titleRect.GetComponent<ThemedColor>(), ThemeRole.InkPrimary, title);
        modified |= SetRect(titleRect, Vector2.zero, Vector2.right, Centre, new Vector2(0f, NavRowY), new Vector2(-2f * HeaderSideWidth, TitleHeight));

        Button edit = EnsureNavButton(header, "Edit", 3, right: true, ref modified);
        EnsureNavWord(edit.transform, PersonalChatsCopy.Edit, fonts.Regular, right: true, ref modified);
        modified |= SetRef(refs, "title", title, Tag);
        modified |= SetRef(refs, "editButton", edit, Tag);
    }

    // The left button: the chevron (the folder itself) or «Отмена» («Изменить») — both PersonalChatsFolderView.TryBack.
    private static void EnsureHeaderBack(RectTransform header, FolderFonts fonts, SerializedObject refs, ref bool modified)
    {
        Button back = EnsureNavButton(header, "Back", 1, right: false, ref modified);
        RectTransform chevron = EnsureChild(back.transform, "Icon", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(ThemedColor));
        var glyph = chevron.GetComponent<Image>();
        modified |= SetGlyph(glyph, LoadSprite(ChevronLeftPath), ThemeRole.AccentText);
        modified |= BindTheme(chevron.GetComponent<ThemedColor>(), ThemeRole.AccentText, glyph);
        modified |= SetRect(chevron, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(Gutter, 0f), Vector2.one * BackGlyph);
        TextMeshProUGUI cancel = EnsureNavWord(back.transform, PersonalChatsCopy.Cancel, fonts.Regular, right: false, ref modified);
        modified |= SetActive(cancel.gameObject, false);   // «Изменить» shows it
        modified |= SetRef(refs, "backButton", back, Tag);
        modified |= SetRef(refs, "backChevron", chevron.gameObject, Tag);
        modified |= SetRef(refs, "backLabel", cancel.gameObject, Tag);
    }

    // A transparent tap box on the header's row, at its left or right edge: HeaderSideWidth wide, the touch floor tall.
    private static Button EnsureNavButton(RectTransform header, string name, int index, bool right, ref bool modified)
    {
        RectTransform box = EnsureChild(header, name, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(Button));
        modified |= SetIndex(box, index);
        Vector2 anchor = right ? Vector2.right : Vector2.zero;
        modified |= SetRect(box, anchor, anchor, new Vector2(right ? 1f : 0f, 0.5f), new Vector2(0f, NavRowY), new Vector2(HeaderSideWidth, NavHitHeight));
        var hit = box.GetComponent<Image>();
        modified |= SetHitBox(hit);
        var button = box.GetComponent<Button>();
        modified |= SetTapButton(button, hit);
        return button;
    }

    // A nav button's word — «Отмена», «Изменить» — in the accent, the gutter from the screen's edge.
    private static TextMeshProUGUI EnsureNavWord(Transform box, string text, TMP_FontAsset font, bool right, ref bool modified)
    {
        RectTransform rect = EnsureChild(box, "Label", ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ThemedColor));
        var word = rect.GetComponent<TextMeshProUGUI>();
        modified |= SetLabel(word, text, font, NavWordSize, right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left,
            TextOverflowModes.Overflow, ThemeRole.AccentText);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), ThemeRole.AccentText, word);
        modified |= SetRect(rect, Vector2.zero, Vector2.one, Centre, new Vector2((right ? -Gutter : Gutter) / 2f, 0f), new Vector2(-Gutter, 0f));
        return word;
    }

    // The bar's bottom hairline. Its thickness belongs to PixelSnapLine (it snaps it to whole pixels at runtime), so it is
    // written once, when the line is made.
    private static void EnsureHeaderLine(RectTransform header, ref bool modified)
    {
        bool fresh = header.Find("Line") == null;
        RectTransform line = EnsureChild(header, "Line", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(PixelSnapLine), typeof(ThemedColor));
        modified |= SetIndex(line, 0);
        var image = line.GetComponent<Image>();
        modified |= SetImage(image, null, ThemeRole.Hairline, raycast: false);
        modified |= BindTheme(line.GetComponent<ThemedColor>(), ThemeRole.Hairline, image);
        modified |= SetSnap(line.GetComponent<PixelSnapLine>());
        float thickness = fresh ? DividerThickness : line.sizeDelta.y;
        modified |= SetRect(line, Vector2.zero, Vector2.right, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, thickness));
    }

    // ---- above the rows ----

    // «✓ WhatsApp подключён» — the onboarding's first line, in the positive ink. Hidden: the onboarding shows it.
    private static void EnsureStatusLine(RectTransform panel, FolderFonts fonts, SerializedObject refs, ref bool modified)
    {
        RectTransform line = EnsureChild(panel, "StatusLine", ref modified, typeof(HorizontalLayoutGroup));
        modified |= SetIndex(line, 1);
        modified |= SetGroup(line.GetComponent<HorizontalLayoutGroup>(), new RectOffset(40, 40, 28, 0), 16f, TextAnchor.MiddleLeft, expandWidth: false);
        EnsureLayoutGlyph(line, "Icon", 0, TickPath, ThemeRole.PositiveInk, StatusGlyph, ref modified);
        TextMeshProUGUI text = EnsureFlexibleText(line, "Text", 1, PersonalChatsCopy.OnboardingConnected(false), fonts.Regular, BodySize,
            ThemeRole.PositiveInk, wrap: false, ref modified);
        modified |= SetActive(line.gameObject, false);
        modified |= SetRef(refs, "statusLine", line.gameObject, Tag);
        modified |= SetRef(refs, "statusText", text, Tag);
    }

    // The explanation in a recessed card — the lock and the folder's hint (or, in the onboarding, what the screen is for).
    private static void EnsureBodyCard(RectTransform panel, FolderFonts fonts, SerializedObject refs, ref bool modified)
    {
        RectTransform body = EnsureChild(panel, "Body", ref modified, typeof(VerticalLayoutGroup));
        modified |= SetIndex(body, 2);
        modified |= SetGroup(body.GetComponent<VerticalLayoutGroup>(), new RectOffset(40, 40, 24, 24), 0f, TextAnchor.UpperCenter, expandWidth: true);
        RectTransform card = EnsureChild(body, "Card", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(ImageWithRoundedCorners),
            typeof(HorizontalLayoutGroup), typeof(ThemedColor));
        var fill = card.GetComponent<Image>();
        modified |= SetImage(fill, null, ThemeRole.Background, raycast: false);
        modified |= EnsureRounded(fill, CardRadius);
        modified |= BindTheme(card.GetComponent<ThemedColor>(), ThemeRole.Background, fill);
        modified |= SetGroup(card.GetComponent<HorizontalLayoutGroup>(), new RectOffset(36, 36, 32, 32), 24f, TextAnchor.UpperLeft, expandWidth: false);
        EnsureLayoutGlyph(card, "Icon", 0, LockIconPath, ThemeRole.InkSecondary, CardGlyph, ref modified);
        TextMeshProUGUI text = EnsureFlexibleText(card, "Text", 1, PersonalChatsCopy.FolderHint, fonts.Regular, BodySize, ThemeRole.InkSecondary,
            wrap: true, ref modified);
        modified |= SetRef(refs, "body", body.gameObject, Tag);
        modified |= SetRef(refs, "bodyText", text, Tag);
    }

    // The chat list's own search bar, cloned once — its pill, its clear button, its input of the app's single-focus field type
    // (DeferredDismissInputField: iOS shares one native keyboard buffer) — then kept to the house keyboard contract. Hidden:
    // «Изменить» and the onboarding show it.
    private static void EnsureFolderSearch(RectTransform panel, ChatListView list, SerializedObject refs, ref bool modified)
    {
        var search = panel.Find(SearchName) as RectTransform;
        if (search == null)
        {
            search = (RectTransform)Object.Instantiate(SearchBarOf(list.content).gameObject, panel, false).transform;
            search.name = SearchName;
            modified = true;
        }
        modified |= SetIndex(search, 3);
        var bar = search.GetComponent<ChatSearchBar>();
        if (bar == null) throw new InvalidOperationException($"{Tag} The cloned search bar has no ChatSearchBar.");
        TMP_InputField input = InputOf(bar);
        if (!(input is DeferredDismissInputField))
            throw new InvalidOperationException($"{Tag} The chat list's search input is not a DeferredDismissInputField — its clone would break the single-focus invariant.");
        if (!(input.placeholder is TextMeshProUGUI placeholder))
            throw new InvalidOperationException($"{Tag} The cloned search bar's input has no TMP placeholder.");
        modified |= SetKeyboardContract(input);
        modified |= SetWord(placeholder, PersonalChatsCopy.EditSearch);
        modified |= SetActive(search.gameObject, false);
        modified |= SetRef(refs, "search", bar, Tag);
        modified |= SetRef(refs, "searchPlaceholder", placeholder, Tag);
    }

    // «НЕДАВНИЕ ЧАТЫ» — the onboarding's overline, letter-spaced like the sketch's. Hidden: the onboarding shows it.
    private static void EnsureSectionHeader(RectTransform panel, FolderFonts fonts, SerializedObject refs, ref bool modified)
    {
        RectTransform section = EnsureChild(panel, "SectionHeader", ref modified, typeof(HorizontalLayoutGroup));
        modified |= SetIndex(section, 4);
        modified |= SetGroup(section.GetComponent<HorizontalLayoutGroup>(), new RectOffset(40, 40, 36, 12), 0f, TextAnchor.LowerLeft, expandWidth: false);
        TextMeshProUGUI label = EnsureFlexibleText(section, "Label", 0, PersonalChatsCopy.OnboardingSection, fonts.Regular, OverlineSize,
            ThemeRole.InkTertiary, wrap: false, ref modified);
        modified |= SetSpacing(label, OverlineSpacing);
        modified |= SetActive(section.gameObject, false);
        modified |= SetRef(refs, "sectionHeader", section.gameObject, Tag);
    }

    // «Загружаем чаты из WhatsApp…» where the rows will be. Hidden: the onboarding shows it while the chat list is empty.
    private static void EnsureLoadingLabel(RectTransform panel, FolderFonts fonts, SerializedObject refs, ref bool modified)
    {
        RectTransform rect = EnsureChild(panel, "LoadingLabel", ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement),
            typeof(ThemedColor));
        modified |= SetIndex(rect, 5);
        var label = rect.GetComponent<TextMeshProUGUI>();
        modified |= SetLabel(label, PersonalChatsCopy.OnboardingLoading(false), fonts.Regular, BodySize, TextAlignmentOptions.Center,
            TextOverflowModes.Ellipsis, ThemeRole.InkSecondary);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), ThemeRole.InkSecondary, label);
        modified |= SetLayout(rect.GetComponent<LayoutElement>(), -1f, LoadingHeight, -1f, LoadingHeight);
        modified |= SetActive(rect.gameObject, false);
        modified |= SetRef(refs, "loadingLabel", label, Tag);
    }

    // ---- the rows ----

    // A scroll that takes what the page leaves, with the chat list's own scroll feel; a clipping viewport (a RectMask2D — no
    // stencil, so the rows' rounded parts never lag a resize); a stacked content the rows are made into at runtime.
    private static ScrollRect EnsureFolderRows(RectTransform panel, ChatListView list, SerializedObject refs, ref bool modified)
    {
        RectTransform box = EnsureChild(panel, "Scroll", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        AddMissing(box, ref modified, typeof(SnappyFlickScrollRect), typeof(ScrollClickBlocker));
        modified |= SetIndex(box, 6);
        modified |= SetHitBox(box.GetComponent<Image>());   // a drag anywhere in the box scrolls
        modified |= SetFill(box.GetComponent<LayoutElement>());
        bool fresh = box.Find("Viewport") == null;
        RectTransform viewport = EnsureChild(box, "Viewport", ref modified, typeof(RectMask2D));
        modified |= SetRect(viewport, Vector2.zero, Vector2.one, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        RectTransform content = EnsureChild(viewport, "Content", ref modified, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        // Its height is the fitter's and its position the scroll's: written in full once, the anchors kept after.
        if (fresh) modified |= SetRect(content, Vector2.up, Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        else modified |= SetAnchors(content, Vector2.up, Vector2.one, new Vector2(0.5f, 1f));
        modified |= SetGroup(content.GetComponent<VerticalLayoutGroup>(), new RectOffset(0, 0, 0, 0), 0f, TextAnchor.UpperCenter, expandWidth: true);
        modified |= SetFitter(content.GetComponent<ContentSizeFitter>());
        var scroll = box.GetComponent<ScrollRect>();
        modified |= SetScroll(scroll, viewport, content, list.content.GetComponentInParent<ScrollRect>(true));
        modified |= SetRef(refs, "scroll", scroll, Tag);
        modified |= SetRef(refs, "rowsContent", content, Tag);
        return scroll;
    }

    // ---- below the rows ----

    // The onboarding's soft note: the phone's messenger, it seems, does not see the address book. Hidden.
    private static void EnsureNoContactsNote(RectTransform panel, FolderFonts fonts, SerializedObject refs, ref bool modified)
    {
        RectTransform note = EnsureChild(panel, "NoContactsNote", ref modified, typeof(VerticalLayoutGroup));
        modified |= SetIndex(note, 7);
        modified |= SetGroup(note.GetComponent<VerticalLayoutGroup>(), new RectOffset(40, 40, 16, 16), 0f, TextAnchor.UpperCenter, expandWidth: true);
        TextMeshProUGUI text = EnsureParagraph(note, PersonalChatsCopy.OnboardingNoContacts(false), fonts.Regular, NoteSize, ThemeRole.InkTertiary,
            ref modified);
        modified |= SetActive(note.gameObject, false);
        modified |= SetRef(refs, "noContactsNote", note.gameObject, Tag);
        modified |= SetRef(refs, "noContactsText", text, Tag);
    }

    // The thumb-zone actions — the primary pill and, in the onboarding, «Отмечу позже» — over the home bar. Hidden: the
    // selection modes show it.
    private static void EnsureBottomBar(RectTransform panel, FolderFonts fonts, ColorBlock press, SerializedObject refs, ref bool modified)
    {
        RectTransform bar = EnsureChild(panel, "BottomBar", ref modified, typeof(VerticalLayoutGroup));
        modified |= SetIndex(bar, 8);
        modified |= SetGroup(bar.GetComponent<VerticalLayoutGroup>(), new RectOffset(40, 40, 24, 24 + HomeBar()), 8f, TextAnchor.UpperCenter,
            expandWidth: true);

        RectTransform primary = EnsureChild(bar, "Primary", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(ImageWithRoundedCorners),
            typeof(Button), typeof(LayoutElement), typeof(ThemedColor));
        modified |= SetIndex(primary, 0);
        var fill = primary.GetComponent<Image>();
        modified |= SetImage(fill, null, ThemeRole.AccentFill, raycast: true);
        modified |= EnsureRounded(fill, PrimaryHeight / 2f);
        modified |= BindTheme(primary.GetComponent<ThemedColor>(), ThemeRole.AccentFill, fill);
        modified |= SetButton(primary.GetComponent<Button>(), fill, press);
        modified |= SetLayout(primary.GetComponent<LayoutElement>(), -1f, PrimaryHeight, -1f, PrimaryHeight);
        TextMeshProUGUI word = EnsureButtonWord(primary, PersonalChatsCopy.MakeBusinessMany(0), fonts.Semibold, PrimarySize, ThemeRole.AccentOnFill,
            ref modified);

        RectTransform secondary = EnsureChild(bar, "Secondary", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        modified |= SetIndex(secondary, 1);
        var hit = secondary.GetComponent<Image>();
        modified |= SetHitBox(hit);
        modified |= SetTapButton(secondary.GetComponent<Button>(), hit);
        modified |= SetLayout(secondary.GetComponent<LayoutElement>(), -1f, SecondaryHeight, -1f, SecondaryHeight);
        EnsureButtonWord(secondary, PersonalChatsCopy.OnboardingLater, fonts.Regular, SecondarySize, ThemeRole.AccentText, ref modified);

        modified |= SetActive(bar.gameObject, false);
        modified |= SetRef(refs, "bottomBar", bar.gameObject, Tag);
        modified |= SetRef(refs, "primaryButton", primary.GetComponent<Button>(), Tag);
        modified |= SetRef(refs, "primaryLabel", word, Tag);
        modified |= SetRef(refs, "secondaryButton", secondary.GetComponent<Button>(), Tag);
    }

    // «Свайп по чату — сделать его рабочим» at the bottom, over the home bar — the folder itself shows it.
    private static void EnsureFooter(RectTransform panel, FolderFonts fonts, SerializedObject refs, ref bool modified)
    {
        RectTransform footer = EnsureChild(panel, "Footer", ref modified, typeof(VerticalLayoutGroup));
        modified |= SetIndex(footer, 9);
        modified |= SetGroup(footer.GetComponent<VerticalLayoutGroup>(), new RectOffset(40, 40, 20, 20 + HomeBar()), 0f, TextAnchor.UpperCenter,
            expandWidth: true);
        EnsureParagraph(footer, PersonalChatsCopy.FolderFooter, fonts.Regular, NoteSize, ThemeRole.InkTertiary, ref modified);
        modified |= SetRef(refs, "footer", footer.gameObject, Tag);
    }

    // The left-edge swipe back (ProfileSubPagesBuilder's strip): it slides the folder, hands a vertical drag to the rows, and
    // lets a tap through to what is under it — inside the folder only. The last child, so it wins the edge; the layout
    // ignores it. PersonalChatsFolderView takes its commit (OnCommitted is not serialized).
    private static void EnsureFolderSwipeBack(RectTransform panel, ScrollRect scroll, SerializedObject refs, ref bool modified)
    {
        RectTransform strip = EnsureChild(panel, "SwipeBack", ref modified, typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement),
            typeof(SwipeToBackPanel), typeof(ClickPassthrough));
        modified |= SetIndex(strip, panel.childCount - 1);
        modified |= SetIgnoreLayout(strip.GetComponent<LayoutElement>());
        modified |= SetRect(strip, Vector2.zero, Vector2.up, Centre, new Vector2(StripWidth / 2f, 0f), new Vector2(StripWidth, 0f));
        modified |= SetHitBox(strip.GetComponent<Image>());
        var swipe = strip.GetComponent<SwipeToBackPanel>();
        var swipeRefs = new SerializedObject(swipe);
        modified |= SetRef(swipeRefs, "panelToSlide", panel, Tag);
        modified |= SetRef(swipeRefs, "contentScrollRect", scroll, Tag);
        modified |= SetPassthrough(strip.GetComponent<ClickPassthrough>(), panel);
        modified |= SetRef(refs, "swipeBack", swipe, Tag);
    }

    // The chat list's delete card, moved up beside the folder and over it: a folder row's «Удалить» asks through the chat
    // list's own RequestDelete — the card must not stand under the folder. Its backdrop covers the folder's rect — the open
    // chat's, over the tab bar — so the card is modal over the whole folder (on the chats screen's own rect the folder's
    // footer, a strip of its rows and its swipe-back strip stayed live under it), and the card itself stands where it stood.
    private static void EnsureDeleteCardOverFolder(ChatListView list, RectTransform panel, ref bool modified)
    {
        var card = new SerializedObject(list).FindProperty("deleteConfirm")?.objectReferenceValue as ChatDeleteConfirm;
        if (card == null) return;   // no card: the list deletes without asking
        var root = (RectTransform)card.transform;
        if (root.parent != panel.parent)
        {
            root.SetParent(panel.parent, false);   // the same local rect: the chats panel fills its screen
            modified = true;
        }
        modified |= PlaceAfter(root, panel);
        modified |= CoverTheFolder(root, panel);
    }

    // The backdrop takes the folder's rect once; the card face, hung from a point of the backdrop, is moved back by exactly how
    // far that point moved — so it stays put (a later hand-made move of the card is left alone: the rect already matches).
    // Only for a backdrop anchored like the folder: then the parent's size cancels out of the move.
    private static bool CoverTheFolder(RectTransform backdrop, RectTransform folder)
    {
        if (backdrop.anchorMin == folder.anchorMin && backdrop.anchorMax == folder.anchorMax && backdrop.pivot == folder.pivot
            && backdrop.anchoredPosition == folder.anchoredPosition && backdrop.sizeDelta == folder.sizeDelta)
            return false;
        if (backdrop.anchorMin != folder.anchorMin || backdrop.anchorMax != folder.anchorMax)
            throw new InvalidOperationException($"{Tag} {backdrop.name} is not anchored like {folder.name} — stretch it over the folder by hand.");
        var face = PopupUI.FindCard(backdrop) as RectTransform;
        if (face != null)
        {
            if (face.anchorMin != face.anchorMax)
                throw new InvalidOperationException($"{Tag} {backdrop.name}/{face.name} is stretched — it would resize with the backdrop.");
            Vector2 before = backdrop.anchoredPosition - Vector2.Scale(backdrop.sizeDelta, backdrop.pivot) + Vector2.Scale(face.anchorMin, backdrop.sizeDelta);
            Vector2 after = folder.anchoredPosition - Vector2.Scale(folder.sizeDelta, folder.pivot) + Vector2.Scale(face.anchorMin, folder.sizeDelta);
            face.anchoredPosition -= after - before;
        }
        SetRect(backdrop, folder.anchorMin, folder.anchorMax, folder.pivot, folder.anchoredPosition, folder.sizeDelta);
        return true;
    }

    // ---- part makers ----

    // A fixed-size glyph in a layout row: a white sprite tinted by its role.
    private static void EnsureLayoutGlyph(RectTransform parent, string name, int index, string spritePath, ThemeRole role, float size,
                                          ref bool modified)
    {
        RectTransform icon = EnsureChild(parent, name, ref modified, typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(ThemedColor));
        modified |= SetIndex(icon, index);
        var glyph = icon.GetComponent<Image>();
        modified |= SetGlyph(glyph, LoadSprite(spritePath), role);
        modified |= BindTheme(icon.GetComponent<ThemedColor>(), role, glyph);
        modified |= SetLayout(icon.GetComponent<LayoutElement>(), size, size);
    }

    // A text that takes the row's free width (it reports no width of its own, so the row never squeezes its neighbours).
    private static TextMeshProUGUI EnsureFlexibleText(RectTransform parent, string name, int index, string text, TMP_FontAsset font, float size,
                                                      ThemeRole role, bool wrap, ref bool modified)
    {
        RectTransform rect = EnsureChild(parent, name, ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement),
            typeof(ThemedColor));
        modified |= SetIndex(rect, index);
        var label = rect.GetComponent<TextMeshProUGUI>();
        modified |= wrap
            ? SetParagraph(label, text, font, size, TextAlignmentOptions.TopLeft, role)
            : SetLabel(label, text, font, size, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis, role);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), role, label);
        modified |= SetLayout(rect.GetComponent<LayoutElement>(), 0f, -1f, 0f, -1f, flexibleWidth: 1f);
        return label;
    }

    // A wrapping, centred line as the only child of a padded stack.
    private static TextMeshProUGUI EnsureParagraph(RectTransform parent, string text, TMP_FontAsset font, float size, ThemeRole role,
                                                   ref bool modified)
    {
        RectTransform rect = EnsureChild(parent, "Text", ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ThemedColor));
        var label = rect.GetComponent<TextMeshProUGUI>();
        modified |= SetParagraph(label, text, font, size, TextAlignmentOptions.Top, role);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), role, label);
        return label;
    }

    // A button's word, filling the button, centred.
    private static TextMeshProUGUI EnsureButtonWord(RectTransform button, string text, TMP_FontAsset font, float size, ThemeRole role, ref bool modified)
    {
        RectTransform rect = EnsureChild(button, "Label", ref modified, typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ThemedColor));
        var word = rect.GetComponent<TextMeshProUGUI>();
        modified |= SetLabel(word, text, font, size, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis, role);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), role, word);
        modified |= SetRect(rect, Vector2.zero, Vector2.one, Centre, Vector2.zero, Vector2.zero);
        return word;
    }

    // The child the owner already holds, else the one by its name, else a new one — built HIDDEN from its first moment.
    private static RectTransform FindOrCreateHidden(Transform parent, string name, GameObject wired, ref bool modified)
    {
        var rect = wired != null && wired.transform.parent == parent ? (RectTransform)wired.transform : parent.Find(name) as RectTransform;
        if (rect != null) return rect;
        var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
        go.SetActive(false);
        rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        modified = true;
        return rect;
    }

    private static int HomeBar() => Mathf.RoundToInt(KeyboardLiftMath.BakedHomeBarCanvasPx);

    // ---- setters: each compares first and reports whether it wrote ----

    private static bool SetGroup(HorizontalOrVerticalLayoutGroup group, RectOffset padding, float spacing, TextAnchor alignment, bool expandWidth)
    {
        RectOffset current = group.padding;
        if (current.left == padding.left && current.right == padding.right && current.top == padding.top && current.bottom == padding.bottom
            && Mathf.Approximately(group.spacing, spacing) && group.childAlignment == alignment && group.childControlWidth && group.childControlHeight
            && group.childForceExpandWidth == expandWidth && !group.childForceExpandHeight && !group.childScaleWidth && !group.childScaleHeight
            && !group.reverseArrangement)
            return false;
        group.padding = padding;
        group.spacing = spacing;
        group.childAlignment = alignment;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = expandWidth;
        group.childForceExpandHeight = false;
        group.childScaleWidth = group.childScaleHeight = false;
        group.reverseArrangement = false;
        return true;
    }

    // A wrapping text — SetLabel's twin for paragraphs.
    private static bool SetParagraph(TextMeshProUGUI label, string text, TMP_FontAsset font, float size, TextAlignmentOptions alignment, ThemeRole role)
    {
        Color ink = Theme.Light.Resolve(role);
        if (label.text == text && label.font == font && Mathf.Approximately(label.fontSize, size) && label.fontStyle == FontStyles.Normal
            && label.alignment == alignment && label.textWrappingMode == TextWrappingModes.Normal && label.overflowMode == TextOverflowModes.Overflow
            && !label.enableAutoSizing && !label.raycastTarget && label.color == ink)
            return false;
        label.text = text;
        label.font = font;
        label.fontSize = size;
        label.fontStyle = FontStyles.Normal;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.enableAutoSizing = false;
        label.raycastTarget = false;
        label.color = ink;   // opaque — the ThemedColor binding keeps the authored alpha
        return true;
    }

    private static bool SetSpacing(TMP_Text label, float spacing)
    {
        if (Mathf.Approximately(label.characterSpacing, spacing)) return false;
        label.characterSpacing = spacing;
        return true;
    }

    private static bool SetWord(TMP_Text label, string text)
    {
        if (label.text == text) return false;
        label.text = text;
        return true;
    }

    // What the page leaves: no height of its own, all the free height.
    private static bool SetFill(LayoutElement element)
    {
        if (!element.ignoreLayout && Same(element.minWidth, -1f) && Same(element.minHeight, 0f) && Same(element.preferredWidth, -1f)
            && Same(element.preferredHeight, 0f) && Same(element.flexibleWidth, -1f) && Same(element.flexibleHeight, 1f))
            return false;
        element.ignoreLayout = false;
        element.minWidth = -1f;
        element.minHeight = 0f;
        element.preferredWidth = -1f;
        element.preferredHeight = 0f;
        element.flexibleWidth = -1f;
        element.flexibleHeight = 1f;
        return true;
    }

    private static bool SetFitter(ContentSizeFitter fitter)
    {
        if (fitter.horizontalFit == ContentSizeFitter.FitMode.Unconstrained && fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize)
            return false;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return true;
    }

    private static bool SetHeight(RectTransform rect, float height)
    {
        if (Same(rect.sizeDelta.y, height)) return false;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        return true;
    }

    private static bool SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        if (rect.anchorMin == anchorMin && rect.anchorMax == anchorMax && rect.pivot == pivot) return false;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        return true;
    }

    // Vertical only, the chat list's own feel (its movement, elasticity, deceleration and sensitivity) when it has one.
    private static bool SetScroll(ScrollRect scroll, RectTransform viewport, RectTransform content, ScrollRect feel)
    {
        ScrollRect.MovementType movement = feel != null ? feel.movementType : ScrollRect.MovementType.Elastic;
        float elasticity = feel != null ? feel.elasticity : 0.1f;
        float deceleration = feel != null ? feel.decelerationRate : 0.135f;
        float sensitivity = feel != null ? feel.scrollSensitivity : 1f;
        if (scroll.viewport == viewport && scroll.content == content && !scroll.horizontal && scroll.vertical && scroll.movementType == movement
            && Same(scroll.elasticity, elasticity) && scroll.inertia && Same(scroll.decelerationRate, deceleration)
            && Same(scroll.scrollSensitivity, sensitivity))
            return false;
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = movement;
        scroll.elasticity = elasticity;
        scroll.inertia = true;
        scroll.decelerationRate = deceleration;
        scroll.scrollSensitivity = sensitivity;
        return true;
    }

    private static TMP_InputField InputOf(ChatSearchBar bar) =>
        new SerializedObject(bar).FindProperty("input")?.objectReferenceValue as TMP_InputField
        ?? throw new InvalidOperationException($"{Tag} The cloned search bar's ChatSearchBar.input is not wired.");

    // The house keyboard contract every input keeps (BusinessContactFieldsTests): never SingleLine, autocorrection off, the
    // Default keypad.
    private static bool SetKeyboardContract(TMP_InputField input)
    {
        if (input.lineType == TMP_InputField.LineType.MultiLineSubmit && input.inputType == TMP_InputField.InputType.Standard
            && input.keyboardType == TouchScreenKeyboardType.Default)
            return false;
        input.lineType = TMP_InputField.LineType.MultiLineSubmit;
        input.inputType = TMP_InputField.InputType.Standard;
        input.keyboardType = TouchScreenKeyboardType.Default;
        return true;
    }

    // Taps on the strip reach what is under it — every handler of the press, the folder's parts only.
    private static bool SetPassthrough(ClickPassthrough pass, Transform panel)
    {
        var refs = new SerializedObject(pass);
        SerializedProperty allowed = refs.FindProperty("allowedPanel");
        SerializedProperty all = refs.FindProperty("deliverPressToAllBehind");
        if (allowed.objectReferenceValue == panel && all.boolValue) return false;
        allowed.objectReferenceValue = panel;
        all.boolValue = true;
        refs.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // The chat row's own fonts: its name's (SF Pro Semibold) and its preview's (SF Pro Regular).
    private readonly struct FolderFonts
    {
        public FolderFonts(ChatItemView row)
        {
            Semibold = row.titleText != null ? row.titleText.font : null;
            Regular = row.lastMessageText != null ? row.lastMessageText.font : null;
            if (Semibold == null || Regular == null)
                throw new InvalidOperationException($"{Tag} ChatItem.prefab: the name's or the preview's font is missing — the folder's fonts come from them.");
        }

        public TMP_FontAsset Semibold { get; }
        public TMP_FontAsset Regular { get; }
    }
}
#endif
