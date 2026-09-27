#!/usr/bin/env python3
"""R11 (iteration 2026-09-27): a refresh may answer 401 ONLY on a POSITIVE database verdict that the install is
unknown / revoked / proven by a wrong secret. Any other shape — the Postgres node's own error item, the input
passed through when the node fails before its handler (n8n continueRegularOutput), an empty row — is a transient
server fault: 503 `db_unavailable`, and the client must keep its credentials.

Executes the REAL Issue Token / Claim Decide Code bodies and the REAL Verify Install SQL (staging Postgres, temp
tables inside BEGIN … ROLLBACK) from the canonical crAuthSession001-Auth_Session.json.
"""
from harness import Case, bind, code, node, report, sql_json, wf

W = wf('crAuthSession001-Auth_Session.json')
ROUTE_REFRESH = {'op': 'refresh', 'invalid': False, 'statusCode': 200, 'error': None, 'installId': 'inst-A', 'installSecret': '',
                 'secretHash': 'h-A', 'appUserId': '', 'platform': 'test', 'appVersion': 'e2e', 'profileId': '', 'channel': 'whatsapp',
                 'code': '', 'nowSec': 1_900_000_000}
PG_ERROR = {'message': 'Connection terminated unexpectedly',
            'error': {'message': 'Connection terminated unexpectedly', 'name': 'NodeOperationError'}}
OK_ROW = {'verdict': 'checked', 'install_id': 'inst-A', 'app_user_id': None, 'token_version': 1, 'p': ['wa-1'], 'w': [], 'c': []}


def issue(row, route=ROUTE_REFRESH):
    return code(W, 'Issue Token', {'json': row}, named={'Route': route})[0]['json']


for label, row in (('the Postgres node\'s error item', PG_ERROR),
                   ('the Route item passed through when the node failed before its handler', dict(ROUTE_REFRESH)),
                   ('an empty row (no verdict at all)', {})):
    with Case(f'R11 refresh + {label} -> 503 db_unavailable, never 401') as c:
        out = issue(row)
        c.check(out.get('_status') == 503 and out.get('error') == 'db_unavailable' and not out.get('token'), f'got {out}')

with Case('R11 refresh + a POSITIVE verdict (checked, no install) -> 401 install_revoked') as c:
    out = issue({'verdict': 'checked', 'install_id': None, 'app_user_id': None})
    c.check(out.get('_status') == 401 and out.get('error') == 'install_revoked', f'got {out}')

with Case('R11 refresh + a verdict row carrying an error is still a fault (503)') as c:
    out = issue({'verdict': 'checked', 'install_id': None, **PG_ERROR})
    c.check(out.get('_status') == 503, f'got {out}')

with Case('R11 refresh + the install row -> 200 and a token') as c:
    out = issue(OK_ROW)
    c.check(out.get('_status') == 200 and out.get('success') is True and out.get('token', '').startswith('v1.'), f'got {out}')

with Case('R11 bootstrap keeps its own codes: DB error -> 500, no row (the cap) -> 429') as c:
    boot = dict(ROUTE_REFRESH, op='bootstrap', installSecret='s')
    a, b = issue(PG_ERROR, boot), issue({}, boot)
    c.check(a.get('_status') == 500 and b.get('_status') == 429 and b.get('error') == 'rate_limited', f'{a} / {b}')

with Case('R11 claim.* with a failed install lookup -> 503 db_unavailable (Claim Decide), a positive verdict -> 401') as c:
    route = dict(ROUTE_REFRESH, op='claim.status', profileId='p-1')
    status = {'statusCode': 200, 'body': {'authorized': False}}
    d1 = code(W, 'Claim Decide', {'json': status}, named={'Route': route, 'Verify Install': PG_ERROR, 'Claim Start': {}})[0]['json']
    d2 = code(W, 'Claim Decide', {'json': status}, named={'Route': route, 'Verify Install': {'verdict': 'checked', 'install_id': None},
                                                          'Claim Start': {}})[0]['json']
    c.check(d1.get('statusCode') == 503 and d1.get('claimError') == 'db_unavailable', f'db fault: {d1}')
    c.check(d2.get('statusCode') == 401 and d2.get('claimError') == 'install_revoked', f'revoked: {d2}')

# ---- the SQL must deliver the verdict: ONE row for every outcome, install_id NULL when the install is not valid ----
SETUP = """create temp table installs (like public.installs including all);
create temp table bot_profiles (like public.bot_profiles including all);
insert into installs (install_id, secret_hash, app_user_id) values ('inst-ok', 'h-ok', null), ('inst-rev', 'h-rev', null);
update installs set revoked_at = now() where install_id = 'inst-rev';
insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('wa-1', 'rc-1', 'whatsapp', 'inst-ok');"""
VERIFY = node(W, 'Verify Install')['parameters']['query']


def verify_rows(install, secret_hash):
    return sql_json(f"create temp table r as {bind(VERIFY.rstrip().rstrip(';'), [install, secret_hash, ''])};\n"
                    "select coalesce(json_agg(row_to_json(r)), '[]'::json) from r;", SETUP)


for label, args in (('a wrong secret', ('inst-ok', 'nope')), ('a revoked install', ('inst-rev', 'h-rev')), ('an unknown install', ('ghost', 'h'))):
    with Case(f'R11 Verify Install SQL: {label} -> exactly one row, verdict checked, install_id NULL') as c:
        rows = verify_rows(*args)
        c.check(len(rows) == 1 and rows[0].get('verdict') == 'checked' and rows[0].get('install_id') is None, f'rows {rows}')

with Case('R11 Verify Install SQL: the valid install -> one row with its ownership (and last_seen touched)') as c:
    rows = verify_rows('inst-ok', 'h-ok')
    c.check(len(rows) == 1 and rows[0].get('install_id') == 'inst-ok' and rows[0].get('p') == ['wa-1'] and rows[0].get('verdict') == 'checked',
            f'rows {rows}')

report()
