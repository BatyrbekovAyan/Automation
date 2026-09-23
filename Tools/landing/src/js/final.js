// Section 90 — «Выбери … ответ.» + the lead form.
// The form is wired on EVERY tier (it is the conversion point). The kinetic line is
// decoration: its stylesheet already shows the finished composition (no-JS, low tier,
// reduced motion and a missing GSAP all land there), and on medium/high this module
// rewinds it once and plays it back the first time the line enters the viewport.
// Transform/opacity only; the one layout read happens when the line comes into view.
import { initForm } from './form.js';

export function init(root, ctx) {
  initForm(root.querySelector('#leadForm'), { onSuccess: () => root.classList.add('sent') });

  const kin = root.querySelector('.kin');
  const gsap = ctx && ctx.gsap;
  if (!kin || !gsap || ctx.tier === 'low' || ctx.reduced) return;
  // Arrived with the line already on screen (deep link /#lead, reload at the bottom):
  // keep the finished composition rather than blanking it and replaying.
  const r = kin.getBoundingClientRect();
  if (r.bottom > 0 && r.top < window.innerHeight) return;

  const el = collect(kin, gsap);
  if (!el) return;
  rewind(el, gsap);

  let tl = null;
  const io = new IntersectionObserver((entries) => {
    if (!entries.some((e) => e.isIntersecting)) return;
    io.disconnect();
    tl = play(el, gsap);
  }, { threshold: 0.3 });
  io.observe(kin);

  return {
    destroy() {
      io.disconnect();
      if (tl) tl.kill();
    },
  };
}

// Splits «Выбери» into letters (each rises from under the mask on its own) and gathers
// the pieces of the composition. Returns null if the markup is not what we expect.
function collect(kin, gsap) {
  const w1 = kin.querySelector('.kin-w1 .kin-i');
  const w2 = kin.querySelector('.kin-w2 em');
  const stack = kin.querySelector('.kin-stack');
  const cards = [...kin.querySelectorAll('.kin-card')];
  if (!w1 || !w2 || !stack || cards.length !== 3) return null;
  const text = w1.textContent;
  w1.textContent = '';
  const chars = [...text].map((ch) => {
    const s = document.createElement('span');
    s.className = 'kin-c';
    s.textContent = ch;
    w1.appendChild(s);
    return s;
  });
  const sel = cards[1];
  const q = (s, from = kin) => from.querySelector(s);
  return {
    kin, stack, cards, chars, w2, sel,
    rings: cards.map((c) => q('.kin-ring', c)),
    fill: q('.kin-fill', sel), lbl: q('.kin-lbl', sel), txt: q('.kin-t', sel),
    ok: q('.kin-ok', sel), sheen: q('.kin-sheen i', sel),
    touch: q('.kin-touch'), ripple: q('.kin-ripple'), ul: q('.kin-ul'), glow: q('.kin-glow'),
    sides: [cards[0], cards[2]],
    gsap,
  };
}

// The «before» frame: letters and «ответ.» below their masks, the three options hidden,
// nothing chosen yet. Set while the line is still off screen, so nobody sees the swap.
function rewind(el, gsap) {
  gsap.set(el.chars, { yPercent: 118 });
  gsap.set(el.w2, { yPercent: 118, rotate: 5, transformOrigin: '0% 100%' });
  gsap.set(el.cards, { opacity: 0 });
  gsap.set(el.fill, { opacity: 0 });
  gsap.set(el.lbl, { color: 'rgba(143,174,239,1)' });
  gsap.set(el.txt, { color: 'rgba(236,240,246,1)' });
  gsap.set(el.ok, { scale: 0 });
  gsap.set(el.ul, { scaleX: 0 });
  if (el.glow) gsap.set(el.glow, { opacity: 0.45, scale: 0.92, transformOrigin: '50% 50%' });
}

function play(el, gsap) {
  const { cards, rings, touch, ripple } = el;
  // Card centres inside the stack — read once, now that fonts and layout have settled.
  const mid = cards.map((c) => c.offsetTop + c.offsetHeight / 2);
  gsap.set([touch, ripple], { y: mid[0] });

  const tl = gsap.timeline({ defaults: { ease: 'expo.out' } });
  tl.to(el.chars, { yPercent: 0, duration: 1.15, stagger: 0.05 }, 0)
    // the options deal out of the middle one — the mark unfolding into three answers
    .fromTo(el.sel, { opacity: 0, scale: 0.9 }, { opacity: 1, scale: 1, duration: 0.9 }, 0.12)
    .fromTo(el.sides, { opacity: 0, scale: 0.94, y: (i) => mid[1] - mid[i === 0 ? 0 : 2] },
      { opacity: 1, scale: 1, y: 0, duration: 1.1, stagger: 0.06 }, 0.24)
    .to(el.w2, { yPercent: 0, rotate: 0, duration: 1.25 }, 0.36)
    // a finger considers the first option, glances at the last, settles on the middle
    .fromTo(touch, { opacity: 0, scale: 1.4 }, { opacity: 1, scale: 1, duration: 0.3, ease: 'power2.out' }, 1.3)
    .to(rings[0], { opacity: 1, duration: 0.25, ease: 'power1.out' }, 1.34)
    .to(touch, { y: mid[2], duration: 0.5, ease: 'power3.inOut' }, 1.72)
    .to(rings[0], { opacity: 0, duration: 0.3, ease: 'power1.inOut' }, 1.78)
    .to(rings[2], { opacity: 1, duration: 0.25, ease: 'power1.out' }, 2.02)
    .to(touch, { y: mid[1], duration: 0.46, ease: 'power3.inOut' }, 2.3)
    .to(rings[2], { opacity: 0, duration: 0.3, ease: 'power1.inOut' }, 2.34)
    .to(rings[1], { opacity: 1, duration: 0.22, ease: 'power1.out' }, 2.56)
    .set(ripple, { y: mid[1] }, 2.8)
    // the tap
    .to(el.sel, { scale: 0.975, duration: 0.12, ease: 'power2.out' }, 2.8)
    .to(el.sel, { scale: 1, duration: 0.55, ease: 'back.out(3)' }, 2.92)
    .fromTo(ripple, { opacity: 0.9, scale: 0.5 }, { opacity: 0, scale: 4.2, duration: 0.9, ease: 'power2.out' }, 2.82)
    .to(touch, { opacity: 0, scale: 0.8, duration: 0.35, ease: 'power1.in' }, 2.95)
    .to(el.fill, { opacity: 1, duration: 0.5, ease: 'power2.out' }, 2.84)
    .to(rings[1], { opacity: 0, duration: 0.4, ease: 'power1.out' }, 2.9)
    .to(el.lbl, { color: 'rgba(255,255,255,.85)', duration: 0.4, ease: 'power1.out' }, 2.86)
    .to(el.txt, { color: 'rgba(255,255,255,1)', duration: 0.4, ease: 'power1.out' }, 2.86)
    .to(el.ok, { scale: 1, duration: 0.6, ease: 'back.out(2.4)' }, 2.9)
    // the light blooms on the choice, then settles back to its resting level
    .to(el.glow, { opacity: 1, scale: 1.06, duration: 0.45, ease: 'power2.out' }, 2.82)
    .to(el.glow, { scale: 1, duration: 1.6, ease: 'power2.out' }, 3.27)
    .to(el.sides, { opacity: 0.5, duration: 0.8, ease: 'power2.out' }, 2.95)
    .fromTo(el.sheen, { xPercent: -120 }, { xPercent: 260, duration: 1.1, ease: 'power2.inOut' }, 3.0)
    .to(el.ul, { scaleX: 1, duration: 1.1 }, 3.05)
    // hand the finished frame back to the stylesheet (same values), freeing the layers
    .add(() => gsap.set([...el.chars, el.w2, ...el.cards, el.fill, el.lbl, el.txt, el.ok, el.ul, el.glow], { clearProps: 'all' }));
  return tl;
}
