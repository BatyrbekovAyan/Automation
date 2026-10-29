#!/usr/bin/env python3
"""Walks ONE execution through the REAL graph of a canonical n8n workflow, edge by edge, in n8n's executionOrder v1 —
shared by the tests of the «Личные и рабочие чаты» workflows: tests/test_chat_audience.py (the ChatAudience webhook,
crChatAudience01, entered at its Webhook), tests/test_decide_audience.py (the Decide Audience sub-workflow,
crAudienceDec001, entered at its Execute Workflow trigger) and tests/test_reply_audience.py (the two bot templates, entered
at their Webhook, calling Decide Audience; tests/reply_chain.py's Run.mode() calls it the same way). Lifted out of
test_chat_audience.py on 2026-10-29: that file runs its scenarios when imported, so the walker had to move to be shared.

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
    hands on for the node's onError setting; no row back is answered the node's way (nothing for SELECT queries by n8n's
    own isSelectQuery, else one {success: true} item — step_postgres cites the source);
  * a node named in `throws` throws OUTSIDE its runner (credentials, connection setup, an expression): one that continues
    on error hands on its INPUT items unchanged, as n8n-core does — the failure that produces no error item at all;
  * every HTTP node through a stub (`fake(call)`) that sees the request the node builds (URL, query, body, credential,
    options) and answers {statusCode, body} or {throw: message} — what the node then hands on follows its own
    fullResponse / neverError / onError;
  * a Webhook node hands on the request it is given; an Execute Workflow trigger (typeVersion ≥ 1.1, inputSource
    workflowInputs) hands on, per input item, every declared field — null when the caller left it out (n8n's
    FALLBACK_DEFAULT_VALUE) — and nothing else (ExecuteWorkflowTrigger.execute trims the item to the schema);
  * an Execute Workflow node (typeVersion ≥ 1.2, source database, mode once, waiting for the sub-workflow) maps each item's
    inputs (workflowInputs.value through n8n's engine) and checks them against its schema with n8n's own validateFieldType
    (step_execute cites the source) — a value that fails the check, or a mapping that throws, throws OUTSIDE the node's
    runner; then it walks the sub-workflow named in `subs` (its id → its JSON) from its Execute Workflow trigger with all the
    items at once, the same Postgres session, stub, faults and throws, and hands on the LAST executed node's outputs (a
    sub-workflow that ends in an error: one {error} item when the node continues on error);
  * a Code node sees `$prevNode.name` — the node whose output it was called with;
  * a Postgres node's queryReplacement in n8n's string form (typeVersion ≥ 2.5, `={{ a }},{{ b }}`) is read the way
    executeQuery.operation.js reads it — every {{ }} of the raw parameter on its own, split at commas unless it is JSON.
Order: depth-first; a node's children (all outputs) by canvas position (y, then x); a node runs once per arrival with all the
items of that arrival; alwaysOutputData turns an empty first output into one empty item. The first Respond node answers. A node
named in `stop` ends the walk where it stands (it is recorded in the trail, it does not run).

    run = walk_graph(pg, workflow, 'Webhook', webhook=request, fake=stub)
    run = walk_graph(pg, workflow, 'When Executed by Another Workflow', items=[{'json': {...}}], fake=stub)
    run = walk_graph(pg, template, 'Webhook', webhook=request, fake=stub, subs={'crAudienceDec001': sub}, stop=('Debounce Wait',))
    run.answer, run.trail, run.calls, run.events, run.outputs['Node'], run.subs['Execute Node'], run.halt
"""
import copy
import json
import re
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
  const fn = new Function('$input', '$', '$env', 'require', 'Buffer', '$json', '$execution', '$prevNode', v.js);
  const exec = {id: 'synthetic-exec'};
  const prev = {name: v.prevNode || ''};
  const items = v.items.map((x) => ({json: x.json}));
  const list = (r) => (Array.isArray(r) ? r : [r]).filter((x) => x !== undefined && x !== null)
    .map((x) => ((x && typeof x === 'object' && 'json' in x) ? {json: x.json, pairedItem: x.pairedItem === undefined ? null : x.pairedItem} : {json: x, pairedItem: null}));
  if (v.mode === 'each') {
    const out = [];
    for (let k = 0; k < v.items.length; k++) {
      const inp = {item: items[k], first: () => items[0], all: () => items, last: () => items[items.length - 1]};
      const r = await fn(inp, dollar(v.items[k].nodes), v.env || {}, require, Buffer, v.items[k].json, exec, prev);
      if (r === undefined || r === null) throw new Error('runOnceForEachItem: nothing returned for item ' + k);
      out.push({json: (r && typeof r === 'object' && 'json' in r) ? r.json : r, pairedItem: {item: k}});
    }
    return out;
  }
  const inp = {first: () => items[0], all: () => items, last: () => items[items.length - 1], item: items[0]};
  return list(await fn(inp, dollar(v.nodes), v.env || {}, require, Buffer, (items[0] || {}).json, exec, prev));
}
// The Postgres node's own text helpers (n8n-nodes-base 2.36, Postgres v2 helpers/utils.js): evaluateExpression, isJSON,
// stringToArray — what the string form of queryReplacement goes through.
const pgText = (x) => (x === undefined ? '' : x === null ? 'null' : (typeof x === 'object' ? JSON.stringify(x) : x.toString()));
const isJSON = (s) => { try { JSON.parse(String(s).trim()); return true; } catch (e) { return false; } };
const toArray = (s) => (s === undefined ? [] : String(s).split(',').filter((e) => e).map((e) => e.trim()));
async function handle(v) {
  try {
    if (v.kind === 'expr') return {ok: true, value: ev(v.expr, v.json, v.nodes)};
    if (v.kind === 'params') {
      // executeQuery.operation.js (n8n-nodes-base 2.36, typeVersion >= 2.5). The array branch: an undefined value is SKIPPED
      // (the following parameters shift), an object is JSON-stringified, anything else goes to pg-promise as is. The string
      // branch (the parameter evaluates to text, e.g. `={{ a }},{{ b }}`): every {{ }} of the RAW parameter is evaluated on
      // its own (getResolvables) — an array spreads as above, anything else becomes the Postgres helpers' text (null →
      // 'null'), kept whole when it is JSON, else split at commas; a raw parameter without {{ }} is split at commas as is
      const r = ev(v.expr, v.json, v.nodes);
      const values = [], skipped = [];
      if (Array.isArray(r)) {
        r.forEach((x, i) => { if (x === undefined) { skipped.push(i); return; }
                              values.push(typeof x === 'object' && x !== null ? JSON.stringify(x) : x); });
        return {ok: true, values, skipped};
      }
      if (typeof r !== 'string' && typeof r !== 'number') throw new Error('queryReplacement is neither an array nor text: ' + JSON.stringify(r));
      if (!((v.version || 0) >= 2.5)) throw new Error('NOT MODELLED: the text form of queryReplacement before typeVersion 2.5 (' + v.version + ')');
      const raw = String(v.expr).replace(/^=+/, '');
      const resolvables = raw.match(/({{[\s\S]*?}})/g) || [];
      if (!resolvables.length) values.push(...toArray(raw));
      for (const one of resolvables) {
        const x = ev('=' + one, v.json, v.nodes);
        if (Array.isArray(x)) {
          for (const item of x) { if (item !== undefined) values.push(typeof item === 'object' && item !== null ? JSON.stringify(item) : item); }
          continue;
        }
        const text = pgText(x);
        values.push(...(isJSON(text) ? [text] : toArray(text)));
      }
      return {ok: true, values, skipped};
    }
    if (v.kind === 'validate') {
      // validate-value-against-schema.js (n8n-core 2.36.5) for a resource mapper value: n8n-workflow's validateFieldType per
      // field of the schema — strict = showTypeConversionOptions (the Execute Workflow node shows them) && !attemptToConvertTypes,
      // parseStrings = … && convertFieldsToString; the field then takes the validated value (undefined when the value is null)
      const {validateFieldType} = require(N + '/n8n-workflow/dist/cjs/type-validation.js');
      const res = validateFieldType(v.key, v.value, v.type, {strict: v.strict, parseStrings: v.parseStrings});
      return {ok: true, valid: res.valid, value: res.valid ? res.newValue : null, error: res.valid ? null : (res.errorMessage || 'invalid')};
    }
    if (v.kind === 'if') {
      const f = v.filter, res = (x) => (typeof x === 'string' && x.startsWith('=') ? ev(x, v.json, v.nodes) : x);
      const conditions = f.conditions.map((c) => Object.assign({}, c, {leftValue: res(c.leftValue), rightValue: res(c.rightValue)}));
      return {ok: true, pass: executeFilter(Object.assign({}, f, {conditions}))};
    }
    if (v.kind === 'code') return {ok: true, items: await runCode(v)};
    if (v.kind === 'isSelect') {
      // the Postgres node's own classifier (n8n-nodes-base Postgres v2 helpers/utils.js): which empty answer the node gives
      const {isSelectQuery} = require(N + '/n8n-nodes-base/dist/nodes/Postgres/v2/helpers/utils.js');
      return {ok: true, value: isSelectQuery(String(v.query))};
    }
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

WEBHOOK, TRIGGER, CODE, IF, POSTGRES, HTTP, RESPOND, EXECUTE = (
    'n8n-nodes-base.webhook', 'n8n-nodes-base.executeWorkflowTrigger', 'n8n-nodes-base.code', 'n8n-nodes-base.if',
    'n8n-nodes-base.postgres', 'n8n-nodes-base.httpRequest', 'n8n-nodes-base.respondToWebhook', 'n8n-nodes-base.executeWorkflow')


# ---------------------------------------------------------------------------------------------------- the walker
class Halt(Exception):
    """The execution ends in an error (n8n's own error execution; no Respond node answers after it)."""


class Run:
    def __init__(self, names=()):
        self.answer, self.trail, self.calls, self.events, self.outputs, self.halt = None, [], [], [], {}, None
        self.names = tuple(names)        # every node of the workflow: `$('Node')` of one that has not run is not an error
        self.subs = {}                   # Execute Workflow node → [the Run of each sub-workflow it called]

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


def _strip_sql(query):
    """The statement with its comments and the contents of its literals blanked — the scan n8n's own classifier makes
    (n8n-nodes-base 2.36.4, Postgres v2 helpers/utils.js stripStringsAndComments): /* */ and -- comments, '…' (with ''
    and, after E, backslash escapes), "…" and $tag$…$tag$."""
    out, i, n = [], 0, len(query)
    while i < n:
        c, nxt = query[i], query[i + 1:i + 2]
        if c == '/' and nxt == '*':
            end = query.find('*/', i + 2)
            i = n if end < 0 else end + 2
            out.append(' ')
        elif c == '-' and nxt == '-':
            end = query.find('\n', i + 2)
            i = n if end < 0 else end
            out.append(' ')
        elif c == "'":
            escape = i > 0 and query[i - 1] in 'eE' and (i < 2 or not (query[i - 2].isalnum() or query[i - 2] == '_'))
            i += 1
            while i < n:
                if escape and query[i] == '\\' and i + 1 < n:
                    i += 2
                elif query[i] == "'" and query[i + 1:i + 2] == "'":
                    i += 2
                elif query[i] == "'":
                    i += 1
                    break
                else:
                    i += 1
            out.append("''")
        elif c == '"':
            end = query.find('"', i + 1)
            i = n if end < 0 else end + 1
            out.append('""')
        elif c == '$' and re.match(r'\$(?:[A-Za-z_][A-Za-z0-9_]*)?\$', query[i:]):
            tag = re.match(r'\$(?:[A-Za-z_][A-Za-z0-9_]*)?\$', query[i:]).group(0)
            end = query.find(tag, i + len(tag))
            i = n if end < 0 else end + len(tag)
            out.append(' ')
        else:
            out.append(c)
            i += 1
    return ''.join(out)


def _returns_rows(statement):
    """A data-modifying statement hands rows back only with its own RETURNING — one at the top level (depth 0), not a
    CTE's inside parentheses."""
    s, depth = _strip_sql(statement), 0
    for m in re.finditer(r'[()]|\breturning\b', s, re.I):
        tok = m.group(0)
        if tok == '(':
            depth += 1
        elif tok == ')':
            depth = max(depth - 1, 0)
        elif depth == 0:
            return True
    return False


def typed_q(pg, sql, select=True):
    """The statement's rows the way n8n's Postgres node hands them on: bigint and numeric columns as STRINGS (node-pg's
    default; the node's largeNumbersOutput is not 'numbers'), json / jsonb parsed, everything else as JSON gives it.
    `select` = n8n's isSelectQuery of the node's query: anything else runs as itself — a data-modifying statement without
    a top-level RETURNING hands back no row; one with it is read through a CTE."""
    body = sql.strip().rstrip(';')
    if not select:
        if not _returns_rows(body):
            pg._run(body + ';')
            return []
        body = f'with __gw_dml as ({body}) select * from __gw_dml'
    pg.n += 1
    t = f'__gw_q{pg.n}'
    txt = pg._run(f"create temp table {t} as {body};\n"
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
                   json=it['json'], nodes=nodes_for(run, it['lin']), version=n.get('typeVersion') or 1)
        if not p['ok'] and p['error'].startswith('NOT MODELLED'):
            raise NotImplementedError(f'{name}: {p["error"]}')
        if not p['ok']:                  # thrown outside the query runner: a continuing node hands its INPUT items on
            if cont:
                return [[derived(items, k, name, items[k]['json']) for k in range(len(items))]]
            raise Halt(f'{name}: {p["error"]}')
        if p['skipped']:
            raise AssertionError(f'{name}: an undefined parameter value would shift the rest: {p}')
        stmts.append(pg_bind(n['parameters']['query'], p['values']))
    run.events.append(('sql', name))
    query = n['parameters']['query']
    sel = ENGINE('isSelect', query=query)
    if not sel['ok']:
        raise AssertionError(f'{name}: n8n\'s isSelectQuery could not be asked: {sel}')
    select = sel['value']
    err = None
    if name in faults:
        err = faults[name]
    else:
        pg.x('begin;')
        out = []
        try:
            for k, sql in enumerate(stmts):
                out += [derived(items, k, name, r) for r in typed_q(pg, sql, select)]
            pg.x('commit;')
        except RuntimeError as e:
            pg.x('rollback;')
            err = str(e).splitlines()[0][:300]
    if err is not None:                 # the query runner's own error item (single batching: one for the whole node)
        if not cont:
            raise Halt(f'{name}: {err}')
        item = {'message': err, 'error': {'message': err}}
        return [[derived(items, 0, name, item) if len(items) == 1 else {'json': item, 'lin': {name: item}}]]
    if not out:
        # No row from any statement. n8n-nodes-base 2.36.4, Postgres v2 helpers/utils.js configureQueryRunner, single
        # batching (lines 311–337): typeVersion < 2.3 answers one {success: true} item for every executeQuery (it is not
        # the 'select' operation); from 2.3 on, nothing when EVERY query is a SELECT by isSelectQuery (asked of n8n's own
        # function: comments and literals stripped, the first top-level command word — `WITH … (INSERT …) SELECT` is a
        # SELECT), else one {success: true} item paired to every input item. The query is the node's own text: the
        # workflows under test put no expression into it.
        if (n.get('typeVersion') or 1) >= 2.3 and select:
            return [[]]
        item = {'success': True}
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


def step_code(n, items, run, env, prev=''):
    name, mode = n['name'], ('each' if n['parameters'].get('mode') == 'runOnceForEachItem' else 'all')
    payload = dict(js=n['parameters']['jsCode'], mode=mode, env=env, prevNode=prev)
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


def step_throw(n, items, run, message):
    """A node that throws OUTSIDE its own runner. n8n-core 2.36.5 (execution-engine/workflow-execute.js, the executionError
    branch, lines 1139–1147): a node that continues on error gets nodeSuccessData = [executionData.data.main[0]] — its INPUT
    items, unchanged, on its first output; no error item at all. Without onError the execution ends. continueErrorOutput
    is not modelled (no workflow under test uses it)."""
    if n.get('onError') == 'continueErrorOutput':
        raise NotImplementedError(f'{n["name"]}: a throw with continueErrorOutput is not modelled')
    if n.get('onError') != 'continueRegularOutput':
        raise Halt(f'{n["name"]}: {message}')
    run.events.append(('throw', n['name']))
    return [[derived(items, k, n['name'], items[k]['json']) for k in range(len(items))]]


def execute_inputs(n, item_json, nodes):
    """What an Execute Workflow node (n8n-nodes-base 2.36.4, typeVersion ≥ 1.2) hands its sub-workflow for ONE input item —
    getWorkflowInputValues: every workflowInputs.value through n8n's engine (a value without a leading '=' is a literal),
    merged OVER the item's own json, each field first checked against the node's schema by validateResourceMapperValue
    (n8n-core 2.36.5, validate-value-against-schema.js: validateFieldType, strict unless attemptToConvertTypes — the node
    shows the conversion options —, strings checked only with convertFieldsToString; a field takes the validated value);
    the schema's removed fields left out (getCurrentWorkflowInputData). `nodes` = what `$('Node')` sees (nodes_for). Returns
    (json, None), or (None, the error) when a value does not evaluate or does not pass — n8n throws that from
    getNodeParameter, outside the node's try."""
    mapping = n['parameters'].get('workflowInputs') or {}
    schema = {s.get('id'): s for s in (mapping.get('schema') or [])}
    strict, parse_strings = not mapping.get('attemptToConvertTypes'), bool(mapping.get('convertFieldsToString'))
    values = {}
    for key, raw in (mapping.get('value') or {}).items():
        if isinstance(raw, str) and raw.startswith('='):
            r = ENGINE('expr', expr=raw, json=item_json, nodes=nodes)
            if not r['ok']:
                return None, f'{key}: {r["error"]}'
            value = r['value']
        else:
            value = raw
        typ = (schema.get(key) or {}).get('type')
        if typ:
            v = ENGINE('validate', key=key, value=value, type=typ, strict=strict, parseStrings=parse_strings)
            if not v['ok']:
                raise AssertionError(f'{n["name"]}: n8n\'s validateFieldType could not be asked: {v}')
            if not v['valid']:
                return None, f"Invalid input for '{key}': {v['error']}"
            value = v['value']
        values[key] = value
    removed = {s.get('displayName') for s in schema.values() if s.get('removed')}
    return {k: x for k, x in {**item_json, **values}.items() if k not in removed}, None


def step_execute(pg, n, items, run, subs, fake, env, faults, throws):
    """n8n-nodes-base 2.36.4 ExecuteWorkflow.execute — typeVersion ≥ 1.2, source database, mode once, waiting for the
    sub-workflow (anything else raises NotImplementedError, and so does continueErrorOutput). getCurrentWorkflowInputData
    (execute_inputs) runs BEFORE the node's try: a value that does not evaluate or does not pass its check throws outside the
    runner (step_throw — a node that continues on error hands its INPUT items on, the sub-workflow never starts). Then the
    sub-workflow subs[workflowId] runs ONCE with all the items (walk_graph from its Execute Workflow trigger; the same session,
    stub, faults and throws — the node names are the sub-workflow's own); the outputs of its LAST executed node come back,
    each item paired to its input when the lengths match. A sub-workflow that ends in an error is caught INSIDE the try: one
    {error} item when the node continues on error, else the execution ends."""
    p, name = n['parameters'], n['name']
    if (n.get('typeVersion') or 1) < 1.2 or p.get('source', 'database') != 'database' or p.get('mode', 'once') != 'once' \
            or (p.get('options') or {}).get('waitForSubWorkflow', True) is not True or n.get('onError') == 'continueErrorOutput':
        raise NotImplementedError(f'{name}: only typeVersion ≥ 1.2, source database, mode once, waiting, no error output is modelled')
    wid = p.get('workflowId')
    wid = wid.get('value') if isinstance(wid, dict) else wid
    sub = (subs or {}).get(wid)
    if sub is None:
        raise AssertionError(f'{name}: the sub-workflow {wid!r} was not handed to the walker (subs)')
    merged = []
    for it in items:
        j, err = execute_inputs(n, it['json'], nodes_for(run, it['lin']))
        if err is not None:
            return step_throw(n, items, run, err)
        merged.append({'json': j})
    run.events.append(('sub', name))
    trigger = next(x['name'] for x in sub['nodes'] if x['type'] == TRIGGER)
    r = walk_graph(pg, sub, trigger, items=merged, fake=fake, env=env, faults=faults, throws=throws, subs=subs)
    run.subs.setdefault(name, []).append(r)
    run.calls += r.calls
    if r.halt:
        if n.get('onError') != 'continueRegularOutput':
            raise Halt(f'{name}: the sub-workflow {wid} failed: {r.halt}')
        item = {'error': f'the sub-workflow {wid} failed: {r.halt}'}
        return [[derived(items, 0, name, item) if len(items) == 1 else {'json': item, 'lin': {name: item}}]]
    outs = r.outputs.get(r.trail[-1]) if r.trail else None
    result = []
    for o in (outs or [[]]):
        same = len(o) == len(items)
        result.append([derived(items, k if same else (0 if len(items) == 1 else -1), name, x['json']) for k, x in enumerate(o)])
    return result


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


def walk_graph(pg, w, start, items=None, webhook=None, fake=None, env=None, faults=None, throws=None, subs=None, stop=()):
    """One execution of workflow `w` from node `start`: a Webhook node hands on `webhook` (the request n8n builds), an
    Execute Workflow trigger takes `items` ([{'json': caller's item}, …]). `fake(call)` answers every HTTP node, `env` is
    the Code nodes' $env, `subs` = {workflow id: workflow JSON} the sub-workflows an Execute Workflow node may call, `stop`
    = nodes where the walk ends (recorded, not run). Two failure shapes, as n8n has them (a sub-workflow's nodes included):
      faults = {Postgres node: error message}  the statement fails INSIDE the query runner: the node's own error item
                                               {message, error} (configureQueryRunner's catch);
      throws = {node: error message}           the node throws OUTSIDE its runner — getCredentials, configurePostgres, a
                                               parameter expression (Postgres v2 router.js / executeQuery.operation.js):
                                               a node that continues on error then hands on its INPUT items unchanged,
                                               any other ends the execution (step_throw)."""
    by = {n['name']: n for n in w['nodes']}
    faults = faults or {}
    throws = throws or {}
    env = env or {}
    run = Run(by)

    def execute(name, its, prev=''):
        n = by[name]
        run.trail.append(name)
        if name in stop:
            run.events.append(('stop', name))
            return
        run.events.append(('node', name))
        if name in throws:
            outs = step_throw(n, its, run, throws[name])
        elif n.get('disabled'):
            outs = [[derived(its, k, name, its[k]['json']) for k in range(len(its))]]
        elif n['type'] == WEBHOOK:
            outs = [[{'json': webhook, 'lin': {name: webhook}}]]
        elif n['type'] == TRIGGER:
            outs = step_trigger(n, its)
        elif n['type'] == CODE:
            outs = step_code(n, its, run, env, prev)
        elif n['type'] == EXECUTE:
            outs = step_execute(pg, n, its, run, subs, fake, env, faults, throws)
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
            execute(kid, [{'json': copy.deepcopy(i['json']), 'lin': i['lin']} for i in nxt], name)

    try:
        execute(start, [{'json': copy.deepcopy(i.get('json') or {}), 'lin': {}} for i in (items or [])])
    except Halt as e:
        run.halt = str(e)
    return run
