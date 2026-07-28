---
phase: 10-message-batching-debounce
verified: 2026-07-22T17:10:00Z
status: passed
score: 5/5 must-haves verified
overrides_applied: 2
overrides:
  - must_have: "In a «Вместе» chat, rapid incoming fragments refresh the suggestion cards ONCE (coalesced), while manual refresh and card-pick still respond immediately — confirmed on-device"
    reason: "On-device behavioral observation is BLOCKED by the still-open Phase-9 09-04 SetReplyMode deploy (in-app Semi-auto toggle 404s at Manager.ReplyModeSync.cs:105 — an expected consequence of an undeployed dev webhook, not a Phase-10 defect). BATCH-03's client coalesce logic is fully proven in EditMode (IncomingDebounceGate: 6 dedicated tests incl. 3-rapid->1-fire and the burst-then-chat-switch regression; full suite 1197/1197) and the SuggestionsController wiring (4/4 lifecycle cancel sites, manual/card-pick untouched) is code-verified. Already tracked as a uat_gap debt row in STATE.md, re-verifies trivially alongside 09-04/09-05."
    accepted_by: "owner"
    accepted_at: "2026-07-22T00:00:00Z"
  - must_have: "A semi-auto chat still skips the whole reply path (Phase-9 gate before the debounce — no wait, no reply) — confirmed on-device"
    reason: "Deferred to post-Phase-9 by explicit owner decision (owner asked to continue/close the phase now without this scenario). The structural/code-level guarantee is already verified: the debounce splice sits on Suppressed?'s main[1] (not-suppressed) branch in both templates, main[0] (suppressed=TRUE) is a dead-end with zero downstream nodes, asserted by verify-message-batching.py (which passes) and confirmed by direct JSON inspection this session. Only the on-device behavioral confirmation is outstanding. Already tracked as a uat_gap debt row in STATE.md, re-verifies alongside 09-04/09-05."
    accepted_by: "owner"
    accepted_at: "2026-07-22T00:00:00Z"
---

# Phase 10: Message Batching / Debounce Verification Report

**Phase Goal:** A pre-generation debounce+dedupe stage in both bot templates so a multi-fragment customer message gets ONE combined reply, plus a client-side debounce in `SuggestionsController.HandleLive` so «Вместе» suggestions coalesce the same way — composed after the Phase-9 suppression gate.
**Verified:** 2026-07-22T17:10:00Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Two+ text fragments within the debounce window produce exactly ONE bot reply grounded in the concatenation; aborted fragments never generate; proven on BOTH channels via runData | ✓ VERIFIED | 10-03 runData scenario A: TG exec 847 aborted / 848 winner (`abort:true`→`false`), WA exec 851 aborted / 852 winner; 10-04 UAT scenarios 1 & 3 PASS on-device (both channels, one combined reply observed) |
| 2 | Single complete message still gets one reply after the window; a bot/owner reply between fragments bounds the combined run; a new chat with no prior bot reply combines within the fetch limit | ✓ VERIFIED | 10-03 runData scenario B (single message, one reply, clean 1-line combinedText) + scenario C (fromMe boundary stops the combine walk, `combinedText` == only the trailing fragment) on both channels; 10-04 UAT scenario 2 PASS. The "no prior reply" case is covered by the same combine-walk mechanism (walks to the last `fromMe` or the fetch limit ~15, whichever comes first) — incidentally exercised live when scenario A's first attempt had no prior reply in history and the combine correctly spanned the whole un-replied run (10-03 SUMMARY "Analysis note") |
| 3 | The debounce sits AFTER the Phase-9 suppression gate (a semi-auto chat skips the whole path — no wait); the humanizer pauses are unchanged | ✓ VERIFIED | Structural: `Suppressed?` main[0] (suppressed=TRUE) is `[]` (dead-end, never reaches `Debounce Wait`) in both committed templates, asserted by `verify-message-batching.py` (passes) and confirmed by direct JSON read this session. Humanizer pause nodes untouched (10-01 acceptance criteria: diff touches ONLY the 4 new nodes + rewired connections + Text value) + 10-04 UAT scenario 2 confirms pauses "feel natural." On-device confirmation of the semi-auto-skip behavior itself is **PASSED (override)** — see frontmatter |
| 4 | Suggestions coalesce: rapid incoming fragments issue exactly ONE live request after the ~2.5s client window; manual refresh and card-pick re-cluster still fire immediately | ✓ VERIFIED | `IncomingDebounceGate` (pure, stateful, injectable clock) + `SuggestionsController` wiring code-verified this session: `HandleLive` pokes the gate (no longer calls `IssueRequest` directly), `DebounceLoop` fires once via `ShouldFire`, `HandleManualRefresh`/`HandleCardTapped` unchanged (call `IssueRequest` directly). 6 EditMode tests (3-rapid→1-fire, cancel-mid-window, burst-then-chat-switch BLOCKER regression, re-arm-after-fire) per 10-02-SUMMARY, full suite 1197/1197. On-device confirmation is **PASSED (override)** — see frontmatter |
| 5 | EditMode debounce-gate tests green (rapid incomings → one request; manual refresh immediate); n8n curl/runData matrix green (two fragments → one reply; single message → one reply; bot-reply boundary) | ✓ VERIFIED | EditMode 1197/1197 (10-02-SUMMARY, fresh recompile); runData matrix A–E all recorded PASS on both channels including id-equality True on all 6 winners (10-03-SUMMARY) |

**Score:** 5/5 truths verified (2 include an accepted override for the on-device behavioral half — see frontmatter `overrides`)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Tools/n8n/apply-message-batching.py` | Idempotent by-node-name splice of the 4 debounce nodes into both templates | ✓ VERIFIED | Exists, `python3 -m py_compile` clean, re-run this session produced no functional diff (idempotent) |
| `Tools/n8n/verify-message-batching.py` | Structural verifier gating the fail-safe invariants | ✓ VERIFIED | Ran this session: exits 0, prints "ALL BATCHING ASSERTS PASSED" incl. cross-template identity |
| `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` | WhatsApp template with debounce stage on api/sync | ✓ VERIFIED | Contains all 4 nodes; `Suppressed?` main[1]→`Debounce Wait`; `Is Latest?` main[0]==`[]`, main[1]→`Input type`; `Fetch Recent` url = `https://wappi.pro/api/sync/messages/get`, no `mark_all` param |
| `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` | Telegram template with debounce stage on tapi/sync | ✓ VERIFIED | Same shape, url = `https://wappi.pro/tapi/sync/messages/get`, no `mark_all` |
| `Assets/Scripts/Chat/IncomingDebounceGate.cs` | Pure stateful debounce gate (Poke/Cancel/ShouldFire, injectable clock) | ✓ VERIFIED | Exists + `.meta`; no namespace, no `using UnityEngine`; `WindowSeconds = 2.5f`; `Poke`/`Cancel`/`ShouldFire` all present and match spec verbatim |
| `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs` | 6 EditMode cases incl. chat-switch-cancel regression | ✓ VERIFIED | Exists + `.meta`; all 6 behaviors present including `BurstThenChatSwitch_CancelsPending_ThenReArmsForNewChat` |
| `Assets/Scripts/Chat/SuggestionsController.cs` | HandleLive drives the gate; 4-site cancel; manual/card immediate | ✓ VERIFIED | `_debounce.Poke(Time.time)` in `HandleLive`; `DebounceLoop` fires `IssueRequest` via `ShouldFire`; 4× `_debounce.Cancel()` + 4× `_pendingIncomingText = null` (OnDisable, ResetForNoOpenChat, RestoreForActiveChat top, HandleToggle OFF); `HandleManualRefresh`/`HandleCardTapped` call `IssueRequest` directly, untouched |
| `.planning/phases/10-message-batching-debounce/10-HUMAN-UAT.md` | Owner e2e runbook + recorded verdicts | ✓ VERIFIED | Exists; all 5 scenarios present with PASS/BLOCKED/DEFERRED verdicts, pre-flight/post-run blocks, verdict table, final disposition |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `Suppressed?` main[1] (not-suppressed) | `Debounce Wait` | rewired connection | ✓ WIRED | Confirmed in both committed JSON files this session |
| `Latest+Combine` | `Is Latest?` → `Input type` | re-emitted body + main[1] | ✓ WIRED | `Latest+Combine` jsCode returns `{ ...wh, abort, combinedText }`; `Is Latest?` main[0]==`[]` (dead-end), main[1]→`Input type` |
| `Fetch Recent` | `messages/get` | httpRequest GET, cred `EuhhqAaV56DpoqAN` | ✓ WIRED | Confirmed URL + no `mark_all`; live-proven via 10-03 runData (real message ids returned) |
| `SuggestionsController.HandleLive` | `IncomingDebounceGate.Poke` | resets the window per incoming | ✓ WIRED | `_debounce.Poke(Time.time)` present; direct `IssueRequest` call removed from `HandleLive` |
| `DebounceLoop` | `IssueRequest` | `ShouldFire(Time.time)` true once | ✓ WIRED | Confirmed in `SuggestionsController.cs` |
| `OnDisable`/`ResetForNoOpenChat`/`RestoreForActiveChat`/`HandleToggle` OFF | `IncomingDebounceGate.Cancel` | cancel + clear `_pendingIncomingText` | ✓ WIRED | All 4 sites present, each paired with `_pendingIncomingText = null` |
| dev n8n bot clone | one combined reply | Debounce Wait→Fetch Recent→Latest+Combine (abort earlier fragments) | ✓ WIRED (live-proven) | 10-03 runData: id-equality True on 6/6 winners across both channels |
| Create orchestrator clone | debounce nodes | Get Sample Workflow → Create Workflow (verbatim clone) | ✓ WIRED (live-proven) | 10-03 scenario E: two fresh clones (`fKCMIGXJSbLRimdR`, `pOMkkP8MYS8WhiNY`) both carry all 4 debounce nodes post `binaryMode`-strip fix |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|---------------------|--------|
| `Latest+Combine` Code node | `combinedText` / `abort` | live `Fetch Recent` → `messages/get` HTTP response | Yes — 10-03 runData shows real message ids/text on every winner/aborted execution across 6 recorded executions | ✓ FLOWING |
| `IncomingDebounceGate` via `SuggestionsController` | `_pendingIncomingText` | `HandleLive`'s `LastIncomingText(msgs)` from live `OnLiveMessagesReceived` | Yes — sourced from the real incoming message stream, not a static/mock value (mock provider was swapped for `N8nSuggestionsProvider` at the Phase-2 single-line seam) | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Structural verifier greens the committed splice | `python3 Tools/n8n/verify-message-batching.py` | `ALL BATCHING ASSERTS PASSED` (both templates + cross-template identity) | ✓ PASS |
| Migration is idempotent (second run = no functional diff) | `python3 Tools/n8n/apply-message-batching.py` (re-run) | No new git diff; splice already present | ✓ PASS |
| Splice topology matches spec | `json.load` + connections/url/mark_all inspection on both templates | All assertions held (nodes present, correct urls, no `mark_all`, correct main[0]/main[1] wiring) | ✓ PASS |
| Live device/n8n e2e (multi-fragment→1 reply, suggestions coalesce) | owner-run (cannot be scripted — requires dev n8n, tunnel, real WA/TG profiles, device build) | 3/5 scenarios PASS, 2/5 tracked debt (override) | ? SKIP (owner-run, already executed — see 10-HUMAN-UAT.md) |

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|-----------------|-------------|--------|----------|
| BATCH-01 | 10-01, 10-03, 10-04 | Multi-fragment message → one combined auto-reply (server-side debounce+combine, both templates) | ✓ SATISFIED | Structural splice (10-01) + runData proof (10-03) + on-device confirmation (10-04 scenarios 1/3 PASS) |
| BATCH-02 | 10-01, 10-03, 10-04 | One `messages/get` fetch drives both is-latest dedupe and combine; channel-agnostic; id-equality holds | ✓ SATISFIED | Code node re-emit + sort verified (10-01); id-equality True on 6/6 runData winners (10-03) |
| BATCH-03 | 10-02, 10-04 | Client-side debounce coalesces rapid «Вместе» incomings into one suggestions request; manual/card stay immediate | ✓ SATISFIED (automated) | EditMode 1197/1197 + code-verified wiring; on-device confirmation carried as tracked override/debt (09-04-blocked) |

**Note (per verification scope):** BATCH-01/02/03 are **deliberately not yet formalized** in `.planning/REQUIREMENTS.md` — confirmed by direct grep (no matches), consistent with the ROADMAP.md Phase 10 note: "to be formalized in the v1.2 REQUIREMENTS.md; definitions locked in `10-CONTEXT.md`." This is expected, known deferred formalization — **not treated as a gap**.

### Anti-Patterns Found

All from the committed `10-REVIEW.md` (2026-07-22, standard depth, 13 files reviewed) — reproduced here for completeness, none blocking:

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| `apply-message-batching.py` (Latest+Combine jsCode) | Empty `combinedText` (`""`, not `null`) when the newest fetched message is outgoing — defeats the `??` fallback, LLM gets an empty prompt in a realistic humanizer-overlap interleaving | ⚠️ Warning (WR-01) | Advisory — not reproduced as a runData failure in the recorded matrix; recommended fix documented in 10-REVIEW.md |
| `apply-message-batching.py` (abort/combine logic) | Mixed-type bursts (voice/text/image) silently drop the earlier fragment — combine only merges text runs | ⚠️ Warning (WR-02) | Advisory — accepted v1 scope per review; recommend recording in 10-CONTEXT.md |
| `Fetch Recent` node (both templates) | No `retryOnFail` on the new hot-path HTTP fetch; concurrent same-endpoint response crossing (project-confirmed Wappi behavior) could double-abort a burst | ⚠️ Warning (WR-03) | Advisory — not observed in the 10-03 runData window; recommended hardening documented |
| `Latest+Combine` Code node | Re-emitted item omits explicit `pairedItem`, relies on n8n implicit auto-pairing across Wait→HTTP→Code | ⚠️ Warning (WR-04) | Advisory — 10-03 runData confirmed downstream nodes (`Mark Read`, `Chat Memory`) resolved correctly on every recorded execution; zero-cost hardening recommended for future n8n version changes |
| Various | 7 Info-level notes (clock mismatch Time.time vs realtime in DebounceLoop, a test literal coupled to the tunable window, missing webhookId on Debounce Wait, pre-wait suppression-flag staleness, Telegram-only unmarked-read aborted fragments, dead `type_suffix` param, pre-existing unused `_mockLatencySeconds` field) | ℹ️ Info | None — advisory only, documented in full in `10-REVIEW.md` |

**Disposition:** 0 critical, 4 warnings (all advisory, none reproduced as functional failures in the live runData/UAT evidence gathered this phase), 7 info. Consistent with the pre-adjudicated review status supplied for this verification.

### Human Verification Required

None outstanding as fresh items. The two on-device behavioral confirmations that could not be completed this phase (suggestions-coalesce observation; semi-auto-skips-path observation) are **already recorded as owner-adjudicated tracked debt** in both `10-HUMAN-UAT.md` and `.planning/STATE.md` (`uat_gap` rows), with an explicit owner continue-decision dated 2026-07-22. They are captured as `overrides` in this report's frontmatter rather than re-surfaced as new human-verification asks — re-verification is scheduled to happen alongside Phase 9's 09-04/09-05 closure, not as a standalone ask against this phase.

### Gaps Summary

No unresolved gaps. All ROADMAP Phase 10 Success Criteria and all four plans' `must_haves` have either (a) full automated + live-runData + on-device confirmation, or (b) full automated + live-runData confirmation with the on-device behavioral half carried as explicit, owner-adjudicated, already-tracked debt (accepted via override in this report, cross-referenced to `STATE.md` `uat_gap` rows and `10-HUMAN-UAT.md`'s final disposition). The code review's 4 warnings are advisory hot-path hardening recommendations, not functional failures observed in the live evidence gathered — none block phase closure.

---

*Verified: 2026-07-22T17:10:00Z*
*Verifier: Claude (gsd-verifier)*
