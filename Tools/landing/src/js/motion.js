// GSAP + ScrollTrigger + Lenis, wired together once. The vendor scripts are classic
// deferred scripts that run before this module, so they are globals by now. Any of them
// may be missing (blocked/failed download) — callers must treat null as «no motion lib».
export function createMotion(tier, w) {
  const gsap = w.gsap || null;
  const ScrollTrigger = w.ScrollTrigger || null;
  if (gsap && ScrollTrigger) gsap.registerPlugin(ScrollTrigger);
  let lenis = null;
  const smooth = tier !== 'low' && w.Lenis && w.matchMedia('(hover: hover) and (pointer: fine)').matches;
  if (smooth) {
    lenis = new w.Lenis({ duration: 1.15, easing: (t) => 1 - Math.pow(1 - t, 4), smoothWheel: true, syncTouch: false });
    if (gsap && ScrollTrigger) {
      lenis.on('scroll', ScrollTrigger.update);
      gsap.ticker.add((time) => lenis.raf(time * 1000));
      gsap.ticker.lagSmoothing(0);
    } else {
      const loop = (t) => { lenis.raf(t); requestAnimationFrame(loop); };
      requestAnimationFrame(loop);
    }
  }
  return { gsap, ScrollTrigger, lenis };
}
