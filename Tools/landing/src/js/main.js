// Entry: decide the quality tier, bring up motion, then lazily init each section module
// when its root nears the viewport. Every module follows the contract in the plan:
// export function init(root, ctx) → void | { destroy() }.
import { bootHead, loadVendor, FrameGovernor } from './quality.js';
import { createMotion } from './motion.js';
import { initReveals } from './reveal.js';
import { initHeader } from './header.js';
import { initMagnetic } from './magnetic.js';

const MODULES = {
  stage: () => import('./stage.js'),
  story: () => import('./story.js'),
  demo: () => import('./demo.js'),
  niches: () => import('./niches.js'),
  pricing: () => import('./pricing.js'),
  final: () => import('./final.js'),
};

// The tier was decided in <head> (quality.js's bootHead, inlined by build.mjs), which also started the
// vendor DOWNLOADS this tier needs (preloads — none on 'low'); they run here, after the first paint.
// Without the inline boot (a blocked inline script) the same function runs here, one round trip later.
const head = window.__crBoot || bootHead(window, new URL('../vendor/', import.meta.url).href);
const { env, tier } = head;
// A touch device sheds load a little earlier: 20 ms ≈ 50 fps, the spec budget is ≥ 55 (линза
// перф-инженера, M-2); the desktop keeps 24 ms (a software-rendered check machine runs ~45 fps there).
const ctx = { tier, reduced: env.reducedMotion, governor: new FrameGovernor(env.coarse ? { limitMs: 20 } : {}), gsap: null, ScrollTrigger: null, lenis: null };

// Neither needs GSAP: the header reads ctx.lenis at click time, reveals are plain CSS + IO.
const header = initHeader(document.getElementById('hdr'), ctx);
initReveals(document, ctx);
// The head guard (index.html) shows every reveal on DOMContentLoaded unless this line ran: a module
// graph that failed to load or parse must not leave the page below the hero at opacity 0 (I-1).
window.__crMain = true;

const pending = new Map();
for (const root of document.querySelectorAll('[data-module]')) pending.set(root, root.dataset.module);

function boot(root) {
  const name = pending.get(root);
  if (!name) return Promise.resolve();
  pending.delete(root);
  const load = MODULES[name];
  if (!load) { console.warn(`[landing] unknown module "${name}"`); return Promise.resolve(); }
  return load().then((m) => m.init(root, ctx)).catch((e) => console.error(`[landing] ${name}:`, e));
}

// The stage (first screen) and the story boot immediately; the rest 1.5 screens ahead of the
// viewport. The story is eager because going live PINS it and grows the page by 1–2k px: booted
// lazily, that growth landed in the middle of an anchor jump from the top (the hero CTA stopped
// thousands of px short of the form) — so it happens once, at load, before anything is measured
// (ревью T6 r4, Important 2; the story compensates its own growth when it is above the reader).
const EAGER = new Set(['stage', 'story']);
loadVendor(window, head).then(() => {
  Object.assign(ctx, createMotion(tier, window));
  if (tier !== 'low') initMagnetic(document);
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) { io.unobserve(e.target); boot(e.target); }
  }, { rootMargin: '150% 0px 150% 0px' });
  const eager = [];
  for (const root of pending.keys()) {
    if (EAGER.has(root.dataset.module)) eager.push(boot(root)); else io.observe(root);
  }
  // A deep link (/#lead, or a reload after a tap — it writes the hash into the address) is re-aimed once
  // the two growers above are up, then held there while the page settles (js/hash.js, O-1).
  if (location.hash) {
    const loaded = document.readyState === 'complete'
      ? Promise.resolve() : new Promise((r) => addEventListener('load', r, { once: true }));
    Promise.all([...eager, loaded]).then(() => import('./hash.js')).then((m) => m.landHashHeld(header));
  }
});
