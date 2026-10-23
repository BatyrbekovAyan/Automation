// Round 9 (2026-10-22). Brief: «почти так же, но больше похоже на то, что на животе у Репли».
//
// The mascot R79 wears the app's idea on its belly: three answer pills on a white panel —
// pale lavender options with one navy text line each, the chosen one cobalt with a white line
// and a white tick. ChatGPT drew six drafts (output/imagegen/appicon-repli-2026-10-22); the
// owner shortlisted 2 «Плашки» (flat) and 5 «Объём» (soft 3D) and asked for exact vectors of
// both, to pick the final one from.
//
// Geometry is MEASURED off those two drafts (bounding boxes of each colour, the tick's medial
// axis) and snapped to clean numbers; the colours are the belly's, sampled from the R79 render.
// The ground is U9's own — ink gradient under an uncut 20 % accent tint — so the icon stays
// «почти так же». Nothing from the draft rasters survives: R5's relief is built from plain SVG
// layers per raised shape (see `raised`).
//
// Then the owner liked the app mark from the video's end card (choosereply-video,
// src/spikes/proba-v2/Brand.tsx `Core`), where the chosen row is LONGER at both ends, and asked
// for both finalists that way, with the tick and without — the «W» concepts below. The W set
// still read wrong to him: in the mark everything sits on the centre line and glows, so the
// «G» concepts take that mark itself — its blue, its centred bars, its light.
const { pill, nid } = require('./concepts_choice');

// The belly's colours (R79 render).
const C = { pale: '#D7E3FD', navy: '#0A2253', cobalt: '#1F66FE', white: '#FFFFFF' };

// A capsule between two centre points, radius r. Two of them sharing an end are exactly a
// round-capped, round-joined polyline — but as FILLED geometry, so the tick can be clipped and
// masked like the pills (a clipPath ignores strokes).
function capsule(x1, y1, x2, y2, r) {
  const L = Math.hypot(x2 - x1, y2 - y1);
  const nx = (-(y2 - y1) / L) * r, ny = ((x2 - x1) / L) * r;
  const p = (v) => +v.toFixed(2);
  return `M ${p(x1 + nx)} ${p(y1 + ny)} L ${p(x2 + nx)} ${p(y2 + ny)} A ${r} ${r} 0 0 0 ${p(x2 - nx)} ${p(y2 - ny)}`
    + ` L ${p(x1 - nx)} ${p(y1 - ny)} A ${r} ${r} 0 0 0 ${p(x1 + nx)} ${p(y1 + ny)} Z`;
}
const tick = ([ax, ay, vx, vy, bx, by], w) => [capsule(ax, ay, vx, vy, w / 2), capsule(vx, vy, bx, by, w / 2)];

// U9's ground: build.js paints the ink gradient, the tint goes on top — uncut here, since
// nothing in this round is a cut-through.
const tint = (P) => `<rect width="1024" height="1024" fill="${P.accent}" opacity="0.20"/>`;

// Layouts: per pill [x, y, w, h] and its text line [x, w]; the line is lineH tall and centred on
// its pill vertically. The middle pill is the chosen one.
// R2, off draft 2: pills 820 wide, 154/186/154 tall with 50 gaps, centred on 512; lines 500×50;
// the tick (stroke 28) sits midway between the middle line's end and the pill's end (44 / 43),
// its box centred on the pill.
const L2 = {
  pills: [[102, 215, 820, 154], [102, 419, 820, 186], [102, 655, 820, 154]],
  lines: [[235, 500], [235, 500], [235, 500]], lineH: 50,
  tick: { pts: [793, 517, 812, 536.5, 865, 487.5], w: 28 },
};
// R5, off draft 5: a size up — pills 840 wide, 166/200/166 tall with 38 gaps, the solids centred
// at 508 because the shadows hang below them; lines 504×52; a heavier tick (stroke 36) where the
// draft had it. Centring it between line and pill end, as in R2, pushed it into the pill's round
// end: at this weight it read as crowding the edge.
const L5 = {
  pills: [[92, 204, 840, 166], [92, 408, 840, 200], [92, 646, 840, 166]],
  lines: [[228, 504], [228, 504], [228, 504]], lineH: 52,
  tick: { pts: [780, 507.5, 802, 533.5, 861, 481.5], w: 36 },
};
// «Выделенная»: the video mark is 56 / 78 / 56 % of its icon — the chosen row 11 % longer at each
// end. Here the chosen pill takes R5's 840 and the neighbours shrink to 640, 100 a side; rows and
// heights stay. Every row keeps its OWN proportions — its line starts 16 % into it and runs 61 %
// (R2) / 60 % (R5) of it — so the chosen row reads as the same row brought forward, not padded;
// aligning all lines at one x instead left the wide pill with an empty left end.
const chosenWide = (L, lines, tickPts = L.tick.pts) => ({
  ...L,
  pills: L.pills.map(([, y, , h], i) => (i === 1 ? [92, y, 840, h] : [192, y, 640, h])),
  lines,
  tick: { ...L.tick, pts: tickPts },
});
// R2's tick follows its line 5 to the right, keeping the 44 gap; R5's stays where the draft had it.
const L2W = chosenWide(L2, [[296, 390], [228, 512], [296, 390]], [798, 517, 817, 536.5, 870, 487.5]);
const L5W = chosenWide(L5, [[296, 384], [228, 504], [296, 384]]);

const lineRect = (L, i) => {
  const [, y, , h] = L.pills[i], [x, w] = L.lines[i];
  return [x, y + (h - L.lineH) / 2, w, L.lineH];
};
const lineOf = (L, i) => pill(...lineRect(L, i));
const lineBox = (L, i) => { const [x, y, w, h] = lineRect(L, i); return [x, y, x + w, y + h]; };
const tickBox = ({ pts, w }) => [Math.min(pts[0], pts[2], pts[4]) - w / 2, Math.min(pts[1], pts[3], pts[5]) - w / 2,
  Math.max(pts[0], pts[2], pts[4]) + w / 2, Math.max(pts[1], pts[3], pts[5]) + w / 2];

// One raised shape (a union of paths) in R5's relief, light from the upper left:
//   shadow + contact — the shape blurred, dropped below it;
//   body             — a vertical gradient across the shape's own height;
//   light / shade    — inner shadows, Figma-style: the shape masked by a shifted copy of itself
//                      leaves a sliver along one edge, which is blurred and clipped back inside.
// Every effect is [dx, dy, blur, opacity, colour].
function raised(ds, [x0, y0, x1, y1], o) {
  const pad = 64;
  const R = `x="${x0 - pad}" y="${y0 - pad}" width="${x1 - x0 + 2 * pad}" height="${y1 - y0 + 2 * pad}"`;
  const paths = (paint) => ds.map((d) => `<path d="${d}" fill="${paint}"/>`).join('');
  const blur = (id, s) => `<filter id="${id}" filterUnits="userSpaceOnUse" ${R}><feGaussianBlur stdDeviation="${s}"/></filter>`;
  const id = { body: nid('g'), clip: nid('c') };
  const drop = ([dx, dy, s, op, col]) => {
    const f = nid('f');
    return `${blur(f, s)}
  <g transform="translate(${dx} ${dy})" opacity="${op}"><g filter="url(#${f})">${paths(col)}</g></g>`;
  };
  const inner = ([dx, dy, s, op, col]) => {
    const f = nid('f'), m = nid('m');
    return `${blur(f, s)}
    <mask id="${m}" maskUnits="userSpaceOnUse" ${R}><rect ${R} fill="#fff"/><g transform="translate(${dx} ${dy})">${paths('#000')}</g></mask>
    <g opacity="${op}" filter="url(#${f})"><rect ${R} fill="${col}" mask="url(#${m})"/></g>`;
  };
  return `
  <linearGradient id="${id.body}" gradientUnits="userSpaceOnUse" x1="0" y1="${y0}" x2="0" y2="${y1}">${
    o.body.map(([t, col]) => `<stop offset="${t}" stop-color="${col}"/>`).join('')}</linearGradient>
  <clipPath id="${id.clip}">${ds.map((d) => `<path d="${d}"/>`).join('')}</clipPath>
  ${drop(o.shadow)}${drop(o.contact)}
  ${paths(`url(#${id.body})`)}
  <g clip-path="url(#${id.clip})">${inner(o.light)}${inner(o.shade)}
  </g>`;
}

// R5's materials, matched to draft 5: soft matte lavender and cobalt, a blue-tinted shade under
// each pill's lower edge (the draft's «rim»), dark contact shadows on the ground, and the lines
// and the tick raised off their pill with a shadow in the pill's own hue.
const M = {
  pale: {
    body: [[0, '#E6EEFE'], [0.2, '#D3E2FC'], [0.5, '#C6DAFC'], [1, '#C0D6FC']],
    shadow: [3, 12, 14, 0.95, '#01040C'], contact: [1, 4, 4, 0.8, '#01030A'],
    light: [3, 9, 6, 0.85, '#FFFFFF'], shade: [-2, -10, 5, 0.75, '#5A88F3'],
  },
  cobalt: {
    body: [[0, '#2A7DFF'], [0.15, '#1068FE'], [0.5, '#0862FD'], [1, '#035AFD']],
    shadow: [3, 12, 14, 0.95, '#01040C'], contact: [1, 4, 4, 0.8, '#01030A'],
    light: [3, 9, 6, 0.35, '#7FB6FF'], shade: [-3, -12, 7, 0.7, '#0036C4'],
  },
  navy: {
    body: [[0, '#062766'], [1, '#011B57']],
    shadow: [1, 4, 3, 0.4, '#2B52BE'], contact: [0, 1, 1, 0.3, '#18388F'],
    light: [1, 3, 1.5, 0.25, '#9FB8F0'], shade: [0, -4, 2, 0.35, '#000A2E'],
  },
  white: {
    body: [[0, '#FFFFFF'], [1, '#E6EDFD']],
    shadow: [1, 6, 5, 0.65, '#0028B0'], contact: [0, 2, 2, 0.45, '#0030C0'],
    light: [2, 4, 2, 0, '#FFFFFF'], shade: [-1, -6, 3, 0.6, '#B3C6F2'],
  },
};

// ---- G: the video's mark. Its box is the night theme's 145° gradient accentHi → accent (U9's own
// #8FAEEF → #5981D6), three white bars centred — 56 / 78 / 56 % of the box, 12 % tall, 8 % apart,
// the neighbours at 55 % — and the light is a bloom round the box. An app icon has no outside, so
// the bloom moves in: each bar glows from a blurred copy of itself behind it.

// CSS `linear-gradient(145deg, …)` on the 1024 square, as SVG: the line runs through the centre at
// 145° and spans |sin| + |cos| of the side, so the corners land on 0 and 1.
const deg145 = (() => {
  const a = (145 * Math.PI) / 180, dx = Math.sin(a), dy = -Math.cos(a), half = (1024 * (Math.abs(dx) + Math.abs(dy))) / 2;
  const p = (v) => +v.toFixed(1);
  return { x1: p(512 - dx * half), y1: p(512 - dy * half), x2: p(512 + dx * half), y2: p(512 + dy * half) };
})();
const blueGround = (P) => {
  const g = nid('g');
  return `<linearGradient id="${g}" gradientUnits="userSpaceOnUse" x1="${deg145.x1}" y1="${deg145.y1}" x2="${deg145.x2}" y2="${deg145.y2}">
    <stop offset="0" stop-color="${P.accentHi}"/><stop offset="1" stop-color="${P.accent}"/></linearGradient>
  <rect width="1024" height="1024" fill="url(#${g})"/>`;
};

// A shape's light: blurred copies of it behind it, each [blur, opacity, colour]; the filter region is
// sized to the blur so it is never clipped.
function glow(d, [x0, y0, x1, y1], layers) {
  return layers.map(([s, op, col]) => {
    const f = nid('f'), pad = 3 * s + 8;
    return `<filter id="${f}" filterUnits="userSpaceOnUse" x="${x0 - pad}" y="${y0 - pad}" width="${x1 - x0 + 2 * pad}" height="${y1 - y0 + 2 * pad}"><feGaussianBlur stdDeviation="${s}"/></filter>
  <g opacity="${op}" filter="url(#${f})"><path d="${d}" fill="${col}"/></g>`;
  }).join('\n  ');
}

// The mark at icon scale: bars 572 / 800 / 572 wide, 122 tall, 85 apart, centred on 512.
const BARS = [[226, 244, 572, 122], [112, 451, 800, 122], [226, 658, 572, 122]];
// Its rows as answers with a line, centred: thicker pills to hold the line (150 / 182 / 150, 48
// apart), lines 55 % of their pill.
const ROWS = { pills: [[212, 223, 600, 150], [102, 421, 820, 182], [212, 651, 600, 150]], lines: [[347, 330], [286, 452], [347, 330]], lineH: 46 };
// R2W with every line on its pill's centre line.
const L2C = { ...L2W, lines: [[317, 390], [256, 512], [317, 390]] };

const box = ([x, y, w, h]) => [x, y, x + w, y + h];
const bars = (rows, fills, glows) => rows.map((r, i) => `${glow(pill(...r), box(r), glows[i === 1 ? 1 : 0])}
  <path d="${pill(...r)}" fill="${fills[i === 1 ? 1 : 0][0]}" opacity="${fills[i === 1 ? 1 : 0][1]}"/>`).join('\n  ');

// ---- H: the owner's favourite is G2 («Ролик · тёмный»); he also liked G3's answers with a line,
// only not on the light ground. Five more on U9's dark ground around those two.
const DIM = (P) => [P.accentHi, 0.5];                              // G2's neighbours
const BLOOM = (P) => [[80, 0.5, P.accent], [26, 0.6, P.accentHi]];  // G2's light round the chosen bar
// The belly's navy line, on this ground: a line the colour of the dark ground, cut into a lit pill.
const INK = '#141C30';
const lines = (L, cols) => L.pills.map((p, i) => {
  const [col, op] = cols[i === 1 ? 1 : 0];
  return `<path d="${lineOf(L, i)}" fill="${col}" opacity="${op}"/>`;
}).join('\n  ');
// H4's chosen row carries line + tick as one centred group: a 400 line, 40 apart, R2's tick (28).
const ROWS_T = { ...ROWS, lines: [[347, 330], [242, 400], [347, 330]] };
const TICK_ROW = { pts: [696, 517, 715, 536.5, 768, 487.5], w: 28 };

// Frosted glass: a top-to-bottom gradient fill and a rim of light running down from the top edge.
function glass([x, y, w, h], [c0, o0, c1, o1], rim, glows) {
  const d = pill(x, y, w, h), g = nid('g'), s = nid('g');
  return `${glow(d, [x, y, x + w, y + h], glows)}
  <linearGradient id="${g}" gradientUnits="userSpaceOnUse" x1="0" y1="${y}" x2="0" y2="${y + h}">
    <stop offset="0" stop-color="${c0}" stop-opacity="${o0}"/><stop offset="1" stop-color="${c1}" stop-opacity="${o1}"/></linearGradient>
  <linearGradient id="${s}" gradientUnits="userSpaceOnUse" x1="0" y1="${y}" x2="0" y2="${y + h}">
    <stop offset="0" stop-color="#FFFFFF" stop-opacity="${rim}"/><stop offset="0.6" stop-color="#FFFFFF" stop-opacity="0"/></linearGradient>
  <path d="${d}" fill="url(#${g})"/>
  <path d="${pill(x + 3, y + 3, w - 6, h - 6)}" fill="none" stroke="url(#${s})" stroke-width="6"/>`;
}
// A pool of light on the ground under the mark, as if the chosen bar lit it: an ellipse 520 × 322.
const pool = (col, op) => {
  const g = nid('g');
  return `<radialGradient id="${g}" gradientUnits="userSpaceOnUse" cx="512" cy="512" r="520" gradientTransform="translate(512 512) scale(1 0.62) translate(-512 -512)">
    <stop offset="0" stop-color="${col}" stop-opacity="${op}"/><stop offset="1" stop-color="${col}" stop-opacity="0"/></radialGradient>
  <rect width="1024" height="1024" fill="url(#${g})"/>`;
};

// G2 — the SHIPPED icon since 2026-10-23. Its mark on its own (no ground), because the Android
// foreground layer carries the mark alone over the background layer (android.js).
const g2Mark = (P) => bars(BARS, [[P.accentHi, 0.55], ['#FFFFFF', 1]],
  [[[18, 0.15, P.accentHi]], [[80, 0.5, P.accent], [26, 0.6, P.accentHi]]]);

// The two renderings of a layout, with or without the tick.
const flat = (L, withTick) => (P) => `${tint(P)}
  ${L.pills.map((p, i) => `<path d="${pill(...p)}" fill="${i === 1 ? C.cobalt : C.pale}"/>`).join('\n  ')}
  ${L.pills.map((p, i) => `<path d="${lineOf(L, i)}" fill="${i === 1 ? C.white : C.navy}"/>`).join('\n  ')}${
  withTick ? `\n  ${tick(L.tick.pts, L.tick.w).map((d) => `<path d="${d}" fill="${C.white}"/>`).join('\n  ')}` : ''}`;

const relief = (L, withTick) => (P) => `${tint(P)}${
  L.pills.map(([x, y, w, h], i) => raised([pill(x, y, w, h)], [x, y, x + w, y + h], i === 1 ? M.cobalt : M.pale)).join('')}${
  L.pills.map((p, i) => raised([lineOf(L, i)], lineBox(L, i), i === 1 ? M.white : M.navy)).join('')}${
  withTick ? raised(tick(L.tick.pts, L.tick.w), tickBox(L.tick), M.white) : ''}`;

const concepts = [
  {
    id: 'R2', dir: 'R', ru: 'Плашки',
    note: 'Плашки с живота Репли на фоне нынешней иконки: светлые со строкой, выбранная — кобальт со строкой и галочкой. Плоско, как U9.',
    draw: flat(L2, true),
  },
  {
    id: 'R5', dir: 'R', ru: 'Объём',
    note: 'Те же плашки мягким объёмом, как сам Репли: свет сверху слева, синяя кромка снизу, тени на фоне, строки и галочка приподняты.',
    draw: relief(L5, true),
  },
  {
    id: 'R2W', dir: 'W', ru: 'Плашки · выделенная, с галочкой',
    note: 'R2, но выбранная плашка длиннее соседних с обеих сторон, как знак в конце ролика.',
    draw: flat(L2W, true),
  },
  {
    id: 'R2W-plain', dir: 'W', ru: 'Плашки · выделенная, без галочки',
    note: 'То же без галочки: выбор показан только цветом и длиной.',
    draw: flat(L2W, false),
  },
  {
    id: 'R5W', dir: 'W', ru: 'Объём · выделенная, с галочкой',
    note: 'R5, но выбранная плашка длиннее соседних с обеих сторон.',
    draw: relief(L5W, true),
  },
  {
    id: 'R5W-plain', dir: 'W', ru: 'Объём · выделенная, без галочки',
    note: 'То же без галочки.',
    draw: relief(L5W, false),
  },
  {
    id: 'G1', dir: 'G', ru: 'Ролик',
    note: 'Знак из ролика как иконка: голубой фон, три белые полосы по центру, выбранная длиннее и светится.',
    draw: (P) => `${blueGround(P)}
  ${bars(BARS, [['#FFFFFF', 0.55], ['#FFFFFF', 1]], [[[18, 0.18, '#FFFFFF']], [[70, 0.45, '#DCE7FF'], [22, 0.55, '#FFFFFF']]])}`,
  },
  {
    id: 'G2', dir: 'G', ru: 'Ролик · тёмный',
    note: 'Тот же знак на нынешнем тёмном фоне: соседние — приглушённый голубой, выбранная белая с голубым свечением.',
    mark: g2Mark,
    draw: (P) => `${tint(P)}
  ${g2Mark(P)}`,
  },
  {
    id: 'G3', dir: 'G', ru: 'Ролик · строки',
    note: 'Знак из ролика, но полосы — ответы со строкой посередине: выбранный белый со строкой цвета живота Репли.',
    draw: (P) => `${blueGround(P)}
  ${bars(ROWS.pills, [['#FFFFFF', 0.5], ['#FFFFFF', 1]], [[[18, 0.15, '#FFFFFF']], [[70, 0.45, '#DCE7FF'], [22, 0.5, '#FFFFFF']]])}
  ${ROWS.pills.map((p, i) => `<path d="${lineOf(ROWS, i)}" fill="${i === 1 ? C.cobalt : P.accent}" opacity="${i === 1 ? 1 : 0.9}"/>`).join('\n  ')}`,
  },
  {
    id: 'G4', dir: 'G', ru: 'Плашки посередине',
    note: 'Плашки с живота (R2W), строки посередине, выбранная светится кобальтом.',
    draw: (P) => `${tint(P)}
  ${L2C.pills.map((p, i) => glow(pill(...p), box(p), i === 1 ? [[60, 0.55, C.cobalt], [20, 0.6, '#3D82FF']] : [[22, 0.2, C.pale]])).join('\n  ')}
  ${L2C.pills.map((p, i) => `<path d="${pill(...p)}" fill="${i === 1 ? C.cobalt : C.pale}"/>`).join('\n  ')}
  ${glow(lineOf(L2C, 1), lineBox(L2C, 1), [[8, 0.6, C.white]])}
  ${L2C.pills.map((p, i) => `<path d="${lineOf(L2C, i)}" fill="${i === 1 ? C.white : C.navy}"/>`).join('\n  ')}`,
  },
  {
    id: 'H1', dir: 'H', ru: 'Тёмный · строки',
    note: 'G3 на тёмном фоне: соседние приглушённо-голубые с тёмной строкой, выбранный белый со строкой цвета Репли и свечением G2.',
    draw: (P) => `${tint(P)}
  ${bars(ROWS.pills, [DIM(P), ['#FFFFFF', 1]], [[[18, 0.15, P.accentHi]], BLOOM(P)])}
  ${lines(ROWS, [[INK, 0.75], [C.cobalt, 1]])}`,
  },
  {
    id: 'H2', dir: 'H', ru: 'Тёмный · кобальт',
    note: 'То же, но выбранный — кобальт живота Репли с белой строкой, светится кобальтом.',
    draw: (P) => `${tint(P)}
  ${bars(ROWS.pills, [DIM(P), [C.cobalt, 1]], [[[18, 0.15, P.accentHi]], [[80, 0.6, C.cobalt], [26, 0.7, '#3D82FF']]])}
  ${glow(lineOf(ROWS, 1), lineBox(ROWS, 1), [[8, 0.6, '#FFFFFF']])}
  ${lines(ROWS, [[INK, 0.75], ['#FFFFFF', 1]])}`,
  },
  {
    id: 'H3', dir: 'H', ru: 'Тёмный · сияние',
    note: 'G2 со светом сильнее: под знаком пятно света, выбранная светится ярче.',
    draw: (P) => `${tint(P)}
  ${pool(P.accent, 0.45)}
  ${bars(BARS, [[P.accentHi, 0.6], ['#FFFFFF', 1]], [[[20, 0.22, P.accentHi]], [[110, 0.55, P.accent], [40, 0.7, P.accentHi], [12, 0.6, '#FFFFFF']]])}`,
  },
  {
    id: 'H4', dir: 'H', ru: 'Тёмный · строки и галочка',
    note: 'H1 с галочкой: строка и галочка выбранного ответа стоят по центру одной группой.',
    draw: (P) => `${tint(P)}
  ${bars(ROWS_T.pills, [DIM(P), ['#FFFFFF', 1]], [[[18, 0.15, P.accentHi]], BLOOM(P)])}
  ${lines(ROWS_T, [[INK, 0.75], [C.cobalt, 1]])}
  ${tick(TICK_ROW.pts, TICK_ROW.w).map((d) => `<path d="${d}" fill="${C.cobalt}"/>`).join('\n  ')}`,
  },
  {
    id: 'H5', dir: 'H', ru: 'Тёмный · стекло',
    note: 'G2 из матового стекла: у соседних полос светится верхняя кромка, выбранная — белое стекло со свечением.',
    draw: (P) => `${tint(P)}
  ${BARS.map((r, i) => (i === 1
    ? glass(r, ['#FFFFFF', 1, '#D6E2FB', 1], 0, [...BLOOM(P), [10, 0.5, '#FFFFFF']])
    : glass(r, [P.accentHi, 0.5, P.accent, 0.3], 0.6, [[18, 0.15, P.accentHi]]))).join('\n  ')}`,
  },
];

module.exports = { concepts, capsule, tick, C, L2, L5, L2W, L5W };
