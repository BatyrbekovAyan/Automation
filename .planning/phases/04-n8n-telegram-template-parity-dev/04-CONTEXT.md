# Phase 4: n8n Telegram Template Parity (dev) - Context

**Gathered:** 2026-07-12
**Status:** Ready for planning
**Source:** Design-spec express path (docs/superpowers/specs/2026-07-12-telegram-parity-design.md §D7, §D3-server; .planning/research/telegram-parity/n8n-templates.md §6; autonomous session)

<domain>
## Phase Boundary

Make the canonical n8n workflow JSONs Telegram-correct at the FILE level, plus the small Unity form change TPL-05 needs. Deploying to dev n8n and the live e2e (TPL-06) are OWNER-assisted (dev n8n is not running; its API key lives in deny-ruled secrets.json) — tracked as the phase's human gate, mirroring Phase 3's pattern.

NOT in this phase: any ChatManager/chat-pipeline change (Phase 5), client suggestions payload (Phase 7), prod bagkz (dormant — Phase 8 checklist).

</domain>

<decisions>
## Implementation Decisions

### Telegram_Bot.json (template `4VN3gsFaC2HUYmcc`) — fixes (TPL-01..04)
All locked by design D5/D7 + research (report `n8n-templates.md` has exact node lines):
1. `HTTP Request` (send reply) URL → `https://wappi.pro/tapi/sync/message/send`.
2. `Mark Read` URL → `https://wappi.pro/tapi/sync/message/mark/read`; body keeps `message_id`; REMOVE any `mark_all` query param (undocumented on tapi mark/read).
3. `Typing` URL → `https://wappi.pro/tapi/sync/chats/typing/start` (plural "chats"; tapi has no typing/stop).
4. BOTH `Input type` Switch nodes: add a `"text"` match routing identically to `"chat"` (keep `"chat"` — harmless; do NOT reorder/remove nodes — Set Fields in the Create orchestrator indexes `nodes[0]`/`nodes[5]`, so node ORDER is load-bearing; URL/param edits and Switch-rule additions do not reorder).
5. `Listening Pause` expression: `media_info.duration` → fallback chain that also reads the webhook's flat `length_seconds` (e.g. `messages[0].media_info?.duration ?? messages[0].length_seconds ?? 2`) — n8n expressions are JS; keep the same pause math.
6. Chat Memory `sessionKey`: `...profile_id + ':' + ...messages[0].from` → `...profile_id + ':' + ...messages[0].chatId` (tapi `from` can be a username; `chatId` is stable). WhatsApp_Bot.json is NOT touched (locked: session-continuity risk).

### Create orchestrators — RAG re-stamp (TPL-05)
- `CreateTelegramWorkflow` (Uz6HBBUpAiUqVysB): after `Get Created Workflow Id` (new TG workflow id known), add a Postgres node `Restamp RAG Chunks` running an UPDATE on `documents`: set `metadata.botTgId` = new id WHERE `metadata->>'botWaId'` = the bot's WhatsApp workflow id AND `metadata->>'botTgId'` = '-1'. Uses the EXISTING Postgres credential (same one the bot templates' memory node uses, referenced by id `1H5xlpFSESU4w6JH` / name — match what Dashboard_Outcomes uses). Skip semantics: if the passed `WhatsappWorkflowId` is empty/"-1", the WHERE clause simply matches nothing — no If-node needed, but the node must tolerate that (alwaysOutputData or onError continue) so the response chain still returns the id to Unity.
- `CreateWhatsappWorkflow` (XuvOp7TxOImOAmlj): mirror — set `metadata.botWaId` WHERE `metadata->>'botTgId'` = the bot's Telegram workflow id AND `metadata->>'botWaId'` = '-1'.
- Placement invariant: the webhook response to Unity (`Send New Workflows Id`, responseMode lastNode) MUST still return `{ id }` of the created workflow — wire the re-stamp node so it does not become the last node's payload (e.g. insert BEFORE the final respond node and re-emit the id, or branch-and-merge — planner's choice, but the response contract is locked).
- SQL must be parameterized via n8n queryReplacement (NOT string-interpolated) — memory gotcha: queryReplacement comma-splits lists; single scalar values are safe.

### Unity form fields (TPL-05 client half)
- `Manager.CreateTelegramWorkflowFromStart` and `CreateTelegramWorkflowFromEdit`: add form field `WhatsappWorkflowId` = the bot's whatsappWorkflowId (or the wizard-local value in FromStart; "-1" when absent).
- `Manager.CreateWhatsappWorkflowFromStart` / `FromEdit` (exact twins): add `TelegramWorkflowId` likewise.
- WWWForm text fields land as binary parts in n8n (memory `n8n-upload-multipart-binary`) — BUT these create calls use WWWForm today and the workflows already read Name/BusinessType etc. from `$json.body` — follow the EXISTING field-reading pattern in those workflows exactly; do not "fix" transport.

### Suggest_Replies channel branch (9PTyYcelRQI7bGDb) — server half of D3 (verified in Phase 7 e2e, edited here)
- Prep code: accept `channel` ("whatsapp" default when absent — backward compat) + `botTgId`; compute `skipRag` from the channel-appropriate workflow id (telegram → botTgId, else botWaId); echo `channel` through.
- RAG: add a second vector-store node `Retrieve RAG TG` with metadata filter key `botTgId` (single-key filter invariant — never OR two keys in one call); an If node on `channel === 'telegram'` routes between the two retrieve nodes; both feed Assemble unchanged (Assemble already tolerates skipRag/empty).
- Assemble system prompt: «Владелец отправит выбранный вариант со своего WhatsApp» → channel-neutral («…из этого чата») or parameterized; keep everything else byte-identical.
- The frozen v1 request contract is ADDITIVE-only: existing clients sending no `channel`/`botTgId` must behave exactly as today.

### Validation (no live n8n available)
- Structural asserts via python/jq against the edited JSONs: URL greps, Switch rule presence, sessionKey expression, node-count/order of Telegram_Bot.json nodes[0]/nodes[5] names unchanged ("Webhook", AI Agent), Create orchestrators still parse + respond-node last, Suggest_Replies Prep/If/second-retrieve wiring, credentials referenced by the same ids/names as before.
- A `Tools/n8n/verify-telegram-parity.py` (or extend existing tooling style, cf. `build-suggest-replies.py`) that runs these asserts and exits non-zero on violation — becomes the repeatable check for the owner's deploy step.

### Deploy + e2e (TPL-06) — OWNER gate (04-HUMAN-UAT.md)
- Steps for the owner: start dev n8n (localhost:5678) + tunnel (`Tools/n8n/rotate-tunnel.py`), import/update the 4 edited workflows (by literal id, keep templates INACTIVE), authorize a dev Telegram profile (same one Phase 3 needs), create a Telegram bot from the app, run the conversation e2e (text + voice + memory multi-turn + pre-auth file re-stamp), then DEACTIVATE the clone. Existing dev TG clones (if any) carry wrong api/sync URLs → recreate.

### Claude's Discretion
- Exact n8n node JSON details (ids, positions), python assert script structure, whether the re-stamp uses one UPDATE with jsonb_set or two.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Design + research
- `docs/superpowers/specs/2026-07-12-telegram-parity-design.md` — §D7 (template fixes), §D3 (suggestions contract)
- `.planning/research/telegram-parity/n8n-templates.md` — exact node lines + parity checklist §6
- `.planning/research/telegram-parity/tapi-shapes.md` — §1 endpoint table (tapi names/params), §8 webhook payload notes
- `.planning/research/telegram-parity/suggestions-vmeste.md` — Suggest_Replies workflow walkthrough §2

### Files to edit
- `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`
- `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`
- `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json`
- `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`
- `Assets/Scripts/Main/Manager.cs` (create-workflow form fields only)

### Conventions + invariants
- `Tools/n8n/README.md` — template invariants (literal ids, keep inactive, shared webhook path 0091024b-7b46)
- `Tools/n8n/build-suggest-replies.py` — existing deployer/tooling style
- Memory gotchas: bodyless n8n POST needs explicit Content-Type: application/json (Unity side); n8n Postgres queryReplacement comma-splits lists; n8n_chat_histories message shape is FLAT {type,content}

</canonical_refs>

<specifics>
## Specific Ideas

- Keep both bot templates byte-identical EXCEPT the documented channel deltas (name, botTgId/botWaId filter key, and now the 6 tapi fixes) — future diff-audits rely on this.
- The `Download Audio` node reads webhook `file_link` — channel-neutral, do not touch.
- Group guard If (`from == chatId`) stays as-is pending Phase 3 capture verdicts (docs example holds for TG private chats).
</specifics>

<deferred>
## Deferred Ideas

- Client suggestions payload (channel/botTgId fields in N8nSuggestionsProvider) → Phase 7.
- Group-guard change for TG groups → only if Phase 3 capture disproves `from == chatId`.
- Prod bagkz replication → Phase 8 checklist.
</deferred>

---

*Phase: 04-n8n-telegram-template-parity-dev*
*Context gathered: 2026-07-12 via design-spec express path*
