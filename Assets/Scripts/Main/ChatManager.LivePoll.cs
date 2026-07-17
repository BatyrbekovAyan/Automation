using System.Collections;
using UnityEngine;

// Partial-class extension housing the D5 open-chat live poll: a single always-running,
// self-gating coroutine that re-issues the existing one-shot SyncLatestMessages on the
// OpenChatLivePollGate cadence while a chat stays open — so a message arriving mid-view
// renders on its own, WITHOUT re-entering the chat, on BOTH WhatsApp and Telegram.
//
// The poll adds NO new messages/get caller. It reuses SyncLatestMessages, inheriting every
// crossing-safety invariant already proven there: the post-await `currentChatId` re-check,
// CrossChatResponseGuard, the _chatFetchesInFlight serial gate, the brandNew-only diff, and
// the OnLiveMessagesReceived fire. Because SyncLatestMessages also refreshes _activeChatCache,
// the fix cascades automatically to all three consumers with no extra wiring:
//   • MessageListView.HandleLiveMessages  → the incoming bubble
//   • SuggestionsController.HandleLive     → «Вместе» card refresh
//   • ChatManager.TryGetRecentMessages     → a suggestions payload that now includes the
//                                            newest incoming (re-opens H2 relevance)
public partial class ChatManager
{
    /// <summary>
    /// True while the app is foregrounded. The poll never runs backgrounded — battery, and the
    /// pairing-code flow deliberately leaves the app on the same phone. Set by
    /// OnApplicationFocus / OnApplicationPause below.
    /// </summary>
    private bool _appFocused = true;

    /// <summary>
    /// realtimeSinceStartup of the last poll issue; the gate throttles to
    /// OpenChatLivePollGate.IntervalSeconds. Reset at chat-open (SelectChat) so the open's own
    /// sync gets a full interval of breathing room before the first poll.
    /// </summary>
    private float _lastLivePollTime;

    /// <summary>
    /// D2-ext loaded-window reaction reconcile throttle. The wider pass is a background correction
    /// (a reaction changed/removed IN the Telegram app on an OLDER loaded message the latest-window
    /// poll never re-fetches), so it runs far slower than the 3s live poll. realtimeSinceStartup of
    /// the last wider-pass issue; reset at chat-open (SelectChat) for a full interval of headroom.
    /// </summary>
    private const float WiderReactionReconcileIntervalSeconds = 12f;
    private float _lastWiderReactionReconcileTime;

    /// <summary>
    /// Round-robin cursor over the older server pages (2..PagesToCover) the wider reaction pass
    /// covers one-per-tick — so at most a single ValidateCachePageAgainstServer is ever in flight,
    /// never a burst. Reset to the first older page at chat-open (SelectChat).
    /// </summary>
    private int _widerReactionReconcilePage = 2;

    /// <summary>
    /// Handle to the single self-gating poll coroutine. Held so it can be re-kicked after every
    /// StopAllCoroutines() (SetActiveBot / SetActiveChannel / ClearAllLocalHistory) without ever
    /// spawning a duplicate.
    /// </summary>
    private Coroutine _livePollRoutine;

    /// <summary>
    /// The open-chat live poll. Ticks once a second and, when OpenChatLivePollGate allows,
    /// re-issues SyncLatestMessages for the open chat. Self-gating: whenever no chat is open,
    /// the app is backgrounded, the open isn't settled, or the interval hasn't elapsed, the
    /// tick is a couple of cheap bool checks — so one instance safely runs the whole session.
    /// </summary>
    private IEnumerator OpenChatLivePollRoutine()
    {
        while (true)
        {
            // Realtime so a paused/zero-timeScale app still ticks predictably; a fresh
            // instance each loop mirrors the codebase idiom (WaitForWhatsAppSyncRoutine) and
            // sidesteps the cached-WaitForSecondsRealtime reuse gotcha.
            yield return new WaitForSecondsRealtime(1f);

            // A chat is "open" only when its id is set AND its panel is on-screen — currentChatId
            // is sticky after ShowChatList (cleared only on channel switch), so the panel check
            // is what stops the poll from running while the owner browses the chat list.
            // activeInHierarchy, NOT activeSelf: BottomTabManager hides screens by deactivating
            // the whole screen panel, leaving MessageListPanel's own activeSelf true — the poll
            // must pause while the owner is on another tab and resume when they return.
            bool chatIsOpen = !string.IsNullOrEmpty(currentChatId)
                              && MessageListPanel != null && MessageListPanel.activeInHierarchy;

            // Exactly the isSettled predicate SyncLatestMessages uses internally (Idle + not
            // sliding), minus Populate — the initial open already syncs during Populate, so the
            // repeating poll only needs to engage once the chat has fully settled.
            bool chatOpenSettled = _phase == ChatOpenPhase.Idle && !SwipeToBack.IsSliding;

            // A settled, visible chat always has its cache populated (OpenChatRoutine sets it);
            // guard anyway — SyncLatestMessages iterates cachedList and would NRE on null.
            if (_activeChatCache == null) continue;

            if (OpenChatLivePollGate.ShouldIssue(
                    chatIsOpen: chatIsOpen,
                    appFocused: _appFocused,
                    fetchInFlight: _chatFetchesInFlight > 0,
                    chatOpenSettled: chatOpenSettled,
                    secondsSinceLastPoll: Time.realtimeSinceStartup - _lastLivePollTime))
            {
                _lastLivePollTime = Time.realtimeSinceStartup;

                // REUSE the one-shot open-chat sync — never a second messages/get caller. It
                // re-checks currentChatId post-await, computes only brandNew, manages
                // _chatFetchesInFlight, refreshes _activeChatCache, and fires OnLiveMessagesReceived.
                // The gate guarantees no sync is in flight here, so StopCoroutine is a defensive
                // no-op mirroring OpenChatRoutine's idiom (guards against any future overlap).
                if (_activeSync != null) StopCoroutine(_activeSync);
                _activeSync = StartCoroutine(SyncLatestMessages(currentChatId, _activeChatCache));
            }

            // D2-ext: the latest-window sync above only re-fetches page 1 (offset 0). A reaction
            // changed/removed IN the Telegram app on a LOADED-but-older message would never reflect,
            // so run a bounded, throttled background pass over the older loaded pages. Serial-safe
            // (reuses the ValidateCachePageAgainstServer seam, gated on no fetch in flight) and
            // Telegram-only, so the WhatsApp path is byte-identical.
            MaybeIssueWiderReactionReconcile(chatIsOpen, chatOpenSettled);
        }
    }

    /// <summary>
    /// D2-ext loaded-window reaction reconcile. The latest-window poll re-fetches only page 1
    /// (offset 0), so a reaction changed or removed IN the Telegram app on a loaded-but-older
    /// message never reconciles. When the loaded window spills past the latest page
    /// (<see cref="ReactionReconcileWindow.NeedsWiderPass"/>), this issues ONE
    /// <c>ValidateCachePageAgainstServer</c> for the next older page (round-robin across ticks) —
    /// REUSING that established serial background seam, which already inherits the
    /// <c>_chatFetchesInFlight</c> gate, <c>CrossChatResponseGuard</c>, the post-await
    /// <c>currentChatId</c> re-check, and never fires <c>OnBatchMessagesLoaded</c>. So NO new
    /// concurrent messages/get caller is introduced (the pass only issues when nothing is in
    /// flight, and covers one page per tick). Throttled far slower than the 3s live poll — a
    /// background correction, not the hot path. Telegram-only: WhatsApp returns immediately (its
    /// reactions flow through <see cref="ReactionStore"/>) → byte-identical. Killed on bot/channel
    /// switch by the existing StopAllCoroutines (the coroutine + the poll are both torn down and
    /// the poll re-kicked); the cursor/throttle re-baseline at the next chat-open.
    /// </summary>
    private void MaybeIssueWiderReactionReconcile(bool chatIsOpen, bool chatOpenSettled)
    {
        if (ActiveChannel != ChatChannel.Telegram) return;    // WhatsApp: no-op, byte-identical
        if (!chatIsOpen || !chatOpenSettled || !_appFocused) return;
        if (_chatFetchesInFlight > 0) return;                 // serial: never overlap another fetch
        if (_activeChatCache == null) return;
        if (Time.realtimeSinceStartup - _lastWiderReactionReconcileTime < WiderReactionReconcileIntervalSeconds)
            return;
        if (!ReactionReconcileWindow.NeedsWiderPass(_activeChatCache.Count, MessagesPerPage)) return;

        _lastWiderReactionReconcileTime = Time.realtimeSinceStartup;

        // Walk the older pages one-per-tick (page 1 is the latest-window sync above); wrap when the
        // cursor passes the loaded window's last page or the window shrank under it.
        int pages = ReactionReconcileWindow.PagesToCover(_activeChatCache.Count, MessagesPerPage);
        if (_widerReactionReconcilePage < 2 || _widerReactionReconcilePage > pages)
            _widerReactionReconcilePage = 2;

        StartCoroutine(ValidateCachePageAgainstServer(currentChatId, _widerReactionReconcilePage));
        _widerReactionReconcilePage++;
    }

    /// <summary>
    /// Foreground gate: pause polling when the app loses focus (battery; and the pairing-code
    /// flow leaves the app on the same phone). Resume edge re-enables it.
    /// </summary>
    private void OnApplicationFocus(bool hasFocus) => _appFocused = hasFocus;

    /// <summary>
    /// Backgrounding also drops focus. Only ever sets false here — the resume edge is owned by
    /// OnApplicationFocus(true) — so a paused app can never poll.
    /// </summary>
    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused) _appFocused = false;
    }
}
