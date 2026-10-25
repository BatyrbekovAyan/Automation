using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// The chats screen's list: one ChatItem row per chat of the active bot's active channel, ordered by the last message,
/// filtered by the search bar. «Личные и рабочие чаты» (spec §5.2): it shows the WORKING chats; the personal ones stand
/// behind the «Личные» row (<see cref="PersonalChatsRowView"/>, right after the search bar) and come back — with a lock and
/// a grey counter — only while a search is on. How each chat is drawn is <see cref="ChatAudienceStore.ViewOf"/>'s verdict
/// under the channel's profile; what follows from it is <see cref="PersonalChatsListModel"/>'s. The verdict is re-read,
/// once per frame at most, whenever it can change: a row's data (facts, unread, last message), a store change (a decision,
/// a connection moment, the owner's move), a row added or removed, the query. Each row's swipe follows the same verdict
/// (spec §5.2; <see cref="ChatRowSwipePolicy.InFolder"/>): a working chat offers «В личные», a personal one in the search
/// results «В рабочие», as in the folder, and every WhatsApp chat keeps «Удалить»; <see cref="RequestMove"/> carries the move
/// out.
/// </summary>
public class ChatListView : MonoBehaviour
{
    [Header("Containers")]
    public Transform content;

    public ChatItemView prefab;

    [SerializeField] private ChatDeleteConfirm deleteConfirm;

    [Tooltip("The «Личные» row in Content, right after ChatsSearchBar — stamped by PersonalChatsBuilder.")]
    [SerializeField] private PersonalChatsRowView personalRow;

    private Dictionary<string, ChatItemView> itemsByChatId = new();

    // Coalesced data-driven ordering: any number of same-frame triggers (a multi-chat
    // sync pass, the initial cache rebuild, a live send) collapse into ONE resort,
    // applied in LateUpdate before the frame renders.
    private bool resortPending;

    // Coalesced the same way: which rows the list shows, their locks and the «Личные» row.
    private bool visibilityPending;

    private ChatSearchBar searchBar;
    private string currentQuery = "";
    private static readonly CompareInfo Ci = CultureInfo.InvariantCulture.CompareInfo;
    private readonly List<(AudienceView v, int unread)> audienceScratch = new();

    private bool SearchActive => !string.IsNullOrEmpty(currentQuery);

    void Start()
    {
        var manager = ChatManager.Instance;
        manager.OnChatAdded += AddChat;
        manager.OnChatListCleared += ClearChatList;
        manager.OnEmptyState += HandleEmptyState;
        manager.OnActiveBotChanged += HandleActiveBotChanged;
        manager.OnChatSelected += HandleChatSelected;
        manager.OnChatRemoved += RemoveChat;
        ChatAudienceStore.OnChanged += RequestVisibilityRefresh;

        searchBar = GetComponentInChildren<ChatSearchBar>(true);
        if (searchBar != null)
            searchBar.OnQueryChanged += ApplyFilter;

        foreach (var chat in manager.Chats)
            AddChat(chat);

        RequestVisibilityRefresh();   // the «Личные» row follows even an empty list
    }

    void ClearChatList()
    {
        // Destroy only the items this view tracks — leaves any non-item
        // siblings (the ChatsSearchBar header, the «Личные» row) intact across bot switches.
        foreach (var item in itemsByChatId.Values)
        {
            if (item != null) Destroy(item.gameObject);
        }
        itemsByChatId.Clear();
        RequestVisibilityRefresh();
    }

    void AddChat(ChatViewModel vm)
    {
        // Real data came in — make sure our content panel is visible.
        if (content != null && !content.gameObject.activeSelf)
        {
            content.gameObject.SetActive(true);
        }

        // --- THE FIX: Everything goes into the normalContent now! ---
        var item = Instantiate(prefab, content);
        item.MoveRequested += RequestMove;   // the row's swipe: «В личные» / «В рабочие»

        // Apply the active filter BEFORE the bind, so a newly-arriving chat never shows for a frame where it does
        // not belong (a personal chat outside a search, a query still set after a bot switch) — and a row hidden here
        // binds inactive, so its avatar waits for the row to be shown instead of starting a download the hide would
        // cut off.
        ApplyMatchToItem(item, vm, ChatAudienceStore.ViewOf(ActiveProfileId(), vm), SearchActive);
        item.Bind(vm);
        itemsByChatId[vm.ChatId] = item;

        // Provisional position only — the coalesced resort below places the row by
        // last-message time. Server order is NOT trusted: a brand-new chat arriving
        // mid-session would otherwise land at the bottom of the list.
        item.transform.SetAsLastSibling();
        item.transform.localScale = Vector3.one;

        RequestResort();
        RequestVisibilityRefresh();   // the «Личные» row may gain a chat

        // Row movement on update is handled inside ChatItemView.OnLastMessageChanged,
        // which unsubscribes itself in OnDestroy. Don't re-subscribe here — that leaks closures.
    }

    private void HandleEmptyState(EmptyStateReason _)
    {
        // The EmptyStateView surface activates itself; we just hide the list area.
        if (content != null)
        {
            content.gameObject.SetActive(false);
        }
    }

    private void HandleActiveBotChanged(string _)
    {
        if (content != null)
        {
            content.gameObject.SetActive(true);
        }
    }

    private void HandleChatSelected(string _)
    {
        // User opened a chat — drop search focus so TMP's caret can't linger
        // on top of the placeholder when they swipe back to the list.
        if (searchBar != null) searchBar.ReleaseFocus();
        // ...and put away any open swipe-to-delete reveal.
        SwipeToDelete.CloseAnyOpen();
    }

    private void ApplyFilter(string query)
    {
        currentQuery = query ?? "";
        ApplyVisibility();   // at once — the owner is typing
    }

    /// <summary>
    /// Re-read which rows the list shows on the next LateUpdate (coalesced): a row's data changed (ChatItemView asks), the
    /// audience store changed, a row came or went.
    /// </summary>
    public void RequestVisibilityRefresh() => visibilityPending = true;

    // One pass over every row: the store's verdict, the row's visibility and marks, and the «Личные» row's count.
    private void ApplyVisibility()
    {
        string profileId = ActiveProfileId();
        bool searchActive = SearchActive;
        audienceScratch.Clear();
        foreach (var item in itemsByChatId.Values)
        {
            if (item == null || item.Vm == null) continue;
            AudienceView view = ChatAudienceStore.ViewOf(profileId, item.Vm);
            ApplyMatchToItem(item, item.Vm, view, searchActive);
            audienceScratch.Add((view, item.Vm.UnreadCount));
        }
        ApplyPersonalRow(searchActive);
    }

    // A row is shown when it is working (or a search is on) and it matches the query; a personal one in the search results
    // wears the lock, its unread badge is grey (personal chats are muted in the app) and it swipes as in the «Личные» folder
    // («В рабочие»). The swipe's place is re-read on every pass, so a chat that changes kind while it is shown never offers
    // the wrong move (the row acts only when its actions change).
    private void ApplyMatchToItem(ChatItemView item, ChatViewModel vm, AudienceView view, bool searchActive)
    {
        if (item == null) return;
        bool shown = PersonalChatsListModel.Visible(view, searchActive) && Matches(vm, currentQuery);
        if (item.gameObject.activeSelf != shown)
            item.gameObject.SetActive(shown);
        item.SetAudienceMarks(PersonalChatsListModel.ShowLock(view, searchActive), PersonalChatsListModel.IsPersonal(view));
        item.SetSwipeContext(ChatRowSwipePolicy.InFolder(view));
    }

    // The «Личные» row: there while there are personal chats and no search; its grey counter is the personal chats with
    // something unread — hidden, like every row badge, when the owner turned the unread counter off.
    private void ApplyPersonalRow(bool searchActive)
    {
        if (personalRow == null) return;
        bool shown = PersonalChatsListModel.RowVisible(PersonalChatsListModel.PersonalCount(audienceScratch), searchActive);
        if (personalRow.gameObject.activeSelf != shown)
            personalRow.gameObject.SetActive(shown);
        personalRow.Bind(NotifPrefs.UnreadBadgeEnabled ? PersonalChatsListModel.PersonalUnreadChats(audienceScratch) : 0);
    }

    // The active channel's profile — the key the audience decisions are kept under.
    private static string ActiveProfileId() =>
        ChatManager.Instance != null ? ChatManager.Instance.ActiveChannelProfileId() : null;

    private static bool Matches(ChatViewModel vm, string q)
    {
        if (string.IsNullOrEmpty(q)) return true;
        if (vm == null) return false;

        if (!string.IsNullOrEmpty(vm.Title)
            && Ci.IndexOf(vm.Title, q, CompareOptions.IgnoreCase) >= 0)
            return true;

        if (!string.IsNullOrEmpty(vm.LastMessage)
            && Ci.IndexOf(vm.LastMessage, q, CompareOptions.IgnoreCase) >= 0)
            return true;

        return false;
    }

    /// <summary>
    /// Schedules a data-driven resort of the list (newest last-message first, via
    /// ChatListOrder). Coalesced: any number of same-frame requests — one per changed
    /// chat in a sync pass, one per row of the initial rebuild — apply as a single
    /// pass in LateUpdate, before the frame renders. Replaces the old per-row
    /// insert-at-top (RaiseToTop), which REVERSED every chat that changed within one
    /// multi-chat sync pass: ParseChatsJson iterates newest-first, so the newest row
    /// was raised first and each older row then landed above it.
    /// </summary>
    public void RequestResort()
    {
        resortPending = true;
    }

    void LateUpdate()
    {
        if (resortPending)
        {
            resortPending = false;
            ApplyOrderNow();
            // A row's last message may have changed since the previous pass — its
            // visibility under the active search query can flip either way.
            visibilityPending = true;
        }

        if (!visibilityPending) return;
        visibilityPending = false;
        ApplyVisibility();
    }

    private void ApplyOrderNow()
    {
        if (content == null) return;

        // Rows in current visual order. Skips non-row children (the pinned
        // ChatsSearchBar header, the «Личные» row) and rows already collapsing out after a delete
        // (removed from itemsByChatId but alive until their tween completes).
        var rows = new List<ChatItemView>();
        for (int i = 0; i < content.childCount; i++)
        {
            var item = TrackedRowAt(i);
            if (item != null) rows.Add(item);
        }

        var ordered = ChatListOrder.Apply(rows, r => r.Vm.LastMessageTime);

        // Fill ONLY the slots currently held by tracked rows, top-down, re-scanning
        // trackedness live because slots shift as rows move. Untracked children —
        // the pinned ChatsSearchBar header, the «Личные» row and any delete-collapsing
        // row — keep their own slots; assigning a contiguous block instead would expel a
        // mid-collapse row to the list bottom whenever a resort lands inside its
        // 0.2s tween (deterministically so on the same-frame RollbackDelete path).
        // Rows only ever move UP to their slot, so placed slots are never disturbed.
        int orderedIdx = 0;
        for (int slot = 0; slot < content.childCount && orderedIdx < ordered.Count; slot++)
        {
            if (TrackedRowAt(slot) == null) continue;

            var row = ordered[orderedIdx++];
            if (row.transform.GetSiblingIndex() != slot)
                row.transform.SetSiblingIndex(slot);
        }
    }

    /// <summary>
    /// The tracked chat row at a content child index, or null when that child is
    /// not a row this view owns (the ChatsSearchBar header, a row mid-collapse
    /// after RemoveChat, or a stale row a rollback re-add has already superseded).
    /// </summary>
    private ChatItemView TrackedRowAt(int childIndex)
    {
        var item = content.GetChild(childIndex).GetComponent<ChatItemView>();
        if (item == null || item.Vm == null) return null;
        return itemsByChatId.TryGetValue(item.Vm.ChatId, out var tracked) && tracked == item
            ? item
            : null;
    }

    // Collapse the row out, then destroy it. The scroll content uses a layout group
    // (rows reorder by sibling index), so animating LayoutElement.preferredHeight reflows.
    private void RemoveChat(string chatId)
    {
        if (!itemsByChatId.TryGetValue(chatId, out var item))
            return;
        itemsByChatId.Remove(chatId);
        RequestVisibilityRefresh();   // a personal chat leaving changes the «Личные» row
        if (item == null) return;

        var rt = (RectTransform)item.transform;
        var le = item.GetComponent<LayoutElement>();
        if (le == null) le = item.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = rt.rect.height;

        var cg = item.GetComponent<CanvasGroup>();
        if (cg == null) cg = item.gameObject.AddComponent<CanvasGroup>();

        var go = item.gameObject;
        DOTween.To(() => le.preferredHeight, v => le.preferredHeight = v, 0f, 0.2f)
            .SetEase(Ease.InCubic)
            .OnComplete(() => { if (go != null) Destroy(go); });
        cg.DOFade(0f, 0.2f);
    }

    /// <summary>
    /// A row's move button (ChatItemView.MoveRequested): a working chat goes to «Личные», a personal one — found by the search —
    /// becomes working. Decided by the chat's kind now, the same verdict that chose the button's word
    /// (<see cref="ChatRowSwipePolicy.InFolder"/>). The store draws the move at once, the queue delivers it, the toast says so.
    /// A chat that is not a person never moves, and a move the queue would drop (no real profile, an id the server refuses)
    /// is not announced.
    /// </summary>
    public void RequestMove(ChatViewModel chat)
    {
        ChatManager manager = ChatManager.Instance;
        if (chat == null || manager == null) return;
        string profileId = manager.ActiveChannelProfileId();
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || !ChatAudienceRequest.IsSendableChatId(chat.ChatId)) return;
        AudienceView view = ChatAudienceStore.ViewOf(profileId, chat);
        if (!view.CanMove) return;

        var chats = new[] { chat.ChatId };
        if (ChatRowSwipePolicy.InFolder(view))
        {
            ChatAudienceSyncService.MakeBusiness(manager.CurrentBotId, profileId, chats);
            AudienceToast.Show(PersonalChatsCopy.MovedToBusiness);
        }
        else
        {
            ChatAudienceSyncService.MakePersonal(manager.CurrentBotId, profileId, chats);
            AudienceToast.Show(PersonalChatsCopy.MovedToPersonal);
        }
    }

    // Called by a row's Delete button (via ChatItemView) — raises the confirm dialog.
    public void RequestDelete(ChatViewModel vm)
    {
        if (vm == null) return;
        if (deleteConfirm != null) deleteConfirm.Ask(vm.ChatId, vm.Title);
        else ChatManager.Instance?.DeleteChat(vm.ChatId); // fallback: no dialog wired
    }

    void OnDestroy()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatAdded -= AddChat;
            ChatManager.Instance.OnChatListCleared -= ClearChatList;
            ChatManager.Instance.OnEmptyState -= HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
            ChatManager.Instance.OnChatSelected -= HandleChatSelected;
            ChatManager.Instance.OnChatRemoved -= RemoveChat;
        }

        ChatAudienceStore.OnChanged -= RequestVisibilityRefresh;

        if (searchBar != null)
            searchBar.OnQueryChanged -= ApplyFilter;
    }
}