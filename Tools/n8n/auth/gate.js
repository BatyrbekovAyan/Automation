// crAuth GATE — generated into every client webhook by Tools/n8n/apply-auth-gate.py from
// Tools/n8n/auth/gate.js (+ token.js). Edit the sources, re-run the migration; never the copies.
//
// Verifies the Bearer capability token WITHOUT a database round-trip (HMAC, see token.js), then
// checks that every id the request names is owned by the token's install:
//   RULE.profiles  — body fields whose value(s) must be ⊆ claims.p   (sentinels ''/'-1' pass)
//   RULE.workflows — body fields whose value(s) must be ⊆ claims.w   (sentinels ''/'-1' pass)
//   RULE.appUser   — body fields that must EQUAL claims.u; a token WITHOUT a billing identity (u = null: the
//                    id is held by another install or was not bindable) can name none (R2, 2026-09-27)
//   RULE.appUserRequired — appUser fields that may not be empty/sentinel in session mode (Create WA/TG: an
//                    empty AppUserID skipped the channel count and let the dialog quota fail open)
//   RULE.legacy    — true: a request WITHOUT a token is admitted in `legacy` mode while
//                    CR_AUTH_COMPAT_UNTIL lies in the future (old clients); false: 401
// Output: the webhook item re-emitted — json AND binary: a Code node drops whatever it does not return,
// and a multipart upload's parts (Unity WWWForm files even its text fields there) live in `binary`
// (R3, 2026-09-27) — with `crAuth` = { ok, mode, reason, installId, appUserId, profiles, workflows,
// claimPending } so downstream nodes can read the verified identity. The gate sees `body` only, so
// ids that arrive as multipart parts are checked by the workflow after it decodes them.
// __CRAUTH_TOKEN__
const RULE = __RULE__;
const inItem = $input.first();
const wh = inItem.json;
const secrets = [$env.CR_AUTH_SECRET, $env.CR_AUTH_SECRET_PREV];
const compatUntil = Date.parse($env.CR_AUTH_COMPAT_UNTIL || '') || 0;
const nowMs = Date.now();
const token = crAuth.bearer(wh.headers);
let mode = 'session', claims = null, deny = null;
if (!token) {
  if (RULE.legacy && compatUntil > nowMs) mode = 'legacy';
  else deny = 'unauthorized:missing';
} else {
  const v = crAuth.verify(token, secrets, Math.floor(nowMs / 1000));
  if (!v.ok) deny = 'unauthorized:' + v.reason; else claims = v.claims;
}
const body = (wh.body && typeof wh.body === 'object') ? wh.body : {};
const isSentinel = (x) => x === undefined || x === null || x === '' || x === '-1';
const listOf = (v) => (Array.isArray(v) ? v : [v]);
const owned = (set, v) => listOf(v).every((x) => isSentinel(x) || (typeof x === 'string' && set.includes(x)));
if (!deny && mode === 'session') {
  for (const f of RULE.profiles || []) if (!owned(claims.p, body[f])) { deny = 'forbidden:' + f; break; }
  if (!deny) for (const f of RULE.workflows || []) if (!owned(claims.w, body[f])) { deny = 'forbidden:' + f; break; }
  if (!deny) for (const f of RULE.appUser || []) {
    const v = body[f];
    if (isSentinel(v)) {
      if ((RULE.appUserRequired || []).includes(f)) { deny = 'forbidden:' + f; break; }
      continue;
    }
    if (!claims.u || String(v) !== claims.u) { deny = 'forbidden:' + f; break; }
  }
}
return [{ json: { ...wh, crAuth: {
  ok: !deny, mode, reason: deny,
  installId: claims ? claims.i : null, appUserId: claims ? claims.u : null,
  profiles: claims ? claims.p : [], workflows: claims ? claims.w : [], claimPending: claims ? (claims.c || []) : [],
} }, ...(inItem.binary ? { binary: inItem.binary } : {}), pairedItem: { item: 0 } }];
