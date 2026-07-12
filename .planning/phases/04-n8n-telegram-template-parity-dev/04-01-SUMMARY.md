---
phase: 04-n8n-telegram-template-parity-dev
plan: 01
subsystem: infra
tags: [n8n, telegram, tapi, wappi, postgres, supabase, rag, workflows, suggestions]

# Dependency graph
requires:
  - phase: 02-live-suggestions (v1.0)
    provides: Suggest Replies workflow (9PTyYcelRQI7bGDb) with frozen v1 request contract + single-key RAG filter
  - phase: 03-tapi-live-shape-capture
    provides: tapi divergence verdicts (type:"text", numeric ids, sessionKey stability) — server half applied here
provides:
  - Telegram_Bot template runs on tapi bases (send/mark-read/typing) — future TG clones converse
  - Telegram text (type:"text") routes through the AI agent, not the fallback
  - Voice humanizer resilient to missing media_info.duration (length_seconds fallback)
  - Chat Memory sessionKey keyed on chatId (stable) instead of from
  - RAG re-stamp node in BOTH Create orchestrators (pre-auth "-1" chunks become retrievable on late channel auth) preserving the { id } response
  - Additive channel-branched RAG retrieval in Suggest_Replies (botWaId | botTgId), injection-hardened
  - Repeatable structural verifier verify-telegram-parity.py
affects: [04-02 (Unity form fields WhatsappWorkflowId/TelegramWorkflowId + HUMAN-UAT deploy), phase-5 (chat pipeline), phase-7 (client suggestions payload)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "n8n RAG re-stamp: parameterized Postgres executeQuery (queryReplacement $1/$2, never string-interpolated) with alwaysOutputData + onError continueRegularOutput so a 0-row/DB error never surfaces in the webhook response"
    - "Additive server branch: a client field (channel) selects between two hardcoded single-key vector-store nodes — never becomes the filter key/value; absent field = byte-identical prior behavior"
    - "Structural-assert verifier (verify_rag.py style): paths resolved from __file__, print-and-sys.exit(1) on first violation"

key-files:
  created:
    - Tools/n8n/verify-telegram-parity.py
  modified:
    - Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json
    - Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json
    - Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json
    - Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json

key-decisions:
  - "Re-stamp SQL parameterized via queryReplacement two-expression comma form (={{ new id }},={{ WorkflowId }}) → $1/$2; safe because workflow ids are scalar with no commas (project invariant)"
  - "jsonb_set path is a static literal ({botTgId}/{botWaId}), never client-derived — SQLi surface closed at the SQL level"
  - "Retrieve RAG TG shares the existing Embeddings sub-node (executor discretion) rather than adding a second embeddings node"
  - "Re-stamp node uses the executeQuery Postgres cred vvRrFiEXzLVqKjOx (Dashboard_Outcomes/Delete_File), NOT the memoryPostgresChat cred 1H5xlpFSESU4w6JH"

patterns-established:
  - "Re-stamp inserted BEFORE the terminal response node (Send New Workflows Id re-emits id by node reference) so the Unity { id } contract is preserved"
  - "Bot-template node order stays load-bearing: only URL/param/rule/expression edits, zero node insert/reorder in Telegram_Bot.json"

requirements-completed: [TPL-01, TPL-02, TPL-03, TPL-04, TPL-05]

# Metrics
duration: ~15min
completed: 2026-07-12
---

# Phase 4 Plan 01: n8n Telegram Template Parity (dev) Summary

**Telegram_Bot template moved onto tapi bases (send/mark-read/typing + text routing + length_seconds voice fallback + chatId session key), a parameterized RAG re-stamp node added to both Create orchestrators so pre-auth "-1" chunks become retrievable on late channel auth, and Suggest_Replies given an additive channel-branched RAG filter — all proven by a new structural verifier.**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-07-12T13:22Z (approx, first file read)
- **Completed:** 2026-07-12T13:36Z
- **Tasks:** 3
- **Files modified:** 4 workflows + 1 new verifier (5 files)

## Accomplishments
- **TPL-01..04 (Telegram_Bot.json):** three outbound HTTP nodes now POST to `tapi/sync/{message/send, message/mark/read, chats/typing/start}` with zero `api/sync` outbound URLs; removed the undocumented `mark_all` query param; both `Input type` Switch nodes route `type:"text"` AND `"chat"` to the AI agent (combinator `or`); `Listening Pause` resolves `media_info?.duration ?? length_seconds ?? 0` (+2); Chat Memory `sessionKey` keys on `profile_id + ':' + chatId`. Node count (24) and order (nodes[0]=Webhook, nodes[5]=AI Agent) unchanged.
- **TPL-05 server half (both Create orchestrators):** a `Restamp RAG Chunks` Postgres node runs a parameterized `UPDATE documents SET metadata = jsonb_set(...)` re-stamping the `-1` sentinel to the newly created workflow id, wired between `Set Wappi Webhook Types` and the terminal `Send New Workflows Id` so Unity still receives `{ id }`. Robustness flags (`alwaysOutputData` + `onError: continueRegularOutput`) mean a 0-row UPDATE or DB error never breaks the response.
- **D3 server half (Suggest_Replies):** Prep reads additive `channel`/`botTgId`, computes the channel-appropriate `wfId` for `skipRag`, and echoes both through; an `If channel TG?` node routes the RAG false-branch to `Retrieve RAG TG` (single-key `botTgId`) for Telegram or the original `Retrieve RAG` (single-key `botWaId`) otherwise; both feed `Assemble`; the Embeddings sub-node fans out to both. Absent `channel` = byte-identical v1.0 routing. Assemble copy made channel-neutral («из этого чата»).
- **Verifier:** `Tools/n8n/verify-telegram-parity.py` asserts every edit above (including the injection-safe re-stamp shape and the cred-id guard) and exits non-zero with a clear message on the first violation.

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix Telegram_Bot.json onto tapi (TPL-01..04)** - `4a3f16b` (fix)
2. **Task 2: RAG re-stamp (both Create orchestrators) + Suggest_Replies channel branch (TPL-05 server + D3)** - `5529fd3` (feat)
3. **Task 3: Structural-assert verifier verify-telegram-parity.py** - `6aaea8f` (test)

**Plan metadata:** committed with STATE.md/ROADMAP.md/REQUIREMENTS.md updates.

## Files Created/Modified
- `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` - tapi URLs, no mark_all, text routing on both Switch nodes, length_seconds voice fallback, chatId sessionKey (node count/order unchanged)
- `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json` - `Restamp RAG Chunks` Postgres node (botTgId re-stamp keyed by botWaId) + connection rewire
- `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json` - mirror `Restamp RAG Chunks` (botWaId re-stamp keyed by botTgId) + connection rewire
- `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` - Prep channel/botTgId/wfId, `If channel TG?` + `Retrieve RAG TG` nodes, Embeddings fan-out, channel-neutral Assemble copy
- `Tools/n8n/verify-telegram-parity.py` - stdlib-only structural verifier for the 4 edited workflows

## Decisions Made
- **queryReplacement two-expression form.** The re-stamp binds `$1`/`$2` via a single comma-joined expression string (`={{ $('Get Created Workflow Id').item.json.id }},={{ $('Unity Webhook').first().json.body.<Field>Id }}`). Safe per the project invariant: workflow ids are scalar with no commas (n8n's queryReplacement comma-splits into positional params). The SQL text itself contains only `$1`/`$2` — the verifier asserts no `{{` leaks into the query string.
- **Static jsonb_set path.** `'{botTgId}'` / `'{botWaId}'` are literals, never client-derived — the client `WhatsappWorkflowId`/`TelegramWorkflowId` only reach the parameterized WHERE value.
- **Shared Embeddings sub-node.** `Retrieve RAG TG` reuses the existing `Embeddings` node (plan-granted discretion) rather than duplicating it.
- **Credential id.** Re-stamp nodes use `vvRrFiEXzLVqKjOx` (the executeQuery Postgres cred), explicitly NOT the memoryPostgresChat cred `1H5xlpFSESU4w6JH`; the verifier fails if that id ever appears on the re-stamp node.

## Deviations from Plan

None - plan executed exactly as written. The revised credential id (`vvRrFiEXzLVqKjOx`, per the prompt's noted plan revision) was used throughout; the plan's older `<decisions>` text mentioning `1H5xlpFSESU4w6JH` was superseded by the plan body's `<interfaces>`/`<action>` and the prompt's explicit revision note.

## Issues Encountered
- A shell-quoting artifact made the Task-1 combined `grep && echo OK` line skip its `OK` (the sessionKey pattern's `\[0\]`/`$('Webhook')` escaping under double quotes); re-ran with `grep -F` and a Python node-level assert — all criteria confirmed. No file impact.

## Threat Surface Notes
No new threat surface beyond the plan's `<threat_model>`. All four registered mitigations are implemented and verifier-checked:
- **T-04-01 (SQLi):** parameterized `$1`/`$2`, static jsonb_set path, verifier asserts no `{{` in SQL.
- **T-04-02 (info disclosure):** `onError: continueRegularOutput` + `alwaysOutputData` + id re-emitted by node reference — DB errors never surface in the HTTP body.
- **T-04-03 (filter-key injection):** server-side branch chooses between two hardcoded single-key nodes; client `channel` never becomes the key/value.
- **T-04-04 (cross-tenant RAG):** accepted per plan — botTgId carries the bot's own workflow id, skipRag sentinel gates empty ids, absent channel = v1.0 behavior.

## User Setup Required
None in this plan. The live deploy + e2e (TPL-06) is the OWNER gate (04-02's `04-HUMAN-UAT.md`): start dev n8n (localhost:5678) + tunnel, import/update the 4 edited workflows by literal id (keep templates INACTIVE), authorize a dev Telegram profile, create a Telegram bot from the app, run the conversation e2e (text + voice + memory + pre-auth file re-stamp), then DEACTIVATE the clone. Not runnable here — no live n8n and its API key is in deny-ruled secrets.json.

## Next Phase Readiness
- **04-02 ready:** this plan reads `WhatsappWorkflowId` (CreateTelegram) and `TelegramWorkflowId` (CreateWhatsapp) from the webhook `body`; 04-02 adds those form fields on the Unity side and owns the HUMAN-UAT deploy. When the fields are absent/"-1" the re-stamp WHERE matches nothing — safe no-op.
- **Phase 7 ready:** Suggest_Replies now accepts the additive `channel`/`botTgId`; the client payload change (N8nSuggestionsProvider) lands there and will be verified live.
- **Blocker carried:** any existing dev Telegram clones carry the old `api/sync` URLs — recreate after importing the fixed template (already noted in STATE.md).

## Self-Check: PASSED

- FOUND: Tools/n8n/verify-telegram-parity.py
- FOUND: .planning/phases/04-n8n-telegram-template-parity-dev/04-01-SUMMARY.md
- FOUND commits: 4a3f16b, 5529fd3, 6aaea8f

---
*Phase: 04-n8n-telegram-template-parity-dev*
*Completed: 2026-07-12*
