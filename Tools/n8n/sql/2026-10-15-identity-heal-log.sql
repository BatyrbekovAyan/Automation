-- A3 «защита платящих» (2026-10-15, fix round 2; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
-- §3 A3): the identity-heal JOURNAL. A heal retires a paying identity and may silence another install's bots, so support
-- must SEE every one (the V3 precedent: billing_conflicts). /webhook/VerifyIdentity (crVerifyIdent001) writes:
--   * ONE row per heal (verdict 'healed') INSIDE the Heal statement — a CTE of the same statement, so one transaction with
--     the consolidation it records: a heal without its row, or a row without its heal, cannot exist. reason is NULL, the
--     counts are what this call changed (a repeat that moved nothing writes its row with zeros);
--   * ONE row per refusal RevenueCat's own alias list decided (rc_mismatch / hint_not_alias / no_aliases), AFTER the answer
--     (`Log Refusal`, best effort: never delays or changes the answer), reason = the refusal, counts 0 — a refusal never
--     writes subscribers or bot_profiles.
-- Both carry the client's HINT (final review M-5, 2026-10-15): the original id the app named (CustomerInfo's
-- OriginalAppUserId, as Validate accepted it — a RevenueCat anonymous id other than the recipient; NULL = none), so a
-- hint_not_alias row shows WHICH id RevenueCat did not confirm, and a heal row shows what the client saw.
-- A RevenueCat outage (502 rc_unavailable) and the gate's refusals (400/401/403) write nothing here — they are visible as
-- status codes. No retention: the client calls at most once per pair per 24 h (IdentityHealLatch).
-- Additive + idempotent. Apply BEFORE importing crVerifyIdent001: without the table the Heal statement fails (503
-- db_unavailable — nothing consolidated) and Log Refusal fails silently.
-- TABLE-BEGIN
create table if not exists public.identity_heal_log (
  id             bigserial primary key,
  install_id     text not null,                 -- the token's install (crAuth.installId)
  to_app_user_id text not null,                 -- the recipient: the token's billing id (crAuth.appUserId)
  aliases        text[] not null,               -- RevenueCat's ids minus the recipient (consolidated, or refused)
  consolidated   int not null,                  -- alias subscriber rows this call retired
  rekeyed        int not null,                  -- the install's live bot_profiles rows this call re-keyed
  healed_at      timestamptz not null default now(),   -- when the call ran (a refusal's time, for a refusal row)
  reason         text,                          -- NULL = healed; else rc_mismatch | hint_not_alias | no_aliases
  hint           text                           -- the client's original id (Validate's; NULL = none or the recipient itself)
);
create index if not exists identity_heal_log_to_idx on public.identity_heal_log (to_app_user_id, healed_at desc);
-- TABLE-END
-- A cluster that applied this file before the final review (2026-10-15; staging only — never deployed) gains the hint
-- column here; a no-op everywhere else.
alter table public.identity_heal_log add column if not exists hint text;
-- RLS default-deny (no policies) + no client-key grants, as sql/2026-08-21-billing-rls.sql: only the server-side Postgres
-- credential (table owner) reads and writes it; support reads it through .secrets/sql-oneoff.py.
alter table public.identity_heal_log enable row level security;
revoke all on table public.identity_heal_log from anon, authenticated;
revoke all on sequence public.identity_heal_log_id_seq from anon, authenticated;

-- Post-checks:
--   \d public.identity_heal_log                                                        -- the nine columns above
--   select relrowsecurity from pg_class where oid = 'public.identity_heal_log'::regclass;              -- expect t
--   select grantee, privilege_type from information_schema.role_table_grants
--   where table_name = 'identity_heal_log' and grantee in ('anon', 'authenticated');                   -- expect 0 rows
-- Support (read-only):
--   select id, healed_at, install_id, to_app_user_id, aliases, hint, consolidated, rekeyed, coalesce(reason, 'healed') as outcome
--   from public.identity_heal_log order by healed_at desc limit 20;
