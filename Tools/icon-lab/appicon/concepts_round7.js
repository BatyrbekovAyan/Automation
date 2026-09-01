// Round 7. Short-list was T1, T4, T12, S8 — and they differ ONLY in colour.
// The chassis is now locked: three full-width stripes, the middle one thicker,
// the middle one chosen, no marks of any kind. T2/T5..T11 (position plays,
// glow, geometry tricks) all dropped.
//
// So this round holds the chassis constant and varies the colour recipe:
// what the neighbours wear, how the chosen stripe is finished, how far the
// inversion tint goes, and — since we are converging — three proportion
// knobs that combine with any recipe.
const { concepts: prevRound6 } = require('./concepts_round6');
const { pill, nid } = require('./concepts_choice');
const { rrect } = require('./concepts');

const KEEP = ['T1', 'T4', 'T12', 'S8'];
const shortlist = KEEP.map((id) => ({ ...prevRound6.find((c) => c.id === id), dir: 'S' }));

// The locked chassis, one place: x 110, width 804; thin 142 @ 208/674, thick 184 @ 420.
const B = { x: 110, w: 804, top: [208, 142], mid: [420, 184], bot: [674, 142] };
const band = (yh, dx = 0, dw = 0) => pill(B.x + dx, yh[0], B.w + dw, yh[1]);

const grad = (P, id, x2 = 1, y2 = 0.4) => `
  <linearGradient id="${id}" x1="0" y1="0" x2="${x2}" y2="${y2}">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>`;

const fresh = [
  // ============ A. Во что одеты соседние ============
  {
    id: 'U1', dir: 'A', ru: 'Градиент на чернилах',
    note: 'T4 и T12 в одном знаке: соседние — приглушённые чернила, принятая несёт градиент. Самый фирменный рецепт из возможных.',
    draw: (P) => {
      const g = nid('g');
      return `${grad(P, g)}
  <path d="${band(B.top)}" fill="${P.accent}" opacity="0.30"/>
  <path d="${band(B.mid)}" fill="url(#${g})"/>
  <path d="${band(B.bot)}" fill="${P.accent}" opacity="0.30"/>`;
    },
  },
  {
    id: 'U2', dir: 'A', ru: 'Белая на градиентных',
    note: 'T1 и T4 в одном: соседние несут градиент вполсилы, принятая — чистая белая. Цвет в соседях, свет в выбранной.',
    draw: (P) => {
      const g = nid('g');
      return `${grad(P, g)}
  <path d="${band(B.top)}" fill="url(#${g})" opacity="0.40"/>
  <path d="${band(B.mid)}" fill="${P.mark}"/>
  <path d="${band(B.bot)}" fill="url(#${g})" opacity="0.40"/>`;
    },
  },
  {
    id: 'U3', dir: 'A', ru: 'Тон в тон',
    note: 'Ни одного белого пикселя: соседние — глухие чернила, принятая — светлый оттенок того же цвета. Самый монолитный.',
    draw: (P) => `
  <path d="${band(B.top)}" fill="${P.accent}" opacity="0.28"/>
  <path d="${band(B.mid)}" fill="${P.accentHi}"/>
  <path d="${band(B.bot)}" fill="${P.accent}" opacity="0.28"/>`,
  },
  {
    id: 'U4', dir: 'A', ru: 'Два оттенка',
    note: 'Соседние разного тона — верхняя светлее, нижняя глубже, между ними белая принятая. Едва заметная глубина вместо плоского повтора.',
    draw: (P) => `
  <path d="${band(B.top)}" fill="${P.accentHi}" opacity="0.42"/>
  <path d="${band(B.mid)}" fill="${P.mark}"/>
  <path d="${band(B.bot)}" fill="${P.accent}" opacity="0.55"/>`,
  },

  // ============ B. Отделка выбранной ============
  {
    id: 'U5', dir: 'B', ru: 'Цветное ребро',
    note: 'Белая принятая, из-под неё на пиксели выглядывает цветное ребро — как подсветка снизу. Один намёк цвета на весь знак.',
    draw: (P) => `
  <path d="${band(B.top)}" fill="${P.mark}" opacity="0.35"/>
  <path d="${pill(B.x, 438, B.w, 184)}" fill="${P.accent}"/>
  <path d="${pill(B.x, 416, B.w, 184)}" fill="${P.mark}"/>
  <path d="${band(B.bot)}" fill="${P.mark}" opacity="0.35"/>`,
  },
  {
    id: 'U6', dir: 'B', ru: 'Стекло',
    note: 'Принятая залита вертикальным градиентом от белого к светлому оттенку — стеклянная. Соседние — глухие чернила.',
    draw: (P) => {
      const g = nid('g');
      return `
  <linearGradient id="${g}" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="${P.mark}"/><stop offset="1" stop-color="${P.accentHi}"/>
  </linearGradient>
  <path d="${band(B.top)}" fill="${P.accent}" opacity="0.30"/>
  <path d="${band(B.mid)}" fill="url(#${g})"/>
  <path d="${band(B.bot)}" fill="${P.accent}" opacity="0.30"/>`;
    },
  },
  {
    id: 'U7', dir: 'B', ru: 'Неон',
    note: 'Принятая — цветная трубка с белой сердцевиной. На тёмных чернилах читается как неоновая вывеска.',
    draw: (P) => `
  <path d="${band(B.top)}" fill="${P.accent}" opacity="0.30"/>
  <path d="${band(B.mid)}" fill="${P.accent}"/>
  <path d="${pill(B.x + 30, 450, B.w - 60, 124)}" fill="${P.mark}"/>
  <path d="${band(B.bot)}" fill="${P.accent}" opacity="0.30"/>`,
  },

  // ============ C. Инверсия и тонировка ============
  {
    id: 'U8', dir: 'C', ru: 'Инверсия с глубиной',
    note: 'S8, где заливка — не плоская, а с затемнением к низу: цвет дышит, белая полоса выступает сильнее.',
    draw: (P) => {
      const m = nid('m'), g = nid('g'), v = nid('g');
      return `${grad(P, g, 1, 1)}
  <linearGradient id="${v}" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0.45" stop-color="#000000" stop-opacity="0"/><stop offset="1" stop-color="#000000" stop-opacity="0.30"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <path d="${pill(96, 186, 832, 132)}" fill="#000"/>
    <path d="${pill(96, 706, 832, 132)}" fill="#000"/>
    <path d="${rrect(60, 396, 904, 232, 116)}" fill="#000"/></mask>
  <g mask="url(#${m})">
    <rect width="1024" height="1024" fill="url(#${g})"/>
    <rect width="1024" height="1024" fill="url(#${v})"/>
  </g>
  <path d="${pill(96, 432, 832, 160)}" fill="${P.mark}"/>`;
    },
  },
  {
    id: 'U9', dir: 'C', ru: 'Тонированная инверсия',
    note: 'Компромисс между S8 и тёмным фоном: чернила лишь подкрашены цветом, соседние прорезаны до тёмного, принятая — яркая. Заметность S8 без цветной заливки.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <path d="${band(B.top, -14, 28)}" fill="#000"/>
    <path d="${rrect(76, 396, 872, 232, 116)}" fill="#000"/>
    <path d="${band(B.bot, -14, 28)}" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.accent}" opacity="0.20" mask="url(#${m})"/>
  <path d="${band(B.mid)}" fill="${P.accent}"/>`;
    },
  },

  // ============ D. Пропорции и углы ============
  {
    id: 'U10', dir: 'D', ru: 'Контраст толщин',
    note: 'Рецепт T1, но иерархия выкручена: соседние заметно тоньше, принятая заметно толще. Ручка, которую можно приложить к любому рецепту.',
    draw: (P) => `
  <path d="${pill(110, 228, 804, 116)}" fill="${P.accent}"/>
  <path d="${pill(110, 400, 804, 224)}" fill="${P.mark}"/>
  <path d="${pill(110, 688, 804, 116)}" fill="${P.accent}"/>`,
  },
  {
    id: 'U11', dir: 'D', ru: 'Шире в кадре',
    note: 'Рецепт T12 с полями вдвое меньше: полосы почти касаются краёв, знак крупнее в той же иконке. Вторая ручка.',
    draw: (P) => `
  <path d="${pill(80, 204, 864, 150)}" fill="${P.accent}" opacity="0.30"/>
  <path d="${pill(80, 414, 864, 196)}" fill="${P.accent}"/>
  <path d="${pill(80, 680, 864, 150)}" fill="${P.accent}" opacity="0.30"/>`,
  },
  {
    id: 'U12', dir: 'D', ru: 'Прямее углы',
    note: 'Рецепт T4 на полосах с умеренным скруглением вместо полного: меньше «таблетки», больше строки интерфейса. Третья ручка.',
    draw: (P) => {
      const g = nid('g');
      return `${grad(P, g)}
  <path d="${rrect(110, 208, 804, 142, 44)}" fill="${P.mark}" opacity="0.32"/>
  <path d="${rrect(110, 420, 804, 184, 52)}" fill="url(#${g})"/>
  <path d="${rrect(110, 674, 804, 142, 44)}" fill="${P.mark}" opacity="0.32"/>`;
    },
  },
];

module.exports = { concepts: [...shortlist, ...fresh] };
