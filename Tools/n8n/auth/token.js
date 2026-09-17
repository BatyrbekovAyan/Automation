// crAuth v1 — capability token sign/verify shared by the Auth Session workflow (sign) and every
// client-webhook gate (verify). SOURCE OF TRUTH: this file. Tools/n8n/apply-auth-gate.py and
// build-auth-workflows.py inline it into Code nodes between the markers below; never edit the copies.
// Runs inside an n8n Code node: needs NODE_FUNCTION_ALLOW_BUILTIN=crypto on the server.
//
// Token: "v1." + b64url(JSON payload) + "." + b64url(HMAC-SHA256(secret, "v1." + b64url(payload)))
// Payload: { i: installId, u: appUserId|null, p: [profileIds], w: [workflowIds], c: [claim-pending profileIds],
//            e: expUnixSec, n: tokenVersion }
// CRAUTH-TOKEN-BEGIN
const crAuth = (() => {
  const crypto = require('crypto');
  const b64u = (buf) => Buffer.from(buf).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  const unb64u = (s) => Buffer.from(String(s).replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((String(s).length + 3) % 4), 'base64');
  const isIdList = (a) => Array.isArray(a) && a.every((x) => typeof x === 'string' && x.length > 0 && x.length <= 128);
  function sign(payload, secret) {
    if (!secret) throw new Error('crAuth: no signing secret');
    const p = b64u(JSON.stringify(payload));
    const sig = crypto.createHmac('sha256', secret).update('v1.' + p).digest();
    return 'v1.' + p + '.' + b64u(sig);
  }
  function verify(token, secrets, nowSec) {
    if (typeof token !== 'string' || !token) return { ok: false, reason: 'missing' };
    const parts = token.split('.');
    if (parts.length !== 3 || parts[0] !== 'v1' || !parts[1] || !parts[2]) return { ok: false, reason: 'malformed' };
    const given = unb64u(parts[2]);
    let good = false;
    for (const sec of (secrets || []).filter(Boolean)) {
      const exp = crypto.createHmac('sha256', sec).update(parts[0] + '.' + parts[1]).digest();
      if (exp.length === given.length && crypto.timingSafeEqual(exp, given)) { good = true; break; }
    }
    if (!good) return { ok: false, reason: 'bad_signature' };
    let c;
    try { c = JSON.parse(unb64u(parts[1]).toString('utf8')); } catch (e) { return { ok: false, reason: 'malformed' }; }
    if (!c || typeof c !== 'object' || typeof c.i !== 'string' || !c.i || !isIdList(c.p) || !isIdList(c.w)) return { ok: false, reason: 'malformed' };
    if (typeof c.e !== 'number' || !(c.e > nowSec)) return { ok: false, reason: 'expired' };
    if (c.c !== undefined && !isIdList(c.c)) return { ok: false, reason: 'malformed' };
    return { ok: true, claims: { i: c.i, u: (typeof c.u === 'string' && c.u) ? c.u : null, p: c.p, w: c.w, c: c.c || [], e: c.e, n: (typeof c.n === 'number') ? c.n : 1 } };
  }
  function sha256Hex(s) { return crypto.createHash('sha256').update(String(s)).digest('hex'); }
  function randomSecret(bytes) { return b64u(crypto.randomBytes(bytes || 32)); }
  function bearer(headers) {
    const h = headers || {};
    const a = String(h.authorization || h.Authorization || '');
    return a.startsWith('Bearer ') ? a.slice(7).trim() : '';
  }
  return { sign, verify, sha256Hex, randomSecret, bearer, b64u, unb64u };
})();
// CRAUTH-TOKEN-END
if (typeof module !== 'undefined') module.exports = crAuth;
