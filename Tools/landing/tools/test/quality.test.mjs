import { test } from 'node:test';
import assert from 'node:assert/strict';
import { decideTier, FrameGovernor } from '../../src/js/quality.js';

const base = { reducedMotion: false, saveData: false, webgl: true, cores: 8, memory: 8, coarse: false, width: 1440, ios: false, override: null };

test('мощный десктоп → high', () => assert.equal(decideTier(base), 'high'));
test('телефон с 8 ядрами → medium', () => assert.equal(decideTier({ ...base, coarse: true, width: 390 }), 'medium'));
test('reduced motion → low', () => assert.equal(decideTier({ ...base, reducedMotion: true }), 'low'));
test('saveData → low', () => assert.equal(decideTier({ ...base, saveData: true }), 'low'));
test('без WebGL → low', () => assert.equal(decideTier({ ...base, webgl: false }), 'low'));
test('Android 4 ядра → low', () => assert.equal(decideTier({ ...base, coarse: true, width: 390, cores: 4 }), 'low'));
test('iPhone с 4 ядрами → medium (Safari занижает cores)', () => assert.equal(decideTier({ ...base, coarse: true, width: 390, cores: 4, ios: true }), 'medium'));
test('4-ядерный ноутбук/десктоп (мышь, широкий экран) → не low: medium', () => assert.equal(decideTier({ ...base, cores: 4 }), 'medium'));
test('4-ядерный ноутбук с узким окном → medium', () => assert.equal(decideTier({ ...base, cores: 4, width: 900 }), 'medium'));
test('4-ядерный планшет-Android (тач) → low', () => assert.equal(decideTier({ ...base, coarse: true, width: 820, cores: 4 }), 'low'));
test('iPad с 4 ядрами → medium', () => assert.equal(decideTier({ ...base, coarse: true, width: 820, cores: 4, ios: true }), 'medium'));
test('2 ГБ памяти → low', () => assert.equal(decideTier({ ...base, memory: 2 }), 'low'));
test('узкий десктоп-экран → medium', () => assert.equal(decideTier({ ...base, width: 900 }), 'medium'));
test('?tier= перекрывает всё', () => assert.equal(decideTier({ ...base, reducedMotion: true, override: 'high' }), 'high'));
test('мусорный override игнорируется', () => assert.equal(decideTier({ ...base, override: 'ultra' }), 'high'));

test('FrameGovernor: ровные 16 мс → none', () => {
  const g = new FrameGovernor();
  for (let i = 0; i < 300; i++) assert.equal(g.sample(16.7), 'none');
});
test('FrameGovernor: ~1 с по 40 мс → degrade, затем ещё столько же → disable', () => {
  const g = new FrameGovernor();
  const seen = [];
  for (let i = 0; i < 200; i++) { const a = g.sample(40); if (a !== 'none') seen.push(a); }
  assert.deepEqual(seen.slice(0, 2), ['degrade', 'disable']);
});
test('FrameGovernor: одиночные провалы не деградируют', () => {
  const g = new FrameGovernor();
  for (let i = 0; i < 300; i++) assert.equal(g.sample(i % 30 === 0 ? 120 : 16), 'none');
});
test('FrameGovernor: паузы вкладки (>250 мс) игнорируются', () => {
  const g = new FrameGovernor();
  for (let i = 0; i < 50; i++) assert.equal(g.sample(1000), 'none');
});

// Task 12: the governor holds by TIME, not by frame count. With a 45-frame hold
// a device at 10 fps took 5+ s to shed load (and a loaded machine never did inside a scroll pass),
// while a mildly slow one at 30 ms tripped in ~2.8 s. Now:
// a per-frame average (α = 1/30) held above the limit for 1.25 s of wall time.
function wallTimeTo(verdict, dt, g = new FrameGovernor()) {
  let t = 0;
  for (let i = 0; i < 2000; i++) { t += dt; if (g.sample(dt) === verdict) return t; }
  return Infinity;
}
test('FrameGovernor: 10 fps (100 мс) → degrade за ≤ 1,6 с реального времени', () => {
  const t = wallTimeTo('degrade', 100);
  assert.ok(t <= 1600, `degrade через ${t} мс`);
});
test('FrameGovernor: умеренно медленно (30 мс) → не раньше 1,5 с и не позже 3 с', () => {
  const t = wallTimeTo('degrade', 30);
  assert.ok(t >= 1500 && t <= 3000, `degrade через ${t} мс`);
});
test('FrameGovernor: через кадр 33/16,7 мс (поле рисует через кадр) — это ещё не «медленно» надолго', () => {
  const g = new FrameGovernor(); let seen = 'none';
  for (let i = 0; i < 60; i++) { const a = g.sample(i % 2 ? 16.7 : 33.3); if (a !== 'none') seen = a; }
  assert.equal(seen, 'none', 'одна секция прокрутки (~1 с) не должна гасить поле');
});
test('FrameGovernor: затяжные кадры по 300 мс (3 fps) — не «пауза вкладки», поле уступает', () => {
  const t = wallTimeTo('degrade', 300);
  assert.ok(t <= 2000, `degrade через ${t} мс (кадры > 250 мс раньше игнорировались целиком)`);
});
test('FrameGovernor: одиночный GC-провал 400 мс среди ровных кадров — ничего', () => {
  const g = new FrameGovernor();
  for (let i = 0; i < 400; i++) assert.equal(g.sample(i === 100 ? 400 : 16.7), 'none');
});

// ---- Lens fixes (2026-09-22): the WebGL probe is asked last and released; vendor only by tier ----
import { readEnv, probeWebgl, bootHead, loadVendor } from '../../src/js/quality.js';

function fakeWindow({ reduced = false, saveData = false, coarse = false, fine = true, cores = 8, memory = 8, width = 1440, webgl = true, search = '' } = {}) {
  const log = { contexts: 0, lost: 0, scripts: [], classes: [] };
  const w = {
    navigator: { hardwareConcurrency: cores, deviceMemory: memory, userAgent: 'x', platform: 'x', maxTouchPoints: 0, connection: { saveData } },
    innerWidth: width,
    location: { search },
    matchMedia: (q) => ({ matches: q.includes('reduce') ? reduced : q.includes('coarse') ? coarse : q.includes('fine') ? fine : false }),
    document: {
      documentElement: { classList: { add: (c) => log.classes.push(c) } },
      head: { appendChild: (s) => log.scripts.push(s) },
      createElement: (tag) => (tag === 'canvas'
        ? { getContext: () => { if (!webgl) return null; log.contexts++; return { getExtension: (n) => (n === 'WEBGL_lose_context' ? { loseContext: () => { log.lost++; } } : null) }; } }
        : {}),
    },
  };
  return { w, log };
}

test('readEnv: WebGL не пробуется, пока decideTier не дошёл до него (reduced / Save-Data / слабый тач)', () => {
  for (const opt of [{ reduced: true }, { saveData: true }, { memory: 2 }, { coarse: true, fine: false, cores: 4, width: 390 }]) {
    const { w, log } = fakeWindow(opt);
    assert.equal(decideTier(readEnv(w)), 'low');
    assert.equal(log.contexts, 0, `проба WebGL не нужна: ${JSON.stringify(opt)}`);
  }
  const { w, log } = fakeWindow();
  const env = readEnv(w);
  assert.equal(decideTier(env), 'high');
  assert.equal(env.webgl, true);
  assert.equal(log.contexts, 1, 'проба — один раз');
  assert.equal(log.lost, 1, 'и контекст пробы сразу отпущен');
  assert.equal(probeWebgl(fakeWindow({ webgl: false }).w), false);
});

test('bootHead: vendor качают только уровни, которые его используют; Lenis — только под мышь', () => {
  const low = fakeWindow({ reduced: true });
  const b0 = bootHead(low.w, '/v/');
  assert.equal(b0.tier, 'low');
  assert.deepEqual(low.log.scripts, [], 'low не качает ни GSAP, ни ScrollTrigger, ни Lenis');
  assert.deepEqual(low.log.classes, ['q-low']);
  const phone = fakeWindow({ coarse: true, fine: false, cores: 8, width: 390 });
  assert.equal(bootHead(phone.w, '/v/').tier, 'medium');
  assert.deepEqual(phone.log.scripts.map((l) => [l.rel, l.as, l.href]), [['preload', 'script', '/v/gsap.min.js'], ['preload', 'script', '/v/ScrollTrigger.min.js']],
    'в <head> — только загрузка (preload), исполнение — после первой отрисовки; телефон — без Lenis');
  const desk = fakeWindow();
  const b2 = bootHead(desk.w, '/v/');
  assert.deepEqual(b2.files, ['gsap.min.js', 'ScrollTrigger.min.js', 'lenis.min.js']);
  assert.equal(desk.w.__crBoot, b2);
});

test('loadVendor: скрипты по порядку (async=false), разрешается после всех; ошибка загрузки не вешает страницу', async () => {
  const desk = fakeWindow();
  const boot = bootHead(desk.w, '/v/');
  desk.log.scripts.length = 0;
  let done = false;
  const p = loadVendor(desk.w, boot).then(() => { done = true; });
  assert.deepEqual(desk.log.scripts.map((s) => [s.src, s.async]), [['/v/gsap.min.js', false], ['/v/ScrollTrigger.min.js', false], ['/v/lenis.min.js', false]]);
  desk.log.scripts[0].onload(); desk.log.scripts[1].onerror(); 
  await Promise.resolve(); assert.equal(done, false, 'ждёт все три');
  desk.log.scripts[2].onload();
  await p; assert.equal(done, true);
  assert.deepEqual(await loadVendor(desk.w, { files: [], base: '/v/' }), [], 'low: сразу');
});
