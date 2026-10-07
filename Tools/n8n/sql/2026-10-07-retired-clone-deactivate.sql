-- V12 (iteration 2026-09-28): the Profile Lifecycle Sweep retires a bot whose trial or subscription ended (it deletes the
-- Wappi profile and marks the bot_profiles row) but used to leave the bot's clone workflow ACTIVE. Its new clone branch
-- deactivates that clone — never deletes it: the owner's data stays stored until they delete the bot in the app or ask
-- support (owner decision 2026-10-07) — and records here WHICH workflow it has confirmed inactive, so a failed call is
-- retried on the next run and a deactivated one is never called again. A row the creating handlers revive with a NEW
-- clone (Bind Workflow Id overwrites workflow_id) is picked again by itself: the stamp no longer matches.
-- Additive + idempotent; apply BEFORE deploying fXYpCXPKw92EzRz8 (Retired Clones / Stamp Clone Deactivated read it).
alter table public.bot_profiles add column if not exists clone_deactivated_id text;

-- Post-check (expect 1 row):
--   select column_name from information_schema.columns
--   where table_schema = 'public' and table_name = 'bot_profiles' and column_name = 'clone_deactivated_id';
