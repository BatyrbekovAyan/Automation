import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { assemble, rewriteAssetPaths, build } from '../build.mjs';

test('assemble вставляет секции по порядку вместо маркера', () => {
  const out = assemble('<main><!-- @sections --></main>', [
    { name: '10-a.html', html: '<section id="a"></section>' },
    { name: '20-b.html', html: '<section id="b"></section>' },
  ]);
  assert.ok(out.indexOf('id="a"') < out.indexOf('id="b"'));
  assert.ok(!out.includes('@sections'));
});

test('assemble падает без маркера', () => {
  assert.throws(() => assemble('<main></main>', []), /@sections/);
});

test('rewriteAssetPaths подставляет хеш во все /assets/__V__/', () => {
  const html = '<link href="/assets/__V__/site.css"><script src="/assets/__V__/js/main.js"></script>';
  const out = rewriteAssetPaths(html, 'abc123');
  assert.equal(out, '<link href="/assets/abc123/site.css"><script src="/assets/abc123/js/main.js"></script>');
});

test('build собирает dist: index без маркеров, site.css, юр-страницы, static', () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-'));
  const src = path.join(tmp, 'src'), legal = path.join(tmp, 'legal'), dist = path.join(tmp, 'dist');
  for (const d of ['sections', 'css', 'js', 'assets/img', 'static']) fs.mkdirSync(path.join(src, d), { recursive: true });
  fs.mkdirSync(legal);
  fs.writeFileSync(path.join(src, 'index.html'), '<head><link href="/assets/__V__/site.css"></head><main><!-- @sections --></main>');
  fs.writeFileSync(path.join(src, 'sections/10-x.html'), '<section id="x"></section>');
  fs.writeFileSync(path.join(src, 'css/01-base.css'), 'body{color:red}');
  fs.writeFileSync(path.join(src, 'css/10-x.css'), '.x{color:blue}');
  fs.writeFileSync(path.join(src, 'js/main.js'), 'export {}');
  fs.writeFileSync(path.join(src, 'assets/img/a.webp'), 'IMG');
  fs.writeFileSync(path.join(src, 'static/robots.txt'), 'User-agent: *');
  fs.writeFileSync(path.join(legal, 'privacy.html'), 'P');
  fs.writeFileSync(path.join(legal, 'terms.html'), 'T');
  const { hash } = build({ src, dist, legal });
  const index = fs.readFileSync(path.join(dist, 'index.html'), 'utf8');
  assert.match(hash, /^[0-9a-f]{10}$/);
  assert.ok(!index.includes('__V__') && !index.includes('@sections'));
  assert.ok(index.includes(`/assets/${hash}/site.css`));
  const css = fs.readFileSync(path.join(dist, 'assets', hash, 'site.css'), 'utf8');
  assert.ok(css.indexOf('color:red') < css.indexOf('color:blue'));
  assert.ok(fs.existsSync(path.join(dist, 'assets', hash, 'js/main.js')));
  assert.ok(fs.existsSync(path.join(dist, 'assets', hash, 'img/a.webp')));
  assert.equal(fs.readFileSync(path.join(dist, 'privacy.html'), 'utf8'), 'P');
  assert.equal(fs.readFileSync(path.join(dist, 'terms.html'), 'utf8'), 'T');
  assert.ok(fs.existsSync(path.join(dist, 'robots.txt')));
});

// ---- Lens fixes (2026-09-22): tier in <head>, modulepreload, CSS without comments ----
import { inlineBoot, importClosure, minifyCss, EAGER_MODULES } from '../build.mjs';
import vm from 'node:vm';

const ROOT = path.resolve(path.dirname(new URL(import.meta.url).pathname), '../..');

test('inlineBoot: блок @inline из quality.js без export, запускается как обычный скрипт', () => {
  const tag = inlineBoot(fs.readFileSync(path.join(ROOT, 'src/js/quality.js'), 'utf8'));
  assert.match(tag, /^<script>\(function\(\)\{[\s\S]*bootHead\(window,'\/assets\/__V__\/vendor\/'\);\n\}\)\(\);<\/script>$/);
  assert.ok(!/\bexport\b/.test(tag) && !/FrameGovernor/.test(tag), 'только чистые функции тира, без модульного синтаксиса');
  // исполняем как классический скрипт в песочнице: q-класс поставлен, vendor для medium запрошен
  const classes = [], scripts = [];
  const window = {
    navigator: { hardwareConcurrency: 8, userAgent: 'x', platform: 'x', maxTouchPoints: 5, connection: {} },
    innerWidth: 390, location: { search: '' },
    matchMedia: (q) => ({ matches: q.includes('coarse') }),
    document: { documentElement: { classList: { add: (c) => classes.push(c) } }, head: { appendChild: (l) => scripts.push(`${l.rel}:${l.href}`) },
      createElement: (t) => (t === 'canvas' ? { getContext: () => ({ getExtension: () => null }) } : {}) },
  };
  vm.runInNewContext(tag.replace(/^<script>|<\/script>$/g, ''), { window, URLSearchParams, Promise });
  assert.deepEqual(classes, ['q-medium']);
  assert.deepEqual(scripts, ['preload:/assets/__V__/vendor/gsap.min.js', 'preload:/assets/__V__/vendor/ScrollTrigger.min.js']);
});

test('modulepreload: статическое замыкание main.js и сразу загружаемых модулей', () => {
  const main = fs.readFileSync(path.join(ROOT, 'src/js/main.js'), 'utf8');
  const eager = main.match(/const EAGER = new Set\(\[([^\]]*)\]\)/)[1].match(/'([a-z]+)'/g).map((q) => q.slice(1, -1) + '.js');
  assert.deepEqual(EAGER_MODULES, eager, 'build.mjs и main.js согласны, какие модули грузятся сразу');
  const mods = importClosure(path.join(ROOT, 'src/js'), ['main.js', ...EAGER_MODULES]);
  for (const m of ['main.js', 'quality.js', 'motion.js', 'reveal.js', 'header.js', 'stage.js', 'story.js', 'particles/shapes.js', 'particles/field.js']) assert.ok(mods.includes(m), m);
  assert.ok(!mods.includes('demo.js') && !mods.includes('pricing.js'), 'ленивые секции не предзагружаются');
});

test('minifyCss: без комментариев и отступов, правила целы', () => {
  const out = minifyCss('/* a */\n.x{\n  color:red; /* b */\n}\n\n\n.y :hover{top:0}\n');
  assert.equal(out, '.x{\ncolor:red;\n}\n.y :hover{top:0}\n');
});

test('сборка: vendor-скриптов в разметке нет, тир решается в <head> до стилей', () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-boot-'));
  build({ src: path.join(ROOT, 'src'), dist: tmp, legal: path.resolve(ROOT, '../../docs/legal') });
  const html = fs.readFileSync(path.join(tmp, 'index.html'), 'utf8');
  assert.ok(!/<script src="[^"]*vendor/.test(html), 'GSAP/ScrollTrigger/Lenis не грузятся безусловно');
  const boot = html.indexOf('bootHead(window,'), css = html.indexOf('rel="stylesheet"');
  assert.ok(boot > 0 && boot < css, 'инлайн-скрипт до стилей: иначе он ждал бы site.css');
  assert.ok(!html.includes('@boot') && !html.includes('@modulepreload'));
  assert.match(html, /<link rel="modulepreload" href="\/assets\/[0-9a-f]{10}\/js\/particles\/field\.js">/);
  for (const f of ['onest-cyr', 'onest-lat', 'serif-italic']) assert.match(html, new RegExp(`rel="preload" href="/assets/[0-9a-f]{10}/fonts/${f}\\.woff2"`), f);
  const hash = html.match(/\/assets\/([0-9a-f]{10})\//)[1];
  const css2 = fs.readFileSync(path.join(tmp, 'assets', hash, 'site.css'), 'utf8');
  assert.ok(!css2.includes('/*'), 'в site.css не осталось комментариев');
});

// ---- Final review (2026-09-22), I-1: a module graph that never ran must not leave reveals hidden ----
function headGuard() {
  const shell = fs.readFileSync(path.join(ROOT, 'src/index.html'), 'utf8');
  const m = shell.match(/<script>(\(function\(d,w\)\{[\s\S]*?)<\/script>/);
  assert.ok(m, 'в <head> есть сторож reveal');
  return m[1];
}
function runGuard(mainRan, via = 'DOMContentLoaded') {
  const html = new Set(), listeners = {}, timers = [];
  const els = [0, 1, 2].map(() => ({ cls: new Set() }));
  for (const e of els) e.classList = { add: (c) => e.cls.add(c) };
  const document = {
    documentElement: { classList: { add: (c) => html.add(c) } },
    addEventListener: (t, fn) => { listeners[t] = fn; },
    querySelectorAll: (q) => (q === '[data-reveal],[data-split]' ? els : []),
  };
  const window = { setTimeout: (fn, ms) => timers.push({ fn, ms }) };
  vm.runInNewContext(headGuard(), { document, window });
  assert.ok(html.has('js'), 'класс js ставится сразу, до модулей');
  assert.ok(listeners.DOMContentLoaded, 'сторож ждёт DOMContentLoaded (он ждёт и модульные скрипты)');
  assert.equal(timers.length, 1, 'и таймер — на модуль, зависший в сети');
  assert.ok(timers[0].ms >= 2500 && timers[0].ms <= 5000, `таймер ${timers[0].ms} мс`);
  if (mainRan) window.__crMain = true;
  if (via === 'DOMContentLoaded') listeners.DOMContentLoaded(); else timers[0].fn();
  return { html, els };
}
for (const via of ['DOMContentLoaded', 'timer']) {
  test(`сторож reveal (${via}): main.js не отработал → всё показано (.rv-safe + .in)`, () => {
    const { html, els } = runGuard(false, via);
    assert.ok(html.has('rv-safe'));
    for (const e of els) assert.ok(e.cls.has('in'));
  });
  test(`сторож reveal (${via}): main.js отметился → reveal остаётся за reveal.js`, () => {
    const { html, els } = runGuard(true, via);
    assert.ok(!html.has('rv-safe'));
    for (const e of els) assert.equal(e.cls.size, 0);
  });
}
test('сторож и инлайн-тир разбираются старыми движками: ES5 у сторожа, без ?? и ?. в @inline', () => {
  const g = headGuard();
  assert.ok(!/=>|\blet\b|\bconst\b|`|\?\?|\?\./.test(g), 'сторож — ES5');
  const tag = inlineBoot(fs.readFileSync(path.join(ROOT, 'src/js/quality.js'), 'utf8'));
  assert.ok(!/\?\?|\?\./.test(tag), 'в <head> нет ?? и ?. (iOS Safari ≤ 13.3, Chrome < 80)');
  const main = fs.readFileSync(path.join(ROOT, 'src/js/main.js'), 'utf8');
  assert.ok(main.indexOf('window.__crMain = true') > main.indexOf('initReveals(document, ctx)'), 'main.js отмечается ПОСЛЕ initReveals');
  const css = fs.readFileSync(path.join(ROOT, 'src/css/01-base.css'), 'utf8');
  assert.match(css, /html:not\(\.js\) \.hdr,\.rv-safe \.hdr\{background:/, 'без JS шапка непрозрачная (M-1)');
});
