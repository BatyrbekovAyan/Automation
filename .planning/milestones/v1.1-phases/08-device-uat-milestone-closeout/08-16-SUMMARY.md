---
phase: 08-device-uat-milestone-closeout
plan: 16
subsystem: uat
tags: [device-uat, gate-a, re-verify, checkpoint, gap-closure, round-2]

# Dependency graph
requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "Round-2 gap plans 08-11..08-15 executed + code-review WR-04/05/06 fixed (tree @ 2e68df9, 1124/1124 EditMode green); D10-fixed Suggest_Replies deployed to dev by canonical PUT"
provides:
  - "Owner round-2 device re-verify verdicts for D2, D9, D10, D11, D12 (one Android build, one ordered pass)"
  - "Gate A disposition: ISSUES REMAIN — round-3 scope reduced to D2-ext, D12, D13"
  - "Owner decision: Telegram gets the WhatsApp-parity post-creation cover (D13) and the «Синхронизация…» pill is removed (D9 superseded)"
affects: [gap-closure-round-3, 08-DEVICE-UAT.md, milestone-close-gates]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created:
    - ".planning/phases/08-device-uat-milestone-closeout/08-16-SUMMARY.md"
  modified:
    - ".planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md"
    - ".planning/STATE.md"
    - ".planning/ROADMAP.md"

key-decisions:
  - "Verdicts transcribed from the owner's stated results only; nothing ticked on the owner's behalf"
  - "D9 sync pill SUPERSEDED by owner decision (remove the pill; build the WhatsApp-parity post-creation cover = D13) — not a technical pass/fail"
  - "D2 core resolved on device; residual D2-ext filed — reaction changes/removals made IN the Telegram app itself may not reflect in-app (round-3 scope)"
  - "D12 RE-FAIL contradicts 08-14's opens-with-WhatsApp diagnosis → needs on-device runtime diagnosis in round 3, not a re-preselect"

patterns-established: []

requirements-completed: []

# Metrics
duration: ~20min
completed: 2026-07-17
---

# Phase 8 Plan 16: Round-2 Device Re-verify (Gate A Disposition) Summary

**Owner round-2 device pass — D10 + D11 resolved, D2 core fixed (residual D2-ext filed), D12 RE-FAIL, new D13 (missing Telegram post-creation cover); the D9 sync pill is superseded by an owner decision to remove it and build the cover instead; Gate A stays ISSUES → round-3 scope = D2-ext / D12 / D13.**

## Performance

- **Duration:** ~20 min (transcription only; owner ran the device pass out-of-band)
- **Started:** 2026-07-17T13:00:13Z
- **Completed:** 2026-07-17T13:20:00Z
- **Tasks:** 1 (checkpoint:human-verify — owner-run gate)
- **Files modified:** 4 (this SUMMARY + 08-DEVICE-UAT.md + STATE.md + ROADMAP.md)

## Checkpoint Outcome

The owner ran the consolidated re-verify on ONE real Android device build (@ `2e68df9` — the round-2 gap plans 08-11..08-15 plus the round-2 code-review fixes WR-04/05/06; suite 1124/1124 EditMode green at build time). The D10 live item was re-tested against the shared dev n8n session with the D10-fixed `Suggest_Replies` workflow deployed by canonical PUT (activation preserved). Prod bagkz stayed dormant.

This is a checkpoint plan — **no app code changed here.** Every verdict below is a verbatim transcription of the owner's stated results.

## Verdicts

| # | Item | Anchor | Verdict (owner's words) |
|---|------|--------|--------------------------|
| 1 | D2 — TG reaction identity | B9 / B13 | **PASS (core)** — owner: *"seems working"*. The three round-1 symptoms (own-reaction count «2», change leaving both pills, two heart glyphs) are gone. **Residual → D2-ext:** *"i noticed that if i change or remove reaction in telegram itself it may not change in our app"* (intermittent — "may not") |
| 2 | D9 — TG sync pill | Extra #2 / O1 | **SUPERSEDED (owner decision)** — owner: *"why we have this pill here at all, and why it is not in whatsapp? when we decided to add it?"* → decision to REMOVE the pill and build the WhatsApp-parity cover instead (→ D13). Owner also reported *"pull-to-refresh does nothing"* — that feature does not exist in the app (the checkpoint instruction wrongly told the owner to try it), so this is expected, **not** a defect |
| 3 | D10 — WA «Вместе» relevance | H2 (WhatsApp half) | **PASS** — owner: *"seems ok"* (re-tested against dev n8n with the D10-fixed `Suggest_Replies`) |
| 4 | D11 — media downloads | B-group media | **PASS** — owner: *"seems ok"*. No download failure reproduced this pass, so no `[MediaDownload] FAIL` lines to capture; the 08-15 instrumentation + serial-safe transient retry stay armed for any future failure |
| 5 | D12 — TG create-bot CTA | F-group | **RE-FAIL** — owner: *"nothing happens when pressing the button"* |
| — | **D13 (NEW)** — TG post-creation cover | new (this pass) | **FAIL (new)** — owner: *"you never resolved why when telegram bot is just created there is no cover page with 5 minute loading slider on top of chats list page"* |

## Owner Decision — D9 pill → D13 cover

Asked and answered this session: **"Cover only, remove pill."**

- **Rationale trail:** the pill traces to the owner's own round-1 clarification (O1 → D9, 2026-07-16: *"the not-good part is that Telegram shows chats instantly with no sync indicator"*) → shipped as the «Синхронизация…» pill (08-09) + visibility gate (08-12) → round-2 owner reversal on device (*"why we have this pill here at all, and why it is not in whatsapp?"*). WhatsApp shows no pill because it already has the full post-creation **cover** (`SyncingState`); Telegram got only the small pill — the owner wants parity via the cover, not the pill.
- **Decision:** Telegram gets the WhatsApp-parity post-creation cover (full overlay + ~5-min progress slider over the chats list), and the «Синхронизация…» pill is removed entirely. The two collapse into ONE round-3 work item (**D13** = build the TG cover **and** remove the D9 pill).

## Unfulfilled Capture Asks

- **D2 echo-hex** (the `#if UNITY_EDITOR` reaction echo-hex + `user_id` log added in 08-11) — **NOT pasted** by the owner. Still wanted, now for the **D2-ext** diagnosis (the residual is server/TG-client-originated reaction deltas, so the live echo form remains the key evidence).
- **D11 `[MediaDownload] FAIL …` logcat lines** — **none existed to paste**: D11 passed, so no failing download surfaced during the pass. The ask is moot for this pass; the instrumentation stays armed for future failures.

## Gate A Disposition

**Gate A stays ISSUES** — but round-2 materially reduced the open set:

- **Resolved this round:** D10 (WA suggestion relevance) + D11 (media downloads); D2 **core** (reaction identity).
- **Round-3 scope (`/gsd-plan-phase 08 --gaps`):**
  - **D2-ext** — reaction change/removal performed IN the Telegram app itself may not reflect in-app (intermittent). *Hypothesis for the planner (not fact):* poll-window absence-vs-removal semantics in `TelegramReactionMerge` — a server-originated reaction delta only reconciles if the message sits inside the polled window, and a removal that arrives as an empty `reactions[]` (absence) vs an explicit change may be dropped.
  - **D12** — create-bot CTA does nothing on device. This **contradicts** 08-14's opens-with-WhatsApp diagnosis (the `SelectPlatform(1)`→`ActiveChannel` preselect fix): the button is inert, not merely mis-preselected. Needs on-device/runtime diagnosis (raycast blocker over the button, a per-channel empty-state instance whose handler is unwired, an `AddBotPanel.Instance` null path, or a swallowed exception).
  - **D13** — build the WhatsApp-parity post-creation cover for Telegram + remove the D9 pill (one item). *Lead for the planner (confirm before mirroring):* the WhatsApp cover is `SyncingState` — built by `Assets/Editor/SyncingStateBuilder.cs` into `Screen_Whatsapp/ChatsPanel` (`ProgressTrack` + `ProgressFill` are its time-based bar), and driven at runtime by `Assets/Scripts/UI/SyncingView.cs`. Find why it does not fire for a Telegram-created bot and mirror it on the Telegram channel.
- **I.3 #10:** D10's closure clears its stated D5/D10 blocker; the remaining round-3 items (D2-ext/D12/D13) are unrelated to the v1.0 suggestions sign-off, so the formal 01-VERIFICATION re-aggregation now rides the eventual Gate A closure. (Left as RE-DEFER in the ledger — not flipped here, as this pass is not all-PASS.)

**Carried reminder (bot-activation policy): deactivate the dev test clone** — the owner did not confirm this at the checkpoint, so it remains OUTSTANDING (clones run against real contacts). Prod bagkz untouched.

## Decisions Made

- Recorded per-item verdicts strictly from the owner's stated words; nothing ticked on the owner's behalf (T-08-16-01).
- Split D2 into a resolved core + a new residual **D2-ext** rather than re-failing D2 wholesale — the three device-visible symptoms the owner reported at round 1 are gone; the residual is a distinct, TG-client-originated reflection gap.
- Treated the missing post-creation cover as a **new** defect (**D13**), not a D9 variant — grep across prior rounds shows it was never filed before, and the owner's decision reframes D9 (pill) as superseded by D13 (cover).

## Deviations from Plan

None — checkpoint plan; no code changed. Verdicts recorded verbatim from the owner's report. (One recording nuance: the checkpoint's D9 instruction told the owner to "pull-to-refresh," a gesture the app does not implement; the owner's "does nothing" is expected and is documented as such rather than filed as a defect.)

## Issues Encountered

None during transcription. The substantive open work is captured as round-3 scope (D2-ext / D12 / D13) above.

## Next Phase Readiness

- Gate A **not** closed → gap round 3 planning is the next step (`/gsd-plan-phase 08 --gaps` for D2-ext / D12 / D13, with D13 folding in the D9 pill removal).
- Gate B (prod bagkz replication) and Gate C (milestone close) remain blocked behind Gate A.
- Blocker/reminder still live: deactivate the dev test clone.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*
