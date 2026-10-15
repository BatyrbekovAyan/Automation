#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""A4 «защита платящих» (2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
§2.2, §3 A4, §1 — the A5 rule: owner notices carry no client text and no client number).

A paying owner's WhatsApp/Telegram logs out (a re-paired phone — an ordinary event): the bot falls silent, the token keeps
listing the profile, the card says «connected» and nobody knows. The orphan sweep no longer deletes such a profile (it has
a live bot_profiles row); this workflow tells the owner:

  crChannelWatch001-Channel_Watch.json   Schedule Trigger «*/10 * * * *»
      List WA Profiles / List TG Profiles (Wappi profile/all/get on both bases; fullResponse + neverError, retried,
      continue) → Live Rows (every LIVE bot_profiles row with its owner and the previous channel_snapshot; read-only; a
      database fault fails the run — nothing written, nobody messaged) → Diff Channel State (the bases count only on a
      real answer, the empty-list floor, the boolean-only flag, the proportional cap max(2, 50%) — the rules of the two
      sweeps; true→false = DOWN + a notice owed, false→true = UP, first sighting = recorded without a notice; an owed notice
      is retried while the channel stays down, at most the Handoff Notifier's six attempts; and `reachable` — the live
      profiles THIS run's lists prove authorised, the only possible routes) → Upsert Snapshot (ONE statement: the new
      states, the notice attempts COUNTED — before the send, at most six —, the rows of retired profiles deleted, and back
      one row per notice it counted — a notice never goes out for a state or an attempt the snapshot did not take)
      → Down Alerts? (a positive field of that row) → Pick Other Channel (ANOTHER live channel of the same install among
      Diff's reachable ids — never the recorded snapshot: a profile Wappi no longer lists keeps its last state and must not
      take the notice from a channel that works —, WhatsApp first; never the down one) → Has Other Channel? → Owner Phone
      (get/status of THAT channel) → Compose Notice (the owner's number by build-handoff-workflows.OWNER_PHONE_JS — the A5
      rule, imported, never copied; the text is NOTICE_TEXT, embedded; the other channel must say authorised and echo its
      own profile id) → Skip? → Send Notice (a self-message: «Choose Reply: {WhatsApp|Telegram} отключился — бот не
      отвечает. Переподключите канал в приложении.») → Settle Notice → Notice Sent? → Record Notified (notified_at; a
      failed send leaves it null — retried next run while attempts remain).
  0DP79STI3pafXO9B-Set_Workflow_State.json  crChannelWatch001 joins the protected infra ids.

The table: sql/2026-10-14-channel-snapshot.sql (apply BEFORE importing this workflow). The return of a channel sends no
message (backlog: «канал снова в сети»).

    python3 Tools/n8n/build-channel-watch.py            # write the workflow + patch Set Workflow State (re-runnable)
    python3 Tools/n8n/build-channel-watch.py --check    # exit 2 when anything is stale
Pinned by verify-channel-watch.py (structure + its own mutations) and tests/test_channel_watch.py (the Diff over fixture
lists, the statements on temp tables, the whole graph through tests/notifier_chain.js).
"""
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


auth = _module("build_auth", "build-auth-workflows.py")
HW = _module("build_handoff_workflows", "build-handoff-workflows.py")     # OWNER_PHONE_JS — the A5 rule, ONE text

WF_ID, WF_NAME = "crChannelWatch001", "Channel Watch"
WF_FILE = f"{WF_ID}-Channel_Watch.json"
SET_STATE = "0DP79STI3pafXO9B-Set_Workflow_State.json"
PROTECTED_ANCHOR = "  'crVerifyIdent001'\n]);"
CRON = "*/10 * * * *"
CAP_FLOOR = 2               # the liveness branch's RETIRE_CAP_FLOOR
CAP_SHARE = 0.5             # … and its 50 %
# An undelivered notice is retried on the next runs — at most the Handoff Notifier's attempts (MAX_NOTIFY_ATTEMPTS, «по
# рецепту Notifier … attempts»). Without a bound, a send Wappi delivered but answered as failed (a timeout, a lost response
# — counted as failed, never as sent) would message the owner again every 10 minutes for the whole outage. Fix round 2
# (2026-10-15): the bound is a COUNTER (channel_snapshot.notify_attempts), not a clock — the recorded DOWN's age was read
# by a later run's Live Rows against the DOWN run's own upsert time (a round trip later in its run), so a window of six
# periods admitted a seventh send. Upsert Snapshot counts the attempt when it hands the notice to the send chain — BEFORE
# the send, as the Notifier counts at claim: a run that dies after Wappi delivered still counts, so at most six sends.
MAX_ATTEMPTS = HW.MAX_NOTIFY_ATTEMPTS
WA_BASE, TG_BASE = "https://wappi.pro/api/", "https://wappi.pro/tapi/"
# The A4 text — Compose Notice embeds THIS map (one place, never an inline copy); verify-channel-watch.py holds its own copy
# of the full text, so a change here is a deliberate change in two places.
NOTICE_TEXT = {"whatsapp": "Choose Reply: WhatsApp отключился — бот не отвечает. Переподключите канал в приложении.",
               "telegram": "Choose Reply: Telegram отключился — бот не отвечает. Переподключите канал в приложении."}
BATCH = {"batch": {"batchSize": 1, "batchInterval": 300}}     # one Wappi call at a time — Wappi crosses concurrent answers

# ---------------------------------------------------------------------------------------------------------------------
LIVE_ROWS_SQL = """-- A4 «защита платящих» (2026-10-14; fix round 2 2026-10-15): the channel watch reads every LIVE bot_profiles row
-- (any subscription status) with its owner (the install; '' for an owner-less legacy row), the state the previous run
-- recorded (no snapshot row: prev_authorized is null) and the notice attempts the recorded DOWN has had (prev_attempts —
-- at most the Handoff Notifier's). Read-only.
select b.profile_id, b.channel, coalesce(b.owner_id, '') as owner_id,
       s.authorized as prev_authorized, (s.notified_at is not null) as prev_notified,
       coalesce(s.notify_attempts, 0) as prev_attempts
from bot_profiles b
left join channel_snapshot s on s.profile_id = b.profile_id
where b.deleted_at is null and b.channel in ('whatsapp', 'telegram')
order by b.profile_id;"""

DIFF_JS = r"""// CHANNEL WATCH / Diff Channel State (generated by Tools/n8n/build-channel-watch.py — edit there, never the JSON).
// A4 «защита платящих» (2026-10-14): which LIVE channels changed Wappi's `authorized` flag since the last run. The rules
// are the two sweeps' (their text mirrored here, one meaning):
//   * a base counts only on a real answer — HTTP 200, status "done", profiles an array (null = empty): the Profile
//     Lifecycle Sweep's validateBase;
//   * the empty-list floor: an EMPTY list while that channel has >= 1 live row is an invalid fetch (the liveness branch's
//     rule) — never a mass «down» on a suspicious empty answer;
//   * a flag counts only as a real boolean (the orphan sweep's «never act on ambiguity»); a profile Wappi lists as
//     deleted, or does not list at all, is skipped too;
//   * the proportional cap: more DOWN transitions on a base than max(2, 50% of its live rows) skips that base for the
//     whole run (the liveness branch's cap) — a Wappi glitch must never message every owner.
// true -> false = DOWN (the snapshot turns false, a notice is owed), false -> true = UP (the snapshot turns true, the
// notice debt is reset), a profile without a snapshot = recorded with no notice (first seen already down = it never worked
// under the watch — nothing owed). A DOWN whose notice did not go out stays owed (false, notified_at null) and is retried
// on the next runs while the channel stays down — at most the Handoff Notifier's attempts: only while the attempts Upsert
// Snapshot counted (prev_attempts) are below MAX_ATTEMPTS (a delivered send answered as failed must not repeat every 10
// minutes for the whole outage). Only a row with an owner can be notified (the notice goes through ANOTHER channel of the
// same install); an owner-less row is only recorded.
// `reachable` = the live profiles THIS run's lists prove authorised (a valid base that passed the floor and the cap,
// listed, not deleted at Wappi, the boolean true) — the only channels Pick Other Channel may route a notice through. The
// recorded snapshot is never trusted for the route: a profile the list no longer carries keeps its last recorded state.
const CAP_FLOOR = __CAP_FLOOR__;
const CAP_SHARE = __CAP_SHARE__;
const MAX_ATTEMPTS = __MAX_ATTEMPTS__;
const CHANNELS = ['whatsapp', 'telegram'];

function validateBase(item) {
  const r = item || {};
  if (r.error) return { ok: false, reason: 'fetch_error: ' + JSON.stringify(r.error).slice(0, 200) };
  const code = Number(r.statusCode) || 0;
  if (code !== 200) return { ok: false, reason: 'http_' + code };
  const b = (r.body && typeof r.body === 'object' && !Array.isArray(r.body)) ? r.body : null;
  if (!b || b.status !== 'done') return { ok: false, reason: 'bad_status: ' + JSON.stringify(b ? b.status : null) };
  const profiles = (b.profiles === null || b.profiles === undefined) ? [] : b.profiles;
  if (!Array.isArray(profiles)) return { ok: false, reason: 'profiles_not_array' };
  return { ok: true, profiles: profiles };
}

const lists = { whatsapp: validateBase($('List WA Profiles').first().json), telegram: validateBase($('List TG Profiles').first().json) };

// Live Rows answers rows, or ONE empty item when there is no live row (alwaysOutputData). Anything else is not a row and
// proves nothing: the run then writes nothing and tells nobody.
const items = $input.all().map(function (i) { return (i && i.json) || {}; });
const isRow = function (r) { return typeof r.profile_id === 'string' && r.profile_id !== '' && CHANNELS.includes(r.channel); };
const noRows = items.length === 1 && Object.keys(items[0]).length === 0;
const rowsProven = noRows || (items.length > 0 && items.every(isRow));
const rows = rowsProven ? items.filter(isRow) : [];

const stats = { ranAt: new Date().toISOString(), liveRows: rows.length, rowsProven: rowsProven, bases: {} };
const writes = [];
const alerts = [];
const reachable = [];
if (!rowsProven) {
  stats.reason = 'live_rows_unproven: the live read answered something that is not a row — nothing written, nobody told';
  return [{ json: { writes: '[]', alerts: '[]', reachable: '[]', stats: stats } }];
}

const liveByChannel = { whatsapp: 0, telegram: 0 };
for (const r of rows) liveByChannel[r.channel] += 1;

for (const ch of CHANNELS) {
  const base = lists[ch];
  const live = liveByChannel[ch];
  const bs = { valid: base.ok, reason: base.ok ? null : base.reason, live: live, listed: base.ok ? base.profiles.length : 0,
               cap: Math.max(CAP_FLOOR, live * CAP_SHARE), down: 0, up: 0, fresh: 0, owed: 0, gaveUp: 0, reachable: 0, skipped: [] };
  stats.bases[ch] = bs;
  if (base.ok && base.profiles.length === 0 && live > 0) {
    bs.valid = false;
    bs.reason = 'empty_list_floor: Wappi listed no ' + ch + ' profile while ' + live + ' live row(s) exist — base skipped this run';
  }
  if (!bs.valid) continue;

  const byId = {};
  for (const p of base.profiles) {
    const pid = String((p && p.profile_id) || '').trim();
    if (pid) byId[pid] = p;
  }
  const planned = [];
  const reach = [];
  for (const r of rows) {
    if (r.channel !== ch) continue;
    const p = byId[r.profile_id];
    if (!p) { bs.skipped.push({ profileId: r.profile_id, reason: 'not_listed' }); continue; }
    if (p.deleted_at) { bs.skipped.push({ profileId: r.profile_id, reason: 'deleted_at_wappi' }); continue; }
    if (typeof p.authorized !== 'boolean') { bs.skipped.push({ profileId: r.profile_id, reason: 'authorized flag missing or ambiguous — never act on ambiguity' }); continue; }
    const cur = p.authorized;
    if (cur === true) reach.push(r.profile_id);
    const prev = (typeof r.prev_authorized === 'boolean') ? r.prev_authorized : null;
    const owner = (typeof r.owner_id === 'string') ? r.owner_id : '';
    const alert = owner ? { profile_id: r.profile_id, channel: ch, owner_id: owner } : null;
    if (prev === null) {
      planned.push({ kind: 'fresh', write: { profile_id: r.profile_id, channel: ch, authorized: cur, settled: cur === false } });
    } else if (prev === true && cur === false) {
      planned.push({ kind: 'down', write: { profile_id: r.profile_id, channel: ch, authorized: false, settled: false }, alert: alert });
    } else if (prev === false && cur === true) {
      planned.push({ kind: 'up', write: { profile_id: r.profile_id, channel: ch, authorized: true, settled: false } });
    } else if (prev === false && cur === false && r.prev_notified !== true) {
      const left = typeof r.prev_attempts === 'number' && r.prev_attempts >= 0 && r.prev_attempts < MAX_ATTEMPTS;
      planned.push(left ? { kind: 'owed', alert: alert } : { kind: 'gaveUp' });
    }
  }
  const downs = planned.filter(function (x) { return x.kind === 'down'; }).length;
  if (downs > bs.cap) {
    bs.valid = false;
    bs.reason = 'down_cap: ' + downs + ' of ' + live + ' live ' + ch + ' row(s) went down at once, cap ' + bs.cap +
      ' = max(' + CAP_FLOOR + ', 50%) — base skipped this run';
    continue;
  }
  for (const x of planned) {
    bs[x.kind] += 1;
    if (x.write) writes.push(x.write);
    if (x.alert) alerts.push(x.alert);
  }
  bs.reachable = reach.length;
  for (const id of reach) reachable.push(id);
}
stats.writes = writes.length;
stats.alerts = alerts.length;
stats.reachable = reachable.length;
return [{ json: { writes: JSON.stringify(writes), alerts: JSON.stringify(alerts), reachable: JSON.stringify(reachable), stats: stats } }];
""".replace("__CAP_FLOOR__", str(CAP_FLOOR)).replace("__CAP_SHARE__", str(CAP_SHARE)).replace(
    "__MAX_ATTEMPTS__", str(MAX_ATTEMPTS))

UPSERT_SNAPSHOT_SQL = """-- A4 «защита платящих» (2026-10-14; fix round 2 2026-10-15): the channel watch records what Diff Channel State
-- decided, in ONE statement:
--   * the new states (a first sighting, a DOWN, an UP): a DOWN and an UP leave notified_at null (a DOWN owes a notice, an
--     UP resets the debt for the next DOWN); a first sighting already down is «nothing owed» (notified_at = now());
--   * the notice ATTEMPTS (notify_attempts), counted HERE — before the send, as the Handoff Notifier counts at claim: a
--     DOWN handed on is attempt 1, a notice still owed one more while fewer than __MAX__ were made (a row still live, down
--     and untold); an UP and a first sighting start from 0. A run that dies after Wappi delivered still counts, so at most
--     __MAX__ sends whatever the timing;
--   * the rows of profiles that no longer have a LIVE bot_profiles row are deleted (the snapshot only describes live ones);
--   * back: one row per notice this statement COUNTED (Diff's list, owners only) — the notice chain reads them, so no
--     notice ever goes out for a state or an attempt this statement did not take. An unchanged state is never rewritten.
-- Parameters, in order: the JSON array of states to record (profile, channel, authorized, settled), the JSON array of the
-- down profiles a notice is owed for (profile, channel, owner).
with w as (
  select x.profile_id, x.channel, x.authorized, coalesce(x.settled, false) as settled
  from jsonb_to_recordset($1::jsonb) as x(profile_id text, channel text, authorized boolean, settled boolean)
  where coalesce(x.profile_id, '') <> '' and x.channel in ('whatsapp', 'telegram') and x.authorized is not null
), a as (
  select distinct x.profile_id, x.channel, x.owner_id
  from jsonb_to_recordset($2::jsonb) as x(profile_id text, channel text, owner_id text)
  where coalesce(x.profile_id, '') <> '' and x.channel in ('whatsapp', 'telegram') and coalesce(x.owner_id, '') <> ''
), up as (
  insert into channel_snapshot as s (profile_id, channel, authorized, seen_at, notified_at, notify_attempts)
  select w.profile_id, w.channel, w.authorized, now(), case when w.settled then now() end,
         case when not w.authorized and not w.settled and exists (select 1 from a where a.profile_id = w.profile_id) then 1 else 0 end
  from w
  on conflict (profile_id) do update
    set channel = excluded.channel, authorized = excluded.authorized, seen_at = excluded.seen_at,
        notified_at = excluded.notified_at, notify_attempts = excluded.notify_attempts
  returning s.profile_id, s.notify_attempts
), retry as (
  update channel_snapshot s set notify_attempts = s.notify_attempts + 1
  where s.profile_id in (select a.profile_id from a)
    and not exists (select 1 from w where w.profile_id = s.profile_id)
    and not s.authorized and s.notified_at is null and s.notify_attempts < __MAX__
    and exists (select 1 from bot_profiles b where b.profile_id = s.profile_id and b.deleted_at is null)
  returning s.profile_id
), gone as (
  delete from channel_snapshot s
  where not exists (select 1 from bot_profiles b where b.profile_id = s.profile_id and b.deleted_at is null)
  returning s.profile_id
)
select a.profile_id, a.channel, a.owner_id, true as alert,
       (select count(*) from up)::int as written, (select count(*) from gone)::int as cleaned
from a
where a.profile_id in (select up.profile_id from up where up.notify_attempts = 1)
   or a.profile_id in (select retry.profile_id from retry);""".replace("__MAX__", str(MAX_ATTEMPTS))
UPSERT_SNAPSHOT_PARAMS = "={{ [ $json.writes, $json.alerts ] }}"
DOWN_ALERT_EXPR = ("={{ $json.alert === true && typeof $json.profile_id === 'string' && $json.profile_id !== '' "
                   "&& typeof $json.owner_id === 'string' && $json.owner_id !== '' }}")

PICK_OTHER_SQL = """-- A4 «защита платящих» (2026-10-14; fix round 2 2026-10-15): the channel a «… отключился» notice goes through —
-- ANOTHER live channel of the SAME install (bot_profiles.owner_id) that THIS run's Wappi lists prove authorised (Diff
-- Channel State's reachable ids: a valid base, listed, not deleted at Wappi, the boolean true), WhatsApp first, then the
-- newest. The recorded snapshot is never trusted for the route: a profile Wappi no longer lists keeps its last recorded
-- state, and such a stale row must never take the notice away from a channel that works. Never the down profile itself;
-- nothing when no such channel exists this run (the app marks the channel itself; a later run retries while attempts
-- remain). Read-only. Parameters, in order: the down profile, its channel, its owner (the install), the JSON array of
-- this run's reachable profile ids.
select $1::text as down_profile_id, $2::text as down_channel, b.profile_id as other_profile_id, b.channel as other_channel
from bot_profiles b
where b.deleted_at is null and b.owner_id = $3::text and b.profile_id <> $1::text and b.channel in ('whatsapp', 'telegram')
  and b.profile_id in (select jsonb_array_elements_text($4::jsonb))
order by (b.channel = 'whatsapp') desc, b.created_at desc nulls last, b.profile_id
limit 1;"""
PICK_OTHER_PARAMS = "={{ [ $json.profile_id, $json.channel, $json.owner_id, $('Diff Channel State').first().json.reachable ] }}"
HAS_OTHER_EXPR = ("={{ typeof $json.other_profile_id === 'string' && $json.other_profile_id !== '' "
                  "&& ($json.other_channel === 'whatsapp' || $json.other_channel === 'telegram') "
                  "&& typeof $json.down_profile_id === 'string' && $json.down_profile_id !== '' "
                  "&& $json.other_profile_id !== $json.down_profile_id }}")
OWNER_PHONE_URL = "={{ ($json.other_channel === 'telegram' ? '" + TG_BASE + "' : '" + WA_BASE + "') + 'sync/get/status' }}"
OWNER_PHONE_QUERY = "={{ JSON.stringify({ profile_id: $json.other_profile_id }) }}"

COMPOSE_NOTICE_JS = r"""// CHANNEL WATCH / Compose Notice (generated by Tools/n8n/build-channel-watch.py; runs PER ITEM — one notice per down
// channel). A self-message on ANOTHER live channel of the same owner: the owner's number is read from THAT channel's
// get/status by the A5 rule (build-handoff-workflows.OWNER_PHONE_JS, embedded verbatim — one rule for every message to the
// owner). The text is build-channel-watch.NOTICE_TEXT (embedded — one place) and names only the channel that went down: no
// client text, no client number (spec §1, the A5 rule). The other channel must say it is authorised and echo its own
// profile id (Wappi crosses concurrent answers) — else no send.
__OWNER_PHONE_JS__
const NOTICE_TEXT = __NOTICE_TEXT__;
const pick = $('Pick Other Channel').item.json;
const reply = $json || {};
const status = (reply.body && typeof reply.body === 'object' && !Array.isArray(reply.body)) ? reply.body : {};
const wapi = pick.other_channel === 'telegram' ? 'tapi' : 'api';
const text = (pick.down_channel === 'whatsapp' || pick.down_channel === 'telegram') ? NOTICE_TEXT[pick.down_channel] : '';
const base = wapi === 'tapi' ? '__TG_BASE__' : '__WA_BASE__';
let error = '';
if (!text) error = 'unknown_down_channel';
else if (Number(reply.statusCode) !== 200) error = 'status_http_' + (Number(reply.statusCode) || 0);
else if (status.profile_id !== pick.other_profile_id) error = 'status_mismatch';
else if (status.authorized !== true) error = 'other_channel_unauthorized';
const phone = error ? '' : ownerPhone(wapi, status);
if (!error && !phone) error = 'no_phone';
return { json: { downProfileId: pick.down_profile_id, skip: !!error, error: error, url: base + 'sync/message/send',
                 profileId: pick.other_profile_id, recipient: phone, body: text } };
""".replace("__NOTICE_TEXT__", json.dumps(NOTICE_TEXT, ensure_ascii=False)).replace("__TG_BASE__", TG_BASE).replace(
    "__WA_BASE__", WA_BASE).replace("__OWNER_PHONE_JS__", HW.OWNER_PHONE_JS)

SEND_QUERY = "={{ JSON.stringify({ profile_id: $json.profileId }) }}"
SEND_BODY = "={{ JSON.stringify({ recipient: $json.recipient, body: $json.body }) }}"

SETTLE_NOTICE_JS = r"""// CHANNEL WATCH / Settle Notice (generated by Tools/n8n/build-channel-watch.py; runs PER ITEM): delivered ONLY on HTTP
// 200 with Wappi's done / a message id — the Handoff Notifier's rule. An undelivered notice records nothing: the channel
// stays owed and the next run retries while it is down.
const c = $('Compose Notice').item.json;
const r = $json || {};
const code = Number(r.statusCode) || 0;
const body = (r.body && typeof r.body === 'object') ? r.body : {};
const ok = code === 200 && (body.status === 'done' || !!body.message_id);
return { json: { profileId: c.downProfileId, sent: ok, error: ok ? '' : ('http ' + code + ' ' + JSON.stringify(body).slice(0, 200)) } };
"""
SENT_EXPR = "={{ $json.sent === true && typeof $json.profileId === 'string' && $json.profileId !== '' }}"

RECORD_NOTIFIED_SQL = """-- A4 «защита платящих» (2026-10-14): the owner was told about this down channel — nothing more is owed for the
-- current down state. Only while the snapshot still says it is down and owed (an UP recorded meanwhile keeps notified_at
-- null for the next DOWN). Parameter: the down profile.
update channel_snapshot set notified_at = now()
where profile_id = $1::text and authorized = false and notified_at is null
returning profile_id, notified_at;"""
RECORD_NOTIFIED_PARAMS = "={{ [ $json.profileId ] }}"


def cid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "channel-watch:" + name))


def own(n):
    n["id"] = cid(n["name"])
    return n


def list_profiles(name, base, pos):
    n = own(auth.http_wappi(name, "GET", base + "profile/all/get", pos, timeout=30000))
    n.update({"retryOnFail": True, "maxTries": 3, "waitBetweenTries": 2000})
    return n


def build():
    n, c = [], {}
    n.append({"parameters": {"rule": {"interval": [{"field": "cronExpression", "expression": CRON}]}}, "id": cid("trigger"),
              "name": "Every 10 Minutes", "type": "n8n-nodes-base.scheduleTrigger", "typeVersion": 1.2, "position": [0, 200]})
    n.append(list_profiles("List WA Profiles", WA_BASE, [220, 200]))
    tg = list_profiles("List TG Profiles", TG_BASE, [440, 200])
    tg["executeOnce"] = True
    n.append(tg)
    live = own(auth.postgres("Live Rows", LIVE_ROWS_SQL, "={{ [] }}", [660, 200]))
    live.pop("onError")                 # a database fault fails the run (an error execution): nothing written, nobody told
    live["executeOnce"] = True
    n.append(live)
    n.append(own(auth.code("Diff Channel State", DIFF_JS, [880, 200])))
    upsert = own(auth.postgres("Upsert Snapshot", UPSERT_SNAPSHOT_SQL, UPSERT_SNAPSHOT_PARAMS, [1100, 200]))
    upsert.pop("onError")               # a failed write fails the run (an error execution): no notice without its snapshot
    n.append(upsert)
    n.append(own(auth.if_true("Down Alerts?", DOWN_ALERT_EXPR, [1320, 200])))
    n.append(own(auth.postgres("Pick Other Channel", PICK_OTHER_SQL, PICK_OTHER_PARAMS, [1540, 200])))
    n.append(own(auth.if_true("Has Other Channel?", HAS_OTHER_EXPR, [1760, 200])))
    phone = own(auth.http_wappi("Owner Phone", "GET", OWNER_PHONE_URL, [1980, 200], query_expr=OWNER_PHONE_QUERY, timeout=20000))
    phone["parameters"]["options"]["batching"] = json.loads(json.dumps(BATCH))
    n.append(phone)
    compose = own(auth.code("Compose Notice", COMPOSE_NOTICE_JS, [2200, 200]))
    compose["parameters"]["mode"] = "runOnceForEachItem"      # one notice per down channel, never only the first
    n.append(compose)
    n.append(own(auth.if_true("Skip?", "={{ $json.skip }}", [2420, 200])))
    send = own(auth.http_wappi("Send Notice", "POST", "={{ $json.url }}", [2640, 300], query_expr=SEND_QUERY, body_expr=SEND_BODY,
                               timeout=30000))
    send["parameters"]["options"]["batching"] = json.loads(json.dumps(BATCH))
    n.append(send)
    settle = own(auth.code("Settle Notice", SETTLE_NOTICE_JS, [2860, 300]))
    settle["parameters"]["mode"] = "runOnceForEachItem"
    n.append(settle)
    n.append(own(auth.if_true("Notice Sent?", SENT_EXPR, [3080, 300])))
    n.append(own(auth.postgres("Record Notified", RECORD_NOTIFIED_SQL, RECORD_NOTIFIED_PARAMS, [3300, 300])))
    auth.edge(c, "Every 10 Minutes", "List WA Profiles")
    auth.edge(c, "List WA Profiles", "List TG Profiles")
    auth.edge(c, "List TG Profiles", "Live Rows")
    auth.edge(c, "Live Rows", "Diff Channel State")
    auth.edge(c, "Diff Channel State", "Upsert Snapshot")
    auth.edge(c, "Upsert Snapshot", "Down Alerts?")
    auth.edge(c, "Down Alerts?", "Pick Other Channel", 0)               # the false output leads nowhere
    auth.edge(c, "Pick Other Channel", "Has Other Channel?")
    auth.edge(c, "Has Other Channel?", "Owner Phone", 0)                # no other channel: only the snapshot (the app marks it)
    auth.edge(c, "Owner Phone", "Compose Notice")
    auth.edge(c, "Compose Notice", "Skip?")
    auth.edge(c, "Skip?", "Send Notice", 1)                             # a skipped notice records nothing: still owed
    auth.edge(c, "Send Notice", "Settle Notice")
    auth.edge(c, "Settle Notice", "Notice Sent?")
    auth.edge(c, "Notice Sent?", "Record Notified", 0)                  # undelivered: notified_at stays null, retried next run
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return auth.workflow(WF_ID, WF_NAME, n, c, settings)


def text():
    return json.dumps(build(), indent=2, ensure_ascii=False) + "\n"


def protected_js_patched(js):
    """Set Workflow State's Validate with crChannelWatch001 appended to its protected list (None: nothing to change)."""
    if "const PROTECTED = new Set([" not in js or f"'{WF_ID}'" in js:
        return None
    assert js.count(PROTECTED_ANCHOR) == 1, "Set Workflow State: the protected list has an unexpected shape"
    return js.replace(PROTECTED_ANCHOR, f"  'crVerifyIdent001',\n  '{WF_ID}'\n]);")


def patch_set_state(write):
    path = os.path.join(WF, SET_STATE)
    wf = json.load(open(path, encoding="utf-8"))
    changed = False
    for nd in wf["nodes"]:
        new = protected_js_patched(nd["parameters"].get("jsCode", ""))
        if new is not None:
            nd["parameters"]["jsCode"] = new
            changed = True
    if changed and write:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return changed


def stale():
    out = []
    p = os.path.join(WF, WF_FILE)
    if not os.path.exists(p) or open(p, encoding="utf-8").read() != text():
        out.append(WF_FILE)
    wf = json.load(open(os.path.join(WF, SET_STATE), encoding="utf-8"))
    if not any(f"'{WF_ID}'" in nd["parameters"].get("jsCode", "") for nd in wf["nodes"]):
        out.append(SET_STATE)
    return out


def main():
    if "--check" in sys.argv:
        s = stale()
        print("channel watch:", "OK" if not s else f"STALE {s}")
        sys.exit(2 if s else 0)
    path = os.path.join(WF, WF_FILE)
    old = open(path, encoding="utf-8").read() if os.path.exists(path) else None
    new = text()
    if old != new:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(new)
    print(f"{'wrote' if old != new else 'up to date'}  {WF_FILE} ({len(build()['nodes'])} nodes)")
    print(f"{'patched' if patch_set_state(write=True) else 'up to date'}  {SET_STATE} (protected ids)")


if __name__ == "__main__":
    main()
