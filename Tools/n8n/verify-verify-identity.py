#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Structural verifier for the identity-heal webhook crVerifyIdent001 (POST /webhook/VerifyIdentity) — A3 «защита платящих»
(2026-10-14). No network. Behaviour lives in tests/test_identity_heal.py (the canonical graph walked on real SQL); this file
pins what that behaviour rests on, so a hand edit, a stale re-export or a careless generator edit fails:

  (1) the workflow IS build-verify-identity.py's output (edit the generator, never the JSON);
  (2) no node is disabled (n8n hands a disabled node's input on untouched — a disabled gate admits everyone, a disabled Heal
      answers «done» without consolidating);
  (3) the gate is TOKEN ONLY: RULE {appUser: [appUserId], appUserRequired: [appUserId], legacy: false} — no compat-window
      admission, no token without a billing id; the Webhook feeds nothing but the gate;
  (4) the recipient and the install come from the VERIFIED token (crAuth) everywhere they are used — Validate, the
      RevenueCat URL, Plan Heal, Heal's parameters — never from the request body;
  (5) RevenueCat is asked read-only (v2 GET …/customers/<the token's id>/aliases) with the «RevenueCat API» credential,
      fullResponse + neverError (a RevenueCat error is an item to judge, not a crash), ≤ 10 s, continue on error;
  (6) nothing is written unless RevenueCat answered a list (Aliases OK? true) AND Plan Heal decided to heal (Heal? true):
      Heal is unreachable with either edge cut; every refusal path answers success:false;
  (7) Heal is billing_consolidation.CONSOLIDATE_SQL(HEAL_PARAMS) byte for byte (the alias merge RevenueCat Events runs),
      recipient = the first parameter, the re-key compares owner_id with the install parameter; success is only Heal's
      positive row (verdict 'healed');
  (8) crVerifyIdent001 is a protected infra id (build-client-webhooks.PROTECTED_IDS and Set Workflow State's list);
  (9) fix round 2 (2026-10-15): the support journal — Heal's statement carries the identity_heal_log CTE (one transaction
      with the heal), Respond Refused answers 409 and is followed by `Log Refusal` ONLY (after the answer, continue on
      error, no retry, nothing after it, identity from crAuth, writes identity_heal_log and nothing else); no other node
      writes the journal.

    python3 Tools/n8n/verify-verify-identity.py              # the canonical workflows
    python3 Tools/n8n/verify-verify-identity.py --dir DIR    # another set of exports (mutation tests)
"""
import argparse
import importlib.util
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


B = _module("build_verify_identity", "build-verify-identity.py")
BC = _module("billing_consolidation", "billing_consolidation.py")
BCW = _module("build_client_webhooks", "build-client-webhooks.py")
EXPECTED = ("Webhook", "Auth Gate", "Authorized?", "Respond 401", "Validate", "Valid?", "Respond 400", "RC Aliases", "Aliases OK?",
            "Respond RC Unavailable", "Plan Heal", "Heal?", "Respond Refused", "Log Refusal", "Heal", "Shape Response", "Respond")
REFUSALS = ("Respond 401", "Respond 400", "Respond RC Unavailable", "Respond Refused")


def targets(wf, src):
    return [[e["node"] for e in (out or [])] for out in (wf["connections"].get(src) or {}).get("main", [])]


def sources(wf, dst):
    return sorted((s, i) for s, c in wf["connections"].items() for i, out in enumerate((c or {}).get("main") or [])
                  for e in (out or []) if e.get("node") == dst)


def reachable(wf, start, cut=None):
    """Nodes reachable from `start` along main edges, with the output `cut` = (node, index) removed."""
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for i, out in enumerate((wf["connections"].get(n) or {}).get("main") or []):
            if cut and (n, i) == cut:
                continue
            todo.extend(e["node"] for e in (out or []))
    return seen


def condition(wf, name):
    conds = wf_node(wf, name)["parameters"]["conditions"]["conditions"]
    return conds[0]["leftValue"] if len(conds) == 1 else None


def wf_node(wf, name):
    return next(x for x in wf["nodes"] if x.get("name") == name)


def gate_rule(js):
    m = re.search(r"^const RULE = (\{.*?\});$", js or "", re.M)
    try:
        return json.loads(m.group(1)) if m else None
    except ValueError:
        return None


def says_failure(body_expr):
    """The response body expression answers a LITERAL success:false (never a computed or a true value)."""
    vals = re.findall(r"""\bsuccess["']?\s*:\s*([^\s,}]+)""", body_expr or "")
    return bool(vals) and all(v == "false" for v in vals)


def check(wf, set_state):
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)
        return cond

    # (1) the generator's output
    need(wf == B.build(), f"{B.WF_FILE} differs from build-verify-identity.py — edit the generator and re-run it, never the JSON")
    names = [n.get("name") for n in wf.get("nodes", [])]
    for name in EXPECTED:
        need(names.count(name) == 1, f"expected exactly one node {name!r}, found {names.count(name)}")
    if any(names.count(x) != 1 for x in EXPECTED):
        return fails
    N = {n: wf_node(wf, n) for n in EXPECTED}

    # (2) nothing disabled
    for name, n in N.items():
        need(not n.get("disabled"), f"{name} is disabled — n8n hands a disabled node's input on untouched (a disabled gate admits "
                                    f"everyone, a disabled Heal answers «done» without consolidating)")

    # (3) the gate: token only, billing id required; the Webhook feeds only the gate
    wp = N["Webhook"]["parameters"]
    need(wp.get("httpMethod") == "POST" and wp.get("path") == B.PATH and wp.get("responseMode") == "responseNode",
         f"Webhook must be POST /webhook/{B.PATH} answered by its Respond nodes: {wp}")
    need(targets(wf, "Webhook") == [["Auth Gate"]], f"the Webhook must feed ONLY the Auth Gate: {targets(wf, 'Webhook')}")
    rule = gate_rule(N["Auth Gate"]["parameters"].get("jsCode"))
    need(rule is not None and rule.get("legacy") is False,
         f"Auth Gate RULE must be token-only (legacy: false) — the compat window must never admit a token-less heal: {rule}")
    need(rule is not None and rule.get("appUser") == ["appUserId"] and rule.get("appUserRequired") == ["appUserId"],
         f"Auth Gate RULE must bind appUserId to the token's billing id and require it (a token without one is refused): {rule}")
    need(N["Auth Gate"]["parameters"].get("jsCode") == B.auth.gate_code(B.RULE),
         "Auth Gate code is not the current auth/gate.js + token.js with the generator's RULE (re-run the generator)")
    need(condition(wf, "Authorized?") == "={{ $json.crAuth.ok }}" and targets(wf, "Authorized?") == [["Validate"], ["Respond 401"]],
         f"Authorized? must pass crAuth.ok to Validate and refuse into Respond 401: {targets(wf, 'Authorized?')}")

    # (4) the identity is the token's — never the body's
    vjs = N["Validate"]["parameters"].get("jsCode", "")
    need("const a = wh.crAuth || {};" in vjs and "a.appUserId" in vjs and "a.installId" in vjs
         and not re.search(r"\b(b|body|wh\.body)\.(appUserId|installId)\b", vjs),
         "Validate must read the recipient and the install from the verified token (crAuth), never from the body")
    need(targets(wf, "Valid?") == [["RC Aliases"], ["Respond 400"]] and condition(wf, "Valid?") == "={{ $json.ok }}",
         f"Valid? must lead to RC Aliases / Respond 400: {targets(wf, 'Valid?')}")
    rep = (N["Heal"]["parameters"].get("options") or {}).get("queryReplacement")
    need(rep == B.HEAL_REPLACEMENT and rep.count("$('Auth Gate').first().json.crAuth.") == 2 and "body" not in rep,
         f"Heal's queryReplacement must read the recipient and the install from crAuth (the gate's verified token), never the "
         f"body: {rep!r}")
    pjs = N["Plan Heal"]["parameters"].get("jsCode", "")
    need("const u = $('Auth Gate').first().json.crAuth.appUserId;" in pjs and not re.search(r"\.body\.appUserId\b", pjs),
         "Plan Heal must exclude the recipient by the token's id (crAuth), never a body field")

    # (5) the RevenueCat call
    rc = N["RC Aliases"]
    rp = rc["parameters"]
    need(rc.get("type") == "n8n-nodes-base.httpRequest" and rp.get("method") == "GET",
         f"RC Aliases must be a GET HTTP request (read-only): {rc.get('type')} {rp.get('method')}")
    need(rp.get("url") == B.RC_ALIASES_URL and "/v2/projects/" in rp.get("url", "") and "/aliases" in rp.get("url", "")
         and "encodeURIComponent($('Auth Gate').first().json.crAuth.appUserId)" in rp.get("url", "")
         and rp.get("url", "").startswith("={{ 'https://api.revenuecat.com/"),
         f"RC Aliases must ask RevenueCat v2 for the aliases of the TOKEN's id (crAuth, URL-encoded): {rp.get('url')!r}")
    need(rc.get("credentials") == {"httpHeaderAuth": B.RC_CRED} and rp.get("authentication") == "genericCredentialType"
         and rp.get("genericAuthType") == "httpHeaderAuth",
         f"RC Aliases must authenticate with the Header-Auth credential «RevenueCat API» {B.RC_CRED}: {rc.get('credentials')}")
    resp = ((rp.get("options") or {}).get("response") or {}).get("response") or {}
    need(resp.get("fullResponse") is True and resp.get("neverError") is True,
         f"RC Aliases must run with fullResponse + neverError — a RevenueCat error is a status to judge (Aliases OK?), never a "
         f"crash that skips the refusal: {resp}")
    need(isinstance((rp.get("options") or {}).get("timeout"), int) and 0 < rp["options"]["timeout"] <= 10000,
         f"RC Aliases timeout must be ≤ 10 s: {(rp.get('options') or {}).get('timeout')}")
    need(rc.get("onError") == "continueRegularOutput" and rc.get("alwaysOutputData") is True,
         f"RC Aliases must continue on error with output (a timeout reaches Aliases OK? and its refusal): onError="
         f"{rc.get('onError')} alwaysOutputData={rc.get('alwaysOutputData')}")
    need(sources(wf, "RC Aliases") == [("Valid?", 0)], f"RC Aliases entered from {sources(wf, 'RC Aliases')}, only Valid? true may")

    # (6) nothing written without RevenueCat's list and Plan Heal's decision; every refusal answers success:false
    need(condition(wf, "Aliases OK?") == B.ALIASES_OK_EXPR,
         f"Aliases OK? must pass ONLY a 200 carrying a list: {condition(wf, 'Aliases OK?')!r}")
    need(targets(wf, "Aliases OK?") == [["Plan Heal"], ["Respond RC Unavailable"]],
         f"Aliases OK?: true must lead to Plan Heal ONLY and false to Respond RC Unavailable ONLY: {targets(wf, 'Aliases OK?')}")
    need(condition(wf, "Heal?") == B.HEAL_GATE_EXPR and targets(wf, "Heal?") == [["Heal"], ["Respond Refused"]],
         f"Heal? must let only heal === true through to Heal, the rest to Respond Refused: {targets(wf, 'Heal?')}")
    need(sources(wf, "Heal") == [("Heal?", 0)], f"Heal entered from {sources(wf, 'Heal')} — only Heal? true may enter it")
    for gate_name, idx in (("Authorized?", 0), ("Valid?", 0), ("Aliases OK?", 0), ("Heal?", 0)):
        need("Heal" not in reachable(wf, "Webhook", cut=(gate_name, idx)),
             f"Heal is reachable without passing {gate_name} true — a refused or failed step could still write")
    need("hint_not_alias" in pjs and "if (hint && !ids.includes(hint)) return refuse('hint_not_alias');" in pjs,
         "Plan Heal must refuse a hint RevenueCat does not list (hint_not_alias) — a client's id is never consolidated on its word")
    need("if (!ids.includes(u)) return refuse('rc_mismatch');" in pjs and "const from = ids.filter((x) => x !== u);" in pjs,
         "Plan Heal must require RevenueCat's list to name the token's id and consolidate every OTHER id into it")
    for name in REFUSALS:
        need(says_failure(N[name]["parameters"].get("responseBody")),
             f"{name} must answer success:false — a refusal or a RevenueCat error is never success: "
             f"{N[name]['parameters'].get('responseBody')!r}")
    need(sources(wf, "Respond") == [("Shape Response", 0)], f"Respond (the success answer) entered from {sources(wf, 'Respond')}")

    # (7) the statement and its verdict
    hq = N["Heal"]["parameters"].get("query", "")
    need(N["Heal"].get("type") == "n8n-nodes-base.postgres" and N["Heal"]["parameters"].get("operation") == "executeQuery",
         "Heal must be a Postgres executeQuery node")
    need(hq == BC.CONSOLIDATE_SQL(B.HEAL_PARAMS) == B.HEAL_SQL,
         "Heal: query differs from billing_consolidation.CONSOLIDATE_SQL(HEAL_PARAMS) — edit the module / the generator, never the JSON")
    need(re.search(r"\bb\.owner_id = \$3::text\b", hq) and not re.search(r"\bb\.owner_id\s*(is not null|<>|!=)", hq),
         "Heal: the re-key must compare owner_id with the install parameter (only the token's install's rows follow the recipient)")
    need(re.search(r"\bb\.app_user_id <> \$1\b", hq) and re.search(r"\bb\.deleted_at is null\b", hq),
         "Heal: the re-key must touch live rows only, never a row already on the recipient")
    need(re.search(r"^\s*select 'healed'::text as verdict,", hq, re.M), "Heal's final select must carry the positive verdict 'healed'")
    need((N["Heal"].get("credentials") or {}).get("postgres") == B.auth.PG_CRED,
         f"Heal must run on the canonical Postgres credential {B.auth.PG_CRED}: {N['Heal'].get('credentials')}")
    sjs = N["Shape Response"]["parameters"].get("jsCode", "")
    need("if (r.verdict !== 'healed') return [{ json: { success: false, reason: 'db_unavailable', _status: 503 } }];" in sjs,
         "Shape Response must answer success ONLY on Heal's positive row (verdict 'healed') — Heal continues on error, so «the "
         "node continued» proves nothing")
    need(targets(wf, "Heal") == [["Shape Response"]] and targets(wf, "Shape Response") == [["Respond"]],
         f"Heal must feed Shape Response → Respond: {targets(wf, 'Heal')} / {targets(wf, 'Shape Response')}")

    # (9) the support journal and the refusal status
    need(B.HEAL_JOURNAL_CTE in hq and re.search(r"\(select id from journal\) as journal_id;\s*$", hq)
         and re.search(r"\bjournal as \(.*?insert into identity_heal_log \(install_id, to_app_user_id, aliases, consolidated, rekeyed\)\s*"
                       r"select \$3::text, \$1::text, array\(select app_user_id from alias_ids order by app_user_id\),", hq, re.S),
         "Heal: the journal CTE (insert into identity_heal_log: the install, the recipient, RevenueCat's aliases, the counts) "
         "must be part of the heal's own statement — support must SEE every heal, and a heal without its trace may not exist")
    codes = {name: (N[name]["parameters"].get("options") or {}).get("responseCode") for name in REFUSALS}
    need(codes["Respond Refused"] == B.REFUSED_STATUS == 409 and codes["Respond RC Unavailable"] == 502 and codes["Respond 400"] == 400,
         f"refusals must answer status codes monitoring can see — Respond Refused 409 (rc_mismatch / hint_not_alias / "
         f"no_aliases), Respond RC Unavailable 502, Respond 400 400: {codes}")
    lr = N["Log Refusal"]
    lq = lr["parameters"].get("query", "")
    need(targets(wf, "Respond Refused") == [["Log Refusal"]] and sources(wf, "Log Refusal") == [("Respond Refused", 0)]
         and targets(wf, "Log Refusal") in ([], [[]]),
         f"Log Refusal must run after the answer, ONLY after Respond Refused, with nothing after it: Respond Refused → "
         f"{targets(wf, 'Respond Refused')}, into Log Refusal {sources(wf, 'Log Refusal')}, after it {targets(wf, 'Log Refusal')}")
    need(lr.get("type") == "n8n-nodes-base.postgres" and lq == B.LOG_REFUSAL_SQL
         and (lr["parameters"].get("options") or {}).get("queryReplacement") == B.LOG_REFUSAL_PARAMS,
         "Log Refusal is stale against build-verify-identity.py (re-run the generator)")
    lrep = (lr["parameters"].get("options") or {}).get("queryReplacement") or ""
    need(lrep.count("$('Auth Gate').first().json.crAuth.") == 2 and "body" not in lrep,
         f"Log Refusal must journal the install and the recipient from crAuth (the gate's verified token), never the body: {lrep!r}")
    need(lr.get("onError") == "continueRegularOutput" and not lr.get("retryOnFail") and lr.get("alwaysOutputData") is True,
         f"Log Refusal must never fail or retry the execution (it runs after the answer): onError={lr.get('onError')} "
         f"retryOnFail={lr.get('retryOnFail')}")
    need("insert into identity_heal_log" in lq and not re.search(r"\b(subscribers|bot_profiles|dialog_counts)\b", lq),
         "Log Refusal may write identity_heal_log ONLY — a refusal never touches subscribers / bot_profiles")
    writers = sorted(n["name"] for n in wf["nodes"] if "identity_heal_log" in (n.get("parameters") or {}).get("query", ""))
    need(writers == ["Heal", "Log Refusal"], f"only Heal (its CTE) and Log Refusal may write the journal: {writers}")

    # (8) protected infra id
    need(B.WF_ID in BCW.PROTECTED_IDS, f"{B.WF_ID} is missing from build-client-webhooks.PROTECTED_IDS (protected infra ids)")
    vs = [n for n in set_state.get("nodes", []) if "const PROTECTED = new Set([" in (n.get("parameters") or {}).get("jsCode", "")]
    need(len(vs) == 1 and f"'{B.WF_ID}'" in vs[0]["parameters"]["jsCode"],
         f"Set Workflow State's protected list must carry {B.WF_ID} — SetWorkflowState could otherwise deactivate/delete it")
    return fails


def run(directory=None):
    d = directory or os.path.join(HERE, "workflows")
    with open(os.path.join(d, B.WF_FILE), encoding="utf-8") as fh:
        wf = json.load(fh)
    with open(os.path.join(d, B.SET_STATE), encoding="utf-8") as fh:
        st = json.load(fh)
    return check(wf, st)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir")
    a = ap.parse_args()
    fails = run(a.dir)
    for m in fails:
        print(f"VERIFY IDENTITY FAIL [{B.WF_FILE}]: {m}")
    if fails:
        print(f"VERIFY IDENTITY FAILED: {len(fails)} check(s)")
        sys.exit(1)
    print(f"OK  {B.WF_FILE} (token-only gate, identity from crAuth, RevenueCat v2 aliases read-only, nothing written without "
          f"RevenueCat's list + Plan Heal's decision, Heal = billing_consolidation.CONSOLIDATE_SQL + its journal CTE, success only on "
          f"'healed', refusals 409 + journaled after the answer, protected)")
    print("ALL VERIFY IDENTITY ASSERTS PASSED")


if __name__ == "__main__":
    main()
