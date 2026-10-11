-- V3 journal (T1, follow-ups 2026-10-11, M1 of docs/audits/2026-10-09-followups/F1F2-plan.md §2): the owner accepted
-- the first-bind limitation of the billing id (V3, 2026-10-11 — the first install that names a RevenueCat id holds it;
-- a later one gets u = NULL + billingConflict) on condition that support can SEE every refused install and revoke the
-- wrong one by the runbook (a1-deploy-runbook.md §5). Auth Session's `Log Billing Conflict` writes here AFTER the
-- refresh has been answered (never delaying or changing the answer): one row per (requested id, claimant install),
-- re-seen conflicts bump attempts + last_seen_at, the holder is the CURRENT one from billing_bindings (never taken
-- from the request). Bounded: a row needs an id another install actually holds; the client re-asks at most once a
-- minute (on return to the app). The old code never reads this table — rollback leaves it in place.
-- Additive + idempotent. Apply BEFORE deploying crAuthSession001 (without the table the journal only fails quietly:
-- continueRegularOutput, the answer is already sent).
-- TABLE-BEGIN (tests/test_billing_identity.py runs this block against a session temp table)
create table if not exists public.billing_conflicts (
  app_user_id         text not null,                   -- the requested RevenueCat id (bound to another install)
  claimant_install_id text not null,                   -- the install that named it and was refused
  holder_install_id   text not null,                   -- billing_bindings.install_id at the last refusal
  first_seen_at       timestamptz not null default now(),
  last_seen_at        timestamptz not null default now(),
  attempts            int not null default 1,
  primary key (app_user_id, claimant_install_id)
);
-- TABLE-END
create index if not exists billing_conflicts_last_seen_idx on public.billing_conflicts (last_seen_at desc);
alter table public.billing_conflicts enable row level security;
revoke all on table public.billing_conflicts from anon, authenticated;

-- Post-checks:
--   select relrowsecurity from pg_class where oid = 'public.billing_conflicts'::regclass;            -- expect t
--   select count(*) from information_schema.columns
--   where table_schema = 'public' and table_name = 'billing_conflicts';                              -- expect 6
--   select indexname from pg_indexes where tablename = 'billing_conflicts' order by 1;
--     -- expect billing_conflicts_last_seen_idx, billing_conflicts_pkey
