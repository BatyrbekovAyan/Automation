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
    runner that passes the INPUT item on, an empty result under alwaysOutputData) → NO clone is created (no template
    read, no Create Workflow, so nothing to activate, point Wappi at, bind or delete), and the webhook answers
    {success:false, error:'reply_mode_not_saved'} without an id (the client's existing failure contract);
  * a transient failure is retried (retryOnFail) — a recovered retry activates normally.

R8-deletefail (iteration 2026-09-28): the compensating `Delete Unseeded Workflow` (an HTTP DELETE, continue on error,
no retry) was the only thing standing between a failed seed and an orphan: when that DELETE failed too, the clone —
carrying the owner's business data in its systemMessage — survived unbound, its id in no answer and in no `bot_profiles`
row, so nothing (SetWorkflowState accepts only ids in the token's `w`, no sweep touches workflows) could ever find it.
The seed's parameters never depended on the clone, so the gate now stands BEFORE `Get Sample Workflow`: a failed seed
never CREATES a clone. Closing criterion: «a reply-mode seed failure never creates a clone» — NOT «no lost clones at
all» (a failure after Create, and a re-authorisation of the same profile from the settings, are a separate finding, F1).

Part A runs the REAL seed SQL of both canonical Create workflows against a session temp table in the staging Postgres.
Part B runs the REAL canonical graphs through create_seed_chain.js (n8n's own expression engine and Postgres
empty-result rule; n8n-core's retry / continue-on-error semantics) from the slot-allowed branch (`Register Profile`).
Part C proves the checks catch the naive «continueErrorOutput» variant (a thrown error still goes live there).
Part D keeps the books of every clone a run touches — created − deleted − handed to the client = LOST — over both
entries into the clone chain (`Register Profile` and the legacy `If Has AppUserID?` false branch), every failure form,
with the compensating DELETE succeeding and failing.

    python3 Tools/n8n/tests/test_create_seed.py
"""
import copy
import json
import subprocess

from harness import N8N, Case, PgSession, bind, expr, module, node, report, wf

CREATES = {'WA': ('XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'WhatsappProfileId'),
           'TG': ('Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', 'TelegramProfileId')}
DRIVER = N8N / 'tests' / 'create_seed_chain.js'
SETUP = 'create temp table reply_mode_flags (like public.reply_mode_flags including all);'
NOT_SAVED = {'success': False, 'error': 'reply_mode_not_saved'}


def body(field, pid, mode, version, app_user_id='$RCAnonymousID:r8'):
    b = {field: pid, 'Name': 'r8', 'BotKey': 'Bot-r8'}
    if app_user_id is not None:
        b['AppUserID'] = app_user_id
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
CREATED = 'WFr8new'
DELETE_FAILED = {'error': {'message': 'getaddrinfo EAI_AGAIN n8n.choosereply.com', 'name': 'NodeApiError'}}
ENTRIES = {'register': ('Register Profile', '$RCAnonymousID:r8'),   # the slot-allowed branch (session clients)
           'legacy': ('If Has AppUserID?', None)}                     # a body without AppUserID: the If's FALSE branch


def scenarios(field, app_user_id, start, http=None):
    """Every seed outcome × mode the graph must answer: «Вместе» failures refuse, everything else goes live."""
    sc = {}
    mk = lambda mode, seed_: {'body': body(field, 'p-r8', mode, 150, app_user_id), 'createdId': CREATED, 'seed': seed_,
                              'start': start, 'http': dict(http or {})}
    for kind, attempts in FAIL.items():
        sc[f'semi:{kind}'] = mk('semi', attempts)
    sc['semi:ok'] = mk('semi', [{'rows': OK_ROWS}])
    sc['semi:transient'] = mk('semi', [{'queryError': 'too many clients already'}, {'rows': OK_ROWS}])
    sc['auto:thrown'] = mk('auto', FAIL['thrown'])
    sc['legacy:thrown'] = mk(None, FAIL['thrown'])
    return sc


def must_refuse(k):
    return k.startswith('semi:') and k not in ('semi:ok', 'semi:transient')


def judge(k, r, no_clone=True):
    """The R8 contract for one run; [] = held. `no_clone`: a refused create must not even CREATE a clone
    (R8-deletefail) — part D leaves it to the clone books instead, so its failures name the LOST clone."""
    bad = []
    created, sample = touched(r, 'Create Workflow'), touched(r, 'Get Sample Workflow')
    act, hook, bound = touched(r, 'Activate Created Workflow'), touched(r, 'Set Wappi Webhook'), touched(r, 'Bind Workflow Id')
    resp = (r['response'] or {}).get('json')
    if r['stoppedAt']:
        bad.append(f'{k}: the execution stopped: {r["stoppedAt"]}')
    if must_refuse(k):
        if act or hook or bound:
            bad.append(f'{k}: a failed seed still went live (activate={bool(act)} wappi_webhook={bool(hook)} bound={bool(bound)})')
        if no_clone and (created or sample):
            bad.append(f'{k}: a failed seed still created a clone (template read={bool(sample)}, Create Workflow={len(created)}): '
                       f'trace={r["trace"]}')
        if resp != NOT_SAVED:
            bad.append(f'{k}: answered {resp} instead of {NOT_SAVED}')
    else:
        if len(created) != 1 or not (act and act[0]['url'].endswith(f'/workflows/{CREATED}/activate') and hook and bound):
            bad.append(f'{k}: a create that must succeed did not go live exactly once: trace={r["trace"]}')
        if resp != {'id': CREATED} or touched(r, 'Delete Unseeded Workflow'):
            bad.append(f'{k}: answered {resp}, deleted={touched(r, "Delete Unseeded Workflow")}')
    if k == 'semi:transient' and r['seedCalls'] < 2:
        bad.append(f'{k}: a transient seed failure was not retried (calls={r["seedCalls"]})')
    return bad


def violations(w, field):
    """Every way the graph breaks the R8 contract from the slot-allowed branch; [] = fail-closed and compatible."""
    rep = drive(w, scenarios(field, ENTRIES['register'][1], ENTRIES['register'][0]))
    return [v for k, r in rep.items() for v in judge(k, r)]


def lost(r, delete_ok):
    """Clone books of one run: created − deleted − handed to the client. A DELETE counts only when it reached n8n."""
    created = len(touched(r, 'Create Workflow'))
    deleted = len([e for e in touched(r, 'Delete Unseeded Workflow')
                   if e['method'] == 'DELETE' and e['url'].endswith(f'/api/v1/workflows/{CREATED}')]) if delete_ok else 0
    returned = 1 if ((r['response'] or {}).get('json') or {}).get('id') == CREATED else 0
    return created - deleted - returned


OK_ROWS = []   # A1 replaces it with the real statement's answer; left empty, every «Вместе» success case fails loudly
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

    with Case(f'R8 [{ch}] B: a seed failure never creates a «Вместе» clone (no template read, no Create) and answers reply_mode_not_saved; ok/transient/auto/legacy go live') as c:
        for v in violations(W, field):
            c.check(False, v)

    with Case(f'R8 [{ch}] C: the checks reject the naive continueErrorOutput variant (a thrown error still goes live there)') as c:
        naive = copy.deepcopy(W)
        gate = naive['connections'].get('Seed Saved?', {}).get('main', [])
        s_ = node(naive, 'Seed Reply Mode')
        s_['onError'] = 'continueErrorOutput'
        # the gate's two branches wired straight to the seed's two outputs — the shape R8 rejected
        naive['connections']['Seed Reply Mode'] = {'main': [list(gate[0]), list(gate[1])]}
        try:
            v = violations(naive, field)
            c.check(any(x.startswith('semi:thrown') for x in v) and any(x.startswith('semi:empty') for x in v), f'violations={v}')
        except Exception as e:
            c.check(False, f'variant not constructible on this graph: {e}')

    # D (R8-deletefail): clone books over both entries, every failure form, the compensating DELETE ok AND failed
    for entry, (start, app_user_id) in ENTRIES.items():
        runs = {}
        for delete_ok in (True, False):
            sc = scenarios(field, app_user_id, start, None if delete_ok else {'Delete Unseeded Workflow': DELETE_FAILED})
            for k, r in drive(W, sc).items():
                if must_refuse(k) or delete_ok:          # a DELETE outcome only matters where a refusal could compensate
                    runs[f'{k}:{"delete_ok" if delete_ok else "delete_failed"}' if must_refuse(k) else k] = (k, r, delete_ok)
        for name, (k, r, delete_ok) in runs.items():
            with Case(f'R8-deletefail [{ch}] D {entry}/{name}: no clone is ever lost') as c:
                n = lost(r, delete_ok)
                c.check(n == 0, f'LOST clone ({n}): trace={" -> ".join(r["trace"])}')
                for v in judge(k, r, no_clone=False):
                    c.check(False, v)

    # E (R8-deletefail): the structural tripwire in verify-reply-mode.py holds the topology even when nobody runs B/D
    with Case(f'R8-deletefail [{ch}] E: verify-reply-mode accepts the canonical graph and rejects a clone chain reachable around the gate') as c:
        vrm = module('verify-reply-mode.py')
        link = lambda *dst: {'main': [[{'node': d, 'type': 'main', 'index': 0}] for d in dst]}

        def entries_to(w, dst):
            w['connections']['Register Profile'] = link(dst)
            w['connections']['If Has AppUserID?']['main'][1] = [{'node': dst, 'type': 'main', 'index': 0}]

        def pre_fix(w):         # the 2026-09-27 shape: clone first, seed after Create, a compensating DELETE on refusal
            entries_to(w, 'Get Sample Workflow')
            w['connections']['Get Created Workflow Id'] = link('Seed Reply Mode')
            w['connections']['Seed Saved?'] = link('Activate Created Workflow', 'Respond Reply Mode Not Saved')

        def bypass(w):          # a later generator re-adds Register Profile → Get Sample: the slot-allowed branch skips the gate
            w['connections']['Register Profile'] = link('Seed Reply Mode', 'Get Sample Workflow')
            w['connections']['Register Profile']['main'] = [w['connections']['Register Profile']['main'][0]
                                                           + w['connections']['Register Profile']['main'][1]]

        def unseeded(w):        # the entries wired straight into the gate: the seed never runs, «Авто» never gets its '*' row
            entries_to(w, 'Seed Saved?')

        def legacy_skips(w):    # only the legacy (no AppUserID) branch goes around the gate
            w['connections']['If Has AppUserID?']['main'][1] = [{'node': 'Get Sample Workflow', 'type': 'main', 'index': 0}]

        try:
            vrm.check_create(fname, field, copy.deepcopy(W))
        except AssertionError as e:
            c.check(False, f'canonical graph rejected: {e}')
        for label, mutate in (('pre-fix (seed after Create)', pre_fix), ('Register Profile bypass', bypass),
                              ('seed unreachable', unseeded), ('legacy branch bypass', legacy_skips)):
            bad = copy.deepcopy(W)
            mutate(bad)
            c.check(bad != W, f'{label}: mutation did not apply')
            try:
                vrm.check_create(fname, field, bad)
                c.check(False, f'{label}: the tripwire accepted it')
            except AssertionError:
                pass

report()
