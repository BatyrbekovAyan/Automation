-- «Вместе» suggestions: per-account DAILY anti-abuse counter (Task 17a, owner decision
-- 2026-08-26 recorded in spec §5.3). Suggestions are FREE — they never consume a
-- `dialog_counts` row — but the /webhook/SuggestReplies endpoint is unauthenticated and
-- spends real LLM tokens per call, so it is gated by (a) the caller's subscription status
-- and (b) this counter.
--
-- Shape mirrors dialog_counts deliberately: text app_user_id + a DATE in the SAME
-- Asia/Almaty convention Count Dialog / Get Usage already use ((now() at time zone
-- 'Asia/Almaty')::date), so "today" means one thing across the whole billing surface.
-- Unlike dialog_counts, existence is NOT the count here — a day's row carries an integer
-- `n` bumped by the gate's own single statement (`on conflict do update set n = n + 1`),
-- because the cap is about REQUEST volume, not distinct chats.
--
-- Retention: rows are never deleted by any workflow. One row per (account, active day) is
-- ~40 bytes; a manual `delete from suggestion_counts where d < current_date - 90` is enough
-- if it ever matters. Rows are only ever created for accounts that PASS the subscription
-- check, so an unknown/expired id cannot grow this table.
--
-- APPLY THROUGH cred `vvRrFiEXzLVqKjOx` (the same Postgres credential every other billing
-- migration used — same DB). Idempotent: safe to re-run.

create table if not exists suggestion_counts (
  app_user_id text not null,
  d date not null,
  n int not null default 0,
  primary key (app_user_id, d)
);

-- RLS + grant hardening, same posture as 2026-08-21-billing-rls.sql: the anon key ships
-- inside the mobile app and PostgREST grants anon/authenticated full CRUD on every new
-- public table by default. n8n connects as the table owner (exempt from non-FORCE RLS),
-- so nothing server-side is affected.
alter table public.suggestion_counts enable row level security;
revoke all on table public.suggestion_counts from anon, authenticated;

-- Post-checks (expect: 1 row `true`; 0 rows):
--   select tablename, rowsecurity from pg_tables where tablename = 'suggestion_counts';
--   select grantee, privilege_type from information_schema.role_table_grants
--     where table_name = 'suggestion_counts' and grantee in ('anon','authenticated');
