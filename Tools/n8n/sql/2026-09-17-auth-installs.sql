-- A1 (audit 2026-09, stage 1): server-side owner identity + ownership columns. Additive, idempotent.
-- APPLY THROUGH the n8n Postgres credential (table owner, exempt from non-FORCE RLS) — same DB as
-- 2026-08-21-billing-schema.sql. Nothing here changes existing rows; owner_id stays NULL for legacy
-- profiles until the owner claims them (Wappi Proxy `profile.claim`).

create table if not exists installs (
  install_id     text primary key,                 -- server-minted uuid
  secret_hash    text not null,                    -- sha256(install_secret), hex; the secret itself is never stored
  app_user_id    text,                             -- RevenueCat id linked by the client (billing key, NOT identity proof)
  platform       text,                             -- ios | android | editor (informational)
  app_version    text,
  created_at     timestamptz not null default now(),
  last_seen_at   timestamptz not null default now(),
  revoked_at     timestamptz,
  token_version  int not null default 1           -- bump = every outstanding access token dies at its next refresh
);
create index if not exists installs_app_user_idx on installs (app_user_id);

alter table bot_profiles add column if not exists owner_id    text;         -- installs.install_id
alter table bot_profiles add column if not exists workflow_id text;         -- the clone workflow this profile drives
alter table bot_profiles add column if not exists claimed_at  timestamptz;  -- when ownership was established
alter table bot_profiles add column if not exists claim_pending_by text;    -- install that started a legacy claim
alter table bot_profiles add column if not exists claim_pending_at timestamptz;
alter table bot_profiles add column if not exists claim_code_hash text;     -- sha256 of the self-message code
alter table bot_profiles add column if not exists claim_code_expires_at timestamptz;
create index if not exists bot_profiles_owner_idx on bot_profiles (owner_id) where deleted_at is null;
create index if not exists bot_profiles_workflow_idx on bot_profiles (workflow_id);

-- Price-list files: ownership registry (chunks carry only botWaId/botTgId; the stored original carries nothing).
create table if not exists uploaded_files (
  file_id      text primary key,
  owner_id     text,                               -- installs.install_id (NULL for legacy-mode uploads)
  bot_wa_id    text,
  bot_tg_id    text,
  content_type text,
  file_name    text,
  created_at   timestamptz not null default now(),
  deleted_at   timestamptz
);
create index if not exists uploaded_files_owner_idx on uploaded_files (owner_id);

-- Same default-deny posture as every other table the mobile app's anon key could otherwise reach.
alter table public.installs enable row level security;
alter table public.uploaded_files enable row level security;
revoke all on table public.installs from anon, authenticated;
revoke all on table public.uploaded_files from anon, authenticated;

-- Post-checks (expect true / 0 rows):
--   select relrowsecurity from pg_class where oid = 'public.installs'::regclass;
--   select column_name from information_schema.columns where table_name='bot_profiles' and column_name in ('owner_id','workflow_id');
