-- V2 (iteration 2026-09-28): the code sent into the bot's own chat is now the ONLY proof that binds a profile to an
-- install (a re-pairing never binds — Wappi does not say which link session completed it), so the code is guarded:
-- Verify Claim Code counts the pending install's wrong answers and the fifth kills the code (hash cleared, expiry
-- kept, so no new code is sent until its 10-minute window ends); Store Claim Code resets the counter for a new code.
-- Additive + idempotent; apply BEFORE deploying crAuthSession001 (Claim Start / Store / Verify / Clear Claim Code
-- reference the column).
alter table public.bot_profiles add column if not exists claim_code_attempts int not null default 0;

-- Post-check (expect 1 row):
--   select column_name from information_schema.columns
--   where table_schema = 'public' and table_name = 'bot_profiles' and column_name = 'claim_code_attempts';
