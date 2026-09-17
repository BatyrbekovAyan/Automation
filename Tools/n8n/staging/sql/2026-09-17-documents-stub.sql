-- STAGING ONLY: the RAG chunk table lives in Supabase on prod (pgvector `documents` + the
-- `match_documents` RPC). The bot templates reach it through the Supabase credential (dev account
-- on staging), but Delete File / Delete Bot Files address it through the Postgres credential —
-- so staging needs the table's SHAPE for those deletes to run (id + metadata are all they read;
-- the embedding column is a plain array here because the staging role cannot install pgvector).
-- Never applied to prod.
create table if not exists public.documents (
  id        bigserial primary key,
  content   text,
  metadata  jsonb,
  embedding real[]
);
create index if not exists documents_metadata_idx on public.documents using gin (metadata);
