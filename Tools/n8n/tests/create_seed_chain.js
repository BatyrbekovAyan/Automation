// Behavioural driver for R8 (iteration 2026-09-27): runs the canonical Create WhatsApp/Telegram graph from its JSON,
// starting at `Seed Reply Mode`, under the outcomes a Postgres node can really have, and reports what the clone went
// through (activated? Wappi webhook set? bound? deleted?) and what the webhook (responseMode: lastNode) answered.
//
// The seed node is executed the way n8n-core 2.36.7 executes a node (workflow-execute.js): retryOnFail/maxTries
// (2..5, default 3) re-run it on a thrown error AND on a first item carrying json.error; after the last try a thrown
// error with onError continue* passes the node's INPUT items on output 0; an empty result becomes {} under
// alwaysOutputData; onError continueErrorOutput moves {error,message}-only items to output 1 (handleNodeErrorOutput).
// Each try's result comes from the scenario:
//   {rows:[…]}          a completed statement; no rows → n8n-nodes-base Postgres v2 (≥ 2.3) answers {success:true}
//                       for a non-SELECT statement and nothing for a SELECT (its own isSelectQuery decides)
//   {queryError:'msg'}  the Postgres node's continueOnFail item {message, error} (a failed query / lost connection)
//   {throw:'msg'}       an error raised outside the query runner (credentials, expression, pool)
//   {noItems:true}      the node returned no items at all
// Downstream: If (n8n's own expression engine) and Set are evaluated; HTTP Request and Postgres are side effects —
// recorded (method + evaluated URL), never executed. n8n executionOrder v1: children run depth-first, sorted by
// canvas position.
//
//   node create_seed_chain.js <Create.json> < {scenario: {body, createdId, seed:[…]}}  -> {scenario: report}
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');

const N = process.env.N8N_MODULES || path.join(os.homedir(), '.n8n-staging/app/node_modules');
const { evaluateExpression, setErrorHandler } = require(N + '/n8n-workflow/dist/cjs/expression-evaluator-proxy.js');
const { isSelectQuery } = require(N + '/n8n-nodes-base/dist/nodes/Postgres/v2/helpers/utils.js');
setErrorHandler((e) => { throw e; });
const GLOBALS = { String, Array, RegExp, Math, Number, JSON, Boolean, Object, Date };

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
  throw new Error('driver: unsupported operator ' + JSON.stringify(op));
}

const isErrorValue = (v) => v !== undefined && v !== null && v !== false;
const isErrorItem = (it) => it && it.json && it.json.error && Object.keys(it.json).every((k) => ['error', 'message', 'details'].includes(k));

function runScenario(wf, sc) {
  const byName = Object.fromEntries(wf.nodes.map((n) => [n.name, n]));
  const out = {
    'Unity Webhook': [[{ json: { body: sc.body, headers: {}, params: {}, query: {} } }]],
    'Auth Gate': [[{ json: { crAuth: { ok: true, installId: 'install-r8' } } }]],
    'Get Created Workflow Id': [[{ json: { id: sc.createdId } }]],
  };
  const $ = (name) => {
    if (!out[name]) throw new TypeError(`node ${name} has not run`);
    const items = out[name][0] || [];
    return { first: () => items[0], item: items[0], all: () => items, last: () => items[items.length - 1] };
  };
  const trace = [];
  const effects = [];
  let seedCalls = 0;
  let lastOutput = null;
  let stoppedAt = null;

  function seedOnce(n) {
    const a = sc.seed[Math.min(seedCalls, sc.seed.length - 1)];
    seedCalls++;
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

  function execSeed(n, input) {            // n8n-core's per-node try loop (retry, alwaysOutputData, continue*)
    const maxTries = n.retryOnFail === true ? Math.min(5, Math.max(2, n.maxTries || 3)) : 1;
    let data = null;
    let error;
    for (let tryIndex = 0; tryIndex < maxTries; tryIndex++) {
      try {
        error = undefined;
        data = seedOnce(n);
        let failed = isErrorValue(((data[0] || [])[0] || { json: {} }).json.error);
        while (failed && tryIndex !== maxTries - 1) {
          data = seedOnce(n);
          failed = isErrorValue(((data[0] || [])[0] || { json: {} }).json.error);
          tryIndex++;
        }
        if (n.onError === 'continueErrorOutput') {
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

  function exec(name, items) {
    const n = byName[name];
    if (!n) throw new Error('driver: no node ' + name);
    trace.push(name);
    const ctx = (it) => ({ json: it.json, $ });
    let outputs;
    switch (n.type) {
      case 'n8n-nodes-base.postgres':
        if (name === 'Seed Reply Mode') {
          outputs = execSeed(n, items);
        } else {
          effects.push({ node: name });
          outputs = [[{ json: { success: true } }]];
        }
        break;
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
      case 'n8n-nodes-base.set':
        outputs = [items.map((it) => {
          const json = n.parameters.includeOtherFields ? { ...it.json } : {};
          for (const a of n.parameters.assignments.assignments) json[a.name] = evalExpr(a.value, ctx(it));
          return { json };
        })];
        break;
      case 'n8n-nodes-base.httpRequest':
        effects.push({ node: name, method: n.parameters.method || 'GET', url: evalExpr(n.parameters.url, ctx(items[0])) });
        outputs = [[{ json: (sc.http || {})[name] || {} }]];
        break;
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
    exec('Seed Reply Mode', out['Get Created Workflow Id'][0]);
  } catch (e) {
    stoppedAt = { node: trace[trace.length - 1], message: e.message };
  }
  return { trace, effects, seedCalls, response: stoppedAt ? null : lastOutput, stoppedAt };
}

const wf = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}');
const report = {};
for (const [k, sc] of Object.entries(input)) report[k] = runScenario(wf, sc);
process.stdout.write(JSON.stringify(report));
