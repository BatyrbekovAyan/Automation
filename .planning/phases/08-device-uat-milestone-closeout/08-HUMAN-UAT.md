---
status: partial
phase: 08-device-uat-milestone-closeout
source: [08-VERIFICATION.md]
started: "2026-07-15T10:10:43Z"
updated: "2026-07-15T10:10:43Z"
---

## Current Test

[awaiting human testing — three owner-run gates, run in order A → B → C]

## Tests

<!-- Each gate points at its purpose-built runbook rather than duplicating its items.
     The device-UAT item-by-item PASS/FAIL/N/A checklist lives in 08-DEVICE-UAT.md. -->

### 1. Gate A — Device UAT (ROADMAP SC1 + SC2)
expected: `08-DEVICE-UAT.md` run end-to-end on a real device build (Android primary); Overall = PASS, or every FAIL filed as a gap-closure plan (`/gsd-plan-phase 08 --gaps`). Covers auth/2FA, chat list/history/media incl. the 05-07/08 treatments (.tgs card / bubble-free кружок / GIF badge), the 05-09 field/UI fixes, the vthumb id-ambiguity probe, the switcher, auto-reply e2e, live «Вместе» + dashboard, and the carried v1.0 scenarios (run or explicitly re-defer with a reason). Groups G/H need dev n8n (localhost:5678) + cloudflared tunnel — prod bagkz stays dormant.
result: [pending]

### 2. Gate B — Prod replication (PROD-01)
expected: `08-PROD-REPLICATION.md` executed once against dormant prod bagkz with post-import `verify-telegram-parity.py --dir <prod-export>` go/no-go GREEN — OR explicitly deferred with a recorded reason. Both bot templates INACTIVE, no bot clone created/activated, prod left dormant. NOTE: the move to production URLs happens only when the owner explicitly says go — this gate is owner-run and Claude-blocked (prod API key + secrets.json deny-ruled).
result: [pending]

### 3. Gate C — Milestone close (v1.1 Telegram Parity)
expected: after Gate A (PASS/gaps-filed) and Gate B (executed GREEN or deferred) are dispositioned, confirm both in `08-MILESTONE-CLOSE.md`, then `/clear` and run `/gsd-complete-milestone` for v1.1. Verify SUPPRESS-01 survived into v1.2 Phase 9 and any re-deferred v1.0 UAT landed in STATE Deferred Items.
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps

<!-- Any Gate-A device FAIL is recorded in 08-DEVICE-UAT.md's Defects table and
     becomes its own gap-closure plan; log a one-line pointer here when that happens. -->
