-- A5/A6 (audit 2026-09, stage 4): ONE history with explicit sources, real handoff requests, and the
-- per-chat handoff pause. Additive, idempotent. APPLY THROUGH the n8n Postgres credential.
--
-- chat_history_events: every message the business side can know about, keyed by the transport's
-- message id, with an explicit SOURCE — never "outgoing ⇒ AI". Written by: the bot templates
-- (client rows at claim time and on the «Вместе» gate — independent of generation; ai rows by
-- Record Send; owner_phone rows from the outgoing_message_phone webhook), the Wappi Proxy
-- (owner_app rows after an app send), and the outgoing_message_api webhook as a fallback
-- (outgoing_api, upgraded to ai/owner_app when the more specific writer lands).
create table if not exists chat_history_events (
  id           bigserial primary key,
  profile_id   text not null,
  chat_id      text not null,
  message_id   text not null,
  source       text not null check (source in ('client', 'ai', 'owner_app', 'owner_phone', 'outgoing_api')),
  body         text,
  msg_type     text,
  sent_at      timestamptz,
  execution_id text,
  created_at   timestamptz not null default now(),
  unique (profile_id, chat_id, message_id)
);
create index if not exists chat_history_events_chat_idx on chat_history_events (profile_id, chat_id, sent_at desc);

-- handoff_requests: the REAL «нужен человек» action — created by the agent's create_handoff tool
-- (once per triggering event: the unique key absorbs retries and re-deliveries), taken when the
-- owner opens the chat from the app, done when the owner explicitly returns the chat to «Авто».
-- The notification is a separate, retried delivery (notify_*) — its failure never loses the request.
create table if not exists handoff_requests (
  id                 bigserial primary key,
  profile_id         text not null,
  chat_id            text not null,
  source_message_id  text not null,
  reason             text not null check (reason in ('order', 'question', 'escalation')),
  status             text not null default 'open' check (status in ('open', 'taken', 'done')),
  data               jsonb not null default '{}'::jsonb,
  summary            text,
  created_by         text not null default 'ai',
  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now(),
  taken_at           timestamptz,
  done_at            timestamptz,
  notify_state       text not null default 'pending' check (notify_state in ('pending', 'sent', 'failed', 'skipped')),
  notify_attempts    int not null default 0,
  notify_last_error  text,
  notify_next_at     timestamptz not null default now(),
  notified_at        timestamptz,
  unique (profile_id, chat_id, source_message_id)
);
create index if not exists handoff_requests_open_idx on handoff_requests (profile_id, status, created_at desc);
create index if not exists handoff_requests_notify_idx on handoff_requests (notify_state, notify_next_at) where notify_state in ('pending', 'failed');

-- The handoff pause: a chat with a pending request does not get auto-replies until the owner acts
-- (Read Reply Mode ORs this flag with the mode). Cleared ONLY by an explicit owner action: the chat's
-- «Авто» toggle (Set Reply Mode, chat-scoped, suppressed=false) or `done` on the request.
alter table chat_reply_state add column if not exists handoff_paused boolean not null default false;

alter table public.chat_history_events enable row level security;
alter table public.handoff_requests enable row level security;
revoke all on table public.chat_history_events from anon, authenticated;
revoke all on table public.handoff_requests from anon, authenticated;
-- Post-checks: select relrowsecurity from pg_class where oid in ('public.chat_history_events'::regclass, 'public.handoff_requests'::regclass);
