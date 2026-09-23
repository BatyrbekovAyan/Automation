// Quality tiers. Decided once, before any animation starts, from what the device reports.
//   high   — fine pointer, wide screen, WebGL, ≥ 8 cores: every effect at full density
//   medium — ordinary phones/tablets/laptops: effects on, particle count reduced
//   low    — reduced motion, Save-Data, no WebGL, ≤ 2 GB, or a ≤ 4-core TOUCH device that is
//            not iOS: static mark instead of particles, native scroll, no pinning, simple reveals
// The core count is a weak-device signal only on phones/tablets (the c-signal prototype's rule
// WEAK = memory ≤ 2 || (cores ≤ 4 && coarse && !ios)): a 4-core Android phone is a budget phone,
// while a 4-core laptop/desktop with a mouse is far stronger and keeps the effects (medium).
// iOS Safari under-reports hardwareConcurrency and has no deviceMemory, so a 4-core
// report there is not a weak-device signal either. ?tier=low|medium|high overrides (QA).
// Everything between the @inline markers is ALSO inlined into <head> by tools/build.mjs (with the
// `export`s stripped): the tier is decided before the first paint, so the vendor scripts (GSAP,
// ScrollTrigger, Lenis) are only fetched by the tiers that use them (линза перф-инженера, I-4).
// Keep this block plain script code: no imports, nothing but these functions — and no nullish or
// optional-chaining operators either: the inline copy must parse on the old engines (iOS Safari ≤ 13.3,
// Chrome < 80) that the module graph cannot serve, or the tier is never set (финальное ревью, I-1).
// @inline-start
const TIERS = ['low', 'medium', 'high'];

export function decideTier(env) {
  if (env.override && TIERS.includes(env.override)) return env.override;
  if (env.reducedMotion || env.saveData) return 'low';
  if ((env.memory == null ? 8 : env.memory) <= 2) return 'low';
  if ((env.cores == null ? 8 : env.cores) <= 4 && env.coarse && !env.ios) return 'low';
  // WebGL is asked LAST: the probe is the only expensive signal (a throwaway context, 17–30 ms on a
  // phone at 4–6× CPU), and a device that is low for any other reason never pays for it
  if (!env.webgl) return 'low';
  if (!env.coarse && env.width >= 1024 && (env.cores == null ? 8 : env.cores) >= 8) return 'high';
  return 'medium';
}

// A throwaway context, released right away: it used to stay alive for the whole visit next to the
// particle field's own context (two live GL contexts on medium/high, линза перф-инженера, M-5).
export function probeWebgl(w) {
  try {
    const c = w.document.createElement('canvas');
    const gl = c.getContext('webgl') || c.getContext('experimental-webgl');
    if (!gl) return false;
    const lose = gl.getExtension && gl.getExtension('WEBGL_lose_context');
    if (lose) lose.loseContext();
    return true;
  } catch (e) { return false; }
}

export function readEnv(w) {
  const nav = w.navigator;
  let webgl;
  return {
    reducedMotion: w.matchMedia('(prefers-reduced-motion: reduce)').matches,
    saveData: !!(nav.connection && nav.connection.saveData),
    // lazy: probed on first read (decideTier reads it last), once
    get webgl() { if (webgl === undefined) webgl = probeWebgl(w); return webgl; },
    cores: nav.hardwareConcurrency,
    memory: nav.deviceMemory,
    coarse: w.matchMedia('(pointer: coarse)').matches,
    width: w.innerWidth,
    ios: /iP(hone|od|ad)/.test(nav.userAgent) || (nav.platform === 'MacIntel' && nav.maxTouchPoints > 1),
    override: new URLSearchParams(w.location.search).get('tier'),
  };
}

// Runs in <head> (inlined): marks the tier on <html> and starts the DOWNLOAD of the vendor scripts the
// tier needs — GSAP + ScrollTrigger for medium/high, Lenis only where it is created (a fine pointer) —
// as preloads: they arrive with the page but are not executed before the first paint. main.js runs
// them with loadVendor(). The weakest tier (Save-Data, reduced motion, budget phones) downloads none of
// it: ScrollTrigger used to auto-register at load and keep a perpetual rAF loop running on a page that
// never used it (линза перф-инженера, I-4).
export function vendorFiles(w, tier) {
  if (tier === 'low') return [];
  const files = ['gsap.min.js', 'ScrollTrigger.min.js'];
  if (w.matchMedia('(hover: hover) and (pointer: fine)').matches) files.push('lenis.min.js');
  return files;
}
export function bootHead(w, vendorBase) {
  const d = w.document;
  const env = readEnv(w);
  const tier = decideTier(env);
  d.documentElement.classList.add('q-' + tier);
  const files = vendorFiles(w, tier);
  for (const f of files) {
    const l = d.createElement('link');
    l.rel = 'preload'; l.as = 'script'; l.href = vendorBase + f;
    d.head.appendChild(l);
  }
  w.__crBoot = { env, tier, files, base: vendorBase };
  return w.__crBoot;
}
// @inline-end

// Runs the vendor scripts (already downloading since <head>) in order; resolves when all have run.
// async=false keeps the insertion order; a failed script resolves too — callers treat a missing
// global as «no motion lib».
export function loadVendor(w, boot) {
  const d = w.document;
  return Promise.all(boot.files.map((f) => new Promise((res) => {
    const s = d.createElement('script');
    s.src = boot.base + f; s.async = false;
    s.onload = s.onerror = res;
    d.head.appendChild(s);
  })));
}

// Watches frame times of a running effect and says when to shed load. The average is per FRAME
// (α = 1/30: the mean frame time, so a lone long frame does not count by its length), but the hold
// is TIME: the average must stay above 24 ms for 1.25 s of wall time before it acts. With a 45-frame
// hold a 10 fps device needed 5+ s to shed load and a loaded machine never did inside one scroll
// pass. A gap of ≥ 1 s is a paused/hidden tab and is ignored; a long frame below that counts,
// clamped to 250 ms, so one GC pause cannot trip it but a device stuck at 3 fps does shed load
// (frames > 250 ms used to be ignored outright, and such a device never degraded).
export class FrameGovernor {
  constructor({ limitMs = 24, holdMs = 1250, alpha = 1 / 30 } = {}) {
    this.limitMs = limitMs; this.holdMs = holdMs; this.alpha = alpha; this.reset();
    this.level = 0;
  }
  reset() { this.avg = 16.7; this.over = 0; }
  sample(dtMs) {
    if (!(dtMs > 0) || dtMs >= 1000) return 'none';
    dtMs = Math.min(dtMs, 250);
    this.avg += (dtMs - this.avg) * this.alpha;
    this.over = this.avg > this.limitMs ? this.over + dtMs : 0;
    if (this.over < this.holdMs) return 'none';
    this.reset();
    this.level += 1;
    return this.level === 1 ? 'degrade' : 'disable';
  }
}
