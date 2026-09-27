-- R6 (iteration 2026-09-27): the chat's incoming generation counts NEWER MESSAGES, not claim arrivals.
-- Claim Event (apply-history-handoff.py CLAIM_SQL) bumps chat_reply_state.incoming_generation only when the
-- claimed event's Wappi message time is STRICTLY newer than the newest the chat has seen (or when either time
-- is unknown — the old always-bump rule), and keeps the newest time here. Before, a late OLDER message claimed
-- after a newer one bumped the generation and refused the newer run's lease, while Latest+Combine had already
-- stopped the older run: zero replies. Strict '>' on purpose — at an equal time only Wappi's order (read by
-- Latest+Combine / Final Check, never by the claim) knows which message is newer.
-- Additive + idempotent (nullable, NULL = unknown = old rule). APPLY BEFORE deploying the bot templates / clones:
-- the new Claim Event references the column, and a failing claim silences every reply (Claimed? = false).
alter table public.chat_reply_state add column if not exists newest_message_time bigint;

comment on column public.chat_reply_state.newest_message_time is
  'R6: unix seconds of the newest claimed incoming message (Wappi time); incoming_generation bumps only on a strictly newer one. NULL = unknown (always bump).';

-- Post-check (expect 1 row):
--   select column_name from information_schema.columns
--   where table_schema = 'public' and table_name = 'chat_reply_state' and column_name = 'newest_message_time';
