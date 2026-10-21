#!/usr/bin/env python3
"""A4 «защита платящих» (2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
§2.2, §3 A4, §1): the channel watch crChannelWatch001 «Channel Watch» (build-channel-watch.py).

Every 10 minutes it lists the Wappi profiles of both bases and compares each LIVE bot_profiles row's `authorized` flag
with channel_snapshot: true -> false (DOWN) records the state and owes ONE notice — a self-message through ANOTHER live
channel of the same install that THIS run's list shows authorised («Choose Reply: WhatsApp отключился — сотрудник не
отвечает. Переподключите канал в приложении.», no client text, no client number) — false -> true (UP) only records, a first
sighting records without a notice; an owed notice is retried while the channel stays down, at most the Handoff Notifier's
six attempts, COUNTED in the snapshot before each send (fix round 2: a counter, not a clock); an owner without another
channel gets no message.

What runs here — no n8n server, no network, never production:
  A  Diff Channel State: the REAL code over fixture lists (harness.run_js) — the transitions, the boolean-only flag, the
     invalid bases, the empty-list floor, the proportional cap max(2, 50%), owner-less rows, an unproven live read, the
     attempts left, and the reachable ids (the only routes: what THIS run's lists show authorised);
  B  the REAL statements (Live Rows, Upsert Snapshot, Pick Other Channel, Record Notified) on session-temp tables
     (channel_snapshot from the migration's own DDL), and the watch's cadence — Live Rows → Diff → Upsert Snapshot run
     after run on a channel that stays down while every send fails: exactly six notices, the seventh run at T+60 min
     minus a few seconds hands on none;
  C  the whole canonical graph through tests/notifier_chain.js (n8n's own expression engine, n8n-core's alwaysOutputData /
     continue-on-error / paired-item semantics): exactly one notice per DOWN, through the other channel, to the owner's
     number by the A5 rule, recorded only when delivered; nothing without another channel, without a recorded snapshot,
     to an unauthorised or mismatched other channel;
  D  verify-channel-watch.py accepts the canon and rejects each of its mutations; every mutation whose effect the Diff, the
     statements or the graph can show is shown here too (EXPOSED_BY) — a tripwire nothing can see is unproven;
  E  the generator is current and embeds the A5 rule (build-handoff-workflows.OWNER_PHONE_JS) verbatim.

    python3 Tools/n8n/tests/test_channel_watch.py
"""
import copy
import json
import os
import re
import subprocess
import sys
import tempfile

from harness import N8N, Case, PgSession, module, node, report, run_js, sql_run, wf, bind

FILE = 'crChannelWatch001-Channel_Watch.json'
W = wf(FILE)
DRIVER = N8N / 'tests' / 'notifier_chain.js'
VERIFIER = N8N / 'verify-channel-watch.py'
B = module('build-channel-watch.py')
HW = module('build-handoff-workflows.py')

O, P2 = 'inst-owner-o', 'inst-owner-p'
API, TAPI = 'https://wappi.pro/api/', 'https://wappi.pro/tapi/'
WA_TEXT = 'Choose Reply: WhatsApp отключился — сотрудник не отвечает. Переподключите канал в приложении.'
TG_TEXT = 'Choose Reply: Telegram отключился — сотрудник не отвечает. Переподключите канал в приложении.'


def lst(*profiles, status=200, wstatus='done'):
    """A List WA/TG Profiles item as the HTTP node emits it (fullResponse)."""
    return {'statusCode': status, 'headers': {}, 'body': {'status': wstatus, 'profiles': list(profiles)}}


def prof(pid, authorized, **kw):
    d = {'profile_id': pid, 'name': 'bot', 'authorized': authorized}
    d.update(kw)
    return d


def row(pid, ch='whatsapp', owner=O, prev=None, notified=False, attempts=None):
    """A Live Rows row (attempts: the notices the recorded DOWN has had — by default 1 on a recorded DOWN, the one its own
    run handed on; 0 otherwise, as Live Rows' coalesce reads a missing snapshot)."""
    if attempts is None:
        attempts = 1 if prev is False else 0
    return {'profile_id': pid, 'channel': ch, 'owner_id': owner, 'prev_authorized': prev, 'prev_notified': notified, 'prev_attempts': attempts}


def write(pid, authorized, settled, ch='whatsapp'):
    return {'profile_id': pid, 'channel': ch, 'authorized': authorized, 'settled': settled}


def alert(pid, ch='whatsapp', owner=O):
    return {'profile_id': pid, 'channel': ch, 'owner_id': owner}


def diff_json(rows, wa, tg=None, graph=None):
    js = node(graph or W, 'Diff Channel State')['parameters']['jsCode']
    out = run_js(js, items=[{'json': r} for r in rows], named={'List WA Profiles': wa, 'List TG Profiles': tg if tg is not None else lst()})
    return out[0]['json']


def diff(rows, wa, tg=None, graph=None):
    """Diff Channel State of `graph` (its REAL code): (writes, alerts, stats)."""
    j = diff_json(rows, wa, tg, graph)
    return json.loads(j['writes']), json.loads(j['alerts']), j['stats']


def reach(rows, wa, tg=None, graph=None):
    """The ids Diff Channel State of `graph` hands Pick Other Channel — THIS run's only possible routes."""
    return json.loads(diff_json(rows, wa, tg, graph).get('reachable') or 'null')


# ------------------------------------------------------------------------------------------------------------------
# A: the Diff
TRANSITIONS = [   # (label, rows, WA list, writes, alerts)
    ('a first sighting, authorised: recorded, no notice', [row('a')], lst(prof('a', True)), [write('a', True, False)], []),
    ('a first sighting, already down (a wizard mid-pairing): recorded as «nothing owed», no notice',
     [row('a')], lst(prof('a', False)), [write('a', False, True)], []),
    ('true -> false (DOWN): recorded and a notice owed', [row('a', prev=True)], lst(prof('a', False)), [write('a', False, False)], [alert('a')]),
    ('false -> true (UP) after a notice: recorded, the debt reset, no notice', [row('a', prev=False, notified=True)], lst(prof('a', True)),
     [write('a', True, False)], []),
    ('false -> true (UP) with a notice still owed: recorded, no notice', [row('a', prev=False)], lst(prof('a', True)), [write('a', True, False)], []),
    ('true -> true: nothing', [row('a', prev=True)], lst(prof('a', True)), [], []),
    ('false -> false, told: nothing', [row('a', prev=False, notified=True)], lst(prof('a', False)), [], []),
    ('false -> false, still owed: the notice is retried, nothing rewritten', [row('a', prev=False)], lst(prof('a', False)), [], [alert('a')]),
    ('DOWN of an owner-less row: recorded, nobody to tell', [row('a', owner='', prev=True)], lst(prof('a', False)), [write('a', False, False)], []),
    ('owed on an owner-less row: nothing', [row('a', owner='', prev=False)], lst(prof('a', False)), [], []),
    ('still owed after five attempts: the sixth (the Notifier\'s last) is handed on',
     [row('a', prev=False, attempts=5)], lst(prof('a', False)), [], [alert('a')]),
    ('still owed after six attempts (the Notifier\'s cap): given up — no notice every 10 minutes for the whole outage',
     [row('a', prev=False, attempts=6)], lst(prof('a', False)), [], []),
    ('still owed with no readable attempt count: given up (never an unbounded retry)', [row('a', prev=False, attempts='x')], lst(prof('a', False)), [], []),
    ('still owed with a negative attempt count: given up', [row('a', prev=False, attempts=-1)], lst(prof('a', False)), [], []),
    ('still owed with attempts null: given up', [row('a', prev=False, attempts=None) | {'prev_attempts': None}], lst(prof('a', False)), [], []),
]
for label, rows, wa, want_w, want_a in TRANSITIONS:
    with Case(f'A4 A [{label}]') as c:
        w, a, s = diff(rows, wa)
        c.check(w == want_w, f'writes {w} != {want_w}')
        c.check(a == want_a, f'alerts {a} != {want_a}')
        c.check(s.get('rowsProven') is True and (s.get('bases') or {}).get('whatsapp', {}).get('valid') is True, f'stats {s}')

with Case('A4 A [Telegram]: a DOWN on the Telegram base is recorded and owed on its own channel') as c:
    w, a, s = diff([row('t', ch='telegram', prev=True)], lst(), lst(prof('t', False)))
    c.check(w == [write('t', False, False, ch='telegram')] and a == [alert('t', ch='telegram')], f'writes {w} alerts {a}')

AMBIGUOUS = {'null': None, 'the string "false"': 'false', 'the string "true"': 'true', '0': 0, '1': 1, 'a list': [], 'an object': {}}
for label, flag in AMBIGUOUS.items():
    with Case(f'A4 A ambiguity [{label}]: a flag that is not a boolean is never acted on (no state, no notice)') as c:
        for prev in (None, True, False):
            w, a, s = diff([row('a', prev=prev)], lst(prof('a', flag)))
            c.check(w == [] and a == [], f'prev {prev}: writes {w} alerts {a}')
            skipped = (s.get('bases') or {}).get('whatsapp', {}).get('skipped') or []
            c.check(any('ambiguous' in x.get('reason', '') for x in skipped), f'prev {prev}: not reported as skipped: {skipped}')

with Case('A4 A ambiguity [no flag at all]: skipped') as c:
    w, a, _ = diff([row('a', prev=True)], lst({'profile_id': 'a', 'name': 'bot'}))
    c.check(w == [] and a == [], f'writes {w} alerts {a}')

with Case('A4 A: a profile Wappi lists as deleted, or does not list at all, is skipped (never a DOWN)') as c:
    w, a, s = diff([row('a', prev=True), row('b', prev=True)], lst(prof('a', False, deleted_at='2026-10-14T00:00:00Z'), prof('c', False)))
    c.check(w == [] and a == [], f'writes {w} alerts {a}')
    reasons = sorted(x['reason'] for x in s['bases']['whatsapp']['skipped'])
    c.check(reasons == ['deleted_at_wappi', 'not_listed'], f'skipped {reasons}')

INVALID = {   # a base the Diff must not believe — the other base still works
    'HTTP 500': {'statusCode': 500, 'headers': {}, 'body': {'status': 'error'}},
    'HTTP 429': {'statusCode': 429, 'headers': {}, 'body': {'status': 'done', 'profiles': [prof('a', False)]}},
    'a transport error item (continue on error)': {'error': {'message': 'ETIMEDOUT'}},
    'status error': lst(prof('a', False), wstatus='error'),
    'no status': {'statusCode': 200, 'headers': {}, 'body': {'profiles': [prof('a', False)]}},
    'profiles is an object': {'statusCode': 200, 'headers': {}, 'body': {'status': 'done', 'profiles': {'a': prof('a', False)}}},
    'no body': {'statusCode': 200, 'headers': {}},
    'a body that is a list': {'statusCode': 200, 'headers': {}, 'body': [prof('a', False)]},
}
for label, wa in INVALID.items():
    with Case(f'A4 A invalid base [{label}]: no state and no notice on that base; the other base is still read') as c:
        w, a, s = diff([row('a', prev=True), row('t', ch='telegram', prev=True)], wa, lst(prof('t', False)))
        c.check([x['profile_id'] for x in w] == ['t'] and [x['profile_id'] for x in a] == ['t'], f'writes {w} alerts {a}')
        c.check(s['bases']['whatsapp']['valid'] is False and s['bases']['whatsapp']['reason'], f'whatsapp base {s["bases"]["whatsapp"]}')

for label, wa in {'profiles null (Wappi\'s empty)': {'statusCode': 200, 'headers': {}, 'body': {'status': 'done', 'profiles': None}},
                  'profiles []': lst(),
                  'no profiles key': {'statusCode': 200, 'headers': {}, 'body': {'status': 'done'}}}.items():
    with Case(f'A4 A floor [{label}]: an EMPTY list while the channel has a live row is an invalid fetch — reported, nothing acted on') as c:
        w, a, s = diff([row('a', prev=True), row('b', prev=False)], wa)
        c.check(w == [] and a == [], f'writes {w} alerts {a}')
        b = s['bases']['whatsapp']
        c.check(b['valid'] is False and str(b['reason']).startswith('empty_list_floor'), f'whatsapp base {b}')

with Case('A4 A floor: an empty list on a channel WITHOUT live rows is a valid (empty) base') as c:
    _, _, s = diff([row('t', ch='telegram', prev=True)], lst(), lst(prof('t', True)))
    c.check(s['bases']['whatsapp']['valid'] is True and s['bases']['whatsapp']['reason'] is None, f'whatsapp base {s["bases"]["whatsapp"]}')


def many(n, prev=True):
    return [row(f'p{i}', prev=prev) for i in range(n)]


CAP = [   # (label, live rows, how many go down, ups, expect processed)
    ('4 live rows, 3 DOWN (> cap 2 = max(2, 50%)): the whole base is skipped', 4, 3, 0, False),
    ('4 live rows, 2 DOWN (= cap): processed', 4, 2, 0, True),
    ('10 live rows, 5 DOWN (= cap 5): processed', 10, 5, 0, True),
    ('10 live rows, 6 DOWN (> cap 5): skipped', 10, 6, 0, False),
    ('1 live row, 1 DOWN (cap floor 2): processed', 1, 1, 0, True),
    ('3 live rows, 3 DOWN (100% > cap 2): skipped', 3, 3, 0, False),
]
for label, n, downs, _, processed in CAP:
    with Case(f'A4 A cap [{label}]') as c:
        rows = many(n) + [row('t', ch='telegram', prev=True)]
        listed = [prof(f'p{i}', i >= downs) for i in range(n)]
        w, a, s = diff(rows, lst(*listed), lst(prof('t', False)))
        wa_w = [x for x in w if x['channel'] == 'whatsapp']
        wa_a = [x for x in a if x['channel'] == 'whatsapp']
        b = s['bases']['whatsapp']
        if processed:
            c.check(len(wa_w) == downs and len(wa_a) == downs and b['valid'] is True, f'writes {wa_w} alerts {wa_a} base {b}')
        else:
            c.check(wa_w == [] and wa_a == [] and b['valid'] is False and str(b['reason']).startswith('down_cap'), f'writes {wa_w} alerts {wa_a} base {b}')
        c.check([x['profile_id'] for x in a if x['channel'] == 'telegram'] == ['t'], f'the telegram base must still be read: {a}')

with Case('A4 A cap: only DOWN transitions count — a mass recovery (UP) or a batch of first sightings is recorded') as c:
    w, a, s = diff(many(4, prev=False), lst(*[prof(f'p{i}', True) for i in range(4)]))
    c.check(len(w) == 4 and a == [] and s['bases']['whatsapp']['valid'] is True, f'ups: writes {w} alerts {a}')
    w, a, s = diff(many(4, prev=None), lst(*[prof(f'p{i}', False) for i in range(4)]))
    c.check(len(w) == 4 and all(x['settled'] for x in w) and a == [], f'first sightings down: writes {w} alerts {a}')
    w, a, s = diff(many(4, prev=True)[:2] + many(4, prev=False)[2:], lst(*[prof(f'p{i}', i >= 2) for i in range(4)]))
    c.check(len(w) == 4 and len(a) == 2, f'2 DOWN + 2 UP of 4: writes {w} alerts {a}')

ERROR_ITEM = {'message': 'Connection terminated unexpectedly', 'error': {'message': 'Connection terminated unexpectedly'}}
UNPROVEN = {'an error item': [ERROR_ITEM], 'rows and an error item': [row('a', prev=True), ERROR_ITEM],
            'a row on an unknown channel': [{'profile_id': 'x', 'channel': 'sms', 'owner_id': O}], 'a row without a profile id': [{'channel': 'whatsapp'}],
            'no item at all': []}
for label, rows in UNPROVEN.items():
    with Case(f'A4 A unproven live read [{label}]: nothing written, nobody told') as c:
        w, a, s = diff(rows, lst(prof('a', False)))
        c.check(w == [] and a == [] and s.get('rowsProven') is False, f'writes {w} alerts {a} stats {s}')

with Case('A4 A: no live row at all (ONE empty item, alwaysOutputData) is a proven empty registry') as c:
    w, a, s = diff([{}], lst(prof('a', False)))
    c.check(w == [] and a == [] and s.get('rowsProven') is True and s.get('liveRows') == 0, f'writes {w} alerts {a} stats {s}')

BASE_500 = {'statusCode': 500, 'headers': {}, 'body': {'status': 'error'}}
REACH = [   # (label, rows, WA list, TG list, the ids handed to Pick Other Channel)
    ('listed authorised on both bases: both are routes, WhatsApp base first',
     [row('a', prev=True), row('t', ch='telegram', prev=True)], lst(prof('a', True)), lst(prof('t', True)), ['a', 't']),
    ('listed DOWN: never a route', [row('a', prev=True), row('b', prev=True)], lst(prof('a', False), prof('b', True)), lst(), ['b']),
    ('a profile Wappi no longer lists is never a route, whatever its snapshot recorded (fix round 2)',
     [row('a', prev=True), row('gone', prev=True), row('t', ch='telegram', prev=True)], lst(prof('a', False)), lst(prof('t', True)), ['t']),
    ('listed as deleted at Wappi: never a route', [row('a', prev=True), row('b', prev=True)],
     lst(prof('a', True, deleted_at='2026-10-14T00:00:00Z'), prof('b', True)), lst(), ['b']),
    ('an ambiguous flag: never a route', [row('a', prev=True), row('b', prev=True)], lst(prof('a', 'true'), prof('b', True)), lst(), ['b']),
    ('a first sighting authorised: a route (this run proves it)', [row('a')], lst(prof('a', True)), lst(), ['a']),
    ('an invalid base: nothing on it is a route', [row('a', prev=True), row('t', ch='telegram', prev=True)], BASE_500, lst(prof('t', True)), ['t']),
    ('the empty-list floor: nothing on that base is a route', [row('a', prev=True), row('t', ch='telegram', prev=True)], lst(),
     lst(prof('t', True)), ['t']),
    ('a capped base (3 of 4 DOWN at once) routes nothing — its one authorised profile included; the other base still does',
     many(4) + [row('t', ch='telegram', prev=True)], lst(*[prof(f'p{i}', i >= 3) for i in range(4)]), lst(prof('t', True)), ['t']),
    ('an unproven live read: no route at all', [ERROR_ITEM], lst(prof('a', True)), lst(), []),
]
for label, rows, wa, tg, want in REACH:
    with Case(f'A4 A reachable [{label}]') as c:
        got = reach(rows, wa, tg)
        c.check(got == want, f'reachable {got} != {want}')


# ------------------------------------------------------------------------------------------------------------------
# B: the statements on session-temp tables
MIGRATION = N8N / 'sql' / '2026-10-14-channel-snapshot.sql'


def snapshot_ddl():
    """channel_snapshot from the migration's OWN table block, as a session temp table — the statements are tested against the
    DDL that ships (notify_attempts included), whatever the staging cluster has applied."""
    text = MIGRATION.read_text(encoding='utf-8')
    block = text.split('-- TABLE-BEGIN')[1].split('\n', 1)[1].split('-- TABLE-END')[0]
    return block.replace('create table if not exists public.', 'create temp table ')


SETUP = "create temp table bot_profiles (like public.bot_profiles including all);\n" + snapshot_ddl()


def q(name, graph=None):
    return node(graph or W, name)['parameters']['query']


def rows_of(out):
    lines = [l for l in out.splitlines() if l.strip()]
    return json.loads(lines[-1]) if lines else None


def run_sql(statement, values, seed='', after=None):
    """One statement bound like n8n's queryReplacement (its rows as json), then `after` (a select whose single column is
    json). A WITH / SELECT is materialised (a data-modifying CTE must stay top level), an UPDATE runs inside a WITH.
    jsonb_agg, not json_agg: psql prints json_agg over RECORDS with a newline between elements (one row per line)."""
    body = bind(statement, values).rstrip().rstrip(';')
    first = re.sub(r'^(\s*--[^\n]*\n)*\s*', '', body).split(None, 1)[0].lower()
    if first in ('with', 'select'):
        main = f"create temp table __out as {body};\nselect coalesce(jsonb_agg(__out), '[]'::jsonb) from __out;"
    else:
        main = f"with __r as ({body}) select coalesce(jsonb_agg(__r), '[]'::jsonb) from __r;"
    tail = after or "select '[]'::json"
    out = sql_run(f"{main}\n{tail};", SETUP + seed)
    lines = [l for l in out.splitlines() if l.strip()]
    return json.loads(lines[-2]), json.loads(lines[-1])


SNAP = ("select coalesce(json_agg(json_build_object('p', profile_id, 'a', authorized, 'n', notified_at is not null, 't', notify_attempts) "
        "order by profile_id), '[]'::json) from channel_snapshot")

with Case('A4 B Live Rows: every LIVE row with its owner (\'\' for none) and the previous snapshot; retired rows never') as c:
    seed = ("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('a', 'u', 'whatsapp', 'inst-1'), ('b', 'u', 'telegram', 'inst-1'),"
            " ('c', 'u', 'whatsapp', null);\n"
            "insert into bot_profiles (profile_id, app_user_id, channel, owner_id, deleted_at, deleted_reason) values ('d', 'u', 'whatsapp', 'inst-2', now(), 'trial_expiry');\n"
            "insert into channel_snapshot (profile_id, channel, authorized, seen_at, notified_at, notify_attempts) values"
            " ('a', 'whatsapp', true, now() - interval '5 minutes', null, 0), ('b', 'telegram', false, now() - interval '1 day', now(), 3),"
            " ('d', 'whatsapp', false, now(), null, 1);\n")
    body = q('Live Rows').rstrip().rstrip(';')
    got = rows_of(sql_run(f"select coalesce(jsonb_agg(x order by x.profile_id), '[]'::jsonb) from ({body}) x;", SETUP + seed))
    c.check(got == [{'profile_id': 'a', 'channel': 'whatsapp', 'owner_id': 'inst-1', 'prev_authorized': True, 'prev_notified': False, 'prev_attempts': 0},
                    {'profile_id': 'b', 'channel': 'telegram', 'owner_id': 'inst-1', 'prev_authorized': False, 'prev_notified': True, 'prev_attempts': 3},
                    {'profile_id': 'c', 'channel': 'whatsapp', 'owner_id': '', 'prev_authorized': None, 'prev_notified': False, 'prev_attempts': 0}],
            f'rows {got}')

UPSERT_SEED = ("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('a', 'u', 'whatsapp', 'inst-1'), ('b', 'u', 'telegram', 'inst-1'),"
               " ('c', 'u', 'whatsapp', 'inst-1'), ('e', 'u', 'telegram', 'inst-1');\n"
               "insert into bot_profiles (profile_id, app_user_id, channel, owner_id, deleted_at, deleted_reason) values ('d', 'u', 'whatsapp', 'inst-1', now(), 'app_delete');\n"
               "insert into channel_snapshot (profile_id, channel, authorized, seen_at, notified_at) values ('a', 'whatsapp', true, now() - interval '1 day', null),"
               " ('b', 'telegram', false, now() - interval '1 day', now() - interval '1 hour'), ('d', 'whatsapp', false, now() - interval '1 day', null),"
               " ('x', 'whatsapp', true, now() - interval '1 day', null);\n")
with Case('A4 B Upsert Snapshot: DOWN / UP / first sightings recorded, rows without a live bot_profiles row deleted, the owed notices handed back (owners only)') as c:
    writes = [write('a', False, False), write('c', False, True), write('e', True, False, ch='telegram'), write('b', True, False, ch='telegram')]
    alerts = [alert('a', owner='inst-1'), alert('z', owner='')]
    back, snap = run_sql(q('Upsert Snapshot'), [json.dumps(writes), json.dumps(alerts)], UPSERT_SEED, SNAP)
    c.check(back == [{'profile_id': 'a', 'channel': 'whatsapp', 'owner_id': 'inst-1', 'alert': True, 'written': 4, 'cleaned': 2}], f'returned {back}')
    c.check(snap == [{'p': 'a', 'a': False, 'n': False, 't': 1}, {'p': 'b', 'a': True, 'n': False, 't': 0},
                     {'p': 'c', 'a': False, 'n': True, 't': 0}, {'p': 'e', 'a': True, 'n': False, 't': 0}],
            f'snapshot {snap} — a DOWN owes (null) and its notice handed on is attempt 1, an UP resets (null, 0), a first '
            f'sighting already down owes nothing, d/x deleted')

with Case('A4 B Upsert Snapshot: seen_at is the time of the change; an unchanged row keeps its time') as c:
    _, snap = run_sql(q('Upsert Snapshot'), [json.dumps([write('a', False, False)]), '[]'], UPSERT_SEED,
                      "select json_object_agg(profile_id, seen_at > now() - interval '1 minute') from channel_snapshot")
    c.check(snap == {'a': True, 'b': False}, f'fresh seen_at per row: {snap}')

with Case('A4 B Upsert Snapshot: nothing to record and nothing owed — no row back, the cleanup still runs') as c:
    back, snap = run_sql(q('Upsert Snapshot'), ['[]', '[]'], UPSERT_SEED, SNAP)
    c.check(back == [], f'returned {back}')
    c.check([x['p'] for x in snap] == ['a', 'b'], f'snapshot {snap}')

# fix round 2 (2026-10-15): the attempts are COUNTED by this statement — before the send, as the Handoff Notifier counts at
# claim — and only what it counted comes back. o1 owed after 3 attempts; o2 owed after the 6th; o3 told; o4 up; o5 down and
# owed but its bot_profiles row retired.
RETRY_SEED = ("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('o1', 'u', 'whatsapp', 'O'), ('o2', 'u', 'whatsapp', 'O'),"
              " ('o3', 'u', 'whatsapp', 'O'), ('o4', 'u', 'whatsapp', 'O');\n"
              "insert into bot_profiles (profile_id, app_user_id, channel, owner_id, deleted_at, deleted_reason) values ('o5', 'u', 'whatsapp', 'O', now(), 'liveness');\n"
              "insert into channel_snapshot (profile_id, channel, authorized, seen_at, notified_at, notify_attempts) values"
              " ('o1', 'whatsapp', false, now() - interval '30 minutes', null, 3), ('o2', 'whatsapp', false, now() - interval '1 hour', null, 6),"
              " ('o3', 'whatsapp', false, now() - interval '1 hour', now(), 2), ('o4', 'whatsapp', true, now() - interval '1 hour', null, 0),"
              " ('o5', 'whatsapp', false, now() - interval '1 hour', null, 1);\n")
OWED_ALL = [alert(p, owner='O') for p in ('o1', 'o2', 'o3', 'o4', 'o5')]
TIMES = "select json_object_agg(profile_id, seen_at > now() - interval '1 minute') from channel_snapshot"

with Case('A4 B Upsert Snapshot: an owed notice is counted (one more attempt) and handed back only while fewer than six were made — '
          'never past the cap, never for a row told, up or retired, even when the Diff asks') as c:
    back, snap = run_sql(q('Upsert Snapshot'), ['[]', json.dumps(OWED_ALL)], RETRY_SEED, SNAP)
    c.check([x['profile_id'] for x in back] == ['o1'], f'returned {back}')
    c.check(snap == [{'p': 'o1', 'a': False, 'n': False, 't': 4}, {'p': 'o2', 'a': False, 'n': False, 't': 6},
                     {'p': 'o3', 'a': False, 'n': True, 't': 2}, {'p': 'o4', 'a': True, 'n': False, 't': 0}],
            f'snapshot {snap} — o1 counted 3 -> 4, o2 stays at the cap, o3/o4 untouched, o5 deleted with its row')
    _, times = run_sql(q('Upsert Snapshot'), ['[]', json.dumps(OWED_ALL)], RETRY_SEED, TIMES)
    c.check(times.get('o1') is False, f'a counted retry is no new state — seen_at (the support query\'s «down since») kept: {times}')

with Case('A4 B Upsert Snapshot: a DOWN handed on is attempt 1; a DOWN nobody can be told about (no alert) counts nothing; '
          'an UP resets the count') as c:
    back, snap = run_sql(q('Upsert Snapshot'), [json.dumps([write('o4', False, False)]), json.dumps([alert('o4', owner='O')])], RETRY_SEED, SNAP)
    c.check([x['profile_id'] for x in back] == ['o4'] and [x['t'] for x in snap if x['p'] == 'o4'] == [1], f'returned {back} snapshot {snap}')
    back, snap = run_sql(q('Upsert Snapshot'), [json.dumps([write('o4', False, False)]), '[]'], RETRY_SEED, SNAP)
    c.check(back == [] and [x['t'] for x in snap if x['p'] == 'o4'] == [0], f'returned {back} snapshot {snap}')
    back, snap = run_sql(q('Upsert Snapshot'), [json.dumps([write('o2', True, False)]), '[]'], RETRY_SEED, SNAP)
    c.check(back == [] and [(x['a'], x['n'], x['t']) for x in snap if x['p'] == 'o2'] == [(True, False, 0)], f'returned {back} snapshot {snap}')


# The watch's own cadence (fix round 2): ONE channel goes down for good and every notice fails.
CADENCE_SEED = ("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('cw-wa', 'u', 'whatsapp', 'O'),"
                " ('cw-tg', 'u', 'telegram', 'O');\n")


def cadence(graph=None, runs=9, shift=597):
    """Run after run of the watch on a WhatsApp that goes down at run 1 and stays down, while every notice fails (Wappi
    answered 500 — or delivered and answered as failed, the case the cap exists for): Live Rows → Diff Channel State →
    Upsert Snapshot of `graph`, its REAL code and statements, in ONE session (each statement commits with a real now()).
    Between two runs every recorded time moves back by `shift` seconds: the 10-minute period less the few seconds by which a
    DOWN run's upsert trails the next run's read — the seventh run then reads the recorded DOWN at T+60 min minus 18 s.
    Returns the notices handed to the send chain, run by run."""
    handed = []
    with PgSession(SETUP + CADENCE_SEED) as pg:
        for k in range(runs):
            rows = pg.q(q('Live Rows', graph))
            w, a, _ = diff(rows, lst(prof('cw-wa', k == 0)), lst(prof('cw-tg', True)), graph=graph)
            handed.append(len(pg.q(bind(q('Upsert Snapshot', graph), [json.dumps(w), json.dumps(a)]))))
            pg.x(f"update channel_snapshot set seen_at = seen_at - interval '{shift} seconds';")
    return handed


with Case('A4 B cadence: a channel down for good, every notice failing — EXACTLY six notices (the DOWN run + five retries), '
          'none at the seventh run (T+60 min minus 18 s) or after; the count, not the clock, decides') as c:
    got = cadence()
    c.check(got == [0, 1, 1, 1, 1, 1, 1, 0, 0], f'notices handed on per run {got} (sum {sum(got)})')
    got = cadence(shift=3600)
    c.check(got == [0, 1, 1, 1, 1, 1, 1, 0, 0], f'with an hour between runs (n8n was down): still six, never fewer — {got}')

PICK_SEED = ("insert into bot_profiles (profile_id, app_user_id, channel, owner_id, created_at) values"
             " ('wa1', 'u', 'whatsapp', 'O', '2026-10-01'), ('tg1', 'u', 'telegram', 'O', '2026-10-01'), ('wa2', 'u', 'whatsapp', 'O', '2026-09-01'),"
             " ('wa3', 'u', 'whatsapp', 'O', '2026-10-10'), ('wa5', 'u', 'whatsapp', 'O', '2026-10-12'), ('wa6', 'u', 'whatsapp', 'O', '2026-10-13'),"
             " ('wa7', 'u', 'whatsapp', 'P', '2026-10-13'), ('q1', 'u', 'whatsapp', 'Q', '2026-10-13');\n"
             "insert into bot_profiles (profile_id, app_user_id, channel, owner_id, created_at, deleted_at, deleted_reason) values"
             " ('wa4', 'u', 'whatsapp', 'O', '2026-10-14', now(), 'liveness');\n"
             "insert into channel_snapshot (profile_id, channel, authorized) values ('wa1', 'whatsapp', false), ('tg1', 'telegram', true),"
             " ('wa2', 'whatsapp', true), ('wa3', 'whatsapp', true), ('wa4', 'whatsapp', true), ('wa5', 'whatsapp', false), ('wa6', 'whatsapp', true),"
             " ('wa7', 'whatsapp', true), ('q1', 'whatsapp', true);\n")
# What THIS run's lists showed authorised (Diff's reachable ids): wa5 is down; wa6 VANISHED from Wappi's list — its snapshot
# still says authorised (the Diff skips a profile Wappi does not list) and it is O's newest WhatsApp, the stale route.
LISTED_UP = ['tg1', 'wa2', 'wa3', 'wa4', 'wa7', 'q1']


def pick(down, ch, owner, reachable, seed=PICK_SEED, graph=None):
    back, _ = run_sql(q('Pick Other Channel', graph), [down, ch, owner, json.dumps(reachable)], seed)
    return back


with Case('A4 B Pick Other Channel: another live channel of the SAME owner that THIS run lists authorised — WhatsApp first, then the newest') as c:
    got = pick('wa1', 'whatsapp', 'O', LISTED_UP)
    c.check(got == [{'down_profile_id': 'wa1', 'down_channel': 'whatsapp', 'other_profile_id': 'wa3', 'other_channel': 'whatsapp'}], f'wa1 down: {got}')
    c.check(pick('tg1', 'telegram', 'O', LISTED_UP)[0]['other_profile_id'] == 'wa3', 'tg1 down: WhatsApp first')
    c.check([x['other_profile_id'] for x in pick('wa1', 'whatsapp', 'O', ['tg1', 'wa7', 'q1'])] == ['tg1'], 'only Telegram listed for O: Telegram')

with Case('A4 B Pick Other Channel (fix round 2): a vanished profile with an authorised snapshot never takes the route — the notice goes '
          'to the channel that works; vanished only ⇒ nothing') as c:
    got = pick('wa1', 'whatsapp', 'O', ['tg1'])
    c.check([x['other_profile_id'] for x in got] == ['tg1'], f'wa6 (vanished, «authorised» in the snapshot, O\'s newest WhatsApp) + tg1 listed: {got}')
    c.check(pick('wa1', 'whatsapp', 'O', []) == [], 'nothing listed authorised this run: no route, whatever the snapshot says')

with Case('A4 B Pick Other Channel: never a retired row, another owner\'s, the down profile itself, no owner — even when listed') as c:
    c.check(pick('wa1', 'whatsapp', 'O', ['wa4']) == [], 'wa4 is retired')
    c.check(pick('wa1', 'whatsapp', 'O', ['wa7', 'q1']) == [], 'wa7 / q1 belong to P / Q')
    c.check(pick('wa7', 'whatsapp', 'P', LISTED_UP) == [], 'P has no other channel')
    c.check(pick('q1', 'whatsapp', 'Q', ['q1']) == [], 'the down profile is never its own other channel')
    c.check(pick('wa1', 'whatsapp', '', LISTED_UP) == [], 'no owner, no pick')

with Case('A4 B Pick Other Channel: a reachable list that is not a JSON array fails the statement (the node continues: no row, no send)') as c:
    try:
        run_sql(q('Pick Other Channel'), ['wa1', 'whatsapp', 'O', json.dumps({'tg1': True})], PICK_SEED)
        c.check(False, 'an object was accepted as the list of routes')
    except RuntimeError as e:
        c.check('cannot extract elements from an object' in str(e), f'unexpected failure: {e}')

with Case('A4 B Record Notified: stamps only a row still down and owed') as c:
    seed = ("insert into channel_snapshot (profile_id, channel, authorized, notified_at) values ('a', 'whatsapp', false, null), ('b', 'whatsapp', true, null),"
            " ('c', 'whatsapp', false, '2026-10-01');\n")
    for pid, want in (('a', 1), ('b', 0), ('c', 0), ('zz', 0)):
        back, snap = run_sql(q('Record Notified'), [pid], seed,
                             "select json_object_agg(profile_id, coalesce(to_char(notified_at, 'YYYY-MM-DD'), 'null')) from channel_snapshot")
        c.check(len(back) == want, f'{pid}: returned {back}')
        if pid == 'b':
            c.check(snap['b'] == 'null', 'a recovered row keeps notified_at null for the next DOWN')
        if pid == 'c':
            c.check(snap['c'] == '2026-10-01', 'an already told row keeps its time')


# ------------------------------------------------------------------------------------------------------------------
# C: the whole graph (notifier_chain.js: n8n's expression engine, alwaysOutputData / continue / paired items)
WA1, TG1, WA2, TG2 = 'prof-wa-1', 'prof-tg-1', 'prof-wa-2', 'prof-tg-2'
NAMED, ACCOUNT_RAW, ACCOUNT, WA_PHONE = '77010001111', '+7 (701) 000-99-94', '77010009994', '77010007777'
STATUS = {'byProfile': {
    TG1: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': TG1, 'phone': NAMED, 'account': {'phone': ACCOUNT_RAW}}},
    WA1: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': WA1, 'phone': WA_PHONE}},
    WA2: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': WA2, 'phone': '77010002222'}},
    TG2: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': TG2, 'phone': '77010003333'}}},
    'default': {'statusCode': 200, 'body': {'authorized': True, 'profile_id': '', 'phone': None}}}
SENT = {'default': {'statusCode': 200, 'body': {'status': 'done', 'message_id': 'notice-1'}}}


def picked(down, down_ch, other, other_ch):
    return {'down_profile_id': down, 'down_channel': down_ch, 'other_profile_id': other, 'other_channel': other_ch}


def sc(live, wa, tg, upsert, picks, status=STATUS, send=SENT, extra_pg=None):
    pg = {'Live Rows': {'rows': live}, 'Upsert Snapshot': upsert, 'Pick Other Channel': picks}
    pg.update(extra_pg or {})
    return {'pg': pg, 'http': {'List WA Profiles': {'default': wa}, 'List TG Profiles': {'default': tg}, 'Owner Phone': status, 'Send Notice': send}}


DOWN_WA = [row(WA1, prev=True), row(TG1, ch='telegram', prev=True)]
SCENARIOS = {
    'wa_down_told_through_telegram': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)),
                                        {'rows': [dict(alert(WA1), alert=True, written=1, cleaned=0)]}, {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]}),
    'tg_down_told_through_whatsapp': sc([row(WA1, prev=True), row(TG1, ch='telegram', prev=True)], lst(prof(WA1, True)), lst(prof(TG1, False)),
                                        {'rows': [dict(alert(TG1, ch='telegram'), alert=True, written=1, cleaned=0)]},
                                        {'rows': [picked(TG1, 'telegram', WA1, 'whatsapp')]}),
    'two_downs': sc([row(WA1, prev=True), row(TG1, ch='telegram', prev=True), row(WA2, prev=True), row(TG2, ch='telegram', prev=True)],
                    lst(prof(WA1, False), prof(WA2, True)), lst(prof(TG1, True), prof(TG2, False)),
                    {'rows': [dict(alert(WA1), alert=True), dict(alert(TG2, ch='telegram'), alert=True)]},
                    {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram'), picked(TG2, 'telegram', WA2, 'whatsapp')]}),
    'no_other_channel': sc([row(WA1, prev=True)], lst(prof(WA1, False)), lst(),
                           {'rows': [dict(alert(WA1), alert=True)]}, {'empty': True}),
    'nothing_owed': sc(DOWN_WA, lst(prof(WA1, True)), lst(prof(TG1, True)), {'empty': True}, {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]}),
    'upsert_failed': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)), {'queryError': 'relation "channel_snapshot" does not exist'},
                        {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]}),
    'live_rows_failed': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)), {'rows': [dict(alert(WA1), alert=True)]},
                           {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]}, extra_pg={'Live Rows': {'throw': 'timeout exceeded when trying to connect'}}),
    'send_failed': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)), {'rows': [dict(alert(WA1), alert=True)]},
                      {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]}, send={'default': {'statusCode': 500, 'body': {'status': 'error'}}}),
    'other_channel_unauthorized': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)), {'rows': [dict(alert(WA1), alert=True)]},
                                     {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]},
                                     status={'byProfile': {TG1: {'statusCode': 200, 'body': {'authorized': False, 'profile_id': TG1, 'phone': NAMED}}}}),
    'other_status_crossed': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)), {'rows': [dict(alert(WA1), alert=True)]},
                               {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]},
                               status={'byProfile': {TG1: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': 'someone-else', 'phone': '77019999999'}}}}),
    'other_status_failed': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)), {'rows': [dict(alert(WA1), alert=True)]},
                              {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]},
                              status={'byProfile': {TG1: {'statusCode': 502, 'body': {'status': 'error'}}}}),
    'other_without_phone': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)), {'rows': [dict(alert(WA1), alert=True)]},
                              {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]},
                              status={'byProfile': {TG1: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': TG1, 'phone': None}}}}),
    'pick_names_the_down_profile': sc(DOWN_WA, lst(prof(WA1, False)), lst(prof(TG1, True)), {'rows': [dict(alert(WA1), alert=True)]},
                                      {'rows': [picked(WA1, 'whatsapp', WA1, 'whatsapp')]}),
    'vanished_route_not_offered': sc([row(WA1, prev=True), row(WA2, prev=True), row(TG1, ch='telegram', prev=True)],
                                     lst(prof(WA1, False)), lst(prof(TG1, True)),
                                     {'rows': [dict(alert(WA1), alert=True)]}, {'rows': [picked(WA1, 'whatsapp', TG1, 'telegram')]}),
}


def run(graph, scenarios=SCENARIOS):
    with tempfile.NamedTemporaryFile('w', suffix='.json', encoding='utf-8') as f:
        json.dump(graph, f, ensure_ascii=False)
        f.flush()
        r = subprocess.run(['node', str(DRIVER), f.name], input=json.dumps(scenarios), text=True, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError('driver failed: ' + r.stderr.strip()[-1500:])
    return json.loads(r.stdout)


def http_calls(r, name=None):
    return [e for e in r['effects'] if e['kind'] == 'http' and (name is None or e['node'] == name)]


def db_calls(r, name):
    return [e for e in r['effects'] if e['kind'] == 'db' and e['node'] == name]


def sends(r):
    return sorted(((e.get('query') or {}).get('profile_id'), e['url'], (e.get('body') or {}).get('recipient'), (e.get('body') or {}).get('body'))
                  for e in http_calls(r, 'Send Notice'))


def recorded(r):
    return sorted(e['params'][0] for e in db_calls(r, 'Record Notified'))


EXPECT = {   # scenario -> (sends [(from profile, url, recipient, text)], Record Notified [down profiles])
    'wa_down_told_through_telegram': ([(TG1, TAPI + 'sync/message/send', ACCOUNT, WA_TEXT)], [WA1]),
    'tg_down_told_through_whatsapp': ([(WA1, API + 'sync/message/send', WA_PHONE, TG_TEXT)], [TG1]),
    'two_downs': (sorted([(TG1, TAPI + 'sync/message/send', ACCOUNT, WA_TEXT), (WA2, API + 'sync/message/send', '77010002222', TG_TEXT)]), [TG2, WA1]),
    'no_other_channel': ([], []),
    'nothing_owed': ([], []),
    'upsert_failed': ([], []),
    'live_rows_failed': ([], []),
    'send_failed': ([(TG1, TAPI + 'sync/message/send', ACCOUNT, WA_TEXT)], []),
    'other_channel_unauthorized': ([], []),
    'other_status_crossed': ([], []),
    'other_status_failed': ([], []),
    'other_without_phone': ([], []),
    'pick_names_the_down_profile': ([], []),
    'vanished_route_not_offered': ([(TG1, TAPI + 'sync/message/send', ACCOUNT, WA_TEXT)], [WA1]),
}
TITLES = {
    'wa_down_told_through_telegram': 'a WhatsApp DOWN: ONE notice through the owner\'s Telegram, to the account\'s own number (A5 rule), recorded',
    'tg_down_told_through_whatsapp': 'a Telegram DOWN: ONE notice through the owner\'s WhatsApp, recorded',
    'two_downs': 'two DOWNs of one tick: one notice each, each through ITS other channel, each recorded on its own row',
    'no_other_channel': 'no other channel (Pick empty): no status call, no notice, nothing recorded — the app marks the channel itself',
    'nothing_owed': 'nothing owed (Upsert Snapshot hands back no row): no pick, no notice',
    'upsert_failed': 'the snapshot write fails: the run stops there (an error execution) — no notice without its snapshot',
    'live_rows_failed': 'the live read fails: the run stops there — nothing written, nobody told',
    'send_failed': 'the send fails: nothing recorded — still owed, retried next run',
    'other_channel_unauthorized': 'the other channel says it is not authorised: no send, nothing recorded',
    'other_status_crossed': 'the other channel\'s status names another profile (Wappi crossing): no send, nothing recorded',
    'other_status_failed': 'the other channel\'s status fails: no send, nothing recorded',
    'other_without_phone': 'the other channel has no number: no send, nothing recorded',
    'pick_names_the_down_profile': 'a pick naming the down profile itself never sends (Has Other Channel?)',
    'vanished_route_not_offered': 'a second WhatsApp vanished from Wappi\'s list (its snapshot still «authorised»): the notice goes through '
                                  'the owner\'s Telegram — Pick is offered only what THIS run listed authorised (fix round 2)',
}
R = run(W)
STOPS = {'upsert_failed': 'Upsert Snapshot', 'live_rows_failed': 'Live Rows'}
for name, (want_sends, want_rec) in EXPECT.items():
    with Case(f'A4 C [{name}]: {TITLES[name]}') as c:
        r = R[name]
        if name in STOPS:
            c.check((r['stoppedAt'] or {}).get('node') == STOPS[name], f'the run must stop at {STOPS[name]}: {r["stoppedAt"]}')
        else:
            c.check(r['stoppedAt'] is None, f'stopped at {r["stoppedAt"]}')
        c.check(sends(r) == want_sends, f'sends {sends(r)} != {want_sends}')
        c.check(recorded(r) == sorted(want_rec), f'Record Notified {recorded(r)} != {sorted(want_rec)}')
        lists = sorted(e['url'] for e in http_calls(r) if e['node'] in ('List WA Profiles', 'List TG Profiles'))
        c.check(lists == [API + 'profile/all/get', TAPI + 'profile/all/get'], f'the two lists: {lists}')
        if name in ('no_other_channel', 'nothing_owed', 'upsert_failed', 'live_rows_failed', 'pick_names_the_down_profile'):
            c.check(http_calls(r, 'Owner Phone') == [], f'no status call: {http_calls(r, "Owner Phone")}')

with Case('A4 C: the status call reads the OTHER channel; the snapshot write carries Diff\'s decision') as c:
    r = R['wa_down_told_through_telegram']
    ph = [(e['url'], (e.get('query') or {}).get('profile_id')) for e in http_calls(r, 'Owner Phone')]
    c.check(ph == [(TAPI + 'sync/get/status', TG1)], f'Owner Phone {ph}')
    up = db_calls(r, 'Upsert Snapshot')
    c.check(len(up) == 1 and json.loads(up[0]['params'][0]) == [write(WA1, False, False)] and json.loads(up[0]['params'][1]) == [alert(WA1)],
            f'Upsert Snapshot params {up}')

with Case('A4 C (fix round 2): Pick Other Channel gets THIS run\'s reachable ids from Diff Channel State as its fourth parameter') as c:
    for name, ids in {'wa_down_told_through_telegram': [TG1], 'two_downs': [WA2, TG1], 'vanished_route_not_offered': [TG1]}.items():
        p = db_calls(R[name], 'Pick Other Channel')
        c.check(bool(p) and all(len(x['params']) == 4 and json.loads(x['params'][3]) == ids for x in p), f'{name}: Pick params {p}')

with Case('A4 C: the notice carries no client data — the text is exactly the channel\'s line') as c:
    for name in ('wa_down_told_through_telegram', 'tg_down_told_through_whatsapp', 'two_downs'):
        for _, _, _, text in sends(R[name]):
            c.check(text in (WA_TEXT, TG_TEXT), f'{name}: {text!r}')


# ------------------------------------------------------------------------------------------------------------------
# D: the verifier holds the structure; its mutations are rejected, and what behaviour can see is seen here too
def run_verifier(graph):
    with tempfile.TemporaryDirectory() as d:
        for f in os.listdir(N8N / 'workflows'):
            if f.endswith('.json'):
                with open(N8N / 'workflows' / f, encoding='utf-8') as src, open(os.path.join(d, f), 'w', encoding='utf-8') as dst:
                    dst.write(src.read())
        with open(os.path.join(d, FILE), 'w', encoding='utf-8') as fh:
            json.dump(graph, fh, indent=2, ensure_ascii=False)
        r = subprocess.run([sys.executable, str(VERIFIER), '--dir', d], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


def decided(rows, wa, tg=None, graph=None):
    """Everything the Diff decides — writes, alerts, routes, stats — minus its clock: stats.ranAt differs on every run, and
    comparing it called every mutation «visible» (fix round 2)."""
    j = diff_json(rows, wa, tg, graph)
    return j.get('writes'), j.get('alerts'), j.get('reachable'), {k: v for k, v in (j.get('stats') or {}).items() if k != 'ranAt'}


def diff_exposes(rows, wa, tg=None):
    """The mutated Diff decides differently from the canonical one on this fixture."""
    def check(bad):
        return decided(rows, wa, tg, graph=bad) != decided(rows, wa, tg)
    return check


def graph_exposes(scenario):
    def check(bad):
        r = run(bad, {scenario: SCENARIOS[scenario]})[scenario]
        want_sends, want_rec = EXPECT[scenario]
        return sends(r) != want_sends or recorded(r) != sorted(want_rec) or r['stoppedAt'] != R[scenario]['stoppedAt'] \
            or any(e['node'] == 'Pick Other Channel' for e in r['effects']) != any(e['node'] == 'Pick Other Channel' for e in R[scenario]['effects'])
    return check


def sql_exposes(down, ch, owner, reachable, seed=PICK_SEED):
    def check(bad):
        return pick(down, ch, owner, reachable, seed, graph=bad) != pick(down, ch, owner, reachable, seed)
    return check


def upsert_exposes(writes, alerts, seed=RETRY_SEED):
    """The mutated Upsert Snapshot hands back or counts differently from the canonical one on this fixture."""
    def check(bad):
        args = [json.dumps(writes), json.dumps(alerts)]
        return run_sql(q('Upsert Snapshot', bad), args, seed, SNAP) != run_sql(q('Upsert Snapshot'), args, seed, SNAP)
    return check


FLOOR_FIX = ([row('a', prev=True)], lst())
EXPOSED_BY = {
    'the empty-list floor removed': diff_exposes(*FLOOR_FIX),
    'the cap removed': diff_exposes(many(4) + [row('t', ch='telegram', prev=True)], lst(*[prof(f'p{i}', i >= 3) for i in range(4)]), lst(prof('t', False))),
    'the ambiguity rule removed (a non-boolean flag read as down)': diff_exposes([row('a')], lst(prof('a', 'false'))),
    'owed notices retried for the whole outage (the attempts cap removed from the Diff)': diff_exposes([row('a', prev=False, attempts=6)], lst(prof('a', False))),
    'a seventh attempt counted (the attempts cap removed from the statement)': upsert_exposes([], [alert('o2', owner='O')]),
    'every owed alert handed back, counted or not (the attempts bypassed)': upsert_exposes([], OWED_ALL),
    "the route through the recorded snapshot (Pick without this run's list)": sql_exposes('wa1', 'whatsapp', 'O', ['tg1']),
    "a channel this run's list shows down offered as a route (reachable without the boolean true)":
        diff_exposes([row('a', prev=True), row('b', prev=True)], lst(prof('a', False), prof('b', True))),
    'a capped base still routes (reachable handed on before the cap)':
        diff_exposes(many(4) + [row('t', ch='telegram', prev=True)], lst(*[prof(f'p{i}', i >= 3) for i in range(4)]), lst(prof('t', True))),
    'the notice text inlined (NOTICE_TEXT bypassed)': graph_exposes('wa_down_told_through_telegram'),
    'the notice text altered (a prefix check would pass it)': graph_exposes('wa_down_told_through_telegram'),
    'a base trusted without status done': diff_exposes([row('a', prev=True)], lst(prof('a', False), wstatus='error')),
    'the notice to the SAME down channel (Pick Other Channel names the down profile)': sql_exposes('q1', 'whatsapp', 'Q', ['q1']),
    'the notice sent from the DOWN profile (Compose Notice)': graph_exposes('wa_down_told_through_telegram'),
    "another owner's channel (Pick Other Channel without the owner)": sql_exposes('wa7', 'whatsapp', 'P', LISTED_UP),
    'Down Alerts? bypassed (every Upsert Snapshot item goes on)': graph_exposes('nothing_owed'),
    'an undelivered notice recorded as told (Notice Sent? bypassed)': graph_exposes('send_failed'),
    'the owner\'s number by the top-level phone only (the A5 rule dropped)': graph_exposes('wa_down_told_through_telegram'),
    'Upsert Snapshot continues on a database fault': graph_exposes('upsert_failed'),
}

vcw, vcw_error = None, None
try:
    vcw = module('verify-channel-watch.py')
except Exception as e:
    vcw_error = e

with Case('A4 D: verify-channel-watch accepts the canonical channel watch') as c:
    rc, out = run_verifier(W)
    c.check(rc == 0, f'canonical rejected: {out[-800:]}')

with Case('A4 D: the verifier knows its mutations (incl. disabled nodes), and EXPOSED_BY names only real ones') as c:
    muts = getattr(vcw, 'MUTATIONS', None) if vcw else None
    c.check(bool(muts), f'verify-channel-watch.py has no MUTATIONS (import error: {vcw_error!r})')
    if muts:
        labels = [m[0] for m in muts]
        for need in ('disabled', 'floor', 'cap', 'ambiguity', 'SAME down channel', 'attempts', "this run's list", 'notice text'):
            c.check(any(need in l for l in labels), f'no {need!r} mutation among {labels}')
        c.check(set(EXPOSED_BY) <= set(labels), f'EXPOSED_BY names unknown mutations: {sorted(set(EXPOSED_BY) - set(labels))}')

if vcw and getattr(vcw, 'MUTATIONS', None):
    for label, mutate, expect in vcw.MUTATIONS:
        with Case(f'A4 D NEG [{label}]: the verifier rejects it, naming the broken rule') as c:
            bad = copy.deepcopy(W)
            mutate(bad)
            c.check(bad != W, 'the mutation did not apply')
            rc, out = run_verifier(bad)
            c.check(rc == 1, f'exit {rc} (expected 1): {out[-600:]}')
            c.check(expect in out, f'the failure does not name {expect!r}: {out[-600:]}')
            seen = EXPOSED_BY.get(label)
            if seen is not None:
                c.check(seen(bad), 'the mutation is invisible in behaviour (the Diff / the statements / the graph decide as the canon does)')

# ------------------------------------------------------------------------------------------------------------------
# E: the generator
with Case('A4 E: build-channel-watch --check is clean and the canonical file is its output') as c:
    r = subprocess.run([sys.executable, str(N8N / 'build-channel-watch.py'), '--check'], capture_output=True, text=True)
    c.check(r.returncode == 0, f'--check: {r.stdout}{r.stderr}')
    c.check(W == B.build(), 'the canonical workflow differs from the generator')

with Case('A4 E: Compose Notice embeds the A5 rule (build-handoff-workflows.OWNER_PHONE_JS) verbatim, once') as c:
    js = node(W, 'Compose Notice')['parameters']['jsCode']
    c.check(HW.OWNER_PHONE_JS in js, 'OWNER_PHONE_JS is not embedded verbatim')
    c.check(js.count('function ownerPhone(') == 1, 'ownerPhone must be defined once')

with Case('A4 E (fix round 2): the notice text is build-channel-watch.NOTICE_TEXT — the full text, embedded once, no inline copy') as c:
    c.check(B.NOTICE_TEXT == {'whatsapp': WA_TEXT, 'telegram': TG_TEXT}, f'NOTICE_TEXT {B.NOTICE_TEXT}')
    js = node(W, 'Compose Notice')['parameters']['jsCode']
    c.check(js.count(WA_TEXT) == 1 and js.count(TG_TEXT) == 1 and js.count('отключился') == 2,
            'each text must appear exactly once — in the embedded constant')

report()
