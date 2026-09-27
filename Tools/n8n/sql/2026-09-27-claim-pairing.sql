-- R1 (iteration 2026-09-27): a reauth claim binds a profile only for a pairing the CLAIMANT itself started
-- through the Wappi Proxy. The proxy's Record Claim Pairing stamps who started it and when; Bind Reauthed
-- Profile (Auth Session) requires claim_pair_by = the claiming install and an authorization after
-- claim_pair_at (within 30 minutes). Additive + idempotent; apply BEFORE deploying crAuthSession001 +
-- crWappiProxy0001 (both reference the columns).
alter table public.bot_profiles add column if not exists claim_pair_by text;
alter table public.bot_profiles add column if not exists claim_pair_at timestamptz;

-- Post-check (expect 2 rows):
--   select column_name from information_schema.columns
--   where table_schema = 'public' and table_name = 'bot_profiles' and column_name in ('claim_pair_by', 'claim_pair_at');
