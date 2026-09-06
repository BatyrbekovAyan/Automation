// Round 8. Brief: «похожие на U9, только выделенный вариант обведён белым,
// внутри тёмно-синий и другие цвета; сделай премиально».
//
// So the chassis is U9's — tinted ink ground, neighbours cut through to the
// dark, the chosen stripe seated in a dark window — and the chosen stripe now
// wears a WHITE RING. What varies: what sits inside the ring (deep navy, ink,
// gradient, night, gold, silver), how the ring is drawn (thin, hollow, glow,
// glass), and the surroundings (no window, outlined neighbours).
//
// «Премиально» is applied family-wide, not per variant: the flat 20% tint of
// U9 becomes a diagonal tinted gradient (light top-left, deeper bottom-right;
// stops 0.36→0.20 after the review panel measured the 0.10 end sinking the
// bottom-right corner below U9's own floor),
// and the ring is precise — 30 units, 3% of the icon — not a chunky border.
const { concepts: prevRound7 } = require('./concepts_round7');
const { pill, nid } = require('./concepts_choice');
const { rrect } = require('./concepts');

const shortlist = [{ ...prevRound7.find((c) => c.id === 'U9'), dir: 'S' }];

// Locked chassis (same as round 7).
const X = 110, W = 804;
const TOP = [208, 142], MID = [420, 184], BOT = [674, 142];
const band = (yh, dx = 0, dw = 0) => pill(X + dx, yh[0], W + dw, yh[1]);
// The dark window the chosen stripe sits in — grown so a 30-unit ring keeps a moat.
const WINDOW = rrect(X - 46, MID[0] - 40, W + 92, MID[1] + 80, (MID[1] + 80) / 2);
const RING = 30;

// Premium ground: a diagonal tinted gradient of the accent, cut through.
function ground(P, cuts) {
  const m = nid('m'), t = nid('g');
  return `
  <linearGradient id="${t}" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="${P.accent}" stop-opacity="0.36"/>
    <stop offset="1" stop-color="${P.accent}" stop-opacity="0.20"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    ${cuts.map((d) => `<path d="${d}" fill="#000"/>`).join('\n    ')}</mask>
  <rect width="1024" height="1024" fill="url(#${t})" mask="url(#${m})"/>`;
}
const CUTS = [band(TOP, -14, 28), WINDOW, band(BOT, -14, 28)];

// The ringed chosen stripe: fill inside, white ring on the edge.
const ringed = (P, fill, ring = RING) =>
  `<path d="${band(MID)}" fill="${fill}" stroke="${P.mark}" stroke-width="${ring}"/>`;

const fresh = [
  // ============ A. Что внутри контура ============
  {
    id: 'V1', dir: 'A', ru: 'Тёмно-синий',
    note: 'То, что вы описали: белый контур, внутри тёмно-синий. Выбранная — самая тёмная полоса знака, и именно поэтому читается как главная: свет идёт по её краю.',
    draw: (P) => `${ground(P, CUTS)}
  ${ringed(P, P.deep)}`,
  },
  {
    id: 'V2', dir: 'A', ru: 'Чернила',
    note: 'Белый контур, внутри — фирменный цвет в полную силу. Самый яркий из семейства, ближе всех к U9.',
    draw: (P) => `${ground(P, CUTS)}
  ${ringed(P, P.accent)}`,
  },
  {
    id: 'V3', dir: 'A', ru: 'Градиент',
    note: 'Внутри контура — градиент от светлого к глубокому. Единственный, где сам цвет живой.',
    draw: (P) => {
      const g = nid('g');
      return `${ground(P, CUTS)}
  <linearGradient id="${g}" x1="0" y1="0" x2="1" y2="0.5">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  ${ringed(P, `url(#${g})`)}`;
    },
  },
  {
    id: 'V4', dir: 'A', ru: 'Ночь',
    note: 'Внутри почти чёрный — темнее фона. Контур становится единственным светлым элементом знака: максимально строго.',
    draw: (P) => `${ground(P, CUTS)}
  ${ringed(P, P.bg[1])}`,
  },
  {
    id: 'V5', dir: 'A', ru: 'Золото',
    note: 'Тёмно-синий и золото — классический «дорогой» дуэт. Единственный знак семейства с тёплым цветом; он же самый заметный на любых обоях.',
    draw: (P) => {
      const g = nid('g');
      return `${ground(P, CUTS)}
  <linearGradient id="${g}" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#F3D680"/><stop offset="0.55" stop-color="#D9B052"/><stop offset="1" stop-color="#B58B2E"/>
  </linearGradient>
  ${ringed(P, `url(#${g})`)}`;
    },
  },
  {
    id: 'V6', dir: 'A', ru: 'Серебро',
    note: 'Внутри — холодный металл от белого к серо-голубому. Контур и заливка одного тона, знак читается как одна светлая полоса с объёмом.',
    draw: (P) => {
      const g = nid('g');
      return `${ground(P, CUTS)}
  <linearGradient id="${g}" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#C3CCDB"/>
  </linearGradient>
  ${ringed(P, `url(#${g})`)}`;
    },
  },

  // ============ B. Как нарисован контур ============
  {
    id: 'V7', dir: 'B', ru: 'Только контур',
    note: 'Внутри ничего — сквозь полосу виден тёмный колодец. Самый воздушный; на мелком размере превращается в светлую рамку на тёмном.',
    draw: (P) => `${ground(P, CUTS)}
  ${ringed(P, 'none', 34)}`,
  },
  {
    id: 'V8', dir: 'B', ru: 'Тонкий контур',
    note: 'Контур вдвое тоньше — 1,5% иконки. Самый ювелирный; проверка, выживает ли линия на 29px (в мелком ряду видно, что почти нет).',
    draw: (P) => `${ground(P, CUTS)}
  ${ringed(P, P.deep, 16)}`,
  },
  {
    id: 'V9', dir: 'B', ru: 'Контур со свечением',
    note: 'Вокруг белого контура — мягкий ореол. Свет не только на кромке, но и в колодце вокруг неё.',
    draw: (P) => `${ground(P, CUTS)}
  <path d="${pill(X - 24, MID[0] - 24, W + 48, MID[1] + 48)}" fill="${P.mark}" opacity="0.10"/>
  <path d="${pill(X - 11, MID[0] - 11, W + 22, MID[1] + 22)}" fill="${P.mark}" opacity="0.18"/>
  ${ringed(P, P.deep)}`,
  },
  {
    id: 'V10', dir: 'B', ru: 'Стекло',
    note: 'Тёмно-синий внутри, а по верхнему краю — блик. Полоса перестаёт быть плоской и становится стеклянной пластиной.',
    draw: (P) => {
      const g = nid('g');
      return `${ground(P, CUTS)}
  <linearGradient id="${g}" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="${P.mark}" stop-opacity="0.34"/><stop offset="1" stop-color="${P.mark}" stop-opacity="0"/>
  </linearGradient>
  ${ringed(P, P.deep)}
  <path d="${pill(X + 36, MID[0] + 20, W - 72, MID[1] * 0.46)}" fill="url(#${g})"/>`;
    },
  },

  // ============ C. Окружение ============
  {
    id: 'V11', dir: 'C', ru: 'Без колодца',
    note: 'Тёмный колодец вокруг выбранной убран — контур лежит прямо на тонированном фоне. Знак становится плотнее и проще.',
    draw: (P) => `${ground(P, [band(TOP, -14, 28), band(BOT, -14, 28)])}
  ${ringed(P, P.deep)}`,
  },
  {
    id: 'V12', dir: 'C', ru: 'Все контурные',
    note: 'Соседние не прорезаны, а обведены — тонко и вполсилы. Три контура, один из них яркий и с заливкой. Самый «графический» из всех.',
    draw: (P) => `${ground(P, [WINDOW])}
  <path d="${band(TOP)}" fill="none" stroke="${P.mark}" stroke-width="20" opacity="0.38"/>
  <path d="${band(BOT)}" fill="none" stroke="${P.mark}" stroke-width="20" opacity="0.38"/>
  ${ringed(P, P.deep)}`,
  },
];

module.exports = { concepts: [...shortlist, ...fresh] };
