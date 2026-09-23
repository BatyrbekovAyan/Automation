import { test } from 'node:test';
import assert from 'node:assert/strict';
import { formatKzt, yearlyMonthly, savingsPercent, PLANS } from '../../src/js/pricing-format.js';

test('formatKzt разбивает разряды неразрывным пробелом', () => {
  assert.equal(formatKzt(9990), '9 990');
  assert.equal(formatKzt(199990), '199 990');
  assert.equal(formatKzt(399990), '399 990');
  assert.equal(formatKzt(900), '900');
});
test('yearlyMonthly', () => {
  assert.equal(yearlyMonthly(99000), 8250);
  assert.equal(yearlyMonthly(198990), 16582);
  assert.equal(yearlyMonthly(399990), 33332);
});
test('savingsPercent — честная выгода годовой оплаты', () => {
  assert.equal(savingsPercent(9990, 99000), 17);
  assert.equal(savingsPercent(19990, 198990), 17);
  assert.equal(savingsPercent(39900, 399990), 16);
});
test('PLANS совпадают с PlanCatalog.cs', () => {
  assert.deepEqual(PLANS.map((p) => [p.id, p.month, p.year, p.bots, p.channels, p.dialogs, !!p.popular]), [
    ['start', 9990, 99000, 1, 1, 300, false],
    ['business', 19990, 198990, 3, 3, 1000, true],
    ['network', 39900, 399990, 5, 5, 3000, false],
  ]);
});
