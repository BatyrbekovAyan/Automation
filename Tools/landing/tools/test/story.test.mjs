// Task 6 — «Как это работает»: the pinned phone story.
// Pure timing helpers of story.js + the markup contract of sections/30-story.html.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { storyPlan, chapterCues, chipCues, armedCount, CUE, CUE_OUT, COPY_IN, COPY_RISE, DEV_FLOAT, FLAT_Q, FIT_AIR, MIN_DEV_W, sceneFits, STORY_UNIT } from '../../src/js/story.js';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const html = fs.readFileSync(path.join(HERE, '../../src/sections/30-story.html'), 'utf8');
const text = (s) => s.replace(/<[^>]+>/g, '').replace(/&nbsp;/g, ' ').replace(/\s+/g, ' ').trim();

// Линза арт-директора (I-2): телефон и глава 01 въезжают ещё до пина, поэтому вход в пине — 0.4,
// а не целый экран, где двигалась одна полоска HUD.
test('storyPlan: короткий вход (0.4) + по экрану на главу', () => {
  const p = storyPlan(4);
  assert.equal(p.enter, 0.4);
  assert.equal(p.total, 4.4);
  assert.deepEqual(p.starts, [0.4, 1.4, 2.4, 3.4]);
});

test('chapterCues: то самое расписание, по которому идёт сцена', () => {
  const p = storyPlan(4);
  assert.deepEqual(chapterCues(p, 0), { b: 0.4, swap: 0.18, in: 0.28, out: 1.04, end: 1.28 });
  assert.deepEqual(chapterCues(p, 3), { b: 3.4, swap: 3.18, in: 3.28, out: 4.04, end: 4.28 });
  for (let i = 0; i < 4; i++) assert.equal(chapterCues(p, i).b, p.starts[i], 'граница главы = plan.starts[i]');
});

test('chapterCues: уход главы заканчивается ДО въезда следующей (монтажный стык)', () => {
  const p = storyPlan(4);
  for (let i = 0; i < 3; i++) {
    const cur = chapterCues(p, i), next = chapterCues(p, i + 1);
    assert.ok(cur.out + CUE_OUT <= next.in + 1e-9,
      `главы ${i + 1} и ${i + 2} были бы видны одновременно: ${cur.out + CUE_OUT} > ${next.in}`);
    assert.ok(cur.swap < next.swap && cur.b < next.b);
  }
});

// Линза арт-директора (I-2): главы 02/03 читались целиком ~0.08 пина, после каждой — пустая колонка.
// Сторож считает, сколько каждая глава держится полностью собранной (от посадки последнего слова и
// lead до начала ухода) и сколько длится пустота между уходом одной и въездом следующей.
test('ритм: каждая глава читается целиком ≥ 0.2 экрана, пустая колонка между главами ≤ 0.05', () => {
  const p = storyPlan(4);
  // коробок-слов в h2 глав, как их режет splitWords: NBSP держит слова вместе, финальная «.» — своя коробка
  const words = articles.map((a) => a.match(/<h2[^>]*>([\s\S]*?)<\/h2>/)[1].replace(/<br>/g, ' ').replace(/<[^>]+>/g, ' ').replace(/\.$/, ' .')
    .split(/[ \t\n]+/).filter(Boolean).length);
  assert.deepEqual(words, [5, 5, 4, 4]);
  for (let i = 1; i < 4; i++) {
    const c = chapterCues(p, i);
    const wordsDone = c.in + COPY_IN.wordsAt + COPY_IN.stagger * (words[i] - 1) + COPY_IN.words;
    const leadDone = c.in + COPY_IN.leadAt + COPY_IN.lead;
    const read = c.out - Math.max(wordsDone, leadDone);
    assert.ok(read >= 0.2 - 1e-9, `глава ${i + 1} собрана лишь ${read.toFixed(3)} экрана`);
  }
  for (let i = 0; i < 3; i++) {
    const gap = chapterCues(p, i + 1).in - (chapterCues(p, i).out + CUE_OUT);
    assert.ok(gap >= -1e-9 && gap <= 0.05 + 1e-9, `пустая колонка после главы ${i + 1}: ${gap.toFixed(3)}`);
  }
  assert.ok(CUE.end <= 1 + CUE.in + 1e-9, 'номер главы гаснет не позже въезда следующей');
});

test('chapterCues: последняя глава успевает въехать внутри пина', () => {
  const p = storyPlan(4);
  const last = chapterCues(p, 3);
  assert.ok(last.in < p.total, 'въезд последней главы — внутри закреплённой сцены');
  assert.ok(last.end <= p.total, 'её номер догорает не позже конца пина');
});

test('chipCues: чипы главы успевают сесть до своего ухода и уйти до следующей главы', () => {
  const p = storyPlan(4);
  // число чипов берём из разметки: иначе сторож разойдётся с тем, что реально анимируется
  const counts = html.split(/(?=<article class="chapter")/).slice(1)
    .map((a) => (a.match(/<li class="chip/g) || []).length);
  assert.deepEqual(counts, [3, 2, 3, 5], 'изменилось число чипов — пересчитать ритм CHIP');
  counts.forEach((k, i) => {
    const c = chipCues(p, i, k);
    assert.ok(c.land <= c.out + 1e-9,
      `глава ${i + 1}: последний чип садится на ${c.land}, а уход начинается на ${c.out} — твины дерутся за opacity`);
    if (i < 3) {
      assert.ok(c.gone <= chapterCues(p, i + 1).in + 1e-9,
        `глава ${i + 1}: чипы догорают на ${c.gone}, а глава ${i + 2} въезжает на ${chapterCues(p, i + 1).in}`);
    }
  });
});

test('armedCount: экран главы i грузится, когда прогресс глав > i − 0.5', () => {
  assert.equal(armedCount(0, 4), 1, 'первый экран — всегда');
  assert.equal(armedCount(1.5, 4), 1, 'ровно на половине — ещё нет');
  assert.equal(armedCount(1.51, 4), 2);
  assert.equal(armedCount(2.49, 4), 2);
  assert.equal(armedCount(2.6, 4), 3);
  assert.equal(armedCount(3.51, 4), 4);
  assert.equal(armedCount(99, 4), 4);
});

test('разметка: секция #how с модулем story и четыре главы по порядку', () => {
  assert.match(html, /<section class="story[^"]*" id="how" data-module="story"/);
  const steps = [...html.matchAll(/<article class="chapter" data-step="(\d)"/g)].map((m) => m[1]);
  assert.deepEqual(steps, ['1', '2', '3', '4']);
});

const CHAPTERS = [
  { eyebrow: '01 · Все чаты', h2: 'Все клиенты —<br><em>в одном окне</em>.',
    lead: 'WhatsApp и Telegram в одном приложении. Бот отвечает с вашего номера — для клиентов ничего не меняется.',
    chips: ['WhatsApp', 'Telegram', 'Отвечает 24/7'], shot: 'chats' },
  { eyebrow: '02 · Режим «Вместе»', h2: 'Бот предлагает.<br><em>Вы выбираете</em>.',
    lead: 'До четырёх готовых вариантов на сообщение клиента — одно касание, и ответ ушёл. А в режиме «Авто» бот отвечает сам.',
    chips: ['До 4 вариантов', 'Одно касание'], shot: 'suggestions' },
  { eyebrow: '03 · Прайс', h2: 'Знает ваш <em>прайс</em>.',
    lead: 'Загрузите прайс-лист фото, Excel, PDF или Word — или заведите каталог в приложении. Бот отвечает по вашим ценам.',
    chips: ['XLSX · Прайс август', 'PDF · Колодки и фильтры', 'JPG · Фото прайса'], shot: 'pricelist' },
  { eyebrow: '04 · Сводка', h2: 'Итог каждого <em>диалога</em>.',
    lead: 'Заявка собрана, нужен владелец, клиент замолчал — видно сразу, без чтения переписок.',
    chips: ['Заявка собрана', 'Нужен владелец', 'В диалоге', 'Клиент замолчал', 'Вопрос закрыт'], shot: 'svodka' },
];
const articles = html.split(/(?=<article class="chapter")/).slice(1);

CHAPTERS.forEach((c, i) => {
  test(`глава ${i + 1}: тексты дословно`, () => {
    const a = articles[i];
    assert.ok(a, 'нет главы');
    assert.equal(text(a.match(/<p class="eyebrow"[^>]*>([\s\S]*?)<\/p>/)[1]), c.eyebrow);
    // разметка h2 дословно; неразрывные пробелы (типографика Task 12) — это те же пробелы
    assert.equal(a.match(/<h2[^>]*>([\s\S]*?)<\/h2>/)[1].trim().replace(/&nbsp;/g, ' '), c.h2);
    assert.equal(text(a.match(/<p class="lead"[^>]*>([\s\S]*?)<\/p>/)[1]), c.lead);
    const chips = [...a.matchAll(/<li class="chip[^"]*"[^>]*>([\s\S]*?)<\/li>/g)].map((m) => text(m[1]));
    assert.deepEqual(chips, c.chips);
  });
  test(`глава ${i + 1}: настоящий экран с srcset 720/1080, lazy, размеры, alt`, () => {
    const fig = articles[i].match(/<figure class="phone-screen[^"]*"[^>]*>([\s\S]*?)<\/figure>/);
    assert.ok(fig, 'нет figure.phone-screen');
    const img = fig[1].match(/<img\b[^>]*>/)[0];
    const attr = (n) => (img.match(new RegExp(`\\s${n}="([^"]*)"`)) || [])[1];
    const base = `/assets/__V__/img/shot-${c.shot}`;
    assert.equal(attr('src'), `${base}-720.webp`);
    assert.equal(attr('srcset'), `${base}-720.webp 720w, ${base}-1080.webp 1080w`);
    assert.equal(attr('sizes'), '(min-width: 900px) 360px, 70vw');
    assert.equal(attr('loading'), 'lazy');
    assert.equal(attr('decoding'), 'async');
    assert.equal(attr('width'), '720');
    assert.equal(attr('height'), '1558', 'реальный размер файлов 720×1558');
    assert.match(attr('alt'), /^Экран приложения: \S/);
  });
});

test('правдивость: в истории нет запрещённых обещаний', () => {
  const all = text(html);
  for (const re of [/отзыв/i, /компаний|клиентов довольны/i, /\d+\s*%/, /за \d+ секунд/i, /Kaspi|1С|CRM/, /казахск/i, /паузу|передаёт диалог/i, /рейтинг/i]) {
    assert.ok(!re.test(all), `запрещено: ${re}`);
  }
});

/* ---- сторожа исходников: две ловушки, на которые эта сцена уже наступала ---- */
const js = fs.readFileSync(path.join(HERE, '../../src/js/story.js'), 'utf8');
const css = fs.readFileSync(path.join(HERE, '../../src/css/30-story.css'), 'utf8');

test('matchMedia: есть условие, совпадающее всегда (иначе на телефоне сцена не соберётся)', () => {
  const call = js.match(/mm\.add\(\{([^}]*)\}/);
  assert.ok(call, 'нет mm.add с набором условий');
  assert.match(call[1], /'\(max-width: 899px\)'/, 'нужна пара к (min-width: 900px): иначе на мобильном ни одно условие не совпадает');
});

/** Границы блока `{…}`, начинающегося с заголовка header. */
function block(src, header) {
  const i = src.indexOf(header);
  assert.ok(i >= 0, `нет блока ${header}`);
  let depth = 0;
  for (let k = src.indexOf('{', i); k < src.length; k++) {
    if (src[k] === '{') depth++;
    else if (src[k] === '}' && --depth === 0) return { start: i, end: k + 1 };
  }
  throw new Error(`не закрыт блок ${header}`);
}

test('layout читается ровно в одном месте — в measureCopy на refresh, а не в прокрутке', () => {
  const m = block(js, 'function measureCopy()');
  // + одноразовая компенсация роста на старте (Task 12, ревью T6 r4 I1) — тоже не в прокрутке
  const hold = block(js, 'function holdBelow()'), rest = block(js, 'function restoreBelow(held)');
  const inside = (i, b) => i > b.start && i < b.end;
  for (const hit of js.matchAll(/\.offset(Width|Height|Top|Left)|\.clientWidth|\.clientHeight|getBoundingClientRect|getComputedStyle/g)) {
    assert.ok(inside(hit.index, m) || inside(hit.index, hold) || inside(hit.index, rest),
      `чтение геометрии вне measureCopy/holdBelow/restoreBelow (символ ${hit.index}): ${hit[0]}`);
  }
  assert.match(js, /ScrollTrigger\.addEventListener\('refreshInit', measureCopy\)/,
    'замер обязан висеть на refreshInit: он идёт ДО того, как ScrollTrigger померит сцену');
  assert.match(js, /ScrollTrigger\.removeEventListener\('refreshInit', measureCopy\)/, 'и сниматься в destroy');
  const upd = js.match(/onUpdate:[^\n]*/)[0];
  assert.ok(!/measureCopy|getBoundingClientRect|\.offset[WH]/.test(upd), `в onUpdate нет замера: ${upd}`);
});

test('повёрнутый телефон получает статичную раскладку, а не сцену с телефоном поверх текста', () => {
  assert.equal(FLAT_Q, '(max-height: 500px) and (orientation: landscape)');
  const call = js.match(/mm\.add\(\{([^}]*)\}/);
  assert.match(call[1], /flat: FLAT_Q/, 'условие «низкий альбомный экран» — в том же наборе matchMedia');
  const cb = js.slice(call.index, call.index + 600);
  assert.match(cb, /if \(flat\) \{ goStatic\(\); return; \}\s*goLive\(\);\s*root\.style\.setProperty\('--pin-k', [^\n]*\n\s*const air = measureCopy\(\);/,
    'на flat — столбик и выход ДО любых gsap.set/ScrollTrigger; иначе — сцена, длина пина в CSS и замер текста до первого refresh');
  // включение/выключение сцены живёт в двух идемпотентных функциях, destroy — через ту же
  const body = (header) => { const b = block(js, header); return js.slice(b.start, b.end); };
  assert.match(body('destroy()'), /goStatic\(\)/, 'destroy возвращает столбик тем же путём');
  const live = body('function goLive()');
  assert.match(live, /if \(root\.classList\.contains\('is-live'\)\) return;/, 'повторный вызов не должен дёргать корпус');
  const stat = body('function goStatic()');
  assert.match(stat, /parked\[i\] = null/, 'в столбике снятые адреса картинок возвращаются');
  assert.ok(!/root\.classList\.add\('is-live'\)/.test(js.slice(0, block(js, 'function goLive()').start)),
    'is-live до matchMedia включать нельзя: на повёрнутом телефоне сцена собралась бы');
});

test('sceneFits: сцена — только если корпус получил резерв под текстом, а не упёрся в пол', () => {
  // воздух между низом текста (с въездом) и верхом корпуса; без упора в пол он равен --copy-gap
  const gap = +css.match(/--copy-gap:(\d+)px/)[1];
  assert.ok(FIT_AIR > 0 && FIT_AIR < gap,
    `порог ${FIT_AIR} обязан быть ниже --copy-gap ${gap}: иначе обычная геометрия (воздух = gap, ±1px округления) не пройдёт`);
  assert.equal(sceneFits(gap), true, 'резерв целиком — сцена');
  assert.equal(sceneFits(gap - 1), true, 'округление ceil(--copy-h) — не повод для столбика');
  // замер r3 (sweep-r3-base): 360×480 и 412×480 → 7, 320×500 → 4, 360×450 → −23, 320×460 → −36
  for (const air of [7, 4, -23, -36]) assert.equal(sceneFits(air), false, `корпус на полу (воздух ${air}) — столбик`);
  assert.equal(sceneFits(null), false, 'нет замера — нет сцены');
});
// Task 12 (ревью T6 r4, Minor 3): при корпусе уже ~130 px «настоящие экраны» — миниатюры, а чипы
// главы 04 закрывают почти весь экран (320×530 → 87 px, iPhone SE в Safari 375×548 → 106 px);
// статичный столбик на тех же окнах показывает каждый экран во всю ширину колонки.
test('sceneFits: корпус уже MIN_DEV_W — столбик', () => {
  assert.equal(MIN_DEV_W, 130);
  assert.equal(sceneFits(18, 161), true, '390×664 (iPhone в Safari) — сцена');
  assert.equal(sceneFits(18, 150), true, '360×640 — сцена');
  assert.equal(sceneFits(18, 106), false, '375×548 — столбик');
  assert.equal(sceneFits(18, 87), false, '320×530 — столбик');
  assert.equal(sceneFits(18), true, 'без ширины решает воздух');
});

test('проверка «помещается» — один раз на старте сцены, не в refresh: клавиатура и панели браузера её не дёргают', () => {
  const call = js.match(/mm\.add\(\{([^}]*)\}/);
  const cb = js.slice(call.index, call.index + 600);
  assert.match(cb, /const air = measureCopy\(\);\s*if \(!desk && !sceneFits\(air, devW\)\) \{ goStatic\(\); return; \}/,
    'на мобильной раскладке не поместилась — столбик и выход');
  const at = js.indexOf('!sceneFits(air, devW)');
  const firstSet = js.indexOf('gsap.set(', call.index);
  assert.ok(at > call.index && at < firstSet, 'решение — ДО первого gsap.set и до ScrollTrigger-ов сцены');
  // measureCopy висит на refreshInit: если бы он сам переключал сцену, открытая в форме ниже
  // клавиатура (Android WebView меняет высоту окна) развалила бы историю в столбик посреди ввода
  const m = block(js, 'function measureCopy()');
  const body = js.slice(m.start, m.end);
  assert.ok(!/goStatic|goLive|sceneFits|mm\./.test(body), 'замер только меряет — решение принимает сцена');
  assert.match(body, /if \(!root\.classList\.contains\('is-live'\)\) return null;/,
    'в столбике замерять нечего: --copy-h там не участвует, а корпус скрыт (offsetTop = 0)');
  assert.match(body, /return air;/, 'замер отдаёт воздух — по нему сцена и решает');
  assert.match(body, /device\.offsetTop/, 'воздух — от раскладки (offset*), не от getBoundingClientRect: на старте корпус ещё повёрнут и уменьшен твином');
});

test('подкачка экранов идёт через armedCount в onUpdate', () => {
  assert.match(js, /onUpdate:[^\n]*armedCount\(self\.progress \* plan\.total, n\)/);
});

test('ритм сцены берётся из chapterCues, а не из литералов (иначе тесты зелены, а пейсинг сломан)', () => {
  assert.match(js, /const cue = chapterCues\(plan, i\)/, 'глава обязана брать расписание из seam-а');
  assert.match(js, /const w = cue\.swap/, 'смена экрана — тоже из seam-а');
  for (const re of [/i \+ 0\.82/, /i \+ 1\.46/, /i \+ 1\.82/, /const b = i \+ 1/]) {
    assert.ok(!re.test(js), `расписание вернулось в литералы: ${re}`);
  }
});

test('CSS: статичная раскладка — состояние по умолчанию, пин только под .is-live', () => {
  const beforeLive = css.slice(0, css.indexOf('.story.is-live{padding:0}'));
  assert.match(beforeLive, /\.story-atmo,\.story-device,\.story-hud\{display:none\}/, 'корпус и индикатор скрыты, пока сцену не включил JS');
  // Линза перф-инженера (I-1): pin ScrollTrigger-а давал layout-shift ≈ 1.0 на каждом входе/выходе и
  // прыжок anticipatePin; сцену держит CSS sticky — и только в кино-режиме.
  const sticky = [...css.matchAll(/([^{}]+)\{[^}]*position:sticky/g)].map((m) => m[1].trim());
  assert.deepEqual(sticky, ['.is-live .story-stage'], 'sticky — только у сцены кино-режима');
  assert.match(css, /\.story\.is-live\{padding:0;height:calc\(var\(--vh\) \* \(1 \+ var\(--pin-k, 0\)\)\)\}/,
    'длина пина — высота секции: экран × (1 + --pin-k)');
  assert.ok(!/\bpin:|anticipatePin|pinSpacing/.test(js.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '')), 'pin ScrollTrigger-а не вернулся');
  for (const re of [/\.is-live \.story-stage\{[^}]*height:var\(--vh\)/, /\.is-live \.chapter\{position:absolute/]) {
    assert.match(css, re);
  }
  // 3D только на десктопе: мобильная сцена плоская
  assert.ok(!/rotate[YX]/.test(css), 'наклон корпуса задаётся из JS (и только на десктопе)');
});

test('CSS: экран внутри корпуса имеет явные размеры (авто-ширина по insets схлопывалась в 0)', () => {
  const rule = css.match(/\.is-live \.story-device \.phone-screen\{([^}]*)\}/);
  assert.ok(rule, 'нет правила для экрана внутри корпуса');
  assert.match(rule[1], /width:var\(--scr-w\)/);
  assert.match(rule[1], /height:var\(--scr-h\)/);
});

test('CSS: у svh есть запасной vh (в custom property невалидная единица роняет всю сцену)', () => {
  assert.match(css, /\.story\.is-live\{\s*--vh:100vh;/, 'сначала vh…');
  assert.match(css, /@supports \(height:1svh\)\{\.story\.is-live\{--vh:100svh\}\}/, '…и svh поверх через @supports');
  const rest = css.replace(/\/\*[\s\S]*?\*\//g, '').replace(/@supports \(height:1svh\)\{[^\n]*\}/, '');
  assert.ok(!/svh/.test(rest), 'остальная сцена обязана считать высоту окна через var(--vh)');
});

test('CSS: место под корпус — остаток под текстом главы, а не «100svh минус константа»', () => {
  assert.match(css, /--copy-h:\d+px/, 'нужна дефолтная высота текста до первого замера');
  assert.match(css, /--scr-h:max\(160px,min\(/, 'у корпуса есть пол — иначе текст выше окна даёт отрицательную высоту');
  assert.match(css, /calc\(\(var\(--vh\) - var\(--ph-bot\) - var\(--copy-top\) - var\(--copy-h\) - var\(--copy-rise\) - var\(--copy-gap\)\) \* var\(--scr-k\)\)/,
    'резерв над корпусом считается от измеренной высоты текста и от въезда');
  assert.match(css, /\.is-live \.chapter-copy\{[^}]*top:var\(--copy-top\)/,
    'текст стоит ровно там, откуда считается резерв: второй литерал верха разъедется с формулой');
  for (const re of [/100svh - 368px/, /100svh - 292px/, /var\(--vh\) - 368px/, /var\(--vh\) - 292px/]) {
    assert.ok(!re.test(css), `константный резерв над корпусом вернулся: ${re}`);
  }
});

test('движение сцены участвует в геометрии: въезд текста и «дыхание» корпуса', () => {
  // на въезде lead стоит ниже своего места — если CSS не заложит это, текст ложится на корпус
  assert.ok(COPY_RISE > 0);
  assert.match(js, /root\.style\.setProperty\('--copy-rise', COPY_RISE \+ 'px'\)/,
    'число въезда отдаётся в CSS из того же места, где им двигают — иначе разъедутся');
  assert.ok(!/\{ opacity: 0, y: 18 \}|y: 18,/.test(js), 'въезд текста вернулся в литерал 18');
  assert.match(css, /--copy-rise:18px/, 'дефолт --copy-rise должен совпадать с COPY_RISE');
  // «дыхание» — только вниз: вверх оно подъезжает под текст и съедает уже посчитанный воздух
  assert.ok(DEV_FLOAT >= 0, 'корпус не должен всплывать вверх');
  assert.match(js, /yPercent: i % 2 \? DEV_FLOAT : 0/, 'всплытие корпуса — из seam-а и неотрицательное');
});

// Task 12 (ревью T6 r4, Important 1): включение кино-режима над читателем компенсируется.
test('рост секции над окном не двигает то, на что смотрит читатель', () => {
  const held = js.indexOf('const held = holdBelow();');
  const mmAdd = js.indexOf('mm.add(');
  const rest = js.indexOf('restoreBelow(held);');
  assert.ok(held >= 0 && mmAdd > held && rest > mmAdd, 'порядок: запомнить → mm.add (goLive) → восстановить');
  const h = js.slice(block(js, 'function holdBelow()').start, block(js, 'function holdBelow()').end);
  assert.match(h, /bottom > 0\) return null/, 'компенсация только когда секция целиком выше окна');
  const r = js.slice(block(js, 'function restoreBelow(held)').start, block(js, 'function restoreBelow(held)').end);
  assert.match(r, /ScrollTrigger\.refresh\(\)/, 'сначала пин меряется');
  assert.match(r, /lenis\.scrollTo\(to, \{ immediate: true, force: true \}\)/, 'с Lenis двигается и его собственная цель');
});

// Task 12 (пейсинг): на телефоне пин короче — 0.8 экрана на единицу таймлайна, на desktop — экран.
// Длину задаёт CSS (--pin-k), таймлайн берёт её из той же раскладки — разъехаться они не могут.
test('длина пина: телефон 3.52 экрана (4.4 единицы × 80 %), desktop 4.4 экрана', () => {
  assert.deepEqual(STORY_UNIT, { desk: 100, mob: 80 });
  assert.equal(Math.round(storyPlan(4).total * STORY_UNIT.mob), 352);
  assert.match(js, /root\.style\.setProperty\('--pin-k', String\(Math\.round\(plan\.total \* \(desk \? STORY_UNIT\.desk : STORY_UNIT\.mob\)\) \/ 100\)\)/);
  assert.match(js, /end: \(\) => '\+=' \+ pinPx/, 'конец таймлайна — измеренная длина sticky-участка');
  const m = block(js, 'function measureCopy()');
  assert.match(js.slice(m.start, m.end), /pinPx = Math\.max\(1, root\.offsetHeight - stage\.offsetHeight\)/,
    'длина пина меряется на refreshInit — вместе с остальной геометрией сцены');
});

// ---- Final review (2026-09-22), I-5: the stepwise mode under load ----
import { restTimes, chapterAt, STEP_S, STEP_HOLD_MS, STEP_ALPHA } from '../../src/js/story.js';
test('restTimes: покой главы — после посадки текста и чипов, до начала ухода; у последней — конец', () => {
  const plan = storyPlan(4), chips = [3, 5, 4, 5], words = [3, 4, 6, 4];
  const rests = restTimes(plan, chips, words);
  assert.equal(rests.length, 4);
  for (let i = 0; i < 3; i++) {
    const cue = chapterCues(plan, i), chip = chipCues(plan, i, chips[i]);
    const textIn = i === 0 ? 0 : cue.in + Math.max(COPY_IN.wordsAt + COPY_IN.words + COPY_IN.stagger * (words[i] - 1), COPY_IN.leadAt + COPY_IN.lead);
    assert.ok(rests[i] > chip.land && rests[i] > textIn, `глава ${i + 1}: ${rests[i]} — всё уже село`);
    assert.ok(rests[i] < cue.out, `глава ${i + 1}: ${rests[i]} — ещё не уходит (${cue.out})`);
  }
  assert.equal(rests[3], plan.total);
});
test('chapterAt: граница — въезд следующей главы; покой каждой главы — в ней самой', () => {
  const plan = storyPlan(4), rests = restTimes(plan, [3, 5, 4, 5]);
  assert.equal(chapterAt(plan, 0), 0);
  assert.equal(chapterAt(plan, plan.total), 3);
  rests.forEach((r, i) => assert.equal(chapterAt(plan, r), i));
  for (let j = 1; j < 4; j++) {
    const b = chapterCues(plan, j).in;
    assert.equal(chapterAt(plan, b - 0.001), j - 1);
    assert.equal(chapterAt(plan, b), j);
  }
  let prev = 0;
  for (let t = 0; t <= plan.total; t += 0.01) { const i = chapterAt(plan, t); assert.ok(i >= prev); prev = i; }
});
test('шаги: регулятор слушает тикер GSAP только пока сцена закреплена, скраб снимается без убийства таймлайна', () => {
  assert.match(js, /import \{ FrameGovernor \} from '\.\/quality\.js'/);
  assert.match(js, /onToggle: \(self\) => \{ sceneOn = self.isActive; toggled\(\); \}/, 'кадры считаются в закреплённой сцене…');
  assert.match(js, /scrub: 0\.5, onToggle: \(self\) => \{ leadOn = self\.isActive; toggled\(\); \}/, '…и уже на подходе к ней');
  assert.match(js, /const toggled = \(\) => sampleWhile\(leadOn \|\| sceneOn\);/);
  assert.match(js, /if \(performance\.now\(\) - offAt > 1000\) gov\.reset\(\)/, 'стык подхода и пина не обнуляет счёт');
  // регулятор поля частиц уже сбрасывал нагрузку — устройство себя показало, сцена сразу на шагах
  assert.match(js, /if \(on && qa !== 'scrub' && ctx\.governor && ctx\.governor\.level > 0\) \{ goStepwise\(\); return; \}/);
  assert.match(js, /if \(!tl\) \{ stepped = true; return; \}/, 'до таймлайна — только флаг, переход делает `if (stepped)`');
  assert.ok(js.indexOf('if (stepped) goStepwise();') > js.indexOf('tl = gsap.timeline({'));
  assert.match(js, /gsap\.ticker\.add\(sample\)/);
  assert.match(js, /gsap\.ticker\.remove\(sample\)/);
  assert.match(js, /st\.kill\(false, true\)/, 'allowAnimation: таймлайн и его состояние остаются');
  assert.match(js, /tl\.tweenTo\(rests\[i\], \{ duration: STEP_S/);
  assert.match(js, /if \(stepTween && stepTween\.isActive\(\)\) \{ stepTween\.kill\(\); stepTween = null; tl\.time\(rests\[i\]\); return; \}/, 'шаг внахлёст — склейка одним кадром');
  assert.ok(STEP_S > 0 && STEP_S <= 0.6 && STEP_HOLD_MS >= 400 && STEP_HOLD_MS <= 1500);
  assert.ok(STEP_ALPHA >= 1 / 30 && STEP_ALPHA <= 1 / 8, 'средний кадр догоняет за 8–30 кадров');
  assert.match(js, /holdMs: STEP_HOLD_MS, alpha: STEP_ALPHA/);
  // регулятор и let-состояние объявлены до таймлайна: onToggle может сработать при создании триггера
  assert.ok(js.indexOf('let sampling = false') < js.indexOf('tl = gsap.timeline({'));
  const step = block(js, 'function goStepwise()');
  const body = js.slice(step.start, step.end);
  assert.ok(!/getBoundingClientRect|\.offset[WHTL]|measureCopy/.test(body), 'шаги не читают раскладку');
});
test('шаги сбрасывают дорогое: 3D-контекст и заднюю плоскость корпуса, размытие под шапкой (.fx-lite)', () => {
  const step = block(js, 'function goStepwise()');
  const body = js.slice(step.start, step.end);
  assert.match(body, /root\.dataset\.mode = 'steps'/);
  assert.match(body, /const hdr = document\.getElementById\('hdr'\);[^\n]*\n\s*if \(hdr\) hdr\.classList\.add\('fx-lite'\);/, 'класс — на шапке: <html> пересчитал бы стили всей страницы');
  assert.match(css, /\.story\[data-mode=steps\] \.story-device\{transform-style:flat\}/);
  assert.match(css, /\.story\[data-mode=steps\] \.dev-back\{display:none\}/);
  const base = fs.readFileSync(path.join(HERE, '../../src/css/01-base.css'), 'utf8');
  const lite = base.match(/\.hdr\.fx-lite\.scrolled\{([^}]*)\}/);
  assert.ok(lite, 'правило .fx-lite для шапки');
  assert.match(lite[1], /backdrop-filter:none/);
  // та же специфичность, что у .q-medium .hdr.scrolled (0,3,0) — выигрывает порядок
  assert.ok(base.indexOf('.hdr.fx-lite.scrolled{') > base.indexOf('.q-high .hdr.scrolled,.q-medium .hdr.scrolled'));
});
test('регулятор сцены (телефон, 20 мс): один кадр 250 мс — не повод, устойчивые 40 fps — переход быстрее 1.3 с', async () => {
  const { FrameGovernor } = await import('../../src/js/quality.js');
  const mk = () => new FrameGovernor({ limitMs: 20, holdMs: STEP_HOLD_MS, alpha: STEP_ALPHA });
  let g = mk(), tripped = false;
  for (let i = 0; i < 120; i++) if (g.sample(16.7) !== 'none') tripped = true;
  if (g.sample(250) !== 'none') tripped = true;
  for (let i = 0; i < 240; i++) if (g.sample(16.7) !== 'none') tripped = true;
  assert.ok(!tripped, 'одиночная сборка мусора / декодирование не снимает скраб');
  g = mk();
  let t = 0, at = null;
  for (let i = 0; i < 200 && at === null; i++) { t += 25; if (g.sample(25) !== 'none') at = t; }
  assert.ok(at !== null && at <= 1300, `40 fps → шаги через ${at} мс`);
});
