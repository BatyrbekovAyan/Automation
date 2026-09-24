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
  // порядок по именам файлов — по селекторам, не по значениям: минификатор переписывает цвета (blue → #00f)
  assert.ok(css.indexOf('body') < css.indexOf('.x'));
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

test('inlineBoot: блок @inline из quality.js без export, минифицирован, запускается как обычный скрипт', () => {
  const src = fs.readFileSync(path.join(ROOT, 'src/js/quality.js'), 'utf8');
  const tag = inlineBoot(src);
  assert.match(tag, /^<script>\(function\(\)\{[\s\S]*\}\)\(\);?<\/script>$/);
  assert.ok(!/\bexport\b/.test(tag) && !/FrameGovernor/.test(tag), 'только чистые функции тира, без модульного синтаксиса');
  // минифицировано: ни комментариев, ни отступов, и заметно короче исходного блока
  const block = src.match(/\/\/ @inline-start\n([\s\S]*?)\/\/ @inline-end/)[1];
  assert.ok(!/^\s*\/\//m.test(tag) && !/\n  /.test(tag), 'в <head> нет комментариев и отступов');
  assert.ok(tag.length < block.length * 0.6, `инлайн ${tag.length} байт против ${block.length} исходных`);
  // контракт с main.js (`window.__crBoot`) и с путём вендора переживают переименование локальных имён
  assert.ok(tag.includes('__crBoot'), 'window.__crBoot ставится — main.js читает именно его');
  assert.match(tag, /\/assets\/__V__\/vendor\//, 'база вендора осталась подставляемой');
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

test('minifyCss: без комментариев и пробелов, правила целы', () => {
  const out = minifyCss('/* a */\n.x{\n  color:red; /* b */\n}\n\n\n.y :hover{top:0}\n');
  assert.equal(out, '.x{color:red}.y :hover{top:0}\n');
});

test('minifyCss: кастомные свойства и их var() переживают минификацию как есть', () => {
  const out = minifyCss(':root{\n  --ink-hi: #E8E6F0;\n  --gap-2: 8px;\n}\n.q-high .x{color:var(--ink-hi);padding:var(--gap-2)}\n');
  // имена переменных минификатор не сокращает — иначе вся палитра темы разъехалась бы
  assert.match(out, /--ink-hi:\s*#E8E6F0/i);
  assert.match(out, /--gap-2:\s*8px/);
  assert.ok(out.includes('var(--ink-hi)') && out.includes('var(--gap-2)'));
  assert.ok(out.includes('.q-high'), 'класс тира — часть контракта с quality.js');
});

test('сборка: vendor-скриптов в разметке нет, тир решается в <head> до стилей', () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-boot-'));
  build({ src: path.join(ROOT, 'src'), dist: tmp, legal: path.resolve(ROOT, '../../docs/legal') });
  const html = fs.readFileSync(path.join(tmp, 'index.html'), 'utf8');
  assert.ok(!/<script src="[^"]*vendor/.test(html), 'GSAP/ScrollTrigger/Lenis не грузятся безусловно');
  // якорь — свойство __crBoot (минификатор переименовывает локальные функции, но не имена свойств)
  const boot = html.indexOf('__crBoot'), css = html.indexOf('rel="stylesheet"');
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

// ---- Минификация (2026-09-23): публикуется сжатый код, исходники остаются читаемыми ----
import { execFileSync } from 'node:child_process';
import { minifyJs } from '../build.mjs';

// Одна сборка настоящего src/ на все проверки ниже (каждая занимает ~0.2 с).
const MIN = (() => {
  const dist = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-min-'));
  const { hash } = build({ src: path.join(ROOT, 'src'), dist, legal: path.resolve(ROOT, '../../docs/legal') });
  const out = path.join(dist, 'assets', hash);
  const jsFiles = (function walk(d) {
    return fs.readdirSync(d, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]));
  })(path.join(out, 'js')).filter((f) => f.endsWith('.js')).sort();
  return { dist, hash, out, jsFiles, css: fs.readFileSync(path.join(out, 'site.css'), 'utf8') };
})();

test('минификация: каждый опубликованный модуль сжат — без комментариев и заметно короче исходника', () => {
  assert.ok(MIN.jsFiles.length >= 15, `модулей: ${MIN.jsFiles.length}`);
  let srcBytes = 0, outBytes = 0;
  for (const f of MIN.jsFiles) {
    const rel = path.relative(path.join(MIN.out, 'js'), f);
    const code = fs.readFileSync(f, 'utf8'), original = fs.readFileSync(path.join(ROOT, 'src/js', rel), 'utf8');
    srcBytes += original.length; outBytes += code.length;
    // комментарии внутри шаблонных строк — это GLSL (particles/field.js), их минификатор JS не трогает
    const js = code.replace(/`(?:\\[\s\S]|[^\\`])*`/g, '``');
    assert.ok(!/\/\//.test(js.replace(/https?:\/\//g, '')), `${rel}: остались строчные комментарии`);
    assert.ok(!/\/\*/.test(js), `${rel}: остались блочные комментарии`);
    assert.ok(code.length < original.length, `${rel}: ${code.length} не меньше исходных ${original.length}`);
  }
  assert.ok(outBytes < srcBytes * 0.65, `весь js: ${outBytes} байт против ${srcBytes} исходных`);
});

test('минификация: опубликованный модуль остаётся валидным ES-модулем со своими импортами', () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-parse-'));
  for (const f of MIN.jsFiles) {
    // .mjs, чтобы node --check разбирал файл как модуль, а не как CommonJS
    const copy = path.join(tmp, path.relative(path.join(MIN.out, 'js'), f).replace(/[\/]/g, '__') + '.mjs');
    fs.copyFileSync(f, copy);
    execFileSync(process.execPath, ['--check', copy]);   // бросит при синтаксической ошибке
  }
  // граф модулей цел: статические импорты main.js и ленивые import() секций — на своих путях
  const main = fs.readFileSync(path.join(MIN.out, 'js/main.js'), 'utf8');
  const srcMain = fs.readFileSync(path.join(ROOT, 'src/js/main.js'), 'utf8');
  const specs = [...srcMain.matchAll(/(?:from|import\()\s*['"](\.[^'"]+)['"]/g)].map((m) => m[1]);
  assert.ok(specs.length >= 8, `в исходном main.js ${specs.length} импортов`);
  for (const s of specs) assert.ok(main.includes(`"${s}"`) || main.includes(`'${s}'`), `${s} пропал из собранного main.js`);
  assert.match(main, /import\s*\{[^}]*\}\s*from\s*["']\.\/quality\.js["']/, 'статический импорт на месте');
  assert.match(main, /import\(["']\.\/stage\.js["']\)/, 'динамический импорт на месте');
  assert.ok(main.includes('import.meta.url'), 'import.meta.url — база для vendor/ — не свёрнут');
});

test('минификация css: переменные темы, классы тиров и @-правила переживают сборку', () => {
  const srcCss = fs.readdirSync(path.join(ROOT, 'src/css')).filter((f) => f.endsWith('.css'))
    .map((f) => fs.readFileSync(path.join(ROOT, 'src/css', f), 'utf8')).join('\n');
  const names = new Set([...srcCss.matchAll(/(--[a-z0-9-]+)\s*:/gi)].map((m) => m[1]));
  assert.ok(names.size >= 20, `кастомных свойств в исходниках: ${names.size}`);
  for (const n of names) assert.ok(MIN.css.includes(n), `${n} пропало из site.css`);
  for (const c of ['.q-low', '.q-high', '.q-medium']) assert.ok(MIN.css.includes(c), `${c} пропал из site.css`);
  for (const at of ['@media', '@font-face', '@keyframes', '@supports']) {
    if (srcCss.includes(at)) assert.ok(MIN.css.includes(at), `${at} пропало из site.css`);
  }
  assert.ok(!MIN.css.includes('/*'), 'в site.css не осталось комментариев');
  assert.ok(MIN.css.length < srcCss.length * 0.9, `site.css ${MIN.css.length} байт против ${srcCss.length} исходных`);
});

test('минификация: сборка падает громко, если esbuild не установлен', () => {
  // подмена не нужна: проверяем сам контракт — загрузчик кидает осмысленную ошибку, а не возвращает null
  const s = fs.readFileSync(path.join(ROOT, 'tools/build.mjs'), 'utf8');
  assert.match(s, /catch[\s\S]{0,80}throw new Error\('esbuild не найден/, 'отсутствие esbuild => throw, а не тихая сборка');
  assert.ok(!/minify:\s*false/.test(s), 'минификация не выключается флагом');
});

test('хеш берётся с опубликованных (сжатых) байтов и меняется при правке исходника', () => {
  // иначе смена минификатора или его опций положила бы новые байты под СТАРЫЙ immutable-путь
  const s = fs.readFileSync(path.join(ROOT, 'tools/build.mjs'), 'utf8');
  assert.match(s, /h\.update\(cssMin\)/, 'в хеш идёт сжатый CSS');
  assert.ok(!/h\.update\(css\)/.test(s), 'несжатый CSS в хеш не идёт');
  assert.match(s, /h\.update\(j\.text == null \? fs\.readFileSync\(j\.file\) : j\.text\)/, 'в хеш идёт сжатый JS');

  const mk = (cssBody) => {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-hash-'));
    const src = path.join(tmp, 'src'), legal = path.join(tmp, 'legal');
    for (const d of ['sections', 'css', 'js', 'assets', 'static']) fs.mkdirSync(path.join(src, d), { recursive: true });
    fs.mkdirSync(legal);
    fs.writeFileSync(path.join(src, 'index.html'), '<head></head><main><!-- @sections --></main>');
    fs.writeFileSync(path.join(src, 'css/01-base.css'), cssBody);
    fs.writeFileSync(path.join(src, 'js/main.js'), 'export const x = 1;');
    for (const n of ['privacy.html', 'terms.html']) fs.writeFileSync(path.join(legal, n), n);
    return build({ src, dist: path.join(tmp, 'dist'), legal }).hash;
  };
  assert.equal(mk('body{color:red}'), mk('body{color:red}'), 'одинаковый src — одинаковый хеш (сборка детерминирована)');
  assert.notEqual(mk('body{color:red}'), mk('body{color:#111}'), 'правка исходника меняет хеш');
});

test('minifyJs: транзит без бандла — импорт остаётся импортом, экспортируемое имя не переименовано', () => {
  const out = minifyJs("import { a as aa } from './x.js';\n// комментарий\nexport function keepMe(longArgumentName) {\n  return aa(longArgumentName) + 1;\n}\n");
  assert.ok(out.includes("from\"./x.js\"") || out.includes("from './x.js'"), out);
  assert.ok(out.includes('keepMe'), 'публичное имя экспорта сохранено');
  assert.ok(!out.includes('longArgumentName'), 'локальные имена сокращены');
  assert.ok(!out.includes('комментарий'));
});
