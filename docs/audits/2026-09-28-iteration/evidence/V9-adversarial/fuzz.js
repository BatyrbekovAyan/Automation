// Random-unicode fuzz of the REAL Reconcile Prices body: looking for throws, non-idempotence, row mutation.
const fs = require('fs');
const sw = JSON.parse(fs.readFileSync('/Users/ayan/Projects/Automation/Tools/n8n/workflows/crPriceSearch001-Price_List_Search.json', 'utf8'));
const RESULT = sw.nodes.find(n => n.name === 'Reconcile Prices').parameters.jsCode;
const fn = new Function('$', RESULT);
function run(docs, prompt) {
  const named = { 'Guard': [{ json: { ok: true, prompt } }], 'Search Price Lists': docs.map(d => ({ json: { document: { pageContent: d } } })) };
  return fn((n) => ({ first: () => named[n][0], all: () => named[n] })) [0].json.documents;
}
let seed = 12345; const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
const pick = (a) => a[Math.floor(rnd() * a.length)];
const atoms = ['90915-YZZE1', '90915YZZE1', 'yzze1', '04465-02220', 'C-1139', 'Фильтр', 'масляный', 'Toyota', '3200', '3 200', '3900', '3.900,00',
  'тг', '₸', 'тенге', 'теңге', 'kzt', '/шт', '/', '-', '–', '—', ';', ':', ',', '.', '(', ')', '[', ']', '«', '»', '\u0000', '\ud800', '\udfff',
  '​', '‮', '́', 'é', '😀', '\r', '\n', '\t', 'Цена', 'Цена опт', 'Цена розн', 'product[1]:', 'Артикул:', 'аналог', 'от', 'до',
  '1999', '2019', '5W-30', '205/55R16', ' ', ' ', ' ', '[Сверка с каталогом: ', 'Product1: ', 'Product1 Price: ', '$', '€', '%'];
const gen = (n) => Array.from({ length: n }, () => pick(atoms)).join(pick(['', ' ', ' ', '  ']));
let thrown = 0, nonIdem = 0, mutated = 0, n = 0;
const MARK = '[Сверка с каталогом: ';
const strip = (t) => t.split('\n').filter(l => !l.startsWith(MARK)).join('\n');
const fullMark = /^\s*\[Сверка с каталогом: .*\]\s*$/u;
for (let i = 0; i < 3000; i++) {
  const prompt = 'Products:\n' + Array.from({ length: 1 + Math.floor(rnd() * 3) }, (_, k) => `Product${k + 1}: ${gen(1 + Math.floor(rnd() * 6))} 90915-YZZE1\nProduct${k + 1} Price: ${gen(1 + Math.floor(rnd() * 3))}`).join('\n\n');
  const docs = Array.from({ length: 1 + Math.floor(rnd() * 3) }, () => "x " + gen(1 + Math.floor(rnd() * 14)));
  n++;
  let a, b;
  try { a = run(docs, prompt); b = run(a, prompt); } catch (e) { thrown++; if (thrown < 4) console.log('THROW', e.message, JSON.stringify(docs), JSON.stringify(prompt)); continue; }
  if (JSON.stringify(a) !== JSON.stringify(b)) { nonIdem++; if (nonIdem < 4) console.log('NONIDEM', JSON.stringify(docs), '\n  A', JSON.stringify(a), '\n  B', JSON.stringify(b)); }
  for (let k = 0; k < docs.length; k++) {
    const want = docs[k].split('\n').filter(l => !fullMark.test(l)).join('\n');
    if (strip(a[k] || '') !== strip(want)) { mutated++; if (mutated < 4) console.log('MUTATED', JSON.stringify(docs[k]), '->', JSON.stringify(a[k])); }
  }
}
console.log({ n, thrown, nonIdem, mutated });
