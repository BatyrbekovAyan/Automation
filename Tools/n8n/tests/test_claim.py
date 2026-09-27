#!/usr/bin/env python3
"""R1 (iteration 2026-09-27): recovering a bot by claim must never hand a profile to an install that did not prove
control of THAT channel itself.

Replays the Auth Session claim round trip (claim.start / claim.verify / claim.status) and the Wappi Proxy's pairing
ops exactly as the canonical graphs run them — the REAL SQL of every Postgres node (staging Postgres, session temp
tables, one transaction, ROLLBACK) and the REAL Code bodies (Claim Decide, Build Request), with every If on the path
evaluated by n8n's own expression engine — and asserts the CORRECT outcome:

  S1  owner re-pairs its own profile while a stranger holds a pending claim -> the stranger gets nothing
  S2  legacy (owner-less) profile re-paired by the claimant through the proxy -> bound to the claimant
  S3  a stranger takes over the pending claim while the claimant is pairing -> nobody is bound (no takeover)
  S4  a claim older than one day records no pairing and its old pairing never binds; a renewed claim needs a new pairing
  S5  a stale claim capability (claim taken over) cannot start a pairing through the proxy
  S7  claim.status never mints or sends a code
  S8  transfer by code on reinstall: wrong / expired / replayed code change nothing, the right code transfers
  S9  two concurrent claims: only the code from the bot's own chat binds; the racing claimant without it changes nothing
  S10 an unauthenticated claim.* call never reaches Claim Start (nothing written before the refusal)
"""
import datetime as dt

from harness import Case, PgSession, bind, code, expr, node, report, run_js, wf

W = wf('crAuthSession001-Auth_Session.json')
P = wf('crWappiProxy0001-Wappi_Proxy.json')
Q = lambda w, name: node(w, name)['parameters']['query']
IF = lambda w, name: node(w, name)['parameters']['conditions']['conditions'][0]['leftValue']


def has(w, name):
    return any(n['name'] == name for n in w['nodes'])


SETUP = """create temp table installs (like public.installs including all);
create temp table bot_profiles (like public.bot_profiles including all);
alter table bot_profiles add column if not exists claim_pair_by text, add column if not exists claim_pair_at timestamptz;  -- sql/2026-09-27-claim-pairing.sql
insert into installs (install_id, secret_hash) values ('inst-O', 'h'), ('inst-A', 'h'), ('inst-L', 'h'), ('inst-N', 'h'), ('inst-B', 'h');"""


def iso(epoch):
    return dt.datetime.fromtimestamp(epoch, dt.timezone.utc).isoformat().replace('+00:00', 'Z')


def now(pg):
    return int(float(pg.q("select extract(epoch from clock_timestamp()) as t")[0]['t']))


def edges_into(w, target):
    return [(src, i) for src, outs in w['connections'].items() for i, out in enumerate(outs.get('main', [])) for c in out if c['node'] == target]


def claim(pg, op, install, pid, status, code_=''):
    """One Auth Session claim.* call from Verify Install on (the install is authenticated). Returns what the client
    would see plus the side effects: {decide, sent_code, bound, verify_rows, reload}."""
    route = {'op': op, 'profileId': pid, 'channel': 'whatsapp', 'installId': install, 'code': code_,
             'codeHash': run_js("return [{json:{h: require('crypto').createHash('sha256').update(String($input.first().json.c)).digest('hex')}}];",
                                {'json': {'c': code_}})[0]['json']['h'] if code_ else ''}
    inst = {'verdict': 'checked', 'install_id': install, 'app_user_id': None}
    if has(W, 'Install Verified?'):
        assert expr(IF(W, 'Install Verified?'), inst, {'Route': route}) is True
    start = pg.q(bind(Q(W, 'Claim Start'), [pid, install, '', 'whatsapp']))[0]
    dec = code(W, 'Claim Decide', {'json': {'statusCode': 200, 'body': status}},
               named={'Route': route, 'Verify Install': inst, 'Claim Start': start})[0]['json']
    out = {'decide': dec, 'sent_code': None, 'bound': None, 'verify_rows': None}
    named = {'Route': route, 'Claim Decide': dec}
    if expr(IF(W, 'Claim Verify?'), dec, named):
        out['verify_rows'] = pg.q(bind(Q(W, 'Verify Claim Code'), [pid, install, route['codeHash']]))
    elif expr(IF(W, 'Send Code?'), dec, named):
        pg.q(bind(Q(W, 'Store Claim Code'), [pid, install, dec['codeHash']]))
        out['sent_code'] = dec['code']                                        # Send Claim Code: into the bot's own chat
    elif expr(IF(W, 'Bind Reauth?'), dec, named):
        out['bound'] = pg.q(bind(Q(W, 'Bind Reauthed Profile'), [pid, install, dec['authorizedEpoch']]))
    out['reload'] = pg.q(bind(Q(W, 'Reload Ownership'), [install]))[0]
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
        params = expr(node(P, 'Record Claim Pairing')['parameters']['options']['queryReplacement'], br, named)
        rows = pg.q(bind(Q(P, 'Record Claim Pairing'), params))
        row = rows[0] if rows else {}
        if not expr(IF(P, 'Pairing Recorded?'), row, named):
            return 'refused claim_not_pending'
    return 'wappi'


AUTH_OLD = lambda pg: {'authorized': True, 'phone': '77001112233', 'authorized_at': iso(now(pg) - 86400)}
UNAUTH = {'authorized': False}


def paired_now(pg):
    pg.x('select pg_sleep(1.1);')
    return {'authorized': True, 'phone': '77005556677', 'authorized_at': iso(now(pg) + 1)}


with Case('R1 S1: the owner re-pairs its own profile while a stranger holds a pending claim -> the stranger gets nothing') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel, owner_id, claimed_at) values ('P1', 'rc-O', 'whatsapp', 'inst-O', now());")
        a1 = claim(pg, 'claim.start', 'inst-A', 'P1', AUTH_OLD(pg))
        pg.x('select pg_sleep(1.1);')
        c.check(pair(pg, 'inst-O', 'P1', {'p': ['P1']}) == 'wappi', 'the owner must reach its own pairing')
        a2 = claim(pg, 'claim.status', 'inst-A', 'P1', paired_now(pg))
        owner = pg.q("select owner_id from bot_profiles where profile_id = 'P1'")[0]['owner_id']
        c.check(owner == 'inst-O', f'owner changed to {owner} ({a2["decide"]})')
        c.check('P1' not in (a2['reload']['p'] or []), f"stranger's token owns P1: {a2['reload']}")
        c.check(a2['bound'] in (None, []), f'bind ran: {a2["bound"]}')

with Case('R1 S2: a legacy profile re-paired BY the claimant through the proxy binds to the claimant (legit recovery)') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P2', 'rc-legacy', 'whatsapp');")
        s = claim(pg, 'claim.start', 'inst-L', 'P2', UNAUTH)
        c.check(s['decide']['claimMethod'] == 'reauth' and 'P2' in s['reload']['c'], f'start {s["decide"]} {s["reload"]}')
        pg.x('select pg_sleep(1.1);')
        c.check(pair(pg, 'inst-L', 'P2', s['reload']) == 'wappi', 'the claimant must be able to pair')
        st = claim(pg, 'claim.status', 'inst-L', 'P2', paired_now(pg))
        c.check('P2' in (st['reload']['p'] or []), f'not bound: {st["decide"]} bound={st["bound"]} reload={st["reload"]}')

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
        owner = pg.q("select owner_id from bot_profiles where profile_id = 'P3'")[0]['owner_id']
        c.check(owner is None and 'P3' not in (a['reload']['p'] or []), f'owner={owner} A={a["decide"]} bound={a["bound"]}')

with Case('R1 S4: a claim older than a day records no pairing and its old pairing never binds; a renewed claim needs a NEW pairing') as c:
    with PgSession(SETUP) as pg:
        pg.x("insert into bot_profiles (profile_id, app_user_id, channel) values ('P4', 'rc-legacy', 'whatsapp');")
        s = claim(pg, 'claim.start', 'inst-L', 'P4', UNAUTH)
        pg.x('select pg_sleep(1.1);')
        pair(pg, 'inst-L', 'P4', s['reload'])
        pg.x("update bot_profiles set claim_pending_at = claim_pending_at - interval '2 days', claim_pair_at = claim_pair_at - interval '2 days' where profile_id = 'P4';")
        c.check(pair(pg, 'inst-L', 'P4', {'c': ['P4']}) == 'refused claim_not_pending', 'an expired claim must not record a pairing')
        old = claim(pg, 'claim.status', 'inst-L', 'P4', paired_now(pg))          # renews the claim; the 2-day-old pairing is before it
        c.check('P4' not in (old['reload']['p'] or []), f'an expired claim\'s pairing bound: {old["decide"]} {old["bound"]}')
        fresh = pg.q("select (claim_pending_at > now() - interval '1 minute') as fresh from bot_profiles where profile_id = 'P4'")[0]['fresh']
        c.check(fresh is True, f'the renewed claim must carry a fresh clock: {fresh}')
        pg.x('select pg_sleep(1.1);')
        c.check(pair(pg, 'inst-L', 'P4', old['reload']) == 'wappi', 'a renewed claim must be able to pair again')
        ok = claim(pg, 'claim.status', 'inst-L', 'P4', paired_now(pg))
        c.check('P4' in (ok['reload']['p'] or []), f'a new pairing after the renewal must bind: {ok["decide"]} {ok["bound"]}')

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
        wrong = claim(pg, 'claim.verify', 'inst-N', 'P8', AUTH_OLD(pg), '000000' if right != '000000' else '111111')
        c.check(not wrong['verify_rows'] and pg.q("select owner_id from bot_profiles where profile_id='P8'")[0]['owner_id'] == 'inst-O', 'a wrong code moved the owner')
        good = claim(pg, 'claim.verify', 'inst-N', 'P8', AUTH_OLD(pg), right)
        c.check('P8' in (good['reload']['p'] or []), f'the right code must transfer: {good}')
        replay = claim(pg, 'claim.verify', 'inst-B', 'P8', AUTH_OLD(pg), right)
        c.check(not replay['verify_rows'] and pg.q("select owner_id from bot_profiles where profile_id='P8'")[0]['owner_id'] == 'inst-N', 'a replayed code moved the owner')
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
        c.check(in_chat and n['sent_code'] is None, f'expected exactly one code in the chat: {a["sent_code"]} / {n["sent_code"]}')
        guess = claim(pg, 'claim.verify', 'inst-A', 'P9', AUTH_OLD(pg), '000000' if in_chat != '000000' else '111111')
        st = claim(pg, 'claim.status', 'inst-A', 'P9', paired_now(pg))
        owner = pg.q("select owner_id from bot_profiles where profile_id='P9'")[0]['owner_id']
        c.check(not guess['verify_rows'] and st['bound'] in (None, []) and owner == 'inst-O', f'the racing claimant moved the owner: {owner}')
        good = claim(pg, 'claim.verify', 'inst-N', 'P9', AUTH_OLD(pg), in_chat)   # the owner reads the code and enters it on N
        c.check('P9' in (good['reload']['p'] or []), f'the owner with the code must win the race: {good}')
        again = claim(pg, 'claim.verify', 'inst-A', 'P9', AUTH_OLD(pg), in_chat)  # the consumed code is worthless afterwards
        c.check(not again['verify_rows'] and pg.q("select owner_id from bot_profiles where profile_id='P9'")[0]['owner_id'] == 'inst-N', 'a consumed code re-bound')

with Case('R1 S10: an unauthenticated claim.* call never reaches Claim Start (no pending claim / row written before the refusal)') as c:
    ins = edges_into(W, 'Claim Start')
    c.check(ins == [('Install Verified?', 0)], f'Claim Start must be reachable only from Install Verified? (true): {ins}')
    if has(W, 'Install Verified?'):
        route = {'op': 'claim.start', 'profileId': 'P10', 'installId': 'ghost'}
        c.check(expr(IF(W, 'Install Verified?'), {'verdict': 'checked', 'install_id': None}, {'Route': route}) is False, 'a refused install passes')
        c.check(expr(IF(W, 'Install Verified?'), {'message': 'db down', 'error': {}}, {'Route': route}) is False, 'a DB fault passes')
        c.check(edges_into(W, 'Install Verified?') == [('Claim Op?', 0)] and ('Install Verified?', 1) in edges_into(W, 'Issue Token'),
                'Install Verified? must sit on Claim Op? true and refuse into Issue Token')

report()
