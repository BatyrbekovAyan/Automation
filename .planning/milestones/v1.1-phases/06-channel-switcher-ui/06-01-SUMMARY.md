---
phase: 06-channel-switcher-ui
plan: 01
subsystem: ui
tags: [unity, csharp, channel-switcher, telegram, topbar-binder, tab-index, tdd, pure-seam]

# Dependency graph
requires:
  - phase: 05-02
    provides: "ChatManager.ActiveChannel + SetActiveChannel + OnActiveChannelChanged + ResolveChannelForBot (per-bot channel persistence) + BotHasNoTelegram empty state"
  - phase: 05-01
    provides: "ChatChannel enum (WhatsApp=0, Telegram=1)"
provides:
  - "ChannelSwitcherModel — pure static selection/muted decision seam (ChannelChipState struct)"
  - "ChannelSwitcherView — event-driven TopBar segmented-pill binder driving ChatManager.SetActiveChannel; serialized-ref field-name contract for the 06-02 builder"
  - "BottomTabManager.BotsTabIndex == 2 (post-restructure constant; WhatsAppTabIndex stays 0)"
  - "TabIndexShiftTests — guard locking BotsTabIndex==2 / WhatsAppTabIndex==0 + TabRefreshGate rule"
affects: [06-02, phase-7-suggestions-dashboard]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure static decision seam (ChannelSwitcherModel) — unit-testable, no scene, mirrors ChannelResolver/TabRefreshGate"
    - "Event-driven TopBar binder (OnEnable subscribe / OnDisable unsubscribe, late-activation catch-up Refresh, no Update polling) — ReplyModeToggleBinder precedent"
    - "Connectivity predicate reuse (Bot.UnauthedProfileSentinel) + alpha-fade-never-tint for muted brand chips — BotSwitcherRowView precedent"
    - "Compile-time-const guard test: inlined const flips expected/actual on revert, locking a runtime index seam"

key-files:
  created:
    - Assets/Scripts/UI/ChannelSwitcherModel.cs
    - Assets/Scripts/UI/ChannelSwitcherView.cs
    - Assets/Tests/Editor/Chat/ChannelSwitcherModelTests.cs
    - Assets/Tests/Editor/Chat/TabIndexShiftTests.cs
  modified:
    - Assets/Scripts/Main/BottomTabManager.cs

key-decisions:
  - "ChannelSwitcherModel is a pure static class + readonly struct (no MonoBehaviour): Selected = equality only, Muted = own-channel connectivity only, never suppressing muted when selected."
  - "The view reads ChatManager.ActiveChannel as the sole source of truth for selection (05-02 auto-resolves per bot) — SWITCH-03 flows through read-only, zero local persistence."
  - "Selected chip = brand fill (WA #25D366 / TG #2AABEE) + white label; muted = ~40% alpha fade on label/icon (BotSwitcherRowView treatment); muted chips stay interactable (SWITCH-02)."
  - "BotsTabIndex flipped 3->2 in lockstep with 06-02's Telegram-tab removal; a guard test locks the post-restructure contract."
  - "Editor-builder hardcoded tab indices (NavRestructureBuilder, DashboardPageBuilder) documented rather than annotated — they are one-time migrations owned by 06-02."
  - "SWITCH-01/SWITCH-04 deferred to 06-02 (shared requirements it finishes in the scene); 06-01 marks only SWITCH-02/SWITCH-03."

patterns-established:
  - "Pattern 1: A TopBar control's logic ships as a pure model seam + an event-driven binder in 06-01; the visible pill + serialized-ref wiring lands in a 06-02 headless Editor builder against the binder's declared field-name contract."
  - "Pattern 2: A tab-index constant shift is committed with a locking guard test one plan ahead of the matching scene mutation, so the runtime seam and the scene stay pinned together."

requirements-completed: [SWITCH-02, SWITCH-03]

# Metrics
duration: 21min
completed: 2026-07-13
---

# Phase 6 Plan 01: Channel Switcher Runtime + Tab-Index Audit Summary

**The runtime half of the channel switcher: a pure `ChannelSwitcherModel` (selected/muted per chip), an event-driven `ChannelSwitcherView` TopBar binder that drives `ChatManager.SetActiveChannel` (muted-but-tappable chips reach the connect empty state), and the `BotsTabIndex` 3->2 shift locked by a guard test — no scene mutation, EditMode suite 900/900 green.**

## Performance

- **Duration:** ~21 min
- **Started:** 2026-07-13T10:12:37Z
- **Completed:** 2026-07-13T10:34:00Z
- **Tasks:** 3 (Task 1 TDD: RED + GREEN)
- **Files modified:** 5 (4 created, 1 modified)

## Accomplishments

- `ChannelSwitcherModel.StateFor(chip, active, waConnected, tgConnected)` — a pure static seam returning `ChannelChipState { Selected, Muted }`: Selected is pure equality (`chip == active`), Muted is pure connectivity (WA chip muted iff `!waConnected`, TG chip iff `!tgConnected`), and a chip can be BOTH selected and muted (active channel on a disconnected profile). Full A–E matrix green.
- `ChannelSwitcherView` — a MonoBehaviour binder that subscribes to `OnActiveBotChanged` + `OnActiveChannelChanged` in OnEnable (with an immediate late-activation catch-up `Refresh`), unsubscribes in OnDisable, and never polls. `OnChipTapped` routes straight to `ChatManager.SetActiveChannel` (which no-ops when unchanged); muted chips are never made non-interactable, so tapping an unconnected channel surfaces its 05-02 connect empty state (SWITCH-02).
- Selection is read live from `ChatManager.ActiveChannel` (05-02 auto-resolves it per bot), so per-bot persistence + single-channel auto-select (SWITCH-03) flow through the binder read-only with zero local state. Connectivity is read from the current `Bot`'s profile fields via the verbatim `BotSwitcherRowView` `IsConnected` predicate; every serialized ref and the bot lookup are null-guarded (a bot deleted mid-screen degrades to computed default state — T-06-02).
- `BottomTabManager.BotsTabIndex` shifted 3 -> 2 (Telegram tab index 1 is removed by 06-02); `WhatsAppTabIndex` stays 0 and `defaultTabIndex` is untouched. `TabIndexShiftTests` locks the post-restructure contract and the `TabRefreshGate` rule (Chats tab re-syncs, Bots tab does not).
- Tab-index consumer audit: every runtime `SwitchTab` call already routes through a constant — `EmptyStateView`, `BotsPage`, `Manager` -> `BotsTabIndex`; `ProfilePage`, `DashboardPage` -> `WhatsAppTabIndex`; `BottomTabManager` -> `defaultTabIndex`/`WhatsAppTabIndex`. No hardcoded-literal `SwitchTab(int)` calls exist.

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): ChannelSwitcherModel matrix** — `166f47b` (test) — Tests A–E + a deterministically-wrong stub so the assembly compiles and all 5 fail on assertions.
2. **Task 1 (GREEN): ChannelSwitcherModel seam** — `7c4530f` (feat) — real per-chip selection/muted logic; 5/5 green.
3. **Task 2: ChannelSwitcherView binder** — `ae41397` (feat) — event-driven TopBar binder; suite 896/896 green.
4. **Task 3: BotsTabIndex 3->2 + guard test** — `2ea7c45` (feat) — constant shift + `TabIndexShiftTests`; suite 900/900 green.

**Plan metadata:** _(final docs commit)_

_TDD note: Task 1 was `tdd="true"` → RED (`166f47b`) then GREEN (`7c4530f`)._

## Files Created/Modified

- `Assets/Scripts/UI/ChannelSwitcherModel.cs` (created) — pure `ChannelSwitcherModel.StateFor` + `readonly struct ChannelChipState`.
- `Assets/Scripts/UI/ChannelSwitcherView.cs` (created) — event-driven TopBar segmented-pill binder; serialized-ref field names (`waChipButton`/`tgChipButton`/`waChipFill`/`tgChipFill`/`waLabel`/`tgLabel`/`waChipIcon`/`tgChipIcon`) are the 06-02 builder's SerializedObject contract.
- `Assets/Scripts/Main/BottomTabManager.cs` (modified) — `BotsTabIndex` 3 -> 2 + post-restructure comment.
- `Assets/Tests/Editor/Chat/ChannelSwitcherModelTests.cs` (created) — A–E connectivity × active × chip matrix (5 tests).
- `Assets/Tests/Editor/Chat/TabIndexShiftTests.cs` (created) — BotsTabIndex==2 / WhatsAppTabIndex==0 + TabRefreshGate rule (4 tests).

## Decisions Made

- **Muted is connectivity-only; selected is equality-only; both can be true.** The model never suppresses muted when a chip is selected, so the active-channel-on-a-disconnected-profile edge (Test D) renders a filled-but-faded chip — exactly the "you're on this channel but it isn't connected, here's the connect CTA" state.
- **No local channel persistence in the binder.** `ActiveChannel` (05-02) is the single source of truth; the binder is a read-only consumer. This keeps SWITCH-03 owned by 05-02 (`{botId}ActiveChatChannel` + `ResolveChannelForBot`) and prevents a second, drifting store.
- **Selection colors.** Selected fill uses the CONTEXT-locked brand accents (WA `#25D366`, TG `#2AABEE` mirroring the private `Manager.TelegramBrandColor`) with a white label; unselected is neutral ink on transparent; muted fades label + icon to 40% alpha (fade-never-tint, per BotSwitcherRowView). Instant color set on Refresh (passive bot/channel changes should not animate); a `DOPunchScale` gives tactile feedback on tap, `DOKill` in OnDisable.
- **Editor-builder hardcoded indices: documented, not annotated.** `NavRestructureBuilder.cs` (L111 dashboardTab@2, L139 newTab@2, L140 botsTab@3) and `DashboardPageBuilder.cs` (L138 screenBots@3) assume the pre-06-02 5-tab array. They are one-time already-run migrations, are NOT re-run in this phase, and `NavRestructureBuilder` is owned/updated by 06-02. I kept my change surface to the plan's declared file set rather than editing files 06-02 owns; the audit's core requirement (all runtime `SwitchTab` consumers use constants) is satisfied and locked by `TabIndexShiftTests`.
- **Requirement marking split.** 06-01 marks **SWITCH-02** (muted-but-tappable chip → connect empty state; logic + 05-02 empty state) and **SWITCH-03** (per-bot persistence + auto-select flows through the binder). **SWITCH-01** (flip via the TopBar control) and **SWITCH-04** (Telegram tab / Screen_Telegram removed, tab 0 «Чаты») are re-listed by 06-02's frontmatter and are only real once 06-02 builds the pill and mutates the scene — 06-02 marks them. Notably SWITCH-04 must NOT be marked now: the Telegram tab is still present in Main.unity.

## Deviations from Plan

**None — plan executed as written.** No Rule 1–4 auto-fixes were required; all three tasks landed on-contract (interfaces matched the plan's `<interfaces>` block verbatim). The items under "Decisions Made" are planned discretionary/coordination choices (the plan explicitly offered "document in SUMMARY" for the editor-builder audit and left animation/naming to Claude's discretion), not unplanned work.

## TDD Gate Compliance

Task 1 (`tdd="true"`) followed RED → GREEN:

- **RED** (`166f47b`, `test`): `ChannelSwitcherModelTests` A–E committed with a deterministically-wrong stub (`Muted = waConnected && tgConnected`). Headless run: `total=5, passed=0, failed=5` — a genuine assertion-level RED (5 test cases executed, not a compile failure).
- **GREEN** (`7c4530f`, `feat`): real `StateFor`. Headless run: `5/5 passed`.

Note: because Unity compiles the test assembly against the type under test, the type must exist for the suite to build; the RED therefore ships a compiling wrong-stub (not a bare missing symbol). No unexpected pass occurred during RED.

## Known Stubs / Cross-Plan Dependencies

- **`ChannelSwitcherView` serialized refs are intentionally unwired until 06-02.** By plan design ("your binder must tolerate unwired refs gracefully… stamped by 06-02's builder"), all `[SerializeField]` refs are null until the 06-02 `ChannelSwitcherBuilder` stamps them via SerializedObject and places the pill under `Screen_Whatsapp/ChatsPanel/TopBar/CenterZone`. The binder is fully null-safe in this state (no NREs; degrades to computed default). This is a declared cross-plan dependency, not a defect.
- **`BotsTabIndex == 2` is only correct after 06-02.** In the current 5-tab scene the Bots tab is still at index 3, so between 06-01 and 06-02 a `SwitchTab(BotsTabIndex)` would land on the Dashboard. This is a coordinated, planned lockstep change (the guard test pins the target); do not device-test Bots-tab navigation until 06-02 lands the scene mutation. No functional impact in the Editor-closed, test-only window.

## Threat Register Coverage

- **T-06-01 (Tampering, `{botId}ActiveChatChannel`):** mitigated — the binder reads `ChatManager.ActiveChannel` (already clamped to a valid enum by 05-02's `ReadPersistedChannel`/`ChannelResolver`); it never casts a raw ordinal, so a tampered on-disk value cannot reach a switch here.
- **T-06-02 (DoS, `ChannelSwitcherView.Refresh` null paths):** mitigated — `ChatManager.Instance`, `Manager.Instance`, the `FindBotByName` result, and every serialized ref (fill/label/icon/button) are null-guarded; a deleted-mid-screen bot degrades to computed default state, never an NRE.
- **T-06-03 (Tampering, tab-index constant vs scene tab array):** mitigated — `TabIndexShiftTests` locks BotsTabIndex==2 / WhatsAppTabIndex==0; all `SwitchTab` consumers grep-verified as constants; 06-02 lands the matching 4-tab scene.

No new threat surface introduced (no network endpoints, auth paths, file access, or schema changes).

## SWITCH-03 Verification (no new code)

Per-bot channel persistence + single-channel auto-select is owned by 05-02 (`{botId}ActiveChatChannel` + `ResolveChannelForBot`/`ChannelResolver`). The binder consumes it read-only via `ChatManager.ActiveChannel` (Task 2). The existing `ChannelResolutionTests` (05-02) remained green inside the full-suite run — that is the SWITCH-03 end-to-end proof through the binder path.

## Issues Encountered

- **Headless runs:** all four completed cleanly. Unity's non-zero process exit on the RED run is the documented libcurl/batch quirk — the parsed NUnit XML is the source of truth and showed a clean 5-case result.
- **State-tooling fixes (finalization, not plan tasks):**
  - `gsd-sdk query requirements.mark-complete` checked the SWITCH-02/03 boxes but inserted a stray newline inside the bold markdown (`**SWITCH-02\n**:`), breaking both lines — repaired by hand to the single-line format; the traceability-table Status column was also left "Pending" and set to "Complete" manually.
  - `gsd-sdk query roadmap.update-plan-progress 6` no-op'd ("no matching checkbox found" — unpadded "Phase 6" heading); the 06-01 checkbox and the `0/2 Planned → 1/2 In Progress` row were updated by hand to match the Phase-5 precedent.
  - `state.advance-plan` incremented the "Plan:" number but left the Phase-5 narrative around it (Current Position / Current focus), which was rewritten by hand for coherence. Machine frontmatter (progress 9/11, 82%) is correct.

## User Setup Required

None — no external service configuration.

## Next Phase Readiness

- **06-02 is unblocked.** It builds the `WhatsApp | Telegram` pill under `CenterZone`, stamps the `ChannelSwitcherView` refs via SerializedObject (field-name contract above), removes the Telegram bottom tab + `Screen_Telegram`, updates `NavRestructureBuilder.ReorderScreens`, and marks SWITCH-01/SWITCH-04. The `BotsTabIndex` shift and its guard test are already in place for the 4-tab scene it produces.
- **Reminder for 06-02:** the editor-builder hardcoded indices (NavRestructureBuilder L111/139/140, DashboardPageBuilder L138) assume the pre-restructure 5-tab array — reconcile or leave-not-re-run when mutating the scene.

## Self-Check: PASSED

- All 4 created files present on disk (`ChannelSwitcherModel.cs`, `ChannelSwitcherView.cs`, `ChannelSwitcherModelTests.cs`, `TabIndexShiftTests.cs`) + `BottomTabManager.cs` modified.
- All 4 task commits present in git log (`166f47b`, `7c4530f`, `ae41397`, `2ea7c45`).
- Final headless EditMode suite: **900/900 green** (891 baseline + 5 model + 4 tab-index tests), `failed=0`, no WhatsApp regression, no scene mutation.

---
*Phase: 06-channel-switcher-ui*
*Completed: 2026-07-13*
