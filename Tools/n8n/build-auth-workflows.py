#!/usr/bin/env python3
"""Generate the two A1 canonical workflows from source (audit 2026-09, stage 1):

  crAuthSession001-Auth_Session.json  — /webhook/AuthSession  {op: bootstrap|refresh|claim.start|claim.verify|claim.status}
  crWappiProxy0001-Wappi_Proxy.json   — /webhook/WappiProxy   {op, channel, ...}  Bearer capability token

Both embed Tools/n8n/auth/token.js (sign/verify) and, for the proxy, Tools/n8n/auth/gate.js. The
files are DERIVED: edit this builder or the auth/*.js sources and re-run; never the JSON by hand.
Design: docs/superpowers/specs/2026-09-17-a1-server-identity-design.md.

    python3 Tools/n8n/build-auth-workflows.py           # (re)write workflows/crAuth*.json
    python3 Tools/n8n/build-auth-workflows.py --check   # exit 2 if the committed JSON is stale
"""
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
AUTH = os.path.join(HERE, "auth")
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}            # canonical executeQuery cred (parity verifier constant)
WAPPI_CRED = {"id": "EuhhqAaV56DpoqAN", "name": "WappiAuthToken"}  # canonical, remapped by deployers / derive-staging

SESSION_ID, SESSION_NAME = "crAuthSession001", "Auth Session"
PROXY_ID, PROXY_NAME = "crWappiProxy0001", "Wappi Proxy"
TOKEN_TTL_SEC = 12 * 3600


def token_block():
    src = open(os.path.join(AUTH, "token.js"), encoding="utf-8").read()
    a, b = src.index("// CRAUTH-TOKEN-BEGIN"), src.index("// CRAUTH-TOKEN-END") + len("// CRAUTH-TOKEN-END")
    return src[a:b]


def gate_code(rule):
    src = open(os.path.join(AUTH, "gate.js"), encoding="utf-8").read()
    return src.replace("// __CRAUTH_TOKEN__", token_block()).replace("__RULE__", json.dumps(rule, ensure_ascii=False))


def nid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "crauth:" + name))


def code(name, js, pos):
    return {"parameters": {"jsCode": js}, "id": nid(name), "name": name, "type": "n8n-nodes-base.code", "typeVersion": 2, "position": pos}


def if_true(name, expr, pos):
    return {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                          "conditions": [{"id": nid(name + ":cond"), "leftValue": expr, "rightValue": "",
                                                          "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                          "combinator": "and"}, "options": {}},
            "id": nid(name), "name": name, "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": pos}


def respond(name, pos, body="={{ Object.fromEntries(Object.entries($json).filter(([k]) => k !== '_status')) }}", code_expr="={{ $json._status || 200 }}"):
    return {"parameters": {"respondWith": "json", "responseBody": body, "options": {"responseCode": code_expr}},
            "id": nid(name), "name": name, "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5, "position": pos}


def respond_fixed(name, pos, body_obj, status):
    return {"parameters": {"respondWith": "json", "responseBody": "={{ " + json.dumps(body_obj, ensure_ascii=False) + " }}",
                           "options": {"responseCode": status}},
            "id": nid(name), "name": name, "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5, "position": pos}


def postgres(name, query, replacement, pos):
    return {"parameters": {"operation": "executeQuery", "query": query, "options": {"queryReplacement": replacement}},
            "id": nid(name), "name": name, "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": pos,
            "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"}


def webhook(path, pos):
    return {"parameters": {"httpMethod": "POST", "path": path, "responseMode": "responseNode", "options": {}},
            "id": nid("webhook:" + path), "name": "Webhook", "type": "n8n-nodes-base.webhook", "typeVersion": 2.1,
            "position": pos, "webhookId": nid("webhookId:" + path)}


def http_wappi(name, method, url_expr, pos, query_expr=None, body_expr=None, timeout=30000, extra=None):
    p = {"method": method, "url": url_expr, "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
         "options": {"timeout": timeout, "response": {"response": {"fullResponse": True, "neverError": True, "responseFormat": "json"}}}}
    if query_expr is not None:
        p.update({"sendQuery": True, "specifyQuery": "json", "jsonQuery": query_expr})
    if body_expr is not None:
        p.update({"sendBody": True, "specifyBody": "json", "jsonBody": body_expr})
    if extra:
        p.update(extra)
    return {"parameters": p, "id": nid(name), "name": name, "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
            "position": pos, "credentials": {"httpHeaderAuth": dict(WAPPI_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"}


def edge(conns, src, dst, out=0):
    conns.setdefault(src, {"main": []})
    while len(conns[src]["main"]) <= out:
        conns[src]["main"].append([])
    conns[src]["main"][out].append({"node": dst, "type": "main", "index": 0})


def workflow(wid, name, nodes, conns, settings=None):
    return {"id": wid, "name": name, "active": False, "nodes": nodes, "connections": conns, "pinData": {},
            "settings": settings or {"executionOrder": "v1"}, "staticData": None, "meta": None, "tags": []}


# ------------------------------------------------------------------------------------------------
# Auth Session
# ------------------------------------------------------------------------------------------------
ROUTE_JS = r"""// AUTH SESSION / Route (generated by Tools/n8n/build-auth-workflows.py — see its docstring)
__TOKEN__
const wh = $input.first().json;
const b = (wh.body && typeof wh.body === 'object') ? wh.body : {};
const clean = (v, max) => (typeof v === 'string' ? v.slice(0, max) : '');
const idOk = (v) => typeof v === 'string' && /^[A-Za-z0-9_\-:.@]{1,128}$/.test(v);
const op = clean(b.op, 32);
const out = { op, invalid: false, statusCode: 200, error: null, installId: '', installSecret: '', secretHash: '',
  appUserId: clean(b.appUserId, 128), platform: clean(b.platform, 32), appVersion: clean(b.appVersion, 32),
  profileId: clean(b.profileId, 128), channel: (b.channel === 'telegram') ? 'telegram' : 'whatsapp',
  code: clean(b.code, 16), nowSec: Math.floor(Date.now() / 1000) };
if (op === 'bootstrap') {
  out.installId = require('crypto').randomUUID();
  out.installSecret = crAuth.randomSecret(32);
  out.secretHash = crAuth.sha256Hex(out.installSecret);
} else if (op === 'refresh' || op === 'claim.start' || op === 'claim.verify' || op === 'claim.status') {
  out.installId = clean(b.installId, 64);
  const sec = clean(b.installSecret, 128);
  if (!idOk(out.installId) || !sec) out.invalid = true; else out.secretHash = crAuth.sha256Hex(sec);
  if (op.startsWith('claim.') && !idOk(out.profileId)) out.invalid = true;
  if (op === 'claim.verify' && !/^\d{6}$/.test(out.code)) out.invalid = true;
} else {
  out.invalid = true;
}
if (out.invalid) { out.statusCode = 400; out.error = 'bad_request'; }
return [{ json: out, pairedItem: { item: 0 } }];
"""

CREATE_INSTALL_SQL = """-- A1: mint an install (the client keeps installId + installSecret; only the hash is stored).
-- Bootstrap is unauthenticated, so it is capped: at most 60 new installs per minute instance-wide.
insert into installs (install_id, secret_hash, app_user_id, platform, app_version)
select $1, $2, nullif($3, ''), nullif($4, ''), nullif($5, '')
where (select count(*) from installs where created_at > now() - interval '1 minute') < 60
returning install_id, app_user_id, token_version, '{}'::text[] as p, '{}'::text[] as w, '{}'::text[] as c;"""

VERIFY_INSTALL_SQL = """-- A1: refresh = prove the install secret (hash compare), touch last_seen, adopt the client's current
-- billing id, and load everything this install owns in ONE round trip (each Postgres node costs
-- ~1.9 s on prod). ALWAYS exactly one row carrying verdict = 'checked' (R11, 2026-09-27): install_id is
-- NULL when the secret is wrong or the install is unknown/revoked -> 401 install_revoked. A row WITHOUT
-- the verdict (node error, input passed through, nothing) is a fault -> 503, never a reason to wipe.
with v as (
  update installs set last_seen_at = now(), app_user_id = coalesce(nullif($3, ''), app_user_id)
  where install_id = $1 and secret_hash = $2 and revoked_at is null
  returning install_id, app_user_id, token_version
)
select 'checked'::text as verdict, v.install_id, v.app_user_id, v.token_version,
  coalesce((select array_agg(bp.profile_id) from bot_profiles bp where bp.owner_id = v.install_id and bp.deleted_at is null), '{}'::text[]) as p,
  coalesce((select array_agg(distinct bp.workflow_id) from bot_profiles bp where bp.owner_id = v.install_id and bp.deleted_at is null and bp.workflow_id is not null), '{}'::text[]) as w,
  coalesce((select array_agg(bp.profile_id) from bot_profiles bp where bp.claim_pending_by = v.install_id and bp.owner_id is null and bp.claim_pending_at > now() - interval '1 day'), '{}'::text[]) as c
from (select 1) as one left join v on true;"""

RELOAD_OWNERSHIP_SQL = """-- A1: re-read ownership after a claim step (same shape as Verify Install's row).
select i.install_id, i.app_user_id, i.token_version,
  coalesce((select array_agg(bp.profile_id) from bot_profiles bp where bp.owner_id = i.install_id and bp.deleted_at is null), '{}'::text[]) as p,
  coalesce((select array_agg(distinct bp.workflow_id) from bot_profiles bp where bp.owner_id = i.install_id and bp.deleted_at is null and bp.workflow_id is not null), '{}'::text[]) as w,
  coalesce((select array_agg(bp.profile_id) from bot_profiles bp where bp.claim_pending_by = i.install_id and bp.owner_id is null and bp.claim_pending_at > now() - interval '1 day'), '{}'::text[]) as c
from installs i where i.install_id = $1 and i.revoked_at is null;"""

CLAIM_START_SQL = """-- A1 claim: register the intent. Legacy (unowned) profiles AND profiles owned by a previous install
-- (reinstall / new phone) are claimable — ownership is proven by control of the channel itself
-- (code in the bot's own chat, or a fresh pairing), never by knowing an id. A still-valid code is
-- reused (no re-send inside its 10-minute window), which also bounds unsolicited codes.
insert into bot_profiles (profile_id, app_user_id, channel, claim_pending_by, claim_pending_at)
values ($1, coalesce(nullif($3, ''), $2), $4, $2, now())
on conflict (profile_id) do update set
  claim_pending_by = case when bot_profiles.owner_id = $2 then bot_profiles.claim_pending_by else excluded.claim_pending_by end,
  -- the claim clock starts ONCE per claiming install: a repeated claim.* call (status polling) must not
  -- move claim_pending_at, or a pairing done in between could never be "after the claim began"
  claim_pending_at = case
      when bot_profiles.owner_id = $2 then bot_profiles.claim_pending_at
      when bot_profiles.claim_pending_by = $2 and bot_profiles.claim_pending_at is not null then bot_profiles.claim_pending_at
      else now() end
returning profile_id, owner_id, channel, claim_pending_by, extract(epoch from claim_pending_at)::bigint as claim_pending_epoch,
  (claim_code_expires_at is not null and claim_code_expires_at > now()) as code_still_valid;"""

STORE_CLAIM_CODE_SQL = """update bot_profiles set claim_code_hash = $3, claim_code_expires_at = now() + interval '10 minutes'
where profile_id = $1 and claim_pending_by = $2 and (claim_code_expires_at is null or claim_code_expires_at <= now())
returning profile_id;"""

VERIFY_CLAIM_CODE_SQL = """-- A1 legacy claim by self-message code: binds ownership only for the install that started the claim,
-- with an unexpired matching code, on a still-unowned profile.
update bot_profiles set owner_id = $2, claimed_at = now(), claim_pending_by = null, claim_pending_at = null,
  claim_code_hash = null, claim_code_expires_at = null
where profile_id = $1 and (owner_id is null or owner_id <> $2) and claim_pending_by = $2 and claim_code_hash = $3 and claim_code_expires_at > now()
returning profile_id;"""

BIND_REAUTH_SQL = """-- A1 legacy claim by re-authorisation: the channel was (re)paired AFTER the claim started.
update bot_profiles set owner_id = $2, claimed_at = now(), claim_pending_by = null, claim_pending_at = null
where profile_id = $1 and (owner_id is null or owner_id <> $2) and claim_pending_by = $2 and claim_pending_at < to_timestamp($3::bigint)
returning profile_id;"""

CLAIM_DECIDE_JS = r"""// AUTH SESSION / Claim Decide: what the claim needs next (generated — see build-auth-workflows.py)
__TOKEN__
const r = $('Route').first().json;
const inst = $('Verify Install').first().json || {};
const row = $('Claim Start').first().json || {};
const st = $input.first().json || {};                 // fullResponse of Wappi get/status
const status = (st.body && typeof st.body === 'object') ? st.body : {};
const out = { op: r.op, profileId: r.profileId, channel: r.channel, installId: inst.install_id, statusCode: 200,
  claimMethod: null, claimError: null, code: null, codeHash: null, phone: null, authorizedEpoch: null };
if (!inst.install_id) {                                // R11: 401 only on the DB's positive verdict, else a fault
  const revoked = !inst.error && inst.verdict === 'checked';
  out.statusCode = revoked ? 401 : 503; out.claimError = revoked ? 'install_revoked' : 'db_unavailable';
}
else if (row.error || !row.profile_id) { out.statusCode = 500; out.claimError = 'db_error'; }
else if (row.owner_id === inst.install_id) { out.claimMethod = 'owned'; }
else if (st.statusCode !== 200 || typeof status.authorized !== 'boolean') { out.statusCode = 502; out.claimError = 'transport_unavailable'; }
else if (status.authorized === true) {
  const phone = String(status.phone || '').replace(/\D/g, '');
  const ts = Date.parse(status.authorized_at || '') || 0;
  out.authorizedEpoch = ts ? Math.floor(ts / 1000) : null;
  if (r.op === 'claim.status' && out.authorizedEpoch && out.authorizedEpoch > Number(row.claim_pending_epoch || 0)) {
    out.claimMethod = 'reauth';                        // paired after the claim began -> bind
  } else if (!phone) { out.statusCode = 409; out.claimError = 'no_phone'; }
  else if (row.code_still_valid === true && r.op === 'claim.start') {
    out.claimMethod = 'code';                        // a code is already on its way / valid: do not re-send
    out.codeReused = true;
  } else {
    out.claimMethod = 'code';
    out.phone = phone;
    out.code = String(Math.floor(100000 + Math.random() * 900000));
    out.codeHash = crAuth.sha256Hex(out.code);
  }
} else {
  out.claimMethod = 'reauth';                          // not authorised: the client re-pairs via the proxy (c-capability)
}
return [{ json: out, pairedItem: { item: 0 } }];
"""

ISSUE_TOKEN_JS = r"""// AUTH SESSION / Issue Token (generated — see build-auth-workflows.py)
__TOKEN__
const r = $('Route').first().json;
const row = $input.first() ? ($input.first().json || {}) : {};
let claimInfo = null;
try { const d = $('Claim Decide').first(); if (d && d.json && d.json.op) claimInfo = d.json; } catch (e) { claimInfo = null; }
let claimVerify = null;
try { const v = $('Verify Claim Code').first(); if (v && v.json) claimVerify = v.json; } catch (e) { claimVerify = null; }
if (claimInfo && claimInfo.claimError) {
  return [{ json: { success: false, error: claimInfo.claimError, _status: claimInfo.statusCode || 400 }, pairedItem: { item: 0 } }];
}
if (r.op === 'claim.verify' && (!claimVerify || !claimVerify.profile_id)) {
  return [{ json: { success: false, error: 'claim_code_rejected', _status: 403 }, pairedItem: { item: 0 } }];
}
if (row.error || !row.install_id) {
  if (r.op === 'bootstrap') {
    const status = row.error ? 500 : 429;
    return [{ json: { success: false, error: status === 429 ? 'rate_limited' : 'db_error', _status: status }, pairedItem: { item: 0 } }];
  }
  // R11: the client DISCARDS its install on a 401 — so 401 only on the database's positive verdict (Verify
  // Install's 'checked' row without an install: wrong secret / unknown / revoked). Every other shape is a
  // transient fault (the node's error item, the Route item passed through when it failed before its handler,
  // an empty row) and answers 503: the client keeps its credentials and retries later.
  const revoked = !row.error && row.verdict === 'checked';
  return [{ json: { success: false, error: revoked ? 'install_revoked' : 'db_unavailable', _status: revoked ? 401 : 503 }, pairedItem: { item: 0 } }];
}
const exp = r.nowSec + __TTL__;
const claims = { i: row.install_id, u: row.app_user_id || null, p: row.p || [], w: row.w || [], c: row.c || [], e: exp, n: row.token_version || 1 };
const token = crAuth.sign(claims, $env.CR_AUTH_SECRET);
const out = { success: true, _status: 200, installId: row.install_id, token, expiresAt: exp, appUserId: claims.u,
  profiles: claims.p, workflows: claims.w, claimPending: claims.c };
if (r.op === 'bootstrap') out.installSecret = r.installSecret;
if (claimInfo) { out.claimMethod = claimInfo.claimMethod; out.claimProfileId = claimInfo.profileId; out.codeReused = !!claimInfo.codeReused; }
return [{ json: out, pairedItem: { item: 0 } }];
"""

CLAIM_STATUS_QUERY = "={{ JSON.stringify({ profile_id: $('Route').first().json.profileId }) }}"
CLAIM_STATUS_URL = "={{ ($('Route').first().json.channel === 'telegram' ? 'https://wappi.pro/tapi/' : 'https://wappi.pro/api/') + 'sync/get/status' }}"
SEND_CODE_URL = "={{ ($('Route').first().json.channel === 'telegram' ? 'https://wappi.pro/tapi/' : 'https://wappi.pro/api/') + 'sync/message/send' }}"
SEND_CODE_QUERY = "={{ JSON.stringify({ profile_id: $('Route').first().json.profileId }) }}"
SEND_CODE_BODY = "={{ JSON.stringify({ recipient: $('Claim Decide').first().json.phone, body: 'Код подтверждения Choose Reply: ' + $('Claim Decide').first().json.code + '. Никому не сообщайте его.' }) }}"


def build_session():
    tok = token_block()
    n, c = [], {}
    n.append(webhook("AuthSession", [0, 300]))
    n.append(code("Route", ROUTE_JS.replace("__TOKEN__", tok), [220, 300]))
    n.append(if_true("If invalid?", "={{ $json.invalid }}", [440, 300]))
    n.append(respond("Respond Error", [660, 520], body="={{ { success: false, error: $json.error } }}", code_expr="={{ $json.statusCode }}"))
    n.append(if_true("Bootstrap?", "={{ $json.op === 'bootstrap' }}", [660, 200]))
    n.append(postgres("Create Install", CREATE_INSTALL_SQL,
                      "={{ [ $json.installId, $json.secretHash, $json.appUserId, $json.platform, $json.appVersion ] }}", [880, 80]))
    n.append(postgres("Verify Install", VERIFY_INSTALL_SQL,
                      "={{ [ $json.installId, $json.secretHash, $json.appUserId ] }}", [880, 320]))
    n.append(if_true("Claim Op?", "={{ String($('Route').first().json.op).startsWith('claim.') }}", [1100, 320]))
    n.append(postgres("Claim Start", CLAIM_START_SQL,
                      "={{ [ $('Route').first().json.profileId, $('Route').first().json.installId, ($input.first().json.app_user_id || ''), $('Route').first().json.channel ] }}", [1320, 520]))
    n.append(http_wappi("Claim Status", "GET", CLAIM_STATUS_URL, [1540, 520], query_expr=CLAIM_STATUS_QUERY))
    n.append(code("Claim Decide", CLAIM_DECIDE_JS.replace("__TOKEN__", tok), [1760, 520]))
    n.append(if_true("Claim Verify?", "={{ $json.op === 'claim.verify' && !$json.claimError }}", [1980, 520]))
    n.append(postgres("Verify Claim Code", VERIFY_CLAIM_CODE_SQL,
                      "={{ [ $('Route').first().json.profileId, $('Route').first().json.installId, crAuthHashOf($('Route').first().json.code) ] }}".replace(
                          "crAuthHashOf($('Route').first().json.code)", "$('Route').first().json.codeHash"), [2200, 400]))
    n.append(if_true("Send Code?", "={{ $json.claimMethod === 'code' && !$json.codeReused && !!$json.code }}", [2200, 640]))
    n.append(postgres("Store Claim Code", STORE_CLAIM_CODE_SQL,
                      "={{ [ $('Route').first().json.profileId, $('Route').first().json.installId, $('Claim Decide').first().json.codeHash ] }}", [2420, 560]))
    n.append(http_wappi("Send Claim Code", "POST", SEND_CODE_URL, [2640, 560], query_expr=SEND_CODE_QUERY, body_expr=SEND_CODE_BODY))
    n.append(if_true("Bind Reauth?", "={{ $('Claim Decide').first().json.op === 'claim.status' && $('Claim Decide').first().json.claimMethod === 'reauth' && !!$('Claim Decide').first().json.authorizedEpoch }}", [2420, 760]))
    n.append(postgres("Bind Reauthed Profile", BIND_REAUTH_SQL,
                      "={{ [ $('Route').first().json.profileId, $('Route').first().json.installId, $('Claim Decide').first().json.authorizedEpoch ] }}", [2640, 760]))
    n.append(postgres("Reload Ownership", RELOAD_OWNERSHIP_SQL, "={{ [ $('Route').first().json.installId ] }}", [2860, 520]))
    n.append(code("Issue Token", ISSUE_TOKEN_JS.replace("__TOKEN__", tok).replace("__TTL__", str(TOKEN_TTL_SEC)), [3080, 320]))
    n.append(respond("Respond", [3300, 320]))
    # Route: the claim.verify code hash is computed in Route (so the SQL node can bind it as a plain param)
    route = next(x for x in n if x["name"] == "Route")
    route["parameters"]["jsCode"] = route["parameters"]["jsCode"].replace(
        "  if (op === 'claim.verify' && !/^\\d{6}$/.test(out.code)) out.invalid = true;",
        "  if (op === 'claim.verify' && !/^\\d{6}$/.test(out.code)) out.invalid = true;\n  out.codeHash = out.code ? crAuth.sha256Hex(out.code) : '';")
    edge(c, "Webhook", "Route"); edge(c, "Route", "If invalid?")
    edge(c, "If invalid?", "Respond Error", 0); edge(c, "If invalid?", "Bootstrap?", 1)
    edge(c, "Bootstrap?", "Create Install", 0); edge(c, "Bootstrap?", "Verify Install", 1)
    edge(c, "Create Install", "Issue Token")
    edge(c, "Verify Install", "Claim Op?")
    edge(c, "Claim Op?", "Claim Start", 0); edge(c, "Claim Op?", "Issue Token", 1)
    edge(c, "Claim Start", "Claim Status"); edge(c, "Claim Status", "Claim Decide"); edge(c, "Claim Decide", "Claim Verify?")
    edge(c, "Claim Verify?", "Verify Claim Code", 0); edge(c, "Claim Verify?", "Send Code?", 1)
    edge(c, "Verify Claim Code", "Reload Ownership")
    edge(c, "Send Code?", "Store Claim Code", 0); edge(c, "Send Code?", "Bind Reauth?", 1)
    edge(c, "Store Claim Code", "Send Claim Code"); edge(c, "Send Claim Code", "Reload Ownership")
    edge(c, "Bind Reauth?", "Bind Reauthed Profile", 0); edge(c, "Bind Reauth?", "Reload Ownership", 1)
    edge(c, "Bind Reauthed Profile", "Reload Ownership")
    edge(c, "Reload Ownership", "Issue Token"); edge(c, "Issue Token", "Respond")
    return workflow(SESSION_ID, SESSION_NAME, n, c)


# ------------------------------------------------------------------------------------------------
# Wappi Proxy
# ------------------------------------------------------------------------------------------------
BUILD_REQUEST_JS = r"""// WAPPI PROXY / Build Request (generated — see build-auth-workflows.py). Allow-listed operations
// only: every op maps to ONE Wappi endpoint with validated parameters; the profile named by the
// request must be owned by the token's install (or be a pending legacy claim for the auth ops).
const g = $input.first().json;
const claims = g.crAuth || {};
const b = (g.body && typeof g.body === 'object') ? g.body : {};
const BASE = { whatsapp: 'https://wappi.pro/api/', telegram: 'https://wappi.pro/tapi/' };
const ID = /^[A-Za-z0-9@._:+\-]{1,128}$/;
const PHONE = /^\d{7,15}$/;
const NAME = /^[\p{L}\p{N} _\-.]{1,64}$/u;
const AUTH_OPS = new Set(['status', 'qr', 'auth.code', 'tg.auth.phone', 'tg.auth.code', 'tg.auth.2fa']);
const OPS = {
  'profile.add':       { m: 'POST', path: 'profile/add', q: { name: ['name', NAME] }, noProfile: true, needsBilling: true },
  'profile.delete':    { m: 'POST', path: 'profile/delete', q: { profile_id: 'pid' } },
  'profile.logout':    { m: 'GET',  path: 'profile/logout', q: { profile_id: 'pid' } },
  'status':            { m: 'GET',  path: 'sync/get/status', q: { profile_id: 'pid' } },
  'qr':                { m: 'GET',  path: { whatsapp: 'sync/qr/get', telegram: 'sync/auth/qr' }, q: { profile_id: 'pid' } },
  'auth.code':         { m: 'GET',  path: 'sync/auth/code', q: { profile_id: 'pid', phone: ['phone', PHONE] }, only: 'whatsapp' },
  'tg.auth.phone':     { m: 'POST', path: 'sync/auth/phone', q: { profile_id: 'pid' }, body: { phone: ['phone', PHONE] }, only: 'telegram' },
  'tg.auth.code':      { m: 'POST', path: 'sync/auth/code', q: { profile_id: 'pid' }, body: { auth_code: ['auth_code', /^\d{4,8}$/] }, only: 'telegram' },
  'tg.auth.2fa':       { m: 'POST', path: 'sync/auth/2fa', q: { profile_id: 'pid' }, body: { pwd_code: ['pwd_code', 'text', 256] }, only: 'telegram' },
  'chats.filter':      { m: 'GET',  path: 'sync/chats/filter', q: { profile_id: 'pid' } },
  'messages.get':      { m: 'GET',  path: 'sync/messages/get', q: { profile_id: 'pid', chat_id: ['chat_id', ID], limit: ['limit', 'int', 200, 15], offset: ['offset', 'int', 100000, 0] } },
  'messages.id.get':   { m: 'GET',  path: 'sync/messages/id/get', q: { profile_id: 'pid', message_id: ['message_id', ID] } },
  'media.download':    { m: 'GET',  path: 'sync/message/media/download', q: { profile_id: 'pid', message_id: ['message_id', ID] }, timeout: 60000 },
  'message.send':      { m: 'POST', path: 'sync/message/send', q: { profile_id: 'pid' }, body: { recipient: ['recipient', ID], body: ['body', 'text', 8192], quoted_message_id: ['quoted_message_id', ID, 'opt'] } },
  'message.reply':     { m: 'POST', path: 'sync/message/reply', q: { profile_id: 'pid' }, body: { recipient: ['recipient', ID, 'opt'], body: ['body', 'text', 8192], message_id: ['message_id', ID] }, only: 'telegram' },
  'message.mark_read': { m: 'POST', path: 'sync/message/mark/read', q: { profile_id: 'pid', mark_all: ['mark_all', 'bool', 'opt'] }, body: { message_id: ['message_id', ID] } },
  'chat.delete':       { m: 'POST', path: 'sync/chat/delete', q: { profile_id: 'pid' }, body: { recipient: ['recipient', ID] } },
  'message.reaction':  { m: 'POST', path: 'sync/message/reaction', q: { profile_id: 'pid' }, body: { body: ['body', 'text', 64], message_id: ['message_id', ID], recipient: ['recipient', ID, 'opt'] } },
  'media.send':        { m: 'POST', path: { img: 'sync/message/img/send', video: 'sync/message/video/send', document: 'sync/message/document/send' }, q: { profile_id: 'pid' },
                         body: { recipient: ['recipient', ID], caption: ['caption', 'text', 1024, 'opt'], file_name: ['file_name', 'text', 255, 'opt'], b64_file: ['b64_file', 'text', 24 * 1024 * 1024] }, kind: true, timeout: 300000 },
};
const deny = (status, error, detail) => [{ json: { ok: false, _status: status, error, detail: detail || null, op: String(b.op || '') }, pairedItem: { item: 0 } }];
const op = typeof b.op === 'string' ? b.op : '';
const spec = OPS[op];
if (!spec) return deny(400, 'unknown_op');
const channel = (b.channel === 'telegram') ? 'telegram' : 'whatsapp';
if (spec.only && spec.only !== channel) return deny(400, 'op_channel_mismatch');
const pid = typeof b.profile_id === 'string' ? b.profile_id : '';
if (!spec.noProfile) {
  if (!ID.test(pid)) return deny(400, 'bad_param', 'profile_id');
  const owned = (claims.profiles || []).includes(pid);
  const pending = AUTH_OPS.has(op) && (claims.claimPending || []).includes(pid);
  if (!owned && !pending) return deny(403, 'forbidden', 'profile_id');
}
if (spec.needsBilling && !claims.appUserId) return deny(409, 'no_billing_identity');
const take = (dest, key, rule) => {
  if (rule === 'pid') { dest[key] = pid; return null; }
  const [field, kind, a, bb] = rule;
  const opt = rule.includes('opt');
  const v = b[field];
  if (v === undefined || v === null || v === '') {
    if (kind === 'int' && bb !== undefined) { dest[key] = bb; return null; }   // int rules carry a default
    if (opt) return null;
    return 'missing:' + field;
  }
  if (kind === 'int') { const n = Number.parseInt(v, 10); if (!Number.isFinite(n) || n < 0 || n > a) return 'bad:' + field; dest[key] = n; return null; }
  if (kind === 'bool') { if (v === true || v === 'true') dest[key] = 'true'; return null; }   // absent when false (byte-parity with the old queries)
  if (kind === 'text') { if (typeof v !== 'string' || v.length > a) return 'bad:' + field; dest[key] = v; return null; }
  if (kind instanceof RegExp) { if (typeof v !== 'string' || !kind.test(v)) return 'bad:' + field; dest[key] = v; return null; }
  return 'bad:' + field;
};
const query = {}, body = {};
for (const [k, rule] of Object.entries(spec.q || {})) { const e = take(query, k, rule); if (e) return deny(400, 'bad_param', e); }
for (const [k, rule] of Object.entries(spec.body || {})) { const e = take(body, k, rule); if (e) return deny(400, 'bad_param', e); }
let path = spec.path;
if (spec.kind) { const kind = String(b.kind || ''); if (!path[kind]) return deny(400, 'bad_param', 'kind'); path = path[kind]; }
else if (typeof path === 'object') path = path[channel];
return [{ json: { ok: true, _status: 200, op, channel, profileId: pid, method: spec.m, url: BASE[channel] + path, query,
  jsonBody: spec.body ? body : {}, sendBody: !!spec.body, timeout: spec.timeout || 30000 }, pairedItem: { item: 0 } }];
"""

COMPOSE_RESPONSE_JS = r"""// WAPPI PROXY / Compose Response: pass Wappi's status + JSON body through unchanged (generated)
const r = $input.first().json || {};
const status = Number(r.statusCode) || (r.error ? 502 : 200);
const body = (r.body && typeof r.body === 'object' && !Array.isArray(r.body)) ? r.body : { status: 'error', detail: r.error ? String(r.error.message || r.error) : String(r.body ?? '') };
return [{ json: { ...body, _status: status }, pairedItem: { item: 0 } }];
"""

BIND_PROFILE_SQL = """-- A1: a profile created through the proxy is OWNED by the creating install from birth.
insert into bot_profiles (profile_id, app_user_id, channel, owner_id, claimed_at)
values ($1, $2, $3, $4, now())
on conflict (profile_id) do update set owner_id = coalesce(bot_profiles.owner_id, excluded.owner_id), deleted_at = null
returning profile_id;"""

COMPOSE_ADD_JS = r"""// WAPPI PROXY / Compose Add Response: Wappi's profile/add body + a refreshed token that already
// carries the new profile, so the client can use it without a second round trip (generated)
__TOKEN__
const wap = $('Wappi Call').first().json || {};
const body = (wap.body && typeof wap.body === 'object') ? wap.body : {};
const row = $input.first() ? ($input.first().json || {}) : {};
const g = $('Auth Gate').first().json.crAuth || {};
let token = null, exp = null;
if (row.install_id) {
  exp = Math.floor(Date.now() / 1000) + __TTL__;
  token = crAuth.sign({ i: row.install_id, u: row.app_user_id || null, p: row.p || [], w: row.w || [], c: row.c || [], e: exp, n: row.token_version || 1 }, $env.CR_AUTH_SECRET);
}
return [{ json: { ...body, token, expiresAt: exp, profiles: row.p || g.profiles || [], workflows: row.w || g.workflows || [], _status: Number(wap.statusCode) || 200 }, pairedItem: { item: 0 } }];
"""


RECORD_OWNER_SEND_SQL = """-- A6 (stage 4): a message the OWNER sent from the app («Вместе» pick or a typed reply) is an
-- owner_app row in the one history and an assistant-side turn in the agent's memory (marked by
-- source, so a cancelled generation's cleanup never touches it). Runs AFTER the response.
with ev as (
  insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id)
  values ($1, $2, $3, 'owner_app', $4, $5, now(), $6)
  on conflict (profile_id, chat_id, message_id) do update set source = 'owner_app', body = excluded.body, execution_id = excluded.execution_id
  returning 1
), mem as (
  insert into n8n_chat_histories (session_id, message)
  select $1 || ':' || $2, jsonb_build_object('type', 'ai', 'content', $4, 'additional_kwargs', jsonb_build_object('source', 'owner_app', 'message_id', $3), 'response_metadata', '{}'::jsonb)
  where not exists (select 1 from n8n_chat_histories x where x.session_id = $1 || ':' || $2 and x.message->'additional_kwargs'->>'message_id' = $3)
  returning 1
)
select exists(select 1 from ev) as recorded, exists(select 1 from mem) as remembered;"""

OWNER_SEND_PARAMS = """={{ (r => { const b = r.jsonBody || {}; const rec = String(b.recipient || ''); const chat = (r.channel === 'whatsapp' && rec && !rec.includes('@')) ? rec + '@c.us' : rec;
  const text = r.op === 'media.send' ? ('[' + String(r.kind || 'file') + ']' + (b.caption ? ' ' + String(b.caption) : '')) : String(b.body || '');
  return [ r.profileId, chat, String($('Wappi Call').first().json.body.message_id || ''), text.slice(0, 4000), r.op === 'media.send' ? String(r.kind || 'file') : 'chat', $execution.id ]; })($('Build Request').first().json) }}"""


def build_proxy():
    tok = token_block()
    n, c = [], {}
    n.append(webhook("WappiProxy", [0, 300]))
    n.append(code("Auth Gate", gate_code({"profiles": [], "workflows": [], "appUser": [], "legacy": False}), [220, 300]))
    n.append(if_true("Authorized?", "={{ $json.crAuth.ok }}", [440, 300]))
    n.append(respond_fixed("Respond 401", [660, 520], {"success": False, "error": "unauthorized"}, 401))
    n.append(code("Build Request", BUILD_REQUEST_JS, [660, 200]))
    n.append(if_true("Op valid?", "={{ $json.ok }}", [880, 200]))
    n.append(respond("Respond Rejected", [1100, 420], body="={{ { success: false, error: $json.error, detail: $json.detail } }}", code_expr="={{ $json._status }}"))
    n.append(http_wappi("Wappi Call", "={{ $json.method }}", "={{ $json.url }}", [1100, 100],
                        query_expr="={{ JSON.stringify($json.query) }}", body_expr="={{ JSON.stringify($json.jsonBody || {}) }}",
                        timeout="={{ $json.timeout }}"))   # sendBody stays a literal true: an expression-valued boolean sent NO body (staging 2026-09-17)
    n.append(if_true("Profile Added?", "={{ $('Build Request').first().json.op === 'profile.add' && Number($json.statusCode) === 200 && !!($json.body && $json.body.profile_id) }}", [1320, 100]))
    n.append(postgres("Bind Profile", BIND_PROFILE_SQL,
                      "={{ [ $json.body.profile_id, $('Auth Gate').first().json.crAuth.appUserId, $('Build Request').first().json.channel, $('Auth Gate').first().json.crAuth.installId ] }}", [1540, 0]))
    n.append(postgres("Reload Ownership", RELOAD_OWNERSHIP_SQL, "={{ [ $('Auth Gate').first().json.crAuth.installId ] }}", [1760, 0]))
    n.append(code("Compose Add Response", COMPOSE_ADD_JS.replace("__TOKEN__", tok).replace("__TTL__", str(TOKEN_TTL_SEC)), [1980, 0]))
    n.append(code("Compose Response", COMPOSE_RESPONSE_JS, [1540, 200]))
    n.append(respond("Respond", [2200, 200]))
    edge(c, "Webhook", "Auth Gate"); edge(c, "Auth Gate", "Authorized?")
    edge(c, "Authorized?", "Build Request", 0); edge(c, "Authorized?", "Respond 401", 1)
    edge(c, "Build Request", "Op valid?")
    edge(c, "Op valid?", "Wappi Call", 0); edge(c, "Op valid?", "Respond Rejected", 1)
    edge(c, "Wappi Call", "Profile Added?")
    edge(c, "Profile Added?", "Bind Profile", 0); edge(c, "Profile Added?", "Compose Response", 1)
    edge(c, "Bind Profile", "Reload Ownership"); edge(c, "Reload Ownership", "Compose Add Response")
    edge(c, "Compose Add Response", "Respond"); edge(c, "Compose Response", "Respond")
    # Stage 4 (A6): after the response is on the wire, an app send lands in the one history + memory.
    n.append(if_true("Owner Send?", "={{ ['message.send', 'message.reply', 'media.send'].includes($('Build Request').first().json.op) && Number($('Wappi Call').first().json.statusCode) === 200 && !!($('Wappi Call').first().json.body && $('Wappi Call').first().json.body.message_id) }}", [2420, 200]))
    rec = postgres("Record Owner Send", RECORD_OWNER_SEND_SQL, OWNER_SEND_PARAMS, [2640, 200])
    rec["alwaysOutputData"] = True
    rec["onError"] = "continueRegularOutput"
    n.append(rec)
    edge(c, "Respond", "Owner Send?"); edge(c, "Owner Send?", "Record Owner Send", 0)
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return workflow(PROXY_ID, PROXY_NAME, n, c, settings)


def main():
    outputs = {f"{SESSION_ID}-Auth_Session.json": build_session(), f"{PROXY_ID}-Wappi_Proxy.json": build_proxy()}
    stale = []
    for fname, wf in outputs.items():
        path = os.path.join(WF, fname)
        text = json.dumps(wf, indent=2, ensure_ascii=False) + "\n"
        if "--check" in sys.argv:
            if not os.path.exists(path) or open(path, encoding="utf-8").read() != text:
                stale.append(fname)
        else:
            with open(path, "w", encoding="utf-8") as fh:
                fh.write(text)
            print(f"wrote {fname} ({len(wf['nodes'])} nodes)")
    if "--check" in sys.argv:
        print("auth workflows:", "up to date" if not stale else f"STALE {stale}")
        sys.exit(2 if stale else 0)


if __name__ == "__main__":
    main()
