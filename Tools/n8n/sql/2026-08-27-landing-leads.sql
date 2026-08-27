-- Landing lead intake (spec docs/superpowers/specs/2026-08-27-landing-page-design.md).
-- Applied to prod Supabase via one-off n8n harness on 2026-08-27.
create table if not exists landing_leads (
  id bigint generated always as identity primary key,
  name text,
  phone text not null,
  source text,
  user_agent text,
  created_at timestamptz not null default now()
);
alter table landing_leads enable row level security;
revoke all on landing_leads from anon, authenticated;
