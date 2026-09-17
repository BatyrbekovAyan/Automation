-- A2 (audit 2026-09, stage 2): desired-state VERSION on the reply-mode rows. Additive, idempotent.
-- The Set Reply Mode upsert applies a row only when the stored version is OLDER than the request's,
-- so a delayed retry or a reordered response can never overwrite a newer desire; legacy clients
-- (no version in the body) get a server-assigned "now" and keep last-arrival semantics.
-- The gate's read (Read Reply Mode: coalesce(...) absence -> false) is UNCHANGED — absence still means
-- «reply», exactly as before; new profiles get an explicit initial row from the Create handlers instead.
alter table public.reply_mode_flags add column if not exists version bigint not null default 0;
-- Post-check: select column_name from information_schema.columns where table_name='reply_mode_flags' and column_name='version';
