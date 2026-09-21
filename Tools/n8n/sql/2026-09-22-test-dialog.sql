-- Stage 7.1: the safe test dialog's action ledger. A test run never creates a request, never notifies,
-- never sends and never counts a dialog — the DRY handoff tool records what the agent WOULD have done
-- here, keyed by the test run, and the Test Dialog workflow reads it back for the owner. Rows older than
-- a day are swept by the workflow itself. Additive, idempotent. APPLY THROUGH cred `Postgres`.
create table if not exists test_dialog_actions (
  id          bigserial primary key,
  run_id      text not null,
  action      text not null,
  args        jsonb not null default '{}'::jsonb,
  created_at  timestamptz not null default now()
);
create index if not exists test_dialog_actions_run_idx on test_dialog_actions (run_id);
alter table public.test_dialog_actions enable row level security;
revoke all on table public.test_dialog_actions from anon, authenticated;
