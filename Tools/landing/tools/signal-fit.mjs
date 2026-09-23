// «Сигнал» fit — the in-page measurements shared by tools/probe-phone.mjs (the full sweep over phone
// viewports) and tools/check.mjs (its short-phone guard). Повторное ревью 2, N-5: the step-1 conversation
// outgrew its bubbles on every phone window under ~720 px and three reviews missed it, because every
// check ran at 390×844. Each measure* function runs INSIDE the page (page.evaluate serialises it), so it
// is self-contained.
import { stepAnchor, OVERLAY_CLEAR } from '../src/js/stage.js';

// These checks measure LAYOUT, not speed (FPS is check.mjs's perf pass, against the v1 baseline). On a
// loaded machine the FrameGovernor sheds the software-rendered field mid-run and the live state is gone
// before it is measured, so the frame clock the page sees advances by at most 19 ms a frame (under both
// governor limits, 20/24 ms). The first callback of a frame advances it, later ones share it.
export async function clampFrames(ctx) {
  await ctx.addInitScript(() => {
    const raf = window.requestAnimationFrame.bind(window);
    let vt = 0, real = 0;
    window.requestAnimationFrame = (cb) => raf((t) => {
      if (!real) { real = t; vt = t; } else if (t !== real) { vt += Math.min(t - real, 19); real = t; }
      cb(vt);
    });
  });
}

export const glide = (p, y, n = 24) => p.evaluate(async ([y, n]) => {
  const y0 = scrollY;
  for (let k = 1; k <= n; k++) { scrollTo(0, y0 + (y - y0) * k / n); await new Promise((r) => requestAnimationFrame(r)); }
}, [y, n]);


// ---- signal: geometry of one step's overlay group, in viewport px ----
// Text is measured by line boxes (Range client rects), not element boxes: a flex item inside a fixed-
// height bubble keeps its own box while its text runs out of it, and a nowrap word runs out sideways.
export function measureGroup(k) {
  const step = document.querySelectorAll('.sg-step')[k - 1];
  const g = step.querySelector('.sg-vis');
  const hdr = document.querySelector('.hdr').getBoundingClientRect().bottom;
  const lines = (el) => {
    const r = document.createRange(); r.selectNodeContents(el);
    return [...r.getClientRects()].filter((q) => q.width > 0.5 && q.height > 0.5);
  };
  const box = (el) => el.getBoundingClientRect();
  const hit = (a, b, pad = 1) => Math.min(a.right, b.right) - Math.max(a.left, b.left) > pad && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > pad;
  const objs = [...g.querySelectorAll('.sg-o')].map((el) => ({ el, name: el.dataset.o, box: box(el), lines: el.classList.contains('sg-badge') ? [] : lines(el) }));
  const live = document.querySelector('.stage').classList.contains('gl-on');
  let spill = 0; const hits = []; let edge = 0; let underHdr = 0;
  for (const o of objs) {
    if (!o.lines.length) continue;
    const t = Math.min(...o.lines.map((q) => q.top)), bt = Math.max(...o.lines.map((q) => q.bottom));
    const l = Math.min(...o.lines.map((q) => q.left)), rt = Math.max(...o.lines.map((q) => q.right));
    if (o.el.classList.contains('sg-bub')) spill = Math.max(spill, bt - o.box.bottom, t - o.box.top < 0 ? o.box.top - t : 0, rt - o.box.right, o.box.left - l);
    edge = Math.max(edge, rt - innerWidth, -l);
    if (live) underHdr = Math.max(underHdr, hdr - t);
    for (const q of objs) {
      if (q === o) continue;
      for (const ln of o.lines) if (hit(ln, q.box)) { hits.push(`${o.name}→${q.name}`); break; }
    }
  }
  // the step's own copy (eyebrow, heading, lead) and how visible it is
  const copy = [...step.querySelectorAll('.sg-f')].flatMap((f) => [...f.querySelectorAll('.eyebrow,.sg-h,.lead')].map((e) => ({ op: parseFloat(getComputedStyle(f).opacity) * parseFloat(getComputedStyle(e).opacity), lines: lines(e) })));
  let onCopy = 0;
  for (const o of objs) for (const ln of [...o.lines, ...(o.lines.length ? [] : [o.box])]) for (const c of copy) if (c.op > 0.1 && c.lines.some((cl) => hit(ln, cl))) onCopy++;
  return {
    cls: document.querySelector('.stage').className.replace(/^stage ?/, ''),
    op: +parseFloat(getComputedStyle(g).opacity).toFixed(2), on: g.classList.contains('on'),
    s: getComputedStyle(document.querySelector('.stage')).getPropertyValue('--ss').trim() || getComputedStyle(document.querySelector('.stage')).getPropertyValue('--s').trim(),
    fs: getComputedStyle(g).fontSize,
    spill: Math.round(spill * 10) / 10, hits, edge: Math.round(edge), underHdr: Math.round(underHdr), onCopy,
  };
}
// A step's rest = its morph anchor, computed by the page's own rule (stage.js stepAnchor) from numbers
// measured here: centred, or as low as the conversation needs on a screen too short for both.
export function stepGeom(k) {
  const st = document.querySelectorAll('.sg-step')[k - 1], s = st.getBoundingClientRect();
  const g = st.querySelector('.sg-vis'), f = st.querySelector('.sg-f');
  const fixed = getComputedStyle(g).position === 'fixed';
  let bottom = null;
  if (fixed && innerWidth < 900) for (const o of g.querySelectorAll('.sg-o:not(.sg-badge)')) bottom = Math.max(bottom ?? -Infinity, g.getBoundingClientRect().top + o.offsetTop + o.offsetHeight);
  return { top: s.top + scrollY, height: s.height, H: innerHeight, copy: f && bottom != null ? f.getBoundingClientRect().top - s.top : null, band: bottom, S: parseFloat(getComputedStyle(document.querySelector('.stage')).getPropertyValue('--s')) };
}
export async function stepRest(p, k) {
  const g = await p.evaluate(stepGeom, k);
  return Math.max(0, Math.round(stepAnchor(g.top, g.height, g.H, g.copy, g.band, OVERLAY_CLEAR * g.S)));
}
// every hero/step copy block that is visible (opacity > .1) vs the SHOWN overlay's text
export function sweepSample() {
  if (!document.querySelector('.stage.gl-on')) return -1;   // the live state is gone (governor/context)
  const g = [...document.querySelectorAll('.sg-vis')].find((x) => x.classList.contains('on') && parseFloat(getComputedStyle(x).opacity) > 0.3);
  if (!g) return null;
  const lines = (el) => { const r = document.createRange(); r.selectNodeContents(el); return [...r.getClientRects()].filter((q) => q.width > 0.5 && q.height > 0.5); };
  const hit = (a, b) => Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1;
  const ov = [...g.querySelectorAll('.sg-o:not(.sg-badge)')].flatMap(lines);
  const blocks = [...document.querySelectorAll('.hero-in > *, .sg-f')];
  let n = 0;
  for (const bl of blocks) {
    let op = 1; for (let e = bl; e && e !== document.body; e = e.parentElement) op *= parseFloat(getComputedStyle(e).opacity);
    if (op <= 0.1) continue;
    const ls = [...bl.querySelectorAll('*')].filter((e) => e.children.length === 0 || e.matches('p,h1,h2,li,a')).flatMap(lines);
    if (ls.some((a) => ov.some((b) => hit(a, b)))) n++;
  }
  return n;
}

// What a measureGroup() result violates. mode: 'live' (gl-on, fixed layer) | 'quiet' (gl-solid, absolute
// in the step). A phone must show the conversation at a step's rest (live); desktop keeps its columns.
export function fitViolations(at, mode, m, phone) {
  const out = [];
  if (m.spill > 0.5) out.push(`${at}: text ${m.spill}px outside its bubble (font ${m.fs}, unit ${m.s})`);
  if (m.hits.length) out.push(`${at}: text on another object: ${m.hits.join(', ')}`);
  if (m.edge > 0) out.push(`${at}: text ${m.edge}px past the screen edge`);
  if (mode === 'live' && m.underHdr > 0) out.push(`${at}: text ${m.underHdr}px under the header`);
  if (mode === 'live' && phone && !m.on) out.push(`${at}: the conversation is not shown at the step's rest`);
  if (mode === 'quiet' && m.onCopy) out.push(`${at}: the conversation lies on the step's copy (${m.onCopy})`);
  if (mode === 'quiet' && m.op < 0.9) out.push(`${at}: the quiet conversation is not shown (opacity ${m.op})`);
  return out;
}
