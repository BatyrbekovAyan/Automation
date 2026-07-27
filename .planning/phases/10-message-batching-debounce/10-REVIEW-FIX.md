---
phase: 10-message-batching-debounce
fixed_at: 2026-07-27T00:00:00Z
review_path: .planning/phases/10-message-batching-debounce/10-REVIEW.md
iteration: 1
findings_in_scope: 4
fixed: 3
skipped: 1
status: partial
---

# Phase 10: Code Review Fix Report

**Fixed at:** 2026-07-27
**Source review:** `.planning/phases/10-message-batching-debounce/10-REVIEW.md`
**Iteration:** 1
**Scope:** the 4 Warnings only (0 Critical). The 7 Info findings were out of scope this run (no `--all`) and were NOT touched.

**Summary:**
- Findings in scope: 4
- Fixed: 3 (WR-01, WR-03, WR-04)
- Skipped: 1 (WR-02 — accepted v1 scope, recorded in `10-CONTEXT.md`)

> ## ⚠️ OWNER ACTION REQUIRED: REDEPLOY BOTH BOT TEMPLATES
> The two canonical templates changed. **Nothing was deployed** — no live n8n/Wappi API was
> called, no secrets were read. The live dev copies of `4wYitz5ek30SVNlT` (WhatsApp Bot) and
> `4VN3gsFaC2HUYmcc` (Telegram Bot) still run the pre-fix code. Until the owner redeploys, the
> committed JSON and the live instance are OUT OF SYNC.
>
> **Redeploy:** import/patch both templates from `Tools/n8n/workflows/`, then re-run
> `python3 Tools/n8n/verify-message-batching.py --dir <re-export dir>` as the post-import go/no-go.
> Remember the canonical exports deliberately carry PROD (`bagkz.app.n8n.cloud`) URLs while the live
> dev copies use `localhost` — that asymmetry is by design, do not "fix" it during the import.
>
> **Re-check after redeploy** (all three are reply-path behaviors the runData matrix / device UAT
> already covered pre-fix, so they are re-runs of passed checks, not new work):
> 1. **Burst still coalesces to ONE reply** — two text fragments ~1s apart: first execution aborts at
>    `Is Latest?`, second combines. The combine string must be unchanged from the 10-03 runData.
> 2. **`pairedItem`** — on a debounced execution, confirm `Mark Read`, `Typing`, `Chat Memory`
>    (sessionKey) and the send node all still resolve their `$('Webhook').item` expressions. This is
>    the one change that touches every downstream node, so it is the highest-value re-check.
> 3. **Empty-run fallback (WR-01)** — a fragment whose window contains a bot reply newest must now
>    reply to the single triggering message instead of receiving an empty prompt.
> 4. **Observability** — `Latest+Combine`'s output now carries `foreignFetched`; it should read `0`
>    on every normal execution. Any run with `foreignFetched > 0` is a crossed Wappi
>    `messages/get` response caught in the act — worth noting if it ever appears.

## Fixed Issues

### WR-01: Empty `combinedText` ("") defeats the Text node's `??` fallback

**Files modified:** `Tools/n8n/apply-message-batching.py`, `Tools/n8n/verify-message-batching.py`, `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json`, `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`
**Commit:** `686e7bf`
**Applied fix:** `combinedText = parts.length > 0 ? parts.join('\n') : null;` in `LATEST_COMBINE_JS`, exactly as the review suggested, plus a comment stating why (`??` is nullish-only). New verifier assert: *"Latest+Combine joins an empty run to '' (defeats the Text node ?? fallback)"*.
**Evidence:** a node harness driving the generated JS confirms the review's scenario — a `fromMe` bot reply newest in the window now yields `combinedText=null` (was `""`), so the Text node's `?? $json.body.messages[0].body` fallback engages.

### WR-03: `Fetch Recent` un-retried on the hot path + crossed-response exposure

**Files modified:** `Tools/n8n/apply-message-batching.py`, `Tools/n8n/verify-message-batching.py`, both bot templates
**Commit:** `201e209`
**Applied fix (a) — retry:** node-level `retryOnFail: true, maxTries: 3, waitBetweenTries: 1000` on `Fetch Recent`, matching the existing `Delete Orphan Profiles` idiom (that sweep uses `waitBetweenTries: 2000`; the review specified 1000 for the hot path, so 1000 it is). Three new verifier asserts.

**Applied fix (b) — crossing, conservative version:** `Latest+Combine` now filters the fetched rows down to the requested chat *before* the is-latest/combine computation, so foreign rows from a crossed response can drive neither decision.

Field names were confirmed in the codebase before writing the filter, not guessed:
- webhook side: `wh.body.messages[0].chatId` — already the `chat_id` query param on `Fetch Recent`.
- fetched rows: `chatId` — `Assets/Scripts/Chat/RawMessage.cs:10`, and `CrossChatResponseGuard` states *"Every message in a single-chat response carries the chat's id in `RawMessage.chatId`"*. So the filter is implementable as specified; the retry-only fallback was not needed.

The filter mirrors `CrossChatResponseGuard`'s conservatism: a row with **no** `chatId` is KEPT (never discard on missing data), so if a Wappi/tapi shape ever omits it the behavior degrades to exactly today's.

**Degenerate case, explicitly chosen and documented:** when filtering leaves nothing (a fully crossed payload), `newestIncoming` is undefined → `abort = true` → the fragment dead-ends with no reply. This is deliberately the SAME outcome the pre-fix code produced for a crossed response (its `newestIncoming.id !== triggeringId` also aborted), so abort semantics are unchanged and a duplicate-reply storm is impossible. The alternative — treating "no evidence" as a licence to proceed — was rejected precisely because two fragments of one chat could then both reply.

**Evidence that the normal path is untouched:** a node harness ran burst-winner, burst-loser, single-message, media-latest, bot-reply-bounded, no-`chatId` and empty-fetch cases against the generated JS — all identical to pre-fix. An A/B against the pre-fix code (extracted from the previous commit) shows the one real behavior delta besides retry: a foreign row time-ordered *between* two of this chat's fragments used to be concatenated into the prompt (`"есть колодки\nЧУЖОЙ ТЕКСТ\nна камри 70?"`) and is now dropped — i.e. this also closes a cross-chat text-leak path that existed whenever Wappi crossed a response.

**Observability:** `foreignFetched` (count of dropped foreign rows) rides on the node output so runData can show whether crossing ever occurs from n8n at all, as the review asked.

### WR-04: Missing explicit `pairedItem` on the spliced Code node

**Files modified:** `Tools/n8n/apply-message-batching.py`, `Tools/n8n/verify-message-batching.py`, both bot templates
**Commit:** `b6a7c22`
**Applied fix:** `return [{ json: { ...wh, abort, combinedText, foreignFetched }, pairedItem: { item: 0 } }];`, matching the orchestrators' `Vertical Prompt` idiom. New verifier assert.
**Note:** the first attempt tripped the pre-existing `"$('Webhook').item" not in js` assert — my explanatory comment contained that literal substring. Rather than weaken the (correct, deliberately strict) assert, the comment was reworded. Nothing was committed in the failing state.

## Skipped Issues

### WR-02: Mixed-type bursts silently drop the earlier fragment

**File:** `Tools/n8n/apply-message-batching.py:57-71` (abort/combine logic)
**Status:** skipped — **accepted v1 scope**, recorded (not code-fixed).
**Reason:** Phase 10's locked scope is text-only combine (`10-CONTEXT.md` line 25: *"Combine boundary rule (v1): trailing run of consecutive incoming TEXT messages … Known v1 limitation"*). The review's own first option is "record it explicitly". The contained alternative it offers — prepending the window's pending text to a media winner — is a behavioral redesign of the reply path that would invalidate the already-passed runData matrix and device UAT and needs its own live e2e; that is not a review-fix-pass change.
**Action taken:** appended a clearly-marked note to the `<deferred>` section of `10-CONTEXT.md` (commit `2bcc1e3`) — the locked decisions were NOT rewritten. The note spells out all three interleavings (voice→text, text→voice, text→image), records that dropped content reaches neither the AI Agent nor Chat Memory, states plainly that it is not a regression for the 10-04 owner pass, and flags the voice→text / text→image cases for the future media-handling work's e2e matrix.

## Out of Scope (not touched)

The 7 Info findings (IN-01 … IN-07) were not addressed — this run was `critical_warning` scope. Two are C#-side (`SuggestionsController` clock mismatch, test literal) and, per the run constraints, no C# file was touched and the Unity test bridge was not used. IN-03 (`Debounce Wait` has no `webhookId`) and IN-06 (dead `type_suffix` param, unguarded `find()` lookups) both live in files this run edited and would be cheap to pick up in a `--all` pass.

## Verification Performed

- `verify-message-batching.py` exits 0 on both committed templates after every commit (`ALL BATCHING ASSERTS PASSED`).
- **Asserts proven to bite:** each of the 6 new asserts was tested against a deliberately mutated scratch copy (retry key removed / `maxTries` 5 / `waitBetweenTries` 250 / filter neutered / `foreignFetched` renamed / `pairedItem` removed / WR-01 fix reverted) — every one exited 1 with its own named reason. All scratch copies were deleted; no scratch files remain.
- **Materialization proven** for every fix: `git diff --stat` on the two templates plus a `grep -c` of the new substring in BOTH files (never a Python-only change).
- **Idempotency proven:** re-running `apply-message-batching.py` immediately after a run leaves both templates byte-identical (`shasum` compare), for the refactor and after each fix.
- **Syntax:** generated `Latest+Combine` JS passes `node --check`; both Python scripts pass `ast.parse`.
- **Semantics:** node harness over 10 scenarios + a pre/post A/B against the previous commit's JS (see WR-03).
- Working tree left clean for tracked files; only deliberately-changed paths were staged (never `git add -A/-u/.`). The unrelated untracked files in the tree were not staged.

## Enabling Refactor (prerequisite, commit `104ead8`)

The migration would otherwise have been a **no-op on re-run**: all four managed nodes were added under `if find(nodes, name=...) is None:` guards, and both templates already contained them — so editing `LATEST_COMBINE_JS` alone would have changed nothing in the JSON. `managed()` now upserts each node in place (replacing it at its current list index, preserving only the stable uuid5 `id` and the `position` in case the owner dragged it in the UI), making the script's specs the source of truth. Committed separately and proved behavior-preserving: running it produced a zero-byte diff on both templates and the verifier still passed. Consequence, now documented in `Tools/n8n/README.md` (commit for the doc: `docs(10): document upsert ownership…`): the four spliced nodes must never be hand-edited in the JSON — a re-run reverts such an edit; change the script instead.

---

_Fixed: 2026-07-27_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
