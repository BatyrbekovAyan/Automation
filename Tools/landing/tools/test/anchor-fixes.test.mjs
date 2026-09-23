// Повторное ревью 3 — открытые пункты O-1 и O-2 (обе — про то, КУДА приходит переход на форму).
//
//   O-1: заход по `/#lead` (и перезагрузка после тапа CTA — тап пишет якорь в адрес) садился мимо:
//        нативный переход случается, пока страница ещё того размера, каким её разобрали, а дальше
//        сцена резервирует форму знака и история закрепляется (+1–2 тыс. px) — на desktop карточка
//        оказывалась на 1733 вместо 525, на телефоне reveal ещё не сработал (opacity 0).
//   O-2: на экранах ниже ~620 px (360×540, 375×548 — iPhone SE в Safari в начале страницы) даже
//        посадка на карточку оставляла поле телефона на 15–71 px ниже сгиба.
//
// Браузерная половина — tools/probe-phone.mjs (режимы cta и deep) и check.mjs §5; здесь — чистое
// правило цепочки посадки и проводка, от которой те прогоны зависят.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { landingChain, pickLanding } from '../../src/js/header.js';
import { isLandingSection } from '../../src/js/hash.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (p) => fs.readFileSync(path.join(ROOT, p), 'utf8');

// ---- O-2: цепочка посадки ----
// Геометрия в координатах РАСКЛАДКИ секции (числа — от замеров стенда): от верха секции до карточки
// 578 px кинетической строки, от карточки до блока полей 350, поле телефона — 1061..1117 от верха
// секции (539 от верха карточки, 189 от верха блока полей).
const el = (smt, top, h = 0) => ({ smt, offsetTop: top, offsetHeight: h, offsetParent: null });
function leadSection({ land = true, fields = true } = {}) {
  const parts = {
    '.lead-land': land ? el(80, 578) : null,
    '.lead-fields': fields ? el(104, 928) : null,
    '#leadPhone': el(0, 1061, 56),
  };
  return { ...el(80, 0), dataset: { land: '.lead-land,.lead-fields', landNeed: '#leadPhone' }, querySelector: (s) => parts[s] };
}
// landingTop читает scroll-margin-top цели, когда Lenis нет (телефоны и планшет — без него).
const withCss = (fn) => {
  const prev = globalThis.getComputedStyle;
  globalThis.getComputedStyle = (t) => ({ scrollMarginTop: `${t.smt}px` });
  try { return fn(); } finally { globalThis.getComputedStyle = prev; }
};
const pick = (vh, opts) => withCss(() => {
  const t = landingChain(leadSection(opts), null, { innerHeight: vh });
  return t.smt === 80 && t.offsetTop === 0 ? 'section' : t.smt === 80 ? 'land' : 'fields';
});

test('цепочка посадки: секция → карточка → поля, и останавливается на первой, где поле видно', () => {
  // высокие телефоны и планшет: карточка (80 + 539 = 619 — в экране)
  for (const vh of [627, 640, 664, 700, 715, 746, 844, 915, 932, 1024]) {
    assert.equal(pick(vh), 'land', `${vh}: карточка`);
  }
  // короткие экраны: 80 + 539 = 619 не помещается — идём на блок полей (O-2)
  for (const vh of [480, 540, 548, 568, 600, 620, 626]) {
    assert.equal(pick(vh), 'fields', `${vh}: блок полей`);
  }
  // ровно на границе: «у самого края — не видно» (NEED_PAD 8)
  assert.equal(pick(619 + 8 - 1), 'fields');
  assert.equal(pick(619 + 8), 'land');
});

test('цепочка посадки: desktop с Lenis остаётся на секции — строка «Выбери … ответ.» над формой', () => {
  // На 1440×900 низ поля телефона — 698 от верха секции; посадка Lenis — 72 (фикс. шапка + 4).
  const parts = { '.lead-land': el(80, 453), '.lead-fields': el(104, 509), '#leadPhone': el(0, 642, 56) };
  const section = { ...el(80, 0), dataset: { land: '.lead-land,.lead-fields', landNeed: '#leadPhone' }, querySelector: (s) => parts[s] };
  const t = landingChain(section, {}, { innerHeight: 900 });
  assert.equal(t.offsetTop, 0, 'сама секция');
  assert.equal(pickLanding(72, 698, 900), 'section', 'то же правило, что и раньше');
});

test('цепочка посадки: нет следующей точки — остаёмся на последней, какая есть', () => {
  assert.equal(pick(540, { fields: false }), 'land', 'без .lead-fields — карточка, как до фикса');
  assert.equal(pick(540, { land: false, fields: false }), 'section', 'без точек — сама секция');
});

test('цепочка посадки меряет РАСКЛАДКУ: 28 px незавершённого reveal не должны менять выбор', () => {
  // 360×640 — ровно тот зазор, где это решало: 619 помещается (632), 619 + 28 = 647 уже нет. Тап
  // меряет ещё не показанную карточку, дип-линк — уже показанную; ответ обязан быть один.
  assert.equal(pick(640), 'land', 'посадка на карточку');
  const shifted = leadSection();
  const phone = shifted.querySelector('#leadPhone');
  phone.offsetTop += 28;                       // как если бы мерили по нарисованному месту
  const t = withCss(() => landingChain(shifted, null, { innerHeight: 640 }));
  assert.equal(t.smt, 104, 'на нарисованном месте выбор был бы другим — потому его и не меряем');
  assert.match(read('src/js/header.js'), /const off = layoutTop\(what\) \+ what\.offsetHeight - layoutTop\(target\);/);
  assert.match(read('src/js/header.js'), /for \(let n = el; n; n = n\.offsetParent\) y \+= n\.offsetTop;/);
});

// ---- проводка O-2 ----
test('блок полей — последняя точка цепочки: разметка и его собственный scroll-margin', () => {
  assert.match(read('src/sections/90-final.html'),
    /<section class="final sec" id="lead"[^>]* data-land="\.lead-land,\.lead-fields" data-land-need="#leadPhone"/);
  const fcss = read('src/css/90-final.css');
  assert.match(fcss, /\.final \.lead-fields\{margin-top:32px;scroll-margin-top:calc\(80px \+ clamp\(24px,4\.4vw,56px\)\)\}/,
    'поля открываются на той же строке, где открылась бы карточка: 80 + её собственный паддинг');
  assert.match(fcss, /\.lead-land\{scroll-margin-top:80px\}/, 'средняя точка цепочки — на месте');
});

// ---- проводка O-1 ----
test('дип-линк садится тем же правилом, что и тап, и УДЕРЖИВАЕТСЯ, пока страница растёт', () => {
  const hash = read('src/js/hash.js'), hdr = read('src/js/header.js');
  assert.match(hash, /function aimAt\(section, ctx\) \{\s*const target = landingChain\(section, ctx\.lenis\);/,
    'цель дип-линка считает та же цепочка, что и клик');
  assert.match(hash, /if \(landingError\(target\.getBoundingClientRect\(\)\.top, landingTop\(target, ctx\.lenis\)\)\)/,
    'повторный прицел — только когда цель действительно съехала');
  assert.match(hash, /if \(ctx\.ScrollTrigger\) ctx\.ScrollTrigger\.refresh\(\);\s*showReveals\(section\);\s*let target = aimAt\(section, ctx\);/,
    'порядок: refresh → показать reveal → прицелиться');
  assert.match(hash, /if \(performance\.now\(\) - t0 >= HOLD_MS\) \{ stop\(\); settle\(target\); return; \}/,
    'после удержания — обычная досадка header.js');
  assert.match(hash, /for \(const ev of READER\) addEventListener\(ev, stop, \{ capture: true, passive: true \}\);\s*holding = stop;/,
    'жест читателя снимает удержание');
  assert.match(hash, /^import \{ landingChain, landingTop, landingError, OFFSET, READER \} from '\.\/header\.js';$/m,
    'правило посадки — одно на тап и на дип-линк, не копия');
  assert.match(hdr, /addEventListener\('hashchange', \(\) => \{ if \(location\.hash\) import\('\.\/hash\.js'\)\.then\(\(m\) => m\.landHashHeld\(api\)\); \}\);/,
    'Back\/Forward по якорю — тем же модулем');
  assert.match(hdr, /export const READER = Object\.freeze\(\['wheel', 'touchstart', 'keydown', 'pointerdown'\]\);/,
    'один список отмены на досадку и на удержание');
  assert.match(hdr, /const api = \{ settle, ctx \};\n/, 'модуль якоря переиспользует досадку шапки');
});

test('модуль дип-линка грузится только когда якорь ЕСТЬ (бюджет байтов до прокрутки — 450 КБ)', () => {
  const main = read('src/js/main.js');
  assert.match(main, /if \(location\.hash\) \{/, 'без якоря модуль не загружается');
  assert.match(main, /\.then\(\(\) => import\('\.\/hash\.js'\)\)\.then\(\(m\) => m\.landHashHeld\(header\)\);/);
  assert.ok(!/hash\.js/.test(read('src/index.html')), 'и не преднагружается в <head>');
});

test('reveal цели применяется БЕЗ анимации — иначе прицел уезжает на 28 px', () => {
  const hdr = read('src/js/hash.js');
  const fn = hdr.slice(hdr.indexOf('function showReveals('), hdr.indexOf('function sectionFor('));
  assert.ok(fn.indexOf("style.transition = 'none'") < fn.indexOf("classList.add('in')"), 'сначала гасим переход');
  assert.match(fn, /requestAnimationFrame\(\(\) => \{ for \(const el of moving\) el\.style\.transition = ''; \}\);/, 'и возвращаем его');
  assert.match(fn, /\.w>span/, 'слова [data-split] едут своим переходом — его тоже гасим');
  assert.match(read('src/css/01-base.css'), /\.js \[data-reveal\]\{opacity:0;transform:translateY\(28px\)\}/, 'те самые 28 px');
});

test('main.js: дип-линк наводится после старта сцены и истории и после load', () => {
  const main = read('src/js/main.js');
  assert.match(main, /const header = initHeader\(document\.getElementById\('hdr'\), ctx\);/);
  assert.match(main, /document\.readyState === 'complete'\s*\?\s*Promise\.resolve\(\) : new Promise\(\(r\) => addEventListener\('load', r, \{ once: true \}\)\)/);
  assert.match(main, /Promise\.all\(\[\.\.\.eager, loaded\]\)/, 'после старта сцены и истории');
  assert.match(main, /return load\(\)\.then\(\(m\) => m\.init\(root, ctx\)\)/, 'boot возвращает обещание — иначе ждать нечего');
});

test('check.mjs: дип-линк и короткие экраны — в стенде, в каждом прогоне', () => {
  const chk = read('tools/check.mjs');
  assert.match(chk, /\{ vp: \[390, 664\], hash: '#lead', target: '\.lead-card' \}/, 'O-1 на телефоне');
  assert.match(chk, /\{ vp: \[1440, 900\], desktop: true, hash: '#lead', target: '#lead' \}/, 'O-1 на desktop');
  assert.match(chk, /\{ vp: \[360, 540\], click: '\.hdr a\[href="#lead"\]', target: '\.lead-fields', want: 104 \}/, 'O-2 тапом');
  assert.match(chk, /\{ vp: \[375, 548\], hash: '#lead', target: '\.lead-fields', want: 104 \}/, 'O-2 дип-линком');
  assert.match(chk, /want: want \|\| \(document\.documentElement\.classList\.contains\('lenis'\) \? 72 : 80\)/,
    'у точки со своим scroll-margin — своё ожидаемое место');
  const probe = read('tools/probe-phone.mjs');
  assert.match(probe, /for \(const sel of \[null, '\.hdr \.btn-s\[href="#lead"\]', '\.hero-cta a\.btn\[href="#lead"\]'\]\)/, 'дип-линк — третий вход пробы');
  assert.match(probe, /const landed = onScreen\(r\.phone\) && \(tight \|\| onScreen\(r\.h\)\)/,
    'поле телефона видно ВСЕГДА, заголовок — когда экран позволяет (O-2)');
  assert.match(probe, /const tight = r\.card && r\.phone && 80 \+ \(r\.phone\.b - r\.card\.t\) > r\.vh - 8;/,
    'у пробы и у цепочки — одна арифметика, иначе они спорят в полосе 620…632');
});

// ---- P-1 (повторное ревью 5): skip-link «К содержанию» пишет #main в адрес → hashchange → hash.js
// раньше раскрывал ВСЕ 44 [data-reveal] сразу (#main оборачивает всю страницу). Цель — только реальная
// секция-цель CTA (#how, #demo, #pricing, #niches, #faq, #lead — все <section class="… sec">), не #main
// и не любой другой нецелевой якорь (например #top — div, не section).
test('isLandingSection: цель — только <section class="… sec">, не #main и не #top', () => {
  assert.equal(isLandingSection({ tagName: 'SECTION', classList: { contains: (c) => c === 'sec' } }), true,
    'реальная секция-цель (#lead, #pricing, #faq, #niches, #how, #demo, #start)');
  assert.equal(isLandingSection({ tagName: 'MAIN', classList: { contains: () => false } }), false,
    '#main — обёртка skip-link, не секция');
  assert.equal(isLandingSection({ tagName: 'DIV', classList: { contains: () => false } }), false,
    '#top — div лого, не секция');
  assert.equal(isLandingSection({ tagName: 'SECTION', classList: { contains: () => false } }), false,
    'секция без .sec (например #signal) — не цель CTA');
  assert.equal(isLandingSection(null), false, 'якорь не найден на странице');
  assert.equal(isLandingSection(undefined), false);
});

test('P-1: sectionFor гейтит найденный элемент через isLandingSection, не возвращает его как есть', () => {
  const hash = read('src/js/hash.js');
  assert.match(hash, /export function isLandingSection\(el\) \{/, 'предикат экспортирован — проверяем не регексом на поведение, а прямым вызовом выше');
  assert.match(hash, /return !!el && el\.tagName === 'SECTION' && !!el\.classList && el\.classList\.contains\('sec'\);/);
  assert.match(hash, /const el = document\.querySelector\(id\); return isLandingSection\(el\) \? el : null; \} catch \{ return null; \}/,
    'sectionFor возвращает null для найденного, но нецелевого элемента (#main, #top) — раньше возвращал document.querySelector(id) напрямую');
});
