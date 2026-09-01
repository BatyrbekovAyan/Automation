// Second family, on one idea: «выбираем из нескольких вариантов ответа».
// Every mark here is some arrangement of option rows + a mark of choice.
//
// The hard constraint is 29px. Three thin lines turn to grey mush there, so
// each variant carries the meaning twice: once in the layout (a list) and once
// in colour (one row is the accent) — colour survives to a single pixel, the
// line count does not.
const { bubble, rrect, check } = require('./concepts');

const pill = (x, y, w, h) => rrect(x, y, w, h, h / 2);

let uid = 1000;
const nid = (s) => `${s}${++uid}`;

// A black knockout of `d`, grown by `w`, for use inside a <mask>.
const cut = (d, w) => `<path d="${d}" fill="#000" stroke="#000" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`;

// Badge: accent disc + tick, separated from whatever it overlaps by a gap ring.
function badge(P, cx, cy, r, sw) {
  const k = r * 0.46;
  return `
  <circle cx="${cx}" cy="${cy}" r="${r}" fill="${P.accent}"/>
  <path d="${check([cx - k, cy + k * 0.12, cx - k * 0.24, cy + k * 0.86, cx + k * 0.98, cy - k * 0.78])}"
        fill="none" stroke="${P.onAccent}" stroke-width="${sw}" stroke-linecap="round" stroke-linejoin="round"/>`;
}

const concepts = [
  // ===================== E. Голый список =====================
  {
    id: 'P1', dir: 'E', ru: 'Три строки и галочка',
    note: 'Самое буквальное прочтение: слева варианты, справа — выбор. Ничего лишнего, поэтому и держится на 29px.',
    draw: (P) => `
  <path d="${pill(140, 252, 404, 120)}" fill="${P.mark}"/>
  <path d="${pill(140, 452, 340, 120)}" fill="${P.mark}"/>
  <path d="${pill(140, 652, 372, 120)}" fill="${P.mark}"/>
  <path d="${check([606, 534, 686, 614, 846, 418])}" fill="none" stroke="${P.accent}" stroke-width="98"
        stroke-linecap="round" stroke-linejoin="round"/>`,
  },
  {
    id: 'P2', dir: 'E', ru: 'Выбранная строка',
    note: 'Вариант, который приняли, окрашен целиком и заканчивается печатью. Цвет строки читается даже там, где сами строки уже слились.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <circle cx="774" cy="512" r="180" fill="#000"/></mask>
  <g mask="url(#${m})">
    <path d="${pill(168, 252, 520, 124)}" fill="${P.mark}" opacity="0.45"/>
    <path d="${pill(168, 450, 640, 124)}" fill="${P.accent}"/>
    <path d="${pill(168, 648, 470, 124)}" fill="${P.mark}" opacity="0.45"/>
  </g>
  <circle cx="774" cy="512" r="142" fill="${P.accent}"/>
  <path d="${check([708, 516, 758, 566, 848, 458])}" fill="none" stroke="${P.onAccent}" stroke-width="50"
        stroke-linecap="round" stroke-linejoin="round"/>`;
    },
  },
  {
    id: 'P3', dir: 'E', ru: 'Галочка поверх списка',
    note: 'Список уходит на второй план, решение — на первый. Самый громкий силуэт в этом семействе.',
    draw: (P) => {
      const m = nid('m');
      const tick = check([300, 566, 412, 678, 726, 328]);
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>${cut(tick, 176)}</mask>
  <g mask="url(#${m})">
    <path d="${pill(178, 262, 540, 104)}" fill="${P.mark}" opacity="0.42"/>
    <path d="${pill(178, 460, 604, 104)}" fill="${P.mark}" opacity="0.42"/>
    <path d="${pill(178, 658, 470, 104)}" fill="${P.mark}" opacity="0.42"/>
  </g>
  <path d="${tick}" fill="none" stroke="${P.accent}" stroke-width="112" stroke-linecap="round" stroke-linejoin="round"/>`;
    },
  },
  {
    id: 'P10', dir: 'E', ru: 'Галочка вместо строки',
    note: 'Две строки — и вместо третьей уже ответ. Читается как «из вариантов получился один».',
    draw: (P) => `
  <path d="${pill(200, 206, 600, 118)}" fill="${P.mark}" opacity="0.50"/>
  <path d="${pill(200, 380, 372, 118)}" fill="${P.mark}" opacity="0.50"/>
  <path d="${check([262, 676, 374, 788, 706, 456])}" fill="none" stroke="${P.accent}" stroke-width="122"
        stroke-linecap="round" stroke-linejoin="round"/>`,
  },

  // ===================== F. Список внутри реплики =====================
  {
    id: 'P5', dir: 'F', ru: 'Варианты в пузыре',
    note: 'Тот же список, но внутри реплики — сразу понятно, что варианты именно текстовые. Средний вариант выбран.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(150, 176, 724, 536, 168, 136)}" fill="#fff"/>
    <path d="${pill(236, 282, 480, 96)}" fill="#000"/>
    <path d="${pill(236, 552, 402, 96)}" fill="#000"/>
    <path d="${pill(214, 410, 596, 116)}" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  <path d="${pill(236, 421, 552, 94)}" fill="${P.accent}"/>`;
    },
  },
  {
    id: 'P6', dir: 'F', ru: 'Пузырь с печатью',
    note: 'Реплика со строками и печать поверх угла. Печать крупная, поэтому именно она доживает до самого мелкого размера.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(132, 168, 700, 500, 160, 132)}" fill="#fff"/>
    <path d="${pill(210, 268, 470, 100)}" fill="#000"/>
    <path d="${pill(210, 418, 372, 100)}" fill="#000"/>
    <circle cx="762" cy="700" r="212" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  ${badge(P, 762, 700, 170, 58)}`;
    },
  },

  // ===================== G. Список как форма =====================
  {
    id: 'P4', dir: 'G', ru: 'Карточка с печатью',
    note: 'Черновик ответа как отдельный документ, на который поставили печать. Самый «деловой» из всех.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${rrect(140, 218, 700, 588, 108)}" fill="#fff"/>
    <path d="${pill(216, 306, 470, 100)}" fill="#000"/>
    <path d="${pill(216, 452, 548, 100)}" fill="#000"/>
    <path d="${pill(216, 598, 392, 100)}" fill="#000"/>
    <circle cx="792" cy="760" r="214" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  ${badge(P, 792, 760, 172, 58)}`;
    },
  },
  {
    id: 'P7', dir: 'G', ru: 'Чек-лист',
    note: 'Классический список с квадратами: первый отмечен. Понятно без объяснений, но и знак самый обычный.',
    draw: (P) => {
      const rows = [[214, 452], [446, 372], [678, 416]];
      return rows.map(([y, w], i) =>
        (i === 0
          ? `<path d="${rrect(160, y, 132, 132, 40)}" fill="${P.accent}"/>
  <path d="${check([192, y + 68, 220, y + 96, 272, y + 38])}" fill="none" stroke="${P.onAccent}" stroke-width="38"
        stroke-linecap="round" stroke-linejoin="round"/>`
          : `<path d="${rrect(160, y, 132, 132, 40)}" fill="${P.mark}" opacity="0.42"/>`) +
        `\n  <path d="${pill(348, y + 22, w, 88)}" fill="${P.mark}" opacity="${i === 0 ? 1 : 0.55}"/>`
      ).join('\n  ');
    },
  },
  {
    id: 'P12', dir: 'G', ru: 'Радио-выбор',
    note: 'Кружки-переключатели: два пустых, один залит. Форма выбора, которую видел каждый.',
    draw: (P) => {
      const rows = [[236, 430], [452, 486], [668, 388]];
      return rows.map(([y, w], i) => {
        const cy = y + 62;
        const dot = i === 1
          ? `<circle cx="242" cy="${cy}" r="76" fill="${P.accent}"/>
  <path d="${check([204, cy + 4, 232, cy + 32, 286, cy - 26])}" fill="none" stroke="${P.onAccent}" stroke-width="30"
        stroke-linecap="round" stroke-linejoin="round"/>`
          : `<circle cx="242" cy="${cy}" r="76" fill="none" stroke="${P.mark}" stroke-width="34" opacity="0.5"/>`;
        return `${dot}\n  <path d="${pill(366, y + 18, w, 88)}" fill="${P.mark}" opacity="${i === 1 ? 1 : 0.5}"/>`;
      }).join('\n  ');
    },
  },

  // ===================== H. Крупный выбор =====================
  {
    id: 'P8', dir: 'H', ru: 'Печать на списке',
    note: 'Список целиком и одна большая печать по центру. Меньше всего деталей — значит, меньше всего теряет при уменьшении.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <circle cx="628" cy="654" r="240" fill="#000"/></mask>
  <g mask="url(#${m})">
    <path d="${pill(156, 250, 660, 118)}" fill="${P.mark}"/>
    <path d="${pill(156, 442, 712, 118)}" fill="${P.mark}"/>
    <path d="${pill(156, 634, 560, 118)}" fill="${P.mark}"/>
  </g>
  ${badge(P, 628, 654, 196, 66)}`;
    },
  },
  {
    id: 'P9', dir: 'H', ru: 'Два варианта',
    note: 'Всего два ответа: один отклонили, второй приняли. Крупнее всех — и потому самый читаемый на иконке в списке приложений.',
    draw: (P) => `
  <path d="${rrect(150, 194, 724, 250, 108)}" fill="${P.mark}" opacity="0.44"/>
  <path d="${rrect(150, 500, 724, 250, 108)}" fill="${P.accent}"/>
  <path d="${check([612, 630, 682, 700, 822, 556])}" fill="none" stroke="${P.onAccent}" stroke-width="80"
        stroke-linecap="round" stroke-linejoin="round"/>`,
  },
  {
    id: 'P11', dir: 'H', ru: 'Палец выбирает',
    note: 'Курсор прямо на выбранной строке — единственный вариант, где видно действие, а не результат.',
    draw: (P) => {
      const m = nid('m');
      const cursor = 'M 556 470 L 556 806 L 640 728 L 700 848 L 774 812 L 712 694 L 818 682 Z';
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <path d="${cursor}" fill="#000" stroke="#000" stroke-width="96" stroke-linejoin="round"/></mask>
  <g mask="url(#${m})">
    <path d="${pill(148, 214, 620, 116)}" fill="${P.mark}" opacity="0.45"/>
    <path d="${pill(148, 396, 700, 116)}" fill="${P.accent}"/>
    <path d="${pill(148, 578, 540, 116)}" fill="${P.mark}" opacity="0.45"/>
  </g>
  <path d="${cursor}" fill="${P.mark}"/>`;
    },
  },

  {
    id: 'P13', dir: 'E', ru: 'Два варианта и галочка',
    note: 'Самая крупная версия P1: всего две строки, поэтому и строки, и галочка остаются толстыми на любом размере.',
    draw: (P) => `
  <path d="${pill(140, 324, 350, 148)}" fill="${P.mark}"/>
  <path d="${pill(140, 552, 288, 148)}" fill="${P.mark}"/>
  <path d="${check([530, 532, 620, 622, 816, 410])}" fill="none" stroke="${P.accent}" stroke-width="110"
        stroke-linecap="round" stroke-linejoin="round"/>`,
  },
  {
    id: 'P14', dir: 'H', ru: 'Выбранный вариант крупнее',
    note: 'Выбор показан не печатью, а размером: принятая строка выше и шире соседних. Как выделенный чип в списке.',
    draw: (P) => `
  <path d="${pill(168, 244, 560, 116)}" fill="${P.mark}" opacity="0.45"/>
  <path d="${pill(168, 434, 700, 156)}" fill="${P.accent}"/>
  <path d="${check([690, 516, 736, 562, 818, 466])}" fill="none" stroke="${P.onAccent}" stroke-width="46"
        stroke-linecap="round" stroke-linejoin="round"/>
  <path d="${pill(168, 664, 500, 116)}" fill="${P.mark}" opacity="0.45"/>`,
  },
  {
    id: 'P15', dir: 'F', ru: 'Три реплики',
    note: 'Варианты — не строки, а сами реплики с хвостиками. Дольше всех объясняет, что речь о переписке, но и деталей больше всех.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <rect width="1024" height="1024" fill="#fff"/>
    <circle cx="754" cy="500" r="150" fill="#000"/></mask>
  <g mask="url(#${m})">
    <path d="${bubble(168, 190, 448, 150, 72, 52)}" fill="${P.mark}" opacity="0.45"/>
    <path d="${bubble(168, 424, 520, 160, 76, 56)}" fill="${P.accent}"/>
    <path d="${bubble(168, 668, 400, 150, 72, 52)}" fill="${P.mark}" opacity="0.45"/>
  </g>
  <circle cx="754" cy="500" r="112" fill="${P.mark}"/>
  <path d="${check([702, 504, 740, 542, 806, 462])}" fill="none" stroke="${P.accent}" stroke-width="40"
        stroke-linecap="round" stroke-linejoin="round"/>`;
    },
  },
  {
    id: 'P16', dir: 'F', ru: 'Пузырь с вариантами и печатью',
    note: 'P5 плюс печать на углу: список объясняет идею на большом размере, печать вытягивает знак на мелком.',
    draw: (P) => {
      const m = nid('m');
      return `
  <mask id="${m}" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
    <path d="${bubble(132, 158, 690, 494, 160, 128)}" fill="#fff"/>
    <path d="${pill(206, 250, 452, 92)}" fill="#000"/>
    <path d="${pill(186, 374, 560, 112)}" fill="#000"/>
    <path d="${pill(206, 518, 386, 92)}" fill="#000"/>
    <circle cx="778" cy="716" r="208" fill="#000"/></mask>
  <rect width="1024" height="1024" fill="${P.mark}" mask="url(#${m})"/>
  <path d="${pill(206, 384, 520, 92)}" fill="${P.accent}"/>
  ${badge(P, 778, 716, 166, 56)}`;
    },
  },
];

module.exports = { concepts, pill, badge, nid, cut };
