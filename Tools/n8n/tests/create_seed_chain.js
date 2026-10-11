// Behavioural driver for the Create WhatsApp/Telegram graphs — R8 (iteration 2026-09-27), R8-deletefail (2026-09-28) and
// the bot's line F1 + F2 (plan 2026-10-11 §1.4, task T3). Runs the canonical Create graph from its JSON, starting at the
// node the scenario names (`start`, default `Register Profile` — the slot-allowed branch; `If Has AppUserID?` for a legacy
// body without AppUserID; `Retire Same Bot Slot` for the whole session path), under the outcomes a node can really have,
// and reports what the run went through and what the webhook (responseMode: lastNode) answered. The clone's id is NOT
// injected: `Create Workflow` answers it (the scenario's `createdId`, `http['Create Workflow']`, or the bridge's fake n8n)
// and the graph's own Set nodes read it, so a run that never created a clone cannot resolve one either.
//
// Every Postgres node is executed the way n8n-core 2.36.7 executes a node (workflow-execute.js): retryOnFail/maxTries
// (2..5, default 3) re-run it on a thrown error AND on a first item carrying json.error; after the last try a thrown
// error with onError continue* passes the node's INPUT items on output 0; an empty result becomes {} under
// alwaysOutputData; onError continueErrorOutput moves {error,message}-only items to output 1 (handleNodeErrorOutput).
// The queryReplacement is evaluated like the Postgres node v2.6 does it (an array expression = the values; undefined
// elements skipped, objects JSON-stringified) and recorded in `effects` (`params`). Each try's result comes from
//   sc.seed (Seed Reply Mode) / sc.pg[<node>] (a list, one entry per try), else the BRIDGE (real SQL, below), else
//   {rows: []}:
//   {rows:[…]}          a completed statement; no rows → the Postgres node v2 (≥ 2.3) answers {success:true}
//                       for a non-SELECT statement and nothing for a SELECT (its own isSelectQuery decides)
//   {queryError:'msg'}  the Postgres node's continueOnFail item {message, error} (a failed query / lost connection)
//   {throw:'msg'}       an error raised outside the query runner (credentials, expression, pool)
//   {noItems:true}      the node returned no items at all
// HTTP Request nodes run once per input item like v4.2 does: the answer is sc.http[<node>] (an object, or a list — one
// entry per call), else the BRIDGE, else `Create Workflow` → {id: createdId} and every other node → 200 {}. An answer is
// {statusCode, body} (a response), {throw:'msg'} (no response: DNS, timeout) or — the legacy form — the node's json
// output itself (= a 200 with that body). The node's own options decide the rest: a non-2xx throws unless
// options.response.response.neverError; fullResponse outputs {statusCode, headers, body}; a thrown error under onError
// continueRegularOutput is the node's own {error:{message}} item (retried by retryOnFail like any error item).
// If (n8n's own expression engine), Set (dot notation, includeOtherFields all/selected/except, executeOnce) and Code
// (runOnceForAllItems) are evaluated. n8n executionOrder v1: children run depth-first, sorted by canvas position. A graph
// that loops is stopped (`stoppedAt`).
//
//   node create_seed_chain.js <Create.json> < {scenario: {body, createdId, seed:[…], start?, http?, pg?, crAuth?}}
//        -> {scenario: report}
//   node create_seed_chain.js <Create.json> <scenarios.json>          BRIDGE mode (tests/test_create_clone.py):
//        every Postgres node without a scripted answer writes {"pg": {node, query, params, select}} and every HTTP call
//        without one writes {"http": {node, method, url, body}} as ONE line to stdout and reads the answer as ONE line
//        from stdin (the test runs the real SQL on its temp tables and plays a fake n8n + Wappi); the reports follow as
//        the last line {"report": {scenario: report}}.
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');

const N = process.env.N8N_MODULES || path.join(os.homedir(), '.n8n-staging/app/node_modules');
const { evaluateExpression, setErrorHandler } = require(N + '/n8n-workflow/dist/cjs/expression-evaluator-proxy.js');
const { isSelectQuery } = require(N + '/n8n-nodes-base/dist/nodes/Postgres/v2/helpers/utils.js');
setErrorHandler((e) => { throw e; });
const GLOBALS = { String, Array, RegExp, Math, Number, JSON, Boolean, Object, Date, Set };

const BRIDGE = Boolean(process.argv[3]);
let pending = Buffer.alloc(0);
function readLine() {
  for (;;) {
    const i = pending.indexOf(10);
    if (i >= 0) {
      const line = pending.slice(0, i).toString('utf8');
      pending = pending.slice(i + 1);
      return line;
    }
    const chunk = Buffer.alloc(1 << 16);
    let n;
    try {
      n = fs.readSync(0, chunk, 0, chunk.length, null);
    } catch (e) {
      if (e.code === 'EAGAIN') continue;
      throw e;
    }
    if (n === 0) throw new Error('bridge: stdin closed');
    pending = Buffer.concat([pending, chunk.slice(0, n)]);
  }
}
function bridge(req) {
  fs.writeSync(1, JSON.stringify(req) + '\n');
  return JSON.parse(readLine());
}

function evalExpr(raw, ctx) {
  if (typeof raw !== 'string' || !raw.startsWith('=')) return raw;
  try {
    return evaluateExpression(raw.slice(1), { ...GLOBALS, $json: ctx.json, $: ctx.$ });
  } catch (e) {
    if (e instanceof TypeError) return null;   // n8n's legacy expression engine renders a TypeError as null
    throw e;
  }
}

function condOk(c, ctx) {
  const left = evalExpr(c.leftValue, ctx);
  const op = c.operator || {};
  if (op.type === 'boolean' && op.operation === 'true') return left === true;
  if (op.type === 'boolean' && op.operation === 'false') return left === false;
  // n8n If v2 string/notEmpty (loose type validation): undefined, null and '' are empty
  if (op.type === 'string' && op.operation === 'notEmpty') return left !== undefined && left !== null && String(left) !== '';
  if (op.type === 'string' && op.operation === 'empty') return left === undefined || left === null || String(left) === '';
  throw new Error('driver: unsupported operator ' + JSON.stringify(op));
}

// lodash-style set for a Set node assignment name with dot notation ("nodes[0].parameters.path")
function setPath(obj, name, value, dot) {
  if (!dot) { obj[name] = value; return; }
  const keys = [];
  name.replace(/[^.[\]]+|\[(\d+)\]/g, (m, idx) => { keys.push(idx !== undefined ? Number(idx) : m); return m; });
  let cur = obj;
  keys.forEach((k, i) => {
    if (i === keys.length - 1) { cur[k] = value; return; }
    if (cur[k] === undefined || cur[k] === null || typeof cur[k] !== 'object') cur[k] = typeof keys[i + 1] === 'number' ? [] : {};
    cur = cur[k];
  });
}

const isErrorValue = (v) => v !== undefined && v !== null && v !== false;
const isErrorItem = (it) => it && it.json && it.json.error && Object.keys(it.json).every((k) => ['error', 'message', 'details'].includes(k));

function runScenario(wf, sc) {
  const byName = Object.fromEntries(wf.nodes.map((n) => [n.name, n]));
  const out = {
    'Unity Webhook': [[{ json: { body: sc.body, headers: {}, params: {}, query: {} } }]],
    'Auth Gate': [[{ json: { crAuth: sc.crAuth || { ok: true, mode: 'session', installId: 'install-r8' } } }]],
  };
  const http = Object.assign(sc.createdId !== undefined ? { 'Create Workflow': { id: sc.createdId } } : {}, sc.http || {});
  let steps = 0;
  const $ = (name) => {
    if (!out[name]) throw new TypeError(`node ${name} has not run`);
    const items = out[name][0] || [];
    return { first: () => items[0], item: items[0], all: () => items, last: () => items[items.length - 1] };
  };
  const trace = [];
  const effects = [];
  const calls = {};
  let seedCalls = 0;
  let lastOutput = null;
  let stoppedAt = null;
  const ctx = (it) => ({ json: (it || {}).json, $ });

  function pgParams(n, item) {
    const qr = ((n.parameters || {}).options || {}).queryReplacement;
    if (qr === undefined || qr === '') return [];
    const v = evalExpr(qr, ctx(item));
    if (Array.isArray(v)) return v.filter((x) => x !== undefined).map((x) => (typeof x === 'object' && x !== null ? JSON.stringify(x) : x));
    if (v === null) return { evalError: 'the queryReplacement expression did not evaluate (an unexecuted node?)' };
    return String(v).split(',');
  }

  function pgOnce(n, params) {        // ONE try of a Postgres node
    const name = n.name;
    calls[name] = (calls[name] || 0) + 1;
    if (name === 'Seed Reply Mode') seedCalls++;
    const list = name === 'Seed Reply Mode' && sc.seed ? sc.seed : (sc.pg || {})[name];
    let a;
    if (params && params.evalError) a = { throw: params.evalError };
    else if (list) a = list[Math.min(calls[name] - 1, list.length - 1)];
    else if (BRIDGE) a = bridge({ pg: { node: name, query: n.parameters.query, params, select: isSelectQuery(n.parameters.query) } });
    else a = { rows: [] };
    if (a.throw) throw new Error(a.throw);
    if (a.noItems) return [[]];
    if (a.queryError) {
      if (!(n.onError === 'continueRegularOutput' || n.onError === 'continueErrorOutput')) throw new Error(a.queryError);
      return [[{ json: { message: a.queryError, error: { message: a.queryError } } }]];
    }
    const rows = a.rows || [];
    if (rows.length) return [rows.map((r) => ({ json: r }))];
    return [isSelectQuery(n.parameters.query) ? [] : [{ json: { success: true } }]];
  }

  function httpAnswer(name, req) {
    calls[name] = (calls[name] || 0) + 1;
    let a = http[name];
    if (Array.isArray(a)) a = a[Math.min(calls[name] - 1, a.length - 1)];
    if (a === undefined) a = BRIDGE ? bridge({ http: req }) : { statusCode: 200, body: {} };
    if (a && a.throw) return { throw: a.throw };
    if (a && typeof a.statusCode === 'number') return { statusCode: a.statusCode, body: a.body === undefined ? {} : a.body };
    return { statusCode: 200, body: a === undefined || a === null ? {} : a };   // the legacy form: the node's json output
  }

  function httpOnce(n, items) {        // ONE try of an HTTP Request node, one request per input item
    const name = n.name;
    const resp = (((n.parameters || {}).options || {}).response || {}).response || {};
    const full = resp.fullResponse === true;
    const never = resp.neverError === true;
    const cont = n.onError === 'continueRegularOutput' || n.onError === 'continueErrorOutput';
    const ok = [];
    const bad = [];
    items.forEach((it, i) => {
      const method = n.parameters.method || 'GET';
      const url = evalExpr(n.parameters.url, ctx(it));
      let body;
      if (n.parameters.sendBody && n.parameters.specifyBody === 'json') {
        body = evalExpr(n.parameters.jsonBody, ctx(it));
        if (typeof body === 'string') { try { body = JSON.parse(body); } catch (e) { /* a literal body */ } }
      }
      const a = httpAnswer(name, { node: name, method, url, body });
      effects.push({ node: name, method, url, status: a.throw ? 'throw' : a.statusCode });
      let err = null;
      if (a.throw) err = a.throw;
      else if (!never && (a.statusCode < 200 || a.statusCode >= 300)) err = `${a.statusCode} - ${JSON.stringify(a.body)}`;
      if (err !== null) {
        if (!cont) throw new Error(name + ': ' + err);
        (n.onError === 'continueErrorOutput' ? bad : ok).push({ json: { error: { message: err } }, pairedItem: { item: i } });
      } else {
        ok.push({ json: full ? { statusCode: a.statusCode, headers: {}, body: a.body, statusMessage: '' } : a.body, pairedItem: { item: i } });
      }
    });
    return n.onError === 'continueErrorOutput' ? [ok, bad] : [ok];
  }

  function execTried(n, input, once) { // n8n-core's per-node try loop (retry, alwaysOutputData, continue*)
    const maxTries = n.retryOnFail === true ? Math.min(5, Math.max(2, n.maxTries || 3)) : 1;
    let data = null;
    let error;
    for (let tryIndex = 0; tryIndex < maxTries; tryIndex++) {
      try {
        error = undefined;
        data = once();
        let failed = isErrorValue(((data[0] || [])[0] || { json: {} }).json.error);
        while (failed && tryIndex !== maxTries - 1) {
          data = once();
          failed = isErrorValue(((data[0] || [])[0] || { json: {} }).json.error);
          tryIndex++;
        }
        if (n.onError === 'continueErrorOutput' && n.type === 'n8n-nodes-base.postgres') {
          const items = data[0] || [];
          data = [items.filter((it) => !isErrorItem(it)), items.filter(isErrorItem)];
        }
        if (!(data[0] || [])[0] && n.alwaysOutputData === true) data[0] = [{ json: {} }];
        break;
      } catch (e) {
        error = e;
      }
    }
    if (error !== undefined) {
      if (n.onError === 'continueRegularOutput' || n.onError === 'continueErrorOutput') return [input];
      throw error;
    }
    return data;
  }

  function exec(name, inItems) {
    const n = byName[name];
    if (!n) throw new Error('driver: no node ' + name);
    if (++steps > 300) throw new Error('driver: the graph loops (' + trace.slice(-6).join(' -> ') + ')');
    trace.push(name);
    const items = n.executeOnce === true ? inItems.slice(0, 1) : inItems;
    let outputs;
    switch (n.type) {
      case 'n8n-nodes-base.postgres': {
        const params = pgParams(n, items[0]);
        effects.push({ node: name, params });
        outputs = execTried(n, items, () => pgOnce(n, params));
        break;
      }
      case 'n8n-nodes-base.if': {
        const t = [], f = [];
        for (const it of items) {
          const conds = n.parameters.conditions.conditions.map((c) => condOk(c, ctx(it)));
          const ok = n.parameters.conditions.combinator === 'or' ? conds.some(Boolean) : conds.every(Boolean);
          (ok ? t : f).push(it);
        }
        outputs = [t, f];
        break;
      }
      case 'n8n-nodes-base.set': {
        const p = n.parameters;
        const dot = (p.options || {}).dotNotation !== false;
        outputs = [items.map((it, i) => {
          let json = {};
          if (p.includeOtherFields) {
            const base = JSON.parse(JSON.stringify(it.json || {}));
            const listed = String(p.includeFields || p.excludeFields || '').split(',').map((s) => s.trim()).filter(Boolean);
            if (p.include === 'selected') for (const k of listed) { if (k in base) json[k] = base[k]; }
            else if (p.include === 'except') { json = base; for (const k of listed) delete json[k]; }
            else json = base;
          }
          for (const a of p.assignments.assignments) setPath(json, a.name, evalExpr(a.value, ctx(it)), dot);
          return { json, pairedItem: { item: i } };
        })];
        break;
      }
      case 'n8n-nodes-base.httpRequest':
        outputs = execTried(n, items, () => httpOnce(n, items));
        break;
      case 'n8n-nodes-base.code': {
        if ((n.parameters.mode || 'runOnceForAllItems') !== 'runOnceForAllItems') throw new Error('driver: Code mode ' + n.parameters.mode);
        const fn = new Function('$input', '$', '$json', '$execution', n.parameters.jsCode);
        const res = fn({ first: () => items[0], all: () => items, last: () => items[items.length - 1], item: items[0] }, $,
          (items[0] || {}).json, { id: 'driver-exec' });
        outputs = [(res || []).map((r) => ({ json: r.json, pairedItem: r.pairedItem }))];
        break;
      }
      default:
        throw new Error('driver: unsupported node type ' + n.type + ' (' + name + ')');
    }
    out[name] = outputs;
    if (outputs.some((o) => o && o.length)) lastOutput = { node: name, json: (outputs.find((o) => o && o.length) || [])[0].json };
    const kids = [];
    ((wf.connections[name] || {}).main || []).forEach((list, o) => {
      if (outputs[o] && outputs[o].length) for (const c of list || []) kids.push({ c, items: outputs[o] });
    });
    kids.sort((a, b) => byName[a.c.node].position[1] - byName[b.c.node].position[1] || byName[a.c.node].position[0] - byName[b.c.node].position[0]);
    for (const k of kids) exec(k.c.node, k.items);
  }

  try {
    // the start node's input: what its real predecessor emits (Retire Same Bot Slot / Vertical Prompt) — never read
    exec(sc.start || 'Register Profile', [{ json: { success: true } }]);
  } catch (e) {
    stoppedAt = { node: trace[trace.length - 1], message: e.message };
  }
  return { trace, effects, seedCalls, calls, response: stoppedAt ? null : lastOutput, stoppedAt };
}

const wf = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const input = JSON.parse(fs.readFileSync(BRIDGE ? process.argv[3] : 0, 'utf8') || '{}');
const report = {};
for (const [k, sc] of Object.entries(input)) report[k] = runScenario(wf, sc);
process.stdout.write(JSON.stringify(BRIDGE ? { report } : report) + (BRIDGE ? '\n' : ''));
