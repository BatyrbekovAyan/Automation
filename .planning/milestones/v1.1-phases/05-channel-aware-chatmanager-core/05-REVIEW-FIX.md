---
phase: 05-channel-aware-chatmanager-core
fixed_at: 2026-07-13T00:00:00Z
review_path: .planning/phases/05-channel-aware-chatmanager-core/05-REVIEW.md
iteration: 1
findings_in_scope: 13
fixed: 5
skipped: 8
status: partial
tests: 891/891 GREEN (headless EditMode; baseline 888 + 3 new WR-03 regression tests)
---

# Phase 5: Code Review Fix Report

**Fixed at:** 2026-07-13
**Source review:** .planning/phases/05-channel-aware-chatmanager-core/05-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 13 (3 Warning + 10 Info; fix policy = WR-* all, IN-* cheap/clearly-correct only)
- Fixed: 5 (WR-01, WR-02, WR-03, IN-01, IN-05)
- Skipped (deferred/wontfix per fix policy): 8 (IN-02, IN-03, IN-04, IN-06, IN-07, IN-08, IN-09, IN-10)
- Verification: full headless EditMode suite GREEN — 891/891 (Editor closed, `Tools/run-tests-headless.sh`)

## Fixed Issues

### WR-01: Channel/bot switch mid-drain permanently stalls quote/reaction resolution

**Files modified:** `Assets/Scripts/Main/ChatManager.QuoteResolve.cs`, `Assets/Scripts/Main/ChatManager.Channel.cs`, `Assets/Scripts/Main/ChatManager.BotState.cs`
**Commit:** da4543b
**Applied fix:** Added `ClearResolveQueues()` (clears `_quoteResolveQueue`/`_quoteResolveInFlight`/`_quoteWaiters`/`_quoteResolveDraining` and `_reactionResolveQueue`/`_reactionResolveInFlight`/`_reactionResolveDraining`) and call it from BOTH `SetActiveChannel` and `SetActiveBot` right after `ClearMediaDownloadQueue()` — the SetActiveBot gap was pre-existing; same one-shape fix in both switchers. WhatsApp event order unchanged: the reset only clears state the just-executed `StopAllCoroutines()` orphaned.
**Test note:** No EditMode test added — the flag-reset contract is private coroutine bookkeeping on the scene ChatManager MonoBehaviour (StopAllCoroutines vs drain-body cleanup); no pure seam exists without restructuring the drain workers into a testable class, which is out of review-fix scope.

### WR-02: Telegram 2FA body built by string concatenation

**Files modified:** `Assets/Scripts/Main/Manager.cs`
**Commit:** fe35c10
**Applied fix:** `string jsonBody = JsonConvert.SerializeObject(new { pwd_code = TelegramCodeInput.text });` — quotes/backslashes in a cloud password now escape correctly. Never-log/never-persist invariants untouched (field still cleared immediately after the request on every path; body never logged).

### WR-03: `dialogType == "chat"` groupness rule not channel-gated

**Files modified:** `Assets/Scripts/Chat/ChatIdFormat.cs`, `Assets/Tests/Editor/Chat/ChatIdFormatTests.cs`
**Commit:** e38d450
**Applied fix:** Full `IsGroup` overload now trusts `dialogType == "chat"` only for suffix-less (Telegram numeric) ids: `(dialogType == "chat" && (chatId == null || chatId.IndexOf('@') < 0))`. Three regression tests added: WA `@c.us` + type "chat" stays non-group; WA `@g.us` + type "chat" stays group (suffix wins); null id + type "chat" stays group (TG-shaped). All pre-existing WA cases preserved.

### IN-01: `SetActiveChannel` never clears `currentChatId` / `_activeChatCache`

**Files modified:** `Assets/Scripts/Main/ChatManager.Channel.cs`
**Commit:** 470beeb
**Applied fix:** `currentChatId = null; _activeChatCache = null;` after the `ShowChatList()` call. Safety verified before applying: `ShowChatList` (ChatManager.cs:430-452) never reads `currentChatId`, and `ParseChatsJson`'s `IncomingNotifyPolicy` treats a null `openChatId` as "no chat open" — the correct post-switch state.

### IN-05: Late QR-poll `2fa` response can wipe a half-typed cloud password

**Files modified:** `Assets/Scripts/Main/Manager.cs`
**Commit:** 1a7ec63
**Applied fix:** `EnterTelegram2faMode()` early-returns when `_telegram2faMode` is already true. Both call sites (`ShowTelegram2faFromQr`, the code-submit 2FA branch) are enter-password-mode transitions; on re-entry the texts/input are already configured, so skipping is correct.

## Skipped Issues

### IN-02: Telegram group chats will not render per-bubble sender headers
**File:** `Assets/Scripts/UI/MessageListView.cs:522,789`
**Reason:** deferred — capture-gated to 05-06/Phase 6 scope; resolving groupness from the owning ChatViewModel in the message view is a pipeline change, not a review fix.

### IN-03: Telegram chat delete is a silent no-op with reachable swipe/confirm UI
**File:** `Assets/Scripts/Main/ChatManager.DeleteChat.cs:29`
**Reason:** deferred — UI gate is Phase 6 by design; `ActiveChannelSupportsChatDelete` already public and ready to consume.

### IN-04: 2FA error path displays raw server `detail` on the submit button
**File:** `Assets/Scripts/Main/Manager.cs:2498`
**Reason:** deferred — RU error-copy mapping belongs to the one-pass localization sweep with IN-09, before store screenshots.

### IN-06: Culture-sensitive `DateTimeOffset.TryParse` in `ChatDialogTime.Resolve`
**File:** `Assets/Scripts/Chat/ChatDialogTime.cs:16-17`
**Reason:** deferred — review itself scopes it as future hardening; changing parse culture alters edge cases and needs a WA-parity check first.

### IN-07: `Recipient` Replace-all semantics / `DisplayFallback` magic length 5
**File:** `Assets/Scripts/Chat/ChatIdFormat.cs:25,38`
**Reason:** wontfix — legacy byte-parity deliberately preserved this phase; behavior identical for all real ids.

### IN-08: tapi parity assumptions unit-untestable
**File:** `Assets/Scripts/Main/ChatManager.cs:1937-2043`, `Assets/Scripts/Chat/WappiMediaRequestFactory.cs:22-33`
**Reason:** deferred → Telegram device/e2e UAT checklist (TG reply tick Pending→Sent; one media of each kind; unread clears on open).

### IN-09: `BotHasNoTelegram` empty-state copy is English
**File:** `Assets/Scripts/UI/EmptyStateView.cs:131-133`
**Reason:** deferred — localize all three empty-state cases to RU in one pass (pre-existing debt) before store screenshots.

### IN-10: Default `channel` parameter footgun; retry tests rebuild URL instead of exercising production predicate
**File:** `Assets/Scripts/Main/ChatManager.cs:1927`, `Assets/Tests/Editor/Chat/OutboxRetryChannelTests.cs:14-15`
**Reason:** deferred (noted) — test-shape/refactor item, not a defect; both current call sites pass the channel explicitly; required-param + `SendPath` seam queued for a later hardening pass.

---

_Fixed: 2026-07-13_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
