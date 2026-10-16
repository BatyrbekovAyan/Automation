#!/usr/bin/env python3
"""V12 (iteration 2026-09-28, found 2026-10-06 on the owner's bot «23»): the owner of a bot the scheduled sweep retired
(trial or subscription over) could not delete it — the two DELETING webhooks took the token's LIVE profiles/workflows
as their ownership rule, the token is built from live rows only, so Delete Bot Files / Set Workflow State answered 403
and the client removed the bot locally while its memory, history and clone stayed on the server. The sweep never
touched the clone either: a retired bot's workflow stayed ACTIVE.

What is pinned here — real node bodies from the canonical JSONs, real SQL on session temp tables in the staging
Postgres, n8n's own expression engine; no n8n server, no network, never production:
  D1  a retired bot's owner deletes its files/memory: the gate passes, Delete Ownership allows, the deletes empty the
      bot's rows and leave a neighbour's untouched (WhatsApp + Telegram);
  D2  Set Workflow State: delete/deactivate of the owner's retired clone are decided by Delete Ownership; activate
      never goes there (token + live row only, as before);
  D3  Delete Ownership refuses another install, an owner-less row, a revoked install, a foreign workflow next to an
      own profile, all-sentinel and non-string ids (M3: the form the deleting nodes use) — and a DB fault is 503, never
      the final 403; an unauthenticated call never reaches it;
  M5  a STALE token whose profile another install has since claimed by code deletes nothing (Delete Bot Files, Set
      Workflow State and the proxy's profile.delete/logout all decide in the database);
  D4  the live path and the legacy compat window still delete;
  D5  Set Workflow State's delete is idempotent (404 = gone) and reports a real failure (502);
  M1  Delete Bot Files answers success only when every delete returned its counter; the stored originals are deleted
      BEFORE anything else and a storage failure is 503 (session) — the retry finds the chunks still there;
  S1  the sweep deactivates the clone of a profile IT retired (trial/churn), once (stamped), never a liveness/app_delete
      row, never a workflow a live row still drives, never a protected id;
  N1  verify-auth-gate.py fails on copies that bypass any of it.
"""
import copy
import json
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from harness import N8N, Case, PgSession, bind, code, expr, module, node, report, run_js, wf

DBF_F, SWS_F, PROXY_F = 'lmjYsdNcQA2IE5rl-Delete_Bot_Files.json', '0DP79STI3pafXO9B-Set_Workflow_State.json', 'crWappiProxy0001-Wappi_Proxy.json'
SWEEP_F = 'fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json'
SESSION = wf('crAuthSession001-Auth_Session.json')
DBF, SWS, PROXY, SWEEP = wf(DBF_F), wf(SWS_F), wf(PROXY_F), wf(SWEEP_F)
RA = '$RCAnonymousID:' + '0123456789abcdef' * 2
RB = '$RCAnonymousID:' + 'fedcba9876543210' * 2

LIKE = 'create temp table {0} (like public.{0} including all);\n'
SETUP = ''.join(LIKE.format(t) for t in ('installs', 'bot_profiles', 'billing_bindings', 'subscribers', 'documents', 'n8n_chat_histories',
                                         'chat_history_events', 'handoff_requests', 'reply_events', 'chat_reply_state',
                                         'conversation_outcomes', 'reply_mode_flags', 'uploaded_files', 'clone_retirements', 'suggestion_sets',
                                         'bot_schedule'))
CHANNELS = (('whatsapp', 'wa'), ('telegram', 'tg'))


def E(n, i=0):
    return {'node': n, 'type': 'main', 'index': i}


def edges(w, src, idx=0):
    outs = w['connections'].get(src, {}).get('main', [])
    return outs[idx] if idx < len(outs) else []


def preds(w, target):
    return sorted((src, i) for src, outs in w['connections'].items() for i, out in enumerate(outs.get('main', [])) for c in out if c['node'] == target)


def Q(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')   # the temp tables shadow the UNqualified names only


def P(w, name, json_=None, named=None):
    """The node's queryReplacement as n8n evaluates it (a template string is split on commas, like the node does)."""
    v = expr(node(w, name)['parameters']['options']['queryReplacement'], json_, named)
    return [x for x in v.split(',')] if isinstance(v, str) else v


def cond(w, name, json_=None, named=None):
    return expr(node(w, name)['parameters']['conditions']['conditions'][0]['leftValue'], json_, named)


def token(claims):
    return run_js("""const s = require('crypto');
const b64u = (b) => Buffer.from(b).toString('base64').replace(/\\+/g,'-').replace(/\\//g,'_').replace(/=+$/,'');
const p = b64u(JSON.stringify($input.first().json.claims));
return [{json: {t: 'v1.' + p + '.' + b64u(s.createHmac('sha256', 'synthetic-test-secret').update('v1.' + p).digest())}}];""",
                  {'json': {'claims': {'c': [], 'e': int(time.time()) + 3600, 'n': 1, **claims}}})[0]['json']['t']


def gate(w, tok, body, compat_open=False):
    headers = {'authorization': 'Bearer ' + tok} if tok else {}
    env = {'CR_AUTH_COMPAT_UNTIL': '2099-01-01'} if compat_open else {}
    return code(w, 'Auth Gate', {'json': {'headers': headers, 'body': body}}, env=env)[0]['json']


def boot(pg, install, rc):
    return pg.q(bind(Q(SESSION, 'Create Install'), [install, 'h-' + install, rc, 'ios', '1.0']))[0]


def claims_of(pg, install, rc):
    r = pg.q(bind(Q(SESSION, 'Verify Install'), [install, 'h-' + install, rc]))[0]
    return {'i': r['install_id'], 'u': r['app_user_id'], 'p': r['p'] or [], 'w': r['w'] or []}


def bot(pg, pid, channel, owner, wid, rc=RA, days=6):
    o = f"'{owner}'" if owner else 'null'
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id, created_at) values "
         f"('{pid}', '{rc}', '{channel}', {o}, '{wid}', now() - interval '{days} days');")


def seed(pg, pid, wid, key):
    pg.x(f"""insert into n8n_chat_histories (session_id, message) values ('{pid}:77001@c.us', '{{"type":"human","content":"hi"}}'::jsonb);
insert into documents (content, metadata) values ('price', '{{"{key}": "{wid}", "fileId": "f-{pid}"}}'::jsonb);
insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at) values ('{pid}', '77001@c.us', 'm-{pid}', 'client', 'hi', 'chat', now());
insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ('{pid}', '*', true, 5);
insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, last_history_id, last_message_at, outcome_at)
  values ('{pid}:77001@c.us', '{pid}', '77001@c.us', 'in_dialog', 1, now(), now());
insert into bot_schedule (profile_id, enabled, days_mask, start_min, end_min, tz, version) values ('{pid}', true, 31, 540, 1080, 'Asia/Almaty', 5);""")


def counts(pg, pid, wid, key):
    return pg.q(f"""select (select count(*) from n8n_chat_histories where split_part(session_id, ':', 1) = '{pid}')::int as mem,
  (select count(*) from documents where metadata->>'{key}' = '{wid}')::int as chunks,
  (select count(*) from chat_history_events where profile_id = '{pid}')::int as hist,
  (select count(*) from reply_mode_flags where profile_id = '{pid}')::int as flags,
  (select count(*) from conversation_outcomes where profile_id = '{pid}')::int as outcomes,
  (select count(*) from bot_schedule where profile_id = '{pid}')::int as schedule""")[0]


def retire_by_sweep(pg):
    """The sweep's REAL trial/churn statements: Candidates, then Mark Deleted per row and Demote Trialing."""
    cands = pg.q(Q(SWEEP, 'Candidates'))
    for r in cands:
        m = pg.q(bind(Q(SWEEP, 'Mark Deleted'), [r['profile_id'], r['reason']]))
        if m:
            pg.q(bind(Q(SWEEP, 'Demote Trialing'), [m[0]['app_user_id'], m[0]['profile_id'], m[0]['channel'], m[0]['deleted_reason']]))
    return cands


def ownership(pg, w, gate_item):
    return pg.q(bind(Q(w, 'Delete Ownership'), P(w, 'Delete Ownership', gate_item)))[0]


def dbf_body(wa_pid='-1', tg_pid='-1', wa_wid='-1', tg_wid='-1'):
    return {'botWaId': wa_wid, 'botTgId': tg_wid, 'waProfileId': wa_pid, 'tgProfileId': tg_pid, 'appUserId': RA}


def run_dbf_deletes(pg, gate_item, storage=(200,), rows=None):
    """Owned?(0) onwards: originals listing + split + storage results + settle, then the three deleting statements. The
    deleting nodes read the bot's line (T5) with parameters taken from the gate's item (`$('Auth Gate')`). `rows` (a
    dict), when given, receives the deleting statements' own rows: retire / chunks / mem."""
    webhook = {k: v for k, v in gate_item.items() if k != 'crAuth'}
    named = {'Webhook': webhook, 'Auth Gate': gate_item}
    listed = pg.q(bind(Q(DBF, 'List Bot Originals'), P(DBF, 'List Bot Originals', gate_item, named)))[0]
    split = [i['json'] for i in code(DBF, 'Split File Ids', {'json': {}}, named={'List Bot Originals': listed})]
    files = [s for s in split if cond(DBF, 'Has Original?', s) is True]
    results = [{'statusCode': storage[i % len(storage)], 'body': {}} for i in range(len(files))] or [s for s in split if s not in files]
    settled = code(DBF, 'Originals Settled', items=[{'json': r} for r in results], named={'Auth Gate': gate_item})[0]['json']
    if cond(DBF, 'Originals Deleted?', settled) is not True:
        return listed, split, settled, None
    retire = pg.q(bind(Q(DBF, 'Retire Bot Profiles'), P(DBF, 'Retire Bot Profiles', settled, named)))[0]
    chunks = pg.q(bind(Q(DBF, 'Delete Bot Chunks'), P(DBF, 'Delete Bot Chunks', retire, named)))[0]
    mem = pg.q(bind(Q(DBF, 'Delete Chat Memory'), P(DBF, 'Delete Chat Memory', chunks, named)))[0]
    if rows is not None:
        rows.update(retire=retire, chunks=chunks, mem=mem)
    ok = cond(DBF, 'Deletion Settled?', mem, {'Retire Bot Profiles': retire, 'Delete Bot Chunks': chunks})
    return listed, split, settled, ok


# ------------------------------------------------------------------------------------------------ D1 / D2 retired owner
for channel, short in CHANNELS:
    key = 'botWaId' if short == 'wa' else 'botTgId'
    with Case(f'V12 D1 ({channel}): the owner of a bot the sweep retired deletes its files and memory; a neighbour is untouched') as c:
        with PgSession(SETUP) as pg:
            boot(pg, 'inst-A', RA)
            pg.x(f"insert into subscribers (app_user_id, plan, status) values ('{RA}', 'trial', 'trialing');")
            pid, wid = f'{short}-A', f'wfClone{short}A0001'
            bot(pg, pid, channel, 'inst-A', wid)
            bot(pg, f'{short}-N', channel, 'inst-N', f'wfClone{short}N0001', rc=RB, days=1)
            seed(pg, pid, wid, key)
            seed(pg, f'{short}-N', f'wfClone{short}N0001', key)
            live = claims_of(pg, 'inst-A', RA)
            cands = retire_by_sweep(pg)
            after = claims_of(pg, 'inst-A', RA)
            c.check(pid in live['p'] and any(r['profile_id'] == pid and r['reason'] == 'trial_expiry' for r in cands)
                    and after['p'] == [] and after['w'] == [], f'premise: live {live} candidates {cands} after {after}')
            body = dbf_body(**({'wa_pid': pid, 'wa_wid': wid} if short == 'wa' else {'tg_pid': pid, 'tg_wid': wid}))
            g = gate(DBF, token(after), body)
            c.check(g['crAuth']['ok'] is True, f'the gate must not decide ownership from the live token: {g["crAuth"]}')
            c.check(edges(DBF, 'Authorized?') == [E('Delete Ownership')], f'Authorized?(0) {edges(DBF, "Authorized?")}')
            own = ownership(pg, DBF, g)
            c.check(own.get('allowed') is True and cond(DBF, 'Owned?', own) is True, f'Delete Ownership {own}')
            c.check(edges(DBF, 'Owned?') == [E('List Bot Originals')] and edges(DBF, 'Owned?', 1) == [E('Respond Owner Refused')],
                    f'Owned? {edges(DBF, "Owned?")} / {edges(DBF, "Owned?", 1)}')
            rows = {}
            listed, split, settled, ok = run_dbf_deletes(pg, g, rows=rows)
            c.check(listed.get('fileIds') == [f'f-{pid}'] and settled['originals']['ok'] is True and settled.get('body') == body,
                    f'originals: listed {listed} split {split} settled {settled.get("originals")}')
            c.check(ok is True, 'Deletion Settled? must pass on numeric counters')
            mine, theirs = counts(pg, pid, wid, key), counts(pg, f'{short}-N', f'wfClone{short}N0001', key)
            c.check(all(v == 0 for v in mine.values()), f'the bot\'s rows must be gone: {mine}')
            c.check(all(v == 1 for v in theirs.values()), f'the neighbour\'s rows must stay: {theirs}')
            # B1: the answer counts what the statement deleted — a statement that deletes the schedule but counts another
            # CTE (or none) under deletedSchedules must fail here, not only in the row counts above
            c.check((rows.get('mem') or {}).get('deletedSchedules') == 1,
                    f'Delete Chat Memory must count the one deleted schedule as deletedSchedules: {rows.get("mem")}')

with Case('V12 D2: Set Workflow State — delete/deactivate of the owner\'s retired clone go to Delete Ownership and pass; activate never does') as c:
    with PgSession(SETUP) as pg:
        boot(pg, 'inst-A', RA)
        bot(pg, 'wa-A', 'whatsapp', 'inst-A', 'wfCloneWaA0001')
        pg.x("update bot_profiles set deleted_at = now(), deleted_reason = 'trial_expiry';")
        tok = token(claims_of(pg, 'inst-A', RA))
        for action in ('delete', 'deactivate'):
            g = gate(SWS, tok, {'workflowId': 'wfCloneWaA0001', 'action': action})
            c.check(g['crAuth']['ok'] is True and cond(SWS, 'Ownership Route?', g) is True, f'{action}: {g["crAuth"]}')
            own = ownership(pg, SWS, g)
            c.check(own.get('allowed') is True, f'{action}: {own}')
            item = code(SWS, 'Owner Item', {'json': own}, named={'Auth Gate': g})[0]['json']
            v = run_js(node(SWS, 'Validate')['parameters']['jsCode'].replace('$workflow.id', "'0DP79STI3pafXO9B'"), {'json': item})[0]['json']
            c.check(item == g and v == {'ok': True, 'id': 'wfCloneWaA0001', 'action': action}, f'{action}: item/validate {v}')
        g = gate(SWS, tok, {'workflowId': 'wfCloneWaA0001', 'action': 'activate'})
        c.check(cond(SWS, 'Ownership Route?', g) is False and cond(SWS, 'Token Owns?', g) is False,
                'activate of a retired clone must stay on the token rule (live rows only) and be refused')
        resp = node(SWS, 'Respond Forbidden')['parameters']
        c.check(resp['options'].get('responseCode') == 403 and 'forbidden:workflowId' in resp['responseBody'], f'Respond Forbidden {resp}')
        c.check(edges(SWS, 'Authorized?') == [E('Ownership Route?')] and edges(SWS, 'Ownership Route?') == [E('Delete Ownership')]
                and edges(SWS, 'Ownership Route?', 1) == [E('Token Owns?')] and edges(SWS, 'Owned?') == [E('Owner Item')]
                and edges(SWS, 'Owner Item') == [E('Validate')] and edges(SWS, 'Token Owns?') == [E('Validate')]
                and edges(SWS, 'Token Owns?', 1) == [E('Respond Forbidden')], 'Set Workflow State routing')
        live_tok = token({'i': 'inst-A', 'u': RA, 'p': ['wa-L'], 'w': ['wfLiveWaL0001']})
        c.check(cond(SWS, 'Token Owns?', gate(SWS, live_tok, {'workflowId': 'wfLiveWaL0001', 'action': 'activate'})) is True,
                'activate of a live clone the token lists must still pass')

# ------------------------------------------------------------------------------------------------ D3 / M3 / M5 refusals
with Case('V12 D3: Delete Ownership refuses what the caller cannot prove it owns; reasons say why') as c:
    with PgSession(SETUP) as pg:
        for i in ('inst-A', 'inst-B', 'inst-R'):
            pg.x(f"insert into installs (install_id, secret_hash) values ('{i}', 'h');")
        pg.x("update installs set revoked_at = now() where install_id = 'inst-R';")
        bot(pg, 'wa-A', 'whatsapp', 'inst-A', 'wfCloneWaA0001')
        bot(pg, 'tg-A', 'telegram', 'inst-A', 'wfCloneTgA0001')
        bot(pg, 'wa-B', 'whatsapp', 'inst-B', 'wfCloneWaB0001', rc=RB)
        bot(pg, 'wa-L', 'whatsapp', None, 'wfLegacyWaL001')
        bot(pg, 'wa-R', 'whatsapp', 'inst-R', 'wfCloneWaR0001')
        pg.x("update bot_profiles set deleted_at = now(), deleted_reason = 'trial_expiry' where profile_id in ('wa-A', 'tg-A');")

        def dbf(install, body, compat=False):
            g = gate(DBF, token({'i': install, 'u': None, 'p': [], 'w': []}) if not compat else None, body, compat_open=compat)
            return ownership(pg, DBF, g)

        cases = {
            'own retired WA + TG': (dbf('inst-A', dbf_body('wa-A', 'tg-A', 'wfCloneWaA0001', 'wfCloneTgA0001')), True, None),
            'another install\'s profile': (dbf('inst-A', dbf_body('wa-B')), False, 'owned_elsewhere'),
            'another install\'s workflow next to an own profile': (dbf('inst-A', dbf_body('wa-A', '-1', 'wfCloneWaB0001')), False, 'owned_elsewhere'),
            'an owner-less (never claimed) profile': (dbf('inst-A', dbf_body('wa-L', '-1', 'wfLegacyWaL001')), False, 'unproven'),
            'an unknown workflow id': (dbf('inst-A', dbf_body('wa-A', '-1', 'wfNobody00001')), False, 'unproven'),
            'a revoked install': (dbf('inst-R', dbf_body('wa-R')), False, 'unproven'),
            'all sentinels': (dbf('inst-A', dbf_body()), False, 'unproven'),
            'M3: the string null as a workflow id': (dbf('inst-A', {**dbf_body('wa-A'), 'botWaId': 'null'}), False, 'unproven'),
            'M3: a number as a workflow id': (dbf('inst-A', {**dbf_body('wa-A'), 'botWaId': 0}), False, 'unproven'),
            'M3: a missing field': (dbf('inst-A', {k: v for k, v in dbf_body('wa-A').items() if k != 'botTgId'}), False, 'unproven'),
            'legacy (compat window) keeps the old behaviour': (dbf(None, dbf_body('wa-B'), compat=True), True, None),
        }
        for label, (got, allowed, reason) in cases.items():
            c.check(got.get('allowed') is allowed and got.get('reason') == reason, f'{label}: {got}')
        rr = node(DBF, 'Respond Owner Refused')['parameters']
        code_403 = expr(rr['options']['responseCode'], {'allowed': False, 'reason': 'unproven'})
        code_503 = expr(rr['options']['responseCode'], {'error': {'message': 'Connection terminated'}})
        body_503 = expr(rr['responseBody'], {'error': {'message': 'Connection terminated'}})
        c.check(code_403 == 403 and code_503 == 503 and body_503.get('error') == 'db_unavailable',
                f'a DB fault must be 503 (retry), never the final 403: {code_403} {code_503} {body_503}')
        bad = gate(DBF, 'v1.e30.forged', dbf_body('wa-A'))
        c.check(bad['crAuth']['ok'] is False and edges(DBF, 'Authorized?', 1) == [E('Respond 401')]
                and expr(node(DBF, 'Respond 401')['parameters']['options']['responseCode'], bad) == 401,
                f'a forged token stays a 401 at the gate: {bad["crAuth"]}')

with Case('V12 M5: a STALE token whose profile another install has since claimed deletes nothing (files, workflow, Wappi profile)') as c:
    with PgSession(SETUP) as pg:
        for i in ('inst-A', 'inst-B'):
            pg.x(f"insert into installs (install_id, secret_hash) values ('{i}', 'h');")
        bot(pg, 'wa-M', 'whatsapp', 'inst-B', 'wfCloneWaM0001')       # claimed by code from inst-A's phone
        stale = token({'i': 'inst-A', 'u': RA, 'p': ['wa-M'], 'w': ['wfCloneWaM0001']})
        d = ownership(pg, DBF, gate(DBF, stale, dbf_body('wa-M', '-1', 'wfCloneWaM0001')))
        s = ownership(pg, SWS, gate(SWS, stale, {'workflowId': 'wfCloneWaM0001', 'action': 'delete'}))
        br = code(PROXY, 'Build Request', {'json': {**gate(PROXY, stale, {}), 'body': {'op': 'profile.delete', 'channel': 'whatsapp', 'profile_id': 'wa-M'}}})[0]['json']
        po = pg.q(bind(Q(PROXY, 'Profile Owner'), P(PROXY, 'Profile Owner', br, {'Auth Gate': gate(PROXY, stale, {})})))[0]
        c.check(d.get('allowed') is False and d.get('reason') == 'owned_elsewhere', f'Delete Bot Files {d}')
        c.check(s.get('allowed') is False, f'Set Workflow State {s}')
        c.check(br.get('ok') is True and br.get('ownerCheck') is True and cond(PROXY, 'Owner Check?', br) is True
                and po.get('allowed') is False, f'proxy profile.delete: build {br} owner {po}')

with Case('V12 proxy: profile.delete / profile.logout are decided by the database (own live or retired row), other ops are not') as c:
    with PgSession(SETUP) as pg:
        for i in ('inst-A', 'inst-R'):
            pg.x(f"insert into installs (install_id, secret_hash) values ('{i}', 'h');")
        pg.x("update installs set revoked_at = now() where install_id = 'inst-R';")
        bot(pg, 'wa-A', 'whatsapp', 'inst-A', 'wfCloneWaA0001')
        bot(pg, 'tg-A', 'telegram', 'inst-A', 'wfCloneTgA0001')
        bot(pg, 'wa-R', 'whatsapp', 'inst-R', 'wfCloneWaR0001')
        pg.x("update bot_profiles set deleted_at = now(), deleted_reason = 'trial_expiry' where profile_id = 'tg-A';")

        def owner(install, op, pid, channel='whatsapp'):
            g = gate(PROXY, token({'i': install, 'u': None, 'p': [pid], 'w': []}), {})
            br = code(PROXY, 'Build Request', {'json': {**g, 'body': {'op': op, 'channel': channel, 'profile_id': pid}}})[0]['json']
            return br, (pg.q(bind(Q(PROXY, 'Profile Owner'), P(PROXY, 'Profile Owner', br, {'Auth Gate': g})))[0] if br.get('ownerCheck') else None)

        for op in ('profile.delete', 'profile.logout'):
            br, own = owner('inst-A', op, 'wa-A')
            c.check(br.get('ownerCheck') is True and own.get('allowed') is True, f'{op} own live: {br} {own}')
            br, own = owner('inst-A', op, 'tg-A', 'telegram')
            c.check(own.get('allowed') is True, f'{op} own retired (Telegram): {own}')
            br, own = owner('inst-R', op, 'wa-R')
            c.check(own.get('allowed') is False, f'{op} revoked install: {own}')
        br, own = owner('inst-A', 'messages.get', 'wa-A')
        c.check(br.get('ok') is False or br.get('ownerCheck') is False, f'a read op must not pay the DB check: {br}')
        br, _ = owner('inst-A', 'chats.filter', 'wa-A')
        c.check(br.get('ownerCheck') is False and cond(PROXY, 'Owner Check?', br) is False, f'chats.filter: {br}')
        c.check(edges(PROXY, 'Op valid?') == [E('Owner Check?')] and edges(PROXY, 'Owner Check?') == [E('Profile Owner')]
                and edges(PROXY, 'Owner Check?', 1) == [E('Claim Pairing?')] and edges(PROXY, 'Profile Owner') == [E('Profile Owned?')]
                and edges(PROXY, 'Profile Owned?') == [E('Wappi Call')] and edges(PROXY, 'Profile Owned?', 1) == [E('Respond Profile Refused')],
                'proxy routing')
        rr = node(PROXY, 'Respond Profile Refused')['parameters']
        c.check(expr(rr['options']['responseCode'], {'allowed': False}) == 403 and expr(rr['options']['responseCode'], {'error': {'message': 'x'}}) == 503,
                f'proxy refusal codes {rr}')

# ------------------------------------------------------------------------------------------------ D4 live + legacy
for channel, short in CHANNELS:
    key = 'botWaId' if short == 'wa' else 'botTgId'
    with Case(f'V12 D4 ({channel}): a live bot (session) and a token-less legacy call in the compat window still delete') as c:
        with PgSession(SETUP) as pg:
            pg.x("insert into installs (install_id, secret_hash) values ('inst-A', 'h');")
            pid, wid = f'{short}-A', f'wfClone{short}A0001'
            bot(pg, pid, channel, 'inst-A', wid, days=1)
            seed(pg, pid, wid, key)
            body = dbf_body(**({'wa_pid': pid, 'wa_wid': wid} if short == 'wa' else {'tg_pid': pid, 'tg_wid': wid}))
            g = gate(DBF, token({'i': 'inst-A', 'u': RA, 'p': [pid], 'w': [wid]}), body)
            own = ownership(pg, DBF, g)
            _, _, settled, ok = run_dbf_deletes(pg, g)
            c.check(own.get('allowed') is True and ok is True and all(v == 0 for v in counts(pg, pid, wid, key).values()),
                    f'live: {own} ok={ok} left={counts(pg, pid, wid, key)}')
            pid2, wid2 = f'{short}-G', f'wfClone{short}G0001'
            bot(pg, pid2, channel, None, wid2, days=1)
            seed(pg, pid2, wid2, key)
            body2 = dbf_body(**({'wa_pid': pid2, 'wa_wid': wid2} if short == 'wa' else {'tg_pid': pid2, 'tg_wid': wid2}))
            g2 = gate(DBF, None, body2, compat_open=True)
            own2 = ownership(pg, DBF, g2)
            _, _, _, ok2 = run_dbf_deletes(pg, g2, storage=(500,))
            c.check(g2['crAuth']['mode'] == 'legacy' and own2.get('allowed') is True and ok2 is True
                    and all(v == 0 for v in counts(pg, pid2, wid2, key).values()),
                    f'legacy: {g2["crAuth"]} {own2} ok={ok2} (a storage failure never blocks an old client that cannot retry)')

# ------------------------------------------------------------------------------------------------ M1 honest answer
with Case('V12 M1: Delete Bot Files says success only on real counters; storage first, its failure is 503 and nothing else ran') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into installs (install_id, secret_hash) values ('inst-A', 'h');")
        bot(pg, 'wa-A', 'whatsapp', 'inst-A', 'wfCloneWaA0001', days=1)
        seed(pg, 'wa-A', 'wfCloneWaA0001', 'botWaId')
        g = gate(DBF, token({'i': 'inst-A', 'u': RA, 'p': ['wa-A'], 'w': ['wfCloneWaA0001']}), dbf_body('wa-A', '-1', 'wfCloneWaA0001'))
        _, _, settled, ok = run_dbf_deletes(pg, g, storage=(500,))
        left = counts(pg, 'wa-A', 'wfCloneWaA0001', 'botWaId')
        c.check(settled['originals'] == {'ok': False, 'error': 'storage_unavailable', 'deleted': 0} and ok is None and all(v == 1 for v in left.values()),
                f'a storage failure must stop BEFORE the chunks (the retry needs them to find the file): {settled["originals"]} left={left}')
        rdf = node(DBF, 'Respond Delete Failed')['parameters']
        c.check(rdf['options'].get('responseCode') == 503 and expr(rdf['responseBody'], settled).get('error') == 'storage_unavailable'
                and expr(rdf['responseBody'], {'error': {'message': 'x'}}).get('error') == 'db_unavailable', f'Respond Delete Failed {rdf}')
    for statuses, want in (((200, 404), True), ((204,), True), ((500,), False), ((401,), False)):
        out = code(DBF, 'Originals Settled', items=[{'json': {'statusCode': s, 'body': {}}} for s in statuses], named={'Auth Gate': {'body': {}, 'crAuth': {'mode': 'session'}}})[0]['json']
        c.check(out['originals']['ok'] is want, f'storage statuses {statuses}: {out["originals"]}')
    err = code(DBF, 'Originals Settled', items=[{'json': {'error': {'message': 'ETIMEDOUT'}}}], named={'Auth Gate': {'body': {}, 'crAuth': {'mode': 'session'}}})[0]['json']
    nf = code(DBF, 'Originals Settled', items=[{'json': {'statusCode': 400, 'body': {'statusCode': '404', 'error': 'not_found'}}}], named={'Auth Gate': {'body': {}, 'crAuth': {'mode': 'session'}}})[0]['json']
    le = code(DBF, 'Split File Ids', {'json': {}}, named={'List Bot Originals': {'error': {'message': 'x'}}})
    c.check(err['originals']['ok'] is False and nf['originals']['ok'] is True and le == [{'json': {'listError': True}}],
            f'no response = failure, «object not found» = gone, a failed listing is marked: {err["originals"]} {nf["originals"]} {le}')
    bad_mem = {'error': {'message': 'Connection terminated'}}
    c.check(cond(DBF, 'Deletion Settled?', bad_mem, {'Retire Bot Profiles': {'retiredProfiles': 1}, 'Delete Bot Chunks': {'deletedChunks': 0, 'fileIds': []}}) is False
            and cond(DBF, 'Deletion Settled?', {**{k: 0 for k in ('deletedMemoryRows', 'deletedOutcomes', 'deletedReplyFlags', 'deletedHistoryEvents',
                                                                 'deletedHandoffs', 'deletedReplyEvents', 'deletedChatStates', 'deletedSuggestionSets',
                                                                 'deletedSchedules')}},
                     {'Retire Bot Profiles': {'error': {'message': 'x'}}, 'Delete Bot Chunks': {'deletedChunks': 0}}) is False,
            'an error item from Delete Chat Memory or Retire must never become success:true')
    for n in ('Delete Bot Chunks', 'Delete Chat Memory', 'Retire Bot Profiles', 'Delete Ownership', 'List Bot Originals'):
        nn = node(DBF, n)
        c.check(nn.get('onError') == 'continueRegularOutput' and nn.get('alwaysOutputData') is True, f'{n}: an error must reach the verdict as an item')
    dso = node(DBF, 'Delete Stored Original')
    resp = (dso['parameters'].get('options') or {}).get('response', {}).get('response', {})
    c.check(resp.get('fullResponse') is True and resp.get('neverError') is True, f'Delete Stored Original must report its status: {resp}')
    c.check(preds(DBF, 'Respond') == [('Deletion Settled?', 0)] and preds(DBF, 'Retire Bot Profiles') == [('Originals Deleted?', 0)]
            and preds(DBF, 'Delete Stored Original') == [('Has Original?', 0)] and preds(DBF, 'List Bot Originals') == [('Owned?', 0)]
            and edges(DBF, 'Respond') == [], f'Delete Bot Files order: Respond<-{preds(DBF, "Respond")} Retire<-{preds(DBF, "Retire Bot Profiles")}')

# ------------------------------------------------------------------------------------------------ D5 idempotent delete
with Case('V12 D5: Set Workflow State delete is idempotent (200/404 = gone) and reports a real failure (502)') as c:
    dw = node(SWS, 'Delete Workflow')
    resp = (dw['parameters'].get('options') or {}).get('response', {}).get('response', {})
    c.check(resp.get('fullResponse') is True and resp.get('neverError') is True and dw.get('onError') == 'continueRegularOutput', f'Delete Workflow {dw}')
    verdicts = {s: cond(SWS, 'Workflow Gone?', {'statusCode': s}) for s in (200, 404, 500, 401)}
    c.check(verdicts == {200: True, 404: True, 500: False, 401: False} and cond(SWS, 'Workflow Gone?', {'error': {'message': 'x'}}) is False, f'{verdicts}')
    c.check(edges(SWS, 'Delete Workflow') == [E('Workflow Gone?')] and edges(SWS, 'Workflow Gone?') == [E('Respond OK')]
            and edges(SWS, 'Workflow Gone?', 1) == [E('Respond Delete Failed')]
            and node(SWS, 'Respond Delete Failed')['parameters']['options'].get('responseCode') == 502, 'Workflow Gone? routing')

# ------------------------------------------------------------------------------------------------ S1 sweep
CLONE_SETUP = LIKE.format('bot_profiles')
with Case('V12 S1: the sweep deactivates the clone of a profile IT retired, once; never liveness/app_delete, a live workflow or a protected id') as c:
    with PgSession(CLONE_SETUP) as pg:
        rows = [('p1', 'trial_expiry', 'wfCloneOne0001', None), ('p2', 'churn_grace', 'wfCloneTwo0002', None),
                ('p3', 'liveness', 'wfCloneThr0003', None), ('p4', 'app_delete', 'wfCloneFou0004', None),
                ('p5', 'trial_expiry', 'wfCloneFiv0005', None), ('p6', 'trial_expiry', 'wfCloneSix0006', 'wfCloneSix0006'),
                ('p7', 'trial_expiry', 'wfCloneSev0007', 'wfOlderClone07'), ('p8', 'trial_expiry', '4wYitz5ek30SVNlT', None),
                ('p9', 'trial_expiry', None, None), ('p10', 'channel_limit', 'wfCloneTen0010', None)]
        for pid, reason, wid, stamped in rows:
            pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id, deleted_at, deleted_reason, clone_deactivated_id) "
                 f"values ('{pid}', 'x', 'whatsapp', 'inst-A', {('%s' % repr(wid)) if wid else 'null'}, now(), '{reason}', {repr(stamped) if stamped else 'null'});")
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id) values ('p5-live', 'x', 'whatsapp', 'inst-A', 'wfCloneFiv0005');")
        picked = sorted(r['profile_id'] for r in pg.q(Q(SWEEP, 'Retired Clones')))
        c.check(picked == ['p1', 'p2', 'p7'], f'Retired Clones picked {picked}')
        clone = {'profile_id': 'p1', 'workflow_id': 'wfCloneOne0001'}
        c.check(cond(SWEEP, 'Has Clone?', clone) is True and all(cond(SWEEP, 'Has Clone?', {'workflow_id': w}) is False
                                                                  for w in ('4wYitz5ek30SVNlT', 'crAuthSession001', '-1', '', 'wf/../x', None)),
                'Has Clone? must accept only a clone-shaped, unprotected id')
        dc = node(SWEEP, 'Deactivate Clone')
        url = expr(dc['parameters']['url'], clone)
        resp = (dc['parameters'].get('options') or {}).get('response', {}).get('response', {})
        c.check(url == 'http://localhost:5678/api/v1/workflows/wfCloneOne0001/deactivate' and dc['parameters']['method'] == 'POST'
                and dc['credentials']['httpHeaderAuth']['name'] == 'n8n Admin API' and resp.get('neverError') is True
                and resp.get('fullResponse') is True and dc.get('onError') == 'continueRegularOutput', f'Deactivate Clone {url} {dc}')
        c.check({s: cond(SWEEP, 'Clone Off?', {'statusCode': s}) for s in (200, 404, 500)} == {200: True, 404: True, 500: False}, 'Clone Off?')
        pg.q(bind(Q(SWEEP, 'Stamp Clone Deactivated'), P(SWEEP, 'Stamp Clone Deactivated', {'statusCode': 200}, {'Retired Clones': clone})))
        again = sorted(r['profile_id'] for r in pg.q(Q(SWEEP, 'Retired Clones')))
        c.check(again == ['p2', 'p7'], f'a deactivated clone is stamped and not picked again: {again}')
    c.check(preds(SWEEP, 'Retired Clones') == [('Sweep Config', 0)] and preds(SWEEP, 'Has Clone?') == [('Clone Dry Run?', 1)]
            and preds(SWEEP, 'Deactivate Clone') == [('Has Clone?', 0)] and preds(SWEEP, 'Stamp Clone Deactivated') == [('Clone Off?', 0)]
            and preds(SWEEP, 'Clone Dry Run?') == [('Retired Clones', 0)], 'sweep clone branch wiring')
    liveness = {'Alive Registry', 'Compute Liveness Diff', 'Is Retire Candidate?', 'Liveness Dry Run?', 'Mark Liveness Deleted', 'Stamp Retired'}
    c.check(not any(src in liveness for src, _ in preds(SWEEP, 'Deactivate Clone') + preds(SWEEP, 'Retired Clones')), 'the liveness branch must never feed a deactivation')
    c.check(node(SWEEP, 'Retired Clones')['position'][1] > max(node(SWEEP, n)['position'][1] for n in ('Candidates', 'List WA Profiles', 'Mark Liveness Deleted')),
            'the clone branch must sit BELOW the trial/churn and liveness rows (n8n v1 runs sibling branches top-down: rows retired this run are picked this run)')

# ------------------------------------------------------------------------------------------------ deploy: the admin credential
with Case('V12 deploy: deploy-canonical binds the n8n Admin API credential from the live Set Workflow State, and refuses without it') as c:
    dc_mod = module('deploy-canonical.py')
    live_sws = {'nodes': [{'name': 'Delete Workflow', 'credentials': {'httpHeaderAuth': {'id': 'liveCred1234', 'name': 'n8n Admin API'}}}]}
    calls = []
    dc_mod.uc.call = lambda base, method, path, body=None: (calls.append(path) or (200, copy.deepcopy(live_sws)))
    cred = dc_mod.admin_credential_id('http://127.0.0.1:9')
    body = dc_mod.body_from(SWEEP)
    bound = dc_mod.bind_admin_credential(body, cred)
    c.check(cred == 'liveCred1234' and '__HDR_CRED__' not in json.dumps(bound)
            and node(bound, 'Deactivate Clone')['credentials']['httpHeaderAuth']['id'] == 'liveCred1234', f'cred={cred} calls={calls}')
    c.check('__HDR_CRED__' in json.dumps(body), 'binding must not mutate the canonical body it was given')
    dc_mod.uc.call = lambda base, method, path, body=None: (200, {'nodes': []})
    c.check(dc_mod.admin_credential_id('http://127.0.0.1:9') is None, 'no live credential -> None (the workflow is refused, not PUT unbound)')

# ------------------------------------------------------------------------------------------------ N1 negative controls
with Case('V12 N1: verify-auth-gate.py rejects copies that route around the database ownership') as c:
    def mutate_run(fname, fn):
        d = Path(tempfile.mkdtemp(prefix='v12-neg-'))
        try:
            for p in (N8N / 'workflows').glob('*.json'):
                shutil.copy(p, d / p.name)
            f = d / fname
            w = json.loads(f.read_text(encoding='utf-8'))
            fn(w)
            f.write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            r = subprocess.run([sys.executable, str(N8N / 'verify-auth-gate.py'), '--dir', str(d)], capture_output=True, text=True)
            return r.returncode, r.stdout[-400:]
        finally:
            shutil.rmtree(d, ignore_errors=True)

    def activate_too(w):
        cnd = node(w, 'Ownership Route?')['parameters']['conditions']['conditions'][0]
        cnd['leftValue'] = cnd['leftValue'].replace("['deactivate', 'delete']", "['activate', 'deactivate', 'delete']")

    def dbf_skip(w):
        w['connections']['Authorized?']['main'][0] = [E('List Bot Originals')]

    def owned_always(w):
        node(w, 'Owned?')['parameters']['conditions']['conditions'][0]['leftValue'] = '={{ true }}'

    def proxy_skip(w):
        w['connections']['Owner Check?']['main'][0] = [E('Wappi Call')]

    def sws_skip(w):
        w['connections']['Owned?']['main'][0] = [E('Validate')]

    for fname, fn, label in ((SWS_F, activate_too, 'activate routed to Delete Ownership'), (DBF_F, dbf_skip, 'Delete Bot Files without Delete Ownership'),
                             (DBF_F, owned_always, 'Owned? always true'), (PROXY_F, proxy_skip, 'profile.delete past the DB check'),
                             (SWS_F, sws_skip, 'Owned? straight into Validate (no Owner Item)')):
        rc, out = mutate_run(fname, fn)
        c.check(rc != 0 and 'V12' in out, f'{label}: verifier passed ({rc}): {out}')

with Case('B1 N2: a delete that keeps the schedule is caught — verify-history-handoff.py (Delete Chat Memory without the '
          'bot_schedule CTE, Respond without deletedSchedules) and verify-auth-gate.py (Deletion Settled? without it)') as c:
    def run_on(tool, fname, fn):
        d = Path(tempfile.mkdtemp(prefix='b1-del-neg-'))
        try:
            for p in (N8N / 'workflows').glob('*.json'):
                shutil.copy(p, d / p.name)
            f = d / fname
            w = json.loads(f.read_text(encoding='utf-8'))
            before = json.dumps(w, sort_keys=True)
            fn(w)
            assert json.dumps(w, sort_keys=True) != before, 'the mutation changed nothing'
            f.write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            r = subprocess.run([sys.executable, str(N8N / tool), '--dir', str(d)], capture_output=True, text=True)
            return r.returncode, (r.stdout + r.stderr)[-500:]
        finally:
            shutil.rmtree(d, ignore_errors=True)

    def dcm_keeps_schedule(w):
        p = node(w, 'Delete Chat Memory')['parameters']
        p['query'] = p['query'].replace('DELETE FROM public.bot_schedule', 'SELECT 1 FROM public.bot_schedule', 1)

    def respond_without_counter(w):
        p = node(w, 'Respond')['parameters']
        p['responseBody'] = p['responseBody'].replace(', "deletedSchedules": $json.deletedSchedules ?? null', '')

    def verdict_without_counter(w):
        cnd = node(w, 'Deletion Settled?')['parameters']['conditions']['conditions'][0]
        cnd['leftValue'] = cnd['leftValue'].replace(" && typeof $json.deletedSchedules === 'number'", '')

    for tool, fn, needle in (('verify-history-handoff.py', dcm_keeps_schedule, 'bot_schedule'),
                             ('verify-history-handoff.py', respond_without_counter, 'deletedSchedules'),
                             ('verify-auth-gate.py', verdict_without_counter, 'Deletion Settled?')):
        rc, out = run_on(tool, DBF_F, fn)
        c.check(rc != 0 and needle in out, f'{tool} / {fn.__name__}: exit {rc}: {out}')

report()
