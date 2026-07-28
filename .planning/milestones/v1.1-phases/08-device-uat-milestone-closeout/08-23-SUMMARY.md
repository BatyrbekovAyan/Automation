---
phase: 08-device-uat-milestone-closeout
plan: 23
subsystem: ui
tags: [empty-state, channel-switch, telegram, whatsapp, onboarding-cta, pure-seam, tdd]

# Dependency graph
requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "08-18 OnActiveChannelChanged re-configure + preselect; ComputeCurrentEmptyState authoritative resolver; ChannelTabStateResolver pure seam home"
provides:
  - "EmptyStateReasonPolicy.Effective — pure NoBots-coercion seam (raw reason promoted to NoBotsExist only when the authoritative resolver agrees)"
  - "HandleEmptyState coercion so the create-first-bot CTA survives a WhatsApp↔Telegram chip switch (D12-ext)"
  - "HandleActiveChannelChanged re-derive from ComputeCurrentEmptyState (WR-02) — Hide when the new channel has no card (kills the stale wrong-channel connect card over the cover)"
  - "Non-silent OpenCurrentBotAuth (Debug.LogWarning on both early-returns) — no more invisible dead CTA"
affects: [08-25 device re-verify, Gate A]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure static coercion seam folded into an existing runtime .cs (WhatsAppTabState.cs) to dodge the Unity new-file import quirk"
    - "View-layer defense: coerce/re-derive against the authoritative resolver instead of touching the out-of-scope BeginLoadForActiveBot reason source (IN-01)"

key-files:
  created: []
  modified:
    - Assets/Scripts/Main/WhatsAppTabState.cs
    - Assets/Scripts/UI/EmptyStateView.cs
    - Assets/Tests/Editor/Chat/ChannelTabStateResolverTests.cs

key-decisions:
  - "Fix D12-ext entirely in the view (coerce + re-derive against ComputeCurrentEmptyState); BeginLoadForActiveBot's zero-bots wrong reason is NOT modified (connect-state regression risk, IN-01)"
  - "Coercion promotes to NoBots ONLY when the authoritative resolver also says NoBots — the WhatsApp invariant (real WA-less bot keeps its connect card) is pinned by a test"
  - "HandleActiveChannelChanged re-DERIVES the reason (WR-02) instead of replaying stale _lastReason, so a channel switch never strands a wrong-channel card over the syncing cover"

patterns-established:
  - "EmptyStateReasonPolicy.Effective(raw, resolved): resolver-agrees coercion — trust the raw reason when the resolver is undecided (null)"

requirements-completed: []  # closeout phase — no new v1.1 REQ ids (gap_closure)

# Metrics
duration: 10min
completed: 2026-07-20
---

# Phase 8 Plan 23: D12-ext EmptyStateReasonPolicy NoBots-coercion Summary

**The create-first-bot CTA now survives a WhatsApp↔Telegram chip switch on both channels via a pure NoBots-coercion seam (Effective) wired into HandleEmptyState, plus a WR-02 re-derive on channel switch and non-silent OpenCurrentBotAuth warnings.**

## Performance

- **Duration:** 10 min
- **Started:** 2026-07-20T12:15:29Z
- **Completed:** 2026-07-20T12:25:35Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Added `EmptyStateReasonPolicy.Effective(raw, resolved)` — a pure NoBots-coercion seam that promotes a `BotHasNo{Channel}` raw reason back to `NoBotsExist` ONLY when the authoritative `ComputeCurrentEmptyState()` also says NoBots (WhatsApp/Telegram connect card for a real bot preserved byte-identically).
- Wired the coercion into `HandleEmptyState`, so `BeginLoadForActiveBot`'s zero-bots wrong reason (which slipped past the `_lastReason` guard and re-wired the CTA to the silent `OpenCurrentBotAuth`) can no longer kill the «Создать бота» button across a channel switch (root cause D12-ext / 08-REVIEW CR-01).
- Folded in **WR-02**: `HandleActiveChannelChanged` now re-DERIVES the reason from `ComputeCurrentEmptyState()` instead of replaying a stale `_lastReason`, and Hides when the new channel has no card — killing the stale wrong-channel connect card (with its raycast block) that could sit over the Telegram syncing cover for minutes.
- Made the primary CTA non-silent: `OpenCurrentBotAuth` now logs a `Debug.LogWarning` on each early-return, so a dead CTA is never invisible again.

## Task Commits

Each task was committed atomically:

1. **Task 1: Pure EmptyStateReasonPolicy NoBots-coercion seam (TDD)**
   - RED: `94e2e3a` (test) — 6 failing assertions referencing the missing symbol
   - GREEN: `382dc95` (feat) — `EmptyStateReasonPolicy.Effective` in WhatsAppTabState.cs
2. **Task 2: Wire coercion + WR-02 re-derive + non-silent CTA warnings** - `eb7ec56` (fix)

**Plan metadata:** (this commit — docs: complete plan)

## Files Created/Modified
- `Assets/Scripts/Main/WhatsAppTabState.cs` - Added the pure `EmptyStateReasonPolicy` static class (Effective seam) after `ChannelTabStateResolver`.
- `Assets/Scripts/UI/EmptyStateView.cs` - HandleEmptyState coercion; HandleActiveChannelChanged re-derive + Hide-on-no-card; OpenCurrentBotAuth LogWarning on both early-returns.
- `Assets/Tests/Editor/Chat/ChannelTabStateResolverTests.cs` - Added `EmptyStateReasonPolicyTests` (6 cases incl. the WhatsApp invariant pin).

## Decisions Made
- **View-layer defense only.** `BeginLoadForActiveBot` (the wrong-reason source for zero bots) is deliberately NOT modified — changing it risks a connect-state regression (documented IN-01). The fix coerces/re-derives against the authoritative `ComputeCurrentEmptyState()`.
- **Coerce only when the resolver agrees.** `Effective` promotes to NoBots strictly when `resolved == NoBotsExist`; a real WA-less bot (resolver returns `BotHasNoWhatsApp`) keeps its connect card byte-identically, and a `null` (undecided) resolver leaves the raw reason untouched. Both are pinned by tests.
- **Preserved the double-fire guard.** The `_lastReason == reason` early-return stays after the coercion so the OnEnable catch-up race is still handled.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- **Freshness-gate nuance (not a defect).** After Task 2's runtime-only edit to EmptyStateView.cs, the bridge summary reported `completed`/1176 but `editorAssemblyWrittenUtc` did NOT advance (12:18:28Z) — expected, because that stamp tracks the editor test assembly, which Task 2 did not touch. Verified freshness the correct way for a runtime edit: `Assembly-CSharp.dll` mtime advanced to 12:22:18Z (postdates the 12:21:39Z edit), and a Unity test run cannot execute while compilation is pending, so the 1176/1176 run reflects the Task 2 code.

## Verification
- Full EditMode suite green FRESH via the in-Editor ClaudeTestBridge: **1176/1176** passed, 0 failures (baseline 1170 + 6 new EmptyStateReasonPolicy tests; Task 2 adds 0 tests).
- Task 1 green run: `editorAssemblyWrittenUtc` 12:18:28Z (postdates the test-file edit) — the 6 new tests incl. the WhatsApp invariant pin pass.
- Task 2 green run: `Assembly-CSharp.dll` mtime 12:22:18Z (postdates the runtime edit); suite 1176/1176.
- All acceptance-criteria greps pass: `EmptyStateReasonPolicy.Effective` present + guarded in HandleEmptyState; `ComputeCurrentEmptyState` used in both HandleEmptyState and HandleActiveChannelChanged; WR-02 `if (!reason.HasValue) { Hide(); return; }` present; both `[D12] OpenCurrentBotAuth` warnings present; the stale `ConfigureForReason(_lastReason.Value)` replay is gone (0 hits); WhatsApp preselect `ActiveChannel == ChatChannel.Telegram) ? 2 : 1` still present.
- WhatsApp byte-identical: coercion is a no-op for a real WA-less bot; OpenCreateBotFlow preselect + all WA-reason wiring unchanged.

## Known Stubs
None — the change is navigation glue over the existing authoritative resolver; no hardcoded empty values, placeholder text, or unwired data sources introduced.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Code fix complete and suite-green. Device confirmation rides **08-25**: the create-first-bot CTA must survive a WhatsApp↔Telegram chip switch on BOTH channels (no dead button), and no stale wrong-channel connect card may sit over the Telegram syncing cover. The new compiled `[D12] OpenCurrentBotAuth` warnings are the on-device diagnostic if the CTA still misfires.
- On an all-PASS device pass at 08-25, Gate A can flip to PASS (unblocking Gates B/C). G6 dev-clone deactivation remains an outstanding BLOCKING line item for 08-25.

## TDD Gate Compliance
Plan type is `execute` (not plan-level `tdd`), but Task 1 ran the RED→GREEN cycle: `test(08-23)` RED commit `94e2e3a` precedes the `feat(08-23)` GREEN commit `382dc95`. No unexpected pass during RED (the symbol did not exist → deterministic compile-fail).

## Self-Check: PASSED

- All 3 modified files + SUMMARY.md present on disk.
- All 3 task commits (`94e2e3a` RED, `382dc95` GREEN, `eb7ec56` fix) exist in git history.
- Grep pins hold: `EmptyStateReasonPolicy` = 1 class in WhatsAppTabState.cs, 7 references in the test file.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*
