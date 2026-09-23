// The demo section is three files that must agree: 40-demo.html holds the chassis, demo.js
// resolves it by class, 40-demo.css paints it. A rename in one of them fails silently in the
// browser (querySelector returns null, the module does nothing), so pin the contract here.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

// classes the module builds at runtime — they are never in the markup, only in the CSS
const RUNTIME = ['demo-card', 'dm-card-bg', 'dm-card-l', 'dm-card-x', 'dm-glow', 'dm-typing', 'dm-bot',
  'dm-chip', 'dm-ochip', 'dm-rip', 'dm-flyc', 'dm-fl-a', 'dm-fl-b', 'dm-fl-g', 'dm-fl-ta', 'dm-fl-tb',
  'dm-fle', 'dm-sv-row', 'dm-sv-empty', 'dm-src-sz', 'is-paused'];

const html = fs.readFileSync(new URL('../../src/sections/40-demo.html', import.meta.url), 'utf8');
const js = fs.readFileSync(new URL('../../src/js/demo.js', import.meta.url), 'utf8');
const css = fs.readFileSync(new URL('../../src/css/40-demo.css', import.meta.url), 'utf8');

// whole-token membership, never a substring: «dm-tray» must not be satisfied by «dm-tray-x»
const CLASSES = new Set();
for (const m of html.matchAll(/class="([^"]+)"/g)) for (const c of m[1].trim().split(/\s+/)) CLASSES.add(c);

test('every class demo.js looks up exists in the section markup', () => {
  const wanted = new Set();
  for (const m of js.matchAll(/\$\$?\('\.([a-z0-9-]+)'/g)) wanted.add(m[1]);
  assert.ok(wanted.size >= 15, `expected a real selector list, got ${wanted.size}`);
  const missing = [...wanted].filter((c) => !RUNTIME.includes(c) && !CLASSES.has(c));
  assert.deepEqual(missing, [], `demo.js resolves classes that 40-demo.html does not provide: ${missing.join(', ')}`);
});

test('classes demo.js creates at runtime are styled', () => {
  // these never appear in the markup — they only exist once the module builds them
  for (const c of RUNTIME) {
    assert.ok(js.includes(c), `demo.js no longer creates .${c}`);
    assert.ok(css.includes(`.${c}`), `40-demo.css has no rule for .${c}`);
  }
});

test('the section keeps its accessible chassis', () => {
  assert.match(html, /id="demo"/);
  assert.match(html, /data-module="demo"/);
  assert.match(html, /role="tablist"/);
  assert.equal((html.match(/role="tab"/g) || []).length, 4, 'four niche tabs');
  assert.match(html, /role="tabpanel"/);
  assert.match(html, /role="switch"[\s\S]*?aria-checked/, 'the mode control is a switch');
  assert.match(html, /aria-live="polite"/, 'the thread announces new messages');
  assert.match(html, /<h2 id="demo-h"[^>]*>Попробуйте <em>сами<\/em>\.<\/h2>/);
  // every tab must name the panel it drives and start with exactly one selected
  assert.equal((html.match(/aria-controls="demo-win"/g) || []).length, 4);
  assert.equal((html.match(/aria-selected="true"/g) || []).length, 1);
});

test('the card is a real button, and its class is the one the page contract names', () => {
  assert.match(js, /el\('button', `demo-card/, 'suggestions must be <button> so Enter/Space work');
  assert.match(js, /b\.type = 'button'/);
  assert.match(js, /aria-label', `Отправить вариант/);
});

test('nothing in the module runs a rAF loop or touches WebGL', () => {
  const loops = js.match(/requestAnimationFrame/g) || [];
  assert.ok(loops.length <= 1, `expected at most one coalescing rAF, found ${loops.length}`);
  assert.ok(!/getContext\(|WebGL|canvas/i.test(js), 'the demo must not create a canvas');
  assert.match(js, /IntersectionObserver/, 'it must pause itself off screen');
  assert.match(js, /visibilitychange/, 'it must pause itself in a hidden tab');
});

// ---- invariants the round-1 review found the hard way (all three are source guards: the
// behaviour they pin needs a browser, but the shape that produced the bug is visible here) ----

test('the suggestion set can only shrink to three, and it grows back', () => {
  // the caption beside the panel promises «до 4 вариантов» and scenarios.test.mjs pins 3–4,
  // so the fitter must never leave two cards on a short window
  assert.match(js, /cardsEl\.children\.length > 3/, 'fitCards must stop at three cards');
  assert.ok(!/cardsEl\.children\.length > 2/.test(js), 'a floor of two contradicts the 3–4 contract');
  // ...and a window that grew again must get the dropped cards back: on the static board
  // (low tier / reduced motion) no next scenario ever rebuilds them
  const fit = js.slice(js.indexOf('function fitCards('), js.indexOf('function slot('));
  assert.ok(fit.length > 100 && fit.length < 1600, 'fitCards not found in one piece');
  assert.match(fit, /renderCards\(src\)/, 'fitCards must be able to re-render the full set');
});

test('a card is addressed by its data index, never by DOM position', () => {
  // fitCards removes nodes, so children[i] and s.cards[i] diverge as soon as `best` is not
  // the first card — the wrong suggestion would fly into the thread
  assert.match(js, /b\.dataset\.i = String\(i\)/, 'each card must carry its index');
  assert.match(js, /cardAt = \(i\) => cardsEl\.querySelector\(`\.demo-card\[data-i="\$\{i\}"\]`\)/);
  assert.ok(!/cardsEl\.children\[/.test(js), 'no positional lookup into the card list');
});

test('the ghost cursor never takes a card out from under a visitor', () => {
  const gr = js.slice(js.indexOf('async function ghostRun('), js.indexOf('// ---------- sending'));
  assert.ok(gr.length > 200, 'ghostRun not found');
  // busy() must be re-checked AFTER the ~1.8 s travel, not only in the wait loop before it
  assert.ok((gr.match(/busy\(\)/g) || []).length >= 3, 'busy() must be re-checked after the travel');
  assert.match(gr, /keyboardPick = focusInCards\(\)/, 'a picked-over focus must be handed to the thread');
  assert.ok(!/keyboardPick = false;\s*\n\s*if \(pending\) pick/.test(gr), 'the ghost must not force focus loss');
});

test('a cancelled choice is rejected, not left suspended', () => {
  assert.match(js, /const dropChoice = \(\) => \{[^}]*p\.rej\(CANCEL\)/, 'cancelling must settle the promise');
  // every `pending = null` must sit on the line that settles it (pick / dropChoice) or be the
  // declaration — a bare clear anywhere else is a suspended chain again
  const stray = js.split('\n').filter((l) => /pending = null;/.test(l) && !/const p = pending;|^\s*let pending/.test(l));
  assert.deepEqual(stray, [], `pending cleared without settling: ${stray.join(' | ')}`);
});

// ---- round-2 review invariants ----

const body = (src, start, end) => {
  const a = src.indexOf(start);
  const b = src.indexOf(end, a + start.length);
  assert.ok(a >= 0 && b > a, `«${start}» … «${end}» not found`);
  return src.slice(a, b);
};

test('a flight that is cancelled takes its flying copy with it', () => {
  // a tab click during the 860 ms flight rejects the sleep; the copy sits at z-index 6 above
  // the thread, so anything left in .dm-fly floats over every following chat
  const send = body(js, 'async function send(', 'async function together(');
  assert.match(send, /finally \{[^}]*c\.remove\(\)/, 'the flight\'s finally must remove the flying card too');
  const clear = body(js, 'function clearCards()', 'function openTray(');
  assert.match(clear, /fly\.replaceChildren\(\)/, 'a new round must empty the fly layer at once, not when the old timer fires');
});

test('every path that destroys a focused card hands focus to the thread first', () => {
  const st = body(js, 'function staticAnswer(', '// ---------- controls');
  const f = st.indexOf('thread.focus(');
  assert.ok(f >= 0, 'the static board must consume keyboardPick like send() does');
  const call = st.search(/^\s*clearCards\(\);/m); // the statement, not a mention in a comment
  assert.ok(call > 0 && f < call, 'focus must move BEFORE clearCards() destroys the button');
  const fit = body(js, 'function fitCards(', 'function slot(');
  const loop = fit.slice(fit.indexOf('while (cardsNeed > trayMax'));
  assert.match(loop, /document\.activeElement/, 'the shrink loop must not pick the focused card blindly');
});

test('the ghost never gives up on an open round', () => {
  const gr = body(js, 'async function ghostRun(', '// ---------- sending');
  assert.match(gr, /for \(let round = 0; pending; round \+= 1\)/, 'the approach loop may only end with the round');
  assert.ok(!/round < \d+/.test(gr), 'a bounded loop leaves the round pending with no ghost and no hint');
});

test('assistive tech: no role-less aria-label, one constant switch label, a quiet autoplay', () => {
  assert.match(html, /class="dm-thread" tabindex="-1" role="region" aria-label="Переписка"/, 'focus is parked on the thread — it needs a role');
  const labels = [...js.matchAll(/setAttribute\('aria-label'/g)].length;
  assert.equal(labels, 1, 'only the card <button> carries a runtime aria-label (typing node / status span / switch must not)');
  assert.ok(!/removeAttribute\('aria-label'\)/.test(js));
  for (const m of js.matchAll(/el\('span', 'dm-chip', 'Сегодня'\)/g)) {
    const tail = js.slice(m.index, m.index + 120);
    assert.match(tail, /aria-hidden/, 'the runtime «Сегодня» chip must be hidden like the static one');
  }
  assert.match(js, /setAttribute\('aria-live', 'off'\)/, 'the endless autoplay must not narrate itself');
  assert.match(js, /setAttribute\('aria-live', 'polite'\)/, '…and the log speaks again once the visitor acts');
});

test('the annotation column cannot re-flow while the script plays', () => {
  // the source line swaps between one- and two-line strings every scenario; without a fixed
  // box everything after the demo jumps (mobile) and the centred column re-centres (desktop).
  // Every string it can show is stacked invisibly in the same grid cell as the live line.
  assert.match(css, /\.dm-src-lines\{display:grid/, 'the source line must be a one-cell grid');
  assert.match(css, /\.dm-src-lines>span\{grid-area:1\/1/, '…with the live line and the sizers in the same cell');
  assert.match(css, /\.dm-src-sz\{visibility:hidden\}/);
  const life = js.slice(js.indexOf('if (srcLines) {'));
  assert.match(life.slice(0, 400), /for \(const sc of SCENARIOS\) for \(const st of \['', 'scan', 'hit'\]\) all\.add\(srcText\(sc, st\)\)/,
    'the sizers must cover every scenario × every state — the same srcText() setSrc() shows');
  assert.match(js, /srcLine\.textContent = srcText\(s, state\)/, 'setSrc must show only strings the sizers were built from');
  // the mode caption: both states in one cell, a flip toggles which one shows
  assert.match(html, /<p class="dm-cap-t"><span class="on" data-m="with">[\s\S]*?<\/span><span data-m="auto">/);
  assert.match(css, /\.dm-cap-t\{display:grid/);
  assert.ok(!/capT\.innerHTML/.test(js), 'rewriting the caption re-flows the column');
  // the «Сводка» list fills up to three rows: they are reserved
  assert.match(css, /\.dm-sv-list\{[^}]*min-height/, 'the «Сводка» list reserves its three rows');
});

test('the three-column stage starts where the inbox fits, and the tab sub-line can ellipsize', () => {
  assert.ok(!/@media \(min-width:1024px\)/.test(css), 'at 1024–1099 the side columns are 225 px and clip the inbox');
  assert.match(css, /@media \(min-width:1100px\)/);
  assert.match(html, /<span class="dm-tab-sub"><i><\/i><span>/, 'the sub-line text needs an inline box for text-overflow');
  assert.match(css, /\.dm-tab-sub>span\{[^}]*text-overflow:ellipsis/);
});

test('every colour in 40-demo.css is a 01-base token or an rgba of one', () => {
  const base = fs.readFileSync(new URL('../../src/css/01-base.css', import.meta.url), 'utf8');
  const rgb = new Set(['255,255,255', '0,0,0']);
  for (const m of base.matchAll(/--[a-z0-9-]+:#([0-9a-f]{6})/gi)) {
    const h = m[1];
    rgb.add([0, 2, 4].map((k) => parseInt(h.slice(k, k + 2), 16)).join(','));
  }
  const bad = [...css.matchAll(/rgba?\((\d+),\s*(\d+),\s*(\d+)/g)].map((m) => `${m[1]},${m[2]},${m[3]}`).filter((t) => !rgb.has(t));
  assert.deepEqual([...new Set(bad)], [], `non-token colours: ${[...new Set(bad)].join(' | ')}`);
  const hexes = [...css.matchAll(/#[0-9a-f]{3,8}\b/gi)].map((m) => m[0].toLowerCase()).filter((x) => x !== '#fff' && x !== '#000');
  assert.deepEqual(hexes, [], `raw hex in 40-demo.css: ${hexes.join(' ')}`);
});

test('a new tray height is committed without the slide transition', () => {
  // the tray's height follows --trayH at once but its transform would transition for .75 s:
  // for that long the composer is cut by the window edge (resize, rotation, first measure)
  const m = body(js, 'function measureTray()', 'const measureCards');
  const off = m.indexOf("tray.style.transition = 'none'");
  const set = m.indexOf("win.style.setProperty('--trayH'");
  assert.ok(off >= 0 && set > off, 'transition must be off while --trayH changes');
  assert.ok(m.indexOf("tray.style.transition = ''") > set, '…and restored for the next --trayV move');
});

// ---- round-3 review invariants ----

const rule = (src, sel) => {
  const m = src.match(new RegExp(`(?:^|\\n)${sel.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\{([^}]*)\\}`));
  assert.ok(m, `no top-level rule for ${sel}`);
  return m[1];
};

test('the window and its clipping boxes are not scroll containers', () => {
  // the folded tray is moved below the window edge by a transform, which still counts as
  // scrollable overflow: an `overflow:hidden` window is programmatically scrollable, and
  // find-in-page / a screen reader's cursor scrolled it for good (header and switch cut off)
  // (a clamped card text is the same trap in miniature: find-in-page scrolls its hidden line in)
  // (round 4: the composer field parks its two status lines one field-height below by a transform —
  // the same trap; a find for «номер» blanked the composer for the rest of the visit)
  for (const sel of ['.dm-win', '.dm-thread', '.dm-sug', '.dm-card-x', '.dm-c-field']) {
    assert.match(rule(css, sel), /overflow:hidden;overflow:clip/, `${sel}: clip, with hidden only as the fallback`);
  }
  // …and generically: no text-bearing box in the section may be a hidden scroll container. Every
  // `overflow:hidden` must be followed by `overflow:clip`, except the boxes that hold no text.
  const NO_TEXT = ['.demo .sr', '.dm-defs', '.dm-tab-prog', '.dm-pl-bars i', '.dm-src-row'];  // .dm-src holds text: it clips too (Task 12)
  for (const m of css.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/(?:^|[}\n])\s*([^{}]+)\{([^{}]*)\}/g)) {
    const sel = m[1].trim(), decl = m[2];
    if (!/overflow:hidden/.test(decl) || /overflow:hidden;overflow:clip/.test(decl)) continue;
    assert.ok(NO_TEXT.includes(sel), `${sel}: overflow:hidden without overflow:clip is a scroll container`);
  }
  // where `clip` is unsupported the boxes fall back to `hidden`: any scroll is undone at once
  assert.match(js, /const clipBoxes = \[win, thread, \$\('\.dm-sug'\), \$\('\.dm-c-field'\)\]/);
  assert.match(js, /clipBoxes\.forEach\(\(n\) => n\.addEventListener\('scroll', unscroll/);
  assert.match(body(js, 'destroy() {', '\n  };\n}'), /clipBoxes\.forEach\(\(n\) => n\.removeEventListener\('scroll', unscroll/);
});

test('the desktop window fits a short laptop viewport', () => {
  // 68 px fixed header + a 640 px window = 708 px: a 1366×768 laptop has ~650–660 inside the
  // browser, so the client's question and the cards were never on screen together
  const desk = body(css, '@media (min-width:1100px){', '.dm-msg{font-size:15px}');
  assert.match(desk, /\.dm-win\{height:clamp\(560px,calc\(100vh - 120px\),640px\);height:clamp\(560px,calc\(100svh - 120px\),640px\)/);
  assert.ok(!/\.dm-win\{height:640px/.test(css), 'a fixed 640 px desktop window is back');
});
