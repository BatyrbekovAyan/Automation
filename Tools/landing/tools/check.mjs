// Headless acceptance check for the landing. Enforces the spec budgets and the
// NO-LIVE-TRAFFIC rule: every request to choosereply.com / n8n.choosereply.com is aborted,
// except POST …/webhook/LandingLead which is answered locally with {success:true} and
// recorded so the form contract can be asserted.
import { launchChrome } from './chrome.mjs';
import { clampFrames, glide, measureGroup, stepRest, fitViolations } from './signal-fit.mjs';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

// --key=value splits on the FIRST '=' only, so --url=http://…/?tier=high keeps its query.
const opt = Object.fromEntries(process.argv.slice(2).map((a) => {
  const s = a.replace(/^--/, ''), i = s.indexOf('=');
  return i < 0 ? [s, true] : [s.slice(0, i), s.slice(i + 1)];
}));
// --tier=low|medium|high appends ?tier= to every profile URL of the page under test (overflow widths,
// mobile/desktop, form). Headless Chrome here reports hardwareConcurrency 4, so without it every
// profile lands on whatever decideTier gives this machine. Deliberately NOT applied to the reduced-motion
// pass (a ?tier override beats reduced motion by design, and that pass asserts q-low) nor to --baseline
// (the v1 reference is measured as it is).
const TIER = opt.tier || null;
if (TIER && !['low', 'medium', 'high'].includes(TIER)) { console.error(`--tier must be low|medium|high, got ${TIER}`); process.exit(2); }
function withTier(u) { if (!TIER) return u; const x = new URL(u); x.searchParams.set('tier', TIER); return x.href; }
function withoutTier(u) { const x = new URL(u); x.searchParams.delete('tier'); return x.href; }
const URL_ = opt.url || 'http://127.0.0.1:8809/';
const URL_T = withTier(URL_);
const BASE = opt.baseline || null;
const OUT = path.resolve(opt.out || '.check');
fs.mkdirSync(OUT, { recursive: true });
const fails = [];
const report = { url: URL_, tier: TIER, at: new Date().toISOString(), widths: {}, mobile: null, desktop: null, reduced: null, form: null, anchors: null, signal: null, cls: null, baseline: null };

// GPU always off (see tools/chrome.mjs); FPS numbers are software-rendered and only meaningful vs --baseline.
if (opt.gpu) { console.error('--gpu was removed: the hardware GPU hangs WindowServer on this Mac'); process.exit(2); }
let browser = null;   // launched right before the passes, closed in finally (rule 0; финальное ревью, M-5)

async function newPage({ width, height, mobile, reduced = false }) {
  const ctx = await browser.newContext({
    viewport: { width, height }, deviceScaleFactor: mobile ? 2 : 1, isMobile: mobile, hasTouch: mobile, locale: 'ru-RU',
    reducedMotion: reduced ? 'reduce' : 'no-preference',
  });
  const page = await ctx.newPage();
  const state = { errors: [], leads: [], bytes: 0, jsGz: 0, live: [] };
  await ctx.route(/https?:\/\/(n8n\.)?choosereply\.com\//, async (route) => {
    const req = route.request();
    if (req.method() === 'POST' && req.url().includes('/webhook/LandingLead')) {
      state.leads.push({ contentType: req.headers()['content-type'] || '', body: req.postData() || '' });
      return route.fulfill({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: '{"success":true}' });
    }
    state.live.push(req.url());
    return route.abort();
  });
  page.on('console', (m) => { if (m.type() === 'error') state.errors.push(m.text().slice(0, 240)); });
  page.on('pageerror', (e) => state.errors.push('pageerror: ' + String(e).slice(0, 240)));
  page.on('response', async (r) => {
    try {
      const b = await r.body(); state.bytes += b.length;
      if (/javascript/.test(r.headers()['content-type'] || '') || r.url().endsWith('.js')) state.jsGz += zlib.gzipSync(b).length;
    } catch {}
  });
  return { ctx, page, state };
}

async function scrollThrough(page, steps = 40) {
  await page.evaluate(async (n) => {
    const H = document.documentElement.scrollHeight - innerHeight;
    for (let i = 1; i <= n; i++) { scrollTo(0, H * i / n); await new Promise((r) => setTimeout(r, 60)); }
    scrollTo(0, 0);
  }, steps);
}

async function perfPass(page, cpu) {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: cpu });
  await page.evaluate(() => scrollTo(0, 0));
  await page.waitForTimeout(800);
  const r = await page.evaluate(async () => {
    const frames = []; let last = performance.now();
    const H = document.documentElement.scrollHeight - innerHeight, dur = 7000, t0 = performance.now();
    await new Promise((res) => { (function tick(now) { frames.push(now - last); last = now; const k = Math.min(1, (now - t0) / dur); scrollTo(0, H * k); if (k < 1) requestAnimationFrame(tick); else res(); })(performance.now()); });
    frames.shift();
    const s = [...frames].sort((a, b) => a - b), avg = frames.reduce((a, b) => a + b, 0) / frames.length;
    return { fpsAvg: Math.round(1000 / avg), p95FrameMs: Math.round(s[Math.floor(s.length * 0.95)]), framesOver50ms: frames.filter((f) => f > 50).length, frames: frames.length };
  });
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: 1 });
  return r;
}

async function profile(name, spec, url, { perf = true } = {}) {
  const { ctx, page, state } = await newPage(spec);
  await page.goto(url, { waitUntil: 'load', timeout: 90000 });
  const heroVisibleAt1s = await (async () => {
    await page.waitForTimeout(1000);
    return page.evaluate(() => {
      const h = document.querySelector('h1');
      if (!h) return false;
      const cs = getComputedStyle(h), r = h.getBoundingClientRect();
      return cs.visibility !== 'hidden' && parseFloat(cs.opacity) > 0.9 && r.width > 0 && r.top < innerHeight;
    });
  })();
  const bytesBeforeScroll = state.bytes;
  const lcp = await page.evaluate(() => new Promise((res) => { let v = 0; try { new PerformanceObserver((l) => { for (const e of l.getEntries()) v = e.startTime; }).observe({ type: 'largest-contentful-paint', buffered: true }); } catch {} setTimeout(() => res(Math.round(v)), 300); }));
  await scrollThrough(page);
  const shots = [];
  for (const k of [0, 0.12, 0.25, 0.4, 0.55, 0.7, 0.85, 1]) {
    await page.evaluate((f) => scrollTo(0, (document.documentElement.scrollHeight - innerHeight) * f), k);
    await page.waitForTimeout(900);
    const f = path.join(OUT, `${name}-${String(Math.round(k * 100)).padStart(3, '0')}.png`);
    await page.screenshot({ path: f }); shots.push(f);
  }
  const perfR = perf ? await perfPass(page, spec.mobile ? 4 : 1) : null;
  const res = { heroVisibleAt1s, lcpMs: lcp, kbBeforeScroll: Math.round(bytesBeforeScroll / 1024), kbTotal: Math.round(state.bytes / 1024), jsGzKb: Math.round(state.jsGz / 1024), errors: state.errors, liveRequests: state.live, perf: perfR, shots };
  await ctx.close();
  return res;
}

browser = await launchChrome();
try {
  // 1. Horizontal overflow at four widths
  for (const width of [360, 390, 768, 1440]) {
    const { ctx, page } = await newPage({ width, height: 900, mobile: width < 768 });
    await page.goto(URL_T, { waitUntil: 'load', timeout: 90000 });
    await scrollThrough(page, 20);
    const over = await page.evaluate(() => {
      const vw = document.documentElement.clientWidth;
      // An element only counts when it really pokes out of the page: skip the ones a clipping ancestor
      // (overflow hidden/clip on the x axis, e.g. a marquee track) cuts at or inside the viewport, and
      // off-screen parked things like the skip link (entirely left of 0 — they cannot be scrolled to).
      const clippedByAncestor = (e) => {
        for (let p = e.parentElement; p && p !== document.body && p !== document.documentElement; p = p.parentElement) {
          const cs = getComputedStyle(p);
          // a horizontal scroller (the phone niches row) clips its own track the same way
          if (/(hidden|clip|auto|scroll)/.test(cs.overflowX) || /(hidden|clip)/.test(cs.overflow)) {
            const pr = p.getBoundingClientRect();
            if (pr.right <= vw + 1 && pr.left >= -1) return true;
          }
          if (cs.position === 'fixed') return true;
        }
        return false;
      };
      const bad = [...document.querySelectorAll('body *')].filter((e) => {
        const r = e.getBoundingClientRect();
        if (!(r.width > 0 && (r.right > vw + 1 || r.left < -1))) return false;
        if (getComputedStyle(e).position === 'fixed') return false;
        if (r.right <= 0) return false;
        return !clippedByAncestor(e);
      }).slice(0, 5).map((e) => `${e.tagName.toLowerCase()}.${[...e.classList].join('.')}`);
      return { scrollW: document.documentElement.scrollWidth, vw, bad };
    });
    report.widths[width] = over;
    if (over.scrollW > over.vw) fails.push(`horizontal scroll at ${width}px: scrollWidth ${over.scrollW} > ${over.vw} (${over.bad.join(', ')})`);
    await ctx.close();
  }

  // 2. Mobile + desktop profiles
  const skipPerf = !!opt['skip-perf'];
  // The baseline is measured RIGHT AFTER the mobile profile it is compared with (it used to come after
  // the desktop profile, ~a minute later): on this machine load comes in bursts (mediaanalysisd,
  // Spotlight), and a relative floor only means something when both sides saw the same machine.
  if (opt.only !== 'desktop') report.mobile = await profile('mobile', { width: 390, height: 844, mobile: true }, URL_T, { perf: !skipPerf });
  if (BASE && !skipPerf && opt.only !== 'desktop') report.baseline = await profile('baseline-mobile', { width: 390, height: 844, mobile: true }, BASE, { perf: true });
  if (opt.only !== 'mobile') report.desktop = await profile('desktop', { width: 1440, height: 900, mobile: false }, URL_T, { perf: !skipPerf });

  // 3. Reduced motion: no errors, content visible, no running canvas
  {
    const { ctx, page, state } = await newPage({ width: 390, height: 844, mobile: true, reduced: true });
    await page.goto(withoutTier(URL_), { waitUntil: 'load', timeout: 90000 });
    await page.waitForTimeout(1200);
    await scrollThrough(page, 20);
    report.reduced = await page.evaluate(() => ({
      tierClass: [...document.documentElement.classList].find((c) => c.startsWith('q-')),
      visibleCanvases: [...document.querySelectorAll('canvas')].filter((c) => c.getBoundingClientRect().width > 0 && getComputedStyle(c).display !== 'none' && parseFloat(getComputedStyle(c).opacity) > 0).length,
      hiddenReveals: [...document.querySelectorAll('[data-reveal],[data-split]')].filter((e) => parseFloat(getComputedStyle(e).opacity) < 0.9).length,
    }));
    report.reduced.errors = state.errors;
    if (report.reduced.tierClass !== 'q-low') fails.push(`reduced motion tier is ${report.reduced.tierClass}, expected q-low`);
    if (report.reduced.visibleCanvases) fails.push(`reduced motion shows ${report.reduced.visibleCanvases} canvas`);
    if (report.reduced.hiddenReveals) fails.push(`reduced motion leaves ${report.reduced.hiddenReveals} reveal elements hidden`);
    if (state.errors.length) fails.push(`reduced motion console errors: ${state.errors.join(' | ')}`);
    await ctx.close();
  }

  // 4. Form contract (stubbed endpoint)
  {
    const { ctx, page, state } = await newPage({ width: 390, height: 844, mobile: true });
    await page.goto(URL_T, { waitUntil: 'load', timeout: 90000 });
    const form = page.locator('form#leadForm');
    let errShown = false, okShown = false;
    if (await form.count()) {
      await form.scrollIntoViewIfNeeded();
      await page.fill('#leadName', 'Проверка');
      await page.fill('#leadPhone', '12');
      await page.click('#leadBtn');
      errShown = await page.locator('#leadErr').isVisible();
      await page.fill('#leadPhone', '+7 700 000 00 00');
      await page.click('#leadBtn');
      await page.waitForSelector('#leadOk:not([hidden])', { timeout: 8000 }).catch(() => {});
      okShown = await page.locator('#leadOk').isVisible();
    }
    const lead = state.leads[0] || null;
    const params = lead ? Object.fromEntries(new URLSearchParams(lead.body)) : null;
    report.form = { errShownForBadPhone: errShown, okShown, leads: state.leads.length, contentType: lead?.contentType, params };
    if (!errShown) fails.push('form: short phone did not show #leadErr');
    if (!okShown) fails.push('form: success state #leadOk not shown');
    if (state.leads.length !== 1) fails.push(`form: expected exactly 1 LandingLead POST, got ${state.leads.length}`);
    if (lead && !lead.contentType.startsWith('application/x-www-form-urlencoded')) fails.push(`form: content-type ${lead.contentType}`);
    if (params && !(params.phone === '+7 700 000 00 00' && params.source === 'landing' && params.website === '' && params.name === 'Проверка')) fails.push(`form: bad params ${JSON.stringify(params)}`);
    await ctx.close();
  }
  // 5. In-page anchors land where they point (ревью T6 r4, Important 2). The pinned story grows the page
  // by 1–2k px; a jump from the top used to stop thousands of px short of the form. Measured on the
  // pinned path: ?tier=medium unless --tier says otherwise (q-low has no pin to get wrong).
  {
    const tier = TIER || 'medium';
    const at = (u) => { const x = new URL(u); x.searchParams.set('tier', tier); return x; };
    const rest = async (page) => {
      let last = -1, since = Date.now(); const t0 = Date.now();
      while (Date.now() - t0 < 12000) {
        await page.waitForTimeout(150);
        const y = await page.evaluate(() => scrollY);
        if (y !== last) { last = y; since = Date.now(); } else if (Date.now() - since > 1200) break;
      }
    };
    report.anchors = [];
    // «Ранний доступ» lands on the FORM card on a phone (the kinetic line above it is taller than the screen
    // minus the form, повторное ревью 2, N-6); a desktop keeps the whole section, line above the form
    const cases = [
      { vp: [360, 800], click: '#main a[href="#lead"]', target: '.lead-card' },
      { vp: [390, 844], click: '#main a[href="#lead"]', target: '.lead-card' },
      { vp: [390, 664], click: '.hdr a[href="#lead"]', target: '.lead-card' },
      { vp: [1440, 900], desktop: true, click: '.hdr a[href="#lead"]', target: '#lead' },
      { vp: [390, 844], hash: '#pricing', target: '#pricing' },
      // a deep link (or a reload after a tap: the tap writes #lead into the address) lands where the
      // tap lands — it used to aim at a page that was about to grow by 1–2k px (ревью 3, O-1)
      { vp: [390, 664], hash: '#lead', target: '.lead-card' },
      { vp: [1440, 900], desktop: true, hash: '#lead', target: '#lead' },
      // a screen too short for the card's heading AND its phone field lands on the fields themselves,
      // by tap and by deep link alike (ревью 3, O-2); `want` is that point's own scroll-margin
      { vp: [360, 540], click: '.hdr a[href="#lead"]', target: '.lead-fields', want: 104 },
      { vp: [375, 548], hash: '#lead', target: '.lead-fields', want: 104 },
    ];
    for (const c of cases) {
      const { ctx, page } = await newPage({ width: c.vp[0], height: c.vp[1], mobile: !c.desktop });
      const u = at(URL_); if (c.hash) u.hash = c.hash;
      await page.goto(u.href, { waitUntil: 'load', timeout: 90000 });
      await page.waitForTimeout(1500);
      const label = `${c.vp.join('x')} ${c.click || c.hash} → ${c.target}`;
      // a partial build (no #lead / no link to it) is a failed row, not an exception that kills the run
      if (c.click && !(await page.locator(c.click).count())) { fails.push(`anchor ${label}: no ${c.click} on the page`); report.anchors.push({ case: label, tier, ok: false }); await ctx.close(); continue; }
      if (c.click) await page.click(c.click);
      await rest(page);
      const r = await page.evaluate(([id, want]) => { const t = document.querySelector(id); return t ? { top: Math.round(t.getBoundingClientRect().top), want: want || (document.documentElement.classList.contains('lenis') ? 72 : 80) } : null; }, [c.target, c.want || 0]);
      if (!r) { fails.push(`anchor ${label}: no ${c.target} on the page`); report.anchors.push({ case: label, tier, ok: false }); await ctx.close(); continue; }
      const row = { case: label, tier, ...r, ok: Math.abs(r.top - r.want) <= 8 };
      report.anchors.push(row);
      if (!row.ok) fails.push(`anchor ${row.case}: target top ${r.top}, expected ${r.want}`);
      await ctx.close();
    }
  }
  // 5b. The «Сигнал» conversation fits its bubbles on SHORT phones (повторное ревью 2, N-5: every profile
  // above is 390×844, where it always fitted; under ~720 px the text spilled 25–57 px out of its bubbles
  // and three reviews missed it). Live state at each step's rest, then the quiet fallback. The frame clock
  // is clamped (tools/signal-fit.mjs): this pass measures layout, and a governor shutdown on a loaded
  // machine would take the live state away before it is measured. The full sweep over 19 viewports is
  // tools/probe-phone.mjs.
  {
    const tier = TIER === 'low' ? null : (TIER || 'medium');
    report.signal = [];
    for (const [w, h] of tier ? [[390, 664], [375, 548], [360, 640]] : []) {
      const { ctx, page, state } = await newPage({ width: w, height: h, mobile: true });
      await clampFrames(ctx);
      const u = new URL(URL_); u.searchParams.set('tier', tier);
      await page.goto(u.href, { waitUntil: 'load', timeout: 90000 });
      await page.waitForTimeout(2200);
      const row = { vp: `${w}x${h}`, tier, live: [], quiet: [] };
      if (!(await page.evaluate(() => !!document.querySelector('.stage.gl-init')))) {
        fails.push(`signal ${row.vp}: the particle field did not start — nothing to measure`);
      } else {
        for (const k of [1, 2]) { await glide(page, await stepRest(page, k)); await page.waitForTimeout(1600); row.live.push(await page.evaluate(measureGroup, k)); }
        await page.evaluate(() => document.querySelector('.stage-gl').dispatchEvent(new Event('webglcontextlost', { cancelable: true })));
        await page.waitForTimeout(1900);
        for (const k of [1, 2]) { await glide(page, await stepRest(page, k)); await page.waitForTimeout(700); row.quiet.push(await page.evaluate(measureGroup, k)); }
        row.live.forEach((m, i) => { if (!/gl-on/.test(m.cls)) fails.push(`signal ${row.vp} live step ${i + 1}: the live state was lost before it was measured (${m.cls})`); else fails.push(...fitViolations(`signal ${row.vp} live step ${i + 1}`, 'live', m, true)); });
        row.quiet.forEach((m, i) => fails.push(...fitViolations(`signal ${row.vp} quiet step ${i + 1}`, 'quiet', m, true)));
      }
      if (state.errors.length) fails.push(`signal ${row.vp}: console errors: ${state.errors.join(' | ')}`);
      report.signal.push(row);
      await ctx.close();
    }
  }
  // 6. Layout stability over a full scroll (линза перф-инженера, I-1): the ScrollTrigger pin of the story
  // wrote a ≈ 1.0 layout-shift on every relative ⇄ fixed switch (CLS 1–3 per scroll). The observer starts
  // after the page has settled, so only what the SCROLL moves is counted; a governor shutdown mid-pass
  // (software WebGL on a loaded machine) is the one reason to measure again.
  {
    const tier = TIER || 'medium';
    report.cls = [];
    for (const [w, h, mobile] of [[390, 844, true], [768, 1024, true], [1440, 900, false]]) {
      let row = null;
      for (let attempt = 0; attempt < 2; attempt++) {
        const { ctx, page } = await newPage({ width: w, height: h, mobile });
        const u = new URL(URL_); u.searchParams.set('tier', tier);
        await page.goto(u.href, { waitUntil: 'load', timeout: 90000 });
        await page.waitForTimeout(1500);
        row = await page.evaluate(async () => {
          const shifts = [];
          const name = (n) => (n ? `${n.nodeName.toLowerCase()}${n.classList && n.classList.length ? '.' + [...n.classList].join('.') : ''}` : '?');
          const po = new PerformanceObserver((l) => { for (const e of l.getEntries()) shifts.push({ v: e.value, y: Math.round(scrollY), src: (e.sources || []).slice(0, 3).map((x) => name(x.node)).join(' ') }); });
          po.observe({ type: 'layout-shift', buffered: false });
          const H = document.documentElement.scrollHeight - innerHeight, dur = 8000, t0 = performance.now();
          await new Promise((res) => { (function tick(now) { const k = Math.min(1, (now - t0) / dur); scrollTo(0, H * k); if (k < 1) requestAnimationFrame(tick); else setTimeout(res, 500); })(performance.now()); });
          po.disconnect();
          const total = shifts.reduce((a, x) => a + x.v, 0);
          return { total: Math.round(total * 10000) / 10000, entries: shifts.length, worst: shifts.sort((a, b) => b.v - a.v).slice(0, 3), glSolid: !!document.querySelector('.stage.gl-solid') };
        });
        await ctx.close();
        if (row.total <= 0.01 || !row.glSolid) break;
      }
      report.cls.push({ vp: `${w}x${h}`, tier, ...row });
      if (row.total > 0.01) fails.push(`layout shift over a full scroll at ${w}x${h}: ${row.total} > 0.01 (${row.worst.map((x) => `${x.v.toFixed(3)}@${x.y} ${x.src}`).join('; ')})`);
    }
  }
} finally {
  await browser.close();
}

// 7. Budgets
for (const name of ['mobile', 'desktop']) {
  const p = report[name]; if (!p) continue;
  if (!p.heroVisibleAt1s) fails.push(`${name}: H1 not visible at 1 s`);
  if (p.errors.length) fails.push(`${name}: console errors: ${p.errors.join(' | ')}`);
  if (p.liveRequests.length) fails.push(`${name}: live requests attempted: ${p.liveRequests.join(', ')}`);
  if (p.jsGzKb > 120) fails.push(`${name}: JS ${p.jsGzKb} KB gz > 120`);
  if (p.kbBeforeScroll > 450) fails.push(`${name}: ${p.kbBeforeScroll} KB before scroll > 450`);
}
if (report.desktop && report.desktop.lcpMs > 2500) fails.push(`desktop LCP ${report.desktop.lcpMs} ms > 2500`);
if (report.mobile?.perf) {
  const m = report.mobile.perf, b = report.baseline?.perf;
  const fpsFloor = b ? Math.min(55, b.fpsAvg - 3) : 55;
  if (m.fpsAvg < fpsFloor) fails.push(`mobile fpsAvg ${m.fpsAvg} < ${fpsFloor}${b ? ` (baseline ${b.fpsAvg})` : ''}`);
  if (m.p95FrameMs > (b ? Math.max(25, b.p95FrameMs + 8) : 25)) fails.push(`mobile p95 frame ${m.p95FrameMs} ms too high${b ? ` (baseline ${b.p95FrameMs})` : ''}`);
  if (m.framesOver50ms > (b ? Math.max(3, b.framesOver50ms + 2) : 3)) fails.push(`mobile frames>50ms ${m.framesOver50ms}${b ? ` (baseline ${b.framesOver50ms})` : ''}`);
}

report.fails = fails;
fs.writeFileSync(path.join(OUT, 'report.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify({ fails, mobile: report.mobile && { ...report.mobile, shots: undefined }, desktop: report.desktop && { ...report.desktop, shots: undefined }, baseline: report.baseline?.perf, reduced: report.reduced, form: report.form, anchors: report.anchors, signal: report.signal && report.signal.map((r) => ({ vp: r.vp, live: r.live.map((m) => `${m.cls.includes('gl-on') ? 'on' : m.cls} spill ${m.spill} hits ${m.hits.length} shown ${m.on}`), quiet: r.quiet.map((m) => `spill ${m.spill} hits ${m.hits.length} copy ${m.onCopy}`) })), cls: report.cls, widths: report.widths }, null, 2));
process.exit(fails.length ? 1 : 0);
