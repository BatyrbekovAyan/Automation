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

# T6 (Q1 B, follow-ups 2026-10-11): the owner's app send enters the bot's memory as «Владелец: <text>» — the mark is
# apply-history-handoff.py's constant (ONE place for the memory marks; verify-history-handoff.py pins the parity).
def _history_handoff():
    import importlib.util
    spec = importlib.util.spec_from_file_location("apply_history_handoff", os.path.join(HERE, "apply-history-handoff.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


OWNER_CONTENT_SQL = _history_handoff().OWNER_CONTENT_SQL

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

# R2 (2026-09-27): only a RevenueCat anonymous id ($RCAnonymousID: + 32 lowercase hex) is a bindable billing id
# (a device-id fallback never is). Written without `$` / regex so no placeholder syntax can ever collide with it.
RC_ID_OF_3 = ("(case when length(coalesce($3, '')) = 47 and left($3, 15) = chr(36) || 'RCAnonymousID:' "
              "and translate(substr($3, 16), '0123456789abcdef', '') = '' then $3 end)")
# The requested id is granted when it is free, already this install's, or its holder was REVOKED by the server — the
# only transfer backed by an act of the server rather than by knowing the id. V3 (2026-09-28): «the holder owns no live
# bot» is NOT a release any more — a paying owner before the first bot (or right after deleting it) lost the id to
# anyone who named it, and two bot-less installs ping-ponged it on every refresh. A lost install secret is a support
# case (revoke the dead install, the id then follows); «Удалить все данные» keeps the install. Used by Verify Install
# ONLY (a refresh proves the secret); bootstrap binds nothing. ON CONFLICT DO UPDATE ... WHERE never raises in a race
# (a unique index would, and a refresh error used to become a 401 — R11).
BILLING_BIND_CTES = """req as (
  select """ + RC_ID_OF_3 + """ as id
), bind as (
  insert into billing_bindings (app_user_id, install_id)
  select req.id, __HOLDER__ from req__FROM__ where req.id is not null
  on conflict (app_user_id) do update set install_id = excluded.install_id,
      bound_at = case when billing_bindings.install_id = excluded.install_id then billing_bindings.bound_at else now() end
  where billing_bindings.install_id = excluded.install_id
     or not exists (select 1 from installs h where h.install_id = billing_bindings.install_id and h.revoked_at is null)
  returning app_user_id
), strip as (
  update installs set app_user_id = null where app_user_id = (select app_user_id from bind) and install_id <> $1
  returning 1
)"""

# V1 (2026-09-28): an SQL comment never names a parameter as `$` + digit — pg-promise fills `$N` inside comments too,
# and bootstrap is unauthenticated (verify-sql-comments.py guards every Postgres node).
CREATE_INSTALL_SQL = """-- A1: mint an install (the client keeps installId + installSecret; only the hash is stored).
-- Bootstrap is unauthenticated, so it is capped: at most 60 new installs per minute instance-wide.
-- V3 (2026-09-28): bootstrap binds NO billing id (the third parameter is deliberately unused) — it is bound only by a
-- refresh, which proves the install secret (Verify Install). An orphaned bootstrap whose answer was lost therefore
-- never holds anyone's id; the client refreshes right after a bootstrap in the same session op.
with ok as (
  select 1 as go where (select count(*) from installs where created_at > now() - interval '1 minute') < 60
), ins as (
  insert into installs (install_id, secret_hash, app_user_id, platform, app_version)
  select $1, $2, null::text, nullif($4, ''), nullif($5, '') from ok
  returning install_id, app_user_id, token_version
)
select ins.install_id, ins.app_user_id, ins.token_version, '{}'::text[] as p, '{}'::text[] as w, '{}'::text[] as c,
  false as billing_conflict
from ins;"""

VERIFY_INSTALL_SQL = """-- A1: refresh = prove the install secret (hash compare), touch last_seen, adopt the client's current
-- billing id, and load everything this install owns in ONE round trip (each Postgres node costs
-- ~1.9 s on prod). ALWAYS exactly one row carrying verdict = 'checked' (R11, 2026-09-27): install_id is
-- NULL when the secret is wrong or the install is unknown/revoked -> 401 install_revoked. A row WITHOUT
-- the verdict (node error, input passed through, nothing) is a fault -> 503, never a reason to wipe.
-- R2/V3: the billing id is adopted only when billing_bindings grants it (free, already ours, or its holder revoked);
-- a refused one keeps the install's own still-bound id (or none) and reports billing_conflict.
with me as (
  select install_id from installs where install_id = $1 and secret_hash = $2 and revoked_at is null
), """ + BILLING_BIND_CTES.replace("__HOLDER__", "me.install_id").replace("__FROM__", ", me") + """, v as (
  update installs i set last_seen_at = now(),
    app_user_id = coalesce((select app_user_id from bind),
                           (select b.app_user_id from billing_bindings b where b.app_user_id = i.app_user_id and b.install_id = i.install_id))
  where i.install_id = $1 and i.secret_hash = $2 and i.revoked_at is null
  returning i.install_id, i.app_user_id, i.token_version
)
select 'checked'::text as verdict, v.install_id, v.app_user_id, v.token_version,
  coalesce((select array_agg(bp.profile_id) from bot_profiles bp where bp.owner_id = v.install_id and bp.deleted_at is null), '{}'::text[]) as p,
  coalesce((select array_agg(distinct bp.workflow_id) from bot_profiles bp where bp.owner_id = v.install_id and bp.deleted_at is null and bp.workflow_id is not null), '{}'::text[]) as w,
  coalesce((select array_agg(bp.profile_id) from bot_profiles bp where bp.claim_pending_by = v.install_id and bp.owner_id is null and bp.claim_pending_at > now() - interval '1 day'), '{}'::text[]) as c,
  ((select id from req) is not null and v.install_id is not null and v.app_user_id is distinct from (select id from req)) as billing_conflict
from (select 1) as one left join v on true;"""

RELOAD_OWNERSHIP_SQL = """-- A1: re-read ownership after a claim step (same shape as Verify Install's row).
select i.install_id, i.app_user_id, i.token_version,
  coalesce((select array_agg(bp.profile_id) from bot_profiles bp where bp.owner_id = i.install_id and bp.deleted_at is null), '{}'::text[]) as p,
  coalesce((select array_agg(distinct bp.workflow_id) from bot_profiles bp where bp.owner_id = i.install_id and bp.deleted_at is null and bp.workflow_id is not null), '{}'::text[]) as w,
  coalesce((select array_agg(bp.profile_id) from bot_profiles bp where bp.claim_pending_by = i.install_id and bp.owner_id is null and bp.claim_pending_at > now() - interval '1 day'), '{}'::text[]) as c
from installs i where i.install_id = $1 and i.revoked_at is null;"""

CLAIM_START_SQL = """-- A1 claim: register the intent. Legacy (unowned) profiles AND profiles owned by a previous install
-- (reinstall / new phone) are claimable — ownership is proven ONLY by the code sent into the bot's own chat
-- (V2, 2026-09-28: a pairing is a precondition that gives the claimant that chat, never a proof — Wappi does not
-- say which link session completed it). A still-valid code is reused (no re-send inside its 10-minute window),
-- which also bounds unsolicited codes. A claim lives one day.
-- V2: ONLY claim.start (the fifth parameter is the op) takes the pending claim or starts its clock; claim.status
-- and claim.verify polling leave both as they are, so another install's poll can never take a claim over.
-- M4 (2026-10-09): a RETIRED row (deleted_at set: the sweep, an unfinished app delete, a refused channel) has nothing
-- left to claim — it never takes a pending claim, and the statement reports it (retired + deleted_reason) so Claim
-- Decide refuses with 410 profile_retired before any Wappi call or code.
insert into bot_profiles (profile_id, app_user_id, channel, claim_pending_by, claim_pending_at)
values ($1, coalesce(nullif($3, ''), $2), $4, $2, now())
on conflict (profile_id) do update set
  claim_pending_by = case
      when bot_profiles.deleted_at is not null then bot_profiles.claim_pending_by
      when bot_profiles.owner_id = $2 then bot_profiles.claim_pending_by
      when $5 = 'claim.start' then excluded.claim_pending_by
      else bot_profiles.claim_pending_by end,
  -- the claim clock starts ONCE per claiming install: a repeated claim.start must not move it. A claim older
  -- than a day has expired (R1) — a new claim.start starts a new one instead of locking the install out.
  claim_pending_at = case
      when bot_profiles.deleted_at is not null then bot_profiles.claim_pending_at
      when bot_profiles.owner_id = $2 then bot_profiles.claim_pending_at
      when $5 <> 'claim.start' then bot_profiles.claim_pending_at
      when bot_profiles.claim_pending_by = $2 and bot_profiles.claim_pending_at > now() - interval '1 day' then bot_profiles.claim_pending_at
      else now() end
returning profile_id, owner_id, channel, claim_pending_by, extract(epoch from claim_pending_at)::bigint as claim_pending_epoch,
  (claim_code_hash is not null and claim_code_expires_at is not null and claim_code_expires_at > now()) as code_still_valid,
  (claim_code_hash is null and claim_code_expires_at is not null and claim_code_expires_at > now()) as code_locked,
  (deleted_at is not null) as retired, deleted_reason;"""

# V2: a fresh code starts with a clean attempt counter.
STORE_CLAIM_CODE_SQL = """update bot_profiles set claim_code_hash = $3, claim_code_expires_at = now() + interval '10 minutes', claim_code_attempts = 0
where profile_id = $1 and claim_pending_by = $2 and (claim_code_expires_at is null or claim_code_expires_at <= now())
returning profile_id;"""

# V2: the transport refused the code (or answered nothing usable): nobody holds it, so it must not be "reused" — clear it
# so the next claim.start sends a new one. Scoped to the hash this execution stored (a concurrent store is not touched).
CLEAR_CLAIM_CODE_SQL = """update bot_profiles set claim_code_hash = null, claim_code_expires_at = null, claim_code_attempts = 0
where profile_id = $1 and claim_code_hash = $2
returning profile_id;"""

MAX_CODE_ATTEMPTS = 5

VERIFY_CLAIM_CODE_SQL = """-- A1 claim by self-message code: binds ownership only for the install that started the claim, with an
-- unexpired matching code. V2 (2026-09-28): the code is the ONLY proof for every profile, so it is guarded — a wrong
-- answer from the pending install counts, and the fifth kills the code (hash cleared, the expiry kept: no new code
-- until its window ends). Exactly one row: profile_id when bound, attempts after a miss, code_dead when an earlier
-- statement had already killed it (the final select reads the snapshot from before this statement's own updates).
with hit as (
  update bot_profiles set owner_id = $2, claimed_at = now(), claim_pending_by = null, claim_pending_at = null,
    claim_code_hash = null, claim_code_expires_at = null, claim_code_attempts = 0
  where profile_id = $1 and (owner_id is null or owner_id <> $2) and claim_pending_by = $2
    and claim_code_hash = $3 and claim_code_expires_at > now() and claim_code_attempts < __MAX__
  returning profile_id
), miss as (
  update bot_profiles set claim_code_attempts = claim_code_attempts + 1,
    claim_code_hash = case when claim_code_attempts + 1 >= __MAX__ then null else claim_code_hash end
  where profile_id = $1 and not exists (select 1 from hit) and claim_pending_by = $2
    and claim_code_hash is not null and claim_code_expires_at > now()
  returning claim_code_attempts
)
select (select profile_id from hit) as profile_id, (select claim_code_attempts from miss) as attempts,
  exists (select 1 from bot_profiles d where d.profile_id = $1 and d.claim_code_hash is null
          and d.claim_code_expires_at is not null and d.claim_code_expires_at > now()) as code_dead;""".replace("__MAX__", str(MAX_CODE_ATTEMPTS))

CLAIM_DECIDE_JS = r"""// AUTH SESSION / Claim Decide: what the claim needs next (generated — see build-auth-workflows.py)
__TOKEN__
const r = $('Route').first().json;
const inst = $('Verify Install').first().json || {};
const row = $('Claim Start').first().json || {};
// V2: claim.verify never calls Wappi (the code is the proof; a status call per guess only amplified a brute force),
// so on that op the input is Claim Start's row, not a get/status response (M4: so it is for a retired row — decided
// below before anything reads the status).
const isVerify = r.op === 'claim.verify';
const st = isVerify ? {} : ($input.first().json || {});   // fullResponse of Wappi get/status
const status = (st.body && typeof st.body === 'object') ? st.body : {};
const out = { op: r.op, profileId: r.profileId, channel: r.channel, installId: inst.install_id, statusCode: 200,
  claimMethod: null, claimError: null, code: null, codeHash: null, phone: null, authorizedEpoch: null };
if (!inst.install_id) {                                // R11: 401 only on the DB's positive verdict, else a fault
  const revoked = !inst.error && inst.verdict === 'checked';
  out.statusCode = revoked ? 401 : 503; out.claimError = revoked ? 'install_revoked' : 'db_unavailable';
}
else if (row.error || !row.profile_id) { out.statusCode = 500; out.claimError = 'db_error'; }
else if (row.retired === true) {
  // M4: a retired profile has nothing to claim — never 'owned' (the token lists live rows only, so the client would ask
  // again forever), never a code or a pending claim. The reason goes to the row's OWNER only; anyone else learns no
  // more than the deletion of an id it already named.
  out.statusCode = 410; out.claimError = 'profile_retired';
  if (row.owner_id === inst.install_id) out.retiredReason = row.deleted_reason || null;
}
else if (row.owner_id === inst.install_id) { out.claimMethod = 'owned'; }
else if (isVerify) { out.claimMethod = 'code'; }       // Verify Claim Code decides (scoped, counted, expiring)
else if (st.statusCode !== 200 || typeof status.authorized !== 'boolean') { out.statusCode = 502; out.claimError = 'transport_unavailable'; }
else if (status.authorized === true) {
  // V2: on Telegram the code goes to the AUTHORISED account (account.phone — its own «Избранное»); the top-level phone
  // may be a number someone named in auth/phone. WhatsApp's get/status carries only the top-level phone.
  const acct = (r.channel === 'telegram' && status.account && typeof status.account === 'object') ? status.account.phone : null;
  const phone = String(acct || status.phone || '').replace(/\D/g, '');
  const ts = Date.parse(status.authorized_at || '') || 0;
  out.authorizedEpoch = ts ? Math.floor(ts / 1000) : null;   // informational only (V2: a pairing proves nothing)
  if (r.op === 'claim.status') {
    // R1: claim.status NEVER mints or sends a code (a stranger polling it spammed the owner's chat with codes).
    // V2: and it never binds either — an authorised profile is claimed only by claim.start + the code in its own chat.
    out.claimMethod = 'pending';
  } else if (!phone) { out.statusCode = 409; out.claimError = 'no_phone'; }
  else if (row.code_locked === true && r.op === 'claim.start') {
    out.statusCode = 429; out.claimError = 'claim_code_locked';   // V2: too many wrong answers — no new code inside its window
  }
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
  const refusal = { success: false, error: claimInfo.claimError, _status: claimInfo.statusCode || 400 };
  if (claimInfo.retiredReason) refusal.retiredReason = claimInfo.retiredReason;   // M4: profile_retired, owner only
  return [{ json: refusal, pairedItem: { item: 0 } }];
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
if (r.op === 'claim.verify' && (!claimVerify || !claimVerify.profile_id)) {
  // V2: the fifth wrong answer killed the code — say so (the client offers a new one after its window), else 403.
  const locked = !!claimVerify && (Number(claimVerify.attempts) >= __MAX__ || claimVerify.code_dead === true);
  return [{ json: { success: false, error: locked ? 'claim_code_locked' : 'claim_code_rejected', _status: locked ? 429 : 403 }, pairedItem: { item: 0 } }];
}
// V2: a freshly minted code counts as sent only when it was stored AND the transport accepted it — otherwise nobody
// holds it (Clear Claim Code removed it), and «we sent you a code» would send the owner to wait for nothing.
if (claimInfo && claimInfo.op === 'claim.start' && claimInfo.claimMethod === 'code' && !claimInfo.codeReused && claimInfo.code) {
  let stored = null, send = null;
  try { stored = $('Store Claim Code').first().json; } catch (e) { stored = null; }
  try { send = $('Send Claim Code').first().json; } catch (e) { send = null; }
  const body = (send && send.body && typeof send.body === 'object') ? send.body : {};
  const sent = !!(stored && stored.profile_id) && !!send && Number(send.statusCode) === 200 && (body.status === 'done' || !!body.message_id);
  if (!sent) return [{ json: { success: false, error: 'code_send_failed', _status: 502 }, pairedItem: { item: 0 } }];
}
const exp = r.nowSec + __TTL__;
const claims = { i: row.install_id, u: row.app_user_id || null, p: row.p || [], w: row.w || [], c: row.c || [], e: exp, n: row.token_version || 1 };
const token = crAuth.sign(claims, $env.CR_AUTH_SECRET);
// billingConflict (R2): the client named a RevenueCat id another install holds — the token carries NO billing
// identity. The client must not read u = null as "identity changed, refresh again" (AuthClient keeps the id it asked for).
const out = { success: true, _status: 200, installId: row.install_id, token, expiresAt: exp, appUserId: claims.u,
  profiles: claims.p, workflows: claims.w, claimPending: claims.c, billingConflict: !!row.billing_conflict };
if (r.op === 'bootstrap') out.installSecret = r.installSecret;
if (claimInfo) { out.claimMethod = claimInfo.claimMethod; out.claimProfileId = claimInfo.profileId; out.codeReused = !!claimInfo.codeReused; }
return [{ json: out, pairedItem: { item: 0 } }];
"""

# V3 journal (T1, follow-ups 2026-10-11; table: sql/2026-10-11-billing-conflicts.sql): the owner accepted the first-bind
# limitation on condition that support SEES every refused install (a1-deploy-runbook.md §5 — then revokes the wrong
# one). Runs AFTER Respond (each Postgres node ≈ 1.9 s on prod: the refresh is neither delayed nor changed, even when
# this statement fails — continueRegularOutput, no retry, nothing after it), only when the answer said billingConflict
# (Issue Token's verdict = Verify Install's billing_conflict on a refresh; Respond passes its input item on). The
# holder is read from billing_bindings — never from the request — and the claimant must be a live install other than
# the holder. One row per (requested id, claimant): a repeat bumps attempts + last_seen_at and records the current holder.
LOG_BILLING_CONFLICT_SQL = """-- V3 journal (T1, 2026-10-11): a refresh answered billingConflict. Parameters, in order: the requested
-- billing id, the refused install (Issue Token's verified install id). Written after the answer; never read by the flow.
insert into billing_conflicts (app_user_id, claimant_install_id, holder_install_id)
select b.app_user_id, i.install_id, b.install_id
from billing_bindings b
join installs i on i.install_id = $2 and i.revoked_at is null
where b.app_user_id = $1 and b.install_id <> i.install_id
on conflict (app_user_id, claimant_install_id) do update set
  holder_install_id = excluded.holder_install_id, last_seen_at = now(), attempts = billing_conflicts.attempts + 1
returning app_user_id, claimant_install_id, holder_install_id, attempts;"""
LOG_BILLING_CONFLICT_PARAMS = "={{ [ $('Route').first().json.appUserId, $json.installId ] }}"
CONFLICT_EXPR = "={{ $json.billingConflict === true }}"

NEEDS_STATUS_EXPR = "={{ $('Route').first().json.op !== 'claim.verify' && $json.retired !== true }}"
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
    # R1: nothing of a claim is written for an install that did not prove its secret (Claim Start used to run
    # first and let an unauthenticated call overwrite claim_pending_by / insert rows before the 401).
    n.append(if_true("Install Verified?", "={{ !!$json.install_id && !$json.error }}", [1210, 520]))
    n.append(postgres("Claim Start", CLAIM_START_SQL,
                      "={{ [ $('Route').first().json.profileId, $('Route').first().json.installId, ($input.first().json.app_user_id || ''), $('Route').first().json.channel, $('Route').first().json.op ] }}", [1320, 520]))
    # V2: claim.verify goes straight to Claim Decide — no Wappi call per guessed code.
    # M4: nor does a retired profile (Claim Start's `retired`) — there is nothing to ask the transport about, and it
    # is one call with the shared server credential per refused claim.
    n.append(if_true("Needs Status?", NEEDS_STATUS_EXPR, [1430, 520]))
    n.append(http_wappi("Claim Status", "GET", CLAIM_STATUS_URL, [1540, 420], query_expr=CLAIM_STATUS_QUERY))
    n.append(code("Claim Decide", CLAIM_DECIDE_JS.replace("__TOKEN__", tok), [1760, 520]))
    n.append(if_true("Claim Verify?", "={{ $json.op === 'claim.verify' && !$json.claimError }}", [1980, 520]))
    n.append(postgres("Verify Claim Code", VERIFY_CLAIM_CODE_SQL,
                      "={{ [ $('Route').first().json.profileId, $('Route').first().json.installId, $('Route').first().json.codeHash ] }}", [2200, 400]))
    n.append(if_true("Send Code?", "={{ $json.op === 'claim.start' && $json.claimMethod === 'code' && !$json.codeReused && !!$json.code }}", [2200, 640]))
    n.append(postgres("Store Claim Code", STORE_CLAIM_CODE_SQL,
                      "={{ [ $('Route').first().json.profileId, $('Route').first().json.installId, $('Claim Decide').first().json.codeHash ] }}", [2420, 560]))
    # V2: send only a code that was actually stored (a concurrent claim.start may have stored its own first)...
    n.append(if_true("Code Stored?", "={{ !!$json.profile_id }}", [2530, 560]))
    n.append(http_wappi("Send Claim Code", "POST", SEND_CODE_URL, [2640, 560], query_expr=SEND_CODE_QUERY, body_expr=SEND_CODE_BODY))
    # ...and count it as sent only when the transport accepted it; a refused one is cleared, never "reused".
    n.append(if_true("Code Sent?", "={{ Number($json.statusCode) === 200 && !!$json.body && ($json.body.status === 'done' || !!$json.body.message_id) }}", [2750, 560]))
    n.append(postgres("Clear Claim Code", CLEAR_CLAIM_CODE_SQL,
                      "={{ [ $('Route').first().json.profileId, $('Claim Decide').first().json.codeHash ] }}", [2860, 700]))
    n.append(postgres("Reload Ownership", RELOAD_OWNERSHIP_SQL, "={{ [ $('Route').first().json.installId ] }}", [2970, 520]))
    n.append(code("Issue Token", ISSUE_TOKEN_JS.replace("__TOKEN__", tok).replace("__TTL__", str(TOKEN_TTL_SEC)).replace("__MAX__", str(MAX_CODE_ATTEMPTS)), [3190, 320]))
    n.append(respond("Respond", [3410, 320]))
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
    edge(c, "Claim Op?", "Install Verified?", 0); edge(c, "Claim Op?", "Issue Token", 1)
    edge(c, "Install Verified?", "Claim Start", 0); edge(c, "Install Verified?", "Issue Token", 1)
    edge(c, "Claim Start", "Needs Status?")
    edge(c, "Needs Status?", "Claim Status", 0); edge(c, "Needs Status?", "Claim Decide", 1)
    edge(c, "Claim Status", "Claim Decide"); edge(c, "Claim Decide", "Claim Verify?")
    edge(c, "Claim Verify?", "Verify Claim Code", 0); edge(c, "Claim Verify?", "Send Code?", 1)
    edge(c, "Verify Claim Code", "Reload Ownership")
    # V2: a pairing never binds — no reauth bind; every non-sending path reloads ownership as it is.
    edge(c, "Send Code?", "Store Claim Code", 0); edge(c, "Send Code?", "Reload Ownership", 1)
    edge(c, "Store Claim Code", "Code Stored?")
    edge(c, "Code Stored?", "Send Claim Code", 0); edge(c, "Code Stored?", "Reload Ownership", 1)
    edge(c, "Send Claim Code", "Code Sent?")
    edge(c, "Code Sent?", "Reload Ownership", 0); edge(c, "Code Sent?", "Clear Claim Code", 1)
    edge(c, "Clear Claim Code", "Reload Ownership")
    edge(c, "Reload Ownership", "Issue Token"); edge(c, "Issue Token", "Respond")
    # V3 journal (T1): after the answer is on the wire, a refused billing id is recorded for support (no false branch).
    n.append(if_true("Conflict?", CONFLICT_EXPR, [3630, 320]))
    n.append(postgres("Log Billing Conflict", LOG_BILLING_CONFLICT_SQL, LOG_BILLING_CONFLICT_PARAMS, [3850, 320]))
    edge(c, "Respond", "Conflict?"); edge(c, "Conflict?", "Log Billing Conflict", 0)
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
// R1: the ops that START or advance a pairing. On a claim-pending (not owned) profile they run only after
// Record Claim Pairing has confirmed, in the database, that THIS install still holds the fresh claim (a `c` in a
// 12-hour token is not enough: the claim may have been taken over since). V2: the pairing is a precondition, never a
// proof of ownership — the profile is bound only by the code sent into its own chat (Auth Session claim.verify).
const PAIR_OPS = new Set(['qr', 'auth.code', 'tg.auth.phone', 'tg.auth.code', 'tg.auth.2fa']);
// V12 (iteration 2026-09-28): the ops that DESTROY a profile's link are decided by the database (Profile Owner): the
// token's list is up to 12 h old, and a profile another install has since claimed by code must not be unlinked by it.
const OWNER_OPS = new Set(['profile.delete', 'profile.logout']);
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
let claimPairing = false;
if (!spec.noProfile) {
  if (!ID.test(pid)) return deny(400, 'bad_param', 'profile_id');
  const owned = (claims.profiles || []).includes(pid);
  const pending = AUTH_OPS.has(op) && (claims.claimPending || []).includes(pid);
  if (!owned && !pending) return deny(403, 'forbidden', 'profile_id');
  claimPairing = !owned && PAIR_OPS.has(op);
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
  jsonBody: spec.body ? body : {}, sendBody: !!spec.body, timeout: spec.timeout || 30000, claimPairing, ownerCheck: OWNER_OPS.has(op) }, pairedItem: { item: 0 } }];
"""

RECORD_CLAIM_PAIRING_SQL = """-- R1: the claimant starts a pairing of a profile it is claiming. Recorded ONLY while this install holds the
-- fresh (one day) claim on a still OWNER-LESS profile — a claim taken over by another install, expired or bound
-- records nothing and the proxy refuses (claim_not_pending): a stale capability cannot pair. V2 (2026-09-28): the
-- stamp is an audit of who started a pairing and when, NOT a proof for any bind (Wappi never says which link session
-- completed a pairing); the profile is bound only by the code sent into its own chat.
update bot_profiles set claim_pair_by = $2,
  claim_pair_at = case when claim_pair_by = $2 and claim_pair_at >= claim_pending_at and claim_pair_at > now() - interval '30 minutes'
                       then claim_pair_at else now() end
where profile_id = $1 and owner_id is null and deleted_at is null
  and claim_pending_by = $2 and claim_pending_at > now() - interval '1 day'
returning profile_id;"""

PROFILE_OWNER_SQL = """-- V12 (iteration 2026-09-28): a profile.delete / profile.logout unlinks the owner's WhatsApp or Telegram, so it runs
-- only for a profile whose bot_profiles row belongs to THIS install now (retired or not) and an install that is not
-- revoked — never on the token's word alone (up to 12 h old: the profile may have been claimed by another install).
-- Read-only. Parameters, in order: profile id, install id.
select exists (select 1 from bot_profiles bp join installs i on i.install_id = bp.owner_id
               where bp.profile_id = $1 and bp.owner_id = $2 and i.revoked_at is null) as allowed;"""

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
-- T6 (Q1 B): the memory content opens with the owner mark — the model sees role + content only, and without the
-- mark the owner's agreement read as the bot's own earlier answer. The history keeps the text as sent.
with ev as (
  insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id)
  values ($1, $2, $3, 'owner_app', $4, $5, now(), $6)
  on conflict (profile_id, chat_id, message_id) do update set source = 'owner_app', body = excluded.body, execution_id = excluded.execution_id
  returning 1
), mem as (
  insert into n8n_chat_histories (session_id, message)
  select $1 || ':' || $2, jsonb_build_object('type', 'ai', 'content', __OWNER_CONTENT__, 'additional_kwargs', jsonb_build_object('source', 'owner_app', 'message_id', $3), 'response_metadata', '{}'::jsonb)
  where not exists (select 1 from n8n_chat_histories x where x.session_id = $1 || ':' || $2 and x.message->'additional_kwargs'->>'message_id' = $3)
  returning 1
)
select exists(select 1 from ev) as recorded, exists(select 1 from mem) as remembered;""".replace("__OWNER_CONTENT__", OWNER_CONTENT_SQL % "$4")

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
    # Wappi Call reads its request from Build Request (not $json): on a claim pairing it runs after Record Claim Pairing.
    n.append(http_wappi("Wappi Call", "={{ $('Build Request').first().json.method }}", "={{ $('Build Request').first().json.url }}", [1100, 100],
                        query_expr="={{ JSON.stringify($('Build Request').first().json.query) }}",
                        body_expr="={{ JSON.stringify($('Build Request').first().json.jsonBody || {}) }}",
                        timeout="={{ $('Build Request').first().json.timeout }}"))   # sendBody stays a literal true: an expression-valued boolean sent NO body (staging 2026-09-17)
    n.append(if_true("Claim Pairing?", "={{ $json.claimPairing === true }}", [990, 0]))
    n.append(postgres("Record Claim Pairing", RECORD_CLAIM_PAIRING_SQL,
                      "={{ [ $json.profileId, $('Auth Gate').first().json.crAuth.installId ] }}", [990, -200]))
    n.append(if_true("Pairing Recorded?", "={{ !!$json.profile_id }}", [1100, -200]))
    n.append(respond("Respond Claim Not Pending", [1320, -400], body="={{ { success: false, error: $json.error ? 'db_unavailable' : 'claim_not_pending' } }}",
                     code_expr="={{ $json.error ? 503 : 403 }}"))
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
    # V12: profile.delete / profile.logout pass the database ownership check first (never pairing ops, so straight on).
    n.append(if_true("Owner Check?", "={{ $json.ownerCheck === true }}", [990, 300]))
    n.append(postgres("Profile Owner", PROFILE_OWNER_SQL, "={{ [ $json.profileId, $('Auth Gate').first().json.crAuth.installId || '' ] }}", [990, 500]))
    n.append(if_true("Profile Owned?", "={{ $json.allowed === true }}", [1100, 500]))
    n.append(respond("Respond Profile Refused", [1320, 600], body="={{ { success: false, error: $json.allowed === false ? 'forbidden' : 'db_unavailable', detail: 'profile_id' } }}",
                     code_expr="={{ $json.allowed === false ? 403 : 503 }}"))
    edge(c, "Op valid?", "Owner Check?", 0); edge(c, "Op valid?", "Respond Rejected", 1)
    edge(c, "Owner Check?", "Profile Owner", 0); edge(c, "Owner Check?", "Claim Pairing?", 1)
    edge(c, "Profile Owner", "Profile Owned?")
    edge(c, "Profile Owned?", "Wappi Call", 0); edge(c, "Profile Owned?", "Respond Profile Refused", 1)
    edge(c, "Claim Pairing?", "Record Claim Pairing", 0); edge(c, "Claim Pairing?", "Wappi Call", 1)
    edge(c, "Record Claim Pairing", "Pairing Recorded?")
    edge(c, "Pairing Recorded?", "Wappi Call", 0); edge(c, "Pairing Recorded?", "Respond Claim Not Pending", 1)
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
