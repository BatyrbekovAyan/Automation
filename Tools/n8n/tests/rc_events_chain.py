#!/usr/bin/env python3
"""Walks ONE RevenueCat delivery through the REAL graph of the canonical RevenueCat Events JSON (ZGYr6srzS3rSSXHp), edge by
edge from Webhook to the Respond node it reaches — shared by the tests of that workflow (A1 test_rc_event_ledger.py, A2
test_billing_consolidation.py; lifted out of the A1 test file 2026-10-14).

What runs — no n8n server, no network, never production: Code bodies through harness.code; every If through n8n's OWN filter
engine (n8n-workflow executeFilter — the staging install = prod's version); every Postgres node's own queryReplacement through
n8n's expression engine with the executeQuery operation's value rules (an `undefined` is SKIPPED, so the following parameters
shift; an object is JSON-stringified), bound into the node's own query (pg-promise `partial`: a placeholder without a value
stays and Postgres refuses it) and run in the local staging Postgres on session temp tables (`like public.X`, autocommit, a
real clock). A node's real SQL error becomes what n8n hands on for its onError setting (regular / error output, the input item
for an error thrown outside the query runner, or no answer at all). Faults are real Postgres errors, never mocks.

    with Session(SETUP) as pg:
        run = deliver(pg, {'api_version': '1.0', 'event': {...}})          # the canonical graph
        run = deliver(pg, body, workflow=mutated_copy)                      # the same walk over another copy
        run.answer, run.trail, run.named['Consolidate Aliases']

The race helpers run a node's REAL statement on TWO real connections of the staging database: the tables live in a uniquely
named fixture schema (session temp tables are invisible to another connection; dropped afterwards); delivery A holds its
transaction open, delivery B blocks on A's lock, A commits.
"""
import concurrent.futures
import json
import re
import subprocess
import time
import uuid

from harness import PG, PgSession, code, module, node, sql_literal, wf

RC_FILE = 'ZGYr6srzS3rSSXHp-RevenueCat_Events.json'
CANON = wf(RC_FILE)
parity = module('verify-telegram-parity.py')    # its statement splitter mirrors n8n's own (Postgres v2 utils.js)

WEBHOOK, CODE, IF, POSTGRES, RESPOND = ('n8n-nodes-base.webhook', 'n8n-nodes-base.code', 'n8n-nodes-base.if',
                                        'n8n-nodes-base.postgres', 'n8n-nodes-base.respondToWebhook')

# Every table a RevenueCat Events statement touches (bot_profiles: the alias merge's re-key CTE, A2).
RC_TABLES = ('subscribers', 'dialog_counts', 'rc_event_ledger', 'bot_profiles')
SETUP = "set timezone = 'UTC';\n" + "\n".join(f"create temp table {t} (like public.{t} including all);" for t in RC_TABLES)

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
  // + the URI / number globals n8n's own Expression.initializeGlobalContext hands every expression (n8n-workflow expression.js)
  const data={$json:v.json, $, $execution:{id:'synthetic-exec'}, String, Array, RegExp, Math, Number, JSON, Boolean, Object, Date,
              encodeURI, encodeURIComponent, decodeURI, decodeURIComponent, parseInt, parseFloat};
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


def bound_sql(w, name, item, named=None):
    """The statement node `name` of workflow `w` sends for the input item `item`: its own queryReplacement evaluated by
    n8n's engine (value rules above), bound into its own query. Refuses a skipped value (the parameters would shift)."""
    n = node(w, name)
    p = engine('params', expr=(n['parameters'].get('options') or {}).get('queryReplacement', '={{ [] }}'), json=item,
               named=named or {})
    assert p['ok'] and not p['skipped'], f'{name}: {p}'
    return pg_bind(n['parameters']['query'], p['values'])


class Session(PgSession):
    """harness.PgSession whose psql SURVIVES a failed statement (ON_ERROR_STOP off): a node's real SQL error reaches the
    walker as RuntimeError and the scenario goes on in the same session (autocommit: the failed statement rolled back
    alone — exactly the n8n Postgres node's single-statement query)."""

    def __init__(self, setup=''):
        self.p = subprocess.Popen([str(PG), 'psql', '-X', '-q', '-t', '-A', '-P', 'pager=off', '-v', 'ON_ERROR_STOP=0'],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)
        self.n = 0
        self.x('set search_path = pg_temp, public;\n' + setup)


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


def _step(pg, w, n, items, named, wh, run):
    t = n['type']
    if n.get('disabled'):             # n8n skips a disabled node and hands its input on
        return [items]
    if t == WEBHOOK:
        return [[wh]]
    if t == CODE:
        res = code(w, n['name'], item={'json': items[0]}, items=[{'json': x} for x in items], named=named, strict=True)
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


def deliver(pg, body, faults=None, workflow=None):
    """One RevenueCat delivery through the graph of `workflow` (default: the canonical RevenueCat Events). `faults` =
    {node name: (sql before it, sql after it)} — a real Postgres condition that holds only while that node runs."""
    w = workflow or CANON
    wh = {'headers': {'content-type': 'application/json', 'authorization': 'Bearer synthetic'}, 'params': {}, 'query': {},
          'body': body, 'webhookUrl': 'http://localhost:5678/webhook/RevenueCatEvent', 'executionMode': 'production'}
    run, queue = Run(), [('Webhook', [wh])]
    while queue:
        name, items = queue.pop(0)
        n = node(w, name)
        run.trail.append(name)
        before, after = (faults or {}).get(name, (None, None))
        if before:
            pg.x(before)
        try:
            outs = _step(pg, w, n, items, run.named, wh, run)
        except Halt as e:
            run.halt = str(e)
            return run
        finally:
            if after:
                pg.x(after)
        if n.get('alwaysOutputData') and not (outs and outs[0]):
            outs = [[{}]] + list(outs[1:])
        run.named[name] = next((o for o in outs if o), [{}])
        branches = (w['connections'].get(name) or {}).get('main') or []
        for idx, out in enumerate(outs):
            if out and idx < len(branches):
                queue.extend((e['node'], out) for e in branches[idx] or [])
    return run


def ok_answer(run):
    return run.answer is not None and run.answer[0] == 200 and (run.answer[1] or {}).get('success') is True


# ---------------------------------------------------------------------------------------------------- the race
class RaceFixture:
    """Three connections (main + two deliveries) on one fixture schema of the staging database; dropped on exit."""

    def __init__(self, tables=('subscribers', 'rc_event_ledger')):
        self.tables = tables

    def __enter__(self):
        self.schema = 't_rcr_' + uuid.uuid4().hex[:16]
        self.main, self.a, self.b = PgSession(), PgSession(), PgSession()
        self.main.x(f'create schema {self.schema}; set search_path = {self.schema};')
        for t in self.tables:
            self.main.x(f'create table {t} (like public.{t} including all);')
        for conn in (self.a, self.b):
            conn.x(f"set search_path = {self.schema}; set statement_timeout = '15s'; set timezone = 'UTC';")
        return self

    def __exit__(self, *exc):
        for conn in (self.a, self.b):
            try:
                conn.x('rollback;')
            except Exception:
                pass
        try:
            self.main.x(f'drop schema if exists {self.schema} cascade;')
        finally:
            for conn in (self.main, self.a, self.b):
                conn.close()


def race(fx, stmt_a, stmt_b):
    """Delivery A runs inside an open transaction (its locks held); delivery B runs meanwhile on the second connection.
    A commits once B waits on a lock (or after ~10 s — never a hang). Returns the wait B was in ('' = it never waited)."""
    fx.a.x('begin;')
    fx.a.x(stmt_a)
    pid = fx.b.q('select pg_backend_pid() as pid')[0]['pid']
    waited = ''
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
        pending = pool.submit(fx.b.x, stmt_b)
        try:
            for _ in range(500):
                if pending.done():
                    break
                st = fx.main.q(f'select wait_event_type, wait_event from pg_stat_activity where pid = {pid}')
                if st and st[0]['wait_event_type'] == 'Lock':
                    waited = st[0]['wait_event']
                    break
                time.sleep(0.02)
        finally:
            fx.a.x('commit;')
        pending.result(timeout=20)
    return waited
