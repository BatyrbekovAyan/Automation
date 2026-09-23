import { test } from 'node:test';
import assert from 'node:assert/strict';
import { SCENARIOS } from '../../src/js/demo/scenarios.js';

const FORBIDDEN = [/отзыв/i, /\d+\s*\+?\s*(компан|клиент|бизнес)/i, /за\s+\d+\s*сек/i, /рейтинг/i, /1С|CRM/];

test('четыре ниши в фиксированном порядке', () => {
  assert.deepEqual(SCENARIOS.map((s) => s.id), ['parts', 'flowers', 'kaspi', 'repair']);
});
for (const s of SCENARIOS) {
  test(`${s.id}: 3–4 карточки, ровно одна лучшая, непустые тексты`, () => {
    assert.ok(s.cards.length >= 3 && s.cards.length <= 4);
    assert.equal(s.cards.filter((c) => c.best).length, 1);
    for (const c of s.cards) { assert.ok(c.title.length > 0 && c.title.length <= 24); assert.ok(c.text.length > 10); }
    assert.ok(['wa', 'tg'].includes(s.channel));
    assert.ok(['order', 'owner', 'dialog', 'silent', 'closed'].includes(s.outcome.status));
  });
  test(`${s.id}: суммы в ₸ с неразрывными пробелами, без запрещённых обещаний`, () => {
    const all = [s.question, s.follow, ...s.cards.map((c) => c.text)].join(' ');
    for (const m of all.matchAll(/(\d[\d\s ]*)\s?₸/g)) {
      assert.ok(!/\d \d/.test(m[0]), `обычный пробел в «${m[0]}»`);
      assert.ok(/ ₸$/.test(m[0]), `нет NBSP перед ₸ в «${m[0]}»`);
    }
    for (const re of FORBIDDEN) assert.ok(!re.test(all), `запрещено: ${re}`);
  });
}

// Beyond the brief: the fields demo.js relies on (source line, message times, avatar letter).
const KINDS = ['xlsx', 'pdf', 'jpg', 'catalog'];
for (const s of SCENARIOS) {
  test(`${s.id}: источник, время и имя для окна чата`, () => {
    assert.ok(KINDS.includes(s.source.kind), `source.kind ${s.source.kind}`);
    assert.ok(s.source.title.length > 0 && s.source.find.length > 0);
    assert.equal(s.times.length, 3);
    for (const t of s.times) assert.match(t, /^\d\d:\d\d$/);
    assert.ok(s.times[0] < s.times[1] && s.times[1] < s.times[2], 'время идёт вперёд');
    assert.ok(s.name.length > 1 && s.niche.length > 1 && s.short.length > 1);
    assert.ok(s.niche.toLowerCase().includes(s.short.toLowerCase()), 'короткая подпись вкладки входит в полную (label-in-name)');
    assert.ok(s.follow.length > 3 && s.question.length > 10);
  });
}

test('данные заморожены — демо не может их испортить', () => {
  assert.ok(Object.isFrozen(SCENARIOS) && SCENARIOS.every((s) => Object.isFrozen(s) && Object.isFrozen(s.cards)));
});

// The lead invites «нажмите на любой вариант ответа», but there is one follow-up per scenario:
// it has to read as a reply to ANY card. A word the client could only have taken from the best
// card («беру оригинал» after the visitor picked the Filtron analogue) breaks that.
const words = (t) => new Set(t.toLowerCase().match(/[а-яёa-z0-9]{4,}/g) || []);
for (const s of SCENARIOS) {
  test(`${s.id}: реплика клиента подходит к любой карточке`, () => {
    const q = words(s.question);
    const best = words(s.cards.find((c) => c.best).text);
    const others = new Set(s.cards.filter((c) => !c.best).flatMap((c) => [...words(c.text)]));
    const onlyBest = [...words(s.follow)].filter((w) => best.has(w) && !others.has(w) && !q.has(w));
    assert.deepEqual(onlyBest, [], `«${s.follow}» опирается на то, что предложила только лучшая карточка`);
  });
}
