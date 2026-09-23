// Section 10 — the first screen and the «Сигнал» transition.
//
// One fixed particle layer spans the whole stage: it holds the mark, then the two chat
// bubbles, then the WhatsApp/Telegram marks, and the scroll position decides which. The
// hero copy is plain HTML that never waits for any of this — the field fades in over it.
//
// Quality: 'low' (or no WebGL) keeps the static mark and lays the chat overlays out as an
// ordinary conversation inside each step, so the section still tells its story.
import { mulberry32, logoTargets, bubbleTargets, channelTargets, rankMatch, tones, BUBBLES, CHANNELS } from './particles/shapes.js';
import { createField } from './particles/field.js';

// Scroll position → morph index (0 = mark, 1 = bubbles, 2 = channels). Each shape HOLDS
// around its anchor — `hold` of the way in and out — so it rests while its text is read.
// Desktop holds 20 %; the phone 30 % (MORPH_HOLD_PHONE): its anchors are closer, and at 20 % the
// step-1 conversation was legible for only ~0.38 of a screen, with half-morphed blobs at rest
// between steps (линза арт-директора).
export const MORPH_HOLD = 0.2;
export const MORPH_HOLD_PHONE = 0.3;
export function morphAt(y, a, hold = MORPH_HOLD) {
  if (y <= a[0]) return 0;
  const last = a.length - 1;
  for (let k = 0; k < last; k++) {
    if (y < a[k + 1]) {
      const t = (y - a[k]) / Math.max(1, a[k + 1] - a[k]);
      const x = Math.min(1, Math.max(0, (t - hold) / (1 - 2 * hold)));
      return k + x * x * (3 - 2 * x);
    }
  }
  return last;
}

// ── Copy vs. form (ревью r3, I1) ───────────────────────────────────────────────
// The form and its HTML overlays are pinned to the viewport while the copy scrolls past.
// On desktop they live in separate columns; on phones and tablets (< 900 px) they share
// one, so the copy slides up THROUGH the form. Two rules keep text off text there:
//   • each block of copy dims as it slides up under the form (the prototype's cardFade);
//   • an overlay is not shown while visible copy crosses its text band (+ a margin, so it
//     is already fading out when the copy arrives).
// Both are pure and measured in plain numbers, so the moving geometry is tested, not
// just screenshotted at the anchors.
export const FADE_TOP = 0.85;          // ×S under the form's centre: the band starts at its bottom edge
export const FADE_BAND = 1.05;         // ×S: from fully visible to gone
export const OVERLAY_HOLD = 0.18;      // |m − k| under which shape k counts as resting
export const OVERLAY_MIN_TAIL = 0.75;  // captions leave before their rings do (r3 Minor 4)
export const OVERLAY_CLEAR = 0.3;      // ×S of clearance between an overlay and visible copy
export const TEXT_VISIBLE = 0.1;       // copy dimmer than this no longer reads as text

// The canvas fades over the last half-screen of the stage — 1 while the stage bottom is
// ≥ 0.95H below the top of the viewport, 0 once it reaches 0.5H.
export function tailAt(y, stageEnd, H) {
  return Math.min(1, Math.max(0, (stageEnd - y - H * 0.5) / (H * 0.45)));
}

// One block of copy: document top/bottom plus its fade band in viewport px. The band starts
// under the form's bottom edge, or at the block's own resting position if that is already
// higher (a landscape phone): a block is ALWAYS fully visible where it is meant to be read —
// the hero at the top of the page (it is the LCP), a step at its anchor.
export function copyUnit(top, bottom, restY, cy, s) {
  const hi = Math.min(cy + FADE_TOP * s, top - restY);
  return { top, bottom, hi, lo: hi - FADE_BAND * s };
}

// Opacity of a block at scroll y. Once the canvas has faded out (tail → 0) there is no form
// left to hide under, so the copy comes back.
export function fadeAt(u, y, tail = 1) {
  const f = Math.min(1, Math.max(0, (u.top - y - u.lo) / (u.hi - u.lo)));
  return 1 - (1 - f) * tail;
}

// Which overlay is shown: 0 = none, k = the one sitting on shape k.
// g = { stageEnd, H, bands: [{top,bottom}] per overlay (viewport px), units, margin }.
export function overlayOn(m, y, g) {
  const k = Math.round(m);
  if (k <= 0 || Math.abs(m - k) >= OVERLAY_HOLD) return 0;
  const tail = tailAt(y, g.stageEnd, g.H);
  if (tail < OVERLAY_MIN_TAIL) return 0;
  const band = g.bands && g.bands[k - 1];
  if (band && g.units) {
    for (const u of g.units) {
      if (fadeAt(u, y, tail) <= TEXT_VISIBLE) continue;
      if (u.top - y < band.bottom + g.margin && u.bottom - y > band.top - g.margin) return 0;
    }
  }
  return k;
}

// The governor's 'disable' collapses the steps (they stop reserving the shape's height), moving all
// below by ~0.9 screen. Under the reader that is scored as a shift even when compensated (0.19 in the
// pinned story, 0.87 further down while scrolling — финальное ревью, I-6), so it runs only while the
// steps are wholly BELOW the viewport; otherwise the stage keeps its quiet fallback (reserved height,
// mark back, no field) — which may last for the rest of the visit, so the reserved height is not left
// empty: 10-stage.css lays each step's conversation into it as plain bubbles (повторное ревью, N-2).
// The compensation stays as a net for a late idle callback.
// …and never while the page is moving (stage.js collapse(): an in-flight anchor jump overshoots).
export const SCROLL_QUIET_MS = 300;
export function compensateScroll(beforeTop, afterTop, y) {
  const d = afterTop - beforeTop;
  return Math.abs(d) < 1 ? y : Math.max(0, y + d);
}

// Under load — the previous frame missed the 60 Hz budget — the field draws every other frame.
// It drifts slowly, so 30 fps particles read the same, while the page's own frames (scroll,
// copy fades) get the time back; a device that keeps up never skips. This is the first, invisible
// step of shedding load; the FrameGovernor's degrade/disable stay for sustained slowness.
export const SLOW_FRAME_MS = 22;
export function drawThisFrame(dt, skippedLast) {
  return skippedLast || !(dt > SLOW_FRAME_MS);
}

// How many particles the device gets, and at what pixel ratio. A phone draws under a quarter of
// a desktop's field at a lower ratio (point size follows 1/√count, so the shape stays as dense).
// Both follow the LAYOUT as well as the tier: the phone layout (< 900 px) draws the small shape,
// whatever the tier says. The FrameGovernor halves whatever we picked if the frames say we were wrong.
export function particleCount(tier, cores, width) {
  // the phone layout draws the shape at ~168 px against up to 320 on desktop: a desktop count
  // there is the same form three times as dense and three times as expensive
  if (tier !== 'high' || width < 900) return 5000;
  return (cores || 0) >= 8 ? 22000 : 15000;
}
export function fieldDpr(tier, dpr, width) {
  return Math.min(dpr || 1, tier === 'high' && !(width < 900) ? 1.5 : 1.25);
}

// The phone layout's shape unit S (px). Up to 20 % of the viewport height — but on a short screen
// (a mass Android with its URL bar: 360×660, 390×700) that put the trial conditions of the first screen
// under the fold, the decorative mark taking the top ~290 px (линза владельца). Below ~800 px the share
// eases down to 14 % at 660 px — linearly in px, so the composition never jumps at a breakpoint.
// 10-stage.css carries the same formula for the no-JS first paint.
export function phoneScale(w, vh) {
  return Math.min((w - 40) / 1.92, Math.min(vh * 0.2, Math.max(vh * 0.14, vh * 0.483 - 226.3)));
}

// The steps' unit on a phone (N-5): font = STEP_FONT × unit, so the unit stays ≥ 13px/.077 while the width
// allows (phoneScale shrinks only the MARK on a short screen); past the width cap the text scales with it.
export const STEP_FONT = 0.077;
export const STEP_UNIT_MIN = 168.8;
export function stepScale(w, s) {
  return Math.max(s, Math.min((w - 40) / 1.92, STEP_UNIT_MIN));
}

// A step's morph anchor: centred, or — if that lifts its copy into the conversation's band + clearance (a
// short phone) — as low as the conversation needs. No band (desktop) → centred.
export function stepAnchor(top, height, H, copyOffset, bandBottom, margin) {
  const centred = top + (height - H) / 2;
  if (copyOffset == null || bandBottom == null) return centred;
  return Math.min(centred, top + copyOffset - bandBottom - margin - 2);
}

// Where each overlay element sits, in shape units (x/y = top-left corner, y UP).
function overlayPlacement() {
  const { client: c, bot: b } = BUBBLES;
  const { wa, tg } = CHANNELS;
  const cap = (ch) => ({ x: ch.cx - 0.45, y: -(ch.r + 0.12), w: 0.9 });
  const badge = (ch) => ({ x: ch.cx - 0.21, y: 0.21, w: 0.42, h: 0.42 });
  return {
    'client-lbl': { x: c.cx - c.hw, y: c.cy + c.hh + 0.1, w: 2 * c.hw },
    client: { x: c.cx - c.hw, y: c.cy + c.hh, w: 2 * c.hw, h: 2 * c.hh },
    bot: { x: b.cx - b.hw, y: b.cy + b.hh, w: 2 * b.hw, h: 2 * b.hh },
    'bot-lbl': { x: b.cx - b.hw, y: b.cy - b.hh - 0.035, w: 2 * b.hw },
    'wa-badge': badge(wa), 'wa-lbl': cap(wa),
    'tg-badge': badge(tg), 'tg-lbl': cap(tg),
  };
}

export function init(root, ctx) {
  const canvas = root.querySelector('.stage-gl');
  const groups = [...root.querySelectorAll('.sg-vis')];
  const steps = [...root.querySelectorAll('.sg-step')];
  const heroWrap = root.querySelector('.hero .wrap');
  // blocks of copy that fade under the form on phones: the hero's rows (none of them is a
  // reveal), and per step the .sg-f wrappers — reveal owns the opacity of the eyebrow, H3
  // and lead themselves, so the fade has to sit one level up
  const heroCopy = heroWrap ? [...heroWrap.querySelectorAll('.hero-in > *')] : [];
  const stepCopy = steps.map((s) => [...s.querySelectorAll('.sg-f')]);

  const place = overlayPlacement();
  for (const el of root.querySelectorAll('[data-o]')) {
    const p = place[el.dataset.o];
    if (!p) continue;
    el.style.setProperty('--x', p.x); el.style.setProperty('--y', p.y);
    el.style.setProperty('--w', p.w); if (p.h) el.style.setProperty('--h', p.h);
  }

  if (ctx.tier === 'low' || ctx.reduced || !canvas) return;

  let field = null, raf = 0, running = false, onScreen = true, disposed = false, down = false;
  let fade = 1, skipped = false;
  // S/CY — the mark's unit and centre, SS/CYS — the steps'
  let W = 0, H = 0, S = 1, CX = 0, CY = 0, SS = 1, CYS = 0, baseH = 0, baseW = 0, desk = false;
  const view = () => ({ cx: CX, cy: CY, s: S, ss: SS, cys: CYS });
  let anchors = [0, 1, 2], stageEnd = 1, morph = 0, last = 0, curOn = -1, scrolled = false, first = true;
  // what overlayOn() needs, kept in one object so the frame allocates nothing; units/bands
  // stay empty on desktop (separate columns) and after a shutdown
  const geo = { stageEnd: 1, H: 1, bands: [], units: [], margin: 0 };
  const fine = matchMedia('(pointer: fine)');
  const count = particleCount(ctx.tier, navigator.hardwareConcurrency, window.innerWidth);

  function measure() {
    const prevW = baseW;
    W = window.innerWidth; H = window.innerHeight;
    desk = W >= 900;
    // On phones the height is frozen so the URL bar cannot re-lay-out the scene — but a
    // rotation is a real new viewport, and it is told apart by the width changing.
    if (!baseH || desk || W !== prevW) baseH = H;
    baseW = W;
    const vh = Math.max(baseH, 1);
    if (desk) { S = Math.min(W * 0.20, vh * 0.35, 320); CX = Math.min(W * 0.75, W - 1.05 * S - 40); CY = vh * 0.52; SS = S; CYS = CY; }
    else { S = phoneScale(W, vh); CX = W / 2; CY = 84 + S * 0.68; SS = stepScale(W, S); CYS = 84 + SS * 0.68; }
    // rounded once: the canvas and the overlays use the same numbers
    const r10 = (v) => Math.round(v * 10) / 10;
    S = r10(S); CX = r10(CX); CY = r10(CY); SS = r10(SS); CYS = r10(CYS);
    const st = root.style;
    st.setProperty('--s', `${S}px`);
    st.setProperty('--cx', `${CX}px`);
    st.setProperty('--cy', `${CY}px`);
    st.setProperty('--ss', `${SS}px`);
    st.setProperty('--cys', `${CYS}px`);
    if (desk && heroWrap) {
      const r = heroWrap.getBoundingClientRect();
      const padL = parseFloat(getComputedStyle(heroWrap).paddingLeft) || 0;
      // 0.82 = реальный габарит нарисованной формы (знак не доходит до ±S), 32 = зазор до
      // текста. Прежние 0.98/40 резервировали пустоту и зажимали заголовок (ревью r2, I3).
      st.setProperty('--textw', `${Math.max(360, Math.round(CX - 0.82 * S - 32 - (r.left + padL)))}px`);
    } else st.removeProperty('--textw');
    anchorsFromLayout();
  }

  function anchorsFromLayout() {
    const y = window.scrollY;
    const r = root.getBoundingClientRect();
    measureBands();
    anchors = [r.top + y];
    steps.forEach((s, i) => {
      const b = s.getBoundingClientRect();
      const f = stepCopy[i][0], band = geo.bands[i];
      const copy = f && band ? f.getBoundingClientRect().top - b.top : null;
      anchors.push(Math.max(anchors[i] + 1, stepAnchor(b.top + y, b.height, H, copy, band ? band.bottom : null, OVERLAY_CLEAR * S)));
    });
    stageEnd = r.bottom + y;
    trackCopy(y);
  }

  // Phones: each overlay's text band (viewport px, fixed layer). offsetTop, not the rect: the rect carries
  // the resting translateY(.6em) of a hidden overlay.
  function measureBands() {
    geo.bands = desk || down ? [] : groups.map((g) => {
      const gt = g.getBoundingClientRect().top;
      let top = Infinity, bottom = -Infinity;
      for (const o of g.querySelectorAll('.sg-o:not(.sg-badge)')) {
        top = Math.min(top, gt + o.offsetTop);
        bottom = Math.max(bottom, gt + o.offsetTop + o.offsetHeight);
      }
      return top < bottom ? { top, bottom } : null;
    });
  }

  // Phones/tablets: where each block of copy sits (document px). Read on layout changes only; the frame
  // just does arithmetic on the numbers.
  function trackCopy(y) {
    const prev = new Map(geo.units.map((u) => [u.el, u.v]));
    geo.units = [];
    // copy fades against the step shapes; the clearance keeps the mark's unit (a short screen is short)
    geo.stageEnd = stageEnd; geo.H = H; geo.margin = OVERLAY_CLEAR * S;
    if (desk || down) { for (const el of prev.keys()) { el.style.opacity = ''; el.style.pointerEvents = ''; } return; }
    const add = (el, restY) => {
      const b = el.getBoundingClientRect();
      // keep the value already written, so a re-measure cannot flash a dimmed block to 1
      geo.units.push({ el, v: prev.has(el) ? prev.get(el) : 1, ...copyUnit(b.top + y, b.bottom + y, restY, CYS, SS) });
      prev.delete(el);
    };
    heroCopy.forEach((el) => add(el, anchors[0]));
    stepCopy.forEach((list, i) => list.forEach((el) => add(el, anchors[i + 1])));
    for (const el of prev.keys()) { el.style.opacity = ''; el.style.pointerEvents = ''; }
  }

  // Write each block's fade — only when its rounded value changes. A block too dim to read also
  // stops taking taps (the hero CTAs sit under the step-1 bubbles once faded), and a block that
  // holds keyboard focus never fades: a focused control must be visible (ревью T5 r4, Minor 2).
  let focusEl = null;
  function fadeCopy(y, tail) {
    for (const u of geo.units) {
      const v = focusEl && u.el.contains(focusEl) ? 1 : Math.round(fadeAt(u, y, tail) * 50) / 50;
      if (v !== u.v) {
        u.v = v; u.el.style.opacity = v === 1 ? '' : `${v}`;
        u.el.style.pointerEvents = v <= TEXT_VISIBLE ? 'none' : '';
      }
    }
  }
  const onFocusIn = (e) => { focusEl = e.target; };
  const onFocusOut = () => { focusEl = null; };

  function overlays(m, y) {
    if (down) return;
    const on = overlayOn(m, y, geo);
    if (on === curOn) return;
    curOn = on;
    groups.forEach((g, i) => g.classList.toggle('on', i + 1 === on));
  }

  function frame(now) {
    if (!field || down || disposed) { stop(); return; }
    raf = requestAnimationFrame(frame);
    const dt = last ? now - last : 16.7; last = now;
    const y = window.scrollY;
    const target = morphAt(y, anchors, desk ? MORPH_HOLD : MORPH_HOLD_PHONE);
    morph += (target - morph) * (1 - Math.exp(-Math.min(0.05, dt / 1000) * 5.5));
    if (Math.abs(target - morph) < 0.0008) morph = target;
    // The layer is pinned to the viewport, so the vignette cannot hide the field where the
    // stage ENDS: without this the WA/TG marks hang a full screen into the next section.
    // The canvas alone fades (the glows are background light), inline so no custom property
    // invalidates the subtree, and only while the tail is actually crossing the screen.
    const tail = tailAt(y, stageEnd, H);
    // A fully faded canvas is not drawn at all (and that is not a «skipped» frame): at the stage's
    // end and at the start of the story every second draw was of an invisible field (M-1).
    if (tail > 0 && drawThisFrame(dt, skipped)) { skipped = false; field.setMorph(morph); field.render(now); }
    else if (tail > 0) skipped = true;
    const t100 = Math.round(tail * 100) / 100;
    if (t100 !== fade) { fade = t100; canvas.style.opacity = t100 === 1 ? '' : `${t100}`; }
    fadeCopy(y, tail);
    overlays(morph, y);
    if ((y > 24) !== scrolled) { scrolled = y > 24; root.classList.toggle('stage-scrolled', scrolled); }
    if (first) { first = false; root.classList.add('gl-on'); }
    const verdict = ctx.governor.sample(dt);
    if (verdict === 'degrade') { field.setCount(count * 0.5); field.setDpr(1); }
    else if (verdict === 'disable') shutDown();
  }

  function start() {
    if (running || disposed || down || !field || !onScreen || document.hidden) return;
    running = true; last = 0;
    raf = requestAnimationFrame(frame);
  }
  function stop() { running = false; cancelAnimationFrame(raf); }

  // Hand the section back to the in-flow conversation. 'gl-init' is what puts the overlays
  // on the fixed particle layer (and 'gl-on' what makes them visible), so dropping both is
  // the whole fallback: text visible, nothing pinned over step 2. One way back, used by the
  // shutdown and by the module teardown alike.
  function fallbackLayout() {
    curOn = -1;
    for (const g of groups) g.classList.remove('on');
    // no form left to hide under: every block of copy back to full opacity
    for (const u of geo.units) { u.el.style.opacity = ''; u.el.style.pointerEvents = ''; }
    geo.units = []; geo.bands = [];
    root.classList.remove('gl-on', 'gl-init');
  }

  // The field gave up (or the context died). 'gl-solid' stays as the latch (canvas fades out,
  // steps stop reserving the shape's height) and start() refuses to re-enter, so a
  // visibilitychange during the 700 ms before the field is freed cannot revive the loop onto
  // a null field. 'disposed' is checked too: field.destroy() queues a webglcontextlost of its
  // own, and that event must not mutate classes on a section that is already torn down.
  // The load is shed THIS frame (no more draws, canvas and captions hidden — class toggles only; the
  // captions come back ~.7 s later as plain bubbles in the steps' reserved height, CSS only);
  // the collapse itself (two forced layouts + a ScrollTrigger.refresh, 94 ms at 1× and ~0.7 s at 4×
  // on the device it is meant to protect) waits for an idle moment (линза перф-инженера, M-2).
  let collapseAt = 0, collapseIO = null, lastScrollAt = 0;
  // only while a collapse is pending (the degraded path): when the page last moved
  const markScroll = () => { lastScrollAt = performance.now(); };
  function shutDown() {
    if (down || disposed) return;
    down = true;
    stop();
    canvas.style.opacity = '0';
    curOn = -1;
    for (const g of groups) g.classList.remove('on');
    // no form left to hide under: the dimmed copy comes back now (style writes, no layout read)
    for (const u of geo.units) { u.el.style.opacity = ''; u.el.style.pointerEvents = ''; u.v = 1; }
    // the latch right away: the static mark comes back and the glow stays; gl-init (the reserved
    // step height) goes with the collapse
    root.classList.remove('gl-on');
    root.classList.add('gl-solid');
    // freed once the canvas has faded (.6 s), not by the collapse, which may never come
    setTimeout(() => { if (field) { field.destroy(); field = null; } }, 700);
    addEventListener('scroll', markScroll, { passive: true });
    scheduleCollapse();
  }
  // Collapse only while the first step is wholly below the viewport (note at compensateScroll).
  const idle = window.requestIdleCallback || ((fn) => setTimeout(fn, 250));
  const stepsBelow = (top, bottom) => top >= bottom;
  function scheduleCollapse() {
    if (disposed || collapseIO || collapseAt || !steps.length) return;
    // rootMargin -1px: a hero of exactly 100svh puts the first step's top ON the viewport's bottom edge
    // at the top of the page, and an edge-adjacent box counts as intersecting — at 1440×900 and
    // 768×1024 the steps were wholly below and the collapse still never came (повторное ревью, N-2)
    collapseIO = new IntersectionObserver((es) => {
      const e = es[es.length - 1];
      if (!collapseIO || e.isIntersecting || !stepsBelow(e.boundingClientRect.top, e.rootBounds ? e.rootBounds.bottom : window.innerHeight)) return;
      collapseIO.disconnect(); collapseIO = null;
      collapseAt = idle(collapse, { timeout: 1500 });
    }, { rootMargin: '0px 0px -1px 0px' });
    collapseIO.observe(steps[0]);
  }
  function collapse() {
    collapseAt = 0;
    if (disposed) return;
    // never under a scroll in flight: an anchor jump (Lenis or native smooth) keeps the absolute target it
    // took at the tap, and the steps shrinking ~0.9 screen below the reader would land them that far past
    // the section — the hero CTA overshot #lead by ~1100 px when the field died at the top on a loaded
    // machine. Once the page has been still for SCROLL_QUIET_MS, the next idle moment decides again.
    if (performance.now() - lastScrollAt < SCROLL_QUIET_MS || (ctx.lenis && ctx.lenis.isScrolling)) { collapseAt = idle(collapse, { timeout: 1500 }); return; }
    // the reader may have scrolled down to the steps while the idle callback waited
    if (!stepsBelow(steps[0].getBoundingClientRect().top, window.innerHeight)) { scheduleCollapse(); return; }
    // one-time path: two forced layouts (before/after) are fine here, never in the frame
    const ref = pickAnchor();
    const before = ref ? ref.getBoundingClientRect().top : 0;
    fallbackLayout();
    root.classList.add('gl-solid');
    canvas.style.opacity = '0';
    if (ref) {
      const y = window.scrollY;
      const to = compensateScroll(before, ref.getBoundingClientRect().top, y);
      if (to !== y) {
        if (ctx.lenis) ctx.lenis.scrollTo(to, { immediate: true, force: true });
        else window.scrollTo({ top: to, behavior: 'instant' });
      }
    }
    removeEventListener('scroll', markScroll);
    // everything below moved by the collapse: the story's triggers re-measure
    if (ctx.ScrollTrigger) ctx.ScrollTrigger.refresh();
  }

  // The block the reader is looking at: the first copy block, later section or the footer whose bottom
  // is below the fixed header (the list used to stop at the next section: no anchor past the story).
  function pickAnchor() {
    const list = [...heroCopy, ...stepCopy.flat()];
    for (let el = root.nextElementSibling; el; el = el.nextElementSibling) list.push(el);
    const ftr = document.querySelector('.ftr');
    if (ftr) list.push(ftr);
    for (const el of list) if (el && el.getBoundingClientRect().bottom > 68) return el;
    return null;
  }

  let rz = 0;
  const onResize = () => {
    cancelAnimationFrame(rz);
    rz = requestAnimationFrame(() => {
      const heightOnly = window.innerWidth === W && Math.abs(window.innerHeight - H) < 140;
      if (heightOnly && !desk) { // mobile URL bar
        // The composition (--s/--cx/--cy) is deliberately frozen so the bar cannot re-lay-out
        // the scene — but the canvas box follows the viewport, so its BACKING STORE has to as
        // well, or the browser stretches the whole field while the text stays put (r2, I2).
        H = window.innerHeight;
        if (field) field.resize(W, H, view());
        anchorsFromLayout();
        return;
      }
      measure();
      if (field) field.resize(W, H, view());
    });
  };
  const onPointer = (e) => {
    if (!field || (e.pointerType && e.pointerType !== 'mouse')) return;
    field.setPointer((e.clientX - CX) / S, (CY - e.clientY) / S, true);
  };
  const onLeave = () => field && field.setPointer(9, 9, false);
  const onLost = (e) => { e.preventDefault(); shutDown(); };
  const onVisibility = () => (document.hidden ? stop() : start());
  const io = new IntersectionObserver((es) => {
    onScreen = es[0].isIntersecting;
    root.classList.toggle('stage-out', !onScreen);
    if (onScreen) start(); else { stop(); overlays(0, 0); }
  }, { rootMargin: '10% 0px' });
  const ro = new ResizeObserver(() => anchorsFromLayout());

  // The shapes are ~200 ms of pure math on a phone at 6× CPU (rankMatch 91, compose 63, targets 49):
  // built in slices with a yield between them, so a tap on the hero CTA is not stuck behind one long
  // task while the SVG mark is on screen anyway (линза перф-инженера, I-2). Same calls, same order —
  // the seeded random sequence and therefore the shapes are identical.
  const pause = () => new Promise((r) => setTimeout(r, 0));
  async function boot() {
    if (disposed) return;
    field = createField(canvas, { count, dpr: fieldDpr(ctx.tier, window.devicePixelRatio, window.innerWidth) });
    if (!field) return; // no WebGL — the static mark and the in-flow conversation stay
    const rand = mulberry32(20260920);
    await pause(); if (disposed || !field) return;
    const logo = logoTargets(count, rand);
    const bubbleRaw = bubbleTargets(count, rand);
    await pause(); if (disposed || !field) return;
    const bubble = rankMatch(logo, bubbleRaw, rand);
    await pause(); if (disposed || !field) return;
    const channel = rankMatch(bubble, channelTargets(count, rand), rand);
    await pause(); if (disposed || !field) return;
    field.setShapes([logo, bubble, channel], [tones('logo', logo), tones('bubble', bubble), tones('channel', channel)]);
    // Put the overlays into their GL resting state NOW — fixed layer, opacity 0, no
    // transition — and force the browser to resolve it. 'gl-on' (the first frame, one rAF
    // later) then only switches the transitions on, so the groups never animate down from
    // the in-flow layout and the conversation cannot flash across the hero (ревью r2, I1).
    // 'gl-init' makes the steps reserve the shape's height: the stage grows by ~half a screen
    // per step. Everything below moves — so (1) a reader who is already below the stage (deep
    // link, reload) keeps what they look at in place (Safari has no scroll anchoring), and (2)
    // pinned sections re-measure: the story booted before this point and pinned ~half a screen
    // early, its copy drawn over the signal's last lead (Task 12).
    const next = root.getBoundingClientRect().bottom <= 0 ? root.nextElementSibling : null;
    const nextTop = next ? next.getBoundingClientRect().top : 0;
    root.classList.add('gl-init');
    if (groups[0]) getComputedStyle(groups[0]).opacity;
    measure();
    field.resize(W, H, view());
    if (ctx.ScrollTrigger) ctx.ScrollTrigger.refresh();
    if (next) {
      const y = window.scrollY, to = compensateScroll(nextTop, next.getBoundingClientRect().top, y);
      if (to !== y) {
        if (ctx.lenis) ctx.lenis.scrollTo(to, { immediate: true, force: true });
        else window.scrollTo({ top: to, behavior: 'instant' });
      }
    }
    addEventListener('resize', onResize, { passive: true });
    addEventListener('orientationchange', onResize, { passive: true });
    document.addEventListener('visibilitychange', onVisibility);
    canvas.addEventListener('webglcontextlost', onLost);
    if (fine.matches) {
      addEventListener('pointermove', onPointer, { passive: true });
      document.documentElement.addEventListener('mouseleave', onLeave);
    }
    io.observe(root);
    ro.observe(root);
    root.addEventListener('focusin', onFocusIn);
    root.addEventListener('focusout', onFocusOut);
    // only while fonts are still loading: once they are in, ready is already resolved and the
    // re-measure would be one more forced layout in the busiest second (I-2)
    if (document.fonts && document.fonts.status === 'loading') document.fonts.ready.then(() => { if (!disposed) measure(); });
    addEventListener('load', anchorsFromLayout, { once: true });
    start();
  }

  // Let the first paint happen before the buffers are built.
  requestAnimationFrame(() => setTimeout(boot, 0));

  return {
    destroy() {
      disposed = true;
      stop();
      if (collapseAt) (window.cancelIdleCallback || clearTimeout)(collapseAt);
      if (collapseIO) { collapseIO.disconnect(); collapseIO = null; }
      removeEventListener('scroll', markScroll);
      io.disconnect(); ro.disconnect();
      removeEventListener('resize', onResize);
      removeEventListener('orientationchange', onResize);
      removeEventListener('pointermove', onPointer);
      document.removeEventListener('visibilitychange', onVisibility);
      document.documentElement.removeEventListener('mouseleave', onLeave);
      canvas.removeEventListener('webglcontextlost', onLost);
      root.removeEventListener('focusin', onFocusIn);
      root.removeEventListener('focusout', onFocusOut);
      fallbackLayout();
      root.classList.remove('gl-solid');
      canvas.style.opacity = '';
      if (field) { field.destroy(); field = null; }
    },
  };
}
