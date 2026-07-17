---
phase: 08-device-uat-milestone-closeout
plan: 14
subsystem: ui
tags: [empty-state, add-bot, channel-aware, telegram, whatsapp, cta]

# Dependency graph
requires:
  - phase: 05-device-uat-milestone-closeout (05-02 ChatManager identity seam)
    provides: "ChatManager.ActiveChannel (ChatChannel enum) — the channel the empty state is themed for"
  - phase: 05-device-uat-milestone-closeout (05-10/05-12 empty-state accent theming)
    provides: "EmptyStateView already themes the create-bot card per ActiveChannel (blue + Telegram logo on TG)"
provides:
  - "Channel-aware create-bot CTA: the Telegram empty-state «Создать бота» opens the Add-Bot overlay with Telegram preselected (D12 closed in code)"
affects: [08-16 device re-verify]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Channel-aware UI glue: read ChatManager.ActiveChannel at the action site to branch behaviour, keeping WhatsApp byte-identical (mirrors 05-10/05-11/05-12)"

key-files:
  created: []
  modified:
    - Assets/Scripts/UI/EmptyStateView.cs

key-decisions:
  - "Root cause of D12 is (ii) opens-with-WhatsApp, NOT an inert handler — the CTA is wired + interactable on every channel; only the hardcoded SelectPlatform(1) was wrong. So the fix is the preselect line only; no wiring/interactivity change."
  - "Preselect derived from ChatManager.ActiveChannel (Telegram→2, else→1) so the form opens on the SAME platform the empty state was themed for; WhatsApp still resolves to 1 (byte-identical)."

patterns-established:
  - "Channel-aware action glue: branch on ChatManager.Instance.ActiveChannel == ChatChannel.Telegram, null-guarded, WhatsApp path unchanged."

requirements-completed: []

# Metrics
duration: 7min
completed: 2026-07-17
---

# Phase 8 Plan 14: D12 Channel-Aware Create-Bot CTA Summary

**The Telegram empty-state «Создать бота» now opens the Add-Bot overlay with Telegram preselected (via ChatManager.ActiveChannel) instead of hardcoding WhatsApp — WhatsApp path byte-identical, no scene change.**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-07-17T07:48:30Z
- **Completed:** 2026-07-17T07:55:45Z
- **Tasks:** 2 (Task 1 diagnosis, Task 2 fix)
- **Files modified:** 1

## Accomplishments
- Confirmed the exact D12 no-op cause with code evidence (Task 1) — see "Task 1 Diagnosis" below.
- Replaced the hardcoded `Manager.Instance.SelectPlatform(1)` in `EmptyStateView.OpenCreateBotFlow` with a channel-aware preselect: Telegram (2) when `ChatManager.ActiveChannel == ChatChannel.Telegram`, WhatsApp (1) otherwise.
- Full EditMode suite green at 1111/1111 on a freshly recompiled assembly (no regressions; WhatsApp byte-identical).

## Task 1 Diagnosis (recorded per plan `<output>` requirement)

**1. Which `EmptyStateReason` surfaces the CTA on the Telegram channel, and is its handler wired?**
`NoBotsExist` — the only reason carrying the «Создать бота» / `OpenCreateBotFlow` CTA. `ChatManager.ComputeCurrentEmptyState()` (ChatManager.BotState.cs:226-242) maps `ChannelTabState.NoBots → EmptyStateReason.NoBotsExist` purely on **bot count == 0** (channel-agnostic — no channel branch), so it is reachable on the Telegram channel. Its handler is wired at `EmptyStateView.cs:195-199` (`primaryButton.onClick.AddListener(OpenCreateBotFlow)`), inside a `switch` case that runs on **every** channel; `ApplyChannelAccent()` at the tail (line 231) only recolors the card blue + swaps in the Telegram logo — it never touches `onClick` or the `CanvasGroup`.

**2. Is `OpenCreateBotFlow` actually invoked (button wired + interactable) on the TG channel?**
Yes. `Show()` (lines 152-158) sets `canvasGroup.interactable = true; blocksRaycasts = true`; `OnDisable()` (lines 144-149) `RemoveAllListeners()` + resets `_lastReason = null` so the next `OnEnable` re-runs `ConfigureForReason` and re-wires. There is **no** Telegram-specific branch that leaves the button unwired or non-interactive. So the handler genuinely fires on tap on the Telegram channel.

**3. Does `StartNewBot()` + `AddBotPanel.Open()` open the overlay from the Telegram-channel context, and does `SelectPlatform` run?**
Yes — both are channel-agnostic. `BotsPage.StartNewBot()` (BotsPage.cs:49-55) switches to the Bots tab then `AddBotPanel.Instance?.Open()`; `AddBotPanel.Instance` resolves include-inactive (AddBotPanel.cs:18-20) and `Open()` slides the overlay in. `SelectPlatform` then ran — but as `SelectPlatform(1)` (WhatsApp, EmptyStateView.cs:246 pre-fix), confirmed 1=WhatsApp/2=Telegram/3=Both from `Manager.SelectPlatform` (Manager.cs:1057-1089 + `UpdateCreateButtonColor` 2⇒TelegramBrandColor).

**Conclusion — cause (ii), NOT an inert handler:** the CTA DOES open the Add-Bot overlay on the Telegram channel, but `OpenCreateBotFlow` hardcoded `SelectPlatform(1)`, so the form opened with **WhatsApp** preselected. An owner viewing the Telegram-themed (blue + Telegram-logo) empty state read "opened on the wrong platform" as "did nothing / dead". No inert-handler fix was required — Task 2 is purely the channel-aware preselect.

## Task Commits

Each task was committed atomically:

1. **Task 1: diagnose the no-op** — no code change (diagnosis recorded in this SUMMARY per plan `<output>`); no commit.
2. **Task 2: channel-aware create-bot CTA** — `a52f385` (fix)

**Plan metadata:** (final docs commit — this SUMMARY + STATE + ROADMAP)

## Files Created/Modified
- `Assets/Scripts/UI/EmptyStateView.cs` — `OpenCreateBotFlow` now reads `ChatManager.ActiveChannel` and preselects platform 2 (Telegram) on the Telegram channel, 1 (WhatsApp) otherwise. `BotsPage.StartNewBot()` (overlay-open) unchanged.

## Decisions Made
- Fix is the preselect line only — Task 1 confirmed the handler was already wired + interactable on all channels, so no wiring/interactivity change was needed.
- Preselect keyed off `ChatManager.ActiveChannel` (the empty state is already themed for that channel via `ApplyChannelAccent`), so the form and the card agree on platform.

## Deviations from Plan

None - plan executed exactly as written. Task 1's diagnosis found cause (ii) (opens-with-WhatsApp), so the plan's optional "if Task 1 found a GENUINE inert-handler cause, ALSO fix that" branch did not apply — only the channel-aware preselect was implemented.

## Issues Encountered
None. The edit compiled cleanly on the first bridge run (the bridge would have written `status: error / CompilationFailed` otherwise) and all 1111 tests passed.

## Verification

- **Acceptance greps (all pass):** `SelectPlatform(platform)` = 1; `ActiveChannel == ChatChannel.Telegram` = 1; `SelectPlatform(1)` = 0 (hardcoded WA preselect removed); `StartNewBot` = 3 (overlay-open call preserved).
- **EditMode suite:** 1111/1111 passed, 0 failed — via the in-Editor ClaudeTestBridge (Unity Editor open, PID 1883; headless runner correctly unavailable under the project lock). Run is **fresh**: `Assembly-CSharp.dll` mtime advanced `1784273032` (12:23:52, pre-edit) → `1784274844` (12:54:04, post-recompile), and summary `finishedAt` advanced `2917.72` → `4704.02`. (`editorAssemblyWrittenUtc` stayed at 07:23:54Z as expected — the edit is to a **runtime** script in Assembly-CSharp, not the editor assembly; the runtime-DLL mtime is the correct freshness gate.)
- **Threat register:** T-08-14-01 (WhatsApp regression) mitigated — WhatsApp channel still resolves `platform = 1`, byte-identical; only the Telegram channel changes to 2. T-08-14-02 (null crash) accepted — both `ChatManager.Instance` and `Manager.Instance` null-guarded, no-op if unavailable.

## Known Stubs
None — the change wires a real, existing behaviour (`SelectPlatform(2)`), no placeholder/empty data introduced.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- D12 is closed in code. Device confirmation rides the 08-16 re-verify checkpoint: on the Telegram channel with no bots, «Создать бота» must open the Add-Bot form with Telegram preselected; on WhatsApp it must still preselect WhatsApp.

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-14-SUMMARY.md`
- FOUND: `Assets/Scripts/UI/EmptyStateView.cs`
- FOUND commit: `a52f385` (Task 2 fix)
- No file deletions introduced by the commit.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*
