#!/usr/bin/env python3
"""T5 (F1 + F2, plan docs/audits/2026-10-09-followups/F1F2-plan.md §1.6, §3 T5): no replaced clone is left undeleted, and
deleting a bot deletes its whole line — on the REAL canonical nodes (Profile Lifecycle Sweep, Delete Bot Files, Create
WA/TG), real SQL on session temp tables in the staging Postgres, n8n's own expression engine. No n8n server, no network,
never production.

  A1–A5  the sweep's branch A «Clone Retirements»: it takes the clones booked in clone_retirements (Create's replaced
         clones, a failed discard, a deleted bot's line) once they are ten minutes old — never one a LIVE row drives,
         never a protected or malformed id — DELETEs them on localhost with the `n8n Admin API` credential and stamps
         200/404 as gone, anything else as one more attempt; `retirementsDryRun` (true until the first day on prod is
         clean, plan §6 steps 5 and 12) only logs, and a missing flag never deletes;
  V1     V12 `Retired Clones` no longer deactivates a SUPERSEDED row's clone (the line booked it for deletion already);
  L1–L7  Delete Bot Files by the bot's line (WhatsApp and Telegram): the app names the bot's CURRENT ids, the server
         deletes every row of this install with the same bot key (both channels) and the superseded chain — memory,
         history, outcomes, handoffs, reply ledger, chat state, flags, chunks and stored originals of every clone of
         the line —, marks every line row app_delete and books the line's clones (bot_deleted) for the sweep:
           L1 the pre-F1 orphans (F1 §1.4: a re-paired profile retired by Retire Same Bot Slot with no reason, its
              clone's chunks) and an earlier deleted bot of the slot (re-deleted without an error);
           L2 the post-F1 line: a predecessor superseded by the reconnect (the real Clone Plan + Bind), the other
              channel's retired row (the client names it no more), a key-less ancestor reached by the chain; a
              second call with the retired ids (the T8 client) is idempotent;
           L3 the slot re-used after the delete: an older client's Create in the same slot adopts nothing;
           L4 negatives: another install's rows with the same bot key, another bot key, a key-less row outside the
              chain, a clone a LIVE row outside the line drives, a protected id — untouched;
           L5 legacy (compat window): the named ids only, nothing booked, a retired row keeps its reason;
           L6 the deleting order and the honest answer stay (originals first, every counter a number);
           L7 the chunk statements read the RAG store through its expression indexes and delete by primary key;
  B1     the bookkeeping end to end: what the delete booked is what the sweep's branch A deletes, once;
  N1–N3  the guards reject copies that delete by the named ids only, drop the dry-run gate or call a public URL.

    python3 Tools/n8n/tests/test_clone_retirements.py
"""
import json
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from harness import N8N, Case, PgSession, bind, code, expr, module, node, report, run_js, sql_literal as L, wf

SWEEP_F = 'fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json'
DBF_F = 'lmjYsdNcQA2IE5rl-Delete_Bot_Files.json'
PROXY_F = 'crWappiProxy0001-Wappi_Proxy.json'
SWEEP, DBF, PROXY = wf(SWEEP_F), wf(DBF_F), wf(PROXY_F)
CL = module('clone_lineage.py')
U = '$RCAnonymousID:' + '0123456789abcdef' * 2
CH = {
    'WA': {'channel': 'whatsapp', 'create': 'XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'key': 'botWaId', 'other': 'TG'},
    'TG': {'channel': 'telegram', 'create': 'Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', 'key': 'botTgId', 'other': 'WA'},
}
CREATE = {ch: wf(spec['create']) for ch, spec in CH.items()}
LOCAL = 'http://localhost:5678/api/v1/workflows/'
TABLES = ('installs', 'subscribers', 'bot_profiles', 'documents', 'uploaded_files', 'clone_retirements', 'n8n_chat_histories',
          'chat_history_events', 'conversation_outcomes', 'handoff_requests', 'reply_events', 'chat_reply_state', 'reply_mode_flags')
SETUP = ''.join('create temp table {0} (like public.{0} including all);\n'.format(t) for t in TABLES) + """
insert into installs (install_id, secret_hash) values ('inst-A', 'h'), ('inst-B', 'h');
"""
ZERO = {'mem': 0, 'hist': 0, 'outc': 0, 'hand': 0, 'ev': 0, 'st': 0, 'fl': 0}


def E(n, i=0):
    return {'node': n, 'type': 'main', 'index': i}


def edges(w, src, idx=0):
    outs = w['connections'].get(src, {}).get('main', [])
    return outs[idx] if idx < len(outs) else []


def preds(w, target):
    return sorted((src, i) for src, outs in w['connections'].items() for i, out in enumerate(outs.get('main', [])) for c in out
                  if c['node'] == target)


def reachable(w, start, cut=()):
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for i, out in enumerate(w['connections'].get(n, {}).get('main', []) or []):
            if (n, i) not in cut:
                todo.extend(c['node'] for c in out or [])
    return seen


def Q(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')    # temp tables shadow the UNqualified names only


def P(w, name, json_=None, named=None):
    v = expr(node(w, name)['parameters']['options']['queryReplacement'], json_, named)
    return [x for x in v.split(',')] if isinstance(v, str) else v


def cond(w, name, json_=None, named=None):
    return expr(node(w, name)['parameters']['conditions']['conditions'][0]['leftValue'], json_, named)


def W(ch, tag):
    return (ch + tag + '0' * 16)[:16]


def token(claims):
    return run_js("""const s = require('crypto');
const b64u = (b) => Buffer.from(b).toString('base64').replace(/\\+/g,'-').replace(/\\//g,'_').replace(/=+$/,'');
const p = b64u(JSON.stringify($input.first().json.claims));
return [{json: {t: 'v1.' + p + '.' + b64u(s.createHmac('sha256', 'synthetic-test-secret').update('v1.' + p).digest())}}];""",
                  {'json': {'claims': {'c': [], 'e': int(time.time()) + 3600, 'n': 1, **claims}}})[0]['json']['t']


def gate(tok, body, compat_open=False):
    headers = {'authorization': 'Bearer ' + tok} if tok else {}
    env = {'CR_AUTH_COMPAT_UNTIL': '2099-01-01'} if compat_open else {}
    return code(DBF, 'Auth Gate', {'json': {'headers': headers, 'body': body}}, env=env)[0]['json']


# ---------------------------------------------------------------- seeding

def row(pg, ch, pid, owner='inst-A', wfid=None, key='Bot0', reason=None, sup=None, live=None):
    gone = 'now()' if (reason is not None and live is not True) else 'null'
    pg.x(f"""insert into bot_profiles (profile_id, app_user_id, channel, bot_key, owner_id, workflow_id, deleted_at, deleted_reason,
                superseded_by) values ({L(pid)}, {L(U)}, {L(CH[ch]['channel'])}, {L(key)}, {L(owner)}, {L(wfid)}, {gone},
                {L(reason)}, {L(sup)});""")


def born(pg, ch, pid, key='Bot0', inst='inst-A', user=U):
    """A profile as the app makes it today: the proxy's Bind Profile (owned from birth), then Create's Retire Same Bot
    Slot + Register Profile (the real canonical statements; `user` = the install's billing id)."""
    pg.q(bind(Q(PROXY, 'Bind Profile'), [pid, user, CH[ch]['channel'], inst]))
    pg.x(bind(Q(CREATE[ch], 'Retire Same Bot Slot'), [user, CH[ch]['channel'], key]))
    pg.x(bind(Q(CREATE[ch], 'Register Profile'), [pid, user, CH[ch]['channel'], key]))


def plan(pg, ch, pid, key='Bot0', origin='', prev='', inst='inst-A'):
    return pg.q(bind(Q(CREATE[ch], 'Clone Plan'), ['session', inst, pid, CH[ch]['channel'], key, origin, prev]))[0]


def bind_clone(pg, ch, pid, wfid, p, inst='inst-A'):
    """Create's Bind Workflow Id (the real canonical statement) with the plan's lists."""
    return pg.q(bind(Q(CREATE[ch], 'Bind Workflow Id'),
                     [pid, wfid, U, CH[ch]['channel'], inst, '', json.dumps(p.get('replace_ids') or []),
                      json.dumps(p.get('pred_profiles') or []), '-1']))[0]


def chat_data(pg, pid, chat='77001@c.us', tag='t'):
    """One chat of a profile in all seven tables Delete Chat Memory clears."""
    t, msg = "now() - interval '1 hour'", 'm-' + pid
    pg.x(f"""insert into n8n_chat_histories (session_id, message) values ({L(pid + ':' + chat)}, jsonb_build_object('type', 'human', 'content', {L(tag)}));
insert into chat_history_events (profile_id, chat_id, message_id, source, body, sent_at, created_at)
  values ({L(pid)}, {L(chat)}, {L(msg)}, 'client', {L(tag)}, {t}, {t});
insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, summary, last_history_id, last_message_at, outcome_at, updated_at)
  values ({L(pid + ':' + chat)}, {L(pid)}, {L(chat)}, 'in_dialog', {L(tag)}, 1, {t}, {t}, {t});
insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary, created_at, updated_at)
  values ({L(pid)}, {L(chat)}, {L(msg)}, 'order', {L(tag)}, {t}, {t});
insert into reply_events (profile_id, chat_id, message_id, received_at, updated_at, state, error) values ({L(pid)}, {L(chat)}, {L(msg)}, {t}, {t}, 'sent', {L(tag)});
insert into chat_reply_state (profile_id, chat_id, incoming_generation, newest_message_time, last_sent_message_id)
  values ({L(pid)}, {L(chat)}, 3, extract(epoch from {t})::bigint, {L(tag)});
insert into reply_mode_flags (profile_id, chat_id, suppressed, updated_at, version) values ({L(pid)}, {L(chat)}, true, {t}, 1);""")


def chunks(pg, ch, wfid, fid, n=2):
    k = CH[ch]['key']
    o = CH[CH[ch]['other']]['key']
    for i in range(n):
        pg.x(f"insert into documents (content, metadata) values ('r{i} {wfid}', jsonb_build_object('{k}', {L(wfid)}, '{o}', '-1', "
             f"'fileId', {L(fid)}));")


def counts(pg, pid):
    p = L(pid)
    return pg.q(f"""select (select count(*) from n8n_chat_histories where split_part(session_id, ':', 1) = {p})::int as mem,
        (select count(*) from chat_history_events where profile_id = {p})::int as hist,
        (select count(*) from conversation_outcomes where profile_id = {p})::int as outc,
        (select count(*) from handoff_requests where profile_id = {p})::int as hand,
        (select count(*) from reply_events where profile_id = {p})::int as ev,
        (select count(*) from chat_reply_state where profile_id = {p})::int as st,
        (select count(*) from reply_mode_flags where profile_id = {p} and chat_id <> '*')::int as fl""")[0]


def nchunks(pg, wfid):
    return pg.q(f"select count(*)::int c from documents where metadata->>'botWaId' = {L(wfid)} or metadata->>'botTgId' = {L(wfid)}")[0]['c']


def prow(pg, pid):
    r = pg.q(f"select deleted_at is not null as retired, deleted_reason as reason from bot_profiles where profile_id = {L(pid)}")
    return r[0] if r else None


def booked(pg):
    return {r['workflow_id']: r['reason'] for r in pg.q("select workflow_id, reason from clone_retirements order by 1")}


def dbf_body(ch, pid, wfid):
    wa = ch == 'WA'
    return {'botWaId': wfid if wa else '-1', 'botTgId': '-1' if wa else wfid, 'waProfileId': pid if wa else '-1',
            'tgProfileId': '-1' if wa else pid, 'appUserId': U}


def delete_bot(pg, body, install='inst-A', compat=False, storage=200):
    """The app's delete of a bot, through the canonical Delete Bot Files from the gate to its verdict. Returns a dict of
    what each node produced (the deleting statements run only past Owned?(0) and Originals Deleted?(0))."""
    g = gate(None if compat else token({'i': install, 'u': None, 'p': [], 'w': []}), body, compat_open=compat)
    out = {'gate': g}
    if g['crAuth']['ok'] is not True:
        return out
    named = {'Webhook': {k: v for k, v in g.items() if k != 'crAuth'}, 'Auth Gate': g}
    own = pg.q(bind(Q(DBF, 'Delete Ownership'), P(DBF, 'Delete Ownership', g, named)))[0]
    out['own'] = own
    if cond(DBF, 'Owned?', own) is not True:
        return out
    listed = pg.q(bind(Q(DBF, 'List Bot Originals'), P(DBF, 'List Bot Originals', own, named)))[0]
    out['listed'] = sorted(listed.get('fileIds') or [])
    split = [i['json'] for i in code(DBF, 'Split File Ids', {'json': {}}, named={'List Bot Originals': listed})]
    files = [s for s in split if cond(DBF, 'Has Original?', s) is True]
    results = [{'statusCode': storage, 'body': {}} for _ in files] or [s for s in split if s not in files]
    settled = code(DBF, 'Originals Settled', items=[{'json': r} for r in results], named={'Auth Gate': g})[0]['json']
    if cond(DBF, 'Originals Deleted?', settled) is not True:
        out['settled'] = settled
        return out
    out['retire'] = retire = pg.q(bind(Q(DBF, 'Retire Bot Profiles'), P(DBF, 'Retire Bot Profiles', settled, named)))[0]
    out['chunks'] = ch_ = pg.q(bind(Q(DBF, 'Delete Bot Chunks'), P(DBF, 'Delete Bot Chunks', retire, named)))[0]
    out['mem'] = mem = pg.q(bind(Q(DBF, 'Delete Chat Memory'), P(DBF, 'Delete Chat Memory', ch_, named)))[0]
    out['ok'] = cond(DBF, 'Deletion Settled?', mem, {'Retire Bot Profiles': retire, 'Delete Bot Chunks': ch_})
    return out


def sweep_picks(pg):
    return [r['workflow_id'] for r in pg.q(Q(SWEEP, 'Clone Retirements'))]


def age_bookings(pg, minutes=11):
    pg.x(f"update clone_retirements set created_at = now() - interval '{minutes} minutes';")


# ------------------------------------------------------------------------------------------------ A: the sweep's branch A
BRANCH_A = ('Clone Retirements', 'Retirements Dry Run?', 'Log Would Remove', 'Has Retired Clone?', 'Delete Retired Clone',
            'Retired Clone Gone?', 'Stamp Retirement Removed', 'Stamp Retirement Kept')

with Case('T5 A1: the sweep carries branch A — Sweep Config → Clone Retirements (= clone_lineage) → Retirements Dry Run? → '
          'Has Retired Clone? → Delete Retired Clone → Retired Clone Gone? → Stamp Removed / Kept (= clone_lineage); below '
          'every other branch') as c:
    c.check(preds(SWEEP, 'Clone Retirements') == [('Sweep Config', 0)], f'Clone Retirements <- {preds(SWEEP, "Clone Retirements")}')
    c.check(node(SWEEP, 'Clone Retirements')['parameters']['query'] == CL.CLONE_RETIREMENTS_SQL, 'Clone Retirements is not clone_lineage\'s')
    c.check(edges(SWEEP, 'Clone Retirements') == [E('Retirements Dry Run?')]
            and edges(SWEEP, 'Retirements Dry Run?') == [E('Log Would Remove')] and edges(SWEEP, 'Retirements Dry Run?', 1) == [E('Has Retired Clone?')]
            and edges(SWEEP, 'Has Retired Clone?') == [E('Delete Retired Clone')] and edges(SWEEP, 'Has Retired Clone?', 1) == []
            and edges(SWEEP, 'Delete Retired Clone') == [E('Retired Clone Gone?')]
            and edges(SWEEP, 'Retired Clone Gone?') == [E('Stamp Retirement Removed')]
            and edges(SWEEP, 'Retired Clone Gone?', 1) == [E('Stamp Retirement Kept')], 'branch A wiring')
    c.check(preds(SWEEP, 'Delete Retired Clone') == [('Has Retired Clone?', 0)] and preds(SWEEP, 'Has Retired Clone?') == [('Retirements Dry Run?', 1)],
            'the DELETE is reachable only past the dry-run gate and the id check')
    leak = {'Delete Retired Clone'} & reachable(SWEEP, 'Every 6 Hours', cut={('Retirements Dry Run?', 1)})
    c.check(not leak, f'a DELETE reachable without Retirements Dry Run?[1]: {leak}')
    for n, sql in (('Stamp Retirement Removed', CL.STAMP_RETIREMENT_REMOVED_SQL), ('Stamp Retirement Kept', CL.STAMP_RETIREMENT_KEPT_SQL)):
        nn = node(SWEEP, n)
        c.check(nn['parameters']['query'] == sql and nn.get('onError') == 'continueRegularOutput', f'{n} is not clone_lineage\'s')
    others = ('Candidates', 'List WA Profiles', 'Mark Liveness Deleted', 'Retired Clones', 'Log Would Deactivate', 'Stamp Clone Kept')
    c.check(node(SWEEP, 'Clone Retirements')['position'][1] > max(node(SWEEP, n)['position'][1] for n in others),
            'branch A must sit BELOW every other branch (n8n v1 runs sibling branches top-down: a clone retired earlier in this run is seen)')
    liveness = {'Alive Registry', 'Compute Liveness Diff', 'Is Retire Candidate?', 'Liveness Dry Run?', 'Mark Liveness Deleted'}
    c.check(not any(src in liveness for n in BRANCH_A for src, _ in preds(SWEEP, n)), 'the liveness branch must never feed branch A')

with Case('T5 A2: Clone Retirements takes booked clones older than ten minutes — never one a LIVE row drives, never a '
          'protected / malformed / removed id; least tried first; at most 50') as c:
    with PgSession(SETUP) as pg:
        def book(wid, minutes, removed=False, attempts=0, reason='replaced'):
            pg.x(f"""insert into clone_retirements (workflow_id, reason, created_at, removed_at, attempts)
                     values ({L(wid)}, {L(reason)}, now() - interval '{minutes} minutes', {'now()' if removed else 'null'}, {attempts});""")
        book('OldClone00000001', 60)
        book('OldClone00000002', 11, attempts=3)
        book('OldClone00000003', 30, reason='discard_failed')
        book('OldClone00000004', 45, reason='bot_deleted')
        book('YoungClone000001', 5)
        book('GoneClone0000001', 60, removed=True)
        book(CL.PROTECTED[0], 60)
        book('bad id', 60)
        book('LiveDriven000001', 60)
        row(pg, 'WA', 'wa-live', wfid='LiveDriven000001')
        book('RetiredOnly00001', 60)
        row(pg, 'WA', 'wa-ret', wfid='RetiredOnly00001', reason='replaced')
        got = sweep_picks(pg)
        c.check(got == ['OldClone00000001', 'RetiredOnly00001', 'OldClone00000004', 'OldClone00000003', 'OldClone00000002'], f'picked {got}')
        pg.x("insert into clone_retirements (workflow_id, reason, created_at) select 'Bulk' || lpad(g::text, 12, '0'), 'replaced', "
             "now() - interval '1 hour' from generate_series(1, 80) g;")
        c.check(len(sweep_picks(pg)) == 50, 'more than 50 per run')

with Case('T5 A3: Has Retired Clone? passes only a clone-shaped unprotected id; Delete Retired Clone is a DELETE on localhost with '
          'the n8n Admin API placeholder, reporting its status; Retired Clone Gone? = 200/404') as c:
    good = {'workflow_id': 'OldClone00000001', 'reason': 'replaced', 'attempts': 0}
    c.check(cond(SWEEP, 'Has Retired Clone?', good) is True, 'a clone id must pass')
    for bad in (CL.PROTECTED[0], 'crAuthSession001', '-1', '', 'wf/../x', 'short', None, 'x' * 40):
        c.check(cond(SWEEP, 'Has Retired Clone?', {'workflow_id': bad}) is False, f'Has Retired Clone? passed {bad!r}')
    d = node(SWEEP, 'Delete Retired Clone')
    resp = (d['parameters'].get('options') or {}).get('response', {}).get('response', {})
    c.check(d['parameters'].get('method') == 'DELETE' and expr(d['parameters']['url'], good) == LOCAL + 'OldClone00000001'
            and d['credentials'] == {'httpHeaderAuth': {'id': '__HDR_CRED__', 'name': 'n8n Admin API'}}
            and resp == {'fullResponse': True, 'neverError': True} and d.get('onError') == 'continueRegularOutput'
            and d['parameters'].get('authentication') == 'genericCredentialType', f'Delete Retired Clone {json.dumps(d)[:600]}')
    verdict = {s: cond(SWEEP, 'Retired Clone Gone?', {'statusCode': s}) for s in (200, 404, 409, 500, 401)}
    c.check(verdict == {200: True, 404: True, 409: False, 500: False, 401: False}
            and cond(SWEEP, 'Retired Clone Gone?', {'error': {'message': 'ECONNREFUSED'}}) is False, f'Retired Clone Gone? {verdict}')

with Case('T5 A4: the stamps — 200/404 marks the booking removed (never picked again); anything else counts one attempt with '
          'the status or the error, and the booking is picked again') as c:
    with PgSession(SETUP) as pg:
        for wid in ('OldClone00000001', 'OldClone00000002', 'OldClone00000003'):
            pg.x(f"insert into clone_retirements (workflow_id, reason, created_at) values ({L(wid)}, 'replaced', now() - interval '1 hour');")
        pick = lambda wid: {'workflow_id': wid, 'reason': 'replaced', 'attempts': 0}
        r = pg.q(bind(Q(SWEEP, 'Stamp Retirement Removed'), P(SWEEP, 'Stamp Retirement Removed', {'statusCode': 404, 'body': {}},
                                                              {'Clone Retirements': pick('OldClone00000001')})))
        c.check(r == [{'workflow_id': 'OldClone00000001'}], f'removed {r}')
        r = pg.q(bind(Q(SWEEP, 'Stamp Retirement Kept'), P(SWEEP, 'Stamp Retirement Kept', {'statusCode': 500, 'body': {}},
                                                           {'Clone Retirements': pick('OldClone00000002')})))
        c.check(r == [{'workflow_id': 'OldClone00000002', 'attempts': 1}], f'kept (500) {r}')
        r = pg.q(bind(Q(SWEEP, 'Stamp Retirement Kept'), P(SWEEP, 'Stamp Retirement Kept', {'error': {'message': 'connect ECONNREFUSED'}},
                                                           {'Clone Retirements': pick('OldClone00000003')})))
        c.check(r == [{'workflow_id': 'OldClone00000003', 'attempts': 1}], f'kept (no response) {r}')
        st = {x['workflow_id']: x for x in pg.q("select workflow_id, removed_at is not null as removed, attempts, last_status from clone_retirements")}
        c.check(st['OldClone00000001'] == {'workflow_id': 'OldClone00000001', 'removed': True, 'attempts': 0, 'last_status': '404'}
                and st['OldClone00000002']['last_status'] == '500' and not st['OldClone00000002']['removed']
                and 'ECONNREFUSED' in (st['OldClone00000003']['last_status'] or ''), f'state {st}')
        c.check(sweep_picks(pg) == ['OldClone00000002', 'OldClone00000003'], f'after the stamps {sweep_picks(pg)}')

with Case('T5 A5: retirementsDryRun — the canonical flag is a boolean on Sweep Config; true only logs, an explicit false '
          'deletes, a MISSING flag never deletes') as c:
    cfg = {a['name']: a for a in node(SWEEP, 'Sweep Config')['parameters']['assignments']['assignments']}
    flag = cfg.get('retirementsDryRun')
    c.check(flag is not None and flag.get('type') == 'boolean' and isinstance(flag.get('value'), bool), f'Sweep Config {list(cfg)}')
    c.check(cfg.get('dryRun', {}).get('value') is False, 'the other branches\' dryRun must stay as it was (false)')
    for cfg_json, want in (({'dryRun': False, 'retirementsDryRun': True}, True), ({'dryRun': False, 'retirementsDryRun': False}, False),
                           ({'dryRun': False}, True), ({'dryRun': False, 'retirementsDryRun': 'false'}, True)):
        got = cond(SWEEP, 'Retirements Dry Run?', {'workflow_id': 'OldClone00000001'}, {'Sweep Config': cfg_json})
        c.check(got is want, f'{cfg_json} -> dry run {got}')
    log = {a['name']: a['value'] for a in node(SWEEP, 'Log Would Remove')['parameters']['assignments']['assignments']}
    c.check(log.get('dryRun') is True and log.get('action') == 'would_delete_clone'
            and expr(log.get('workflowId', ''), {'workflow_id': 'OldClone00000001', 'reason': 'replaced'}) == 'OldClone00000001',
            f'Log Would Remove {log}')

# ------------------------------------------------------------------------------------------------ V: V12 Retired Clones
with Case('T5 V1: V12 Retired Clones never deactivates the clone of a SUPERSEDED row (the line booked it); an unsuperseded '
          'trial_expiry row is still picked') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'WA', 'wa-s1', wfid='TrialClone000001', reason='trial_expiry')
        row(pg, 'WA', 'wa-s2', wfid='TrialClone000002', reason='trial_expiry', sup='wa-s3')
        row(pg, 'WA', 'wa-s3', wfid='NewClone00000003')
        picked = sorted(r['profile_id'] for r in pg.q(Q(SWEEP, 'Retired Clones')))
        c.check(picked == ['wa-s1'], f'Retired Clones picked {picked}')

# ------------------------------------------------------------------------------------------------ L: Delete Bot Files by the line
for ch, spec in CH.items():
    oc = spec['other']
    with Case(f'T5 L1 [{ch}] F1 §1.4: the app deletes by the CURRENT ids after a re-pairing; the orphaned predecessor (retired '
              f'by Retire Same Bot Slot, no reason) loses its memory, chunks and stored originals, its clone is booked; an '
              f'earlier deleted bot of the slot is re-deleted without an error') as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, f'{ch}-slot', wfid=W(ch, 'slot'), reason='app_delete')         # an earlier bot of this slot, deleted
            born(pg, ch, f'{ch}-P1')
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'old'))} where profile_id = {L(ch + '-P1')};")   # the pre-F1 Bind
            chat_data(pg, f'{ch}-P1', tag='old')
            chunks(pg, ch, W(ch, 'old'), 'f-old')
            born(pg, ch, f'{ch}-P2')                                                     # the re-pairing: P1 retired, reason NULL
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'new'))} where profile_id = {L(ch + '-P2')};")
            chat_data(pg, f'{ch}-P2', tag='new')
            chunks(pg, ch, W(ch, 'new'), 'f-new')
            born(pg, ch, f'{ch}-P7', key='Bot1')                                         # another bot of this install
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'seven'))} where profile_id = {L(ch + '-P7')};")
            chat_data(pg, f'{ch}-P7')
            chunks(pg, ch, W(ch, 'seven'), 'f-seven')
            born(pg, ch, f'{ch}-PB', inst='inst-B', user='rc-B')                         # another install, the same slot name
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'B'))} where profile_id = {L(ch + '-PB')};")
            chat_data(pg, f'{ch}-PB')
            chunks(pg, ch, W(ch, 'B'), 'f-B')
            c.check(prow(pg, f'{ch}-P1') == {'retired': True, 'reason': None}, f'premise: P1 {prow(pg, ch + "-P1")}')
            out = delete_bot(pg, dbf_body(ch, f'{ch}-P2', W(ch, 'new')))
            c.check(out.get('own', {}).get('allowed') is True, f'Delete Ownership {out.get("own")}')
            c.check(out.get('listed') == ['f-new', 'f-old'], f'the stored originals of the whole line must be listed: {out.get("listed")}')
            c.check(out.get('ok') is True, f'Deletion Settled? {out.get("retire")} {out.get("chunks")} {out.get("mem")}')
            left = {p: counts(pg, p) for p in (f'{ch}-P1', f'{ch}-P2') if counts(pg, p) != ZERO}
            c.check(left == {}, f'left under the line\'s profiles: {left}')
            c.check(nchunks(pg, W(ch, 'old')) == 0 and nchunks(pg, W(ch, 'new')) == 0,
                    f'chunks left: old {nchunks(pg, W(ch, "old"))} new {nchunks(pg, W(ch, "new"))}')
            c.check(all(prow(pg, p) == {'retired': True, 'reason': 'app_delete'} for p in (f'{ch}-P1', f'{ch}-P2', f'{ch}-slot')),
                    f'line rows: {[prow(pg, p) for p in (ch + "-P1", ch + "-P2", ch + "-slot")]}')
            c.check(booked(pg) == {W(ch, 'new'): 'bot_deleted', W(ch, 'old'): 'bot_deleted', W(ch, 'slot'): 'bot_deleted'},
                    f'booked {booked(pg)}')
            c.check((out.get('retire') or {}).get('retiredProfiles') == 2 and (out.get('retire') or {}).get('bookedClones') == 3,
                    f'Retire Bot Profiles {out.get("retire")}')
            for p, w in ((f'{ch}-P7', W(ch, 'seven')), (f'{ch}-PB', W(ch, 'B'))):
                c.check(all(v == 1 for v in counts(pg, p).values()) and nchunks(pg, w) == 2 and prow(pg, p) == {'retired': False, 'reason': None},
                        f'{p} must be untouched: {counts(pg, p)} chunks {nchunks(pg, w)} row {prow(pg, p)}')

    with Case(f'T5 L2 [{ch}] the post-F1 line: a predecessor superseded by the reconnect (real Clone Plan + Bind), the other '
              f'channel\'s retired row the client no longer names, a key-less ancestor of the chain — all deleted, every row '
              f'app_delete, every clone booked; a second call with the retired ids (T8) changes nothing') as c:
        with PgSession(SETUP) as pg:
            born(pg, ch, f'{ch}-P1')
            p1 = plan(pg, ch, f'{ch}-P1', origin='start')
            b1 = bind_clone(pg, ch, f'{ch}-P1', W(ch, 'one'), p1)
            chat_data(pg, f'{ch}-P1', tag='one')
            chunks(pg, ch, W(ch, 'one'), 'f-one')
            pg.x(f"update bot_profiles set deleted_at = now(), deleted_reason = 'trial_expiry' where profile_id = {L(ch + '-P1')};")
            born(pg, ch, f'{ch}-P2')
            p2 = plan(pg, ch, f'{ch}-P2', origin='edit', prev=f'{ch}-P1')
            b2 = bind_clone(pg, ch, f'{ch}-P2', W(ch, 'two'), p2)
            c.check(b1.get('bound') is True and b2.get('bound') is True and b2.get('adopted_profiles') == [f'{ch}-P1'],
                    f'premise: the reconnect adopted P1: {b1} {b2}')
            row(pg, ch, f'{ch}-P0', wfid=W(ch, 'zero'), key=None, reason='liveness', sup=f'{ch}-P1')     # a key-less ancestor
            chat_data(pg, f'{ch}-P0', tag='zero')
            chunks(pg, ch, W(ch, 'zero'), 'f-zero')
            born(pg, oc, f'{oc}-T1')                                                       # the other channel of the bot
            pg.x(f"update bot_profiles set workflow_id = {L(W(oc, 'tee'))}, deleted_at = now(), deleted_reason = 'trial_expiry' "
                 f"where profile_id = {L(oc + '-T1')};")
            chat_data(pg, f'{oc}-T1', tag='tee')
            chunks(pg, oc, W(oc, 'tee'), 'f-tee')
            out = delete_bot(pg, dbf_body(ch, f'{ch}-P2', W(ch, 'two')))
            c.check(out.get('ok') is True and out.get('listed') == ['f-one', 'f-tee', 'f-zero'], f'delete {out.get("listed")} ok={out.get("ok")}')
            line = (f'{ch}-P0', f'{ch}-P1', f'{ch}-P2', f'{oc}-T1')
            left = {p: counts(pg, p) for p in line if counts(pg, p) != ZERO}
            c.check(left == {}, f'left under the line: {left}')
            c.check(all(nchunks(pg, w) == 0 for w in (W(ch, 'one'), W(ch, 'two'), W(ch, 'zero'), W(oc, 'tee'))),
                    f'chunks left {[nchunks(pg, w) for w in (W(ch, "one"), W(ch, "two"), W(ch, "zero"), W(oc, "tee"))]}')
            c.check(all(prow(pg, p) == {'retired': True, 'reason': 'app_delete'} for p in line), f'rows {[prow(pg, p) for p in line]}')
            c.check(booked(pg) == {W(ch, 'one'): 'replaced', W(ch, 'two'): 'bot_deleted', W(ch, 'zero'): 'bot_deleted', W(oc, 'tee'): 'bot_deleted'},
                    f'booked {booked(pg)}')
            again = delete_bot(pg, dbf_body(oc, f'{oc}-T1', W(oc, 'tee')))           # the T8 client's call with the retired ids
            c.check(again.get('own', {}).get('allowed') is True and again.get('ok') is True
                    and again.get('retire') == {'retiredProfiles': 0, 'bookedClones': 0} and again.get('chunks', {}).get('deletedChunks') == 0
                    and all(v == 0 for v in again.get('mem', {}).values()), f'second call {again.get("retire")} {again.get("chunks")} {again.get("mem")}')

    with Case(f'T5 L3 [{ch}] the slot re-used after the delete: an older client\'s Create in the same slot (no CreateOrigin) '
              f'adopts nothing of the deleted bot') as c:
        with PgSession(SETUP) as pg:
            born(pg, ch, f'{ch}-P1')
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'old'))} where profile_id = {L(ch + '-P1')};")
            chat_data(pg, f'{ch}-P1', tag='old')
            born(pg, ch, f'{ch}-P2')
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'new'))} where profile_id = {L(ch + '-P2')};")
            out = delete_bot(pg, dbf_body(ch, f'{ch}-P2', W(ch, 'new')))
            c.check(out.get('ok') is True, f'delete {out}')
            born(pg, ch, f'{ch}-P3')
            got = plan(pg, ch, f'{ch}-P3', origin='', prev='')
            c.check(got.get('pred_profiles') == [] and got.get('replace_ids') == [], f'the new bot of the slot adopts {got}')
            got = plan(pg, ch, f'{ch}-P3', origin='edit', prev=f'{ch}-P2')
            c.check(got.get('pred_profiles') == [] and got.get('replace_ids') == [], f'… even naming the deleted profile: {got}')

    with Case(f'T5 L4 [{ch}] negatives: another install\'s rows of the same bot key, another bot key, a key-less row outside '
              f'the chain, a clone a LIVE row outside the line drives, a protected id — untouched, never booked') as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, f'{ch}-cur', wfid=W(ch, 'cur'))
            chat_data(pg, f'{ch}-cur')
            chunks(pg, ch, W(ch, 'cur'), 'f-cur')
            row(pg, ch, f'{ch}-B', owner='inst-B', wfid=W(ch, 'B'), reason='trial_expiry')     # another install, same key
            row(pg, ch, f'{ch}-B2', owner='inst-B', wfid=W(ch, 'B2'), key=None, sup=f'{ch}-cur', reason='replaced')
            row(pg, ch, f'{ch}-k7', wfid=W(ch, 'k7'), key='Bot7', reason='trial_expiry')       # another bot of this install
            row(pg, ch, f'{ch}-nk', wfid=W(ch, 'nk'), key=None, reason='liveness')             # key-less, not in the chain
            row(pg, ch, f'{ch}-sh', wfid=W(ch, 'shared'), reason='replaced')                   # in the line ...
            row(pg, ch, f'{ch}-live', wfid=W(ch, 'shared'), key='Bot5')                        # ... but a LIVE row of Bot5 drives it
            row(pg, oc, f'{oc}-prot', wfid=CL.PROTECTED[0], reason='trial_expiry')             # a tampered binding in the line
            for p, w in ((f'{ch}-B', W(ch, 'B')), (f'{ch}-B2', W(ch, 'B2')), (f'{ch}-k7', W(ch, 'k7')), (f'{ch}-nk', W(ch, 'nk')),
                         (f'{ch}-live', W(ch, 'shared')), (f'{oc}-prot', CL.PROTECTED[0])):
                chat_data(pg, p)
                chunks(pg, ch if p != f'{oc}-prot' else oc, w, 'f-' + p)
            out = delete_bot(pg, dbf_body(ch, f'{ch}-cur', W(ch, 'cur')))
            c.check(out.get('ok') is True and counts(pg, f'{ch}-cur') == ZERO and nchunks(pg, W(ch, 'cur')) == 0, f'the bot itself: {out}')
            for p in (f'{ch}-B', f'{ch}-B2', f'{ch}-k7', f'{ch}-nk', f'{ch}-live'):
                c.check(all(v == 1 for v in counts(pg, p).values()), f'{p} lost data: {counts(pg, p)}')
            c.check(prow(pg, f'{ch}-B') == {'retired': True, 'reason': 'trial_expiry'} and prow(pg, f'{ch}-k7') == {'retired': True, 'reason': 'trial_expiry'}
                    and prow(pg, f'{ch}-nk') == {'retired': True, 'reason': 'liveness'} and prow(pg, f'{ch}-live') == {'retired': False, 'reason': None},
                    'rows outside the line keep their state')
            c.check(all(nchunks(pg, w) == 2 for w in (W(ch, 'B'), W(ch, 'B2'), W(ch, 'k7'), W(ch, 'nk'), W(ch, 'shared'), CL.PROTECTED[0])),
                    'chunks outside the line (or of a clone a live row outside it drives, or of a protected id) must stay')
            c.check(booked(pg) == {W(ch, 'cur'): 'bot_deleted'}, f'booked {booked(pg)}')
            c.check(prow(pg, f'{ch}-sh') == {'retired': True, 'reason': 'app_delete'} and counts(pg, f'{oc}-prot') == ZERO,
                    'the line rows themselves go (their shared / protected clone stays)')

    with Case(f'T5 L5 [{ch}] legacy (compat window, no token): the named ids only — an owner-less predecessor of the same key '
              f'keeps its data, nothing is booked, a retired named row keeps its reason') as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, f'{ch}-L0', owner=None, wfid=W(ch, 'L0'), reason='replaced')
            row(pg, ch, f'{ch}-L1', owner=None, wfid=W(ch, 'L1'))
            row(pg, oc, f'{oc}-L2', owner=None, wfid=W(oc, 'L2'), reason='trial_expiry')
            for p, w, k in ((f'{ch}-L0', W(ch, 'L0'), ch), (f'{ch}-L1', W(ch, 'L1'), ch), (f'{oc}-L2', W(oc, 'L2'), oc)):
                chat_data(pg, p)
                chunks(pg, k, w, 'f-' + p)
            body = dbf_body(ch, f'{ch}-L1', W(ch, 'L1'))
            body.update({'botTgId' if ch == 'WA' else 'botWaId': W(oc, 'L2'), 'tgProfileId' if ch == 'WA' else 'waProfileId': f'{oc}-L2'})
            out = delete_bot(pg, body, install=None, compat=True, storage=500)
            c.check(out['gate']['crAuth']['mode'] == 'legacy' and out.get('ok') is True, f'legacy delete {out.get("own")} {out.get("ok")}')
            c.check(counts(pg, f'{ch}-L1') == ZERO and counts(pg, f'{oc}-L2') == ZERO and nchunks(pg, W(ch, 'L1')) == 0,
                    'the named bot is deleted')
            c.check(all(v == 1 for v in counts(pg, f'{ch}-L0').values()) and nchunks(pg, W(ch, 'L0')) == 2, 'legacy: no line')
            c.check(booked(pg) == {} and prow(pg, f'{ch}-L1') == {'retired': True, 'reason': 'app_delete'}
                    and prow(pg, f'{oc}-L2') == {'retired': True, 'reason': 'trial_expiry'} and prow(pg, f'{ch}-L0')['reason'] == 'replaced',
                    f'legacy books nothing and keeps the old retire: {booked(pg)} {prow(pg, ch + "-L1")} {prow(pg, oc + "-L2")}')

with Case('T5 L6: the order and the honest answer stay — originals first (a storage failure stops before anything is deleted), '
          'every deleting node behind Owned?(0), success only on numeric counters (Retire also answers bookedClones)') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'WA', 'WA-P1', wfid=W('WA', 'old'), reason='replaced', sup='WA-P2')
        row(pg, 'WA', 'WA-P2', wfid=W('WA', 'new'))
        chat_data(pg, 'WA-P1')
        chunks(pg, 'WA', W('WA', 'old'), 'f-old')
        out = delete_bot(pg, dbf_body('WA', 'WA-P2', W('WA', 'new')), storage=500)
        c.check(out.get('settled', {}).get('originals', {}).get('error') == 'storage_unavailable' and 'retire' not in out
                and counts(pg, 'WA-P1') != ZERO and nchunks(pg, W('WA', 'old')) == 2 and booked(pg) == {},
                f'a storage failure must stop the line before anything: {out.get("settled")}')
    c.check(preds(DBF, 'Retire Bot Profiles') == [('Originals Deleted?', 0)] and edges(DBF, 'Retire Bot Profiles') == [E('Delete Bot Chunks')]
            and edges(DBF, 'Delete Bot Chunks') == [E('Delete Chat Memory')] and edges(DBF, 'Delete Chat Memory') == [E('Deletion Settled?')]
            and preds(DBF, 'List Bot Originals') == [('Owned?', 0)], 'Delete Bot Files order')
    counters = {'retiredProfiles': 1, 'bookedClones': 1}
    mem_ok = {k: 0 for k in ('deletedMemoryRows', 'deletedOutcomes', 'deletedReplyFlags', 'deletedHistoryEvents', 'deletedHandoffs',
                             'deletedReplyEvents', 'deletedChatStates')}
    c.check(cond(DBF, 'Deletion Settled?', mem_ok, {'Retire Bot Profiles': counters, 'Delete Bot Chunks': {'deletedChunks': 0}}) is True
            and cond(DBF, 'Deletion Settled?', mem_ok, {'Retire Bot Profiles': {'error': {'message': 'relation "clone_retirements" does not exist'}},
                                                         'Delete Bot Chunks': {'deletedChunks': 0}}) is False
            and cond(DBF, 'Deletion Settled?', {**mem_ok, 'deletedChatStates': None}, {'Retire Bot Profiles': counters,
                                                                                   'Delete Bot Chunks': {'deletedChunks': 0}}) is False,
            'Deletion Settled? must still require every counter as a number (a failed Retire — e.g. M2 not applied — is a 503)')
    for n in ('List Bot Originals', 'Retire Bot Profiles', 'Delete Bot Chunks', 'Delete Chat Memory'):
        nn = node(DBF, n)
        c.check(nn.get('onError') == 'continueRegularOutput' and nn.get('alwaysOutputData') is True
                and nn['parameters']['options']['queryReplacement'] == CL.DBF_LINE_PARAMS
                and CL.lineage_cte('$1', '$2', '$3', '$4') in nn['parameters']['query'].replace('public.', ''),
                f'{n} must read the bot\'s line (clone_lineage) with the line parameters')

with Case('T5 L7: the line\'s chunk statements read the RAG store through its expression indexes and delete by primary key — '
          'never a scan of the store (prod documents_metadata_bot_wa_idx / _tg_idx; 20 000 rows, planner left to choose)') as c:
    import re
    ddl = [l.replace('public.', '').strip() for l in (N8N / 'supabase' / '2026-07-02-harden-rag-store.sql').read_text().splitlines()
           if re.match(r'create index if not exists documents_metadata_bot_(wa|tg)_idx', l)]
    c.check(len(ddl) == 2, f'index DDL not found: {ddl}')
    with PgSession(SETUP) as pg:
        row(pg, 'WA', 'WA-cur', wfid='W7')
        row(pg, 'TG', 'TG-cur', wfid='T7')
        pg.x("insert into documents (content, metadata) select 'x', jsonb_build_object('botWaId', 'W' || (g % 500), 'botTgId', "
             "'T' || (g % 400), 'fileId', 'f' || g) from generate_series(1, 20000) g;\n" + '\n'.join(ddl) + '\nanalyze documents;')
        args = ['session', 'inst-A', json.dumps(['WA-cur', 'TG-cur']), json.dumps(['W7', 'T7'])]
        for n in ('List Bot Originals', 'Delete Bot Chunks'):
            plan_ = pg.x('explain ' + bind(Q(DBF, n), args) + ';')
            docs = [l for l in plan_.splitlines() if 'documents' in l]
            c.check(not any('Seq Scan on documents' in l for l in docs)
                    and sum(1 for l in docs if re.search(r'Index Scan (using|on) documents_metadata_bot_(wa|tg)_idx', l)) >= 2,
                    f'{n}: a scan of the store:\n' + '\n'.join(docs))
            if n == 'Delete Bot Chunks':
                c.check(any('Index Scan using documents_pkey' in l for l in docs), f'{n}: the delete must go by primary key:\n' + '\n'.join(docs))

# ------------------------------------------------------------------------------------------------ B: the bookkeeping end to end
for ch in CH:
    with Case(f'T5 B1 [{ch}] what the delete booked is what the sweep deletes, once — never the neighbour\'s clone') as c:
        with PgSession(SETUP) as pg:
            born(pg, ch, f'{ch}-P1')
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'old'))} where profile_id = {L(ch + '-P1')};")
            born(pg, ch, f'{ch}-P2')
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'new'))} where profile_id = {L(ch + '-P2')};")
            born(pg, ch, f'{ch}-N', key='Bot9')
            pg.x(f"update bot_profiles set workflow_id = {L(W(ch, 'nine'))} where profile_id = {L(ch + '-N')};")
            pg.x(f"insert into clone_retirements (workflow_id, reason) values ({L(W(ch, 'nine'))}, 'replaced');")   # tampered: a live row drives it
            out = delete_bot(pg, dbf_body(ch, f'{ch}-P2', W(ch, 'new')))
            c.check(out.get('ok') is True, f'delete {out}')
            c.check(sweep_picks(pg) == [], f'a fresh booking (an in-flight delete) is never taken: {sweep_picks(pg)}')
            age_bookings(pg)
            picks = sweep_picks(pg)
            c.check(sorted(picks) == sorted([W(ch, 'old'), W(ch, 'new')]), f'picked {picks}')
            for wid in picks:
                pg.q(bind(Q(SWEEP, 'Stamp Retirement Removed'), P(SWEEP, 'Stamp Retirement Removed', {'statusCode': 404},
                                                                  {'Clone Retirements': {'workflow_id': wid}})))
            c.check(sweep_picks(pg) == [], f'removed bookings are never taken again: {sweep_picks(pg)}')

# ------------------------------------------------------------------------------------------------ N: the guards
with Case('T5 N1: the verifiers reject copies that delete by the named ids only, bypass the dry-run gate or call a public URL') as c:
    head_dbf = json.loads(subprocess.run(['git', '-C', str(N8N.parents[1]), 'show', f'358ab7ec:Tools/n8n/workflows/{DBF_F}'],
                                         capture_output=True, text=True, check=True).stdout)

    def mutate_run(fname, fn, verifier):
        d = Path(tempfile.mkdtemp(prefix='t5-neg-'))
        try:
            for p in (N8N / 'workflows').glob('*.json'):
                shutil.copy(p, d / p.name)
            f = d / fname
            w = json.loads(f.read_text(encoding='utf-8'))
            fn(w)
            f.write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            r = subprocess.run([sys.executable, str(N8N / verifier), '--dir', str(d)], capture_output=True, text=True)
            return r.returncode, (r.stdout + r.stderr)[-500:]
        finally:
            shutil.rmtree(d, ignore_errors=True)

    def named_only(name):
        def fn(w):
            node(w, name)['parameters'] = json.loads(json.dumps(node(head_dbf, name)['parameters']))
        return fn

    def dry_bypass(w):
        w['connections']['Clone Retirements']['main'][0] = [E('Has Retired Clone?')]

    def public_url(w):
        node(w, 'Delete Retired Clone')['parameters']['url'] = '=https://n8n.choosereply.com/api/v1/workflows/{{ $json.workflow_id }}'

    def any_status_gone(w):
        node(w, 'Retired Clone Gone?')['parameters']['conditions']['conditions'][0]['leftValue'] = '={{ true }}'

    def unprotected(w):
        cnd = node(w, 'Has Retired Clone?')['parameters']['conditions']['conditions'][0]
        cnd['leftValue'] = "={{ typeof $json.workflow_id === 'string' && $json.workflow_id.length > 0 }}"

    def superseded_again(w):
        q = node(w, 'Retired Clones')['parameters']['query']
        node(w, 'Retired Clones')['parameters']['query'] = q.replace('\n  and bp.superseded_by is null', '')

    cases = [(DBF_F, named_only(n), 'verify-auth-gate.py', f'{n} by the named ids only')
             for n in ('List Bot Originals', 'Retire Bot Profiles', 'Delete Bot Chunks')]
    cases += [(DBF_F, named_only('Delete Chat Memory'), 'verify-history-handoff.py', 'Delete Chat Memory by the named ids only')]
    cases += [(SWEEP_F, fn, 'verify-clone-lifecycle.py', label) for fn, label in
              ((dry_bypass, 'DELETE without the dry-run gate'), (public_url, 'DELETE on the public URL'),
               (any_status_gone, 'any status stamped as gone'), (unprotected, 'no protected-id check before the DELETE'),
               (superseded_again, 'V12 Retired Clones without the superseded filter'))]
    for fname, fn, verifier, label in cases:
        rc, out = mutate_run(fname, fn, verifier)
        c.check(rc != 0 and 'T5' in out, f'{verifier}: {label} passed ({rc}): {out}')
    for verifier in ('verify-auth-gate.py', 'verify-history-handoff.py', 'verify-clone-lifecycle.py'):
        rc, out = mutate_run(DBF_F, lambda w: None, verifier)
        c.check(rc == 0, f'{verifier} must pass the unmodified canon: {out}')

report()
