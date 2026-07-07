-- Conversation outcomes for the «Сводка» dashboard (2026-07-07).
-- Run once in the Supabase SQL editor (or a service-role/postgres connection).
-- Idempotent: safe to re-run. Mirrors the default-deny RLS of
-- 2026-07-02-harden-rag-store.sql — no policies on purpose, only
-- service_role/owner get through (the n8n Postgres credential is the table
-- owner and is exempt from non-FORCE RLS).

-- 1. n8n_chat_histories has no timestamps (LangChain memory writes id/session_id/
--    message only). Add created_at so the dashboard can bucket by time and apply
--    the 12h silence rule. The memory node inserts without naming columns, so the
--    default applies to every new row. Pre-existing rows share the migration
--    timestamp (acceptable — only skews last_message_at for old sessions, ages out).
alter table public.n8n_chat_histories
  add column if not exists created_at timestamptz not null default now();

-- 2. Outcome per conversation. session_id == '<profile_id>:<chat_id>' (the bots'
--    memory sessionKey). last_history_id is the watermark into n8n_chat_histories.
create table if not exists public.conversation_outcomes (
  session_id      text primary key,
  profile_id      text not null,
  chat_id         text not null,
  outcome         text not null check (outcome in
    ('order_collected','owner_needed','in_dialog','client_silent','question_closed')),
  summary         text not null default '',
  last_history_id bigint not null,
  last_message_at timestamptz not null,
  outcome_at      timestamptz not null,
  updated_at      timestamptz not null default now()
);

create index if not exists conversation_outcomes_profile_idx
  on public.conversation_outcomes (profile_id);

-- 3. Default-deny RLS: no policies, strip client-key roles (the anon key ships in
--    the mobile app). service_role (n8n Supabase cred) has bypassrls; the Postgres
--    cred is the owner — both unaffected.
alter table public.conversation_outcomes enable row level security;
revoke all on table public.conversation_outcomes from anon, authenticated;

-- Post-checks (expect true):
--   select relrowsecurity from pg_class where oid = 'public.conversation_outcomes'::regclass;
--   select not has_table_privilege('anon', 'public.conversation_outcomes', 'select');
--   select column_name from information_schema.columns
--     where table_name = 'n8n_chat_histories' and column_name = 'created_at';
