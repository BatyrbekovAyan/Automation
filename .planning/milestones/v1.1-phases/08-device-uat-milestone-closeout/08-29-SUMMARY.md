---
phase: 08-device-uat-milestone-closeout
plan: 29
subsystem: device-uat
tags: [device-uat, gate-a, checkpoint, telegram, whatsapp, reactions, sync-cover, gap-closure, milestone-v1.1]

requires:
  - phase: 08-26 (D2-view poll-path hardened re-render + WR-01)
    provides: HandleReactionsChanged routed through the 08-22 one-frame-deferred re-render + [D2-view] post-render state log
  - phase: 08-27 (D15 WhatsApp reaction-removal ingest)
    provides: candidate-(a) already-seen re-process + [D15] wa-reaction shape log (rawId/stanza/bodyEmpty/seen)
  - phase: 08-28 (D16 late-channel Telegram sync cover)
    provides: {bot}TelegramSyncUntil stamp on late Telegram auth (ShowAuthSuccess settings-reauth branch)
provides:
  - Round-5 owner device re-verify verdicts (D2-view / D15 / D16 + WhatsApp invariants) transcribed VERBATIM into 08-DEVICE-UAT.md with the discriminating [D2-view]/[D15] log captures
  - Gate A disposition = ISSUES (D2-view relocated UPSTREAM, D15 removal-shape answered, D17 minted, D16 RESOLVED, echo-hex CAPTURED/CLOSED)
  - Round-6 scope named: D2-view own-reaction event-suppression / D15 absence-based WA reconcile (or platform limit) / D17 late-WhatsApp-auth cover stamp
affects: [08-30+ (round-6 gap plans), 08-02 (prod replication — still blocked), 08-03 (milestone close — still blocked), I.3 #10 (01-VERIFICATION sign-off — still re-deferred)]

tech-stack:
  added: []
  patterns:
    - "Checkpoint gap-closure re-verify: transcribe owner verdicts VERBATIM + capture the discriminating compiled logs; orchestrator analysis is clearly LABELED and separated from owner input"

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-29-SUMMARY.md
  modified:
    - .planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md

key-decisions:
  - "D2-view mechanism REFUTED-and-RELOCATED: the captured [D2-view] logs show a HEALTHY post-render on the first change but NO change event at all for subsequent own-reaction changes — the view layer (08-22/08-26) is EXONERATED; round-6 target is UPSTREAM own-reaction event-suppression in TelegramReactionMerge's 90s optimistic-grace window (echo-without-event evidence)"
  - "D15 removal SHAPE answered = candidate (b) no removal raw: the [D15] logs show the ADD raw re-delivering (seen=True) every poll, NO empty-body raw ever after the in-WhatsApp removal — 08-27's candidate-(a) re-process fix is correct-but-INERT; round-6 = absence-based WA reconcile OR documented Wappi platform limit"
  - "D17 minted (owner scope-override): the 08-28 parity decision (no late-auth WA stamp) behaved as designed but the owner overrode it — covers must show for BOTH channels on every late-add; D17 mirrors the 08-28 Telegram stamp with {bot}WhatsappSyncUntil (supersedes the 08-28 parity decision, owner-approved like D14)"
  - "Gate A STAYS ISSUES — 2 FAIL + 1 scope-override; D16 RESOLVED + echo-hex CLOSED do not clear the gate; Gates B/C + I.3 #10 stay blocked, prod bagkz dormant; round 6 next"

patterns-established:
  - "A FAIL with a discriminating compiled log can REFUTE the prior fix's mechanism and RELOCATE the defect to a different layer (D2-view: view → upstream event-suppression) — the log is the whole point of the round"

requirements-completed: []

duration: ~15min
completed: 2026-07-21
---

# Phase 08 Plan 29: Round-5 Owner Device Re-Verify (D2-view / D15 / D16) Summary

**Round-5 Gate A checkpoint RUN 2026-07-21 — Overall ISSUES, Gate A STAYS ISSUES. The owner ran ONE build off the post-08-28 tree and returned verdicts WITH two Unity Editor Console screenshots: D16 late-Telegram cover RESOLVED (owner "pass"); D2-view + D15 STILL FAIL but the captured logs are decisive — D2-view is REFUTED-and-RELOCATED UPSTREAM (a healthy post-render on the first change, then NO change event at all for subsequent own-reaction changes → view layer exonerated, round-6 target = own-reaction event-suppression in `TelegramReactionMerge`), and D15's removal shape is ANSWERED (candidate (b) no removal raw — the ADD raw just keeps re-delivering, no empty-body raw ever after removal). The owner OVERRODE the 08-28 parity decision → new D17 (late-WhatsApp-auth cover stamp). Echo-hex CAPTURED at last (ask CLOSED).**

## Performance

- **Duration:** ~15 min (checkpoint continuation — transcribe verdicts + log evidence, file/update defects, mint D17, write SUMMARY + STATE/ROADMAP)
- **Completed:** 2026-07-21
- **Tasks:** 1 (checkpoint:human-verify — owner-run gate)
- **Files modified:** 1 (08-DEVICE-UAT.md) + 1 created (this SUMMARY)

## Accomplishments

- **Ran the round-5 Gate A checkpoint** (the milestone v1.1 gate). The owner built ONE app off the post-08-28 tree (08-26 D2-view + 08-27 D15 + 08-28 D16 all merged), re-verified D2-view / D15 / D16 + the WhatsApp byte-identical invariants, and returned per-item verdicts plus two Unity Editor Console screenshots carrying the discriminating `[D2-view]` / `[D15]` / `[TG reaction echo]` log lines.
- **Transcribed every verdict VERBATIM** into the §Round 5 re-verify block, each mapped to its source anchor, with the captured Console lines pasted into the FAIL items and a dedicated log-evidence timeline block. Orchestrator analysis is clearly LABELED (`ORCHESTRATOR ANALYSIS (labeled — not owner input)`) and separated from owner input throughout.
- **D16 RESOLVED** (owner "pass" item 5) — a WhatsApp-first bot that authorizes Telegram later now shows the Telegram post-creation sync cover (the 08-28 `{bot}TelegramSyncUntil` late-auth stamp fires the 08-19 cover gate).
- **D2-view mechanism REFUTED-and-RELOCATED UPSTREAM.** The captured `[D2-view]` logs show the first remote change fired `OnMessageReactionsChanged` and the 08-26 hardened re-render reported a HEALTHY post-render (`active=True len=24 culled=False` → exception + cull candidates both refuted), but the two SUBSEQUENT emoji changes (😁 U+1F601, 👌 U+1F44C) produced only `[TG reaction echo]` at Normalize level and NO change event at all. The view layer (08-22/08-26) is EXONERATED; round-6 target moves upstream.
- **D15 removal SHAPE ANSWERED = candidate (b) no removal raw.** The `[D15]` logs show the ADD raw re-delivering `bodyEmpty=False seen=True` on every poll, with NO empty-body raw ever arriving after the in-WhatsApp removal — 08-27's candidate-(a) re-process fix is correct-but-INERT for removal (harmless, idempotent).
- **Minted D17** (owner scope-override) — the 08-28 parity decision behaved exactly as designed (no WA cover) but the owner overrode it: covers must show for BOTH channels on every late-add. D17 mirrors the 08-28 Telegram stamp with `{bot}WhatsappSyncUntil` on late WhatsApp auth; it SUPERSEDES the 08-28 parity decision (owner-approved scope change, like D14).
- **Echo-hex CAPTURED and CLOSED** — the previously-uncaptured (4 checkpoints) tapi reaction-echo hex is in Screenshot 1: BASE-form codepoints (U+1F44D / U+1F601 / U+1F44C, no U+FE0F) with `user_id == ownId` (1038376805), confirming the round-2 `ReactionEmoji` base-form finding (08-11). This same echo-without-event evidence is what relocated D2-view.
- **Gate A held at ISSUES**; Gates B (prod replication, 08-02) + C (milestone close, 08-03) + I.3 #10 (01-VERIFICATION sign-off) stay blocked; prod bagkz stays dormant. Round 6 scope named.

## Round-5 verdict table (owner verbatim, transcribed 2026-07-21)

| # | Item | Verdict | Owner (verbatim) |
|---|------|---------|------------------|
| 1 | D2-view — reaction changed IN Telegram repaints the bubble | **FAIL** | "still not updating reaction when changed in telegram. logs show right reaction but it doesnt update on message bubble. (Screenshot 1, logs update reaction, but not on screen.)" |
| 2 | D2-view — WhatsApp add/change unaffected | **PASS** | "pass" |
| 3 | D15 — reaction REMOVED in the WhatsApp app clears in-app | **FAIL** | "still same, removing reaction in whatsaap doesnt remove it in our app. (Screenshot 2)" |
| 4 | D15 — WhatsApp add/change still repaints (invariant) | **PASS** | "pass" |
| 5 | D16 — late Telegram auth shows the Telegram sync cover | **PASS** | "pass" |
| 6 | D16 — late WhatsApp auth shows NO new cover (byte-identical) | **SCOPE-OVERRIDE → D17** | "when telegram channel exist on bot and then when adding whatsapp channel to same bot there is no sync chats cover page for whatsapp (should be sync chats cover page for both channels every time they are just added)" |
| 7 | D2-ext echo-hex (nice-to-have) | **CAPTURED / CLOSED** | "screenshot 1 have [TG reaction echo] logs, hope it is what you need" |

**Round-5 Overall:** ISSUES — 3 PASS (2/4/5, incl. D16 RESOLVED), 2 FAIL (1 D2-view relocated, 3 D15 shape-answered), 1 owner scope-override (6 → D17), echo-hex CAPTURED. **Gate A STAYS ISSUES.**

## Captured log evidence (Unity Editor Console — repro ran in the EDITOR, not on device)

**Screenshot 1 (D2-view item 1 + echo-hex item 7) — timeline:**
- 00:23:58 `[TG reaction echo] '👍' [U+1F44D] user_id=1038376805 ownId=1038376805`
- 00:24:00 `[D2-view] reactions changed id=23475 n=1`
- 00:24:00 `[D2-view] post-render id=23475 active=True len=24 culled=False`
- 00:24:01 `[TG reaction echo] '👍' [U+1F44D]` ; 00:24:04 `'😁' [U+1F601]` ; 00:24:07 `'👌' [U+1F44C]`
- 00:24:10 / :13 / :16 / :19 / :22 `[TG reaction echo] '👌' [U+1F44C]` (same echo re-delivered each poll)
- Stack: `ChatManager:LogTelegramReactionEcho` (ChatManager.cs:1712) ← `Normalize` (1671) ← `SyncLatestMessages` (754).
- **KEY:** NO further `[D2-view]` change/post-render lines after 00:24:00 despite the emoji changing twice more → change event stops firing after the first own-user reaction applies (upstream event-suppression).

**Screenshot 2 (D15 item 3) — timeline:**
- 00:33:20 `[D15] wa-reaction rawId=3A8976F33979EE5EE8EB stanza=3AAFD6395EE4345C8EA0 bodyEmpty=False seen=False` (ADD)
- 00:33:20 `[D2-view] reactions changed id=3AAFD6395EE4345C8EA0 n=1` + `post-render active=True len=24 culled=False`
- 00:33:22 `[D15] wa-reaction rawId=… stanza=… bodyEmpty=False seen=True`
- 00:33:25 `[D15] wa-reaction rawId=… bodyEmpty=False seen=True` (same add-raw re-delivered; NO empty-body raw EVER after the in-WhatsApp removal)

The Editor reproduces BOTH failures — round-6 fix loop is Editor-reproducible (no device build needed for the fix; a device pass still gates Gate A).

## Defect updates (08-DEVICE-UAT.md §Defects)

- **D2-view** — RE-FAIL @ round 5, mechanism REFUTED-and-RELOCATED UPSTREAM (own-reaction event-suppression in `TelegramReactionMerge`'s 90s optimistic-grace window); view layer exonerated. Round-6 scope recorded with the labeled hypothesis + Editor-reproducible test ask.
- **D15** — RE-FAIL @ round 5, removal shape ANSWERED = candidate (b) no removal raw; 08-27 candidate-(a) fix correct-but-inert. Round-6 scope = absence-based WA reconcile or documented Wappi platform limit.
- **D16** — RESOLVED @ round 5 (owner "pass" item 5).
- **D17** — MINTED (owner scope-override, item 6): late-WhatsApp-auth cover stamp `{bot}WhatsappSyncUntil`, exact mirror of the 08-28 Telegram stamp; supersedes the 08-28 parity decision.
- **D2-ext** — echo-hex CAPTURED & CLOSED (BASE-form codepoints, `user_id == ownId`; confirms 08-11).

## Round-6 scope (recorded)

**D2-view** upstream event-suppression fix (own-reaction grace in `TelegramReactionMerge` should key on a pending LOCAL optimistic set, not mere own-identity — evidence: echo-without-event; Editor-reproducible EditMode test); **D15** absence-based WhatsApp removal reconcile (poll the target message's `reactions[]` state and clear the `ReactionStore` entry when the server no longer carries it — OR document as a Wappi WA platform limit); **D17** late-WhatsApp-auth cover stamp (`{bot}WhatsappSyncUntil`, exact mirror of 08-28's Telegram stamp — supersedes the 08-28 parity decision).

## Deviations from Plan

- **[Rule 2 — Missing critical scope] D17 minted from the owner's item-6 scope-override.** The plan authored item 6 as a WhatsApp byte-identical check (expected: no new WA cover). The behaviour was CONFIRMED (no cover, as the 08-28 parity decision designed), but the owner overrode the decision itself — covers must show for BOTH channels on every late-add. This is an owner-approved scope change (exactly like D14 was), so a new defect D17 was minted rather than recording item 6 as a plain PASS or FAIL. No app code changed (planning docs only), consistent with the plan's "no app code changes in this plan."
- Otherwise the plan executed as written: verdicts transcribed verbatim, FAIL logs captured into the Defects rows, Gate A dispositioned to ISSUES with round-6 scope named.

## Testing

- No code changed in this plan (checkpoint re-verify — planning docs only). The pre-build gate (EditMode suite green FRESH at the current baseline +5 = 1181) was authored into the round-5 runbook and is the owner's build precondition; the round-5 build the owner ran was off the post-08-28 tree that carried the 1181/1181 green (08-26 → 1180, 08-27 → 1181, 08-28 → 1181 delta-0).

## Known Stubs

None — planning-doc transcription only; no code, no placeholder UI, no unwired data source.

## Threat Flags

None — no new network endpoint, secret, auth path, file access, or schema change. This plan records owner verdicts into planning docs; `secrets.json` is deny-ruled and referenced by name only (T-08-29-01 accept: no code, no new surface).

## Issues Encountered

None blocking. The owner's two-part reply (verdicts + two Console screenshots) arrived complete this pass — the discriminating logs the round was designed to capture were all present, which is what let both FAILs be resolved to a precise round-6 target instead of another guess.

## Next Phase Readiness

- **Round 6** can be planned via `/gsd-plan-phase 08 --gaps` for D2-view (upstream event-suppression) / D15 (absence-based WA reconcile or platform limit) / D17 (late-WA-auth cover stamp). All three are Editor-reproducible (D2-view + D15 proven so this pass; D17 is a deterministic stamp mirror), so the round-6 fix loop does not need a device build — though a device pass still gates Gate A.
- **Gate A stays ISSUES.** Gates B (prod replication, 08-02) + C (milestone close, 08-03) + I.3 #10 (01-VERIFICATION sign-off) remain blocked; prod bagkz stays dormant. G6 (dev-clone deactivation) resolved post-08-25 — not carried.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-21*

## Self-Check: PASSED

- `08-29-SUMMARY.md` created on disk; `08-DEVICE-UAT.md` modified (D17 minted — 12 references; `RUN 2026-07-21` header + round-5 block present; Round-5 Overall + Gate A both = ISSUES).
- `STATE.md` updated: `stopped_at: Completed 08-29-PLAN.md`, `completed_plans: 57`, round-5 context + blocker entries + P29 metric row + Session Continuity all present.
- `ROADMAP.md`: 08-29 checkbox ticked `[x]` with the RUN outcome; Phase-8 Flags + progress-table row updated to round-5 RUN / Gate A ISSUES / round-6 next.
- No per-task code commits (checkpoint re-verify — planning docs only); final docs commit records the four planning files ONLY (no `git add -A`, per the standing shared-tree rule).
