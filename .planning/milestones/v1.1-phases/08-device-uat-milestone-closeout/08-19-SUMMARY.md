---
phase: 08-device-uat-milestone-closeout
plan: 19
subsystem: ui
tags: [telegram, whatsapp, sync-window, playerprefs, syncing-cover, tdd]

requires:
  - phase: 08-16 (round-2 device re-verify)
    provides: D13 finding + owner decision "cover only, remove pill" (this plan = half a)
  - phase: 05-02
    provides: ChatChannel seam (ActiveChannel, OnActiveChannelChanged, SetActiveChannel choreography)
provides:
  - Per-channel post-creation sync window ({bot}TelegramSyncUntil sibling of {bot}WhatsappSyncUntil, same 300s constant)
  - Channel-aware gate IsChannelSyncing + SyncUntilSuffixFor/IsSyncingRawValue pure seams (IsWhatsAppSyncing delegates byte-identically)
  - SyncingState cover now fires for Telegram with RU copy (title/body/footnote + RU countdown buckets)
affects: [08-20 (D9 pill removal), 08-21 (device re-verify), any future late-auth window stamping]

tech-stack:
  added: []
  patterns:
    - "Per-channel PlayerPrefs key via pure static suffix resolver (SyncUntilSuffixFor), WhatsApp suffix byte-identical"
    - "Pure static members on MonoBehaviour/manager classes (SyncingView.FormatCountdownFor, ChatManager.IsSyncingRawValue) for EditMode testability without instances"

key-files:
  created: []
  modified:
    - Assets/Scripts/Main/Manager.cs
    - Assets/Scripts/Main/ChatManager.BotState.cs
    - Assets/Scripts/UI/SyncingView.cs
    - Assets/Scripts/Main/Bot.cs
    - Assets/Tests/Editor/Chat/WhatsAppSyncTests.cs

key-decisions:
  - "Reuse the existing SyncingState overlay + OnWhatsAppSyncing/OnWhatsAppSyncReady events for both channels — no scene change, no event rename (keeps 08-19 entirely off ChatManager.cs, which 08-20 owns)"
  - "Channel-switch handler hides unconditionally (mirror of HandleActiveBotChanged); SetActiveChannel's BeginLoadForActiveBot re-fires in the same synchronous stack with its own profile-validity guards"
  - "Telegram countdown buckets are Russian via SyncingView.FormatCountdownFor; WhatsAppSyncGate.FormatCountdown untouched (English buckets stay pinned by its tests)"

patterns-established:
  - "Per-channel window keys: {botName}{Channel}SyncUntil, missing/unparseable ⇒ fail-safe not-syncing"

requirements-completed: []

duration: ~45min
completed: 2026-07-17
---

# Phase 08 Plan 19: Telegram Post-Creation Cover Parity (D13a) Summary

**Freshly-created Telegram bots now get the same full-page cover (spinner + ~5-min time-based progress slider + countdown) WhatsApp shows over the chats list — via a per-channel PlayerPrefs sync window, a channel-aware gate on the shared SyncingState overlay, and Russian cover copy on Telegram.**

## Performance

- **Duration:** ~45 min (including two bridge-verified filtered runs + full-suite wait)
- **Started:** 2026-07-17T15:23:57Z
- **Completed:** 2026-07-17T16:09:00Z (full-suite completion pending Editor focus — see Testing)
- **Tasks:** 2 (1 diagnosis + 1 TDD implementation)
- **Files modified:** 5

## Accomplishments

- **Task 1 (diagnosis)** confirmed the reuse premise and pinned the three WhatsApp-only gates (below) — locking the code-only fix (no scene stamp, no builder).
- **Task 2 (TDD)** parameterized the stamp/gate/copy per channel with WhatsApp byte-identical, RED-verified-failing then GREEN-verified-fresh via the in-Editor bridge.

## Task 1 Evidence (reuse + the three gates)

**(a) Shared overlay ⇒ no new scene object:**
- `grep -c "Screen_Telegram" Assets/Scenes/Main.unity` → **0** (Phase 6 removed it; the Telegram list renders in the same `Screen_Whatsapp/ChatsPanel` via the channel switcher).
- `grep -c "m_Name: SyncingState" Assets/Scenes/Main.unity` → **1** (exactly one cover instance).
- `SyncingStateBuilder.cs` builds it full-stretch (`StretchFull`) with an opaque white `Image` (`raycastTarget = true`), inserted above the chat list and below TopBar/Sheet_BotSwitcher (`PlaceAboveListBelowHeader`) — it covers whatever list the shared panel is showing, Telegram included.

**(b) The three WhatsApp-only gates (pre-fix):**
1. **Stamp** — `Manager.cs:1443` `if (useWhatsapp)` → `:1448` writes `{bot}WhatsappSyncUntil`; no Telegram stamp existed. `useTelegram` in scope at `:1412`/`:1428`.
2. **Read key** — `ChatManager.BotState.cs:216` `SyncUntilKeySuffix = "WhatsappSyncUntil"`, read at `:228` by `IsWhatsAppSyncing` (`:224`).
3. **Fire** — `ChatManager.BotState.cs:275` (`BeginLoadForActiveBot`) gated `ActiveChannel == ChatChannel.WhatsApp && IsWhatsAppSyncing(...)`; same-shaped gates at `:248` (`ComputeCurrentEmptyState`) and `:310` (`RefreshActiveBotChats`).

**(c) Late-channel auth is out of scope with exact parity:** zero `SyncUntil` hits in any `BotSettings*.cs` — WhatsApp itself only stamps in the create-bot wizard tail, so stamping Telegram only in the wizard is true parity (see Follow-ups).

**(d)** `SetActiveChannel` (ChatManager.Channel.cs) stops `_syncWaitRoutine` (`:63`), fires `OnActiveChannelChanged` (`:76`), then unconditionally calls `BeginLoadForActiveBot` (`:97`) in the same synchronous stack — the basis for the hide-on-switch handler design.

## Task Commits

1. **Task 1: diagnosis** — no commit (no files changed; evidence above)
2. **Task 2 RED:** `3cd6537` — `test(08-19): add failing per-channel sync-window + RU countdown tests (D13a RED)`
3. **Task 2 GREEN:** `91b97b3` — `feat(08-19): give Telegram the WhatsApp-parity post-creation cover (D13a GREEN)`

## Files Created/Modified

- `Assets/Scripts/Main/Manager.cs` — wizard tail now also stamps `{bot}TelegramSyncUntil` (now + `ChatManager.WhatsAppSyncWindowSeconds`, same 300s) under `if (useTelegram)`; the WhatsApp stamp block is untouched (diff-verified).
- `Assets/Scripts/Main/ChatManager.BotState.cs` — pure `SyncUntilSuffixFor(channel)` + `IsSyncingRawValue(raw, now, out until)` seams; instance `IsChannelSyncing(botId, channel, out until)`; `IsWhatsAppSyncing` delegates with `ChatChannel.WhatsApp` (byte-identical: same key, parse, gate math); the three fire gates broadened to the ACTIVE channel (`BeginLoadForActiveBot`, `ComputeCurrentEmptyState`, `RefreshActiveBotChats`), reusing the existing `OnWhatsAppSyncing` event + `WaitForWhatsAppSyncRoutine` (channel-neutral math) for both channels.
- `Assets/Scripts/UI/SyncingView.cs` — `ApplyCopy` channel-aware (Telegram: «Готовим всё к работе» / «Импортируем ваши чаты и сообщения из Telegram.» / RU footnote; WhatsApp English byte-identical) and re-applied on every show; `FormatCountdownFor` gives Telegram RU countdown buckets («Завершаем…» / «Осталось меньше минуты» / «Осталось около N мин») while WhatsApp delegates to `WhatsAppSyncGate.FormatCountdown` verbatim; subscribed to `OnActiveChannelChanged` (hide-on-switch; re-shown by the load path when the new channel is mid-window); OnEnable catch-up now channel-aware.
- `Assets/Scripts/Main/Bot.cs` — `DeleteBot` also deletes `TelegramSyncUntil` (deviation, below).
- `Assets/Tests/Editor/Chat/WhatsAppSyncTests.cs` — +8 tests: `ChannelSyncGateTests` (key contract WA/TG, parse+gate core incl. fail-safe unparseable) and `SyncingCountdownCopyTests` (WA byte-identity pins + RU buckets).

## Decisions Made

- **Events keep their WhatsApp names** (`OnWhatsAppSyncing`/`OnWhatsAppSyncReady`) while serving both channels — renaming would ripple into `ChatManager.cs`, which plan 08-20 owns. Documented at the fire site.
- **Hide-on-switch instead of re-evaluate-in-handler:** the plan sketched the `OnActiveChannelChanged` handler re-evaluating the gate and calling `HandleSyncing`/`Hide` itself. Implemented as unconditional `Hide()` (exact mirror of the existing `HandleActiveBotChanged` pattern) because `SetActiveChannel` calls `BeginLoadForActiveBot` in the same synchronous stack right after the event — the load path re-fires `OnWhatsAppSyncing` for a mid-window channel through its own profile-validity guards. Same outcome, no duplicated guard logic, and a stale window on a logged-out profile can never paint the cover over the connect empty-state.
- **RU countdown formatter lives in SyncingView** (`FormatCountdownFor`), not `WhatsAppSyncGate` — the plan freezes the gate's signatures/tests (English buckets pinned); a half-Russian cover with an English countdown would have failed the channel-appropriate-copy bar.
- **Same 300s window constant** (`ChatManager.WhatsAppSyncWindowSeconds`) reused for Telegram — the owner asked for parity ("~5-min"); no new constant.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] DeleteBot cleanup for the new per-bot key**
- **Found during:** Task 1 (diagnosis) / applied in Task 2 GREEN
- **Issue:** `Bot.DeleteBot()` deletes `{bot}WhatsappSyncUntil` (Bot.cs:185) but the plan's file list omitted Bot.cs — the new `{bot}TelegramSyncUntil` key would orphan on bot deletion, violating the bot-persistence skill's full-teardown mandate (and, on a recreated bot name, a stale window could mis-show the cover).
- **Fix:** one line — `PlayerPrefs.DeleteKey(transform.name + "TelegramSyncUntil");` next to its WhatsApp sibling.
- **Files modified:** Assets/Scripts/Main/Bot.cs (outside the plan's `files_modified` list; ChatManager.cs still untouched, per the hard constraint)
- **Verification:** part of the GREEN compile + suite run
- **Committed in:** `91b97b3`

---

**Total deviations:** 1 auto-fixed (Rule 2). **Impact:** correctness-only, one line; no scope creep. The plan's "no ChatManager.cs" constraint holds — per-commit file lists: `3cd6537` = ChatManager.BotState.cs + SyncingView.cs + WhatsAppSyncTests.cs; `91b97b3` = Bot.cs + ChatManager.BotState.cs + Manager.cs + SyncingView.cs. No ChatManager.cs, no deletions. (An unrelated commit `8c0ac6e` from a parallel session — phase-11 onboarding registration — interleaved between the 08-18 baseline and this plan's commits.)

## Testing

TDD gate compliance: RED commit (`3cd6537`) precedes GREEN (`91b97b3`); RED verified genuinely failing.

- **Baseline (pre-RED):** full suite **1134/1134 green** — completed 2026-07-17T15:23Z on the in-Editor bridge (this run also retroactively confirms 08-17/08-18's "pending green": their changes are in that green baseline).
- **RED:** filtered `Sync` run — 31 matched, **4 failed exactly as designed** (TG suffix, future-epoch gate, past-epoch out-value, RU buckets), 27 byte-identity pins passed. Fresh: `editorAssemblyWrittenUtc 15:31:33Z` postdates the RED edits.
- **GREEN:** filtered `Sync` run — **31/31 passed**. Fresh: `editorAssemblyWrittenUtc 15:37:28Z` postdates all GREEN edits (compile gate also covers Manager.cs/Bot.cs).
- **Full suite (post-GREEN): PENDING user GREEN.** The run was armed and consumed (trigger picked up 15:39:31Z, status `running`), but did not complete within ~28 min of polling — the Editor evidently lost focus mid-run (the prior full run needed ~17 focused minutes). Expected result: **1142/1142** (1134 baseline + 8 new). The in-flight run completes whenever the Editor regains focus and writes `Temp/claude/test-summary.json` — **08-20 (or the orchestrator) should read that file before new work and treat a `completed/Passed` with `total: 1142` as this plan's confirmation.** Residual risk is low: both filtered runs were verified FRESH (compile gate covers all five files), `IsWhatsAppSyncing` is a byte-identical delegation, and no non-Sync test exercises the changed instance/MonoBehaviour paths.

## Known Stubs

None — no placeholder text, no hardcoded empty values wired to UI, no unwired data sources added. (The two RED stubs were replaced in the GREEN commit; grep for "RED stub" returns nothing.)

## Threat Flags

None — no new network endpoints, auth paths, or schema changes. The one new surface (`{bot}TelegramSyncUntil` PlayerPrefs key) is inside the plan's threat model: T-08-19-01 fail-safe verified by `IsSyncingRawValue_Unparseable_FailSafeFalse`; T-08-19-02 upheld (no new coroutines — the single `_syncWaitRoutine` is reused, stopped before restart and on switches); T-08-19-03 upheld (WhatsApp stamp/gate/copy byte-identical, `WhatsAppSyncGate.cs` diff-empty vs baseline).

## Issues Encountered

- The in-Editor bridge was responsive for both filtered runs (verified fresh RED failure + fresh GREEN pass — better than the full stalls 08-17/08-18 hit), but the Editor lost focus during the ~17-min FULL suite, leaving it in-flight. Handled per protocol: honest "pending user GREEN" with a handoff instruction for 08-20 (see Testing).

## Follow-ups (documented, not fixed)

- **Late-channel auth stamps no window on EITHER channel:** connecting WhatsApp or Telegram later via BotSettings never stamps a sync window (verified: zero `SyncUntil` writes outside the wizard), so no cover shows after a late auth. Today that is exact WA↔TG parity; if the owner wants the cover there, both channels need a stamp at the auth-completion sites (new plan).
- **08-18's latent zero-bots reason bug** (`BeginLoadForActiveBot` resolves a missing bot to a connect-state reason instead of `NoBotsExist`) — outside this plan's gates; left as documented in 08-18.
- **Device confirmation** of the Telegram cover (fresh TG bot → cover with RU copy + slider → elapses to the TG list; WhatsApp unchanged) rides **08-21**.

## Next Phase Readiness

- 08-20 (D13b: remove the D9 «Синхронизация…» pill) can proceed — this plan deliberately never touched `ChatManager.cs` or `ChatListSyncIndicator`; the pill and cover cannot show together during the window (the sync gates suppress `SyncAllChats` while the cover owns the panel).
- 08-21 device re-verify covers D13a on-device.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*

## Self-Check: PASSED

- All 5 modified files exist on disk (Manager.cs, ChatManager.BotState.cs, SyncingView.cs, Bot.cs, WhatsAppSyncTests.cs) + this SUMMARY.
- Commits `3cd6537` (RED) and `91b97b3` (GREEN) exist; per-commit file lists contain only plan files (+ documented Bot.cs deviation); no deletions.
- No "RED stub" markers remain in Assets/.
- Acceptance greps re-verified post-commit (TG stamp under useTelegram; IsChannelSyncing + three gates; SyncingView channel subscription + copy; WhatsAppSyncGate.cs diff-empty; ChatManager.cs untouched).
- TDD gate: test commit precedes feat commit; RED verified genuinely failing (4 designed failures, fresh assembly stamp).
- Full-suite freshness gate: NOT green yet — honestly reported as pending user GREEN (in-flight bridge run; expected 1142/1142).
