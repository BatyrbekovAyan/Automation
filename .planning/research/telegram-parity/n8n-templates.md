# n8n-templates

## Summary
BUG CONFIRMED: all three outbound Wappi HTTP nodes in Telegram_Bot.json (message/send, message/mark/read, chat/typing/start) post to the WhatsApp base `https://wappi.pro/api/sync/*` instead of `tapi/sync/*`, and CreateTelegramWorkflow's Set Fields never rewrites those URLs when cloning, so every created Telegram bot clone inherits them. Apart from that, the two bot templates are byte-identical (structural diff shows only the workflow name, the vector-store metadata key botTgId vs botWaId, and triggerCount) — so the group guard (If from==chatId), AI Agent config, sessionKey, memory, error paths, and «Вместе»-suppression absence are all symmetric, and the Telegram template blindly assumes the WhatsApp webhook payload shape (body.messages[0].{from,chatId,type:chat|ptt|image|document,file_link,media_info.duration}), which is undocumented/unverified for tapi. The four orchestrators have full parity: byte-identical Vertical Prompt code nodes (md5-equal across all 4), identical Set Fields/Set Bussiness Type expressions, and CreateTelegramWorkflow correctly registers webhooks via `tapi/webhook/url/set` + `tapi/webhook/types/set` with body ["incoming_message"]. One real RAG gap: chunks uploaded before Telegram auth are stamped botTgId="-1" and will never match a later-created Telegram clone's `botTgId == $workflow.id` filter.

## Open questions
- No tapi webhook payload sample exists anywhere in the repo — cannot verify from source whether Wappi Telegram sends the same body.messages[0] field names, the same type strings (chat/ptt/image/document), or the same from/chatId semantics as WhatsApp; requires a live tapi payload capture (external API calls were off-limits for this audit).
- Whether wappi.pro/tapi/sync/ actually exposes message/mark/read and chat/typing/start equivalents (the CLAUDE.md tapi endpoint list only documents auth/profile/status endpoints) — needs Wappi docs or live probing.
- Whether any Telegram bot clones already exist in the dev n8n instance carrying the wrong api/sync URLs (repo shows only the template; instance state not inspectable read-only from here).
- Whether the Download Audio node's WappiAuthToken header is accepted/required by tapi-hosted file_link URLs.

## Report
# Telegram vs WhatsApp n8n diff-audit

All paths relative to `/Users/ayan/Projects/Automation/Tools/n8n/workflows/` unless absolute.

## 1. BUG CHECK — Telegram_Bot.json outbound HTTP nodes (CONFIRMED)

Complete list of `n8n-nodes-base.httpRequest` nodes in `4VN3gsFaC2HUYmcc-Telegram_Bot.json`:

| Node | Method | URL | Line | Verdict |
|---|---|---|---|---|
| `HTTP Request` (send reply) | POST | `https://wappi.pro/api/sync/message/send` | :23 | **WRONG — WhatsApp base; should be `tapi/sync/message/send`** |
| `Mark Read` | POST | `https://wappi.pro/api/sync/message/mark/read` | :268 | **WRONG — should be `tapi/sync/...`** |
| `Typing` | POST | `https://wappi.pro/api/sync/chat/typing/start` | :313 | **WRONG — should be tapi (if the endpoint exists on tapi at all — unverified)** |
| `Download Audio` | GET (default) | `={{ $json.body.messages[0].file_link }}` | :555 | Dynamic — channel-neutral by construction (follows whatever URL Wappi puts in the payload); carries the `WappiAuthToken` httpHeaderAuth credential |

Bodies (all still valid-looking for tapi, pending payload-shape confirmation):
- send: `body={{ $json.text }}`, `recipient={{ $('Webhook').item.json.body.messages[0].from }}` (:37–46)
- mark read: `message_id={{ ...messages[0].id }}` (:288)
- typing: `recipient={{ ...messages[0].from }}` (:329)

**The bug propagates to every created bot**: `Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json` clones template `4VN3gsFaC2HUYmcc` verbatim (Get Sample Workflow, :6) and its `Set Fields` node (:71–90) rewrites ONLY `name`, `nodes[0].parameters.path` (webhook path → TelegramProfileId, :80), and `nodes[5].parameters.options.systemMessage` (:86). No URL rewriting anywhere. So all existing Telegram clones send/mark-read/typing against the WhatsApp API namespace with a Telegram profile_id — wrong channel.

**Templates are otherwise identical**: a structural diff of the two bot templates (positions/ids stripped, nodes sorted) yields exactly 3 differences: workflow `name` ("Telegram Bot" vs "WhatsApp Bot"), vector-store metadata key `botTgId` (TG :690) vs `botWaId` (WA :690), and `triggerCount` (0 vs 1). Both share the same template webhook `path: "0091024b-7b46"` AND the same `webhookId` (TG :7) — deliberate per `Tools/n8n/README.md` ("Keep both inactive — they share webhook path 0091024b-7b46 and only the per-bot clones ever go active"). Both `active: false`.

## 2. Group-message guard

Telegram_Bot **has the identical guard**: `If` node (:641, condition :608–628) — `{{ $json.body.messages[0].from }}` string-equals `{{ $json.body.messages[0].chatId }}` (strict, case-sensitive). True → `Input type`; false output is unconnected → group messages silently dropped. Byte-identical to WhatsApp_Bot's.

**Template payload expectations** (all WhatsApp-Wappi shape, unverified for tapi):
- `body.messages[0].id` (Mark Read), `.from` (recipient + guard + sessionKey), `.chatId` (guard), `.profile_id` (sessionKey), `.body` (text input + word count), `.file_link` (voice download), `.media_info.duration` (Listening Pause, :577)
- `type` values switched on: `"chat"`, `"ptt"`, `"image"`, `"document"` + fallback (Input type / Input type2 nodes). image/document/fallback all route to `Ask to Send Text` → sends literal "Please send text messages".
- Webhook event registration is `["incoming_message"]` only (see §4).

No tapi webhook payload sample or doc exists anywhere in the repo (grepped `Tools/`, `docs/`, `.planning/`) — whether tapi emits the same field names, the same `type` strings (`ptt` is WhatsApp jargon), and `from==chatId` semantics for Telegram private chats/groups/channels is an **open question requiring live capture**.

## 3. Vertical Prompt injection — FULL PARITY (confirmed)

All four orchestrators contain a `Vertical Prompt` code node with **byte-identical jsCode** (md5 `7959a0833c76794aab3456185600125b` for all 4): CreateTelegram :282, CreateWhatsapp :282, Edit_Telegram :154, Edit_Whatsapp :154. Id extraction: `const id = ($('Unity Webhook').first().json.body || {}).BusinessTypeId || "";` → `PROMPTS[id] || ""` (all 6 KZ vertical prompts embedded, generated by `Tools/n8n/inject-prompts.py`).

- Create composition (`Set Fields`, CreateTelegram :86–87): `Business Type: {BusinessType}\n\n{{ verticalPrompt || existing systemMessage.slice(1) }}\n\nAdditional Instructions: … About Business / Products / Services` — identical to WhatsApp's, wired Unity Webhook → Vertical Prompt → Get Sample Workflow → Set Fields.
- Edit composition (Edit_Telegram `Set Fields` :97–98 with preserve-head slice on `"\nAdditional Instructions: "`; `Set Bussiness Type` :123–124 rewrites the first line): identical to WhatsApp's, wired Unity Webhook → Get Workflow → Vertical Prompt → Set Bussiness Type → Set Fields → Update Workflow (PUT `{{ $json }}`, responseMode lastNode).
- Complete Edit-pair diff (ids stripped) = 3 lines: workflow name, `Get Workflow` URL field `TelegramWorkflowId` vs `WhatsappWorkflowId` (:23), webhook path `EditTelegramWorkflow` vs `EditWhatsappWorkflow` (:7).

## 4. Webhook registration — CORRECT tapi bases in CreateTelegramWorkflow

- `Set Wappi Webhook`: POST `https://wappi.pro/tapi/webhook/url/set` (:180), query `profile_id={TelegramProfileId}`, `url=https://bagkz.app.n8n.cloud/webhook/{TelegramProfileId}` (:184–193). WhatsApp: same but `api/webhook/url/set` (:24) with WhatsappProfileId.
- `Set Wappi Webhook Types`: POST `https://wappi.pro/tapi/webhook/types/set` (:216), query `profile_id`, JSON body `["incoming_message"]` (:230). WhatsApp identical (`api/...`, :60, body :74).
- Clone's inbound webhook path = TelegramProfileId (Set Fields `nodes[0].parameters.path` :80) — matches the registered callback URL.
- Flow (both channels identical): Unity Webhook (`path: CreateTelegramWorkflow`, responseMode lastNode) → Vertical Prompt → Get Sample Workflow → Set Fields → Create Workflow (POST /api/v1/workflows, `jsonBody={{ $json }}`) → Get Created Workflow Id → Activate Created Workflow → Set Wappi Webhook → Set Wappi Webhook Types → Send New Workflows Id (returns `{ id }` to Unity).
- Cosmetic asymmetry: CreateWhatsapp's Activate URL has a **trailing space** (`.../{{ $json.id }}/activate `); CreateTelegram's doesn't.

## 5. Other asymmetries / parity notes

- **AI Agent**: identical in both templates (generic English assistant systemMessage; the vertical/business prompt is stamped at clone/edit time into `nodes[5]`). Both Set Fields paths are **index-based** (`nodes[0]`, `nodes[5]`) — template node ORDER is load-bearing (0=Webhook, 5=AI Agent); any node insertion into a template breaks both orchestrators.
- **Chat Memory** (both templates, TG :592): `memoryPostgresChat`, `sessionIdType: customKey`, EXACT sessionKey expression: `={{ $('Webhook').item.json.body.messages[0].profile_id + ':' + $('Webhook').item.json.body.messages[0].from }}`, `contextWindowLength: 50`, Postgres cred `1H5xlpFSESU4w6JH`. Telegram sessions will therefore land in the same `n8n_chat_histories` table with `tgProfileId:from` keys — note `2htWSV5IHO8E2CgB-Dashboard_Outcomes.json` consumes that table and is WhatsApp-only in v1.
- **RAG/vector tool**: both `retrieve-as-tool`, table `documents`, queryName `match_documents`, topK 10, no reranker, single metadata filter = `$workflow.id`: `botTgId` (TG :690) vs `botWaId` (WA :690). `KoTuIlk4LMrlvnWI-Upload_File.json`'s Data Loader stamps **both** `botWaId` and `botTgId` (+contentType/source/fileId) on every chunk; Unity sends both form fields per upload (`Assets/Scripts/Main/BotSettings.Auth.cs:584-585`). **GAP**: a bot whose files were uploaded before Telegram auth has chunks stamped `botTgId="-1"` (sentinel, `Assets/Scripts/Main/Bot.cs:67`) — a later-created Telegram clone's filter will never match them; needs re-index (originals are preserved in the `price-lists` bucket precisely for re-indexing) or a re-stamp UPDATE on `documents.metadata`.
- **Вместе (reply-mode) suppression**: NO suppression hook in either bot template — symmetric absence (the bot always replies when its workflow is active). Server-side Вместе suppression is a known pending item. `9PTyYcelRQI7bGDb-Suggest_Replies.json` is WhatsApp-only (request carries `botWaId` only) — Telegram semi-auto mode needs a `botTgId` (or channel-generic) extension.
- **Error paths**: zero `onError`/`retryOnFail` on any node in either template or any orchestrator — failures kill the execution silently. Symmetric (nothing to fix for parity, but same fragility on both channels).
- **Credentials**: all Wappi HTTP nodes in BOTH templates use the same `httpHeaderAuth` credential `WappiAuthToken` (id `EuhhqAaV56DpoqAM`… exactly `EuhhqAaV56DpoqAN`) — one shared token serves api and tapi (matches `Manager.wappiAuthToken` single-token pattern). OpenAI cred `XVjhR1xlWrIgJjKz`, Supabase `ifCYb9DhUxoCq3pu` identical.
- **Humanizer chain** (Pause Before Reading → Mark Read → Input type2 → Count Input Words/Listening Pause → Reading Pause → Typing → Count Output Words → Typing Pause → Retrieve Answer → HTTP Request): identical in both; the whole chain depends on the WhatsApp-shaped payload + api endpoints.
- **`Tools/n8n/workflows-local/`** is a STALE Jul-2 local-dev snapshot (localhost:5678 n8n URLs, trycloudflare Wappi callback, NO Vertical Prompt wiring, different credential ids, only 8 of 12 workflows). Its `Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json` predates the vertical-prompts rollout — don't audit parity against it; `rotate-tunnel.py` targets the live local instance, not these files.

## 6. Parity checklist — what must change

**Telegram_Bot.json (template `4VN3gsFaC2HUYmcc`) — the only file with confirmed bugs:**
1. `HTTP Request` :23 → `https://wappi.pro/tapi/sync/message/send`.
2. `Mark Read` :268 → `https://wappi.pro/tapi/sync/message/mark/read` (verify endpoint exists on tapi).
3. `Typing` :313 → `https://wappi.pro/tapi/sync/chat/typing/start` (verify tapi has a typing endpoint; if not, remove/bypass the node — the humanizer chain routes through it).
4. Capture a real tapi `incoming_message` webhook payload and re-validate every expression: `body.messages[0].{id,from,chatId,profile_id,type,body,file_link,media_info.duration}`, the `type` switch values (`chat`/`ptt`/`image`/`document` are WhatsApp strings — Telegram voice may not be `ptt`), and whether `from==chatId` still cleanly separates private chats from groups/channels on Telegram (else replace the guard).
5. Re-deploy: since Create clones by literal id, fixing the template fixes only FUTURE bots — existing Telegram clones (if any) must be recreated or patched in place.

**CreateTelegramWorkflow (`Uz6HBBUpAiUqVysB`) — already correct:** tapi webhook/url/set + webhook/types/set, TelegramProfileId path stamping, Vertical Prompt parity. No changes needed (optionally trim WhatsApp's trailing-space activate URL for hygiene, in the WA file).

**Edit_Telegram_Workflow (`TwWPW3gIyjZS3foR`) — already correct:** full parity with Edit_Whatsapp. No changes.

**Adjacent parity work surfaced (not in these 6 files):**
6. RAG re-stamp: decide how bots that uploaded files while `telegramWorkflowId=="-1"` get retrievable chunks after Telegram auth (SQL UPDATE of `documents.metadata->>'botTgId'` keyed by botWaId, or bucket-driven re-index).
7. Suggest Replies (`9PTyYcelRQI7bGDb`): WhatsApp-only request contract (`botWaId`); extend for Telegram if «Вместе» ships on TG.
8. Dashboard Outcomes (`2htWSV5IHO8E2CgB`): WhatsApp-only v1; Telegram sessions will appear in `n8n_chat_histories` under the same key scheme once the template is fixed.
9. `Tools/n8n/README.md` invariants carry over unchanged: keep both templates inactive, never change their literal ids, both share webhook path `0091024b-7b46`.
