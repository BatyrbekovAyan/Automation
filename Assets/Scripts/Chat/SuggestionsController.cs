using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

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
    [SerializeField] private KeyboardAwarePanel _keyboardMover;  // MovingArea rider — owns the bottom inset the slot swap drives
    [SerializeField] private ComposerSlotKey _slotKey;           // ✦⇄⌨ key inside the composer field (sketch-003 A)
    // NOTE: the old _mockLatencySeconds knob is gone — MockSuggestionsProvider has not been
    // constructed here since the Phase-2 swap to N8nSuggestionsProvider (it owns its own latency
    // default), so the field was dead inspector surface.

    private ISuggestionsProvider _provider;
    private long _requestSeq;          // monotonic; newest wins (A6)
    private bool _semiAutoOn;

    // Keyboard-slot tenancy. Chassis = sketch-003 A (the panel is a tenant of the keyboard's slot);
    // interaction model = sketch-005 E. _slotState is the INTENT — the panel view can be
    // active-but-covered while the native keyboard slides over it — and it is the ONLY place that
    // can tell «the owner collapsed the slot» (Collapsed) apart from «there is no panel here»
    // («Авто», no chat), a distinction the old _sheetOpen bool could not express and which rules
    // 3/4/5/9 all key off. Every intent goes through SuggestionSlotStateMachine.Resolve.
    // _yieldingToKeyboard bridges the handoff where the panel has conceded the slot but the held
    // inset must not drop until the keyboard actually arrives (SuggestionSlotSwap.ShouldReleaseHold
    // — the no-dip rule). _pendingShow parks a show that found the «+» attach sheet owning the slot.
    private SuggestionSlotState _slotState = SuggestionSlotState.Collapsed;
    private bool _yieldingToKeyboard;
    private float _yieldStartedAt;
    private bool _kbWasVisible;
    private bool _pendingShow;
    private float _slotCanvasPx;
    private Tweener _insetTween;       // slot open/close inset animation (handoffs never tween — they hold)

    // The panel holds the slot in both of its up states; the difference is only how tall it is.
    private bool PanelOwnsSlot =>
        _slotState == SuggestionSlotState.Panel || _slotState == SuggestionSlotState.Expanded;

    // --- Drag handle (model E rule 6) ---
    // The handle reports a PROPOSED height; this controller owns the inset, the detent snap and the
    // smoothing bypass. _draggingSlot suppresses every automatic tenancy decision for the length of
    // the gesture — the swap watcher must not reinterpret a finger-driven inset as a keyboard claim.
    [SerializeField] private SuggestionSlotDragHandle _dragHandle;
    private bool _draggingSlot;
    private ScrollTopInsetCompensator _threadInset;   // live rest geometry for the Expanded cap
    // Captured ONCE per gesture: the handle polls the ceiling every drag frame, and a ceiling that
    // shrank mid-drag (cards re-rendering under the finger) would yank the slot down with it.
    private float _dragCeilingCanvasPx;

    // Last measured live keyboard height. The return handoff (the keyboard leaves, the panel takes
    // the slot back) fires on the frame the keyboard is ALREADY gone, so there is nothing left to
    // read — without this the inset would have to tween up from 0 and the composer would dip a full
    // slot height first, the exact no-dip violation the opposite direction is careful to avoid.
    private float _lastKeyboardCanvasPx;

    // --- Thread tap (model E rule 5) ---
    // The Canvas, not its scaleFactor: CanvasScaler writes the real factor from its own OnEnable, so
    // a value latched in Awake is the serialised default of 1 and every tap would be measured at a
    // third of its true length on a 3x device.
    private Canvas _rootCanvas;
    private bool _threadPressActive;
    private Vector2 _threadPressPos;
    private bool _threadPressEligible;
    private readonly List<RaycastResult> _tapHits = new List<RaycastResult>();   // reused; no GC per press
    private PointerEventData _tapEventData;                                      // ditto — built once

    // BATCH-03 debounce: HandleLive pokes the gate; a single self-gating loop fires ONE coalesced
    // request when the ~2.5s window settles. The window is cancelled + _pendingIncomingText cleared at
    // all four lifecycle sites (close / bot switch / same-bot chat switch / toggle-off) so a pending
    // fire can never carry the wrong chat's fragment (the seq guard catches a chat-switched RENDER,
    // but NOT a stale lastIncomingText baked into the request payload at fire time).
    private readonly IncomingDebounceGate _debounce = new IncomingDebounceGate();
    private string _pendingIncomingText;   // latest incoming text captured for the eventual coalesced fire
    private Coroutine _debounceLoop;

    // Audit F9: last rendered set per chat, keyed by the history tail. A re-open with an
    // unmoved tail renders instantly and issues NO request; any tail drift = miss = today's
    // skeleton+request path. Cleared on bot switch (chat ids recur across bots).
    private readonly SuggestionCache _cache = new SuggestionCache();

    // Flow decision 2026-08-11: an ANSWERED run fully hides the sheet, leaving the pre-send
    // set rendered but stale. This latch makes the next manual ✦ open regenerate instead of
    // showing those stale cards; any IssueRequest clears it (a fresh set is on its way).
    private bool _answeredIdle;

    // Rounds flow (2026-08-11): each pick steers the NEXT set toward the chosen card; the
    // stack remembers the rounds being left so ‹ restores them instantly (no LLM call, no
    // skeleton, composer untouched). _currentSteer is the direction that produced the round
    // ON SCREEN (null = fresh set) — refresh re-rolls it; _currentRendered is the last Ok
    // set, i.e. what Push records when a pick moves forward.
    private readonly SuggestionRoundStack _rounds = new SuggestionRoundStack();
    private string _currentSteer;
    private string _currentHeader;   // display title of the round ON SCREEN (null = default «ПРЕДЛОЖЕНИЯ»)
    private SuggestionResult _currentRendered;

    void Awake()
    {
        _provider = new N8nSuggestionsProvider();   // Phase-2 live provider (N8N-02 single-line swap); coroutine runs on ChatManager.Instance
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
            _panel.OnBackRequested += HandleBack;
        }
        if (_slotKey != null) _slotKey.Tapped += HandleSlotKeyTapped;

        // Model E rule 6: the handle owns pointer math only — grab height, ceiling, snapping and
        // the inset all live here.
        if (_dragHandle != null)
        {
            _dragHandle.HeightProvider = () => _keyboardMover != null ? _keyboardMover.AppliedBottomInset : 0f;
            _dragHandle.MaxHeightProvider = () => _dragCeilingCanvasPx;
            _dragHandle.Grabbed += HandleDragGrabbed;
            _dragHandle.Dragged += HandleDragMoved;
            _dragHandle.Released += HandleDragReleased;
        }

        // Model E rule 4: a tap on the composer while the slot is COLLAPSED raises the panel
        // instead of focusing the field. The veto is installed on THIS field only — every other
        // input in the project keeps stock TMP behaviour.
        var composer = _bottomPanel != null ? _bottomPanel.inputField as DeferredDismissInputField : null;
        if (composer != null)
        {
            composer.ActivationVeto = ShouldVetoComposerActivation;
            composer.ActivationVetoed += HandleComposerActivationVetoed;
        }

        _threadInset = _keyboardMover != null
            ? _keyboardMover.GetComponentInChildren<ScrollTopInsetCompensator>(true)
            : null;

        var canvas = GetComponentInParent<Canvas>();
        _rootCanvas = canvas != null ? canvas.rootCanvas : null;
    }

    /// <summary>Screen px → canvas units, read live (see <see cref="_rootCanvas"/>).</summary>
    private float CanvasScale =>
        _rootCanvas != null && _rootCanvas.scaleFactor > 0f ? _rootCanvas.scaleFactor : 1f;

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
            _panel.OnBackRequested -= HandleBack;
        }
        if (_slotKey != null) _slotKey.Tapped -= HandleSlotKeyTapped;
        if (_dragHandle != null)
        {
            _dragHandle.Grabbed -= HandleDragGrabbed;
            _dragHandle.Dragged -= HandleDragMoved;
            _dragHandle.Released -= HandleDragReleased;
            _dragHandle.HeightProvider = null;
            _dragHandle.MaxHeightProvider = null;
        }
        var composer = _bottomPanel != null ? _bottomPanel.inputField as DeferredDismissInputField : null;
        if (composer != null)
        {
            composer.ActivationVeto = null;                       // never outlive this controller
            composer.ActivationVetoed -= HandleComposerActivationVetoed;
        }
    }

    void OnEnable()
    {
        if (ChatManager.Instance != null) ChatManager.Instance.OnLiveMessagesReceived += HandleLive;   // active-only
        _debounceLoop = StartCoroutine(DebounceLoop());       // one always-running self-gating fire loop (BATCH-03)
    }

    void OnDisable()
    {
        _requestSeq++;                                         // supersede in-flight requests on deactivate (Render guards on this)
        _debounce.Cancel();                                   // chat close: drop any pending coalesced fire (BATCH-03)
        _pendingIncomingText = null;                          // ...and its stale text, so it can never land later
        if (_debounceLoop != null) { StopCoroutine(_debounceLoop); _debounceLoop = null; }
        _insetTween?.Kill();
        _insetTween = null;
        // Chat screen closing mid-tenancy: drop the slot claim so the MovingArea settles back to
        // rest for the next chat, and reset the swap state with it.
        if (_keyboardMover != null) _keyboardMover.VirtualBottomInset = 0f;
        _slotState = SuggestionSlotState.Collapsed;
        _yieldingToKeyboard = false;
        _pendingShow = false;
        _draggingSlot = false;
        _threadPressActive = false;      // a press in flight when the chat closed must not
        _threadPressEligible = false;    // resolve into a tap on the next screen
        if (_keyboardMover != null) _keyboardMover.TrackInsetImmediately = false;   // a drag cut short by the chat closing
        if (_panel != null) _panel.Deactivate();
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
        _debounce.Cancel();                                  // bot switch: drop a window pending from the previous bot's chat (BATCH-03)
        _pendingIncomingText = null;
        _answeredIdle = false;
        StartFreshRound();
        _cache.Clear();                                      // chat ids recur across bots — entries must not outlive the bot (F9)
        if (_toggle != null) _toggle.SetLit(false);
        if (_slotKey != null) _slotKey.SetVisible(false);
        HidePanel();
    }

    private void RestoreForActiveChat()
    {
        if (ChatManager.Instance == null) return;
        // A same-bot chat switch fires this on EVERY OnChatSelected; a window still pending from the
        // PREVIOUS chat must NOT fire into this one (its _pendingIncomingText is chat A's fragment while
        // CurrentChatId is now chat B — a mixed-context call the seq guard cannot catch). Drop it. (BATCH-03)
        _debounce.Cancel();
        _pendingIncomingText = null;
        _answeredIdle = false;   // per-chat latch — never carries into another chat's open
        _pendingShow = false;    // a parked auto-show must not leak into another chat's open
        StartFreshRound();       // rounds are per-question, never per-app — a chat open starts at round 1
        _semiAutoOn = SemiAutoStore.IsOn(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId);
        if (_toggle != null) _toggle.SetLit(_semiAutoOn);     // default OFF → other chats stay manual (SEMI-03)
        // SUP-02 heal: re-assert only an EXPLICIT per-chat override (tri-state 1/2) — covers a lost
        // «Вместе» (true) AND a lost «back to Авто» (false) write. Inherited chats (raw 0) push
        // NOTHING: they rely on the '*' bot-default row alone; writing the collapsed boolean here
        // would turn a mere chat-open into a sticky per-chat server row (WR-01).
        if (SemiAutoStore.TryGetOverride(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId, out bool overrideOn))
            PushReplyModeForActiveChat(overrideOn);
        if (_slotKey != null) _slotKey.SetVisible(_semiAutoOn);
        if (_semiAutoOn)
        {
            ShowPanel(claimSlotFromKeyboard: false);
            // F9: an unmoved history tail renders the cached set instantly — no skeleton, no
            // paid call. Any drift (new message, owner reply, first visit) = miss = fresh request.
            if (!TryRenderCached()) IssueRequest(null, null);
        }
        else HidePanel();
    }

    // Renders the cached set for the open chat when its tail key still matches. False = issue.
    private bool TryRenderCached()
    {
        var cm = ChatManager.Instance;
        if (cm == null || _panel == null) return false;
        string tailKey = CurrentTailKey();
        if (tailKey == null || !_cache.TryGet(cm.CurrentChatId, tailKey, out var cached)) return false;
        _panel.Render(cached);
        return true;
    }

    // Tail identity of the OPEN chat's freshest message, or null when there is none to key on.
    private static string CurrentTailKey()
    {
        var cm = ChatManager.Instance;
        if (cm == null) return null;
        if (!cm.TryGetRecentMessages(cm.CurrentChatId, 1, out var tail) || tail == null || tail.Count == 0)
            return null;
        return SuggestionCache.TailKey(tail[tail.Count - 1]);
    }

    // --- Toggle on/off (SEMI-01 / D-08/09/10/11) ---

    private void HandleToggle(bool desiredOn)
    {
        if (ChatManager.Instance == null) return;
        _semiAutoOn = desiredOn;
        SemiAutoStore.Set(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId, desiredOn);   // persist
        PushReplyModeForActiveChat(desiredOn);                 // SUP-02: mirror the per-chat override (ON and OFF) to the server
        if (_toggle != null) _toggle.SetLit(desiredOn);
        if (_slotKey != null) _slotKey.SetVisible(desiredOn);
        if (desiredOn)
        {
            ShowPanel(claimSlotFromKeyboard: false);           // gentle: an open keyboard keeps the slot until it closes
            StartFreshRound();                                 // explicit turn-on = round 1
            IssueRequest(null, null);                          // first set on turn-on
        }
        else
        {
            _requestSeq++;                                     // supersede any in-flight request — no late render
            _debounce.Cancel();                                // toggle-OFF: a stale window must not survive to fire after a later toggle-ON (BATCH-03)
            _pendingIncomingText = null;
            _pendingShow = false;
            HidePanel();                                       // D-11: off = hide; composer untouched
        }
    }

    // --- Server sync of the per-chat override (SUP-02, client half) ---
    // Fire-and-forget write of the active chat's suppression flag for the ACTIVE channel's
    // profile. Called from HandleToggle (explicit ON/OFF) and RestoreForActiveChat (explicit-
    // override heal, both states — inherited chats push nothing, WR-01).
    // NEVER from HandleLive — the 3s open-chat LivePoll would storm the server (Pitfall 3).
    private void PushReplyModeForActiveChat(bool suppressed)
    {
        var cm = ChatManager.Instance;
        if (cm == null || Manager.Instance == null) return;
        Bot bot = Manager.Instance.FindBotByName(cm.CurrentBotId);
        if (bot == null) return;
        string profileId = cm.ActiveChannelProfileId();       // C3 accessor, wraps GetActiveProfileId()
        if (string.IsNullOrEmpty(profileId) || profileId == Bot.UnauthedProfileSentinel) return;
        Manager.Instance.SyncReplyMode(new[] { profileId }, cm.CurrentChatId, suppressed);
    }

    // --- Issue + guard (DATA-03 — capture seq + chat, discard superseded/chat-switched) ---

    private void IssueRequest(string steerTowardText, string lastIncomingText)
    {
        if (ChatManager.Instance == null || _provider == null) return;
        _answeredIdle = false;                                 // a fresh set is on its way — stale latch off
        long seq = ++_requestSeq;                              // newest wins (also supersedes any in-flight)
        string chatId = ChatManager.Instance.CurrentChatId;
        if (string.IsNullOrEmpty(chatId)) return;             // no open chat → nothing to scope a request to (WR-02)
        if (_panel != null) _panel.ShowSkeleton();            // D-12: skeleton EVERY load
        var req = new SuggestionRequest
        {
            chatId = chatId,
            steerTowardText = steerTowardText,
            lastIncomingText = lastIncomingText,
            requestSeq = seq
        };
        // F9 capture-at-issue: the tail this request answers. Verified again at store time —
        // either drift direction degrades to a cache miss, never to stale cards.
        string tailKey = CurrentTailKey();
        bool freshSet = steerTowardText == null;   // only round-1 sets are cache-worthy
        _provider.Request(req, result => OnResult(seq, chatId, tailKey, freshSet, result));
    }

    private void OnResult(long seq, string capturedChatId, string capturedTailKey, bool freshSet, SuggestionResult result)
    {
        if (!_semiAutoOn) return;                              // user opted out mid-flight → never render
        string currentChatId = ChatManager.Instance != null ? ChatManager.Instance.CurrentChatId : null;
        if (!SuggestionSequenceGuard.IsCurrent(seq, _requestSeq, capturedChatId, currentChatId))
            return;                                            // superseded / chat switched → DISCARD
        if (_panel != null) _panel.Render(result);            // skeleton → cards | empty | error
        // Rounds: only an Ok set becomes the restorable "current round" — an error/empty render
        // leaves _currentRendered on the last good set, so a pick-after-retry still pushes it.
        if (result != null && result.status == SuggestionStatus.Ok) _currentRendered = result;
        // F9 verify-at-store, narrowed by the drill flow (2026-08-18): only FRESH sets are
        // cached — a re-opened chat must render a round-1 set under the default header, never
        // a mid-drill set whose steer/back context is gone. Tail drift still degrades to a
        // cache miss, never to stale cards.
        if (freshSet && capturedTailKey != null && capturedTailKey == CurrentTailKey())
            _cache.Store(capturedChatId, capturedTailKey, result);
    }

    // --- Card tap (INT-01 + INT-04 unified, D-01/D-02/D-03) ---

    private void HandleCardTapped(string replyText)
    {
        if (_bottomPanel != null && _bottomPanel.inputField != null)
            StartCoroutine(WriteComposerRoutine(_bottomPanel.inputField, replyText));
        // Rounds flow: record the round being left (cards + steer + header) so ‹ restores it
        // locally, remember the new direction for refresh re-rolls, retitle the header to the
        // picked card's title (drill flow 2026-08-18), and count the pick's MOVE.
        SuggestionItem picked = FindRenderedItem(replyText);
        _rounds.Push(_currentRendered, _currentSteer, _currentHeader);
        _currentSteer = replyText;
        if (picked != null) _currentHeader = picked.intentLabel;
        if (_panel != null) _panel.SetHeaderTitle(_currentHeader);
        RecordPick(picked);
        UpdateBackUi();
        IssueRequest(steerTowardText: replyText, lastIncomingText: null);   // next round drills into the pick (INT-04/D-01)
        // NEVER auto-send — only the existing composer Send button delivers a message (D-03).
        // The sheet stays open on a pick; it hides on the OUTGOING echo (flow decision 2026-08-11).
    }

    // The tap event carries only the text; texts within one set are distinct by generation.
    private SuggestionItem FindRenderedItem(string replyText)
    {
        if (_currentRendered?.items == null) return null;
        foreach (var item in _currentRendered.items)
            if (item != null && item.text == replyText) return item;
        return null;
    }

    // ‹ pressed: restore the previous round's cards INSTANTLY — no LLM call, no skeleton.
    // The seq bump kills any in-flight forward/refresh result so it cannot overwrite the
    // restored round. The composer is deliberately untouched: back changes the CARDS only
    // (an owner edit in the composer must never be destroyed by navigation).
    private void HandleBack()
    {
        if (!_semiAutoOn) return;
        if (!_rounds.TryPop(out SuggestionResult previous, out string previousSteer, out string previousHeader)) return;
        _requestSeq++;
        _currentSteer = previousSteer;
        _currentHeader = previousHeader;
        _currentRendered = previous;
        if (_panel != null)
        {
            _panel.Render(previous);
            _panel.SetHeaderTitle(previousHeader);
        }
        UpdateBackUi();
    }

    // Preference learning v1 under the drill redesign: count the picked card's internal MOVE
    // (server field since 2026-08-18); a legacy server without `move` still counts when the
    // display label IS one of the 6 moves (the pre-redesign contract). Free-form titles never
    // mint PlayerPrefs keys — the counter namespace must stay the closed taxonomy.
    private void RecordPick(SuggestionItem picked)
    {
        if (ChatManager.Instance == null) return;
        string botName = ChatManager.Instance.CurrentBotId;
        string move = ResolvePickStatsMove(picked);
        if (string.IsNullOrEmpty(botName) || move == null) return;
        string key = botName + "SuggestPick" + move;
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence)
    }

    /// <summary>Pure pick-stats resolution: the item's move when valid, else its label if
    /// that label IS a move (legacy server), else null (record nothing). EditMode-tested.</summary>
    public static string ResolvePickStatsMove(SuggestionItem picked)
    {
        if (picked == null) return null;
        if (SuggestionMoves.IsMove(picked.move)) return picked.move;
        if (SuggestionMoves.IsMove(picked.intentLabel)) return picked.intentLabel;
        return null;
    }

    // A fresh round 1: new incoming, chat/bot switch, explicit toggle-on, answered run.
    private void StartFreshRound()
    {
        _rounds.Clear();
        _currentSteer = null;
        _currentHeader = null;
        if (_panel != null) _panel.SetHeaderTitle(null);   // back to the default «ПРЕДЛОЖЕНИЯ»
        UpdateBackUi();
    }

    private void UpdateBackUi()
    {
        if (_panel != null) _panel.SetBackVisible(_rounds.CanGoBack);
    }

    // Audit F14 + flow decision 2026-08-11. iOS shares ONE native keyboard buffer: writing
    // .text into a still-FOCUSED TMP field round-trips through it and lands wrong (input
    // invariants; same ordering as BotSettings.Prompts' MutatePromptRoutine — blur, let the
    // release land, THEN write). A pick must NOT open the keyboard: send-as-is is tap-card →
    // tap-Send with the thread visible; editing starts by tapping the composer, where TMP's
    // own pointer path seats the caret and native buffer. Only a field that was ALREADY in
    // edit mode gets its focus restored — and then the programmatic caret must sync via
    // KeyboardSelectionSync.Push (TextSelection invariant #1).
    private IEnumerator WriteComposerRoutine(TMPro.TMP_InputField field, string text)
    {
        bool wasFocused = field.isFocused;
        if (wasFocused)
        {
            field.DeactivateInputField();
            yield return null;                       // let the release land before touching .text
        }
        if (field == null) yield break;              // chat closed under us mid-frame
        field.text = text;                           // OVERWRITE composer (deliberate, D-02)
        PulseSendButton();                           // after the write: the button exists once text is in
        if (!wasFocused) yield break;                // keyboard stays CLOSED — the pick is not an edit
        field.ActivateInputField();                  // owner was mid-edit — keep them in edit mode
        yield return null;                           // activation is a promise — focus lands end-of-frame
        if (field == null || !field.isFocused) yield break;
        field.caretPosition = field.text.Length;     // deterministic caret at end
        KeyboardSelectionSync.Push(field);
    }

    // The send-as-is path is two taps (card → Send); the pulse advertises the second one.
    private void PulseSendButton()
    {
        var send = _bottomPanel != null ? _bottomPanel.sendButton : null;
        if (send == null || !send.gameObject.activeInHierarchy) return;
        send.transform.DOKill();
        send.transform.localScale = Vector3.one;
        send.transform.DOPunchScale(Vector3.one * 0.12f, 0.35f, 6, 0.6f);
    }

    // --- Auto-populate on incoming (INT-02, incoming-only, NEVER writes composer — Pitfall 7) ---

    private void HandleLive(List<MessageViewModel> msgs)
    {
        if (!_semiAutoOn) return;                              // SEMI-03
        var fold = FoldLiveBatch(_pendingIncomingText, msgs);
        _pendingIncomingText = fold.Pending;
        // The owner (or bot) replied: a request already in flight was issued for a burst that is
        // now answered — supersede it so its cards can never render post-answer (audit F11).
        if (fold.SawOutgoing) _requestSeq++;
        if (fold.Cancel)
        {
            _debounce.Cancel();
            // Flow decision 2026-08-11: the batch ended ANSWERED with nothing re-armed — the
            // chat is quiet, so the sheet fully hides. (A reply followed by a NEW question in
            // the same batch re-arms instead: Cancel is false and the sheet stays for the
            // fresh set.) The next incoming re-shows it at the debounce fire.
            _answeredIdle = true;
            StartFreshRound();   // the question was answered — its refinement rounds are over
            // Model E: the answered run lands on COLLAPSED specifically — that is the one state
            // rule 9's auto-raise fires from, so the next incoming message brings the panel back.
            ApplySlotInput(SuggestionSlotInput.AnsweredRun);
        }
        // UNSCALED wall clock, matching DebounceLoop's WaitForSecondsRealtime tick and the
        // ChatManager poll idiom: Time.time is maximumDeltaTime-capped (so a frame hitch or an
        // app resume silently stretches the window) and stops entirely at timeScale 0.
        if (fold.Arm) _debounce.Poke(Time.realtimeSinceStartup);   // reset the ~2.5s window instead of firing per-fragment (BATCH-03)
    }

    /// <summary>Outcome of folding one live batch over the pending burst: the new pending text, and
    /// whether the coalesce window should be armed or dropped.</summary>
    public readonly struct LiveBatchFold
    {
        public readonly string Pending;
        public readonly bool Arm;         // an incoming fragment survived the batch → (re)start the window
        public readonly bool Cancel;      // the batch ended on an outgoing echo → drop any armed window
        public readonly bool SawOutgoing; // ANY outgoing echo in the batch → supersede in-flight requests (audit F11)

        public LiveBatchFold(string pending, bool arm, bool cancel, bool sawOutgoing)
        {
            Pending = pending;
            Arm = arm;
            Cancel = cancel;
            SawOutgoing = sawOutgoing;
        }
    }

    /// <summary>Pure fold of ONE live batch over the pending burst (BATCH-03), in arrival order.
    ///
    /// Incoming fragments accumulate (see <see cref="AppendBurst"/>). An OUTGOING echo — the owner or
    /// the bot replied — is the run BOUNDARY: it drops the pending burst and disarms, because
    /// suggestions for an already-answered burst are noise. Incoming fragments arriving after that
    /// echo start a fresh run.
    ///
    /// This is the counterpart to the deliberate NON-clear in <see cref="DebounceLoop"/>: the pending
    /// text must SURVIVE a fire (a burst straddling the window fires twice and the second fire still
    /// has to carry the earlier fragments), so the ONLY places it clears are this boundary and the four
    /// lifecycle sites. Keeping the rule here — pure and clock-free — is what makes it testable.</summary>
    public static LiveBatchFold FoldLiveBatch(string pending, IReadOnlyList<MessageViewModel> batch)
    {
        if (batch == null) return new LiveBatchFold(pending, false, false, false);
        bool arm = false;
        bool sawOutgoing = false;
        for (int i = 0; i < batch.Count; i++)
        {
            var m = batch[i];
            if (m == null) continue;
            if (m.isIncoming)
            {
                pending = AppendBurst(pending, m.text);
                arm = true;
            }
            else
            {
                pending = null;
                arm = false;               // a later incoming in the same batch re-arms below
                sawOutgoing = true;
            }
        }
        // Cancel only matters when nothing re-armed: Poke() re-arms unconditionally, so a
        // Cancel-then-Poke pair would be indistinguishable from a bare Poke. SawOutgoing is
        // reported raw — the supersede signal must survive a re-arm (audit F11).
        return new LiveBatchFold(pending, arm, !arm && sawOutgoing, sawOutgoing);
    }

    /// <summary>Pure burst accumulator: append an incoming fragment to the pending coalesced text
    /// unless it is already the tail line (live-poll re-delivery guard). The WHOLE burst — not just
    /// the last fragment — must ride the request's lastIncomingText, because the payload's history
    /// snapshot re-syncs on chat fetch, not on live poll, so it can lag behind the burst indefinitely
    /// and the server-side run-walk cannot recover fragments that are in neither place.</summary>
    public static string AppendBurst(string pending, string fragment)
    {
        if (string.IsNullOrEmpty(fragment)) return pending;
        if (string.IsNullOrEmpty(pending)) return fragment;
        if (pending == fragment || pending.EndsWith("\n" + fragment)) return pending;   // re-delivered fragment
        return pending + "\n" + fragment;
    }

    // One always-running self-gating loop (mirrors ChatManager.LivePoll): polls the debounce gate a
    // few times a second and, when the ~2.5s window has settled, fires the SINGLE coalesced request
    // with the captured incoming text. Started in OnEnable, stopped in OnDisable. Manual refresh and
    // card-pick never come through here — they call IssueRequest directly (INT-03/INT-04, immediate).
    private IEnumerator DebounceLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(0.25f);   // fresh instance each loop (codebase idiom)
            if (!_semiAutoOn) continue;                       // cheap guard; do not fire when off
            if (_debounce.ShouldFire(Time.realtimeSinceStartup))   // SAME clock as Poke (unscaled, matches this loop's tick)
            {
                // A new incoming after an answered-and-hidden run re-opens the sheet HERE, at the
                // fire — arriving together with the skeleton, never flashing the stale pre-send
                // set for the window's 2.5s (flow decision 2026-08-11). Never steals the slot
                // from an open keyboard — ShowPanel parks the show until the keyboard leaves.
                // Model E rule 9: an incoming message auto-raises the panel ONLY from Collapsed.
                // While the owner is typing nothing moves, and an already-open panel just gets the
                // fresh set rendered into it by the IssueRequest below.
                ApplySlotInput(SuggestionSlotInput.IncomingMessage);
                StartFreshRound();   // a new client message is a NEW question — round 1 again
                // NOTE: _pendingIncomingText deliberately survives the fire — it mirrors the
                // UN-REPLIED trailing run, and a burst that straddles the window fires twice; the
                // second fire must still carry the earlier fragments (the payload's history snapshot
                // lags live messages, and the server dedups re-sent lines — observed live: exec 1168
                // lost the roses question when the fire cleared it). It clears only at a run
                // boundary: an outgoing reply (HandleLive) or the four lifecycle cancel sites.
                IssueRequest(steerTowardText: null, lastIncomingText: _pendingIncomingText);   // coalesced fire (INT-02)
            }
        }
    }

    // --- Keyboard-slot tenancy (sketch-003 variant A) -----------------------
    // The bottom slot has exactly one tenant: the native keyboard or the suggestions panel.
    // The panel sits at the screen bottom OUTSIDE the MovingArea; opening it raises the
    // MovingArea by the slot height via KeyboardAwarePanel.VirtualBottomInset — exactly what
    // the keyboard does — and LateUpdate glues the panel's top edge to the composer's bottom.
    // Swaps hold the LARGER claim until the incoming tenant arrives (the no-dip rule,
    // SuggestionSlotSwap), so the composer and thread never move during a handoff.

    /// <summary>
    /// Open the slot for the panel at a detent. Only the «+» attach sheet still PARKS a show
    /// (it is a third tenant the model never mentions and it must not be stolen from); the
    /// keyboard needs no parking any more, because model E returns the slot to the panel on
    /// every blur (KeyboardDismissed in <see cref="Update"/>) — so a show that arrives while the
    /// owner is typing simply does not happen, rather than queueing behind them.
    /// <paramref name="claimSlotFromKeyboard"/> is the explicit ask (the ⌨/✦ key): it dismisses
    /// the keyboard and takes the slot at the keyboard's own height, never moving the composer.
    /// </summary>
    private void ShowPanel(bool claimSlotFromKeyboard, SlotDetent detent = SlotDetent.Standard)
    {
        if (_panel == null) return;
        bool kbVisible = _keyboardMover != null && _keyboardMover.NativeKeyboardVisible;
        if ((kbVisible || AttachOpen) && !claimSlotFromKeyboard)
        {
            // Never steal the slot from a typing owner or an open «+» sheet. The park lands from
            // Update the moment the other tenant leaves. (Rule 2's automatic return goes through
            // the same branch, so an auto-raise arriving mid-typing simply waits its turn.)
            _pendingShow = true;
            return;
        }
        // Explicit ask while the attach sheet is up: the sheet hands the slot over, like the keyboard.
        if (AttachOpen) _bottomPanel.AttachSheet.Close();

        float kbCanvas = _keyboardMover != null ? _keyboardMover.EffectiveAreaCanvasPx : 0f;
        float standard = SuggestionSlotSwap.SlotForOpen(kbVisible, kbCanvas, SuggestionSlotHeight.Remembered);
        // Over a LIVE keyboard the slot MUST equal that keyboard's height or the composer moves
        // mid-swap (SuggestionSlotSwap's invariant), so a handoff is always Standard — Expanded is
        // only ever reached by dragging the handle, which needs the panel to already own the slot.
        _slotCanvasPx = kbVisible ? standard : SuggestionSlotDetents.HeightFor(detent, standard, ExpandedDetent(standard));
        _panel.SetSlotMetrics(_slotCanvasPx, _keyboardMover != null ? _keyboardMover.SafeBottomCanvasPx : 0f);
        _panel.ShowInSlot();
        _slotState = kbVisible || detent != SlotDetent.Expanded
            ? SuggestionSlotState.Panel
            : SuggestionSlotState.Expanded;
        _yieldingToKeyboard = false;
        _pendingShow = false;
        _panel.SetFadeSuppressed(_slotState == SuggestionSlotState.Expanded);
        ApplyKeyStyle();

        _insetTween?.Kill();
        if (_keyboardMover == null) return;
        if (kbVisible)
        {
            // Handoff FROM the keyboard: hold the inset where the keyboard already has it and
            // dismiss — the composer must not move a pixel while the keyboard slides away.
            _keyboardMover.VirtualBottomInset = _slotCanvasPx;
            DismissComposerKeyboard();
        }
        else
        {
            _insetTween = DOTween.To(
                    () => _keyboardMover.VirtualBottomInset,
                    v => _keyboardMover.VirtualBottomInset = v,
                    _slotCanvasPx, 0.25f)
                .SetEase(Ease.OutCubic);
        }
    }

    /// <summary>
    /// Drop the slot to nothing. Model E calls this state Collapsed and it is a real tenant
    /// state, not merely "no panel": an incoming message may auto-raise from it (rule 9) and a
    /// tap anywhere brings the panel back — which is exactly why every caller that used to mean
    /// "the panel is gone" now records Collapsed rather than a bare hidden flag.
    /// </summary>
    private void HidePanel()
    {
        _slotState = SuggestionSlotState.Collapsed;
        _pendingShow = false;
        ApplyKeyStyle();
        if (_yieldingToKeyboard) return;   // the keyboard is mid-takeover — the yield watcher finishes the handoff
        _insetTween?.Kill();
        if (_keyboardMover != null && _keyboardMover.VirtualBottomInset > 0.5f)
        {
            _insetTween = DOTween.To(
                    () => _keyboardMover.VirtualBottomInset,
                    v => _keyboardMover.VirtualBottomInset = v,
                    0f, 0.20f)
                .SetEase(Ease.InCubic)
                .OnComplete(() => { if (_panel != null) _panel.Deactivate(); });
        }
        else if (_panel != null) _panel.Deactivate();
    }

    // --- Detents (model E rule 6) -------------------------------------------

    /// <summary>
    /// The third detent's height: the panel's own chrome plus its measured card stack, capped so a
    /// readable slice of thread survives. Both measurements come from the panel itself so the
    /// detent and the bottom fade can never disagree about "everything fits"; the cap is read from
    /// the thread's live rest geometry rather than a constant, because canvas height varies with
    /// the device aspect ratio.
    /// </summary>
    private float ExpandedDetent(float standardCanvasPx)
    {
        if (_panel == null) return standardCanvasPx;
        float threadRest = _threadInset != null ? _threadInset.RestViewportHeightCanvasPx : 0f;
        return SuggestionSlotDetents.ExpandedHeight(
            _panel.ChromeHeightCanvasPx, _panel.MeasuredContentHeight, standardCanvasPx, threadRest);
    }

    private float StandardDetent()
    {
        bool kbVisible = _keyboardMover != null && _keyboardMover.NativeKeyboardVisible;
        float kbCanvas = _keyboardMover != null ? _keyboardMover.EffectiveAreaCanvasPx : 0f;
        return SuggestionSlotSwap.SlotForOpen(kbVisible, kbCanvas, SuggestionSlotHeight.Remembered);
    }

    // --- Drag handle --------------------------------------------------------

    private void HandleDragGrabbed()
    {
        if (_keyboardMover == null || _panel == null) return;
        _draggingSlot = true;
        _insetTween?.Kill();                       // a tween and a finger must never write the inset together
        _dragCeilingCanvasPx = ExpandedDetent(StandardDetent());
        // 1:1 finger tracking: SmoothDamp would leave the panel trailing the drag, and a smoothed
        // inset lagging a SHRINKING slot breaks FollowInset's applied ≤ slot assumption.
        _keyboardMover.TrackInsetImmediately = true;
        _panel.SetFadeSuppressed(false);           // the fade is settled again on release
    }

    private void HandleDragMoved(float proposedCanvasPx)
    {
        if (!_draggingSlot || _keyboardMover == null || _panel == null) return;
        // Lockstep, both every frame: the panel's stored slot height and the applied inset.
        _panel.SetSlotHeightLive(proposedCanvasPx);
        _keyboardMover.VirtualBottomInset = proposedCanvasPx;
        if (!_panel.IsShown && proposedCanvasPx > 0.5f) _panel.ShowInSlot();
    }

    /// <summary>
    /// Abandon a drag without snapping to a detent — used when a modal takes the region out from
    /// under the finger. Deliberately does NOT touch the slot state or the inset: the caller's own
    /// eviction owns both, and a snap here would fight it. Releases the smoothing bypass, which is
    /// otherwise only cleared on a release that will now never be honoured.
    /// </summary>
    private void CancelSlotDrag()
    {
        _draggingSlot = false;
        if (_keyboardMover != null) _keyboardMover.TrackInsetImmediately = false;
    }

    private void HandleDragReleased(float finalCanvasPx)
    {
        if (!_draggingSlot) return;
        _draggingSlot = false;
        if (_keyboardMover != null) _keyboardMover.TrackInsetImmediately = false;

        float standard = StandardDetent();
        float expanded = _dragCeilingCanvasPx;
        SlotDetent snapped = SuggestionSlotDetents.Snap(finalCanvasPx, standard, expanded);
        _slotState = SuggestionSlotStateMachine.AfterDrag(snapped);
        ApplyKeyStyle();

        if (snapped == SlotDetent.Collapsed) { HidePanel(); return; }

        float target = SuggestionSlotDetents.HeightFor(snapped, standard, expanded);
        _slotCanvasPx = target;
        // Re-settle through the full metrics path so the fade re-measures at the new height.
        if (_panel != null)
        {
            _panel.SetSlotMetrics(target, _keyboardMover != null ? _keyboardMover.SafeBottomCanvasPx : 0f);
            _panel.SetFadeSuppressed(snapped == SlotDetent.Expanded);
        }
        if (_keyboardMover == null) return;
        _insetTween?.Kill();
        _insetTween = DOTween.To(
                () => _keyboardMover.VirtualBottomInset,
                v => _keyboardMover.VirtualBottomInset = v,
                target, 0.18f)
            .SetEase(Ease.OutCubic);
    }

    // --- Composer activation veto (model E rule 4) --------------------------

    /// <summary>
    /// The two-step entry: while the slot is COLLAPSED in «Вместе», a tap on the composer raises
    /// the panel instead of focusing the field — focusing there would open the keyboard underneath
    /// a panel still on its way up. Every other situation focuses normally, including all of «Авто».
    /// </summary>
    private bool ShouldVetoComposerActivation() =>
        _semiAutoOn && _slotState == SuggestionSlotState.Collapsed && !_draggingSlot;

    private void HandleComposerActivationVetoed() => ApplySlotInput(SuggestionSlotInput.FieldTap);

    // --- One place that turns an intent into slot motion --------------------

    /// <summary>
    /// Route an intent through the pure transition table and apply whatever it decided. This is
    /// the ONLY way the slot changes tenant outside the swap watcher, so the rules «a tap never
    /// hides an open panel» and «only a drag collapses» hold by construction rather than by every
    /// call site remembering them.
    /// </summary>
    private void ApplySlotInput(SuggestionSlotInput input)
    {
        // The finger owns the slot for the length of a gesture. DebounceLoop and HandleLive fire from
        // outside Update (the chat poll), and either would start a DOTween on the very inset
        // HandleDragMoved is writing this frame — the tween-vs-finger collision the drag path is
        // otherwise careful to avoid. The release re-settles everything anyway. «Авто» still wins,
        // because it must be able to tear the whole surface down mid-drag.
        if (_draggingSlot && input != SuggestionSlotInput.ReplyModeOff) return;

        // Resolve against the LIVE tenant, not the remembered one. _slotState can legitimately
        // disagree with reality: the answered-run collapse fires on the outgoing echo while
        // MessagesBottomPanel.KeepKeyboardOpenRoutine is still holding the keyboard up, which would
        // leave a Collapsed state sitting over a live keyboard — and the very next incoming message
        // would then read rule 9's «raise from Collapsed» and tear the keyboard away mid-word.
        bool kbUp = _keyboardMover != null && _keyboardMover.NativeKeyboardVisible;
        SuggestionSlotState current = kbUp ? SuggestionSlotState.Keyboard : _slotState;

        SlotTransition t = SuggestionSlotStateMachine.Resolve(current, input, _semiAutoOn);
        bool blurHandledByShow = false;

        // Only a deliberate user ask may take the slot from a typing owner; everything automatic
        // parks and lands when the keyboard leaves.
        bool explicitAsk = input == SuggestionSlotInput.FieldTap
                           || input == SuggestionSlotInput.ThreadTap
                           || input == SuggestionSlotInput.KeyTap;

        if (t.State != current)
        {
            switch (t.State)
            {
                case SuggestionSlotState.Panel:
                    // Claiming FROM a live keyboard is only allowed for an explicit ask, and then
                    // ShowPanel holds the inset at the keyboard's own height and dismisses it
                    // itself, so the composer cannot move mid-handoff. It owns the blur too —
                    // dismissing here as well would fire the dismissal pair twice for one gesture.
                    blurHandledByShow = kbUp && explicitAsk;
                    ShowPanel(claimSlotFromKeyboard: blurHandledByShow);
                    break;
                case SuggestionSlotState.Expanded:
                    ShowPanel(claimSlotFromKeyboard: false, detent: SlotDetent.Expanded);
                    break;
                case SuggestionSlotState.Collapsed:
                    HidePanel();
                    break;
                case SuggestionSlotState.Keyboard:
                    // Deliberately NO state write here. The keyboard has not risen yet — it rises
                    // because of the FocusField below — and the swap watcher recognises the claim
                    // as `kbVisible && PanelOwnsSlot`. Flipping the state now would make that test
                    // false forever, so the no-dip hold would never start and the inset would never
                    // be released: the panel would sit active behind the keyboard for the whole
                    // typing session. The watcher owns this transition (see Update).
                    break;
            }
        }

        if (t.BlurField && !blurHandledByShow) DismissComposerKeyboard();
        if (t.FocusField) FocusComposer();
    }

    private void FocusComposer()
    {
        var field = _bottomPanel != null ? _bottomPanel.inputField : null;
        if (field == null) return;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(field.gameObject);
        field.ActivateInputField();
#if UNITY_EDITOR
        if (_keyboardMover != null) _keyboardMover.SetSimulatedKeyboard(true);
#endif
    }

    /// <summary>Repaint the composer key from the current tenant — one call site for the whole grammar.</summary>
    private void ApplyKeyStyle()
    {
        if (_slotKey != null) _slotKey.Apply(ComposerSlotKeyModel.For(_slotState, _semiAutoOn));
    }

    // The swap watcher. Runs every frame the chat screen is up: measures the live keyboard for
    // the next slot open, detects the keyboard claiming the slot (⌨ key or a direct composer
    // tap — both just activate the field; the rise is detected here), completes or reinstates
    // a yield, and lands a parked auto-show once the keyboard leaves.
    void Update()
    {
        if (_semiAutoOn) PumpThreadTap();   // model E rule 5; «Авто» has no panel to raise
        if (_keyboardMover == null) return;

#if UNITY_EDITOR
        // Editor parity: focusing the composer must claim the slot like the device keyboard
        // does. K still toggles the simulated keyboard manually.
        if (ComposerFocused && !_keyboardMover.NativeKeyboardVisible)
            _keyboardMover.SetSimulatedKeyboard(true);
#endif

        bool kbVisible = _keyboardMover.NativeKeyboardVisible;
        float kbCanvas = _keyboardMover.EffectiveAreaCanvasPx;

        if (kbVisible && SuggestionSlotHeight.IsValid(kbCanvas))
        {
            SuggestionSlotHeight.Remember(kbCanvas);   // measure while it's up — the next slot open matches exactly
            _lastKeyboardCanvasPx = kbCanvas;          // ...and keep it for the return handoff's hold
        }

        // A finger owns the slot for the length of a drag: the watcher must not read a
        // finger-driven inset as a tenant change, and the release re-settles everything anyway.
        // The «+» sheet is the one exception. It is a MODAL that evicts whoever holds the region
        // and then waits for the composer to come down before it rises, and HandleDragMoved is the
        // only writer of the inset that AttachOpen does not gate — so a finger still on the handle
        // would both suppress this eviction and drive the rise back up, starving that wait until
        // its timeout. The sheet outranks the drag: cancel the gesture and let the eviction run.
        if (_draggingSlot)
        {
            if (!AttachOpen) { _kbWasVisible = kbVisible; return; }
            CancelSlotDrag();
        }

        bool keyboardClaimsSlot = kbVisible && PanelOwnsSlot && !_yieldingToKeyboard
                                  && (!_kbWasVisible || ComposerFocused);
        if (keyboardClaimsSlot)
        {
            // The keyboard is rising into the slot: the sheet yields, but the held inset must
            // NOT drop until the keyboard is actually there (no-dip). The panel stays active
            // underneath — the native keyboard renders above everything and covers it.
            // Yielding FROM Expanded: retarget the hold down to the standard detent first.
            // ShouldReleaseHold passes when the keyboard covers 95% of the HELD height, and an
            // expanded slot is taller than any keyboard — the fraction could never be reached, so
            // every such handoff would sit frozen for the full 0.7s timeout with a strip of panel
            // wedged above the keyboard, then drop the difference in one step.
            if (_slotState == SuggestionSlotState.Expanded && _keyboardMover.VirtualBottomInset > kbCanvas)
            {
                _slotCanvasPx = SuggestionSlotSwap.SlotForOpen(true, kbCanvas, SuggestionSlotHeight.Remembered);
                _keyboardMover.VirtualBottomInset = _slotCanvasPx;
                if (_panel != null)
                    _panel.SetSlotMetrics(_slotCanvasPx, _keyboardMover.SafeBottomCanvasPx);
            }
            _slotState = SuggestionSlotState.Keyboard;
            _yieldingToKeyboard = true;
            _yieldStartedAt = Time.realtimeSinceStartup;
            _insetTween?.Kill();
            if (_panel != null) _panel.SetFadeSuppressed(false);   // Expanded is over; the fade rules again
            ApplyKeyStyle();
        }

        if (_yieldingToKeyboard)
        {
            if (!kbVisible)
            {
                // The keyboard bounced away before taking the slot — the panel is still in
                // place and holding the inset: reinstate it instead of dropping the slot.
                _yieldingToKeyboard = false;
                _slotState = SuggestionSlotState.Panel;
                ApplyKeyStyle();
            }
            else if (SuggestionSlotSwap.ShouldReleaseHold(
                         kbVisible, kbCanvas, _keyboardMover.VirtualBottomInset,
                         Time.realtimeSinceStartup - _yieldStartedAt))
            {
                _yieldingToKeyboard = false;
                _keyboardMover.VirtualBottomInset = 0f;   // the real keyboard owns the inset now
                if (_panel != null) _panel.Deactivate();
            }
        }

        // The «+» attach sheet is slot-exclusive too (device finding 2026-08-12): opening it
        // over the panel must swap exactly like the keyboard — panel slides away, sheet rises.
        // The show is parked and lands again when the sheet closes.
        bool attachOpen = AttachOpen;
        if (attachOpen && (PanelOwnsSlot || _slotState == SuggestionSlotState.Keyboard))
        {
            // The sheet must park the show from EITHER tenant. Parking only from the panel left the
            // keyboard→sheet path with nothing to restore: the sheet's own opening blurs the field,
            // so the keyboard-left signal is consumed while the sheet still owns the slot, and
            // closing the sheet would then leave the slot with no tenant at all.
            if (PanelOwnsSlot) HidePanel();
            _pendingShow = true;   // after HidePanel — it clears the flag
        }

        // A parked auto-show lands the moment nothing else owns the slot.
        if (_pendingShow && _semiAutoOn && !PanelOwnsSlot && !_yieldingToKeyboard && !kbVisible && !attachOpen)
        {
            _pendingShow = false;
            ShowPanel(claimSlotFromKeyboard: false);
        }

        // Model E rule 2: the panel is the slot's DEFAULT tenant, so it comes back whenever the
        // keyboard leaves — however it left (thread tap, Send, the ⌨ key, the OS). Today's build
        // simply deactivated the panel here and nothing brought it back. Never against a deliberate
        // collapse: Collapsed is not Keyboard, so the transition table declines it by itself.
        // LEVEL-triggered, not edge-triggered: an edge is a single frame, and any guard that
        // rejects that one frame (the «+» sheet opening, a yield still finishing) would consume it
        // for good and strand the slot with no tenant. Resolve only ever moves Keyboard→Panel here,
        // so re-asserting it every frame is idempotent.
        if (!kbVisible && !_yieldingToKeyboard && !attachOpen
            && _slotState == SuggestionSlotState.Keyboard)
        {
            // Hold BEFORE handing over. This fires on the frame the keyboard is already gone, so
            // the applied rise has just collapsed to 0: letting ShowPanel tween up from there drops
            // the composer a full slot height and flies it back — the one handoff that would dip.
            float hold = _lastKeyboardCanvasPx > 0f ? _lastKeyboardCanvasPx : StandardDetent();
            _insetTween?.Kill();
            _keyboardMover.VirtualBottomInset = hold;
            ApplySlotInput(SuggestionSlotInput.KeyboardDismissed);
        }

        _kbWasVisible = kbVisible;
    }

    private bool AttachOpen =>
        _bottomPanel != null && _bottomPanel.AttachSheet != null && _bottomPanel.AttachSheet.IsOpen;

    // Glue: the panel's top edge tracks the composer's bottom edge exactly — smoothing, live
    // keyboard motion and all. KeyboardAwarePanel applies its inset in Update; LateUpdate runs
    // after every Update, so the glue always reads this frame's applied value.
    void LateUpdate()
    {
        if (_panel != null && _panel.IsShown && _keyboardMover != null)
            _panel.FollowInset(_keyboardMover.AppliedBottomInset);
    }

    private bool ComposerFocused =>
        _bottomPanel != null && _bottomPanel.inputField != null && _bottomPanel.inputField.isFocused;

    // AttachSheet's sanctioned dismissal pair — ReleaseSelection AFTER DeactivateInputField
    // (ghost-caret rule; `Reset On Deactivation` is off on every input in this project).
    private void DismissComposerKeyboard()
    {
        var field = _bottomPanel != null ? _bottomPanel.inputField : null;
        if (field == null) return;
        field.DeactivateInputField();
        field.ReleaseSelection();
#if UNITY_EDITOR
        if (_keyboardMover != null) _keyboardMover.SetSimulatedKeyboard(false);
#endif
    }

    // The ✦ ⇄ ⌨ key inside the composer field: ✦ (slot closed) opens the panel — over the
    // keyboard if one is up; ⌨ (slot open) hands the slot back to the keyboard by activating
    // the composer — the rise is detected and choreographed by the Update watcher.
    private void HandleSlotKeyTapped()
    {
        if (!_semiAutoOn) return;
        bool wasIdleAndOpening = _answeredIdle && !PanelOwnsSlot;
        ApplySlotInput(SuggestionSlotInput.KeyTap);
        // Re-opening after an answered run: the rendered cards are the stale pre-send set —
        // regenerate for "what's next" instead of showing them (flow decision 2026-08-11).
        if (wasIdleAndOpening && PanelOwnsSlot) IssueRequest(steerTowardText: null, lastIncomingText: null);
    }

    // --- Manual refresh (INT-03) ---
    // Re-rolls the CURRENT round: same steer, new sampling (rounds flow 2026-08-11). Round 1
    // has a null steer, so there it stays the old fresh-set refresh. Never pushed — a refresh
    // replaces the round in place, it is not forward movement.

    private void HandleManualRefresh()
    {
        if (_semiAutoOn) IssueRequest(steerTowardText: _currentSteer, lastIncomingText: null);
    }

    // --- Manual sheet show/hide (✦ key + any legacy toggle listeners) ---
    // Routes through ShowPanel/HidePanel so the slot inset always follows the sheet. Only
    // meaningful in «Вместе» — in Авто there is no suggestions flow to reveal. A manual close
    // is a soft dismiss: later results keep rendering silently and the next chat-open or
    // toggle-ON re-shows as before.

    public void SetSheetOpen(bool open)
    {
        if (!_semiAutoOn) return;
        if (open)
        {
            ShowPanel(claimSlotFromKeyboard: true);   // explicit ask — the keyboard hands the slot over
            // Re-opening after an answered run: the rendered cards are the stale pre-send set —
            // regenerate for "what's next" instead of showing them (flow decision 2026-08-11).
            if (_answeredIdle) IssueRequest(steerTowardText: null, lastIncomingText: null);
        }
        else HidePanel();
    }

    public void ToggleSheet() => SetSheetOpen(!PanelOwnsSlot);

    // --- Thread tap (model E rule 5) ----------------------------------------
    // Nothing on the messages thread listened for taps before: the keyboard dismissal was a side
    // effect of EventSystem deselection. Model E gives the tap a meaning — it RAISES the panel
    // (from the keyboard, blurring first; from Collapsed) and never hides an open one. Run as a
    // press/release pump rather than an IPointerClickHandler on the Scroll, because the gesture
    // has to be rejected when a bubble long-press, a swipe-to-reply, a fling-stop or a plain
    // scroll already owns it — none of which a click handler can see.
    private void PumpThreadTap()
    {
        var pointer = UnityEngine.InputSystem.Pointer.current;
        if (pointer == null) return;

        if (pointer.press.wasPressedThisFrame)
        {
            _threadPressActive = true;
            _threadPressPos = pointer.position.ReadValue();
            // WHERE the press landed can only be known while the finger is down — the raycast needs
            // a live pointer position. WHETHER it was a fling-stop is read at RELEASE instead:
            // ScrollClickBlocker latches IsBlocking inside its own Update on this very frame, and
            // nothing orders the two components, so reading it now is a coin flip. No new press can
            // occur between this press and its release, so by release time the verdict is settled.
            _threadPressEligible = PressLandedOnThread(_threadPressPos);
            return;
        }

        if (!pointer.press.wasReleasedThisFrame || !_threadPressActive) return;
        _threadPressActive = false;
        if (!_threadPressEligible) return;

        Vector2 release = pointer.position.ReadValue();
        float scale = CanvasScale;
        if (!SuggestionSlotGestures.IsThreadTap(
                _threadPressPos.x / scale, _threadPressPos.y / scale,
                release.x / scale, release.y / scale,
                pressWasInsideThread: true,
                scrollWasFlinging: ScrollClickBlocker.IsBlocking,
                otherGestureOwnedIt: ReactionBarShowing))
            return;

        ApplySlotInput(SuggestionSlotInput.ThreadTap);
    }

    // A long-press that opened the reaction bar ends with a pointer-up like any tap; the bar's own
    // scrim is what tells us the gesture became something else. Checked at RELEASE because the bar
    // opens DURING the press.
    private static bool ReactionBarShowing =>
        ReactionBarController.Instance != null && ReactionBarController.Instance.IsShowing;

    /// <summary>
    /// Was the press inside the messages thread? Walks ALL raycast hits rather than taking the top
    /// one: the left-edge SwipeBack strip and other transparent overlays sit above the thread, so a
    /// top-hit test would reject every tap in that band.
    /// <para>
    /// Deliberately does NOT reject a press merely because a bubble gesture component is under it.
    /// MessageBubbleLongPress and SwipeToReply live on every Bubble, which is most of the thread's
    /// area — rejecting on their presence would leave rule 5 alive only in the gaps between
    /// messages. A plain tap on a bubble IS a thread tap; the gestures those components actually
    /// fire (a long-press opening the reaction bar, a swipe) are rejected at release instead, by
    /// the movement tolerance and the reaction-bar check.
    /// </para>
    /// </summary>
    private bool PressLandedOnThread(Vector2 screenPos)
    {
        if (EventSystem.current == null || _threadInset == null) return false;
        // A modal above the thread still lets the thread's own graphics answer a raycast —
        // RaycastAll does no occlusion culling — so overlays have to be rejected by name.
        if (ReactionBarShowing) return false;
        if (PhotoViewer.Instance != null && PhotoViewer.Instance.panel != null
            && PhotoViewer.Instance.panel.activeSelf) return false;

        _tapEventData ??= new PointerEventData(EventSystem.current);
        _tapEventData.position = screenPos;
        _tapHits.Clear();
        EventSystem.current.RaycastAll(_tapEventData, _tapHits);

        bool sawThread = false;
        for (int i = 0; i < _tapHits.Count; i++)
        {
            Transform hit = _tapHits[i].gameObject.transform;
            // The panel and the composer are the slot's own surfaces — a tap there is never a
            // "raise the panel" request, and the cheap checks go first.
            if (hit.GetComponentInParent<SuggestionsPanel>() != null) return false;
            if (hit.GetComponentInParent<MessagesBottomPanel>() != null) return false;
            if (hit.GetComponentInParent<ScrollTopInsetCompensator>() == _threadInset) sawThread = true;
        }
        return sawThread;
    }
}
