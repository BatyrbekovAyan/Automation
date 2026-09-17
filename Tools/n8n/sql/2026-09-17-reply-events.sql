-- A3/A4/A7 (audit 2026-09, stage 3): event registration + per-chat processing state for the bot pipeline.
-- Additive, idempotent. APPLY THROUGH the n8n Postgres credential (same DB as reply_mode_flags).
--
-- reply_events: ONE row per incoming Wappi event (profile, chat, message id). The INSERT ... ON CONFLICT
-- in the templates' `Claim Event` node is the atomic "right to process": a re-delivered event finds its
-- row and is refused (`duplicate`), unless the earlier claim is stale (>10 min in claimed/generated —
-- an n8n restart mid-flight) in which case the re-delivery may re-claim. `state` is the per-event
-- outcome ledger the Outcome stamp mirrors; send_unknown is a REAL state (transport accepted, HTTP
-- response lost, echo not found) and is never silently retried.
-- The LangChain Postgres memory table (created lazily by the bots' memory node on prod) — the claim
-- and cancel statements read it, so a fresh database gets the exact LangChain shape up front.
create table if not exists public.n8n_chat_histories (id serial primary key, session_id varchar(255) not null, message jsonb not null);

create table if not exists reply_events (
  profile_id            text not null,
  chat_id               text not null,
  message_id            text not null,
  received_at           timestamptz not null default now(),
  updated_at            timestamptz not null default now(),
  state                 text not null default 'claimed' check (state in (
                          'claimed','generated','send_pending','sent','send_failed','send_unknown',
                          'superseded','cancelled_owner','cancelled_mode','cancelled_busy','cancelled_fetch',
                          'generation_failed','quota_denied','suppressed')),
  execution_id          text,
  transport_message_id  text,
  error                 text,
  primary key (profile_id, chat_id, message_id)
);
create index if not exists reply_events_chat_idx on reply_events (profile_id, chat_id, received_at desc);

-- chat_reply_state: per-chat counters + a short send LEASE. incoming_generation bumps on every claimed
-- event, so a reply that started for generation N is refused at send time once a newer incoming has
-- been claimed (N+1) — the "final check" that A3 lacked. The lease (90 s) serialises the send of ONE
-- chat only; different chats never block each other.
create table if not exists chat_reply_state (
  profile_id            text not null,
  chat_id               text not null,
  incoming_generation   bigint not null default 0,
  lock_token            text,
  lock_expires_at       timestamptz,
  last_sent_at          timestamptz,
  last_sent_message_id  text,
  primary key (profile_id, chat_id)
);

alter table public.reply_events enable row level security;
alter table public.chat_reply_state enable row level security;
revoke all on table public.reply_events from anon, authenticated;
revoke all on table public.chat_reply_state from anon, authenticated;
-- Post-checks: select relrowsecurity from pg_class where oid in ('public.reply_events'::regclass, 'public.chat_reply_state'::regclass);
