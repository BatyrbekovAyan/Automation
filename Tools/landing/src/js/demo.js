// 40 — «Попробуйте сами». Plays four scripted conversations in a chat window: the client
// writes, the bot checks the price list, suggestions rise into the keyboard slot, the visitor
// (or a ghost cursor, after ~3 s) picks one — the chosen card lifts out of the tray, flies
// across and BECOMES the sent bubble. «Авто» skips the cards and answers by itself.
//
// Budget rules this module keeps:
//  · no rAF loop at all — every motion is a Web Animations API transform/opacity animation,
//    so the compositor owns it and nothing runs on the main thread between beats;
//  · the script is a chain of `sleep()`s that FREEZE while the section is off screen, while
//    the tab is hidden, or while the visitor paused «Автопоказ» — and every live animation
//    is paused with them (CSS keyframes via `.is-paused`, WAAPI via the registry);
//  · layout is read at discrete moments (tray measure, card→bubble hand-off), never per frame;
//  · on the low tier / reduced motion the whole dialog is laid out at once and autoplay is off:
//    the demo is then a static, fully clickable board.
import { SCENARIOS } from './demo/scenarios.js';

const CANCEL = { cancel: true };
const TICKS = '<svg class="dm-tk" viewBox="0 0 20 12" aria-hidden="true"><path class="t1" d="M1.5 6.5l3.2 3.2L11 3"/><path class="t2" d="M8.2 9.1l.6.6L15.5 3"/></svg>';
const SPARK = '<svg aria-hidden="true"><use href="#dm-spark"/></svg>';
const GRID = '<svg viewBox="0 0 20 20" aria-hidden="true"><rect x="2.6" y="2.6" width="6.2" height="6.2" rx="1.6"/><rect x="11.2" y="2.6" width="6.2" height="6.2" rx="1.6"/><rect x="2.6" y="11.2" width="6.2" height="6.2" rx="1.6"/><rect x="11.2" y="11.2" width="6.2" height="6.2" rx="1.6"/></svg>';
const KINDS = { xlsx: 'XLSX', pdf: 'PDF', jpg: 'JPG' };
const MAP = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' };
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => MAP[c]);
const CH = { wa: 'WhatsApp', tg: 'Telegram' };
// «прайс» for a file, «каталог» for the in-app catalogue — the wording must match what was read.
const where = (kind) => (kind === 'catalog' ? ['каталогом', 'каталоге'] : ['прайсом', 'прайсе']);

export function init(root, ctx = {}) {
  const $ = (sel, n = root) => n.querySelector(sel);
  const $$ = (sel, n = root) => [...n.querySelectorAll(sel)];

  const win = $('.dm-win');
  const list = $('.dm-list');
  if (!win || !list) return;

  const thread = $('.dm-thread');
  const tray = $('.dm-tray');
  const comp = $('.dm-comp');
  const cardsEl = $('.dm-cards');
  const hint = $('.dm-hint');
  const pl = $('.dm-pl');
  const plT = $('.dm-pl-t');
  const fly = $('.dm-fly');
  const ghost = $('.dm-ghost');
  const flash = $('.dm-flash');
  const who = $('.dm-who');
  const nameEl = $('.dm-name');
  const avaEl = $('.dm-ava');
  const chanEl = $('.dm-ch');
  const stEl = $('.dm-st');
  const modeBtn = $('.dm-mode');
  const tabs = $$('.dm-tab');
  const srcEl = $('.dm-src');
  const srcKind = $('.dm-src-kind');
  const srcTitle = $('.dm-src-title');
  const srcLine = $('.dm-src-line');
  const srcLines = $('.dm-src-lines');
  const capNote = $('.dm-cap');
  const capT = $('.dm-cap-t');
  const svNote = $('.dm-sv');
  const svList = $('.dm-sv-list');
  const playBtn = $('.dm-play');

  const RM = ctx.reduced === true || ctx.tier === 'low';
  const TOUCH = matchMedia('(pointer: coarse)').matches;
  if (TOUCH) root.classList.add('dm-touch');
  if (hint) hint.textContent = TOUCH ? 'Коснитесь варианта' : 'Нажмите на вариант';

  // ---------- clock: a pausable, cancellable sleep + a registry of live animations ----------
  let gen = 0;
  let dead = false;
  let offscreen = true;
  let userPaused = false;
  let paused = true;
  let waiters = [];
  const timers = new Set();
  const anims = new Set();
  const held = new Set();

  function sleep(ms, g) {
    return new Promise((res, rej) => {
      const t = setTimeout(async () => {
        timers.delete(t);
        while (paused && !dead) await new Promise((r) => waiters.push(r));
        if (g === gen && !dead) res(); else rej(CANCEL);
      }, RM ? 0 : ms);
      timers.add(t);
    });
  }

  function anim(node, frames, opts) {
    if (RM || !node) return null;
    let a;
    try { a = node.animate(frames, opts); } catch { return null; }
    anims.add(a);
    const off = () => { anims.delete(a); held.delete(a); };
    a.finished.then(off, off);
    if (paused) { a.pause(); held.add(a); }
    return a;
  }

  let pauseWritten = false;
  function applyPause() {
    const p = offscreen || userPaused || document.hidden;
    // The FIRST call always writes the class: `paused` starts true while `.is-paused` is absent,
    // so an early return would leave the CSS half of the pause contract unset while off screen.
    if (p === paused && pauseWritten) return;
    pauseWritten = true;
    paused = p;
    root.classList.toggle('is-paused', p);
    if (p) {
      for (const a of anims) if (a.playState === 'running') { a.pause(); held.add(a); }
    } else {
      for (const a of held) { try { a.play(); } catch { /* finished */ } }
      held.clear();
      waiters.splice(0).forEach((r) => r());
    }
  }

  // ---------- tiny DOM helpers ----------
  function el(tag, cls, html) {
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    if (html != null) n.innerHTML = html;
    return n;
  }
  const cssPx = (node, name, dflt) => parseFloat(getComputedStyle(node).getPropertyValue(name)) || dflt;

  function bubble(dir, text, time, from, bot) {
    const b = el('div', `dm-msg ${dir}`,
      (bot ? `<span class="dm-bot">${SPARK}ИИ-сотрудник · «Авто»</span>` : '')
      + `<span class="sr">${esc(from)}: </span><span class="dm-tx">${esc(text)}</span>`
      + `<span class="dm-meta">${esc(time)}${dir === 'out' ? TICKS : ''}</span>`);
    return b;
  }
  // Three dots are decoration: the header already says «печатает…», and an aria-label on a
  // role-less <div> is ignored anyway. Hidden, so the log announces only real messages.
  function typingNode(dir) {
    const n = el('div', `dm-msg ${dir} dm-typing`, '<i></i><i></i><i></i>');
    n.setAttribute('aria-hidden', 'true');
    return n;
  }
  // the runtime twin of the static chip, hidden from AT the same way
  const dayChip = () => { const n = el('span', 'dm-chip', 'Сегодня'); n.setAttribute('aria-hidden', 'true'); return n; };

  // The list is bottom-anchored, so growth slides it up by the height delta: animate that
  // delta away and the thread reads as pushing the history upward instead of jumping.
  let listAnim = null;
  function mutate(fn) {
    const h0 = list.offsetHeight;
    fn();
    const d = list.offsetHeight - h0;
    if (d && !RM) {
      let off = 0;
      if (listAnim && listAnim.playState === 'running') {
        try { off = new DOMMatrixReadOnly(getComputedStyle(list).transform).m42; } catch { off = 0; }
        listAnim.cancel();
      }
      listAnim = anim(list, [{ transform: `translateY(${off + d}px)` }, { transform: 'translateY(0)' }],
        { duration: 560, easing: 'cubic-bezier(.22,.9,.24,1)' });
    }
    while (list.children.length > 12) list.firstElementChild.remove();
    return d;
  }
  function popIn(n) {
    anim(n, [{ opacity: 0, transform: 'translateY(12px) scale(.92)' }, { opacity: 1, transform: 'none' }],
      { duration: 440, easing: 'cubic-bezier(.2,.9,.3,1.18)' });
    return n;
  }
  const push = (n) => { mutate(() => list.appendChild(n)); return popIn(n); };
  const swap = (a, b) => { mutate(() => a.replaceWith(b)); return popIn(b); };

  // ---------- header / annotations ----------
  function setHeader(s) {
    nameEl.textContent = s.name;
    avaEl.textContent = s.name[0];
    chanEl.textContent = CH[s.channel];
    win.dataset.ch = s.channel;
    srcKind.innerHTML = KINDS[s.source.kind] ? esc(KINDS[s.source.kind]) : GRID;
    srcTitle.textContent = s.source.title;
    srcTitle.title = s.source.title;
    setSrc(s, '');
  }
  const srcText = (s, state) => {
    const [with_, in_] = where(s.source.kind);
    return state === 'scan' ? `сверяется с ${with_}…` : state === 'hit' ? `нашёл в ${in_}: ${s.source.find}` : 'источник для ответов';
  };
  function setSrc(s, state) {
    srcEl.dataset.state = state;
    srcLine.textContent = srcText(s, state);
  }
  function setTyping(on) {
    stEl.textContent = on ? 'печатает…' : 'в сети';
    stEl.classList.toggle('is-typing', on);
    who.classList.toggle('typing', on);
  }
  let plOff = 0;
  function setLoader(state, html) {
    if (plOff) { clearTimeout(plOff); timers.delete(plOff); plOff = 0; }
    pl.classList.toggle('on', !!state);
    // Leaving: hold BOTH the «scan» and the «done» look through the fade. Dropping them here
    // makes the filled bar snap back and — since `.done` is what hides the three bars — pops
    // the whole bar stack in under the verdict for the 300 ms the block takes to fade out.
    if (state) { pl.classList.toggle('scan', state === 'scan'); pl.classList.toggle('done', state === 'done'); }
    else {
      // capture the id in a local: a later setLoader reassigns `plOff`, and a callback closing
      // over the mutable name would then unregister the NEW timer instead of its own
      const id = setTimeout(() => { pl.classList.remove('scan', 'done'); timers.delete(id); if (plOff === id) plOff = 0; }, 340);
      plOff = id;
      timers.add(id);
    }
    if (html != null && plT.innerHTML !== html) {
      plT.innerHTML = html;
      if (state) anim(plT, [{ opacity: 0, transform: 'translateY(5px)' }, { opacity: 1, transform: 'none' }], { duration: 320, easing: 'ease-out' });
    }
  }
  // Both captions live stacked in one grid cell (40-demo.css), so the note is as tall as the
  // longer of the two and a mode flip cannot re-flow the column; only the visible one changes.
  const capSpans = capT ? [...capT.children] : [];
  function setCap() {
    for (const n of capSpans) n.classList.toggle('on', n.dataset.m === mode);
    lit(capNote, true);
  }
  function lit(node, on) { if (node) node.classList.toggle('lit', !!on); }

  function svodka(s) {
    if (!svList) return;
    const row = el('li', 'dm-sv-row', `<i aria-hidden="true"></i><b>${esc(s.name)}</b><span>${esc(s.outcome.label)}</span>`);
    row.dataset.st = s.outcome.status;
    // FLIP: the new row lands on top and the older ones SLIDE down to make room (a transform —
    // a bare jump of every row is a layout shift, and reads as a glitch). One read per scenario.
    const rows = [...svList.children];
    const y0 = rows.map((r) => r.offsetTop);
    const empty = svList.querySelector('.dm-sv-empty');
    if (empty) empty.remove();
    const twin = [...svList.children].find((r) => r.dataset.name === s.id);
    if (twin) twin.remove();
    row.dataset.name = s.id;
    svList.prepend(row);
    while (svList.children.length > 3) svList.lastElementChild.remove();
    rows.forEach((r, k) => {
      const d = r.isConnected ? y0[k] - r.offsetTop : 0;
      if (d) anim(r, [{ transform: `translateY(${d}px)` }, { transform: 'none' }], { duration: 500, easing: 'cubic-bezier(.22,.9,.24,1)' });
    });
    svNote.dataset.st = s.outcome.status;
    lit(svNote, true);
    anim(row, [{ opacity: 0, transform: 'translateY(-8px)' }, { opacity: 1, transform: 'none' }], { duration: 500, easing: 'ease-out' });
    const tab = tabs[SCENARIOS.indexOf(s)];
    if (tab) tab.querySelector('.dm-tab-st').dataset.st = s.outcome.status;
  }

  // ---------- tabs ----------
  function setTab(i) {
    tabs.forEach((t, j) => {
      const on = j === i;
      t.setAttribute('aria-selected', String(on));
      t.tabIndex = on ? 0 : -1;
      const bar = t.querySelector('.dm-tab-prog i');
      if (!on) t.style.setProperty('--p', '0');
      else if (bar) { bar.style.transition = 'none'; t.style.setProperty('--p', '0'); void bar.offsetWidth; bar.style.transition = ''; }
    });
    win.setAttribute('aria-labelledby', tabs[i] ? tabs[i].id : '');
  }
  const prog = (v) => { const t = tabs[cur]; if (t) t.style.setProperty('--p', String(v)); };

  // ---------- the slot under the composer: composer | + loader | + cards ----------
  let trayMax = 0;
  let cardsNeed = 0;
  let detent = 'closed';
  // The slot may take everything except the chat header and a strip of thread — enough to keep
  // the client's question in sight. Short windows (a 640-tall phone) give the thread less.
  function measureTray() {
    const winH = win.clientHeight;
    const reserve = Math.min(150, Math.max(96, winH * 0.2));
    trayMax = Math.max(160, Math.round(winH - cssPx(root, '--dm-headH', 62) - reserve));
    const v = `${trayMax}px`;
    if (win.style.getPropertyValue('--trayH') === v) return;
    // The tray's HEIGHT follows --trayH at once, but its transform (trayH − trayV) would TRANSITION
    // over .75 s — for that long the visible slot is off by the delta: the composer is cut by the
    // window edge (a smaller window, a rotation) or the panel peeks above it. Commit the new
    // geometry without the transition; only a --trayV change is a motion.
    tray.style.transition = 'none';
    win.style.setProperty('--trayH', v);
    void tray.offsetHeight;
    tray.style.transition = '';
  }
  const measureCards = () => Math.round(cssPx(root, '--dm-barH', 62) + cardsEl.offsetTop + cardsEl.offsetHeight + 14);
  // «До 4 вариантов» is the promise, so a window too short for four shows three rather than
  // cutting the last one in half: a clipped card would read as a broken panel. THREE is the
  // floor — 3–4 is the data contract and the caption beside the panel says «до 4 вариантов».
  // The fit runs BOTH ways: a window that grew again gets the full set back. On the static
  // board (low tier / reduced motion) nothing rebuilds the cards, so a one-way shrink would
  // keep the reduced panel — and a short tray — for the rest of the visit.
  function fitCards(s) {
    const src = s || SCENARIOS[cur];
    const n = cardsEl.children.length;
    if (src && n && n < src.cards.length && !cardsEl.contains(document.activeElement)
      && measureCards() + (cardsEl.offsetHeight / n) * (src.cards.length - n) <= trayMax) renderCards(src);
    cardsNeed = measureCards();
    while (cardsNeed > trayMax && cardsEl.children.length > 3) {
      // the last card that is neither the best one nor the one holding keyboard focus (a rotation
      // while a keyboard visitor sits on card #4 must not drop them to <body>)
      const f = document.activeElement;
      const last = [...cardsEl.children].reverse().find((c) => !c.classList.contains('is-best') && c !== f) || cardsEl.lastElementChild;
      if (last === f) thread.focus({ preventScroll: true });
      last.remove();
      cardsNeed = measureCards();
    }
    cardsNeed = Math.min(cardsNeed, trayMax);
  }
  function slot(v) {
    detent = v;
    const barH = cssPx(root, '--dm-barH', 62);
    const px = v === 'cards' ? (cardsNeed || trayMax)
      : v === 'wait' ? Math.min(trayMax, barH + 36 + cssPx(root, '--dm-plH', 150) + 6)
        : barH;
    win.style.setProperty('--trayV', `${Math.round(px)}px`);
  }

  // ---------- suggestion cards ----------
  let pending = null;
  const pick = (i, byUser) => { if (!pending) return; const p = pending; pending = null; if (byUser) hideGhost(); p.res({ i, u: byUser }); };
  // A round that is cancelled (tab, mode flip, destroy) must REJECT: a promise that can only be
  // settled through `pending` would otherwise strand its whole await-chain for the page's life.
  const dropChoice = () => { if (!pending) return; const p = pending; pending = null; p.rej(CANCEL); };
  const waitChoice = () => new Promise((res, rej) => { pending = { res, rej }; });

  // `renderCards` lays the full set out; `fitCards` decides how many of them survive. They are
  // split because the fitter has to be able to put the dropped ones back after a resize.
  const cardAt = (i) => cardsEl.querySelector(`.demo-card[data-i="${i}"]`);
  function renderCards(s) {
    cardsEl.innerHTML = '';
    s.cards.forEach((c, i) => {
      const b = el('button', `demo-card${c.best ? ' is-best' : ''}`,
        `${c.best ? '<span class="dm-glow" aria-hidden="true"></span>' : ''}<span class="dm-card-bg" aria-hidden="true"></span>`
        + `<span class="dm-card-l">${c.best ? SPARK : ''}${esc(c.title)}</span>`
        + `<span class="dm-card-x">${esc(c.text)}</span>`);
      b.type = 'button';
      // the index into s.cards, NOT the DOM position: fitCards may have dropped a card, and
      // `send()` has to fly the one the visitor actually chose
      b.dataset.i = String(i);
      b.setAttribute('aria-label', `Отправить вариант «${c.title}»: ${c.text}`);
      b.addEventListener('click', () => onPick(i, b));
      cardsEl.appendChild(b);
    });
  }
  function buildCards(s) { renderCards(s); fitCards(s); }
  function onPick(i, card) {
    // A card vanishes the moment it is chosen, so a keyboard visitor would be dropped to <body>.
    // «Was this a pointer click?» is answered by a pointerdown in the last moment, not by
    // :focus-visible — programmatic focus does not always set it.
    keyboardPick = performance.now() - lastPointer > 300 || !!(card && card.matches(':focus-visible'));
    voice();
    engage(4000);
    if (userPaused) setAutoplay(true); // a tap on a card is a «play» request
    if (pending) { pick(i, true); return; }
    if (RM) { keyboardPick = false; return; } // the static board answers through `pending`
    // The visitor tapped while the script was elsewhere: answer from their choice.
    run(cur, 'pick', i);
  }
  function clearCards() {
    // A new round starts here, synchronously on the tab click: a flight still in the air belongs
    // to the chat being left. Its own `finally` only runs when its timer fires, up to 860 ms on.
    fly.replaceChildren();
    cardsEl.innerHTML = '';
    cardsEl.style.visibility = '';
    cardsEl.style.pointerEvents = '';
    comp.classList.remove('sent');
    hint.classList.remove('on');
    dropChoice();
    setLoader(null);
  }
  // In «Вместе» the panel owns the slot from the moment the chat opens (as in the app), so it
  // is never an empty dark box: build the cards invisibly, size the tray, then open.
  function openTray(s, text) {
    if (!cardsEl.children.length) { buildCards(s); cardsEl.style.visibility = 'hidden'; }
    slot('wait');
    if (text) setLoader('wait', text);
  }
  function revealCards(s) {
    if (!cardsEl.children.length) buildCards(s); // a resize dropped them while they waited
    slot('cards');
    cardsEl.style.visibility = '';
    hint.classList.add('on');
    [...cardsEl.children].forEach((b, i) => {
      const d = i * 85;
      // «pill → card»: each background grows out of a thin capsule, like the app icon's bars
      anim(b.querySelector('.dm-card-bg'),
        [{ transform: 'scale(.62,.22)', opacity: 0 }, { transform: 'scale(.9,.3)', opacity: 1, offset: 0.32 }, { transform: 'none', opacity: 1 }],
        { duration: 640, delay: d, easing: 'cubic-bezier(.2,.8,.2,1)', fill: 'backwards' });
      [...b.querySelectorAll('.dm-card-l,.dm-card-x')].forEach((n, k) =>
        anim(n, [{ opacity: 0, transform: 'translateY(7px)' }, { opacity: 1, transform: 'none' }],
          { duration: 420, delay: d + 190 + k * 55, easing: 'ease-out', fill: 'backwards' }));
    });
    anim(cardsEl.querySelector('.demo-card.is-best .dm-glow'),
      [{ opacity: 0 }, { opacity: 1, offset: 0.4 }, { opacity: 0 }], { duration: 1500, delay: 380, easing: 'ease-out' });
  }

  // ---------- ghost cursor ----------
  let hovering = false;
  let engagedUntil = 0;
  let ghostOn = false;
  let gx = 0;
  let gy = 0;
  let keyboardPick = false;
  let lastPointer = -1e9;
  const engage = (ms) => { engagedUntil = Math.max(engagedUntil, performance.now() + ms); };
  const winXY = (r, fx, fy) => { const d = win.getBoundingClientRect(); return [r.left - d.left + r.width * fx, r.top - d.top + r.height * fy]; };

  async function moveGhost(target, g) {
    const [x1, y1] = winXY(target.getBoundingClientRect(), TOUCH ? 0.62 : 0.7, 0.58);
    if (!ghostOn) {
      gx = win.clientWidth - 46;
      gy = win.clientHeight - 24;
      ghostOn = true;
      anim(ghost, [{ opacity: 0 }, { opacity: 1 }], { duration: 300, fill: 'forwards' });
    }
    const mx = gx + (x1 - gx) * 0.5 + 50;
    const my = gy + (y1 - gy) * 0.5 + 34;
    const a = anim(ghost, [
      { transform: `translate(${gx}px,${gy}px)` },
      { transform: `translate(${mx.toFixed(1)}px,${my.toFixed(1)}px)`, offset: 0.55 },
      { transform: `translate(${x1.toFixed(1)}px,${y1.toFixed(1)}px)` }],
    { duration: 1000, easing: 'cubic-bezier(.45,0,.2,1)', fill: 'forwards' });
    gx = x1; gy = y1;
    await sleep(620, g);
    target.classList.add('hot');
    await sleep(400, g);
    ghost.style.transform = `translate(${x1.toFixed(1)}px,${y1.toFixed(1)}px)`;
    if (a) a.cancel();
  }
  function hideGhost() {
    $$('.demo-card.hot', cardsEl).forEach((c) => c.classList.remove('hot'));
    if (!ghostOn) return;
    ghostOn = false;
    anim(ghost, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'forwards' });
    if (RM) ghost.style.opacity = '0';
  }
  const focusInCards = () => cardsEl.contains(document.activeElement) && document.activeElement !== document.body;
  const busy = () => hovering || performance.now() < engagedUntil || focusInCards();
  async function ghostRun(s, g) {
    await sleep(1250 * K, g);
    const bi = s.cards.findIndex((c) => c.best);
    // Several APPROACHES, not one: the wait loop before the travel is not enough, because the
    // travel itself takes ~1.8 s. A visitor who reaches for the cards in that window gets the
    // round handed back — the ghost must never pull a button out from under a hand, and never
    // destroy the button that holds keyboard focus (that would drop the visitor to <body>).
    // The loop ends only with the round: after 8 aborted approaches the visitor plainly has the
    // cards in hand, so the ghost stops dodging, shows the hint for the full 30 s cap and only
    // then answers. A bounded loop that just gave up left the round pending, the hint off and
    // autoplay stalled for good.
    for (let round = 0; pending; round += 1) {
      const last = round >= 8;
      let waited = 0;
      // Someone is looking at the cards — wait them out rather than answering over their hand.
      while (pending && (busy() || last) && waited < 30000) { hint.classList.add('on'); await sleep(250, g); waited += 250; }
      if (!pending) return;
      // waited out the cap: answer anyway, but then hand focus to the thread, so the visitor
      // is still somewhere sensible when the focused card disappears.
      const capped = waited >= 30000;
      if (waited) await sleep(700, g);
      hint.classList.remove('on');
      const target = cardAt(bi);
      if (!target) return;
      await moveGhost(target, g);
      if (!pending) { hideGhost(); return; }
      if (busy() && !capped) { hideGhost(); continue; }
      anim(ghost.querySelector('.dm-g-ring'), [{ transform: 'scale(.4)', opacity: 1 }, { transform: 'scale(1.5)', opacity: 0 }], { duration: 520, easing: 'ease-out' });
      const base = ghost.style.transform;
      anim(ghost, [{ transform: `${base} scale(1)` }, { transform: `${base} scale(.82)` }, { transform: `${base} scale(1)` }], { duration: 260 });
      anim(target, [{ transform: 'scale(1)' }, { transform: 'scale(.97)' }, { transform: 'scale(1)' }], { duration: 260 });
      await sleep(200, g);
      if (!pending) { hideGhost(); return; }
      if (busy() && !capped) { hideGhost(); continue; }
      keyboardPick = focusInCards();
      pick(bi, false);
      await sleep(350, g);
      hideGhost();
      return;
    }
    hideGhost();
  }

  // ---------- sending: the card lifts, flies and becomes the bubble ----------
  // Positions are window-local LAYOUT coordinates (the offset chain plus each ancestor's own
  // transform), not screen rects — the tray is mid-slide while we measure.
  function local(n, stopAt) {
    let x = 0; let y = 0;
    for (let e = n; e && e !== win; e = e.offsetParent) {
      x += e.offsetLeft; y += e.offsetTop;
      if (e !== n && e !== stopAt) {
        const tf = getComputedStyle(e).transform;
        if (tf && tf !== 'none') { try { const m = new DOMMatrixReadOnly(tf); x += m.m41; y += m.m42; } catch { /* ignore */ } }
      }
    }
    return [x, y];
  }

  async function send(s, i, g) {
    stage = 'sending';
    const card = cardAt(i);
    const b = bubble('out', s.cards[i].text, s.times[1], 'Вы');
    if (RM || !card) {
      clearCards(); slot('closed'); push(b);
      b.classList.add('t1s', 't2', 'read');
      comp.classList.add('sent');
      stage = 'answered';
      if (keyboardPick) { keyboardPick = false; thread.focus({ preventScroll: true }); }
      return;
    }
    // Hand focus over BEFORE touching the buttons: `disabled` drops focus to <body>, so a
    // keyboard visitor would spend the whole flight there. The thread is where the answer lands.
    if (keyboardPick) { keyboardPick = false; thread.focus({ preventScroll: true }); }
    // the choice is made: the losing cards must stop being click targets while they fade
    cardsEl.style.pointerEvents = 'none';
    [...cardsEl.children].forEach((c, j) => {
      c.disabled = true;
      if (j !== i) anim(c, [{ opacity: 1 }, { opacity: 0, transform: 'translateY(10px) scale(.96)' }], { duration: 260, fill: 'forwards' });
    });
    b.style.opacity = '0';
    mutate(() => list.appendChild(b));
    const [fx, fy] = local(card);
    const [tx, ty] = local(b, list);
    const cw = card.offsetWidth; const chh = card.offsetHeight;
    const bw = b.offsetWidth; const bh = b.offsetHeight;
    const sx = bw / cw; const sy = bh / chh;

    const c = el('div', `dm-flyc${card.classList.contains('is-best') ? ' best' : ''}`, '<span class="dm-fl-g"></span><span class="dm-fl-a"></span><span class="dm-fl-b"></span>');
    const ta = el('div', 'demo-card dm-fl-ta', card.innerHTML);
    ta.setAttribute('aria-hidden', 'true'); // a flying copy must not double the card for a screen reader
    ta.querySelectorAll('.dm-card-bg,.dm-glow').forEach((n) => n.remove());
    const tb = b.cloneNode(true);
    tb.setAttribute('aria-hidden', 'true');
    tb.classList.add('dm-fl-tb');
    tb.style.cssText = `width:${bw}px;opacity:0`;
    c.append(ta, tb);
    c.style.cssText = `left:${fx}px;top:${fy}px;width:${cw}px;height:${chh}px`;

    const dx = tx - fx; const dy = ty - fy; const dur = 860; const E = 'cubic-bezier(.62,0,.22,1)';
    const lift = Math.min(60, Math.abs(dy) * 0.35 + 18);
    const path = [
      { transform: 'translate(0,0) scale(1)' },
      { transform: 'translate(0,-6px) scale(1.035)', offset: 0.14 },
      { transform: `translate(${(dx * 0.45).toFixed(1)}px,${(dy * 0.5 - lift).toFixed(1)}px) scale(1.05) rotate(-1.8deg)`, offset: 0.56 },
      { transform: `translate(${dx.toFixed(1)}px,${dy.toFixed(1)}px) scale(1)` }];
    // a luminous trail: two translucent echoes on the same path, a beat behind
    const echoes = [70, 140].map((lag, k) => {
      const e = el('span', 'dm-fle');
      e.style.cssText = `left:${fx}px;top:${fy}px;width:${cw}px;height:${chh}px`;
      fly.appendChild(e);
      anim(e, path, { duration: dur, delay: lag, easing: E, fill: 'forwards' });
      anim(e, [{ opacity: 0 }, { opacity: 0, offset: 0.2 }, { opacity: k ? 0.16 : 0.3, offset: 0.55 }, { opacity: 0 }], { duration: dur, delay: lag });
      return e;
    });
    fly.appendChild(c);
    card.style.visibility = 'hidden';
    anim(c, path, { duration: dur, easing: E, fill: 'forwards' });
    const grow = [{ transform: 'none' }, { transform: 'none', offset: 0.18 }, { transform: `scale(${sx.toFixed(3)},${sy.toFixed(3)})` }];
    c.querySelectorAll('.dm-fl-a,.dm-fl-b,.dm-fl-g').forEach((n) => anim(n, grow, { duration: dur, easing: E, fill: 'forwards' }));
    anim(c.querySelector('.dm-fl-a'), [{ opacity: 1 }, { opacity: 1, offset: 0.2 }, { opacity: 0, offset: 0.6 }, { opacity: 0 }], { duration: dur, fill: 'forwards' });
    anim(c.querySelector('.dm-fl-b'), [{ opacity: 0 }, { opacity: 0, offset: 0.2 }, { opacity: 1, offset: 0.6 }, { opacity: 1 }], { duration: dur, fill: 'forwards' });
    anim(c.querySelector('.dm-fl-g'), [{ opacity: 0 }, { opacity: 1, offset: 0.35 }, { opacity: 0.9, offset: 0.7 }, { opacity: 0 }], { duration: dur, fill: 'forwards' });
    anim(ta, [{ opacity: 1 }, { opacity: 1, offset: 0.25 }, { opacity: 0, offset: 0.5 }, { opacity: 0 }], { duration: dur, fill: 'forwards' });
    anim(tb, [{ opacity: 0 }, { opacity: 0, offset: 0.45 }, { opacity: 1, offset: 0.82 }, { opacity: 1 }], { duration: dur, fill: 'forwards' });

    let landed = false;
    // Cancelled mid-air (a tab, destroy): the copy is forward-filled at its landing transform at
    // z-index 6, above thread and tray — nothing else would ever take it down, so it goes here.
    try { await sleep(dur, g); landed = true; } finally { echoes.forEach((e) => e.remove()); if (!landed) c.remove(); }
    b.style.opacity = '';
    const bye = anim(c, [{ opacity: 1 }, { opacity: 0 }], { duration: 160, fill: 'forwards' });
    const drop = () => c.remove();
    if (bye) bye.finished.then(drop, drop); else drop(); // `finished` settles on cancel() too
    // landing: a small overshoot, a ripple, then ✓ is drawn and the tray folds back down
    anim(b, [{ transform: 'scale(1.04)' }, { transform: 'none' }], { duration: 440, easing: 'cubic-bezier(.2,.9,.3,1.25)' });
    const rip = el('i', 'dm-rip');
    b.appendChild(rip);
    const ra = anim(rip, [{ transform: 'scale(1)', opacity: 1 }, { transform: 'scale(1.1,1.35)', opacity: 0 }], { duration: 700, easing: 'cubic-bezier(.2,.8,.3,1)' });
    if (ra) ra.onfinish = () => rip.remove(); else rip.remove();
    b.classList.add('t1s');
    await sleep(220, g);
    comp.classList.add('sent');
    // Fill the panel BEFORE moving the slot: dropping to the `wait` detent first and only then
    // writing the loader leaves an empty dark box for the whole 750 ms slide. The losing cards
    // finished fading ~600 ms ago, so clearing them here is invisible.
    cardsEl.innerHTML = '';
    cardsEl.style.pointerEvents = '';
    if (mode === 'with') setLoader('wait', 'Ждём ответ клиента');
    slot(mode === 'with' ? 'wait' : 'closed');
    await sleep(640, g); b.classList.add('t2');
    await sleep(800, g); b.classList.add('read');
    stage = 'answered';
  }

  async function together(s, g, forced) {
    stage = 'suggest';
    openTray(s);
    const in_ = where(s.source.kind)[1];
    setLoader('scan', `Ищу в&nbsp;${esc(in_)}: <b>${esc(s.source.find)}</b>`);
    setSrc(s, 'scan');
    lit(srcEl, true);
    await sleep(1300 * K, g);
    setSrc(s, 'hit');
    setLoader(null);
    prog(0.55);
    revealCards(s);
    let i = forced;
    if (i == null) {
      const ch = waitChoice();
      if (!RM) ghostRun(s, g).catch(() => {});
      ({ i } = await ch);
    }
    if (g !== gen) throw CANCEL;
    hint.classList.remove('on');
    await send(s, i, g);
    prog(0.82);
  }

  async function autoReply(s, g) {
    stage = 'auto';
    slot('closed');
    clearCards();
    setSrc(s, 'scan');
    lit(srcEl, true);
    await sleep(450, g);
    const ty = push(typingNode('out'));
    await sleep(1350 * K, g);
    setSrc(s, 'hit');
    prog(0.55);
    const best = s.cards.find((c) => c.best) || s.cards[0];
    const b = swap(ty, bubble('out', best.text, s.times[1], 'ИИ-сотрудник', true));
    comp.classList.add('sent');
    b.classList.add('t1s');
    await sleep(650, g); b.classList.add('t2');
    await sleep(750, g); b.classList.add('read');
    prog(0.82);
    stage = 'answered';
  }

  // Switching customers reads like opening another chat: the old thread slides out to the left.
  async function switchChat(s, g) {
    if (list.children.length && !RM) {
      const o = { duration: 300, easing: 'cubic-bezier(.55,0,.75,.2)', fill: 'forwards' };
      const outs = [
        anim(thread, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateX(-44px)' }], o),
        anim(who, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateX(-12px)' }], o),
        anim(avaEl, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'scale(.6)' }], o)];
      try { await sleep(310, g); } finally {
        if (listAnim) { listAnim.cancel(); listAnim = null; }
        list.innerHTML = '';
        outs.forEach((a) => a && a.cancel());
      }
    } else {
      if (listAnim) { listAnim.cancel(); listAnim = null; }
      list.innerHTML = '';
    }
    setHeader(s);
    const o = { duration: 460, easing: 'cubic-bezier(.22,.9,.24,1)' };
    anim(thread, [{ opacity: 0, transform: 'translateX(44px)' }, { opacity: 1, transform: 'none' }], o);
    anim(who, [{ opacity: 0, transform: 'translateX(12px)' }, { opacity: 1, transform: 'none' }], o);
    anim(avaEl, [{ opacity: 0, transform: 'scale(.6)' }, { opacity: 1, transform: 'none' }], { duration: 520, easing: 'cubic-bezier(.2,.9,.3,1.3)' });
  }

  // ---------- the script ----------
  let mode = 'with';
  let cur = 0;
  let stage = 'idle';
  let runs = 0;
  let K = 1;

  async function scenario(i, g, keep, forced) {
    const s = SCENARIOS[i];
    cur = i;
    K = runs++ === 0 ? 0.72 : 1; // the first run — the one every visitor sees — reaches the choice sooner
    setTab(i);
    if (!keep) {
      stage = 'intro';
      clearCards();
      await switchChat(s, g);
      push(dayChip());
      if (mode === 'with') openTray(s, 'Ждём сообщение клиента'); else slot('closed');
      await sleep(420 * K, g);
      stage = 'typing';
      setTyping(true);
      if (mode === 'with') setLoader('wait', 'Клиент печатает…');
      const ty = push(typingNode('in'));
      await sleep(1250 * K, g);
      setTyping(false);
      swap(ty, bubble('in', s.question, s.times[0], s.name));
      prog(0.22);
      stage = 'asked';
      await sleep(480 * K, g);
    } else {
      clearCards();
      $$('.dm-typing', list).forEach((n) => mutate(() => n.remove()));
    }
    if (mode === 'with') await together(s, g, forced); else await autoReply(s, g);

    await sleep(900, g);
    stage = 'follow';
    setTyping(true);
    comp.classList.remove('sent');
    if (mode === 'with') setLoader('wait', 'Клиент печатает…');
    const ty2 = push(typingNode('in'));
    await sleep(1150, g);
    setTyping(false);
    swap(ty2, bubble('in', s.follow, s.times[2], s.name));
    if (mode === 'with') setLoader('wait', 'ИИ-сотрудник подводит итог диалога…');
    await sleep(750, g);
    const chip = el('p', 'dm-ochip', `<i aria-hidden="true"></i>Сводка: ${esc(s.outcome.label.toLowerCase())}`);
    chip.dataset.st = s.outcome.status;
    push(chip);
    svodka(s);
    setSrc(s, '');
    lit(srcEl, false);
    if (mode === 'with') setLoader('done', `Итог в&nbsp;«Сводке»: <b>${esc(s.outcome.label.toLowerCase())}</b>`);
    prog(1);
    stage = 'done';
    await sleep(2600, g);
  }

  // `how`: 'fresh' — replay the niche from the top; 'keep' — same chat, answer again (mode flip);
  // 'pick' — the visitor chose a card while the script was idle.
  async function run(i, how = 'fresh', forced) {
    const g = ++gen;
    dropChoice();
    idle = false;
    hideGhost();
    try {
      if (how === 'pick') {
        const s = SCENARIOS[i];
        cur = i; setTab(i);
        $$('.dm-typing', list).forEach((n) => mutate(() => n.remove()));
        await send(s, forced, g);
        await followUp(s, g);
      } else {
        let keep = how === 'keep';
        for (;;) {
          await scenario(i, g, keep, forced);
          keep = false; forced = undefined;
          if (RM || !autoplay) break;
          i = (i + 1) % SCENARIOS.length;
        }
      }
      while (!RM && autoplay) { i = (i + 1) % SCENARIOS.length; await scenario(i, g, false); }
      if (g === gen) idle = true;
    } catch (e) { if (e !== CANCEL) console.error('[landing] demo:', e); }
  }

  // the tail of a scenario after the answer is on screen (used by the 'pick' entry)
  async function followUp(s, g) {
    await sleep(700, g);
    stage = 'follow';
    comp.classList.remove('sent');
    setTyping(true);
    const ty = push(typingNode('in'));
    await sleep(1000, g);
    setTyping(false);
    swap(ty, bubble('in', s.follow, s.times[2], s.name));
    await sleep(650, g);
    const chip = el('p', 'dm-ochip', `<i aria-hidden="true"></i>Сводка: ${esc(s.outcome.label.toLowerCase())}`);
    chip.dataset.st = s.outcome.status;
    push(chip);
    svodka(s);
    setSrc(s, '');
    prog(1);
    stage = 'done';
    await sleep(2400, g);
  }

  // ---------- static board (low tier / reduced motion) ----------
  function renderStatic(i) {
    const s = SCENARIOS[i];
    cur = i;
    gen += 1;
    dropChoice();
    setTab(i);
    setHeader(s);
    list.innerHTML = '';
    clearCards();
    comp.classList.remove('sent');
    setTyping(false);
    list.appendChild(dayChip());
    list.appendChild(bubble('in', s.question, s.times[0], s.name));
    setSrc(s, 'hit');
    prog(0.4);
    if (mode === 'with') {
      buildCards(s);
      slot('cards');
      setLoader(null);
      hint.classList.add('on');
      pending = { res: ({ i: idx }) => staticAnswer(s, idx, false), rej: () => {} };
    } else {
      slot('closed');
      const best = s.cards.findIndex((c) => c.best);
      staticAnswer(s, best < 0 ? 0 : best, true);
    }
  }
  function staticAnswer(s, i, bot) {
    // clearCards() destroys the chosen (focused) button: a keyboard visitor goes to the thread,
    // where the answer lands, instead of falling to <body> — the same hand-off send() makes
    if (keyboardPick) { keyboardPick = false; thread.focus({ preventScroll: true }); }
    clearCards();
    slot('closed');
    const b = bubble('out', s.cards[i].text, s.times[1], bot ? 'ИИ-сотрудник' : 'Вы', bot);
    b.classList.add('t1s', 't2', 'read');
    list.appendChild(b);
    comp.classList.add('sent');
    list.appendChild(bubble('in', s.follow, s.times[2], s.name));
    const chip = el('p', 'dm-ochip', `<i aria-hidden="true"></i>Сводка: ${esc(s.outcome.label.toLowerCase())}`);
    chip.dataset.st = s.outcome.status;
    list.appendChild(chip);
    svodka(s);
    prog(1);
    stage = 'done';
  }

  // ---------- controls ----------
  function setMode(m) {
    if (m === mode) return;
    mode = m;
    win.dataset.mode = m;
    modeBtn.setAttribute('aria-checked', String(m === 'auto'));
    setCap();
    if (!RM) {
      flash.dataset.m = m;
      anim(flash, [{ opacity: 0 }, { opacity: 1, offset: 0.25 }, { opacity: 0 }], { duration: 950, easing: 'ease-out' });
    }
    if (RM) { renderStatic(cur); return; }
    if (stage === 'asked' || stage === 'suggest' || stage === 'auto') run(cur, 'keep');
    else if (stage === 'done' || stage === 'follow' || stage === 'answered') run((cur + 1) % SCENARIOS.length, 'fresh');
    else if (stage === 'intro' || stage === 'typing') {
      // the next beat already reads the new mode; only the slot tenant has to follow at once
      if (m === 'auto') { slot('closed'); setLoader(null); }
      else openTray(SCENARIOS[cur], stage === 'typing' ? 'Клиент печатает…' : 'Ждём сообщение клиента');
    }
    // 'sending': the tray folds after the landing anyway
  }

  // «Автопоказ» is a real pause button: off freezes the script where it stands (WCAG 2.2.2)
  // and stops the advance to the next niche. Any deliberate action — a card, a tab, the mode
  // switch — is a «play» request and lifts it again.
  let autoplay = !RM;
  let idle = false;
  function setAutoplay(on) {
    autoplay = on;
    userPaused = !on;
    if (playBtn) playBtn.setAttribute('aria-pressed', String(on));
    applyPause();
    if (on && idle && !RM) run((cur + 1) % SCENARIOS.length, 'fresh');
  }

  // The polite log would narrate the endless autoplay for as long as the section is on screen.
  // It stays muted until the visitor acts here (a card, a tab, the switch, «Автопоказ», focus);
  // from then on the conversation they are driving is announced. The static board only ever
  // changes on the visitor's own action, so it speaks from the start.
  let voiced = RM;
  function voice() { if (voiced) return; voiced = true; list.setAttribute('aria-live', 'polite'); }

  const onTab = (i) => {
    voice();
    engage(1200);
    if (userPaused) setAutoplay(true);
    if (RM) renderStatic(i); else run(i, 'fresh');
  };
  tabs.forEach((t, i) => {
    t.addEventListener('click', () => onTab(i));
    t.addEventListener('keydown', (e) => {
      const k = e.key;
      let j = -1;
      if (k === 'ArrowRight' || k === 'ArrowDown') j = (i + 1) % tabs.length;
      else if (k === 'ArrowLeft' || k === 'ArrowUp') j = (i - 1 + tabs.length) % tabs.length;
      else if (k === 'Home') j = 0;
      else if (k === 'End') j = tabs.length - 1;
      if (j < 0) return;
      e.preventDefault();
      tabs[j].focus();
      onTab(j);
    });
  });
  modeBtn.addEventListener('click', () => {
    voice();
    engage(2500);
    if (userPaused) setAutoplay(true);
    setMode(mode === 'with' ? 'auto' : 'with');
  });
  if (playBtn) {
    playBtn.hidden = RM;
    playBtn.addEventListener('click', () => { voice(); setAutoplay(playBtn.getAttribute('aria-pressed') !== 'true'); });
  }

  const onEnter = (e) => { if (e.pointerType === 'mouse') hovering = true; };
  const onLeave = () => { hovering = false; };
  const onTouch = () => engage(3500);
  const onFocusIn = () => { voice(); engage(5000); };
  const onDown = () => { lastPointer = performance.now(); };
  root.addEventListener('pointerdown', onDown, { passive: true });
  tray.addEventListener('pointerenter', onEnter);
  tray.addEventListener('pointerleave', onLeave);
  root.addEventListener('touchstart', onTouch, { passive: true });
  root.addEventListener('focusin', onFocusIn);

  // desktop, high tier: the window's rim lights under the pointer (two custom properties,
  // written at most once per frame — no loop, nothing runs when the pointer is still)
  let mvQueued = false;
  let mvX = 0;
  let mvY = 0;
  // the window's screen rect only changes on scroll/resize — reading it per pointermove is a
  // style/layout flush against the custom properties the coalesced frame writes on the same node
  let winRect = null;
  const dropRect = () => { winRect = null; };
  const onMove = (e) => {
    const r = winRect || (winRect = win.getBoundingClientRect());
    mvX = ((e.clientX - r.left) / r.width) * 100;
    mvY = ((e.clientY - r.top) / r.height) * 100;
    if (mvQueued) return;
    mvQueued = true;
    requestAnimationFrame(() => {
      mvQueued = false;
      win.style.setProperty('--mx', `${mvX.toFixed(1)}%`);
      win.style.setProperty('--my', `${mvY.toFixed(1)}%`);
      win.style.setProperty('--rim', '1');
    });
  };
  const onWinLeave = () => { winRect = null; win.style.setProperty('--rim', '0'); };
  const rimOn = ctx.tier === 'high' && !TOUCH && !RM;
  if (rimOn) {
    win.addEventListener('pointermove', onMove);
    win.addEventListener('pointerenter', dropRect);
    win.addEventListener('pointerleave', onWinLeave);
    addEventListener('scroll', dropRect, { passive: true });
  }

  // The window, the thread and the panel clip with `overflow:clip` (40-demo.css), which is never
  // a scroll container. Where `clip` is unsupported they fall back to `hidden` — still scrollable
  // by find-in-page or a screen reader's cursor, and the folded tray below the window edge is
  // exactly what those scroll to: the header and the mode switch would stay cut off for the
  // rest of the visit. Undo any such scroll the moment it happens (scroll does not bubble).
  const clipBoxes = [win, thread, $('.dm-sug'), $('.dm-c-field')].filter(Boolean);
  const unscroll = (e) => { const n = e.currentTarget; if (n.scrollTop || n.scrollLeft) { n.scrollTop = 0; n.scrollLeft = 0; } };
  clipBoxes.forEach((n) => n.addEventListener('scroll', unscroll, { passive: true }));

  // ---------- lifecycle ----------
  measureTray();
  slot('closed');
  // park the ghost inside the window: it is clipped anyway, but a rect hanging off the left
  // edge of the page is exactly what an overflow audit trips on
  ghost.style.transform = 'translate(40px,40px)';
  const ro = new ResizeObserver(() => {
    measureTray();
    dropRect();
    if (cardsEl.children.length) {
      // not shown yet → throw them away and let the next beat rebuild at the new size
      if (detent !== 'cards') { cardsEl.innerHTML = ''; cardsNeed = 0; }
      // ...and never re-fit while a card is in flight: `send()` holds that node
      else if (stage !== 'sending') fitCards();
    }
    slot(detent);
  });
  ro.observe(win);

  if (svList) svList.innerHTML = '<li class="dm-sv-empty">Итог каждого диалога — в&nbsp;«Сводке». Пока пусто.</li>';
  // The source line changes between one- and two-line strings every scenario. Every string it
  // can ever show is stacked, invisible, in the same grid cell, so its box is as tall as the
  // longest one at the current width — nothing below the demo moves while it plays (CLS), and
  // the centred desktop column does not re-centre. Pure layout: no measuring, no resize code.
  if (srcLines) {
    const all = new Set();
    for (const sc of SCENARIOS) for (const st of ['', 'scan', 'hit']) all.add(srcText(sc, st));
    for (const t of all) { const z = el('span', 'dm-src-sz'); z.textContent = t; z.setAttribute('aria-hidden', 'true'); srcLines.appendChild(z); }
  }
  const capWith = capSpans.find((n) => n.dataset.m === 'with');
  if (capWith && !TOUCH) capWith.innerHTML = capWith.innerHTML.replace('одним касанием', 'одним нажатием');
  if (!voiced) list.setAttribute('aria-live', 'off');
  setCap();
  lit(capNote, false);

  const io = new IntersectionObserver(([e]) => { offscreen = !e.isIntersecting; applyPause(); }, { threshold: 0.08 });
  io.observe(root);
  const onVis = () => applyPause();
  document.addEventListener('visibilitychange', onVis);

  if (RM) { renderStatic(0); paused = false; } else { applyPause(); run(0, 'fresh'); }

  return {
    destroy() {
      dead = true;
      gen += 1;
      dropChoice();
      paused = false;
      waiters.splice(0).forEach((r) => r());
      timers.forEach((t) => clearTimeout(t));
      timers.clear();
      anims.forEach((a) => { try { a.cancel(); } catch { /* done */ } });
      anims.clear();
      held.clear();
      io.disconnect();
      ro.disconnect();
      document.removeEventListener('visibilitychange', onVis);
      tray.removeEventListener('pointerenter', onEnter);
      tray.removeEventListener('pointerleave', onLeave);
      root.removeEventListener('touchstart', onTouch);
      root.removeEventListener('focusin', onFocusIn);
      root.removeEventListener('pointerdown', onDown);
      clipBoxes.forEach((n) => n.removeEventListener('scroll', unscroll));
      if (rimOn) {
        win.removeEventListener('pointermove', onMove);
        win.removeEventListener('pointerenter', dropRect);
        win.removeEventListener('pointerleave', onWinLeave);
        removeEventListener('scroll', dropRect);
      }
    },
  };
}
