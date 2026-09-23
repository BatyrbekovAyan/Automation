// Generic scroll reveals. [data-reveal] fades/rises in once; [data-split] headings rise
// word by word. The HTML stays readable without JS (the hiding CSS is scoped to .js),
// and on the low tier / reduced motion everything is shown immediately.
export function splitWords(el) {
  if (el.dataset.splitDone) return;
  el.dataset.splitDone = '1';
  const walk = (node) => {
    for (const child of [...node.childNodes]) {
      if (child.nodeType === 3) {
        // split on BREAKABLE whitespace only: JS \s also matches NBSP, and a hard space authored
        // to keep «в тенге» or «на которые» together became a break point between two word boxes
        // (the heading ended a line on a lone preposition). Words joined by NBSP share one box.
        const parts = child.textContent.split(/([ \t\n\r\f]+)/);
        const frag = document.createDocumentFragment();
        for (const p of parts) {
          if (!p) continue;
          if (/^[ \t\n\r\f]+$/.test(p)) { frag.appendChild(document.createTextNode(p)); continue; }
          const w = document.createElement('span'); w.className = 'w';
          const i = document.createElement('span'); i.textContent = p;
          w.appendChild(i); frag.appendChild(w);
        }
        child.replaceWith(frag);
      } else if (child.nodeType === 1 && child.tagName !== 'BR') walk(child);
    }
  };
  walk(el);
  [...el.querySelectorAll('.w>span')].forEach((s, i) => { s.style.transitionDelay = `${Math.min(i * 45, 600)}ms`; });
}

// A block is revealed as soon as its top edge enters the reading area — the viewport minus its bottom
// 12 %. Not by a share of the BLOCK (it was 0.18 of it past the bottom 8 %): a tall block could then sit
// on screen and stay invisible — the header «Ранний доступ» lands the lead card with its top at 658 of
// 844 px, 186 px in view and 15.8 % of it, so the form waited for more scrolling (повторное ревью, N-4).
// For a paragraph the trigger line is where it was (≈ 88 % of the viewport either way).
export const REVEAL_IO = Object.freeze({ threshold: 0, rootMargin: '0px 0px -12% 0px' });

export function initReveals(doc, ctx) {
  const els = [...doc.querySelectorAll('[data-reveal],[data-split]')];
  if (ctx.tier === 'low' || ctx.reduced) { els.forEach((e) => e.classList.add('in')); return; }
  doc.querySelectorAll('[data-split]').forEach(splitWords);
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
  }, { ...REVEAL_IO });
  els.forEach((e) => io.observe(e));
}
