-- «Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §3.2, §3.3, §4.1,
-- §4.3, §4.6; plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 2): a chat is PERSONAL (the assistant does
-- not answer, suggest or store anything) or BUSINESS (everything as before). The server decides ONCE, on the first
-- incoming message of a chat that has no decision yet, and writes it down; afterwards only the owner changes it. The
-- statements over these tables live in Tools/n8n/audience.py (pinned by tests/test_audience_sql.py) — except the bot's
-- line: the move on a reconnect is in Tools/n8n/clone_lineage.py (Create's Bind Workflow Id), the delete with the bot in
-- Tools/n8n/apply-history-handoff.py (Delete Bot Files' Delete Chat Memory); pinned by tests/test_clone_lineage_sql.py
-- and tests/test_retired_delete.py.
--
-- chat_audience — one row per (Wappi profile, chat key = the webhook's `from`):
--   kind            personal | business;
--   source          default (computed by the rule, Decide Audience) | owner (the owner's own decision, ChatAudience);
--   reason          nonhuman | contact | history | new (computed) | owner;
--   version         the owner's desired-state version (the client's ms clock): an owner write applies only over an OLDER
--                   one (the reply_mode_flags / bot_schedule guard); computed rows keep 0;
--   decided_at      when the row's decision was taken: a computed decision HOLDS only while decided_at >= the
--                   profile's connected_at (another number restarts the connection and voids the older computed rows
--                   without touching them); the owner's decision always holds (audience.VALID_SQL);
--   personal_since  when the OWNER made the chat personal (the 2-day wipe of the Profile Lifecycle Sweep counts from it);
--   wiped_at        when that wipe ran (the partial index below is the wipe's work list).
-- audience_connections — one row per profile: the moment its channel counts as connected (the earliest created_at of the
--   bot's line, or now() when another number was connected) and the connected number's digits (null = not known yet).
-- reply_events.state gains 'cancelled_personal' (a reply in preparation is cancelled when its chat turned personal).
--
-- Both tables move with the bot's line on a reconnect (clone_lineage.MOVED_TABLES, the bind in clone_lineage.py) and are
-- deleted with the bot (Delete Bot Files, Delete Chat Memory in apply-history-handoff.py). Additive + idempotent:
-- re-running this file changes nothing (the CHECK is rebuilt only while it lacks 'cancelled_personal'). Apply BEFORE the
-- workflows that read the tables: the templates' Read Reply Mode is fail-closed by design — without the tables every
-- incoming message would fail.
-- TABLE-BEGIN
create table if not exists public.chat_audience (
  profile_id     text not null,                    -- the Wappi profile (a bot_profiles row's id)
  chat_id        text not null,                    -- the chat key: the incoming webhook's `from`
  kind           text not null check (kind in ('personal', 'business')),
  source         text not null check (source in ('default', 'owner')),
  reason         text not null check (reason in ('nonhuman', 'owner', 'contact', 'history', 'new')),
  version        bigint not null default 0,
  decided_at     timestamptz not null default now(),
  personal_since timestamptz,
  wiped_at       timestamptz,
  updated_at     timestamptz not null default now(),
  primary key (profile_id, chat_id)
);
create index if not exists chat_audience_wipe_idx on public.chat_audience (personal_since)
  where source = 'owner' and kind = 'personal' and wiped_at is null;
create table if not exists public.audience_connections (
  profile_id   text primary key,                   -- the Wappi profile
  phone        text,                               -- the connected number's digits (null = not known yet)
  connected_at timestamptz not null,
  updated_at   timestamptz not null default now()
);
-- TABLE-END

-- The reply ledger learns the new refusal. Rebuilt only while the CHECK lacks it, so a re-run (pg.sh migrate re-applies
-- every file) is a no-op and a later migration that widens the list again is never narrowed back. The old list is the
-- one of sql/2026-09-17-reply-events.sql (the CHECK's name is the one Postgres gave that inline column check). Every
-- existing row already satisfies the wider list; the validation scan runs under the table lock (milliseconds).
do $do$
begin
  if not exists (select 1 from pg_catalog.pg_constraint
                 where conrelid = 'public.reply_events'::regclass and conname = 'reply_events_state_check'
                   and pg_catalog.pg_get_constraintdef(oid) like '%''cancelled_personal''%') then
    alter table public.reply_events drop constraint if exists reply_events_state_check;
    alter table public.reply_events add constraint reply_events_state_check check (state in (
      'claimed','generated','send_pending','sent','send_failed','send_unknown',
      'superseded','cancelled_owner','cancelled_mode','cancelled_busy','cancelled_fetch',
      'generation_failed','quota_denied','suppressed','cancelled_personal'));
  end if;
end
$do$;

-- RLS default-deny (no policies) + no client-key grants, as sql/2026-08-21-billing-rls.sql: only the server-side Postgres
-- credential (table owner) reads and writes them.
alter table public.chat_audience enable row level security;
alter table public.audience_connections enable row level security;
revoke all on table public.chat_audience from anon, authenticated;
revoke all on table public.audience_connections from anon, authenticated;

-- Post-checks:
--   \d public.chat_audience                                                          -- the ten columns above
--   \d public.audience_connections                                                   -- the four columns above
--   select indexdef from pg_indexes where indexname = 'chat_audience_wipe_idx';     -- (personal_since) WHERE source = 'owner' …
--   select relname, relrowsecurity from pg_class
--   where oid in ('public.chat_audience'::regclass, 'public.audience_connections'::regclass);       -- expect t, t
--   select grantee, privilege_type from information_schema.role_table_grants
--   where table_name in ('chat_audience', 'audience_connections') and grantee in ('anon', 'authenticated');   -- expect 0 rows
--   select pg_get_constraintdef(oid) from pg_constraint
--   where conrelid = 'public.reply_events'::regclass and conname = 'reply_events_state_check';      -- … 'cancelled_personal'
-- Reverse — only once no deployed workflow reads the tables (the templates' Read Reply Mode and Acquire Send Lease, Decide
-- Audience, ChatAudience, the proxy's Record Owner Send, Suggest Replies, Dashboard Outcomes, Create's bind, Delete Bot
-- Files, the Sweep) and no reply_events row carries 'cancelled_personal':
--   drop table public.chat_audience; drop table public.audience_connections;
--   alter table public.reply_events drop constraint reply_events_state_check;
--   alter table public.reply_events add constraint reply_events_state_check check (state in (
--     'claimed','generated','send_pending','sent','send_failed','send_unknown',
--     'superseded','cancelled_owner','cancelled_mode','cancelled_busy','cancelled_fetch',
--     'generation_failed','quota_denied','suppressed'));
