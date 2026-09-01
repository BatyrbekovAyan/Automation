// Round 4. Short-list was Q12, Q4, Q3 — three knives that look different but
// share a rule: the option rows are BIG and of EQUAL weight, and the choice is
// shown by a fill, a frame or a band — never by a little round stamp. The
// opacity-ramp + badge recipe (P2, P15, P16) is out.
//
// New here: some variants carry real messages. Text is a large-size bonus only
// — at 29px it degrades into the same bars the wordless variants are made of,
// which is exactly why every text variant is built on a layout that still works
// when the words turn to mush. In the shipped asset the text becomes outlines
// (and bakes in Russian — worth knowing before EN/KZ ever ships).
const { bubble, bubbleR, rrect, check } = require('./concepts');
const { concepts: prevChoice, pill, nid } = require('./concepts_choice');
const { concepts: prevRound3 } = require('./concepts_round3');

const KEEP = ['Q12', 'Q4', 'Q3'];
const all3 = prevRound3.concat(prevChoice);
const shortlist = KEEP.map((id) => ({ ...all3.find((c) => c.id === id), dir: 'S' }));

// Helvetica/Arial carry Cyrillic on every platform this page is read on, and
// resvg picks the same faces the browser does — so sheet and page agree.
const FACE = 'Helvetica, Arial, sans-serif';
// Measured off a resvg specimen: mixed Cyrillic runs ~0.545em per character.
const textW = (s, size) => Math.round(0.545 * size * s.length);
function label(s, x, cy, size, fill, opts = {}) {
  const anchor = opts.anchor ? ` text-anchor="${opts.anchor}"` : '';
  const op = opts.opacity ? ` opacity="${opts.opacity}"` : '';
  return `<text x="${x}" y="${Math.round(cy + size * 0.35)}"${anchor}${op} font-family="${FACE}" font-size="${size}" font-weight="700" fill="${fill}">${s}</text>`;
}

const fresh = [
  // ============ T. С текстом сообщений ============
  {
    id: 'R1', dir: 'T', ru: 'Три ответа словами',
    note: 'Варианты написаны как есть, принятый лежит на цветной плашке. На большом размере читаются слова, на мелком — те же три строки.',
    draw: (P) => `
  ${label('Привет!', 214, 288, 90, P.mark, { opacity: 0.55 })}
  <path d="${pill(168, 430, 547, 156)}" fill="${P.accent}"/>
  ${label('Как дела?', 214, 508, 90, P.onAccent)}
  ${label('Здравствуйте!', 214, 728, 90, P.mark, { opacity: 0.55 })}`,
  },
  {
    id: 'R2', dir: 'T', ru: 'Слова в рамке',
    note: 'Механика Q4, но с текстом: все три варианта равны, выбор показывает только рамка. Самый спокойный из текстовых.',
    draw: (P) => `
  ${label('Привет!', 214, 288, 84, P.mark, { opacity: 0.62 })}
  <path d="${rrect(160, 428, 700, 164, 82)}" fill="none" stroke="${P.accent}" stroke-width="46"/>
  ${label('Как дела?', 214, 510, 84, P.mark)}
  <path d="${check([712, 514, 748, 550, 810, 474])}" fill="none" stroke="${P.accent}" stroke-width="42"
        stroke-linecap="round" stroke-linejoin="round"/>
  ${label('Здравствуйте!', 214, 728, 84, P.mark, { opacity: 0.62 })}`,
  },
  {
    id: 'R3', dir: 'T', ru: 'Цветная реплика со словами',
    note: 'Q12 с текстом: реплика залита цветом, отклонённые варианты прорезаны до фона, принятый лежит на белой плашке.',
    draw: (P) => {
      const m = nid('m'), g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(150, 170, 726, 520, 166, 132)}" fill="#fff"/>
    ${label('Привет!', 226, 268, 80, '#000')}
    ${label('Здравствуйте!', 226, 596, 80, '#000')}</mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>
  <path d="${pill(214, 380, 502, 132)}" fill="${P.mark}"/>
  ${label('Как дела?', 262, 446, 80, P.accent)}`;
    },
  },
  {
    id: 'R4', dir: 'T', ru: 'Барабан со словами',
    note: 'Q3 с текстом. Полосы шире всех, поэтому это единственный знак, куда целиком влезает «Привет, как дела?».',
    draw: (P) => `
  <path d="${pill(72, 150, 880, 132)}" fill="${P.mark}" opacity="0.20"/>
  ${label('Привет!', 512, 216, 78, P.mark, { anchor: 'middle', opacity: 0.8 })}
  <path d="${pill(56, 396, 912, 200)}" fill="${P.accent}"/>
  ${label('Привет, как дела?', 512, 496, 84, P.onAccent, { anchor: 'middle' })}
  <path d="${pill(72, 742, 880, 132)}" fill="${P.mark}" opacity="0.20"/>
  ${label('Здравствуйте!', 512, 808, 78, P.mark, { anchor: 'middle', opacity: 0.8 })}`,
  },
  {
    id: 'R5', dir: 'T', ru: 'Вопрос и два ответа',
    note: 'Вся история целиком: клиент написал, бот предложил два ответа, один вы приняли. Рассказывает больше всех — и деталей у него больше всех.',
    draw: (P) => `
  <path d="${bubble(150, 126, 512, 180, 84, 66)}" fill="${P.mark}" opacity="0.34"/>
  ${label('Привет!', 214, 216, 84, P.mark, { opacity: 0.85 })}
  <path d="${pill(150, 444, 692, 156)}" fill="${P.mark}" opacity="0.26"/>
  ${label('Здравствуйте!', 198, 522, 84, P.mark, { opacity: 0.85 })}
  <path d="${pill(150, 650, 692, 168)}" fill="${P.accent}"/>
  ${label('Чем помочь?', 198, 734, 84, P.onAccent)}`,
  },

  // ============ U. Полосы (развитие Q3) ============
  {
    id: 'R6', dir: 'U', ru: 'Полосы в рамке',
    note: 'Q3 и Q4 в одном: полосы одинаковые, выбор держит только рамка. Ни одной формы, кроме прямоугольников — самый строгий знак набора.',
    draw: (P) => `
  <path d="${pill(140, 176, 744, 130)}" fill="${P.mark}"/>
  <path d="${rrect(96, 388, 832, 214, 107)}" fill="none" stroke="${P.accent}" stroke-width="48"/>
  <path d="${pill(140, 430, 744, 130)}" fill="${P.mark}"/>
  <path d="${pill(140, 684, 744, 130)}" fill="${P.mark}"/>`,
  },
  {
    id: 'R11', dir: 'U', ru: 'Светлая выбранная',
    note: 'Все полосы цветные, выбранная — белая и толще. Выбор показан не отметкой, а тем, что вариант «загорелся».',
    draw: (P) => `
  <path d="${pill(140, 200, 744, 148)}" fill="${P.accent}"/>
  <path d="${pill(140, 424, 744, 176)}" fill="${P.mark}"/>
  <path d="${pill(140, 676, 744, 148)}" fill="${P.accent}"/>`,
  },

  // ============ V. Рамка выбора (развитие Q4) ============
  {
    id: 'R9', dir: 'V', ru: 'Рамка вокруг реплики',
    note: 'Три готовых сообщения, рамка обводит выбранное прямо по контуру, вместе с хвостиком. Самый «продуктовый» из рамочных.',
    draw: (P) => {
      const b2 = bubbleR(268, 406, 592, 152, 72, 50);
      return `
  <path d="${bubbleR(400, 160, 460, 140, 68, 46)}" fill="${P.mark}" opacity="0.8"/>
  <g transform="translate(564,494) scale(1.13) translate(-564,-494)">
    <path d="${b2}" fill="none" stroke="${P.accent}" stroke-width="38"/></g>
  <path d="${b2}" fill="${P.mark}"/>
  <path d="${bubbleR(430, 664, 430, 140, 68, 46)}" fill="${P.mark}" opacity="0.8"/>`;
    },
  },
  {
    id: 'R10', dir: 'V', ru: 'Список в поле',
    note: 'Настоящий интерфейсный список: варианты в поле, выбранный подсвечен полосой во всю ширину — как наведённая строка в меню.',
    draw: (P) => `
  <path d="${rrect(120, 196, 784, 632, 124)}" fill="${P.mark}" opacity="0.16"/>
  <path d="${pill(190, 268, 520, 110)}" fill="${P.mark}"/>
  <path d="${rrect(150, 418, 724, 188, 94)}" fill="${P.accent}"/>
  <path d="${pill(190, 466, 604, 110)}" fill="${P.onAccent}"/>
  <path d="${pill(190, 648, 452, 110)}" fill="${P.mark}"/>`,
  },
  {
    id: 'R12', dir: 'V', ru: 'Рамка с галочкой на краю',
    note: 'Рамка Q4 плюс отметка, посаженная прямо на её край. Компромисс: рамка держит идею на большом, галочка — на мелком.',
    draw: (P) => {
      const m = nid('m');
      return `
  <path d="${pill(200, 250, 520, 112)}" fill="${P.mark}"/>
  <path d="${pill(200, 674, 460, 112)}" fill="${P.mark}"/>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <circle cx="818" cy="518" r="128" fill="#000"/></mask>
  <g mask="url(#${m})">
    <path d="${rrect(156, 414, 664, 208, 104)}" fill="none" stroke="${P.accent}" stroke-width="46"/>
    <path d="${pill(200, 462, 560, 112)}" fill="${P.mark}"/>
  </g>
  <circle cx="818" cy="518" r="92" fill="${P.accent}"/>
  <path d="${check([772, 522, 806, 556, 866, 480])}" fill="none" stroke="${P.onAccent}" stroke-width="34"
        stroke-linecap="round" stroke-linejoin="round"/>`;
    },
  },

  // ============ W. Инверсия цвета (развитие Q12) ============
  {
    id: 'R7', dir: 'W', ru: 'Реплика с рамкой',
    note: 'Q12, где выбранный вариант не залит белым, а обведён. Цвета столько же, но знак дышит — внутри реплики остаётся воздух.',
    draw: (P) => {
      const m = nid('m'), g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(150, 180, 700, 508, 164, 132)}" fill="#fff"/>
    <path d="${pill(226, 268, 440, 96)}" fill="#000"/>
    <path d="${pill(226, 398, 496, 110)}" fill="#000"/>
    <path d="${pill(226, 534, 370, 96)}" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>
  <path d="${rrect(192, 364, 564, 178, 89)}" fill="none" stroke="${P.mark}" stroke-width="40"/>`;
    },
  },
  {
    id: 'R8', dir: 'W', ru: 'Цветной барабан',
    note: 'Инверсия Q3: цвет уходит в фон целиком, полосы прорезаны до тёмного, выбранная — белая. Самый громкий знак из всех, что были.',
    draw: (P) => {
      const m = nid('m'), g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <path d="${pill(96, 168, 832, 140)}" fill="#000"/>
    <path d="${pill(96, 706, 832, 140)}" fill="#000"/>
    <path d="${rrect(60, 396, 904, 232, 116)}" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>
  <path d="${pill(96, 432, 832, 160)}" fill="${P.mark}"/>`;
    },
  },
];

module.exports = { concepts: [...shortlist, ...fresh] };
