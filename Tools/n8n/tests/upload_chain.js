// Behavioural driver for R3 (iteration 2026-09-27): executes the canonical Upload File graph from its JSON,
// from the multipart item a Unity WWWForm produces (every AddField part lands in item.binary as a text/plain
// part, body = {}), with the REAL n8n node code where the node type decides the outcome:
//   * Code nodes  -> the node's own jsCode, result through n8n's validateRunCodeAllItems + normalizeItems
//                    (the path that silently drops a `binary` the code did not return);
//   * Extract From File (text) -> n8n-nodes-base's moveTo.operation + n8n-core's assertBinaryData /
//                    getBinaryDataBuffer (the node that hard-fails when a binary part is missing);
//   * If / Switch / Set / Merge -> evaluated from their parameters (expressions as JS; a TypeError reads as
//                    null like n8n's expression engine).
// Side-effect nodes are NOT executed, only recorded: Postgres (evaluated queryReplacement), HTTP Request
// (evaluated URL + whether it got the file), LangChain nodes (the json they would receive), respondToWebhook
// (the first response wins, like the webhook's single reply).
// Modules: N8N_MODULES or ~/.n8n-staging/app/node_modules (the staging install = prod version 2.36.7).
//
//   node upload_chain.js chain <Upload_File.json>   < scenarios.json   -> {scenario: report}
//   node upload_chain.js gates <file.json> [...]    < {binary}          -> {file: {binaryKept, ok}}
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');

const N = process.env.N8N_MODULES || path.join(os.homedir(), '.n8n-staging/app/node_modules');
const { normalizeItems } = require(N + '/n8n-core/dist/execution-engine/node-execution-context/utils/normalize-items.js');
const bh = require(N + '/n8n-core/dist/execution-engine/node-execution-context/utils/binary-helper-functions.js');
const { validateRunCodeAllItems } = require(N + '/n8n-nodes-base/dist/nodes/Code/result-validation.js');
const moveTo = require(N + '/n8n-nodes-base/dist/nodes/Files/ExtractFromFile/actions/moveTo.operation.js');
const crAuth = require(path.join(__dirname, '..', 'auth', 'token.js'));

const SECRET = 'synthetic-upload-secret';
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

function bin(text, fileName, ext, mime) {
  const o = { data: Buffer.from(text, 'utf8').toString('base64'), mimeType: mime || 'text/plain' };
  if (fileName) { o.fileName = fileName; o.fileExtension = ext; }
  return o;
}

// n8n's own expression engine (@n8n/tournament + the sandboxing hooks n8n-workflow installs): template
// splitting, sanitisers and the language subset are the server's, not an approximation.
const { evaluateExpression, setErrorHandler } = require(N + '/n8n-workflow/dist/cjs/expression-evaluator-proxy.js');
setErrorHandler((e) => { throw e; });
const GLOBALS = { String, Array, RegExp, Math, Number, JSON, Boolean, Object, Date, parseInt, parseFloat, isNaN,
  encodeURIComponent, decodeURIComponent };

function evalExpr(raw, ctx) {
  if (typeof raw !== 'string' || !raw.startsWith('=')) return raw;
  try {
    return evaluateExpression(raw.slice(1), { ...GLOBALS, $json: ctx.json, $binary: ctx.binary || {}, $: ctx.$ });
  } catch (e) {
    if (e instanceof TypeError) return null;   // n8n's legacy expression engine renders a TypeError as null
    throw e;
  }
}

function condOk(c, ctx) {
  const left = evalExpr(c.leftValue, ctx);
  const right = evalExpr(c.rightValue, ctx);
  const op = c.operator || {};
  switch (op.operation) {
    case 'true': return left === true;
    case 'false': return left === false;
    case 'equals': return left === right;
    case 'notEquals': return left !== right;
    case 'notContains': return typeof left === 'string' && !left.includes(right);
    case 'contains': return typeof left === 'string' && left.includes(right);
    case 'notEmpty': return left !== undefined && left !== null && left !== '';
    case 'empty': return left === undefined || left === null || left === '';
    default: throw new Error('driver: unsupported operator ' + JSON.stringify(op));
  }
}

async function runCode(jsCode, items, env, $) {
  const fn = new AsyncFunction('$input', '$env', 'require', 'Buffer', '$', jsCode);
  const res = await fn({ first: () => items[0], all: () => items, last: () => items[items.length - 1] }, env, require, Buffer, $);
  return validateRunCodeAllItems(res, { object: { singular: 'object', plural: 'objects' } }, normalizeItems);
}

async function runScenario(wf, sc) {
  const byName = Object.fromEntries(wf.nodes.map((n) => [n.name, n]));
  const out = {};
  const trace = [];
  const events = { sideEffects: [] };
  const mergeInputs = {};
  const $ = (name) => {
    const items = out[name];
    if (!items) throw new TypeError(`node ${name} has not run`);
    return { first: () => items[0], item: items[0], all: () => items, last: () => items[items.length - 1] };
  };
  const env = { CR_AUTH_SECRET: SECRET, CR_AUTH_COMPAT_UNTIL: sc.compatOpen ? '2999-01-01T00:00:00Z' : '2000-01-01T00:00:00Z' };
  async function exec(name, items, inputIndex) {
    const n = byName[name];
    if (!n) throw new Error('driver: no node ' + name);
    trace.push(name);
    const ctx = (it) => ({ json: it.json, binary: it.binary, $ });
    let outputs;
    switch (n.type) {
      case 'n8n-nodes-base.webhook':
        outputs = [items];
        break;
      case 'n8n-nodes-base.code':
        outputs = [await runCode(n.parameters.jsCode, items, env, $)];
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
          return it.binary ? { json, binary: it.binary } : { json };
        })];
        break;
      case 'n8n-nodes-base.extractFromFile': {
        const p = n.parameters;
        if (p.operation !== 'text') { events[name] = 'not executed (non-text op)'; return; }
        const inputData = { main: [items] };
        const self = {
          getWorkflowSettings: () => ({ binaryMode: undefined }),
          getNodeParameter: (k, i, d) => (k === 'options' ? (p.options || {}) : k === 'binaryPropertyName' ? String(p.binaryPropertyName).replace(/^=/, '')
            : k === 'destinationKey' ? p.destinationKey : d),
          helpers: {
            assertBinaryData: (i, prop) => bh.assertBinaryData(inputData, n, i, prop, 0),
            getBinaryDataBuffer: (i, prop) => bh.getBinaryDataBuffer(inputData, i, prop, 0),
            detectBinaryEncoding: (buf) => bh.detectBinaryEncoding(buf),
          },
          continueOnFail: () => false,
          getNode: () => n,
        };
        outputs = [await moveTo.execute.call(self, items, 'text')];
        break;
      }
      case 'n8n-nodes-base.switch': {
        const rules = n.parameters.rules.values;
        const res = rules.map(() => []);
        if ((n.parameters.options || {}).fallbackOutput === 'extra') res.push([]);
        for (const it of items) {
          const idx = rules.findIndex((r) => {
            const vals = r.conditions.conditions.map((c) => condOk(c, ctx(it)));
            return r.conditions.combinator === 'or' ? vals.some(Boolean) : vals.every(Boolean);
          });
          res[idx >= 0 ? idx : res.length - 1].push(it);
        }
        outputs = res;
        break;
      }
      case 'n8n-nodes-base.merge': {
        mergeInputs[name] = mergeInputs[name] || {};
        mergeInputs[name][inputIndex] = items;
        const m = mergeInputs[name];
        if (!(m[0] && m[1])) return;                      // v1 order: waits for the other input
        outputs = [m[0].map((a, i) => ({ json: { ...(m[1][i] || {}).json, ...a.json }, binary: { ...((m[1][i] || {}).binary || {}), ...(a.binary || {}) } }))];
        break;
      }
      case 'n8n-nodes-base.postgres':
        events.sideEffects.push(name);
        events[name] = { params: evalExpr(n.parameters.options.queryReplacement, ctx(items[0])), query: n.parameters.query };
        outputs = [[{ json: {} }]];
        break;
      case 'n8n-nodes-base.respondToWebhook':
        if (!events.response) {
          const code = (n.parameters.options || {}).responseCode;
          events.response = { node: name, code: Number(evalExpr(code === undefined ? 200 : code, ctx(items[0]))),
            body: evalExpr(n.parameters.responseBody, ctx(items[0])) };
        }
        outputs = [items];
        break;
      case 'n8n-nodes-base.httpRequest':
        events.sideEffects.push(name);
        events[name] = { url: evalExpr(n.parameters.url, ctx(items[0])), gotFile: !!(items[0].binary && items[0].binary[n.parameters.inputDataFieldName || 'data']) };
        return;
      default:
        events.sideEffects.push(name);
        events[name] = { reached: true, json: items[0].json };
        return;
    }
    out[name] = outputs.flat();
    const conns = (wf.connections[name] || {}).main || [];
    for (let o = 0; o < conns.length; o++) {
      if (!outputs[o] || !outputs[o].length) continue;
      for (const c of conns[o] || []) await exec(c.node, outputs[o], c.index || 0);
    }
  }
  const now = Math.floor(Date.now() / 1000);
  const headers = { 'content-type': 'multipart/form-data; boundary=x' };
  if (sc.claims) headers.authorization = 'Bearer ' + crAuth.sign({ ...sc.claims, e: now + 3600, n: 1 }, SECRET);
  const f = sc.fields;
  const item = {
    json: { headers, params: {}, query: {}, body: {}, webhookUrl: 'http://localhost/webhook/UploadFile', executionMode: 'production' },
    binary: { whatsappWorkflowId: bin(f.whatsappWorkflowId), telegramWorkflowId: bin(f.telegramWorkflowId), contentType: bin(f.contentType),
      fileId: bin(f.fileId), data: bin(sc.file.text, sc.file.name, sc.file.ext, sc.file.mime) },
  };
  let error = null;
  try { await exec('Webhook', [item], 0); } catch (e) { error = { node: trace[trace.length - 1], message: e.message }; }
  const gate = out['Auth Gate'] ? out['Auth Gate'][0] : null;
  return {
    gate: gate ? { ok: gate.json.crAuth.ok, mode: gate.json.crAuth.mode, reason: gate.json.crAuth.reason, binaryKeys: Object.keys(gate.binary || {}) } : null,
    error,
    response: events.response || null,
    extractedText: out['Extract from TXT'] ? out['Extract from TXT'][0].json.text : null,
    vectorStoreInput: events['Supabase Vector Store'] ? events['Supabase Vector Store'].json : null,
    registerUpload: events['Register Upload'] || null,
    storeOriginal: events['Store Original File'] || null,
    sideEffects: events.sideEffects,
    trace,
  };
}

async function gates(files, payload) {
  const report = {};
  for (const file of files) {
    const wf = JSON.parse(fs.readFileSync(file, 'utf8'));
    const g = wf.nodes.find((n) => n.name === 'Auth Gate');
    if (!g) { report[path.basename(file)] = { gate: false }; continue; }
    const item = { json: { headers: {}, params: {}, query: {}, body: {} }, binary: { data: bin(payload.text, 'price.txt', 'txt') } };
    const env = { CR_AUTH_SECRET: SECRET, CR_AUTH_COMPAT_UNTIL: '2999-01-01T00:00:00Z' };
    const res = await runCode(g.parameters.jsCode, [item], env, () => { throw new TypeError('no $ in gate'); });
    const b = res[0].binary && res[0].binary.data;
    report[path.basename(file)] = { gate: true, binaryKept: !!b && Buffer.from(b.data, 'base64').toString('utf8') === payload.text,
      crAuth: !!res[0].json.crAuth, bodyKept: 'body' in res[0].json };
  }
  return report;
}

(async () => {
  const [mode, ...files] = process.argv.slice(2);
  const input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}');
  let report;
  if (mode === 'chain') {
    const wf = JSON.parse(fs.readFileSync(files[0], 'utf8'));
    report = {};
    for (const [k, sc] of Object.entries(input)) report[k] = await runScenario(wf, sc);
  } else if (mode === 'gates') {
    report = await gates(files, input);
  } else {
    throw new Error('usage: upload_chain.js chain|gates <files…>');
  }
  process.stdout.write(JSON.stringify(report));
})().catch((e) => { console.error(e && e.stack || e); process.exit(1); });
