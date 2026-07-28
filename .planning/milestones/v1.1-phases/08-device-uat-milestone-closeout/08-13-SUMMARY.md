---
phase: 08-device-uat-milestone-closeout
plan: 13
subsystem: api
tags: [n8n, suggest-replies, rag, prompt, telegram, whatsapp, D10, gap-closure, diagnosis-first]

# Dependency graph
requires:
  - phase: 07-suggestions-dashboard
    provides: "N8nSuggestionsProvider.BuildPayloadJson (single channel-resolved builder); channel-branched RAG (botWaId | botTgId) in Suggest_Replies"
  - phase: 08-device-uat-milestone-closeout
    provides: "08-04 open-chat live poll (cross-channel fresh _activeChatCache → fresh suggestions transcript on both channels)"
provides:
  - "Suggest_Replies Assemble prompt anchored on the NEWEST incoming: all 4 cards must answer the last client message; RAG/catalog are facts-only, not a topic switch"
  - "Explicit fenced.lastClientMessage (= p.queryText) so the anchor is concrete when the transcript is long or RAG retrieval is noisy"
  - "Written diagnosis: client + workflow branches are channel-symmetric; the sole WA-vs-TG runtime asymmetry is RAG retrieval (WA runs a live botWaId query; TG empty on dev)"
affects: [08-16 device re-verify (D10 WA relevance), prod bagkz Suggest_Replies replication]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Prompt-level relevance anchor: name the newest incoming explicitly (directive + fenced field) so retrieval noise can't drown it — shared across channels, TG-node bytes untouched"

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-13-SUMMARY.md
  modified:
    - Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json

key-decisions:
  - "Diagnosis-first: refuted all four pre-flagged workflow hypotheses (a/b/c/d) — the verifier already guarantees them green; the canonical WA branch is structurally correct and symmetric to TG"
  - "The genuine WA-vs-TG asymmetry is RAG retrieval at runtime (WA executes a live botWaId query, TG returns 0 rows on dev), which can bury the newest incoming → 'irrelevant on WhatsApp'"
  - "Fix in the SHARED Assemble prompt (a PROMPT fix, explicitly in the plan's connection/filter/prompt space) — equivalent on both channels; NO client change (client is symmetric)"
  - "Telegram byte-identical: git diff touches ONLY the Assemble jsCode line (line 237); zero TG-node bytes (botTgId / Retrieve RAG TG / If channel TG) added or removed"

patterns-established:
  - "When diagnosis shows structural symmetry, fix the fragility (weak anchor) not a non-existent asymmetry — and prove byte-identity of the protected branch by isolating the diff"

requirements-completed: []

# Metrics
duration: 13min
completed: 2026-07-17
---

# Phase 08 Plan 13: D10 WhatsApp «Вместе» Relevance Summary

**Anchored the shared Suggest_Replies `Assemble` prompt on the newest incoming client message (directive + explicit `fenced.lastClientMessage`), so WhatsApp suggestions track the last message even when the WA-branch RAG retrieval injects noise — Telegram byte-identical, parity verifier green, no client change.**

## Performance

- **Duration:** ~13 min
- **Started:** 2026-07-17T07:32:15Z
- **Completed:** 2026-07-17T07:45:52Z
- **Tasks:** 2 (Task 1 diagnosis-only; Task 2 workflow fix)
- **Files modified:** 1 source (`Suggest_Replies.json`, 1 line) + this SUMMARY

## Diagnosis (Task 1) — the deliverable

**Symptom (D10, device 2026-07-17):** «Вместе» suggestions are RELEVANT on Telegram but IRRELEVANT on WhatsApp. Live refresh + draft protection PASS on both channels; D5 (live-incoming render) is RESOLVED on both — so the transcript reaching the payload is fresh on both channels.

### 1. Is the CLIENT payload channel-symmetric? — YES (confirmed)

- `N8nSuggestionsProvider.BuildPayloadJson` is ONE builder for both channels (`N8nSuggestionsProvider.cs:148`). Only `profileId` (channel-resolved), the `channel` string (enum-derived lowercase), and *which of* `botWaId`/`botTgId` is the real id differ. The relevance drivers — `messages`, `lastIncomingText`/`steerTowardText`, `catalog`, `businessTypeId`, `ownerPrompt` — are built by **identical** logic on both channels.
- The transcript comes from `ChatManager.TryGetRecentMessages` (`ChatManager.RecentMessages.cs:17`), which is **channel-agnostic**: it reads the private `_activeChatCache` scoped to `currentChatId` with **no `ChatChannel` branch**. The 08-04 open-chat live poll (cross-channel) keeps that cache fresh on both channels.
- `SuggestionsController.HandleLive` (`SuggestionsController.cs:166`) sets `lastIncomingText = LastIncomingText(msgs)` with **no channel branch** — the newest incoming populates `queryText` identically on WhatsApp and Telegram.
- **Conclusion:** No WA-only client gap (the plan's "`lastIncomingText` not populated on the WA trigger" hypothesis is refuted). The client is symmetric.

### 2–4. Is the WORKFLOW WA branch dropped / wrong-id / mis-ordered / TG-only-prompted? — NO (all four refuted)

Traced the full `connections` graph of `9PTyYcelRQI7bGDb-Suggest_Replies.json`:

```
Prep → If invalid?[false] → If skipRag?[false] → If channel TG?
        If channel TG?[true]  → Retrieve RAG TG (filter botTgId) → Assemble
        If channel TG?[false] → Retrieve RAG    (filter botWaId) → Assemble
        If skipRag?[true] ─────────────────────────────────────→ Assemble
Assemble → LLM → Validate → If ok? → (Build Response | LLM Retry → Validate 2 → Build Response) → Respond
```

- **(a) dropped WA RAG connection — REFUTED.** `connections["Retrieve RAG"]["main"][0][0].node == "Assemble"` (workflow lines 470–480). WA RAG output DOES reach `Assemble`.
- **(b) wrong/empty id filter — REFUTED.** `Retrieve RAG` metadata filter is the single key `botWaId = {{ $json.botWaId }}` (lines 116–124); the client always sends `botWaId = whatsappWorkflowId`, and the WhatsApp Bot template stamps chunks `botWaId = {{ $workflow.id }}` (README). Keys match.
- **(c) branch ordering — REFUTED.** `If channel TG?` routes `[true]→Retrieve RAG TG`, `[false]→Retrieve RAG`; both single-hop into `Assemble`. The `If skipRag?`[false] correctly feeds `If channel TG?`.
- **(d) Assemble/LLM uses queryText/messages TG-only — REFUTED.** `Assemble` reads `const p = $('Prep').first().json` and builds `fenced.messages = p.messages` **path-independently** (from Prep, not from the RAG item). The system prompt is channel-neutral («из этого чата», no «со своего WhatsApp»). Both paths feed the LLM the same transcript + catalog + queryText-derived context.

`verify-telegram-parity.py` **already asserts** (a)/(b)/(c)/(d): both retrieve nodes → `Assemble`, single-key `botWaId`/`botTgId` filters, `Embeddings` ai_embedding into both, the `If channel TG?` routing, and no WhatsApp-specific copy in `Assemble`. It passes at baseline → the four hypotheses are structurally impossible.

`grep -c "Retrieve RAG" ...` = **8** (≥ 2), confirming the workflow was read and the `Retrieve RAG` / `Retrieve RAG TG` connections were traced.

### Actual locus (contradicts all four hypotheses, per the plan's escape clause)

The client is symmetric, the workflow is structurally symmetric, and the shared `Assemble` feeds the LLM the same fresh transcript on both paths. The **only** WhatsApp-vs-Telegram difference that survives at runtime is **RAG retrieval**:

- **WhatsApp:** for a WA-authed bot `botWaId` is a real id → `skipRag == false` → `Retrieve RAG` runs a **live Supabase vector query**. On dev the `documents` table holds WhatsApp chunks (leftovers from the price-list upload e2e, stamped with a matching `botWaId`), so `Assemble` builds a non-empty `ragChunks` (up to 4000 chars).
- **Telegram:** no TG chunks are seeded on dev → `Retrieve RAG TG` returns 0 rows → `ragChunks == ''` → the TG prompt is clean → cards track the newest incoming → **relevant**.

The newest incoming is only **implicitly** the last of up-to-12 transcript messages inside `fenced.messages`; there is no explicit "answer THIS message" anchor. When WA's `ragChunks` is large/off-topic, it derails the LLM off the last incoming → **irrelevant on WhatsApp**. This matches the owner's exact words ("suggested messages are not relevant to last incoming message"), and it is a *relevance* defect (D10), distinct from RAG-grounding-with-data (H2, still deferred to prod RAG seeding).

**Fix specified:** make the newest-incoming anchor explicit in the shared `Assemble` prompt (directive + `fenced.lastClientMessage = p.queryText`) so the 4 cards reliably answer the last incoming regardless of retrieval noise — the plan's sanctioned "prompt" fix locus, applied equivalently to both channels, with the TG-specific nodes untouched.

## Accomplishments

- Closed **D10** at the prompt level: a `РЕЛЕВАНТНОСТЬ (ГЛАВНОЕ)` directive makes all 4 cards answer the LAST incoming client message; catalog/RAG are facts-only, never a topic switch.
- Surfaced the newest-incoming query explicitly as `fenced.lastClientMessage` (= `p.queryText`), kept inside the injection-fenced ДАННЫЕ block (it is client data).
- Proved Telegram byte-identity: `git diff` touches **only** line 237 (the shared `Assemble` jsCode); zero `botTgId` / `Retrieve RAG TG` / `If channel TG` bytes changed.
- `verify-telegram-parity.py` green; JSON valid; `node --check` on the extracted `Assemble` JS = exit 0. No client change (client confirmed symmetric).

## Task Commits

1. **Task 1 (D10): diagnose — client symmetry + WA-branch locus** — no source change (diagnosis lands in this SUMMARY); baseline verifier captured green (exit 0).
2. **Task 2 (D10): anchor Assemble prompt on the newest incoming** — `fa2ac8c` (fix)

**Plan metadata:** _(final docs commit — SUMMARY/STATE/ROADMAP)_

## Files Created/Modified

- `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` (modified, 1 line) — `Assemble` jsCode: added the `РЕЛЕВАНТНОСТЬ (ГЛАВНОЕ)` system-prompt directive + `fenced.lastClientMessage: p.queryText || ''`. `Retrieve RAG TG`, the `botTgId` filter, the `If channel TG?` routing, and all connections are byte-identical.

## Decisions Made

- **Diagnosis-first, honest outcome.** Rather than force-fit one of the four pre-flagged structural hypotheses (all of which the verifier already guarantees green), the diagnosis states the actual finding: symmetry everywhere except RAG retrieval, and a weak newest-incoming anchor.
- **Prompt fix in the shared node, not a WA-only branch.** Keeping `Assemble` shared preserves the must-have "prompts the LLM equivalently to the Telegram branch"; the anchor is additive and reinforces TG's already-correct behavior (no regression). A WA-only branch would have broken that equivalence for no benefit.
- **No client change.** The client is symmetric; per Task-2 acceptance, `N8nSuggestionsProvider.cs` and `SuggestRepliesPayloadTests.cs` stay unchanged and the EditMode suite (baseline 1111) is untouched (no `.cs` edited).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Corrected the parity-verifier invocation**
- **Found during:** Task 1 (baseline verification)
- **Issue:** The plan's stated command `python3 Tools/n8n/verify-telegram-parity.py Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` fails with `unrecognized arguments` (exit 2) — the script takes `--dir DIR` (or no arg), not a positional file. It verifies the committed `workflows/` directory, which INCLUDES this file.
- **Fix:** Ran the correct no-arg form `python3 Tools/n8n/verify-telegram-parity.py` (verifies all 4 canonical workflows including Suggest_Replies). Green at baseline and after the edit.
- **Files modified:** None (invocation only).
- **Verification:** `ALL PARITY ASSERTS PASSED`, exit 0, both before and after the edit.
- **Committed in:** n/a (no file change).

---

**Total deviations:** 1 (Rule 3 — verifier invocation form; no code impact).
**Impact on plan:** None on scope. The gate is satisfied by the correct invocation; the fix and TG byte-identity are proven.

## Threat Flags

None — no new network endpoint, auth path, file-access pattern, or schema change. T-08-13-01 (regress TG) mitigated: diff isolated to the shared `Assemble` line, zero TG-node bytes changed, verifier green. T-08-13-02 (cross-bot RAG leak) untouched: the single-key `botWaId` filter is byte-identical (not broadened). T-08-13-03 (live/prod call) respected: canonical-file edit + offline verifier only; no dev/prod n8n or secrets.json calls.

## Known Stubs

None — the change is a real prompt-behavior fix wired end-to-end through the existing pipeline.

## Issues Encountered

None beyond the verifier-invocation correction above. The dev-n8n deploy and the live WA relevance re-test are out of this plan's scope by design (owner-gated, ride 08-16).

## Live re-test ask for 08-16 (owner-gated)

1. Deploy the edited canonical `9PTyYcelRQI7bGDb-Suggest_Replies.json` to dev n8n (localhost:5678) — import by literal id, keep activation state exactly as found; do NOT touch prod bagkz.
2. On a WhatsApp-authed bot with an open chat, toggle «Вместе» and send a fresh incoming → confirm all 4 cards now **track the newest incoming** (relevance parity with Telegram), even if the WA bot has RAG chunks on dev.
3. Re-test **RAG grounding** (H2) together only once dev `documents` are seeded for the tested bot — grounding-with-data remains deferred to prod replication; D10 here is the *relevance* half.

## Next Phase Readiness

- D10 fix is canonical-committed and verifier-green; ready for the 08-16 dev deploy + device re-verify.
- Milestone closeout (08-14/15/16) unblocked from the D10 side; RAG-grounding-with-data + prod bagkz replication remain the tracked pre-prod items.

## Self-Check: PASSED

- Created files verified on disk: `08-13-SUMMARY.md`, `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`.
- Task commit verified in git log: `fa2ac8c` (fix — Assemble prompt anchor); `lastClientMessage` present in the committed file.
- Parity verifier green (exit 0) after the edit; `git diff` isolated to the shared `Assemble` line (zero TG-node bytes changed).

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*
