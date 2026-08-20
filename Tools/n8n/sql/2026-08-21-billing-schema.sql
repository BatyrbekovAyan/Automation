create table if not exists subscribers (
  app_user_id text primary key,
  plan text not null default 'trial' check (plan in ('trial','start','business','network','none')),
  status text not null default 'trialing' check (status in ('trialing','active','grace','expired')),
  trial_started_at timestamptz,
  current_period_end timestamptz,
  topup_balance int not null default 0,
  updated_at timestamptz not null default now()
);

create table if not exists bot_profiles (
  profile_id text primary key,
  app_user_id text not null,
  channel text not null check (channel in ('whatsapp','telegram')),
  created_at timestamptz not null default now(),
  deleted_at timestamptz
);
create index if not exists bot_profiles_owner_alive on bot_profiles (app_user_id) where deleted_at is null;

create table if not exists dialog_counts (
  app_user_id text not null,
  chat_id text not null,
  d date not null,
  primary key (app_user_id, chat_id, d)
);
create index if not exists dialog_counts_month on dialog_counts (app_user_id, d);
