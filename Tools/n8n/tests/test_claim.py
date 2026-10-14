#!/usr/bin/env python3
"""R1 (iteration 2026-09-27) + V2 (iteration 2026-09-28): recovering a bot by claim must never hand a profile to an
install that did not prove control of THAT channel itself.

V2: Wappi never says WHICH link session completed a pairing, so a pairing (even one the claimant started through the
proxy) is a precondition — it gives the claimant the bot's chat — but never a proof. Every profile, owner-less ones
included, is bound ONLY by the code sent into the bot's own chat (Verify Claim Code). That code is therefore guarded:
a code counts as sent only when the transport accepted it (a lost one is cleared, never "reused"), at most five wrong
answers per code (then it is dead until its 10-minute window ends), claim.verify never calls Wappi, and only
claim.start moves the pending claim (claim.status / claim.verify polling cannot take it over).

Replays the Auth Session claim round trip (claim.start / claim.verify / claim.status) and the Wappi Proxy's pairing
ops exactly as the canonical graphs run them — the REAL SQL of every Postgres node (staging Postgres, session temp
tables) and the REAL Code bodies (Claim Decide, Issue Token, Build Request), with every If on the path evaluated by
n8n's own expression engine — and asserts the CORRECT outcome:

  S1  owner re-pairs its own profile while a stranger holds a pending claim -> the stranger gets nothing
  S2  legacy (owner-less) profile re-paired by its owner through the proxy -> NOT bound by the pairing; bound by the
      code that then lands in its own chat
  S3  a stranger takes over the pending claim while the claimant is pairing -> nobody is bound (no takeover)
  S4  a claim older than one day records no pairing; a renewed claim (claim.start) pairs again and binds by code
  S5  a stale claim capability (claim taken over) cannot start a pairing through the proxy
  S7  claim.status never mints or sends a code
  S8  transfer by code on reinstall: wrong / expired / replayed code change nothing, the right code transfers
  S9  two concurrent claims: only the code from the bot's own chat binds; the racing claimant without it changes nothing
  S10 an unauthenticated claim.* call never reaches Claim Start (nothing written before the refusal)
  S11 V2: a QR request + the OWNER's independent re-connection never hands an owner-less profile (or its workflow) over
  S12 V2: a code the transport did not accept is reported (code_send_failed) and cleared; the retry sends a new one
  S13 V2: five wrong answers kill the code; the right one is then refused, no new code until the window ends
  S14 V2: structure — no reauth bind exists, only Verify Claim Code writes owner_id, claim.verify never calls Wappi
  S15 V2: claim.status / claim.verify of another install never take over a pending claim (nor burn its attempts)
  S16 V2: Telegram — the code goes to the AUTHORISED account (account.phone), never to a number named in auth/phone
  S17 M4: claim.* on the install's OWN retired row (the sweep / an unfinished delete removed the profile) -> 410
      profile_retired + the reason; Wappi is not asked, no pending claim, no code, Verify Claim Code never runs
  S18 M4: a retired row this install does not own (owner-less, another install's) -> the same 410 WITHOUT a reason;
      no pending claim, never in the token's c; a live Wappi profile behind it (liveness false positive) gets no code
  S19 M4: the invariant the defect broke — every claimMethod:'owned' answer issues a token whose p lists the profile
      (a matrix of row states: own / foreign / owner-less × live / retired)
  S20 A3: claim.verify by an install WITH a billing identity moves the claimed row onto it — owner_id AND app_user_id (spec
      §1: the row follows the owning install's billing id; else a heal retires the old id and the claimed bots fall silent)
  S21 A3: claim.verify by an install WITHOUT one (billingConflict / nothing bound) moves the owner and leaves app_user_id
  S22 A3: a wrong / foreign-install / retired claim changes no app_user_id

M4 (follow-ups 2026-10-09): Claim Decide answered 'owned' for the install's own RETIRED row (deleted_at set) while the
token — built from live rows only — never listed it; the client's boot loop read 'owned' as «done, next» and asked the
same profile again forever (one n8n execution, three Postgres statements and a Wappi get/status per turn).
"""
import base64
import datetime as dt
import json
import re

from harness import Case, PgSession, bind, code, expr, node, report, run_js, sql_literal, wf

W = wf('crAuthSession001-Auth_Session.json')
P = wf('crWappiProxy0001-Wappi_Proxy.json')
Q = lambda w, name: node(w, name)['parameters']['query']
IF = lambda w, name: node(w, name)['parameters']['conditions']['conditions'][0]['leftValue']
REPL = lambda w, name: node(w, name)['parameters']['options']['queryReplacement']


def has(w, name):
    return any(n['name'] == name for n in w['nodes'])


SETUP = """create temp table installs (like public.installs including all);
create temp table bot_profiles (like public.bot_profiles including all);
alter table bot_profiles add column if not exists claim_pair_by text, add column if not exists claim_pair_at timestamptz;  -- sql/2026-09-27-claim-pairing.sql
alter table bot_profiles add column if not exists claim_code_attempts int not null default 0;                           -- sql/2026-10-07-claim-code-attempts.sql
insert into installs (install_id, secret_hash) values ('inst-O', 'h'), ('inst-A', 'h'), ('inst-L', 'h'), ('inst-N', 'h'), ('inst-B', 'h');"""

SEND_OK = {'statusCode': 200, 'body': {'status': 'done', 'message_id': 'mock-1'}}
SEND_FAIL = {'statusCode': 500, 'body': {'status': 'error', 'detail': 'injected'}}


def iso(epoch):
    return dt.datetime.fromtimestamp(epoch, dt.timezone.utc).isoformat().replace('+00:00', 'Z')


def now(pg):
    return int(float(pg.q("select extract(epoch from clock_timestamp()) as t")[0]['t']))


def edges_into(w, target):
    return [(src, i) for src, outs in w['connections'].items() for i, out in enumerate(outs.get('main', [])) for c in out if c['node'] == target]


def sha(code_):
    return run_js("return [{json:{h: require('crypto').createHash('sha256').update(String($input.first().json.c)).digest('hex')}}];",
                  {'json': {'c': code_}})[0]['json']['h']


def claim(pg, op, install, pid, status, code_='', channel='whatsapp', send=SEND_OK, status_http=200, u=None):
    """One Auth Session claim.* call from Verify Install on (the install is authenticated), walking the canonical graph.
    Returns what the client would see (`issued` = Issue Token's response) plus the side effects:
    {decide, status_called, sent_code, recipient, bound, verify_rows, reload, issued}. `status_http` is the HTTP status
    Wappi's get/status answers with (fullResponse) when it is called. `u` = the billing id Verify Install hands on for the
    install (the token's u; None = no billing identity — billingConflict or nothing bound)."""
    route = {'op': op, 'profileId': pid, 'channel': channel, 'installId': install, 'code': code_,
             'codeHash': sha(code_) if code_ else '', 'nowSec': now(pg), 'installSecret': ''}
    inst = {'verdict': 'checked', 'install_id': install, 'app_user_id': u}
    if has(W, 'Install Verified?'):
        assert expr(IF(W, 'Install Verified?'), inst, {'Route': route}) is True
    start = pg.q(bind(Q(W, 'Claim Start'), [pid, install, u or '', channel, op]))[0]   # its replacement: (app_user_id || '')
    named = {'Route': route, 'Verify Install': inst, 'Claim Start': start}
    status_called = expr(IF(W, 'Needs Status?'), start, named) if has(W, 'Needs Status?') else True
    item = {'json': {'statusCode': status_http, 'body': status}} if status_called else {'json': start}
    dec = code(W, 'Claim Decide', item, named=named)[0]['json']
    named['Claim Decide'] = dec
    out = {'decide': dec, 'status_called': status_called, 'sent_code': None, 'recipient': None, 'bound': None, 'verify_rows': None}
    if expr(IF(W, 'Claim Verify?'), dec, named):
        # A3: bound by the node's OWN queryReplacement (n8n's expression engine) — it names the install's billing id too
        rows = pg.q(bind(Q(W, 'Verify Claim Code'), expr(REPL(W, 'Verify Claim Code'), dec, named)))
        named['Verify Claim Code'] = rows[0] if rows else {}
        out['verify_rows'] = [r for r in rows if r.get('profile_id')]
    elif expr(IF(W, 'Send Code?'), dec, named):
        stored = pg.q(bind(Q(W, 'Store Claim Code'), [pid, install, dec['codeHash']]))
        named['Store Claim Code'] = stored[0] if stored else {}
        if not has(W, 'Code Stored?') or expr(IF(W, 'Code Stored?'), named['Store Claim Code'], named):
            named['Send Claim Code'] = send
            out['recipient'] = json.loads(expr(node(W, 'Send Claim Code')['parameters']['jsonBody'], {}, named))['recipient']
            out['sent_code'] = dec['code']                                         # Send Claim Code: into the bot's own chat
            if has(W, 'Code Sent?') and not expr(IF(W, 'Code Sent?'), send, named):
                pg.q(bind(Q(W, 'Clear Claim Code'), expr(REPL(W, 'Clear Claim Code'), send, named)))
                out['sent_code'] = None                                            # the transport refused it: nobody holds it
    elif has(W, 'Bind Reauth?') and expr(IF(W, 'Bind Reauth?'), dec, named):
        out['bound'] = pg.q(bind(Q(W, 'Bind Reauthed Profile'), [pid, install, dec['authorizedEpoch']]))
    out['reload'] = pg.q(bind(Q(W, 'Reload Ownership'), [install]))[0]
    out['issued'] = code(W, 'Issue Token', {'json': out['reload']}, named=named)[0]['json']
    return out


def pair(pg, install, pid, token):
    """The proxy's qr op on `pid` with the install's token claims -> 'wappi' (reached the transport) or the refusal."""
    gate = {'crAuth': {'ok': True, 'mode': 'session', 'installId': install, 'profiles': token.get('p') or [],
                       'claimPending': token.get('c') or [], 'workflows': [], 'appUserId': None},
            'body': {'op': 'qr', 'channel': 'whatsapp', 'profile_id': pid}}
    br = code(P, 'Build Request', {'json': gate})[0]['json']
    if not br.get('ok'):
        return f"refused {br.get('_status')} {br.get('error')}"
    named = {'Auth Gate': gate, 'Build Request': br}
    if has(P, 'Claim Pairing?') and expr(IF(P, 'Claim Pairing?'), br, named):
        params = expr(REPL(P, 'Record Claim Pairing'), br, named)
        rows = pg.q(bind(Q(P, 'Record Claim Pairing'), params))
        row = rows[0] if rows else {}
        if not expr(IF(P, 'Pairing Recorded?'), row, named):
            return 'refused claim_not_pending'
    return 'wappi'


def owner_of(pg, pid):
    return pg.q(f"select owner_id from bot_profiles where profile_id = '{pid}'")[0]['owner_id']


def pending_of(pg, pid):
    return pg.q(f"select claim_pending_by from bot_profiles where profile_id = '{pid}'")[0]['claim_pending_by']


def wrong(c):
    return '000000' if c != '000000' else '111111'


AUTH_OLD = lambda pg: {'authorized': True, 'phone': '77001112233', 'authorized_at': iso(now(pg) - 86400)}
UNAUTH = {'authorized': False}


def paired_now(pg, phone='77005556677'):
    pg.x('select pg_sleep(1.1);')
    return {'authorized': True, 'phone': phone, 'authorized_at': iso(now(pg) + 1)}


with Case('R1 S1: the owner re-pairs its own profile while a stranger holds a pending claim -> the stranger gets nothing') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id, claimed_at) values ('P1', 'rc-O', 'whatsapp', 'inst-O', now());")
        a1 = claim(pg, 'claim.start', 'inst-A', 'P1', AUTH_OLD(pg))
        pg.x('select pg_sleep(1.1);')
        c.check(pair(pg, 'inst-O', 'P1', {'p': ['P1']}) == 'wappi', 'the owner must reach its own pairing')
        a2 = claim(pg, 'claim.status', 'inst-A', 'P1', paired_now(pg))
        owner = owner_of(pg, 'P1')
        c.check(owner == 'inst-O', f'owner changed to {owner} ({a2["decide"]})')
        c.check('P1' not in (a2['reload']['p'] or []), f"stranger's token owns P1: {a2['reload']}")
        c.check(a2['bound'] in (None, []), f'bind ran: {a2["bound"]}')

with Case('V2 S2: a legacy profile re-paired by its owner is NOT bound by the pairing — it is bound by the code that then lands in its own chat') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, workflow_id) values ('P2', 'rc-legacy', 'whatsapp', 'wf-2');")
        s = claim(pg, 'claim.start', 'inst-L', 'P2', UNAUTH)
        c.check(s['decide']['claimMethod'] == 'reauth' and 'P2' in s['reload']['c'], f'start {s["decide"]} {s["reload"]}')
        pg.x('select pg_sleep(1.1);')
        c.check(pair(pg, 'inst-L', 'P2', s['reload']) == 'wappi', 'the claimant must be able to pair')
        done = paired_now(pg)
        st = claim(pg, 'claim.status', 'inst-L', 'P2', done)
        c.check(st['decide']['claimMethod'] == 'pending' and 'P2' not in (st['reload']['p'] or []) and owner_of(pg, 'P2') is None,
                f'a pairing alone must not bind: {st["decide"].get("claimMethod")} bound={st["bound"]} reload={st["reload"]}')
        st2 = claim(pg, 'claim.start', 'inst-L', 'P2', done)
        c.check(st2['issued'].get('claimMethod') == 'code' and st2['sent_code'] and st2['recipient'] == '77005556677',
                f'claim.start after the pairing must send a code into the paired account\'s own chat: {st2["issued"]} -> {st2["recipient"]}')
        v = claim(pg, 'claim.verify', 'inst-L', 'P2', done, st2['sent_code'] or '')
        c.check('P2' in (v['reload']['p'] or []) and v['reload']['w'] == ['wf-2'] and v['issued'].get('success') is True,
                f'the code from the own chat must bind (with its workflow): {v["issued"]} {v["reload"]}')

with Case('R1 S3: a stranger takes over the pending claim while the claimant pairs -> nobody is bound') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P3', 'rc-legacy', 'whatsapp');")
        s = claim(pg, 'claim.start', 'inst-L', 'P3', UNAUTH)
        pg.x('select pg_sleep(1.1);')
        pair(pg, 'inst-L', 'P3', s['reload'])
        claim(pg, 'claim.start', 'inst-A', 'P3', UNAUTH)                         # takeover of the pending claim
        done = paired_now(pg)                                                     # L's pairing completes
        a = claim(pg, 'claim.status', 'inst-A', 'P3', done)
        l = claim(pg, 'claim.status', 'inst-L', 'P3', done)
        owner = owner_of(pg, 'P3')
        c.check(owner is None and 'P3' not in (a['reload']['p'] or []), f'owner={owner} A={a["decide"]} bound={a["bound"]}')

with Case('V2 S4: a claim older than a day records no pairing; a renewed claim (claim.start) pairs again and binds only by code') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P4', 'rc-legacy', 'whatsapp');")
        s = claim(pg, 'claim.start', 'inst-L', 'P4', UNAUTH)
        pg.x('select pg_sleep(1.1);')
        pair(pg, 'inst-L', 'P4', s['reload'])
        pg.x("update bot_profiles set claim_pending_at = claim_pending_at - interval '2 days', claim_pair_at = claim_pair_at - interval '2 days' where profile_id = 'P4';")
        c.check(pair(pg, 'inst-L', 'P4', {'c': ['P4']}) == 'refused claim_not_pending', 'an expired claim must not record a pairing')
        old = claim(pg, 'claim.status', 'inst-L', 'P4', paired_now(pg))
        c.check('P4' not in (old['reload']['p'] or []), f'an expired claim\'s pairing bound: {old["decide"]} {old["bound"]}')
        renew = claim(pg, 'claim.start', 'inst-L', 'P4', UNAUTH)                 # the renewal is claim.start (V2: never status polling)
        fresh = pg.q("select (claim_pending_at > now() - interval '1 minute') as fresh from bot_profiles where profile_id = 'P4'")[0]['fresh']
        c.check(fresh is True, f'the renewed claim must carry a fresh clock: {fresh}')
        pg.x('select pg_sleep(1.1);')
        c.check(pair(pg, 'inst-L', 'P4', renew['reload']) == 'wappi', 'a renewed claim must be able to pair again')
        done = paired_now(pg)
        st = claim(pg, 'claim.status', 'inst-L', 'P4', done)
        c.check('P4' not in (st['reload']['p'] or []), f'a pairing alone must not bind: {st["decide"]} {st["bound"]}')
        start = claim(pg, 'claim.start', 'inst-L', 'P4', done)
        ok = claim(pg, 'claim.verify', 'inst-L', 'P4', done, start['sent_code'] or '')
        c.check('P4' in (ok['reload']['p'] or []), f'the code after the renewed pairing must bind: {start["issued"]} {ok["issued"]}')

with Case('R1 S5: a stale claim capability (the claim was taken over) cannot start a pairing through the proxy') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P5', 'rc-legacy', 'whatsapp');")
        s = claim(pg, 'claim.start', 'inst-L', 'P5', UNAUTH)
        claim(pg, 'claim.start', 'inst-A', 'P5', UNAUTH)
        res = pair(pg, 'inst-L', 'P5', s['reload'])                               # L's token still lists P5 in c
        c.check(res == 'refused claim_not_pending', f'stale capability reached the transport: {res}')

with Case('R1 S7: claim.status never mints or sends a code (no unsolicited codes / phishing aid)') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('P7', 'rc-O', 'whatsapp', 'inst-O');")
        sent = [claim(pg, 'claim.status', 'inst-A', 'P7', AUTH_OLD(pg))['sent_code'] for _ in range(3)]
        dec = claim(pg, 'claim.status', 'inst-A', 'P7', AUTH_OLD(pg))['decide']
        c.check(sent == [None, None, None] and not dec.get('code'), f'claim.status sent codes {sent} / minted {dec.get("code")}')

with Case('R1 S8: transfer by code on reinstall — wrong / expired / replayed codes change nothing, the right code transfers') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('P8', 'rc-O', 'whatsapp', 'inst-O');")
        s = claim(pg, 'claim.start', 'inst-N', 'P8', AUTH_OLD(pg))
        right = s['sent_code']
        c.check(s['decide']['claimMethod'] == 'code' and right and len(right) == 6, f'claim.start must send a code into the bot chat: {s["decide"]}')
        bad = claim(pg, 'claim.verify', 'inst-N', 'P8', AUTH_OLD(pg), wrong(right))
        c.check(not bad['verify_rows'] and owner_of(pg, 'P8') == 'inst-O', 'a wrong code moved the owner')
        c.check(bad['issued'].get('error') == 'claim_code_rejected' and bad['issued'].get('_status') == 403, f'a wrong code must answer 403: {bad["issued"]}')
        good = claim(pg, 'claim.verify', 'inst-N', 'P8', AUTH_OLD(pg), right)
        c.check('P8' in (good['reload']['p'] or []), f'the right code must transfer: {good}')
        replay = claim(pg, 'claim.verify', 'inst-B', 'P8', AUTH_OLD(pg), right)
        c.check(not replay['verify_rows'] and owner_of(pg, 'P8') == 'inst-N', 'a replayed code moved the owner')
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('P8x', 'rc-O', 'whatsapp', 'inst-O');")
        s2 = claim(pg, 'claim.start', 'inst-N', 'P8x', AUTH_OLD(pg))
        pg.x("update bot_profiles set claim_code_expires_at = now() - interval '1 second' where profile_id = 'P8x';")
        late = claim(pg, 'claim.verify', 'inst-N', 'P8x', AUTH_OLD(pg), s2['sent_code'])
        c.check(not late['verify_rows'], 'an expired code bound')

with Case('R1 S9: two concurrent claims — only the proof (the code from the bot\'s own chat) binds; the racing claimant without it changes nothing') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('P9', 'rc-O', 'whatsapp', 'inst-O');")
        a = claim(pg, 'claim.start', 'inst-A', 'P9', AUTH_OLD(pg))               # a stranger starts first: a code lands in the owner's chat
        n = claim(pg, 'claim.start', 'inst-N', 'P9', AUTH_OLD(pg))               # the owner's new phone: no second code while the first is valid
        in_chat = a['sent_code']
        c.check(in_chat and n['sent_code'] is None and n['issued'].get('codeReused') is True, f'expected exactly one code in the chat: {a["sent_code"]} / {n["sent_code"]}')
        guess = claim(pg, 'claim.verify', 'inst-A', 'P9', AUTH_OLD(pg), wrong(in_chat))
        st = claim(pg, 'claim.status', 'inst-A', 'P9', paired_now(pg))
        owner = owner_of(pg, 'P9')
        c.check(not guess['verify_rows'] and st['bound'] in (None, []) and owner == 'inst-O', f'the racing claimant moved the owner: {owner}')
        good = claim(pg, 'claim.verify', 'inst-N', 'P9', AUTH_OLD(pg), in_chat)   # the owner reads the code and enters it on N
        c.check('P9' in (good['reload']['p'] or []), f'the owner with the code must win the race: {good}')
        again = claim(pg, 'claim.verify', 'inst-A', 'P9', AUTH_OLD(pg), in_chat)  # the consumed code is worthless afterwards
        c.check(not again['verify_rows'] and owner_of(pg, 'P9') == 'inst-N', 'a consumed code re-bound')

with Case('R1 S10: an unauthenticated claim.* call never reaches Claim Start (no pending claim / row written before the refusal)') as c:
    ins = edges_into(W, 'Claim Start')
    c.check(ins == [('Install Verified?', 0)], f'Claim Start must be reachable only from Install Verified? (true): {ins}')
    if has(W, 'Install Verified?'):
        route = {'op': 'claim.start', 'profileId': 'P10', 'installId': 'ghost'}
        c.check(expr(IF(W, 'Install Verified?'), {'verdict': 'checked', 'install_id': None}, {'Route': route}) is False, 'a refused install passes')
        c.check(expr(IF(W, 'Install Verified?'), {'message': 'db down', 'error': {}}, {'Route': route}) is False, 'a DB fault passes')
        c.check(edges_into(W, 'Install Verified?') == [('Claim Op?', 0)] and ('Install Verified?', 1) in edges_into(W, 'Issue Token'),
                'Install Verified? must sit on Claim Op? true and refuse into Issue Token')

with Case('V2 S11: a QR request + the OWNER\'s independent re-connection never hands an owner-less profile (or its workflow) to the requester') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, workflow_id) values ('P11', 'rc-legacy', 'whatsapp', 'wf-11');")
        a0 = claim(pg, 'claim.start', 'inst-A', 'P11', UNAUTH)
        c.check(a0['decide']['claimMethod'] == 'reauth', f'start {a0["decide"]}')
        pg.x('select pg_sleep(1.1);')
        c.check(pair(pg, 'inst-A', 'P11', a0['reload']) == 'wappi', 'the claimant may still start a pairing (precondition only)')
        owner_reconnect = paired_now(pg, phone='77001112233')                    # the OWNER reconnects (legacy client / Wappi cabinet)
        a1 = claim(pg, 'claim.status', 'inst-A', 'P11', owner_reconnect)
        c.check(owner_of(pg, 'P11') is None and a1['reload']['p'] == [] and a1['reload']['w'] == [],
                f'the requester got the profile from someone else\'s reconnection: owner={owner_of(pg, "P11")} p={a1["reload"]["p"]} w={a1["reload"]["w"]}')
        a2 = claim(pg, 'claim.start', 'inst-A', 'P11', owner_reconnect)
        code_in_owner_chat = a2['sent_code']
        c.check(a2['issued'].get('claimMethod') == 'code' and a2['recipient'] == '77001112233',
                f'the code must go into the paired (owner\'s) account, never anywhere the requester reads: {a2["recipient"]}')
        a3 = claim(pg, 'claim.verify', 'inst-A', 'P11', owner_reconnect, wrong(code_in_owner_chat or ''))
        c.check(a3['verify_rows'] == [] and owner_of(pg, 'P11') is None, f'a wrong code bound: {a3["verify_rows"]}')
        o1 = claim(pg, 'claim.start', 'inst-O', 'P11', owner_reconnect)            # the owner on a new install: the code is in its chat
        c.check(o1['issued'].get('codeReused') is True and o1['sent_code'] is None, f'no second code while one is valid: {o1["issued"]}')
        o2 = claim(pg, 'claim.verify', 'inst-O', 'P11', owner_reconnect, code_in_owner_chat or '')
        c.check(owner_of(pg, 'P11') == 'inst-O' and o2['reload']['w'] == ['wf-11'], f'the owner with the code must get it: {o2["issued"]} {o2["reload"]}')
        a4 = claim(pg, 'claim.verify', 'inst-A', 'P11', owner_reconnect, code_in_owner_chat or '')
        c.check(a4['verify_rows'] == [] and owner_of(pg, 'P11') == 'inst-O', 'a replayed code moved the owner')

with Case('V2 S12: a code the transport did not accept is reported (code_send_failed, 502) and cleared — the retry sends a NEW one, never "reuses" a lost one') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P12', 'rc-legacy', 'whatsapp');")
        f = claim(pg, 'claim.start', 'inst-L', 'P12', AUTH_OLD(pg), send=SEND_FAIL)
        c.check(f['issued'].get('success') is False and f['issued'].get('error') == 'code_send_failed' and f['issued'].get('_status') == 502,
                f'a refused send must be reported, not claimMethod code: {f["issued"]}')
        left = pg.q("select claim_code_hash is null and claim_code_expires_at is null as cleared from bot_profiles where profile_id = 'P12'")[0]['cleared']
        c.check(left is True, 'the unsent code must be cleared')
        r = claim(pg, 'claim.start', 'inst-L', 'P12', AUTH_OLD(pg))
        c.check(r['sent_code'] and not r['issued'].get('codeReused') and r['issued'].get('claimMethod') == 'code', f'the retry must send a new code: {r["issued"]}')
        v = claim(pg, 'claim.verify', 'inst-L', 'P12', AUTH_OLD(pg), r['sent_code'] or '')
        c.check('P12' in (v['reload']['p'] or []), f'the delivered code binds: {v["issued"]}')

with Case('V2 S13: five wrong answers kill the code — the right one is then refused (claim_code_locked) and no new code is sent until its window ends') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P13', 'rc-legacy', 'whatsapp');")
        s = claim(pg, 'claim.start', 'inst-A', 'P13', AUTH_OLD(pg))
        right = s['sent_code'] or ''
        answers = [claim(pg, 'claim.verify', 'inst-A', 'P13', AUTH_OLD(pg), wrong(right))['issued'] for _ in range(5)]
        late = claim(pg, 'claim.verify', 'inst-A', 'P13', AUTH_OLD(pg), right)
        c.check(owner_of(pg, 'P13') is None and late['verify_rows'] == [], f'the right code bound after five wrong ones: {late["issued"]}')
        c.check(late['issued'].get('error') == 'claim_code_locked' and late['issued'].get('_status') == 429, f'a dead code must say so: {late["issued"]}')
        c.check([x.get('error') for x in answers[:4]] == ['claim_code_rejected'] * 4 and answers[4].get('error') == 'claim_code_locked',
                f'per-answer verdicts: {[x.get("error") for x in answers]}')
        again = claim(pg, 'claim.start', 'inst-A', 'P13', AUTH_OLD(pg))
        c.check(again['sent_code'] is None and again['issued'].get('error') == 'claim_code_locked', f'a new code inside the window: {again["issued"]}')
        pg.x("update bot_profiles set claim_code_expires_at = now() - interval '1 second' where profile_id = 'P13';")
        fresh = claim(pg, 'claim.start', 'inst-A', 'P13', AUTH_OLD(pg))
        ok = claim(pg, 'claim.verify', 'inst-A', 'P13', AUTH_OLD(pg), fresh['sent_code'] or '')
        c.check(fresh['sent_code'] and 'P13' in (ok['reload']['p'] or []), f'after the window a new code works: {fresh["issued"]} {ok["issued"]}')

with Case('V2 S14: structure — no reauth bind, only Verify Claim Code writes owner_id, claim.verify never calls Wappi, a send is judged') as c:
    c.check(not has(W, 'Bind Reauthed Profile') and not has(W, 'Bind Reauth?'), 'a reauth bind node still exists')
    writers = [n['name'] for n in W['nodes'] if n['type'].endswith('.postgres')
               and re.search(r'(?:\bset|,)\s+owner_id\s*=', n['parameters'].get('query', ''), re.I)]
    c.check(writers == ['Verify Claim Code'], f'owner_id writers in Auth Session: {writers}')
    c.check(edges_into(W, 'Claim Status') == [('Needs Status?', 0)] if has(W, 'Needs Status?') else False,
            f'Claim Status must be reachable only from Needs Status? (true): {edges_into(W, "Claim Status")}')
    if has(W, 'Needs Status?'):
        c.check(expr(IF(W, 'Needs Status?'), {}, {'Route': {'op': 'claim.verify'}}) is False, 'claim.verify reaches Wappi get/status')
        c.check(expr(IF(W, 'Needs Status?'), {}, {'Route': {'op': 'claim.start'}}) is True, 'claim.start must read get/status')
    c.check(has(W, 'Code Sent?') and ('Code Sent?', 1) in edges_into(W, 'Clear Claim Code'), 'an unsent code must be cleared')
    c.check("'reauth' : 'pending'" not in node(W, 'Claim Decide')['parameters']['jsCode'], 'claim.status still decides a reauth bind')

with Case('V2 S15: another install\'s claim.status / claim.verify never take over a pending claim, nor burn its attempts') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P15', 'rc-legacy', 'whatsapp');")
        o = claim(pg, 'claim.start', 'inst-O', 'P15', AUTH_OLD(pg))
        for _ in range(3):
            claim(pg, 'claim.status', 'inst-A', 'P15', AUTH_OLD(pg))
        guesses = [claim(pg, 'claim.verify', 'inst-A', 'P15', AUTH_OLD(pg), wrong(o['sent_code'] or '')) for _ in range(5)]
        a = claim(pg, 'claim.status', 'inst-A', 'P15', AUTH_OLD(pg))
        c.check(pending_of(pg, 'P15') == 'inst-O' and 'P15' not in (a['reload']['c'] or []),
                f'polling took the claim over: pending={pending_of(pg, "P15")} A.c={a["reload"]["c"]}')
        c.check(all(g['verify_rows'] == [] for g in guesses), 'a non-pending install bound')
        v = claim(pg, 'claim.verify', 'inst-O', 'P15', AUTH_OLD(pg), o['sent_code'] or '')
        c.check(owner_of(pg, 'P15') == 'inst-O', f'the pending owner\'s right code must still bind: {v["issued"]}')

with Case('V2 S16: Telegram — the code goes to the AUTHORISED account (account.phone), never to a number named in auth/phone') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P16', 'rc-legacy', 'telegram');")
        tg = {'authorized': True, 'phone': '70000000009', 'account': {'phone': '70000000001', 'user_id': 1}, 'authorized_at': iso(now(pg) - 60)}
        s = claim(pg, 'claim.start', 'inst-A', 'P16', tg, channel='telegram')
        c.check(s['recipient'] == '70000000001', f'recipient {s["recipient"]} (top-level phone may be a number someone typed into auth/phone)')
        wa = claim(pg, 'claim.start', 'inst-A', 'P16x', {'authorized': True, 'phone': '77001234567'}, channel='whatsapp')
        c.check(wa['recipient'] == '77001234567', f'WhatsApp keeps the top-level phone: {wa["recipient"]}')
        v = claim(pg, 'claim.verify', 'inst-A', 'P16', tg, s['sent_code'] or '', channel='telegram')
        c.check('P16' in (v['reload']['p'] or []), f'the Telegram code binds: {v["issued"]}')

RETIRED_REASONS = ('trial_expiry', 'churn_grace', 'liveness', 'app_delete', 'channel_limit')


def retire(pg, pid, owner, reason, channel='whatsapp'):
    """A bot_profiles row the Profile Lifecycle Sweep / Delete Bot Files / Retire Refused Profile has retired."""
    pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id, claimed_at, deleted_at, deleted_reason) values "
         f"({sql_literal(pid)}, 'rc-O', {sql_literal(channel)}, {sql_literal(owner)}, now() - interval '3 days', now() - interval '2 days', {sql_literal(reason)});")


def row_of(pg, pid):
    return pg.q(f"select owner_id, claim_pending_by, claim_pending_at, claim_code_hash, deleted_at is not null as retired "
                f"from bot_profiles where profile_id = {sql_literal(pid)}")[0]


def token_profiles(issued):
    """The p claim of the token an answer carries (decoded, not verified) — None when the answer carries no token."""
    tok = issued.get('token')
    if not tok:
        return None
    part = tok.split('.')[1]
    return json.loads(base64.urlsafe_b64decode(part + '=' * (-len(part) % 4))).get('p') or []


def refused_retired(r, reason=None):
    """What the client must see for a retired profile: 410 profile_retired, no token; the reason only for its owner."""
    i = r['issued']
    problems = []
    if not (i.get('success') is False and i.get('error') == 'profile_retired' and i.get('_status') == 410):
        problems.append(f"answer {i.get('_status')} {i.get('error')} success={i.get('success')} claimMethod={i.get('claimMethod')}")
    if 'token' in i:
        problems.append('a refusal must not carry a token')
    if reason is None and 'retiredReason' in i:
        problems.append(f"the reason leaked to a caller that does not own the row: {i.get('retiredReason')}")
    if reason is not None and i.get('retiredReason') != reason:
        problems.append(f"retiredReason {i.get('retiredReason')!r} != {reason!r}")
    if r['status_called']:
        problems.append('Wappi get/status was called for a retired profile')
    if r['sent_code']:
        problems.append('a code was sent into a retired profile\'s chat')
    if r['verify_rows'] is not None:
        problems.append('Verify Claim Code ran for a retired profile')
    return problems


with Case('M4 S17: claim.start / claim.status / claim.verify on the install\'s OWN retired row -> 410 profile_retired + the reason; Wappi is not asked, no pending claim, no code, Verify Claim Code never runs') as c:
    with PgSession(SETUP) as pg:
        calls = []
        for reason in RETIRED_REASONS:
            pid = f'Z-{reason}'
            retire(pg, pid, 'inst-O', reason)
            calls += [(pid, reason, 'whatsapp', 'claim.start', UNAUTH, 200, ''),
                      (pid, reason, 'whatsapp', 'claim.start', {'detail': 'profile not found'}, 404, ''),   # Wappi already deleted it
                      (pid, reason, 'whatsapp', 'claim.status', UNAUTH, 200, ''),
                      (pid, reason, 'whatsapp', 'claim.verify', UNAUTH, 200, '123456')]
        retire(pg, 'Z-tg', 'inst-O', 'trial_expiry', channel='telegram')
        calls += [('Z-tg', 'trial_expiry', 'telegram', op, UNAUTH, 200, '123456' if op == 'claim.verify' else '')
                  for op in ('claim.start', 'claim.status', 'claim.verify')]
        for pid, reason, ch, op, status, http, code_ in calls:
            r = claim(pg, op, 'inst-O', pid, status, code_, channel=ch, status_http=http)
            for p in refused_retired(r, reason):
                c.check(False, f'{pid} {op} (Wappi {http}): {p}')
            row = row_of(pg, pid)
            c.check(row['claim_pending_by'] is None and row['claim_pending_at'] is None and row['claim_code_hash'] is None,
                    f'{pid} {op}: a claim was written onto a retired row: {row}')
            c.check(row['owner_id'] == 'inst-O' and row['retired'] is True, f'{pid} {op}: the row itself changed: {row}')

with Case('M4 S18: a retired row this install does NOT own (owner-less, another install\'s) -> the same 410 without a reason; no pending claim, never in c; a live Wappi profile behind it (liveness false positive) gets no code and no owner') as c:
    with PgSession(SETUP) as pg:
        retire(pg, 'R-wa', None, 'liveness')                                     # an owner-less (compat-era) row the sweep retired
        retire(pg, 'F-wa', 'inst-A', 'trial_expiry')                             # another install's retired row
        for pid in ('R-wa', 'F-wa'):
            for op in ('claim.start', 'claim.status', 'claim.start'):
                r = claim(pg, op, 'inst-O', pid, UNAUTH)
                for p in refused_retired(r):
                    c.check(False, f'{pid} {op}: {p}')
                c.check(row_of(pg, pid)['claim_pending_by'] is None, f'{pid} {op}: a pending claim was written onto a retired row: {row_of(pg, pid)}')
                c.check(pid not in (r['reload']['c'] or []), f'{pid} {op}: the retired row entered the token\'s c: {r["reload"]["c"]}')
        retire(pg, 'B-wa', None, 'liveness')                                     # the Wappi profile is in fact still authorised
        s = claim(pg, 'claim.start', 'inst-O', 'B-wa', AUTH_OLD(pg))
        for p in refused_retired(s):
            c.check(False, f'B-wa claim.start: {p}')
        v = claim(pg, 'claim.verify', 'inst-O', 'B-wa', AUTH_OLD(pg), s['sent_code'] or '123456')
        for p in refused_retired(v):
            c.check(False, f'B-wa claim.verify: {p}')
        c.check(row_of(pg, 'B-wa')['owner_id'] is None, f'a retired owner-less row got an owner: {row_of(pg, "B-wa")}')

with Case('M4 S19: invariant — every claimMethod:\'owned\' answer issues a token whose p lists the profile (own / foreign / owner-less × live / retired)') as c:
    with PgSession(SETUP) as pg:
        cells = [('own live', 'C-own', 'inst-O', None)]
        cells += [(f'own retired {r}', f'C-own-{r}', 'inst-O', r) for r in RETIRED_REASONS]
        cells += [('foreign live', 'C-for', 'inst-A', None), ('foreign retired', 'C-for-r', 'inst-A', 'churn_grace'),
                  ('owner-less live', 'C-none', None, None), ('owner-less retired', 'C-none-r', None, 'liveness')]
        for label, pid, owner, reason in cells:
            if reason:
                retire(pg, pid, owner, reason)
            else:
                pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ({sql_literal(pid)}, 'rc-O', 'whatsapp', {sql_literal(owner)});")
        owned_seen = False
        for label, pid, owner, reason in cells:
            for op in ('claim.start', 'claim.status', 'claim.verify'):
                r = claim(pg, op, 'inst-O', pid, AUTH_OLD(pg), '123456' if op == 'claim.verify' else '', send=SEND_OK)
                i = r['issued']
                if i.get('success') and i.get('claimMethod') == 'owned':
                    owned_seen = owned_seen or label == 'own live'
                    c.check(pid in (i.get('profiles') or []) and pid in (token_profiles(i) or []),
                            f'{label} {op}: «owned» without the profile in the token — profiles={i.get("profiles")} token p={token_profiles(i)}')
                if reason:
                    c.check(i.get('success') is False, f'{label} {op}: a retired row answered success ({i.get("claimMethod")})')
        c.check(owned_seen, 'positive control: the install\'s own live row must answer «owned» (else the invariant is vacuous)')

def keyed_row(pg, pid):
    return pg.q(f"select owner_id, app_user_id, workflow_id, claimed_at is not null as claimed from bot_profiles where profile_id = {sql_literal(pid)}")[0]


with Case('A3 S20: claim.verify by an install WITH a billing identity moves the claimed row onto it — owner_id AND app_user_id '
          '(spec §1: the row follows the owning install\'s billing id); the workflow and the rest of the row stay') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id) values ('P20', 'rc-O', 'whatsapp', 'inst-O', 'wf-20');")
        s = claim(pg, 'claim.start', 'inst-N', 'P20', AUTH_OLD(pg), u='rc-N')
        c.check(keyed_row(pg, 'P20')['app_user_id'] == 'rc-O', f'claim.start must not move the billing key: {keyed_row(pg, "P20")}')
        good = claim(pg, 'claim.verify', 'inst-N', 'P20', AUTH_OLD(pg), s['sent_code'] or '', u='rc-N')
        row = keyed_row(pg, 'P20')
        c.check(row == {'owner_id': 'inst-N', 'app_user_id': 'rc-N', 'workflow_id': 'wf-20', 'claimed': True} and 'P20' in (good['reload']['p'] or []),
                f'the claimed row must follow the claimant\'s billing id: {row} issued={good["issued"]}')
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, workflow_id) values ('P20L', 'rc-legacy', 'telegram', 'wf-20l');")
        tg = {'authorized': True, 'phone': '70000000009', 'account': {'phone': '70000000001'}, 'authorized_at': iso(now(pg) - 60)}
        s2 = claim(pg, 'claim.start', 'inst-N', 'P20L', tg, channel='telegram', u='rc-N')
        claim(pg, 'claim.verify', 'inst-N', 'P20L', tg, s2['sent_code'] or '', channel='telegram', u='rc-N')
        c.check(keyed_row(pg, 'P20L')['app_user_id'] == 'rc-N' and keyed_row(pg, 'P20L')['owner_id'] == 'inst-N',
                f'an owner-less (compat-era) row claimed by code follows the claimant too: {keyed_row(pg, "P20L")}')

with Case('A3 S21: claim.verify by an install WITHOUT a billing identity (u = null: billingConflict, or nothing bound) moves the '
          'owner and leaves app_user_id as it is — never NULL, never the empty string') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('P21', 'rc-O', 'whatsapp', 'inst-O');")
        s = claim(pg, 'claim.start', 'inst-N', 'P21', AUTH_OLD(pg), u=None)
        claim(pg, 'claim.verify', 'inst-N', 'P21', AUTH_OLD(pg), s['sent_code'] or '', u=None)
        row = keyed_row(pg, 'P21')
        c.check(row['owner_id'] == 'inst-N' and row['app_user_id'] == 'rc-O', f'u = null must leave app_user_id: {row}')
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('P21e', 'rc-O', 'whatsapp', 'inst-O');")
        s = claim(pg, 'claim.start', 'inst-N', 'P21e', AUTH_OLD(pg), u='')
        claim(pg, 'claim.verify', 'inst-N', 'P21e', AUTH_OLD(pg), s['sent_code'] or '', u='')
        c.check(keyed_row(pg, 'P21e')['app_user_id'] == 'rc-O', f'an empty u must leave app_user_id: {keyed_row(pg, "P21e")}')

with Case('A3 S22: a wrong code, another install\'s guess and a retired row change NO app_user_id (only a bound claim re-keys)') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('P22', 'rc-O', 'whatsapp', 'inst-O');")
        s = claim(pg, 'claim.start', 'inst-N', 'P22', AUTH_OLD(pg), u='rc-N')
        claim(pg, 'claim.verify', 'inst-N', 'P22', AUTH_OLD(pg), wrong(s['sent_code'] or ''), u='rc-N')
        claim(pg, 'claim.verify', 'inst-A', 'P22', AUTH_OLD(pg), s['sent_code'] or '', u='rc-A')     # not the pending install
        c.check(keyed_row(pg, 'P22')['app_user_id'] == 'rc-O' and keyed_row(pg, 'P22')['owner_id'] == 'inst-O',
                f'a refused claim moved the billing key: {keyed_row(pg, "P22")}')
        retire(pg, 'P22r', 'inst-O', 'trial_expiry')
        claim(pg, 'claim.verify', 'inst-O', 'P22r', UNAUTH, '123456', u='rc-N')
        c.check(keyed_row(pg, 'P22r')['app_user_id'] == 'rc-O', f'a retired row was re-keyed: {keyed_row(pg, "P22r")}')


with Case('V2/M4/A3 negative: verify-auth-gate.py rejects an Auth Session that binds on a pairing again, lets claim.verify call Wappi, loses either half of the M4 retired-row refusal, or claims without re-keying the row onto the claimant\'s billing id (A3)') as c:
    import shutil
    import subprocess
    import sys
    import tempfile
    from pathlib import Path
    from harness import N8N

    def drop_retired_branch(w):
        cd = node(w, 'Claim Decide')['parameters']
        cd['jsCode'] = re.sub(r"else if \(row\.retired === true\) \{.*?\n\}\n", '', cd['jsCode'], flags=re.S)

    def pending_on_retired(w):
        cs = node(w, 'Claim Start')['parameters']
        cs['query'] = re.sub(r"\n\s*when bot_profiles\.deleted_at is not null then bot_profiles\.claim_pending_(by|at)", '', cs['query'])

    for label, mutate, tag in (
        ('reauth bind', lambda w: (w['nodes'].append({'parameters': {'operation': 'executeQuery', 'query': 'update bot_profiles set owner_id = $2 where profile_id = $1 returning profile_id;'},
                                                       'id': 'neg-bind', 'name': 'Bind Reauthed Profile', 'type': 'n8n-nodes-base.postgres', 'typeVersion': 2.6, 'position': [0, 0]}),), 'V2'),
        ('verify calls Wappi', lambda w: node(w, 'Needs Status?')['parameters']['conditions']['conditions'][0].__setitem__('leftValue', '={{ true }}'), 'V2'),
        ('unjudged send', lambda w: w['connections'].__setitem__('Send Claim Code', {'main': [[{'node': 'Reload Ownership', 'type': 'main', 'index': 0}]]}), 'V2'),
        # M4: each half of the fix, removed alone, must fail the structural verifier too
        ('M4 retired answered owned', drop_retired_branch, 'M4'),
        ('M4 retired asks Wappi', lambda w: node(w, 'Needs Status?')['parameters']['conditions']['conditions'][0].__setitem__(
            'leftValue', "={{ $('Route').first().json.op !== 'claim.verify' }}"), 'M4'),
        ('M4 pending on a retired row', pending_on_retired, 'M4'),
        # A3: the claimed row must follow the claimant's billing id (the SQL) from Verify Install's verdict (the parameter)
        ('A3 claim without the re-key', lambda w: node(w, 'Verify Claim Code')['parameters'].__setitem__('query', re.sub(
            r",\s*app_user_id = coalesce\(nullif\(\$4, ''\), app_user_id\)", '', node(w, 'Verify Claim Code')['parameters']['query'])), 'A3'),
        ('A3 re-key from the request', lambda w: node(w, 'Verify Claim Code')['parameters']['options'].__setitem__('queryReplacement',
            node(w, 'Verify Claim Code')['parameters']['options']['queryReplacement'].replace(
                "$('Verify Install').first().json.app_user_id", "$('Route').first().json.appUserId")), 'A3'),
    ):
        d = Path(tempfile.mkdtemp(prefix='v2-neg-'))
        try:
            for f in (N8N / 'workflows').glob('*.json'):
                shutil.copy(f, d / f.name)
            f = d / 'crAuthSession001-Auth_Session.json'
            w = json.loads(f.read_text(encoding='utf-8'))
            untouched = json.dumps(w, sort_keys=True)
            mutate(w)
            c.check(json.dumps(w, sort_keys=True) != untouched, f'{label}: the mutation found nothing to change in the canon')
            f.write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            r = subprocess.run([sys.executable, str(N8N / 'verify-auth-gate.py'), '--dir', str(d)], capture_output=True, text=True)
            c.check(r.returncode != 0 and tag in r.stdout, f'{label}: verifier passed (or failed without naming {tag}): {r.stdout[-300:]}')
        finally:
            shutil.rmtree(d, ignore_errors=True)
    r = subprocess.run([sys.executable, str(N8N / 'verify-auth-gate.py')], capture_output=True, text=True)
    c.check(r.returncode == 0, f'positive control: the committed canon must pass: {r.stdout[-300:]}')

report()
