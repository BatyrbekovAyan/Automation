#!/usr/bin/env python3
"""Learning B (2026-10-14) + progress percent (2026-10-23): the readiness CTE of Get Usage's Read Usage, run as real SQL on session temp
tables of the staging Postgres (tests/harness.py). One bot = one bot_key of the install (both channels + the superseded row of its line).
History = every owner_app send WITH a shown set in personal chats. best = the most card-1 matches in any 20 consecutive sends of the
history; progress = round(100 * best / 16), at most 69 until the history holds 3 chats and 2 days; ready = those conditions and
best >= 12. matches / total / chats / days keep describing the CURRENT window (the last 20 sends) for the report.
Needs `Tools/n8n/staging/pg.sh start` (+ `migrate` once)."""
import importlib.util
import itertools
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from harness import Case, PgSession, bind, node, report, wf  # noqa: E402

N8N = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("apply_learning_b", N8N / "apply-learning-b.py")
LB = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(LB)
_rspec = importlib.util.spec_from_file_location("report_learning", N8N / "report-learning.py")
RL = importlib.util.module_from_spec(_rspec); _rspec.loader.exec_module(RL)

LIKE = 'create temp table {0} (like public.{0} including all);\n'
SETUP = ''.join(LIKE.format(t) for t in ('installs', 'subscribers', 'dialog_counts', 'bot_profiles', 'chat_history_events'))
GU = wf('jtbssfzXbOxwTK4k-Get_Usage.json')
INST, OTHER, U = 'inst-B', 'inst-X', 'u-B'
_mid = itertools.count(1)
KEYS = {'botKey', 'matches', 'total', 'chats', 'days', 'ready', 'bestMatches', 'progress', 'unlockAt', 'capped', 'needChats', 'needDays'}
# Get Usage key -> report-learning column: the two renderers of the ONE arithmetic must agree on every one of them
REPORT_COLUMNS = (('matches', 'matches'), ('total', 'total'), ('chats', 'chats'), ('days', 'days'), ('ready', 'ready'),
                  ('bestMatches', 'best_matches'), ('progress', 'progress'), ('capped', 'capped'),
                  ('needChats', 'need_chats'), ('needDays', 'need_days'))


def Q(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')   # the temp tables shadow the UNqualified names only


def readiness(pg, mode='session', inst=INST):
    row = pg.q(bind(Q(GU, 'Read Usage'), [U, inst, mode]))[0]
    r = row['readiness']
    return json.loads(r) if isinstance(r, str) else r


def seed(pg):
    pg.x(f"insert into installs (install_id, secret_hash) values ('{INST}', 'h'), ('{OTHER}', 'h');")
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id, bot_key) values "
         f"('p-wa', '{U}', 'whatsapp', '{INST}', 'wWa000000001', 'Bot0'), ('p-tg', '{U}', 'telegram', '{INST}', 'wTg000000001', 'Bot0'), "
         f"('p-old', '{U}', 'whatsapp', '{INST}', 'wOld00000001', 'Bot0'), ('p-b1', '{U}', 'whatsapp', '{INST}', 'wB1000000001', 'Bot1'), "
         f"('p-b2', '{U}', 'whatsapp', '{INST}', 'wB2000000001', 'Bot2'), "
         f"('p-x', 'u-X', 'whatsapp', '{OTHER}', 'wX0000000001', 'Bot0');")
    pg.x("update bot_profiles set superseded_by = 'p-wa', superseded_at = now(), deleted_at = now(), deleted_reason = 'replaced' where profile_id = 'p-old';")


def _sql(v):
    return 'null' if v is None else v


def send(pg, pid, chat, top1, days_ago=0, origin_set=3):
    """One owner_app send; top1 / origin_set None = the retention sweep's NULL (an expired label)."""
    pg.x(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, origin_kind, origin_set, origin_top1, created_at) values "
         f"('{pid}', '{chat}', 'm{next(_mid)}', 'owner_app', 't', 'card', {_sql(origin_set)}, {_sql(top1)}, now() - interval '{days_ago} days');")


def bot(pg, key, **kw):
    return next((e for e in readiness(pg, **kw) if e['botKey'] == key), None)


def pick(row, *keys):
    """Only the asked-for keys of a readiness row — a case pins what it is about, not the whole row."""
    return {k: row[k] for k in keys}


def half_up(m):
    """round(100 * m / TARGET_MATCHES), ties up — integer arithmetic, so the expectation shares no float with the SQL."""
    return (200 * m + LB.TARGET_MATCHES) // (2 * LB.TARGET_MATCHES)


def synthetic_name(m, chats_ok, days_ok):
    return f"B{m}{'c' if chats_ok else 'x'}{'d' if days_ok else 'y'}"


def synthetic_sql():
    """The REAL window_columns / readiness_columns over synthetic histories: one bot per (best, 3 chats?, 2 days?). Twenty sends each, the
    first `m` of them are matches, so the best window is exactly m; the 10 oldest sends lie two days before the rest when the history has 2 days."""
    return f"""
with spec as (
  select m, ch, dy, 'B' || m || case when ch then 'c' else 'x' end || case when dy then 'd' else 'y' end as bot_key
  from generate_series(0, 20) m
  cross join (values (true), (false)) a(ch)
  cross join (values (true), (false)) b(dy)
), e as (
  select s.bot_key, g as id,
         case when s.ch then 'chat' || (g % 3) else 'chat0' end as chat_id,
         (case when g <= s.m then 1 else 0 end)::smallint as origin_top1,
         timestamptz '2026-03-03 12:00+00' - case when s.dy and g <= 10 then interval '2 days' else interval '0' end
           + g * interval '1 minute' as created_at
  from spec s cross join generate_series(1, 20) g
), s as (
  select e.bot_key, e.chat_id, e.origin_top1, e.created_at,
         {LB.window_columns('e.bot_key', 'e')}
  from e
)
select s.bot_key,
       {LB.readiness_columns('s')}
from s
group by s.bot_key
"""


with PgSession(SETUP) as pg:
    with Case('constants: spec §2 numbers, the client holds none') as c:
        c.check((LB.WINDOW, LB.TARGET_MATCHES, LB.UNLOCK_PERCENT, LB.MIN_MATCHES, LB.MIN_CHATS, LB.MIN_DAYS) == (20, 16, 70, 12, 3, 2),
                'spec §2 constants')
        c.check(not hasattr(LB, 'MIN_TOTAL'), 'MIN_TOTAL is gone')
    with Case('progress table of spec §2 (round half up, cap 69 without the conditions)') as c:
        rows = pg.q('select m, c, ' + LB.progress_sql('m', 'c') + ' as p from (select g as m, b as c '
                    'from generate_series(0, 20) g cross join (values (true), (false)) v(b)) t order by m, c desc')
        met = [0, 6, 13, 19, 25, 31, 38, 44, 50, 56, 63, 69, 75, 81, 88, 94, 100, 100, 100, 100, 100]
        c.check([r['p'] for r in rows if r['c']] == met, f"conditions met: {[r['p'] for r in rows if r['c']]}")
        c.check([r['p'] for r in rows if not r['c']] == [min(p, 69) for p in met], f"conditions not met: {[r['p'] for r in rows if not r['c']]}")
    with Case('ready <=> progress >= unlockAt for best 0..20 x (3 chats?) x (2 days?), through the real window_columns / readiness_columns') as c:
        got = {r['bot_key']: r for r in pg.q(synthetic_sql())}
        c.check(len(got) == 21 * 4, f'{len(got)} synthetic bots')
        bad = []
        for m in range(21):
            for chats_ok in (True, False):
                for days_ok in (True, False):
                    name = synthetic_name(m, chats_ok, days_ok)
                    r = got.get(name)
                    cond = chats_ok and days_ok
                    raw = min(100, half_up(m))
                    want = {'best_matches': m, 'matches': m, 'total': 20, 'chats': 3 if chats_ok else 1, 'days': 2 if days_ok else 1,
                            'progress': raw if cond else min(raw, LB.UNLOCK_PERCENT - 1),
                            'ready': cond and m >= LB.MIN_MATCHES,
                            'capped': (not cond) and raw >= LB.UNLOCK_PERCENT,
                            'need_chats': 0 if chats_ok else LB.MIN_CHATS - 1, 'need_days': 0 if days_ok else LB.MIN_DAYS - 1}
                    if r is None or {k: r[k] for k in want} != want:
                        bad.append((name, want, r))
        c.check(not bad, f'{len(bad)} synthetic bots differ, first: {bad[:1]}')
        c.check(all(r['ready'] == (r['progress'] >= LB.UNLOCK_PERCENT) for r in got.values()),
                'ready must be exactly progress >= unlockAt: ' + str([n for n, r in got.items() if r['ready'] != (r['progress'] >= LB.UNLOCK_PERCENT)][:5]))
    with Case('fresh install: every bot_key with a live row is listed with zeros, ready=false and the full need') as c:
        seed(pg)
        r = readiness(pg)
        c.check([e['botKey'] for e in r] == ['Bot0', 'Bot1', 'Bot2'], f'rows: {r}')
        zero = {'matches': 0, 'total': 0, 'chats': 0, 'days': 0, 'ready': False, 'bestMatches': 0, 'progress': 0, 'unlockAt': 70,
                'capped': False, 'needChats': 3, 'needDays': 2}
        c.check(all(e == dict(zero, botKey=e['botKey']) for e in r), f'zeros: {r}')
        c.check(all(set(e) == KEYS for e in r), f'the answer carries exactly the keys the client reads: {[sorted(e) for e in r][:1]}')
    with Case('ready: 12 matches across both channels + the superseded row, 3 chats, 2 days') as c:
        for _ in range(4): send(pg, 'p-wa', '7701@c.us', 1, days_ago=1)
        for _ in range(4): send(pg, 'p-tg', '123456', 1)
        for _ in range(4): send(pg, 'p-old', '7703@c.us', 1)
        want = {'botKey': 'Bot0', 'matches': 12, 'total': 12, 'chats': 3, 'days': 2, 'ready': True, 'bestMatches': 12, 'progress': 75,
                'unlockAt': 70, 'capped': False, 'needChats': 0, 'needDays': 0}
        c.check(bot(pg, 'Bot0') == want, f"{bot(pg, 'Bot0')}")
    with Case('groups, sends without a set and sends with an EXPIRED label (NULL) are not counted; another install with the same bot_key is a different bot') as c:
        send(pg, 'p-wa', '120363@g.us', 1); send(pg, 'p-tg', '-100777', 1); send(pg, 'p-wa', '7701@c.us', 1, origin_set=0)
        send(pg, 'p-wa', '7701@c.us', None, origin_set=None)
        send(pg, 'p-x', '7701@c.us', 1)
        e = bot(pg, 'Bot0')
        c.check(pick(e, 'total', 'matches', 'bestMatches', 'ready') == {'total': 12, 'matches': 12, 'bestMatches': 12, 'ready': True}, f'{e}')
        other = readiness(pg, inst=OTHER)
        c.check([(x['botKey'], x['total']) for x in other] == [('Bot0', 1)], f'inst-X sees only its own send: {other}')
    with Case('a ready bot stays ready: eleven newer misses move the window, not the best window') as c:
        for _ in range(11): send(pg, 'p-wa', '7701@c.us', 0)
        e = bot(pg, 'Bot0')
        c.check(pick(e, 'matches', 'total', 'bestMatches', 'progress', 'ready') == {'matches': 9, 'total': 20, 'bestMatches': 12, 'progress': 75, 'ready': True}, f'{e}')
    with Case('anti-gaming: 12 matches in ONE chat on one day are capped at 69') as c:
        for _ in range(12): send(pg, 'p-b1', '7709@c.us', 1)
        e = bot(pg, 'Bot1')
        c.check(pick(e, 'bestMatches', 'progress', 'capped', 'ready', 'needChats', 'needDays') ==
                {'bestMatches': 12, 'progress': 69, 'capped': True, 'ready': False, 'needChats': 2, 'needDays': 1}, f'{e}')
        c.check(pick(e, 'matches', 'total', 'chats', 'days') == {'matches': 12, 'total': 12, 'chats': 1, 'days': 1}, f'the window fields stay what the report reads: {e}')
    with Case('busy owner: 25 sends today in 3 chats + 1 miss yesterday → the day counts from history') as c:
        chats = ['7711@c.us', '7712@c.us', '7713@c.us']
        send(pg, 'p-b2', chats[0], 0, days_ago=1)
        for i in range(25): send(pg, 'p-b2', chats[i % 3], 1)
        e = bot(pg, 'Bot2')
        c.check(pick(e, 'days', 'bestMatches', 'progress', 'ready', 'capped', 'needDays') ==
                {'days': 1, 'bestMatches': 20, 'progress': 100, 'ready': True, 'capped': False, 'needDays': 0}, f'{e}')
        c.check(pick(e, 'matches', 'total') == {'matches': 20, 'total': 20}, f'the window is the last 20 sends: {e}')
    with Case('ready is exactly progress >= unlockAt in every row the real Get Usage query returns') as c:
        r = readiness(pg)
        c.check(len(r) == 3 and all(e['ready'] == (e['progress'] >= e['unlockAt']) for e in r), f'{r}')
    with Case('legacy mode or an empty install id → []') as c:
        c.check(readiness(pg, mode='legacy') == [], 'legacy caller')
        c.check(readiness(pg, inst='') == [], 'no install id')
    with Case('the rest of Read Usage is untouched: plan/used/bots columns still answer') as c:
        row = pg.q(bind(Q(GU, 'Read Usage'), [U, INST, 'session']))[0]
        c.check(row['plan'] == 'trial' and row['used'] == 0 and row['bots_registered'] == 3, f'{row}')
    with Case("report-learning's readiness table agrees with Get Usage and skips a bot with no live row (review #7)") as c:
        pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id, bot_key, deleted_at, deleted_reason) values "
             f"('p-dead', '{U}', 'whatsapp', '{INST}', 'wDead0000001', 'Bot3', now(), 'trial_expiry');")
        for _ in range(3): send(pg, 'p-dead', '7799@c.us', 1)
        c.check(bot(pg, 'Bot3') is None, 'Get Usage lists no bot without a live row')
        rows = {(r['owner_id'], r['bot_key']): r for r in pg.q(RL.READINESS_SQL)}
        c.check((INST, 'Bot3') not in rows, f'the report must not print a bot the lock never sees: {sorted(rows)}')
        for key in ('Bot0', 'Bot1', 'Bot2'):
            g = bot(pg, key); r = rows.get((INST, key))
            c.check(r is not None and all(r[col] == g[k] for k, col in REPORT_COLUMNS), f'{key}: report {r} vs gate {g}')
        c.check((OTHER, 'Bot0') in rows and rows[(OTHER, 'Bot0')]['total'] == 1, 'other installs are listed with their own counts')
report()
