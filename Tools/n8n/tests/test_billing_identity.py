#!/usr/bin/env python3
"""R2 (iteration 2026-09-27) + V3 (iteration 2026-09-28): naming a RevenueCat billing id never grants its plan, quota or
channel slots.

A billing id (only the RevenueCat anonymous format `$RCAnonymousID:` + 32 hex — a device-id fallback is never bound)
belongs to ONE install (billing_bindings). Another install that names it gets NO billing identity in its token
(u = null) and a `billingConflict` flag. V3: the id moves ONLY from a holder the server itself revoked — never because
the holder «has no live bot» (a paying owner before the first bot, or right after deleting it, lost the id to anyone
who named it, and the two installs then ping-ponged it on every refresh). And it is bound ONLY on a refresh (an install
that proved its secret): the unauthenticated bootstrap binds nothing, so an orphaned bootstrap whose answer was lost can
never hold anyone's id (the case the «no live bot» exception used to cover). The client refreshes right after a
bootstrap; «Удалить все данные» keeps the install (owner decision 2026-10-06). The gates then refuse any billing-id field
that is not the token's own (u = null refuses them all), and Create WA/TG refuse an empty AppUserID in session mode.

Real SQL of Create Install / Verify Install (+ the migration's seed) on staging-Postgres session temp tables with a
real clock; real Issue Token and gate Code bodies from the canonical JSONs; the legacy compat window CLOSED
(harness default CR_AUTH_COMPAT_UNTIL=2000-01-01).
"""
import re
import time
from pathlib import Path

from harness import N8N, Case, PgSession, bind, code, expr, module, node, report, run_js, wf

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


def join(pg, install, app_user_id):
    """What the client does on a fresh install (V3): bootstrap, then refresh in the same session op — the refresh is
    where the billing id is bound. Returns the refresh row (the token the client ends up with)."""
    boot(pg, install, app_user_id)
    return refresh(pg, install, app_user_id)


def usage_gate_for(row):
    """Issue Token on a Verify Install row → the real Auth Gate of Get Usage with that token naming R."""
    issued = code(W, 'Issue Token', {'json': row}, named={'Route': {'op': 'refresh', 'installId': row.get('install_id'), 'nowSec': int(time.time()), 'installSecret': ''}})[0]['json']
    g = code(wf('jtbssfzXbOxwTK4k-Get_Usage.json'), 'Auth Gate', {'json': {'headers': {'authorization': 'Bearer ' + issued.get('token', '')}, 'body': {'appUserId': R}}})[0]['json']['crAuth']
    return 'ok' if g['ok'] else g['reason']


def paid(pg, app_user_id):
    pg.x("create temp table if not exists subscribers (app_user_id text primary key, plan text, status text);")
    pg.x(f"insert into subscribers (app_user_id, plan, status) values ('{app_user_id}', 'business', 'active') on conflict do nothing;")


def holders(pg, app_user_id):
    return [r['install_id'] for r in pg.q(f"select install_id from installs where app_user_id = '{app_user_id}' and revoked_at is null order by install_id")]


def own_bot(pg, install, pid):
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, claimed_at) values ('{pid}', 'x', 'whatsapp', '{install}', now());")


with Case('R2 B1: a second install naming a billing id another install holds (with a live bot) gets NO billing identity + billingConflict') as c:
    with PgSession(SETUP) as pg:
        a = join(pg, 'inst-A', R)
        own_bot(pg, 'inst-A', 'wa-A')
        b = join(pg, 'inst-B', R)
        c.check(a.get('app_user_id') == R, f'first install must hold it: {a}')
        c.check(b.get('install_id') == 'inst-B' and b.get('app_user_id') is None and b.get('billing_conflict') is True, f'second install: {b}')
        c.check(holders(pg, R) == ['inst-A'], f'holders {holders(pg, R)}')

with Case('R2 B2: a refresh naming another install\'s billing id does not move it (the caller keeps its own / nothing)') as c:
    with PgSession(SETUP) as pg:
        join(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        join(pg, 'inst-C', R2)
        r = refresh(pg, 'inst-C', R)
        c.check(r.get('app_user_id') == R2 and r.get('billing_conflict') is True and holders(pg, R) == ['inst-A'], f'{r} holders={holders(pg, R)}')

with Case('R2 B3: the holder\'s own repeated refresh is idempotent (no conflict)') as c:
    with PgSession(SETUP) as pg:
        join(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        r1, r2 = refresh(pg, 'inst-A', R), refresh(pg, 'inst-A', R)
        c.check(r1.get('app_user_id') == R and r2.get('app_user_id') == R and not r2.get('billing_conflict'), f'{r1} / {r2}')

with Case('R2 B4: a REVOKED holder releases the id — the next install that names it takes it over') as c:
    with PgSession(SETUP) as pg:
        join(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        join(pg, 'inst-B', R)
        pg.x("update installs set revoked_at = now() where install_id = 'inst-A';")
        r = refresh(pg, 'inst-B', R)
        c.check(r.get('app_user_id') == R and not r.get('billing_conflict'), f'{r}')

with Case('V3 B5: a live holder WITHOUT a live bot (a paying owner before the first bot) keeps its id — another install naming it gets nothing, on bootstrap AND refresh, and cannot pass Get Usage') as c:
    with PgSession(SETUP) as pg:
        paid(pg, R)
        v = join(pg, 'inst-V', R)                                   # the paying owner, no bot yet
        x_boot = boot(pg, 'inst-X', R)
        x_ref = refresh(pg, 'inst-X', R)
        victim_row = pg.q("select app_user_id from installs where install_id = 'inst-V'")[0]['app_user_id']
        v2 = refresh(pg, 'inst-V', R)
        c.check(v.get('app_user_id') == R, f'the owner must hold its id: {v}')
        c.check(x_boot.get('app_user_id') is None and x_ref.get('app_user_id') is None and x_ref.get('billing_conflict') is True,
                f'the second install got the id: boot={x_boot} refresh={x_ref}')
        c.check(victim_row == R and v2.get('app_user_id') == R and not v2.get('billing_conflict') and holders(pg, R) == ['inst-V'],
                f'the owner lost it: row={victim_row} refresh={v2} holders={holders(pg, R)}')
        c.check(usage_gate_for(x_ref) != 'ok', 'the second install passes Get Usage with the owner\'s id')
        c.check(usage_gate_for(v2) == 'ok', 'the owner must pass its own Get Usage')

with Case('V3 B8: the holder deleted its ONLY bot (deleted_at set, as Delete Bot Files does) — the id still does not move') as c:
    with PgSession(SETUP) as pg:
        join(pg, 'inst-V', R); own_bot(pg, 'inst-V', 'wa-V')
        pg.x("update bot_profiles set deleted_at = now(), deleted_reason = 'app_delete' where profile_id = 'wa-V';")
        x = join(pg, 'inst-X', R)
        c.check(x.get('app_user_id') is None and x.get('billing_conflict') is True and holders(pg, R) == ['inst-V'], f'{x} holders={holders(pg, R)}')

with Case('V3 B9: «Удалить все данные» keeps the install — all its profiles retired, the same install refreshes with the same id: same u, no conflict, nothing owned') as c:
    with PgSession(SETUP) as pg:
        join(pg, 'inst-V', R); own_bot(pg, 'inst-V', 'wa-V'); own_bot(pg, 'inst-V', 'tg-V')
        pg.x("update bot_profiles set deleted_at = now(), deleted_reason = 'app_delete' where owner_id = 'inst-V';")
        r = refresh(pg, 'inst-V', R)
        c.check(r.get('app_user_id') == R and not r.get('billing_conflict') and r.get('p') == [], f'{r}')

with Case('V3 B10: no ping-pong — after a conflict, repeated refreshes of the holder and of the other install never move the holder') as c:
    with PgSession(SETUP) as pg:
        join(pg, 'inst-V', R)
        boot(pg, 'inst-X', R)                                       # the other install exists (bootstrap) and names the id
        seen = []
        for _ in range(3):
            seen.append(refresh(pg, 'inst-X', R).get('app_user_id'))
            seen.append(refresh(pg, 'inst-V', R).get('app_user_id'))
        c.check(seen == [None, R] * 3 and holders(pg, R) == ['inst-V'], f'alternating refreshes: {seen} holders={holders(pg, R)}')

with Case('V3 B11: the unauthenticated bootstrap binds NOTHING — an orphaned bootstrap (answer lost) never holds the id, the real install\'s refresh binds it') as c:
    with PgSession(SETUP) as pg:
        orphan = boot(pg, 'inst-ORPHAN', R)                         # the client never saw this answer
        bound = pg.q(f"select count(*) as n from billing_bindings where app_user_id = '{R}'")[0]['n']
        c.check(orphan.get('app_user_id') is None and not orphan.get('billing_conflict') and bound == 0,
                f'a bootstrap bound the id: {orphan} bindings={bound}')
        mine = join(pg, 'inst-MINE', R)
        c.check(mine.get('app_user_id') == R and not mine.get('billing_conflict') and holders(pg, R) == ['inst-MINE'],
                f'the real install must get its id on its first refresh: {mine} holders={holders(pg, R)}')

with Case('R2 B6: a device-id fallback is never bound (not a RevenueCat id) — no identity, no conflict; the RC id binds later') as c:
    with PgSession(SETUP) as pg:
        d = boot(pg, 'inst-D', DEVICE)
        r = refresh(pg, 'inst-D', R2)
        c.check(d.get('app_user_id') is None and not d.get('billing_conflict') and r.get('app_user_id') == R2, f'{d} / {r}')

with Case('R2 B7: exactly one live install ever holds a billing id') as c:
    with PgSession(SETUP) as pg:
        join(pg, 'inst-A', R)                                       # V3: no live bot needed to keep it
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

# ---- V3 journal (T1, 2026-10-11): a refused billing id leaves a record for support, AFTER the answer ----
# The owner accepted the first-bind limitation (2026-10-11) on condition that support can SEE a conflict and revoke the
# wrong install by the runbook (a1-deploy-runbook.md §5). Each refresh that answers billingConflict writes / bumps ONE
# row (requested id, claimant install, holder install from billing_bindings, first/last seen, attempts). It runs after
# Respond, so the refresh answer is never delayed (each Postgres node ≈ 1.9 s on prod) nor changed — not even when the
# journal itself fails. Here: the REAL Verify Install / Create Install SQL → the REAL Issue Token → n8n's own engine on
# Respond's body/code (the answer) and on the post-response If → the REAL journal SQL (an error becomes an item, as
# continueRegularOutput does), on session temp tables; the journal table comes from the migration's own DDL.
M1_MIGRATION = N8N / 'sql' / '2026-10-11-billing-conflicts.sql'
LOG_NODE, LOG_IF = 'Log Billing Conflict', 'Conflict?'
NOW = int(time.time())


def conflicts_ddl():
    text = M1_MIGRATION.read_text(encoding='utf-8')
    block = text.split('-- TABLE-BEGIN')[1].split('\n', 1)[1].split('-- TABLE-END')[0]
    return block.replace('create table if not exists public.', 'create temp table ')


def out_edges(src, idx=0):
    outs = W['connections'].get(src, {}).get('main', [])
    return [c['node'] for c in outs[idx]] if idx < len(outs) else []


def session_answer(pg, op, install, app_user_id, secret=None):
    """One AuthSession call as the client sees it + what runs after the answer is on the wire.
    Returns (answer, journal): answer = (status, body) of Respond; journal = None when the If does not route to the
    journal, its RETURNING rows when it does, {'error': …} when the journal statement fails."""
    route = {'op': op, 'installId': install, 'nowSec': NOW, 'appUserId': app_user_id,
             'installSecret': ('sec-' + install) if op == 'bootstrap' else ''}
    if op == 'bootstrap':
        row = boot(pg, install, app_user_id)
    else:
        row = pg.q(bind(Q('Verify Install'), [install, secret or ('h-' + install), app_user_id]))[0]
    out = code(W, 'Issue Token', {'json': row}, named={'Route': route})[0]['json']
    rp = node(W, 'Respond')['parameters']
    answer = (expr(rp['options']['responseCode'], out), expr(rp['responseBody'], out))
    journal = None
    for nxt in out_edges('Respond'):                    # Respond passes its input item on (n8n RespondToWebhook)
        cond = node(W, nxt)['parameters']['conditions']['conditions'][0]['leftValue']
        if expr(cond, out) is True:
            for log in out_edges(nxt, 0):
                params = expr(node(W, log)['parameters']['options']['queryReplacement'], out, named={'Route': route})
                try:
                    journal = pg.q(bind(node(W, log)['parameters']['query'], params))
                except RuntimeError as e:
                    journal = {'error': str(e)[:200]}
    return answer, journal


def journal_rows(pg):
    return pg.q("select app_user_id, claimant_install_id, holder_install_id, attempts, first_seen_at, last_seen_at "
                "from billing_conflicts order by app_user_id, claimant_install_id")


LOG_SETUP = lambda: SETUP + '\n' + conflicts_ddl()

with Case('V3-journal B12: a refresh answered billingConflict writes ONE row — the requested id, the claimant, the holder FROM billing_bindings, attempts 1; a second claimant gets its own row') as c:
    with PgSession(LOG_SETUP()) as pg:
        join(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        boot(pg, 'inst-B', R)
        (st, body), j = session_answer(pg, 'refresh', 'inst-B', R)
        rows = journal_rows(pg)
        c.check(st == 200 and body.get('billingConflict') is True and body.get('appUserId') is None, f'the refresh must still answer the conflict: {st} {body}')
        c.check(isinstance(j, list) and len(rows) == 1, f'journal: returned {j} rows {rows}')
        if rows:
            r0 = rows[0]
            c.check((r0['app_user_id'], r0['claimant_install_id'], r0['holder_install_id'], r0['attempts']) == (R, 'inst-B', 'inst-A', 1)
                    and r0['first_seen_at'] == r0['last_seen_at'], f'row {r0}')
        boot(pg, 'inst-C', R)
        session_answer(pg, 'refresh', 'inst-C', R)
        rows = journal_rows(pg)
        c.check([(r['claimant_install_id'], r['holder_install_id'], r['attempts']) for r in rows] == [('inst-B', 'inst-A', 1), ('inst-C', 'inst-A', 1)],
                f'two claimants: {rows}')

with Case('V3-journal B13: a repeated conflict bumps attempts + last_seen_at (first_seen_at kept, holder = the CURRENT holder); a refresh WITHOUT a conflict writes nothing') as c:
    with PgSession(LOG_SETUP()) as pg:
        join(pg, 'inst-A', R)
        boot(pg, 'inst-B', R)
        session_answer(pg, 'refresh', 'inst-B', R)
        first = journal_rows(pg)
        session_answer(pg, 'refresh', 'inst-B', R)
        again = journal_rows(pg)
        c.check(len(first) == 1 and len(again) == 1 and again[0]['attempts'] == 2 and again[0]['first_seen_at'] == first[0]['first_seen_at']
                and again[0]['last_seen_at'] > first[0]['last_seen_at'], f'repeat: {first} -> {again}')
        # the holder changes: A revoked, C takes the id over first — B's next conflict names C
        pg.x("update installs set revoked_at = now() where install_id = 'inst-A';")
        join(pg, 'inst-C', R)
        (st, body), _ = session_answer(pg, 'refresh', 'inst-B', R)
        now_rows = journal_rows(pg)
        c.check(body.get('billingConflict') is True and [(r['claimant_install_id'], r['holder_install_id'], r['attempts']) for r in now_rows] == [('inst-B', 'inst-C', 3)],
                f'the holder must be the current one: {body} {now_rows}')
    with PgSession(LOG_SETUP()) as pg:
        quiet = []
        boot(pg, 'inst-F', R2)
        quiet.append(('free id, first bind', session_answer(pg, 'refresh', 'inst-F', R2)))
        join(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        quiet.append(("the holder's own refresh", session_answer(pg, 'refresh', 'inst-A', R)))
        quiet.append(('bootstrap naming a held id (binds nothing, no conflict)', session_answer(pg, 'bootstrap', 'inst-B', R)))
        quiet.append(('device-id fallback (never bound)', session_answer(pg, 'refresh', 'inst-B', DEVICE)))
        quiet.append(('wrong secret (install_revoked)', session_answer(pg, 'refresh', 'inst-B', R, secret='h-wrong')))
        pg.x("update installs set revoked_at = now() where install_id = 'inst-A';")
        quiet.append(('takeover from a REVOKED holder', session_answer(pg, 'refresh', 'inst-B', R)))
        for label, ((st, body), j) in quiet:
            c.check(j is None and not body.get('billingConflict'), f'{label}: routed to the journal ({j}) / {st} {body}')
        c.check(journal_rows(pg) == [], f'rows written without a conflict: {journal_rows(pg)}')

with Case('V3-journal B14: the journal failing (its table missing) changes NOTHING in the refresh answer; it runs only after Respond, on continueRegularOutput, without retries, with nothing after it') as c:
    answers = {}
    # «missing»: the session sees ONLY its temp tables — a migrated staging DB has public.billing_conflicts, which the
    # default `pg_temp, public` search path would otherwise reach (and write a row into)
    for label, setup in (('with table', LOG_SETUP()), ('table missing', SETUP + '\nset search_path = pg_temp;')):
        with PgSession(setup) as pg:
            join(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
            boot(pg, 'inst-B', R)
            answers[label] = session_answer(pg, 'refresh', 'inst-B', R)
    (a_ok, j_ok), (a_bad, j_bad) = answers['with table'], answers['table missing']
    c.check(isinstance(j_ok, list) and len(j_ok) == 1, f'with the table the journal must write: {j_ok}')
    c.check(isinstance(j_bad, dict) and 'error' in j_bad, f'without the table the journal statement must fail (the case under test): {j_bad}')
    c.check(a_ok == a_bad and a_ok[0] == 200 and a_ok[1].get('billingConflict') is True, f'the answer changed: {a_ok} vs {a_bad}')
    log = node(W, LOG_NODE)
    c.check(out_edges('Respond') == [LOG_IF] and out_edges(LOG_IF, 0) == [LOG_NODE] and out_edges(LOG_IF, 1) == [], 'the journal must hang off Respond → Conflict?[0] only')
    into_log = sorted(s for s, o in W['connections'].items() for out in o.get('main', []) for x in out if x['node'] == LOG_NODE)
    c.check(into_log == [LOG_IF] and out_edges(LOG_NODE) == [], f'reached from {into_log}, followed by {out_edges(LOG_NODE)}')
    c.check(log.get('onError') == 'continueRegularOutput' and not log.get('retryOnFail'), f"onError={log.get('onError')} retryOnFail={log.get('retryOnFail')}")

with Case('V3-journal B15: the statement itself records only a real refusal — never the holder as claimant, an unknown or revoked claimant, an id nobody holds, nor a holder named by the request') as c:
    with PgSession(LOG_SETUP()) as pg:
        join(pg, 'inst-A', R); own_bot(pg, 'inst-A', 'wa-A')
        boot(pg, 'inst-B', R); boot(pg, 'inst-REV', R)
        pg.x("update installs set revoked_at = now() where install_id = 'inst-REV';")
        log_q = node(W, LOG_NODE)['parameters']['query']
        for label, vals in (('the holder itself', [R, 'inst-A']), ('an unknown install', [R, 'inst-NOPE']),
                            ('a revoked install', [R, 'inst-REV']), ('an id nobody holds', [R2, 'inst-B']),
                            ('a device-id fallback', [DEVICE, 'inst-B'])):
            c.check(pg.q(bind(log_q, vals)) == [], f'{label}: recorded')
        c.check(journal_rows(pg) == [], f'rows: {journal_rows(pg)}')
        got = pg.q(bind(log_q, [R, 'inst-B']))
        c.check([(g['claimant_install_id'], g['holder_install_id'], g['attempts']) for g in got] == [('inst-B', 'inst-A', 1)], f'the real refusal: {got}')

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

with Case('V3 negative: verify-auth-gate.py rejects a bootstrap that binds (or adopts the named id), and a bind that moves the id from a holder without live bots') as c:
    import json as _json
    import shutil
    import subprocess
    import sys
    import tempfile
    gen = module('build-auth-workflows.py')
    old_cond = "     or not exists (select 1 from bot_profiles bp where bp.owner_id = billing_bindings.install_id and bp.deleted_at is null)\n"
    # the realistic regression: the generator's own bind CTEs put back into the unauthenticated bootstrap
    boot_ctes = gen.BILLING_BIND_CTES.replace('__HOLDER__', '$1').replace('__FROM__', ', ok')
    for label, field, make in (
        ('bootstrap binds', 'Create Install', lambda q: q.replace("), ins as (", "), " + boot_ctes + ", ins as (", 1)
                                                         .replace("null::text", "(select app_user_id from bind)", 1)),
        ('bootstrap adopts the named id', 'Create Install', lambda q: q.replace("select $1, $2, null::text,", "select $1, $2, $3,", 1)),
        ('no-live-bot takeover', 'Verify Install', lambda q: q.replace("  returning app_user_id\n), strip", old_cond + "  returning app_user_id\n), strip", 1)),
    ):
        d = Path(tempfile.mkdtemp(prefix='v3-neg-'))
        try:
            for p in (N8N / 'workflows').glob('*.json'):
                shutil.copy(p, d / p.name)
            f = d / 'crAuthSession001-Auth_Session.json'
            w = _json.loads(f.read_text(encoding='utf-8'))
            n = next(x for x in w['nodes'] if x['name'] == field)
            before = n['parameters']['query']
            n['parameters']['query'] = make(before)
            c.check(n['parameters']['query'] != before, f'{label}: the mutation did not apply')
            f.write_text(_json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            r = subprocess.run([sys.executable, str(N8N / 'verify-auth-gate.py'), '--dir', str(d)], capture_output=True, text=True)
            c.check(r.returncode != 0 and 'V3' in r.stdout, f'{label}: verifier passed: {r.stdout[-300:]}')
        finally:
            shutil.rmtree(d, ignore_errors=True)


def _before_answer(w):
    w['connections']['Respond'] = {'main': [[]]}
    w['connections']['Issue Token']['main'][0].append({'node': LOG_IF, 'type': 'main', 'index': 0})


def _always(w):
    next(x for x in w['nodes'] if x['name'] == LOG_IF)['parameters']['conditions']['conditions'][0]['leftValue'] = '={{ true }}'


def _node_set(field, value):
    def f(w):
        n = next(x for x in w['nodes'] if x['name'] == LOG_NODE)
        if value is None:
            n.pop(field, None)
        else:
            n[field] = value
    return f


def _any_id(w):
    n = next(x for x in w['nodes'] if x['name'] == LOG_NODE)
    n['parameters']['query'] = n['parameters']['query'].replace('where b.app_user_id = $1', 'where true', 1)


with Case('V3-journal negative: verify-auth-gate.py rejects a journal that runs before the answer, on every refresh, stops or retries the workflow, or logs ids that are not the requested one') as c:
    import json as _json
    import shutil
    import subprocess
    import sys
    import tempfile
    for label, mutate in (('journal before the answer', _before_answer), ('If always true', _always),
                          ('no continueRegularOutput', _node_set('onError', None)), ('retries', _node_set('retryOnFail', True)),
                          ('any bound id, not the requested one', _any_id)):
        d = Path(tempfile.mkdtemp(prefix='v3j-neg-'))
        try:
            for p in (N8N / 'workflows').glob('*.json'):
                shutil.copy(p, d / p.name)
            f = d / 'crAuthSession001-Auth_Session.json'
            w = _json.loads(f.read_text(encoding='utf-8'))
            before = _json.dumps(w, sort_keys=True)
            mutate(w)
            c.check(_json.dumps(w, sort_keys=True) != before, f'{label}: the mutation did not apply')
            f.write_text(_json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            r = subprocess.run([sys.executable, str(N8N / 'verify-auth-gate.py'), '--dir', str(d)], capture_output=True, text=True)
            c.check(r.returncode != 0 and 'V3-journal' in r.stdout, f'{label}: verifier passed: {r.stdout[-300:]}')
        finally:
            shutil.rmtree(d, ignore_errors=True)

report()
