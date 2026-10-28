#!/usr/bin/env python3
"""Walks ONE execution through the REAL graph of a canonical n8n workflow, edge by edge, in n8n's executionOrder v1 —
shared by the tests of the «Личные и рабочие чаты» workflows: tests/test_chat_audience.py (the ChatAudience webhook,
crChatAudience01, entered at its Webhook) and tests/test_decide_audience.py (the Decide Audience sub-workflow,
crAudienceDec001, entered at its Execute Workflow trigger). Lifted out of test_chat_audience.py on 2026-10-29: that file
runs its scenarios when imported, so the walker had to move to be shared.

What runs — no n8n server, no network, never production:
  * every Code body, If and parameter expression through n8n's OWN engine (n8n-workflow from the staging install = prod's
    version), ONE node process for the whole test file; `$('Node')` is built from what the walker hands in — every executed
    node's output-0 items (`all`) and, for the item being evaluated, the item of that node its pairing leads to (`pair`,
    null when the pairing is broken: n8n then throws) — and, as n8n's WorkflowDataProxy does, a node of the workflow that
    has NOT run answers `isExecuted` false and throws on first() / last() / all() / item;
  * every Postgres node's REAL statement bound from its own queryReplacement (the executeQuery operation's value rules: an
    `undefined` is skipped, an object JSON-stringified) on session temp tables of the staging Postgres — all of a node's
    per-item statements in one transaction (the node's single query batching), bigint and numeric columns handed on as
    STRINGS (node-pg's default), json / jsonb parsed; a fault named in `faults` or a real SQL error becomes the item n8n
    hands on for the node's onError setting;
  * every HTTP node through a stub (`fake(call)`) that sees the request the node builds (URL, query, body, credential,
    options) and answers {statusCode, body} or {throw: message} — what the node then hands on follows its own
    fullResponse / neverError / onError;
  * a Webhook node hands on the request it is given; an Execute Workflow trigger (typeVersion ≥ 1.1, inputSource
    workflowInputs) hands on, per input item, every declared field — null when the caller left it out (n8n's
    FALLBACK_DEFAULT_VALUE) — and nothing else (ExecuteWorkflowTrigger.execute trims the item to the schema).
Order: depth-first; a node's children (all outputs) by canvas position (y, then x); a node runs once per arrival with all the
items of that arrival; alwaysOutputData turns an empty first output into one empty item. The first Respond node answers.

    run = walk_graph(pg, workflow, 'Webhook', webhook=request, fake=stub)
    run = walk_graph(pg, workflow, 'When Executed by Another Workflow', items=[{'json': {...}}], fake=stub)
    run.answer, run.trail, run.calls, run.events, run.outputs['Node'], run.halt
"""
import copy
import json
import subprocess

from rc_events_chain import pg_bind

# ---------------------------------------------------------------------------------------------------- n8n's engine
# JSON lines in/out. A node entry {executed: false} is a node of the workflow that has not run; any other entry has run.
_ENGINE = r"""const path = require('path'), os = require('os');
const N = process.env.N8N_MODULES || path.join(os.homedir(), '.n8n-staging/app/node_modules');
const {evaluateExpression, setErrorHandler} = require(N + '/n8n-workflow/dist/cjs/expression-evaluator-proxy.js');
const {executeFilter} = require(N + '/n8n-workflow/dist/cjs/node-parameters/filter-parameter.js');
setErrorHandler((e) => { throw e; });
// + the URI / number globals n8n's Expression.initializeGlobalContext hands every expression
const GLOBALS = {String, Array, RegExp, Math, Number, JSON, Boolean, Object, Date, Set,
                 encodeURI, encodeURIComponent, decodeURI, decodeURIComponent, parseInt, parseFloat};
function dollar(nodes) {
  return (name) => {
    const n = (nodes || {})[name];
    if (!n) throw new Error("Referenced node doesn't exist: " + name);
    if (n.executed === false) {
      // n8n's WorkflowDataProxy: isExecuted reads the run data; every accessor of the items throws («hasn't been executed»)
      const unexecuted = () => { throw new Error("Node '" + name + "' hasn't been executed"); };
      const api = {isExecuted: false};
      for (const key of ['first', 'last', 'all', 'item']) Object.defineProperty(api, key, {get: unexecuted});
      return api;
    }
    const items = n.all.map((j) => ({json: j}));
    const api = {first: () => items[0], last: () => items[items.length - 1], all: () => items, isExecuted: true};
    Object.defineProperty(api, 'item', {get: () => {
      if (!n.pair) throw new Error('Paired item data for item from node ' + name + ' is unavailable');
      return {json: n.pair.json};
    }});
    return api;
  };
}
const ev = (expr, json, nodes) => evaluateExpression(String(expr).replace(/^=/, ''),
  Object.assign({}, GLOBALS, {$json: json, $: dollar(nodes), $execution: {id: 'synthetic-exec'}}));
async function runCode(v) {
  const fn = new Function('$input', '$', '$env', 'require', 'Buffer', '$json', '$execution', v.js);
  const exec = {id: 'synthetic-exec'};
  const items = v.items.map((x) => ({json: x.json}));
  const list = (r) => (Array.isArray(r) ? r : [r]).filter((x) => x !== undefined && x !== null)
    .map((x) => ((x && typeof x === 'object' && 'json' in x) ? {json: x.json, pairedItem: x.pairedItem === undefined ? null : x.pairedItem} : {json: x, pairedItem: null}));
  if (v.mode === 'each') {
    const out = [];
    for (let k = 0; k < v.items.length; k++) {
      const inp = {item: items[k], first: () => items[0], all: () => items, last: () => items[items.length - 1]};
      const r = await fn(inp, dollar(v.items[k].nodes), v.env || {}, require, Buffer, v.items[k].json, exec);
      if (r === undefined || r === null) throw new Error('runOnceForEachItem: nothing returned for item ' + k);
      out.push({json: (r && typeof r === 'object' && 'json' in r) ? r.json : r, pairedItem: {item: k}});
    }
    return out;
  }
  const inp = {first: () => items[0], all: () => items, last: () => items[items.length - 1], item: items[0]};
  return list(await fn(inp, dollar(v.nodes), v.env || {}, require, Buffer, (items[0] || {}).json, exec));
}
async function handle(v) {
  try {
    if (v.kind === 'expr') return {ok: true, value: ev(v.expr, v.json, v.nodes)};
    if (v.kind === 'params') {
      // executeQuery.operation.js (n8n-nodes-base 2.36, typeVersion >= 2.5), the array branch: an undefined value is SKIPPED
      // (the following parameters shift), an object is JSON-stringified, anything else goes to pg-promise as is
      const r = ev(v.expr, v.json, v.nodes);
      if (!Array.isArray(r)) throw new Error('queryReplacement did not evaluate to an array: ' + JSON.stringify(r));
      const values = [], skipped = [];
      r.forEach((x, i) => { if (x === undefined) { skipped.push(i); return; }
                            values.push(typeof x === 'object' && x !== null ? JSON.stringify(x) : x); });
      return {ok: true, values, skipped};
    }
    if (v.kind === 'if') {
      const f = v.filter, res = (x) => (typeof x === 'string' && x.startsWith('=') ? ev(x, v.json, v.nodes) : x);
      const conditions = f.conditions.map((c) => Object.assign({}, c, {leftValue: res(c.leftValue), rightValue: res(c.rightValue)}));
      return {ok: true, pass: executeFilter(Object.assign({}, f, {conditions}))};
    }
    if (v.kind === 'code') return {ok: true, items: await runCode(v)};
    throw new Error('unknown kind ' + v.kind);
  } catch (e) { return {ok: false, error: String((e && e.message) || e)}; }
}
let chain = Promise.resolve();
require('readline').createInterface({input: process.stdin}).on('line', (line) => {
  chain = chain.then(async () => {
    const r = await handle(JSON.parse(line));
    process.stdout.write(JSON.stringify(r, (k, x) => (x === undefined ? null : x)) + '\n');
  });
});"""


class _Engine:
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


ENGINE = _Engine()

WEBHOOK, TRIGGER, CODE, IF, POSTGRES, HTTP, RESPOND = (
    'n8n-nodes-base.webhook', 'n8n-nodes-base.executeWorkflowTrigger', 'n8n-nodes-base.code', 'n8n-nodes-base.if',
    'n8n-nodes-base.postgres', 'n8n-nodes-base.httpRequest', 'n8n-nodes-base.respondToWebhook')


# ---------------------------------------------------------------------------------------------------- the walker
class Halt(Exception):
    """The execution ends in an error (n8n's own error execution; no Respond node answers after it)."""


class Run:
    def __init__(self, names=()):
        self.answer, self.trail, self.calls, self.events, self.outputs, self.halt = None, [], [], [], {}, None
        self.names = tuple(names)        # every node of the workflow: `$('Node')` of one that has not run is not an error

    def ran(self, name):
        return name in self.trail

    def at(self, kind, name):
        return next((i for i, e in enumerate(self.events) if e == (kind, name)), None)

    def __repr__(self):
        return f'answer={self.answer} trail={self.trail} halt={self.halt}'


def nodes_for(run, lin):
    """What `$('Node')` sees while one item is evaluated: each executed node's output-0 items, and the item of that node
    the evaluated item's pairing leads to (n8n's WorkflowDataProxy.getPairedItem); every other node of the workflow as
    not executed."""
    seen = {name: {'all': [i['json'] for i in (outs[0] if outs else [])],
                   'pair': {'json': lin[name]} if name in lin else None}
            for name, outs in run.outputs.items()}
    for name in run.names:
        seen.setdefault(name, {'executed': False})
    return seen


def derived(items, k, name, json_):
    """An output item paired to input item k: its lineage is k's, plus this node's own item."""
    base = items[k]['lin'] if 0 <= k < len(items) else {}
    return {'json': json_, 'lin': {**base, name: json_}}


def typed_q(pg, sql):
    """The statement's rows the way n8n's Postgres node hands them on: bigint and numeric columns as STRINGS (node-pg's
    default; the node's largeNumbersOutput is not 'numbers'), json / jsonb parsed, everything else as JSON gives it."""
    pg.n += 1
    t = f'__gw_q{pg.n}'
    txt = pg._run(f"create temp table {t} as {sql.strip().rstrip(';')};\n"
                  f"select json_build_object('types', (select coalesce(json_object_agg(attname, format_type(atttypid, atttypmod)), "
                  f"'{{}}'::json) from pg_attribute where attrelid = '{t}'::regclass and attnum > 0 and not attisdropped), "
                  f"'rows', (select coalesce(json_agg({t}), '[]'::json) from {t}));\ndrop table {t};")
    d = json.loads(txt)
    text = {k for k, v in d['types'].items() if v == 'bigint' or v.startswith('numeric')}
    return [{k: (str(v) if k in text and v is not None else v) for k, v in r.items()} for r in d['rows']]


def step_postgres(pg, n, items, run, faults):
    name = n['name']
    cont = n.get('onError') in ('continueRegularOutput', 'continueErrorOutput')
    stmts = []
    for it in items:
        p = ENGINE('params', expr=(n['parameters'].get('options') or {}).get('queryReplacement', '={{ [] }}'),
                   json=it['json'], nodes=nodes_for(run, it['lin']))
        if not p['ok']:                  # thrown outside the query runner: a continuing node hands its INPUT items on
            if cont:
                return [[derived(items, k, name, items[k]['json']) for k in range(len(items))]]
            raise Halt(f'{name}: {p["error"]}')
        if p['skipped']:
            raise AssertionError(f'{name}: an undefined parameter value would shift the rest: {p}')
        stmts.append(pg_bind(n['parameters']['query'], p['values']))
    run.events.append(('sql', name))
    err = None
    if name in faults:
        err = faults[name]
    else:
        pg.x('begin;')
        out = []
        try:
            for k, sql in enumerate(stmts):
                out += [derived(items, k, name, r) for r in typed_q(pg, sql)]
            pg.x('commit;')
        except RuntimeError as e:
            pg.x('rollback;')
            err = str(e).splitlines()[0][:300]
    if err is not None:                 # the query runner's own error item (single batching: one for the whole node)
        if not cont:
            raise Halt(f'{name}: {err}')
        item = {'message': err, 'error': {'message': err}}
        return [[derived(items, 0, name, item) if len(items) == 1 else {'json': item, 'lin': {name: item}}]]
    return [out]


def step_http(n, items, run, fake):
    name, p = n['name'], n['parameters']
    opts = p.get('options') or {}
    resp_opts = (opts.get('response') or {}).get('response') or {}
    cont = n.get('onError') in ('continueRegularOutput', 'continueErrorOutput')
    out = []
    for k, it in enumerate(items):
        nodes = nodes_for(run, it['lin'])

        def ev(key):
            r = ENGINE('expr', expr=p[key], json=it['json'], nodes=nodes)
            if not r['ok']:
                raise Halt(f'{name}.{key}: {r["error"]}')
            return r['value']
        call = {'node': name, 'method': p.get('method', 'GET'), 'url': ev('url'),
                'query': json.loads(ev('jsonQuery')) if p.get('sendQuery') else None,
                'body': json.loads(ev('jsonBody')) if p.get('sendBody') else None,
                'credentials': n.get('credentials'), 'authentication': p.get('authentication'), 'options': opts}
        run.calls.append(call)
        run.events.append(('http', name))
        r = fake(call)
        if 'throw' in r:
            if not cont:
                raise Halt(f'{name}: {r["throw"]}')
            j = {'error': {'message': r['throw']}}
        elif r['statusCode'] >= 400 and not resp_opts.get('neverError'):
            if not cont:
                raise Halt(f'{name}: HTTP {r["statusCode"]}')
            j = {'error': {'message': f'Request failed with status code {r["statusCode"]}'}}
        elif resp_opts.get('fullResponse'):
            j = {'statusCode': r['statusCode'], 'statusMessage': 'OK', 'headers': {}, 'body': r.get('body')}
        else:
            j = r.get('body') if isinstance(r.get('body'), dict) else {'data': r.get('body')}
        out.append(derived(items, k, name, j))
    return [out]


def step_code(n, items, run, env):
    name, mode = n['name'], ('each' if n['parameters'].get('mode') == 'runOnceForEachItem' else 'all')
    payload = dict(js=n['parameters']['jsCode'], mode=mode, env=env)
    if mode == 'each':
        payload['items'] = [{'json': it['json'], 'nodes': nodes_for(run, it['lin'])} for it in items]
    else:
        payload['items'] = [{'json': it['json']} for it in items]
        payload['nodes'] = nodes_for(run, items[0]['lin'] if items else {})
    r = ENGINE('code', **payload)
    if not r['ok']:
        raise Halt(f'{name}: {r["error"]}')
    out = []
    for idx, x in enumerate(r['items']):
        pi = x.get('pairedItem')
        if isinstance(pi, dict) and isinstance(pi.get('item'), int):
            k = pi['item']
        elif len(items) == 1:            # n8n's own pairing when the mapping is unambiguous
            k = 0
        elif len(r['items']) == len(items):
            k = idx
        else:
            k = -1
        out.append(derived(items, k, name, x['json']))
    return [out]


def step_if(n, items, run):
    yes, no = [], []
    for k, it in enumerate(items):
        r = ENGINE('if', filter=n['parameters']['conditions'], json=it['json'], nodes=nodes_for(run, it['lin']))
        if not r['ok']:
            raise Halt(f'{n["name"]}: {r["error"]}')
        (yes if r['pass'] else no).append(derived(items, k, n['name'], it['json']))
    return [yes, no]


def step_respond(n, items, run):
    p, it = n['parameters'], items[0]
    nodes = nodes_for(run, it['lin'])
    body = ENGINE('expr', expr=p.get('responseBody', '={{ {} }}'), json=it['json'], nodes=nodes)
    code = (p.get('options') or {}).get('responseCode', 200)
    if isinstance(code, str) and code.startswith('='):
        r = ENGINE('expr', expr=code, json=it['json'], nodes=nodes)
        code = r['value'] if r['ok'] else None
    if not body['ok']:
        raise Halt(f'{n["name"]}: {body["error"]}')
    if run.answer is None:              # the first Respond answers; n8n ignores any later one
        run.answer = (int(code), body['value'])
        run.events.append(('respond', n['name']))
    return [[derived(items, k, n['name'], items[k]['json']) for k in range(len(items))]]


def step_trigger(n, items):
    """ExecuteWorkflowTrigger.execute (n8n-nodes-base 2.36) for inputSource workflowInputs: per input item every field of
    the schema — null (FALLBACK_DEFAULT_VALUE) when the caller left it out — and only those fields. The trigger itself does
    no type check (the calling Execute Workflow node validates against the schema)."""
    p = n['parameters']
    if p.get('inputSource') != 'workflowInputs':
        return [[{'json': copy.deepcopy(i['json']), 'lin': {n['name']: i['json']}} for i in items]]
    fields = [v['name'] for v in ((p.get('workflowInputs') or {}).get('values') or [])]
    out = []
    for i in items:
        j = {f: None for f in fields}
        j.update({k: copy.deepcopy(v) for k, v in (i.get('json') or {}).items() if k in fields})
        out.append({'json': j, 'lin': {n['name']: j}})
    return [out]


def walk_graph(pg, w, start, items=None, webhook=None, fake=None, env=None, faults=None):
    """One execution of workflow `w` from node `start`: a Webhook node hands on `webhook` (the request n8n builds), an
    Execute Workflow trigger takes `items` ([{'json': caller's item}, …]). `fake(call)` answers every HTTP node, `faults`
    = {Postgres node: error message} fails that node's statement, `env` is the Code nodes' $env."""
    by = {n['name']: n for n in w['nodes']}
    faults = faults or {}
    env = env or {}
    run = Run(by)

    def execute(name, its):
        n = by[name]
        run.trail.append(name)
        run.events.append(('node', name))
        if n.get('disabled'):
            outs = [[derived(its, k, name, its[k]['json']) for k in range(len(its))]]
        elif n['type'] == WEBHOOK:
            outs = [[{'json': webhook, 'lin': {name: webhook}}]]
        elif n['type'] == TRIGGER:
            outs = step_trigger(n, its)
        elif n['type'] == CODE:
            outs = step_code(n, its, run, env)
        elif n['type'] == IF:
            outs = step_if(n, its, run)
        elif n['type'] == POSTGRES:
            outs = step_postgres(pg, n, its, run, faults)
        elif n['type'] == HTTP:
            outs = step_http(n, its, run, fake)
        elif n['type'] == RESPOND:
            outs = step_respond(n, its, run)
        else:
            raise NotImplementedError(f'{name}: {n["type"]}')
        if n.get('alwaysOutputData') and not (outs and outs[0]):
            outs = [[derived(its, 0 if len(its) == 1 else -1, name, {})]] + list(outs[1:])
        run.outputs[name] = outs
        kids = []
        for o, targets in enumerate((w['connections'].get(name) or {}).get('main') or []):
            if o < len(outs) and outs[o]:
                kids += [(e['node'], outs[o]) for e in targets or []]
        kids.sort(key=lambda kid: (by[kid[0]]['position'][1], by[kid[0]]['position'][0]))
        for kid, nxt in kids:
            execute(kid, [{'json': copy.deepcopy(i['json']), 'lin': i['lin']} for i in nxt])

    try:
        execute(start, [{'json': copy.deepcopy(i.get('json') or {}), 'lin': {}} for i in (items or [])])
    except Halt as e:
        run.halt = str(e)
    return run
