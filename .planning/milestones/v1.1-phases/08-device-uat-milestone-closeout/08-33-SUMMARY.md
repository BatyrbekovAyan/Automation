---
phase: 08-device-uat-milestone-closeout
plan: 33
subsystem: testing
tags: [device-uat, gate-a, reactions, telegram, whatsapp, sync-cover, checkpoint]

# Dependency graph
requires:
  - phase: 08 (08-30)
    provides: D2-view confirmation-clears-grace + WR-01 tombstone drop-on-confirmed-absence + Editor [D2-merge] log
  - phase: 08 (08-31)
    provides: WR-02 revert (in-app WA reaction-removal stays removed) + D15 platform-limit disposition + Editor [D15-probe]
  - phase: 08 (08-32)
    provides: D17 late-WhatsApp-auth sync-cover stamp ({bot}WhatsappSyncUntil)
provides:
  - "Round-6 owner verdicts transcribed VERBATIM into 08-DEVICE-UAT.md §Round 6 (4 PASS, 1 FAIL, 1 probe-did-not-fire) with the two-screenshot Console log evidence"
  - "D17 RESOLVED (late-WhatsApp-auth cover, owner 'ok') + WR-02 RESOLVED (in-app WA removal stays removed) recorded in §Defects"
  - "D2-view residual isolated EXACTLY: an external own-change that DIFFERS from the optimistic emoji is suppressed for the full grace window — confirmed by the [D2-merge] discriminator (age 9s→21s, climbing)"
  - "D15 platform-limit disposition UNCONFIRMED-not-refuted (probe never triggered — no WA quoted-reply resolve); CLAUDE.md note STAYS marked 'probe confirmation pending'"
  - "Gate A stays ISSUES; round-7 scope named (D2-view grace discrimination + D15 deterministic probe trigger); Gates B/C + I.3 #10 stay blocked; prod bagkz dormant"
affects: [round-7 gap planning, Gate A milestone v1.1 close, Gates B/C]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Checkpoint continuation: owner verdicts + Editor Console screenshots transcribed verbatim, analysis labeled ORCHESTRATOR ANALYSIS, discriminating logs captured into the Defects row"

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-33-SUMMARY.md
  modified:
    - .planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md

key-decisions:
  - "Gate A stays ISSUES — 1 FAIL (D2-view external-change-during-grace) blocks the milestone close; round 7 next"
  - "D2-view residual is NOT a new mechanism — the [D2-merge] discriminator proves the round-6 confirmation-clears-grace fix is defeated by tapi's current-state-only reactions[] (the confirming SAME-emoji echo never arrives), so the first server-me is already the newer differing emoji and the never-clear-on-differ branch eats it"
  - "D15 platform-limit note STAYS (not refuted) despite the probe not firing — the probe seam only triggers on a WA quoted-reply resolve, which did not happen; round-7 gives it a deterministic Editor-only trigger"
  - "WR-02 (08-27 candidate-(a) regression) is RESOLVED and is DISTINCT from D15 (the WhatsApp-app-side removal platform limit) — the owner's item-2 remaining complaint is D15, not a WR-02 re-fail"

patterns-established:
  - "Pattern: a reconcile grace ended by 'server confirmation' fails when the transport never echoes the confirming intermediate state — grace discrimination must track the DISPLACED value or clear on the send-HTTP-success, not wait for a same-value echo"

requirements-completed: []

# Metrics
duration: ~10min
completed: 2026-07-21
---

# Phase 8 Plan 33: Round-6 Owner Re-verify — Differ-During-Grace Residual Isolated, D17/WR-02 Resolved Summary

**Round-6 Gate A owner re-verify recorded: 4 PASS (WR-02 in-app WhatsApp removal stays removed, WA add/change invariant, TG add/change/remove invariant, D17 late-WhatsApp cover), 1 FAIL (D2-view — the `[D2-merge]` discriminator proves an external own-change that DIFFERS from the optimistic emoji is suppressed for the full grace window because tapi never echoes the confirming intermediate state), D15 probe did not fire (disposition unconfirmed-not-refuted); Gate A stays ISSUES, round 7 scoped.**

## Performance

- **Duration:** ~10 min (checkpoint continuation — owner ran the Editor Play-Mode pass and returned verdicts + two Console screenshots)
- **Started:** 2026-07-21T08:14:00Z (approx — continuation spawn)
- **Completed:** 2026-07-21T08:24:08Z
- **Tasks:** 1 (checkpoint:human-verify, owner-run)
- **Files modified:** 2 (08-DEVICE-UAT.md + this SUMMARY)

## Accomplishments

- **Recorded the round-6 owner verdicts VERBATIM** into 08-DEVICE-UAT.md §Round 6, each mapped to its source anchor, with the two-screenshot Unity Editor Console log evidence transcribed as a captured-log block.
- **D17 RESOLVED** (owner "ok" item 5): a Telegram-first bot that authorizes WhatsApp later now shows the WhatsApp post-creation sync cover — the 08-32 `{bot}WhatsappSyncUntil` late-auth stamp fires the cover gate (exact mirror of 08-28's Telegram stamp). Defects row D17 → RESOLVED.
- **WR-02 RESOLVED** (owner item 2): an own WhatsApp reaction removed IN-APP now stays cleared across polls (Screenshot 2 shows the removal `n=0` applying against the still re-delivering stale add-raw — the 08-31 revert holds). Recorded distinct from D15.
- **D2-view residual isolated EXACTLY** (owner "still same behavior. see screenshot", item 1 FAIL): the `[D2-merge]` discriminator built in 08-30 fires on every subsequent own-change (`suppressed server-me '🔥' by fresh local '🥺' age=9s` → `'👎' age=21s`, climbing). Root cause pinned: tapi `reactions[]` carries only the CURRENT state, so the confirming SAME-emoji echo of the in-app tap (🥺) never arrives; the first observed server-me is already the newer Telegram-app emoji which DIFFERS from the optimistic one, so the never-clear-on-differ branch eats it for the full 90s grace. The round-6 confirmation-clears-grace fix cannot distinguish "stale pre-tap state" from "genuinely newer external change".
- **D15 platform-limit disposition recorded as UNCONFIRMED-not-refuted** (owner "do not see any reactionsKey=False", item 6): the `[D15-probe]` never fired — it only triggers on a WhatsApp quoted-reply resolve via `messages/id/get`, which did not happen this session. The CLAUDE.md WhatsApp reaction-removal platform-limit note STAYS (round-5 evidence still supports it), marked "probe confirmation pending".
- **Gate A stays ISSUES**; round-7 scope named (D2-view grace discrimination + D15 deterministic probe trigger). Gates B/C + I.3 #10 re-aggregation stay blocked; prod bagkz dormant. G6 resolved (not carried); echo-hex closed (not carried).

## Round-6 Verdict Table

| # | Item | Verdict | Owner (verbatim) |
|---|------|---------|------------------|
| 1 | D2-view — rapid own-reaction change in the Telegram app repaints every time | **FAIL** | "still same behavior. see screenshot" |
| 2 | WR-02 — own WhatsApp reaction removed in-app STAYS removed across polls | **PASS** | "…the pill stays cleared across several polls. this works now and worked before. what needs the fix is that removing reaction in whatsapp app doest remove reaction in our app." |
| 3 | D2-view — WhatsApp add/change unaffected (invariant) | **PASS** | "pass" |
| 4 | Telegram add/change/remove still correct (invariant) | **PASS** | "add/change/remove reaction in telegram channel in our app works" |
| 5 | D17 — late WhatsApp auth on an existing bot shows the WhatsApp sync cover | **PASS** | "ok" |
| 6 | D15 disposition — no WhatsApp reaction-state key surfaces | **PROBE DID NOT FIRE** (unconfirmed-not-refuted) | "see screenshot, do not see any reactionsKey=False" |
| 7 | D2-view residual `[D2-merge]` discriminator (nice-to-have) | **FIRED** (captured — item 1 FAILED) | "see screenshot." |

**Round-6 Overall: ISSUES. Gate A stays ISSUES.**

## Captured Log Evidence (two Unity Editor Console screenshots)

**Screenshot 1 (item 1 D2-view + item 7 `[D2-merge]`):**
- 12:39:57 `[D2-merge] suppressed server-me '🔥' by fresh local '🥺' age=9s`
- 12:40:00 `[TG reaction echo] '🔥' [U+1F525]` → `[D2-merge] … age=12s`
- 12:40:03 `[D2-merge] … '🔥' … age=15s`
- 12:40:06 `[TG reaction echo] '👎' [U+1F44E]` → `[D2-merge] … '👎' … age=18s`
- 12:40:09 `[D2-merge] … '👎' … age=21s`
- Stack: `TelegramReactionMerge:Merge` (TelegramReactionMerge.cs:79) ← `ChatManager:RefreshCachedMessageReactions` (ChatManager.cs:1862) ← `SyncLatestMessages` (752).

**Screenshot 2 (item 2 WR-02 PASS + item 6 probe absent):**
- 13:14:32 `[D2-view] reactions changed id=3EB0A97D71DC0A5FAEA3F7 n=1` + `post-render active=True len=28 culled=False` (ADD applied)
- 13:14:34..:53 `[D15] wa-reaction rawId=3EB09C1E576856CD345288 stanza=3EB0A97D71DC0A5FAEA3F7 bodyEmpty=False seen=True` (SAME add-raw re-delivering every ~3s)
- 13:14:52 `[D2-view] reactions changed id=3EB0A97D71DC0A5FAEA3F7 n=0` + `post-render active=False len=28 culled=False` (in-app removal applying and STAYING — WR-02 confirmed)
- **NO `[D15-probe]` line anywhere** (no WA quoted-reply resolve → probe seam never triggered).

## Round-7 Scope (as recorded)

- **D2-view — grace discrimination:** so an external own-change that DIFFERS from the optimistic emoji is never suppressed. Candidates (a/b/c): (a) track the DISPLACED emoji (what the optimistic tap replaced) — suppress ONLY when server-me equals it, any THIRD value adopts+clears; (b) clear the grace on reaction-send HTTP success (any later differing server-me is genuinely newer), possibly with a short residual; (c) drastically shorten the 90s grace.
- **D15 — deterministic probe trigger:** give the `[D15-probe]` an Editor-only one-shot auto-probe on the FIRST WhatsApp `type:"reaction"` raw seen (resolve its TARGET stanza id via the existing authed seam) so the platform-limit disposition can be confirmed-or-refuted without owner choreography. The CLAUDE.md platform-limit note stays until then.
- **Do NOT touch Gates B/C or I.3 #10 this pass.**

## Task Commits

No per-task code commits — this is a checkpoint continuation that records owner verdicts into planning docs only (no app code changed).

**Plan metadata:** (docs commit — SUMMARY/STATE/ROADMAP/08-DEVICE-UAT).

## Files Created/Modified

- `.planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md` — §Round 6 filled with verbatim verdicts + the two-screenshot captured-log block + labeled ORCHESTRATOR ANALYSIS per item; status header → RUN 2026-07-21 ISSUES; Round-6 Overall + Gate A = ISSUES; round-7 scope line; §Defects rows updated (D2-view differ-during-grace residual with the `[D2-merge]` capture; D15 probe-pending-deterministic-trigger + WR-02 RESOLVED note; D17 → RESOLVED); §Overall-result round-6 bullet added.
- `.planning/phases/08-device-uat-milestone-closeout/08-33-SUMMARY.md` — this file.

## Decisions Made

See key-decisions frontmatter. In short: Gate A stays ISSUES on the single D2-view FAIL; the residual is the round-6 fix being defeated by tapi's current-state-only reactions[] (no confirming echo); D15's platform-limit note stays (probe never triggered ≠ refuted); WR-02 is resolved and distinct from D15.

## Deviations from Plan

None - plan executed exactly as written. The plan's Task 1 is a `checkpoint:human-verify` gate; this continuation records the owner's returned verdicts. No auto-fixes, no blocking issues, no authentication gates. No app code changed (planning docs only, per the plan's standing rule — staged ONLY the phase planning docs + STATE/ROADMAP, never `git add -A`).

## Known Stubs

None. Documentation-only recording of owner verdicts.

## Issues Encountered

None during planned work. The one nuance handled: the owner's item-2 message bundles a PASS (in-app removal stays removed = WR-02) with a restatement of the still-open D15 (removal in the WhatsApp app itself) — recorded as PASS for WR-02 with D15 kept open as the documented platform limit, not a WR-02 re-fail.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **Round 7 gap planning** is next: `/gsd-plan-phase 08 --gaps` for D2-view (grace discrimination — candidates a/b/c) + D15 (deterministic `[D15-probe]` trigger). Gate A stays ISSUES until D2-view closes; a device pass still gates the final Gate A sweep even though the fix loop is Editor-reproducible.
- Gates B (prod replication, 08-02) + C (milestone close, 08-03) + I.3 #10 (01-VERIFICATION sign-off) remain blocked behind Gate A; prod bagkz stays dormant.
- The `[D2-merge]` / `[D15-probe]` / `[D15]` / `[D2-view]` Editor diagnostics stay armed for round 7; all are tagged for removal at phase close (IN-03/IN-04).

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-33-SUMMARY.md`
- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md` §Round 6 filled (status-header → RUN 2026-07-21 ISSUES; verdicts 1–7 transcribed verbatim; two-screenshot captured-log block; Round-6 Overall + Gate A = ISSUES; round-7 scope line)
- FOUND: §Defects updated — D2-view `RE-FAIL @ round-6 … residual mechanism now EXACT`, D15 `probe DID NOT FIRE … probe confirmation pending` + WR-02 RESOLVED note, D17 `RESOLVED @ round-6`
- FOUND: STATE.md advanced (completed_plans 60→61, stopped_at 08-33, [08-33] evolution entry, round-6 blocker line, P33 metric, session continuity), ROADMAP.md 08-33 checked off + Phase 8 status note updated
- No per-task code commits to verify — checkpoint continuation records owner verdicts into planning docs only (no app code changed)

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-21*
