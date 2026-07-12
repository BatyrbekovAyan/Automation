---
phase: 02-n8n-live-wiring
fixed_at: 2026-07-11T13:08:51Z
review_path: .planning/phases/02-n8n-live-wiring/02-REVIEW.md
iteration: 1
findings_in_scope: 4
fixed: 4
skipped: 0
status: all_fixed
---

# Phase 02: Code Review Fix Report

**Fixed at:** 2026-07-11T13:08:51Z
**Source review:** .planning/phases/02-n8n-live-wiring/02-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 4 (WR-01..WR-04; scope = critical_warning, 0 Critical / 4 Warning; 6 Info findings out of scope)
- Fixed: 4
- Skipped: 0

## Fixed Issues

### WR-01: Chat-switch race — provider defeats `TryGetRecentMessages`' designed chat-mismatch guard

**Files modified:** `Assets/Scripts/Chat/N8nSuggestionsProvider.cs`
**Commit:** 155b9fe
**Applied fix:** `Run` now calls `cm.TryGetRecentMessages(req.chatId, ...)` (the chat the request was issued for) instead of `cm.CurrentChatId` (the chat open after the drain), so the accessor's `chatId != currentChatId` guard fires on a chat switch and short-circuits to Empty — no mixed-context paid LLM call. Comment updated to document the scoping. Verified: `SuggestionsController` stamps `req.chatId` from `CurrentChatId` at request-issue time (guarded non-empty), so the happy path is unchanged.

### WR-02: `MapResponse` has no upper cap — >4 server items render >4 cards

**Files modified:** `Assets/Scripts/Chat/N8nSuggestionsProvider.cs`, `Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs`
**Commit:** b4631cf
**Applied fix:** Added `.Take(4)` after the validity filter in `MapResponse` (wire contract's upper bound enforced at the trust boundary) and updated the XML doc. Added `FiveItems_CappedAtFour_Ok` regression test (5 valid items → Ok with exactly 4, first four kept in order, requestSeq from the request).

### WR-03: Workflow pays 1–2 LLM calls for known-invalid requests — no short-circuit on `invalid`

**Files modified:** `Tools/n8n/build-suggest-replies.py`, `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`, `Tools/n8n/README.md`
**Commit:** a2ce17d
**Applied fix:** Added an `If invalid?` node (id `...0308`, condition `={{ $json.invalid }}`) right after `Prep` in `build_full()`: TRUE → `Build Response` (existing `j.invalid || !j.ok` check emits `generation_failed` with echoed requestSeq — no code change), FALSE → `If skipRag?`. Redeployed to the live dev instance (`--stage full --update 9PTyYcelRQI7bGDb`, re-activated) and re-exported the canonical JSON — `cmp` proves the committed file is byte-for-byte identical to the live instance. README graph descriptions updated. Prod bagkz untouched (fix carries into the pending replication via the committed script/JSON).

### WR-04: `resolve_cred` resolves by credential *type*, not *name* as documented — wrong-credential risk on prod

**Files modified:** `Tools/n8n/build-suggest-replies.py`, `Tools/n8n/README.md`
**Commit:** 24d462a
**Applied fix:** `resolve_cred` now matches `WHERE type=? AND name=?` (exact name from `FALLBACK_CREDS`). If the DB is readable but the name is absent, it raises `SystemExit` listing the candidate credentials of that type (id + name) instead of silently binding the alphabetically-first one — stricter than the review's silent type-fallback suggestion, per workflow instruction. The pinned-id fallback survives only for a missing/unreadable DB. README credential sentence updated to match.

## Verification

- **Unity (WR-01 + WR-02):** headless EditMode run `Tools/run-tests-headless.sh 'SuggestReplies'` on the post-fix code — **GREEN, 27/27 passed** (0 failed / 0 inconclusive), including the new `FiveItems_CappedAtFour_Ok` (confirmed present in `results.xml`). Phase-1 zero-edit invariant held: no Phase-1 UI/seam file touched (`git diff` scope = provider + map tests only).
- **n8n (WR-03), verified against the live dev instance:**
  - Malformed request (`{"v":1,"requestSeq":42,"chatId":"…","messages":[]}`) → `{"v":1,"requestSeq":42,"suggestions":[],"error":"generation_failed"}` in 0.2 s; execution 517 runData = `Webhook, Prep, If invalid?, Build Response, Respond` — **zero LLM nodes ran** (previously 1–2 gpt-4o-mini calls).
  - Valid request (skipRag path, flowers catalog) → exactly 4 suggestions, all labels distinct and inside the closed 6-label enum, catalog-grounded prices echoed, `requestSeq:777` echoed.
  - Canonical JSON re-export `cmp`-identical to the live workflow (byte-for-byte).
- **n8n (WR-04):** exact-name resolution returns the known dev ids (`WNHwKWlO2E9OClkA`/`vrywn6AxQMlvbbzC`); doctored name → `SystemExit` listing candidates; nonexistent DB → pinned fallback. Full `--update` redeploy through the new `resolve_cred` produced a byte-identical workflow (idempotence proven).
- Dev n8n server was not running before this session; it was started for the WR-03/WR-04 deploy + verification and stopped afterwards (healthz 000, as found). Workflow left `active: true`, exactly as found.

---

_Fixed: 2026-07-11T13:08:51Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
