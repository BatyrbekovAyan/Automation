// Round 3. The owner short-listed P2 P5 P12 P14 P15 P16, which share three
// things — THREE options (never two), the chosen option is the accent row
// ITSELF, and the mark of choice is small and attached to that row. Standalone
// giant ticks (P1 P3 P10) and two-row versions (P9 P13) were out.
//
// So: same space, new arrangements. What varies here is WHERE the choice lives
// — beside the row, in the row's shape, in the frame around it, or in the fact
// that the chosen row turns into an actual sent reply.
const { bubble, bubbleR, rrect, check } = require('./concepts');
const { concepts: prev, pill, badge, nid } = require('./concepts_choice');

const KEEP = ['P2', 'P5', 'P12', 'P14', 'P15', 'P16'];
const shortlist = KEEP.map((id) => ({ ...prev.find((c) => c.id === id), dir: 'S' }));

const fresh = [
  // ============ K. Отметка рядом со строкой ============
  {
    id: 'Q2', dir: 'K', ru: 'Галочка вместо маркера',
    note: 'Слева колонка маркеров: у отклонённых — точки, у принятого — галочка. Отметка живёт в той же колонке, что и остальные, поэтому список не перекошен.',
    draw: (P) => `
  <circle cx="242" cy="290" r="48" fill="${P.mark}" opacity="0.45"/>
  <path d="${pill(380, 232, 440, 116)}" fill="${P.mark}" opacity="0.45"/>
  <path d="${check([172, 516, 228, 572, 320, 458])}" fill="none" stroke="${P.accent}" stroke-width="66"
        stroke-linecap="round" stroke-linejoin="round"/>
  <path d="${pill(380, 448, 486, 128)}" fill="${P.accent}"/>
  <circle cx="242" cy="734" r="48" fill="${P.mark}" opacity="0.45"/>
  <path d="${pill(380, 676, 400, 116)}" fill="${P.mark}" opacity="0.45"/>`,
  },
  {
    id: 'Q5', dir: 'K', ru: 'Выбранный выехал вперёд',
    note: 'Принятый вариант сдвинут вправо и вырос, а печать срослась с ним в одну форму — галочка прорезана до фона. Читается как «этот вытянули из списка».',
    draw: (P) => {
      const m = nid('m');
      return `
  <path d="${pill(150, 236, 470, 112)}" fill="${P.mark}" opacity="0.42"/>
  <path d="${pill(150, 664, 396, 112)}" fill="${P.mark}" opacity="0.42"/>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <path d="${check([712, 512, 762, 562, 852, 464])}" fill="none" stroke="#000" stroke-width="52"
          stroke-linecap="round" stroke-linejoin="round"/></mask>
  <g mask="url(#${m})">
    <path d="${pill(300, 434, 466, 154)}" fill="${P.accent}"/>
    <circle cx="782" cy="511" r="126" fill="${P.accent}"/>
  </g>`;
    },
  },
  {
    id: 'Q9', dir: 'K', ru: 'Печать слева',
    note: 'Зеркало P2: печать не в конце строки, а в начале — как штамп «принято» на полях. Композиция уравновешена влево, а не вправо.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <circle cx="252" cy="512" r="172" fill="#000"/></mask>
  <g mask="url(#${m})">
    <path d="${pill(318, 244, 500, 116)}" fill="${P.mark}" opacity="0.42"/>
    <path d="${pill(318, 656, 440, 116)}" fill="${P.mark}" opacity="0.42"/>
  </g>
  <path d="${pill(318, 450, 546, 124)}" fill="${P.accent}"/>
  ${badge(P, 252, 512, 134, 48)}`;
    },
  },

  // ============ L. Выбранный становится репликой ============
  {
    id: 'Q1', dir: 'L', ru: 'Выбранный обретает хвост',
    note: 'Два варианта — просто строки, а принятый уже отправлен: у него появился хвостик реплики. Единственный знак, где виден сам момент отправки.',
    draw: (P) => `
  <path d="${pill(178, 216, 560, 120)}" fill="${P.mark}" opacity="0.42"/>
  <path d="${pill(178, 378, 468, 120)}" fill="${P.mark}" opacity="0.42"/>
  <path d="${bubble(178, 546, 668, 150, 74, 150)}" fill="${P.accent}"/>
  <path d="${check([698, 588, 742, 632, 816, 544])}" fill="none" stroke="${P.onAccent}" stroke-width="46"
        stroke-linecap="round" stroke-linejoin="round"/>`,
  },
  {
    id: 'Q6', dir: 'L', ru: 'Три реплики, выбрана средняя',
    note: 'Варианты выровнены вправо и с хвостиками — это уже не список, а три готовых сообщения. Ближе всего к тому, что человек видит в «Вместе».',
    draw: (P) => `
  <path d="${bubbleR(400, 176, 460, 140, 68, 48)}" fill="${P.mark}" opacity="0.42"/>
  <path d="${bubbleR(268, 412, 592, 152, 72, 52)}" fill="${P.accent}"/>
  <path d="${check([330, 488, 372, 530, 444, 448])}" fill="none" stroke="${P.onAccent}" stroke-width="44"
        stroke-linecap="round" stroke-linejoin="round"/>
  <path d="${bubbleR(430, 664, 430, 140, 68, 48)}" fill="${P.mark}" opacity="0.42"/>`,
  },

  // ============ M. Список внутри реплики ============
  {
    id: 'Q7', dir: 'M', ru: 'Строка выходит за край',
    note: 'Принятый вариант не помещается в реплику и выезжает за её край. Самый «живой» знак набора — и самый рискованный: на мелком размере выступ можно принять за ошибку.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(140, 186, 640, 500, 156, 126)}" fill="#fff"/>
    <path d="${pill(216, 276, 420, 92)}" fill="#000"/>
    <path d="${pill(216, 546, 350, 92)}" fill="#000"/>
    <path d="${pill(196, 392, 720, 132)}" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  <path d="${pill(216, 404, 682, 108)}" fill="${P.accent}"/>`;
    },
  },
  {
    id: 'Q8', dir: 'M', ru: 'Печать на хвосте',
    note: 'Та же реплика со списком, но печать съехала на хвост. Знак становится «тяжёлым» слева-внизу — на сетке домашнего экрана это читается спокойнее, чем угловая печать.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(196, 130, 668, 462, 154, 128)}" fill="#fff"/>
    <path d="${pill(272, 214, 440, 94)}" fill="#000"/>
    <path d="${pill(272, 470, 380, 94)}" fill="#000"/>
    <circle cx="290" cy="742" r="196" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  <path d="${pill(272, 336, 512, 106)}" fill="${P.accent}"/>
  ${badge(P, 290, 742, 148, 50)}`;
    },
  },
  {
    id: 'Q10', dir: 'M', ru: 'Точки и строки в реплике',
    note: 'Внутри реплики — настоящий список с маркерами. Больше всего похоже на то, как варианты выглядят в приложении, но и деталей больше всех.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(140, 170, 700, 500, 160, 128)}" fill="#fff"/>
    <circle cx="248" cy="296" r="30" fill="#000"/>
    <path d="${pill(334, 250, 372, 92)}" fill="#000"/>
    <circle cx="248" cy="572" r="30" fill="#000"/>
    <path d="${pill(334, 526, 320, 92)}" fill="#000"/>
    <circle cx="248" cy="434" r="42" fill="#000"/>
    <path d="${pill(320, 378, 456, 112)}" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  <circle cx="248" cy="434" r="34" fill="${P.accent}"/>
  <path d="${pill(334, 388, 428, 92)}" fill="${P.accent}"/>`;
    },
  },
  {
    id: 'Q11', dir: 'M', ru: 'Печать внутри строки',
    note: 'P16 наоборот: печать не на углу реплики, а в конце самой выбранной строки. Знак получается компактнее — вся история происходит внутри пузыря.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(140, 164, 716, 512, 162, 130)}" fill="#fff"/>
    <path d="${pill(220, 252, 440, 94)}" fill="#000"/>
    <path d="${pill(220, 526, 370, 94)}" fill="#000"/>
    <path d="${pill(200, 376, 590, 134)}" fill="#000"/>
    <circle cx="720" cy="443" r="126" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  <path d="${pill(220, 396, 480, 94)}" fill="${P.accent}"/>
  ${badge(P, 720, 443, 96, 34)}`;
    },
  },
  {
    id: 'Q12', dir: 'M', ru: 'Цветная реплика',
    note: 'Вывернутая P5: реплика залита цветом, отклонённые строки прорезаны до фона, а принятая — белая. Ярче всех на тёмном домашнем экране.',
    draw: (P) => {
      const m = nid('m'), g = nid('g');
      return `
  <linearGradient id="${g}" x1="0.1" y1="0" x2="0.9" y2="1">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/>
  </linearGradient>
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(150, 180, 700, 508, 164, 132)}" fill="#fff"/>
    <path d="${pill(226, 268, 440, 96)}" fill="#000"/>
    <path d="${pill(226, 534, 370, 96)}" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="url(#${g})" mask="url(#${m})"/>
  <path d="${pill(226, 398, 560, 110)}" fill="${P.mark}"/>`;
    },
  },

  // ============ N. Другой механизм выбора ============
  {
    id: 'Q3', dir: 'N', ru: 'Колесо выбора',
    note: 'Не список, а барабан: варианты уходят за края, выбранный стоит в центральной полосе. Самые толстые формы во всём наборе — лучше всех держит 29px.',
    draw: (P) => `
  <path d="${pill(96, 148, 832, 118)}" fill="${P.mark}" opacity="0.28"/>
  <path d="${pill(76, 396, 872, 196)}" fill="${P.accent}"/>
  <path d="${check([700, 500, 754, 554, 856, 440])}" fill="none" stroke="${P.onAccent}" stroke-width="58"
        stroke-linecap="round" stroke-linejoin="round"/>
  <path d="${pill(96, 722, 832, 118)}" fill="${P.mark}" opacity="0.28"/>`,
  },
  {
    id: 'Q4', dir: 'N', ru: 'Кольцо выбора',
    note: 'Строки остаются одинаковыми, выбор показывает рамка вокруг одной из них — как фокус на поле ввода. Самый сдержанный знак: цвета в нём меньше всех.',
    draw: (P) => `
  <path d="${pill(212, 252, 520, 104)}" fill="${P.mark}" opacity="0.8"/>
  <path d="${pill(212, 468, 452, 104)}" fill="${P.mark}"/>
  <path d="${check([700, 510, 738, 548, 806, 466])}" fill="none" stroke="${P.accent}" stroke-width="44"
        stroke-linecap="round" stroke-linejoin="round"/>
  <path d="${rrect(158, 414, 706, 212, 106)}" fill="none" stroke="${P.accent}" stroke-width="48"/>
  <path d="${pill(212, 684, 456, 104)}" fill="${P.mark}" opacity="0.8"/>`,
  },
];

module.exports = { concepts: [...shortlist, ...fresh] };
