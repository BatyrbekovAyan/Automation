#!/usr/bin/env node
// Pure, offline reproduction harness for the decision-bearing n8n Code nodes (audit stage 0.4).
//
// It executes the UNMODIFIED jsCode of a named Code node from a canonical workflow JSON with a
// minimal emulation of the n8n Code-node runtime ($('Node').first()/.item/.all(), $input, $json,
// $items, $execution) and asserts the CORRECT behaviour per scenario. Scenarios that document a
// confirmed audit defect are therefore RED against the current implementation on purpose — the
// stage that fixes the defect turns them green without touching the scenario file.
//
//   node Tools/n8n/repro/run-code-node.mjs                       # all scenarios, exit 1 if any FAIL
//   node Tools/n8n/repro/run-code-node.mjs --only human_reply    # substring filter
//   node Tools/n8n/repro/run-code-node.mjs --observe             # print observed values, never fail
//   node Tools/n8n/repro/run-code-node.mjs --dir <other workflows dir>   # e.g. a prod re-export
//
// No network, no n8n, no LLM: the harness only proves what the JavaScript decides.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const flag = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : null; };
const observe = args.includes('--observe');
const only = flag('--only');
const wfDir = flag('--dir') || path.join(here, '..', 'workflows');
const scenarios = JSON.parse(fs.readFileSync(path.join(here, 'scenarios.json'), 'utf8'));

const cache = new Map();
function loadCode(file, nodeName) {
  const k = `${file}::${nodeName}`;
  if (!cache.has(k)) {
    const wf = JSON.parse(fs.readFileSync(path.join(wfDir, file), 'utf8'));
    const node = wf.nodes.find((n) => n.name === nodeName);
    if (!node) throw new Error(`${file}: node ${nodeName} not found`);
    cache.set(k, node.parameters.jsCode);
  }
  return cache.get(k);
}

// Minimal Code-node runtime: nodes = { 'Webhook': [{json}], ... }, input = [{json}]
function runCode(js, { nodes = {}, input = [] }) {
  const wrap = (items) => ({
    first: () => items[0],
    last: () => items[items.length - 1],
    all: () => items,
    get item() { return items[0]; },
    get json() { return items[0] && items[0].json; },
  });
  const $ = (name) => {
    if (!(name in nodes)) throw new Error(`$('${name}') has no data in this scenario`);
    return wrap(nodes[name]);
  };
  const $input = wrap(input);
  const $json = input[0] ? input[0].json : {};
  const $items = () => input;
  const $execution = { id: 'repro-exec', mode: 'test' };
  const $prevNode = { name: 'repro' };
  const $now = new Date();
  const fn = new Function('$', '$input', '$json', '$items', '$execution', '$prevNode', '$now', js);
  return fn($, $input, $json, $items, $execution, $prevNode, $now);
}

function get(obj, dotted) {
  return dotted.split('.').reduce((o, k) => (o == null ? undefined : o[k]), obj);
}

function check(expect, out) {
  const failures = [];
  for (const [k, spec] of Object.entries(expect)) {
    const v = get(out, k);
    if (spec && typeof spec === 'object' && !Array.isArray(spec)) {
      if ('contains' in spec) for (const s of [].concat(spec.contains)) if (!(typeof v === 'string' && v.includes(s))) failures.push(`${k} should contain ${JSON.stringify(s)}, got ${JSON.stringify(v)}`);
      if ('notContains' in spec) for (const s of [].concat(spec.notContains)) if (typeof v === 'string' && v.includes(s)) failures.push(`${k} must not contain ${JSON.stringify(s)}`);
      if ('truthy' in spec) if (Boolean(v) !== spec.truthy) failures.push(`${k} truthiness should be ${spec.truthy}, got ${JSON.stringify(v)}`);
      if ('equals' in spec) if (JSON.stringify(v) !== JSON.stringify(spec.equals)) failures.push(`${k} should equal ${JSON.stringify(spec.equals)}, got ${JSON.stringify(v)}`);
      if ('minLength' in spec) if (!(typeof v === 'string' && v.length >= spec.minLength)) failures.push(`${k} should have length >= ${spec.minLength}, got ${JSON.stringify(v)}`);
    } else if (JSON.stringify(v) !== JSON.stringify(spec)) {
      failures.push(`${k} should equal ${JSON.stringify(spec)}, got ${JSON.stringify(v)}`);
    }
  }
  return failures;
}

let pass = 0, fail = 0, err = 0;
for (const sc of scenarios) {
  if (only && !sc.id.includes(only)) continue;
  for (const file of sc.workflows) {
    let out, failures;
    try {
      const js = loadCode(file, sc.node);
      const result = runCode(js, sc.ctx);
      out = Array.isArray(result) ? (result[0] && result[0].json) : (result && result.json) || result;
      failures = observe ? [] : check(sc.expect, out);
    } catch (e) {
      err++;
      console.log(`ERR   ${sc.id} [${file}] ${e.message}`);
      continue;
    }
    const observed = Object.fromEntries(Object.keys(sc.expect).map((k) => [k, get(out, k)]));
    if (failures.length) {
      fail++;
      console.log(`FAIL  ${sc.id} [${file}] — ${sc.title}${sc.defect ? `  (audit ${sc.defect})` : ''}`);
      for (const f of failures) console.log(`        ${f}`);
    } else {
      pass++;
      console.log(`${observe ? 'OBS ' : 'PASS'}  ${sc.id} [${file}] — ${sc.title}`);
    }
    if (observe || failures.length) console.log(`        observed: ${JSON.stringify(observed)}`);
  }
}
console.log(`\n${pass} pass, ${fail} fail, ${err} error (${observe ? 'observe mode' : 'assert mode'})`);
process.exit(!observe && (fail || err) ? 1 : 0);
