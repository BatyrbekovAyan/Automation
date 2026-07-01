-- RAG store hardening, 2026-07-02. Run once in the Supabase SQL editor (or via a
-- read-write MCP connection). Safe re-run: every statement is idempotent or a no-op
-- on second application.
--
-- PRECONDITION (confirmed 2026-07-02): the n8n "Supabase" API credential uses the
-- service_role key. service_role has bypassrls, and the n8n "Postgres" credential
-- connects as the postgres table owner (exempt from non-FORCE RLS) — so nothing
-- n8n does is affected by any of this. What it closes: the anon key ships inside
-- the mobile app, and before this migration it had full CRUD on both tables
-- (cross-tenant read/write of catalogs and chat memory at the DB layer).

-- 1. RLS default-deny: no policies on purpose — only service_role/owner get through.
alter table public.documents enable row level security;
alter table public.n8n_chat_histories enable row level security;

-- 2. Strip client-key roles' table privileges entirely.
revoke all on table public.documents from anon, authenticated;
revoke all on table public.n8n_chat_histories from anon, authenticated;

-- 3. match_documents: remove the default PUBLIC execute so client keys cannot
--    probe the vector store via RPC. service_role needs an explicit grant back:
--    it is not the function owner, and bypassrls does not cover EXECUTE.
revoke execute on function public.match_documents(extensions.vector, jsonb, integer) from public, anon, authenticated;
grant execute on function public.match_documents(extensions.vector, jsonb, integer) to service_role;

-- 4. ANN index: match_documents orders by cosine distance (<=>). Without this,
--    every similarity search is a sequential scan (O(n) at catalog scale).
--    HNSW needs no training rows and suits incremental inserts.
create index if not exists documents_embedding_hnsw_idx
  on public.documents using hnsw (embedding extensions.vector_cosine_ops);

-- 5. Metadata lookups used by per-bot retrieval filters and per-file delete.
create index if not exists documents_metadata_bot_wa_idx on public.documents ((metadata->>'botWaId'));
create index if not exists documents_metadata_bot_tg_idx on public.documents ((metadata->>'botTgId'));
create index if not exists documents_metadata_file_id_idx on public.documents ((metadata->>'fileId'));

-- 6. Pin search_path (clears the function_search_path_mutable advisor): the
--    function body references unqualified `documents` and pgvector's <=> operator.
alter function public.match_documents(extensions.vector, jsonb, integer)
  set search_path = public, extensions;

-- Post-checks (all should return true / rows):
--   select relrowsecurity from pg_class where oid in ('public.documents'::regclass, 'public.n8n_chat_histories'::regclass);
--   select not has_table_privilege('anon', 'public.documents', 'select');
--   select not has_function_privilege('anon', 'public.match_documents(extensions.vector, jsonb, integer)', 'execute');
--   select has_function_privilege('service_role', 'public.match_documents(extensions.vector, jsonb, integer)', 'execute');
--   select indexname from pg_indexes where tablename = 'documents';
