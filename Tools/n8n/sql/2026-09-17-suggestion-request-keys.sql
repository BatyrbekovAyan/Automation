-- S3 (audit stage 5): a suggestion request that times out (server deadline) or is retried by
-- the client must not be COUNTED twice against the daily cap. The gate keys each count by a
-- request key the client derives from (chat, tail message, steer); the same key within 10 min
-- is served without a second increment. Additive, idempotent. APPLY THROUGH cred `Postgres`.
create table if not exists suggestion_request_keys (
  app_user_id  text not null,
  request_key  text not null,
  counted_at   timestamptz not null default now(),
  primary key (app_user_id, request_key)
);
create index if not exists suggestion_request_keys_age_idx on suggestion_request_keys (counted_at);
alter table public.suggestion_request_keys enable row level security;
revoke all on table public.suggestion_request_keys from anon, authenticated;
-- Retention: `delete from suggestion_request_keys where counted_at < now() - interval '1 day'` (any sweep).
