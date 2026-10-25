using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.3, §5.4; sketch 009 screens 1, 3, 4): the «Личные» folder — a page over the chat list, like
/// «Архив» in WhatsApp, for the bot and the channel the chats screen shows. Three modes (<see cref="FolderMode"/>):
///   • Browse — every personal chat, newest first, its unread counters grey; a person's row swipes «В рабочие» (and «Удалить»
///     on WhatsApp), a tap opens the chat as the list does. «Изменить» switches to Edit; the chevron, the left-edge swipe and
///     Android Back close the folder.
///   • Edit — «Отметьте клиентов»: a checkbox on every person; «Сделать рабочими · N» makes the marked chats working (nothing
///     marked: «Отметьте хотя бы один чат»); «Отмена» and Back go back to Browse.
///   • Onboarding — «Кто из них клиенты?» (<see cref="PersonalChatsOnboarding"/>): the same selection right after a channel was
///     connected, each row saying why it is personal; the loader while the channel's chat list is empty — asked again every
///     <see cref="OnboardingPollSeconds"/> s while the screen is up — and a soft note when chats came and none is a contact.
///     «Готово · клиентов: N» makes the marked chats working and «Отмечу позже» (Back too) leaves them: both are done with the
///     screen for good.
/// What is listed is <see cref="PersonalChatsFolderModel"/>'s; how each chat stands, <see cref="ChatAudienceStore.ViewOf"/> under
/// the channel's profile. The rows are the chat list's own ChatItem rows; a row's swipe moves and deletes through the chat
/// list's own <see cref="ChatListView.RequestMove"/> / <see cref="ChatListView.RequestDelete"/> — one code path for both lists —
/// each row subscribed once, when it is made. While open the folder redraws on a chat added or removed, a row's data, a
/// decision or a move (once per frame), and it belongs to one bot and one channel: a switch to another closes it, a switch
/// naming its own leaves it open (the settings open it right after switching the chats to that bot).
///
/// Authored by PersonalChatsBuilder (Tools/Personal Chats/Build) as Screen_Messanger/PersonalChatsPanel — right after the chat
/// list, before the open chat, which slides in over it — hidden; every colour is a ThemedColor binding. The panel is inactive
/// while closed, so the folder is found with an include-inactive lookup (<see cref="Instance"/>) and wires itself on its first
/// open; its entry, the working list's «Личные» row, is subscribed at launch.
/// </summary>
public class PersonalChatsFolderView : MonoBehaviour
{
    /// <summary>
    /// «Кто из них клиенты?» asks for the channel's chats again this often while it is up: WhatsApp sends its history over
    /// minutes after a connection, and nothing else re-reads the list while the owner stays on the screen.
    /// </summary>
    public const float OnboardingPollSeconds = 10f;

    private const float SlideInSeconds = 0.3f;
    private const float SlideOutSeconds = 0.25f;

    [Header("The chat list the folder stands on")]
    [Tooltip("A row's swipe moves and deletes through the chat list's own RequestMove / RequestDelete.")]
    [SerializeField] private ChatListView chatList;
    [Tooltip("The chat list's own row prefab (ChatListView.prefab).")]
    [SerializeField] private ChatItemView rowPrefab;

    [Header("Header")]
    [SerializeField] private Button backButton;
    [SerializeField] private GameObject backChevron;       // Browse: the chevron
    [SerializeField] private GameObject backLabel;         // Edit: «Отмена»
    [SerializeField] private TextMeshProUGUI title;
    [SerializeField] private Button editButton;            // Browse: «Изменить»

    [Header("Above the rows")]
    [SerializeField] private GameObject statusLine;        // Onboarding: «WhatsApp подключён»
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject body;              // Browse: the hint; Onboarding: what the screen is for
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private ChatSearchBar search;         // Edit, Onboarding
    [SerializeField] private TextMeshProUGUI searchPlaceholder;
    [SerializeField] private GameObject sectionHeader;     // Onboarding: «НЕДАВНИЕ ЧАТЫ»
    [SerializeField] private TextMeshProUGUI loadingLabel; // Onboarding, while the channel's chat list is empty

    [Header("The rows")]
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private RectTransform rowsContent;

    [Header("Below the rows")]
    [SerializeField] private GameObject noContactsNote;    // Onboarding: chats came, none is a contact
    [SerializeField] private TextMeshProUGUI noContactsText;
    [SerializeField] private GameObject bottomBar;         // Edit, Onboarding
    [SerializeField] private Button primaryButton;
    [SerializeField] private TextMeshProUGUI primaryLabel;
    [SerializeField] private Button secondaryButton;       // Onboarding: «Отмечу позже»
    [SerializeField] private GameObject footer;            // Browse: the swipe hint
    [SerializeField] private SwipeToBackPanel swipeBack;   // Browse: the left-edge swipe

    private static PersonalChatsFolderView _instance;

    private readonly Dictionary<string, ChatItemView> _rows = new Dictionary<string, ChatItemView>(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _shown = new List<string>();
    private IReadOnlyList<FolderRow> _allRows = Array.Empty<FolderRow>();
    private string _bot;
    private ChatChannel _channel;
    private string _query = "";
    private bool _wired;
    private bool _refreshPending;
    private ChatManager _chats;
    private Coroutine _poll;
    private Tween _slide;

    /// <summary>The scene's folder — found inactive too: it is closed most of the time.</summary>
    public static PersonalChatsFolderView Instance =>
        _instance != null ? _instance : _instance = FindFirstObjectByType<PersonalChatsFolderView>(FindObjectsInactive.Include);

    /// <summary>The folder is open and on screen — what Android Back acts on.</summary>
    public static bool IsShowing
    {
        get
        {
            PersonalChatsFolderView folder = Instance;
            return folder != null && folder.IsOpen && folder.gameObject.activeInHierarchy;
        }
    }

    public bool IsOpen { get; private set; }
    public FolderMode Mode { get; private set; }

    /// <summary>The chat list is on screen: its tab is shown and no chat is open over it (the open chat hides the list).</summary>
    public bool ChatsScreenVisible => chatList != null && chatList.gameObject.activeInHierarchy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // Remove first: a launch without a domain reload keeps the static event's old subscribers.
        PersonalChatsRowView.Tapped -= OpenFromTheList;
        PersonalChatsRowView.Tapped += OpenFromTheList;
    }

    /// <summary>The working list's «Личные» row was tapped.</summary>
    internal static void OpenFromTheList() => Instance?.Open(FolderMode.Browse);

    // ---- the contract ----

    /// <summary>
    /// Opens the folder in <paramref name="mode"/> for the bot and the channel the chats screen shows now. An open folder
    /// switches its mode instead; either way the marks and the search start afresh.
    /// </summary>
    public void Open(FolderMode mode)
    {
        ChatManager chats = ChatManager.Instance;
        if (chats == null) return;
        EnsureWired();
        bool opening = !IsOpen;
        _bot = chats.CurrentBotId;
        _channel = chats.ActiveChannel;
        IsOpen = true;
        bool fromEdge = !gameObject.activeSelf;
        if (fromEdge) gameObject.SetActive(true);
        SetMode(mode);
        if (opening) SlideIn(fromEdge);
        Rebuild();
    }

    /// <summary>Closes the folder; it slides off, or goes at once when it is not on screen.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        StopPoll();
        _selected.Clear();
        SlideOut();
    }

    /// <summary>Android Back — the folder's own left button: Edit is cancelled, the onboarding is «Отмечу позже», else close.</summary>
    public bool TryBack()
    {
        if (!IsOpen) return false;
        switch (Mode)
        {
            case FolderMode.Edit:
                SetMode(FolderMode.Browse);
                Rebuild();
                break;
            case FolderMode.Onboarding:
                Later();
                break;
            default:
                Close();
                break;
        }
        return true;
    }

    /// <summary>«Кто из них клиенты?» is owed for the chats on screen: shown, unless it already is or the owner is marking chats.</summary>
    public void ShowOnboarding()
    {
        if (IsOpen && Mode != FolderMode.Browse) return;
        Open(FolderMode.Onboarding);
    }

    // ---- lifecycle ----

    private void Awake() => EnsureWired();

    private void OnEnable()
    {
        if (!IsOpen) return;
        // Back on screen after its tab was hidden: the chats may show another bot or channel by now.
        ChatManager chats = ChatManager.Instance;
        if (chats != null && !PersonalChatsFolderModel.StaysOpen(_bot, _channel, chats.CurrentBotId, chats.ActiveChannel))
        {
            CloseAtOnce();
            return;
        }
        RequestRefresh();
        RestartPoll();
    }

    private void OnDisable() => _poll = null;   // a hidden object's coroutines are stopped already

    private void LateUpdate()
    {
        if (_refreshPending) Rebuild();
    }

    private void OnDestroy()
    {
        Unwire();
        if (_instance == this) _instance = null;
    }

    // Once: on the first open (the panel is authored inactive, so Awake comes late) or in Awake, whichever is first.
    private void EnsureWired()
    {
        if (_wired) return;
        _wired = true;
        _instance = this;
        if (backButton != null) backButton.onClick.AddListener(HandleBackTapped);
        if (editButton != null) editButton.onClick.AddListener(EnterEdit);
        if (primaryButton != null) primaryButton.onClick.AddListener(HandlePrimary);
        if (secondaryButton != null) secondaryButton.onClick.AddListener(Later);
        if (search != null) search.OnQueryChanged += HandleQuery;
        if (swipeBack != null) swipeBack.OnCommitted = HandleSwipedAway;
        ChatAudienceStore.OnChanged += RequestRefresh;
        _chats = ChatManager.Instance;
        if (_chats == null) return;
        _chats.OnChatAdded += HandleChatAdded;
        _chats.OnChatRemoved += HandleChatRemoved;
        _chats.OnChatListCleared += HandleChatListCleared;
        _chats.OnActiveBotChanged += HandleActiveBotChanged;
        _chats.OnActiveChannelChanged += HandleActiveChannelChanged;
    }

    private void Unwire()
    {
        if (!_wired) return;
        ChatAudienceStore.OnChanged -= RequestRefresh;
        if (search != null) search.OnQueryChanged -= HandleQuery;
        if (_chats == null) return;
        _chats.OnChatAdded -= HandleChatAdded;
        _chats.OnChatRemoved -= HandleChatRemoved;
        _chats.OnChatListCleared -= HandleChatListCleared;
        _chats.OnActiveBotChanged -= HandleActiveBotChanged;
        _chats.OnActiveChannelChanged -= HandleActiveChannelChanged;
    }

    // ---- what changes it ----

    private void RequestRefresh() => _refreshPending = true;

    private void HandleChatAdded(ChatViewModel _) => RequestRefresh();

    private void HandleChatRemoved(string chatId)
    {
        DiscardRow(chatId);
        RequestRefresh();
    }

    // The chat list was rebuilt (a bot or channel switch, a privacy clear): its rows belong to chats that are gone.
    private void HandleChatListCleared()
    {
        foreach (ChatItemView item in _rows.Values)
            if (item != null) Discard(item.gameObject);
        _rows.Clear();
        _selected.Clear();
        _shown.Clear();
        RequestRefresh();
    }

    // A bot switch announces the bot before it resolves that bot's channel, so the bot alone decides here.
    private void HandleActiveBotChanged(string botId)
    {
        if (this != null && IsOpen && !PersonalChatsFolderModel.StaysOpen(_bot, _channel, botId, _channel)) Close();
    }

    private void HandleActiveChannelChanged(ChatChannel channel)
    {
        if (this != null && IsOpen && !PersonalChatsFolderModel.StaysOpen(_bot, _channel, _bot, channel)) Close();
    }

    private void HandleQuery(string query)
    {
        _query = query ?? "";
        if (IsOpen) Rebuild();   // at once — the owner is typing
    }

    // ---- the owner's taps ----

    private void HandleBackTapped() => TryBack();

    private void EnterEdit() => SetModeAndRedraw(FolderMode.Edit);

    private void HandlePrimary()
    {
        if (Mode == FolderMode.Edit) ConfirmEdit();
        else if (Mode == FolderMode.Onboarding) FinishOnboarding(moveMarked: true);
    }

    // «Сделать рабочими · N»: the marked chats become working and the folder is itself again; nothing marked — it says so.
    private void ConfirmEdit()
    {
        int moved = MoveMarkedToWork();
        if (moved == 0)
        {
            AudienceToast.Show(PersonalChatsCopy.SelectAtLeastOne);
            return;
        }
        AudienceToast.Show(PersonalChatsFolderModel.MovedToast(moved));
        SetModeAndRedraw(FolderMode.Browse);
    }

    // «Отмечу позже» (and Back): the screen is done with, nothing moves.
    private void Later() => FinishOnboarding(moveMarked: false);

    // «Готово» moves what is marked — nothing at all when nothing is — and either way the screen is no longer owed.
    private void FinishOnboarding(bool moveMarked)
    {
        int moved = moveMarked ? MoveMarkedToWork() : 0;
        PersonalChatsOnboarding.Clear(_bot, _channel == ChatChannel.Telegram);
        Close();
        if (moved > 0) AudienceToast.Show(PersonalChatsFolderModel.MovedToast(moved));
    }

    // The marked chats the folder lists and the server takes go to work through the queue; how many went (never an empty call).
    private int MoveMarkedToWork()
    {
        ChatManager chats = ChatManager.Instance;
        string profileId = chats != null ? chats.ActiveChannelProfileId() : null;
        List<string> chatIds = PersonalChatsFolderModel.ChatsToMove(_allRows, _selected);
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || chatIds.Count == 0) return 0;
        ChatAudienceSyncService.MakeBusiness(_bot, profileId, chatIds);
        return chatIds.Count;
    }

    // The left-edge swipe already slid the page off the screen (Browse only).
    private void HandleSwipedAway()
    {
        if (!IsOpen) return;
        IsOpen = false;
        StopPoll();
        _selected.Clear();
        FinishClose();
    }

    // ---- a row's own requests: one code path for both lists ----

    private void HandleRowMove(ChatViewModel chat)
    {
        if (chatList != null) chatList.RequestMove(chat);   // it moves the chat by its kind now and says so
    }

    private void HandleRowDelete(ChatViewModel chat)
    {
        if (chatList != null) chatList.RequestDelete(chat); // its confirm card stands over the folder
    }

    private void HandleRowSelectToggled(ChatViewModel chat)
    {
        if (chat == null || Mode == FolderMode.Browse) return;
        bool marked = !_selected.Remove(chat.ChatId);
        if (marked) _selected.Add(chat.ChatId);
        if (_rows.TryGetValue(chat.ChatId, out ChatItemView item) && item != null) item.SetSelectable(true, marked);
        SetText(primaryLabel, PersonalChatsFolderModel.PrimaryLabel(Mode, _selected.Count));
    }

    // ---- drawing ----

    // A mode starts afresh: no marks, no search, the list at its top. The chrome goes first: a search the new mode hides lets
    // go of the keyboard before its text is cleared — never a write into a field that still holds the focus.
    private void SetMode(FolderMode mode)
    {
        Mode = mode;
        _selected.Clear();
        _query = "";
        ApplyChrome();
        if (search != null && !string.IsNullOrEmpty(search.CurrentQuery)) search.Clear();
        if (scroll != null && scroll.content != null) scroll.verticalNormalizedPosition = 1f;
        RestartPoll();
    }

    private void SetModeAndRedraw(FolderMode mode)
    {
        SetMode(mode);
        Rebuild();
    }

    // What each mode shows (sketch 009: screen 3 — Browse, 4 — Edit, 1 — Onboarding).
    private void ApplyChrome()
    {
        bool browse = Mode == FolderMode.Browse, edit = Mode == FolderMode.Edit, onboarding = Mode == FolderMode.Onboarding;
        bool telegram = _channel == ChatChannel.Telegram;
        SetShown(backButton, !onboarding);
        SetShown(backChevron, browse);
        SetShown(backLabel, edit);
        SetText(title, browse ? PersonalChatsCopy.FolderTitle : edit ? PersonalChatsCopy.EditTitle : PersonalChatsCopy.OnboardingTitle);
        SetShown(statusLine, onboarding);
        SetText(statusText, PersonalChatsCopy.OnboardingConnected(telegram));
        SetShown(body, !edit);
        SetText(bodyText, onboarding ? PersonalChatsCopy.OnboardingBody : PersonalChatsCopy.FolderHint);
        SetShown(search, !browse);
        SetText(searchPlaceholder, onboarding ? PersonalChatsCopy.OnboardingSearch : PersonalChatsCopy.EditSearch);
        SetShown(sectionHeader, onboarding);
        SetText(loadingLabel, PersonalChatsCopy.OnboardingLoading(telegram));
        SetText(noContactsText, PersonalChatsCopy.OnboardingNoContacts(telegram));
        SetShown(bottomBar, !browse);
        SetShown(secondaryButton, onboarding);
        SetShown(swipeBack, browse);
    }

    private void Rebuild()
    {
        _refreshPending = false;
        if (!IsOpen) return;
        List<(ChatViewModel chat, AudienceView view)> chats = CurrentChats();
        _allRows = PersonalChatsFolderModel.Rows(chats, Mode, "");
        IReadOnlyList<FolderRow> shown = string.IsNullOrEmpty(_query) ? _allRows : PersonalChatsFolderModel.Rows(chats, Mode, _query);
        KeepMarksOnListedChats();
        PlaceRows(shown);
        ApplyState(chats.Count);
    }

    // The chats the chats screen holds for the folder's bot and channel, each with how it stands.
    private static List<(ChatViewModel chat, AudienceView view)> CurrentChats()
    {
        var chats = new List<(ChatViewModel chat, AudienceView view)>();
        ChatManager manager = ChatManager.Instance;
        if (manager == null) return chats;
        string profileId = manager.ActiveChannelProfileId();
        foreach (ChatViewModel chat in manager.Chats)
            if (chat != null) chats.Add((chat, ChatAudienceStore.ViewOf(profileId, chat)));
        return chats;
    }

    // A mark on a chat the folder no longer lists (it became working, it was deleted) goes; one the search hides stays.
    private void KeepMarksOnListedChats()
    {
        if (_selected.Count == 0) return;
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (FolderRow row in _allRows) listed.Add(row.Chat.ChatId);
        _selected.IntersectWith(listed);
    }

    // The shown rows in order, the rest hidden but kept — a search typed and cleared brings them back at once.
    private void PlaceRows(IReadOnlyList<FolderRow> shown)
    {
        _shown.Clear();
        foreach (FolderRow row in shown)
        {
            ChatItemView item = RowFor(row);
            if (item == null) continue;
            if (item.transform.GetSiblingIndex() != _shown.Count) item.transform.SetSiblingIndex(_shown.Count);
            if (!item.gameObject.activeSelf) item.gameObject.SetActive(true);
            _shown.Add(row.Chat.ChatId);
        }
        var shownIds = new HashSet<string>(_shown, StringComparer.Ordinal);
        foreach (KeyValuePair<string, ChatItemView> row in _rows)
            if (row.Value != null && !shownIds.Contains(row.Key) && row.Value.gameObject.activeSelf) row.Value.gameObject.SetActive(false);
    }

    // The row the chat has, else a new one; its place, marks and line re-applied (each a no-op when unchanged), its chat bound
    // when it is another one.
    private ChatItemView RowFor(FolderRow row)
    {
        if (!_rows.TryGetValue(row.Chat.ChatId, out ChatItemView item) || item == null)
        {
            item = MakeRow();
            if (item == null) return null;
            _rows[row.Chat.ChatId] = item;
        }
        item.SetSwipeContext(true);                  // «В рабочие»; «Удалить» stays on WhatsApp
        item.SetAudienceMarks(false, true);          // everything here is personal: the counter is grey, no lock needed
        item.SetSubline(row.Subline);
        item.SetSelectable(row.Selectable, _selected.Contains(row.Chat.ChatId));
        if (item.Vm != row.Chat) item.Bind(row.Chat);
        return item;
    }

    // A row of the chat list's own kind, its requests subscribed ONCE — here, when it is made, never on a redraw.
    private ChatItemView MakeRow()
    {
        if (rowPrefab == null || rowsContent == null) return null;
        ChatItemView item = Instantiate(rowPrefab, rowsContent);
        item.MoveRequested += HandleRowMove;
        item.DeleteRequested += HandleRowDelete;
        item.SelectToggled += HandleRowSelectToggled;
        item.ChatUpdated += RequestRefresh;
        item.OrderChanged += RequestRefresh;
        return item;
    }

    private void DiscardRow(string chatId)
    {
        if (string.IsNullOrEmpty(chatId) || !_rows.TryGetValue(chatId, out ChatItemView item)) return;
        _rows.Remove(chatId);
        _selected.Remove(chatId);
        if (item != null) Discard(item.gameObject);
    }

    // What depends on the rows: «Изменить» and the swipe hint need a person to move, the loader an empty chat list, the soft
    // note chats without a contact, the bottom button the marks.
    private void ApplyState(int chatCount)
    {
        bool browse = Mode == FolderMode.Browse, onboarding = Mode == FolderMode.Onboarding;
        bool movable = PersonalChatsFolderModel.AnyMovable(_allRows);
        SetShown(editButton, browse && movable);
        SetShown(footer, browse && movable);
        SetShown(loadingLabel, onboarding && chatCount == 0);
        SetShown(noContactsNote, onboarding && PersonalChatsFolderModel.ShowNoContactsNote(_allRows, loaded: chatCount > 0));
        SetText(primaryLabel, PersonalChatsFolderModel.PrimaryLabel(Mode, _selected.Count));
    }

    // ---- the onboarding's poll ----

    private void RestartPoll()
    {
        StopPoll();
        if (IsOpen && Mode == FolderMode.Onboarding && Application.isPlaying && isActiveAndEnabled) _poll = StartCoroutine(PollChats());
    }

    private void StopPoll()
    {
        if (_poll != null) StopCoroutine(_poll);
        _poll = null;
    }

    // RefreshActiveBotChats collapses a sync already on its way and waits out a post-creation sync window by itself.
    private IEnumerator PollChats()
    {
        var wait = new WaitForSecondsRealtime(OnboardingPollSeconds);
        while (true)
        {
            yield return wait;
            if (ChatManager.Instance != null) ChatManager.Instance.RefreshActiveBotChats();
        }
    }

    // ---- the slide ----

    private RectTransform Page => (RectTransform)transform;

    private void SlideIn(bool fromEdge)
    {
        _slide?.Kill();
        if (!Application.isPlaying)
        {
            SetPageX(0f);
            return;
        }
        if (fromEdge) SetPageX(CanvasWidth());
        _slide = Page.DOAnchorPosX(0f, SlideInSeconds).SetEase(Ease.OutCubic).SetLink(gameObject);
    }

    private void SlideOut()
    {
        _slide?.Kill();
        if (!Application.isPlaying || !gameObject.activeInHierarchy)
        {
            FinishClose();
            return;
        }
        _slide = Page.DOAnchorPosX(CanvasWidth(), SlideOutSeconds).SetEase(Ease.InCubic).SetLink(gameObject).OnComplete(FinishClose);
    }

    // Back on screen for a bot or channel it no longer belongs to: gone at once, no slide over the new chats.
    private void CloseAtOnce()
    {
        IsOpen = false;
        StopPoll();
        _selected.Clear();
        _slide?.Kill();
        FinishClose();
    }

    private void FinishClose()
    {
        _slide = null;
        if (IsOpen) return;   // opened again while it slid away
        SetPageX(0f);
        gameObject.SetActive(false);
    }

    private void SetPageX(float x) => Page.anchoredPosition = new Vector2(x, Page.anchoredPosition.y);

    private float CanvasWidth()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        return canvas != null ? ((RectTransform)canvas.rootCanvas.transform).rect.width : 1080f;
    }

    // ---- helpers ----

    private static void SetShown(Component part, bool shown)
    {
        if (part != null) SetShown(part.gameObject, shown);
    }

    private static void SetShown(GameObject part, bool shown)
    {
        if (part != null && part.activeSelf != shown) part.SetActive(shown);
    }

    private static void SetText(TMP_Text label, string text)
    {
        if (label != null && text != null && label.text != text) label.text = text;
    }

    private static void Discard(GameObject row)
    {
        if (Application.isPlaying) Destroy(row);
        else DestroyImmediate(row);
    }

    // ---- test seams ----

    internal IReadOnlyList<string> ShownChatIdsForTests => _shown;

    internal ChatItemView RowForTests(string chatId) =>
        chatId != null && _rows.TryGetValue(chatId, out ChatItemView item) ? item : null;

    internal void RefreshNowForTests() => Rebuild();

    internal static void ResetForTests() => _instance = null;
}
