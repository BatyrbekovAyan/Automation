-- Supabase RAG store contract (project mnwsdbqvehrkeqwnwpqb), captured 2026-07-02.
-- This is the shape the n8n workflows depend on. It is NOT applied by any tool in
-- this repo — it documents what exists so the retrieval contract is version-controlled.
-- If you recreate the project, run this file first, then the harden-*.sql migration.

create extension if not exists vector with schema extensions;

-- Vector store written by Upload File (insert) and read by every bot clone
-- (retrieve-as-tool). embedding is vector(1536) == OpenAI text-embedding-3-small,
-- pinned on BOTH sides in the workflows; changing either breaks retrieval silently.
create table if not exists public.documents (
  id        bigserial primary key,
  content   text,
  metadata  jsonb,   -- { botWaId, botTgId, contentType, source, fileId }
  embedding extensions.vector(1536)
);

-- Chat memory written by the bots' memoryPostgresChat node (Postgres credential).
create table if not exists public.n8n_chat_histories (
  id         serial primary key,
  session_id varchar not null,
  message    jsonb not null
);

-- Similarity search invoked by the n8n Supabase Vector Store node via PostgREST RPC.
-- NOTE the filter semantics: multiple keys in `filter` are combined with OR, not AND
-- (custom implementation — the stock LangChain template uses `metadata @> filter`).
-- The bot templates send a single key ({ botWaId | botTgId : <workflow id> }), so
-- OR vs AND is currently equivalent; keep it single-key or fix this before relying
-- on multi-key filters.
CREATE OR REPLACE FUNCTION public.match_documents(query_embedding extensions.vector, filter jsonb DEFAULT '{}'::jsonb, match_count integer DEFAULT 5)
 RETURNS TABLE(id bigint, content text, metadata jsonb, similarity double precision)
 LANGUAGE plpgsql
AS $function$
declare
  k text;
  v text;
  conditions text := '';
  i int := 0;
begin
  -- Формируем OR-условия
  for k, v in
    select f.key, f.value
    from jsonb_each_text(filter) as f
  loop
    if i > 0 then
      conditions := conditions || ' OR ';
    end if;

    conditions := conditions || format(
      'metadata ->> %L = %L',
      k, v
    );

    i := i + 1;
  end loop;

  -- Без фильтров
  if conditions = '' then
    return query
    select
      d.id,
      d.content,
      d.metadata,
      1 - (d.embedding <=> query_embedding) as similarity
    from documents d
    order by d.embedding <=> query_embedding
    limit match_count;
  end if;

  -- С OR-фильтрами
  return query execute
    'select d.id, d.content, d.metadata,
            1 - (d.embedding <=> $1) as similarity
     from documents d
     where ' || conditions || '
     order by d.embedding <=> $1
     limit ' || match_count
  using query_embedding;

end;
$function$
