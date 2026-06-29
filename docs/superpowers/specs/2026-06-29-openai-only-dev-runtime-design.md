# OpenAI-only Dev Bot Runtime — Design

- **Date:** 2026-06-29
- **Status:** Approved (design)
- **Sub-project:** 2 of 3 in the "wire app to local n8n for development" effort
- **Depends on:** Sub-project 1 (switchable n8n endpoint + tunnel) — DONE & device-verified

## Background

With sub-project 1 done, creating a bot on-device now POSTs to local n8n, which clones a
per-bot runtime from a clone-source template and activates it. Incoming WhatsApp messages
already reach local via the tunnel. But the per-bot **runtime fails** because the template
uses a RAG stack — `Supabase Vector Store` + `Reranker Cohere` + `OpenAI Embedding` (tool)
and `Postgres Chat Memory` — none of which are configured locally (e.g.
`Credential ... does not exist for type openAiApi`, and the Supabase/Cohere/Postgres creds
are absent).

The user chose to run a **simplified OpenAI-only runtime for dev** rather than stand up
four external services. The per-bot template already bakes the bot's business / products /
services text into the AI Agent **system prompt** (via the Edit handler), so the Supabase
vector store only adds *uploaded-document* RAG — dropping it still lets the bot answer from
its prompt. Whisper transcription (`Transcribe Audio`) uses OpenAI, so voice still works.
Net: the dev runtime needs exactly **one** new credential, `OpenAi account`.

## Goal

A per-bot dev runtime that **replies using only OpenAI** (chat model + Whisper) with
in-memory conversation context, no Supabase/Cohere/Postgres — reproducibly derived from the
canonical prod templates, so a bot created on-device against local n8n actually answers.

### Definition of done
1. The 2 clone-source templates, as imported into local n8n, contain **no** Supabase /
   Cohere / Postgres / OpenAI-Embedding nodes, and the AI Agent has an in-memory window
   memory.
2. The only credentials the dev runtime references are `OpenAi account` and `WappiAuthToken`.
3. A bot created on-device (after recreating) **replies** to a WhatsApp message — its
   runtime execution status is `success`, and a reply is sent via Wappi.

## Template structure (current, from `4wYitz5ek30SVNlT-WhatsApp_Bot`)

AI Agent sub-connections:
- `OpenAI` (`lmChatOpenAi`, `openAiApi`) `--ai_languageModel-->` AI Agent — **KEEP**
- `Chat Memory` (`memoryPostgresChat`, `postgres`) `--ai_memory-->` AI Agent — **REMOVE**
- `Supabase Vector Store` (`vectorStoreSupabase`, `supabaseApi`) `--ai_tool-->` AI Agent — **REMOVE**
  - `Reranker Cohere` (`rerankerCohere`, `cohereApi`) `--ai_reranker-->` Supabase Vector Store — **REMOVE**
  - `OpenAI Embedding` (`embeddingsOpenAi`, `openAiApi`) `--ai_embedding-->` Supabase Vector Store — **REMOVE**

Main message path (`Webhook → If → Input type → Text/Audio → AI Agent → pacing → Wappi reply`),
`Transcribe Audio` (`openAi`/Whisper), and all Wappi HTTP nodes are **untouched**.

## Design

### Component 1 — `apply-dev-config.py`: `simplify_runtime()` step

A new function applied **only** to the two clone-source workflow ids
(`4wYitz5ek30SVNlT`, `4VN3gsFaC2HUYmcc`), after the existing URL/credential remap and
before writing to `Tools/n8n/workflows-local/`. Handlers and `Upload File` are **not**
touched (Upload File's Supabase vector store is its core purpose).

For each clone-source workflow:
1. **Identify** the RAG nodes by type (robust to renamed node titles):
   `@n8n/n8n-nodes-langchain.memoryPostgresChat`, `...vectorStoreSupabase`,
   `...rerankerCohere`, `...embeddingsOpenAi`. Collect their node names.
2. **Delete** those nodes from `nodes[]`.
3. **Prune connections**: remove any connection entry whose source is a deleted node, and
   within every remaining source's output branches remove links whose `node` targets a
   deleted node. (Covers `ai_memory`, `ai_tool`, `ai_reranker`, `ai_embedding`.)
4. **Inject** a Window Buffer Memory node:
   - type `@n8n/n8n-nodes-langchain.memoryBufferWindow` (exact type + params **confirmed
     via the n8n MCP `get_node_types` at implementation time**, not guessed),
   - reasonable `contextWindowLength` (target 10),
   - positioned near the old Chat Memory node,
   - wired with a new connection `<newNode> --ai_memory--> <AI Agent node name>`.
5. Idempotent: if the RAG nodes are already absent (re-run), skip deletion; ensure exactly
   one window-memory node exists (don't add a duplicate).

The function is pure JSON manipulation; the AI Agent remains valid with only
`ai_languageModel` + `ai_memory` connected (tools are optional in n8n).

### Component 2 — local OpenAI credential (user action)

User creates a credential named exactly **`OpenAi account`** (type OpenAI, `openAiApi`) in
the local n8n UI with their OpenAI API key. The script's existing credential-remap then maps
the template's `openAiApi` references (chat model + Whisper) to this local id by name.
`WappiAuthToken` already exists locally.

### Component 3 — deploy + recreate

1. Stop n8n → `N8N_PUBLIC_URL=<tunnel> python3 apply-dev-config.py` → re-derive
   `workflows-local/`.
2. `n8n import:workflow --separate --input=Tools/n8n/workflows-local/` (upserts the 2 clone
   sources; they stay inactive). Restart n8n.
3. **Bot 23**: delete it in the app and create a fresh bot, so the new per-bot workflow is
   cloned from the simplified template with the local `OpenAi account` cred id.

## Risks & edge cases

- **Connection pruning correctness**: the AI Agent's `ai_tool`/`ai_memory` links live under
  the *source* node's outputs (`Supabase Vector Store`, `Chat Memory`), and the reranker/
  embedding links live under *their* source nodes feeding Supabase — deleting by walking all
  sources + removing links to deleted targets covers both. Verify post-derive: zero
  Supabase/Cohere/Postgres/embeddings node types remain in the 2 clone sources.
- **AI Agent validity**: tools are optional; memory is optional but we add the window buffer.
  Validate the derived workflow with the n8n MCP `validate_workflow` before relying on it.
- **Scope guard**: only the 2 clone-source ids are simplified; `Upload File` keeps its
  Supabase vector store (its purpose). Assert the handlers/Upload File are byte-unchanged by
  `simplify_runtime` (it returns early for non-clone-source ids).
- **Canonical stays prod-faithful**: `Tools/n8n/workflows/` is never modified; simplification
  happens only in the gitignored `workflows-local/` output.
- **memoryBufferWindow node shape**: do NOT hardcode from memory — fetch the exact type
  string and parameter names via the n8n MCP `get_node_types` during implementation.

## Testing

- **Script unit checks** (run after `apply-dev-config.py`): in each derived clone source —
  zero nodes of the 4 RAG types; exactly one `memoryBufferWindow`; an `ai_memory` connection
  into the AI Agent; AI Agent still has `ai_languageModel`. Handlers/Upload File unchanged.
- **n8n validation**: `validate_workflow` (MCP) on each derived clone source → no errors.
- **Manual end-to-end** (definition of done): delete bot 23, create a new bot on-device,
  send it a WhatsApp message → confirm in local n8n the per-bot runtime execution is
  `success` and a reply is delivered via Wappi.

## Out of scope
- Real RAG (Supabase/Cohere/Postgres) on local — intentionally dropped for dev.
- Upload File / document ingestion locally (needs Supabase).
- The semi-auto **suggestions** workflow (sub-project 3).
