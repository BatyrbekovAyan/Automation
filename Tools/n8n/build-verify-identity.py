#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""A3 «защита платящих» (2026-10-14) — identity-heal after a reinstall. Spec
docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md §2.1, §3 A3, §1 (bot_profiles.app_user_id
follows the owning install's billing id).

A reinstall gets a NEW anonymous RevenueCat id; the server seeds it trial/trialing while the paid mirror (plan, period,
balance, this month's dialogs) stays under the OLD id until RevenueCat's next event (a monthly renewal — up to a month of
trial limits, and the lifecycle sweep deleting the bots created after the reinstall). The app sees the merge at once
(CustomerInfo.OriginalAppUserId != its AppUserId) and asks the server to heal; the server trusts NEITHER the client's
recipient NOR its alias:

  crVerifyIdent001-Verify_Identity.json   POST /webhook/VerifyIdentity  {appUserId, originalAppUserId?}
      Auth Gate (token only: legacy false; appUserId must be the token's billing id and may not be empty — a token
      without one is refused) → Validate (the recipient and the install are READ FROM crAuth; the original id is an
      optional HINT, a RevenueCat anonymous id) → RC Aliases (RevenueCat API v2, read-only:
      GET /v2/projects/<project>/customers/<the token's id>/aliases with the server's secret key; fullResponse +
      neverError, 10 s) → Aliases OK? (200 + a list) → Plan Heal (the list must name the token's id; a hint must be on
      it; every OTHER id on it is a source) → Heal? → Heal (ONE statement: billing_consolidation.CONSOLIDATE_SQL —
      the very alias merge RevenueCat Events runs — into the token's id, the token's install re-keying ITS live
      bot_profiles rows) → Shape Response (success only on Heal's positive row) → {success, aliases, consolidated, rekeyed}.
      Any refusal, an RevenueCat error, a missing list, a hint RevenueCat does not list → {success:false, reason} and
      NOTHING is written (Heal is reachable only through Aliases OK? true and Heal? true).
  0DP79STI3pafXO9B-Set_Workflow_State.json  crVerifyIdent001 joins the protected infra ids.

Step 0 fact-check (2026-10-15, read-only, RevenueCat sandbox customers of Block 1's device pass): v2
`GET /v2/projects/{project}/customers/{customer_id}/aliases` answers 200
{"object":"list","items":[{"object":"customer.alias","id":"$RCAnonymousID:…","created_at":<ms>}, …],"next_page":null,
"url":…} — the list INCLUDES the customer's own id; both merged ids list the same 10 aliases; `limit` up to 1000 is
accepted (2 → a next_page cursor); an unknown customer → 404 {"object":"error","type":"resource_missing",
"retryable":false,…} and creates nothing (a GET after it is still 404). v1 `GET /v1/subscribers/{id}` (documented
get-or-create) is NOT used. The secret key lives only in the n8n credential «RevenueCat API» (Header Auth
`Authorization: Bearer sk_…`), created on prod in the deploy package (A7) — with the id below, so canonical PUTs keep
resolving it — and on staging by staging/bootstrap.py (the host is rewritten to the mock by derive-staging.py).

    python3 Tools/n8n/build-verify-identity.py            # write the workflow + patch Set Workflow State (re-runnable)
    python3 Tools/n8n/build-verify-identity.py --check    # exit 2 when anything is stale
Pinned by verify-verify-identity.py (structure + mutations) and tests/test_identity_heal.py (behaviour on real SQL).
"""
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


auth = _module("build_auth", "build-auth-workflows.py")
BC = _module("billing_consolidation", "billing_consolidation.py")

WF_ID, WF_NAME = "crVerifyIdent001", "Verify Identity"
WF_FILE = f"{WF_ID}-Verify_Identity.json"
PATH = "VerifyIdentity"
SET_STATE = "0DP79STI3pafXO9B-Set_Workflow_State.json"
PROTECTED_ANCHOR = "  'crPriceSearch001'\n]);"

# Token only (legacy false): the body's appUserId must be the token's billing id and may not be empty — a token WITHOUT a
# billing identity (u = null: billingConflict, or nothing bound) is refused 403 before anything runs.
RULE = {"appUser": ["appUserId"], "appUserRequired": ["appUserId"], "legacy": False}

# RevenueCat API v2 (step 0 above). The project id is an identifier, not a credential (it is in every v2 URL RevenueCat
# answers with); the secret key never leaves the credential.
RC_HOST = "https://api.revenuecat.com"
RC_PROJECT_ID = "0f8c9529"
RC_ALIAS_LIMIT = 1000
RC_TIMEOUT_MS = 10000
RC_CRED = {"id": "crRevenueCatApi1", "name": "RevenueCat API"}
# The recipient and the install come from the gate's VERIFIED identity, never from the body — in every node that uses them.
TOKEN_U = "$('Auth Gate').first().json.crAuth.appUserId"
TOKEN_INSTALL = "$('Auth Gate').first().json.crAuth.installId"
RC_ALIASES_URL = ("={{ '" + RC_HOST + "/v2/projects/" + RC_PROJECT_ID + "/customers/' + encodeURIComponent(" + TOKEN_U
                  + ") + '/aliases?limit=" + str(RC_ALIAS_LIMIT) + "' }}")
ALIASES_OK_EXPR = "={{ Number($json.statusCode) === 200 && !!$json.body && Array.isArray($json.body.items) }}"
HEAL_GATE_EXPR = "={{ $json.heal === true }}"

VALIDATE_JS = r"""// VERIFY IDENTITY / Validate (generated by Tools/n8n/build-verify-identity.py — edit there, never the JSON). The
// recipient and the install are the VERIFIED token's (crAuth) — every later node reads them from the gate, never from the
// body (the gate already refused a body whose appUserId is not the token's billing id, and a token without one). The
// original id the client names is a HINT only: a RevenueCat anonymous id, checked against RevenueCat's own alias list in
// Plan Heal — never a recipient, never a source by itself.
const wh = $input.first().json || {};
const a = wh.crAuth || {};
const b = (wh.body && typeof wh.body === 'object') ? wh.body : {};
const RC_ID = /^\$RCAnonymousID:[0-9a-f]{32}$/;
const u = typeof a.appUserId === 'string' ? a.appUserId : '';
const install = typeof a.installId === 'string' ? a.installId : '';
if (a.ok !== true || a.mode !== 'session' || !RC_ID.test(u) || !install) {
  return [{ json: { ok: false, error: 'bad_request', detail: 'identity' } }];
}
const raw = b.originalAppUserId;
let hint = null;
if (raw !== undefined && raw !== null && raw !== '') {
  if (typeof raw !== 'string' || !RC_ID.test(raw)) return [{ json: { ok: false, error: 'bad_request', detail: 'originalAppUserId' } }];
  if (raw !== u) hint = raw;   // the token's own id is no hint: RevenueCat's list alone decides
}
return [{ json: { ok: true, hint } }];
"""

PLAN_HEAL_JS = r"""// VERIFY IDENTITY / Plan Heal (generated by Tools/n8n/build-verify-identity.py). RevenueCat's OWN list of the
// customer's ids (read with the server's secret key) decides what may be consolidated — never the client:
//   * the list must name the token's billing id itself (it describes THAT customer) — else rc_mismatch;
//   * the client's hint, when it named one, must be on the list — else hint_not_alias;
//   * every OTHER id on the list is consolidated INTO the token's id (RevenueCat Events' alias merge, run now instead of at
//     RevenueCat's next event) — none: no_aliases.
// A refusal writes nothing: Heal? lets only heal === true through.
const u = $('Auth Gate').first().json.crAuth.appUserId;
const hint = $('Validate').first().json.hint;
const body = $input.first().json.body || {};
const ids = [...new Set((Array.isArray(body.items) ? body.items : [])
  .map((x) => ((x && typeof x.id === 'string') ? x.id : ''))
  .filter((x) => x.length > 0 && x.length <= 128))];
const refuse = (reason) => [{ json: { heal: false, reason } }];
if (!ids.includes(u)) return refuse('rc_mismatch');
if (hint && !ids.includes(hint)) return refuse('hint_not_alias');
const from = ids.filter((x) => x !== u);
if (from.length === 0) return refuse('no_aliases');
return [{ json: { heal: true, fromIds: JSON.stringify(from), aliases: from.length, truncated: !!body.next_page } }];
"""

SHAPE_RESPONSE_JS = r"""// VERIFY IDENTITY / Shape Response (generated by Tools/n8n/build-verify-identity.py). Heal runs behind
// continue-on-error + alwaysOutputData, so ONLY its positive row (verdict 'healed') is a success — an empty item, the input
// passed on or an error item is a fault (503), never «done».
const r = $input.first().json || {};
const plan = $('Plan Heal').first().json || {};
if (r.verdict !== 'healed') return [{ json: { success: false, reason: 'db_unavailable', _status: 503 } }];
return [{ json: { success: true, aliases: Number(plan.aliases) || 0, consolidated: Number(r.retired_rows) || 0,
  rekeyed: Number(r.rekeyed_rows) || 0, truncated: plan.truncated === true, _status: 200 } }];
"""

# The alias merge of RevenueCat Events (billing_consolidation.py — ONE text for both callers), rendered for the heal:
# recipient = the first parameter (the token's billing id), aliases = the second (RevenueCat's list minus the recipient),
# install = the third (the token's install: only ITS live bot_profiles rows follow the recipient). No event — the plan of an
# active paid alias may be handed over (A2's carry), only to a recipient still trialing without a period.
HEAL_HEAD = """-- A3 «защита платящих» (2026-10-14): ЛЕЧЕНИЕ ЗЕРКАЛА после переустановки (вебхук VerifyIdentity, crVerifyIdent001).
-- Та же цепочка склейки, что Consolidate Aliases в RevenueCat Events (billing_consolidation.py — один текст на оба места),
-- но её запускает не событие RevenueCat, а клиент, увидевший CustomerInfo.OriginalAppUserId, отличный от своего AppUserId.
-- Параметры по порядку: биллинг-ID ТОКЕНА (получатель; из crAuth, никогда из тела запроса), JSON-массив алиасов, которые
-- сам RevenueCat перечислил для этого ID (без получателя; Plan Heal), установка токена (перепривязываются только ЕЁ живые
-- строки bot_profiles под алиасами — спека §1). Подсказка клиента не бывает ни получателем, ни источником. События нет —
-- план активного платного алиаса может переехать (как топ-ап первым событием, A2), но только получателю trialing без
-- периода. Повтор ничего не переписывает: ноль новых версий строк.
"""
HEAL_RESULT = """select 'healed'::text as verdict,
       (select count(*) from locked)::int as alias_rows,
       (select count(*) from retired)::int as retired_rows,
       (select count(*) from carried)::int as carried_rows,
       (select topup_balance from credited) as balance_after,
       (select plan from credited) as plan_after,
       (select count(*) from rekeyed)::int as rekeyed_rows;"""
HEAL_PARAMS = {"head": HEAL_HEAD, "to": "$1", "aliases": "$2", "event_plan": "null::text", "install": "$3::text",
               "result": HEAL_RESULT}
HEAL_SQL = BC.CONSOLIDATE_SQL(HEAL_PARAMS)
HEAL_REPLACEMENT = "={{ [ " + TOKEN_U + ", $json.fromIds, " + TOKEN_INSTALL + " ] }}"


def vid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "verify-identity:" + name))


def own(n):
    n["id"] = vid(n["name"])
    return n


def rc_aliases(pos):
    p = {"method": "GET", "url": RC_ALIASES_URL, "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
         "options": {"timeout": RC_TIMEOUT_MS, "response": {"response": {"fullResponse": True, "neverError": True, "responseFormat": "json"}}}}
    return {"parameters": p, "id": vid("RC Aliases"), "name": "RC Aliases", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
            "position": pos, "credentials": {"httpHeaderAuth": dict(RC_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"}


def build():
    n, c = [], {}
    wh = auth.webhook(PATH, [0, 300])
    wh["id"], wh["webhookId"] = vid("webhook:" + PATH), vid("webhookId:" + PATH)
    n.append(wh)
    n.append(own(auth.code("Auth Gate", auth.gate_code(RULE), [220, 300])))
    n.append(own(auth.if_true("Authorized?", "={{ $json.crAuth.ok }}", [440, 300])))
    n.append(own(auth.respond("Respond 401", [660, 520], body="={{ { success: false, error: 'unauthorized', reason: $json.crAuth.reason } }}",
                              code_expr="={{ String($json.crAuth.reason || '').startsWith('forbidden') ? 403 : 401 }}")))
    n.append(own(auth.code("Validate", VALIDATE_JS, [660, 200])))
    n.append(own(auth.if_true("Valid?", "={{ $json.ok }}", [880, 200])))
    n.append(own(auth.respond_fixed("Respond 400", [1100, 420], {"success": False, "error": "bad_request"}, 400)))
    n.append(rc_aliases([1100, 100]))
    n.append(own(auth.if_true("Aliases OK?", ALIASES_OK_EXPR, [1320, 100])))
    n.append(own(auth.respond_fixed("Respond RC Unavailable", [1540, 320], {"success": False, "reason": "rc_unavailable"}, 502)))
    n.append(own(auth.code("Plan Heal", PLAN_HEAL_JS, [1540, 0])))
    n.append(own(auth.if_true("Heal?", HEAL_GATE_EXPR, [1760, 0])))
    n.append(own(auth.respond("Respond Refused", [1980, 220], body="={{ { success: false, reason: $json.reason } }}", code_expr=200)))
    n.append(own(auth.postgres("Heal", HEAL_SQL, HEAL_REPLACEMENT, [1980, -100])))
    n.append(own(auth.code("Shape Response", SHAPE_RESPONSE_JS, [2200, -100])))
    n.append(own(auth.respond("Respond", [2420, -100])))
    auth.edge(c, "Webhook", "Auth Gate"); auth.edge(c, "Auth Gate", "Authorized?")
    auth.edge(c, "Authorized?", "Validate", 0); auth.edge(c, "Authorized?", "Respond 401", 1)
    auth.edge(c, "Validate", "Valid?")
    auth.edge(c, "Valid?", "RC Aliases", 0); auth.edge(c, "Valid?", "Respond 400", 1)
    auth.edge(c, "RC Aliases", "Aliases OK?")
    auth.edge(c, "Aliases OK?", "Plan Heal", 0); auth.edge(c, "Aliases OK?", "Respond RC Unavailable", 1)
    auth.edge(c, "Plan Heal", "Heal?")
    auth.edge(c, "Heal?", "Heal", 0); auth.edge(c, "Heal?", "Respond Refused", 1)
    auth.edge(c, "Heal", "Shape Response"); auth.edge(c, "Shape Response", "Respond")
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return auth.workflow(WF_ID, WF_NAME, n, c, settings)


def text():
    return json.dumps(build(), indent=2, ensure_ascii=False) + "\n"


def protected_js_patched(js):
    """Set Workflow State's Validate with crVerifyIdent001 appended to its protected list (None: nothing to change)."""
    if "const PROTECTED = new Set([" not in js or f"'{WF_ID}'" in js:
        return None
    assert js.count(PROTECTED_ANCHOR) == 1, "Set Workflow State: the protected list has an unexpected shape"
    return js.replace(PROTECTED_ANCHOR, f"  'crPriceSearch001',\n  '{WF_ID}'\n]);")


def patch_set_state(write):
    path = os.path.join(WF, SET_STATE)
    wf = json.load(open(path, encoding="utf-8"))
    changed = False
    for nd in wf["nodes"]:
        new = protected_js_patched(nd["parameters"].get("jsCode", ""))
        if new is not None:
            nd["parameters"]["jsCode"] = new
            changed = True
    if changed and write:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return changed


def stale():
    out = []
    p = os.path.join(WF, WF_FILE)
    if not os.path.exists(p) or open(p, encoding="utf-8").read() != text():
        out.append(WF_FILE)
    wf = json.load(open(os.path.join(WF, SET_STATE), encoding="utf-8"))
    if not any(f"'{WF_ID}'" in nd["parameters"].get("jsCode", "") for nd in wf["nodes"]):
        out.append(SET_STATE)
    return out


def main():
    if "--check" in sys.argv:
        s = stale()
        print("verify identity:", "OK" if not s else f"STALE {s}")
        sys.exit(2 if s else 0)
    path = os.path.join(WF, WF_FILE)
    old = open(path, encoding="utf-8").read() if os.path.exists(path) else None
    new = text()
    if old != new:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(new)
    print(f"{'wrote' if old != new else 'up to date'}  {WF_FILE} ({len(build()['nodes'])} nodes)")
    print(f"{'patched' if patch_set_state(write=True) else 'up to date'}  {SET_STATE} (protected ids)")


if __name__ == "__main__":
    main()
