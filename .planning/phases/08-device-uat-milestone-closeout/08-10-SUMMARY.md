---
phase: 08-device-uat-milestone-closeout
plan: 10
subsystem: uat
tags: [device-uat, gate-a, re-verify, checkpoint, gap-closure]

# Dependency graph
requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "Gap plans 08-04..08-09 executed + 08-REVIEW WR-01/02/03 fixed (tree @ 1b2e60b, 1093/1093 EditMode green)"
provides:
  - "Owner device re-verify verdicts for every D1–D9 item (one Android build, one ordered pass)"
  - "Gate A disposition: ISSUES REMAIN — scope reduced to D2 (refined), D9, plus new D10/D11/D12"
affects: [gap-closure-round-2, 08-DEVICE-UAT.md, milestone-close-gates]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - ".planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md"

key-decisions:
  - "Verdicts transcribed from the owner's stated results only (T-08-10-01); nothing ticked on the owner's behalf"
  - "D5's WhatsApp suggestion-relevance residual split out as D10 rather than keeping D5 open — the live-render core (both channels), card refresh, and draft protection all PASSED"
  - "D2's new symptoms recorded as a refinement of D2 (same component stack), with the 08-REVIEW IN-01/IN-06 VS16-identity hypothesis attached for the round-2 planner"

metrics:
  duration: "owner pass 2026-07-17"
  completed: "2026-07-17"
---

# 08-10 Summary — Owner device re-verify of D1–D9 (Gate A disposition)

One Android build off `1b2e60b` (all six gap plans + the three 08-REVIEW warning fixes), one ordered pass, run by the owner on 2026-07-17.

## Verdicts

| # | Item | Anchor | Verdict |
|---|------|--------|---------|
| 1 | D5 — live incoming, BOTH channels | I.1 #3 / I.2 #6 / H2 | **PASS (core)** — renders within ~one cycle without re-entering on WA+TG; «Вместе» cards refresh; draft survives. **Residual → D10:** suggestions relevant on Telegram but IRRELEVANT on WhatsApp |
| 2 | D7 — TG service dialog | Extra #3 / CHAT-11 | **PASS** — one row on TG, absent from WA, no real WA chat lost |
| 3 | D1 — TG reactions add | B9a | **PASS** — quick-bar + «+» picker picks succeed, no REACTION_INVALID |
| 4 | D2 — TG reaction remove | B9b / B13 | **FAIL (refined)** — own reaction shows count «2»; changing a reaction can leave BOTH pills; adding ❤ next to an existing reaction renders TWO different heart glyphs. Hypothesis: optimistic-vs-tapi-echo emoji identity mismatch (VS16/alternate codepoint) breaking merge/tombstone/count equality — pre-flagged as 08-REVIEW IN-01 + IN-06 |
| 5 | D3a — badge corners | B5 | **PASS** |
| 6 | D3b — incoming note float | E1 | **PASS** |
| 7 | D4 — no TG swipe affordance | B12 / F8 | **PASS** (WA swipe-delete intact) |
| 8 | D6 — bot-creation NRE | Extra #1 | **PASS** — no NRE on auto-return to Bots |
| 9 | D8 — RU empty-state copy | F9 | **PASS** |
| 10 | D9 — TG sync indicator | Extra #2 / O1 | **FAIL** — no indicator visible; list appears instantly (pill + events shipped in 08-09; needs runtime diagnosis: sync-faster-than-paint, event-vs-OnEnable ordering, gate timing, or occlusion) |
| 11 | B7 — static webp sticker (optional) | B7 | **PASS** (was N/A at Gate A) |

**New observations filed:**
- **D11** — some video files never download, incl. GIFs and video notes (owner suspects Wappi/tapi server-side; instrument `message/media/download` failures first).
- **D12** — Telegram empty-state create-bot CTA does nothing; expected: WhatsApp CTA flow with Telegram preselected in the add-bot form.

## Gate A disposition

**Gate A stays ISSUES** — materially reduced: 7/9 original defects resolved on device (+B7 now PASS), open set is now **D2 (refined), D9, D10, D11, D12**. Each is filed in the 08-DEVICE-UAT.md Defects table with anchors and diagnosis leads; the next round is `/gsd-plan-phase 08 --gaps`. I.3 #10 stays re-deferred until D10 closes (I.1 #3 itself is now device-confirmed).

Carried reminder (bot-activation policy): **deactivate the dev test clone** now that the re-verify window is done — clones run against real contacts.

## Deviations

None — checkpoint plan; no code changed. Verdicts recorded verbatim from the owner's report.

## Self-Check: PASSED

- 08-DEVICE-UAT.md Defects table updated with all dispositions + D10/D11/D12 rows — verified by re-read.
- This SUMMARY contains the per-item verdicts and the "Gate A" disposition (must_have artifact contract).
- No source files modified; working tree carries only pre-existing unrelated changes.
