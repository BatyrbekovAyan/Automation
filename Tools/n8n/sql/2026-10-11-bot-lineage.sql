-- F1 + F2 «линия бота» (T2, follow-ups 2026-10-11, M2 of docs/audits/2026-10-09-followups/F1F2-plan.md §2): Create WA/TG
-- becomes «ensure exactly one bound clone for this channel of this bot». A new profile of the same bot (a re-pairing, a
-- reconnect after a purchase) gets a new clone, and ONE statement (Tools/n8n/clone_lineage.py BIND_WORKFLOW_SQL) moves
-- everything the bot had on the server onto it and books the replaced clone here BEFORE anything tries to delete it.
--   * bot_profiles.superseded_by / superseded_at — the profile that took this row's place (the bot's line is a chain of
--     profiles: memory and history are keyed by profile). A row that is bound again loses the mark.
--   * clone_retirements — every clone this system replaced (replaced), failed to discard after a failed Create
--     (discard_failed), took from a deleted bot's line (bot_deleted) or found unaccounted (inventory), until the sweep
--     confirms it gone (removed_at). The sweep never takes an id a LIVE bot_profiles row drives, nor a protected one.
--   * bot_profiles (owner_id, channel, bot_key) — the predecessor lookup of Clone Plan and the line of Delete Bot Files.
-- The old code never reads these columns or the table — rollback leaves them in place.
-- Additive + idempotent. Apply BEFORE deploying the Profile Lifecycle Sweep, Delete Bot Files and Create WA/TG that read
-- them (plan §6: M1 → M2 → workflows → M3).
-- TABLE-BEGIN (tests/test_clone_lineage_sql.py runs this block against session temp tables)
alter table public.bot_profiles add column if not exists superseded_by text;          -- the profile that replaced this row
alter table public.bot_profiles add column if not exists superseded_at timestamptz;
create table if not exists public.clone_retirements (
  workflow_id text primary key,                   -- the clone to delete
  profile_id  text,                               -- the profile it was bound to (audit)
  owner_id    text,                               -- the install whose Create / delete booked it (null: a legacy call)
  reason      text not null check (reason in ('replaced', 'discard_failed', 'bot_deleted', 'inventory')),
  replaced_by text,                               -- the clone that took its place (replaced)
  created_at  timestamptz not null default now(),
  removed_at  timestamptz,                        -- the sweep saw 200 or 404 on DELETE
  attempts    integer not null default 0,
  last_status text
);
create index if not exists clone_retirements_open on public.clone_retirements (created_at) where removed_at is null;
create index if not exists bot_profiles_lineage_idx on public.bot_profiles (owner_id, channel, bot_key);
alter table public.clone_retirements enable row level security;   -- like installs / uploaded_files: service role only
revoke all on table public.clone_retirements from anon, authenticated;
-- TABLE-END

-- Post-checks:
--   select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'bot_profiles'
--     and column_name in ('superseded_by', 'superseded_at');                                    -- expect 2
--   select relrowsecurity from pg_class where oid = 'public.clone_retirements'::regclass;      -- expect t
--   select count(*) from information_schema.columns
--   where table_schema = 'public' and table_name = 'clone_retirements';                       -- expect 9
--   select indexname from pg_indexes where indexname in ('clone_retirements_open', 'bot_profiles_lineage_idx')
--   order by 1;                                                     -- expect bot_profiles_lineage_idx, clone_retirements_open
