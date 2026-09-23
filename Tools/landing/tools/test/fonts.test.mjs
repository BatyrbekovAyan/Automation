// Every character the page renders must be drawn by Onest, not by a system fallback font:
// ₸ used to sit outside the cyrillic/latin subsets' unicode-range, so every price on the page drew
// its tenge sign in whatever the OS had. The guard reads the unicode-ranges from 00-fonts.css and
// checks the built page's text plus the string literals of the modules that write copy at runtime.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from '../build.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fontsCss = fs.readFileSync(path.join(ROOT, 'src/css/00-fonts.css'), 'utf8');

function onestRanges() {
  const out = [];
  for (const m of fontsCss.matchAll(/@font-face\{([^}]*)\}/g)) {
    if (!/font-family:\s*Onest/.test(m[1])) continue;
    const r = m[1].match(/unicode-range:([^;}]+)/);
    if (!r) continue;
    for (const part of r[1].split(',')) {
      const [a, b] = part.trim().replace(/U\+/i, '').split('-');
      out.push([parseInt(a, 16), parseInt(b ?? a, 16)]);
    }
  }
  return out;
}
const ranges = onestRanges();
const covered = (cp) => cp < 0x80 || ranges.some(([a, b]) => cp >= a && cp <= b);

const dist = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-fonts-'));
build({ src: path.join(ROOT, 'src'), dist, legal: path.resolve(ROOT, '../../docs/legal') });
const html = fs.readFileSync(path.join(dist, 'index.html'), 'utf8');
const pageText = html
  .replace(/<script[\s\S]*?<\/script>/g, ' ')
  .replace(/<style[\s\S]*?<\/style>/g, ' ')
  .replace(/<!--[\s\S]*?-->/g, ' ')
  .replace(/<[^>]+>/g, ' ')
  .replace(/&[a-z#0-9]+;/gi, ' ');

// String literals of the modules that put copy on screen (comments stripped first).
const RUNTIME_COPY = ['pricing.js', 'pricing-format.js', 'demo/scenarios.js', 'demo.js', 'final.js', 'form.js', 'niches.js', 'story.js', 'stage.js'];
function literals(file) {
  const src = fs.readFileSync(path.join(ROOT, 'src/js', file), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/(^|[^:\\'"`])\/\/.*$/gm, '$1');
  return [...src.matchAll(/'(?:[^'\\\n]|\\.)*'|"(?:[^"\\\n]|\\.)*"|`(?:[^`\\]|\\.)*`/g)].map((m) => m[0]).join(' ');
}

function uncovered(text) {
  const miss = new Map();
  for (const ch of text) { const cp = ch.codePointAt(0); if (!covered(cp) && !/\s/.test(ch)) miss.set(ch, cp.toString(16)); }
  return [...miss].map(([ch, hex]) => `${ch} U+${hex.toUpperCase()}`);
}

test('Onest: у ₸ и ≈ есть своё начертание в подмножествах шрифта', () => {
  assert.ok(covered(0x20b8), '₸ вне unicode-range Onest');
  assert.ok(covered(0x2248), '≈ вне unicode-range Onest');
  assert.ok(fs.existsSync(path.join(ROOT, 'src/assets/fonts/onest-sym.woff2')), 'нет onest-sym.woff2 (node tools/fetch-fonts.mjs)');
});
test('весь текст страницы покрыт Onest (без системного запасного шрифта)', () => {
  assert.deepEqual(uncovered(pageText), []);
});
test('строки, которые модули пишут на экран, покрыты Onest', () => {
  for (const f of RUNTIME_COPY) assert.deepEqual(uncovered(literals(f)), [], f);
});
