// Particle target geometry — pure functions, no DOM, no Math.random (every generator takes
// the caller's seeded `rand`, so a shape is reproducible from its seed).
//
// Space: the unit square [-1,1]², y UP. The U9 mark is inscribed exactly: a point v of the
// 1024-space maps to x = v/512 − 1, y = 1 − v/512. Every generator returns Float32Array(2n)
// of (x, y) pairs in SHUFFLED order — the field draws only the first n vertices after it
// sheds load, so any prefix must be a uniform subsample of the whole shape.

const TAU = Math.PI * 2;
const d2r = (deg) => (deg * Math.PI) / 180;

export function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// ---- geometry the page overlays must agree with (stage.js reads these) ----
const u = (v) => v / 512;
const BAR_HW = u(402); // x 110..914 → ±0.785
export const LOGO_BARS = [
  { cx: 0, cy: 1 - u(279), hw: BAR_HW, hh: u(71) }, // top    y 208..350 → centre 279
  { cx: 0, cy: 1 - u(512), hw: BAR_HW, hh: u(92) }, // middle y 420..604 → centre 512 — the chosen one
  { cx: 0, cy: 1 - u(745), hw: BAR_HW, hh: u(71) }, // bottom y 674..816 → centre 745
];

// Two chat bubbles: the client's (upper-left, tail bottom-left) and the bot's reply
// (lower-right, tail bottom-right, taller — its answer is longer).
export const BUBBLES = {
  client: { cx: -0.2, cy: 0.36, hw: 0.74, hh: 0.215, r: 0.12 },
  bot: { cx: 0.2, cy: -0.29, hw: 0.74, hh: 0.29, r: 0.12 },
};
// WhatsApp (left) and Telegram (right) marks.
export const CHANNELS = {
  wa: { cx: -0.5, cy: 0, r: 0.3 },
  tg: { cx: 0.5, cy: 0, r: 0.3 },
};

// ---- samplers ----
const gauss = (rand) => (rand() + rand() + rand() - 1.5) / 1.5; // bell-ish in [-1,1]
const jit = (rand, p, j) => [p[0] + gauss(rand) * j, p[1] + gauss(rand) * j];

function rrEdge(rand, cx, cy, hw, hh, r) {
  const sx = 2 * (hw - r), sy = 2 * (hh - r), arc = (Math.PI * r) / 2, per = 2 * sx + 2 * sy + 4 * arc;
  let t = rand() * per;
  if (t < sx) return [cx - hw + r + t, cy + hh]; t -= sx;
  if (t < sx) return [cx - hw + r + t, cy - hh]; t -= sx;
  if (t < sy) return [cx - hw, cy - hh + r + t]; t -= sy;
  if (t < sy) return [cx + hw, cy - hh + r + t]; t -= sy;
  const q = Math.min(3, Math.floor(t / arc)), a = (t - q * arc) / r;
  const C = [[cx + hw - r, cy + hh - r, 0], [cx - hw + r, cy + hh - r, Math.PI / 2], [cx - hw + r, cy - hh + r, Math.PI], [cx + hw - r, cy - hh + r, 1.5 * Math.PI]][q];
  return [C[0] + Math.cos(C[2] + a) * r, C[1] + Math.sin(C[2] + a) * r];
}
function rrFill(rand, cx, cy, hw, hh, r) {
  for (let k = 0; k < 64; k++) {
    const x = (rand() * 2 - 1) * hw, y = (rand() * 2 - 1) * hh;
    const dx = Math.max(Math.abs(x) - (hw - r), 0), dy = Math.max(Math.abs(y) - (hh - r), 0);
    if (dx * dx + dy * dy <= r * r) return [cx + x, cy + y];
  }
  return [cx, cy];
}
const edge = (b, j = 0.005, grow = 0) => (rand) => jit(rand, rrEdge(rand, b.cx, b.cy, b.hw + grow, b.hh + grow, (b.r ?? b.hh) + grow), j);
const fill = (b) => (rand) => rrFill(rand, b.cx, b.cy, b.hw, b.hh, b.r ?? b.hh);
const line = (x0, y0, x1, y1, j = 0.004) => (rand) => { const t = rand(); return jit(rand, [x0 + (x1 - x0) * t, y0 + (y1 - y0) * t], j); };
const ring = (cx, cy, r, th) => (rand) => { const a = rand() * TAU, q = r + gauss(rand) * th; return [cx + Math.cos(a) * q, cy + Math.sin(a) * q]; };
const disc = (cx, cy, r) => (rand) => { const a = rand() * TAU, q = Math.sqrt(rand()) * r; return [cx + Math.cos(a) * q, cy + Math.sin(a) * q]; };
// annular sector: angles in degrees, radii r0..r1
const band = (cx, cy, a0, a1, r0, r1) => (rand) => { const a = d2r(a0 + (a1 - a0) * rand()), q = Math.sqrt(r0 * r0 + (r1 * r1 - r0 * r0) * rand()); return [cx + Math.cos(a) * q, cy + Math.sin(a) * q]; };
function tri(a, b, c, shrink = 0) {
  if (shrink) {
    const gx = (a[0] + b[0] + c[0]) / 3, gy = (a[1] + b[1] + c[1]) / 3;
    const pull = (p) => { const dx = p[0] - gx, dy = p[1] - gy, L = Math.hypot(dx, dy) || 1, k = Math.max(0, (L - shrink) / L); return [gx + dx * k, gy + dy * k]; };
    a = pull(a); b = pull(b); c = pull(c);
  }
  return (rand) => { let s = rand(), t = rand(); if (s + t > 1) { s = 1 - s; t = 1 - t; } return [a[0] + (b[0] - a[0]) * s + (c[0] - a[0]) * t, a[1] + (b[1] - a[1]) * s + (c[1] - a[1]) * t]; };
}

// Allocate exactly n points over weighted parts, then shuffle.
function compose(n, rand, parts) {
  const out = new Float32Array(2 * n);
  const total = parts.reduce((s, p) => s + p[0], 0);
  let i = 0;
  parts.forEach(([w, f], k) => {
    const c = k === parts.length - 1 ? n - i : Math.min(n - i, Math.round((w / total) * n));
    for (let m = 0; m < c; m++, i++) {
      const p = f(rand);
      out[2 * i] = Math.max(-1.04, Math.min(1.04, p[0]));
      out[2 * i + 1] = Math.max(-1.04, Math.min(1.04, p[1]));
    }
  });
  for (let a = n - 1; a > 0; a--) {
    const b = Math.floor(rand() * (a + 1));
    const x = out[2 * a], y = out[2 * a + 1];
    out[2 * a] = out[2 * b]; out[2 * a + 1] = out[2 * b + 1];
    out[2 * b] = x; out[2 * b + 1] = y;
  }
  return out;
}

// The mark: three pills, the middle one chosen — filled, edged and ringed by a thin
// «selection» outline; the side pills are outlines with a faint fill; a little dust.
export function logoTargets(n, rand) {
  const [top, mid, bot] = LOGO_BARS;
  return compose(n, rand, [
    [33, fill(mid)], [9, edge(mid, 0.004)], [6, edge(mid, 0.0035, 0.05)],
    [7, edge(top)], [16, fill(top)],
    [7, edge(bot)], [16, fill(bot)],
    [7, disc(0, 0, 1)],
  ]);
}

// The client's message (neutral, sparse fill) and the bot's reply (accent, dense fill).
export function bubbleTargets(n, rand) {
  const { client: c, bot: b } = BUBBLES;
  const cl = c.cx - c.hw, cb = c.cy - c.hh; // client left / bottom
  const br = b.cx + b.hw, bb = b.cy - b.hh; // bot right / bottom
  return compose(n, rand, [
    [15, edge(c)], [1.6, line(cl + 0.14, cb, cl - 0.05, cb - 0.055)], [1.4, line(cl - 0.05, cb - 0.055, cl, cb + 0.095)],
    [1, tri([cl + 0.14, cb], [cl - 0.05, cb - 0.055], [cl, cb + 0.095])], [11, fill(c)],
    [20, edge(b)], [1.6, line(br - 0.14, bb, br + 0.05, bb - 0.055)], [1.4, line(br + 0.05, bb - 0.055, br, bb + 0.095)],
    [1, tri([br - 0.14, bb], [br + 0.05, bb - 0.055], [br, bb + 0.095])], [40, fill(b)],
    [5, disc(0, 0, 1)],
  ]);
}

// WhatsApp: a ring-tube with its speech tail and the handset; Telegram: a thinner ring
// around the paper plane (two facets, a hairline crease between them). A dotted bridge
// joins them — one number, both messengers.
export function channelTargets(n, rand) {
  const { wa, tg } = CHANNELS;
  const polar = (c, deg, r) => [c.cx + Math.cos(d2r(deg)) * r, c.cy + Math.sin(d2r(deg)) * r];
  const tip = polar(wa, 232, 0.41), t0 = polar(wa, 212, wa.r), t1 = polar(wa, 252, wa.r);
  const hc = { cx: wa.cx + 0.025, cy: wa.cy + 0.025 };
  const P = (x, y) => [tg.cx + x, tg.cy + y];
  const A = P(-0.19, 0.02), B = P(0.16, 0.15), C = P(0.035, -0.17), D = P(-0.035, -0.055);
  return compose(n, rand, [
    [18, ring(wa.cx, wa.cy, wa.r, 0.022)],
    [2.2, line(t0[0], t0[1], tip[0], tip[1])], [2.2, line(tip[0], tip[1], t1[0], t1[1])], [1.4, tri(t0, tip, t1)],
    [8, band(hc.cx, hc.cy, 138, 292, 0.085, 0.14)],
    [2.2, disc(...polar(hc, 124, 0.112), 0.048)], [2.2, disc(...polar(hc, 306, 0.112), 0.048)],
    [13, ring(tg.cx, tg.cy, tg.r, 0.012)],
    [10, tri(A, B, D, 0.012)], [7, tri(D, B, C, 0.012)],
    [2.5, line(A[0], A[1], B[0], B[1], 0.003)], [2, line(B[0], B[1], C[0], C[1], 0.003)],
    [4, line(-0.17, 0, 0.17, 0, 0.006)],
    [4, disc(0, 0, 1)],
  ]);
}

// Re-order `next` so particle i travels to a point near where it already is: both shapes
// are cut into equal-count slices along a diagonal key, and matched by a perpendicular key
// inside each slice (a cheap sliced transport). A morph then reads as one liquid flow
// instead of an explosion. Returns a permutation of `next`'s points.
export function rankMatch(prev, next, rand) {
  const n = prev.length / 2;
  const k1 = (x, y) => x * 0.82 + y * 0.38, k2 = (x, y) => -x * 0.38 + y * 0.82;
  const pk = new Float64Array(n), nk = new Float64Array(n), ps = new Float64Array(n), ns = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    const px = prev[2 * i], py = prev[2 * i + 1], nx = next[2 * i], ny = next[2 * i + 1];
    pk[i] = k1(px, py) + (rand() - 0.5) * 0.02; ps[i] = k2(px, py);
    nk[i] = k1(nx, ny); ns[i] = k2(nx, ny);
  }
  const idx = () => { const a = new Uint32Array(n); for (let i = 0; i < n; i++) a[i] = i; return a; };
  const po = idx().sort((a, b) => pk[a] - pk[b]), no = idx().sort((a, b) => nk[a] - nk[b]);
  const slices = Math.max(1, Math.round(Math.sqrt(n) / 3));
  const out = new Float32Array(2 * n);
  for (let s = 0; s < slices; s++) {
    const lo = Math.floor((s * n) / slices), hi = Math.floor(((s + 1) * n) / slices);
    const a = po.subarray(lo, hi).sort((x, y) => ps[x] - ps[y]);
    const b = no.subarray(lo, hi).sort((x, y) => ns[x] - ns[y]);
    for (let r = 0; r < a.length; r++) { out[2 * a[r]] = next[2 * b[r]]; out[2 * a[r] + 1] = next[2 * b[r] + 1]; }
  }
  return out;
}

// ---- tone: which palette slot a particle takes in a given shape (pure, geometry-derived) ----
// 0 accent body · 1 accent rim · 2 chosen/bright · 3 neutral rim · 4 WhatsApp · 5 Telegram · 6 dust
const sdPill = (x, y, b) => {
  const r = b.r ?? b.hh;
  const qx = Math.abs(x - b.cx) - (b.hw - r), qy = Math.abs(y - b.cy) - (b.hh - r);
  return Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) + Math.min(Math.max(qx, qy), 0) - r;
};

const TONE = {
  logo(x, y) {
    if (sdPill(x, y, LOGO_BARS[1]) <= 0.075) return 2; // the chosen bar and its selection ring
    for (const b of [LOGO_BARS[0], LOGO_BARS[2]]) {
      const d = sdPill(x, y, b);
      if (d < -0.012) return 0;
      if (d < 0.012) return 1;
    }
    return 6;
  },
  bubble(x, y) {
    const db = sdPill(x, y, BUBBLES.bot);
    if (db <= -0.02) return 0;
    if (db < 0.07) return 2; // rim + tail of the bot's reply — the brightest thing on screen
    const dc = sdPill(x, y, BUBBLES.client);
    if (dc <= -0.02) return 6;
    if (dc < 0.07) return 3;
    return 6;
  },
  channel(x, y) {
    if (x < -0.2) return 4;
    if (x > 0.2) return 5;
    return 6;
  },
};

export function tones(kind, points) {
  const f = TONE[kind];
  if (!f) throw new Error(`unknown shape kind "${kind}"`);
  const n = points.length / 2;
  const out = new Uint8Array(n);
  for (let i = 0; i < n; i++) out[i] = f(points[2 * i], points[2 * i + 1]);
  return out;
}
