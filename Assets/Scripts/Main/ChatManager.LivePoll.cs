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

            if (!OpenChatLivePollGate.ShouldIssue(
                    chatIsOpen: chatIsOpen,
                    appFocused: _appFocused,
                    fetchInFlight: _chatFetchesInFlight > 0,
                    chatOpenSettled: chatOpenSettled,
                    secondsSinceLastPoll: Time.realtimeSinceStartup - _lastLivePollTime))
                continue;

            _lastLivePollTime = Time.realtimeSinceStartup;

            // REUSE the one-shot open-chat sync — never a second messages/get caller. It
            // re-checks currentChatId post-await, computes only brandNew, manages
            // _chatFetchesInFlight, refreshes _activeChatCache, and fires OnLiveMessagesReceived.
            // The gate guarantees no sync is in flight here, so StopCoroutine is a defensive
            // no-op mirroring OpenChatRoutine's idiom (guards against any future overlap).
            if (_activeSync != null) StopCoroutine(_activeSync);
            _activeSync = StartCoroutine(SyncLatestMessages(currentChatId, _activeChatCache));
        }
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
