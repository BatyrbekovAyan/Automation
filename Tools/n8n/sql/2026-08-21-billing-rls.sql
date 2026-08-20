-- Billing schema RLS hardening, 2026-08-21. Run once via a service-role/postgres
-- connection. Idempotent: safe to re-run (ENABLE ROW LEVEL SECURITY and REVOKE
-- are both no-ops on an already-hardened table).
--
-- PRECONDITION (mirrors 2026-07-02-harden-rag-store.sql / 2026-07-19-reply-mode-flags.sql):
-- the n8n "Supabase" API credential uses the service_role key (bypassrls), and the
-- n8n "Postgres" credential connects as the postgres table owner (exempt from
-- non-FORCE RLS) — so nothing n8n does is affected by any of this; RLS gates
-- PostgREST, not n8n's owner-connection. What it closes: Supabase's PostgREST
-- grants anon/authenticated full CRUD on every new public table by default
-- (confirmed precedent: 2026-07-02-harden-rag-store.sql, "the anon key ... had
-- full CRUD on both tables"), and the anon key ships inside the mobile app. The
-- three tables below (subscribers, bot_profiles, dialog_counts — created by
-- 2026-08-21-billing-schema.sql / Task 6) hold billing-sensitive data
-- (plan/status/topup_balance) and were sitting exposed to that default grant
-- until this migration runs.
--
-- APPLY THROUGH cred `vvRrFiEXzLVqKjOx` (the SAME Postgres credential
-- 2026-08-21-billing-schema.sql used to create these tables — same DB).

-- 1. RLS default-deny: no policies on purpose — only service_role/owner get through.
alter table public.subscribers enable row level security;
alter table public.bot_profiles enable row level security;
alter table public.dialog_counts enable row level security;

-- 2. Strip client-key roles' table privileges entirely (the anon key ships in
--    the mobile app; the app never touches these tables directly — only the
--    server-side Postgres credential does, same as reply_mode_flags).
revoke all on table public.subscribers from anon, authenticated;
revoke all on table public.bot_profiles from anon, authenticated;
revoke all on table public.dialog_counts from anon, authenticated;

-- Post-checks (expect: 3 rows all `true`; 0 rows):
--   select tablename, rowsecurity from pg_tables where tablename in ('subscribers','bot_profiles','dialog_counts');
--   select grantee, table_name, privilege_type from information_schema.role_table_grants where table_name in ('subscribers','bot_profiles','dialog_counts') and grantee in ('anon','authenticated');
