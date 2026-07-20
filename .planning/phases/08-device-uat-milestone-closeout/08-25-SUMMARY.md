---
phase: 08-device-uat-milestone-closeout
plan: 25
subsystem: testing
tags: [device-uat, telegram, whatsapp, reactions, sync-cover, gate-a, checkpoint]

requires:
  - phase: 08-22 (D2-view fix)
    provides: reaction-bubble VISUAL repaint on the reaction-bar dismiss path (RefreshSourceNextFrame)
  - phase: 08-23 (D12-ext fix)
    provides: EmptyStateReasonPolicy NoBots-coercion + WR-02 re-derive so the create-bot CTA survives a chip switch
  - phase: 08-24 (D14 fix)
    provides: Telegram post-creation cover recolored to brand blue #2AABEE via ChannelAccent.Resolve
provides:
  - Round-4 owner device verdicts transcribed verbatim (D2-view / D12-ext / D14 + G6)
  - Two new defects filed (D15 WhatsApp reaction-removal not propagated, D16 late-channel Telegram sync cover)
  - Gate A disposition = ISSUES (unchanged); round-5 scope named
affects: [round-5 gap planning (/gsd-plan-phase 08 --gaps), Gate B (prod replication), Gate C (milestone close)]

tech-stack:
  added: []
  patterns: []

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-25-SUMMARY.md
  modified:
    - .planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md
    - .planning/STATE.md
    - .planning/ROADMAP.md

key-decisions:
  - "Round-4 Gate A stays ISSUES — 1 FAIL (D2-view) + 2 new defects; Gates B/C + I.3 #10 stay blocked"
  - "Item 4 WR-02 stale-card check NOT recorded as PASS — owner superseded it with a new late-channel-cover observation (D16); the interrupted first-draft 'PASS' is discarded per the authoritative second reply"
  - "D15 (WA reaction-removal) + D16 (late-channel TG cover) filed as pre-existing/promotion — neither is a round-4 regression"

patterns-established: []

requirements-completed: []

duration: ~12min
completed: 2026-07-20
---

# Phase 08 Plan 25: Round-4 Owner Device Re-verify Summary

**Owner ran ONE Android build (08-22/08-23/08-24 merged); D12-ext CTA and D14 blue-cover PASS, but D2-view still FAILS and two new defects (D15 WhatsApp reaction-removal, D16 late-channel Telegram sync cover) surfaced — Gate A stays ISSUES, round 5 spins.**

## Performance

- **Duration:** ~12 min (verdict transcription + defect filing + state/roadmap update; the device run itself is owner time)
- **Started:** 2026-07-20 (checkpoint resumed with owner verdicts)
- **Completed:** 2026-07-20
- **Tasks:** 1 (checkpoint:human-verify, resolved with owner verdicts)
- **Files modified:** 4 (08-DEVICE-UAT.md, 08-25-SUMMARY.md, STATE.md, ROADMAP.md)

## Accomplishments

- Transcribed all 8 round-4 verdict items VERBATIM into the §Round 4 re-verify block of 08-DEVICE-UAT.md, each mapped to its source anchor.
- Filed two new defects with ledger-consistent numbering (**D15**, **D16**) + cross-references (D15 → 08-17 TG removal work / WhatsApp ReactionStore untouched; D16 → documented 08-19 late-channel follow-up).
- Updated the status-header chain, Round-4 Overall/Gate-A disposition lines, and the Overall-result Round-4 bullet — all reflecting **ISSUES**.
- Named the round-5 scope and kept Gate A = ISSUES (no flip; Gates B/C + I.3 #10 re-aggregation untouched).

## Verdict Table (owner verbatim, 2026-07-20)

| # | Item | Verdict | Owner words (verbatim) | Disposition |
|---|------|---------|------------------------|-------------|
| 1 | D2-view — reaction bubble repaint | **FAIL** | "no pass, still sometimes not updating bubble reaction when it is updated in telegram even though logs show updated reaction." | D2-view stays open → round 5 |
| 2 | D2-view — WhatsApp unaffected | not-regressed-not-confirmed | "i noticed that if in whatsapp itself reaction is removed it is still not removed in our app" | NEW defect **D15** |
| 3 | D12-ext — CTA survives chip switch | **PASS** | "pass" | D12-ext CTA RESOLVED |
| 4 | D12-ext — no stale card over cover (WR-02) | not explicitly verdicted | "if whatsapp channel exists and telegram channel is created its sunc cover page is not shown," | NEW defect **D16** |
| 5 | D14 — Telegram cover blue | **PASS** | "PASS," | D14 RESOLVED |
| 6 | D14 — WhatsApp cover byte-identical | **PASS** | "PASS," | WA cover unchanged |
| 7 | G6 — deactivate dev clone (BLOCKING) | **done** (post-checkpoint) | "G6: what exactly should be done?" → after explanation: "G6 done" | resolved 2026-07-20 same day — no round-5 carry |
| 8 | D2-ext echo-hex (nice-to-have) | not captured | — | not captured (3rd consecutive) |

**Two-message note:** the owner replied twice; the SECOND message is authoritative. The interrupted first draft had "4: PASS" (WR-02 stale-card) and "G6: done" — both were REPLACED in the second message (item 4 → the new late-channel-cover observation; G6 → the clarification question). Per the authoritative reply, WR-02 is NOT recorded as PASS and G6 is NOT recorded as done.

## New Defects Minted

- **D15** (medium) — a reaction REMOVED in the WhatsApp app itself is not removed in our app. Cross-ref: 08-17 built TG absence-vs-removal semantics; the WhatsApp `ReactionStore` was deliberately left untouched throughout v1.1 → WA removal propagation likely NEVER implemented → **pre-existing, not a round-4 regression**. Source-anchor: 08-25 (round-4) / D2 (WhatsApp analogue).
- **D16** (medium) — a bot that already has WhatsApp shows NO Telegram post-creation sync cover when Telegram is added later. Cross-ref: the documented **08-19 follow-up** ("late-channel auth stamps NO window on EITHER channel — exact parity; follow-up if ever wanted") — the wizard tail is the only site stamping `{bot}TelegramSyncUntil` → **promotion of a documented follow-up, not a regression**. Source-anchor: 08-25 (round-4) / D13 (08-19 late-channel follow-up).

## Round-5 Scope (as recorded in the UAT ledger)

D2-view continued diagnosis on the poll-driven `HandleReactionsChanged` repaint path (NOT the merge — data layer proven); D15 WhatsApp reaction-removal propagation; D16 late-channel Telegram sync-cover stamp. (G6 resolved post-checkpoint 2026-07-20 — no carry.) Spin via `/gsd-plan-phase 08 --gaps`.

## Gate A Disposition

**ISSUES (unchanged).** 3 PASS (D12-ext CTA, D14 ×2), 1 FAIL (D2-view), 2 new defects (D15/D16), G6 done post-checkpoint. Gate A does NOT flip; Gates B/C (prod replication 08-02 / milestone close 08-03) and the I.3 #10 (01-VERIFICATION sign-off) re-aggregation stay blocked. Prod bagkz stays dormant. I.3 #10 was NOT re-aggregated.

## Decisions Made

- Kept Gate A = ISSUES and did NOT touch Gates B/C or re-aggregate I.3 #10 (per plan: any FAIL keeps ISSUES).
- Recorded item-4 WR-02 as "not explicitly verdicted" rather than PASS — the owner's authoritative second message substituted a new observation (D16) for the interrupted first-draft "PASS".
- Recorded G6 as `still-outstanding` (owner asked for clarification), not `done`. **Amended same day:** after the deactivation steps were explained, the owner confirmed "G6 done" — disposition flipped to `done (post-checkpoint 2026-07-20)` in the ledger; no round-5 carry.

## Deviations from Plan

None — this checkpoint plan executed exactly as written. The plan front-loaded the all-PASS → Gate A PASS branch; the owner's verdicts took the "any FAIL → ISSUES" branch, which is the plan's explicit alternate path (not a deviation).

## Issues Encountered

- The owner replied in two messages; the first was interrupted mid-reply. Resolved by treating the second message as authoritative while preserving the first-draft substitutions in the ledger (WR-02 not-PASS, G6 not-done) for traceability.

## Threat Flags

None — this plan changed no app code and added no security surface. It records owner verdicts into planning docs; secrets.json referenced by name only.

## Known Stubs

None — no code, no UI, no data sources; planning-doc transcription only.

## Next Phase Readiness

- Round 5 is ready to plan: `/gsd-plan-phase 08 --gaps` for D2-view / D15 / D16 (G6 resolved — not carried).
- Gate A remains the milestone-v1.1 gate; Gates B/C stay blocked until Gate A goes all-PASS.
- Prod bagkz stays dormant until the 08-02 replication gate.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*

## Self-Check: PASSED

- All 4 touched files exist on disk (08-DEVICE-UAT.md, 08-25-SUMMARY.md, STATE.md, ROADMAP.md).
- D15 and D16 rows present in the §Defects table (grep count 1 each); status header, Round-4 Overall/Gate-A lines, and Overall-result bullet all read ISSUES.
- All 8 verdict items transcribed VERBATIM with 2026-07-20 owner quotes; G6 recorded still-outstanding, echo-hex not captured.
- Gate A NOT flipped (stays ISSUES); Gates B/C + I.3 #10 untouched.
- STATE.md advanced (stopped_at 08-25, completed_plans 52→53, percent 95→96) + [08-25] decision + round-4 blocker + P25 metric row; ROADMAP 08-25 checkbox ticked + Flags/status-row updated.
- Commit hash recorded once the metadata commit lands.
