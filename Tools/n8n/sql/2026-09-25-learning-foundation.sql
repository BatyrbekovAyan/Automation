-- Learning loop, подпроект A «Фундамент» (2026-09-25): (1) метка происхождения отправки владельца
-- из приложения рядом с owner_app-строкой истории; (2) журнал показанных наборов панели «Вместе».
-- Обе — сырьё теневого счёта «карточка №1 совпала с отправленным» (критерий готовности «Авто», B).
-- Additive, idempotent. APPLY THROUGH cred `vvRrFiEXzLVqKjOx` (через .secrets/sql-oneoff.py по go владельца);
-- на staging — `Tools/n8n/staging/pg.sh psql -f`. Удаление: Delete Bot Files → Delete Chat Memory (CTE `sets`, линия бота)
-- (suggestion_sets по profile_id; колонки origin_* уходят вместе со строками chat_history_events);
-- retention 180 дней — Profile Lifecycle Sweep → Expire Learning Rows.

alter table chat_history_events
  add column if not exists origin_kind  text check (origin_kind in ('card', 'edited', 'typed', 'none')),
  add column if not exists origin_index smallint,
  add column if not exists origin_key   text,
  add column if not exists origin_edit  smallint check (origin_edit between 0 and 100),
  add column if not exists origin_top1  smallint check (origin_top1 in (0, 1)),
  add column if not exists origin_set   smallint;
create index if not exists chat_history_events_origin_idx
  on chat_history_events (profile_id, origin_key) where origin_key is not null;

create table if not exists suggestion_sets (
  request_key      text not null,            -- = requestKey клиента (FNV-1a64 hex, ≤32)
  profile_id       text not null,
  chat_id          text not null,
  tail_ts          bigint,                   -- ts последнего сообщения в payload (unix sec)
  business_type_id text,
  stage            text check (stage in ('learning', 'trained')),
  items            jsonb not null,           -- [{label, move, text}] ≤ 4
  prompt_version   text,
  usage            jsonb,                    -- {prompt_tokens, completion_tokens, …} из ответа OpenAI
  created_at       timestamptz not null default now(),
  primary key (profile_id, request_key)
);
create index if not exists suggestion_sets_age_idx on suggestion_sets (created_at);

alter table public.suggestion_sets enable row level security;
revoke all on table public.suggestion_sets from anon, authenticated;

-- Post-checks (expect: 6 rows; 1 row `true`; 0 rows):
--   select column_name from information_schema.columns where table_name = 'chat_history_events' and column_name like 'origin_%';
--   select tablename, rowsecurity from pg_tables where tablename = 'suggestion_sets';
--   select grantee, privilege_type from information_schema.role_table_grants
--     where table_name = 'suggestion_sets' and grantee in ('anon','authenticated');
