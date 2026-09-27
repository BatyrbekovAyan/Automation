#!/usr/bin/env python3
"""R8 (iteration 2026-09-27): a failed `Seed Reply Mode` must never let a «Вместе» clone go live.

The Create WhatsApp/Telegram workflow writes the profile's initial '*' reply-mode row BEFORE activating the clone,
because the bot's gate reads «no row» as «reply». The seed node continued on error straight into `Activate Created
Workflow`, so a failed write still activated the clone and answered the client with its id: once the database was
back, a «Вместе» bot auto-replied to live clients.

Contract (fix design of the iteration, adapted by the refuter's objections):
  * the seed statement itself returns a POSITIVE marker (one row `seeded = true`) whenever it completed — new row,
    legacy body (nothing to write), stale version (a newer choice kept) alike;
  * activation requires that marker for a «Вместе» (ReplyMode=semi) create; «Авто» and legacy bodies keep the
    absence semantics (absence IS their mode), so a seed failure never refuses them;
  * every failure form n8n can produce (the Postgres node's {message,error} item, an error thrown outside the query
    runner that passes the INPUT item on, an empty result under alwaysOutputData) → the clone is NOT activated, no
    Wappi webhook points at it, it is not bound, it is deleted, and the webhook answers {success:false,
    error:'reply_mode_not_saved'} without an id (the client's existing failure contract);
  * a transient failure is retried (retryOnFail) — a recovered retry activates normally.

Part A runs the REAL seed SQL of both canonical Create workflows against a session temp table in the staging Postgres.
Part B runs the REAL canonical graphs through create_seed_chain.js (n8n's own expression engine and Postgres
empty-result rule; n8n-core's retry / continue-on-error semantics). Part C proves the checks catch the naive
«continueErrorOutput» variant (a thrown error still activates there).

    python3 Tools/n8n/tests/test_create_seed.py
"""
import copy
import json
import subprocess

from harness import N8N, Case, PgSession, bind, expr, node, report, wf

CREATES = {'WA': ('XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'WhatsappProfileId'),
           'TG': ('Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', 'TelegramProfileId')}
DRIVER = N8N / 'tests' / 'create_seed_chain.js'
SETUP = 'create temp table reply_mode_flags (like public.reply_mode_flags including all);'
NOT_SAVED = {'success': False, 'error': 'reply_mode_not_saved'}


def body(field, pid, mode, version):
    b = {field: pid, 'Name': 'r8', 'AppUserID': '$RCAnonymousID:r8', 'BotKey': 'Bot-r8'}
    if mode is not None:
        b.update({'ReplyMode': mode, 'ReplyModeVersion': str(version)})
    return b


def rows_of(pg, sql):
    """The statement's result rows exactly as the Postgres node would get them (an INSERT without RETURNING → [])."""
    txt = pg.x('\\pset tuples_only off\n' + sql + '\n\\pset tuples_only on')
    lines = [l for l in txt.splitlines() if l and not (l.startswith('(') and l.endswith(')'))]
    if not lines:
        return []
    cols = lines[0].split('|')
    conv = lambda v: True if v == 't' else False if v == 'f' else int(v) if v.lstrip('-').isdigit() else v
    return [dict(zip(cols, map(conv, l.split('|')))) for l in lines[1:]]


def seed(pg, w, b):
    n = node(w, 'Seed Reply Mode')
    params = expr(n['parameters']['options']['queryReplacement'], {}, {'Unity Webhook': {'body': b}})
    return rows_of(pg, bind(n['parameters']['query'], params))


def row(pg, pid):
    r = pg.q(f"select suppressed, version from reply_mode_flags where profile_id = '{pid}' and chat_id = '*'")
    return (r[0]['suppressed'], r[0]['version']) if r else None


def drive(w, scenarios):
    tmp = N8N / 'tests' / '.r8-wf.json'
    tmp.write_text(json.dumps(w), encoding='utf-8')
    try:
        r = subprocess.run(['node', str(DRIVER), str(tmp)], input=json.dumps(scenarios), text=True, capture_output=True)
    finally:
        tmp.unlink()
    if r.returncode != 0:
        raise RuntimeError('driver failed: ' + r.stderr.strip()[-2000:])
    return json.loads(r.stdout)


def touched(rep, name):
    return [e for e in rep['effects'] if e['node'] == name]


FAIL = {'query_error': [{'queryError': 'connection terminated unexpectedly'}],
        'thrown': [{'throw': 'Connection pool exhausted'}],
        'empty': [{'noItems': True}]}


def violations(w, field, ok_rows):
    """Every way the graph breaks the R8 contract, across the failure forms × modes; [] = fail-closed and compatible."""
    sc = {}
    for kind, attempts in FAIL.items():
        sc[f'semi:{kind}'] = {'body': body(field, 'p-r8', 'semi', 150), 'createdId': 'WFr8new', 'seed': attempts}
    sc['semi:ok'] = {'body': body(field, 'p-r8', 'semi', 150), 'createdId': 'WFr8new', 'seed': [{'rows': ok_rows}]}
    sc['semi:transient'] = {'body': body(field, 'p-r8', 'semi', 150), 'createdId': 'WFr8new',
                            'seed': [{'queryError': 'too many clients already'}, {'rows': ok_rows}]}
    sc['auto:thrown'] = {'body': body(field, 'p-r8', 'auto', 150), 'createdId': 'WFr8new', 'seed': FAIL['thrown']}
    sc['legacy:thrown'] = {'body': body(field, 'p-r8', None, 0), 'createdId': 'WFr8new', 'seed': FAIL['thrown']}
    rep = drive(w, sc)
    bad = []
    for k, r in rep.items():
        act, hook, bound = touched(r, 'Activate Created Workflow'), touched(r, 'Set Wappi Webhook'), touched(r, 'Bind Workflow Id')
        deleted = touched(r, 'Delete Unseeded Workflow')
        resp = (r['response'] or {}).get('json')
        if k.startswith('semi:') and k not in ('semi:ok', 'semi:transient'):
            if act or hook or bound:
                bad.append(f'{k}: a failed seed still went live (activate={bool(act)} wappi_webhook={bool(hook)} bound={bool(bound)})')
            if resp != NOT_SAVED:
                bad.append(f'{k}: answered {resp} instead of {NOT_SAVED}')
            if not (deleted and deleted[0]['method'] == 'DELETE' and deleted[0]['url'].endswith('/api/v1/workflows/WFr8new')):
                bad.append(f'{k}: the unseeded clone was not deleted: {deleted}')
        else:
            if not (act and act[0]['url'].endswith('/workflows/WFr8new/activate') and hook and bound):
                bad.append(f'{k}: a create that must succeed did not go live: trace={r["trace"]}')
            if resp != {'id': 'WFr8new'} or deleted:
                bad.append(f'{k}: answered {resp}, deleted={deleted}')
        if k == 'semi:transient' and r['seedCalls'] < 2:
            bad.append(f'{k}: a transient seed failure was not retried (calls={r["seedCalls"]})')
    return bad


for ch, (fname, field) in CREATES.items():
    W = wf(fname)
    pid = f'{ch.lower()}-prof-r8'

    with Case(f'R8 [{ch}] A1: the seed statement answers seeded=true for a new «Вместе» row and writes it') as c:
        with PgSession(SETUP) as pg:
            rows = seed(pg, W, body(field, pid, 'semi', 150))
            c.check(len(rows) == 1 and rows[0].get('seeded') is True, f'rows={rows}')
            c.check(row(pg, pid) == (True, 150), f'row={row(pg, pid)}')
            OK_ROWS = rows

    with Case(f'R8 [{ch}] A2: a legacy body (no ReplyMode) and a stale version complete with seeded=true and write nothing new') as c:
        with PgSession(SETUP) as pg:
            legacy = seed(pg, W, body(field, pid, None, 0))
            c.check(len(legacy) == 1 and legacy[0].get('seeded') is True and row(pg, pid) is None, f'legacy rows={legacy} row={row(pg, pid)}')
            pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ('{pid}', '*', false, 200);")
            stale = seed(pg, W, body(field, pid, 'semi', 150))
            c.check(len(stale) == 1 and stale[0].get('seeded') is True and row(pg, pid) == (False, 200), f'stale rows={stale} row={row(pg, pid)}')

    with Case(f'R8 [{ch}] A3: a failing write yields no marker (the statement errors)') as c:
        with PgSession(SETUP + f"\nalter table reply_mode_flags add constraint r8_fail check (profile_id <> '{pid}');") as pg:
            try:
                rows = seed(pg, W, body(field, pid, 'semi', 150))
                c.check(False, f'the injected failure did not fail: {rows}')
            except RuntimeError as e:
                c.check('r8_fail' in str(e), str(e)[:200])

    with Case(f'R8 [{ch}] B: every seed failure keeps a «Вместе» clone inactive, deleted and answered reply_mode_not_saved; ok/transient/auto/legacy go live') as c:
        for v in violations(W, field, OK_ROWS if 'OK_ROWS' in globals() else []):
            c.check(False, v)

    with Case(f'R8 [{ch}] C: the checks reject the naive continueErrorOutput variant (a thrown error still activates there)') as c:
        naive = copy.deepcopy(W)
        s = node(naive, 'Seed Reply Mode')
        s['onError'] = 'continueErrorOutput'
        naive['connections']['Seed Reply Mode'] = {'main': [[{'node': 'Activate Created Workflow', 'type': 'main', 'index': 0}],
                                                            [{'node': 'Delete Unseeded Workflow', 'type': 'main', 'index': 0}]]}
        try:
            v = violations(naive, field, OK_ROWS)
            c.check(any(x.startswith('semi:thrown') for x in v) and any(x.startswith('semi:empty') for x in v), f'violations={v}')
        except Exception as e:     # the pre-fix graph has no Delete Unseeded Workflow node to wire
            c.check(False, f'variant not constructible on this graph: {e}')

report()
