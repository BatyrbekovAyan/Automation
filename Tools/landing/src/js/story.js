// 30-story — «Как это работает»: четыре главы под одним телефоном.
// Разметка самодостаточна: без JS (и на .q-low, и при prefers-reduced-motion) главы идут
// столбиком, у каждой свой экран. Кино-режим включается классом .is-live: четыре <figure>
// переезжают в общий корпус (одна рамка, одна трансформация, один клип), сцена
// закрепляется CSS-ом (position:sticky) на длину таймлайна, главы и экраны сменяются по прогрессу.
//
// Геометрия ниже — чистая арифметика таймлайна в «экранах»: 1 единица = 1 экран прокрутки.
// В onUpdate нет ни одного чтения layout — только подкачка следующего скриншота.

import { FrameGovernor } from './quality.js';

/** Раскладка сцены: короткий вход + по экрану на главу. */
export function storyPlan(count) {
  // Телефон и текст главы 01 въезжают ещё ДО пина (допиновая шкала `lead`), поэтому вход внутри
  // пина — только 0.4: при целом экране глава 01 держалась 20/61 точек покоя, из них первую
  // «единицу» двигалась одна полоска HUD (линза арт-директора, I-2).
  const enter = 0.4;
  const starts = [];                                // граница главы i — на ней меняется экран
  for (let i = 0; i < count; i++) starts.push(i + enter);
  return { enter, total: count + enter, starts };
}

/* Ритм главы — смещения от её границы b, в экранах прокрутки. swap: смена экрана в корпусе;
   in: въезд текста (чуть раньше границы); out: начало ухода; end: номер переходит к следующей.
   Инвариант, на котором держится «монтажный стык»: уход заканчивается ДО въезда следующей
   главы (out + OUT ≤ следующий in), иначе два текста видны одновременно. */
/* Ритм перебалансирован по линзе арт-директора: главы 02/03 читались целиком ~0.08 пина (один флик),
   после каждой — три точки покоя с пустой колонкой. Теперь текст въезжает быстрее и держится дольше:
   out + OUT = 0.84 ≤ следующий in 0.88 — стык тот же, пустоты ≤ 0.04 экрана. */
export const CUE = { swap: -0.22, in: -0.12, out: 0.64, end: 0.88 };
export const CUE_OUT = 0.2;                         // длительность ухода главы
/** Въезд текста главы (в единицах таймлайна): слова, eyebrow, lead. */
export const COPY_IN = { eyebrow: 0.24, words: 0.32, stagger: 0.04, wordsAt: 0.04, leadAt: 0.14, lead: 0.3 };

/** Моменты событий главы i в единицах таймлайна (1 = экран прокрутки). */
export function chapterCues(plan, i) {
  const b = plan.starts[i];
  const at = (off) => Math.round((b + off) * 1000) / 1000;   // без округления 1 − 0.18 = 0.8200000000000001
  return { b, swap: at(CUE.swap), in: at(CUE.in), out: at(CUE.out), end: at(CUE.end) };
}

/* Ритм чипов, в тех же единицах. Инвариант: ПОСЛЕДНИЙ чип главы садится до того, как первый
   начинает уходить, а уход всей пачки заканчивается до въезда следующей главы. До этого въезд
   (0.44 + 5×0.055) и уход (out − 0.06) перекрывались на ~0.2 экрана: два твина писали opacity
   одним и тем же элементам, и держалось это только на удаче порядка рендера. */
export const CHIP = {
  first: 0.2,                                       // глава 01 приезжает на подходе, не по своей границе
  in: 0.2, dur: 0.28, stagger: 0.028,
  out: 0, outDur: 0.16, outStagger: 0.02,           // out — смещение от CUE.out главы
};

/** Моменты чипов главы i при k чипах: начало въезда, посадка последнего, начало и конец ухода. */
export function chipCues(plan, i, k) {
  const cue = chapterCues(plan, i);
  const at = (v) => Math.round(v * 1000) / 1000;
  const start = i === 0 ? CHIP.first : cue.in + CHIP.in;
  const last = Math.max(0, k - 1);
  const out = cue.out + CHIP.out;
  return {
    in: at(start),
    land: at(start + CHIP.dur + CHIP.stagger * last),
    out: at(out),
    gone: at(out + CHIP.outDur + CHIP.outStagger * last),
  };
}

/** Сколько экранов держим загруженными: глава i подкачивается, когда t перевалит за i − 0.5. */
export function armedCount(t, count) {
  return Math.min(count, Math.max(1, Math.ceil(t - 0.5)));
}

/* Пошаговый режим (финальное ревью, I-5): если кадры сцены медленнее бюджета, скраб (стили десятку
   элементов в каждом кадре) снимается — главы сменяются прокруткой, но коротким tweenTo до покоя главы
   на её границе. Раскладка та же (transform/opacity): ни сдвигов, ни скачка прокрутки. */
export const STEP_S = 0.5;                            // длительность шага: уход главы + въезд следующей
export const STEP_HOLD_MS = 800;                      // сколько медленных кадров терпим (регулятор сцены)
export const STEP_ALPHA = 1 / 30;                     // и как быстро средний кадр их догоняет
/* Кадры считаются уже на подходе, а если регулятор поля частиц сбрасывал нагрузку (ctx.governor.level
   > 0), шаги сразу: переход к середине истории ничего не давал. Порог не короче 800 мс: один кадр
   250 мс держит средний выше лимита ~670 мс. */
/** Состояние покоя главы i: текст и чипы сели, уход ещё не начался; у последней — конец таймлайна. */
export function restTimes(plan, chipCounts, wordCounts = []) {
  const n = plan.starts.length;
  return plan.starts.map((b, i) => {
    if (i === n - 1) return plan.total;
    const cue = chapterCues(plan, i);
    const chip = chipCues(plan, i, chipCounts[i] || 0);
    const words = COPY_IN.wordsAt + COPY_IN.words + COPY_IN.stagger * Math.max(0, (wordCounts[i] || 1) - 1);
    const textIn = i === 0 ? 0 : cue.in + Math.max(words, COPY_IN.leadAt + COPY_IN.lead);
    return Math.round(((Math.max(chip.land, textIn) + cue.out) / 2) * 1000) / 1000;
  });
}
/** Какая глава стоит при прогрессе t (в единицах таймлайна): i, пока t не дошёл до въезда i+1. */
export function chapterAt(plan, t) {
  let i = 0;
  for (let j = 1; j < plan.starts.length; j++) if (t >= chapterCues(plan, j).in) i = j;
  return i;
}

// Наклон корпуса по главам (только desktop): [rotationY, rotationX] в градусах.
const TILTS = [[-14, 6], [-8.5, 3.4], [-15, 6.6], [-9.5, 3.8]];

/* Две константы движения, которые ОБЯЗАНЫ участвовать в геометрии сцены, иначе замер
   «текст кончается здесь» врёт ровно на них (замерено на 360×780: зазор до корпуса падал
   с 20px до 1.3px):
   COPY_RISE — на въезде lead приезжает снизу, то есть в эти кадры текст НИЖЕ своего места;
     story.js отдаёт число в CSS через --copy-rise, и резерв над корпусом его закладывает.
   DEV_FLOAT — «дыхание» корпуса по главам. Оно только ВНИЗ: было ±(−1.4/+0.6)%, и на чётных
     главах корпус подъезжал под текст на 6.5px, съедая воздух, который CSS уже посчитал. */
/** Длина единицы таймлайна сцены в % высоты окна. */
export const STORY_UNIT = { desk: 100, mob: 80 };

export const COPY_RISE = 18;
export const DEV_FLOAT = 1.4;

/* Сцене нужен экран, где под текстом главы остаётся место на телефон. У телефона, повёрнутого
   горизонтально, его нет: корпус упирается в свой пол (160px) и ложится на текст — замер
   −24…−118px на 568×320…896×414, чипы при этом наезжают на строки. Там показываем ту же
   статичную раскладку, что без JS; поворот обратно возвращает сцену (matchMedia). */
export const FLAT_Q = '(max-height: 500px) and (orientation: landscape)';

/* Тот же принцип в портрете: корпус получает всё, что осталось под текстом главы, но не меньше
   пола (160px экрана). Пол только страховал от отрицательной высоты — упор в него и значит
   «не помещается»: воздух между текстом (с въездом) и корпусом, который без упора равен
   --copy-gap (18px), падает до 7 / 4 / −23 / −36px на 360×480 / 320×500 / 360×450 / 320×460
   (Android split-screen, всплывающее окно) — корпус ложится на текст, чипы вылезают за окно.
   Там — статичная раскладка. Решение принимается один раз на старте сцены (и заново при смене
   условий matchMedia — поворот, desk ⇄ mob), а НЕ на каждый refresh: клавиатура в форме ниже
   (в Android WebView она меняет высоту окна) и панели браузера меняют только высоту, и история
   не должна от этого разваливаться в столбик посреди ввода или прокрутки. */
export const FIT_AIR = 12;
/* …и корпус не уже MIN_DEV_W: ниже ~130 px «настоящие экраны» становятся миниатюрами, а пачка
   чипов главы 04 закрывает почти весь экран (320×530 → 87 px, iPhone SE в Safari 375×548 → 106 px).
   Статичный столбик на тех же окнах показывает каждый экран во всю ширину колонки (Task 12). */
export const MIN_DEV_W = 130;
export function sceneFits(air, devW = Infinity) {
  return typeof air === 'number' && air >= FIT_AIR && devW >= MIN_DEV_W;
}

export function init(root, ctx) {
  const stage = root.querySelector('.story-stage');
  const device = root.querySelector('.story-device');
  const chapters = [...root.querySelectorAll('.chapter')];
  const n = chapters.length;
  const { gsap, ScrollTrigger } = ctx;
  // статичная раскладка — это состояние разметки по умолчанию, делать нечего
  if (!stage || !device || !n || !gsap || !ScrollTrigger || ctx.tier === 'low' || ctx.reduced) return;

  const figures = chapters.map((c) => c.querySelector('.phone-screen'));
  const imgs = figures.map((f) => f && f.querySelector('img'));
  if (figures.some((f) => !f)) return;

  const plan = storyPlan(n);
  // reveal.js прячет [data-reveal] до пересечения — здесь показом заведует таймлайн
  // (а в статичной раскладке .in — ровно конечное состояние показа)
  root.querySelectorAll('[data-reveal],[data-split]').forEach((el) => el.classList.add('in'));

  const copies = chapters.map((c) => c.querySelector('.chapter-copy'));
  root.style.setProperty('--copy-rise', COPY_RISE + 'px');

  /* Единственное место в модуле, где читается layout. Резерв над корпусом (--copy-h) —
     это фактическая высота САМОЙ ВЫСОКОЙ главы: число строк в lead зависит от ширины окна,
     поэтому константа резерва промахивается на узких и невысоких экранах (360×780 — бюджетный
     Android, 375×667 — iPhone SE: текст главы 02 садился на верхний край корпуса).
     Вызывается на refreshInit, ДО того как ScrollTrigger меряет сцену, и на смену шрифта;
     в onUpdate (то есть в прокрутке) чтения геометрии нет.
     Возвращает воздух между нижней точкой текста (с въездом lead) и верхом корпуса — по нему
     старт сцены решает, помещается ли она (sceneFits). Воздух берётся из раскладки (offset*),
     а не из прямоугольника на экране: на старте корпус уже повёрнут и уменьшен твином подхода.
     Для десктопа число смысла не имеет (текст слева от корпуса) — там его никто не спрашивает. */
  let devW = Infinity;                                 // ширина корпуса из того же замера
  let pinPx = 0;                                       // длина закрепления: сколько прокрутки сцена стоит (sticky)
  function measureCopy() {
    if (!root.classList.contains('is-live')) return null;
    let h = 0, bottom = 0;
    for (const c of copies) {
      if (!c) continue;
      h = Math.max(h, c.getBoundingClientRect().height);
      bottom = Math.max(bottom, c.offsetTop + c.offsetHeight);   // .chapter и .story-chapters — inset:0 в сцене
    }
    if (h > 0) root.style.setProperty('--copy-h', Math.ceil(h) + 'px');
    const air = device.offsetTop - bottom - COPY_RISE;          // после записи --copy-h: корпус уже пересчитан
    devW = device.offsetWidth;
    // длину пина задаёт CSS (высота секции = экран × (1 + --pin-k)), таймлайн берёт её отсюда же:
    // сколько секция выше сцены — ровно столько sticky держит сцену на месте
    pinPx = Math.max(1, root.offsetHeight - stage.offsetHeight);
    return air;
  }
  ScrollTrigger.addEventListener('refreshInit', measureCopy);

  /* Экраны глав 2–4 не качаем заранее: снимаем адрес с <img> (он остаётся в разметке —
     значит, без JS картинка на месте) и возвращаем по прогрессу. Уже загруженную или
     загружающуюся картинку не трогаем — прерывать закачку незачем. */
  const parked = imgs.map((im, i) => {
    if (!im || i === 0 || im.complete || im.currentSrc) return null;
    const keep = { src: im.getAttribute('src'), srcset: im.getAttribute('srcset') };
    im.removeAttribute('srcset');
    im.removeAttribute('src');
    return keep;
  });
  let armed = 1;
  function arm(count) {
    if (count <= armed) return;
    for (let i = armed; i < count; i++) {
      const keep = parked[i];
      if (!keep) continue;
      if (keep.srcset) imgs[i].setAttribute('srcset', keep.srcset);
      imgs[i].setAttribute('src', keep.src);
      parked[i] = null;
    }
    armed = count;
  }

  /* Кино-режим ⇄ статичная раскладка. Обе операции идемпотентны: matchMedia зовёт сцену заново
     при каждой смене условий (desk ⇄ mob, поворот), и корпус не должен при этом метаться. */
  function goLive() {
    if (root.classList.contains('is-live')) return;
    root.classList.add('is-live');
    figures.forEach((f) => device.appendChild(f));
  }
  function goStatic() {
    // экраны — обратно в свои главы, снятые адреса — на место: в столбике картинки нужны все
    figures.forEach((f, i) => {
      if (f.parentElement !== chapters[i]) chapters[i].appendChild(f);
      const keep = parked[i];
      if (!keep) return;
      if (keep.srcset) imgs[i].setAttribute('srcset', keep.srcset);
      imgs[i].setAttribute('src', keep.src);
      parked[i] = null;
    });
    root.classList.remove('is-live');
  }

  const glare = root.querySelector('.dev-glare');
  const atmo = root.querySelector('.story-atmo');
  const glows = [...root.querySelectorAll('.sa-g0,.sa-g1')];
  const fills = [...root.querySelectorAll('.story-hud b')];
  const nums = [...root.querySelectorAll('.story-hud span')];
  const chipsOf = chapters.map((c) => [...c.querySelectorAll('.chip')]);
  const ring = root.querySelector('.tap-ring');
  const dot = root.querySelector('.tap-dot');

  /* Кино-режим удлиняет секцию на длину пина (+1–2 тыс. px). Если это происходит, когда секция
     целиком ВЫШЕ окна (глубокая ссылка /#pricing, перезагрузка или «Назад» ниже истории), всё,
     на что смотрит читатель, уехало бы вниз на эту величину. Запоминаем следующую секцию до
     переключения и возвращаем её на место после (ревью T6 r4, Important 1). Одноразовый путь
     на старте: два чтения layout, не в прокрутке. */
  function holdBelow() {
    if (root.getBoundingClientRect().bottom > 0) return null;   // рост ниже читателя ничего не двигает
    const next = root.nextElementSibling;
    return next ? { next, top: next.getBoundingClientRect().top } : null;
  }
  function restoreBelow(held) {
    if (!held) return;
    ScrollTrigger.refresh();
    const d = held.next.getBoundingClientRect().top - held.top;
    if (Math.abs(d) < 1) return;
    const to = Math.max(0, window.scrollY + d);
    if (ctx.lenis) ctx.lenis.scrollTo(to, { immediate: true, force: true });
    else window.scrollTo({ top: to, behavior: 'instant' });
  }

  const mm = gsap.matchMedia();
  let pointer = null;
  // один раз перешла на шаги — так и остаётся (matchMedia собирает сцену заново при повороте);
  // ?story=steps включает режим сразу, ?story=scrub запрещает его — для проверки и замеров
  const qa = new URLSearchParams(window.location.search).get('story');
  let stepped = qa === 'steps';
  const held = holdBelow();

  // у matchMedia обязан быть вариант, который совпадает всегда, иначе на телефоне
  // (desk/fine оба false) GSAP просто не вызовет функцию и сцена не соберётся
  mm.add({ desk: '(min-width: 900px)', mob: '(max-width: 899px)', fine: '(hover: hover) and (pointer: fine)', flat: FLAT_Q }, (mctx) => {
    const { desk, fine, flat } = mctx.conditions;
    if (flat) { goStatic(); return; }
    goLive();
    root.style.setProperty('--pin-k', String(Math.round(plan.total * (desk ? STORY_UNIT.desk : STORY_UNIT.mob)) / 100));
    const air = measureCopy();
    if (!desk && !sceneFits(air, devW)) { goStatic(); return; }   // замер до первого refresh; корпус на полу — столбик (FIT_AIR)
    const tilt = desk ? TILTS : TILTS.map(() => [0, 0]);
    const first = {
      eyebrow: chapters[0].querySelector('.eyebrow'),
      lead: chapters[0].querySelector('.lead'),
      words: [...chapters[0].querySelectorAll('h2 .w>span')],
    };

    gsap.set(device, { transformPerspective: 1400, transformOrigin: '50% 58%' });
    gsap.set(figures, { opacity: (i) => (i ? 0 : 1) });
    gsap.set(imgs, { xPercent: (i) => (i ? 7 : 0), scale: (i) => (i ? 1.06 : 1) });
    gsap.set(copies, { opacity: (i) => (i ? 0 : 1) });
    if (desk) gsap.set(copies, { yPercent: -50 });
    gsap.set(nums, { opacity: 0.38 });
    gsap.set([ring, dot].filter(Boolean), { opacity: 0 });
    chapters.forEach((ch, i) => {
      gsap.set(ch.querySelectorAll('.eyebrow,.lead'), { opacity: 0, y: COPY_RISE });
      gsap.set(ch.querySelectorAll('h2 .w>span'), { yPercent: 115 });
      gsap.set(chipsOf[i], { opacity: 0, scale: 0.82, y: 14, x: (k) => (k % 2 ? -34 : 34) });
    });

    /* Регулятор сцены: кадры берутся у тикера GSAP (он и так идёт, своего rAF нет) и только пока
       идёт подход или сцена закреплена. Свой экземпляр: у поля частиц — свой счёт уровней.
       Объявлен ДО таймлайна: onToggle может сработать уже при создании триггера (перезагрузка
       посреди сцены), и let ниже не должен быть в TDZ. tl используется только в вызовах. */
    const rests = restTimes(plan, chipsOf.map((c) => c.length), chapters.map((c) => c.querySelectorAll('h2 .w').length));
    const gov = new FrameGovernor({ limitMs: ctx.governor ? ctx.governor.limitMs : 24, holdMs: STEP_HOLD_MS, alpha: STEP_ALPHA });
    let sampling = false, stepper = null, stepTween = null, shown = -1, tl = null, leadOn = false, sceneOn = false, offAt = -1e9;
    const sample = (time, dt) => { if (gov.sample(dt) !== 'none' && qa !== 'scrub') goStepwise(); };
    function sampleWhile(on) {
      if (on && qa !== 'scrub' && ctx.governor && ctx.governor.level > 0) { goStepwise(); return; }
      if (on === sampling || (on && stepper)) return;
      sampling = on;
      // подход → пин: на стыке оба триггера на миг неактивны — счёт не сбрасываем, если пауза < 1 с
      if (on) { if (performance.now() - offAt > 1000) gov.reset(); gsap.ticker.add(sample); }
      else { offAt = performance.now(); gsap.ticker.remove(sample); }
    }
    const toggled = () => sampleWhile(leadOn || sceneOn);
    function stepTo(i) {
      if (i === shown) return;
      shown = i;
      // следующая глава пришла, пока предыдущий шаг ещё ехал (быстрая прокрутка): монтажная склейка
      // одним кадром — иначе шаги шли бы внахлёст непрерывно и стоили столько же, сколько скраб
      if (stepTween && stepTween.isActive()) { stepTween.kill(); stepTween = null; tl.time(rests[i]); return; }
      if (stepTween) stepTween.kill();
      stepTween = tl.tweenTo(rests[i], { duration: STEP_S, ease: 'power2.inOut' });
    }
    function goStepwise() {
      if (stepper) return;
      if (!tl) { stepped = true; return; }          // подход ещё строится: переход сделает `if (stepped)` ниже
      sampleWhile(false);
      stepped = true;
      root.dataset.mode = 'steps';                  // видно в разметке: сцена на шагах (проверка, check.mjs)
      const hdr = document.getElementById('hdr');   // класс на шапке, не на <html>: пересчёт стилей только её
      if (hdr) hdr.classList.add('fx-lite');       // CSS: без размытия под шапкой; 3D корпуса снимает data-mode
      const st = tl.scrollTrigger;
      const t = st ? st.progress * plan.total : 0;
      if (st) st.kill(false, true);                 // скраб снят, таймлайн (и его состояние) остаётся
      tl.pause();
      stepper = ScrollTrigger.create({
        trigger: root, start: 'top top', end: () => '+=' + pinPx,
        onUpdate: (self) => { const u = self.progress * plan.total; arm(armedCount(u, n)); stepTo(chapterAt(plan, u)); },
      });
      stepTo(chapterAt(plan, t));
    }
    /* ПОДХОД: пока секция въезжает снизу (ровно один экран прокрутки), телефон
       поднимается в кадр, свет разгорается, приходит текст первой главы. Без этого
       на подъезде к закреплённой сцене был бы пустой тёмный экран. */
    const lead = gsap.timeline({
      defaults: { ease: 'none' },
      scrollTrigger: { trigger: root, start: 'top bottom', end: 'top top', scrub: 0.5, onToggle: (self) => { leadOn = self.isActive; toggled(); } },
    });
    lead.fromTo(device,
      { yPercent: 26, scale: 0.86, opacity: 0, rotationY: tilt[0][0] - (desk ? 10 : 0), rotationX: tilt[0][1] + (desk ? 10 : 6) },
      { yPercent: 0, scale: 1, opacity: 1, rotationY: tilt[0][0], rotationX: tilt[0][1], ease: 'power2.out', duration: 1 }, 0)
      .fromTo(atmo, { opacity: 0 }, { opacity: 1, ease: 'power1.out', duration: 0.75 }, 0.05)
      .fromTo(first.eyebrow, { opacity: 0, y: COPY_RISE }, { opacity: 1, y: 0, ease: 'power2.out', duration: 0.3 }, 0.3)
      .fromTo(first.words, { yPercent: 115 }, { yPercent: 0, ease: 'power3.out', duration: 0.42, stagger: 0.04 }, 0.36)
      .fromTo(first.lead, { opacity: 0, y: COPY_RISE }, { opacity: 1, y: 0, ease: 'power2.out', duration: 0.34 }, 0.52);

    /* СЦЕНА: plan.total единиц таймлайна. На desktop 1 единица = 1 экран прокрутки; на телефоне —
       0.8 экрана (STORY_UNIT, через --pin-k). Закрепляет её CSS (position:sticky на .story-stage),
       а не pin ScrollTrigger-а: переключение relative ⇄ fixed Chrome писал как layout-shift ≈ 1.0 на
       входе и на выходе (CLS 1–3 за прокрутку), а anticipatePin дёргал сцену на 6–49 px (линза
       перф-инженера, I-1). Sticky едет на компоновщике, не сдвигает раскладку и не добавляет
       pin-spacer в каждый refresh. Твины здесь не зависят от раскладки (проценты, px-константы),
       поэтому invalidateOnRefresh не нужен: он перечитывал ~100 стартовых значений на каждом refresh. */
    tl = gsap.timeline({
      defaults: { ease: 'none' },
      scrollTrigger: {
        trigger: root,
        start: 'top top',
        end: () => '+=' + pinPx,
        scrub: desk ? 0.55 : 0.4,
        onUpdate: (self) => arm(armedCount(self.progress * plan.total, n)),
        onToggle: (self) => { sceneOn = self.isActive; toggled(); },
      },
    });
    tl.fromTo(glare, { xPercent: -130 }, { xPercent: 130, ease: 'power1.inOut', duration: 0.8, immediateRender: false }, 0.1);

    chapters.forEach((ch, i) => {
      const copy = copies[i];
      const eyebrow = ch.querySelector('.eyebrow');
      const lede = ch.querySelector('.lead');
      const words = [...ch.querySelectorAll('h2 .w>span')];
      const chips = chipsOf[i];
      const cue = chapterCues(plan, i);             // расписание главы — из seam-а, не из литералов
      const s = cue.in;                             // глава въезжает чуть раньше своей границы
      const o = cue.out;                            // и уходит ДО въезда следующей: тексты не накладываются
      const e = cue.end;                            // момент, когда главной становится следующая

      if (i > 0) {
        tl.fromTo(copy, { opacity: 0 }, { opacity: 1, duration: 0.12, immediateRender: false }, s)
          .fromTo(eyebrow, { opacity: 0, y: COPY_RISE }, { opacity: 1, y: 0, ease: 'power2.out', duration: COPY_IN.eyebrow, immediateRender: false }, s)
          .fromTo(words, { yPercent: 115 }, { yPercent: 0, ease: 'power3.out', duration: COPY_IN.words, stagger: COPY_IN.stagger, immediateRender: false }, s + COPY_IN.wordsAt)
          .fromTo(lede, { opacity: 0, y: COPY_RISE }, { opacity: 1, y: 0, ease: 'power2.out', duration: COPY_IN.lead, immediateRender: false }, s + COPY_IN.leadAt);
      }
      const chip = chipCues(plan, i, chips.length);   // ритм чипов — тоже из seam-а
      if (chips.length) {
        tl.fromTo(chips, { opacity: 0, scale: 0.82, y: 14, x: (k) => (k % 2 ? -34 : 34) },
          { opacity: 1, scale: 1, y: 0, x: 0, ease: 'back.out(1.7)', duration: CHIP.dur, stagger: CHIP.stagger, immediateRender: false },
          chip.in);
      }
      if (i < n - 1) {
        tl.to(copy, { opacity: 0, y: desk ? -30 : -22, ease: 'power2.in', duration: CUE_OUT }, o)
          .to(chips, { opacity: 0, scale: 0.9, y: -16, ease: 'power2.in', duration: CHIP.outDur, stagger: CHIP.outStagger }, chip.out);
      }

      if (i > 0) {
        const w = cue.swap;                         // смена экрана в корпусе
        tl.fromTo(figures[i], { opacity: 0 }, { opacity: 1, ease: 'power2.inOut', duration: 0.44, immediateRender: false }, w)
          .fromTo(imgs[i], { xPercent: 7, scale: 1.06 }, { xPercent: 0, scale: 1, ease: 'power2.out', duration: 0.64, immediateRender: false }, w)
          .to(figures[i - 1], { opacity: 0, ease: 'power2.inOut', duration: 0.42 }, w + 0.02)
          .to(imgs[i - 1], { xPercent: -5, scale: 1.04, ease: 'power2.inOut', duration: 0.58 }, w)
          .fromTo(glare, { xPercent: -130 }, { xPercent: 130, ease: 'power1.inOut', duration: 0.66, immediateRender: false }, w - 0.02);
        if (desk) tl.to(device, { rotationY: tilt[i][0], rotationX: tilt[i][1], ease: 'power2.inOut', duration: 0.75 }, w - 0.08);
        tl.to(device, { yPercent: i % 2 ? DEV_FLOAT : 0, ease: 'power2.inOut', duration: 0.75 }, w - 0.08);
      }

      // индикатор: полоса главы наливается, пока глава на экране
      const from = i === 0 ? 0 : s;
      const to = i === n - 1 ? plan.total : e;
      tl.fromTo(fills[i], { scaleX: 0 }, { scaleX: 1, duration: to - from, immediateRender: i === 0 }, from);
      if (nums[i]) {
        tl.to(nums[i], { opacity: 1, duration: 0.25, immediateRender: false }, i === 0 ? 0 : s);
        if (i < n - 1) tl.to(nums[i], { opacity: 0.38, duration: 0.25 }, e);
      }
    });

    /* «одно касание»: на экране предложений подсвечивается карточка и по ней стучат */
    if (ring && dot) {
      const t = chapterCues(plan, 1).b;             // граница главы 02 — экран предложений
      // уложено в новую главу (уход 0.64 → смена экрана 0.78): кольцо гаснет до того, как экран сменится
      tl.fromTo(ring, { opacity: 0, scale: 1.04 }, { opacity: 1, scale: 1, ease: 'power2.out', duration: 0.22, immediateRender: false }, t + 0.1)
        .fromTo(dot, { opacity: 0, scale: 0.7 }, { opacity: 1, scale: 1, ease: 'power2.out', duration: 0.1, immediateRender: false }, t + 0.34)
        .to(ring, { scale: 0.985, ease: 'power2.out', duration: 0.08 }, t + 0.4)
        .to(ring, { scale: 1, ease: 'back.out(3)', duration: 0.18 }, t + 0.48)
        .to(dot, { opacity: 0, scale: 2.5, ease: 'power1.out', duration: 0.3 }, t + 0.44)
        .to(ring, { opacity: 0, duration: 0.2 }, t + 0.58);
    }

    if (stepped) goStepwise();

    /* параллакс света за указателем — только мышь, только пока сцена на экране */
    if (fine && desk && glows.length) {
      const x = gsap.quickTo(glows, 'x', { duration: 0.9, ease: 'power3' });
      const y = gsap.quickTo(glows, 'y', { duration: 0.9, ease: 'power3' });
      const move = (ev) => { x((ev.clientX / window.innerWidth - 0.5) * 26); y((ev.clientY / window.innerHeight - 0.5) * 18); };
      pointer = { move, on: false };
      const io = new IntersectionObserver((entries) => {
        for (const en of entries) {
          if (en.isIntersecting === pointer.on) continue;
          pointer.on = en.isIntersecting;
          if (en.isIntersecting) window.addEventListener('pointermove', move, { passive: true });
          else { window.removeEventListener('pointermove', move); x(0); y(0); }
        }
      }, { rootMargin: '0px' });
      io.observe(stage);
      pointer.io = io;
    }

    return () => {
      if (pointer) { window.removeEventListener('pointermove', pointer.move); pointer.io.disconnect(); pointer = null; }
      sampleWhile(false);
      if (stepTween) stepTween.kill();
      if (stepper) { stepper.kill(); stepper = null; }
      delete root.dataset.mode;
      [lead, tl].forEach((t) => { if (t.scrollTrigger) t.scrollTrigger.kill(); t.kill(); });
    };
  });

  restoreBelow(held);
  // Только если шрифты ещё грузятся: к этому моменту они почти всегда готовы, и безусловный второй
  // refresh стоил 417 мс длинной задачи на телефоне при 6× CPU (линза перф-инженера, I-2).
  if (document.fonts && document.fonts.status === 'loading') document.fonts.ready.then(() => ScrollTrigger.refresh());

  return {
    destroy() {
      mm.revert();
      ScrollTrigger.removeEventListener('refreshInit', measureCopy);
      root.style.removeProperty('--copy-h');
      root.style.removeProperty('--copy-rise');
      root.style.removeProperty('--pin-k');
      /* .in НЕ снимаем сознательно: reveal.js делает io.unobserve сразу после показа, так что
         снятый класс уже некому вернуть — текст остался бы прозрачным (.js [data-reveal]).
         Лишний .in безвреден: в статичной раскладке это ровно конечное состояние показа. */
      // вернуть экраны в свои главы и снятые адреса: без этого после destroy секция осталась бы
      // вовсе без картинок (корпус скрыт вместе с .is-live, а src глав 2–4 сняты)
      goStatic();
    },
  };
}
