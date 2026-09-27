#!/usr/bin/env python3
"""R2 (iteration 2026-09-27): naming a RevenueCat billing id no longer grants its plan, quota or channel slots.

A billing id (only the RevenueCat anonymous format `$RCAnonymousID:` + 32 hex — a device-id fallback is never bound)
belongs to ONE install (billing_bindings). Another install that names it gets NO billing identity in its token
(u = null) and a `billingConflict` flag — unless the holder is revoked or owns no live bot (recovery after a lost
install secret: claim the bots by code first, then the billing id follows). The gates then refuse any billing-id
field that is not the token's own (u = null refuses them all), and Create WA/TG refuse an empty AppUserID in
session mode (it skipped the channel count and let the quota fail open).

Real SQL of Create Install / Verify Install (+ the migration's seed) on staging-Postgres session temp tables with a
real clock; real Issue Token and gate Code bodies from the canonical JSONs; the legacy compat window CLOSED
(harness default CR_AUTH_COMPAT_UNTIL=2000-01-01).
"""
import re
import time
from pathlib import Path

from harness import N8N, Case, PgSession, bind, code, node, report, run_js, wf

W = wf('crAuthSession001-Auth_Session.json')
Q = lambda name: node(W, name)['parameters']['query']
R = '$RCAnonymousID:' + '0123456789abcdef' * 2          # the victim's RevenueCat id
R2 = '$RCAnonymousID:' + 'fedcba9876543210' * 2
DEVICE = 'A1B2C3D4-E5F6-47A8-9B0C-D1E2F3A4B5C6'           # SystemInfo.deviceUniqueIdentifier-style fallback

SETUP = """create temp table installs (like public.installs including all);
create temp table bot_profiles (like public.bot_profiles including all);
create temp table billing_bindings (app_user_id text primary key, install_id text not null, bound_at timestamptz not null default now());  -- sql/2026-09-27-billing-bindings.sql"""


def boot(pg, install, app_user_id):
    rows = pg.q(bind(Q('Create Install'), [install, 'h-' + install, app_user_id, 'test', 'e2e']))
    return rows[0] if rows else {}


def refresh(pg, install, app_user_id):
    return pg.q(bind(Q('Verify Install'), [install, 'h-' + install, app_user_id]))[0]


def holders(pg, app_user_id):
    return [r['install_id'] for r in pg.q(f"select install_id from installs where app_user_id = '{app_user_id}' and revoked_at is null order by install_id")]


def own_bot(pg, install, pid):
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, claimed_at) values ('{pid}', 'x', 'whatsapp', '{install}', now());")


with Case('R2 B1: a second install naming a billing id another install holds (with a live bot) gets NO billing identity + billingConflict') as c:
    with PgSession(SETUP) as pg:
        a = boot(pg, 'inst-A', R)
        own_bot(pg, 'inst-A', 'wa-A')
        b = boot(pg, 'inst-B', R)
        c.check(a.get('app_user_id') == R, f'first install must hold it: {a}')
        c.check(b.get('install_id') == 'inst-B' and b.get('app_user_id') is None and b.get('billing_conflict') is True, f'second install: {b}')
        c.check(holders(pg, R) == ['inst-A'], f'holders {holders(pg, R)}')

with Case('R2 B2: a refresh naming another install\'s billing id does not move it (the caller keeps its own / nothing)') as c:
    with PgSession(SETUP) as pg:
        boot(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        boot(pg, 'inst-C', R2)
        r = refresh(pg, 'inst-C', R)
        c.check(r.get('app_user_id') == R2 and r.get('billing_conflict') is True and holders(pg, R) == ['inst-A'], f'{r} holders={holders(pg, R)}')

with Case('R2 B3: the holder\'s own repeated refresh is idempotent (no conflict)') as c:
    with PgSession(SETUP) as pg:
        boot(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        r1, r2 = refresh(pg, 'inst-A', R), refresh(pg, 'inst-A', R)
        c.check(r1.get('app_user_id') == R and r2.get('app_user_id') == R and not r2.get('billing_conflict'), f'{r1} / {r2}')

with Case('R2 B4: a REVOKED holder releases the id — the next install that names it takes it over') as c:
    with PgSession(SETUP) as pg:
        boot(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        boot(pg, 'inst-B', R)
        pg.x("update installs set revoked_at = now() where install_id = 'inst-A';")
        r = refresh(pg, 'inst-B', R)
        c.check(r.get('app_user_id') == R and not r.get('billing_conflict'), f'{r}')

with Case('R2 B5: a holder with NO live bot releases the id (recovery: claim the bots first, the billing id follows)') as c:
    with PgSession(SETUP) as pg:
        boot(pg, 'inst-OLD', R); own_bot(pg, 'inst-OLD', 'wa-1')
        n1 = boot(pg, 'inst-NEW', R)
        pg.x("update bot_profiles set owner_id = 'inst-NEW' where profile_id = 'wa-1';")      # claimed by code
        n2 = refresh(pg, 'inst-NEW', R)
        c.check(n1.get('app_user_id') is None and n2.get('app_user_id') == R and holders(pg, R) == ['inst-NEW'],
                f'before claim {n1} after claim {n2} holders={holders(pg, R)}')

with Case('R2 B6: a device-id fallback is never bound (not a RevenueCat id) — no identity, no conflict; the RC id binds later') as c:
    with PgSession(SETUP) as pg:
        d = boot(pg, 'inst-D', DEVICE)
        r = refresh(pg, 'inst-D', R2)
        c.check(d.get('app_user_id') is None and not d.get('billing_conflict') and r.get('app_user_id') == R2, f'{d} / {r}')

with Case('R2 B7: exactly one live install ever holds a billing id') as c:
    with PgSession(SETUP) as pg:
        boot(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        for i in range(3):
            boot(pg, f'inst-X{i}', R)
            refresh(pg, f'inst-X{i}', R)
        c.check(holders(pg, R) == ['inst-A'], f'holders {holders(pg, R)}')

MIGRATION = N8N / 'sql' / '2026-09-27-billing-bindings.sql'
with Case('R2 M1: the migration seed keeps ONE live holder per id (the most recently seen) and clears the others') as c:
    text = MIGRATION.read_text(encoding='utf-8') if MIGRATION.exists() else ''
    seed = text.split('-- SEED-BEGIN')[1].split('\n', 1)[1].split('-- SEED-END')[0].replace('public.', '') if '-- SEED-BEGIN' in text else ''
    c.check(bool(seed), f'{MIGRATION.name}: no SEED block')
    if seed:
        with PgSession(SETUP) as pg:
            pg.x(f"insert into installs (install_id, secret_hash, app_user_id, last_seen_at) values "
                 f"('old', 'h', '{R}', now() - interval '2 days'), ('new', 'h', '{R}', now()), ('dev', 'h', '{DEVICE}', now()), "
                 f"('rev', 'h', '{R2}', now());")
            pg.x("update installs set revoked_at = now() where install_id = 'rev';")
            pg.x(seed)
            rows = {r['install_id']: r['app_user_id'] for r in pg.q('select install_id, app_user_id from installs')}
            binds = {r['app_user_id']: r['install_id'] for r in pg.q('select app_user_id, install_id from billing_bindings')}
            c.check(rows == {'old': None, 'new': R, 'dev': None, 'rev': R2} and binds == {R: 'new'}, f'installs {rows} bindings {binds}')

# ---- tokens and gates (compat window CLOSED) ----
ROUTE = {'op': 'refresh', 'installId': 'inst-B', 'nowSec': int(time.time()), 'installSecret': ''}

with Case('R2 T1: Issue Token signs u = null and tells the client billingConflict') as c:
    out = code(W, 'Issue Token', {'json': {'verdict': 'checked', 'install_id': 'inst-B', 'app_user_id': None, 'token_version': 1,
                                           'p': [], 'w': [], 'c': [], 'billing_conflict': True}}, named={'Route': ROUTE})[0]['json']
    claims = run_js("const t=$input.first().json.t.split('.')[1]; return [{json: JSON.parse(Buffer.from(t.replace(/-/g,'+').replace(/_/g,'/'),'base64').toString())}];",
                    {'json': {'t': out.get('token', 'v1.e30.x')}})[0]['json']
    c.check(out.get('billingConflict') is True and out.get('appUserId') is None and claims.get('u') is None, f'{out} claims={claims}')


def token(u, p=None, w=None):
    return run_js("""const s = require('crypto');
const b64u = (b) => Buffer.from(b).toString('base64').replace(/\\+/g,'-').replace(/\\//g,'_').replace(/=+$/,'');
const p = b64u(JSON.stringify($input.first().json.claims));
return [{json: {t: 'v1.' + p + '.' + b64u(s.createHmac('sha256', 'synthetic-test-secret').update('v1.' + p).digest())}}];""",
                  {'json': {'claims': {'i': 'inst-B', 'u': u, 'p': p or [], 'w': w or [], 'c': [], 'e': int(time.time()) + 3600, 'n': 1}}})[0]['json']['t']


def gate(fname, tok, body, webhook='Webhook'):
    g = code(wf(fname), 'Auth Gate', {'json': {'headers': {'authorization': 'Bearer ' + tok}, 'body': body}})[0]['json']['crAuth']
    return 'ok' if g['ok'] else g['reason']


USAGE, SUGGEST = 'jtbssfzXbOxwTK4k-Get_Usage.json', '9PTyYcelRQI7bGDb-Suggest_Replies.json'
CREATE_WA, CREATE_TG = 'XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json'
DBF = 'lmjYsdNcQA2IE5rl-Delete_Bot_Files.json'

with Case('R2 G1/G2: a token WITHOUT a billing identity cannot name any billing id (GetUsage, SuggestReplies)') as c:
    t = token(None, p=['wa-B'])
    c.check(gate(USAGE, t, {'appUserId': R}) != 'ok', 'GetUsage accepted a foreign id on u=null')
    c.check(gate(SUGGEST, t, {'appUserId': R, 'profileId': 'wa-B'}) != 'ok', 'SuggestReplies accepted a foreign id on u=null')

with Case('R2 G3/G4: a token with u = R passes its own id and refuses another') as c:
    t = token(R)
    c.check(gate(USAGE, t, {'appUserId': R}) == 'ok' and gate(USAGE, t, {'appUserId': R2}) != 'ok', 'own/foreign mismatch')

with Case('R2 G5: Create WA/TG refuse an EMPTY AppUserID in session mode (it skipped the channel count and the quota)') as c:
    t = token(R, p=['wa-B', 'tg-B'])
    for f, pf in ((CREATE_WA, 'WhatsappProfileId'), (CREATE_TG, 'TelegramProfileId')):
        c.check(gate(f, t, {pf: 'wa-B' if 'Whatsapp' in pf else 'tg-B', 'AppUserID': ''}) != 'ok', f'{f}: empty AppUserID accepted')
        c.check(gate(f, t, {pf: 'wa-B' if 'Whatsapp' in pf else 'tg-B', 'AppUserID': R}) == 'ok', f'{f}: own AppUserID refused')

with Case('R2 G7: Delete Bot Files is not blocked by its audit-only appUserId (a conflicted install can still delete its bot)') as c:
    t = token(None, p=['wa-B'])
    c.check(gate(DBF, t, {'waProfileId': 'wa-B', 'tgProfileId': '-1', 'botWaId': '-1', 'botTgId': '-1', 'appUserId': R}) == 'ok', 'DeleteBotFiles refused')

with Case('R2 negative: verify-auth-gate.py rejects an Auth Session that adopts the named billing id directly') as c:
    import json as _json
    import shutil
    import subprocess
    import sys
    import tempfile
    d = Path(tempfile.mkdtemp(prefix='r2-neg-'))
    try:
        for p in (N8N / 'workflows').glob('*.json'):
            shutil.copy(p, d / p.name)
        f = d / 'crAuthSession001-Auth_Session.json'
        w = _json.loads(f.read_text(encoding='utf-8'))
        vi = next(n for n in w['nodes'] if n['name'] == 'Verify Install')
        vi['parameters']['query'] = ("with v as (update installs set last_seen_at = now(), app_user_id = coalesce(nullif($3, ''), app_user_id) "
                                     "where install_id = $1 and secret_hash = $2 and revoked_at is null returning install_id, app_user_id, token_version) "
                                     "select 'checked'::text as verdict, v.install_id from (select 1) as one left join v on true;")
        f.write_text(_json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
        r = subprocess.run([sys.executable, str(N8N / 'verify-auth-gate.py'), '--dir', str(d)], capture_output=True, text=True)
        c.check(r.returncode != 0 and 'R2' in r.stdout, f'verifier passed the pre-R2 refresh: {r.stdout[-300:]}')
    finally:
        shutil.rmtree(d, ignore_errors=True)

report()
