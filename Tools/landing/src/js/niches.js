// niches.js — module for section 50 (ниши): the marquee ribbon runs only while it is
// in view and the tab is visible; niche cards get a pointer-tracked spotlight border
// on fine-pointer/hover devices. No persistent rAF loop: the tilt is one requestAnimationFrame
// scheduled per pointermove event, never a running ticker.
export function init(root, ctx) {
  const marquee = root.querySelector('.marquee');
  const grid = root.querySelector('.nch-grid');
  const teardown = [];

  if (marquee) {
    let inView = false;
    const apply = () => marquee.classList.toggle('running', inView && !document.hidden);
    const io = new IntersectionObserver(([entry]) => { inView = entry.isIntersecting; apply(); }, { threshold: 0.01 });
    io.observe(marquee);
    const onVis = () => apply();
    document.addEventListener('visibilitychange', onVis);
    teardown.push(() => { io.disconnect(); document.removeEventListener('visibilitychange', onVis); });
  }

  const cards = grid ? [...grid.querySelectorAll('.nch-card')] : [];
  cards.forEach((c, i) => { c.style.transitionDelay = `${(i % 3) * 60}ms`; });

  const canTilt = grid && !ctx.reduced && ctx.tier !== 'low' && matchMedia('(pointer: fine)').matches;
  if (canTilt) {
    let raf = 0, activeCard = null, lastEvent = null;
    const onMove = (e) => {
      const card = e.target.closest('.nch-card');
      if (!card || !grid.contains(card)) return;
      activeCard = card; lastEvent = e;
      if (raf) return;
      raf = requestAnimationFrame(() => {
        raf = 0;
        if (!activeCard || !lastEvent) return;
        const r = activeCard.getBoundingClientRect();
        activeCard.style.setProperty('--mx', `${lastEvent.clientX - r.left}px`);
        activeCard.style.setProperty('--my', `${lastEvent.clientY - r.top}px`);
      });
    };
    grid.addEventListener('pointermove', onMove);
    teardown.push(() => { grid.removeEventListener('pointermove', onMove); if (raf) cancelAnimationFrame(raf); });
  }

  return { destroy() { teardown.forEach((fn) => fn()); } };
}
