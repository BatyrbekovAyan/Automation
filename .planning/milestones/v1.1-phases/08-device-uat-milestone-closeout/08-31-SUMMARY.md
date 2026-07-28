---
phase: 08-device-uat-milestone-closeout
plan: 31
subsystem: chat
tags: [whatsapp, reactions, reaction-removal, chatmanager, live-poll, quote-resolve, platform-limit, revert, instrumentation]

# Dependency graph
requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "08-27 candidate-(a) already-seen WhatsApp reaction re-process (the block reverted here) + [D15] shape log (retained)"
  - phase: 08-device-uat-milestone-closeout
    provides: "08-29 round-5 device evidence — candidate (b) confirmed (add raw re-delivers seen=true every poll, no removal raw) + WR-02 resurrection mechanism"
provides:
  - "WR-02 revert — the harmful 08-27 candidate-(a) re-process is removed; an own WhatsApp reaction removed/changed in-app is no longer resurrected/reverted on the next poll (already-seen fall-through resumes at the media/quote/reactions refresh, pre-08-27 behavior)"
  - "Editor-only [D15-probe] on the existing authed messages/id/get call — logs whether a WhatsApp per-message payload carries any reaction-state key (reactionsKey/reactionKey booleans only), zero new request, no token handling"
  - "CLAUDE.md confirmed-platform-limit (D15) note under the message/reaction endpoint — the documented Wappi WhatsApp reaction-removal constraint"
affects: [08-33 round-6 re-verify checkpoint, device-uat, whatsapp-reactions]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Revert-the-harmful-fix on refuted-candidate evidence: when a diagnosis-first candidate (08-27 candidate-a) is proven inert AND live-harmful by the follow-on device round, delete it and keep only the shape log"
    - "Piggyback probe on an existing authed call (no new request, no secret): reuse DrainQuoteResolveQueue's messages/id/get parse to answer a key-presence question, Editor-gated + WhatsApp-gated"
    - "Document a confirmed transport constraint in CLAUDE.md (chat/delete isDeleted style) as the disposition for a platform limit rather than building a client-side workaround"

key-files:
  created: []
  modified:
    - "Assets/Scripts/Main/ChatManager.cs — SyncLatestMessages: deleted the candidate-(a) already-seen WhatsApp reaction re-process (WR-02 revert); [D15] wa-reaction shape log retained"
    - "Assets/Scripts/Main/ChatManager.QuoteResolve.cs — DrainQuoteResolveQueue: Editor-only [D15-probe] reaction-state key-presence log on the existing messages/id/get response"
    - "CLAUDE.md — Wappi.pro (WhatsApp) message/reaction endpoint: appended the Confirmed platform limit (D15) note"

key-decisions:
  - "WR-02 revert is a pure deletion — no replacement logic. Seen WhatsApp reaction raws drop at the seen gate exactly as before 08-27; brand-new reaction raws still ingest via the unchanged unseen branch (676-680)"
  - "D15 disposed as a documented Wappi/WhatsApp platform limit (candidate b confirmed round-5) rather than a client-side absence reconcile. The Editor [D15-probe] rides the 08-33 checkpoint to positively confirm no reaction-state key ever surfaces; only if one does is a round-7 reconcile built"
  - "The 96da6f4 pin (Apply_RemovalThenRedelivery_SecondIsIdempotentNoOp) is LEFT UNCHANGED — it pins ReactionStore.Apply idempotency (a pure reducer property), not the reverted re-process block; valid regardless"

patterns-established:
  - "A refuted diagnosis-first candidate is reverted, not left inert — an inert-but-harmful fix is worse than none"

requirements-completed: []  # closeout phase — no new v1.1 REQ ids (plan frontmatter requirements: [])

# Metrics
duration: 7min
completed: 2026-07-20
---

# Phase 8 Plan 31: Round-6 WR-02 Revert + D15 Probe + Platform-Limit Doc Summary

**Reverted the harmful 08-27 candidate-(a) WhatsApp reaction re-process (WR-02) so an own reaction removed/changed in-app is no longer resurrected by the still-re-delivering add raw, added a secret-free Editor `[D15-probe]` that piggybacks the existing authed `messages/id/get` call to check for any WhatsApp reaction-state key, and documented the WhatsApp reaction-removal platform limit in CLAUDE.md — suite green at 1184/1184 (delta 0), Telegram byte-identical.**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-07-20T21:34:37Z
- **Completed:** 2026-07-20T21:41:03Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments
- **WR-02 revert (Task 1).** Deleted the 08-27 candidate-(a) block in `SyncLatestMessages` (the `// [D15] Candidate-(a) fix ...` comment + the `if (ActiveChannel == ChatChannel.WhatsApp && raw.type == "reaction") { if (HandleReactionEvent(...)) ...; continue; }` re-process). The round-5 `[D15]` evidence proved a WhatsApp add raw re-delivers `seen=true` every poll, so the block resurrected an own reaction removed in-app / reverted a changed one within one poll — inert for its intended purpose (candidate a refuted) and harmful outside it. The already-seen fall-through now resumes directly at the media/quote/reactions refresh (pre-08-27 behavior: seen WA reaction raws drop at the seen gate — no resurrection).
- **D15 probe (Task 2).** Added an `#if UNITY_EDITOR` `[D15-probe]` inside `DrainQuoteResolveQueue`'s existing authed `messages/id/get` parse (`msg` in scope), WhatsApp-gated, logging only key-presence booleans (`reactionsKey`/`reactionKey`) — never content. It reuses the already-authed QuoteResolve call, so zero new network code and no token handling (the `secrets.json` deny rule is honored). It answers "does a WhatsApp per-message payload carry any reaction-state key" for the 08-33 checkpoint.
- **Platform-limit doc (Task 3).** Appended a `**Confirmed platform limit** (D15)` note to the `message/reaction` endpoint bullet in CLAUDE.md's Wappi.pro (WhatsApp) endpoints list, mirroring the `chat/delete` `isDeleted` style — records that an in-app-removed WhatsApp reaction emits no row and WA payloads carry no `reactions[]` state (unlike tapi/Telegram), so there is no signal to clear it; a Wappi/WhatsApp constraint, not an app bug.
- **[D15] shape log retained + 96da6f4 pin unchanged.** The `[D15] wa-reaction` log (both branches) stays until D15 formally closes; the `Apply_RemovalThenRedelivery_SecondIsIdempotentNoOp` reducer-idempotency pin is untouched and green.

## Task Commits

Each task was committed atomically (only the named file staged; the shared live tree's ~40 unrelated entries left alone):

1. **Task 1: Revert 08-27 candidate-(a) WhatsApp reaction re-process (WR-02)** — `d9130a7` (fix) — ChatManager.cs, 13 deletions
2. **Task 2: D15 Editor probe on the existing messages/id/get call** — `4435c63` (feat) — ChatManager.QuoteResolve.cs, 9 insertions
3. **Task 3: Document the WhatsApp reaction-removal platform limit** — `d72b4e0` (docs) — CLAUDE.md, 1 line replaced

**Plan metadata:** final docs commit — this file + STATE.md + ROADMAP.md.

## Files Created/Modified
- `Assets/Scripts/Main/ChatManager.cs` — `SyncLatestMessages`: deleted the candidate-(a) already-seen WhatsApp reaction re-process (−13 lines); `[D15] wa-reaction` shape log retained.
- `Assets/Scripts/Main/ChatManager.QuoteResolve.cs` — `DrainQuoteResolveQueue`: Editor-only `[D15-probe]` reaction-state key-presence log on the existing `messages/id/get` response (+9 lines).
- `CLAUDE.md` — Wappi.pro (WhatsApp) `message/reaction` bullet: appended the `Confirmed platform limit` (D15) note.

## Decisions Made
- **Pure deletion for the revert.** No replacement logic — the correct pre-08-27 behavior is exactly the seen-gate skip. Brand-new WhatsApp reaction raws still ingest via the unchanged unseen branch; the Telegram-only `RefreshCachedMessageReactions` refresh at the fall-through is unchanged.
- **D15 disposed as a documented platform limit, not a client workaround.** Candidate (b) (no removal raw ever) was confirmed round-5; the models predict WA payloads carry no reaction-state key. The Editor `[D15-probe]` positively confirms this at the 08-33 checkpoint; a round-7 absence reconcile is built ONLY if a key surprisingly surfaces.
- **96da6f4 pin stays.** It pins `ReactionStore.Apply` idempotency (a pure reducer property), independent of the reverted caller-side block.

## Deviations from Plan

None — plan executed exactly as written. All three tasks matched their `<action>` and `<acceptance_criteria>` verbatim; every grep gate hit its exact expected count on the first attempt.

## Verification
- **Task 1 greps:** `[D15]` count 1 (log only), WA reaction gate count 1 (log gate only), `Candidate-(a)` count 0, `[D15] wa-reaction` count 1, `Apply_RemovalThenRedelivery_SecondIsIdempotentNoOp` present + unchanged (count 1).
- **Task 2 greps:** `[D15-probe]` count 1 (log string only; comment unbracketed `// D15 probe`), `UNITY_EDITOR` count 1 (Editor-guarded), request count unchanged at 2 (no new `SendWebRequest`/`UnityWebRequest.Get` — the probe reuses the existing call).
- **Task 3 greps:** `Confirmed platform limit** (D15)` count 1, `message/reaction` still present, `D15-probe` referenced (count 1), `git diff --stat CLAUDE.md` a single localized hunk at the Wappi WhatsApp endpoints list.
- **Full EditMode suite:** 1184/1184 green FRESH after Task 1 AND after Task 2 (delta 0 each; a glue deletion and an Editor-only diagnostic — no test change). Freshness confirmed via Assembly-CSharp.dll mtime advancing on each run (runtime-only edits; editor stamp false-stales as documented). Task 3 is documentation only — no compile.
- **Telegram byte-identical:** the revert is WhatsApp-gated (the deleted block only fired on `ActiveChannel == WhatsApp && raw.type == "reaction"`), the probe is WhatsApp-gated + Editor-only, and the doc is prose. No scene mutation, no new network endpoint, no secret, no auth path.

## Threat Model Disposition
- **T-08-31-01 (Tampering — stale add raw resurrecting an in-app-removed reaction):** MITIGATED by the revert — seen WhatsApp reaction raws drop at the seen gate (pre-08-27), so a stale add raw can no longer re-add a removed reaction; brand-new reaction raws still ingest via the unchanged unseen branch.
- **T-08-31-02 (Info Disclosure — `[D15-probe]`/`[D15]` logs):** ACCEPTED — capped to ids + booleans, never emoji/body/reactor content; the probe reuses the existing authed call and never handles the token. Grep-removable at phase close (IN-03).
- **T-08-31-03 (CLAUDE.md doc):** ACCEPTED — documentation only, no code/surface.
- No new threat surface introduced (no new endpoint, secret, auth path, or scene mutation).

## Issues Encountered
None. The in-Editor bridge writes a `status: "running"` summary before the final `completed`; both test runs were gated on `status == "completed"` AND a fresh `finishedAt` AND an advanced Assembly-CSharp.dll mtime, so no stale-green trap.

## User Setup Required
None — no external service configuration required.

## Next Phase Readiness
- The 08-33 round-6 device/Editor re-verify checkpoint carries: (1) confirm an own WhatsApp reaction removed/changed in-app is no longer resurrected on the next poll (WR-02 fixed); (2) read the `[D15-probe]` line on a WhatsApp quote-resolve to confirm `reactionsKey=false reactionKey=false` (no reaction-state key surfaces) — if confirmed empty, D15 closes as the documented platform limit and the `[D15]`/`[D15-probe]` logs schedule for removal (IN-03); if a key surprisingly surfaces, spin round 7 for an absence-based reconcile.
- D2-view CR-01 root fix (08-30) also rides the 08-33 checkpoint; Gate A stays ISSUES until the round-6 device pass.

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-31-SUMMARY.md`
- FOUND: commit `d9130a7` (Task 1 — ChatManager.cs)
- FOUND: commit `4435c63` (Task 2 — ChatManager.QuoteResolve.cs)
- FOUND: commit `d72b4e0` (Task 3 — CLAUDE.md)

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*
