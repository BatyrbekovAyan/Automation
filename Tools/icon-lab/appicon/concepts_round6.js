// Round 6. Short-list was R8, R11, S1, S3, S8 — and the words this time:
// «мне больше интересен тёмный фон чернильный». Two facts fall out of that:
// every survivor is PURE BANDS with NO tick anywhere (choice = colour,
// thickness, length, position), and the colour-flooded grounds of R8/S8 were
// liked for their shape, not their brightness.
//
// So this round is dark-first: every mark sits on the ink ground, and what
// varies is HOW the chosen band lights up (white vs ink vs gradient vs glow)
// and WHERE it sits (full-bleed, shifted, off the edge, bottom = the reply
// position in a real chat). No text — no words survived the cut, which also
// buries the localisation problem.
const { concepts: prevRound5 } = require('./concepts_round5');
const { pill: pill_, nid: nid_ } = require('./concepts_choice');

const KEEP = ['R8', 'R11', 'S1', 'S3', 'S8'];
const shortlist = KEEP.map((id) => ({ ...prevRound5.find((c) => c.id === id), dir: 'S' }));

const fresh = [
  // ============ A. Как загорается выбранная ============
  {
    id: 'T1', dir: 'A', ru: 'Белая среди чернильных',
    note: 'R11 и S8 в одном знаке: полосы цветные, принятая — белая и толще. Всё, чем были хороши оба, без цветного фона.',
    draw: (P) => `
  <path d="${pill_(110, 208, 804, 142)}" fill="${P.accent}"/>
  <path d="${pill_(110, 420, 804, 184)}" fill="${P.mark}"/>
  <path d="${pill_(110, 674, 804, 142)}" fill="${P.accent}"/>`,
  },
  {
    id: 'T2', dir: 'A', ru: 'Чернильная среди белых',
    note: 'Зеркало T1: полосы белые, принятая — цветная и толще. Спокойнее, потому что цвета в знаке ровно одна полоса.',
    draw: (P) => `
  <path d="${pill_(110, 208, 804, 142)}" fill="${P.mark}"/>
  <path d="${pill_(110, 420, 804, 184)}" fill="${P.accent}"/>
  <path d="${pill_(110, 674, 804, 142)}" fill="${P.mark}"/>`,
  },
  {
    id: 'T4', dir: 'A', ru: 'Градиент в выбранной',
    note: 'Отклонённые — приглушённые, принятая несёт градиент. Единственный знак, где сам цвет живой, а не плоский.',
    draw: (P) => {
      const g = nid_('g');
      return `
  <linearGradient id="${g}" x1="0" y1="0" x2="1" y2="0.4">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <path d="${pill_(110, 208, 804, 142)}" fill="${P.mark}" opacity="0.32"/>
  <path d="${pill_(110, 420, 804, 184)}" fill="url(#${g})"/>
  <path d="${pill_(110, 674, 804, 142)}" fill="${P.mark}" opacity="0.32"/>`;
    },
  },
  {
    id: 'T5', dir: 'A', ru: 'Подсветка',
    note: 'Принятая полоса светится — вокруг неё мягкий ореол цвета на тёмных чернилах. На мелком размере ореол остаётся светлым пятном вокруг полосы.',
    draw: (P) => `
  <path d="${pill_(110, 196, 804, 140)}" fill="${P.mark}" opacity="0.5"/>
  <path d="${pill_(46, 386, 932, 252)}" fill="${P.accent}" opacity="0.12"/>
  <path d="${pill_(78, 416, 868, 192)}" fill="${P.accent}" opacity="0.22"/>
  <path d="${pill_(110, 442, 804, 140)}" fill="${P.accent}"/>
  <path d="${pill_(110, 688, 804, 140)}" fill="${P.mark}" opacity="0.5"/>`,
  },
  {
    id: 'T6', dir: 'A', ru: 'Полоса в полосе',
    note: 'Внутри принятой — тёмная строка, как текст на выделении. Двухслойность видна даже тогда, когда сами полосы уже слились.',
    draw: (P) => `
  <path d="${pill_(110, 208, 804, 142)}" fill="${P.mark}" opacity="0.45"/>
  <path d="${pill_(110, 420, 804, 184)}" fill="${P.accent}"/>
  <path d="${pill_(166, 468, 470, 88)}" fill="${P.onAccent}"/>
  <path d="${pill_(110, 674, 804, 142)}" fill="${P.mark}" opacity="0.45"/>`,
  },
  {
    id: 'T12', dir: 'A', ru: 'Одни чернила',
    note: 'Весь знак — один цвет: отклонённые полосы тёмно-чернильные, принятая — яркая. Максимально фирменный, особенно в палитре «Чернильный».',
    draw: (P) => `
  <path d="${pill_(110, 208, 804, 142)}" fill="${P.accent}" opacity="0.30"/>
  <path d="${pill_(110, 420, 804, 184)}" fill="${P.accent}"/>
  <path d="${pill_(110, 674, 804, 142)}" fill="${P.accent}" opacity="0.30"/>`,
  },

  // ============ B. Геометрия выбора ============
  {
    id: 'T3', dir: 'B', ru: 'До самых краёв',
    note: 'Принятая полоса пробивает края иконки насквозь, отклонённые остаются внутри. Выбор показан не цветом, а тем, что полоса вышла за раму.',
    draw: (P) => `
  <path d="${pill_(150, 200, 724, 142)}" fill="${P.mark}"/>
  <path d="${pill_(-120, 419, 1264, 186)}" fill="${P.accent}"/>
  <path d="${pill_(150, 682, 724, 142)}" fill="${P.mark}"/>`,
  },
  {
    id: 'T9', dir: 'B', ru: 'Шаг вправо',
    note: 'Все полосы одинаковые, принятая сделала шаг вправо — как строка, которую двинули отправить. Движение вместо отметки.',
    draw: (P) => `
  <path d="${pill_(110, 208, 700, 142)}" fill="${P.mark}"/>
  <path d="${pill_(214, 441, 700, 142)}" fill="${P.accent}"/>
  <path d="${pill_(110, 674, 700, 142)}" fill="${P.mark}"/>`,
  },
  {
    id: 'T10', dir: 'B', ru: 'Уходит за край',
    note: 'Принятая начинается как все, но её правый конец уже за краем иконки — сообщение в момент отправки. Самый «сюжетный» из беззвучных.',
    draw: (P) => `
  <path d="${pill_(110, 208, 724, 142)}" fill="${P.mark}"/>
  <path d="${pill_(214, 441, 1000, 142)}" fill="${P.accent}"/>
  <path d="${pill_(110, 674, 724, 142)}" fill="${P.mark}"/>`,
  },
  {
    id: 'T11', dir: 'B', ru: 'Раскрытая',
    note: 'Отклонённые свёрнуты в короткие полоски, принятая раскрыта во всю ширину и толще. Длина и толщина работают вместе.',
    draw: (P) => `
  <path d="${pill_(110, 216, 420, 132)}" fill="${P.mark}" opacity="0.6"/>
  <path d="${pill_(110, 420, 804, 184)}" fill="${P.accent}"/>
  <path d="${pill_(110, 676, 420, 132)}" fill="${P.mark}" opacity="0.6"/>`,
  },

  // ============ C. Порядок и позиция ============
  {
    id: 'T7', dir: 'C', ru: 'Барабан, белый центр',
    note: 'S1 наизнанку: пять цветных полос гаснут к краям, в центре — белая. Белое пятно в цветном барабане видно с любого расстояния.',
    draw: (P) => `
  <path d="${pill_(150, 108, 724, 104)}" fill="${P.accent}" opacity="0.16"/>
  <path d="${pill_(120, 278, 784, 116)}" fill="${P.accent}" opacity="0.38"/>
  <path d="${pill_(96, 424, 832, 176)}" fill="${P.mark}"/>
  <path d="${pill_(120, 630, 784, 116)}" fill="${P.accent}" opacity="0.38"/>
  <path d="${pill_(150, 812, 724, 104)}" fill="${P.accent}" opacity="0.16"/>`,
  },
  {
    id: 'T8', dir: 'C', ru: 'Ответ снизу',
    note: 'Полосы светлеют сверху вниз, принятая — нижняя. В переписке ответ всегда снизу, так что позиция сама несёт смысл.',
    draw: (P) => `
  <path d="${pill_(110, 200, 804, 142)}" fill="${P.mark}" opacity="0.25"/>
  <path d="${pill_(110, 432, 804, 142)}" fill="${P.mark}" opacity="0.55"/>
  <path d="${pill_(110, 664, 804, 168)}" fill="${P.accent}"/>`,
  },
];

module.exports = { concepts: [...shortlist, ...fresh] };
