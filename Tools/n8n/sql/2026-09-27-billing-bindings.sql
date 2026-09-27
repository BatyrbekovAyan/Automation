-- R2 (iteration 2026-09-27): a RevenueCat billing id belongs to ONE install. Naming an id in bootstrap /
-- refresh no longer makes it the token's billing identity (u): Auth Session binds it only when it is a
-- RevenueCat anonymous id (`$RCAnonymousID:` + 32 hex — a device-id fallback is never bound) that is free,
-- already ours, or held by an install that is revoked or owns no live bot (recovery after a lost install
-- secret: claim the bots by code, then the id follows). Everyone else gets u = NULL + billingConflict.
-- Invariant kept by Create/Verify Install: installs.app_user_id is NULL or bound to that very install.
-- Additive + idempotent. Apply BEFORE deploying crAuthSession001 (it references the table); before that,
-- read-only check for duplicates the seed will resolve (the most recently seen live install wins):
--   select app_user_id, array_agg(install_id order by last_seen_at desc), count(*) from installs
--   where revoked_at is null and app_user_id is not null group by 1 having count(*) > 1;
create table if not exists public.billing_bindings (
  app_user_id text primary key,                      -- the RevenueCat anonymous id
  install_id  text not null,                         -- installs.install_id holding it
  bound_at    timestamptz not null default now()
);
create index if not exists billing_bindings_install_idx on public.billing_bindings (install_id);
alter table public.billing_bindings enable row level security;
revoke all on table public.billing_bindings from anon, authenticated;

-- SEED-BEGIN (tests/test_billing_identity.py runs this block verbatim against temp tables)
insert into public.billing_bindings (app_user_id, install_id, bound_at)
select distinct on (i.app_user_id) i.app_user_id, i.install_id, now()
from public.installs i
where i.revoked_at is null and length(i.app_user_id) = 47 and left(i.app_user_id, 15) = chr(36) || 'RCAnonymousID:'
  and translate(substr(i.app_user_id, 16), '0123456789abcdef', '') = ''
order by i.app_user_id, i.last_seen_at desc
on conflict (app_user_id) do nothing;
update public.installs i set app_user_id = null
where i.revoked_at is null and i.app_user_id is not null
  and not exists (select 1 from public.billing_bindings b where b.app_user_id = i.app_user_id and b.install_id = i.install_id);
-- SEED-END

-- Post-checks (expect 0 rows each):
--   select app_user_id from installs where revoked_at is null and app_user_id is not null group by 1 having count(*) > 1;
--   select i.install_id from installs i where i.revoked_at is null and i.app_user_id is not null
--     and not exists (select 1 from billing_bindings b where b.app_user_id = i.app_user_id and b.install_id = i.install_id);
