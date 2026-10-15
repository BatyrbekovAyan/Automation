-- A4 «защита платящих» (2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
-- §2.2, §3 A4): the CHANNEL WATCH's memory. Every 10 minutes crChannelWatch001 «Channel Watch» lists the Wappi profiles of
-- both bases and compares each LIVE bot_profiles row's `authorized` flag with the state recorded here:
--   * true → false (DOWN): the row turns false and a notice is OWED (notified_at null) — one self-message to the owner
--     through ANOTHER live, authorised channel of the same install («Choose Reply: WhatsApp отключился — …»; no client text,
--     no client number); a delivered notice stamps notified_at, an undelivered one is retried on the next runs while the
--     channel stays down — at most the Handoff Notifier's six attempts (while the recorded DOWN is younger than an hour), so a
--     send Wappi delivered but answered as failed never repeats for the whole outage; an owner without another channel gets
--     no message (the app marks the channel itself);
--   * false → true (UP): the row turns true, notified_at back to null (the next DOWN owes a notice again) — no message;
--   * a profile seen for the first time is recorded without a notice: authorised → notified_at null; already down → it
--     never worked under the watch (a wizard mid-pairing), nothing is owed: notified_at = now().
-- notified_at therefore reads «nothing more is owed for the current down state since then» (a delivered notice, or a first
-- sighting already down); NULL on a false row = a notice owed (sent while seen_at is younger than the retry window).
-- seen_at = when the stored state was first observed (the transition's run); an unchanged state is not rewritten. No app_user_id: ownership is read by joining the live
-- bot_profiles rows (owner_id = the install); rows of retired profiles are deleted by the watch's own snapshot statement.
-- Additive + idempotent. Apply BEFORE importing crChannelWatch001: without the table Live Rows fails (an error execution
-- every 10 minutes, nothing written, nobody messaged).
-- TABLE-BEGIN
create table if not exists public.channel_snapshot (
  profile_id  text primary key,                                          -- the Wappi profile (a live bot_profiles row's id)
  channel     text not null check (channel in ('whatsapp', 'telegram')),
  authorized  boolean not null,                                          -- Wappi's authorized flag as last recorded
  seen_at     timestamptz not null default now(),                        -- when the stored state was first observed
  notified_at timestamptz                                                -- NULL on a false row = a notice is owed
);
-- TABLE-END
-- RLS default-deny (no policies) + no client-key grants, as sql/2026-08-21-billing-rls.sql: only the server-side Postgres
-- credential (table owner) reads and writes it.
alter table public.channel_snapshot enable row level security;
revoke all on table public.channel_snapshot from anon, authenticated;

-- Post-checks:
--   \d public.channel_snapshot                                                        -- the five columns above
--   select relrowsecurity from pg_class where oid = 'public.channel_snapshot'::regclass;              -- expect t
--   select grantee, privilege_type from information_schema.role_table_grants
--   where table_name = 'channel_snapshot' and grantee in ('anon', 'authenticated');                   -- expect 0 rows
-- Support (read-only): the channels down right now and whether their owner was told
--   select s.profile_id, s.channel, s.seen_at as down_since, s.notified_at, b.owner_id
--   from public.channel_snapshot s join public.bot_profiles b on b.profile_id = s.profile_id and b.deleted_at is null
--   where not s.authorized order by s.seen_at desc;
