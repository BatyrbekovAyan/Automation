#!/usr/bin/env python3
"""T3 (F1 + F2, plan docs/audits/2026-10-09-followups/F1F2-plan.md §1.4, §1.7, §3 T3): the Create WhatsApp/Telegram GRAPH
as «ensure exactly one bound clone for this channel of this bot», run end to end.

The canonical Create graph runs through tests/create_seed_chain.js in BRIDGE mode: every Postgres node it reaches runs
its REAL statement (the parameters the graph itself evaluated) on session temp tables of the staging Postgres
(`like public.*` + migration M2), and every HTTP call goes to a fake n8n public API + a fake Wappi played here (workflows
with their active / archived state and webhook path; activation refuses a path another active workflow holds — n8n
2.36.7's WebhookPathTakenError; DELETE is the public API's force delete; failures injected per node). Nothing reaches
n8n, Wappi or production.

The BOOKS of clones (F1 §3 B): every clone the run created, and every clone a LIVE row named before the run, is
afterwards either deleted (the fake n8n no longer has it), or bound — a live bot_profiles row names it — or booked in
clone_retirements AND taken by the sweep's real branch-A statement (clone_lineage.CLONE_RETIREMENTS_SQL) once older than
ten minutes. Anything else is LOST. The single expected residual is named: a failed Create whose new clone could be
neither deleted nor journaled (Discard New Clone and Record Discard Failure both failing → Tier 2).
The answer contract (§1.7, F1 §3 C): success {id, created, adoptedProfiles} and nothing else, the id bound to the profile
and in the install's token (Auth Session's real Reload Ownership); a refusal {success:false, error} WITHOUT an id.

Scenarios × WhatsApp / Telegram × session / legacy (F1 §3 B + F2): fresh; reuse of the bound clone (inactive, active);
the bound clone gone (404) or archived; the probe failing (nothing created); a new profile of the same bot (а3) with the
replaced clone's DELETE ok and failing; Activate failing (network, webhook-path conflict, 500); the Wappi webhook / types
failing; Bind failing (query error, thrown, no items); Discard failing (journaled); Discard + journal failing (the named
residual); Clone Plan failing three ways; F2 R1 (life → sweep → purchase → reconnect: memory, history, outcomes under the
new profile, price lists under the new clone, the prior clone deleted), R4 (the same pair again: idempotent), R9 (a
two-channel bot); negatives — another install's bot with the same bot key, a wizard Create (never adopts), a legacy
Create on a profile an install owns (profile_owned, nothing created).

    python3 Tools/n8n/tests/test_create_clone.py            # the canon — must pass
    python3 Tools/n8n/tests/test_create_clone.py --head     # the same scenarios against the pre-F1 graph (must fail)
"""
import json
import os
import re
import subprocess
import sys
import tempfile
from urllib.parse import urlparse

from harness import N8N, ROOT, Case, PgSession, bind, module, node, report, sql_literal as L, wf

HEAD_MODE = '--head' in sys.argv
VERBOSE = '--verbose' in sys.argv      # print each run's trace, n8n/Wappi calls and answer (evidence)
PRE_REV = '5ac9b2f9'          # the last canon before the line's Create graph (Restamp RAG Chunks + the A1 Bind Workflow Id)
DRIVER = N8N / 'tests' / 'create_seed_chain.js'
CL = module('clone_lineage.py')
U = '$RCAnonymousID:' + 'fedcba9876543210' * 2
U_B = '$RCAnonymousID:' + '0123456789abcdef' * 2
CH = {
    'WA': {'channel': 'whatsapp', 'create': 'XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'field': 'WhatsappProfileId',
           'other_field': 'TelegramWorkflowId', 'key': 'botWaId', 'other': 'botTgId', 'fcol': 'bot_wa_id',
           'template': '4wYitz5ek30SVNlT-WhatsApp_Bot.json', 'twin': 'TG'},
    'TG': {'channel': 'telegram', 'create': 'Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', 'field': 'TelegramProfileId',
           'other_field': 'WhatsappWorkflowId', 'key': 'botTgId', 'other': 'botWaId', 'fcol': 'bot_tg_id',
           'template': '4VN3gsFaC2HUYmcc-Telegram_Bot.json', 'twin': 'WA'},
}
MEMORY_TABLES = ('n8n_chat_histories', 'chat_history_events', 'conversation_outcomes', 'handoff_requests', 'reply_events',
                 'chat_reply_state', 'reply_mode_flags', 'bot_schedule')   # B1 (2026-10-14): the bind moves the schedule too
PROTECTED = set(CL.PROTECTED)


def canon(name):
    if HEAD_MODE:
        out = subprocess.run(['git', '-C', str(ROOT), 'show', f'{PRE_REV}:Tools/n8n/workflows/{name}'],
                             capture_output=True, text=True, check=True).stdout
        return json.loads(out)
    return wf(name)


def Q(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')


AUTH = wf('crAuthSession001-Auth_Session.json')
PROXY = wf('crWappiProxy0001-Wappi_Proxy.json')
TEMPLATES = {ch: wf(c['template']) for ch, c in CH.items()}


def m2_block():
    text = (N8N / 'sql' / '2026-10-11-bot-lineage.sql').read_text(encoding='utf-8')
    block = text.split('-- TABLE-BEGIN')[1].split('\n', 1)[1].split('-- TABLE-END')[0].replace('public.', '')
    return block.replace('create table if not exists', 'create temp table if not exists')


LIKE = 'create temp table {0} (like public.{0} including all);\n'
SETUP = (''.join(LIKE.format(t) for t in ('installs', 'subscribers', 'bot_profiles', 'documents', 'uploaded_files') + MEMORY_TABLES)
         + m2_block()
         + "insert into installs (install_id, secret_hash) values ('inst-A', 'h'), ('inst-B', 'h');\n")


def W(ch, tag):
    return (ch + tag + '0' * 16)[:16]


# ------------------------------------------------------------------------------------------------ the fake services

class Fake:
    """n8n's public workflow API (2.36.7 semantics, see the module docstring) + Wappi's webhook setters. `faults`:
    node name → an answer (or a list, one per call) that replaces the routed answer for that node."""

    def __init__(self, ch, workflows=None, faults=None, created=()):
        self.ch = ch
        self.wf = {k: dict(v) for k, v in (workflows or {}).items()}
        self.faults = faults or {}
        self.created_ids = list(created)
        self.created, self.deleted, self.calls, self.n = [], [], [], {}

    def handle(self, req):
        name, method, url = req['node'], req['method'], req.get('url') or ''
        self.n[name] = self.n.get(name, 0) + 1
        ans = None
        if name in self.faults:
            f = self.faults[name]
            ans = f[min(self.n[name] - 1, len(f) - 1)] if isinstance(f, list) else f
        if ans is None:
            ans = self.route(method, urlparse(url).path, req.get('body'))
        self.calls.append((name, method, urlparse(url).netloc, urlparse(url).path, ans.get('statusCode', 'throw')))
        if method == 'DELETE' and ans.get('statusCode') == 200:
            self.deleted.append(urlparse(url).path.rsplit('/', 1)[-1])
        return ans

    def route(self, method, path, body):
        if 'webhook/url/set' in path or 'webhook/types/set' in path:
            return {'statusCode': 200, 'body': {'status': 'done'}}
        m = re.search(r'/api/v1/workflows(?:/([^/]+))?(/activate|/deactivate)?$', path)
        if not m:
            return {'statusCode': 404, 'body': {'message': 'not routed by the fake: ' + path}}
        wid, act = m.group(1), m.group(2)
        tpl = next((t for t in TEMPLATES.values() if t['id'] == wid), None)
        if method == 'GET' and tpl is not None:
            return {'statusCode': 200, 'body': tpl}
        if method == 'POST' and wid is None:                         # create
            nid = self.created_ids.pop(0) if self.created_ids else W(self.ch, f'extra{len(self.created)}')
            hook = next((n for n in (body or {}).get('nodes') or [] if n.get('name') == 'Webhook'), {})
            self.wf[nid] = {'active': False, 'archived': False, 'path': (hook.get('parameters') or {}).get('path')}
            self.created.append(nid)
            return {'statusCode': 200, 'body': {'id': nid, 'name': (body or {}).get('name'), 'active': False}}
        w = self.wf.get(wid)
        if w is None:
            return {'statusCode': 404, 'body': {'message': 'Not Found'}}
        if method == 'GET':
            return {'statusCode': 200, 'body': {'id': wid, 'active': w['active'], 'isArchived': w['archived'],
                                                'nodes': [{'name': 'Webhook', 'type': 'n8n-nodes-base.webhook',
                                                           'parameters': {'path': w['path']}}]}}
        if method == 'POST' and act == '/activate':
            if w['archived']:
                return {'statusCode': 400, 'body': {'message': 'Cannot activate an archived workflow.'}}
            if any(o['active'] and o['path'] == w['path'] for k, o in self.wf.items() if k != wid):
                return {'statusCode': 409, 'body': {'message': 'There is a conflict with one of the webhooks.'}}
            w['active'] = True
            return {'statusCode': 200, 'body': {'id': wid, 'active': True}}
        if method == 'DELETE':
            del self.wf[wid]
            return {'statusCode': 200, 'body': {'id': wid}}
        return {'statusCode': 405, 'body': {'message': 'method'}}


# ------------------------------------------------------------------------------------------------ the bridge

def strip_comments(q):
    return re.sub(r'--[^\n]*', '', q)


def run_pg(pg, req):
    q = bind(req['query'].replace('public.', ''), req['params'])
    body = strip_comments(q).strip()
    try:
        if req.get('select') or (re.search(r'\breturning\b', body, re.I) and not re.match(r'with\b', body, re.I)):
            return {'rows': pg.q(q)}
        pg.x(q.rstrip().rstrip(';') + ';')
        return {'rows': []}
    except RuntimeError as e:
        return {'queryError': str(e)[-300:]}


def drive(w, sc, pg, fake):
    """One scenario of graph `w` through the driver; the driver's Postgres / HTTP requests are answered here."""
    fd1, gpath = tempfile.mkstemp(prefix='clone-wf-', suffix='.json')
    fd2, spath = tempfile.mkstemp(prefix='clone-sc-', suffix='.json')
    try:
        with os.fdopen(fd1, 'w', encoding='utf-8') as f:
            json.dump(w, f)
        with os.fdopen(fd2, 'w', encoding='utf-8') as f:
            json.dump({'run': sc}, f)
        p = subprocess.Popen(['node', str(DRIVER), gpath, spath], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                             stderr=subprocess.PIPE, text=True, bufsize=1)
        while True:
            line = p.stdout.readline()
            if not line:
                raise RuntimeError('driver died: ' + p.stderr.read()[-2000:])
            msg = json.loads(line)
            if 'report' in msg:
                p.wait()
                return msg['report']['run']
            ans = run_pg(pg, msg['pg']) if 'pg' in msg else fake.handle(msg['http'])
            p.stdin.write(json.dumps(ans) + '\n')
            p.stdin.flush()
    finally:
        os.unlink(gpath)
        os.unlink(spath)


# ------------------------------------------------------------------------------------------------ seeding helpers

def row(pg, ch, pid, owner='inst-A', wfid=None, key='Bot0', reason=None, retired=False, app_user=U, channel=None):
    gone = 'now()' if (retired or reason) else 'null'
    pg.x(f"""insert into bot_profiles (profile_id, app_user_id, channel, bot_key, owner_id, workflow_id, deleted_at, deleted_reason)
             values ({L(pid)}, {L(app_user)}, {L(channel or CH[ch]['channel'])}, {L(key)}, {L(owner)}, {L(wfid)}, {gone}, {L(reason)});""")


def proxy_row(pg, ch, pid, inst='inst-A'):
    pg.q(bind(Q(PROXY, 'Bind Profile'), [pid, U, CH[ch]['channel'], inst]))


def chunks(pg, ch, wfid, n=2, fid='F1', other='-1', key=None):
    k = key or CH[ch]['key']
    o = CH[ch]['other'] if k == CH[ch]['key'] else CH[ch]['key']
    for i in range(n):
        pg.x(f"insert into documents (content, metadata) values ('r{i} {wfid}', jsonb_build_object('{k}', {L(wfid)}, '{o}', "
             f"{L(other)}, 'fileId', {L(fid)}));")


def nchunks(pg, ch, wfid, key=None):
    return pg.q(f"select count(*)::int c from documents where metadata->>'{key or CH[ch]['key']}' = {L(wfid)}")[0]['c']


def chat_data(pg, pid, chat='7701@c.us', tag='old'):
    """One chat of a profile in the tables the line moves (memory, history, outcomes, handoffs, ledger, state, flags)."""
    pg.x(f"insert into n8n_chat_histories (session_id, message) values ({L(pid + ':' + chat)}, "
         f"jsonb_build_object('type', 'human', 'content', {L(tag + ' m0')})), ({L(pid + ':' + chat)}, "
         f"jsonb_build_object('type', 'ai', 'content', {L(tag + ' m1')}));")
    pg.x(f"""insert into chat_history_events (profile_id, chat_id, message_id, source, body, sent_at)
             values ({L(pid)}, {L(chat)}, 'm-1', 'client', {L(tag)}, now());""")
    pg.x(f"""insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, summary, last_history_id, last_message_at,
             outcome_at, updated_at) values ({L(pid + ':' + chat)}, {L(pid)}, {L(chat)}, 'in_dialog', {L(tag)}, 1, now(), now(), now());""")
    pg.x(f"""insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary)
             values ({L(pid)}, {L(chat)}, 'm-1', 'order', {L(tag)});""")
    pg.x(f"""insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ({L(pid)}, {L(chat)}, true, 1);""")


def counts(pg, pid):
    p = L(pid)
    return pg.q(f"""select (select count(*) from n8n_chat_histories where split_part(session_id, ':', 1) = {p})::int as mem,
        (select count(*) from chat_history_events where profile_id = {p})::int as hist,
        (select count(*) from conversation_outcomes where profile_id = {p})::int as outc,
        (select count(*) from handoff_requests where profile_id = {p})::int as hand,
        (select count(*) from reply_mode_flags where profile_id = {p} and chat_id <> '*')::int as fl""")[0]


def prow(pg, pid):
    r = pg.q(f"select workflow_id, superseded_by, deleted_at is not null as retired, deleted_reason, owner_id from bot_profiles "
             f"where profile_id = {L(pid)}")
    return r[0] if r else None


def live_named(pg, wid):
    return pg.q(f"select count(*)::int c from bot_profiles where workflow_id = {L(wid)} and deleted_at is null")[0]['c'] > 0


def body(ch, pid, *, origin=None, prev=None, other='-1', app_user=True, key='Bot0', mode='auto'):
    c = CH[ch]
    b = {c['field']: pid, c['other_field']: other, 'Name': 'line', 'BotKey': key, 'BusinessType': 'Цветы',
         'BusinessTypeId': 'flowers', 'Business': 'About Business:\nx', 'Prompt': 'p', 'ProductsList': 'Products:\n',
         'ServicesList': 'Services:\n', 'ReplyMode': mode, 'ReplyModeVersion': '1700000000000'}
    if app_user:
        b['AppUserID'] = U
    if origin is not None:
        b['CreateOrigin'] = origin
    if prev is not None:
        b['PreviousProfileId'] = prev
    return b


SESSION = {'ok': True, 'mode': 'session', 'reason': None, 'installId': 'inst-A', 'appUserId': U}
LEGACY = {'ok': True, 'mode': 'legacy', 'reason': None, 'installId': None, 'appUserId': None}


# ------------------------------------------------------------------------------------------------ the scenarios

class S:
    def __init__(self, name, ch, mode='session', pid=None, seed=None, workflows=None, faults=None, pg=None, created=1,
                 origin='edit', prev=None, other='-1', expect=None, residual=(), check=None, repeat=False):
        self.name, self.ch, self.mode = name, ch, mode
        self.pid = pid or f'{ch.lower()}-p1'
        self.seed, self.workflows, self.faults, self.pg = seed, workflows or {}, faults or {}, pg or {}
        self.created = [W(ch, f'new{i}') for i in range(1, created + 1)]
        self.origin, self.prev, self.other = origin, prev, other
        self.expect = expect or {}
        self.residual, self.check, self.repeat = set(residual), check, repeat

    def scenario(self):
        legacy = self.mode == 'legacy'
        return {'body': body(self.ch, self.pid, origin=self.origin, prev=self.prev, other=self.other, app_user=not legacy),
                'start': 'If Has AppUserID?' if legacy else 'Retire Same Bot Slot',
                'crAuth': LEGACY if legacy else SESSION, 'pg': self.pg}


def scenarios(ch):
    p1, p2 = f'{ch.lower()}-p1', f'{ch.lower()}-p2'
    old, orph, frgn = W(ch, 'old'), W(ch, 'orph'), W(ch, 'frgn')
    new1, new2 = W(ch, 'new1'), W(ch, 'new2')
    twin = CH[ch]['twin']
    tw = W(twin, 'twin')
    out = []

    def bound_old(active=False, archived=False, owner='inst-A'):
        def f(pg):
            row(pg, ch, p1, owner=owner, wfid=old)
            chunks(pg, ch, old, 2, 'F1')
        return f, {old: {'active': active, 'archived': archived, 'path': p1}}

    def a3(pg):          # an earlier profile of the bot, bound to its clone, live; the new profile from the proxy
        row(pg, ch, p1, wfid=old)
        chunks(pg, ch, old, 2, 'F1')
        pg.x(f"insert into uploaded_files (file_id, owner_id, {CH[ch]['fcol']}) values ('F1', 'inst-A', {L(old)});")
        chat_data(pg, p1)
        proxy_row(pg, ch, p2)

    def retired_bot(pg, reason='trial_expiry'):   # F2: the sweep retired the bot, V12 switched its clone off
        row(pg, ch, p1, wfid=old, reason=reason)
        chunks(pg, ch, old, 2, 'F1')
        pg.x(f"insert into uploaded_files (file_id, owner_id, {CH[ch]['fcol']}) values ('F1', 'inst-A', {L(old)});")
        chat_data(pg, p1)
        proxy_row(pg, ch, p2)

    fail = lambda err: {'error': err}

    # --- session: the plain cases
    out.append(S('fresh (wizard, a new bot)', ch, pid=p2, seed=lambda pg: proxy_row(pg, ch, p2), origin='start',
                 expect={'created': True, 'adopted': []}))
    def activations(n):
        def chk(c, pg, fake, rep, resp):
            got = [x for x in fake.calls if x[0] == 'Activate Created Workflow']
            c.check(len(got) == n, f'activations of the reused clone: {got} (expected {n}: n8n 2.36.7 re-registers an active '
                                   f'workflow\'s webhook on activate)')
        return chk
    f, w = bound_old(active=False)
    out.append(S('reuse: the bound clone, inactive (channel saved off / session over): activated', ch, seed=f, workflows=w,
                 created=0, expect={'reuse': old}, check=activations(1)))
    f, w = bound_old(active=True)
    out.append(S('reuse: the bound clone, active (toggle off→on without saving): not activated again', ch, seed=f, workflows=w,
                 created=0, expect={'reuse': old}, check=activations(0)))

    def gone_check(c, pg, fake, rep, resp):
        c.check((nchunks(pg, ch, old), nchunks(pg, ch, new1)) == (0, 2), f'chunks old/new {(nchunks(pg, ch, old), nchunks(pg, ch, new1))}')
    f, _ = bound_old()
    out.append(S('the bound clone is gone (404): a new clone takes its price lists, the old id is booked', ch, seed=f, workflows={},
                 expect={'created': True, 'adopted': []}, check=gone_check))
    f, w = bound_old(archived=True)
    out.append(S('the bound clone is archived: a new clone, the archived one deleted', ch, seed=f, workflows=w,
                 expect={'created': True, 'adopted': [], 'deleted': [old]}, check=gone_check))
    f, w = bound_old(active=True)
    out.append(S('the probe answers 500: nothing is created (clone_unavailable)', ch, seed=f, workflows=w,
                 faults={'Probe Bound Clone': {'statusCode': 500, 'body': {'message': 'boom'}}}, expect=fail('clone_unavailable')))
    f, w = bound_old(active=True)
    out.append(S('the probe cannot reach n8n: nothing is created (clone_unavailable)', ch, seed=f, workflows=w,
                 faults={'Probe Bound Clone': {'throw': 'connect ECONNREFUSED 127.0.0.1:5678'}}, expect=fail('clone_unavailable')))

    # --- (а3) a new profile of the same bot (an older client: no CreateOrigin / PreviousProfileId — the bot key proves it)
    def a3_check(c, pg, fake, rep, resp):
        c.check((nchunks(pg, ch, old), nchunks(pg, ch, new1)) == (0, 2), f'chunks old/new {(nchunks(pg, ch, old), nchunks(pg, ch, new1))}')
        c.check(counts(pg, p1) == {'mem': 0, 'hist': 0, 'outc': 0, 'hand': 0, 'fl': 0}, f'left under the old profile {counts(pg, p1)}')
        c.check(counts(pg, p2) == {'mem': 2, 'hist': 1, 'outc': 1, 'hand': 1, 'fl': 1}, f'under the new profile {counts(pg, p2)}')
        r1 = prow(pg, p1)
        c.check(r1 and r1['superseded_by'] == p2 and r1['retired'], f'the old row {r1}')
    out.append(S('(а3) a new profile of the same bot: the old clone replaced, its data moved, DELETE ok', ch, pid=p2, seed=a3,
                 workflows={old: {'active': True, 'archived': False, 'path': p1}}, origin=None,
                 expect={'created': True, 'adopted': [p1], 'deleted': [old]}, check=a3_check))
    out.append(S('(а3) … and the replaced clone\'s DELETE fails: it stays booked for the sweep', ch, pid=p2, seed=a3,
                 workflows={old: {'active': True, 'archived': False, 'path': p1}}, origin=None,
                 faults={'Delete Replaced Clone': {'statusCode': 500, 'body': {'message': 'boom'}}},
                 expect={'created': True, 'adopted': [p1]}, check=a3_check))

    # --- (б) a failure after Create: the new clone is discarded, the answer has no id
    for label, faults, extra in (
            ('Activate cannot reach n8n', {'Activate Created Workflow': {'throw': 'getaddrinfo EAI_AGAIN'}}, {}),
            ('Activate refused: an orphan holds the webhook path', {}, {orph: {'active': True, 'archived': False, 'path': p2}}),
            ('Activate answers 500', {'Activate Created Workflow': {'statusCode': 500, 'body': {'message': 'boom'}}}, {}),
            ('the Wappi webhook answers 502', {'Set Wappi Webhook': {'statusCode': 502, 'body': {'detail': 'bad gateway'}}}, {}),
            ('the Wappi webhook types time out', {'Set Wappi Webhook Types': {'throw': 'ETIMEDOUT'}}, {})):
        out.append(S(f'(б) {label}: the new clone is discarded (create_failed)', ch, pid=p2, seed=lambda pg: proxy_row(pg, ch, p2),
                     workflows=extra, faults=faults, expect=fail('create_failed')))
    for label, attempts in (('a query error ×3', [{'queryError': 'connection terminated unexpectedly'}]),
                            ('an error thrown outside the query ×3', [{'throw': 'Connection pool exhausted'}]),
                            ('no items', [{'noItems': True}]),
                            ('bound = false (a concurrent Create won)', [{'rows': [{'bound': False, 'workflow_id': new1}]}])):
        out.append(S(f'(б) Bind Workflow Id: {label}: the new clone is discarded (create_failed)', ch, pid=p2,
                     seed=lambda pg: proxy_row(pg, ch, p2), pg={'Bind Workflow Id': attempts}, expect=fail('create_failed')))
    out.append(S('(б) Activate fails AND the discard answers 500: the new clone is journaled for the sweep', ch, pid=p2,
                 seed=lambda pg: proxy_row(pg, ch, p2),
                 faults={'Activate Created Workflow': {'statusCode': 500, 'body': {}},
                         'Discard New Clone': {'statusCode': 500, 'body': {}}}, expect=fail('create_failed')))
    out.append(S('(б) Activate fails, the discard AND the journal fail: the ONE named residual (Tier 2)', ch, pid=p2,
                 seed=lambda pg: proxy_row(pg, ch, p2),
                 faults={'Activate Created Workflow': {'statusCode': 500, 'body': {}},
                         'Discard New Clone': {'statusCode': 500, 'body': {}}},
                 pg={'Record Discard Failure': [{'queryError': 'connection terminated unexpectedly'}]},
                 expect=fail('create_failed'), residual={new1}))
    for label, attempts in (('a query error ×3', [{'queryError': 'connection terminated unexpectedly'}]),
                            ('an error thrown outside the query ×3', [{'throw': 'Connection pool exhausted'}]),
                            ('no items', [{'noItems': True}])):
        f, w = bound_old(active=True)
        out.append(S(f'Clone Plan: {label}: nothing is created (clone_plan_unavailable)', ch, seed=f, workflows=w,
                     pg={'Clone Plan': attempts}, expect=fail('clone_plan_unavailable')))

    # --- F2
    def r1_check(c, pg, fake, rep, resp):
        c.check(counts(pg, p2) == {'mem': 2, 'hist': 1, 'outc': 1, 'hand': 1, 'fl': 1}, f'under the new profile {counts(pg, p2)}')
        c.check(counts(pg, p1) == {'mem': 0, 'hist': 0, 'outc': 0, 'hand': 0, 'fl': 0}, f'left under the old profile {counts(pg, p1)}')
        c.check((nchunks(pg, ch, old), nchunks(pg, ch, new1)) == (0, 2), f'chunks old/new {(nchunks(pg, ch, old), nchunks(pg, ch, new1))}')
        fw = pg.q(f"select {CH[ch]['fcol']} as w from uploaded_files where file_id = 'F1'")[0]['w']
        c.check(fw == new1, f'the file registry names {fw}')
        r1 = prow(pg, p1)
        c.check(r1 and r1['superseded_by'] == p2 and r1['deleted_reason'] == 'trial_expiry', f'the old row {r1}')
    out.append(S('F2 R1: life → sweep (trial over) → purchase → reconnect with PreviousProfileId: everything under the new '
                 'connection, the prior clone deleted', ch, pid=p2, seed=retired_bot,
                 workflows={old: {'active': False, 'archived': False, 'path': p1}}, prev=p1,
                 expect={'created': True, 'adopted': [p1], 'deleted': [old]}, check=r1_check))
    out.append(S('F2 R4: the same pair again (the answer was lost): the bound clone is reused, nothing moves twice', ch, pid=p2,
                 seed=retired_bot, workflows={old: {'active': False, 'archived': False, 'path': p1}}, prev=p1, repeat=True,
                 expect={'reuse': new1, 'adopted': [p1]}, check=r1_check))

    def r9_seed(pg):
        retired_bot(pg)
        row(pg, twin, f'{twin.lower()}-p1', wfid=tw, reason='trial_expiry')          # the bot's other channel, retired too
        chunks(pg, twin, tw, 1, 'FT')
    def r9_check(c, pg, fake, rep, resp):
        r1_check(c, pg, fake, rep, resp)
        tp = prow(pg, f'{twin.lower()}-p1')
        c.check(tp and tp['superseded_by'] is None and tp['workflow_id'] == tw, f'the other channel\'s row was touched: {tp}')
        c.check(nchunks(pg, twin, tw) == 1 and tw in fake.wf, 'the other channel\'s clone or chunks were touched')
    out.append(S('F2 R9: a two-channel bot reconnects this channel with the other id moved away (-1): only this channel moves',
                 ch, pid=p2, seed=r9_seed, workflows={old: {'active': False, 'archived': False, 'path': p1},
                                                     tw: {'active': False, 'archived': False, 'path': f'{twin.lower()}-p1'}},
                 prev=p1, other='-1', expect={'created': True, 'adopted': [p1], 'deleted': [old]}, check=r9_check))

    def sentinel_seed(pg):
        row(pg, twin, f'{twin.lower()}-p1', wfid=tw)                                   # the other channel, live
        pg.x(f"insert into documents (content, metadata) values ('t', jsonb_build_object('{CH[ch]['key']}', '-1', "
             f"'{CH[ch]['other']}', {L(tw)}));")
        proxy_row(pg, ch, p2)
    def sentinel_check(c, pg, fake, rep, resp):
        c.check(nchunks(pg, ch, new1) == 1, f'the -1 chunk of the other channel was not taken: {nchunks(pg, ch, new1)}')
    out.append(S('F2 R9: this channel joins a bot of the other channel (the pre-F1 -1 rule still holds)', ch, pid=p2,
                 seed=sentinel_seed, workflows={tw: {'active': True, 'archived': False, 'path': f'{twin.lower()}-p1'}}, other=tw,
                 expect={'created': True, 'adopted': []}, check=sentinel_check))

    # --- negatives
    def foreign_seed(pg):
        row(pg, ch, 'pb', owner='inst-B', wfid=frgn, app_user=U_B)
        chunks(pg, ch, frgn, 2, 'FB')
        chat_data(pg, 'pb', tag='b')
        proxy_row(pg, ch, p2)
    def foreign_check(c, pg, fake, rep, resp):
        r = prow(pg, 'pb')
        c.check(r and r['workflow_id'] == frgn and not r['retired'] and r['superseded_by'] is None, f'the other install\'s row {r}')
        c.check(nchunks(pg, ch, frgn) == 2 and frgn in fake.wf and counts(pg, 'pb')['mem'] == 2, 'the other install\'s bot was touched')
    out.append(S('negative: another install\'s bot with the same bot key is never touched', ch, pid=p2, seed=foreign_seed,
                 workflows={frgn: {'active': True, 'archived': False, 'path': 'pb'}}, prev='pb',
                 expect={'created': True, 'adopted': []}, check=foreign_check))

    def wizard_check(c, pg, fake, rep, resp):
        c.check(counts(pg, p1)['mem'] == 2 and counts(pg, p2)['mem'] == 0, f'a wizard bot adopted: {counts(pg, p1)} {counts(pg, p2)}')
        c.check(nchunks(pg, ch, old) == 2 and old in fake.wf and prow(pg, p1)['superseded_by'] is None, 'the old bot was touched')
    out.append(S('negative: a wizard Create (a NEW bot in a reused slot) never adopts, even naming the old profile', ch, pid=p2,
                 seed=retired_bot, workflows={old: {'active': False, 'archived': False, 'path': p1}}, origin='start', prev=p1,
                 expect={'created': True, 'adopted': []}, check=wizard_check))
    f, w = bound_old(active=True)
    out.append(S('negative: a legacy Create on a profile an install owns is refused (profile_owned), nothing created', ch,
                 mode='legacy', seed=f, workflows=w, origin=None, expect=fail('profile_owned')))

    # --- legacy (compat window)
    out.append(S('legacy: fresh', ch, mode='legacy', seed=None, origin=None, expect={'created': True, 'adopted': []}))
    f, w = bound_old(active=False, owner=None)
    out.append(S('legacy: reuse an owner-less binding', ch, mode='legacy', seed=f, workflows=w, created=0, origin=None,
                 expect={'reuse': old}))
    f, _ = bound_old(owner=None)
    out.append(S('legacy: the owner-less binding is gone (404): a new clone, the old id booked', ch, mode='legacy', seed=f,
                 workflows={}, origin=None, expect={'created': True, 'adopted': []}, check=gone_check))
    out.append(S('legacy: Activate fails: the new clone is discarded', ch, mode='legacy', seed=None, origin=None,
                 faults={'Activate Created Workflow': {'statusCode': 500, 'body': {}}}, expect=fail('create_failed')))

    def legacy_a3_seed(pg):
        row(pg, ch, p1, owner=None, wfid=old)
    out.append(S('legacy: a second profile of an owner-less bot replaces nothing (no proof): the old clone stays bound', ch,
                 mode='legacy', pid=p2, seed=legacy_a3_seed, workflows={old: {'active': True, 'archived': False, 'path': p1}},
                 origin=None, expect={'created': True, 'adopted': []}))
    return out


# ------------------------------------------------------------------------------------------------ judging

def books(pg, fake, tracked, residual):
    """F1 §3 B: [(clone, verdict)] for every tracked clone that is neither deleted, nor bound, nor booked + swept."""
    lost = []
    for wid in sorted(tracked):
        if wid not in fake.wf or live_named(pg, wid):
            continue
        pg.x(f"update clone_retirements set created_at = now() - interval '11 minutes' where workflow_id = {L(wid)};")
        picked = {r['workflow_id'] for r in pg.q(CL.CLONE_RETIREMENTS_SQL)}
        if wid in picked:
            continue
        lost.append(wid)
    return sorted(set(lost) - residual), sorted(set(lost) & residual)


def judge(c, s, pg, fake, reps, before_live):
    rep = reps[-1]
    resp = (rep.get('response') or {}).get('json') if rep.get('response') else None
    stopped = rep.get('stoppedAt')
    c.check(stopped is None, f'the execution stopped (n8n answers 500): {stopped}')
    c.check(resp is not None, f'no answer: trace={" -> ".join(rep["trace"][-8:])}')
    resp = resp or {}
    exp = s.expect
    # the answer contract (§1.7)
    if 'error' in exp:
        c.check(resp == {'success': False, 'error': exp['error']}, f'answered {resp}, expected the refusal {exp["error"]}')
    else:
        c.check(set(resp) == {'id', 'created', 'adoptedProfiles'} and isinstance(resp.get('id'), str)
                and isinstance(resp.get('created'), bool) and isinstance(resp.get('adoptedProfiles'), list),
                f'the success answer is not {{id, created, adoptedProfiles}}: {resp}')
        if 'reuse' in exp:
            c.check(resp.get('id') == exp['reuse'] and resp.get('created') is False, f'expected the reuse of {exp["reuse"]}: {resp}')
        if exp.get('created'):
            c.check(resp.get('id') == s.created[0] and resp.get('created') is True, f'expected a new clone {s.created[0]}: {resp}')
        if 'adopted' in exp:
            c.check(sorted(resp.get('adoptedProfiles') or []) == sorted(exp['adopted']), f'adoptedProfiles {resp.get("adoptedProfiles")}')
        rid = resp.get('id')
        if isinstance(rid, str):
            r = prow(pg, s.pid)
            c.check(r is not None and r['workflow_id'] == rid and not r['retired'], f'the answered id is not bound to the profile: {r}')
            c.check(fake.wf.get(rid, {}).get('active') is True, f'the answered clone is not active: {fake.wf.get(rid)}')
            if s.mode == 'session':
                own = pg.q(bind(Q(AUTH, 'Reload Ownership'), ['inst-A']))[0]
                c.check(rid in (own.get('w') or []), f'the token would not list the answered clone: {own.get("w")}')
    if 'id' in resp and resp.get('success') is False:
        c.check(False, f'a refusal carries an id: {resp}')
    for d in exp.get('deleted', ()):
        c.check(d in fake.deleted and d not in fake.wf, f'{d} was not deleted: deleted={fake.deleted}')
    # no create on a refusal before the clone chain; never more than one create per run
    if exp.get('error') in ('clone_plan_unavailable', 'clone_unavailable', 'profile_owned'):
        c.check(fake.created == [], f'a refusal still created a clone: {fake.created}')
    c.check(len(fake.created) <= 1, f'clones created: {fake.created}')
    if 'reuse' in exp:
        c.check(fake.created == ([] if not s.repeat else [s.created[0]]), f'a reuse created a clone: {fake.created}')
    # nothing a live row names is ever deleted; nothing protected is ever touched
    for d in fake.deleted:
        c.check(not live_named(pg, d), f'deleted {d}, which a live row still names')
    touched = [x for x in fake.calls if any(p in x[3] for p in PROTECTED) and x[1] != 'GET']
    c.check(touched == [], f'a protected id was written: {touched}')
    # the books
    tracked = set(fake.created) | {w for w in before_live if w in s.workflows}
    lost, residual = books(pg, fake, tracked, s.residual)
    c.check(lost == [], f'LOST clone(s) {lost}: neither deleted, nor bound to a live row, nor booked for the sweep')
    c.check(residual == sorted(s.residual), f'the named residual did not happen as named: {residual} != {sorted(s.residual)}')
    if s.check:
        s.check(c, pg, fake, rep, resp)


def run_case(s):
    w = canon(CH[s.ch]['create'])
    tag = 'HEAD' if HEAD_MODE else 'canon'
    with Case(f'T3 [{s.ch}] {s.mode}: {s.name} [{tag}]') as c:
        with PgSession(SETUP) as pg:
            if s.seed:
                s.seed(pg)
            fake = Fake(s.ch, s.workflows, s.faults, s.created)
            before_live = {r['workflow_id'] for r in pg.q("select workflow_id from bot_profiles where deleted_at is null "
                                                          "and workflow_id is not null")}
            reps = [drive(w, s.scenario(), pg, fake)]
            if s.repeat:
                reps.append(drive(w, s.scenario(), pg, fake))
                first = (reps[0].get('response') or {}).get('json') or {}
                c.check(first.get('id') == s.created[0] and first.get('created') is True, f'the first run: {first}')
            judge(c, s, pg, fake, reps, before_live)
            if VERBOSE:
                for i, r in enumerate(reps):
                    print(f'--- {s.ch} {s.mode}: {s.name} (run {i + 1})\n    trace: {" -> ".join(r["trace"])}\n'
                          f'    answer: {(r.get("response") or {}).get("json")} stopped: {r.get("stoppedAt")}')
                print(f'    n8n/Wappi calls: {[(x[0], x[1], x[4]) for x in fake.calls]}\n    created {fake.created} deleted {fake.deleted} '
                      f'left {sorted(fake.wf)}\n    clone_retirements: {pg.q("select workflow_id, reason, replaced_by from clone_retirements")}')


for ch in CH:
    for s in scenarios(ch):
        run_case(s)
report()
