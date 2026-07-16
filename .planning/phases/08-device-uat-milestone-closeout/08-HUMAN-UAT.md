---
status: partial
phase: 08-device-uat-milestone-closeout
source: [08-VERIFICATION.md]
started: "2026-07-15T10:10:43Z"
updated: "2026-07-16T09:00:00Z"
---

## Current Test

[awaiting human testing — three owner-run gates, run in order A → B → C]

## Tests

<!-- Each gate points at its purpose-built runbook rather than duplicating its items.
     The device-UAT item-by-item PASS/FAIL/N/A checklist lives in 08-DEVICE-UAT.md. -->

### 1. Gate A — Device UAT (ROADMAP SC1 + SC2)
expected: `08-DEVICE-UAT.md` run end-to-end on a real device build (Android primary); Overall = PASS, or every FAIL filed as a gap-closure plan (`/gsd-plan-phase 08 --gaps`). Covers auth/2FA, chat list/history/media incl. the 05-07/08 treatments (.tgs card / bubble-free кружок / GIF badge), the 05-09 field/UI fixes, the vthumb id-ambiguity probe, the switcher, auto-reply e2e, live «Вместе» + dashboard, and the carried v1.0 scenarios (run or explicitly re-defer with a reason). Groups G/H need dev n8n (localhost:5678) + cloudflared tunnel — prod bagkz stays dormant.
result: **ISSUES (owner ran 2026-07-15/16; recorded 2026-07-16; clarifications folded in same day).** 51/51 items dispositioned — the bulk is green (auth/2FA, chat core, 05-09 fixes, switcher, auto-reply e2e, dashboard); FAILs consolidated into defect rows **D1–D9** in `08-DEVICE-UAT.md` §Defects found (2 high: D5 live-incoming render on BOTH channels incl. H2 stale-suggestion relevance, D7 TG service-dialog duplication + cross-channel bleed; 4 medium: D1 REACTION_INVALID, D2 reaction-removal stuck, D3 video-note presentation, D6 bot-creation NRE with owner-provided SwipeToDelete stack; 3 owner-approved polish: D4 remove TG swipe affordance, D8 RU empty-state copy, D9 TG sync indicator). I.3 re-deferred (blocked by D5); G6 clone-deactivation reminder outstanding. Gap planning started (`/gsd-plan-phase 08 --gaps`).

### 2. Gate B — Prod replication (PROD-01)
expected: `08-PROD-REPLICATION.md` executed once against dormant prod bagkz with post-import `verify-telegram-parity.py --dir <prod-export>` go/no-go GREEN — OR explicitly deferred with a recorded reason. Both bot templates INACTIVE, no bot clone created/activated, prod left dormant. NOTE: the move to production URLs happens only when the owner explicitly says go — this gate is owner-run and Claude-blocked (prod API key + secrets.json deny-ruled).
result: [pending]

### 3. Gate C — Milestone close (v1.1 Telegram Parity)
expected: after Gate A (PASS/gaps-filed) and Gate B (executed GREEN or deferred) are dispositioned, confirm both in `08-MILESTONE-CLOSE.md`, then `/clear` and run `/gsd-complete-milestone` for v1.1. Verify SUPPRESS-01 survived into v1.2 Phase 9 and any re-deferred v1.0 UAT landed in STATE Deferred Items.
result: [pending]

## Summary

total: 3
passed: 0
issues: 1
pending: 2
skipped: 0
blocked: 0

## Gaps

<!-- Any Gate-A device FAIL is recorded in 08-DEVICE-UAT.md's Defects table and
     becomes its own gap-closure plan; log a one-line pointer here when that happens. -->

- **Gate A (2026-07-16):** defects **D1–D9** logged in `08-DEVICE-UAT.md` §Defects found →
  gap-closure planning via `/gsd-plan-phase 08 --gaps` (started 2026-07-16). Highs: D5
  (incoming never renders in the open chat until re-enter — BOTH channels; «Вместе» cards
  stale, H2 relevance folded in) + D7 (TG service-dialog duplicated in the TG list — logo
  avatar + silhouette rows — and visible in the WA list). All owner clarifications received:
  D5 both-channels, D6 SwipeToDelete stack, D7 identity, O1→D9 sync indicator. Re-tests after
  fixes: H2 (relevance + RAG grounding), I.3 aggregation, B7 static webp.
