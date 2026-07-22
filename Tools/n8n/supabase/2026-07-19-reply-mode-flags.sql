-- Reply-mode suppression flags for the semi-auto «Авто/Вместе» gate (2026-07-19).
-- Run once via a service-role/postgres connection. Idempotent: safe to re-run.
-- Default-deny RLS — no policies on purpose; the n8n Postgres credential is the
-- table owner and is exempt from non-FORCE RLS (mirrors
-- 2026-07-07-conversation-outcomes.sql). Only service_role/owner get through.
--
-- APPLY THROUGH cred `vvRrFiEXzLVqKjOx` (the Postgres credential the bot templates'
-- gate binds — the SAME DB the gate reads). Ground truth 2026-07-22: the dev instance
-- has a SINGLE Postgres cred `vvRrFiEXzLVqKjOx`; the old `1H5xlpFSESU4w6JH` id from the
-- C5 research/plan does NOT exist on the instance. Both ids always pointed at the same
-- Supabase DB (RESEARCH A3), so applying here is correct; if this table is created on the
-- wrong DB the gate cannot see it and every reply fails closed. (RESEARCH Pitfall 2 / A3.)

-- Suppression flag per (profile_id, chat_id). chat_id = '*' is the bot-wide
-- default row; a specific chat_id is a per-chat override. The gate resolves
-- precedence (override beats '*') and fails open on absence in a single
-- always-one-row coalesce query, so this table only stores explicit intent.
create table if not exists public.reply_mode_flags (
  profile_id  text        not null,
  chat_id     text        not null default '*',   -- '*' = bot-wide default row; a specific chat_id is a per-chat override
  suppressed  boolean     not null default false,  -- true = «Вместе» (suppress auto-reply)
  updated_at  timestamptz not null default now(),
  primary key (profile_id, chat_id)
);

-- Default-deny RLS: no policies, strip client-key roles (the anon key ships in
-- the mobile app). service_role (n8n Supabase cred) has bypassrls; the Postgres
-- cred is the owner — both unaffected. The app never touches this table directly;
-- only the server-side Postgres credential does (both the write webhook and the
-- read gate). The pk already covers (profile_id, chat_id) lookups — no extra index.
alter table public.reply_mode_flags enable row level security;
revoke all on table public.reply_mode_flags from anon, authenticated;

-- Post-checks (expect true):
--   select relrowsecurity from pg_class where oid = 'public.reply_mode_flags'::regclass;
--   select not has_table_privilege('anon', 'public.reply_mode_flags', 'select');
--   select count(*) >= 0 from public.reply_mode_flags;
