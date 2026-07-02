# n8n workflows

Canonical n8n workflows for the BAGKZ app. The app talks to n8n for bot creation,
prompt editing, RAG file upload/delete, and (in progress) live reply suggestions.

## Layout

- `workflows/` — **committed source of truth**: the 8 workflows the app actually depends on.
  Each JSON has its original n8n `id` injected at the top level so it round-trips on import.
- `supabase/` — the RAG store's DB contract: `schema.sql` (documents table +
  `match_documents` as deployed — note its multi-key filter uses OR semantics), the
  applied hardening migrations (RLS default-deny, anon revoke, HNSW + metadata indexes),
  and the `price-lists` originals bucket (`2026-07-02-price-list-originals-bucket.sql` —
  must be applied once per Supabase project before the Store Original File node works).
- `apply-*.py` — idempotent migrations over `workflows/` (edit by node name, re-runnable);
  `verify_rag.py` asserts every applied invariant; `test-upload-e2e.sh` exercises the
  Upload/Delete webhooks end-to-end against a live instance (curl mimicking Unity's
  WWWForm binary-part quirk).
- `reference/` — **gitignored**: downloaded community/marketplace templates + n8n onboarding
  samples, kept only to mine for ideas. Not part of the app, never imported.

## The 8 canonical workflows

| id | name | role |
|----|------|------|
| `XuvOp7TxOImOAmlj` | CreateWhatsappWorkflow | App webhook `/webhook/CreateWhatsappWorkflow` — clones the WhatsApp template per bot |
| `Uz6HBBUpAiUqVysB` | CreateTelegramWorkflow | App webhook `/webhook/CreateTelegramWorkflow` — clones the Telegram template per bot |
| `3qax5J9u2qsT9Vao` | Edit Whatsapp Workflow | App webhook `/webhook/EditWhatsappWorkflow` — edits a bot's system prompt |
| `TwWPW3gIyjZS3foR` | Edit Telegram Workflow | App webhook `/webhook/EditTelegramWorkflow` — edits a bot's system prompt |
| `KoTuIlk4LMrlvnWI` | Upload File | App webhook `UploadFile` — ingests files into the Supabase vector store; stamps `botWaId`/`botTgId`/`fileId` on every chunk; extension routing is case-insensitive; archives the uploaded bytes to Storage `price-lists/{fileId}` (dead-end branch, `onError: continue` — never fails the upload); unsupported types get an explicit 415 |
| `ZTqpumOpL1rNDOp6` | Delete File | App webhook `DeleteFile` — body `{ fileId }`; deletes that file's chunks from `documents` AND its stored original `price-lists/{fileId}` (404 tolerated for pre-bucket files), returns `{ success, deletedChunks }` |
| `4wYitz5ek30SVNlT` | WhatsApp Bot | **Clone source** for every WhatsApp bot (referenced by literal id in CreateWhatsappWorkflow); retrieval self-scoped by `botWaId = {{ $workflow.id }}` |
| `4VN3gsFaC2HUYmcc` | Telegram Bot | **Clone source** for every Telegram bot (referenced by literal id in CreateTelegramWorkflow); retrieval self-scoped by `botTgId = {{ $workflow.id }}` |

> ⚠️ `4wYitz5ek30SVNlT` and `4VN3gsFaC2HUYmcc` are referenced by **literal id** inside the
> two Create handlers. Never change their ids, or bot creation 404s on the clone step.
> Keep both **inactive** — they share webhook path `0091024b-7b46` and only the per-bot
> clones (with rewritten paths) ever go active.

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
