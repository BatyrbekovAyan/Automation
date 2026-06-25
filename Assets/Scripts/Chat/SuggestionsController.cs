using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The semi-auto mediator (Wave 3). Ties the <see cref="ISuggestionsProvider"/> seam (Plan 01),
/// the per-chat persistence + accessors (Plan 02), and the panel/toggle views (Plan 03) into the
/// live loop: toggle on/off → persist + show/hide; card tap → composer hand-off + re-cluster;
/// incoming message → auto-populate cards (never the composer); manual refresh; and the
/// monotonic-seq + captured-chat guard that discards stale/superseded results (DATA-03).
///
/// Above the seam: references <see cref="ISuggestionsProvider"/> + the views + ChatManager events
/// ONLY — no live-backend / messaging-API / web-request types. The mock is named on exactly ONE
/// line (Awake); Phase 2 swaps that single line for the live provider with zero other edits.
/// </summary>
public class SuggestionsController : MonoBehaviour
{
    [SerializeField] private SuggestionsPanel _panel;
    [SerializeField] private SemiAutoToggle _toggle;
    [SerializeField] private MessagesBottomPanel _bottomPanel;
    [SerializeField] private float _mockLatencySeconds = 1.0f;

    private ISuggestionsProvider _provider;
    private long _requestSeq;          // monotonic; newest wins (A6)
    private bool _semiAutoOn;

    void Awake()
    {
        _provider = new MockSuggestionsProvider(this, _mockLatencySeconds);   // 'this' = coroutine runner; ONLY mock reference
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected += HandleChatSelected;        // fires while this may be INACTIVE (Pitfall 3)
            ChatManager.Instance.OnActiveBotChanged += HandleBotChanged;
        }
        if (_toggle != null) _toggle.OnToggled += HandleToggle;
        if (_panel != null)
        {
            _panel.OnCardTapped += HandleCardTapped;
            _panel.OnRefreshRequested += HandleManualRefresh;
        }
    }

    void OnDestroy()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected -= HandleChatSelected;
            ChatManager.Instance.OnActiveBotChanged -= HandleBotChanged;
        }
        if (_toggle != null) _toggle.OnToggled -= HandleToggle;
        if (_panel != null)
        {
            _panel.OnCardTapped -= HandleCardTapped;
            _panel.OnRefreshRequested -= HandleManualRefresh;
        }
    }

    void OnEnable()
    {
        if (ChatManager.Instance != null) ChatManager.Instance.OnLiveMessagesReceived += HandleLive;   // active-only
    }

    void OnDisable()
    {
        _requestSeq++;                                         // supersede in-flight requests on deactivate (Render guards on this)
        if (ChatManager.Instance != null) ChatManager.Instance.OnLiveMessagesReceived -= HandleLive;
    }

    // --- State restore on chat-open / bot-switch (SEMI-02/SEMI-03) ---

    private void HandleChatSelected(string chatId) => RestoreForActiveChat();
    private void HandleBotChanged(string botId) => ResetForNoOpenChat();

    // After a bot switch no chat is open (ChatManager.SetActiveBot clears the chat list) and
    // CurrentChatId is sticky to the PREVIOUS bot's chat — do NOT restore against it. Drop to
    // OFF/hidden; HandleChatSelected restores real per-chat state when a chat is opened.
    private void ResetForNoOpenChat()
    {
        _semiAutoOn = false;
        _requestSeq++;                                        // supersede any in-flight request
        if (_toggle != null) _toggle.SetLit(false);
        if (_panel != null) _panel.Hide();
    }

    private void RestoreForActiveChat()
    {
        if (ChatManager.Instance == null) return;
        _semiAutoOn = SemiAutoStore.IsOn(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId);
        if (_toggle != null) _toggle.SetLit(_semiAutoOn);     // default OFF → other chats stay manual (SEMI-03)
        if (_semiAutoOn)
        {
            if (_panel != null) _panel.Show();
            IssueRequest(null, null);
        }
        else if (_panel != null) _panel.Hide();
    }

    // --- Toggle on/off (SEMI-01 / D-08/09/10/11) ---

    private void HandleToggle(bool desiredOn)
    {
        if (ChatManager.Instance == null) return;
        _semiAutoOn = desiredOn;
        SemiAutoStore.Set(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId, desiredOn);   // persist
        if (_toggle != null) _toggle.SetLit(desiredOn);
        if (desiredOn)
        {
            if (_panel != null) _panel.Show();
            IssueRequest(null, null);                          // first set on turn-on
        }
        else
        {
            _requestSeq++;                                     // supersede any in-flight request — no late render
            if (_panel != null) _panel.Hide();                // D-11: off = hide; composer untouched
        }
    }

    // --- Issue + guard (DATA-03 — capture seq + chat, discard superseded/chat-switched) ---

    private void IssueRequest(string steerTowardText, string lastIncomingText)
    {
        if (ChatManager.Instance == null || _provider == null) return;
        long seq = ++_requestSeq;                              // newest wins
        string chatId = ChatManager.Instance.CurrentChatId;
        if (_panel != null) _panel.ShowSkeleton();            // D-12: skeleton EVERY load
        var req = new SuggestionRequest
        {
            chatId = chatId,
            steerTowardText = steerTowardText,
            lastIncomingText = lastIncomingText,
            requestSeq = seq
        };
        _provider.Request(req, result => OnResult(seq, chatId, result));
    }

    private void OnResult(long seq, string capturedChatId, SuggestionResult result)
    {
        if (!_semiAutoOn) return;                              // user opted out mid-flight → never render
        string currentChatId = ChatManager.Instance != null ? ChatManager.Instance.CurrentChatId : null;
        if (!SuggestionSequenceGuard.IsCurrent(seq, _requestSeq, capturedChatId, currentChatId))
            return;                                            // superseded / chat switched → DISCARD
        if (_panel != null) _panel.Render(result);            // skeleton → cards | empty | error
    }

    // --- Card tap (INT-01 + INT-04 unified, D-01/D-02/D-03) ---

    private void HandleCardTapped(string replyText)
    {
        if (_bottomPanel != null && _bottomPanel.inputField != null)
        {
            _bottomPanel.inputField.text = replyText;          // OVERWRITE composer (deliberate, D-02)
            _bottomPanel.inputField.ActivateInputField();      // focus for edit
        }
        IssueRequest(steerTowardText: replyText, lastIncomingText: null);   // re-cluster toward the pick (INT-04/D-01)
        // NEVER auto-send — only the existing composer Send button delivers a message (D-03).
    }

    // --- Auto-populate on incoming (INT-02, incoming-only, NEVER writes composer — Pitfall 7) ---

    private void HandleLive(List<MessageViewModel> msgs)
    {
        if (!_semiAutoOn) return;                              // SEMI-03
        if (msgs == null || !msgs.Exists(m => m != null && m.isIncoming)) return;   // ignore outgoing echoes (Pitfall 7)
        IssueRequest(steerTowardText: null, lastIncomingText: LastIncomingText(msgs));   // refreshes CARDS ONLY (INT-02)
    }

    private static string LastIncomingText(List<MessageViewModel> msgs)
    {
        for (int i = msgs.Count - 1; i >= 0; i--)
            if (msgs[i] != null && msgs[i].isIncoming) return msgs[i].text;
        return null;
    }

    // --- Manual refresh (INT-03) ---

    private void HandleManualRefresh()
    {
        if (_semiAutoOn) IssueRequest(steerTowardText: null, lastIncomingText: null);
    }
}
