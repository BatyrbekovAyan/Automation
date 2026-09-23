// Task 12: word-split headings keep hard spaces. splitWords needs a DOM, so the guard is on the
// source: the split must not use \s (which matches U+00A0) — «в&nbsp;тенге» ended a desktop line on «в».
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const src = readFileSync(new URL('../../src/js/reveal.js', import.meta.url), 'utf8');
test('splitWords режет только по разрывным пробелам, NBSP держит слова вместе', () => {
  const fn = src.slice(src.indexOf('export function splitWords'), src.indexOf('export function initReveals'));
  assert.ok(!/split\(\/\(\\s\+\)\/\)/.test(fn), 'split(/(\\s+)/) ловит и NBSP');
  assert.match(fn, /split\(\/\(\[ \\t\\n\\r\\f\]\+\)\/\)/);
  const re = /([ \t\n\r\f]+)/;
  assert.deepEqual('Честные цены в тенге.'.split(re).filter((p) => p && !re.test(p)), ['Честные', 'цены', 'в тенге.']);
});

// Повторное ревью, N-4: блок раскрывается, когда его ВЕРХ вошёл в зону чтения (окно минус нижние 12 %),
// а не когда виден заданный процент самого блока — высокий блок (карточка заявки) иначе стоял на экране
// невидимым. Геометрия замера: шапка «Ранний доступ» на 390×844 кладёт верх .lead-card на 658 px.
test('reveal: порог — край блока в зоне чтения, не доля высокого блока', async () => {
  const { REVEAL_IO } = await import('../../src/js/reveal.js');
  assert.equal(REVEAL_IO.threshold, 0, 'доля блока не требуется');
  const m = REVEAL_IO.rootMargin.match(/^0px 0px -(\d+(?:\.\d+)?)% 0px$/);
  assert.ok(m, 'поле снизу — в процентах окна: ' + REVEAL_IO.rootMargin);
  const cut = Number(m[1]) / 100;
  assert.ok(cut >= 0.08 && cut <= 0.15, 'линия раскрытия — там же, где у абзаца раньше (≈ 88 % окна)');
  const vh = 844, cardTop = 658;
  assert.ok(cardTop < vh * (1 - cut), 'карточка заявки после перехода из шапки уже в зоне чтения');
  const fn = src.slice(src.indexOf('export function initReveals'));
  assert.match(fn, /new IntersectionObserver\([\s\S]*\{ \.\.\.REVEAL_IO \}\)/, 'наблюдатель берёт ровно эти параметры');
  assert.ok(!/threshold: 0\.\d/.test(fn), 'и не держит свою долю');
});
