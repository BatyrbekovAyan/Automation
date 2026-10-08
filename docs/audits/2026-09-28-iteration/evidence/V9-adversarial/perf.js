const fs = require('fs');
const sw = JSON.parse(fs.readFileSync('/Users/ayan/Projects/Automation/Tools/n8n/workflows/crPriceSearch001-Price_List_Search.json', 'utf8'));
const RESULT = sw.nodes.find(n => n.name === 'Reconcile Prices').parameters.jsCode;
const items = Array.from({ length: 300 }, (_, i) => `Product${i + 1}: Деталь ${i} Toyota ${10000 + i}-YZZ${i % 10}\nProduct${i + 1} Price: ${1000 + i} тг\nProduct${i + 1} Description: ${'описание '.repeat(30)}`).join('\n\n');
const prompt = 'x'.repeat(25000) + '\nProducts:\n' + items;
const docs = Array.from({ length: 10 }, (_, k) => Array.from({ length: 25 }, (_, j) => `product[${k * 25 + j}]: Артикул: ${10000 + k * 25 + j}-YZZ${j % 10}; Наименование: Деталь; Цена опт: ${900 + j}; Цена розн: ${1500 + j}; `).join('\n').slice(0, 1000));
const named = { 'Guard': [{ json: { ok: true, prompt } }], 'Search Price Lists': docs.map(d => ({ json: { document: { pageContent: d } } })) };
const $ = (n) => ({ first: () => named[n][0], all: () => named[n] });
let t = process.hrtime.bigint();
const fn = new Function('$', RESULT); const compileNs = process.hrtime.bigint() - t;
t = process.hrtime.bigint(); let r; for (let i = 0; i < 20; i++) r = fn($); const runNs = (process.hrtime.bigint() - t) / 20n;
console.log('prompt chars', prompt.length, 'docs', docs.length, '| compile ms', Number(compileNs) / 1e6, '| per-call ms', Number(runNs) / 1e6,
  '| marks', r[0].json.documents.join('\n').split('\n').filter(l => l.startsWith('[Сверка')).length);
