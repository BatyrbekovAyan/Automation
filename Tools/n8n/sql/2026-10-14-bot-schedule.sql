-- B1 «расписание бота» (2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
-- §4 B1, §1): the owner's working hours of a bot, per Wappi profile. By default a bot has NO row (no schedule). With an
-- ENABLED row, a message that arrives outside the window is handled exactly as in «Вместе» — no auto-reply, the event is
-- not claimed, no quota is spent, the history and the memory still learn it (Record Suppressed) — because the bot
-- templates' Read Reply Mode ORs «the window is closed» into its `suppressed` (Tools/n8n/bot_schedule.py, one predicate;
-- the lease never reads it). Written only by crSetSchedule001 «Set Schedule» (POST /webhook/SetSchedule, one
-- version-guarded upsert); moved with the bot's line on a reconnect (clone_lineage.MOVED_TABLES) and deleted with the
-- bot (Delete Bot Files, `deletedSchedules`).
--   days_mask  bit 0 = Monday … bit 6 = Sunday (0 = never open: an enabled row with no day is always «outside»);
--   start_min / end_min  minutes of the bot's local day: start = end → 24 h; start < end → [start, end);
--              start > end → across midnight, [start, 1440) ∪ [0, end), decided by the day the window STARTED;
--   tz         the bot's IANA zone ({bot}TimeZone on the phone); the column default Asia/Almaty is the spec's fallback
--              zone (Set Schedule itself requires one) — checked against pg_timezone_names by the trigger below (a zone
--              Postgres does not know would make the mode read throw at every message, i.e. silence the bot);
--   version    the client's desired-state version (ms); a write applies only over an OLDER stored version.
-- Additive + idempotent (re-runnable). Apply BEFORE importing crSetSchedule001 and BEFORE the bot templates that read
-- it: their Read Reply Mode is fail-closed by design (no onError) — without the table every incoming message fails.
-- TABLE-BEGIN
create table if not exists public.bot_schedule (
  profile_id text primary key,                                           -- the Wappi profile (a bot_profiles row's id)
  enabled    boolean not null,
  days_mask  int not null check (days_mask between 0 and 127),
  start_min  int not null check (start_min between 0 and 1439),
  end_min    int not null check (end_min between 0 and 1439),
  tz         text not null default 'Asia/Almaty',
  version    bigint not null default 0,
  updated_at timestamptz
);
-- TABLE-END

-- The zone is checked when one is WRITTEN — an insert, or an update that names tz (Set Schedule's upsert does both) —
-- never by an update that leaves it alone (the bind's profile_id move, a version bump): a tz can enter only through
-- those two, and pg_timezone_names reads every zone file per call. An unknown name is refused with a recognisable
-- message (Set Schedule answers it as 400 bad_timezone). Not security definer: it only reads the catalog view.
create or replace function public.bot_schedule_tz_check() returns trigger
language plpgsql as $fn$
begin
  if not exists (select 1 from pg_catalog.pg_timezone_names z where z.name = new.tz) then
    raise exception 'bot_schedule_bad_timezone: %', new.tz using errcode = 'invalid_parameter_value';
  end if;
  return new;
end
$fn$;
drop trigger if exists bot_schedule_tz_check on public.bot_schedule;
create trigger bot_schedule_tz_check before insert or update of tz on public.bot_schedule
  for each row execute function public.bot_schedule_tz_check();

-- RLS default-deny (no policies) + no client-key grants, as sql/2026-08-21-billing-rls.sql: only the server-side Postgres
-- credential (table owner) reads and writes it.
alter table public.bot_schedule enable row level security;
revoke all on table public.bot_schedule from anon, authenticated;

-- Post-checks:
--   \d public.bot_schedule                                                                  -- the eight columns above
--   select tgname from pg_trigger where tgrelid = 'public.bot_schedule'::regclass and not tgisinternal;   -- bot_schedule_tz_check
--   select relrowsecurity from pg_class where oid = 'public.bot_schedule'::regclass;                       -- expect t
--   select grantee, privilege_type from information_schema.role_table_grants
--   where table_name = 'bot_schedule' and grantee in ('anon', 'authenticated');                            -- expect 0 rows
-- Reverse — only once no deployed workflow reads the table (the templates' Read Reply Mode, Set Schedule, Delete Bot
-- Files, Create's bind): drop table public.bot_schedule; drop function public.bot_schedule_tz_check();
