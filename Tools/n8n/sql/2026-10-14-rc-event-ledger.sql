-- A1 «защита платящих» (2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
-- §3 A1): the idempotency ledger of the RevenueCat webhook. RevenueCat delivers at least once — an answer it did not
-- receive (timeout, proxy) is delivered again minutes later — and Upsert Subscriber's `topup_balance + <the event's
-- top-up>` is not idempotent: a replay of a committed NON_RENEWING_PURCHASE credited 500 dialogs twice.
-- RevenueCat Events (ZGYr6srzS3rSSXHp):
--   * READS this table first (`Dedup Gate`, read-only, fail-open: a read error lets the event through) — an event id
--     found here is acknowledged with 200 and changes nothing;
--   * WRITES it only inside Upsert Subscriber's own statement (CTE `ledger`) — one transaction with the event's
--     effect: a row written earlier would, on a failure between the two, turn RevenueCat's retry into a «duplicate»
--     and lose the event.
-- An event without an id passes without a row; the branches without Upsert (TRANSFER, CANCELLATION / no-op) write
-- nothing. The name `rc_events` is reserved (docs/gtm/2026-09-22-acquisition-agent-decision.md). No retention: one
-- row per RevenueCat event (a handful per subscriber per month).
-- Additive + idempotent. Apply BEFORE deploying RevenueCat Events: without the table the gate only fails open, but
-- Upsert Subscriber's CTE fails — every event then answers 500 and RevenueCat retries until the table exists.
-- TABLE-BEGIN
create table if not exists public.rc_event_ledger (
  event_id    text primary key,                   -- RevenueCat event.id
  received_at timestamptz not null default now()
);
-- TABLE-END
-- RLS default-deny (no policies) + no client-key grants, as sql/2026-08-21-billing-rls.sql: only the server-side
-- Postgres credential (table owner) reads and writes it.
alter table public.rc_event_ledger enable row level security;
revoke all on table public.rc_event_ledger from anon, authenticated;

-- Post-checks:
--   \d public.rc_event_ledger                                                        -- event_id text PK, received_at not null default now()
--   select relrowsecurity from pg_class where oid = 'public.rc_event_ledger'::regclass;               -- expect t
--   select grantee, privilege_type from information_schema.role_table_grants
--   where table_name = 'rc_event_ledger' and grantee in ('anon', 'authenticated');                    -- expect 0 rows
