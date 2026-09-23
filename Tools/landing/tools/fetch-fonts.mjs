// Self-host the web fonts: fetch the Google Fonts CSS with a modern UA (→ woff2 URLs),
// download the Cyrillic + Latin subsets of Onest (variable 400–700), a text-subset of Onest for
// the symbols outside them (₸ ≈), and a text-subset of
// Noto Serif Display Italic (only the letters the accent words can use), and write
// src/css/00-fonts.css pointing at the local files.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const OUT = path.join(ROOT, 'src/assets/fonts');
const UA = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36';
fs.mkdirSync(OUT, { recursive: true });

async function css(url) {
  const r = await fetch(url, { headers: { 'User-Agent': UA } });
  if (!r.ok) throw new Error(`${r.status} ${url}`);
  return r.text();
}
async function download(url, file) {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${r.status} ${url}`);
  fs.writeFileSync(path.join(OUT, file), Buffer.from(await r.arrayBuffer()));
  return file;
}

const onest = await css('https://fonts.googleapis.com/css2?family=Onest:wght@400..700&display=swap');
const blocks = [...onest.matchAll(/\/\*\s*([\w-]+)\s*\*\/\s*@font-face\s*{([^}]+)}/g)];
const wanted = { cyrillic: 'onest-cyr.woff2', latin: 'onest-lat.woff2' };
let faces = '';
for (const [, subset, body] of blocks) {
  if (!wanted[subset]) continue;
  const url = body.match(/url\((https:[^)]+)\)/)[1];
  const range = body.match(/unicode-range:\s*([^;]+);/)[1];
  await download(url, wanted[subset]);
  faces += `@font-face{font-family:Onest;font-style:normal;font-weight:400 700;font-display:swap;src:url(/assets/__V__/fonts/${wanted[subset]}) format("woff2");unicode-range:${range}}\n`;
}
if (!faces.includes('onest-cyr') || !faces.includes('onest-lat')) throw new Error('Onest subsets not found in Google CSS');

// Symbols the copy renders that sit outside the cyrillic/latin subsets: ₸ (U+20B8, latin-ext) and
// ≈ (U+2248, math). Onest draws both; pulling the whole latin-ext/math subsets for two glyphs would
// cost ~30 KB, so they come as a text= subset (variable 400–700, ~1 KB) with its own unicode-range.
// Without it every price on the page drew its tenge sign in the system fallback font.
const SYMBOLS = '₸≈';
const sym = await css(`https://fonts.googleapis.com/css2?family=Onest:wght@400..700&display=swap&text=${encodeURIComponent(SYMBOLS)}`);
const symUrl = sym.match(/url\((https:[^)]+)\)/)[1];
const symRange = sym.match(/unicode-range:\s*([^;]+);/)[1].trim();
for (const ch of SYMBOLS) {
  if (!new RegExp(`U\\+${ch.codePointAt(0).toString(16)}\\b`, 'i').test(symRange)) throw new Error(`Onest lacks ${ch} (range ${symRange})`);
}
await download(symUrl, 'onest-sym.woff2');
faces += `@font-face{font-family:Onest;font-style:normal;font-weight:400 700;font-display:swap;src:url(/assets/__V__/fonts/onest-sym.woff2) format("woff2");unicode-range:${symRange}}\n`;

const letters = 'АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюяABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?«»—–-’';
const serif = await css(`https://fonts.googleapis.com/css2?family=Noto+Serif+Display:ital,wght@1,400&display=swap&text=${encodeURIComponent(letters)}`);
const serifUrl = serif.match(/url\((https:[^)]+)\)/)[1];
await download(serifUrl, 'serif-italic.woff2');
faces += `@font-face{font-family:"CR Serif Italic";font-style:italic;font-weight:400;font-display:swap;src:url(/assets/__V__/fonts/serif-italic.woff2) format("woff2")}\n`;

fs.writeFileSync(path.join(ROOT, 'src/css/00-fonts.css'), faces);
console.log(fs.readdirSync(OUT).map((f) => `${f} ${fs.statSync(path.join(OUT, f)).size}B`).join('\n'));
