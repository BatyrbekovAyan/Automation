// header.js — «scrolled» state + anchor links routed through Lenis when present.
//
// An anchor jump is SETTLED: when the scroll comes to rest, the target is checked against the
// place it was meant to land (its scroll-margin, or Lenis's offset) and put there if the page
// moved under the flight — a late ScrollTrigger.refresh (webfonts, `load`) re-measures the pinned
// story mid-flight and ends or deflects a native smooth scroll, and Lenis flies to a number it
// computed at the click (ревью T6 r4, Important 2). One correction, never a loop; a wheel/touch/key
// from the reader cancels it — their scroll wins.
export const OFFSET = 72;          // Lenis offset: the 68 px fixed header + 4
const TOLERANCE = 4;               // px — closer than this is «landed»
const QUIET_MS = 140;              // no scroll for this long = at rest (Safari has no scrollend)
const START_MS = 1500;             // no scroll at all this long after the click = it is not coming
const GIVE_UP_MS = 5000;
export const READER = Object.freeze(['wheel', 'touchstart', 'keydown', 'pointerdown']);  // their scroll wins

// Where the target's top should sit in the viewport after the jump.
export function landingTop(target, lenis) {
  if (lenis) return OFFSET;
  return parseFloat(getComputedStyle(target).scrollMarginTop) || 0;
}

// How far the target is from where it should be (+ = still below its place).
export function landingError(targetTop, wantTop) {
  const d = targetTop - wantTop;
  return Math.abs(d) <= TOLERANCE ? 0 : d;
}

// A section may name nearer landing points (data-land, from the most context to the least) and what the
// jump is FOR (data-land-need): the lead's kinetic line hid the form below the fold on a phone (N-6), so
// the jump lands on the form card there — and on a screen too short for the card's heading AND its phone
// field (360×540, 375×548: an SE in Safari) on the fields themselves, the field being what it is for (O-2).
const NEED_PAD = 8;
export function pickLanding(wantTop, needBottom, viewportH) {
  return wantTop + needBottom <= viewportH - NEED_PAD ? 'section' : 'land';
}
// Distances are LAYOUT ones (offsetTop): [data-reveal] holds the card — and the phone field in it — 28 px
// low until it fires, and deciding on that painted position picked a different point for a tap than for a
// deep link, which reveals first (360×640: 567 px against a 632 budget, 539 after). Only the difference
// is used, so the walk may overshoot the section.
function layoutTop(el) {
  let y = 0;
  for (let n = el; n; n = n.offsetParent) y += n.offsetTop;
  return y;
}
// Keeps the point it stands on as soon as the need fits under it, steps on otherwise, ends on the last.
export function landingChain(section, lenis, win = window) {
  const ds = section.dataset || {};
  const need = ds.landNeed ? section.querySelector(ds.landNeed) : null;
  let target = section;
  for (const sel of String(ds.land || '').split(',').map((s) => s.trim()).filter(Boolean)) {
    const next = section.querySelector(sel);
    if (!next) break;
    const what = need || next;
    const off = layoutTop(what) + what.offsetHeight - layoutTop(target);
    if (pickLanding(landingTop(target, lenis), off, win.innerHeight) === 'section') break;
    target = next;
  }
  return target;
}

export function initHeader(hdr, ctx) {
  if (hdr) {
    const onScroll = () => hdr.classList.toggle('scrolled', window.scrollY > 24);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
  }

  let cancelSettle = null;
  function settle(target) {
    if (cancelSettle) cancelSettle();
    let timer = 0, done = false;
    const t0 = performance.now();
    const stop = () => {
      if (done) return;
      done = true; clearTimeout(timer);
      removeEventListener('scroll', onMove);
      removeEventListener('scrollend', onEnd);
      for (const ev of READER) removeEventListener(ev, stop, true);
      cancelSettle = null;
    };
    // At rest = scrollY still over two frames. A quiet timer alone misfired under load, where a smooth
    // scroll's frames come 150–300 ms apart: the correction fired mid-flight and, as the note below
    // says, landed past the target — at the page's end, the form 400–750 px above the screen (N-6 probe).
    const check = () => {
      if (done) return;
      const y0 = window.scrollY;
      requestAnimationFrame(() => requestAnimationFrame(() => {
        if (done) return;
        if (Math.abs(window.scrollY - y0) > 0.5) { clearTimeout(timer); timer = setTimeout(check, QUIET_MS); return; }
        judge();
      }));
    };
    const judge = () => {
      const err = landingError(target.getBoundingClientRect().top, landingTop(target, ctx.lenis));
      stop();
      if (!err) return;
      // Lenis: a short second leg reads as the same flight decelerating; native: jump, since a
      // second smooth scroll can be deflected by the same late refresh
      if (ctx.lenis) ctx.lenis.scrollTo(target, { offset: -OFFSET, duration: 0.6, force: true });
      else target.scrollIntoView({ behavior: 'auto', block: 'start' });
    };
    // Judge only a scroll that has come to REST: never before the flight has started (on a busy
    // main thread a smooth scroll can begin hundreds of ms after the click, and a correcting jump
    // issued then does not cancel it — it lands past the target instead).
    let moved = false;
    const onEnd = () => { if (moved) check(); };
    const onMove = () => {
      moved = true;
      clearTimeout(timer);
      timer = setTimeout(performance.now() - t0 > GIVE_UP_MS ? stop : check, QUIET_MS);
    };
    addEventListener('scroll', onMove, { passive: true });
    addEventListener('scrollend', onEnd, { passive: true });
    for (const ev of READER) addEventListener(ev, stop, { capture: true, passive: true });
    timer = setTimeout(check, START_MS);   // a jump that never scrolled (already there, or blocked)
    cancelSettle = stop;
    return stop;
  }

  document.addEventListener('click', (e) => {
    const a = e.target.closest('a[href^="#"]');
    // the skip link keeps the browser's own jump: it also moves the keyboard's starting point
    if (!a || a.classList.contains('skip')) return;
    const id = a.getAttribute('href');
    const section = id.length > 1 ? document.querySelector(id) : document.body;
    if (!section) return;
    e.preventDefault();
    const target = section === document.body ? section : landingChain(section, ctx.lenis);
    if (ctx.lenis) {
      ctx.lenis.scrollTo(target === document.body ? 0 : target, { offset: -OFFSET, duration: 1.4, onComplete: () => { if (target !== document.body) settle(target); } });
    } else {
      target.scrollIntoView({ behavior: ctx.reduced ? 'auto' : 'smooth', block: 'start' });
      if (target !== document.body) settle(target);
    }
    history.replaceState(null, '', id);
    // A link activated from the keyboard (click with detail 0) takes the focus along, the way a
    // native fragment jump moves the keyboard's starting point — otherwise the next Tab would go
    // back to the header. The section gets tabindex -1 (focusable by script only, no ring: CSS).
    if (e.detail === 0 && section !== document.body) {
      if (!section.hasAttribute('tabindex')) section.setAttribute('tabindex', '-1');
      section.focus({ preventScroll: true });
    }
  });

  // A hash the page is OPENED with (or one a Back/Forward lands on) is re-aimed the same way a tap is,
  // by a module of its own: it is dead weight on every visit that has no hash, and most have none (O-1).
  const api = { settle, ctx };
  addEventListener('hashchange', () => { if (location.hash) import('./hash.js').then((m) => m.landHashHeld(api)); });

  return api;
}
