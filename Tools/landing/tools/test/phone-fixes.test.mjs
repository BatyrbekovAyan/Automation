// Повторное ревью 2 — phone fixes: N-5 (the «Сигнал» conversation fits its bubbles on short phones),
// N-6 (every «Ранний доступ» lands on the FORM on a phone), I-4 (the consent line is gone, owner's call).
// The browser half of N-5/N-6 is tools/probe-phone.mjs (19 viewports) and check.mjs §5/§5b; this file
// pins the pure rules and the wiring those runs depend on.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as stage from '../../src/js/stage.js';
import { pickLanding } from '../../src/js/header.js';
import { build } from '../build.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (p) => fs.readFileSync(path.join(ROOT, p), 'utf8');
const css = read('src/css/10-stage.css');
const stageSrc = read('src/js/stage.js');
const field = read('src/js/particles/field.js');
const rule = (src, sel) => (src.match(new RegExp(sel.replace(/[.()]/g, '\\$&') + '\\{([^}]*)\\}')) || [, ''])[1];

// ---- N-5 ----
// The overlay font is STEP_FONT × the steps' unit, and the bubbles are drawn for exactly that ratio: the
// text fits by construction as long as nothing floors it. What must hold is that the unit is large enough
// for a readable size wherever the width allows it — and that it never exceeds the width.
test('stepScale: единица шагов не меньше 13px/.077 (168.8), пока позволяет ширина; шире экрана не бывает', () => {
  const need = stage.STEP_UNIT_MIN;
  assert.ok(Math.abs(need * stage.STEP_FONT - 13) < 0.01, 'единица, при которой кегль переписки — 13px');
  for (let w = 360; w <= 430; w += 2) {
    for (let h = 540; h <= 932; h += 4) {
      const s = stage.phoneScale(w, h), ss = stage.stepScale(w, s);
      assert.ok(ss >= s, `${w}×${h}: шаги не мельче знака`);
      assert.ok(ss * 1.92 <= w - 40 + 1e-9 || ss === s, `${w}×${h}: пузыри в ширине экрана (${ss})`);
      const font = Math.min(21, ss * stage.STEP_FONT);
      assert.ok(font >= 12.8, `${w}×${h}: кегль переписки ${font.toFixed(2)}px — не мельче 12.8`);
      if ((w - 40) / 1.92 >= need) assert.ok(font >= 12.99, `${w}×${h}: где ширина позволяет — 13px`);
      assert.ok(font / ss <= stage.STEP_FONT + 1e-9, `${w}×${h}: кегль не обгоняет пузырь`);
    }
  }
  // короткий экран: знак остаётся маленьким (условия пробного над сгибом), переписка — своего размера
  assert.ok(Math.abs(stage.phoneScale(390, 664) - 94.4) < 0.2, 'знак на 390×664 — как решила линза владельца');
  assert.ok(Math.abs(stage.stepScale(390, stage.phoneScale(390, 664)) - need) < 0.01);
  assert.ok(Math.abs(stage.stepScale(360, stage.phoneScale(360, 640)) - 320 / 1.92) < 0.01, '360 — по ширине');
  // высокий экран не меняется: там единица шагов = единица знака (390×844 = 168.8, 412×915 = 183)
  for (const [w, h] of [[390, 844], [412, 915], [430, 932], [768, 1024]]) {
    const s = stage.phoneScale(w, h);
    assert.equal(stage.stepScale(w, s), s, `${w}×${h}`);
  }
});

test('stage css: оверлеи и резерв шагов — на единице шагов, кегль без нижнего предела', () => {
  assert.match(css, /--ss:max\(var\(--s\), min\(calc\(\(100vw - 40px\) \/ 1\.92\), 168\.8px\)\);/, 'первый кадр — та же формула, что stepScale');
  assert.match(css, /--cys:calc\(84px \+ var\(--ss\) \* \.68\);/);
  assert.match(css, /--ss:var\(--s\); --cys:var\(--cy\);/, 'desktop: одна единица на всё');
  const vis = (css.match(/\.stage\.gl-init \.sg-vis\{[\s\S]*?\}/) || [''])[0];
  assert.match(vis, /font-size:min\(calc\(var\(--ss\) \* \.077\), 21px\)/, 'кегль — доля единицы шагов');
  assert.ok(!/clamp\(13px/.test(vis), 'пол 13px выталкивал текст из пузырей (N-5)');
  const o = (css.match(/\.stage\.gl-init \.sg-o\{[\s\S]*?\}/) || [''])[0];
  assert.match(o, /left:calc\(var\(--cx\) \+ var\(--x\) \* var\(--ss\)\);top:calc\(var\(--cys\) - var\(--y\) \* var\(--ss\)\)/);
  assert.match(o, /width:calc\(var\(--w,1\) \* var\(--ss\)\);height:calc\(var\(--h,\.2\) \* var\(--ss\)\)/);
  assert.match(rule(css, '.stage.gl-init .sg-bub'), /border-radius:calc\(var\(--ss\) \* \.12\)/);
  assert.match(css, /\.sg-step\{[^}]*padding:calc\(var\(--cys\) \+ var\(--ss\) \* \.78 \+ 20px\)/, 'резерв шага — под форму шага');
  assert.match(css, /\.hero\{[^}]*padding:calc\(var\(--cy\) \+ var\(--s\) \* \.72/, 'первый экран — по-прежнему под знак');
  assert.equal(stage.STEP_FONT, 0.077, 'CSS и stepScale делят одну долю');
});

test('stage.js: холст и оверлеи сидят на одних и тех же числах единицы шагов', () => {
  const m = stageSrc.slice(stageSrc.indexOf('function measure()'), stageSrc.indexOf('function anchorsFromLayout()'));
  assert.match(m, /SS = stepScale\(W, S\); CYS = 84 \+ SS \* 0\.68;/);
  assert.match(m, /SS = S; CYS = CY;/, 'desktop — без своей единицы');
  assert.match(m, /setProperty\('--ss', `\$\{SS\}px`\)/);
  assert.match(m, /setProperty\('--cys', `\$\{CYS\}px`\)/);
  assert.match(stageSrc, /const view = \(\) => \(\{ cx: CX, cy: CY, s: S, ss: SS, cys: CYS \}\);/);
  assert.equal((stageSrc.match(/field\.resize\(W, H, view\(\)\)/g) || []).length, 3, 'все три resize — с единицей шагов');
  assert.ok(!/field\.resize\(W, H, \{ cx: CX, cy: CY, s: S \}\)/.test(stageSrc));
  // текст шагов гаснет против формы ШАГА, запас до текста — по-прежнему в единице знака (иначе на
  // 390×664 переписка не показывалась бы в покое шага)
  assert.match(stageSrc, /copyUnit\(b\.top \+ y, b\.bottom \+ y, restY, CYS, SS\)/);
  assert.match(stageSrc, /geo\.margin = OVERLAY_CLEAR \* S;/);
});

test('field.js: формы шагов переводятся одной uniform — пузыри и каналы, точки крупнее пропорционально', () => {
  assert.match(field, /uniform vec3 uStep;/);
  assert.match(field, /vec2 B = aB \* uStep\.x \+ vec2\(0\., uStep\.y\), C = aC \* uStep\.x \+ vec2\(0\., uStep\.y\);/);
  assert.match(field, /vec2 p = mix\(mix\(aA, B, p0\), C, p1\);/);
  assert.match(field, /vec2 tv = \(B - aA\) \* s0 \+ \(C - B\) \* s1;/, 'дуга перелёта — к тем же точкам');
  assert.match(field, /\* mix\(1\., uStep\.z, p0\);/, 'размер точки — по плотности формы шага');
  assert.match(field, /gl\.uniform3f\(U\.uStep, SS \/ S, \(cy - cys\) \/ S, ptStep \/ ptScale\);/);
  // знак (aA) не трогается: он и есть единица S
  assert.ok(!/aA \* uStep/.test(field));
});

// the same arithmetic the shader does, checked against what the CSS overlay does
test('uStep: точка формы шага ложится ровно туда, где её оверлей (cys − y·ss)', () => {
  const S = 94.4, CY = 84 + S * 0.68, SS = 168.8, CYS = 84 + SS * 0.68;
  const k = SS / S, dy = (CY - CYS) / S;
  for (const y of [0.575, 0.145, 0, -0.58, 0.3]) {
    const shader = CY - (y * k + dy) * S;   // px = uCenter.y − p.y·uS
    const overlay = CYS - y * SS;           // top:calc(var(--cys) - var(--y) * var(--ss))
    assert.ok(Math.abs(shader - overlay) < 1e-9, `y=${y}: ${shader} vs ${overlay}`);
  }
});

test('stepAnchor: шаг покоится в центре — или так низко, как нужно переписке на коротком экране', () => {
  // высокий экран (фикстура 390×844): центрированный текст и так ниже полосы — без изменений
  assert.equal(stage.stepAnchor(883, 844, 844, 452, 319, 50.6), 883);
  // 360×640: шаг 654 px, в центре текст заходил в запас под перепиской на 3 px — покой на 11 px раньше
  const a = stage.stepAnchor(814, 654, 640, 347, 316, 26.9);
  assert.ok(a < 814 + 7 && Math.abs(a - (814 + 347 - 316 - 26.9 - 2)) < 1e-9, `${a}`);
  // в покое текст шага — ниже полосы + запас
  assert.ok((814 + 347) - a >= 316 + 26.9);
  // нет полосы (desktop, отдельные колонки; поле погашено) — центр
  assert.equal(stage.stepAnchor(900, 1000, 900, null, null, 0), 950);
  assert.equal(stage.stepAnchor(900, 1000, 900, 300, null, 0), 950);
  const a2 = stageSrc.slice(stageSrc.indexOf('function anchorsFromLayout()'), stageSrc.indexOf('function trackCopy('));
  assert.ok(a2.indexOf('measureBands()') < a2.indexOf('stepAnchor('), 'полосы меряются до якорей');
  assert.match(a2, /Math\.max\(anchors\[i\] \+ 1, stepAnchor\(/, 'якоря строго по возрастанию (morphAt)');
});

// ---- N-6 ----
test('pickLanding: секция, пока нужное поле видно под ней; иначе — ближняя точка посадки', () => {
  assert.equal(pickLanding(72, 698, 900), 'section', '1440×900: строка «Выбери … ответ.» остаётся над формой');
  assert.equal(pickLanding(80, 1117, 664), 'land', '390×664: поле телефона было на 1141–1197');
  assert.equal(pickLanding(80, 813, 900), 'land', 'у самого края — не «видно»');
  assert.equal(pickLanding(80, 812, 900 + 8), 'section');
});
test('«Ранний доступ» ведёт на карточку формы: разметка и посадка', () => {
  const html = read('src/sections/90-final.html');
  assert.match(html, /<section class="final sec" id="lead"[^>]* data-land="\.lead-land,\.lead-fields" data-land-need="#leadPhone"/);
  assert.match(html, /<div class="wrap lead-land">\s*<div class="lead-card" data-reveal>/, 'точка посадки — обёртка карточки, первая её же строка');
  const fcss = read('src/css/90-final.css');
  assert.match(fcss, /\.lead-land\{scroll-margin-top:80px\}/);
  // верх обёртки = верх карточки только пока margin карточки схлопывается через обёртку
  assert.match(read('src/css/01-base.css'), /\.wrap\{max-width:var\(--wrap\);margin:0 auto;padding:0 var\(--gutter\)\}/, 'у .wrap нет padding-top');
  assert.ok(!/\.lead-land\{[^}]*(padding|border|overflow|display)/.test(fcss), 'обёртка не должна становиться BFC или получать отступ');
  const hdr = read('src/js/header.js');
  assert.match(hdr, /const target = section === document\.body \? section : landingChain\(section, ctx\.lenis\);/);
  assert.match(hdr, /history\.replaceState\(null, '', id\);/, 'адрес остаётся #lead');
  assert.match(hdr, /section\.focus\(\{ preventScroll: true \}\)/, 'клавиатурный фокус — на секцию');
  // все CTA раннего доступа — на #lead (header, hero, тарифы)
  const all = read('src/index.html') + read('src/sections/10-stage.html') + read('src/sections/70-pricing.html');
  assert.equal((all.match(/href="#lead"/g) || []).length, 5);
});
test('check.mjs: якоря «Ранний доступ» проверяются на карточке формы, в т. ч. на 390×664', () => {
  const chk = read('tools/check.mjs');
  assert.match(chk, /\{ vp: \[390, 664\], click: '\.hdr a\[href="#lead"\]', target: '\.lead-card' \}/);
  assert.match(chk, /\{ vp: \[360, 800\], click: '#main a\[href="#lead"\]', target: '\.lead-card' \}/);
  assert.match(chk, /\{ vp: \[1440, 900\], desktop: true, click: '\.hdr a\[href="#lead"\]', target: '#lead' \}/);
  assert.match(chk, /for \(const \[w, h\] of tier \? \[\[390, 664\], \[375, 548\], \[360, 640\]\] : \[\]\)/, 'короткие телефоны в стенде (N-5)');
});

// ---- I-4 ----
test('строки согласия под формой нет; ссылки на политику и условия — в подвале и FAQ', () => {
  const dist = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-phone-'));
  build({ src: path.join(ROOT, 'src'), dist, legal: path.resolve(ROOT, '../../docs/legal') });
  const html = fs.readFileSync(path.join(dist, 'index.html'), 'utf8');
  const cssOut = fs.readdirSync(path.join(dist, 'assets')).map((v) => path.join(dist, 'assets', v, 'site.css')).filter((f) => fs.existsSync(f)).map((f) => fs.readFileSync(f, 'utf8')).join('');
  fs.rmSync(dist, { recursive: true, force: true });
  assert.ok(!/Нажимая кнопку/.test(html), 'строка согласия убрана (решение владельца)');
  assert.ok(!/class="legal"/.test(html));
  assert.ok(!/\.final \.legal/.test(read('src/css/90-final.css')), 'и её CSS');
  if (cssOut) assert.ok(!/\.final \.legal/.test(cssOut));
  assert.match(html, /<nav class="ftr-links"[^>]*><a href="\/privacy\.html">Политика конфиденциальности<\/a><a href="\/terms\.html">Условия использования<\/a><\/nav>/);
});
