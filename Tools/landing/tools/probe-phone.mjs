// Phone-layout probe (повторное ревью 2, N-5 и N-6). Two questions the 390×844 profiles of check.mjs
// cannot answer, asked over the viewports people actually have — iPhone Safari at the top of the page is
// 390×664, an Android with its URL bar ≈ 360×640, an SE in Safari 375×548:
//
//   signal — does the «Сигнал» conversation fit its bubbles? At each step's rest, in the live GL state
//            and in the quiet fallback (webglcontextlost → gl-solid): no text outside its bubble (spill),
//            no text line on another overlay object or under the header (hits), nothing past the screen
//            edge; the quiet layer never on the step's own copy; and — sweeping the scroll through both
//            steps in the live state — a SHOWN overlay never on visible copy.
//   cta    — does «Ранний доступ» land on the FORM? After a tap on the header CTA, on the hero CTA and
//            after opening /#lead directly, at rest: the lead card is revealed and the phone field is
//            wholly on screen below the fixed header, with the heading too wherever the screen is tall
//            enough for both (a screen shorter than heading+field lands on the fields block, O-2).
//
//   node tools/probe-phone.mjs [--url=http://127.0.0.1:8809/] [--only=signal|cta] [--sizes=390x664,…]
//                              [--sweep=0] [--shots=.check/probe] [--real-frames]
// Exit 1 on any violation. Rule 0 of the project: one browser (tools/chrome.mjs, GPU off), closed in
// finally; every request to choosereply.com is aborted (NO LIVE TRAFFIC).
import { launchChrome } from './chrome.mjs';
import { clampFrames, glide, measureGroup, stepRest, sweepSample, fitViolations } from './signal-fit.mjs';
import fs from 'node:fs';
import path from 'node:path';

const opt = Object.fromEntries(process.argv.slice(2).map((a) => {
  const s = a.replace(/^--/, ''), i = s.indexOf('=');
  return i < 0 ? [s, true] : [s.slice(0, i), s.slice(i + 1)];
}));
const URL_ = opt.url || 'http://127.0.0.1:8809/';
const SHOTS = opt.shots ? path.resolve(opt.shots) : null;
if (SHOTS) fs.mkdirSync(SHOTS, { recursive: true });
const parse = (s) => s.split(',').filter(Boolean).map((v) => v.split('x').map(Number));
// phones 360×540 … 430×932 (the short ones are the point), a tablet, desktops
const SIGNAL = parse(opt.sizes || '360x540,360x640,360x740,360x800,375x548,375x667,390x664,390x700,390x844,393x659,412x915,414x715,428x746,430x932,768x1024,1024x768,1280x720,1440x900,1920x1080');
const CTA = parse(opt.sizes || '360x540,360x640,375x548,390x664,390x844,412x915,768x1024,1280x720,1440x900');
const SWEEP = opt.sweep === '0' ? 0 : 28;   // samples through the two steps (live state)
const fails = [];

const tierFor = (W) => (W < 900 ? 'medium' : 'high');
async function open(b, W, H, tier, hash) {
  const mobile = W < 900;
  const ctx = await b.newContext({ viewport: { width: W, height: H }, isMobile: mobile, hasTouch: mobile, deviceScaleFactor: 1, locale: 'ru-RU' });
  await ctx.route(/https?:\/\/([a-z0-9-]+\.)?choosereply\.com\//, (r) => r.abort());
  if (!opt['real-frames']) await clampFrames(ctx);   // layout, not speed (tools/signal-fit.mjs)
  const p = await ctx.newPage();
  const errs = [];
  p.on('pageerror', (e) => errs.push(String(e).slice(0, 200)));
  p.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  const u = new URL(URL_); u.searchParams.set('tier', tier);
  if (hash) u.hash = hash;
  await p.goto(u.href, { waitUntil: 'load', timeout: 90000 });
  return { ctx, p, errs };
}
async function signal(b) {
  for (const [W, H] of SIGNAL) {
    const { ctx, p, errs } = await open(b, W, H, tierFor(W));
    await p.waitForTimeout(2200);
    const gl = await p.evaluate(() => document.querySelector('.stage').classList.contains('gl-init'));
    const row = { W, H, gl };
    if (!gl) { fails.push(`signal ${W}x${H}: the field did not start (no gl-init) — nothing measured`); console.log(JSON.stringify(row)); await ctx.close(); continue; }
    // live state: each step at its rest
    row.live = [];
    for (const k of [1, 2]) {
      await glide(p, await stepRest(p, k)); await p.waitForTimeout(1600);
      row.live.push(await p.evaluate(measureGroup, k));
      if (SHOTS && k === 1) await p.screenshot({ path: path.join(SHOTS, `signal-${W}x${H}-live.png`) });
    }
    // live sweep: from the hero down to past step 2, a SHOWN overlay never on visible copy
    if (SWEEP) {
      const [a, z] = await p.evaluate(() => { const s = document.querySelectorAll('.sg-step'); const e = s[s.length - 1].getBoundingClientRect(); return [0, Math.round(e.bottom + scrollY - innerHeight * 0.5)]; });
      await glide(p, a, 12); await p.waitForTimeout(600);
      let worst = 0, at = null, shown = 0, lost = 0;
      for (let i = 0; i <= SWEEP; i++) {
        const y = Math.round(a + (z - a) * i / SWEEP);
        await glide(p, y, 8); await p.waitForTimeout(420);
        const n = await p.evaluate(sweepSample);
        if (n === -1) { lost++; continue; }
        if (n !== null) shown++;
        if (n > worst) { worst = n; at = y; }
      }
      row.sweep = { samples: SWEEP + 1, shown, onCopy: worst, at, lost };
    }
    // quiet fallback: the context is lost at step 1's rest, the conversation lies in the steps' reserve
    await glide(p, await stepRest(p, 1)); await p.waitForTimeout(900);
    await p.evaluate(() => document.querySelector('.stage-gl').dispatchEvent(new Event('webglcontextlost', { cancelable: true })));
    await p.waitForTimeout(1900);
    row.quiet = [];
    for (const k of [1, 2]) {
      await glide(p, await stepRest(p, k)); await p.waitForTimeout(700);
      row.quiet.push(await p.evaluate(measureGroup, k));
      if (SHOTS && k === 1) await p.screenshot({ path: path.join(SHOTS, `signal-${W}x${H}-quiet.png`) });
    }
    row.errs = errs;
    console.log(JSON.stringify(row));
    if ([...row.live].some((m) => !/gl-on/.test(m.cls))) fails.push(`signal ${W}x${H}: the live state was lost before it was measured (${row.live.map((m) => m.cls).join(' / ')})`);
    for (const [mode, list] of [['live', row.live], ['quiet', row.quiet]]) {
      list.forEach((m, i) => fails.push(...fitViolations(`signal ${W}x${H} ${mode} step ${i + 1}`, mode, m, W < 900)));
    }
    if (row.sweep && row.sweep.lost) fails.push(`signal ${W}x${H} live sweep: the live state was lost in ${row.sweep.lost} samples`);
    if (row.sweep && row.sweep.onCopy) fails.push(`signal ${W}x${H} live sweep: a shown overlay on visible copy at y=${row.sweep.at}`);
    if (row.sweep && W < 900 && !row.sweep.shown) fails.push(`signal ${W}x${H} live sweep: the conversation is never shown`);
    if (errs.length) fails.push(`signal ${W}x${H}: console errors: ${errs.join(' | ')}`);
    await ctx.close();
  }
}

// ---- cta: where «Ранний доступ» lands ----
async function rest(p) {
  let last = -1, since = Date.now(); const t0 = Date.now();
  while (Date.now() - t0 < 12000) {
    await p.waitForTimeout(150);
    const y = await p.evaluate(() => scrollY);
    if (y !== last) { last = y; since = Date.now(); } else if (Date.now() - since > 1200) break;
  }
}
async function cta(b) {
  for (const [W, H] of CTA) {
    // null = the deep link /#lead: opened at the address a CTA tap leaves behind (ревью 3, O-1), it
    // must come to rest in exactly the same place as the tap
    for (const sel of [null, '.hdr .btn-s[href="#lead"]', '.hero-cta a.btn[href="#lead"]']) {
      const { ctx, p, errs } = await open(b, W, H, tierFor(W), sel ? null : '#lead');
      await p.waitForTimeout(1800);
      if (sel) { if (W < 900) await p.tap(sel); else await p.click(sel); }
      await rest(p);
      await p.waitForTimeout(900);   // the card's reveal (opacity .8 s)
      const r = await p.evaluate(() => {
        const hdr = document.querySelector('.hdr').getBoundingClientRect().bottom;
        const rc = (s) => { const e = document.querySelector(s); if (!e) return null; const q = e.getBoundingClientRect(); return { t: Math.round(q.top), b: Math.round(q.bottom) }; };
        const card = document.querySelector('.lead-card');
        return { hdr: Math.round(hdr), vh: innerHeight, lead: rc('#lead'), kin: rc('.kin'), card: rc('.lead-card'), h: rc('#lead-h'), phone: rc('#leadPhone'), btn: rc('#leadBtn'), op: +parseFloat(getComputedStyle(card).opacity).toFixed(2), hash: location.hash };
      });
      const onScreen = (q) => q && q.t >= r.hdr - 1 && q.b <= r.vh + 1;
      // A screen too short for the card AND its phone field (360×540, 375×548, and up to ~630: the
      // card lands at 80 and the field's bottom is 539 px below that) cannot show both. The field is
      // the one that must be there — it is what the jump is FOR — so the chain gives up the heading
      // and opens the fields block instead (ревью 3, O-2). The test is the chain's own arithmetic on
      // the measured numbers: the card→field span does not depend on where we ended up landing.
      const tight = r.card && r.phone && 80 + (r.phone.b - r.card.t) > r.vh - 8;
      const landed = onScreen(r.phone) && (tight || onScreen(r.h));
      const row = { cta: sel ? (sel.startsWith('.hdr') ? 'header' : 'hero') : 'deep', W, H, ...r, tight, ok: landed && r.op > 0.9 && r.hash === '#lead' };
      console.log(JSON.stringify(row));
      if (!row.ok) fails.push(`cta ${row.cta} ${W}x${H}: heading ${JSON.stringify(r.h)} phone ${JSON.stringify(r.phone)} card top ${r.card && r.card.t} (screen ${r.hdr}..${r.vh}, card opacity ${r.op}, hash ${r.hash}${tight ? ', tight screen' : ''})`);
      if (errs.length) fails.push(`cta ${row.cta} ${W}x${H}: console errors: ${errs.join(' | ')}`);
      if (SHOTS && [390, 768, 1440].includes(W) && (!sel || sel.startsWith('.hdr'))) await p.screenshot({ path: path.join(SHOTS, `cta-${W}x${H}-${row.cta}.png`) });
      await ctx.close();
    }
  }
}

const b = await launchChrome();
try {
  if (opt.only !== 'cta') await signal(b);
  if (opt.only !== 'signal') await cta(b);
} finally {
  await b.close();
}
console.log(JSON.stringify({ fails }, null, 2));
process.exit(fails.length ? 1 : 0);
