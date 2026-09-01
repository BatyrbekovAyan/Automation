// Round 5. Short-list was R4, R6, R8, R11 — all four are BANDS: full-width
// stripes of equal thickness. Every bubble, every ring-around-a-bubble, every
// list-with-plates dropped out. Text survived only inside a band (R4 in, the
// plain text lines R1/R2 out).
//
// So the constant is now the stripe. What varies: how the chosen stripe is
// marked (length, cut-out, contour, side tab, tail), how many stripes there
// are, and whether the colour sits in the stripes or floods the ground.
const { bubble, rrect, check } = require('./concepts');
const { concepts: prevChoice, pill, nid } = require('./concepts_choice');
const { concepts: prevRound3 } = require('./concepts_round3');
const { concepts: prevRound4 } = require('./concepts_round4');

const KEEP = ['R4', 'R6', 'R8', 'R11'];
const pool = prevRound4.concat(prevRound3, prevChoice);
const shortlist = KEEP.map((id) => ({ ...pool.find((c) => c.id === id), dir: 'S' }));

const FACE = 'Helvetica, Arial, sans-serif';
// From textmetrics.json — measured, not estimated (see measure.js).
const EM = { 'Привет!': 3.79, 'Как дела?': 5.05, 'Здравствуйте!': 7.19, 'Привет, как дела?': 9.10, 'Чем помочь?': 6.43 };
const textW = (s, size) => Math.round(EM[s] * size);
const mid = (s, cy, size, fill, opacity) =>
  `<text x="512" y="${Math.round(cy + size * 0.35)}" text-anchor="middle"${opacity ? ` opacity="${opacity}"` : ''} font-family="${FACE}" font-size="${size}" font-weight="700" fill="${fill}">${s}</text>`;

const cutPath = (d) => `<path d="${d}" fill="#000"/>`;

const fresh = [
  // ============ X. Полосы: чем отмечен выбор ============
  {
    id: 'S3', dir: 'X', ru: 'Выбранная во всю ширину',
    note: 'Отметки нет вообще: принятый вариант просто длиннее остальных и уходит под самый край. Самый минимальный знак набора.',
    draw: (P) => `
  <path d="${pill(190, 216, 644, 140)}" fill="${P.mark}"/>
  <path d="${pill(60, 424, 904, 176)}" fill="${P.accent}"/>
  <path d="${pill(190, 668, 644, 140)}" fill="${P.mark}"/>`,
  },
  {
    id: 'S4', dir: 'X', ru: 'Галочка, прорезанная в полосе',
    note: 'Полосы равные, галочка не лежит сверху, а вырезана прямо в цветной — до фона. Ни одной лишней формы.',
    draw: (P) => {
      const m = nid('m');
      return `
  <path d="${pill(110, 200, 804, 150)}" fill="${P.mark}"/>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <path d="${check([690, 512, 744, 566, 840, 458])}" fill="none" stroke="#000" stroke-width="56"
          stroke-linecap="round" stroke-linejoin="round"/></mask>
  <path d="${pill(110, 437, 804, 150)}" fill="${P.accent}" mask="url(#${m})"/>
  <path d="${pill(110, 674, 804, 150)}" fill="${P.mark}"/>`;
    },
  },
  {
    id: 'S5', dir: 'X', ru: 'Выбранная — контур',
    note: 'Две полосы залиты, третья пустая и обведена цветом. Контраст «сплошное против пустого» вместо контраста цветов.',
    draw: (P) => `
  <path d="${pill(110, 200, 804, 150)}" fill="${P.mark}"/>
  <path d="${rrect(134, 461, 756, 150, 75)}" fill="none" stroke="${P.accent}" stroke-width="48"/>
  <path d="${pill(110, 674, 804, 150)}" fill="${P.mark}"/>`,
  },
  {
    id: 'S6', dir: 'X', ru: 'Метка сбоку',
    note: 'Полосы вообще не меняются — выбор отмечает короткая цветная метка у края, как активный пункт в боковом меню.',
    draw: (P) => `
  <path d="${pill(228, 210, 694, 140)}" fill="${P.mark}"/>
  <path d="${rrect(76, 410, 104, 204, 52)}" fill="${P.accent}"/>
  <path d="${pill(228, 442, 694, 140)}" fill="${P.mark}"/>
  <path d="${pill(228, 674, 694, 140)}" fill="${P.mark}"/>`,
  },
  {
    id: 'S10', dir: 'X', ru: 'Слова в рамке',
    note: 'R4 и R6 вместе: слова стоят в полосах, а выбор держит рамка, а не заливка. Читается спокойнее цветного барабана.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${pill(110, 190, 804, 156)}" fill="#fff"/>
    <path d="${pill(110, 434, 804, 156)}" fill="#fff"/>
    <path d="${pill(110, 678, 804, 156)}" fill="#fff"/>
    ${mid('Привет!', 268, 76, '#000')}
    ${mid('Привет, как дела?', 512, 76, '#000')}
    ${mid('Здравствуйте!', 756, 76, '#000')}</mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  <path d="${rrect(66, 390, 892, 244, 122)}" fill="none" stroke="${P.accent}" stroke-width="46"/>`;
    },
  },
  {
    id: 'S11', dir: 'X', ru: 'Полоса с хвостом',
    note: 'У принятой полосы вырастает хвостик — она перестаёт быть строчкой списка и становится отправленным сообщением.',
    draw: (P) => `
  <path d="${pill(110, 176, 804, 140)}" fill="${P.mark}"/>
  <path d="${bubble(110, 392, 804, 176, 88, 96)}" fill="${P.accent}"/>
  <path d="${pill(110, 716, 804, 140)}" fill="${P.mark}"/>`,
  },

  // ============ Y. Барабан ============
  {
    id: 'S1', dir: 'Y', ru: 'Барабан на пять полос',
    note: 'Полос больше трёх, крайние уходят под край иконки — становится видно, что список прокручивается, а не заканчивается тремя вариантами.',
    draw: (P) => `
  <path d="${pill(150, 108, 724, 104)}" fill="${P.mark}" opacity="0.16"/>
  <path d="${pill(120, 278, 784, 116)}" fill="${P.mark}" opacity="0.34"/>
  <path d="${pill(96, 424, 832, 176)}" fill="${P.accent}"/>
  <path d="${pill(120, 630, 784, 116)}" fill="${P.mark}" opacity="0.34"/>
  <path d="${pill(150, 812, 724, 104)}" fill="${P.mark}" opacity="0.16"/>`,
  },
  {
    id: 'S2', dir: 'Y', ru: 'Полосы разной длины',
    note: 'Полосы разной длины, как настоящие строки текста — знак перестаёт напоминать эквалайзер и снова читается как список фраз.',
    draw: (P) => `
  <path d="${pill(130, 216, 700, 148)}" fill="${P.mark}"/>
  <path d="${pill(130, 438, 796, 148)}" fill="${P.accent}"/>
  <path d="${pill(130, 660, 604, 148)}" fill="${P.mark}"/>`,
  },
  {
    id: 'S12', dir: 'Y', ru: 'Окно выбора',
    note: 'Настоящий барабан: выбранная фраза стоит в полосе, которая идёт от края до края без полей, остальные — просто текст. Ближе всех к тому, как выбор устроен в iOS.',
    draw: (P) => `
  ${mid('Привет!', 288, 78, P.mark, 0.5)}
  <rect x="0" y="400" width="1024" height="224" fill="${P.accent}"/>
  ${mid('Привет, как дела?', 512, 84, P.onAccent)}
  ${mid('Здравствуйте!', 738, 78, P.mark, 0.5)}`,
  },

  // ============ Z. Инверсия ============
  {
    id: 'S7', dir: 'Z', ru: 'Инверсия с обводкой',
    note: 'R8, где выбранная полоса не залита белым, а обведена. Внутри знака появляется воздух — на светлых обоях выглядит легче.',
    draw: (P) => {
      const m = nid('m'), g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    ${cutPath(pill(96, 196, 832, 148))}
    ${cutPath(pill(96, 438, 832, 148))}
    ${cutPath(pill(96, 680, 832, 148))}</mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>
  <path d="${rrect(56, 398, 912, 228, 114)}" fill="none" stroke="${P.mark}" stroke-width="44"/>`;
    },
  },
  {
    id: 'S8', dir: 'Z', ru: 'Инверсия, выбранная толще',
    note: 'R8 без единой рамки: цвет в фоне, полосы прорезаны, а принятая просто заметно толще и белая. Меньше всего элементов из всего набора.',
    draw: (P) => {
      const m = nid('m'), g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    ${cutPath(pill(96, 186, 832, 132))}
    ${cutPath(pill(96, 706, 832, 132))}
    ${cutPath(rrect(60, 396, 904, 232, 116))}</mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>
  <path d="${pill(96, 432, 832, 160)}" fill="${P.mark}"/>`;
    },
  },
  {
    id: 'S9', dir: 'Z', ru: 'Слова на инверсии',
    note: 'R4 и R8 вместе: цвет в фоне, отклонённые фразы прорезаны до тёмного, принятая лежит на белой полосе. Самый контрастный из текстовых.',
    draw: (P) => {
      const m = nid('m'), g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    ${cutPath(pill(76, 176, 872, 150))}
    ${cutPath(pill(76, 700, 872, 150))}
    ${cutPath(rrect(56, 396, 912, 232, 116))}</mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>
  ${mid('Привет!', 251, 76, P.mark, 0.85)}
  <path d="${pill(96, 432, 832, 160)}" fill="${P.mark}"/>
  ${mid('Привет, как дела?', 512, 80, P.accent)}
  ${mid('Здравствуйте!', 775, 76, P.mark, 0.85)}`;
    },
  },
];

module.exports = { concepts: [...shortlist, ...fresh] };
