---
phase: 05-channel-aware-chatmanager-core
reviewed: 2026-07-12T19:02:49Z
depth: standard
files_reviewed: 38
files_reviewed_list:
  - Assets/Scripts/Chat/ChatChannel.cs
  - Assets/Scripts/Chat/WappiEndpoints.cs
  - Assets/Scripts/Chat/ChatIdFormat.cs
  - Assets/Scripts/Chat/ChatDialogTime.cs
  - Assets/Scripts/Chat/MessageTypeParser.cs
  - Assets/Scripts/Chat/ChatDialog.cs
  - Assets/Scripts/Chat/DeliveryTickFormatter.cs
  - Assets/Scripts/Chat/OutboxStore.cs
  - Assets/Scripts/Chat/WappiMediaRequestFactory.cs
  - Assets/Scripts/Chat/WappiRecipient.cs
  - Assets/Scripts/Main/ChatManager.Channel.cs
  - Assets/Scripts/Main/TelegramAuthResponseParser.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Scripts/Main/ChatManager.BotState.cs
  - Assets/Scripts/Main/ChatManager.DeleteChat.cs
  - Assets/Scripts/Main/ChatManager.MediaSend.cs
  - Assets/Scripts/Main/ChatManager.Outbox.cs
  - Assets/Scripts/Main/ChatManager.QuoteResolve.cs
  - Assets/Scripts/Main/ChatManager.ReactionResolve.cs
  - Assets/Scripts/Main/ChatManager.ReactionSend.cs
  - Assets/Scripts/Main/Manager.cs
  - Assets/Scripts/Main/WhatsAppTabState.cs
  - Assets/Scripts/UI/ChatViewModel.cs
  - Assets/Scripts/UI/EmptyStateView.cs
  - Assets/Scripts/UI/MessageListView.cs
  - Assets/Tests/Editor/Chat/ChannelCacheRootTests.cs
  - Assets/Tests/Editor/Chat/ChannelResolutionTests.cs
  - Assets/Tests/Editor/Chat/ChannelTabStateResolverTests.cs
  - Assets/Tests/Editor/Chat/ChatDialogTimeFallbackTests.cs
  - Assets/Tests/Editor/Chat/ChatIdFormatTests.cs
  - Assets/Tests/Editor/Chat/DeliveryTickFormatterTests.cs
  - Assets/Tests/Editor/Chat/OutboxEntryChannelTests.cs
  - Assets/Tests/Editor/Chat/OutboxRetryChannelTests.cs
  - Assets/Tests/Editor/Chat/ParseMessageTypeTests.cs
  - Assets/Tests/Editor/Chat/TelegramAuthResponseParserTests.cs
  - Assets/Tests/Editor/Chat/TelegramReactionRequestTests.cs
  - Assets/Tests/Editor/Chat/TelegramReplyRequestTests.cs
  - Assets/Tests/Editor/Chat/WappiEndpointsTests.cs
findings:
  critical: 0
  warning: 3
  info: 10
  total: 13
status: fixes_applied
fixes:
  applied_at: 2026-07-13
  fixed: 5        # WR-01, WR-02, WR-03, IN-01, IN-05
  deferred: 7     # IN-02, IN-03, IN-04, IN-06, IN-08, IN-09, IN-10
  wontfix: 1      # IN-07
---

# Phase 5: Code Review Report

**Reviewed:** 2026-07-12T19:02:49Z
**Depth:** standard
**Files Reviewed:** 38 (25 production, 13 test)
**Status:** issues_found

## Summary

Reviewed the Phase 5 diff (`f89ae5e^..HEAD`) that made the WhatsApp-only ChatManager pipeline channel-aware (WhatsApp | Telegram on Wappi api/tapi). The phase is well-engineered overall: no critical issues found, and the highest-risk invariants explicitly hold:

**Verified clean (WhatsApp regression invariants):**
- All rewired URLs are byte-identical for WhatsApp (`WappiEndpoints.Sync(WhatsApp, …)` reproduces the exact legacy literals; locked by `WappiEndpointsTests`). WA request bodies are byte-identical: reaction `recipient` uses `NullValueHandling.Ignore` and is only set on Telegram (ChatManager.ReactionSend.cs:68-78, 165), the media factory keeps a WA back-compat overload, and `mark_all=true` is retained on the WA mark-read path (ChatManager.cs:2039-2043).
- `SetActiveChannel` (ChatManager.Channel.cs:50-79) mirrors `SetActiveBot` (ChatManager.BotState.cs:109-138) line-for-line: same persist-then-fire ordering, same `_outbox = null`, same post-`StopAllCoroutines` resets (`_chatFetchesInFlight`, `_chatListSyncing`, video-thumb queue, media-download queue), plus a sensible extra `ShowChatList()` when a chat is open. One shared reset gap exists — see WR-01.
- Channel-switch races on the send paths are handled correctly: `SendTextMessageRoutine`, `PostMediaMessageRoutine`, and `RetryRoutine` all run on `Manager.Instance` (survive `StopAllCoroutines`), snapshot `channel`/`profileId`/`sendCacheRoot` before any yield, and clear the outbox on success via root-parameterized `OutboxStore.RemoveAt(sendCacheRoot, …)` — so a mid-flight channel switch cannot orphan or cross-write outbox/history entries (ChatManager.cs:1856-1910, ChatManager.MediaSend.cs:75, 225, 387; ChatManager.Outbox.cs:77-85). Media retry passes `(ChatChannel)entry.channel` (covered), and in-flight `chats/filter`/`messages/get` coroutines are ChatManager-hosted, so `StopAllCoroutines` kills them before they can pollute the other channel's list.
- PlayerPrefs channel key is clamped on read at both layers (`ReadPersistedChannel`, `ChannelResolver.Resolve` — ChatManager.Channel.cs:34-38, 112-118); corrupt `(ChatChannel)entry.channel` ints degrade to WhatsApp everywhere because every consumer compares `== ChatChannel.Telegram`.
- New pure classes (`ChatIdFormat`, `ChatDialogTime`, `MessageTypeParser`, `TelegramAuthResponseParser`, `ChannelResolver`, `ChannelCachePath`) are null-tolerant and never throw; each is unit-covered including null/empty/garbage inputs.
- Telegram 2FA: mode flag is reset on all three exits (panel open — Manager.cs:1691, panel close and change-number via `ResetTelegram2faMode` — Manager.cs:2539, 2555 areas), password is cleared immediately after the request on every path (Manager.cs, `SubmitTelegram2fa`) and never logged. The `DeleteChat` Telegram guard's comment does confirm the swipe UI gate is deferred to Phase 6 (ChatManager.DeleteChat.cs:14-18).

**Key concerns:** a session-permanent quote/reaction resolver stall reachable through the new channel switch (WR-01), an unescaped JSON body that hard-fails Telegram 2FA for passwords containing quotes/backslashes (WR-02), and an unguarded `dialogType == "chat"` groupness rule that is one server-side field addition away from flipping every WhatsApp row to group rendering (WR-03).

## Warnings

### WR-01: Channel switch mid-drain permanently stalls quote/reaction resolution (draining flags never reset after StopAllCoroutines)

**Status:** FIXED (da4543b) — `ClearResolveQueues()` added in ChatManager.QuoteResolve.cs (clears both quote and reaction queues/in-flight sets/waiters + draining flags); called from BOTH `SetActiveChannel` and `SetActiveBot` right after `ClearMediaDownloadQueue()` (the SetActiveBot gap was pre-existing; same one-shape fix). No EditMode test: the contract is private coroutine bookkeeping on the scene ChatManager MonoBehaviour (StopAllCoroutines vs drain-body cleanup) — no pure seam exists without restructuring the drain workers, which is out of fix scope. WA event order unchanged: the reset only clears already-killed workers' stale state.

**File:** `Assets/Scripts/Main/ChatManager.Channel.cs:73-77`, `Assets/Scripts/Main/ChatManager.QuoteResolve.cs:17,66-69,149-152`, `Assets/Scripts/Main/ChatManager.ReactionResolve.cs:15,34-37,126-128`
**Issue:** `DrainQuoteResolveQueue` and `DrainReactionResolveQueue` are ChatManager-hosted coroutines whose cleanup (`_quoteResolveDraining = false`, clearing `_quoteResolveInFlight`/`_quoteWaiters`, and the reaction equivalents) runs only at the end of the coroutine body. `SetActiveChannel`'s `StopAllCoroutines()` kills them mid-drain, leaving `_quoteResolveDraining`/`_reactionResolveDraining` stuck `true` and the in-flight sets populated. After that, every future `ResolveQuotedMessage`/`ResolveRowDetails` call enqueues work but never starts a worker (`if (!_quoteResolveDraining) StartCoroutine(...)` is permanently false) and previously-queued ids are blocked by the stale in-flight set — quoted-reply cards and reaction row details stop resolving for the rest of the session, on BOTH channels. This gap is pre-existing in `SetActiveBot` (same `StopAllCoroutines`, same missing reset), but `SetActiveChannel` replicates it into a path Phase 6's channel toggle will exercise far more often, and the drains' `WaitForChatFetchesToDrain` deferral widens the kill window (a switch during list load + row resolution is the common case). Note the drains' existing `GetActiveProfileId() != profileId` guard does cover channel switches (profile ids differ per channel) — the problem is purely the killed-coroutine bookkeeping.
**Fix:** Add a reset helper and call it from both switchers right after the other queue resets:
```csharp
// ChatManager.QuoteResolve.cs / ReactionResolve.cs (or a shared partial)
private void ClearResolveQueues()
{
    _quoteResolveQueue.Clear();
    _quoteResolveInFlight.Clear();
    _quoteWaiters.Clear();
    _quoteResolveDraining = false;

    _reactionResolveQueue.Clear();
    _reactionResolveInFlight.Clear();
    _reactionResolveDraining = false;
}

// SetActiveChannel (ChatManager.Channel.cs) and SetActiveBot (ChatManager.BotState.cs),
// after ClearMediaDownloadQueue():
ClearResolveQueues();           // drain workers were just killed; reset their bookkeeping
```

### WR-02: Telegram 2FA body built by string concatenation — passwords containing `"` or `\` always fail auth

**Status:** FIXED (fe35c10) — `JsonConvert.SerializeObject(new { pwd_code = TelegramCodeInput.text })`. Never-log/never-persist invariants untouched (field still cleared on every path immediately after the request; jsonBody never logged).

**File:** `Assets/Scripts/Main/Manager.cs:2473`
**Issue:** `SubmitTelegram2fa` builds the request body as `"{\"pwd_code\":\"" + TelegramCodeInput.text + "\"}"`. A Telegram cloud password is free-form text; any password containing a double quote or backslash produces malformed JSON (and arbitrary JSON structure injection, though only self-inflicted), so the wappi call fails and the user is locked in a «Неверный пароль» loop with no way to complete Telegram auth. The neighboring `auth_code` concat (Manager.cs:2359) is effectively safe because the code is numeric, but the password is not. The project's own networking rule mandates `JsonConvert` for payloads.
**Fix:**
```csharp
string jsonBody = JsonConvert.SerializeObject(new { pwd_code = TelegramCodeInput.text });
```

### WR-03: `dialogType == "chat"` groupness rule is not channel-gated — one WA-side `type` field away from flipping every WhatsApp row to group rendering

**Status:** FIXED (e38d450) — `dialogType == "chat"` now trusted only for suffix-less ids (`chatId == null || chatId.IndexOf('@') < 0`). Three regression tests added to ChatIdFormatTests: WA `@c.us` + type "chat" stays non-group, WA `@g.us` + type "chat" stays group (suffix wins), null id + type "chat" stays group (TG-shaped). Every current WA case preserved (suffix/flag paths untouched).

**File:** `Assets/Scripts/Chat/ChatIdFormat.cs:54-55`, `Assets/Scripts/Main/ChatManager.cs:296`
**Issue:** `ChatIdFormat.IsGroup(chatId, dialogType, dialogIsGroup)` returns true whenever `dialogType == "chat"`, relying entirely on the assumption that the WhatsApp `chats/filter` payload never populates `ChatDialog.type` (comment in ChatDialog.cs: "api never sends them"). That assumption is load-bearing and untestable by the unit suite: if Wappi ever adds a `type` field to the api-side dialog object (where "chat" plausibly means an ordinary conversation, not a group), every WhatsApp 1:1 row would silently become a "group" — sender prefixes on all list rows and sender headers on all bubbles. A one-expression guard removes the entire risk class since WhatsApp ids always carry an `@` suffix and Telegram ids never do.
**Fix:**
```csharp
public static bool IsGroup(string chatId, string dialogType, bool dialogIsGroup) =>
    IsGroup(chatId) || dialogIsGroup ||
    // "chat" == Telegram group; only trust it for suffix-less (Telegram numeric) ids
    (dialogType == "chat" && (chatId == null || chatId.IndexOf('@') < 0));
```

## Info

### IN-01: `SetActiveChannel` never clears `currentChatId` / `_activeChatCache`

**Status:** FIXED (470beeb) — `currentChatId = null; _activeChatCache = null;` right after the `ShowChatList()` call in `SetActiveChannel`. Verified safe: `ShowChatList` (ChatManager.cs:430-452) reads only SwipeToBack/MessageListPanel/AudioController, and downstream `ParseChatsJson` treats a null `openChatId` as "no chat open" — correct post-switch.

**File:** `Assets/Scripts/Main/ChatManager.Channel.cs:56`, `Assets/Scripts/Main/ChatManager.cs:139,501`
**Issue:** `ShowChatList()` slides the panel out but `currentChatId` is only ever assigned in `SelectChat` (ChatManager.cs:501) and never nulled, so after a channel switch with a chat open it keeps pointing at the other channel's chat. Impact today is low (all send paths require the visible panel; WA jids and TG numeric ids can't collide in `IncomingNotifyPolicy`), but the public `CurrentChatId` accessor (ChatManager.Suggestions.cs) and `TryGetRecentMessages` (ChatManager.RecentMessages.cs) will serve stale cross-channel state until the next `SelectChat`.
**Fix:** In `SetActiveChannel`, after `ShowChatList()`: `currentChatId = null; _activeChatCache = null;` (verify no ShowChatList-side reads need it first).

### IN-02: Telegram group chats will not render per-bubble sender headers

**Status:** DEFERRED — capture-gated to 05-06/Phase 6 scope: resolving groupness from the owning ChatViewModel in the message view is a pipeline change, not a review fix.

**File:** `Assets/Scripts/UI/MessageListView.cs:522,789`
**Issue:** The per-bubble group check is suffix-only (`ChatIdFormat.IsGroup(vm.chatId)`); Telegram group ids are numeric, so `isGroup` is always false in the message view even though the chat list row correctly knows groupness via dialog type. Known scope limitation (the message view only has a chatId), but it will surface as "no sender names in TG group chats" on device.
**Fix:** Resolve groupness from the owning `ChatViewModel` (`chatLookup`/`GetChat(vm.chatId)?.IsGroup ?? ChatIdFormat.IsGroup(vm.chatId)`) — flag for Phase 6 if deferred.

### IN-03: Telegram chat delete is a silent no-op while the swipe affordance and confirm dialog remain fully reachable

**Status:** DEFERRED — the swipe/dialog UI gate is Phase 6 by design (`ActiveChannelSupportsChatDelete` is already public and ready to consume).

**File:** `Assets/Scripts/Main/ChatManager.DeleteChat.cs:29`, `Assets/Scripts/UI/ChatListView.cs:190`, `Assets/Scripts/UI/ChatDeleteConfirm.cs:49`
**Issue:** The guard correctly prevents a destructive call where no tapi endpoint exists, and the code comment correctly defers the UI gate to Phase 6 (verified present at DeleteChat.cs:14-18). Interim UX: on Telegram the user can swipe a row, get the confirm dialog, confirm — and nothing happens. Acceptable per plan, but Phase 6 must land the `ActiveChannelSupportsChatDelete` gate on the swipe/dialog before Telegram ships (the property is already public and ready to consume).
**Fix:** Track as a Phase 6 requirement; optionally gate `ChatListView`'s swipe-delete affordance on `ChatManager.Instance.ActiveChannelSupportsChatDelete` now (one-line check).

### IN-04: 2FA network-error path displays the raw server `detail` string on the submit button

**Status:** DEFERRED — RU error-copy mapping belongs to the one-pass empty-state/auth localization sweep (with IN-09) before store screenshots.

**File:** `Assets/Scripts/Main/Manager.cs:2498`
**Issue:** On a protocol error, `errorMsg = errDetail` puts an untranslated, potentially technical wappi string (e.g. "profile not found") on a Russian-language button, with no length cap.
**Fix:** Map known details to fixed RU copy and fall back to «Ошибка. Попробуйте ещё раз»; at minimum truncate.

### IN-05: Late QR-poll `2fa` response can wipe a half-typed cloud password

**Status:** FIXED (1a7ec63) — `EnterTelegram2faMode()` early-returns when `_telegram2faMode` is already true (both call sites are enter-password-mode transitions; texts/input are already set on re-entry).

**File:** `Assets/Scripts/Main/Manager.cs:2129-2133,2193-2206,2441-2456`
**Issue:** `ShowTelegramAuth` activates the QR panel and code panel together; `OpenTelegramQRPanel` polls up to 5×/3s. If the user enters 2FA mode via the phone-code path and a still-pending QR poll then returns `detail:"2fa"`, `ShowTelegram2faFromQr` → `EnterTelegram2faMode` runs again and `TelegramCodeInput.text = ""` erases whatever password the user has typed. Narrow window (~15s), but free to close.
**Fix:** Early-return when already in password mode: `private void EnterTelegram2faMode() { if (_telegram2faMode) return; … }` (or guard in `ShowTelegram2faFromQr`).

### IN-06: `ChatDialogTime.Resolve` uses culture-sensitive `DateTimeOffset.TryParse`

**Status:** DEFERRED — the review itself scopes this as future hardening, not this phase: changing the parse culture alters edge-case behavior and requires a WA-parity check first.

**File:** `Assets/Scripts/Chat/ChatDialogTime.cs:16-17`
**Issue:** Inherited verbatim from the legacy WA parse (byte-identical is deliberate), and RFC3339 strings parse under all cultures — but the invariant-culture overload is the safer idiom if wappi ever emits a non-ISO shape.
**Fix (future hardening, not this phase):** `DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out …)` — only alongside a WA-parity check, since it changes edge-case behavior.

### IN-07: `ChatIdFormat.Recipient` uses `Replace` (removes all occurrences) while the doc says "strip the suffix"; `DisplayFallback` hardcodes the magic length 5

**Status:** WONTFIX — legacy byte-parity is deliberately preserved this phase; behavior is identical for all real ids, so a rewrite adds regression surface with no user-visible gain.

**File:** `Assets/Scripts/Chat/ChatIdFormat.cs:25,38`
**Issue:** `id.Replace(OneToOneSuffix, "")` removes every occurrence, not just the trailing suffix — preserved byte-identical from the legacy code (deliberate), but it diverges from the docstring for pathological ids. `DisplayFallback` slices `id.Length - 5` instead of `OneToOneSuffix.Length`.
**Fix:** `return id.Substring(0, id.Length - OneToOneSuffix.Length);` for both (behavior-identical for all real ids), or update the doc to note Replace-all semantics.

### IN-08: tapi parity assumptions are unit-untestable — add to the Telegram device/e2e checklist

**Status:** DEFERRED → Telegram device/e2e UAT checklist: (1) send a TG reply, verify tick Pending→Sent; (2) send one media of each kind; (3) verify unread clears after opening a TG chat.

**File:** `Assets/Scripts/Main/ChatManager.cs:1937-1941,1968-1980,2039-2043`, `Assets/Scripts/Chat/WappiMediaRequestFactory.cs:22-33`
**Issue:** Several tapi behaviors are asserted only by construction, not by any test that talks to tapi: (1) `message/reply`'s response is parsed as `WappiSendTextResponse {status, message_id}` — if tapi wraps it differently, the tempId→realId swap silently fails and a delivered reply renders Failed; (2) media endpoints `message/img|video|document/send` are assumed to exist symmetrically under `tapi/sync/`; (3) mark-read body `{message_id}` without `mark_all`. All are reasonable per the phase research, but none can regress visibly until the live pass.
**Fix:** Add explicit line items to the pending Telegram device/e2e pass: send a TG reply and verify the tick transitions Pending→Sent; send one media of each kind; verify unread clears after opening a TG chat.

### IN-09: New `BotHasNoTelegram` empty-state copy is English in an otherwise Russian UI

**Status:** DEFERRED — localize all three empty-state cases to RU in one pass (pre-existing debt shared with `NoBotsExist`/`BotHasNoWhatsApp`) before store screenshots.

**File:** `Assets/Scripts/UI/EmptyStateView.cs:131-133`
**Issue:** "Telegram not connected" / "Connect Telegram…" matches the pre-existing English `NoBotsExist`/`BotHasNoWhatsApp` cases, so it is consistent — but the rest of the app ships RU copy («Бот работает», «Получить код»). Extending the inconsistency rather than fixing it.
**Fix:** Localize all three empty-state cases to RU in one pass (pre-existing debt; fine to defer, but do it before store screenshots).

### IN-10: `PostTextMessageRoutine`'s default `channel` parameter is a silent-WhatsApp footgun; retry tests rebuild the URL instead of exercising the production predicate

**Status:** DEFERRED (noted) — test-shape/refactor item, not a defect: both current call sites pass the channel explicitly; the required-param + `SendPath` seam refactor is queued for a later hardening pass.

**File:** `Assets/Scripts/Main/ChatManager.cs:1927`, `Assets/Tests/Editor/Chat/OutboxRetryChannelTests.cs:14-15`
**Issue:** `ChatChannel channel = ChatChannel.WhatsApp` means any future call site that forgets the argument silently posts a Telegram profile to the api base (the exact bug class this phase eliminates). Relatedly, `OutboxRetryChannelTests` constructs the retry URL inside the test helper, so the production `telegramReply` predicate (`channel == Telegram && quotedMessageId != null`) and `RetryRoutine`'s cast are never executed by a test — the tests lock `WappiEndpoints` but not the branch that chooses `message/reply` vs `message/send`.
**Fix:** Make `channel` a required parameter (both current call sites already pass it), and extract the reply-endpoint decision to a pure seam, e.g. `static string SendPath(ChatChannel c, string quotedId)`, then point the tests at it.

---

_Reviewed: 2026-07-12T19:02:49Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
