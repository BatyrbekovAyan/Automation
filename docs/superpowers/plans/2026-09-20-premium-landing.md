# Премиальный лендинг choosereply.com v2 — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить одностраничник v1 на премиальный анимированный лендинг «Гибрид А+В+Б», который восхищает и не тормозит на слабом Android, с той же формой заявки и юр-страницами.

**Architecture:** Статический сайт из исходников `Tools/landing/src/` (HTML-секции, CSS-файлы, ES-модули) собирается node-скриптом без зависимостей в `Tools/landing/dist/` (склейка секций, один `site.css`, ассеты в `/assets/<hash>/`). `main.js` определяет уровень качества (`low|medium|high`), поднимает GSAP/ScrollTrigger/Lenis и лениво инициализирует модуль каждой секции по атрибуту `data-module`. Анимации — только transform/opacity, WebGL-частицы — raw WebGL с деградацией. Проверки — `node:test` для чистых модулей + headless-стенд `tools/check.mjs` (playwright-core + системный Chrome) с жёсткими порогами.

**Tech Stack:** HTML/CSS/ES2020 модули; GSAP 3.13 + ScrollTrigger, Lenis 1.3 (самохостинг); raw WebGL1; шрифты Onest + Noto Serif Display Italic (самохостинг woff2); Node 25 (`node:test`, без npm-зависимостей в рантайме); playwright-core 1.63 (только проверки/OG); ffmpeg (webp); Caddy на VPS `choosereply`.

## Global Constraints

- Спека: `docs/superpowers/specs/2026-09-19-premium-landing-design.md` — структура, правдивость, бюджеты, выкладка. Бриф и прототипы: `.planning/sketches/008-premium-landing/` (`BRIEF.md`, `a-cinema/`, `b-live-chat/`, `c-signal/`).
- Всё RU. Числа с неразрывным пробелом: `19 990 ₸` (U+00A0 между разрядами и перед ₸).
- Цены: Старт 9 990 ₸/мес (год 99 000), Бизнес 19 990 ₸/мес (год 198 990, «Популярный»), Сеть 39 900 ₸/мес (год 399 990); пробный — 5 дней, 150 диалогов, без карты; докупка 500 диалогов за 3 900 ₸; лимиты 1/1/300, 3/3/1 000, 5/5/3 000.
- Бренд: фон #0A0D16→#182036, deep #13214A, surface #171C24, accent #5981D6, accentFill #3E61C6, accentHi #8FAEEF, accentSoft #1E2A44, ink #ECF0F6, ink-2 #9AA6B8, ink-3 #79869A; WhatsApp #25D366, Telegram #2AABEE; статусы: заявка #3A934C, нужен владелец #E46602, в диалоге #8F7AFA, замолчал #8A94A6, закрыт #9B5DE0.
- Знак U9 (1024-пространство): таблетки x=110 w=804; верх y=208 h=142; середина y=420 h=184 (выбрана, accent); низ y=674 h=142; полное скругление.
- Запрещено в тексте: отзывы, логотипы клиентов, «N компаний/клиентов», проценты роста, «отвечает за N секунд», рейтинги, интеграции Kaspi/1С/CRM, «понимает казахский», «ставит бота на паузу/передаёт диалог» как функция.
- Текст первого экрана (H1, подзаголовок, CTA) виден БЕЗ JS и без ожидания анимации (LCP = текст).
- Бюджеты (mobile 390×844, 4× CPU): fpsAvg ≥ 55 (или ≥ baseline−3 при нагруженной машине), p95 кадра ≤ 25 мс, кадров > 50 мс ≤ 3; JS ≤ 120 КБ gzip; до прокрутки ≤ 450 КБ; 0 ошибок консоли; нет горизонтального скролла на 360/390/768/1440.
- NO LIVE TRAFFIC: ни одна проверка не шлёт запросы на `choosereply.com`/`n8n.choosereply.com` (стенд блокирует и подменяет). Выкладка — только отдельной задачей по «да» владельца.
- Git: коммиты только по согласию владельца и только своими путями (`git add Tools/landing docs/superpowers/...`) — worktree общий с параллельными сессиями.
- Юр-тексты `docs/legal/{privacy,terms}.html` не редактируются; копируются в `dist/` на `/privacy.html`, `/terms.html`.
- Каждый исполнитель: если файлы задачи уже существуют (прерванная попытка) — продолжать с них, не начинать заново.

## Файловая структура

```
Tools/landing/
  package.json                 # type:module; scripts build/test/check; devDep playwright-core
  README.md                    # как собирать, проверять, выкладывать
  .gitignore                   # dist/, node_modules/, .check/
  src/
    index.html                 # оболочка: <head>, шапка, <!-- @sections -->, подвал, скрипты
    sections/
      10-stage.html            # hero + переход «Сигнал» (общий fixed-canvas частиц)
      30-story.html            # pinned-телефон 01–04
      40-demo.html             # «Попробуйте сами»
      50-niches.html           # лента + 6 карточек
      60-steps.html            # 3 шага
      70-pricing.html          # тарифы
      80-faq.html              # вопросы (<details>)
      90-final.html            # «Выбери ответ.» + форма
    css/
      00-fonts.css 01-base.css 10-stage.css 30-story.css 40-demo.css
      50-niches.css 60-steps.css 70-pricing.css 80-faq.css 90-final.css
    js/
      main.js                  # вход: tier → motion → lazy init модулей секций
      quality.js               # decideTier(), readEnv(), FrameGovernor (чистые)
      motion.js                # createMotion(tier) → {gsap, ScrollTrigger, lenis}
      reveal.js                # splitWords(), initReveals()
      magnetic.js              # магнитные кнопки (pointer:fine)
      header.js                # состояние шапки при прокрутке, якоря через lenis
      particles/shapes.js      # чистая геометрия целей частиц
      particles/field.js       # WebGL-рендер частиц
      stage.js                 # модуль секции 10: hero + morph по прокрутке
      story.js                 # модуль секции 30
      demo/scenarios.js        # данные демо (чистые)
      demo.js                  # модуль секции 40
      niches.js                # модуль секции 50
      pricing-format.js        # formatKzt(), yearlyMonthly(), savingsPercent() (чистые)
      pricing.js               # модуль секции 70
      form.js                  # validatePhone(), encodeLead(), submitLead() (чистые) + initForm()
      final.js                 # модуль секции 90 (кинетика «Выбери ответ.» + форма)
    assets/
      fonts/                   # onest-*.woff2, serif-italic.woff2 (tools/fetch-fonts.mjs)
      img/                     # shots-*.webp 720/1080, mark.svg (tools/make-images.sh)
      vendor/                  # gsap.min.js, ScrollTrigger.min.js, lenis.min.js + VENDOR.md
    static/                    # копируется в корень dist: favicon.svg, favicon.png,
                               # apple-touch-icon.png, og.png, robots.txt
  tools/
    build.mjs                  # src → dist
    check.mjs                  # headless-стенд с порогами
    fetch-fonts.mjs  fetch-vendor.sh  make-images.sh  og.mjs  og.html
    deploy.sh                  # бэкап + rsync без --delete + Caddy (только по «да»)
    Caddyfile.site             # целевой блок choosereply.com
    test/*.test.mjs            # node:test
```
Старые `Tools/landing/{index.html,favicon.*,og.png,privacy.html,terms.html}` удаляются в Task 1 (история в git; юр-страницы берутся из `docs/legal/`).

**Контракт модуля секции** (все задачи секций): файл `src/js/<name>.js` экспортирует
`export function init(root, ctx)` где `root` — элемент `[data-module="<name>"]`, `ctx = { tier: 'low'|'medium'|'high', gsap, ScrollTrigger, lenis /* null на low */, governor /* FrameGovernor */, reduced: boolean }`. Возвращает `void` или `{ destroy() }`. Модуль сам ставит свои rAF-циклы на паузу вне экрана (IntersectionObserver) и при `document.hidden`.

**Параллельность:** Task 1 → (Task 2, 3, 4 параллельно) → (Task 5–11 параллельно, каждая владеет своими файлами) → Task 12 → Task 13 → Task 14.

---

### Task 1: Каркас и сборка

**Files:**
- Create: `Tools/landing/package.json`, `Tools/landing/.gitignore`, `Tools/landing/tools/build.mjs`, `Tools/landing/tools/test/build.test.mjs`, `Tools/landing/src/index.html`, `Tools/landing/src/css/01-base.css`, `Tools/landing/src/js/main.js`, `Tools/landing/src/js/header.js`, `Tools/landing/src/js/magnetic.js`, `Tools/landing/src/sections/*.html` (заглушки для 8 секций), `Tools/landing/src/static/robots.txt`
- Delete: `Tools/landing/index.html`, `Tools/landing/privacy.html`, `Tools/landing/terms.html` (favicon.*/og.png переедут в Task 3)

**Interfaces:**
- Produces: `build({ src, dist, legal }) → { hash, files }`; `assemble(shell, sections) → string`; `rewriteAssetPaths(html, hash) → string`. Маркеры: `<!-- @sections -->` в оболочке, `/assets/__V__/` в путях. `main.js` — реестр модулей `{stage, story, demo, niches, pricing, final}`.

- [ ] **Step 1: package.json и .gitignore**

```json
{
  "name": "choosereply-landing",
  "private": true,
  "type": "module",
  "scripts": {
    "build": "node tools/build.mjs",
    "test": "node --test tools/test/*.test.mjs",
    "check": "node tools/check.mjs",
    "serve": "python3 -m http.server 8809 --bind 127.0.0.1 --directory dist"
  },
  "devDependencies": { "playwright-core": "1.63.0" }
}
```
`.gitignore`:
```
dist/
node_modules/
.check/
```

- [ ] **Step 2: Падающий тест сборки** — `tools/test/build.test.mjs`

```js
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
```

- [ ] **Step 3: Запуск — ожидается FAIL**

Run: `cd Tools/landing && node --test tools/test/build.test.mjs`
Expected: FAIL — `Cannot find module '../build.mjs'`.

- [ ] **Step 4: tools/build.mjs**

```js
#!/usr/bin/env node
// Zero-dependency build: src/ → dist/.
//  - sections/*.html are spliced into index.html at <!-- @sections --> (name order)
//  - css/*.css are concatenated (name order) into assets/<hash>/site.css
//  - js/ and assets/ are copied under assets/<hash>/ — the hash is over their content,
//    so every changed deploy gets a fresh directory and nested ES-module imports can be
//    cached immutably without per-file versioning
//  - static/* goes to the dist root; docs/legal/{privacy,terms}.html are copied verbatim
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));

export function assemble(shell, sections) {
  if (!shell.includes('<!-- @sections -->')) throw new Error('shell has no <!-- @sections --> marker');
  const body = sections.map((s) => `<!-- ${s.name} -->\n${s.html.trim()}\n`).join('\n');
  return shell.replace('<!-- @sections -->', body);
}

export function rewriteAssetPaths(text, hash) {
  return text.split('/assets/__V__/').join(`/assets/${hash}/`);
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
  const codeFiles = [...walk(path.join(src, 'js')), ...walk(path.join(src, 'assets'))];

  const h = crypto.createHash('sha256');
  h.update(css);
  for (const f of codeFiles) { h.update(path.relative(src, f)); h.update(fs.readFileSync(f)); }
  const hash = h.digest('hex').slice(0, 10);

  fs.rmSync(dist, { recursive: true, force: true });
  const out = path.join(dist, 'assets', hash);
  fs.mkdirSync(out, { recursive: true });
  fs.writeFileSync(path.join(out, 'site.css'), rewriteAssetPaths(css, hash));
  for (const f of walk(path.join(src, 'js'))) {
    const rel = path.relative(path.join(src, 'js'), f);
    const to = path.join(out, 'js', rel);
    fs.mkdirSync(path.dirname(to), { recursive: true });
    fs.writeFileSync(to, rewriteAssetPaths(fs.readFileSync(f, 'utf8'), hash));
  }
  for (const f of walk(path.join(src, 'assets'))) copy(f, path.join(out, path.relative(path.join(src, 'assets'), f)));
  for (const f of walk(path.join(src, 'static'))) copy(f, path.join(dist, path.relative(path.join(src, 'static'), f)));

  const sections = walk(path.join(src, 'sections')).filter((f) => f.endsWith('.html'))
    .map((f) => ({ name: path.basename(f), html: fs.readFileSync(f, 'utf8') }));
  const index = rewriteAssetPaths(assemble(fs.readFileSync(path.join(src, 'index.html'), 'utf8'), sections), hash);
  fs.writeFileSync(path.join(dist, 'index.html'), index);
  for (const name of ['privacy.html', 'terms.html']) copy(path.join(legal, name), path.join(dist, name));
  return { hash, files: walk(dist).map((f) => path.relative(dist, f)) };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const root = path.resolve(HERE, '..');
  const r = build({ src: path.join(root, 'src'), dist: path.join(root, 'dist'), legal: path.resolve(root, '../../docs/legal') });
  console.log(`built dist/ (assets/${r.hash}, ${r.files.length} files)`);
}
```

- [ ] **Step 5: Запуск — ожидается PASS**

Run: `cd Tools/landing && node --test tools/test/build.test.mjs`
Expected: `# pass 4`, `# fail 0`.

- [ ] **Step 6: Оболочка `src/index.html`**

```html
<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>Choose Reply — ИИ-бот для WhatsApp и Telegram</title>
<meta name="description" content="Бот отвечает вашим клиентам в WhatsApp и Telegram с вашего номера и по вашему прайсу, 24/7. Сам — или предлагает до 4 готовых ответов, а вы выбираете одним касанием.">
<link rel="canonical" href="https://choosereply.com/">
<meta name="theme-color" content="#0A0D16">
<meta property="og:type" content="website">
<meta property="og:url" content="https://choosereply.com/">
<meta property="og:title" content="Choose Reply — ответ за ботом, выбор за вами">
<meta property="og:description" content="ИИ-бот для WhatsApp и Telegram: отвечает с вашего номера по вашему прайсу. 5 дней бесплатно.">
<meta property="og:image" content="https://choosereply.com/og.png">
<meta property="og:locale" content="ru_RU">
<meta name="twitter:card" content="summary_large_image">
<link rel="icon" type="image/svg+xml" href="/favicon.svg">
<link rel="icon" type="image/png" href="/favicon.png">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<link rel="preload" href="/assets/__V__/fonts/onest-cyr.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="/assets/__V__/site.css">
<script>document.documentElement.classList.add('js')</script>
<!-- @jsonld (Task 11) -->
</head>
<body>
<a class="skip" href="#main">К содержанию</a>
<header class="hdr" id="hdr">
  <div class="wrap hdr-bar">
    <a class="brand" href="#top" aria-label="Choose Reply — наверх">
      <svg class="mark" viewBox="0 0 1024 1024" aria-hidden="true"><rect x="110" y="208" width="804" height="142" rx="71" class="m-side"/><rect x="110" y="420" width="804" height="184" rx="92" class="m-mid"/><rect x="110" y="674" width="804" height="142" rx="71" class="m-side"/></svg>
      <span>Choose Reply</span>
    </a>
    <nav class="nav" aria-label="Разделы">
      <a href="#how">Как работает</a><a href="#demo">Попробуйте</a><a href="#pricing">Тарифы</a><a href="#faq">Вопросы</a>
    </nav>
    <a class="btn btn-s" href="#lead" data-magnetic>Ранний доступ</a>
  </div>
</header>
<main id="main">
<!-- @sections -->
</main>
<footer class="ftr">
  <div class="wrap ftr-grid">
    <div class="ftr-brand"><svg class="mark" viewBox="0 0 1024 1024" aria-hidden="true"><rect x="110" y="208" width="804" height="142" rx="71" class="m-side"/><rect x="110" y="420" width="804" height="184" rx="92" class="m-mid"/><rect x="110" y="674" width="804" height="142" rx="71" class="m-side"/></svg><span>Choose Reply</span></div>
    <p class="ftr-note">ИИ-бот для WhatsApp и Telegram. Сделано в Казахстане.</p>
    <nav class="ftr-links" aria-label="Документы"><a href="/privacy.html">Политика конфиденциальности</a><a href="/terms.html">Условия использования</a></nav>
    <p class="ftr-copy">© 2026 Choose Reply</p>
  </div>
</footer>
<script src="/assets/__V__/vendor/gsap.min.js" defer></script>
<script src="/assets/__V__/vendor/ScrollTrigger.min.js" defer></script>
<script src="/assets/__V__/vendor/lenis.min.js" defer></script>
<script type="module" src="/assets/__V__/js/main.js"></script>
</body>
</html>
```

- [ ] **Step 7: Заглушки секций** — по одному файлу, каждый с корректным корнем (задачи 5–10 заменят содержимое):

`10-stage.html`: `<div class="stage" data-module="stage" id="top"><section class="hero" aria-labelledby="h1"><div class="wrap"><h1 id="h1">Ответ — за ботом. Выбор — <em>за вами</em>.</h1></div></section></div>`
`30-story.html`: `<section class="story" id="how" data-module="story" aria-labelledby="how-h"><div class="wrap"><h2 id="how-h">Как это работает</h2></div></section>`
`40-demo.html`: `<section class="demo" id="demo" data-module="demo" aria-labelledby="demo-h"><div class="wrap"><h2 id="demo-h">Попробуйте сами</h2></div></section>`
`50-niches.html`: `<section class="niches" id="niches" data-module="niches" aria-labelledby="ni-h"><div class="wrap"><h2 id="ni-h">Для вашего бизнеса</h2></div></section>`
`60-steps.html`: `<section class="steps" id="start" aria-labelledby="st-h"><div class="wrap"><h2 id="st-h">Три шага — и бот работает</h2></div></section>`
`70-pricing.html`: `<section class="pricing" id="pricing" data-module="pricing" aria-labelledby="pr-h"><div class="wrap"><h2 id="pr-h">Тарифы</h2></div></section>`
`80-faq.html`: `<section class="faq" id="faq" aria-labelledby="faq-h"><div class="wrap"><h2 id="faq-h">Вопросы</h2></div></section>`
`90-final.html`: `<section class="final" id="lead" data-module="final" aria-labelledby="lead-h"><div class="wrap"><h2 id="lead-h">Выбери ответ.</h2></div></section>`

- [ ] **Step 8: `src/css/01-base.css`** — токены, сброс, типографика, кнопки, шапка, подвал, reveal-классы, reduced-motion:

```css
:root{
  --bg:#0A0D16; --bg-2:#182036; --deep:#13214A; --surface:#171C24; --surface-2:#11151D;
  --line:rgba(154,166,184,.14); --line-2:rgba(154,166,184,.24);
  --ink:#ECF0F6; --ink-2:#9AA6B8; --ink-3:#79869A;
  --accent:#5981D6; --accent-fill:#3E61C6; --accent-hi:#8FAEEF; --accent-soft:#1E2A44;
  --wa:#25D366; --tg:#2AABEE;
  --st-order:#3A934C; --st-owner:#E46602; --st-dialog:#8F7AFA; --st-silent:#8A94A6; --st-closed:#9B5DE0;
  --r:22px; --r-s:14px; --wrap:1180px; --gutter:clamp(16px,4vw,32px);
  --ease-out:cubic-bezier(.16,1,.3,1);
  --f-sans:Onest,-apple-system,"Segoe UI",Roboto,sans-serif;
  --f-serif:"CR Serif Italic",Georgia,serif;
}
*,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
html{-webkit-text-size-adjust:100%;text-size-adjust:100%}
html.lenis,html.lenis body{height:auto}
.lenis.lenis-smooth{scroll-behavior:auto!important}
body{background:var(--bg);color:var(--ink);font:400 17px/1.6 var(--f-sans);-webkit-font-smoothing:antialiased;overflow-x:clip}
img,svg,canvas{display:block;max-width:100%}
a{color:inherit}
em.serif,h1 em,h2 em{font-family:var(--f-serif);font-style:italic;font-weight:400;color:var(--accent-hi);letter-spacing:0}
::selection{background:rgba(89,129,214,.4)}
:focus-visible{outline:2px solid var(--accent-hi);outline-offset:3px;border-radius:6px}
.wrap{max-width:var(--wrap);margin:0 auto;padding:0 var(--gutter)}
.skip{position:absolute;left:-9999px;top:8px;z-index:100;background:var(--ink);color:var(--bg);padding:10px 16px;border-radius:10px}
.skip:focus{left:8px}
h1{font-size:clamp(40px,7.2vw,92px);line-height:1.02;letter-spacing:-.035em;font-weight:600}
h2{font-size:clamp(32px,5vw,64px);line-height:1.05;letter-spacing:-.03em;font-weight:600}
h3{font-size:clamp(19px,2vw,22px);line-height:1.25;letter-spacing:-.01em;font-weight:600}
.eyebrow{font-size:12.5px;font-weight:600;letter-spacing:.18em;text-transform:uppercase;color:var(--accent-hi)}
.lead{color:var(--ink-2);font-size:clamp(17px,1.6vw,20px);max-width:620px}
section{position:relative;scroll-margin-top:80px}
.sec{padding:clamp(88px,12vw,160px) 0}
.btn{display:inline-flex;align-items:center;justify-content:center;gap:10px;min-height:48px;padding:0 26px;border-radius:999px;border:0;
  background:var(--accent-fill);color:#fff;font:600 16px/1 var(--f-sans);text-decoration:none;cursor:pointer;
  box-shadow:0 10px 30px -10px rgba(62,97,198,.7),inset 0 1px 0 rgba(255,255,255,.18);transition:transform .3s var(--ease-out),box-shadow .3s,background .3s}
.btn:hover{background:#4A6FD4;box-shadow:0 16px 40px -12px rgba(89,129,214,.9),inset 0 1px 0 rgba(255,255,255,.22)}
.btn:active{transform:scale(.98)}
.btn-ghost{background:transparent;color:var(--ink);box-shadow:inset 0 0 0 1px var(--line-2)}
.btn-ghost:hover{background:rgba(255,255,255,.04);box-shadow:inset 0 0 0 1px rgba(143,174,239,.5)}
.btn-s{min-height:44px;padding:0 18px;font-size:14.5px}
.mark{width:26px;height:26px}
.mark .m-side{fill:rgba(89,129,214,.28)}
.mark .m-mid{fill:var(--accent)}
.hdr{position:fixed;inset:0 0 auto;z-index:50;transition:background .35s,box-shadow .35s,transform .45s var(--ease-out)}
.hdr.scrolled{background:rgba(10,13,22,.72);box-shadow:0 1px 0 var(--line)}
.q-high .hdr.scrolled,.q-medium .hdr.scrolled{-webkit-backdrop-filter:saturate(1.4) blur(16px);backdrop-filter:saturate(1.4) blur(16px)}
.q-low .hdr.scrolled{background:rgba(10,13,22,.96)}
.hdr-bar{display:flex;align-items:center;justify-content:space-between;height:68px;gap:16px}
.brand{display:flex;align-items:center;gap:10px;text-decoration:none;font-weight:600;font-size:16.5px;letter-spacing:-.01em;min-height:44px}
.nav{display:flex;gap:28px}
.nav a{color:var(--ink-2);text-decoration:none;font-size:15px;transition:color .2s}
.nav a:hover{color:var(--ink)}
@media (max-width:860px){.nav{display:none}}
.ftr{border-top:1px solid var(--line);padding:56px 0 calc(40px + env(safe-area-inset-bottom))}
.ftr-grid{display:grid;grid-template-columns:1fr auto;gap:18px 32px;color:var(--ink-2);font-size:14.5px}
.ftr-brand{display:flex;align-items:center;gap:10px;color:var(--ink);font-weight:600}
.ftr-links{display:flex;gap:22px;flex-wrap:wrap}
.ftr-links a{text-decoration:none;min-height:44px;display:inline-flex;align-items:center}
.ftr-links a:hover{color:var(--ink)}
@media (max-width:700px){.ftr-grid{grid-template-columns:1fr}}
/* reveal: скрытие только при наличии JS и не на low; текст hero НЕ использует этот класс */
.js [data-reveal]{opacity:0;transform:translateY(28px)}
.js [data-reveal].in{opacity:1;transform:none;transition:opacity .9s var(--ease-out),transform .9s var(--ease-out)}
.w{display:inline-block;overflow:hidden;vertical-align:top;padding-bottom:.08em;margin-bottom:-.08em}
.w>span{display:inline-block;transform:translateY(105%);transition:transform 1s var(--ease-out)}
.in .w>span{transform:none}
@media (prefers-reduced-motion:reduce){
  *,*::before,*::after{animation-duration:.001ms!important;animation-iteration-count:1!important;transition-duration:.001ms!important;scroll-behavior:auto!important}
  .js [data-reveal]{opacity:1;transform:none}
  .w>span{transform:none}
}
```

- [ ] **Step 9: `src/js/main.js`**

```js
// Entry: decide the quality tier, bring up motion, then lazily init each section module
// when its root nears the viewport. Every module follows the contract in the plan:
// export function init(root, ctx) → void | { destroy() }.
import { readEnv, decideTier, FrameGovernor } from './quality.js';
import { createMotion } from './motion.js';
import { initReveals } from './reveal.js';
import { initHeader } from './header.js';
import { initMagnetic } from './magnetic.js';

const MODULES = {
  stage: () => import('./stage.js'),
  story: () => import('./story.js'),
  demo: () => import('./demo.js'),
  niches: () => import('./niches.js'),
  pricing: () => import('./pricing.js'),
  final: () => import('./final.js'),
};

const env = readEnv(window);
const tier = decideTier(env);
document.documentElement.classList.add(`q-${tier}`);
const motion = createMotion(tier, window);
const ctx = { tier, reduced: env.reducedMotion, governor: new FrameGovernor(), ...motion };

initHeader(document.getElementById('hdr'), ctx);
initReveals(document, ctx);
if (tier !== 'low') initMagnetic(document);

const pending = new Map();
for (const root of document.querySelectorAll('[data-module]')) pending.set(root, root.dataset.module);

function boot(root) {
  const name = pending.get(root);
  if (!name) return;
  pending.delete(root);
  const load = MODULES[name];
  if (!load) { console.warn(`[landing] unknown module "${name}"`); return; }
  load().then((m) => m.init(root, ctx)).catch((e) => console.error(`[landing] ${name}:`, e));
}

// The stage (first screen) boots immediately; the rest 1.5 screens ahead of the viewport.
const io = new IntersectionObserver((entries) => {
  for (const e of entries) if (e.isIntersecting) { io.unobserve(e.target); boot(e.target); }
}, { rootMargin: '150% 0px 150% 0px' });
for (const root of pending.keys()) {
  if (root.dataset.module === 'stage') boot(root); else io.observe(root);
}
```

- [ ] **Step 10: `src/js/header.js` и `src/js/magnetic.js`**

```js
// header.js — «scrolled» state + anchor links routed through Lenis when present.
export function initHeader(hdr, ctx) {
  if (!hdr) return;
  const onScroll = () => hdr.classList.toggle('scrolled', window.scrollY > 24);
  onScroll();
  window.addEventListener('scroll', onScroll, { passive: true });
  document.addEventListener('click', (e) => {
    const a = e.target.closest('a[href^="#"]');
    if (!a) return;
    const id = a.getAttribute('href');
    const target = id.length > 1 ? document.querySelector(id) : document.body;
    if (!target) return;
    e.preventDefault();
    if (ctx.lenis) ctx.lenis.scrollTo(target, { offset: -72, duration: 1.4 });
    else target.scrollIntoView({ behavior: ctx.reduced ? 'auto' : 'smooth', block: 'start' });
    history.replaceState(null, '', id);
  });
}
```
```js
// magnetic.js — buttons with [data-magnetic] lean toward a fine pointer. No-op on touch.
export function initMagnetic(doc) {
  if (!matchMedia('(hover: hover) and (pointer: fine)').matches) return;
  for (const el of doc.querySelectorAll('[data-magnetic]')) {
    let raf = 0;
    el.addEventListener('pointermove', (e) => {
      const r = el.getBoundingClientRect();
      const dx = (e.clientX - (r.left + r.width / 2)) * 0.22;
      const dy = (e.clientY - (r.top + r.height / 2)) * 0.32;
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => { el.style.transform = `translate(${dx}px, ${dy}px)`; });
    });
    el.addEventListener('pointerleave', () => {
      cancelAnimationFrame(raf);
      el.style.transition = 'transform .6s cubic-bezier(.16,1,.3,1)';
      el.style.transform = '';
      setTimeout(() => { el.style.transition = ''; }, 600);
    });
  }
}
```

- [ ] **Step 11: robots.txt, удаление v1-файлов, сборка**

`src/static/robots.txt`:
```
User-agent: *
Allow: /
```
Run: `cd Tools/landing && rm index.html privacy.html terms.html && node tools/build.mjs`
Expected: `built dist/ (assets/<10 hex>, N files)` (модули секций ещё не существуют — это нормально до Task 5–10; `main.js` импортирует quality/motion/reveal из Task 2 — сборка от этого не зависит).

- [ ] **Step 12: Commit (по согласию владельца)**

```bash
git add Tools/landing && git commit -m "feat(landing): каркас v2 — сборка src→dist, оболочка, базовые стили, реестр модулей"
```

---

### Task 2: Ядро движения — уровни качества, GSAP/Lenis, reveal

**Files:**
- Create: `Tools/landing/src/js/quality.js`, `Tools/landing/src/js/motion.js`, `Tools/landing/src/js/reveal.js`, `Tools/landing/tools/test/quality.test.mjs`

**Interfaces:**
- Consumes: `main.js` (Task 1) вызывает `readEnv(window)`, `decideTier(env)`, `new FrameGovernor()`, `createMotion(tier, window)`, `initReveals(document, ctx)`.
- Produces: `decideTier(env) → 'low'|'medium'|'high'`; `FrameGovernor#sample(dtMs) → 'none'|'degrade'|'disable'`, `FrameGovernor#reset()`; `createMotion() → { gsap, ScrollTrigger, lenis }` (lenis=null на low/reduced; gsap/ScrollTrigger могут быть null, если скрипты не загрузились); `splitWords(el)` оборачивает слова в `.w>span`.

- [ ] **Step 1: Падающие тесты** — `tools/test/quality.test.mjs`

```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { decideTier, FrameGovernor } from '../../src/js/quality.js';

const base = { reducedMotion: false, saveData: false, webgl: true, cores: 8, memory: 8, coarse: false, width: 1440, ios: false, override: null };

test('мощный десктоп → high', () => assert.equal(decideTier(base), 'high'));
test('телефон с 8 ядрами → medium', () => assert.equal(decideTier({ ...base, coarse: true, width: 390 }), 'medium'));
test('reduced motion → low', () => assert.equal(decideTier({ ...base, reducedMotion: true }), 'low'));
test('saveData → low', () => assert.equal(decideTier({ ...base, saveData: true }), 'low'));
test('без WebGL → low', () => assert.equal(decideTier({ ...base, webgl: false }), 'low'));
test('Android 4 ядра → low', () => assert.equal(decideTier({ ...base, coarse: true, width: 390, cores: 4 }), 'low'));
test('iPhone с 4 ядрами → medium (Safari занижает cores)', () => assert.equal(decideTier({ ...base, coarse: true, width: 390, cores: 4, ios: true }), 'medium'));
test('2 ГБ памяти → low', () => assert.equal(decideTier({ ...base, memory: 2 }), 'low'));
test('узкий десктоп-экран → medium', () => assert.equal(decideTier({ ...base, width: 900 }), 'medium'));
test('?tier= перекрывает всё', () => assert.equal(decideTier({ ...base, reducedMotion: true, override: 'high' }), 'high'));
test('мусорный override игнорируется', () => assert.equal(decideTier({ ...base, override: 'ultra' }), 'high'));

test('FrameGovernor: ровные 16 мс → none', () => {
  const g = new FrameGovernor();
  for (let i = 0; i < 300; i++) assert.equal(g.sample(16.7), 'none');
});
test('FrameGovernor: ~1 с по 40 мс → degrade, затем ещё столько же → disable', () => {
  const g = new FrameGovernor();
  const seen = [];
  for (let i = 0; i < 200; i++) { const a = g.sample(40); if (a !== 'none') seen.push(a); }
  assert.deepEqual(seen.slice(0, 2), ['degrade', 'disable']);
});
test('FrameGovernor: одиночные провалы не деградируют', () => {
  const g = new FrameGovernor();
  for (let i = 0; i < 300; i++) assert.equal(g.sample(i % 30 === 0 ? 120 : 16), 'none');
});
test('FrameGovernor: паузы вкладки (>250 мс) игнорируются', () => {
  const g = new FrameGovernor();
  for (let i = 0; i < 50; i++) assert.equal(g.sample(1000), 'none');
});
```

- [ ] **Step 2: Запуск — FAIL**

Run: `cd Tools/landing && node --test tools/test/quality.test.mjs`
Expected: FAIL — `Cannot find module '../../src/js/quality.js'`.

- [ ] **Step 3: `src/js/quality.js`**

```js
// Quality tiers. Decided once, before any animation starts, from what the device reports.
//   high   — fine pointer, wide screen, WebGL, ≥ 8 cores: every effect at full density
//   medium — ordinary phones/tablets/laptops: effects on, particle count reduced
//   low    — reduced motion, Save-Data, no WebGL, ≤ 4 cores (non-iOS) or ≤ 2 GB: static
//            mark instead of particles, native scroll, no pinning, simple reveals
// iOS Safari under-reports hardwareConcurrency and has no deviceMemory, so a 4-core
// report there is not a weak-device signal. ?tier=low|medium|high overrides (QA).
const TIERS = ['low', 'medium', 'high'];

export function decideTier(env) {
  if (env.override && TIERS.includes(env.override)) return env.override;
  if (env.reducedMotion || env.saveData || !env.webgl) return 'low';
  if ((env.memory ?? 8) <= 2) return 'low';
  if ((env.cores ?? 8) <= 4 && !env.ios) return 'low';
  if (!env.coarse && env.width >= 1024 && (env.cores ?? 8) >= 8) return 'high';
  return 'medium';
}

export function readEnv(w) {
  const nav = w.navigator;
  let webgl = false;
  try { const c = w.document.createElement('canvas'); webgl = !!(c.getContext('webgl') || c.getContext('experimental-webgl')); } catch { webgl = false; }
  return {
    reducedMotion: w.matchMedia('(prefers-reduced-motion: reduce)').matches,
    saveData: !!(nav.connection && nav.connection.saveData),
    webgl,
    cores: nav.hardwareConcurrency,
    memory: nav.deviceMemory,
    coarse: w.matchMedia('(pointer: coarse)').matches,
    width: w.innerWidth,
    ios: /iP(hone|od|ad)/.test(nav.userAgent) || (nav.platform === 'MacIntel' && nav.maxTouchPoints > 1),
    override: new URLSearchParams(w.location.search).get('tier'),
  };
}

// Watches frame times of a running effect and says when to shed load. An exponential
// moving average over ~1 s (α = 1/60) must stay above 24 ms for 45 consecutive frames
// before it acts; frames > 250 ms are tab switches/GC pauses and are ignored.
export class FrameGovernor {
  constructor({ limitMs = 24, holdFrames = 45 } = {}) {
    this.limitMs = limitMs; this.holdFrames = holdFrames; this.reset();
    this.level = 0;
  }
  reset() { this.avg = 16.7; this.over = 0; }
  sample(dtMs) {
    if (!(dtMs > 0) || dtMs > 250) return 'none';
    this.avg += (dtMs - this.avg) / 60;
    this.over = this.avg > this.limitMs ? this.over + 1 : 0;
    if (this.over < this.holdFrames) return 'none';
    this.reset();
    this.level += 1;
    return this.level === 1 ? 'degrade' : 'disable';
  }
}
```

- [ ] **Step 4: Запуск — PASS**

Run: `cd Tools/landing && node --test tools/test/quality.test.mjs`
Expected: `# pass 15`, `# fail 0`. (Если «~1 с по 40 мс» не набирает оба действия за 200 кадров — EMA от 16.7 к 40 пересекает 24 мс на ~22-м кадре, +45 кадров → degrade ≈ 67-й, после reset снова ~22+45 → disable ≈ 134-й; оба < 200.)

- [ ] **Step 5: `src/js/motion.js`**

```js
// GSAP + ScrollTrigger + Lenis, wired together once. The vendor scripts are classic
// deferred scripts that run before this module, so they are globals by now. Any of them
// may be missing (blocked/failed download) — callers must treat null as «no motion lib».
export function createMotion(tier, w) {
  const gsap = w.gsap || null;
  const ScrollTrigger = w.ScrollTrigger || null;
  if (gsap && ScrollTrigger) gsap.registerPlugin(ScrollTrigger);
  let lenis = null;
  const smooth = tier !== 'low' && w.Lenis && w.matchMedia('(hover: hover) and (pointer: fine)').matches;
  if (smooth) {
    lenis = new w.Lenis({ duration: 1.15, easing: (t) => 1 - Math.pow(1 - t, 4), smoothWheel: true, syncTouch: false });
    if (gsap && ScrollTrigger) {
      lenis.on('scroll', ScrollTrigger.update);
      gsap.ticker.add((time) => lenis.raf(time * 1000));
      gsap.ticker.lagSmoothing(0);
    } else {
      const loop = (t) => { lenis.raf(t); requestAnimationFrame(loop); };
      requestAnimationFrame(loop);
    }
  }
  return { gsap, ScrollTrigger, lenis };
}
```
(Lenis только на устройствах с мышью: на тач-устройствах нативная прокрутка быстрее и привычнее.)

- [ ] **Step 6: `src/js/reveal.js`**

```js
// Generic scroll reveals. [data-reveal] fades/rises in once; [data-split] headings rise
// word by word. The HTML stays readable without JS (the hiding CSS is scoped to .js),
// and on the low tier / reduced motion everything is shown immediately.
export function splitWords(el) {
  if (el.dataset.splitDone) return;
  el.dataset.splitDone = '1';
  const walk = (node) => {
    for (const child of [...node.childNodes]) {
      if (child.nodeType === 3) {
        const parts = child.textContent.split(/(\s+)/);
        const frag = document.createDocumentFragment();
        for (const p of parts) {
          if (!p) continue;
          if (/^\s+$/.test(p)) { frag.appendChild(document.createTextNode(p)); continue; }
          const w = document.createElement('span'); w.className = 'w';
          const i = document.createElement('span'); i.textContent = p;
          w.appendChild(i); frag.appendChild(w);
        }
        child.replaceWith(frag);
      } else if (child.nodeType === 1 && child.tagName !== 'BR') walk(child);
    }
  };
  walk(el);
  [...el.querySelectorAll('.w>span')].forEach((s, i) => { s.style.transitionDelay = `${Math.min(i * 45, 600)}ms`; });
}

export function initReveals(doc, ctx) {
  const els = [...doc.querySelectorAll('[data-reveal],[data-split]')];
  if (ctx.tier === 'low' || ctx.reduced) { els.forEach((e) => e.classList.add('in')); return; }
  doc.querySelectorAll('[data-split]').forEach(splitWords);
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
  }, { threshold: 0.18, rootMargin: '0px 0px -8% 0px' });
  els.forEach((e) => io.observe(e));
}
```

- [ ] **Step 7: Полный прогон тестов** — Run: `cd Tools/landing && node --test tools/test/*.test.mjs` → Expected: `# fail 0`.

- [ ] **Step 8: Commit (по согласию)** — `git add Tools/landing/src/js/{quality,motion,reveal}.js Tools/landing/tools/test/quality.test.mjs && git commit -m "feat(landing): уровни качества, регулятор кадров, GSAP/Lenis, reveal"`

---

### Task 3: Ассеты — шрифты, изображения, вендор-библиотеки, иконки

**Files:**
- Create: `Tools/landing/tools/fetch-fonts.mjs`, `Tools/landing/tools/fetch-vendor.sh`, `Tools/landing/tools/make-images.sh`, `Tools/landing/src/css/00-fonts.css`, `Tools/landing/src/assets/vendor/VENDOR.md`, `Tools/landing/src/assets/fonts/*.woff2`, `Tools/landing/src/assets/img/*`, `Tools/landing/src/static/{favicon.svg,favicon.png,apple-touch-icon.png}`
- Delete: `Tools/landing/favicon.svg`, `Tools/landing/favicon.png` (заменены знаком U9)

**Interfaces:**
- Produces: шрифты `Onest` (переменный 400–700, subsets cyr+lat) и `CR Serif Italic`; изображения `/assets/__V__/img/shot-<name>-720.webp` и `-1080.webp` для name ∈ {chats, thread-auto, suggestions, svodka, bots, paywall, pricelist, prompt}; `/assets/__V__/img/mark.svg`; `/assets/__V__/img/app-icon.svg`.

- [ ] **Step 1: `tools/fetch-vendor.sh`**

```bash
#!/usr/bin/env bash
# Pin and self-host the motion libraries (no third-party CDN at runtime).
set -euo pipefail
cd "$(dirname "$0")/../src/assets/vendor"
GSAP=3.13.0; LENIS=1.3.11
curl -fsSL "https://cdn.jsdelivr.net/npm/gsap@${GSAP}/dist/gsap.min.js" -o gsap.min.js
curl -fsSL "https://cdn.jsdelivr.net/npm/gsap@${GSAP}/dist/ScrollTrigger.min.js" -o ScrollTrigger.min.js
curl -fsSL "https://cdn.jsdelivr.net/npm/lenis@${LENIS}/dist/lenis.min.js" -o lenis.min.js
{
  echo "# Vendored libraries"
  echo "GSAP ${GSAP} (standard no-charge license), Lenis ${LENIS} (MIT). Fetched by tools/fetch-vendor.sh."
  echo; echo '```'; shasum -a 256 *.js; echo '```'
} > VENDOR.md
ls -la
```
Run: `mkdir -p Tools/landing/src/assets/vendor && bash Tools/landing/tools/fetch-vendor.sh`
Expected: три файла > 10 КБ, `VENDOR.md` с тремя sha256.

- [ ] **Step 2: `tools/fetch-fonts.mjs`** — скачивает woff2 с Google Fonts (современный UA → woff2), кириллица и латиница раздельно для Onest; курсив — subset по `text=`:

```js
// Self-host the web fonts: fetch the Google Fonts CSS with a modern UA (→ woff2 URLs),
// download the Cyrillic + Latin subsets of Onest (variable 400–700) and a text-subset of
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

const letters = 'АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюяABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?«»—–-’';
const serif = await css(`https://fonts.googleapis.com/css2?family=Noto+Serif+Display:ital,wght@1,400&display=swap&text=${encodeURIComponent(letters)}`);
const serifUrl = serif.match(/url\((https:[^)]+)\)/)[1];
await download(serifUrl, 'serif-italic.woff2');
faces += `@font-face{font-family:"CR Serif Italic";font-style:italic;font-weight:400;font-display:swap;src:url(/assets/__V__/fonts/serif-italic.woff2) format("woff2")}\n`;

fs.writeFileSync(path.join(ROOT, 'src/css/00-fonts.css'), faces);
console.log(fs.readdirSync(OUT).map((f) => `${f} ${fs.statSync(path.join(OUT, f)).size}B`).join('\n'));
```
Run: `cd Tools/landing && node tools/fetch-fonts.mjs`
Expected: `onest-cyr.woff2`, `onest-lat.woff2`, `serif-italic.woff2` (каждый 8–60 КБ); `src/css/00-fonts.css` с тремя `@font-face`.

- [ ] **Step 3: `tools/make-images.sh`** — webp 720 и 1080 из исходных скриншотов, знак и иконки:

```bash
#!/usr/bin/env bash
# Phone screenshots → webp (720 for 1×/phones, 1080 for 2×/desktop), brand mark SVGs,
# favicon/apple-touch from the shipped app icon (U9, ink palette).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../../.." && pwd)"
SRC="$REPO/Tools/store/screenshots"; IMG="$HERE/../src/assets/img"; STATIC="$HERE/../src/static"
mkdir -p "$IMG" "$STATIC"
declare -A MAP=( [chats]=04-tab0 [thread-auto]=02-thread-auto [suggestions]=03-suggestions [svodka]=04-tab1
                 [bots]=04-tab2 [paywall]=05-paywall [pricelist]=06-settings-products [prompt]=07-settings-prompt )
for name in "${!MAP[@]}"; do
  for w in 720 1080; do
    ffmpeg -loglevel error -y -i "$SRC/${MAP[$name]}.png" -vf "scale=${w}:-1:flags=lanczos" -c:v libwebp -quality 84 "$IMG/shot-$name-$w.webp"
  done
done
# App icon + bare mark (vector)
node -e '
const {P, iconSvg} = require(process.argv[1] + "/Tools/icon-lab/appicon/build");
const {concepts} = require(process.argv[1] + "/Tools/icon-lab/appicon/concepts_round7");
const u9 = concepts.find((c) => c.id === "U9");
require("fs").writeFileSync(process.argv[2] + "/app-icon.svg", iconSvg(u9, P.ink));
' "$REPO" "$IMG"
cat > "$IMG/mark.svg" <<'SVG'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024"><rect x="110" y="208" width="804" height="142" rx="71" fill="#5981D6" fill-opacity=".28"/><rect x="110" y="420" width="804" height="184" rx="92" fill="#5981D6"/><rect x="110" y="674" width="804" height="142" rx="71" fill="#5981D6" fill-opacity=".28"/></svg>
SVG
cp "$IMG/app-icon.svg" "$STATIC/favicon.svg"
# Raster icons via headless Chrome screenshot of the SVG (exact rendering)
node "$HERE/raster-icon.mjs" "$IMG/app-icon.svg" "$STATIC/favicon.png" 64
node "$HERE/raster-icon.mjs" "$IMG/app-icon.svg" "$STATIC/apple-touch-icon.png" 180
ls -la "$IMG" "$STATIC"
```
И `tools/raster-icon.mjs`:
```js
// Render an SVG to a square PNG of the given size through headless Chrome.
import { chromium } from 'playwright-core';
import fs from 'node:fs';
const [svgPath, outPath, sizeArg] = process.argv.slice(2);
const size = Number(sizeArg);
const svg = fs.readFileSync(svgPath, 'utf8');
const browser = await chromium.launch({ executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', headless: true });
const page = await browser.newPage({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
await page.setContent(`<html><body style="margin:0;background:transparent">${svg.replace('<svg ', `<svg width="${size}" height="${size}" `)}</body></html>`);
await page.screenshot({ path: outPath, omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
await browser.close();
```
Run: `cd Tools/landing && npm i --silent && bash tools/make-images.sh && rm favicon.svg favicon.png`
Expected: 16 файлов `shot-*-{720,1080}.webp` (720: 5–25 КБ, 1080: 10–45 КБ), `mark.svg`, `app-icon.svg`, `static/favicon.{svg,png}`, `static/apple-touch-icon.png`.
(macOS bash 3.2 не знает `declare -A` — запускать `/usr/local/bin/bash` если есть, иначе заменить ассоциативный массив на пары `name:src` в цикле `for pair in chats:04-tab0 ...; do name=${pair%%:*}; src=${pair#*:}`. Исполнитель выбирает второй вариант, если `bash --version` < 4.)

- [ ] **Step 4: Визуальная проверка** — открыть `src/assets/img/shot-suggestions-1080.webp` и `src/static/apple-touch-icon.png` через Read: скриншот резкий, иконка — три полосы, средняя синяя на чернильном фоне.

- [ ] **Step 5: Commit (по согласию)** — `git add Tools/landing/src/assets Tools/landing/src/static Tools/landing/src/css/00-fonts.css Tools/landing/tools/{fetch-fonts.mjs,fetch-vendor.sh,make-images.sh,raster-icon.mjs} && git commit -m "feat(landing): самохостинг шрифтов и библиотек, webp-скриншоты, иконки U9"`

---

### Task 4: Стенд проверки с порогами

**Files:**
- Create: `Tools/landing/tools/check.mjs`

**Interfaces:**
- Consumes: собранный `dist/` на `http://127.0.0.1:8809/`; baseline v1 на `http://127.0.0.1:8808/baseline/index.html` (прототипная папка, сервер 8808).
- Produces: `node tools/check.mjs [--url=...] [--baseline=...] [--out=.check] [--only=mobile|desktop] [--skip-perf]` → JSON-отчёт в stdout + `.check/report.json` + PNG; exit 0 — все пороги пройдены, exit 1 — перечень нарушений.

- [ ] **Step 1: `tools/check.mjs`**

```js
// Headless acceptance check for the landing. Enforces the spec budgets and the
// NO-LIVE-TRAFFIC rule: every request to choosereply.com / n8n.choosereply.com is aborted,
// except POST …/webhook/LandingLead which is answered locally with {success:true} and
// recorded so the form contract can be asserted.
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const opt = Object.fromEntries(process.argv.slice(2).map((a) => { const [k, v] = a.replace(/^--/, '').split('='); return [k, v ?? true]; }));
const URL_ = opt.url || 'http://127.0.0.1:8809/';
const BASE = opt.baseline || null;
const OUT = path.resolve(opt.out || '.check');
fs.mkdirSync(OUT, { recursive: true });
const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const fails = [];
const report = { url: URL_, at: new Date().toISOString(), widths: {}, mobile: null, desktop: null, reduced: null, form: null, baseline: null };

const browser = await chromium.launch({ executablePath: CHROME, headless: true, args: ['--ignore-gpu-blocklist', '--enable-gpu-rasterization'] });

async function newPage({ width, height, mobile, reduced = false }) {
  const ctx = await browser.newContext({
    viewport: { width, height }, deviceScaleFactor: mobile ? 2 : 1, isMobile: mobile, hasTouch: mobile, locale: 'ru-RU',
    reducedMotion: reduced ? 'reduce' : 'no-preference',
  });
  const page = await ctx.newPage();
  const state = { errors: [], leads: [], bytes: 0, jsGz: 0, live: [] };
  await ctx.route(/https?:\/\/(n8n\.)?choosereply\.com\//, async (route) => {
    const req = route.request();
    if (req.method() === 'POST' && req.url().includes('/webhook/LandingLead')) {
      state.leads.push({ contentType: req.headers()['content-type'] || '', body: req.postData() || '' });
      return route.fulfill({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: '{"success":true}' });
    }
    state.live.push(req.url());
    return route.abort();
  });
  page.on('console', (m) => { if (m.type() === 'error') state.errors.push(m.text().slice(0, 240)); });
  page.on('pageerror', (e) => state.errors.push('pageerror: ' + String(e).slice(0, 240)));
  page.on('response', async (r) => {
    try {
      const b = await r.body(); state.bytes += b.length;
      if (/javascript/.test(r.headers()['content-type'] || '') || r.url().endsWith('.js')) state.jsGz += zlib.gzipSync(b).length;
    } catch {}
  });
  return { ctx, page, state };
}

async function scrollThrough(page, steps = 40) {
  await page.evaluate(async (n) => {
    const H = document.documentElement.scrollHeight - innerHeight;
    for (let i = 1; i <= n; i++) { scrollTo(0, H * i / n); await new Promise((r) => setTimeout(r, 60)); }
    scrollTo(0, 0);
  }, steps);
}

async function perfPass(page, cpu) {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: cpu });
  await page.evaluate(() => scrollTo(0, 0));
  await page.waitForTimeout(800);
  const r = await page.evaluate(async () => {
    const frames = []; let last = performance.now();
    const H = document.documentElement.scrollHeight - innerHeight, dur = 7000, t0 = performance.now();
    await new Promise((res) => { (function tick(now) { frames.push(now - last); last = now; const k = Math.min(1, (now - t0) / dur); scrollTo(0, H * k); if (k < 1) requestAnimationFrame(tick); else res(); })(performance.now()); });
    frames.shift();
    const s = [...frames].sort((a, b) => a - b), avg = frames.reduce((a, b) => a + b, 0) / frames.length;
    return { fpsAvg: Math.round(1000 / avg), p95FrameMs: Math.round(s[Math.floor(s.length * 0.95)]), framesOver50ms: frames.filter((f) => f > 50).length, frames: frames.length };
  });
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: 1 });
  return r;
}

async function profile(name, spec, url, { perf = true } = {}) {
  const { ctx, page, state } = await newPage(spec);
  await page.goto(url, { waitUntil: 'load', timeout: 90000 });
  const heroVisibleAt1s = await (async () => {
    await page.waitForTimeout(1000);
    return page.evaluate(() => {
      const h = document.querySelector('h1');
      if (!h) return false;
      const cs = getComputedStyle(h), r = h.getBoundingClientRect();
      return cs.visibility !== 'hidden' && parseFloat(cs.opacity) > 0.9 && r.width > 0 && r.top < innerHeight;
    });
  })();
  const bytesBeforeScroll = state.bytes;
  const lcp = await page.evaluate(() => new Promise((res) => { let v = 0; try { new PerformanceObserver((l) => { for (const e of l.getEntries()) v = e.startTime; }).observe({ type: 'largest-contentful-paint', buffered: true }); } catch {} setTimeout(() => res(Math.round(v)), 300); }));
  await scrollThrough(page);
  const shots = [];
  for (const k of [0, 0.12, 0.25, 0.4, 0.55, 0.7, 0.85, 1]) {
    await page.evaluate((f) => scrollTo(0, (document.documentElement.scrollHeight - innerHeight) * f), k);
    await page.waitForTimeout(900);
    const f = path.join(OUT, `${name}-${String(Math.round(k * 100)).padStart(3, '0')}.png`);
    await page.screenshot({ path: f }); shots.push(f);
  }
  const perfR = perf ? await perfPass(page, spec.mobile ? 4 : 1) : null;
  const res = { heroVisibleAt1s, lcpMs: lcp, kbBeforeScroll: Math.round(bytesBeforeScroll / 1024), kbTotal: Math.round(state.bytes / 1024), jsGzKb: Math.round(state.jsGz / 1024), errors: state.errors, liveRequests: state.live, perf: perfR, shots };
  await ctx.close();
  return res;
}

// 1. Horizontal overflow at four widths
for (const width of [360, 390, 768, 1440]) {
  const { ctx, page } = await newPage({ width, height: 900, mobile: width < 768 });
  await page.goto(URL_, { waitUntil: 'load', timeout: 90000 });
  await scrollThrough(page, 20);
  const over = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth;
    const bad = [...document.querySelectorAll('body *')].filter((e) => { const r = e.getBoundingClientRect(); return r.width > 0 && (r.right > vw + 1 || r.left < -1) && getComputedStyle(e).position !== 'fixed'; })
      .slice(0, 5).map((e) => `${e.tagName.toLowerCase()}.${[...e.classList].join('.')}`);
    return { scrollW: document.documentElement.scrollWidth, vw, bad };
  });
  report.widths[width] = over;
  if (over.scrollW > over.vw) fails.push(`horizontal scroll at ${width}px: scrollWidth ${over.scrollW} > ${over.vw} (${over.bad.join(', ')})`);
  await ctx.close();
}

// 2. Mobile + desktop profiles
const skipPerf = !!opt['skip-perf'];
if (opt.only !== 'desktop') report.mobile = await profile('mobile', { width: 390, height: 844, mobile: true }, URL_, { perf: !skipPerf });
if (opt.only !== 'mobile') report.desktop = await profile('desktop', { width: 1440, height: 900, mobile: false }, URL_, { perf: !skipPerf });
if (BASE && !skipPerf && opt.only !== 'desktop') report.baseline = await profile('baseline-mobile', { width: 390, height: 844, mobile: true }, BASE, { perf: true });

// 3. Reduced motion: no errors, content visible, no running canvas
{
  const { ctx, page, state } = await newPage({ width: 390, height: 844, mobile: true, reduced: true });
  await page.goto(URL_, { waitUntil: 'load', timeout: 90000 });
  await page.waitForTimeout(1200);
  await scrollThrough(page, 20);
  report.reduced = await page.evaluate(() => ({
    tierClass: [...document.documentElement.classList].find((c) => c.startsWith('q-')),
    visibleCanvases: [...document.querySelectorAll('canvas')].filter((c) => c.getBoundingClientRect().width > 0 && getComputedStyle(c).display !== 'none' && parseFloat(getComputedStyle(c).opacity) > 0).length,
    hiddenReveals: [...document.querySelectorAll('[data-reveal],[data-split]')].filter((e) => parseFloat(getComputedStyle(e).opacity) < 0.9).length,
  }));
  report.reduced.errors = state.errors;
  if (report.reduced.tierClass !== 'q-low') fails.push(`reduced motion tier is ${report.reduced.tierClass}, expected q-low`);
  if (report.reduced.visibleCanvases) fails.push(`reduced motion shows ${report.reduced.visibleCanvases} canvas`);
  if (report.reduced.hiddenReveals) fails.push(`reduced motion leaves ${report.reduced.hiddenReveals} reveal elements hidden`);
  if (state.errors.length) fails.push(`reduced motion console errors: ${state.errors.join(' | ')}`);
  await ctx.close();
}

// 4. Form contract (stubbed endpoint)
{
  const { ctx, page, state } = await newPage({ width: 390, height: 844, mobile: true });
  await page.goto(URL_, { waitUntil: 'load', timeout: 90000 });
  const form = page.locator('form#leadForm');
  await form.scrollIntoViewIfNeeded();
  await page.fill('#leadName', 'Проверка');
  await page.fill('#leadPhone', '12');
  await page.click('#leadBtn');
  const errShown = await page.locator('#leadErr').isVisible();
  await page.fill('#leadPhone', '+7 700 000 00 00');
  await page.click('#leadBtn');
  await page.waitForSelector('#leadOk:not([hidden])', { timeout: 8000 }).catch(() => {});
  const okShown = await page.locator('#leadOk').isVisible();
  const lead = state.leads[0] || null;
  const params = lead ? Object.fromEntries(new URLSearchParams(lead.body)) : null;
  report.form = { errShownForBadPhone: errShown, okShown, leads: state.leads.length, contentType: lead?.contentType, params };
  if (!errShown) fails.push('form: short phone did not show #leadErr');
  if (!okShown) fails.push('form: success state #leadOk not shown');
  if (state.leads.length !== 1) fails.push(`form: expected exactly 1 LandingLead POST, got ${state.leads.length}`);
  if (lead && !lead.contentType.startsWith('application/x-www-form-urlencoded')) fails.push(`form: content-type ${lead.contentType}`);
  if (params && !(params.phone === '+7 700 000 00 00' && params.source === 'landing' && params.website === '' && params.name === 'Проверка')) fails.push(`form: bad params ${JSON.stringify(params)}`);
  await ctx.close();
}
await browser.close();

// 5. Budgets
for (const name of ['mobile', 'desktop']) {
  const p = report[name]; if (!p) continue;
  if (!p.heroVisibleAt1s) fails.push(`${name}: H1 not visible at 1 s`);
  if (p.errors.length) fails.push(`${name}: console errors: ${p.errors.join(' | ')}`);
  if (p.liveRequests.length) fails.push(`${name}: live requests attempted: ${p.liveRequests.join(', ')}`);
  if (p.jsGzKb > 120) fails.push(`${name}: JS ${p.jsGzKb} KB gz > 120`);
  if (p.kbBeforeScroll > 450) fails.push(`${name}: ${p.kbBeforeScroll} KB before scroll > 450`);
}
if (report.desktop && report.desktop.lcpMs > 2500) fails.push(`desktop LCP ${report.desktop.lcpMs} ms > 2500`);
if (report.mobile?.perf) {
  const m = report.mobile.perf, b = report.baseline?.perf;
  const fpsFloor = b ? Math.min(55, b.fpsAvg - 3) : 55;
  if (m.fpsAvg < fpsFloor) fails.push(`mobile fpsAvg ${m.fpsAvg} < ${fpsFloor}${b ? ` (baseline ${b.fpsAvg})` : ''}`);
  if (m.p95FrameMs > (b ? Math.max(25, b.p95FrameMs + 8) : 25)) fails.push(`mobile p95 frame ${m.p95FrameMs} ms too high${b ? ` (baseline ${b.p95FrameMs})` : ''}`);
  if (m.framesOver50ms > (b ? Math.max(3, b.framesOver50ms + 2) : 3)) fails.push(`mobile frames>50ms ${m.framesOver50ms}${b ? ` (baseline ${b.framesOver50ms})` : ''}`);
}

report.fails = fails;
fs.writeFileSync(path.join(OUT, 'report.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify({ fails, mobile: report.mobile && { ...report.mobile, shots: undefined }, desktop: report.desktop && { ...report.desktop, shots: undefined }, baseline: report.baseline?.perf, reduced: report.reduced, form: report.form, widths: report.widths }, null, 2));
process.exit(fails.length ? 1 : 0);
```

- [ ] **Step 2: Проверка стенда на заведомо плохой странице** — собрать каркас (`node tools/build.mjs`), поднять `npm run serve` в фоне, запустить `node tools/check.mjs --skip-perf`.
Expected: exit 1, в `fails` есть `form: success state #leadOk not shown` (формы ещё нет) — стенд ловит отсутствие контракта. Это «красный» тест для Task 10.

- [ ] **Step 3: Commit (по согласию)** — `git add Tools/landing/tools/check.mjs && git commit -m "test(landing): headless-стенд с бюджетами, блокировкой прода и контрактом формы"`

---

### Task 5: Первый экран + «Сигнал» (частицы)

**Files:**
- Create: `Tools/landing/src/sections/10-stage.html` (заменить заглушку), `Tools/landing/src/css/10-stage.css`, `Tools/landing/src/js/stage.js`, `Tools/landing/src/js/particles/shapes.js`, `Tools/landing/src/js/particles/field.js`, `Tools/landing/tools/test/shapes.test.mjs`

**Interfaces:**
- Consumes: контракт модуля (`init(root, ctx)`), `ctx.governor.sample(dt)`, `ctx.tier`.
- Produces: `shapes.js` — `mulberry32(seed) → () => number`; `logoTargets(n, rand) → Float32Array(2n)` (точки на/в таблетках знака в NDC-подобных координатах [-1,1], средняя плотнее); `bubbleTargets(n, rand)` (два пузыря «клиент/бот»); `channelTargets(n, rand)` (кольцо WhatsApp слева + самолётик Telegram справа, x<0 → WA, x>0 → TG). `field.js` — `createField(canvas, { count, dpr }) → { setShapes(listOfFloat32Array), setMorph(t /*0..shapes-1*/), setPointer(x,y,active), resize(w,h), render(timeMs), setCount(n), destroy() }` или `null`, если WebGL недоступен.

Эталон: `.planning/sketches/008-premium-landing/c-signal/index.html` — разметка hero/story 386–470, стили 15–361 (блоки hero/story/overlay), JS 633–1000 (`layout`, `morphTarget`, `setOverlay`, шейдеры `VS` 713–802 и `FS` 803–815, `buildParticles` 816–890, `initGL` 891–914, цикл `frame` 922–953, `bootGL` 957–977, `fallbackScroll` 978–1003). Переносить с разрезанием на `shapes.js` (геометрия, чистая) и `field.js` (GL).

Тексты:
- H1: `Ответ — за ботом.<br>Выбор — <em>за вами</em>.`
- Подзаголовок: «Бот отвечает вашим клиентам в WhatsApp и Telegram — с вашего номера, по вашему прайсу, 24/7. Сам — или предлагает до 4 готовых ответов, а вы выбираете одним касанием.»
- CTA: «Получить ранний доступ →» (`href="#lead"`, `data-magnetic`) + вторичная `btn-ghost` «Как это работает» (`#how`).
- Микро: «✓ 5 дней бесплатно · ✓ 150 диалогов · ✓ без карты»; бейджи «App Store · скоро», «Google Play · скоро».
- Переход, шаг 1 (частицы → два пузыря): eyebrow «01 · Режим «Авто»», H2 `Клиент пишет.<br>Бот уже <em>отвечает</em>.`, текст «Вопрос приходит в WhatsApp или Telegram — днём, ночью, в выходные. Бот отвечает с вашего номера живым русским языком, по вашему прайсу и каталогу.» Пузыри (HTML-оверлей поверх формы из частиц): клиент — «Здравствуйте! Масляный фильтр на RAV4 2019 есть?», бот — «Здравствуйте! Есть оригинал 04152-YZZA1 — 4 900 ₸. Подобрать аналог дешевле?».
- Шаг 2 (частицы → WA/TG): eyebrow «02 · Один номер», H2 `WhatsApp и Telegram<br>в <em>одном</em> окне.`, текст «Все переписки — в одном приложении. Клиенты пишут туда же, куда и раньше: номер не меняется, отдельная SIM не нужна.»

- [ ] **Step 1: Падающие тесты геометрии** — `tools/test/shapes.test.mjs`

```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mulberry32, logoTargets, bubbleTargets, channelTargets } from '../../src/js/particles/shapes.js';

const N = 4000;
const inRange = (a) => a.every((v) => v >= -1.05 && v <= 1.05 && Number.isFinite(v));

test('mulberry32 детерминирован', () => {
  const a = mulberry32(7), b = mulberry32(7);
  for (let i = 0; i < 10; i++) assert.equal(a(), b());
});

for (const [name, fn] of [['logo', logoTargets], ['bubble', bubbleTargets], ['channel', channelTargets]]) {
  test(`${name}: длина 2n, значения в [-1,1], детерминизм по seed`, () => {
    const a = fn(N, mulberry32(1)), b = fn(N, mulberry32(1));
    assert.ok(a instanceof Float32Array);
    assert.equal(a.length, 2 * N);
    assert.ok(inRange(a));
    assert.deepEqual(a, b);
  });
}

test('logo: три полосы, средняя самая плотная', () => {
  const a = logoTargets(N, mulberry32(3));
  // знак U9 в 1024-пространстве: полосы по y 208–350, 420–604, 674–816; перевод в [-1,1] с y вверх
  const toY = (v) => 1 - (v / 1024) * 2;
  const bands = [[toY(350), toY(208)], [toY(604), toY(420)], [toY(816), toY(674)]];
  const counts = [0, 0, 0];
  for (let i = 0; i < N; i++) { const y = a[2 * i + 1]; bands.forEach(([lo, hi], k) => { if (y >= lo - 0.03 && y <= hi + 0.03) counts[k]++; }); }
  assert.ok(counts[0] + counts[1] + counts[2] > N * 0.9, `в полосах ${counts}`);
  assert.ok(counts[1] > counts[0] && counts[1] > counts[2], `средняя плотнее ${counts}`);
});

test('channel: примерно поровну слева (WA) и справа (TG)', () => {
  const a = channelTargets(N, mulberry32(5));
  let left = 0; for (let i = 0; i < N; i++) if (a[2 * i] < 0) left++;
  assert.ok(left > N * 0.35 && left < N * 0.65, `left=${left}`);
});
```

- [ ] **Step 2: Запуск — FAIL** — Run: `cd Tools/landing && node --test tools/test/shapes.test.mjs` → Expected: FAIL `Cannot find module`.

- [ ] **Step 3: `src/js/particles/shapes.js`** — перенести генераторы из `buildParticles` прототипа В в чистые функции с сигнатурами из Interfaces. Координаты: знак вписан в квадрат [-1,1] (x = v/512−1, y = 1−v/512 для 1024-пространства), распределение ~22 % верх, ~50 % середина (заливка + кромка), ~22 % низ, ~6 % рассеянная пыль по кругу r ≤ 1. `bubbleTargets`: два скруглённых прямоугольника (клиент — выше-левее, бот — ниже-правее) + хвостики, по кромке с лёгким джиттером. `channelTargets`: кольцо-трубка с хвостом (WA) с центром (-0.5, 0) r≈0.32 и самолётик (TG) с центром (0.5, 0). Без DOM и без `Math.random` — только переданный `rand`.

- [ ] **Step 4: Запуск — PASS** — Run: `node --test tools/test/shapes.test.mjs` → Expected: `# pass 6`.

- [ ] **Step 5: `src/js/particles/field.js`** — порт `initGL`/шейдеров В: один `gl.POINTS` draw, атрибуты = цели всех форм (по одному `vec2` на форму) + seed; вершинный шейдер интерполирует между формами по `uMorph`, добавляет шум/«дыхание», отталкивание от `uMouse`, `uIntro` (сбор из рассеяния за ~1,6 с); фрагментный — мягкая круглая точка с аддитивным смешиванием; цвета: accent/accentHi, средняя полоса логотипа ярче. `setCount(n)` меняет только число рисуемых вершин (буферы не пересоздаются). DPR ограничен 1.5. Возвращает `null` без WebGL. `destroy()` освобождает буферы/программу и `WEBGL_lose_context`.

- [ ] **Step 6: Разметка и стили** — `10-stage.html`: `<div class="stage" data-module="stage" id="top">` содержит `<canvas class="stage-gl" aria-hidden="true"></canvas>`, статичный фолбэк `<img class="stage-mark" src="/assets/__V__/img/mark.svg" alt="" width="520" height="520">` (виден на low и до старта GL), `<section class="hero">` (тексты выше) и `<section class="signal" aria-labelledby="sig-h">` из двух шагов с HTML-оверлеями пузырей. Canvas `position:fixed; inset:0; z-index:0; pointer-events:none`, скрывается (`opacity:0` + пауза), когда `.stage` вне экрана. Hero-текст рендерится сразу (никаких `opacity:0` на h1/подзаголовке/CTA). Мобильный: знак над текстом, высота hero `min-height:100svh`; десктоп: текст слева, знак справа (как в прототипе В).

- [ ] **Step 7: `src/js/stage.js`**

Поведение (`init(root, ctx)`):
1. `tier==='low'` → только статичный знак (`.stage-mark` виден), шаги «Сигнала» — обычные блоки с reveal, пузыри-оверлеи видны статично; вернуть.
2. Иначе `createField(canvas, { count: ctx.tier==='high' ? (navigator.hardwareConcurrency>=8 ? 22000 : 15000) : 7000, dpr: Math.min(devicePixelRatio, 1.5) })`; `null` → как low.
3. `setShapes([logo, bubble, channel])`; прогресс морфа от прокрутки: 0 на верху hero → 1 в середине шага 1 → 2 в середине шага 2 (ScrollTrigger `scrub:true` если есть, иначе своя функция от `scrollY` в rAF).
4. rAF-цикл: `render(t)`, `dt → ctx.governor.sample(dt)`: `'degrade'` → `setCount(Math.round(count*0.5))` и DPR 1; `'disable'` → остановить цикл, плавно скрыть canvas, показать `.stage-mark`.
5. Пауза: IntersectionObserver на `.stage` + `visibilitychange`.
6. Указатель: `pointermove` (только `pointer:fine`) → `setPointer`; на тач — лёгкое «дыхание» без указателя.
7. После первого кадра — `root.classList.add('gl-on')` (CSS прячет `.stage-mark` с переходом 0.6 с).

- [ ] **Step 8: Проверка** — `node tools/build.mjs && node tools/check.mjs --skip-perf --only=desktop` (остальные секции могут быть заглушками) — hero-скриншоты `desktop-000.png`/`mobile-000.png` через Read: знак из частиц, текст читаем, нет ошибок консоли. Затем `?tier=low`: открыть `http://127.0.0.1:8809/?tier=low` снимком — виден статичный знак, canvas скрыт.

- [ ] **Step 9: Commit (по согласию)** — `git add Tools/landing/src/sections/10-stage.html Tools/landing/src/css/10-stage.css Tools/landing/src/js/stage.js Tools/landing/src/js/particles Tools/landing/tools/test/shapes.test.mjs && git commit -m "feat(landing): первый экран и «Сигнал» — частицы с уровнями качества"`

---

### Task 6: История с телефоном (pinned)

**Files:**
- Create: `Tools/landing/src/sections/30-story.html`, `Tools/landing/src/css/30-story.css`, `Tools/landing/src/js/story.js`

**Interfaces:**
- Consumes: контракт модуля; `ctx.gsap`, `ctx.ScrollTrigger`; изображения `shot-{chats,suggestions,pricelist,svodka}-{720,1080}.webp`.
- Produces: секция `#how` (якорь меню «Как работает»).

Эталон: `.planning/sketches/008-premium-landing/a-cinema/index.html` — стили телефона 232–416, разметка film/story 550–720 (без интро «Выбери … ответ», оно уходит в Task 10), JS `markGeometry`/`measure` и story-часть 960–1175.

Главы (eyebrow · H2 · текст · чипы):
1. «01 · Все чаты» · `Все клиенты —<br><em>в одном окне</em>.` · «WhatsApp и Telegram в одном приложении. Бот отвечает с вашего номера — для клиентов ничего не меняется.» · чипы на телефоне: «WhatsApp», «Telegram», «Отвечает 24/7». Экран: `shot-chats`.
2. «02 · Режим «Вместе»» · `Бот предлагает.<br><em>Вы выбираете</em>.` · «До четырёх готовых вариантов на сообщение клиента — одно касание, и ответ ушёл. А в режиме «Авто» бот отвечает сам.» · чипы: «До 4 вариантов», «Одно касание». Экран: `shot-suggestions`.
3. «03 · Прайс» · `Знает ваш <em>прайс</em>.` · «Загрузите прайс-лист фото, Excel, PDF или Word — или заведите каталог в приложении. Бот отвечает по вашим ценам.» · чипы-файлы: «XLSX · Прайс август», «PDF · Колодки и фильтры», «JPG · Фото прайса». Экран: `shot-pricelist`.
4. «04 · Сводка» · `Итог каждого <em>диалога</em>.` · «Заявка собрана, нужен владелец, клиент замолчал — видно сразу, без чтения переписок.» · статус-чипы цветами статусов: «Заявка собрана», «Нужен владелец», «В диалоге», «Клиент замолчал», «Вопрос закрыт». Экран: `shot-svodka`.

- [ ] **Step 1: Разметка** — `<section class="story" id="how" data-module="story">`: для каждой главы `<article class="chapter" data-step="N">` с текстом и `<figure class="phone-screen">` с `<picture>`/`<img srcset="…-720.webp 720w, …-1080.webp 1080w" sizes="(min-width: 900px) 360px, 70vw" loading="lazy" decoding="async" width="720" height="1557" alt="Экран приложения: …">`. Без JS/на low — главы идут столбиком, у каждой свой телефон (читаемо и без pin).
- [ ] **Step 2: Стили** — телефон из прототипа А (рамка, «остров», блик), 3D-наклон `perspective(1400px) rotateY(-14deg) rotateX(6deg)` на desktop, на mobile плоский; прогресс-индикатор 01–04. `.q-low` — без 3D и без sticky.
- [ ] **Step 3: `story.js`** — `tier==='low' || !ctx.ScrollTrigger` → выход (статичная раскладка). Иначе: одна sticky-сцена (`ScrollTrigger.create({ trigger: root, start: 'top top', end: '+=' + (chapters*100) + '%', pin: '.story-stage', scrub: true })`), кросс-фейд экранов (opacity/scale) и текстов глав по прогрессу, чипы влетают `transform`-ом, лёгкий поворот телефона по прогрессу (desktop). Изображения следующей главы предзагружаются, когда прогресс > (i−0.5). Никакого `layout`-чтения в onUpdate (геометрия меряется в `refresh`).
- [ ] **Step 4: Проверка** — build + `node tools/check.mjs --skip-perf`; кадры `mobile-025/040` и `desktop-025/040` через Read: телефон с настоящими экранами, тексты глав не наезжают; `?tier=low` — столбик глав.
- [ ] **Step 5: Commit (по согласию)** — `git add Tools/landing/src/sections/30-story.html Tools/landing/src/css/30-story.css Tools/landing/src/js/story.js && git commit -m "feat(landing): история с закреплённым телефоном и настоящими экранами"`

---

### Task 7: «Попробуйте сами» (интерактивное демо)

**Files:**
- Create: `Tools/landing/src/sections/40-demo.html`, `Tools/landing/src/css/40-demo.css`, `Tools/landing/src/js/demo.js`, `Tools/landing/src/js/demo/scenarios.js`, `Tools/landing/tools/test/scenarios.test.mjs`

**Interfaces:**
- Consumes: контракт модуля.
- Produces: `SCENARIOS` — массив `{ id, niche, name, channel: 'wa'|'tg', times: [q, a, f], source: { kind, title }, question, cards: [{ title, text, best?: true }], follow, outcome: { status: 'order'|'owner'|'dialog'|'silent'|'closed', label } }`; секция `#demo`.

Эталон: `.planning/sketches/008-premium-landing/b-live-chat/index.html` — разметка hero-демо 621–690, стили демо (блоки `.demo*`, `.tray`, `.card`, `.bubble`, `.src`), JS 1007–1480 (`sleep`, `bubble`, `openTray`, `revealCards`, `pick`, `setMode`, `local`). Аврора (904–1006) НЕ переносится — фон секции CSS-градиент.

- [ ] **Step 1: Падающий тест данных** — `tools/test/scenarios.test.mjs`

```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { SCENARIOS } from '../../src/js/demo/scenarios.js';

const FORBIDDEN = [/отзыв/i, /\d+\s*\+?\s*(компан|клиент|бизнес)/i, /за\s+\d+\s*сек/i, /рейтинг/i, /1С|CRM/];

test('четыре ниши в фиксированном порядке', () => {
  assert.deepEqual(SCENARIOS.map((s) => s.id), ['parts', 'flowers', 'kaspi', 'repair']);
});
for (const s of SCENARIOS) {
  test(`${s.id}: 3–4 карточки, ровно одна лучшая, непустые тексты`, () => {
    assert.ok(s.cards.length >= 3 && s.cards.length <= 4);
    assert.equal(s.cards.filter((c) => c.best).length, 1);
    for (const c of s.cards) { assert.ok(c.title.length > 0 && c.title.length <= 24); assert.ok(c.text.length > 10); }
    assert.ok(['wa', 'tg'].includes(s.channel));
    assert.ok(['order', 'owner', 'dialog', 'silent', 'closed'].includes(s.outcome.status));
  });
  test(`${s.id}: суммы в ₸ с неразрывными пробелами, без запрещённых обещаний`, () => {
    const all = [s.question, s.follow, ...s.cards.map((c) => c.text)].join(' ');
    for (const m of all.matchAll(/(\d[\d\s ]*)\s?₸/g)) {
      assert.ok(!/\d \d/.test(m[0]), `обычный пробел в «${m[0]}»`);
      assert.ok(/ ₸$/.test(m[0]), `нет NBSP перед ₸ в «${m[0]}»`);
    }
    for (const re of FORBIDDEN) assert.ok(!re.test(all), `запрещено: ${re}`);
  });
}
```

- [ ] **Step 2: Запуск — FAIL** — `node --test tools/test/scenarios.test.mjs` → `Cannot find module`.

- [ ] **Step 3: `src/js/demo/scenarios.js`** — четыре сценария из прототипа Б (строки 1019–1056: Айгерим/запчасти/WA, Дана/цветы/TG, Руслан/Kaspi-продавец/WA, Тимур/ремонт/TG), переименовать поля по Interfaces, `id` = parts/flowers/kaspi/repair, `niche` = «Автозапчасти»/«Цветы»/«Kaspi-продавец»/«Ремонт телефонов», все числа с ` ` между разрядами и перед `₸` (например `'4 900 ₸'`), статусы: parts→order «Заявка собрана», flowers→dialog «В диалоге», kaspi→closed «Вопрос закрыт», repair→owner «Нужен владелец».

- [ ] **Step 4: Запуск — PASS** — `node --test tools/test/scenarios.test.mjs` → `# fail 0`.

- [ ] **Step 5: Разметка/стили** — заголовок `Попробуйте <em>сами</em>.` + лид «Это демо на примерах: нажмите на любой вариант ответа — или переключите «Авто», и бот ответит сам.»; вкладки ниш (`role="tablist"`, кнопки `role="tab"`), окно чата (шапка с именем клиента, индикатор канала WA/TG цветом канала, «печатает…»), лента сообщений (`aria-live="polite"`), панель «Предложения» с карточками (`<button>` с заголовком-капсом и текстом), переключатель «Вместе / Авто» (`role="switch"`, `aria-checked`), строка источника («нашёл в прайсе: …»), итоговый статус-чип. Мобильный — чат во всю ширину, карточки под ним; десктоп — чат по центру, поясняющие подписи сбоку.
- [ ] **Step 6: `demo.js`** — порт логики Б: сценарий проигрывается, когда секция в зоне видимости (IO), пауза вне экрана и при `document.hidden`; «призрак-курсор» выбирает лучшую карточку через ~3 с, если посетитель не выбрал сам; клик/Enter/Space по карточке — выбор; в «Авто» карточки не показываются, бот отвечает лучшей с пометкой «Ответил бот · «Авто»»; после ответа — `follow` клиента и итоговый статус; затем следующая ниша (или выбранная вкладкой). `tier==='low'`/reduced — без «печатает…» и пролётов, сообщения появляются сразу, автопроигрывание выключено (только по кликам). Никакого WebGL, никаких чтений layout в цикле.
- [ ] **Step 7: Проверка** — build + check; кадр демо на mobile/desktop через Read; вручную через playwright: `page.click('.demo-card >> nth=1')` → в ленте появился пузырь с текстом второй карточки.
- [ ] **Step 8: Commit (по согласию)** — `git add Tools/landing/src/sections/40-demo.html Tools/landing/src/css/40-demo.css Tools/landing/src/js/demo.js Tools/landing/src/js/demo Tools/landing/tools/test/scenarios.test.mjs && git commit -m "feat(landing): интерактивное демо «Попробуйте сами» по нишам"`

---

### Task 8: Ниши и «три шага»

**Files:**
- Create: `Tools/landing/src/sections/50-niches.html`, `Tools/landing/src/css/50-niches.css`, `Tools/landing/src/js/niches.js`, `Tools/landing/src/sections/60-steps.html`, `Tools/landing/src/css/60-steps.css`

**Interfaces:**
- Consumes: контракт модуля (только niches); reveal-атрибуты `data-reveal`/`data-split` (Task 2).

Эталон: `.planning/sketches/008-premium-landing/b-live-chat/index.html` 1626–1661 (данные ниш и лента), стили `.nch*`/`.marquee`.

Ниши (заголовок · вопрос клиента · что делает бот):
1. Автозапчасти · «Есть колодки на Camry 2015?» · «Подбирает по марке и году, называет артикул, оригинал и аналог из вашего прайса.»
2. Оптовая торговля · «Какая цена от 10 коробок?» · «Отвечает по оптовому прайсу и собирает заявку: позиции, количество, контакты.»
3. Цветы · «Соберёте букет к 18:00?» · «Знает состав и цены букетов, уточняет адрес и время — и передаёт заказ флористу.»
4. Kaspi-продавцы · «Есть в наличии? Можно в рассрочку?» · «Отвечает про наличие и цены по вашему каталогу, даёт ссылку на товар в вашем магазине.»
5. Обучение · «Когда ближайший набор и сколько стоит?» · «Рассказывает о курсах, расписании и ценах, записывает на пробное занятие.»
6. Ремонт телефонов · «Сколько стоит замена экрана на iPhone 13?» · «Называет цену «от» по прайсу и записывает на диагностику — точную цену подтверждает мастер.»
Лента-marquee: названия ниш через « · » + «и любой бизнес, где клиенты пишут в мессенджеры».

Три шага (`#start`): H2 `Три шага — и бот <em>работает</em>.`; «01 Подключите номер — WhatsApp или Telegram по QR-коду или коду, за пару минут. Отдельная SIM не нужна.» «02 Загрузите прайс — фото, Excel, PDF или Word; или заведите товары и услуги в приложении.» «03 Выберите режим — «Авто»: бот отвечает сам. «Вместе»: предлагает варианты, отправляете вы.» Соединительная линия между шагами прорисовывается при прокрутке (CSS `scale` по `--p`, выставляемому reveal-классом).

- [ ] **Step 1: Разметка и стили обеих секций** (карточки ниш с подсветкой рамки за курсором: CSS-переменные `--mx/--my` + `radial-gradient` в `::before`, только `@media (hover:hover)`; лента — CSS `@keyframes` на `transform: translateX`, `animation-play-state: paused` у `.q-low` и вне экрана).
- [ ] **Step 2: `niches.js`** — IO ставит/снимает класс `.running` на ленте; `pointermove` на сетке (pointer:fine) пишет `--mx/--my` в карточку под курсором через rAF.
- [ ] **Step 3: Проверка** — build + check (`--skip-perf`), кадры `*-055/070` через Read.
- [ ] **Step 4: Commit (по согласию)** — `git add Tools/landing/src/sections/{50-niches,60-steps}.html Tools/landing/src/css/{50-niches,60-steps}.css Tools/landing/src/js/niches.js && git commit -m "feat(landing): ниши с лентой и три шага подключения"`

---

### Task 9: Тарифы

**Files:**
- Create: `Tools/landing/src/sections/70-pricing.html`, `Tools/landing/src/css/70-pricing.css`, `Tools/landing/src/js/pricing.js`, `Tools/landing/src/js/pricing-format.js`, `Tools/landing/tools/test/pricing.test.mjs`

**Interfaces:**
- Produces: `formatKzt(n) → string` («19 990» c U+00A0, без «₸»); `yearlyMonthly(yearKzt) → number` (округление до целого вниз); `savingsPercent(monthKzt, yearKzt) → number` (целое, округление вниз); `PLANS` — `[{ id, name, forWhom, month, year, bots, channels, dialogs, popular? }]`.

Эталон: `.planning/sketches/008-premium-landing/c-signal/index.html` 523–588 (разметка), 1030–1082 (`setPrices`, переключатель с «бегунком»); прокрутка цифр — `b-live-chat` 1703+.

- [ ] **Step 1: Падающие тесты** — `tools/test/pricing.test.mjs`

```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { formatKzt, yearlyMonthly, savingsPercent, PLANS } from '../../src/js/pricing-format.js';

test('formatKzt разбивает разряды неразрывным пробелом', () => {
  assert.equal(formatKzt(9990), '9 990');
  assert.equal(formatKzt(199990), '199 990');
  assert.equal(formatKzt(399990), '399 990');
  assert.equal(formatKzt(900), '900');
});
test('yearlyMonthly', () => {
  assert.equal(yearlyMonthly(99000), 8250);
  assert.equal(yearlyMonthly(198990), 16582);
  assert.equal(yearlyMonthly(399990), 33332);
});
test('savingsPercent — честная выгода годовой оплаты', () => {
  assert.equal(savingsPercent(9990, 99000), 17);
  assert.equal(savingsPercent(19990, 198990), 17);
  assert.equal(savingsPercent(39900, 399990), 16);
});
test('PLANS совпадают с PlanCatalog.cs', () => {
  assert.deepEqual(PLANS.map((p) => [p.id, p.month, p.year, p.bots, p.channels, p.dialogs, !!p.popular]), [
    ['start', 9990, 99000, 1, 1, 300, false],
    ['business', 19990, 198990, 3, 3, 1000, true],
    ['network', 39900, 399990, 5, 5, 3000, false],
  ]);
});
```

- [ ] **Step 2: Запуск — FAIL** — `node --test tools/test/pricing.test.mjs`.

- [ ] **Step 3: `src/js/pricing-format.js`**

```js
// Pure pricing helpers. Numbers mirror Assets/Scripts/Billing/PlanCatalog.cs — when the
// catalog changes, change PLANS here (pricing.test.mjs pins the values).
export const PLANS = [
  { id: 'start', name: 'Старт', forWhom: 'Для одной точки', month: 9990, year: 99000, bots: 1, channels: 1, dialogs: 300 },
  { id: 'business', name: 'Бизнес', forWhom: 'Для растущего бизнеса', month: 19990, year: 198990, bots: 3, channels: 3, dialogs: 1000, popular: true },
  { id: 'network', name: 'Сеть', forWhom: 'Для сети точек', month: 39900, year: 399990, bots: 5, channels: 5, dialogs: 3000 },
];
export function formatKzt(n) {
  return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
}
export function yearlyMonthly(yearKzt) { return Math.floor(yearKzt / 12); }
export function savingsPercent(monthKzt, yearKzt) { return Math.floor((1 - yearKzt / (monthKzt * 12)) * 100); }
```

- [ ] **Step 4: Запуск — PASS** — `node --test tools/test/pricing.test.mjs` → `# pass 4`.

- [ ] **Step 5: Разметка** (цены в HTML — месячные, чтобы без JS было верно): H2 `Честные цены <em>в тенге</em>.`, лид «Подписка оформляется в приложении. Никаких договоров, внедрений и скрытых платежей.»; баннер пробного: «Начните бесплатно — 5 дней и 150 диалогов, без карты.»; переключатель «Месяц / Год · выгода до 17 %» (`role="radiogroup"`); 3 карточки (Бизнес — бейдж «Популярный», акцентная рамка; на mobile первой); строки: «N бот(а/ов)», «N канал(а/ов)», «N диалогов в месяц» (плюрализация вручную для 1/3/5: «1 бот», «3 бота», «5 ботов», «1 канал», «3 канала», «5 каналов»); кнопка карточки «Хочу ранний доступ» → `#lead`. В режиме «Год»: крупно `formatKzt(year)` + «₸ / год», мелко «≈ {formatKzt(yearlyMonthly(year))} ₸ в месяц». Под карточками: «Закончились диалоги — докупите 500 за 3 900 ₸. Оплата — подпиской в App Store или Google Play, отмена в любой момент.»
- [ ] **Step 6: `pricing.js`** — переключатель: «бегунок» переезжает `transform`-ом; цифры «докручиваются» за 0,6 с (rAF-интерполяция числа с `formatKzt`, `tabular-nums`, фиксированная ширина контейнера — без сдвига разметки); на low/reduced — мгновенная замена. Тилт карточек 3° за курсором только на pointer:fine.
- [ ] **Step 7: Проверка** — build + check; playwright: клик «Год» → в карточке Бизнес текст содержит `198 990`; кадры тарифов через Read.
- [ ] **Step 8: Commit (по согласию)** — `git add Tools/landing/src/sections/70-pricing.html Tools/landing/src/css/70-pricing.css Tools/landing/src/js/pricing*.js Tools/landing/tools/test/pricing.test.mjs && git commit -m "feat(landing): тарифы с переключателем месяц/год и анимацией цифр"`

---

### Task 10: Вопросы, финал «Выбери ответ.», форма заявки

**Files:**
- Create: `Tools/landing/src/sections/80-faq.html`, `Tools/landing/src/css/80-faq.css`, `Tools/landing/src/sections/90-final.html`, `Tools/landing/src/css/90-final.css`, `Tools/landing/src/js/final.js`, `Tools/landing/src/js/form.js`, `Tools/landing/tools/test/form.test.mjs`

**Interfaces:**
- Produces: `validatePhone(value) → { ok: boolean, digits: number }` (ok ⇔ 10–15 цифр); `encodeLead(fields) → URLSearchParams` (ровно `name`, `phone`, `website`, `source`); `submitLead(endpoint, fields, fetchImpl) → Promise<{ ok: true } | { ok: false, reason: 'bad_phone'|'server'|'network' }>`; `initForm(formEl)`; DOM-контракт для стенда: `form#leadForm`, `#leadName`, `#leadPhone`, `#leadBtn`, `#leadErr` (`role="alert"`, `hidden`), `#leadOk` (`hidden` до успеха), `data-endpoint="https://n8n.choosereply.com/webhook/LandingLead"` на форме, honeypot `input[name=website]` в `.hp`, `input[type=hidden][name=source][value=landing]`.

Эталоны: интро «Выбери … ответ.» — `.planning/sketches/008-premium-landing/a-cinema/index.html` `buildIntro` 985–1027 и соответствующая разметка/стили hero (128–231, 550–600); форма — `Tools/landing` v1 (git `HEAD:Tools/landing/index.html`, блок `#lead` и скрипт формы).

FAQ (вербатим, `<details>`/`<summary>`; первый открыт):
1. **Нужна ли отдельная SIM-карта или новый номер?** — Нет. Бот подключается к вашему WhatsApp или Telegram по QR-коду или коду из приложения. Клиенты пишут на тот же номер, что и раньше.
2. **Что, если бот не знает ответа?** — Бот отвечает по вашему прайсу и каталогу. Если нужного там нет, он говорит клиенту, что уточнит, а диалог попадает в «Сводку» как «Нужен владелец». А в режиме «Вместе» без вашего выбора не уходит ни одно сообщение.
3. **Можно ли быстро выключить бота?** — Да, одним касанием: кнопка «Авто» на карточке бота. Можно выключить и отдельный канал — WhatsApp или Telegram.
4. **Где хранятся мои данные и переписки?** — На защищённых серверах, передача — по шифрованному соединению. Данные не продаются и не передаются для рекламы, рекламных трекеров в приложении нет. Удаление бота удаляет его прайс-листы; полное удаление данных — по запросу. Подробно — в <a href="/privacy.html">Политике конфиденциальности</a>.
5. **Что будет после пробного периода?** — Пробный период — 5 дней и 150 диалогов, без карты. Дальше выбираете тариф в приложении; оплата — подпиской в App Store или Google Play, отменить можно в любой момент.
6. **Когда можно начать?** — Приложение сейчас проходит проверку в App Store и Google Play. Оставьте номер — напишем в WhatsApp, как только откроем доступ, и поможем настроить бота.

Финал (`#lead`): огромная кинетическая строка «Выбери … ответ.» (между словами три карточки вариантов — «Дешевле», «Цена и артикул» ✓, «Оформить?» — средняя подсвечивается и «выбирается», как в интро прототипа А; запускается при входе в экран, один раз), под ней карточка формы: H2 `Получите <em>ранний доступ</em>.`, лид «Оставьте номер — напишем в WhatsApp, когда откроем доступ, и поможем настроить бота под ваш бизнес.», поля «Как к вам обращаться» (плейсхолдер «Например, Айдос», `autocomplete="name"`), «Номер телефона в WhatsApp» (плейсхолдер «+7 ___ ___-__-__», `type="tel"`, `autocomplete="tel"`, `required`, `inputmode="tel"`), кнопка «Оставить заявку», мелко «Нажимая кнопку, вы соглашаетесь с <a href="/privacy.html">политикой конфиденциальности</a>.», успех: знак U9 с «выбранной» полосой, «Спасибо! Мы напишем вам в WhatsApp.».

- [ ] **Step 1: Падающие тесты** — `tools/test/form.test.mjs`

```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validatePhone, encodeLead, submitLead } from '../../src/js/form.js';

test('validatePhone: 10–15 цифр', () => {
  assert.equal(validatePhone('+7 (700) 000-00-00').ok, true);
  assert.equal(validatePhone('8700000000').ok, true);
  assert.equal(validatePhone('12345').ok, false);
  assert.equal(validatePhone('+7 700 000 00 00 00 00').ok, false);
  assert.equal(validatePhone('').ok, false);
});
test('encodeLead: ровно четыре поля, source=landing, пустой honeypot', () => {
  const p = encodeLead({ name: ' Айдос ', phone: '+7 700 000 00 00', website: '', extra: 'x' });
  assert.deepEqual([...p.keys()], ['name', 'phone', 'website', 'source']);
  assert.equal(p.get('name'), 'Айдос');
  assert.equal(p.get('source'), 'landing');
  assert.equal(p.get('website'), '');
});
test('submitLead: плохой телефон — без сети', async () => {
  let called = 0;
  const r = await submitLead('https://x/webhook/LandingLead', { phone: '123' }, async () => { called++; });
  assert.deepEqual(r, { ok: false, reason: 'bad_phone' });
  assert.equal(called, 0);
});
test('submitLead: form-encoded POST и успех', async () => {
  let seen;
  const r = await submitLead('https://x/webhook/LandingLead', { name: 'А', phone: '+77000000000', website: '' }, async (url, init) => {
    seen = { url, init };
    return { json: async () => ({ success: true }) };
  });
  assert.deepEqual(r, { ok: true });
  assert.equal(seen.init.method, 'POST');
  assert.ok(seen.init.body instanceof URLSearchParams);
});
test('submitLead: bad_request сервера → bad_phone, иное → server, исключение → network', async () => {
  assert.deepEqual(await submitLead('u', { phone: '+77000000000' }, async () => ({ json: async () => ({ success: false, error: 'bad_request' }) })), { ok: false, reason: 'bad_phone' });
  assert.deepEqual(await submitLead('u', { phone: '+77000000000' }, async () => ({ json: async () => ({ success: false, error: 'db' }) })), { ok: false, reason: 'server' });
  assert.deepEqual(await submitLead('u', { phone: '+77000000000' }, async () => { throw new Error('offline'); }), { ok: false, reason: 'network' });
});
```

- [ ] **Step 2: Запуск — FAIL** — `node --test tools/test/form.test.mjs`.

- [ ] **Step 3: `src/js/form.js`**

```js
// Lead form. The wire contract is v1's, unchanged: form-encoded POST (no CORS preflight)
// to the LandingLead webhook with exactly name, phone, website (honeypot), source=landing;
// the webhook answers {success:true} or {success:false,error:"bad_request"|…}.
export function validatePhone(value) {
  const digits = (String(value || '').match(/\d/g) || []).length;
  return { ok: digits >= 10 && digits <= 15, digits };
}

export function encodeLead(fields) {
  const p = new URLSearchParams();
  p.set('name', String(fields.name || '').trim());
  p.set('phone', String(fields.phone || '').trim());
  p.set('website', String(fields.website || ''));
  p.set('source', 'landing');
  return p;
}

export async function submitLead(endpoint, fields, fetchImpl) {
  if (!validatePhone(fields.phone).ok) return { ok: false, reason: 'bad_phone' };
  try {
    const r = await fetchImpl(endpoint, { method: 'POST', body: encodeLead(fields) });
    const j = await r.json();
    if (j && j.success) return { ok: true };
    return { ok: false, reason: j && j.error === 'bad_request' ? 'bad_phone' : 'server' };
  } catch {
    return { ok: false, reason: 'network' };
  }
}

const MESSAGES = {
  bad_phone: 'Проверьте номер телефона — нужно от 10 до 15 цифр.',
  server: 'Не получилось отправить. Попробуйте ещё раз чуть позже.',
  network: 'Нет связи с сервером. Проверьте интернет и попробуйте ещё раз.',
};

export function initForm(form, { onSuccess } = {}) {
  if (!form) return;
  const btn = form.querySelector('#leadBtn'), err = form.querySelector('#leadErr');
  const ok = document.getElementById('leadOk'), phone = form.querySelector('#leadPhone');
  const label = btn.textContent;
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    err.hidden = true;
    const fields = Object.fromEntries(new FormData(form));
    if (!validatePhone(fields.phone).ok) { err.textContent = MESSAGES.bad_phone; err.hidden = false; phone.focus(); return; }
    btn.disabled = true; btn.textContent = 'Отправляем…';
    const r = await submitLead(form.dataset.endpoint, fields, (u, i) => fetch(u, i));
    if (r.ok) {
      form.hidden = true; ok.hidden = false; ok.focus();
      if (onSuccess) onSuccess();
      return;
    }
    err.textContent = MESSAGES[r.reason]; err.hidden = false;
    btn.disabled = false; btn.textContent = label;
    if (r.reason === 'bad_phone') phone.focus();
  });
}
```

- [ ] **Step 4: Запуск — PASS** — `node --test tools/test/form.test.mjs` → `# pass 5`.

- [ ] **Step 5: Разметка/стили FAQ и финала** — FAQ: `<details>` с кастомным маркером «+»/«−» (CSS, без JS), плавное раскрытие через `::details-content` там, где поддерживается (иначе мгновенно). Финал: кинетическая строка (clamp 48–160 px), форма в «стеклянной» карточке с акцентной рамкой; `.hp{position:absolute;left:-9999px}`; поля ≥ 48 px высотой, `font-size ≥ 16px` (иначе iOS зумит).
- [ ] **Step 6: `final.js`** — `initForm(root.querySelector('#leadForm'), { onSuccess: () => root.classList.add('sent') })` для ЛЮБОГО уровня; кинетика «Выбери … ответ.» — GSAP-таймлайн при входе в экран (IO, один раз), на low/reduced — сразу конечное состояние.
- [ ] **Step 7: Проверка — стенд зелёный по форме** — build + `node tools/check.mjs --skip-perf`. Expected: в `fails` нет пунктов `form:`; `report.form.params` = `{ name: 'Проверка', phone: '+7 700 000 00 00', website: '', source: 'landing' }`; `liveRequests` пуст.
- [ ] **Step 8: Commit (по согласию)** — `git add Tools/landing/src/sections/{80-faq,90-final}.html Tools/landing/src/css/{80-faq,90-final}.css Tools/landing/src/js/{final,form}.js Tools/landing/tools/test/form.test.mjs && git commit -m "feat(landing): вопросы, финал «Выбери ответ.» и форма заявки (контракт v1)"`

---

### Task 11: Мета, OG-картинка, JSON-LD, страж текста

**Files:**
- Create: `Tools/landing/tools/og.html`, `Tools/landing/tools/og.mjs`, `Tools/landing/src/static/og.png`, `Tools/landing/tools/test/copy.test.mjs`
- Modify: `Tools/landing/src/index.html` (строка `<!-- @jsonld (Task 11) -->`)
- Delete: `Tools/landing/og.png`

**Interfaces:**
- Consumes: собранный `dist/index.html` (страж текста), шрифты/знак из Task 3.

- [ ] **Step 1: JSON-LD** — заменить маркер в `src/index.html` на:
```html
<script type="application/ld+json">{"@context":"https://schema.org","@type":"SoftwareApplication","name":"Choose Reply","applicationCategory":"BusinessApplication","operatingSystem":"iOS, Android","description":"ИИ-бот для WhatsApp и Telegram: отвечает клиентам с номера владельца по его прайсу — сам или предлагая варианты ответа на выбор.","inLanguage":"ru","url":"https://choosereply.com/","offers":{"@type":"AggregateOffer","priceCurrency":"KZT","lowPrice":"9990","highPrice":"39900","offerCount":"3"}}</script>
```
- [ ] **Step 2: OG** — `tools/og.html` (1200×630: фон-градиент бренда, знак U9 слева, справа «Ответ — за ботом. / Выбор — за вами.» Onest 600 + курсив-акцент, внизу «choosereply.com · WhatsApp и Telegram»; шрифты по относительным путям `../src/assets/fonts/…`) и `tools/og.mjs`:
```js
// Render tools/og.html → src/static/og.png (1200×630) through headless Chrome.
import { chromium } from 'playwright-core';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const browser = await chromium.launch({ executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
await page.goto(pathToFileURL(path.join(HERE, 'og.html')).href, { waitUntil: 'load' });
await page.evaluate(() => document.fonts.ready);
await page.screenshot({ path: path.join(HERE, '../src/static/og.png') });
await browser.close();
console.log('og.png written');
```
Run: `cd Tools/landing && node tools/og.mjs && rm og.png` → просмотр `src/static/og.png` через Read (кириллица без «тофу», знак чёткий).
- [ ] **Step 3: Страж текста** — `tools/test/copy.test.mjs` (собирает сайт во временную папку и проверяет итоговый HTML):
```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from '../build.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const dist = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-copy-'));
build({ src: path.join(ROOT, 'src'), dist, legal: path.resolve(ROOT, '../../docs/legal') });
const html = fs.readFileSync(path.join(dist, 'index.html'), 'utf8');
const text = html.replace(/<script[\s\S]*?<\/script>/g, ' ').replace(/<style[\s\S]*?<\/style>/g, ' ').replace(/<[^>]+>/g, ' ');

test('нет запрещённых обещаний', () => {
  for (const re of [/отзыв/i, /\d[\d\s ]*\+?\s*(компан|клиентов|бизнесов)/i, /за\s+\d+\s*сек/i, /рейтинг/i, /\b1С\b|\bCRM\b/, /казахск/i, /на паузу/i])
    assert.ok(!re.test(text), `найдено: ${re}`);
});
test('цены тарифов на месте, с NBSP', () => {
  for (const p of ['9 990', '19 990', '39 900', '3 900']) assert.ok(text.includes(p), p);
});
test('юр-ссылки и форма с приёмником заявок', () => {
  assert.ok(html.includes('href="/privacy.html"') && html.includes('href="/terms.html"'));
  assert.ok(html.includes('data-endpoint="https://n8n.choosereply.com/webhook/LandingLead"'));
  assert.ok(/name="website"/.test(html) && /name="source" value="landing"/.test(html));
});
test('все восемь секций собраны, маркеров не осталось', () => {
  for (const id of ['id="top"', 'id="how"', 'id="demo"', 'id="niches"', 'id="start"', 'id="pricing"', 'id="faq"', 'id="lead"']) assert.ok(html.includes(id), id);
  assert.ok(!html.includes('__V__') && !html.includes('@sections') && !html.includes('@jsonld'));
});
test('нет внешних скриптов/стилей (самохостинг)', () => {
  assert.ok(!/(src|href)="https:\/\/(cdn\.jsdelivr\.net|fonts\.googleapis\.com|fonts\.gstatic\.com|cdnjs)/.test(html));
});
```
Run: `node --test tools/test/copy.test.mjs` → Expected после Task 5–10: `# fail 0`.
- [ ] **Step 4: Commit (по согласию)** — `git add Tools/landing/src/index.html Tools/landing/src/static/og.png Tools/landing/tools/{og.html,og.mjs} Tools/landing/tools/test/copy.test.mjs && git commit -m "feat(landing): JSON-LD, новая OG-картинка, страж правдивости текста"`

---

### Task 12: Интеграция и доводка до бюджетов

**Files:**
- Modify: любые файлы `Tools/landing/src/**` по находкам (в пределах контрактов задач 5–11)

- [ ] **Step 1: Полная сборка и тесты** — `cd Tools/landing && node tools/build.mjs && node --test tools/test/*.test.mjs` → `# fail 0`.
- [ ] **Step 2: Стенд с baseline** — сервер прототипов 8808 (`.planning/sketches/008-premium-landing`) и `npm run serve` (8809) запущены; `node tools/check.mjs --baseline=http://127.0.0.1:8808/baseline/index.html` → цель exit 0. Каждое нарушение из `fails` чинить по месту (тяжёлый кадр — искать чтения layout в rAF, `backdrop-filter` на больших площадях, `filter: blur` в анимациях, лишние `box-shadow` в прокрутке; частицы — снижать `count` уровня medium).
- [ ] **Step 3: Визуальный проход** — все PNG `.check/mobile-*.png` и `.check/desktop-*.png` через Read (уменьшенные `sips -Z 1000`): наложения, обрезанный текст, «вдовы» в заголовках, контраст серого текста, одинаковые отступы секций, выравнивание сетки; + `?tier=low` на 390 (снимки вручную playwright-скриптом) — сайт цельный и красивый без эффектов.
- [ ] **Step 4: Клавиатура и доступность** — playwright: Tab по странице — фокус виден на ссылках меню, CTA, вкладках демо, карточках демо, переключателях тарифов, `summary` FAQ, полях формы; `Enter` на карточке демо отправляет её; `axe-core` не используется (нет зависимостей) — проверить вручную: у изображений `alt`, у кнопок имена, `lang="ru"`.
- [ ] **Step 5: Независимое ревью** — три линзы (как в прототипах): арт-директор (крафт, «вау», единство стиля), инженер скорости (перезапуск стенда 2×, 6× CPU, reduced-motion, без WebGL через `--disable-gpu`/`?tier=low`), владелец бизнеса из КЗ на 390 px (понятно ли за 5 секунд, доверие, CTA). Исправить подтверждённые находки, стенд снова зелёный.
- [ ] **Step 6: Commit (по согласию)** — `git add Tools/landing && git commit -m "fix(landing): доводка до бюджетов скорости и визуальный проход"`

---

### Task 13: Инструменты выкладки и документация

**Files:**
- Create: `Tools/landing/tools/deploy.sh`, `Tools/landing/tools/Caddyfile.site`, `Tools/landing/README.md`
- Modify: `CLAUDE.md` (раздел Build & Development — одна строка про лендинг v2)

- [ ] **Step 1: `tools/Caddyfile.site`** — целевой блок (заменяет блок `choosereply.com { … }` на VPS, остальные блоки не трогаются):
```
choosereply.com {
	root * /srv/site
	encode zstd gzip
	@assets path /assets/*
	header @assets Cache-Control "public, max-age=31536000, immutable"
	@html path / /index.html /privacy.html /terms.html
	header @html Cache-Control "no-cache"
	file_server
}
```
- [ ] **Step 2: `tools/deploy.sh`** (по умолчанию `--dry-run`; настоящая выкладка — только `--go` после «да» владельца):
```bash
#!/usr/bin/env bash
# Deploy dist/ to the VPS. Default is a dry run; pass --go to actually deploy.
#  1. backup ~/choosereply/site → site.bak-<stamp>, Caddyfile → Caddyfile.bak-<stamp>
#  2. rsync dist/ → site/ WITHOUT --delete (review/ and old asset dirs stay)
#  3. swap the choosereply.com block in the Caddyfile for tools/Caddyfile.site, validate, reload
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; DIST="$HERE/../dist"; HOST=choosereply
GO=0; [[ "${1:-}" == "--go" ]] && GO=1
STAMP=$(date +%Y%m%d-%H%M%S)
test -f "$DIST/index.html" || { echo "no dist/ — run node tools/build.mjs"; exit 1; }
run() { if [[ $GO == 1 ]]; then "$@"; else echo "DRY: $*"; fi; }
run ssh "$HOST" "cp -a ~/choosereply/site ~/choosereply/site.bak-$STAMP && cp -a ~/choosereply/Caddyfile ~/choosereply/Caddyfile.bak-$STAMP"
run rsync -rlptvz --exclude '.DS_Store' "$DIST/" "$HOST:choosereply/site/"
run scp "$HERE/Caddyfile.site" "$HOST:/tmp/Caddyfile.site"
run ssh "$HOST" 'python3 - <<"PY"
import re, pathlib
p = pathlib.Path.home() / "choosereply/Caddyfile"
src = p.read_text()
new = pathlib.Path("/tmp/Caddyfile.site").read_text().strip() + "\n"
out, n = re.subn(r"(?ms)^choosereply\.com \{.*?^\}\n", new, src, count=1)
assert n == 1, "choosereply.com block not found"
p.write_text(out)
PY'
run ssh "$HOST" 'cd ~/choosereply && docker compose exec -T caddy caddy validate --config /etc/caddy/Caddyfile && docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile'
echo "rollback: ssh $HOST 'cd ~/choosereply && rm -rf site && mv site.bak-$STAMP site && cp Caddyfile.bak-$STAMP Caddyfile && docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile'"
```
Перед первым `--go` исполнитель подтверждает по VPS (только чтение): имя сервиса Caddy в `docker compose ps`, путь монтирования Caddyfile (`/etc/caddy/Caddyfile`) и `root /srv/site` ↔ `~/choosereply/site`; при расхождении — поправить команды в скрипте.
- [ ] **Step 3: `README.md`** — коротко: структура, `npm i`, `npm run build`, `npm test`, `npm run serve` + `npm run check -- --baseline=…`, `?tier=low|medium|high`, как обновить цены (PLANS + тест), как перегенерировать ассеты (fetch-fonts, fetch-vendor, make-images, og), выкладка (`deploy.sh` → dry-run → `--go` по согласию), откат.
- [ ] **Step 4: CLAUDE.md** — в Build & Development добавить пункт: «**Лендинг choosereply.com** (`Tools/landing/`, v2 с 2026-09): исходники `src/` → `node tools/build.mjs` → `dist/` (публикуется только dist); `npm test` (node:test) + `node tools/check.mjs` (headless-бюджеты, прод заблокирован, форма — заглушка); выкладка `tools/deploy.sh` (dry-run по умолчанию, без `--delete`, `review/` не трогать); цены в `src/js/pricing-format.js` зеркалят `PlanCatalog.cs`.»
- [ ] **Step 5: Dry-run** — `bash tools/deploy.sh` → печатает команды `DRY: …`, ничего не меняет.
- [ ] **Step 6: Commit (по согласию)** — `git add Tools/landing/tools/{deploy.sh,Caddyfile.site} Tools/landing/README.md CLAUDE.md && git commit -m "docs(landing): инструменты выкладки v2 и README"`

---

### Task 14: Выкладка (только по отдельному «да» владельца)

- [ ] **Step 1: Показать владельцу** итог (скриншоты 390/1440, цифры стенда против baseline) и спросить разрешение на выкладку и изменение Caddyfile.
- [ ] **Step 2: `bash tools/deploy.sh --go`**.
- [ ] **Step 3: Проверки с боевого домена** — `curl -sI https://choosereply.com/` → 200, `content-encoding` при `Accept-Encoding: gzip`, `cache-control: no-cache`; `curl -sI https://choosereply.com/assets/<hash>/site.css` → `immutable`; 200 на `/privacy.html`, `/terms.html`, `/review/923843a986255cba/` (листинг или файл — как было); `node tools/check.mjs --url=https://choosereply.com/ --skip-perf` (форма — заглушкой, прод-POST заблокирован стендом).
- [ ] **Step 4: Тестовая заявка** — только если владелец разрешит: одна отправка с именем «test» и его номером; иначе пропустить.
- [ ] **Step 5: Память** — обновить `project_premium_landing_v2.md` (выложено, хеш ассетов, где бэкап).
