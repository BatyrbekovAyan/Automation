// Section module: тарифы. Месяц/год-переключатель (сегмент с «бегунком»), докрутка цифр
// rAF-интерполяцией (formatKzt на каждом кадре; на время докрутки бокс держит ширину итога), лёгкий 3°-тилт карточек за курсором на pointer:fine. Никаких
// постоянных rAF-циклов здесь нет (тилт — по pointermove, докрутка — разовая на 0,6 с),
// так что паузить offscreen/document.hidden нечего.
import { formatKzt, yearlyMonthly, savingsPercent, PLANS } from './pricing-format.js';

const DURATION_MS = 600;
const TILT_DEG = 3;

function easeOutCubic(t) {
  return 1 - Math.pow(1 - t, 3);
}

function animateAmount(card, toValue, instant) {
  const digitsEl = card.querySelector('.pr-amt-digits');
  const srEl = card.querySelector('.pr-sr');
  const fromValue = card._prCurrent ?? toValue;
  card._prCurrent = toValue;
  const unit = card._prPeriod === 'year' ? 'в год' : 'в месяц';

  if (card._prRaf) cancelAnimationFrame(card._prRaf);

  if (instant || fromValue === toValue) {
    digitsEl.style.minWidth = '';
    digitsEl.textContent = formatKzt(toValue);
    srEl.textContent = `${formatKzt(toValue)} тенге ${unit}`;
    return;
  }

  // The box takes the FINAL number's width for the length of the count, so the ₸ steps once (to
  // where it will rest) instead of following every frame; at rest no width is reserved at all.
  digitsEl.textContent = formatKzt(toValue);
  const w = digitsEl.getBoundingClientRect().width;
  digitsEl.textContent = formatKzt(fromValue);
  digitsEl.style.minWidth = `${Math.ceil(w)}px`;
  const start = performance.now();
  const tick = (now) => {
    const t = Math.min(1, (now - start) / DURATION_MS);
    const eased = easeOutCubic(t);
    const value = Math.round(fromValue + (toValue - fromValue) * eased);
    digitsEl.textContent = formatKzt(value);
    if (t < 1 && !document.hidden) {
      card._prRaf = requestAnimationFrame(tick);
    } else {
      digitsEl.textContent = formatKzt(toValue);
      digitsEl.style.minWidth = '';
      srEl.textContent = `${formatKzt(toValue)} тенге ${unit}`;
      card._prRaf = null;
    }
  };
  card._prRaf = requestAnimationFrame(tick);
}

function applyPeriod(cards, period, instant) {
  for (const card of cards) {
    card._prPeriod = period;
    const month = +card.dataset.month;
    const year = +card.dataset.year;
    const perEl = card.querySelector('.pr-amt-per');
    const subEl = card.querySelector('.pr-amt-sub');
    if (period === 'year') {
      perEl.textContent = '/ год';
      // Текст всегда в потоке (не через [hidden], который снял бы min-height и
      // сдвинул фичи/CTA ниже) — высота строки зарезервирована в CSS всегда,
      // поэтому переключение периода не двигает ничего под ценой.
      subEl.textContent = `≈ ${formatKzt(yearlyMonthly(year))} ₸ в месяц`;
      animateAmount(card, year, instant);
    } else {
      perEl.textContent = '/ мес';
      subEl.textContent = '';
      animateAmount(card, month, instant);
    }
  }
}

function initToggle(root, cards, instantOnly) {
  const seg = root.querySelector('.pr-seg');
  if (!seg) return;
  const thumb = seg.querySelector('.pr-seg-th');
  const buttons = [...seg.querySelectorAll('button[data-period]')];

  // instant=true suppresses the CSS width/transform transition for the initial mount and
  // for font-load re-measurement — the CSS-only default (50% width, under «Месяц») is what
  // no-JS visitors see, and it must not visibly animate into place once JS attaches; a click
  // toggle (instant=false) keeps the intended sliding transition.
  function placeThumb(instant) {
    const active = buttons.find((b) => b.getAttribute('aria-checked') === 'true');
    if (!active) return;
    if (instant) thumb.style.transition = 'none';
    thumb.style.width = `${active.offsetWidth}px`;
    thumb.style.transform = `translateX(${active.offsetLeft - 4}px)`;
    if (instant) {
      // force layout before restoring the transition so it never applies to this change
      void thumb.offsetWidth;
      thumb.style.transition = '';
    }
  }

  placeThumb(true);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => placeThumb(true));
  window.addEventListener('resize', () => placeThumb(true), { passive: true });

  // A radiogroup is ONE tab stop: the checked radio takes Tab (roving tabindex) and the arrow keys
  // move the choice, as in a native radio group (WAI-ARIA radio pattern). Click works as before.
  function select(btn) {
    if (btn.getAttribute('aria-checked') === 'true') return;
    for (const b of buttons) { b.setAttribute('aria-checked', String(b === btn)); b.tabIndex = b === btn ? 0 : -1; }
    placeThumb(false);
    applyPeriod(cards, btn.dataset.period, instantOnly);
  }
  for (const b of buttons) b.tabIndex = b.getAttribute('aria-checked') === 'true' ? 0 : -1;
  for (const btn of buttons) {
    btn.addEventListener('click', () => select(btn));
    btn.addEventListener('keydown', (e) => {
      const i = buttons.indexOf(btn), n = buttons.length;
      const to = { ArrowRight: i + 1, ArrowDown: i + 1, ArrowLeft: i - 1, ArrowUp: i - 1, Home: 0, End: n - 1 }[e.key];
      if (to === undefined) return;
      e.preventDefault();
      const next = buttons[(to + n) % n];
      select(next);
      next.focus();
    });
  }
}

function initTilt(cards, reduced) {
  if (reduced || !matchMedia('(pointer: fine)').matches) return;
  for (const card of cards) {
    let raf = 0;
    card.addEventListener('pointermove', (e) => {
      const r = card.getBoundingClientRect();
      const nx = (e.clientX - r.left) / r.width - 0.5;
      const ny = (e.clientY - r.top) / r.height - 0.5;
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => {
        card.style.transition = 'transform .12s linear';
        card.style.transform = `perspective(900px) rotateX(${(-ny * TILT_DEG * 2).toFixed(2)}deg) rotateY(${(nx * TILT_DEG * 2).toFixed(2)}deg) translateY(-4px)`;
      });
    });
    card.addEventListener('pointerleave', () => {
      cancelAnimationFrame(raf);
      card.style.transition = 'transform .6s var(--ease-out)';
      card.style.transform = 'perspective(900px)';
    });
  }
}

function initSavingsBadge(root) {
  const tagEl = root.querySelector('.pr-seg-tag');
  if (!tagEl) return;
  // HTML ships a correct hand-typed "выгода до 17%" for no-JS visitors; re-derive it from
  // PLANS/savingsPercent here so a future catalog price change can't silently make the
  // hardcoded badge wrong without anything noticing (savingsPercent itself stays pinned by
  // pricing.test.mjs).
  const max = Math.max(...PLANS.map((p) => savingsPercent(p.month, p.year)));
  tagEl.textContent = `выгода до ${max}%`;
}

export function init(root, ctx) {
  const cards = [...root.querySelectorAll('.pr-card')];
  const instantOnly = ctx.tier === 'low' || ctx.reduced;

  // Начальное состояние уже верно в разметке (месячные цены) — фиксируем current, чтобы
  // первый клик по «Год» анимировал ОТ реального отображаемого значения.
  for (const card of cards) {
    card._prPeriod = 'month';
    card._prCurrent = +card.dataset.month;
  }

  initSavingsBadge(root);
  initToggle(root, cards, instantOnly);
  initTilt(cards, ctx.reduced);
}
