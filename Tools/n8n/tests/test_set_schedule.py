#!/usr/bin/env python3
"""B1 «расписание бота» (2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
§4 B1): the owner's schedule reaches the server through ONE webhook, crSetSchedule001 «Set Schedule»
(POST /webhook/SetSchedule {profileIds, enabled, daysMask, startMin, endMin, timeZone, version}) — the contract of Set
Reply Mode v2: one version-guarded upsert for every profile of the bot, a per-profile report, 400 for a malformed body,
500 for a database fault, 401 / 403 from the gate.

Replayed exactly as the canonical graph runs it — the REAL Auth Gate (token.js + gate.js, synthetic secret), the REAL
Validate and Shape Response Code bodies, the REAL queryReplacement expression (n8n's own engine) and the REAL statement —
against a session temp table of the staging Postgres that carries the REAL zone trigger of the migration
(sql/2026-10-14-bot-schedule.sql: its own `create trigger` statement, re-targeted at the temp copy because LIKE does not
copy triggers, over public.bot_schedule_tz_check). Every scenario runs for a WhatsApp-shaped and a Telegram-shaped
profile.

  S1  a fresh write applies; the row holds the request; a NEWER version applies over it
  S2  a stale (older) and a replayed (same) version are not applied — the report names the server's version, the row stays
  S3  per profile: a bot-wide call for two profiles where one already holds a newer version → applied [false, true];
      the report follows the request's order
  S4  the ids: '-1' / '' dropped, duplicates once, at most ten; nothing left → 400 profileIds
  S5  every malformed field → 400 bad_request naming it (and nothing written)
  S6  a zone that passes the shape but Postgres does not know (Europe/Nowhere): the trigger refuses the statement → 400
      bad_timezone, nothing written; any other fault → 500 db_unavailable
  S7  the gate: token only (no token → 401 even inside the compat window), another install's profile → 403
      forbidden:profileIds before anything runs
  S8  crSetSchedule001 is a protected infra id in build-client-webhooks.PROTECTED_IDS and in the four dependents
  S9  the trigger fires only when a zone is WRITTEN (insert, or an update that names tz): a version bump and the bind's
      profile_id move never re-validate the stored zone; an update naming tz is refused
  V   verify-set-schedule.py passes the canon and rejects scratch copies that open the gate, skip a check, drop the version
      guard, write past a refusal, answer a fault as success, or disable a node.

    python3 Tools/n8n/tests/test_set_schedule.py
"""
import json
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from harness import N8N, WF, Case, PgSession, bind, code, expr, module, node, report, run_js, wf

FILE = 'crSetSchedule001-Set_Schedule.json'
VERIFY = N8N / 'verify-set-schedule.py'
SS = wf(FILE)
B = module('build-set-schedule.py')
MIGRATION = N8N / 'sql' / '2026-10-14-bot-schedule.sql'


def migration_trigger():
    """The migration's OWN create trigger statement, re-targeted at the session's temp copy (LIKE copies no trigger) —
    so the event clause under test is the file's, never a copy typed here."""
    sql = MIGRATION.read_text(encoding='utf-8')
    found = re.findall(r'^create trigger bot_schedule_tz_check\b.*?;', sql, re.M | re.S)
    assert len(found) == 1 and found[0].count(' on public.bot_schedule\n') == 1, f'the migration\'s create trigger: {found}'
    return found[0].replace(' on public.bot_schedule\n', ' on pg_temp.bot_schedule\n')


SETUP = "create temp table bot_schedule (like public.bot_schedule including all);\n" + migration_trigger()
PROFILES = {'WA': ('wa-sched-1', 'wa-sched-2'), 'TG': ('tg-sched-1', 'tg-sched-2')}
RA = '$RCAnonymousID:' + '0123456789abcdef' * 2


def token(claims):
    return run_js("""const s = require('crypto');
const b64u = (b) => Buffer.from(b).toString('base64').replace(/\\+/g,'-').replace(/\\//g,'_').replace(/=+$/,'');
const p = b64u(JSON.stringify($input.first().json.claims));
return [{json: {t: 'v1.' + p + '.' + b64u(s.createHmac('sha256', 'synthetic-test-secret').update('v1.' + p).digest())}}];""",
                  {'json': {'claims': {'c': [], 'e': int(time.time()) + 3600, 'n': 1, 'i': 'inst-A', 'u': RA, 'w': [], **claims}}})[0]['json']['t']


def gate(tok, body, compat_open=False):
    headers = {'authorization': 'Bearer ' + tok} if tok else {}
    env = {'CR_AUTH_COMPAT_UNTIL': '2099-01-01'} if compat_open else {}
    return code(SS, 'Auth Gate', {'json': {'headers': headers, 'body': body}}, env=env)[0]['json']


def cond(name, json_):
    return expr(node(SS, name)['parameters']['conditions']['conditions'][0]['leftValue'], json_)


def body(pids, version, enabled=True, days=31, start=540, end=1080, tz='Asia/Almaty', **extra):
    return {'profileIds': list(pids), 'enabled': enabled, 'daysMask': days, 'startMin': start, 'endMin': end, 'timeZone': tz,
            'version': version, **extra}


def run_statement(pg, sql):
    """The statement inside a plpgsql exception block: a refusal (the zone trigger, a constraint) rolls the statement's
    effects back exactly as a failed statement does, and its message is recorded — n8n's continue-on-error item is
    {message, error:{message}} with the server's primary message (Postgres v2, single batch) — instead of ending the
    session (pg.sh psql runs with ON_ERROR_STOP; the session's temp tables would go with it). Success: the statement's
    rows, in its own order (a fresh temp table read back sequentially)."""
    pg.x('drop table if exists pg_temp.b1_rows; drop table if exists pg_temp.b1_err; create temp table b1_err (msg text);')
    assert '$q$' not in sql and '$do$' not in sql
    pg.x('do $do$ begin execute $q$create temp table b1_rows as ' + sql.strip().rstrip(';') + '$q$; '
         'exception when others then insert into b1_err values (sqlerrm); end $do$;')
    err = pg.q('select msg from b1_err')
    if err:
        return [{'message': err[0]['msg'], 'error': {'message': err[0]['msg']}}]
    return pg.q('select * from b1_rows')


def call(pg, owned, b, tok=None, compat_open=False):
    """One webhook call through the canonical graph: Auth Gate → Authorized? → Validate → Valid? → Upsert Schedule →
    Shape Response → Respond. Returns (HTTP status, answer body, the nodes that ran past the gate)."""
    g = gate(token({'p': list(owned)}) if tok is None else tok, b, compat_open)
    if not cond('Authorized?', g):
        r = node(SS, 'Respond 401')['parameters']
        return expr(r['options']['responseCode'], g), expr(r['responseBody'], g), []
    v = code(SS, 'Validate', {'json': g})[0]['json']
    if not cond('Valid?', v):
        r = node(SS, 'Respond 400')['parameters']
        return r['options']['responseCode'], expr(r['responseBody'], v), ['Validate']
    up = node(SS, 'Upsert Schedule')
    params = expr(up['parameters']['options']['queryReplacement'], v)
    rows = run_statement(pg, bind(up['parameters']['query'], params))
    out = code(SS, 'Shape Response', items=[{'json': r} for r in rows] or [{'json': {}}], named={'Validate': v})[0]['json']
    resp = node(SS, 'Respond')['parameters']
    return expr(resp['options']['responseCode'], out), expr(resp['responseBody'], out), ['Validate', 'Upsert Schedule']


def row(pg, pid):
    r = pg.q(f"select enabled, days_mask, start_min, end_min, tz, version from bot_schedule where profile_id = '{pid}'")
    return r[0] if r else None


def n_rows(pg):
    return pg.q('select count(*)::int as n from bot_schedule')[0]['n']


for ch, (P1, P2) in PROFILES.items():
    with Case(f'B1 S1 [{ch}] a fresh write applies (the row holds the request); a NEWER version applies over it') as c:
        with PgSession(SETUP) as pg:
            st, d, _ = call(pg, [P1], body([P1], 1000))
            c.check(st == 200 and d == {'success': True, 'results': [{'profileId': P1, 'applied': True, 'version': 1000}]}, f'{st} {d}')
            c.check(row(pg, P1) == {'enabled': True, 'days_mask': 31, 'start_min': 540, 'end_min': 1080, 'tz': 'Asia/Almaty', 'version': 1000},
                    f'row {row(pg, P1)}')
            st, d, _ = call(pg, [P1], body([P1], 2000, enabled=False, days=96, start=1320, end=360, tz='Europe/Moscow'))
            c.check(st == 200 and d['results'] == [{'profileId': P1, 'applied': True, 'version': 2000}]
                    and row(pg, P1) == {'enabled': False, 'days_mask': 96, 'start_min': 1320, 'end_min': 360, 'tz': 'Europe/Moscow', 'version': 2000},
                    f'{st} {d} row {row(pg, P1)}')

    with Case(f'B1 S2 [{ch}] a stale (older) or replayed (same) version is not applied — the report names the server\'s version, '
              'the row stays') as c:
        with PgSession(SETUP) as pg:
            call(pg, [P1], body([P1], 2000, days=127))
            before = row(pg, P1)
            st1, d1, _ = call(pg, [P1], body([P1], 1500, days=1))
            st2, d2, _ = call(pg, [P1], body([P1], 2000, days=2))
            st3, d3, _ = call(pg, [P1], body([P1], '1999', days=3))
            c.check(st1 == 200 and d1['results'] == [{'profileId': P1, 'applied': False, 'version': 2000}], f'stale {st1} {d1}')
            c.check(st2 == 200 and d2['results'] == [{'profileId': P1, 'applied': False, 'version': 2000}], f'replay {st2} {d2}')
            c.check(st3 == 200 and d3['results'] == [{'profileId': P1, 'applied': False, 'version': 2000}], f'stale (string version) {st3} {d3}')
            c.check(row(pg, P1) == before and before['days_mask'] == 127, f'the row moved: {before} → {row(pg, P1)}')

    with Case(f'B1 S3 [{ch}] per profile: a bot-wide call for two profiles where one already holds a newer version → applied '
              '[false, true], one statement') as c:
        with PgSession(SETUP) as pg:
            call(pg, [P1], body([P1], 5000, days=1))
            st, d, _ = call(pg, [P1, P2], body([P1, P2], 4000, days=2))
            got = {r['profileId']: (r['applied'], r['version']) for r in d.get('results', [])}
            c.check(st == 200 and got == {P1: (False, 5000), P2: (True, 4000)}, f'{st} {d}')
            c.check(row(pg, P1)['days_mask'] == 1 and row(pg, P2)['days_mask'] == 2, f'{row(pg, P1)} {row(pg, P2)}')
            st, d, _ = call(pg, [P1, P2], body([P2, P1], 6000, days=4))
            c.check(st == 200 and [r['profileId'] for r in d['results']] == [P2, P1], f'the report follows the request order: {d}')

    with Case(f'B1 S4 [{ch}] the ids: \'-1\' and \'\' dropped, duplicates once, at most ten; nothing left → 400 profileIds') as c:
        with PgSession(SETUP) as pg:
            st, d, _ = call(pg, [P1], body(['-1', P1, '', P1], 100))
            c.check(st == 200 and d['results'] == [{'profileId': P1, 'applied': True, 'version': 100}] and n_rows(pg) == 1, f'{st} {d}')
            many = [f'{P1}-{i}' for i in range(12)]
            st, d, _ = call(pg, many, body(many, 200))
            c.check(st == 200 and [r['profileId'] for r in d['results']] == many[:B.MAX_PROFILES] and n_rows(pg) == 1 + B.MAX_PROFILES,
                    f'{st} {[r["profileId"] for r in d.get("results", [])]} rows {n_rows(pg)}')
            st, d, ran = call(pg, [P1], body(['-1', ''], 300))
            c.check(st == 400 and d == {'success': False, 'error': 'bad_request', 'reason': 'profileIds'} and 'Upsert Schedule' not in ran, f'{st} {d}')
            g = gate(token({'p': [P1]}), body([P1, 'someone-else'], 400))
            v = code(SS, 'Validate', {'json': {**g, 'crAuth': {**g['crAuth'], 'ok': True}}})[0]['json']
            c.check(v.get('profileIds') == [P1], f'Validate must re-intersect with the token even past the gate: {v}')

    with Case(f'B1 S5 [{ch}] every malformed field → 400 bad_request naming it, nothing written') as c:
        with PgSession(SETUP) as pg:
            bad = {'enabled': body([P1], 1, enabled='yes'), 'daysMask': body([P1], 1, days=128), 'startMin': body([P1], 1, start=1440),
                   'endMin': body([P1], 1, end=-1), 'timeZone': body([P1], 1, tz='Asia/Almaty; drop table x'),
                   'version': body([P1], 0), 'profileIds': {**body([P1], 1), 'profileIds': P1}}
            bad['version (missing)'] = {k: v for k, v in body([P1], 1).items() if k != 'version'}
            bad['daysMask (float)'] = body([P1], 1, days=3.5)
            bad['timeZone (too long)'] = body([P1], 1, tz='Area/' + 'x' * 70)
            wrong = []
            for label, b in bad.items():
                st, d, ran = call(pg, [P1], b)
                if st != 400 or d.get('error') != 'bad_request' or d.get('reason') != label.split(' ')[0] or 'Upsert Schedule' in ran:
                    wrong.append(f'{label}: {st} {d}')
            c.check(not wrong and n_rows(pg) == 0, f'{wrong} rows {n_rows(pg)}')
            st, d, _ = call(pg, [P1], body([P1], 1, tz='Etc/GMT+5'))
            st2, d2, _ = call(pg, [P1], body([P1], 2, tz='UTC'))
            st3, d3, _ = call(pg, [P1], body([P1], 3, tz='America/Argentina/Buenos_Aires'))
            c.check((st, st2, st3) == (200, 200, 200) and row(pg, P1)['tz'] == 'America/Argentina/Buenos_Aires', f'{st} {st2} {st3} {d3}')

    with Case(f'B1 S6 [{ch}] Europe/Nowhere passes the shape and is refused by the table\'s trigger → 400 bad_timezone, nothing '
              'written; any other database fault → 500 db_unavailable') as c:
        with PgSession(SETUP) as pg:
            g = gate(token({'p': [P1]}), body([P1], 7, tz='Europe/Nowhere'))
            v = code(SS, 'Validate', {'json': g})[0]['json']
            c.check(v.get('ok') is True and v.get('timeZone') == 'Europe/Nowhere', f'the shape must pass it (the trigger decides): {v}')
            st, d, ran = call(pg, [P1], body([P1], 7, tz='Europe/Nowhere'))
            c.check(st == 400 and d == {'success': False, 'error': 'bad_timezone'} and 'Upsert Schedule' in ran and n_rows(pg) == 0, f'{st} {d}')
            call(pg, [P1], body([P1], 8))
            st, d, _ = call(pg, [P1], body([P1], 9, tz='Europe/Nowhere'))
            c.check(st == 400 and row(pg, P1)['version'] == 8 and row(pg, P1)['tz'] == 'Asia/Almaty', f'an update is checked too: {st} {d} {row(pg, P1)}')
            pg.x(f"alter table bot_schedule add constraint b1_fault check (profile_id <> '{P2}');")
            st, d, _ = call(pg, [P1, P2], body([P1, P2], 10))
            c.check(st == 500 and d == {'success': False, 'error': 'db_unavailable'} and row(pg, P1)['version'] == 8, f'{st} {d} {row(pg, P1)}')
            out = code(SS, 'Shape Response', items=[{'json': {}}], named={'Validate': v})[0]['json']
            c.check(out.get('_status') == 500 and out.get('success') is False, f'an empty item (alwaysOutputData) is never success: {out}')

    with Case(f'B1 S7 [{ch}] the gate: token only (no token → 401, also inside the compat window), another install\'s profile '
              '→ 403 before anything runs') as c:
        with PgSession(SETUP) as pg:
            st, d, ran = call(pg, [], body([P1], 1), tok='')
            st2, d2, ran2 = call(pg, [], body([P1], 1), tok='', compat_open=True)
            c.check(st == 401 and d.get('success') is False and d.get('reason') == 'unauthorized:missing' and not ran
                    and st2 == 401 and not ran2, f'{st} {d} / compat {st2} {d2}')
            st, d, ran = call(pg, [P1], body([P2], 1))
            c.check(st == 403 and d.get('reason') == 'forbidden:profileIds' and not ran and n_rows(pg) == 0, f'{st} {d}')
            st, d, ran = call(pg, [P1], body([P1], 1), tok='v1.e30.forged')
            c.check(st == 401 and not ran, f'forged: {st} {d}')

    with Case(f'B1 S9 [{ch}] the trigger fires only when a zone is WRITTEN (insert, or an update naming tz): a stored zone '
              'the trigger would refuse (seeded with the trigger off) takes a version bump and the bind\'s profile_id move '
              'without a refusal; an update naming tz is refused') as c:
        with PgSession(SETUP) as pg:
            pg.x('alter table pg_temp.bot_schedule disable trigger bot_schedule_tz_check;')
            pg.x(f"insert into bot_schedule (profile_id, enabled, days_mask, start_min, end_min, tz, version, updated_at) "
                 f"values ('{P1}', true, 31, 540, 1080, 'Europe/Nowhere', 5, now());")
            pg.x('alter table pg_temp.bot_schedule enable trigger bot_schedule_tz_check;')
            bump = run_statement(pg, f"with u as (update bot_schedule set version = version + 1, updated_at = now() "
                                     f"where profile_id = '{P1}' returning version) select version from u")
            moved = run_statement(pg, f"with u as (update bot_schedule set profile_id = '{P2}' where profile_id = '{P1}' "
                                      f"returning profile_id) select profile_id from u")
            named = run_statement(pg, f"with u as (update bot_schedule set tz = tz where profile_id = '{P2}' returning tz) select tz from u")
            c.check(bump == [{'version': 6}], f'a version bump must not re-validate the stored zone: {bump}')
            c.check(moved == [{'profile_id': P2}], f'the bind\'s profile_id move must not re-validate it: {moved}')
            c.check(len(named) == 1 and B.TZ_MARKER in str(named[0].get('message')), f'an update naming tz must be refused: {named}')
            c.check(row(pg, P2) == {'enabled': True, 'days_mask': 31, 'start_min': 540, 'end_min': 1080, 'tz': 'Europe/Nowhere', 'version': 6}
                    and row(pg, P1) is None, f'the row after the three writes: {row(pg, P2)} / {row(pg, P1)}')

with Case('B1 S8: crSetSchedule001 is a protected infra id — build-client-webhooks.PROTECTED_IDS, clone_lineage.PROTECTED and the '
          'four dependents regenerated from it') as c:
    bcw, cl = module('build-client-webhooks.py'), module('clone_lineage.py')
    c.check(B.WF_ID in bcw.PROTECTED_IDS and B.WF_ID in cl.PROTECTED, 'not in PROTECTED_IDS / clone_lineage.PROTECTED')
    sws = next(n for n in wf('0DP79STI3pafXO9B-Set_Workflow_State.json')['nodes'] if 'const PROTECTED' in n['parameters'].get('jsCode', ''))
    c.check(f"'{B.WF_ID}'" in sws['parameters']['jsCode'], 'Set Workflow State does not protect it')
    for f, names in (('XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', ('Clone Plan', 'Bind Workflow Id')),
                     ('Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', ('Clone Plan', 'Bind Workflow Id')),
                     ('lmjYsdNcQA2IE5rl-Delete_Bot_Files.json', ('List Bot Originals', 'Retire Bot Profiles', 'Delete Bot Chunks', 'Delete Chat Memory')),
                     ('fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json', ('Clone Retirements',))):
        w = wf(f)
        missing = [n for n in names if f"'{B.WF_ID}'" not in node(w, n)['parameters'].get('query', '')]
        c.check(not missing, f'{f}: {missing} do not exclude {B.WF_ID}')


# ------------------------------------------------------------------------------------------------ V: the verifier, on scratch
def run_verifier(directory=None):
    r = subprocess.run([sys.executable, str(VERIFY)] + (['--dir', str(directory)] if directory else []), capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


def nd(w, name):
    return next(x for x in w['nodes'] if x['name'] == name)


def m_js(name, old, new):
    def f(w):
        p = nd(w, name)['parameters']
        assert old in p['jsCode'], f'anchor not in {name}: {old!r}'
        p['jsCode'] = p['jsCode'].replace(old, new, 1)
    return f


def m_query(old, new):
    def f(w):
        p = nd(w, 'Upsert Schedule')['parameters']
        assert old in p['query'], f'anchor not in the statement: {old!r}'
        p['query'] = p['query'].replace(old, new, 1)
    return f


def m_set(name, k, v):
    def f(w):
        if v is None:
            nd(w, name).pop(k, None)
        else:
            nd(w, name)[k] = v
    return f


def m_rewire(src, out, dst):
    def f(w):
        w['connections'][src]['main'][out] = [{'node': dst, 'type': 'main', 'index': 0}]
    return f


MUTATIONS = (
    ('gate legacy:true', m_js('Auth Gate', '"legacy": false', '"legacy": true'), 'token ONLY'),
    ('gate without the profile scope', m_js('Auth Gate', '"profiles": ["profileIds"]', '"profiles": []'), 'token ONLY'),
    ('Validate without the re-intersection', m_js('Validate', " && owned.has(x)", ''), 're-intersected'),
    ('Validate keeps the sentinels', m_js('Validate', "x !== '' && x !== '-1' && ", ''), 're-intersected'),
    ('Validate without the zone shape', m_js('Validate', "|| !/", "|| false && !/"), 'IANA shape'),
    ('Validate without the version check', m_js('Validate', "if (!version) return bad('version');", ''), 'version'),
    ('the statement without the version guard', m_query("\n    where bot_schedule.version < excluded.version", ''), 'version-guarded'),
    ('the statement fails closed (no onError)', m_set('Upsert Schedule', 'onError', None), 'continue on error'),
    ('the statement reachable past a refusal', m_rewire('Valid?', 1, 'Upsert Schedule'), 'Valid?'),
    ('the statement reachable without the gate', m_rewire('Authorized?', 1, 'Validate'), 'Authorized?'),
    ('success on any row', m_js('Shape Response', "rows.every((r) => r.error === undefined && typeof r.profileId === 'string' && typeof r.applied === 'boolean')", 'true'), 'well-formed row'),
    ('bad_timezone answered as 500', m_js('Shape Response', "error: 'bad_timezone', _status: 400", "error: 'bad_timezone', _status: 500"), 'bad_timezone'),
    ('Auth Gate disabled', m_set('Auth Gate', 'disabled', True), 'disabled'),
    ('Validate disabled', m_set('Validate', 'disabled', True), 'disabled'),
    ('Valid? disabled', m_set('Valid?', 'disabled', True), 'disabled'),
    ('Upsert Schedule disabled', m_set('Upsert Schedule', 'disabled', True), 'disabled'),
    ('Shape Response disabled', m_set('Shape Response', 'disabled', True), 'disabled'),
)

with Case('B1 V0: verify-set-schedule.py passes the committed canon') as c:
    rc_, out = run_verifier()
    c.check(rc_ == 0 and 'ALL SET SCHEDULE ASSERTS PASSED' in out, f'exit {rc_}: {out[-800:]}')


def scratch():
    d = Path(tempfile.mkdtemp(prefix='b1-setsched-'))
    for f in WF.glob('*.json'):
        shutil.copy(f, d / f.name)
    return d


for label, mutate, needle in MUTATIONS:
    with Case(f'B1 V NEG {label}: the verifier fails, naming it') as c:
        d = scratch()
        try:
            w = json.loads((d / FILE).read_text(encoding='utf-8'))
            untouched = json.dumps(w, sort_keys=True)
            mutate(w)
            c.check(json.dumps(w, sort_keys=True) != untouched, f'{label}: the mutation changed nothing')
            (d / FILE).write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            rc_, out = run_verifier(d)
            fails = [l for l in out.splitlines() if l.startswith('VERIFY SET SCHEDULE FAIL [') and not l.startswith('VERIFY SET SCHEDULE FAIL [1]')]
            c.check(rc_ != 0 and any(needle in l for l in fails), f'{label}: exit {rc_}, no specific FAIL naming {needle!r}: {out[-900:]}')
        finally:
            shutil.rmtree(d, ignore_errors=True)

for dep in ('0DP79STI3pafXO9B-Set_Workflow_State.json', 'XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json'):
    with Case(f'B1 V NEG {dep} without crSetSchedule001: the verifier fails, naming the protected list') as c:
        d = scratch()
        try:
            p = d / dep
            text = p.read_text(encoding='utf-8')
            c.check(f"'{B.WF_ID}'" in text, 'premise: the dependent carries the id')
            p.write_text(text.replace(f", '{B.WF_ID}'", '').replace(f",\\n  '{B.WF_ID}'", ''), encoding='utf-8')
            rc_, out = run_verifier(d)
            c.check(rc_ != 0 and any(dep in l and 'protected' in l for l in out.splitlines()), f'exit {rc_}: {out[-600:]}')
        finally:
            shutil.rmtree(d, ignore_errors=True)

report()
