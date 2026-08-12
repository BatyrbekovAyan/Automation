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

    // Keyboard-slot tenancy (sketch-003 variant A). _sheetOpen is the INTENT — the panel view
    // can be active-but-covered while the native keyboard slides over it. _yieldingToKeyboard
    // bridges the handoff where the panel has conceded the slot but the held inset must not
    // drop until the keyboard actually arrives (SuggestionSlotSwap.ShouldReleaseHold — the
    // no-dip rule). _pendingShow parks an auto-show that found the keyboard owning the slot
    // (an auto-show never steals the slot from a typing owner) until the keyboard leaves.
    private bool _sheetOpen;
    private bool _yieldingToKeyboard;
    private float _yieldStartedAt;
    private bool _kbWasVisible;
    private bool _pendingShow;
    private float _slotCanvasPx;
    private Tweener _insetTween;       // slot open/close inset animation (handoffs never tween — they hold)

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
            _panel.OnBackRequested -= HandleBack;
        }
        if (_slotKey != null) _slotKey.Tapped -= HandleSlotKeyTapped;
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
        _sheetOpen = false;
        _yieldingToKeyboard = false;
        _pendingShow = false;
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
        _provider.Request(req, result => OnResult(seq, chatId, tailKey, result));
    }

    private void OnResult(long seq, string capturedChatId, string capturedTailKey, SuggestionResult result)
    {
        if (!_semiAutoOn) return;                              // user opted out mid-flight → never render
        string currentChatId = ChatManager.Instance != null ? ChatManager.Instance.CurrentChatId : null;
        if (!SuggestionSequenceGuard.IsCurrent(seq, _requestSeq, capturedChatId, currentChatId))
            return;                                            // superseded / chat switched → DISCARD
        if (_panel != null) _panel.Render(result);            // skeleton → cards | empty | error
        // Rounds: only an Ok set becomes the restorable "current round" — an error/empty render
        // leaves _currentRendered on the last good set, so a pick-after-retry still pushes it.
        if (result != null && result.status == SuggestionStatus.Ok) _currentRendered = result;
        // F9 verify-at-store: cache only when the tail is STILL the one this request answered —
        // a message that landed mid-flight makes this set already-stale, so let it render (the
        // corrective fire is coming) but never persist it. Store ignores non-Ok results itself.
        if (capturedTailKey != null && capturedTailKey == CurrentTailKey())
            _cache.Store(capturedChatId, capturedTailKey, result);
    }

    // --- Card tap (INT-01 + INT-04 unified, D-01/D-02/D-03) ---

    private void HandleCardTapped(string replyText)
    {
        if (_bottomPanel != null && _bottomPanel.inputField != null)
            StartCoroutine(WriteComposerRoutine(_bottomPanel.inputField, replyText));
        // Rounds flow: record the round being left so ‹ can restore it locally, remember the
        // new direction for refresh re-rolls, and count the pick for preference learning.
        _rounds.Push(_currentRendered, _currentSteer);
        _currentSteer = replyText;
        RecordPick(replyText);
        UpdateBackUi();
        IssueRequest(steerTowardText: replyText, lastIncomingText: null);   // next round steers toward the pick (INT-04/D-01)
        // NEVER auto-send — only the existing composer Send button delivers a message (D-03).
        // The sheet stays open on a pick so a re-clustered variant is one tap to swap in;
        // it hides on the OUTGOING echo instead (flow decision 2026-08-11).
    }

    // ‹ pressed: restore the previous round's cards INSTANTLY — no LLM call, no skeleton.
    // The seq bump kills any in-flight forward/refresh result so it cannot overwrite the
    // restored round. The composer is deliberately untouched: back changes the CARDS only
    // (an owner edit in the composer must never be destroyed by navigation).
    private void HandleBack()
    {
        if (!_semiAutoOn) return;
        if (!_rounds.TryPop(out SuggestionResult previous, out string previousSteer)) return;
        _requestSeq++;
        _currentSteer = previousSteer;
        _currentRendered = previous;
        if (_panel != null) _panel.Render(previous);
        UpdateBackUi();
    }

    // Preference learning v1 (2026-08-11): count which MOVE the owner picks, per bot. The tap
    // carries only the text, so the label is resolved from the set on screen; texts within one
    // set are distinct by generation. Read back by N8nSuggestionsProvider.BuildPickStats.
    private void RecordPick(string replyText)
    {
        if (_currentRendered?.items == null || ChatManager.Instance == null) return;
        string botName = ChatManager.Instance.CurrentBotId;
        if (string.IsNullOrEmpty(botName)) return;
        foreach (var item in _currentRendered.items)
        {
            if (item == null || item.text != replyText) continue;
            string key = botName + "SuggestPick" + item.intentLabel;
            PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
            PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence)
            return;
        }
    }

    // A fresh round 1: new incoming, chat/bot switch, explicit toggle-on, answered run.
    private void StartFreshRound()
    {
        _rounds.Clear();
        _currentSteer = null;
        _currentRendered = null;
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
            SetSheetOpen(false);
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
                if (!_sheetOpen) ShowPanel(claimSlotFromKeyboard: false);
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
    /// Open the slot for the panel. Auto-shows (chat open, toggle-on, debounce fire) pass
    /// false and PARK the show while the keyboard owns the slot — stealing it mid-typing is
    /// never acceptable; the parked show lands when the keyboard leaves. The explicit ✦ tap
    /// passes true: it dismisses the keyboard and takes the slot at the keyboard's own height.
    /// </summary>
    private void ShowPanel(bool claimSlotFromKeyboard)
    {
        if (_panel == null) return;
        bool kbVisible = _keyboardMover != null && _keyboardMover.NativeKeyboardVisible;
        if ((kbVisible || AttachOpen) && !claimSlotFromKeyboard)
        {
            _pendingShow = true;   // never steal the slot from a typing owner or an open «+» sheet
            return;
        }
        // Explicit ✦ while the attach sheet is up: the sheet hands the slot over, like the keyboard.
        if (AttachOpen) _bottomPanel.AttachSheet.Close();

        float kbCanvas = _keyboardMover != null ? _keyboardMover.EffectiveAreaCanvasPx : 0f;
        _slotCanvasPx = SuggestionSlotSwap.SlotForOpen(kbVisible, kbCanvas, SuggestionSlotHeight.Remembered);
        _panel.SetSlotMetrics(_slotCanvasPx, _keyboardMover != null ? _keyboardMover.SafeBottomCanvasPx : 0f);
        _panel.ShowInSlot();
        _sheetOpen = true;
        _yieldingToKeyboard = false;
        _pendingShow = false;
        if (_slotKey != null) _slotKey.SetSlotOpen(true);

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

    private void HidePanel()
    {
        _sheetOpen = false;
        _pendingShow = false;
        if (_slotKey != null) _slotKey.SetSlotOpen(false);
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

    // The swap watcher. Runs every frame the chat screen is up: measures the live keyboard for
    // the next slot open, detects the keyboard claiming the slot (⌨ key or a direct composer
    // tap — both just activate the field; the rise is detected here), completes or reinstates
    // a yield, and lands a parked auto-show once the keyboard leaves.
    void Update()
    {
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
            SuggestionSlotHeight.Remember(kbCanvas);   // measure while it's up — the next slot open matches exactly

        bool keyboardClaimsSlot = kbVisible && _sheetOpen && !_yieldingToKeyboard
                                  && (!_kbWasVisible || ComposerFocused);
        if (keyboardClaimsSlot)
        {
            // The keyboard is rising into the slot: the sheet yields, but the held inset must
            // NOT drop until the keyboard is actually there (no-dip). The panel stays active
            // underneath — the native keyboard renders above everything and covers it.
            _sheetOpen = false;
            _yieldingToKeyboard = true;
            _yieldStartedAt = Time.realtimeSinceStartup;
            _insetTween?.Kill();
            if (_slotKey != null) _slotKey.SetSlotOpen(false);
        }

        if (_yieldingToKeyboard)
        {
            if (!kbVisible)
            {
                // The keyboard bounced away before taking the slot — the panel is still in
                // place and holding the inset: reinstate it instead of dropping the slot.
                _yieldingToKeyboard = false;
                _sheetOpen = true;
                if (_slotKey != null) _slotKey.SetSlotOpen(true);
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
        if (attachOpen && _sheetOpen)
        {
            HidePanel();
            _pendingShow = true;   // after HidePanel — it clears the flag
        }

        // A parked auto-show lands the moment nothing else owns the slot.
        if (_pendingShow && _semiAutoOn && !_sheetOpen && !_yieldingToKeyboard && !kbVisible && !attachOpen)
        {
            _pendingShow = false;
            ShowPanel(claimSlotFromKeyboard: false);
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
        if (_sheetOpen)
        {
            var field = _bottomPanel != null ? _bottomPanel.inputField : null;
            if (field == null) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
#if UNITY_EDITOR
            if (_keyboardMover != null) _keyboardMover.SetSimulatedKeyboard(true);
#endif
        }
        else SetSheetOpen(true);
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

    public void ToggleSheet() => SetSheetOpen(!_sheetOpen);
}
