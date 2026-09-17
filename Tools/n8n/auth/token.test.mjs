// node Tools/n8n/auth/token.test.mjs — pure tests of the shared token code (no n8n).
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const crAuth = require('./token.js');

const secret = 'test-secret-1', prev = 'test-secret-0';
const now = 1_800_000_000;
const claims = { i: 'inst-1', u: 'rc-1', p: ['wa1', 'tg1'], w: ['wf1'], e: now + 3600, n: 2 };
const t = crAuth.sign(claims, secret);
assert.match(t, /^v1\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/, 'token shape');

let v = crAuth.verify(t, [secret], now);
assert.equal(v.ok, true); assert.deepEqual(v.claims, { ...claims, c: [] });
v = crAuth.verify(t, [prev, secret], now); assert.equal(v.ok, true, 'accepts the current secret listed second (rotation)');
v = crAuth.verify(t, ['other'], now); assert.equal(v.ok, false); assert.equal(v.reason, 'bad_signature');
v = crAuth.verify(t, [secret], now + 3600); assert.equal(v.ok, false); assert.equal(v.reason, 'expired', 'exp is exclusive');
v = crAuth.verify('', [secret], now); assert.equal(v.reason, 'missing');
v = crAuth.verify('v1.abc', [secret], now); assert.equal(v.reason, 'malformed');
v = crAuth.verify(t.slice(0, -2) + 'AA', [secret], now); assert.equal(v.reason, 'bad_signature', 'tampered signature');
const [h, p, s] = t.split('.');
const forgedPayload = crAuth.b64u(JSON.stringify({ ...claims, p: ['victim'] }));
v = crAuth.verify(`${h}.${forgedPayload}.${s}`, [secret], now); assert.equal(v.reason, 'bad_signature', 'tampered payload');
// malformed claims still signed correctly must be rejected (defence in depth)
v = crAuth.verify(crAuth.sign({ i: 'x', p: 'notalist', w: [], e: now + 10 }, secret), [secret], now); assert.equal(v.reason, 'malformed');
v = crAuth.verify(crAuth.sign({ i: 'x', p: [], w: [], e: 'soon' }, secret), [secret], now); assert.equal(v.reason, 'expired');
v = crAuth.verify(crAuth.sign({ i: 'x', p: [], w: [], c: ['pend'], e: now + 10 }, secret), [secret], now); assert.deepEqual(v.claims.c, ['pend'], 'claim-pending list survives');
v = crAuth.verify(crAuth.sign({ i: 'x', p: [], w: [], c: 'bad', e: now + 10 }, secret), [secret], now); assert.equal(v.reason, 'malformed');
assert.equal(crAuth.bearer({ authorization: 'Bearer abc' }), 'abc');
assert.equal(crAuth.bearer({ Authorization: 'Basic abc' }), '');
assert.equal(crAuth.sha256Hex('a').length, 64);
assert.ok(crAuth.randomSecret(32).length >= 40);
console.log('token.js: all assertions passed');
