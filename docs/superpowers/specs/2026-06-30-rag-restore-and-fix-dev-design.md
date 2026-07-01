# Restore + Fix RAG in Dev (promotable to Prod) — Design

- **Date:** 2026-06-30
- **Status:** Approved (design)
- **Supersedes (partly):** `2026-06-29-openai-only-dev-runtime-design.md` — that effort *stripped* RAG from the dev derivation so OpenAI-only bots could reply against a bare local n8n. We now restore RAG in dev (the owner will provision Supabase + Postgres) to test the real pipeline before promoting improvements to prod.

## Background

The per-bot bot templates use a RAG stack (Supabase vector store retrieval + Cohere reranker +
OpenAI embeddings, plus Postgres chat memory). For dev, `apply-dev-config.py:simplify_runtime()`
deletes that stack and injects an in-memory window buffer, so the only dev credential needed is
OpenAI. The owner now wants to **test real RAG end-to-end in dev** and **promote the validated
result to prod**, and the bots genuinely rely on uploaded documents (catalogs/menus/FAQs).

A multi-agent audit of the canonical workflows (read directly from the JSON) found the stack is
tutorial-grade but functional, with a few concrete defects worth fixing as part of the restore:

- **3 dead nodes** in Upload File (`Supabase Vector Store1`, `Embeddings OpenAI1`, `Data Loader1`)
  — verified orphans (no `main` inbound, output `[[]]`), plus a disabled AI-cleaner chain.
- **Broken chunking:** no text splitter anywhere; `Split into Chunks` only splits on `product[N]:`
  markers injected (only) on the PDF path by `Normalize PDF`. Any plain `.txt`/prose doc becomes
  ONE giant chunk → zero retrieval granularity. `Normalize PDF` also assumes every PDF line is a
  product, mangling prose PDFs.
- **Embeddings match only by accident:** insert and retrieve embeddings nodes both have `options:{}`
  → both silently default to `text-embedding-ada-002`/1536. The moment one side pins a model (or the
  Supabase column dimension differs) retrieval silently returns garbage with no error.
- **Unconfigured Cohere reranker:** `parameters:{}` (no model, no `topN`) with `topK:20`. Adds a
  Cohere round-trip every call but can't even shrink the context. Its credential is not justified.
- **`sessionKey` bug** (bot Chat Memory): `{{ from+to || to+from }}` — `+` binds tighter than `||`,
  so the `|| to+from` branch is dead code, and the key is not namespaced per bot → cross-bot memory
  leak on a shared Postgres.
- **Telegram webhook-path copy-paste bug** (`CreateTelegramWorkflow`): the cloned bot's webhook
  `path` is set from `body.WhatsappProfileId` while its `profile_id` and the Wappi callback URL use
  `TelegramProfileId` → inbound Telegram messages 404 unless the two ids coincide.

## Goal

Restore a **correct, professional** RAG pipeline in the canonical templates so that (a) the dev
derivation runs real RAG with exactly 3 credentials (OpenAI + Supabase + Postgres), and (b) the same
canonical files promote to prod with no rework or dev/prod drift.

### Definition of done

1. Canonical `workflows/` contain a working RAG pipeline with: no Cohere reranker, an explicit
   `text-embedding-3-small` embedding model on **both** insert and retrieve sides, a native
   `recursiveCharacterTextSplitter` doing the chunking, `topK:10`, a namespaced `sessionKey`, no dead
   nodes, and the Telegram webhook path derived from `TelegramProfileId`.
2. `apply-dev-config.py` no longer strips RAG (`simplify_runtime` removed); it only rewrites hosts and
   remaps credentials by name. Re-deriving produces dev workflows with full RAG referencing local
   `OpenAi account` / `Supabase` / `Postgres` credentials.
3. In dev: upload a `.txt` and a prose `.pdf`, message the bot, and confirm the runtime execution is
   `success`, the agent calls the Supabase tool, retrieved chunks are relevant, and a reply is sent
   via Wappi.

## Locked decisions

- **Approach A — single source of truth.** All fixes land in canonical `workflows/` (+ the derive
  script). Dev derives from canonical; prod is the same canonical re-imported to Cloud. No dev-only
  edits to the gitignored `workflows-local/`.
- **Remove the Cohere reranker from canonical entirely** (gone in dev *and* prod). A properly
  configured reranker (explicit model + `topN≈3-5`) can be reintroduced later as a measured upgrade.
- **Embedding model:** pin `text-embedding-3-small` (1536 dims) on both sides.
- **Chunking:** native `recursiveCharacterTextSplitter` (chunkSize ~1000, overlap ~150) attached to
  the Data Loader; retire the `Normalize PDF` + `Split into Chunks` product-marker hack.

## Changes by file

### 1. Bot templates — `4wYitz5ek30SVNlT-WhatsApp_Bot.json` and twin `4VN3gsFaC2HUYmcc-Telegram_Bot.json`

Apply identically to both (they are parameter-identical twins):

- **Delete** the `Reranker Cohere` node and its `ai_reranker` connection into `Supabase Vector Store`.
- On `Supabase Vector Store` (retrieve-as-tool): set `useReranker = false`; set `topK = 10`; rewrite
  the generic `toolDescription` to explicitly mention products, prices, and uploaded catalog/docs so
  the agent invokes the tool for those questions.
- On the retrieve-side `OpenAI Embedding` (`embeddingsOpenAi`): pin `model = text-embedding-3-small`.
- On `Chat Memory` (`memoryPostgresChat`): replace the buggy `{{ from+to || to+from }}` with a key
  **namespaced per bot** so a shared Postgres can't leak history across bots — target
  `{{ <bot-id> + ':' + $('Webhook').item.json.body.messages[0].from }}`, dead `||` removed,
  `sessionIdType = customKey` kept. Verify at build time which stable per-bot field is actually
  present on the inbound body for **both** WhatsApp and Telegram (e.g. `messages[0].profile_id`);
  if `profile_id` is absent on either shape, fall back to another stable per-bot identifier.

### 2. Upload File — `KoTuIlk4LMrlvnWI-Upload_File.json`

- **Delete** the orphan trio (`Supabase Vector Store1`, `Embeddings OpenAI1`, `Data Loader1`) and the
  disabled `Prepare AI Prompt → AI Cleaner → Extract Clean Text` chain.
- **Chunking rewire (target flow):**
  `… → Clean Text → Source Text (content = full cleaned text, ONE item) → Supabase Vector Store (insert)`.
  Remove `Normalize PDF` and `Split into Chunks`. The PDF branch becomes `Extract from PDF → Merge`
  (treated as prose). Attach a `recursiveCharacterTextSplitter`
  (`@n8n/n8n-nodes-langchain.textSplitterRecursiveCharacterTextSplitter`, chunkSize ~1000, overlap
  ~150) to the `Data Loader` via its custom text-splitting input; the loader chunks internally.
  Keep the loader's static metadata (`fileName`/`fileNameN` per-file key, `contentType`, `source`);
  per-chunk `chunkIndex`/`chunkCount` are no longer needed.
- On the insert-side `Embeddings OpenAI`: pin `model = text-embedding-3-small` (must equal the bot side).
- **Exact node type + parameter names for the text splitter and the loader's text-splitting option
  MUST be confirmed at build time via the n8n MCP `get_node_types`, not guessed.**

### 3. Create Telegram handler — `Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`

- In the Set node that builds the cloned bot's webhook `path`, change the value from
  `body.WhatsappProfileId` to `body.TelegramProfileId`.

### 4. `Tools/n8n/apply-dev-config.py`

- Remove `simplify_runtime()` and its call, plus the now-unused `CLONE_SOURCE_IDS`,
  `RAG_NODE_SUFFIXES`, `WINDOW_MEMORY_TYPE` constants and the `uuid` import if it becomes unused.
- Keep: Cloud→localhost API rewrite, tunnel webhook rewrite, `/activate ` typo fix, credential remap
  by name. Update the module docstring + the comment block at lines ~27-31 to reflect that dev now
  runs full RAG (no stripping) and requires local `OpenAi account` / `Supabase` / `Postgres` creds.

## Credentials (dev)

Created once in local n8n, matched by **name** by the remap step:

| Name (exact) | Type | Used by |
|---|---|---|
| `OpenAi account` | OpenAI | chat model, Whisper, both embeddings nodes |
| `Supabase` | Supabase API | vector store insert (Upload File) + retrieve (Bot) |
| `Postgres` | Postgres | bot Chat Memory |

`WappiAuthToken` and the n8n API header credential already exist locally. `CohereApi account` is no
longer referenced.

Confirm the Supabase `documents` table `embedding` column is `vector(1536)` (3-small is 1536-dim, so
the existing column shape works). The dev Supabase store starts empty, so re-uploading docs to test
is expected and there is no dev migration.

## Out of scope (deferred to the later "make it professional" pass)

Tracked for the next pass, intentionally **not** in this change to keep it focused and testable:

- Error handling: `retryOnFail` + timeouts on every Wappi/n8n HTTP and AI node; one shared Error
  Trigger workflow set as each workflow's Error Workflow.
- Prompt-injection hardening: validate/length-cap/fence the six business fields the Create/Edit
  handlers interpolate raw into the system prompt; neutralize literal `Additional Instructions:`.
- Tuning: `lmChat maxTokens 200→~500`; `contextWindowLength 50→~15`.
- Robustness: resolve `nodes[21]` (Upload metadata) and `nodes[5]` (Edit prompt) by node name/type
  instead of hardcoded index; inbound-message dedupe on `messages[0].id`; `Switch` default output.
- Cleanups: unused `n8nApi` + `httpBearerAuth` credential bindings on the 4 n8n-API HTTP nodes;
  `Return File Id` responding `{{$json.name}}` (never set → empty body); system-prompt typos
  (`Keep answers shot`, `relevent`).

## Risks & edge cases

- **Embedding parity is the #1 silent failure.** Pin `text-embedding-3-small` on BOTH nodes in the
  same change; never pin one side alone.
- **Prod promotion re-embed:** existing prod `documents` vectors were made with ada-002. Switching to
  3-small means re-uploading/re-embedding prod docs ONCE at promotion. (Dev store is fresh.)
- **Native splitter node shape:** do not hardcode the text-splitter type/params from memory — verify
  via n8n MCP `get_node_types` at build time, then `validate_workflow` the derived templates.
- **Twin parity:** the two bot templates must stay parameter-identical; apply every bot edit to both.
- **Clone-source ids stay inactive and unrenamed** (per CLAUDE.md): `4wYitz5ek30SVNlT` /
  `4VN3gsFaC2HUYmcc`. Editing their node contents is fine; their ids/active state are not touched.
- **Connection pruning correctness:** deleting Cohere + the dead trio + the chunker hack must also
  remove every dangling connection (both as a source key and inside other sources' output branches).

## Testing / validation

- **Static (post-edit, before deploy):** in each canonical template — zero `rerankerCohere` nodes;
  exactly one `embeddingsOpenAi` per side with `model=text-embedding-3-small`; one
  `recursiveCharacterTextSplitter` attached to the Data Loader; no orphan/dead nodes; `topK=10`;
  namespaced `sessionKey`; Telegram path = `TelegramProfileId`. Validate each with n8n MCP
  `validate_workflow`.
- **Derive check:** `python3 apply-dev-config.py` reports no missing credentials given the 3 local
  creds, and `workflows-local/` bot templates contain full RAG (no window-buffer injection).
- **End-to-end (definition of done):** import to local n8n, upload a `.txt` and a prose `.pdf`,
  message a bot → runtime execution `success`, Supabase tool invoked, relevant chunks retrieved, reply
  delivered via Wappi.

## Promotion to prod

After dev validation, import the same canonical `workflows/` files into n8n Cloud. The only prod-side
action beyond import is the one-time re-embed of existing documents (ada-002 → 3-small) and confirming
the Cloud Supabase `documents` column is `vector(1536)`.
