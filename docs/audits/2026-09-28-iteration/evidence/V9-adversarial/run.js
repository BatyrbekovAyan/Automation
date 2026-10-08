// Adversarial runner for V9 Reconcile Prices: executes the REAL canonical Code-node body (crPriceSearch001
// «Reconcile Prices») with a mocked $(), many cases in one process. Read-only over the repo.
// usage: node run.js cases.json  → prints JSON results
const fs = require('fs');
const path = require('path');
const ROOT = '/Users/ayan/Projects/Automation/Tools/n8n/workflows';
const sw = JSON.parse(fs.readFileSync(path.join(ROOT, 'crPriceSearch001-Price_List_Search.json'), 'utf8'));
const RESULT = sw.nodes.find(n => n.name === 'Reconcile Prices').parameters.jsCode;
const fn = new Function('$', RESULT);

function search(docs, prompt, opts) {
  opts = opts || {};
  const hits = opts.error ? [{ json: { error: 'x' } }]
    : (docs.length ? docs.map(d => ({ json: { document: { pageContent: d, metadata: {} }, score: 0.8 } })) : [{ json: {} }]);
  const named = {
    'Guard': [{ json: { ok: opts.ok !== false, query: 'q', filterKey: 'botWaId', botId: 'wfV9price00001', prompt } }],
    'Search Price Lists': hits,
  };
  const $ = (n) => { const items = named[n]; if (!items) throw new Error('not executed ' + n);
    return { first: () => items[0], all: () => items }; };
  return fn($)[0].json;
}

const catalog = (items) => items.map((it, i) => `${it.kind === 'service' ? 'Service' : 'Product'}${i + 1}: ${it.name}\n${it.kind === 'service' ? 'Service' : 'Product'}${i + 1} Price: ${it.price}\n${it.kind === 'service' ? 'Service' : 'Product'}${i + 1} Description: ${it.desc || 'x'}`).join('\n\n');
const promptOf = (items) => `Business Type: Автозапчасти\n\n<vertical prompt>\n\nAdditional Instructions:   \n\nAbout Business: x\n\nProducts:\n${catalog(items.filter(i => i.kind !== 'service'))}\n\nServices:\n${catalog(items.filter(i => i.kind === 'service'))}`;

const cases = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const out = [];
for (const c of cases) {
  const prompt = c.prompt !== undefined ? c.prompt : promptOf(c.items);
  let r;
  try { r = search(c.docs, prompt, c.opts); } catch (e) { r = { THROWN: String(e && e.stack || e) }; }
  const marks = (r.documents || []).flatMap(d => d.split('\n').filter(l => l.includes('[Сверка с каталогом')));
  const level = marks.map(m => /Цены расходятся\.\]$/.test(m) && !/Если это та же позиция/.test(m) ? 'CONFLICT' : (/Если это та же позиция/.test(m) ? 'MAYBE' : 'UNCLEAR'));
  out.push({ id: c.id, expect: c.expect, level, marks, docs: r.documents, note: r.note, thrown: r.THROWN });
}
console.log(JSON.stringify(out, null, 1));
