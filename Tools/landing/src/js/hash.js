// hash.js — a deep link lands exactly where a tap lands (повторное ревью 3, O-1).
//
// The browser's own fragment jump happens while the page is still the size it was parsed at; the stage
// then reserves its shape and the story pins (+1–2k px), and nothing re-aims it — a 1440×900 desktop
// ended up with the form 1200 px below the screen, a phone with the card past the bottom edge and still
// unrevealed. So the rule a tap follows (header.js, landingChain + settle) is run again here. A reload
// counts as a deep link: the tap writes #lead into the address.
//
// Loaded only when there IS a hash (main.js, and header.js on hashchange) — most visits have none.
import { landingChain, landingTop, landingError, OFFSET, READER } from './header.js';

const HOLD_MS = 2200;              // the landing is held this long while the page is still settling
const HOLD_STEP_MS = 120;

// The reveal of the target is applied first, and WITHOUT its animation: the jump aims at where the block
// will BE, and `.in` takes .9 s to travel the 28 px. A block the reader never saw hidden has nothing to
// reveal anyway — the same judgement final.js makes for the kinetic line on a deep link.
function showReveals(section) {
  const els = [section, ...section.querySelectorAll('[data-reveal],[data-split]')]
    .filter((el) => el.hasAttribute('data-reveal') || el.hasAttribute('data-split'));
  const moving = [...els, ...els.flatMap((el) => [...el.querySelectorAll('.w>span')])];
  for (const el of moving) el.style.transition = 'none';
  for (const el of els) el.classList.add('in');
  if (moving.length) moving[0].offsetHeight;                 // one forced layout: apply it now
  requestAnimationFrame(() => { for (const el of moving) el.style.transition = ''; });
}

// A landing target is one of the page's own anchor sections — every id a CTA points at (#how, #demo,
// #pricing, #niches, #faq, #lead) is a <section class="… sec">. #main (the skip-link's own target) and
// #top (the logo, never routed through this module on a plain click) are neither, and MUST stay excluded:
// #main wraps every section on the page, so reaching it here would reveal-all + re-aim at the whole
// document instead of doing nothing, which is what a skip-link needs (P-1, повторное ревью 5).
export function isLandingSection(el) {
  return !!el && el.tagName === 'SECTION' && !!el.classList && el.classList.contains('sec');
}

function sectionFor(hash) {
  const id = hash === undefined ? location.hash : hash;
  if (!id || id.length < 2 || id[0] !== '#') return null;
  try { const el = document.querySelector(id); return isLandingSection(el) ? el : null; } catch { return null; }
}

function aimAt(section, ctx) {
  const target = landingChain(section, ctx.lenis);
  if (landingError(target.getBoundingClientRect().top, landingTop(target, ctx.lenis))) {
    if (ctx.lenis) ctx.lenis.scrollTo(target, { offset: -OFFSET, immediate: true, force: true });
    else target.scrollIntoView({ behavior: 'auto', block: 'start' });
  }
  return target;
}

// HELD, not aimed once: everything at load (the stage's reserve, the pin, `load`, the webfonts) moves
// what is under the reader and compensates only for ITSELF, in its own moment — aiming at one of them
// left that desktop 1200 px short in one run out of three (measured). So the target is re-aimed while it
// drifts, and dropped the moment the reader touches the page. One ScrollTrigger.refresh, at the start
// (it costs a long task); header.js's `settle` takes the tail.
let holding = null;
export function landHashHeld({ settle, ctx }, hash) {
  const section = sectionFor(hash);
  if (!section) return null;
  if (holding) holding();
  let done = false, timer = 0;
  const t0 = performance.now();
  const stop = () => {
    if (done) return;
    done = true; clearTimeout(timer);
    for (const ev of READER) removeEventListener(ev, stop, true);
    holding = null;
  };
  if (ctx.ScrollTrigger) ctx.ScrollTrigger.refresh();
  showReveals(section);
  let target = aimAt(section, ctx);
  const step = () => {
    if (done) return;
    target = aimAt(section, ctx);
    if (performance.now() - t0 >= HOLD_MS) { stop(); settle(target); return; }
    timer = setTimeout(step, HOLD_STEP_MS);
  };
  timer = setTimeout(step, HOLD_STEP_MS);
  for (const ev of READER) addEventListener(ev, stop, { capture: true, passive: true });
  holding = stop;
  return target;
}
