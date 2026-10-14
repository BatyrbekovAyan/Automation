#!/usr/bin/env python3
"""Learning B (2026-10-14): the readiness CTE of Get Usage's Read Usage, run as real SQL on session temp tables of the staging
Postgres (tests/harness.py). One bot = one bot_key of the install (both channels + the superseded row of its line); the window is
the last 20 owner_app sends WITH a shown set in personal chats; ready = total >= 10 and matches >= 10 and chats >= 3 and days >= 2.
Needs `Tools/n8n/staging/pg.sh start` (+ `migrate` once)."""
import importlib.util
import itertools
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from harness import Case, PgSession, bind, node, report, wf  # noqa: E402

N8N = Path(__file__).resolve().parents[1]
ROOT = N8N.parents[1]
_spec = importlib.util.spec_from_file_location("apply_learning_b", N8N / "apply-learning-b.py")
LB = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(LB)
_rspec = importlib.util.spec_from_file_location("report_learning", N8N / "report-learning.py")
RL = importlib.util.module_from_spec(_rspec); _rspec.loader.exec_module(RL)

LIKE = 'create temp table {0} (like public.{0} including all);\n'
SETUP = ''.join(LIKE.format(t) for t in ('installs', 'subscribers', 'dialog_counts', 'bot_profiles', 'chat_history_events'))
GU = wf('jtbssfzXbOxwTK4k-Get_Usage.json')
INST, OTHER, U = 'inst-B', 'inst-X', 'u-B'
_mid = itertools.count(1)


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
         f"('p-x', 'u-X', 'whatsapp', '{OTHER}', 'wX0000000001', 'Bot0');")
    pg.x("update bot_profiles set superseded_by = 'p-wa', superseded_at = now(), deleted_at = now(), deleted_reason = 'replaced' where profile_id = 'p-old';")


def send(pg, pid, chat, top1, days_ago=0, origin_set=3):
    pg.x(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, origin_kind, origin_set, origin_top1, created_at) values "
         f"('{pid}', '{chat}', 'm{next(_mid)}', 'owner_app', 't', 'card', {origin_set}, {top1}, now() - interval '{days_ago} days');")


def bot(pg, key, **kw):
    return next((e for e in readiness(pg, **kw) if e['botKey'] == key), None)


with PgSession(SETUP) as pg:
    with Case('constants: spec §2 numbers, mirrored by the client for its copy') as c:
        c.check((LB.WINDOW, LB.MIN_MATCHES, LB.MIN_TOTAL, LB.MIN_CHATS, LB.MIN_DAYS) == (20, 10, 10, 3, 2), 'spec §2 constants')
        cs_path = ROOT / 'Assets' / 'Scripts' / 'Billing' / 'AutoReadiness.cs'
        cs = cs_path.read_text(encoding='utf-8') if cs_path.exists() else ''
        c.check((not cs) or (f'Window = {LB.WINDOW};' in cs and f'MinMatches = {LB.MIN_MATCHES};' in cs),
                'AutoReadinessPolicy.Window/MinMatches mirror the module (once Task 3 exists)')
    with Case('fresh install: every bot_key with a live row is listed with zeros and ready=false') as c:
        seed(pg)
        r = readiness(pg)
        c.check([e['botKey'] for e in r] == ['Bot0', 'Bot1'], f'rows: {r}')
        c.check(all(e['total'] == 0 and e['matches'] == 0 and e['chats'] == 0 and e['days'] == 0 and e['ready'] is False for e in r), f'zeros: {r}')
    with Case('ready: 10 matches across both channels + the superseded row, 3 chats, 2 days') as c:
        for _ in range(4): send(pg, 'p-wa', '7701@c.us', 1, days_ago=1)
        for _ in range(3): send(pg, 'p-tg', '123456', 1)
        for _ in range(3): send(pg, 'p-old', '7703@c.us', 1)
        c.check(bot(pg, 'Bot0') == {'botKey': 'Bot0', 'matches': 10, 'total': 10, 'chats': 3, 'days': 2, 'ready': True}, f"{bot(pg, 'Bot0')}")
    with Case('groups and sends without a set are not counted; another install with the same bot_key is a different bot') as c:
        send(pg, 'p-wa', '120363@g.us', 1); send(pg, 'p-tg', '-100777', 1); send(pg, 'p-wa', '7701@c.us', 1, origin_set=0)
        send(pg, 'p-x', '7701@c.us', 1)
        e = bot(pg, 'Bot0')
        c.check(e['total'] == 10 and e['matches'] == 10 and e['ready'] is True, f'{e}')
        other = readiness(pg, inst=OTHER)
        c.check([(x['botKey'], x['total']) for x in other] == [('Bot0', 1)], f'inst-X sees only its own send: {other}')
    with Case('the window is the last 20: eleven newer misses push the ready bot back under the threshold') as c:
        for _ in range(11): send(pg, 'p-wa', '7701@c.us', 0)
        c.check(bot(pg, 'Bot0') == {'botKey': 'Bot0', 'matches': 9, 'total': 20, 'chats': 3, 'days': 2, 'ready': False}, f"{bot(pg, 'Bot0')}")
    with Case('anti-gaming: 10 matches in ONE chat on one day are not ready') as c:
        for _ in range(10): send(pg, 'p-b1', '7709@c.us', 1)
        e = bot(pg, 'Bot1')
        c.check(e['matches'] == 10 and e['total'] == 10 and e['chats'] == 1 and e['days'] == 1 and e['ready'] is False, f'{e}')
    with Case('legacy mode or an empty install id → []') as c:
        c.check(readiness(pg, mode='legacy') == [], 'legacy caller')
        c.check(readiness(pg, inst='') == [], 'no install id')
    with Case('the rest of Read Usage is untouched: plan/used/bots columns still answer') as c:
        row = pg.q(bind(Q(GU, 'Read Usage'), [U, INST, 'session']))[0]
        c.check(row['plan'] == 'trial' and row['used'] == 0 and row['bots_registered'] == 2, f'{row}')
    with Case("report-learning's readiness table agrees with Get Usage and skips a bot with no live row (review #7)") as c:
        pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id, bot_key, deleted_at, deleted_reason) values "
             f"('p-dead', '{U}', 'whatsapp', '{INST}', 'wDead0000001', 'Bot2', now(), 'trial_expiry');")
        for _ in range(3): send(pg, 'p-dead', '7799@c.us', 1)
        c.check(bot(pg, 'Bot2') is None, 'Get Usage lists no bot without a live row')
        rows = {(r['owner_id'], r['bot_key']): r for r in pg.q(RL.READINESS_SQL)}
        c.check((INST, 'Bot2') not in rows, f'the report must not print a bot the lock never sees: {sorted(rows)}')
        for key in ('Bot0', 'Bot1'):
            g = bot(pg, key); r = rows.get((INST, key))
            c.check(r is not None and all(r[k] == g[k] for k in ('matches', 'total', 'chats', 'days', 'ready')), f'{key}: report {r} vs gate {g}')
        c.check((OTHER, 'Bot0') in rows and rows[(OTHER, 'Bot0')]['total'] == 1, 'other installs are listed with their own counts')
report()
