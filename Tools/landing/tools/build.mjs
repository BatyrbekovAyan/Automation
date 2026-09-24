#!/usr/bin/env node
// Zero-dependency build: src/ → dist/.
//  - sections/*.html are spliced into index.html at <!-- @sections --> (name order)
//  - css/*.css are concatenated (name order) into assets/<hash>/site.css
//  - js/ and assets/ are copied under assets/<hash>/ — the hash is over their content,
//    so every changed deploy gets a fresh directory and nested ES-module imports can be
//    cached immutably without per-file versioning
//  - static/* goes to the dist root; docs/legal/{privacy,terms}.html are copied verbatim
//  - JS and CSS are MINIFIED on the way out (esbuild, transform-only): the sources stay readable and
//    commented, while every published file keeps its own path and its imports, so the module graph and
//    the dynamic imports work exactly as in src/ (задача «сжатие кода»)
//  - <!-- @boot --> becomes an inline <script> with quality.js's @inline block: the tier is decided in
//    <head> and the vendor scripts are fetched only by the tiers that use them (линза перф-инженера, I-4)
//  - <!-- @modulepreload --> lists the static import closure of main.js and of the eagerly booted
//    section modules, so a slow network fetches the module graph in one round instead of three (M-3)
//  - CSS comments and indentation are dropped: the stylesheet blocks rendering (M-3)
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const requireFromHere = createRequire(import.meta.url);

// Minifier. Loaded lazily through require() so a missing devDependency fails with a sentence a human can
// act on — and FAILS, never silently ships the readable sources: unminified JS costs ~30 KB gz of the
// 120 KB budget, and the failure would only show up as a budget check on the next machine.
let _esbuild = null;
export function esbuild() {
  if (_esbuild) return _esbuild;
  try { _esbuild = requireFromHere('esbuild'); } catch (e) {
    throw new Error('esbuild не найден — сборка остановлена, чтобы не выложить несжатый код. Установи: npm i -D esbuild (в Tools/landing). Причина: ' + e.message);
  }
  return _esbuild;
}

// Transform-only, never bundle: each file stays its own ES module at its own published path.
// target es2020 = exactly what the sources already use (?. и ?? встречаются в src/js), so nothing is
// lowered and nothing newer is introduced.
export function minifyJs(code, file = '<js>') {
  // charset utf8: по умолчанию esbuild экранирует не-ASCII в \uXXXX, и русские строки (demo/scenarios.js)
  // раздуваются втрое. Модульные скрипты браузер всегда декодирует как UTF-8, страница — тоже (meta charset).
  const r = esbuild().transformSync(code, { loader: 'js', format: 'esm', target: 'es2020', charset: 'utf8', minify: true, legalComments: 'none', sourcefile: file });
  for (const w of r.warnings || []) console.warn(`esbuild: ${file}: ${w.text}`);
  return r.code.trim() + '\n';
}

// The <head> copy is a classic script for OLD engines: target es2019 so the minifier may not fold
// `a && a.b` into `a?.b` (iOS Safari ≤ 13.3, Chrome < 80 — финальное ревью, I-1).
export function minifyHeadJs(code) {
  const r = esbuild().transformSync(code, { loader: 'js', target: 'es2019', charset: 'utf8', minify: true, legalComments: 'none', sourcefile: 'head-boot.js' });
  for (const w of r.warnings || []) console.warn(`esbuild: head-boot.js: ${w.text}`);
  return r.code.trim();
}

export function assemble(shell, sections) {
  if (!shell.includes('<!-- @sections -->')) throw new Error('shell has no <!-- @sections --> marker');
  const body = sections.map((s) => `<!-- ${s.name} -->\n${s.html.trim()}\n`).join('\n');
  return shell.replace('<!-- @sections -->', body);
}

export function rewriteAssetPaths(text, hash) {
  return text.split('/assets/__V__/').join(`/assets/${hash}/`);
}

// The <head> boot script: quality.js between its @inline markers, `export`s stripped (it must be plain
// script code), wrapped in an IIFE with a bootHead() call on the vendor directory of this build, and
// minified as one unit — the comments and the names go, the behaviour does not.
export function inlineBoot(qualitySrc) {
  const m = qualitySrc.match(/\/\/ @inline-start\n([\s\S]*?)\/\/ @inline-end/);
  if (!m) throw new Error('quality.js has no // @inline-start … // @inline-end block');
  const code = m[1].replace(/^export /gm, '').trim();
  return `<script>${minifyHeadJs(`(function(){\n${code}\nbootHead(window,'/assets/__V__/vendor/');\n})();`)}</script>`;
}

// Modules booted at load (main.js's EAGER set) — their static imports are preloaded with main.js.
export const EAGER_MODULES = ['stage.js', 'story.js'];
export function importClosure(jsDir, entries) {
  const seen = new Set();
  const visit = (rel) => {
    const norm = path.posix.normalize(rel);
    if (seen.has(norm)) return;
    const file = path.join(jsDir, norm);
    if (!fs.existsSync(file)) return;
    seen.add(norm);
    const src = fs.readFileSync(file, 'utf8');
    for (const m of src.matchAll(/^import\s[^'"]*?from\s*['"](\.{1,2}\/[^'"]+)['"]/gm)) visit(path.posix.join(path.posix.dirname(norm), m[1]));
  };
  entries.forEach(visit);
  return [...seen];
}

export function minifyCss(css) {
  const r = esbuild().transformSync(css, { loader: 'css', charset: 'utf8', minify: true, legalComments: 'none', sourcefile: 'site.css' });
  for (const w of r.warnings || []) console.warn(`esbuild: site.css: ${w.text}`);
  return r.code.trim() + '\n';
}

function walk(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
    const p = path.join(dir, e.name);
    return e.isDirectory() ? walk(p) : [p];
  }).sort();
}

function copy(from, to) {
  fs.mkdirSync(path.dirname(to), { recursive: true });
  fs.copyFileSync(from, to);
}

export function build({ src, dist, legal }) {
  const cssFiles = walk(path.join(src, 'css')).filter((f) => f.endsWith('.css'));
  const css = cssFiles.map((f) => `/* ${path.basename(f)} */\n${fs.readFileSync(f, 'utf8')}`).join('\n');

  // Minify BEFORE hashing: the hash must be a function of the bytes actually published, or a change in
  // the minifier (its version, its options) would write new content under an UNCHANGED /assets/<hash>/
  // path that browsers and the CDN cache immutably. '/assets/__V__/' survives minification untouched
  // (it only ever appears in CSS url() and in the markup), so the path rewrite comes afterwards.
  const cssMin = minifyCss(css);
  const jsOut = walk(path.join(src, 'js')).map((f) => {
    const rel = path.relative(path.join(src, 'js'), f);
    return { rel, text: f.endsWith('.js') ? minifyJs(fs.readFileSync(f, 'utf8'), rel) : null, file: f };
  });
  const assetFiles = walk(path.join(src, 'assets'));

  const h = crypto.createHash('sha256');
  h.update(cssMin);
  for (const j of jsOut) { h.update(path.posix.join('js', j.rel)); h.update(j.text == null ? fs.readFileSync(j.file) : j.text); }
  for (const f of assetFiles) { h.update(path.relative(src, f)); h.update(fs.readFileSync(f)); }
  const hash = h.digest('hex').slice(0, 10);

  fs.rmSync(dist, { recursive: true, force: true });
  const out = path.join(dist, 'assets', hash);
  fs.mkdirSync(out, { recursive: true });
  fs.writeFileSync(path.join(out, 'site.css'), rewriteAssetPaths(cssMin, hash));
  for (const j of jsOut) {
    const to = path.join(out, 'js', j.rel);
    fs.mkdirSync(path.dirname(to), { recursive: true });
    if (j.text == null) { copy(j.file, to); continue; }
    fs.writeFileSync(to, rewriteAssetPaths(j.text, hash));
  }
  for (const f of assetFiles) copy(f, path.join(out, path.relative(path.join(src, 'assets'), f)));
  for (const f of walk(path.join(src, 'static'))) copy(f, path.join(dist, path.relative(path.join(src, 'static'), f)));

  const sections = walk(path.join(src, 'sections')).filter((f) => f.endsWith('.html'))
    .map((f) => ({ name: path.basename(f), html: fs.readFileSync(f, 'utf8') }));
  let shell = fs.readFileSync(path.join(src, 'index.html'), 'utf8');
  if (shell.includes('<!-- @boot -->')) shell = shell.replace('<!-- @boot -->', inlineBoot(fs.readFileSync(path.join(src, 'js/quality.js'), 'utf8')));
  if (shell.includes('<!-- @modulepreload -->')) {
    const mods = importClosure(path.join(src, 'js'), ['main.js', ...EAGER_MODULES]);
    shell = shell.replace('<!-- @modulepreload -->', mods.map((m) => `<link rel="modulepreload" href="/assets/__V__/js/${m}">`).join('\n'));
  }
  const index = rewriteAssetPaths(assemble(shell, sections), hash);
  fs.writeFileSync(path.join(dist, 'index.html'), index);
  for (const name of ['privacy.html', 'terms.html']) copy(path.join(legal, name), path.join(dist, name));
  return { hash, files: walk(dist).map((f) => path.relative(dist, f)) };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const root = path.resolve(HERE, '..');
  const r = build({ src: path.join(root, 'src'), dist: path.join(root, 'dist'), legal: path.resolve(root, '../../docs/legal') });
  console.log(`built dist/ (assets/${r.hash}, ${r.files.length} files)`);
}
