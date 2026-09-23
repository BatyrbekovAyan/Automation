import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validatePhone, encodeLead, submitLead } from '../../src/js/form.js';

test('validatePhone: 10–15 цифр', () => {
  assert.equal(validatePhone('+7 (700) 000-00-00').ok, true);
  assert.equal(validatePhone('8700000000').ok, true);
  assert.equal(validatePhone('12345').ok, false);
  // The brief's literal '+7 700 000 00 00 00 00' has 15 digits — valid by the v1 contract
  // (digits < 10 || digits > 15 is the only rejection) — so the boundary is pinned both ways.
  assert.equal(validatePhone('+7 700 000 00 00 00 00').ok, true);
  assert.equal(validatePhone('+7 700 000 00 00 00 000').ok, false);
  assert.equal(validatePhone('123456789').ok, false);
  assert.equal(validatePhone('').ok, false);
});
test('encodeLead: ровно четыре поля, source=landing, пустой honeypot', () => {
  const p = encodeLead({ name: ' Айдос ', phone: '+7 700 000 00 00', website: '', extra: 'x' });
  assert.deepEqual([...p.keys()], ['name', 'phone', 'website', 'source']);
  assert.equal(p.get('name'), 'Айдос');
  assert.equal(p.get('source'), 'landing');
  assert.equal(p.get('website'), '');
});
test('submitLead: плохой телефон — без сети', async () => {
  let called = 0;
  const r = await submitLead('https://x/webhook/LandingLead', { phone: '123' }, async () => { called++; });
  assert.deepEqual(r, { ok: false, reason: 'bad_phone' });
  assert.equal(called, 0);
});
test('submitLead: form-encoded POST и успех', async () => {
  let seen;
  const r = await submitLead('https://x/webhook/LandingLead', { name: 'А', phone: '+77000000000', website: '' }, async (url, init) => {
    seen = { url, init };
    return { json: async () => ({ success: true }) };
  });
  assert.deepEqual(r, { ok: true });
  assert.equal(seen.init.method, 'POST');
  assert.ok(seen.init.body instanceof URLSearchParams);
});
test('submitLead: bad_request сервера → bad_phone, иное → server, исключение → network', async () => {
  assert.deepEqual(await submitLead('u', { phone: '+77000000000' }, async () => ({ json: async () => ({ success: false, error: 'bad_request' }) })), { ok: false, reason: 'bad_phone' });
  assert.deepEqual(await submitLead('u', { phone: '+77000000000' }, async () => ({ json: async () => ({ success: false, error: 'db' }) })), { ok: false, reason: 'server' });
  assert.deepEqual(await submitLead('u', { phone: '+77000000000' }, async () => { throw new Error('offline'); }), { ok: false, reason: 'network' });
});
