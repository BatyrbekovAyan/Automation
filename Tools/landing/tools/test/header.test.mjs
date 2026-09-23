// Task 12 (ревью T6 r4, Important 2): in-page anchors land where they point, even when the pinned
// story re-measures in the middle of the flight.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { landingError } from '../../src/js/header.js';

const header = readFileSync(new URL('../../src/js/header.js', import.meta.url), 'utf8');
const main = readFileSync(new URL('../../src/js/main.js', import.meta.url), 'utf8');

test('landingError: рядом с местом — 0, иначе знаковое расстояние', () => {
  assert.equal(landingError(80, 80), 0);
  assert.equal(landingError(83, 80), 0, 'в пределах допуска');
  assert.equal(landingError(1282, 80), 1202, 'не долетели — цель ниже своего места');
  assert.equal(landingError(-300, 72), -372, 'перелетели');
});
test('прыжок по якорю досаживается в обоих путях (Lenis и нативный)', () => {
  assert.match(header, /lenis\.scrollTo\([^;]*onComplete: \(\) => \{[^}]*settle\(target\)/, 'Lenis: settle по onComplete');
  assert.match(header, /scrollIntoView\(\{ behavior: ctx\.reduced \? 'auto' : 'smooth', block: 'start' \}\);\s*if \(target !== document\.body\) settle\(target\);/, 'нативный: settle после scrollIntoView');
  assert.match(header, /'wheel', 'touchstart', 'keydown', 'pointerdown'/, 'прокрутка читателя отменяет досадку');
  assert.match(header, /a\.classList\.contains\('skip'\)\) return;/, 'ссылку «К содержанию» не перехватываем');
});
test('история загружается сразу, а не на подлёте (иначе пин растёт посреди прыжка)', () => {
  assert.match(main, /const EAGER = new Set\(\['stage', 'story'\]\)/);
  assert.match(main, /if \(EAGER\.has\(root\.dataset\.module\)\) eager\.push\(boot\(root\)\)/);
});
