import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mulberry32, logoTargets, bubbleTargets, channelTargets, rankMatch, tones, BUBBLES, CHANNELS } from '../../src/js/particles/shapes.js';
import { morphAt, particleCount } from '../../src/js/stage.js';
import * as stage from '../../src/js/stage.js';
import { readFileSync } from 'node:fs';

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

// --- beyond the brief: the two pure seams the GL path depends on ---

test('любой префикс — равномерная подвыборка (setCount рисует первые n)', () => {
  // the field draws the first n vertices after a degrade, so part order must be shuffled:
  // the first quarter of the logo must still hit all three bars in roughly the full ratio
  const a = logoTargets(N, mulberry32(9));
  const q = N / 4, bars = [0, 0, 0];
  for (let i = 0; i < q; i++) { const y = a[2 * i + 1]; if (y > 0.25) bars[0]++; else if (y > -0.25) bars[1]++; else bars[2]++; }
  assert.ok(bars.every((c) => c > q * 0.12), `bars in first quarter ${bars}`);
  assert.ok(bars[1] > bars[0] && bars[1] > bars[2], `middle still densest ${bars}`);
});

test('rankMatch: перестановка следующей формы, соседи остаются соседями', () => {
  const prev = logoTargets(N, mulberry32(11)), next = bubbleTargets(N, mulberry32(12));
  const out = rankMatch(prev, next, mulberry32(13));
  assert.equal(out.length, next.length);
  const pairs = (a) => { const p = []; for (let i = 0; i < a.length; i += 2) p.push(`${a[i]},${a[i + 1]}`); return p.sort(); };
  assert.deepEqual(pairs(out), pairs(next), 'same multiset of points');
  // leftmost logo particles should land on the left half of the bubbles far more often than not
  let leftToLeft = 0, left = 0;
  for (let i = 0; i < N; i++) if (prev[2 * i] < -0.5) { left++; if (out[2 * i] < 0) leftToLeft++; }
  assert.ok(leftToLeft / left > 0.8, `left→left ${leftToLeft}/${left}`);
});

test('morphAt: 0 наверху, держит форму у якорей, 2 в конце', () => {
  const A = [0, 1000, 2000];
  assert.equal(morphAt(-50, A), 0);
  assert.equal(morphAt(0, A), 0);
  assert.equal(morphAt(150, A), 0, 'hold after the hero top');
  assert.equal(morphAt(1000, A), 1);
  assert.equal(morphAt(900, A), 1, 'hold before step 1 centre');
  assert.equal(morphAt(1100, A), 1, 'hold after step 1 centre');
  assert.equal(morphAt(2000, A), 2);
  assert.equal(morphAt(9000, A), 2);
  const mid = morphAt(500, A);
  assert.ok(mid > 0.4 && mid < 0.6, `mid ${mid}`);
  let prev = -1;
  for (let y = 0; y <= 2000; y += 25) { const v = morphAt(y, A); assert.ok(v >= prev, `monotonic at ${y}`); prev = v; }
});

// --- tone classification: the GL palette index per particle, derived from geometry ---

test('tones: длина n, значения 0..6', () => {
  const p = logoTargets(N, mulberry32(21));
  const t = tones('logo', p);
  assert.ok(t instanceof Uint8Array);
  assert.equal(t.length, N);
  assert.ok([...t].every((v) => v >= 0 && v <= 6));
});

test('tones logo: средняя полоса — яркий тон 2, боковые — акцент 0/1, пыль — 6', () => {
  const p = logoTargets(N, mulberry32(22));
  const t = tones('logo', p);
  const at = (x, y) => tones('logo', Float32Array.from([x, y]))[0];
  assert.equal(at(0, 0), 2, 'centre of the chosen bar');
  assert.equal(at(0.6, 0.199), 2, 'its selection ring');
  assert.equal(at(0, 0.455), 0, 'top bar body');
  assert.equal(at(0, 0.455 + 0.1387), 1, 'top bar rim');
  assert.equal(at(0.95, 0.9), 6, 'dust outside the mark');
  let mid = 0, midOk = 0, side = 0, sideOk = 0, dust = 0;
  for (let i = 0; i < N; i++) {
    const x = p[2 * i], y = p[2 * i + 1];
    if (Math.abs(x) <= 0.7 && Math.abs(y) <= 0.17) { mid++; if (t[i] === 2) midOk++; }
    else if (Math.abs(x) <= 0.7 && Math.abs(Math.abs(y) - 0.4551) <= 0.145) { side++; if (t[i] === 0 || t[i] === 1) sideOk++; }
    if (t[i] === 6) dust++;
  }
  assert.ok(mid > N * 0.3 && midOk === mid, `middle ${midOk}/${mid}`);
  assert.ok(side > N * 0.25 && sideOk === side, `sides ${sideOk}/${side}`);
  assert.ok(dust > N * 0.02, `dust ${dust}`);
});

// Судим по ГЕОМЕТРИИ знаков, а не по порогу классификатора: точки на кольце WhatsApp
// должны быть зелёными, на кольце Telegram — синими, перемычка между ними — пылью.
// (Повтор правила «x < -0.2 → 4» проверял бы только проводку, а не классификацию.)
test('tones channel: кольцо WhatsApp — тон WA, кольцо Telegram — тон TG, перемычка — пыль', () => {
  const p = channelTargets(N, mulberry32(23));
  const t = tones('channel', p);
  const onRing = (x, y, c) => Math.abs(Math.hypot(x - c.cx, y - c.cy) - c.r) < 0.035;
  let waRing = 0, waOk = 0, tgRing = 0, tgOk = 0, bridge = 0, bridgeOk = 0;
  for (let i = 0; i < N; i++) {
    const x = p[2 * i], y = p[2 * i + 1];
    if (onRing(x, y, CHANNELS.wa)) { waRing++; if (t[i] === 4) waOk++; }
    else if (onRing(x, y, CHANNELS.tg)) { tgRing++; if (t[i] === 5) tgOk++; }
    if (Math.abs(x) < 0.16 && Math.abs(y) < 0.01) { bridge++; if (t[i] === 6) bridgeOk++; }
  }
  // 10 % кольца у самого центра попадает в «пыль» — это край знака, он и должен гаснуть
  assert.ok(waRing > N * 0.08 && waOk > waRing * 0.88, `WA ring ${waOk}/${waRing}`);
  assert.ok(tgRing > N * 0.05 && tgOk > tgRing * 0.88, `TG ring ${tgOk}/${tgRing}`);
  assert.ok(bridge > N * 0.005 && bridgeOk === bridge, `bridge ${bridgeOk}/${bridge}`);
  // два знака не могут делить один тон — иначе «один номер, два мессенджера» не читается
  const tone = (x, y) => tones('channel', Float32Array.from([x, y]))[0];
  assert.notEqual(tone(CHANNELS.wa.cx, 0), tone(CHANNELS.tg.cx, 0));
});

// Страж отката (C1, ревью r1): оверлеи уводят на фиксированный слой частиц классы
// gl-init (раскладка) и gl-on (видимость). Если поле гаснет, а классы остаются, «Сигнал»
// теряет содержимое (или замораживает шаг 1 поверх шага 2). Проверяем исходник: браузера
// здесь нет, композицию меряет .check/t5/solid.mjs в реальном Chrome.
test('stage: выключение поля возвращает раскладку отката и не оставляет включённых оверлеев', () => {
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const back = src.slice(src.indexOf('function fallbackLayout()'), src.indexOf('function shutDown()'));
  for (const cls of ['gl-on', 'gl-init']) {
    assert.ok(new RegExp(`classList\\.remove\\([^)]*'${cls}'`).test(back), `откат обязан снять ${cls}`);
  }
  assert.match(back, /remove\('on'\)/, 'и погасить открытый оверлей');
  const body = src.slice(src.indexOf('function shutDown()'), src.indexOf('let rz = 0;'));
  assert.match(body, /fallbackLayout\(\)/, 'shutDown обязан вернуть раскладку отката');
  assert.match(body, /classList\.add\('gl-solid'\)/, 'и поставить защёлку gl-solid');
  assert.match(body, /if \(down \|\| disposed\) return;/, 'повторный вызов и вызов после teardown не делают ничего');
  // и после выключения цикл не воскресает ни по visibilitychange, ни по IntersectionObserver
  const start = src.slice(src.indexOf('function start()'), src.indexOf('function stop()'));
  assert.match(start, /\bdown\b/, 'start() обязан уважать защёлку');
  const frame = src.slice(src.indexOf('function frame(now)'), src.indexOf('function start()'));
  assert.match(frame, /^\s*if \(!field/m, 'frame() не должен трогать освобождённое поле');
  // teardown модуля тоже возвращает секцию в поток (иначе остался бы пустой fixed-слой)
  const destroy = src.slice(src.indexOf('destroy() {'));
  assert.match(destroy, /fallbackLayout\(\)/, 'destroy() обязан вернуть раскладку отката');
  assert.match(destroy, /removeEventListener\('webglcontextlost'/, 'и снять слушателя потери контекста');
});

// Откат в CSS — одно условие :not(.gl-init); «gl-init + gl-solid» не должно нигде скрывать текст
test('stage css: раскладка отката висит на :not(.gl-init), а не на gl-solid', () => {
  const css = readFileSync(new URL('../../src/css/10-stage.css', import.meta.url), 'utf8');
  assert.match(css, /\.stage:not\(\.gl-init\) \.sg-vis\{display:flex/, 'переписка в потоке');
  assert.ok(!/visibility:hidden/.test(css), 'оверлей не прячется через visibility (дерево доступности)');
  // плотный скин — ОДНО правило на оба отката (в потоке и тихий, повторное ревью N-2), а не копия под gl-solid
  for (const decl of ['box-shadow:inset 0 0 0 1px var(--line)}', 'background:var(--accent-soft)', 'rgba(255,255,255,.035),rgba(255,255,255,.01)),var(--surface)']) {
    assert.equal(css.split(decl).length - 1, 1, `скин не дублируется: ${decl}`);
  }
  for (const sub of ['sg-bub', 'sg-bub-in', 'sg-bub-out']) {
    assert.match(css, new RegExp(`\\.stage:not\\(\\.gl-init\\) \\.${sub},\\.stage\\.gl-solid \\.${sub}\\{`), `${sub}: один список селекторов`);
  }
  // ревью r3, Minor 3: сброс высоты шагов — на том же единственном условии, что и вся
  // раскладка отката, иначе без JS (и при createField() === null) над шагом пустая полоса
  assert.match(css, /\.stage:not\(\.gl-init\) \.sg-step\{min-height:0/, 'шаги без формы не резервируют под неё высоту');
  assert.ok(!/\.(q-low|gl-solid) \.sg-step\{/.test(css), 'и никаких отдельных сбросов под q-low / gl-solid');
  assert.ok(!/!important/.test(css), 'в секции не должно остаться !important');
});

// Повторное ревью, N-2: тихий откат (поле погасло, collapse ждёт, пока шаги уйдут ниже окна — может не
// прийти до конца визита) не оставляет пустую полосу резерва: переписка ложится в неё сама — absolute
// внутри шага (высота шагов и поток не меняются), в кадр переключения прозрачна, потом проявляется.
test('stage css: тихий откат кладёт переписку в резерв высоты шага, а не прячет её', () => {
  const css = readFileSync(new URL('../../src/css/10-stage.css', import.meta.url), 'utf8');
  const rule = (sel) => (css.match(new RegExp(sel.replace(/\./g, '\\.') + '\\{([^}]*)\\}')) || [, ''])[1];
  const vis = rule('.stage.gl-init.gl-solid .sg-vis');
  assert.match(vis, /position:absolute/, 'слой уезжает вместе с шагом, а не висит в окне');
  assert.match(vis, /animation:sg-rest /);
  const kf = (css.match(/@keyframes sg-rest\{[^\n]*/) || [''])[0];
  assert.match(kf, /\{0%[^{]*\{opacity:0\}/, 'кадр переключения — прозрачный: перенос слоя не виден');
  assert.match(kf, /to\{opacity:1\}/);
  assert.match(rule('.stage.gl-init.gl-solid .sg-o'), /opacity:1;transform:none/, 'и пузыри, и знаки каналов видны');
  assert.match(css, /\.sg-step\{position:relative;/, 'absolute-слой отсчитывается от своего шага');
  assert.ok(!/≤ 1\.5 с/.test(css), 'тихий откат больше не «на миг»: комментарий состояния не должен это обещать');
});

// Ревью r2, I1: переход «поток → фиксированный слой» обязан быть мгновенным. Класс раскладки
// ставится ДО первого кадра, с transition:none; gl-on потом только включает переходы. Если
// раскладка и переход придут одним классом, первый кадр GL покажет всю переписку поверх
// первого экрана на полной непрозрачности и лишь затем погасит её.
test('stage: GL-раскладка оверлеев приходит без переходов и раньше первого кадра', () => {
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const boot = src.slice(src.indexOf('function boot()'), src.indexOf('// Let the first paint'));
  assert.match(boot, /classList\.add\('gl-init'\)/, 'gl-init ставится в boot(), до цикла');
  const frame = src.slice(src.indexOf('function frame(now)'), src.indexOf('function start()'));
  assert.ok(!/gl-init/.test(frame), 'и НЕ в кадре — иначе состояние станет наблюдаемым переходом');
  assert.match(frame, /classList\.add\('gl-on'\)/, 'gl-on — по-прежнему после первого кадра');

  const css = readFileSync(new URL('../../src/css/10-stage.css', import.meta.url), 'utf8');
  for (const sel of ['.stage.gl-init .sg-vis', '.stage.gl-init .sg-o']) {
    const r = (css.match(new RegExp(sel.replace(/\./g, '\\.') + '\\{[\\s\\S]*?\\}')) || [''])[0];
    assert.match(r, /position:(fixed|absolute)/, `${sel}: раскладка GL`);
    assert.match(r, /opacity:0/, `${sel}: стартовая непрозрачность 0`);
    assert.match(r, /transition:none/, `${sel}: без перехода — иначе вспышка`);
  }
  assert.ok(!/\.stage\.gl-on \.sg-(vis|o)\{[^}]*position:fixed/.test(css), 'раскладку не должен приносить gl-on');
});

// Ревью r2, I2: у изменения ТОЛЬКО высоты (адресная строка телефона) композиция заморожена,
// но буфер рисования обязан следовать за вьюпортом — иначе поле растягивается, а текст нет.
test('stage: изменение высоты вьюпорта обновляет буфер рисования', () => {
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const branch = src.slice(src.indexOf('if (heightOnly && !desk)'), src.indexOf('measure();', src.indexOf('if (heightOnly && !desk)')));
  assert.match(branch, /field\.resize\(W, H/, 'ветка адресной строки обязана вызвать field.resize');
  assert.ok(!/setProperty\('--s'/.test(branch), 'и при этом не пересчитывать композицию');
});

// Ревью r2, I3: заголовок страницы не может быть мельче заголовков её секций. Сравниваем
// ramp первого экрана (мобильный до 900px, десктопный после) с h2 из 01-base.css.
test('stage css: H1 первого экрана крупнее H2 секций на всей полосе 320–1920', () => {
  const css = readFileSync(new URL('../../src/css/10-stage.css', import.meta.url), 'utf8');
  const base = readFileSync(new URL('../../src/css/01-base.css', import.meta.url), 'utf8');
  const ramps = [...css.matchAll(/\.hero h1\{font-size:(?:min\()?clamp\(([\d.]+)px,\s*([\d.]+)vw,\s*([\d.]+)px\)/g)]
    .map((m) => [+m[1], +m[2], +m[3]]);
  assert.equal(ramps.length, 2, 'ожидались два ramp H1: мобильный и десктопный (900px)');
  // второй предел — ширина колонки: без него ramp «крупнее h2» ломает авторские две строки
  assert.match(css, /\.hero h1\{font-size:min\(clamp\([^)]*\),\s*calc\(var\(--heroc\)\s*\/\s*[\d.]+\)\)\}/,
    'десктопный H1 обязан быть ограничен и шириной колонки (--heroc)');
  const h2 = base.match(/\bh2\{font-size:clamp\(([\d.]+)px,\s*([\d.]+)vw,\s*([\d.]+)px\)/);
  assert.ok(h2, 'не найден ramp h2 в 01-base.css — переписан? сверить размеры заново');
  const px = ([lo, vw, hi], w) => Math.min(hi, Math.max(lo, (vw * w) / 100));
  const H2 = [+h2[1], +h2[2], +h2[3]];
  for (let w = 320; w <= 1920; w += 4) {
    const h1 = px(ramps[w >= 900 ? 1 : 0], w), sec = px(H2, w);
    assert.ok(h1 > sec, `при ${w}px H1 ${h1.toFixed(1)} <= H2 ${sec.toFixed(1)}`);
  }
});

test('tones bubble: у ответа бота яркая кромка (2) и залитое тело (0), у клиента — нейтраль', () => {
  const p = bubbleTargets(N, mulberry32(24));
  const t = tones('bubble', p);
  const c = [0, 0, 0, 0, 0, 0, 0];
  for (let i = 0; i < N; i++) c[t[i]]++;
  assert.ok(c[2] > N * 0.05, `bot edge ${c[2]}`);
  assert.ok(c[0] > N * 0.25, `bot fill ${c[0]}`);
  assert.ok(c[3] > N * 0.08, `client edge ${c[3]}`);
  assert.ok(c[6] > N * 0.05, `client fill + dust ${c[6]}`);
  // a point deep inside the bot bubble is body-toned, one on its rim is bright
  const { bot } = BUBBLES;
  assert.equal(tones('bubble', Float32Array.from([bot.cx, bot.cy]))[0], 0);
  assert.equal(tones('bubble', Float32Array.from([bot.cx, bot.cy + bot.hh]))[0], 2);
});

test('particleCount: бюджет частиц по уровню качества', () => {
  assert.equal(particleCount('high', 8), 22000);
  assert.equal(particleCount('high', 16), 22000);
  assert.equal(particleCount('high', 4), 15000);
  assert.equal(particleCount('high', undefined), 15000);
  // Task 12: обычный телефон — 5000 (было 7000): точки крупнее на √(7/5), форма так же плотна,
  // а кадр на программном рендере стенда укладывается в бюджет рядом с baseline
  assert.equal(particleCount('medium', 16), 5000);
  assert.equal(particleCount('low', 16), 5000);
});
test('particleCount: фигура телефонного размера (< 900 px) — 5000 и на high (плотность по площади)', () => {
  // S на телефоне ≈ 168 px против до 320 на desktop: площадь в ~3.6 раза меньше, 15000 частиц там —
  // та же форма втрое плотнее и втрое дороже (случай ?tier=high на телефоне и узкого окна)
  assert.equal(particleCount('high', 8, 390), 5000);
  assert.equal(particleCount('high', 4, 899), 5000);
  assert.equal(particleCount('high', 8, 1440), 22000);
  assert.equal(particleCount('high', 4, 1440), 15000);
  assert.equal(particleCount('medium', 8, 1440), 5000);
});
test('fieldDpr: high до 1.5, остальные до 1.25', () => {
  assert.equal(stage.fieldDpr('high', 3, 390), 1.25, 'телефонная раскладка — как у телефона, при любом уровне');
  assert.equal(stage.fieldDpr('high', 3, 1440), 1.5);
  assert.equal(stage.fieldDpr('high', 3), 1.5);
  assert.equal(stage.fieldDpr('medium', 3), 1.25);
  assert.equal(stage.fieldDpr('medium', 1), 1);
  assert.equal(stage.fieldDpr('medium', undefined), 1);
});

// --- Ревью r3, I1: на телефоне текст шага не должен проезжать сквозь видимый текст оверлея ---
// Форма из частиц и её HTML-оверлеи пришиты к верху экрана, текст шагов едет под ними.
// Раньше проверялись только якоря — а столкновение живёт в 150–300 px ПОСЛЕ якоря.
// Геометрия ниже измерена на сборке (.check/t5/geom.mjs, программный GL): блоки текста —
// в координатах документа (раскладка, без 28px сдвига reveal), полосы оверлеев — в px экрана.
const PHONE = { // 390×844, ?tier=medium
  H: 844, S: 168.8, CY: 198.8, A: [0, 883, 1727], stageEnd: 2571,
  hero: [[349, 428], [445, 581], [606, 714], [733, 786], [801, 833]],
  steps: [[[1336, 1437], [1457, 1566]], [[2180, 2281], [2301, 2410]]],
  bands: [{ top: 85, bottom: 319 }, { top: 270, bottom: 286 }],
};
const TABLET = { // 768×1024 — та же одноколоночная раскладка
  H: 1024, S: 204.8, CY: 223.3, A: [0, 1024, 2048], stageEnd: 3072,
  hero: [[406, 501], [522, 603], [634, 682], [705, 727], [745, 777]],
  steps: [[[1589, 1713], [1733, 1814]], [[2626, 2750], [2770, 2825]]],
  bands: [{ top: 85, bottom: 365 }, { top: 309, bottom: 325 }],
};
const geoOf = (f) => ({
  stageEnd: f.stageEnd, H: f.H, bands: f.bands, margin: stage.OVERLAY_CLEAR * f.S,
  units: [
    ...f.hero.map(([t, b]) => stage.copyUnit(t, b, f.A[0], f.CY, f.S)),
    ...f.steps.flatMap((list, i) => list.map(([t, b]) => stage.copyUnit(t, b, f.A[i + 1], f.CY, f.S))),
  ],
});
// all (y, text-block) pairs where a SHOWN overlay's text band and VISIBLE copy intersect.
// The smoothed morph lags its target in both scroll directions, so the hold is also taken
// from targets up to 300 px behind/ahead of y.
function collisions(f, rule) {
  const g = geoOf(f), hits = [];
  for (let y = f.A[0]; y <= f.stageEnd; y += 2) {
    for (const lag of [-300, -150, 0, 150, 300]) {
      const k = rule(morphAt(y - lag, f.A), y, g);
      if (!k) continue;
      const band = f.bands[k - 1];
      const tail = stage.tailAt(y, f.stageEnd, f.H);
      for (const u of g.units) {
        if (stage.fadeAt(u, y, tail) <= stage.TEXT_VISIBLE) continue;
        if (u.top - y < band.bottom && u.bottom - y > band.top) hits.push({ y, lag, k, top: u.top });
      }
    }
  }
  return hits;
}

test('overlayOn: на телефоне и планшете текст шага не проезжает сквозь видимый оверлей', () => {
  // правило r2 — оставлено здесь как доказательство, что фикстура воспроизводит дефект
  const r2 = (m, y, g) => { const k = Math.round(m); return k > 0 && Math.abs(m - k) < 0.18 && y < g.stageEnd - g.H * 0.55 ? k : 0; };
  for (const [name, f] of [['390×844', PHONE], ['768×1024', TABLET]]) {
    assert.ok(collisions(f, r2).length > 0, `${name}: фикстура обязана воспроизводить r3 I1 при старом правиле`);
    const hits = collisions(f, stage.overlayOn);
    assert.deepEqual(hits.slice(0, 3), [], `${name}: текст на тексте при y=${hits.map((h) => h.y).slice(0, 5)}`);
  }
});

test('overlayOn: оверлей по-прежнему виден у своего якоря (фикс не просто гасит пузыри)', () => {
  for (const [name, f] of [['390×844', PHONE], ['768×1024', TABLET]]) {
    const g = geoOf(f);
    for (const k of [1, 2]) for (const d of [-60, 0, 60]) {
      assert.equal(stage.overlayOn(morphAt(f.A[k] + d, f.A), f.A[k] + d, g), k, `${name}: оверлей ${k} у якоря ${d >= 0 ? '+' : ''}${d}`);
    }
  }
});

test('overlayOn: подписи каналов уходят раньше колец — порог хвоста холста (r3 Minor 4)', () => {
  // десктоп: колонки текста и формы разные, блоков текста нет — остаётся только хвост
  const D = { stageEnd: 2700, H: 900, A: [0, 900, 1800] };
  const g = { stageEnd: D.stageEnd, H: D.H, bands: [], units: [], margin: 0 };
  assert.equal(stage.overlayOn(2, 1800, g), 2, 'у якоря шага 2 подписи видны');
  for (let y = 1800; y <= D.stageEnd; y += 5) {
    if (stage.overlayOn(morphAt(y, D.A), y, g)) {
      assert.ok(stage.tailAt(y, D.stageEnd, D.H) >= stage.OVERLAY_MIN_TAIL, `при y=${y} подпись пережила кольцо`);
    }
  }
  assert.equal(stage.overlayOn(2, D.stageEnd - D.H * 0.6, g), 0, 'низ сцены на 0.6H: кольцо почти погасло — подписей нет');
});

test('fadeAt: блок текста полностью виден в покое и гаснет, уезжая под форму', () => {
  const g = geoOf(PHONE);
  const hero = g.units.slice(0, 5), step1 = g.units.slice(5, 7);
  // первый экран при загрузке — ровно 1 (H1/подзаголовок/CTA — это LCP)
  for (const u of hero) assert.equal(stage.fadeAt(u, 0), 1);
  for (const u of step1) assert.equal(stage.fadeAt(u, PHONE.A[1]), 1, 'шаг читается у своего якоря');
  for (const u of g.units) {
    let prev = 2;
    for (let y = 0; y <= PHONE.stageEnd; y += 7) { const v = stage.fadeAt(u, y); assert.ok(v <= prev + 1e-9, 'только гаснет'); prev = v; }
    assert.equal(stage.fadeAt(u, u.top - u.lo + 1), 0, 'под формой — погашен');
  }
  // холст ушёл (хвост сцены) — затемнять текст больше нечем
  assert.equal(stage.fadeAt(step1[0], step1[0].top, 0), 1);
  // блок, который уже в покое заходит в полосу (альбомный телефон), в покое всё равно = 1
  const tight = stage.copyUnit(1000, 1100, 900, 137, 78);
  assert.equal(stage.fadeAt(tight, 900), 1);
  assert.ok(stage.fadeAt(tight, 960) < 1);
});

// Task 12 (ревью T5 r4, I1): 'disable' от FrameGovernor схлопывает шаги под читателем за один
// кадр (страница прыгала на 227–368 px). Раскладка отката остаётся конечным состоянием, но
// прокрутка компенсируется: элемент у верха окна до и после смены классов стоит на месте.
test('stage: compensateScroll держит опорный элемент на месте', () => {
  assert.equal(typeof stage.compensateScroll, 'function');
  assert.equal(stage.compensateScroll(336, -32, 1000), 632, 'текст ушёл вверх на 368 — прокрутка на 368 назад');
  assert.equal(stage.compensateScroll(517, 290, 700), 473);
  assert.equal(stage.compensateScroll(100, 100.3, 500), 500, 'дробные доли пикселя не трогают прокрутку');
  assert.equal(stage.compensateScroll(100, 600, 0), 500, 'рост над читателем — прокрутка вниз');
  assert.equal(stage.compensateScroll(-50, -400, 200), 0, 'не уходит выше начала страницы');
});
test('stage: shutDown() меряет опору ДО отката и восстанавливает её ПОСЛЕ', () => {
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const body = src.slice(src.indexOf('function shutDown()'), src.indexOf('let rz = 0;'));
  const pick = body.indexOf('pickAnchor(');
  const back = body.indexOf('fallbackLayout()');
  const comp = body.indexOf('compensateScroll(');
  assert.ok(pick >= 0 && back > pick && comp > back, 'порядок: опора → откат → компенсация');
});
test('stage: погасшая копия hero не ловит касания, фокус возвращает её', () => {
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const fade = src.slice(src.indexOf('function fadeCopy('), src.indexOf('function overlays('));
  assert.match(fade, /pointerEvents/, 'погасший блок снимает pointer-events');
  assert.match(fade, /focusEl/, 'блок с фокусом внутри не гаснет');
  assert.match(src, /addEventListener\('focusin'/);
  const back = src.slice(src.indexOf('function fallbackLayout()'), src.indexOf('function shutDown()'));
  assert.match(back, /pointerEvents = ''/, 'откат возвращает pointer-events');
});

// Task 12 (перф): под нагрузкой поле рисует через кадр; успевающее устройство не пропускает.
test('stage: drawThisFrame — пропуск только после медленного кадра и не два подряд', () => {
  assert.equal(stage.drawThisFrame(16.7, false), true, 'успевает — рисует каждый кадр');
  assert.equal(stage.drawThisFrame(33, false), false, 'медленный кадр — следующий пропускаем');
  assert.equal(stage.drawThisFrame(33, true), true, 'но не два подряд: частицы не замирают');
  assert.equal(stage.drawThisFrame(NaN, false), true);
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const frame = src.slice(src.indexOf('function frame(now)'), src.indexOf('function start()'));
  // линза перф-инженера, M-1: погасший холст (tail = 0) не рисуется вовсе — и это не «пропуск»
  assert.match(frame, /if \(tail > 0 && drawThisFrame\(dt, skipped\)\) \{ skipped = false; field\.setMorph\(morph\); field\.render\(now\); \}\s*else if \(tail > 0\) skipped = true;/);
  assert.ok(frame.indexOf('const tail = tailAt(') < frame.indexOf('field.render(now)'), 'tail считается до отрисовки');
  assert.ok(frame.indexOf('ctx.governor.sample(dt)') > 0, 'регулятор по-прежнему кормится каждый кадр');
});

// Task 12: переход на GL-раскладку удлиняет «Сигнал» (шаги резервируют высоту формы) — закреплённая
// история ниже обязана перемериться, а читатель ниже сцены (deep link) — остаться на месте.
test('stage: boot() после gl-init перемеряет ScrollTrigger и держит читателя ниже сцены', () => {
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const boot = src.slice(src.indexOf('function boot()'), src.indexOf('// Let the first paint happen'));
  const hold = boot.indexOf('root.nextElementSibling');
  const init = boot.indexOf("root.classList.add('gl-init')");
  const refresh = boot.indexOf('ctx.ScrollTrigger.refresh()');
  const comp = boot.indexOf('compensateScroll(nextTop');
  assert.ok(hold >= 0 && init > hold && refresh > init && comp > refresh, 'порядок: опора → gl-init → refresh → компенсация');
});

// ---- Lens fixes (2026-09-22) ----
test('morphAt: у телефона полоса покоя шире (30 %), переход короче', () => {
  const A = [0, 1000, 2000];
  assert.equal(stage.MORPH_HOLD, 0.2);
  assert.equal(stage.MORPH_HOLD_PHONE, 0.3);
  assert.ok(morphAt(1250, A) > 1, 'desktop на 25 % уже уходит к следующей форме');
  assert.equal(morphAt(1250, A, 0.3), 1, 'телефон на 25 % ещё держит форму');
  assert.equal(morphAt(1700, A, 0.3), 2, 'и раньше садится на следующую');
  const mid = morphAt(1500, A, 0.3);
  assert.ok(mid > 1.4 && mid < 1.6, `середина перехода — середина пути (${mid})`);
  let prev = -1;
  for (let y = 0; y <= 2000; y += 25) { const v = morphAt(y, A, 0.3); assert.ok(v >= prev, `monotonic at ${y}`); prev = v; }
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  assert.match(src, /morphAt\(y, anchors, desk \? MORPH_HOLD : MORPH_HOLD_PHONE\)/);
});

test('phoneScale: 20 % высоты на обычном телефоне, плавно к 14 % на коротком (условия пробного — над сгибом)', () => {
  const near = (a, b) => Math.abs(a - b) < 0.2;
  assert.ok(near(stage.phoneScale(390, 844), 168.8), '390×844 — как было (20 %)');
  assert.ok(near(stage.phoneScale(360, 660), 92.4), '360×660 — 14 %');
  assert.ok(stage.phoneScale(360, 740) < 0.2 * 740 && stage.phoneScale(360, 740) > 0.14 * 740);
  assert.ok(near(stage.phoneScale(320, 900), 280 / 1.92), 'узкий — по ширине');
  let prev = stage.phoneScale(430, 560);
  for (let h = 560; h <= 1000; h += 2) { const v = stage.phoneScale(430, h); assert.ok(v >= prev && v - prev < 1.5, `без скачков при ${h}px`); prev = v; }
  const css = readFileSync(new URL('../../src/css/10-stage.css', import.meta.url), 'utf8');
  assert.match(css, /--s:min\(calc\(\(100vw - 40px\) \/ 1\.92\), clamp\(14svh, calc\(48\.3svh - 226\.3px\), 20svh\)\)/, 'CSS первого кадра — та же формула');
});

test('stage: shutDown сбрасывает нагрузку в том же кадре, раскладку — в тихий момент', () => {
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const sd = src.slice(src.indexOf('function shutDown()'), src.indexOf('function collapse()'));
  for (const re of [/stop\(\)/, /canvas\.style\.opacity = '0'/, /remove\('on'\)/, /requestIdleCallback/]) assert.match(sd, re);
  assert.ok(!/getBoundingClientRect|ScrollTrigger\.refresh|pickAnchor/.test(sd), 'в кадре отключения — ни чтения раскладки, ни refresh');
  const col = src.slice(src.indexOf('function collapse()'), src.indexOf('let rz = 0;'));
  assert.match(col, /ScrollTrigger\.refresh\(\)/, 'refresh — в отложенном collapse');
  assert.match(src.slice(src.indexOf('destroy() {')), /cancelIdleCallback/, 'teardown отменяет отложенный collapse');
});

test('stage: формы строятся кусками с уступкой главному потоку, fonts.ready — только пока шрифты грузятся', () => {
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const boot = src.slice(src.indexOf('async function boot()'), src.indexOf('// Let the first paint happen'));
  assert.ok((boot.match(/await pause\(\)/g) || []).length >= 3, 'между формами — уступки');
  assert.match(boot, /document\.fonts\.status === 'loading'/);
  const story = readFileSync(new URL('../../src/js/story.js', import.meta.url), 'utf8');
  assert.match(story, /if \(document\.fonts && document\.fonts\.status === 'loading'\) document\.fonts\.ready\.then\(\(\) => ScrollTrigger\.refresh\(\)\)/);
});

test('stage css: знак гаснет под блик-проходом, декоративные циклы стоят вне экрана', () => {
  const css = readFileSync(new URL('../../src/css/10-stage.css', import.meta.url), 'utf8');
  assert.match(css, /\.stage\.gl-on \.stage-mark\{opacity:0;transition:opacity \.5s var\(--ease-out\) 1\.1s\}/, 'угасание 1.1→1.6 с после первого кадра');
  assert.match(css, /\.stage\.stage-out \.sg-glow-b,\.stage\.stage-out \.hero-scroll i::after\{animation-play-state:paused\}/,
    'вне экрана бесконечные анимации не пересчитывают стили каждый кадр (линза перф-инженера, I-3)');
  const field = readFileSync(new URL('../../src/js/particles/field.js', import.meta.url), 'utf8');
  assert.match(field, /\(1\.1 \+ aRnd\.w \* 1\.2\)/, 'частицы влетают из кольца у знака');
});

// check.mjs CLS guard (768×1024): a collapse with a step on screen was a layout shift of ~0.8; a
// compensated one still scored 0.19 with the pinned story on screen and 0.87 further down while the page
// scrolled (финальное ревью, I-6). So it only runs while the steps are wholly BELOW the viewport.
test('stage: collapse — только пока шаги целиком ниже окна; иначе тихий откат без сдвига раскладки', () => {
  assert.equal(stage.COLLAPSE_CLEAR, undefined, 'правило «1.5 экрана выше окна» снято: компенсация не спасала от CLS');
  const src = readFileSync(new URL('../../src/js/stage.js', import.meta.url), 'utf8');
  const sched = src.slice(src.indexOf('function scheduleCollapse()'), src.indexOf('function collapse()'));
  assert.match(sched, /new IntersectionObserver/);
  assert.match(sched, /collapseIO\.observe\(steps\[0\]\)/, 'следим за первым шагом');
  assert.match(sched, /\}, \{ rootMargin: '0px 0px -1px 0px' \}\);/, 'шаг, чей верх лежит НА нижнем крае окна (hero ровно 100svh), — уже ниже окна');
  assert.match(sched, /e\.isIntersecting \|\| !stepsBelow\(e\.boundingClientRect\.top, e\.rootBounds \? e\.rootBounds\.bottom : window\.innerHeight\)/,
    'вердикт — по прямоугольнику самой записи: шаг не на экране И ниже окна');
  assert.match(src, /const stepsBelow = \(top, bottom\) => top >= bottom;/);
  const col = src.slice(src.indexOf('function collapse()'), src.indexOf('let rz = 0;'));
  assert.ok(col.indexOf('stepsBelow(steps[0].getBoundingClientRect().top, window.innerHeight)') > 0
    && col.indexOf('stepsBelow(') < col.indexOf('pickAnchor('), 'перед откатом — повторная проверка: читатель мог спуститься к шагам, пока ждал idle');
  assert.ok(!/field\.destroy/.test(col), 'контекст освобождает shutDown, а не откат, которого может не быть');
  // …and never under a scroll in flight (an anchor jump keeps its absolute target: the hero CTA overshot
  // #lead by ~1100 px when the field died at the top on a loaded machine) — checked BEFORE any layout read
  assert.equal(stage.SCROLL_QUIET_MS, 300);
  assert.ok(col.indexOf('performance.now() - lastScrollAt < SCROLL_QUIET_MS || (ctx.lenis && ctx.lenis.isScrolling)') > 0
    && col.indexOf('lastScrollAt < SCROLL_QUIET_MS') < col.indexOf('stepsBelow('), 'сначала «страница стоит», потом геометрия');
  assert.match(col, /collapseAt = idle\(collapse, \{ timeout: 1500 \}\); return; \}/, 'во время прокрутки — повтор в следующий тихий момент');
  assert.match(col, /removeEventListener\('scroll', markScroll\)/, 'слушатель прокрутки живёт только пока collapse ждёт');
  assert.match(src.slice(src.indexOf('function shutDown()'), src.indexOf('function scheduleCollapse()')), /addEventListener\('scroll', markScroll, \{ passive: true \}\)/);
  assert.match(src.slice(src.indexOf('destroy() {')), /removeEventListener\('scroll', markScroll\)/);
  const down = src.slice(src.indexOf('function shutDown()'), src.indexOf('function scheduleCollapse()'));
  assert.match(down, /setTimeout\(\(\) => \{ if \(field\) \{ field\.destroy\(\); field = null; \} \}, 700\)/);
  // the anchor list covers every later section and the footer (it stopped at the next one: CLS 0.87)
  const anchor = src.slice(src.indexOf('function pickAnchor()'), src.indexOf('let rz = 0;'));
  assert.match(anchor, /for \(let el = root\.nextElementSibling; el; el = el\.nextElementSibling\) list\.push\(el\)/);
});
