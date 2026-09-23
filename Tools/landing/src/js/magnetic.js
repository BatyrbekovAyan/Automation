// magnetic.js — buttons with [data-magnetic] lean toward a fine pointer. No-op on touch.
export function initMagnetic(doc) {
  if (!matchMedia('(hover: hover) and (pointer: fine)').matches) return;
  for (const el of doc.querySelectorAll('[data-magnetic]')) {
    let raf = 0;
    el.addEventListener('pointermove', (e) => {
      const r = el.getBoundingClientRect();
      const dx = (e.clientX - (r.left + r.width / 2)) * 0.22;
      const dy = (e.clientY - (r.top + r.height / 2)) * 0.32;
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => { el.style.transform = `translate(${dx}px, ${dy}px)`; });
    });
    el.addEventListener('pointerleave', () => {
      cancelAnimationFrame(raf);
      el.style.transition = 'transform .6s cubic-bezier(.16,1,.3,1)';
      el.style.transform = '';
      setTimeout(() => { el.style.transition = ''; }, 600);
    });
  }
}
