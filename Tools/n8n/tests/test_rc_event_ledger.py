#!/usr/bin/env python3
"""A1 «защита платящих» (2026-10-14): the RevenueCat webhook changes the books ONCE per event id.

RevenueCat delivers at least once — an answer it did not get (a timeout, a proxy) is delivered again minutes later —
and Upsert Subscriber's `topup_balance + <the event's top-up>` is not idempotent: a replay of a committed
NON_RENEWING_PURCHASE credited 500 dialogs twice (spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-
schedule-design.md §2.3, §3 A1). The fix: `Dedup Gate` (Webhook → Dedup Gate → If Duplicate? → Map Event) only READS
rc_event_ledger — an id found there is acknowledged by Respond 200 and changes nothing; the id is WRITTEN only by
Upsert Subscriber's CTE `ledger`, inside the same statement (= transaction) as the event's effect, so a failed upsert
leaves no row and RevenueCat's retry still processes the event. The read is fail-open: a read error lets the event
through. An event without an id passes unrecorded; the branches without Upsert (TRANSFER, the no-op) write nothing.

What runs — no n8n server, no network, never production: the REAL graph of the canonical RevenueCat Events JSON,
walked edge by edge from Webhook to the Respond node it reaches. Code bodies through harness.code; every If through
n8n's OWN filter engine (n8n-workflow executeFilter — the staging install = prod's version); every Postgres node's own
queryReplacement through n8n's expression engine with the executeQuery operation's value rules (an `undefined` is
SKIPPED, so the following parameters shift; an object is JSON-stringified), bound into the node's own query
(pg-promise `partial`: a placeholder without a value stays and Postgres refuses it) and run in the local staging
Postgres on session temp tables (`like public.X`, autocommit, a real clock). A node's real SQL error becomes what n8n
hands on for its onError setting (regular / error output, the input item for an error thrown outside the query
runner, or no answer at all). Faults are real Postgres errors, never mocks.

The verifier half (verify-telegram-parity.py `check_rc_event_ledger`) is exercised at the end: the canonical set
passes, and each one-mutation copy of RevenueCat Events (a writing gate, the CTE gone, a disabled node, Map Event
reading `$json`, …) fails it, naming the file.

    python3 Tools/n8n/tests/test_rc_event_ledger.py
"""
import contextlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from harness import N8N, PG, WF, Case, PgSession, code, module, node, report, sql_literal, sql_run, wf

RC_FILE = 'ZGYr6srzS3rSSXHp-RevenueCat_Events.json'
W = wf(RC_FILE)
PARITY = N8N / 'verify-telegram-parity.py'
parity = module('verify-telegram-parity.py')    # its statement splitter mirrors n8n's own (Postgres v2 utils.js)
MIGRATION = N8N / 'sql' / '2026-10-14-rc-event-ledger.sql'

WEBHOOK, CODE, IF, POSTGRES, RESPOND = ('n8n-nodes-base.webhook', 'n8n-nodes-base.code', 'n8n-nodes-base.if',
                                        'n8n-nodes-base.postgres', 'n8n-nodes-base.respondToWebhook')
GATE, DUP_IF = 'Dedup Gate', 'If Duplicate?'

# ---------------------------------------------------------------------------------------------------- n8n's engine
_ENGINE = r"""const path=require('path'), os=require('os');
const N = process.env.N8N_MODULES || path.join(os.homedir(), '.n8n-staging/app/node_modules');
const {evaluateExpression,setErrorHandler}=require(N + '/n8n-workflow/dist/cjs/expression-evaluator-proxy.js');
const {executeFilter}=require(N + '/n8n-workflow/dist/cjs/node-parameters/filter-parameter.js');
setErrorHandler(e=>{throw e;});
function handle(v) {
  const named=v.named||{};
  const $=(n)=>{ if(!(n in named)) throw new Error('Referenced node is unexecuted: '+n);
    const items=(Array.isArray(named[n])?named[n]:[named[n]]).map(j=>({json:j}));
    return {first:()=>items[0], all:()=>items, item:items[0], last:()=>items[items.length-1], isExecuted:true}; };
  const data={$json:v.json, $, $execution:{id:'synthetic-exec'}, String, Array, RegExp, Math, Number, JSON, Boolean, Object, Date};
  const ev=(e)=>evaluateExpression(String(e).replace(/^=/, ''), data);
  try {
    if (v.kind === 'params') {
      // executeQuery.operation.js (n8n-nodes-base 2.36, node typeVersion >= 2.5), the array branch: an undefined value is
      // SKIPPED (the following parameters shift), an object is JSON-stringified, anything else goes to pg-promise as is
      const r = ev(v.expr);
      if (!Array.isArray(r)) throw new Error('queryReplacement did not evaluate to an array: ' + JSON.stringify(r));
      const values = [], skipped = [];
      r.forEach((x, i) => { if (x === undefined) { skipped.push(i); return; }
                            values.push(typeof x === 'object' && x !== null ? JSON.stringify(x) : x); });
      return {ok: true, values, skipped};
    }
    if (v.kind === 'if') {
      const f = v.filter, res = (x) => (typeof x === 'string' && x.startsWith('=') ? ev(x) : x);
      const conditions = f.conditions.map(c => Object.assign({}, c, {leftValue: res(c.leftValue), rightValue: res(c.rightValue)}));
      return {ok: true, pass: executeFilter(Object.assign({}, f, {conditions}))};
    }
    return {ok: true, value: ev(v.expr)};
  } catch (e) { return {ok: false, error: String((e && e.message) || e)}; }
}
// one request per line in, one answer per line out — the engine is loaded ONCE (≈ 0.6 s) for the whole file
require('readline').createInterface({input: process.stdin}).on('line', (line) => {
  process.stdout.write(JSON.stringify(handle(JSON.parse(line)), (k, x) => (x === undefined ? null : x)) + '\n');
});"""


class _Engine:
    """ONE node process holding n8n's expression and filter engine (n8n-workflow), JSON lines over stdin/stdout."""

    def __init__(self):
        self.p = None

    def __call__(self, kind, **kw):
        if self.p is None or self.p.poll() is not None:
            self.p = subprocess.Popen(['node', '-e', _ENGINE], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                      stderr=subprocess.PIPE, text=True, bufsize=1)
        self.p.stdin.write(json.dumps(dict(kind=kind, **kw)) + '\n')
        self.p.stdin.flush()
        line = self.p.stdout.readline()
        if not line:
            raise RuntimeError('n8n engine died: ' + self.p.stderr.read()[-1500:])
        return json.loads(line)


engine = _Engine()


def pg_bind(query, values):
    """harness.bind as pg-promise does it with `partial: true`: a placeholder without a value stays in the text (Postgres
    then refuses it) — so a skipped undefined fails here the way it fails on n8n, instead of an IndexError."""
    return re.sub(r'\$([1-9]\d{0,16})(?!\d)',
                  lambda m: sql_literal(values[int(m[1]) - 1]) if int(m[1]) <= len(values) else m[0], query)


class Session(PgSession):
    """harness.PgSession whose psql SURVIVES a failed statement (ON_ERROR_STOP off): a node's real SQL error reaches the
    walker as RuntimeError and the scenario goes on in the same session (autocommit: the failed statement rolled back
    alone — exactly the n8n Postgres node's single-statement query)."""

    def __init__(self, setup=''):
        self.p = subprocess.Popen([str(PG), 'psql', '-X', '-q', '-t', '-A', '-P', 'pager=off', '-v', 'ON_ERROR_STOP=0'],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)
        self.n = 0
        self.x('set search_path = pg_temp, public;\n' + setup)


SETUP = """set timezone = 'UTC';
create temp table subscribers (like public.subscribers including all);
create temp table dialog_counts (like public.dialog_counts including all);
create temp table rc_event_ledger (like public.rc_event_ledger including all);"""


# ---------------------------------------------------------------------------------------------------- the walker
class Halt(Exception):
    """The execution ends in an error: no Respond node answers (n8n's own 500 to RevenueCat, which retries)."""


class Run:
    def __init__(self):
        self.answer, self.trail, self.named, self.halt = None, [], {}, None

    def __repr__(self):
        return f'answer={self.answer} trail={self.trail} halt={self.halt}'


def _pg_node(pg, n, items, named):
    if len(items) != 1:
        raise NotImplementedError(f"{n['name']}: {len(items)} input items (the RC graph carries one)")
    cont = n.get('onError') in ('continueRegularOutput', 'continueErrorOutput') or n.get('continueOnFail') is True
    p = engine('params', expr=(n['parameters'].get('options') or {}).get('queryReplacement', '={{ [] }}'),
               json=items[0], named=named)
    if not p['ok']:                   # thrown OUTSIDE the query runner: a continuing node hands its INPUT on (output 0)
        if cont:
            return [items]
        raise Halt(f"{n['name']}: {p['error']}")
    q = n['parameters']['query']
    stmts = parity._pg_statements(q)
    is_select = bool(stmts) and all(parity._top_level_command(s) == 'SELECT' for s in stmts)
    if not is_select and re.search(r'\breturning\b', parity.sql_code(q).split(')')[-1], re.I):
        raise NotImplementedError(f"{n['name']}: a top-level RETURNING on a non-SELECT is not modelled")
    sql = pg_bind(q, p['values'])
    try:
        rows = pg.q(sql) if is_select else (pg.x(sql), [])[1]
    except RuntimeError as e:         # the query runner's own error item (continueOnFail)
        if not cont:
            raise Halt(f"{n['name']}: {str(e).splitlines()[0]}")
        err = {'message': str(e).splitlines()[0][:300], 'error': {'message': str(e)[:300]}}
        return [[], [err]] if n.get('onError') == 'continueErrorOutput' else [[err]]
    if not rows and not is_select:    # typeVersion >= 2.3: a statement that is not a SELECT and returned nothing
        rows = [{'success': True}]
    return [rows]


def _step(pg, n, items, named, wh, run):
    t = n['type']
    if n.get('disabled'):             # n8n skips a disabled node and hands its input on
        return [items]
    if t == WEBHOOK:
        return [[wh]]
    if t == CODE:
        res = code(W, n['name'], item={'json': items[0]}, items=[{'json': x} for x in items], named=named, strict=True)
        return [[r.get('json', {}) for r in res]]
    if t == POSTGRES:
        return _pg_node(pg, n, items, named)
    if t == IF:
        yes, no = [], []
        for it in items:
            r = engine('if', filter=n['parameters']['conditions'], json=it, named=named)
            if not r['ok']:
                raise Halt(f"{n['name']}: {r['error']}")
            (yes if r['pass'] else no).append(it)
        return [yes, no]
    if t == RESPOND:
        p = n['parameters']
        body = engine('expr', expr=p.get('responseBody', '={{ {} }}'), json=items[0], named=named)
        if not body['ok']:
            raise Halt(f"{n['name']}: {body['error']}")
        if run.answer is None:        # the first Respond answers; n8n ignores any later one
            run.answer = (int((p.get('options') or {}).get('responseCode', 200)), body['value'])
        return [items]
    raise NotImplementedError(f"{n['name']}: node type {t}")


def deliver(pg, body, faults=None):
    """One RevenueCat delivery through the canonical graph. `faults` = {node name: (sql before it, sql after it)} — a
    real Postgres condition that holds only while that node runs."""
    wh = {'headers': {'content-type': 'application/json', 'authorization': 'Bearer synthetic'}, 'params': {}, 'query': {},
          'body': body, 'webhookUrl': 'http://localhost:5678/webhook/RevenueCatEvent', 'executionMode': 'production'}
    run, queue = Run(), [('Webhook', [wh])]
    while queue:
        name, items = queue.pop(0)
        n = node(W, name)
        run.trail.append(name)
        before, after = (faults or {}).get(name, (None, None))
        if before:
            pg.x(before)
        try:
            outs = _step(pg, n, items, run.named, wh, run)
        except Halt as e:
            run.halt = str(e)
            return run
        finally:
            if after:
                pg.x(after)
        if n.get('alwaysOutputData') and not (outs and outs[0]):
            outs = [[{}]] + list(outs[1:])
        run.named[name] = next((o for o in outs if o), [{}])
        branches = (W['connections'].get(name) or {}).get('main') or []
        for idx, out in enumerate(outs):
            if out and idx < len(branches):
                queue.extend((e['node'], out) for e in branches[idx] or [])
    return run


# ---------------------------------------------------------------------------------------------------- fixtures
U = '$RCAnonymousID:' + 'a1a1a1a1b2b2b2b2' * 2          # the paying owner
OLD = '$RCAnonymousID:' + 'c3c3c3c3d4d4d4d4' * 2        # the same owner before a reinstall (an alias)
PERIOD_MS = 4102444800000                                # 2100-01-01T00:00:00Z


def ev(type_, eid='evt-a1-0001', **kw):
    e = {'type': type_, 'app_user_id': U, 'original_app_user_id': U, 'aliases': [U], 'store': 'APP_STORE',
         'environment': 'SANDBOX', 'event_timestamp_ms': 1760400000000}
    if eid is not None:
        e['id'] = eid
    e.update(kw)
    return {'api_version': '1.0', 'event': e}


def purchase(eid='evt-a1-0001', **kw):
    return ev('INITIAL_PURCHASE', eid, entitlement_ids=['tier_business'], product_id='sub.business.month',
              expiration_at_ms=PERIOD_MS, **kw)


def topup(eid='evt-a1-0002', **kw):
    return ev('NON_RENEWING_PURCHASE', eid, product_id='topup.dialogs.500', **kw)


def rows(pg, uid=None):
    """Every subscribers row (or one) with its physical version: an unchanged xmin = not rewritten at all."""
    where = f" where app_user_id = {sql_literal(uid)}" if uid else ''
    return pg.q("select xmin::text as ver, app_user_id, plan, status, topup_balance, product_id, "
                "(extract(epoch from current_period_end) * 1000)::bigint as period_ms, updated_at::text as updated_at "
                f"from subscribers{where} order by app_user_id")


def ledger(pg):
    return pg.q("select event_id, xmin::text as ver, received_at::text as received_at from rc_event_ledger order by event_id")


def ok_answer(run):
    return run.answer is not None and run.answer[0] == 200 and (run.answer[1] or {}).get('success') is True


# ---------------------------------------------------------------------------------------------------- L0: the table
with Case('A1 L0: the migration is applied as written — rc_event_ledger(event_id text PK, received_at timestamptz not null '
          'default now()), RLS on, no anon/authenticated grants; pg.sh migrates it') as c:
    shape = sql_run("select json_agg(json_build_object('c', column_name, 't', data_type, 'n', is_nullable, 'd', column_default) "
                    "order by ordinal_position) from information_schema.columns where table_schema = 'public' "
                    "and table_name = 'rc_event_ledger';").strip()
    cols = json.loads(shape.splitlines()[-1]) if shape else None
    c.check(cols == [{'c': 'event_id', 't': 'text', 'n': 'NO', 'd': None},
                     {'c': 'received_at', 't': 'timestamp with time zone', 'n': 'NO', 'd': 'now()'}],
            f'public.rc_event_ledger columns: {cols} — run Tools/n8n/staging/pg.sh migrate')
    meta = [l for l in sql_run(
        "select (select relrowsecurity from pg_class where oid = 'public.rc_event_ledger'::regclass)::text "
        "|| '|' || (select count(*) from pg_constraint where conrelid = 'public.rc_event_ledger'::regclass and contype = 'p' "
        "and conkey = array[(select attnum from pg_attribute where attrelid = 'public.rc_event_ledger'::regclass "
        "and attname = 'event_id')]::int2[]) || '|' || (select count(*) from information_schema.role_table_grants "
        "where table_name = 'rc_event_ledger' and grantee in ('anon', 'authenticated'));").splitlines() if l.strip()]
    c.check(meta[-1:] == ['true|1|0'], f'rls|pk on event_id|client grants = {meta[-1:]} (expected true|1|0)')
    c.check(MIGRATION.name in (N8N / 'staging' / 'pg.sh').read_text(encoding='utf-8'),
            f'staging/pg.sh does not migrate {MIGRATION.name}')

# ---------------------------------------------------------------------------------------------------- L1–L3: replays
with Case('A1 L1: the first INITIAL_PURCHASE → 200; the subscriber gets business/active/the period; ONE ledger row (the id)') as c:
    with Session(SETUP) as pg:
        r = deliver(pg, purchase('evt-l1'))
        c.check(ok_answer(r), f'{r}')
        got = rows(pg)
        c.check([(x['app_user_id'], x['plan'], x['status'], x['period_ms'], x['product_id'], x['topup_balance']) for x in got]
                == [(U, 'business', 'active', PERIOD_MS, 'sub.business.month', 0)], f'subscribers {got}')
        c.check([x['event_id'] for x in ledger(pg)] == ['evt-l1'], f'ledger {ledger(pg)} — trail {r.trail}')

with Case('A1 L2: a VERBATIM replay → 200 from the gate (If Duplicate? → Respond 200; Map Event never runs); the subscriber '
          'row is byte-identical (same xmin = not rewritten: plan, period, balance, updated_at); still ONE ledger row') as c:
    with Session(SETUP) as pg:
        first = deliver(pg, purchase('evt-l2'))
        before, led = rows(pg), ledger(pg)
        again = deliver(pg, purchase('evt-l2'))
        c.check(ok_answer(first) and ok_answer(again), f'first {first} / replay {again}')
        c.check(rows(pg) == before, f'the replay rewrote the row: {before} -> {rows(pg)}')
        c.check(ledger(pg) == led and len(led) == 1, f'ledger {led} -> {ledger(pg)}')
        c.check(again.trail == ['Webhook', GATE, DUP_IF, 'Respond 200'], f'replay trail {again.trail}')

with Case('A1 L3: NON_RENEWING_PURCHASE (top-up 500) delivered twice with one id → +500 exactly ONCE; ONE ledger row; a second '
          'top-up with its OWN id → +500 again') as c:
    with Session(SETUP) as pg:
        pg.x(f"insert into subscribers (app_user_id, plan, status, topup_balance) values ({sql_literal(U)}, 'business', 'active', 40);")
        a, b = deliver(pg, topup('evt-l3')), deliver(pg, topup('evt-l3'))
        after_two = rows(pg, U)[0]['topup_balance']
        d = deliver(pg, topup('evt-l3-next'))
        c.check(ok_answer(a) and ok_answer(b) and ok_answer(d), f'{a} / {b} / {d}')
        c.check(after_two == 540, f'balance after the same top-up twice: {after_two} (expected 40 + 500 once)')
        c.check(rows(pg, U)[0]['topup_balance'] == 1040, f'a NEW top-up must still credit: {rows(pg, U)}')
        c.check([x['event_id'] for x in ledger(pg)] == ['evt-l3', 'evt-l3-next'], f'ledger {ledger(pg)}')

# ---------------------------------------------------------------------------------------------------- L4–L5: boundaries
with Case('A1 L4: an event WITHOUT an id is processed (the top-up credited) and leaves NO ledger row (nothing to dedup on)') as c:
    with Session(SETUP) as pg:
        r = deliver(pg, topup(None))
        c.check(ok_answer(r) and rows(pg, U) and rows(pg, U)[0]['topup_balance'] == 500, f'{r} / {rows(pg)}')
        c.check(ledger(pg) == [], f'ledger {ledger(pg)}')
        e = deliver(pg, topup(''))                          # an empty id is no id either
        c.check(ok_answer(e) and rows(pg, U)[0]['topup_balance'] == 1000 and ledger(pg) == [], f'{e} / {rows(pg)} / {ledger(pg)}')

with Case('A1 L5: CANCELLATION (the no-op branch: no Upsert) → 200 noop; nothing written, no ledger row') as c:
    with Session(SETUP) as pg:
        pg.x(f"insert into subscribers (app_user_id, plan, status, topup_balance) values ({sql_literal(U)}, 'business', 'active', 7);")
        before = rows(pg)
        r = deliver(pg, ev('CANCELLATION', 'evt-l5'))
        c.check(r.answer is not None and r.answer[0] == 200 and (r.answer[1] or {}).get('noop') is True, f'{r}')
        c.check(rows(pg) == before and ledger(pg) == [], f'subscribers {rows(pg)} ledger {ledger(pg)}')

# ---------------------------------------------------------------------------------------------------- L6–L7: faults
UNREADABLE = ("alter table pg_temp.rc_event_ledger rename to rc_event_ledger_unreadable; set search_path = pg_temp;",
              "alter table pg_temp.rc_event_ledger_unreadable rename to rc_event_ledger; set search_path = pg_temp, public;")

with Case('A1 L6: the gate\'s READ fails (a real Postgres error: the ledger is unreadable for its statement) → fail-open: the '
          'event is processed (top-up credited) and Upsert records the id') as c:
    with Session(SETUP) as pg:
        r = deliver(pg, topup('evt-l6'), faults={GATE: UNREADABLE})
        gate_out = r.named.get(GATE, [{}])[0]
        c.check('does not exist' in str(gate_out.get('message', '')), f'premise: the gate read must really fail: {gate_out}')
        c.check(ok_answer(r) and rows(pg, U) and rows(pg, U)[0]['topup_balance'] == 500, f'{r} / {rows(pg)}')
        c.check([x['event_id'] for x in ledger(pg)] == ['evt-l6'], f'ledger {ledger(pg)}')
        c.check('Map Event' in r.trail and 'Upsert Subscriber' in r.trail, f'trail {r.trail}')

UPSERT_FAULT = f"alter table subscribers add constraint a1_upsert_fault check (app_user_id <> {sql_literal(U)}) not valid;"
with Case('A1 L7: Upsert fails (a real CHECK violation on this subscriber) → 500 upsert_failed and NO ledger row (rolled back '
          'with the upsert); RevenueCat\'s retry once the fault is gone credits exactly once; a third delivery is a duplicate') as c:
    with Session(SETUP) as pg:
        pg.x(f"insert into subscribers (app_user_id, plan, status, topup_balance) values ({sql_literal(U)}, 'business', 'active', 0);")
        pg.x(UPSERT_FAULT)
        failed = deliver(pg, topup('evt-l7'))
        c.check(failed.answer is not None and failed.answer[0] == 500 and (failed.answer[1] or {}).get('error') == 'upsert_failed',
                f'the failing upsert must answer 500 upsert_failed: {failed}')
        c.check(ledger(pg) == [] and rows(pg, U)[0]['topup_balance'] == 0, f'after the failure: ledger {ledger(pg)} {rows(pg)}')
        pg.x("alter table subscribers drop constraint a1_upsert_fault;")
        retry, dup = deliver(pg, topup('evt-l7')), deliver(pg, topup('evt-l7'))
        c.check(ok_answer(retry) and ok_answer(dup) and rows(pg, U)[0]['topup_balance'] == 500, f'{retry} / {dup} / {rows(pg)}')
        c.check([x['event_id'] for x in ledger(pg)] == ['evt-l7'] and dup.trail[-2:] == [DUP_IF, 'Respond 200'],
                f'ledger {ledger(pg)} dup trail {dup.trail}')

# ---------------------------------------------------------------------------------------------------- L8–L9: other paths
with Case('A1 L8: the ALIAS path (Consolidate Aliases carries the id through its select) — the purchase after a reinstall moves '
          'the alias balance, sets the plan and records ONE ledger row; the replay rewrites NO row (alias, live, dialog_counts)') as c:
    with Session(SETUP) as pg:
        pg.x(f"insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end) values "
             f"({sql_literal(OLD)}, 'start', 'active', 300, now() + interval '20 days');"
             f"insert into dialog_counts (app_user_id, chat_id, d) values ({sql_literal(OLD)}, 'c1', (now() at time zone 'Asia/Almaty')::date);")
        body = purchase('evt-l8', aliases=[U, OLD], original_app_user_id=OLD)
        r = deliver(pg, body)
        got = {x['app_user_id']: (x['plan'], x['status'], x['topup_balance']) for x in rows(pg)}
        c.check(ok_answer(r) and 'Consolidate Aliases' in r.trail, f'{r}')
        c.check(got == {U: ('business', 'active', 300), OLD: ('start', 'expired', 0)}, f'subscribers {got}')
        c.check(r.named.get('Consolidate Aliases', [{}])[0].get('event_id') == 'evt-l8',
                f"Consolidate Aliases must hand the id on: {r.named.get('Consolidate Aliases')}")
        c.check([x['event_id'] for x in ledger(pg)] == ['evt-l8'], f'ledger {ledger(pg)}')
        before, dc = rows(pg), pg.q('select xmin::text as ver, * from dialog_counts order by app_user_id')
        again = deliver(pg, body)
        c.check(ok_answer(again) and rows(pg) == before and pg.q('select xmin::text as ver, * from dialog_counts order by app_user_id') == dc,
                f'the replay changed something: {again} / {rows(pg)}')

with Case('A1 L9: TRANSFER (a branch without Upsert) moves the subscription, hands the id on in its result and writes NO ledger '
          'row; its replay (not deduped — idempotent by its own design) leaves the balance as it was') as c:
    with Session(SETUP) as pg:
        pg.x(f"insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end) values "
             f"({sql_literal(OLD)}, 'business', 'active', 200, now() + interval '20 days');")
        body = {'api_version': '1.0', 'event': {'id': 'evt-l9', 'type': 'TRANSFER', 'transferred_from': [OLD], 'transferred_to': [U],
                                                 'store': 'APP_STORE', 'environment': 'SANDBOX'}}
        r = deliver(pg, body)
        c.check(ok_answer(r) and 'Transfer Subscriber' in r.trail, f'{r}')
        c.check(r.named.get('Transfer Subscriber', [{}])[0].get('event_id') == 'evt-l9',
                f"Transfer Subscriber must hand the id on: {r.named.get('Transfer Subscriber')}")
        c.check(ledger(pg) == [], f'ledger {ledger(pg)}')
        moved = {x['app_user_id']: (x['plan'], x['status'], x['topup_balance']) for x in rows(pg)}
        again = deliver(pg, body)
        c.check(ok_answer(again) and ledger(pg) == [] and
                {x['app_user_id']: (x['plan'], x['status'], x['topup_balance']) for x in rows(pg)} == moved == {
                    U: ('business', 'active', 200), OLD: ('business', 'expired', 0)}, f'{moved} -> {rows(pg)}')

# ---------------------------------------------------------------------------------------------------- L10–L12: the gate
SHAPES = {
    'wrapped (RevenueCat)': {'api_version': '1.0', 'event': {'id': 'evt-w', 'type': 'RENEWAL', 'app_user_id': U}},
    'unwrapped body': {'id': 'evt-flat', 'type': 'RENEWAL', 'app_user_id': U},
    'event null, id on top': {'event': None, 'id': 'evt-top', 'type': 'RENEWAL', 'app_user_id': U},
    'no id': {'event': {'type': 'RENEWAL', 'app_user_id': U}},
    'empty id': {'event': {'id': '', 'type': 'RENEWAL', 'app_user_id': U}},
    'numeric id': {'event': {'id': 12345, 'type': 'RENEWAL', 'app_user_id': U}},
    'object id': {'event': {'id': {'x': 1}, 'type': 'RENEWAL', 'app_user_id': U}},
    'TRANSFER': {'event': {'id': 'evt-t', 'type': 'TRANSFER', 'transferred_from': [OLD], 'transferred_to': [U]}},
}
with Case('A1 L10: the id the gate looks up IS the id Map Event hands on (one source, one rule: a non-empty string), for every '
          'body shape — and the gate\'s queryReplacement is the array form with no value ever skipped') as c:
    gate = node(W, GATE)
    raw = gate['parameters']['options']['queryReplacement']
    c.check(raw.startswith('={{ [') and raw.endswith('] }}'), f'not the `={{{{ [ … ] }}}}` array form: {raw}')
    for label, body in SHAPES.items():
        wh = {'body': body, 'headers': {}, 'params': {}, 'query': {}}
        p = engine('params', expr=raw, json=wh, named={'Webhook': wh})
        m = code(W, 'Map Event', item={'json': {}}, named={'Webhook': wh}, strict=True)
        mapped = (m[0]['json'] if m else {}).get('event_id')
        c.check(p['ok'] and p['skipped'] == [] and len(p['values']) == 1, f'{label}: gate params {p}')
        c.check(p['ok'] and p['values'][0] == mapped, f'{label}: the gate looks up {p.get("values")}, Map Event hands on {mapped!r}')

with Case('A1 L11: Dedup Gate only READS — its own statement, bound, runs inside a READ ONLY transaction against the real table') as c:
    gate = node(W, GATE)
    p = engine('params', expr=gate['parameters']['options']['queryReplacement'], json={}, named={'Webhook': {'body': purchase('evt-ro')}})
    try:
        out = sql_run('set transaction read only;\n' + pg_bind(gate['parameters']['query'], p['values']) + '\nselect 1;')
        c.check(out.strip().splitlines()[-1:] == ['1'], f'unexpected output {out!r}')
    except RuntimeError as e:
        c.check(False, f'the gate statement is not read-only: {str(e)[-300:]}')

with Case('A1 L12: If Duplicate? keys on a POSITIVE field — only a ledger row with a non-empty event_id is a duplicate; every '
          'other shape the gate can hand on (no row, a query error, the webhook item after a thrown error) goes to Map Event') as c:
    cond = node(W, DUP_IF)['parameters']['conditions']
    shapes = {
        'no row (alwaysOutputData)': ({}, False),
        'query error (continueRegularOutput)': ({'message': 'relation "rc_event_ledger" does not exist', 'error': {'message': 'x'}}, False),
        'thrown error (the webhook item passes on)': ({'headers': {}, 'params': {}, 'query': {}, 'body': purchase('evt-x')}, False),
        'non-SELECT marker': ({'success': True}, False),
        'empty id row': ({'event_id': ''}, False),
        'null id row': ({'event_id': None}, False),
        'the ledger row': ({'event_id': 'evt-x'}, True),
    }
    for label, (j, want) in shapes.items():
        r = engine('if', filter=cond, json=j, named={})
        c.check(r['ok'] and r['pass'] is want, f'{label}: {r} (expected {want})')
    edges = (W['connections'].get(DUP_IF) or {}).get('main') or []
    c.check([[e['node'] for e in b] for b in edges] == [['Respond 200'], ['Map Event']], f'If Duplicate? edges {edges}')


# ---------------------------------------------------------------------------------------------------- V: the verifier
def run_parity(*args):
    r = subprocess.run([sys.executable, str(PARITY), *args], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


@contextlib.contextmanager
def canon_copy():
    d = Path(tempfile.mkdtemp(prefix='a1-parity-'))
    try:
        for p in WF.glob('*.json'):
            shutil.copy(p, d / p.name)
        yield d
    finally:
        shutil.rmtree(d)


def nd(w, name):
    return next(n for n in w['nodes'] if n['name'] == name)


def link(*targets):
    return [[{'node': t, 'type': 'main', 'index': 0} for t in targets]]


def m_gate_writes(w):               # the gate records the id itself: a crash before the upsert loses the event
    p = nd(w, GATE)['parameters']
    p['query'] = ("with rec as (insert into rc_event_ledger (event_id) values ($1) on conflict do nothing)\n"
                  "select event_id from rc_event_ledger where event_id = $1;")


def m_gate_insert_statement(w):     # a second, writing statement in the gate
    p = nd(w, GATE)['parameters']
    p['query'] = "insert into rc_event_ledger (event_id) values ($1) on conflict do nothing;\n" + p['query']


def m_ledger_cte_removed(w):        # Upsert no longer records: every replay credits again
    p = nd(w, 'Upsert Subscriber')['parameters']
    q = p['query']
    p['query'] = q[q.index('insert into subscribers'):]


def m_ledger_separate_statement(w):  # recorded in a SECOND statement: no longer the upsert's own transaction guarantee
    p = nd(w, 'Upsert Subscriber')['parameters']
    q = p['query']
    p['query'] = (q[q.index('insert into subscribers'):].rstrip() + "\ninsert into rc_event_ledger (event_id) select $7 "
                  "where nullif($7, '') is not null on conflict do nothing;")


def m_ledger_unguarded(w):          # an empty id recorded: every id-less event after it collides on ''
    p = nd(w, 'Upsert Subscriber')['parameters']
    p['query'] = re.sub(r"where nullif\([^)]*\) is not null", '', p['query'])


def m_gate_disabled(w):
    nd(w, GATE)['disabled'] = True


def m_if_disabled(w):
    nd(w, DUP_IF)['disabled'] = True


def m_map_event_reads_json(w):      # after a Postgres node $json is the gate's row: the payload is gone
    p = nd(w, 'Map Event')['parameters']
    p['jsCode'] = re.sub(r"\$\('Webhook'\)\.first\(\)\.json\.body", '$json.body', p['jsCode'])


def m_gate_bypassed(w):             # Webhook straight into Map Event (the pre-A1 wiring)
    w['connections']['Webhook'] = {'main': link('Map Event')}


def m_branches_swapped(w):          # a NEW event acknowledged and dropped, a duplicate processed
    w['connections'][DUP_IF] = {'main': link('Map Event') + link('Respond 200')}


def m_gate_no_always_output(w):     # zero rows (every NEW event) → no item → nothing runs, no answer
    nd(w, GATE).pop('alwaysOutputData', None)


def m_gate_fail_closed(w):          # a read error stops the execution: the event is never processed
    nd(w, GATE).pop('onError', None)


def m_gate_error_output(w):         # continueErrorOutput: a read error lands on an unconnected output
    nd(w, GATE)['onError'] = 'continueErrorOutput'


def m_if_negative_field(w):         # «the node continued» instead of a positive field of the row
    c = nd(w, DUP_IF)['parameters']['conditions']['conditions'][0]
    c['leftValue'] = '={{ $json.message }}'
    c['operator'] = {'type': 'string', 'operation': 'empty', 'singleValue': True}


def m_consolidate_drops_id(w):      # the alias path never records: its replays credit again
    p = nd(w, 'Consolidate Aliases')['parameters']
    p['query'] = re.sub(r",\s*\$8::text as event_id", '', p['query'])


def m_upsert_param_dropped(w):      # Upsert reads no id: nothing is ever recorded
    o = nd(w, 'Upsert Subscriber')['parameters']['options']
    o['queryReplacement'] = o['queryReplacement'].replace(', ($json.event_id ?? null)', '')


MUTATIONS = [m_gate_writes, m_gate_insert_statement, m_ledger_cte_removed, m_ledger_separate_statement, m_ledger_unguarded,
             m_gate_disabled, m_if_disabled, m_map_event_reads_json, m_gate_bypassed, m_branches_swapped,
             m_gate_no_always_output, m_gate_fail_closed, m_gate_error_output, m_if_negative_field, m_consolidate_drops_id,
             m_upsert_param_dropped]

CONTROL_RC, CONTROL_OUT = run_parity()
with Case('A1 V0: the canonical set passes verify-telegram-parity (exit 0) and the RevenueCat ledger check reports OK') as c:
    c.check(CONTROL_RC == 0 and 'ALL PARITY ASSERTS PASSED' in CONTROL_OUT, f'exit {CONTROL_RC}:\n{CONTROL_OUT[-700:]}')
    c.check(f'OK  {RC_FILE} (idempotency ledger' in CONTROL_OUT, f'no ledger check in the verifier output:\n{CONTROL_OUT[-700:]}')

for fn in MUTATIONS:
    with Case(f'A1 V NEG {fn.__name__[2:]}: the one-mutation copy fails verify-telegram-parity, naming RevenueCat Events') as c:
        with canon_copy() as d:
            p = d / RC_FILE
            w = json.loads(p.read_text(encoding='utf-8'))
            before = json.dumps(w, sort_keys=True)
            fn(w)
            c.check(json.dumps(w, sort_keys=True) != before, 'premise: the mutation changed nothing')
            p.write_text(json.dumps(w, ensure_ascii=False, indent=2), encoding='utf-8')
            rc, out = run_parity('--dir', str(d))
        fails = [l for l in out.splitlines() if l.startswith('PARITY FAIL:')]
        c.check(CONTROL_RC == 0, f'control exit {CONTROL_RC} — a verifier red on everything detects nothing')
        c.check(rc == 1 and any(RC_FILE in l for l in fails), f'exit {rc}; failures: {fails[:3]}')

report()
