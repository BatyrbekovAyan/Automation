# n8n workflows

Canonical n8n workflows for the BAGKZ app. The app talks to n8n for bot creation,
prompt editing, RAG file upload/delete, and (in progress) live reply suggestions.

## Layout

- `workflows/` — **committed source of truth**: the 13 workflows the app actually depends on.
  Each JSON has its original n8n `id` injected at the top level so it round-trips on import
  (except the freshly-authored `Set_Reply_Mode.json`, whose id is assigned on first deploy).
- `supabase/` — the RAG store's DB contract: `schema.sql` (documents table +
  `match_documents` as deployed — note its multi-key filter uses OR semantics), the
  applied hardening migrations (RLS default-deny, anon revoke, HNSW + metadata indexes),
  the `price-lists` originals bucket (`2026-07-02-price-list-originals-bucket.sql` —
  must be applied once per Supabase project before the Store Original File node works),
  and `audit-price-lists-bucket.sql` — cross-checks bucket objects against
  `documents.metadata->>'fileId'`. Invariant: zero `orphaned-unexpected`; image
  orphans are `orphaned-by-rejection` (422-rejected photos, kept for re-OCR by design).
- `build-suggest-replies.py` — deploys/exports the shared **Suggest Replies** workflow. Deploy
  imports the committed canonical `workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` VERBATIM
  (Webhook → Prep → If invalid [known-garbage → straight to Build Response's `generation_failed`,
  zero LLM spend] → If skipRag → If channel TG? → channel-scoped RAG load [`botTgId`|`botWaId`] →
  Assemble [incl. the 08-13 D10 «РЕЛЕВАНТНОСТЬ» newest-incoming anchor] → LLM json_schema →
  Validate → retry-once → Build Response → Respond), rebinds ONLY the credential ids for the
  target, and activates it; `--update <id>` PUTs the same content onto an existing id; `--dry-run`
  prints the exact payload offline; `--export <id> <out>` re-emits the canonical JSON. Credential
  ids resolve flag/env (`--openai-cred`/`--supabase-cred`, `N8N_OPENAI_CRED_ID`/
  `N8N_SUPABASE_CRED_ID`) > exact-NAME lookup in the local SQLite (misnamed fails loudly listing
  candidates) > **loud error** — never a silent dev-id fallback. The old `--stage front/full`
  generator literals predated the phase-4 channel branch + D10 and are retired (deploying them
  silently reverted both); the flag now errors with guidance.
- `build-set-reply-mode.py` — deploys/exports the shared **Set Reply Mode** workflow (the 13th).
  Deploy imports the committed canonical `workflows/Set_Reply_Mode.json` VERBATIM
  (Webhook → Validate [malformed body → `bad_request` before any DB write] → If invalid? →
  one item per surviving profileId → Upsert into `reply_mode_flags` [on conflict do update] →
  Respond), rebinds ONLY the Postgres credential id, and activates it; `--update <id>` PUTs the
  same content onto an existing id; `--dry-run` prints the exact payload offline; `--export
  <id> <out>` re-emits the canonical JSON. The Postgres cred is bound by **explicit id**
  (default `1H5xlpFSESU4w6JH`, override `--postgres-cred` / `N8N_POSTGRES_CRED_ID`) — never by
  name, because two credentials are both named "Postgres" (the Chat Memory DB the gate reads vs
  the Dashboard/RAG DB). The DDL for `reply_mode_flags` lives in
  `supabase/2026-07-19-reply-mode-flags.sql` (apply through cred `1H5xlpFSESU4w6JH`).
- `apply-*.py` — idempotent migrations over `workflows/` (edit by node name, re-runnable);
  `verify_rag.py` asserts every applied invariant; `test-upload-e2e.sh` exercises the
  Upload/Delete webhooks end-to-end against a live instance (curl mimicking Unity's
  WWWForm binary-part quirk).
- `rotate-tunnel.py` — run after every cloudflared quick-tunnel restart: auto-detects the
  new trycloudflare host and re-points secrets.json, the live local Create handlers'
  Wappi callback, and every bot's Wappi webhook registration, then verifies (see
  `dev-tunnel.md`). A missed manual step here caused the 2026-07-03 "bots stopped
  replying" outage.
- `reference/` — **gitignored**: downloaded community/marketplace templates + n8n onboarding
  samples, kept only to mine for ideas. Not part of the app, never imported.

## The 13 canonical workflows

| id | name | role |
|----|------|------|
| `XuvOp7TxOImOAmlj` | CreateWhatsappWorkflow | App webhook `/webhook/CreateWhatsappWorkflow` — clones the WhatsApp template per bot |
| `Uz6HBBUpAiUqVysB` | CreateTelegramWorkflow | App webhook `/webhook/CreateTelegramWorkflow` — clones the Telegram template per bot |
| `3qax5J9u2qsT9Vao` | Edit Whatsapp Workflow | App webhook `/webhook/EditWhatsappWorkflow` — edits a bot's system prompt |
| `TwWPW3gIyjZS3foR` | Edit Telegram Workflow | App webhook `/webhook/EditTelegramWorkflow` — edits a bot's system prompt |
| `KoTuIlk4LMrlvnWI` | Upload File | App webhook `UploadFile` — ingests files into the Supabase vector store; stamps `botWaId`/`botTgId`/`fileId` on every chunk; extension routing is case-insensitive; archives the uploaded bytes to Storage `price-lists/{fileId}` (dead-end branch, `onError: continue` — never fails the upload); unsupported types get an explicit 415; photos (jpg/jpeg/png/webp client-side) route to OpenAI gpt-4o-mini vision extraction (422 `no_price_data` gate if unreadable), archived like all other uploads |
| `ZTqpumOpL1rNDOp6` | Delete File | App webhook `DeleteFile` — body `{ fileId }`; deletes that file's chunks from `documents` AND its stored original `price-lists/{fileId}` (404 tolerated for pre-bucket files), returns `{ success, deletedChunks }` |
| `4wYitz5ek30SVNlT` | WhatsApp Bot | **Clone source** for every WhatsApp bot (referenced by literal id in CreateWhatsappWorkflow); retrieval self-scoped by `botWaId = {{ $workflow.id }}`; **Phase 10:** carries the pre-generation debounce+combine splice on the `Suppressed?` FALSE branch (see note below) |
| `4VN3gsFaC2HUYmcc` | Telegram Bot | **Clone source** for every Telegram bot (referenced by literal id in CreateTelegramWorkflow); retrieval self-scoped by `botTgId = {{ $workflow.id }}`; **Phase 10:** carries the same debounce+combine splice on the `Suppressed?` FALSE branch (see note below) |
| `lmjYsdNcQA2IE5rl` | Delete Bot Files | App webhook `DeleteBotFiles` — body `{ botWaId, botTgId }`; sweeps ALL of a deleted bot's RAG chunks + stored originals (guards the `"-1"` unauthed sentinel) |
| `2htWSV5IHO8E2CgB` | Dashboard Outcomes | App webhook `DashboardOutcomes` — body `{ profileIds }`; classifies conversation outcomes from `n8n_chat_histories` into `conversation_outcomes`, returns them for the «Сводка» dashboard |
| `2islisFH7jjLoPQM` | Delete Orphan Profiles | **Scheduled, hourly** (no webhook) — server-side TTL sweep deleting Wappi profiles that stay unauthorized ≥ 24h; see below |
| _(assigned on first deploy)_ | Set Reply Mode | App webhook `SetReplyMode` — shared always-active; body `{ profileIds:[...], chatId:"*"\|"<id>", suppressed:bool }`; validates (malformed → `bad_request` before any DB write), fans out one item per surviving profileId, upserts each into `reply_mode_flags` (on conflict do update). The semi-auto «Авто/Вместе» suppression write path (SUP-02); the bot templates' gate reads the same table. Deployed by `build-set-reply-mode.py` (Postgres cred bound by explicit id `1H5xlpFSESU4w6JH`); id assigned on first deploy, filename finalized to `<id>-Set_Reply_Mode.json` in 09-04 |
| `9PTyYcelRQI7bGDb` | Suggest Replies | App webhook `SuggestReplies` — body = frozen v1 request (`{ v, requestSeq, chatId, botWaId, businessTypeId, catalog, steerTowardText, messages… }`); known-invalid requests (v mismatch / missing `chatId` / empty `messages`) short-circuit straight to `generation_failed` — zero LLM spend on the unauthenticated webhook; optional channel-branched tenant-scoped RAG pre-retrieval (one single-key filter per channel: `botWaId` WA / `botTgId` TG, topK 5, skipped on `""`/`"-1"`) → one gpt-4o-mini call (strict json_schema, closed 6-label enum) → Code validation (exactly 4 distinct enum-labeled moves, ≤300 clamp, markdown-strip, one retry then `generation_failed`) → returns `{ v:1, requestSeq, suggestions:[{text,label}×4] }` for the semi-auto «Вместе» reply panel. Deployed from the committed canonical JSON by `build-suggest-replies.py` (dev id here; prod bagkz replication pending). Adversarially verified on dev 2026-07-10 (6-case matrix — grounding / missing-data / steer / injection / trivial / sentinel — plus format-hijack + malformed→`generation_failed`, **zero fixes needed**); dev RAG grounding is **catalog-only** until Supabase `documents` are seeded — RAG-with-data deferred to prod replication |

> ⚠️ `4wYitz5ek30SVNlT` and `4VN3gsFaC2HUYmcc` are referenced by **literal id** inside the
> two Create handlers. Never change their ids, or bot creation 404s on the clone step.
> Keep both **inactive** — they share webhook path `0091024b-7b46` and only the per-bot
> clones (with rewritten paths) ever go active.

> **Phase 10 — message-batching / debounce splice.** Both bot templates carry a pre-generation
> `Debounce Wait → Fetch Recent → Latest+Combine → Is Latest?` stage on the `Suppressed?` FALSE
> branch (before `Input type`) that coalesces a burst of multi-fragment customer messages into ONE
> combined reply — only the last fragment's execution proceeds; earlier fragments dead-end. It is
> authored by the idempotent `apply-message-batching.py` (edits both templates in place, by node
> name). **Re-run `apply-message-batching.py` after any template re-import / UI round-trip**, then
> run `verify-message-batching.py` to gate the splice (asserts the 4 nodes, the `Suppressed? →
> Debounce Wait` rewire, the `messages/get` fetch with no `mark_all`, and the Code-node body
> re-emit). This edits the two existing templates — **no new canonical workflow, the count stays 13.**

### Delete Orphan Profiles (scheduled sweep) — policy & gotchas

Covers the orphan-profile leaks the client can never settle (swipe-kill / iOS quit mid-wizard,
`profile/add` response lost in flight): hourly, lists ALL profiles (`GET /api/profile/all/get` +
`GET /tapi/profile/all/get`), tracks unauthorized ones in a **first-seen ledger in workflow
staticData** (Wappi exposes no creation timestamp — TTL runs from first observation, so a fresh
import grants every existing orphan the full 24h grace), then re-checks `get/status` per candidate
and POSTs `profile/delete`. Never deletes: authorized profiles, `deleted_at`-set entries, ambiguous
`authorized` flags, or profiles with `authorized_at`/`logouted_at` inside the TTL window.
`Sweep Config` node: `ttlHours` (24; values ≤ 0 coerce back to 24) / `dryRun` (reports
`wouldDelete` without deleting). Verified e2e on dev 2026-07-10: seeded the ledger 25h in the past
for two throwaway profiles → both deleted (WA + TG paths), the two live authorized profiles untouched.

- **`get/status` has NO `status:"done"` field** (unlike add/delete/list/all-get). Response validity =
  boolean `authorized` + `profile_id` echo match (the id match also guards Wappi's known
  concurrent-response crossing). Don't "fix" the verify predicate to check `status`.
- `profile/all/get` returns `profiles: null` (not `[]`) when the namespace is empty — handled.
- `is_subscribe` is `false` even on working authorized profiles and `last_activity` is often `0` —
  neither is usable in deletion policy.
- staticData persists only across **production** (scheduled) runs; manual runs read but never write it.
  The e2e trick: PUT seeded `staticData` via REST, then run manually with the real 24h TTL.
- The n8n MCP builder strips/rejects generic-auth (`httpHeaderAuth`) credentials on HTTP Request
  nodes — attach `WappiAuthToken` via the public REST API (`PUT /api/v1/workflows/{id}`) instead.
- **Prod pass**: recreate the WappiAuthToken credential and repoint the 4 HTTP nodes' credential id
  (dev id `ZowntFGvApDJ7UzQ`), import with fresh (empty) staticData, activate. Nothing else to wire —
  no webhook, no Supabase.

## Import / export (local DEV server, `~/.n8n`)

Stop the n8n server first (CLI talks to the SQLite DB directly).

```bash
# import the canonical set (ids preserved from each file's top-level "id")
n8n import:workflow --separate --input=Tools/n8n/workflows/

# re-export from the local server after editing in the UI (then re-inject ids if needed)
n8n export:workflow --backup --output=/tmp/n8n-export
```

## Known follow-ups before this is production-/dev-ready

1. **Credentials are not in these files** (referenced by id only). The local server has none yet —
   recreate WappiAuthToken, n8nAPIKey, OpenAi, Supabase, Cohere, Postgres before the workflows run.
2. **Create/Edit handlers hardcode `https://bagkz.app.n8n.cloud/api/v1/...`** for their clone/activate
   calls. For true local dev, point these at `http://localhost:5678/api/v1/...` + a local API key.
3. **`CreateWhatsappWorkflow`** has a trailing space in the `/activate ` URL — fix during the prod pass.
4. **Edit handlers** assume target node indices (`nodes[5]` is the AI agent) and have a `Set Bussiness Type`
   node-name typo + leftover unused credential refs — clean up during the prod pass.
