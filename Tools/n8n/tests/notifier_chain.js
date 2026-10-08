// Behavioural driver for F3 (iteration 2026-09-28, final regression 2026-10-08): runs ONE schedule tick of the canonical
// Handoff Notifier graph from its JSON, under the outcomes its Postgres nodes can really have, and reports every transport
// call it would make (method + evaluated URL + query + body) and every statement it would run (evaluated parameters).
//
// Semantics follow n8n-core / n8n-nodes-base 2.36.7 (the staging install = prod version), read from their sources:
//  * Postgres v2.6 executeQuery (queryBatching single): one statement per input item, `queryReplacement` evaluated per
//    item; rows -> items paired to their input item ({item: i}, utils.js configureQueryRunner); a SELECT with no rows ->
//    nothing, which workflow-execute turns into ONE empty item `{}` under alwaysOutputData; a failed statement under
//    continueOnFail -> `{message, error}` (no pairedItem); an error thrown outside the query runner under onError
//    continue* -> the node's INPUT items pass on unchanged (workflow-execute, continuesOnError).
//  * before ANY node runs, the engine re-pairs each input item to its own index ({item: i}, workflow-execute.js, the
//    `newTaskDataConnections` map) — so a pass-through node (If) keeps the ancestry exact even though Postgres pairs all
//    of a statement's rows to {item: 0}. Confirmed on the staging n8n 2.36.7 + task runners (2026-10-08 probe:
//    Postgres 3 rows → If → HTTP → per-item Code read `$('<postgres>').item` = each item's own row).
//  * If v2.2: conditions through n8n's own expression engine, per item; items pass through unchanged.
//  * Code: the node's jsCode; runOnceForEachItem pairs output k to input k (the JS task runner does the same);
//    runOnceForAllItems keeps a returned pairedItem, else assignPairedItems' rules. `$('Node').item` resolves through
//    pairedItem the way WorkflowDataProxy.getPairedItem walks the ancestry (stop AT the named node, read its output).
//  * HTTP Request: recorded, never executed; output k paired to input k; answers the scenario's response
//    `http[<node>] = {byProfile: {<query profile_id>: resp}, default: resp}` (resp = the fullResponse shape).
//  * a node runs only when it receives items; executionOrder v1: depth-first, children by canvas position (y, then x);
//    a node fed by two branches runs once per arrival (its own run index, used by the paired-item walk).
//
//   node notifier_chain.js <Handoff_Notifier.json> < {scenario: {pg: {<node>: outcome}, http: {...}}}  -> {scenario: report}
//   outcome: {rows: [...]} | {empty: true} | {queryError: 'msg'} | {throw: 'msg'}; an unlisted Postgres node answers one
//   row {ok: true} per input item.
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');

const N = process.env.N8N_MODULES || path.join(os.homedir(), '.n8n-staging/app/node_modules');
const { evaluateExpression, setErrorHandler } = require(N + '/n8n-workflow/dist/cjs/expression-evaluator-proxy.js');
const { isSelectQuery } = require(N + '/n8n-nodes-base/dist/nodes/Postgres/v2/helpers/utils.js');
setErrorHandler((e) => { throw e; });
const GLOBALS = { String, Array, RegExp, Math, Number, JSON, Boolean, Object, Date };

// what a Schedule Trigger emits: a timestamp item, never an id or a profile
const TRIGGER_ITEM = { timestamp: '2026-10-08T17:38:00.004+05:00', 'Readable date': 'October 8th 2026, 5:38:00 pm',
  'Readable time': '5:38:00 pm', 'Day of week': 'Thursday', Year: '2026', Month: 'October', 'Day of month': '08',
  Hour: '17', Minute: '38', Second: '00', Timezone: 'Asia/Almaty (UTC+05:00)' };

function runScenario(wf, sc) {
  const byName = Object.fromEntries(wf.nodes.map((n) => [n.name, n]));
  const runData = {};          // name -> [{outputs: [[item]], source: [{previousNode, previousNodeOutput, previousNodeRun}] | null}]
  const trace = [];
  const effects = [];
  let steps = 0;

  function normalizePaired(p) {
    if (p === undefined || p === null) return [];
    if (typeof p === 'number') return [{ item: p }];
    return Array.isArray(p) ? p.map((x) => (typeof x === 'number' ? { item: x } : x)) : [p];
  }

  // WorkflowDataProxy.getPairedItem: walk from the CURRENT node's input item back to `dest`
  function getPairedItem(dest, sourceData, paired) {
    if (!sourceData) throw new Error(`paired item: no path to ${dest}`);
    const task = (runData[sourceData.previousNode] || [])[sourceData.previousNodeRun || 0];
    if (!task) throw new Error(`paired item: ${sourceData.previousNode} has not run`);
    const out = task.outputs[sourceData.previousNodeOutput || 0] || [];
    const item = out[paired.item];
    if (sourceData.previousNode === dest) {
      if (!item) throw new Error(`paired item: ${dest} has no item ${paired.item}`);
      return item;
    }
    if (!item) throw new Error(`paired item: ${sourceData.previousNode} has no item ${paired.item}`);
    const next = normalizePaired(item.pairedItem);
    if (!next.length) throw new Error(`paired item: ${sourceData.previousNode} item ${paired.item} carries no pairedItem`);
    const found = [];
    for (const p of next) {
      const src = (task.source || [])[p.input || 0];
      if (!src) continue;
      found.push(getPairedItem(dest, src, { item: p.item, input: p.input || 0 }));
    }
    if (!found.length) throw new Error(`paired item: no branch to ${dest}`);
    if (found.some((f) => f !== found[0])) throw new Error(`paired item: several items of ${dest}`);
    return found[0];
  }

  function dollar(source, itemIndex) {
    return (name) => {
      const runs = runData[name];
      if (!runs || !runs.length) throw new TypeError(`node ${name} has not run`);
      const items = runs[runs.length - 1].outputs[0] || [];
      const api = { first: () => items[0], last: () => items[items.length - 1], all: () => items,
                    isExecuted: true };
      Object.defineProperty(api, 'item', { get: () => getPairedItem(name, (source || [])[0], { item: itemIndex }) });
      return api;
    };
  }

  function evalExpr(raw, json, $) {
    if (typeof raw !== 'string' || !raw.startsWith('=')) return raw;
    try {
      return evaluateExpression(raw.slice(1), { ...GLOBALS, $json: json, $ });
    } catch (e) {
      if (e instanceof TypeError) return null;       // n8n's legacy engine renders a TypeError as null
      throw e;
    }
  }

  function condOk(c, json, $) {
    const left = evalExpr(c.leftValue, json, $);
    const op = c.operator || {};
    if (op.type === 'boolean' && op.operation === 'true') return left === true;
    if (op.type === 'boolean' && op.operation === 'false') return left === false;
    if (op.type === 'string' && op.operation === 'notEmpty') return left !== undefined && left !== null && String(left) !== '';
    if (op.type === 'string' && op.operation === 'empty') return left === undefined || left === null || String(left) === '';
    throw new Error('driver: unsupported operator ' + JSON.stringify(op));
  }

  function parseMaybe(v) {
    if (typeof v !== 'string') return v;
    try { return JSON.parse(v); } catch (e) { return v; }
  }

  function postgres(n, items, source) {
    const outcome = (sc.pg || {})[n.name];
    const params = items.map((it, i) => {
      const r = evalExpr(n.parameters.options && n.parameters.options.queryReplacement, it.json, dollar(source, i));
      return Array.isArray(r) ? r : (r === undefined || r === null ? [] : [r]);
    });
    params.forEach((p, i) => effects.push({ node: n.name, kind: 'db', params: p, item: i }));
    const continues = n.onError === 'continueRegularOutput' || n.onError === 'continueErrorOutput';
    if (outcome && outcome.throw) {
      if (!continues) throw new Error(outcome.throw);
      return [items];                                 // the INPUT items pass on, pairedItem and all
    }
    if (outcome && outcome.queryError) {
      if (!continues) throw new Error(outcome.queryError);
      return [[{ json: { message: outcome.queryError, error: { message: outcome.queryError } } }]];
    }
    let out = [];
    if (outcome && (outcome.rows || outcome.empty)) {
      out = (outcome.rows || []).map((r) => ({ json: r, pairedItem: { item: 0 } }));     // one statement: input item 0
      if (!out.length && !isSelectQuery(n.parameters.query)) out = [{ json: { success: true }, pairedItem: { item: 0 } }];
    } else {
      out = items.map((_, i) => ({ json: { ok: true }, pairedItem: { item: i } }));
    }
    return [out];
  }

  function http(n, items) {
    const p = n.parameters;
    const conf = (sc.http || {})[n.name] || {};
    return [items.map((it, i) => {
      const e = { node: n.name, kind: 'http', method: p.method || 'GET', url: evalExpr(p.url, it.json, null), item: i };
      if (p.sendQuery) e.query = parseMaybe(evalExpr(p.jsonQuery, it.json, null)) || {};
      if (p.sendBody) e.body = parseMaybe(evalExpr(p.jsonBody, it.json, null)) || {};
      effects.push(e);
      const pid = e.query && e.query.profile_id;
      const resp = ((conf.byProfile || {})[pid]) || conf.default || { statusCode: 200, body: {} };
      return { json: JSON.parse(JSON.stringify(resp)), pairedItem: { item: i } };
    })];
  }

  function code(n, items, source) {
    const js = n.parameters.jsCode;
    const fn = new Function('$input', '$', '$json', '$execution', '$env', js);
    const exec = { id: 'notifier-driver' };
    if (n.parameters.mode === 'runOnceForEachItem') {
      return [items.map((it, k) => {
        const inp = { item: it, first: () => items[0], all: () => items, last: () => items[items.length - 1] };
        const r = fn(inp, dollar(source, k), it.json, exec, {});
        if (r === undefined || r === null) throw new Error(`driver: ${n.name} returned nothing for item ${k}`);
        const json = (r && typeof r === 'object' && 'json' in r) ? r.json : r;
        return { json, pairedItem: { item: k } };
      })];
    }
    const inp = { first: () => items[0], all: () => items, last: () => items[items.length - 1] };
    const r = fn(inp, dollar(source, 0), (items[0] || {}).json, exec, {});
    const list = (Array.isArray(r) ? r : [r]).filter((x) => x !== undefined && x !== null)
      .map((x) => ((x && typeof x === 'object' && 'json' in x) ? { ...x } : { json: x }));
    list.forEach((x, idx) => {
      if (x.pairedItem === undefined) {
        if (items.length === 1) x.pairedItem = { item: 0 };
        else if (items.length === list.length) x.pairedItem = { item: idx };
      }
    });
    return [list];
  }

  function exec(name, items, source) {
    const n = byName[name];
    if (!n) throw new Error('driver: no node ' + name);
    if (++steps > 200) throw new Error('driver: the graph loops (' + trace.slice(-6).join(' -> ') + ')');
    trace.push(name);
    items = items.map((it, i) => ({ ...it, pairedItem: { item: i } }));      // engine: re-pair the input to this node
    let outputs;
    switch (n.type) {
      case 'n8n-nodes-base.postgres': outputs = postgres(n, items, source); break;
      case 'n8n-nodes-base.httpRequest': outputs = http(n, items); break;
      case 'n8n-nodes-base.code': outputs = code(n, items, source); break;
      case 'n8n-nodes-base.if': {
        const t = [], f = [];
        items.forEach((it, i) => {
          const conds = n.parameters.conditions.conditions.map((c) => condOk(c, it.json, dollar(source, i)));
          const ok = n.parameters.conditions.combinator === 'or' ? conds.some(Boolean) : conds.every(Boolean);
          (ok ? t : f).push(it);
        });
        outputs = [t, f];
        break;
      }
      default:
        throw new Error('driver: unsupported node type ' + n.type + ' (' + name + ')');
    }
    // engine (assignPairedItems): an item a node returned without pairing is paired when the mapping is unambiguous
    for (const list of outputs) {
      (list || []).forEach((x, idx) => {
        if (x.pairedItem !== undefined) return;
        if (items.length === 1 && list.length === 1) x.pairedItem = { item: 0 };
        else if (outputs.length === 1 && items.length === list.length) x.pairedItem = { item: idx };
      });
    }
    // engine: an empty first output becomes ONE empty item under alwaysOutputData
    if (!(outputs[0] || [])[0] && n.alwaysOutputData === true) {
      outputs[0] = [{ json: {}, pairedItem: items.map((_, i) => ({ item: i, input: 0 })) }];
    }
    runData[name] = runData[name] || [];
    const runIndex = runData[name].length;
    runData[name].push({ outputs, source });
    const kids = [];
    ((wf.connections[name] || {}).main || []).forEach((list, o) => {
      if (outputs[o] && outputs[o].length) {
        for (const c of list || []) kids.push({ c, items: outputs[o], src: [{ previousNode: name, previousNodeOutput: o, previousNodeRun: runIndex }] });
      }
    });
    kids.sort((a, b) => byName[a.c.node].position[1] - byName[b.c.node].position[1] || byName[a.c.node].position[0] - byName[b.c.node].position[0]);
    for (const k of kids) exec(k.c.node, k.items, k.src);
  }

  const trigger = wf.nodes.find((n) => n.type === 'n8n-nodes-base.scheduleTrigger');
  let stoppedAt = null;
  try {
    trace.push(trigger.name);
    runData[trigger.name] = [{ outputs: [[{ json: { ...TRIGGER_ITEM } }]], source: null }];
    const kids = (((wf.connections[trigger.name] || {}).main || [])[0] || []).slice()
      .sort((a, b) => byName[a.node].position[1] - byName[b.node].position[1] || byName[a.node].position[0] - byName[b.node].position[0]);
    for (const c of kids) {
      exec(c.node, runData[trigger.name][0].outputs[0], [{ previousNode: trigger.name, previousNodeOutput: 0, previousNodeRun: 0 }]);
    }
  } catch (e) {
    stoppedAt = { node: trace[trace.length - 1], message: e.message };
  }
  const runs = Object.fromEntries(Object.entries(runData).map(([k, v]) => [k, v.length]));
  return { trace, effects, runs, stoppedAt };
}

const wf = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}');
const report = {};
for (const [k, sc] of Object.entries(input)) report[k] = runScenario(wf, sc);
process.stdout.write(JSON.stringify(report));
