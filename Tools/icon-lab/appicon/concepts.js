// App-icon concept lab. Each concept is a pure function of a palette and
// returns SVG markup on a 1024x1024 grid (background is drawn by build.js).
//
// Grid rules borrowed from ../STYLE.md and adapted for an app icon:
//   - live area 1024x1024, ink kept inside ~[150..875] so an Android circular
//     crop (66% diameter) never bites the mark;
//   - one accent element per icon, everything else is `mark`;
//   - judged at 60px, not at 1024.

// ---------- geometry helpers ----------

// Rounded rect with a soft tail hanging off the bottom-left corner.
// This is the app's own bubble language (see glyphs/nav_chats_outline.svg).
function bubble(x, y, w, h, r, t) {
  return [
    `M ${x + r} ${y}`,
    `L ${x + w - r} ${y}`,
    `A ${r} ${r} 0 0 1 ${x + w} ${y + r}`,
    `L ${x + w} ${y + h - r}`,
    `A ${r} ${r} 0 0 1 ${x + w - r} ${y + h}`,
    `L ${x + r * 1.05} ${y + h}`,
    `C ${x + r * 0.52} ${y + h + t * 0.34} ${x + r * 0.28} ${y + h + t * 0.70} ${x - t * 0.10} ${y + h + t}`,
    `C ${x + r * 0.18} ${y + h + t * 0.50} ${x} ${y + h + t * 0.10} ${x} ${y + h - r * 0.72}`,
    `L ${x} ${y + r}`,
    `A ${r} ${r} 0 0 1 ${x + r} ${y}`,
    'Z',
  ].join(' ');
}

// Same bubble mirrored about its own vertical axis, so the tail hangs off the
// bottom-RIGHT — an outgoing message. Arc sweep flags flip with the mirror.
function bubbleR(x, y, w, h, r, t) {
  const X = (v) => 2 * x + w - v;
  return [
    `M ${X(x + r)} ${y}`,
    `L ${X(x + w - r)} ${y}`,
    `A ${r} ${r} 0 0 0 ${X(x + w)} ${y + r}`,
    `L ${X(x + w)} ${y + h - r}`,
    `A ${r} ${r} 0 0 0 ${X(x + w - r)} ${y + h}`,
    `L ${X(x + r * 1.05)} ${y + h}`,
    `C ${X(x + r * 0.52)} ${y + h + t * 0.34} ${X(x + r * 0.28)} ${y + h + t * 0.70} ${X(x - t * 0.10)} ${y + h + t}`,
    `C ${X(x + r * 0.18)} ${y + h + t * 0.50} ${X(x)} ${y + h + t * 0.10} ${X(x)} ${y + h - r * 0.72}`,
    `L ${X(x)} ${y + r}`,
    `A ${r} ${r} 0 0 0 ${X(x + r)} ${y}`,
    'Z',
  ].join(' ');
}

// Plain rounded rect (bubble without the tail).
function rrect(x, y, w, h, r) {
  return [
    `M ${x + r} ${y}`,
    `L ${x + w - r} ${y}`,
    `A ${r} ${r} 0 0 1 ${x + w} ${y + r}`,
    `L ${x + w} ${y + h - r}`,
    `A ${r} ${r} 0 0 1 ${x + w - r} ${y + h}`,
    `L ${x + r} ${y + h}`,
    `A ${r} ${r} 0 0 1 ${x} ${y + h - r}`,
    `L ${x} ${y + r}`,
    `A ${r} ${r} 0 0 1 ${x + r} ${y}`,
    'Z',
  ].join(' ');
}

// Four-point AI sparkle with concave sides.
function sparkle(cx, cy, R, waist = 0.26) {
  const k = R * waist;
  return [
    `M ${cx} ${cy - R}`,
    `C ${cx} ${cy - k} ${cx + k} ${cy} ${cx + R} ${cy}`,
    `C ${cx + k} ${cy} ${cx} ${cy + k} ${cx} ${cy + R}`,
    `C ${cx} ${cy + k} ${cx - k} ${cy} ${cx - R} ${cy}`,
    `C ${cx - k} ${cy} ${cx} ${cy - k} ${cx} ${cy - R}`,
    'Z',
  ].join(' ');
}

// Arc of a circle, as a stroked path. Angles in degrees, y-down, clockwise.
function arc(cx, cy, R, a0, a1) {
  const rad = (d) => (d * Math.PI) / 180;
  const p0 = [cx + R * Math.cos(rad(a0)), cy + R * Math.sin(rad(a0))];
  const p1 = [cx + R * Math.cos(rad(a1)), cy + R * Math.sin(rad(a1))];
  const large = Math.abs(a1 - a0) > 180 ? 1 : 0;
  const sweep = a1 > a0 ? 1 : 0;
  return `M ${p0[0].toFixed(1)} ${p0[1].toFixed(1)} A ${R} ${R} 0 ${large} ${sweep} ${p1[0].toFixed(1)} ${p1[1].toFixed(1)}`;
}

const check = (pts) => `M ${pts[0]} ${pts[1]} L ${pts[2]} ${pts[3]} L ${pts[4]} ${pts[5]}`;

let uid = 0;
const nid = (s) => `${s}${++uid}`;

// ---------- concepts ----------

const concepts = [
  // ============ A. «Выбор ответа» — the name, made visible ============
  {
    id: 'A1',
    dir: 'A',
    ru: 'Два ответа, один выбран',
    note: 'Прямая метафора названия: слева пришёл вопрос, справа — ответ, который вы утвердили. Силуэт из двух пузырей сразу читается как переписка.',
    draw: (P) => {
      const m = nid('m');
      const back = bubble(150, 186, 424, 300, 104, 96);
      const front = bubbleR(452, 424, 440, 330, 112, 108);
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <path d="${front}" fill="#000" stroke="#000" stroke-width="76" stroke-linejoin="round"/>
  </mask>
  <path d="${back}" fill="${P.mark}" opacity="0.38" mask="url(#${m})"/>
  <path d="${front}" fill="${P.accent}"/>
  <path d="${check([546, 588, 634, 676, 800, 492])}" fill="none" stroke="${P.onAccent}" stroke-width="62"
        stroke-linecap="round" stroke-linejoin="round"/>`;
    },
  },
  {
    id: 'A2',
    dir: 'A',
    ru: 'Пузырь-галочка',
    note: 'Самый громкий силуэт из всех: одна форма, одна идея — «ответ выбран». Читается на 40px.',
    draw: (P) => {
      const m = nid('m');
      const g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(168, 176, 688, 556, 176, 142)}" fill="#fff"/>
    <path d="${check([378, 452, 470, 544, 668, 336])}" fill="none" stroke="#000" stroke-width="96"
          stroke-linecap="round" stroke-linejoin="round"/>
  </mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>`;
    },
  },
  {
    id: 'A3',
    dir: 'A',
    ru: 'Пузырь-галочка, светлый',
    note: 'Тот же знак, но акцент несёт галочка, а не заливка. Спокойнее и «дороже», хотя на домашнем экране тише.',
    draw: (P) => `
  <path d="${bubble(168, 176, 688, 556, 176, 142)}" fill="${P.mark}"/>
  <path d="${check([378, 452, 470, 544, 668, 336])}" fill="none" stroke="${P.accent}" stroke-width="96"
        stroke-linecap="round" stroke-linejoin="round"/>`,
  },

  // ============ B. Бот-маскот — категория читается мгновенно ============
  {
    id: 'B1',
    dir: 'B',
    ru: 'Бот-пузырь',
    note: 'Пузырь и лицо — одна форма. Продолжает маскота с лендинга, но упрощён до силуэта. Самый «дружелюбный» вариант.',
    draw: (P) => {
      const m = nid('m');
      return `
  <circle cx="512" cy="152" r="52" fill="${P.accent}"/>
  <rect x="486" y="192" width="52" height="104" rx="26" fill="${P.mark}"/>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(172, 276, 680, 484, 168, 126)}" fill="#fff"/>
    <rect x="330" y="424" width="96" height="150" rx="48" fill="#000"/>
    <rect x="598" y="424" width="96" height="150" rx="48" fill="#000"/>
    <path d="M 414 636 Q 512 712 610 636" fill="none" stroke="#000" stroke-width="46" stroke-linecap="round"/>
  </mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>`;
    },
  },
  {
    id: 'B2',
    dir: 'B',
    ru: 'Бот-глиф',
    note: 'Ровно тот бот, что стоит во вкладке «Боты» — иконка приложения и таб-бар говорят одним языком. Линейный, самый «системный».',
    draw: (P) => `
  <circle cx="512" cy="190" r="68" fill="${P.accent}"/>
  <path d="M 512 258 L 512 352" stroke="${P.mark}" stroke-width="86" stroke-linecap="round"/>
  <path d="${rrect(172, 352, 680, 454, 116)}" fill="none" stroke="${P.mark}" stroke-width="88" stroke-linejoin="round"/>
  <circle cx="382" cy="580" r="70" fill="${P.mark}"/>
  <circle cx="642" cy="580" r="70" fill="${P.mark}"/>`,
  },

  // ============ C. ИИ-искра — конвенция категории 2026 ============
  {
    id: 'C1',
    dir: 'C',
    ru: 'Пузырь + искра',
    note: 'Самый «безопасный» вариант: в 2026 искра = ИИ, пузырь = переписка. Мгновенно понятно, но и наименее уникально.',
    draw: (P) => `
  <path d="${bubble(168, 200, 688, 548, 176, 140)}" fill="${P.mark}"/>
  <path d="${sparkle(486, 452, 192)}" fill="${P.accent}"/>
  <path d="${sparkle(690, 300, 72)}" fill="${P.accent}"/>`,
  },
  {
    id: 'C3',
    dir: 'C',
    ru: 'Искра в цвете',
    note: 'Та же идея, вывернутая: цветной пузырь, искра — прорезь до фона. Ярче на домашнем экране.',
    draw: (P) => {
      const m = nid('m');
      const g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(168, 200, 688, 548, 176, 140)}" fill="#fff"/>
    <path d="${sparkle(486, 452, 198)}" fill="#000"/>
    <path d="${sparkle(692, 302, 74)}" fill="#000"/>
  </mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>`;
    },
  },
  {
    id: 'C2',
    dir: 'C',
    ru: 'Чистая искра',
    note: 'Ставка на «ИИ», а не на «мессенджер». Отлично масштабируется, но не говорит, что это про переписку.',
    draw: (P) => {
      const g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.15" y1="0" x2="0.85" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <path d="${sparkle(470, 546, 322)}" fill="url(#${g})"/>
  <path d="${sparkle(772, 268, 122)}" fill="${P.mark}"/>`;
    },
  },

  // ============ D. Монограмма — самый «бренд» ============
  {
    id: 'D1',
    dir: 'D',
    ru: '«C» с точкой',
    note: 'Буква C от Choose, она же незакрытый пузырь. Циановая точка — та же, что в логотипе на сайте. Самый долгоиграющий знак.',
    draw: (P) => `
  <path d="${arc(512, 500, 272, 52, 308)}" fill="none" stroke="${P.mark}" stroke-width="140" stroke-linecap="round"/>
  <circle cx="512" cy="500" r="92" fill="${P.accent}"/>`,
  },
  {
    id: 'D2',
    dir: 'D',
    ru: 'Эхо',
    note: 'Две вложенные дуги — вопрос и отклик. Абстрактный «взрослый» знак: отлично масштабируется, но продукт из него не считывается.',
    draw: (P) => `
  <path d="${arc(512, 512, 306, 54, 306)}" fill="none" stroke="${P.mark}" stroke-width="116" stroke-linecap="round"/>
  <path d="${arc(512, 512, 134, 54, 306)}" fill="none" stroke="${P.accent}" stroke-width="112" stroke-linecap="round"/>`,
  },
];

module.exports = { concepts, bubble, bubbleR, rrect, sparkle, arc, check };
