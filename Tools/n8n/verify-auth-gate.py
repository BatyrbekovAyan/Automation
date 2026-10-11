#!/usr/bin/env python3
"""Structural-assert verifier for the A1 auth layer (audit stage 1). No network.

Pins, per canonical JSON:
  * every CLIENT webhook is fronted by Auth Gate -> Authorized? and its deny branch (401 / Set),
    with the gate code generated from the CURRENT auth/gate.js + token.js and the workflow's RULE;
  * nothing hangs off the Webhook node except the gate (a stray direct edge would bypass it);
  * the gate code never reads the secret from anything but $env, never logs the token;
  * Wappi Proxy: allow-list present, no legacy mode, ownership check for every op with a profile,
    executions of successful calls not stored;
  * Auth Session: bootstrap cap, secret hash compare, single-round-trip refresh, token TTL; claim scoping (V2) and the
    retired-row refusal (M4: never 'owned', no pending claim, no Wappi call — 410 profile_retired); the V3 conflict
    journal (T1, 2026-10-11: after the answer, only on billingConflict, the generator's statement, never failing the run);
  * bot templates: the callback binding condition (profile_id == webhook path);
  * Upload File validates the decoded multipart ids (session ownership, GUID fileId) and only then registers
    the upload BEFORE the ingest chain; Delete File deletes chunks and storage only when `allowed`; both decide a
    file not registered to the caller by the ONE generated proof predicate (V4: every known workflow id is the token's);
  * the Set Workflow State protected list carries the two new infra ids.

    python3 Tools/n8n/verify-auth-gate.py            # committed workflows/
    python3 Tools/n8n/verify-auth-gate.py --dir DIR  # a re-export
"""
import argparse
import importlib.util
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WF = os.path.join(HERE, "workflows")
WF = DEFAULT_WF


def load_migration():
    spec = importlib.util.spec_from_file_location("apply_auth_gate", os.path.join(HERE, "apply-auth-gate.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def load(f):
    with open(os.path.join(WF, f), encoding="utf-8") as fh:
        return json.load(fh)


def node(wf, name):
    n = next((x for x in wf["nodes"] if x["name"] == name), None)
    assert n is not None, f"node {name!r} missing"
    return n


def edges(wf, src, idx=0):
    return wf["connections"].get(src, {}).get("main", [[]])[idx] if idx < len(wf["connections"].get(src, {}).get("main", [])) else []


def check_gated(mig):
    for fname, cfg in mig.GATED.items():
        wf = load(fname)
        wh_name = cfg.get("webhook", "Webhook")
        wh = node(wf, wh_name)
        gate = node(wf, "Auth Gate")
        assert gate["type"] == "n8n-nodes-base.code", f"{fname}: Auth Gate is not a Code node"
        assert gate["parameters"]["jsCode"] == mig.gate_js(cfg["rule"]), f"{fname}: Auth Gate code is stale (re-run apply-auth-gate.py)"
        assert edges(wf, wh_name) == [{"node": "Auth Gate", "type": "main", "index": 0}], \
            f"{fname}: the webhook must feed ONLY the gate: {edges(wf, wh_name)}"
        assert edges(wf, "Auth Gate") == [{"node": "Authorized?", "type": "main", "index": 0}], f"{fname}: gate does not feed Authorized?"
        cond = node(wf, "Authorized?")["parameters"]["conditions"]["conditions"][0]
        assert cond["leftValue"] == "={{ $json.crAuth.ok }}" and cond["operator"]["operation"] == "true", f"{fname}: Authorized? condition wrong"
        last_node = wh["parameters"].get("responseMode") == "lastNode"
        deny = "Respond Unauthorized" if last_node else "Respond 401"
        assert edges(wf, "Authorized?", 1) == [{"node": deny, "type": "main", "index": 0}], f"{fname}: false-branch must end at {deny}"
        assert edges(wf, "Authorized?", 0), f"{fname}: Authorized? true-branch is empty"
        assert all(c["node"] not in ("Auth Gate", deny) for c in edges(wf, "Authorized?", 0)), f"{fname}: true-branch loops into the gate/deny"
        d = node(wf, deny)
        if last_node:
            assigns = {a["name"]: a["value"] for a in d["parameters"]["assignments"]["assignments"]}
            assert assigns.get("success") is False and assigns.get("error") == "unauthorized", f"{fname}: deny Set body wrong"
        else:
            assert d["parameters"]["options"].get("responseCode") == "={{ String($json.crAuth.reason || '').startsWith('forbidden') ? 403 : 401 }}", f"{fname}: Respond 401 code must be 403 for ownership refusals, 401 otherwise"
        js = gate["parameters"]["jsCode"]
        assert "$env.CR_AUTH_SECRET" in js and "crAuth.verify(" in js and "timingSafeEqual" in js, f"{fname}: gate lacks HMAC verification"
        assert "console.log" not in js, f"{fname}: gate must not log"
        for f in cfg["rule"].get("profiles", []) + cfg["rule"].get("workflows", []) + cfg["rule"].get("appUser", []):
            assert f'"{f}"' in js, f"{fname}: rule field {f} not in gate code"
        print(f"OK  {fname} (gate + rule {cfg['rule']})")


def check_proxy():
    wf = load("crWappiProxy0001-Wappi_Proxy.json")
    gate = node(wf, "Auth Gate")["parameters"]["jsCode"]
    assert '"legacy": false' in gate, "proxy gate must have NO legacy mode"
    br = node(wf, "Build Request")["parameters"]["jsCode"]
    for op in ("profile.add", "profile.delete", "profile.logout", "status", "qr", "auth.code", "tg.auth.phone", "tg.auth.code", "tg.auth.2fa",
               "chats.filter", "messages.get", "messages.id.get", "media.download", "message.send", "message.reply", "message.mark_read",
               "chat.delete", "message.reaction", "media.send"):
        assert f"'{op}':" in br, f"proxy lacks op {op}"
    assert "return deny(403, 'forbidden', 'profile_id')" in br, "proxy must refuse an unowned profile with 403"
    assert "if (!spec) return deny(400, 'unknown_op')" in br, "proxy must refuse unknown ops"
    assert "needsBilling" in br and "no_billing_identity" in br, "profile.add must require a billing identity"
    call = node(wf, "Wappi Call")
    assert call["parameters"]["url"] == "={{ $('Build Request').first().json.url }}" and call["parameters"]["method"] == "={{ $('Build Request').first().json.method }}", "Wappi Call not driven by Build Request"
    # R1: a pairing op on a claim-pending (not owned) profile reaches the transport only after Record Claim Pairing
    # confirmed, in the DB, that THIS install still holds the fresh claim (V2: an audit + stale-capability refusal,
    # never a proof for a bind).
    assert "const PAIR_OPS = new Set(['qr', 'auth.code', 'tg.auth.phone', 'tg.auth.code', 'tg.auth.2fa'])" in br and "claimPairing = !owned && PAIR_OPS.has(op)" in br, \
        "Build Request must flag pairing ops on a claim-pending profile"
    # V12 (2026-10-07): profile.delete / profile.logout pass the DATABASE ownership check (Profile Owner) before the
    # transport — a token up to 12 h old must not unlink a profile another install has since claimed.
    assert "const OWNER_OPS = new Set(['profile.delete', 'profile.logout'])" in br and "ownerCheck: OWNER_OPS.has(op)" in br, \
        "V12: Build Request must flag profile.delete / profile.logout for the database ownership check"
    assert edges(wf, "Op valid?", 0) == [{"node": "Owner Check?", "type": "main", "index": 0}], "V12: Op valid? must route through Owner Check?"
    assert node(wf, "Owner Check?")["parameters"]["conditions"]["conditions"][0]["leftValue"] == "={{ $json.ownerCheck === true }}", "V12: Owner Check? condition"
    assert edges(wf, "Owner Check?", 0) == [{"node": "Profile Owner", "type": "main", "index": 0}] and \
        edges(wf, "Owner Check?", 1) == [{"node": "Claim Pairing?", "type": "main", "index": 0}], "V12: Owner Check? routing"
    po = node(wf, "Profile Owner")["parameters"]["query"]
    assert "bp.owner_id = $2" in po and "i.revoked_at is null" in po and "as allowed" in po, "V12: Profile Owner must require this (unrevoked) install's row"
    assert edges(wf, "Profile Owner") == [{"node": "Profile Owned?", "type": "main", "index": 0}] and \
        edges(wf, "Profile Owned?", 0) == [{"node": "Wappi Call", "type": "main", "index": 0}] and \
        edges(wf, "Profile Owned?", 1) == [{"node": "Respond Profile Refused", "type": "main", "index": 0}], "V12: Profile Owned? routing"
    assert node(wf, "Profile Owned?")["parameters"]["conditions"]["conditions"][0]["leftValue"] == "={{ $json.allowed === true }}", "V12: Profile Owned? condition"
    assert edges(wf, "Claim Pairing?", 0) == [{"node": "Record Claim Pairing", "type": "main", "index": 0}] and edges(wf, "Claim Pairing?", 1) == [{"node": "Wappi Call", "type": "main", "index": 0}]
    assert edges(wf, "Record Claim Pairing") == [{"node": "Pairing Recorded?", "type": "main", "index": 0}]
    assert edges(wf, "Pairing Recorded?", 0) == [{"node": "Wappi Call", "type": "main", "index": 0}] and edges(wf, "Pairing Recorded?", 1) == [{"node": "Respond Claim Not Pending", "type": "main", "index": 0}]
    rq = node(wf, "Record Claim Pairing")["parameters"]["query"]
    assert "owner_id is null" in rq and "claim_pending_by = $2" in rq and "claim_pending_at > now() - interval '1 day'" in rq, "Record Claim Pairing must require this install's fresh claim on an owner-less profile"
    into_call = sorted(src for src, outs in wf["connections"].items() for out in outs.get("main", []) for c in out if c["node"] == "Wappi Call")
    assert into_call == ["Claim Pairing?", "Pairing Recorded?", "Profile Owned?"], f"Wappi Call must be reachable only past the pairing / V12 owner checks: {into_call}"
    assert call["parameters"]["options"]["response"]["response"]["fullResponse"] is True, "Wappi Call must return fullResponse"
    assert call["credentials"]["httpHeaderAuth"]["name"] == "WappiAuthToken", "Wappi Call must use the server-side Wappi credential"
    assert wf["settings"].get("saveDataSuccessExecution") == "none", "proxy must not store successful executions"
    assert edges(wf, "Webhook") == [{"node": "Auth Gate", "type": "main", "index": 0}]
    assert edges(wf, "Authorized?", 1) == [{"node": "Respond 401", "type": "main", "index": 0}]
    assert edges(wf, "Op valid?", 1) == [{"node": "Respond Rejected", "type": "main", "index": 0}]
    print("OK  crWappiProxy0001-Wappi_Proxy.json (allow-list, ownership, no legacy, no stored executions)")


def check_session():
    wf = load("crAuthSession001-Auth_Session.json")
    ci = node(wf, "Create Install")["parameters"]["query"]
    assert "interval '1 minute') < 60" in ci, "bootstrap cap missing"
    vi = node(wf, "Verify Install")["parameters"]["query"]
    assert "secret_hash = $2" in vi and "revoked_at is null" in vi, "refresh must compare the secret hash and honour revocation"
    # R2: a named billing id becomes the token's u only through billing_bindings (one install per RevenueCat id)
    for q, label in ((ci, "Create Install"), (vi, "Verify Install")):
        assert "billing_conflict" in q, f"R2: {label} must report billing_conflict"
        assert "coalesce(nullif($3, ''), app_user_id)" not in q and "nullif($3, '')," not in q.split("insert into installs")[-1][:120], f"R2: {label} must not adopt the named id directly"
    assert "insert into billing_bindings" in vi and "RCAnonymousID:" in vi, "R2: Verify Install must bind the billing id via billing_bindings"
    # V3 (2026-09-28): only a refresh (the secret proven) binds; the unauthenticated bootstrap binds nothing, and the id
    # moves only from a REVOKED holder — never from one that merely owns no live bot.
    assert "insert into billing_bindings" not in ci and "select $1, $2, null::text," in ci, "V3: bootstrap (Create Install) must bind no billing id"
    # the grant rule is pinned EXACTLY (whitespace-normalised): an added «or …» — such as the old «the holder owns no live
    # bot» — would hand the id to anyone who names it
    grant = " ".join(vi.split("on conflict (app_user_id) do update", 1)[-1].split("returning app_user_id", 1)[0].split())
    grant = grant[grant.find(" where ") + 1:] if " where " in grant else ""
    assert grant == ("where billing_bindings.install_id = excluded.install_id or not exists (select 1 from installs h "
                     "where h.install_id = billing_bindings.install_id and h.revoked_at is null)"), \
        f"V3: the billing id may move only to its own holder or from a REVOKED one (a holder without live bots keeps it): {grant!r}"
    assert "array_agg" in vi and "bot_profiles" in vi, "refresh must load ownership in the same statement"
    it = node(wf, "Issue Token")["parameters"]["jsCode"]
    assert "billingConflict: !!row.billing_conflict" in it, "R2: the client must learn billingConflict"
    assert "crAuth.sign(" in it and "$env.CR_AUTH_SECRET" in it and "12 * 3600" in it.replace("43200", "12 * 3600"), "token must be signed with the env secret and carry a 12 h TTL"
    rt = node(wf, "Route")["parameters"]["jsCode"]
    assert "sha256Hex(out.installSecret)" in rt and "randomSecret(32)" in rt, "bootstrap must mint and hash the install secret server-side"
    # V2 (2026-09-28): a pairing never binds (Wappi does not say which link session completed it). No reauth bind may
    # exist; the ONLY Auth Session statement that writes owner_id is Verify Claim Code, and that code is guarded.
    names = {n["name"] for n in wf["nodes"]}
    assert not names & {"Bind Reauth?", "Bind Reauthed Profile"}, "V2: a reauth (pairing-time) bind must not exist"
    writers = sorted(n["name"] for n in wf["nodes"] if n["type"] == "n8n-nodes-base.postgres"
                     and re.search(r"(?:\bset|,)\s+owner_id\s*=", n["parameters"].get("query", ""), re.I))
    assert writers == ["Verify Claim Code"], f"V2: only Verify Claim Code may write owner_id: {writers}"
    assert "'reauth' : 'pending'" not in node(wf, "Claim Decide")["parameters"]["jsCode"], "V2: claim.status must not decide a reauth bind"
    vcq = node(wf, "Verify Claim Code")["parameters"]["query"]
    assert "claim_code_attempts" in vcq and "claim_code_attempts < 5" in vcq and ">= 5 then null" in vcq, "V2: Verify Claim Code must count wrong answers and kill the code on the fifth"
    assert "when $5 = 'claim.start' then excluded.claim_pending_by" in node(wf, "Claim Start")["parameters"]["query"], \
        "V2: only claim.start may take a pending claim"
    assert edges(wf, "Claim Start") == [{"node": "Needs Status?", "type": "main", "index": 0}], "V2: Claim Start must feed Needs Status?"
    ns = node(wf, "Needs Status?")["parameters"]["conditions"]["conditions"][0]["leftValue"]
    assert ns == "={{ $('Route').first().json.op !== 'claim.verify' && $json.retired !== true }}", \
        "V2/M4: claim.verify must not call Wappi, nor may any op on a retired profile (Claim Start's retired)"
    # M4 (2026-10-09): a RETIRED row (deleted_at set) is never 'owned' — the token lists live rows only, so «owned» there
    # spun the client's boot loop forever. Claim Start keeps any pending claim off it and reports it; Claim Decide refuses
    # it (410 profile_retired) BEFORE the owned / verify branches, naming the reason to the row's owner only.
    csq = node(wf, "Claim Start")["parameters"]["query"]
    assert "(deleted_at is not null) as retired, deleted_reason" in csq, "M4: Claim Start must report a retired row (retired + deleted_reason)"
    for col in ("claim_pending_by", "claim_pending_at"):
        assert re.search(rf"{col} = case\s+when bot_profiles\.deleted_at is not null then bot_profiles\.{col}\s", csq), \
            f"M4: a retired row must never take a pending claim ({col}: the FIRST case branch must keep it as it is)"
    cdj = node(wf, "Claim Decide")["parameters"]["jsCode"]
    i_ret = cdj.find("else if (row.retired === true) {")
    i_owned = cdj.find("else if (row.owner_id === inst.install_id) { out.claimMethod = 'owned'; }")
    i_verify = cdj.find("else if (isVerify)")
    assert 0 <= i_ret < i_owned < i_verify, "M4: Claim Decide must refuse a retired row before the owned and verify branches"
    ret = cdj[i_ret:i_owned]
    assert "out.statusCode = 410; out.claimError = 'profile_retired';" in ret and \
        "if (row.owner_id === inst.install_id) out.retiredReason = row.deleted_reason || null;" in ret and ret.count("retiredReason") == 1, \
        "M4: a retired row answers 410 profile_retired, with the reason for the row's owner only"
    assert "if (claimInfo.retiredReason) refusal.retiredReason = claimInfo.retiredReason;" in node(wf, "Issue Token")["parameters"]["jsCode"], \
        "M4: Issue Token must pass the owner's retiredReason with the refusal"
    into_status = sorted(src for src, outs in wf["connections"].items() for i, out in enumerate(outs.get("main", [])) for c in out if c["node"] == "Claim Status")
    assert into_status == ["Needs Status?"] and edges(wf, "Needs Status?", 0) == [{"node": "Claim Status", "type": "main", "index": 0}], \
        f"V2: Claim Status must be reachable only from Needs Status? true: {into_status}"
    assert edges(wf, "Send Code?", 1) == [{"node": "Reload Ownership", "type": "main", "index": 0}], "V2: Send Code? false must reload ownership (no bind)"
    assert edges(wf, "Send Claim Code") == [{"node": "Code Sent?", "type": "main", "index": 0}] and \
        edges(wf, "Code Sent?", 1) == [{"node": "Clear Claim Code", "type": "main", "index": 0}], "V2: an unsent code must be judged and cleared"
    assert "code_send_failed" in node(wf, "Issue Token")["parameters"]["jsCode"], "V2: the client must learn that the code was not sent"
    cs_in = sorted(src for src, outs in wf["connections"].items() for out in outs.get("main", []) for c in out if c["node"] == "Claim Start")
    assert cs_in == ["Install Verified?"] and edges(wf, "Install Verified?", 0) == [{"node": "Claim Start", "type": "main", "index": 0}], \
        f"R1: Claim Start must run only for a verified install: {cs_in}"
    assert node(wf, "Send Code?")["parameters"]["conditions"]["conditions"][0]["leftValue"].startswith("={{ $json.op === 'claim.start' &&"), "R1: only claim.start may send a code"
    vc = node(wf, "Verify Claim Code")["parameters"]["query"]
    assert "claim_code_expires_at > now()" in vc and "claim_pending_by = $2" in vc and "(owner_id is null or owner_id <> $2)" in vc, \
        "claim verify must be scoped to the claiming install, expiring, and allow a proven transfer"
    check_conflict_journal(wf)
    print("OK  crAuthSession001-Auth_Session.json (cap, hash compare, single round-trip refresh, TTL, claim scoping, M4 retired refusal, V3 conflict journal)")


def load_builder():
    spec = importlib.util.spec_from_file_location("build_auth_workflows", os.path.join(HERE, "build-auth-workflows.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def check_conflict_journal(wf):
    """V3-journal (T1, follow-ups 2026-10-11): a refresh answered billingConflict leaves ONE row for support
    (billing_conflicts) — AFTER the answer (Respond → Conflict? → Log Billing Conflict, never delaying or changing the
    refresh), ONLY on that answer, with the generator's statement (holder read from billing_bindings, the claimant a live
    install other than the holder, a repeat bumps attempts), continueRegularOutput, no retries, nothing after it."""
    gen = load_builder()
    E = lambda n: {"node": n, "type": "main", "index": 0}
    srcs = lambda dst: sorted(s for s, outs in wf["connections"].items() for out in outs.get("main", []) for c in out if c["node"] == dst)
    assert edges(wf, "Respond") == [E("Conflict?")] and srcs("Conflict?") == ["Respond"], \
        f"V3-journal: the conflict journal must hang off Respond ONLY (it runs after the answer): Respond -> {edges(wf, 'Respond')}, into Conflict? {srcs('Conflict?')}"
    cj = node(wf, "Conflict?")
    conds = cj["parameters"]["conditions"]
    assert cj["type"] == "n8n-nodes-base.if" and conds["combinator"] == "and" and len(conds["conditions"]) == 1 and \
        conds["conditions"][0]["leftValue"] == "={{ $json.billingConflict === true }}" == gen.CONFLICT_EXPR and \
        conds["conditions"][0]["operator"].get("operation") == "true", \
        f"V3-journal: Conflict? must route ONLY an answer that said billingConflict: {conds['conditions']}"
    assert edges(wf, "Conflict?", 0) == [E("Log Billing Conflict")] and edges(wf, "Conflict?", 1) == [] and srcs("Log Billing Conflict") == ["Conflict?"], \
        "V3-journal: Log Billing Conflict must be reachable only from Conflict? true (no false branch)"
    log = node(wf, "Log Billing Conflict")
    q = log["parameters"]["query"]
    assert log["type"] == "n8n-nodes-base.postgres" and q == gen.LOG_BILLING_CONFLICT_SQL and \
        log["parameters"]["options"].get("queryReplacement") == gen.LOG_BILLING_CONFLICT_PARAMS, \
        "V3-journal: Log Billing Conflict is stale against build-auth-workflows.py (re-run it)"
    for frag in ("insert into billing_conflicts", "from billing_bindings b", "where b.app_user_id = $1 and b.install_id <> i.install_id",
                 "i.install_id = $2 and i.revoked_at is null", "on conflict (app_user_id, claimant_install_id) do update set",
                 "attempts = billing_conflicts.attempts + 1"):
        assert frag in q, f"V3-journal: the journal statement lost {frag!r} (holder from billing_bindings, the requested id only, a live claimant, one row per claimant)"
    assert log["parameters"]["options"]["queryReplacement"] == "={{ [ $('Route').first().json.appUserId, $json.installId ] }}", \
        "V3-journal: the journal must name the requested id (Route) and the verified install (the answer)"
    assert log.get("onError") == "continueRegularOutput" and not log.get("retryOnFail") and edges(wf, "Log Billing Conflict") == [], \
        f"V3-journal: the journal must never fail the execution, never retry, and nothing may follow it: onError={log.get('onError')} retryOnFail={log.get('retryOnFail')}"
    assert "Log Billing Conflict" not in reachable(wf, "Webhook", cut=("Respond", 0)), "V3-journal: a path reaches the journal without passing Respond"


def check_bots():
    for f in ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json"):
        wf = load(f)
        conds = node(wf, "If")["parameters"]["conditions"]
        assert conds["combinator"] == "and"
        assert any(c["leftValue"] == "={{ String($json.webhookUrl || '').split('/').pop() }}" and c["rightValue"] == "={{ $json.body.messages[0].profile_id }}"
                   for c in conds["conditions"]), f"{f}: callback binding condition missing"
        print(f"OK  {f} (callback bound to the registered profile)")


def check_files(mig):
    up = load("KoTuIlk4LMrlvnWI-Upload_File.json")
    reg = node(up, "Register Upload")
    assert reg["parameters"]["query"] == mig.REGISTER_UPLOAD_SQL and "as allowed" in reg["parameters"]["query"], "Register Upload must return the owner verdict `allowed`"
    assert reg["parameters"]["options"]["queryReplacement"] == mig.REGISTER_UPLOAD_PARAMS, "Register Upload must read the decoded values from the nodes that decoded them"
    # R3 (2026-09-27): the multipart ids are binary parts the gate cannot see — validated right after decoding
    # (ownership in session mode, GUID fileId), then the upload is registered IN SEQUENCE (n8n v1 runs siblings by
    # canvas position: a sibling registry ran LAST, after the response) and only an `allowed` verdict reaches the ingest.
    E = lambda n: {"node": n, "type": "main", "index": 0}
    assert edges(up, "Extract Telegram Workflow Id") == [E("Upload Fields Valid?")], \
        f"the decoded multipart fields must pass Upload Fields Valid? before anything else: {edges(up, 'Extract Telegram Workflow Id')}"
    cond = node(up, "Upload Fields Valid?")["parameters"]["conditions"]["conditions"]
    assert len(cond) == 1 and cond[0]["leftValue"] == mig.UPLOAD_FIELDS_VALID and cond[0]["operator"]["operation"] == "true", "Upload Fields Valid? condition is stale"
    assert edges(up, "Upload Fields Valid?", 0) == [E("Register Upload")], f"the check's true branch must go ONLY to Register Upload: {edges(up, 'Upload Fields Valid?', 0)}"
    assert edges(up, "Upload Fields Valid?", 1) == [E("Respond Upload Forbidden")], "a refused upload must end at Respond Upload Forbidden"
    assert node(up, "Respond Upload Forbidden")["parameters"]["options"].get("responseCode") == 403
    assert edges(up, "Register Upload") == [E("Upload Registered?")], "the registry verdict must gate the ingest"
    rc = node(up, "Upload Registered?")["parameters"]["conditions"]["conditions"]
    assert len(rc) == 1 and rc[0]["leftValue"] == mig.UPLOAD_REGISTERED, "Upload Registered? must test allowed === true"
    assert edges(up, "Upload Registered?", 0) == [E("Upload Item")] and edges(up, "Upload Registered?", 1) == [E("Respond Upload Refused")]
    assert node(up, "Upload Item")["parameters"]["jsCode"] == mig.UPLOAD_ITEM_JS
    assert {c["node"] for c in edges(up, "Upload Item")} >= {"Switch", "Merge"}, f"the ingest fan-out must hang off Upload Item: {edges(up, 'Upload Item')}"
    pdf = node(up, "Extract from PDF")
    assert pdf.get("onError") == "continueErrorOutput" and edges(up, "Extract from PDF", 1) == [E("Respond Unreadable File")], \
        "an unreadable PDF must answer 422, not the empty 200 n8n sends when a responseNode workflow fails"
    for src, outs in up["connections"].items():
        for out in outs.get("main", []):
            for c in out:
                if c["node"] in ("Switch", "Merge", "Store Original File", "Register Upload") and src not in ("Upload Item", "Switch", "Upload Fields Valid?", "Extract from PDF", "Extract from TXT", "Image Text"):
                    raise AssertionError(f"{src} -> {c['node']} bypasses the field check / registry chain")
    df = load("ZTqpumOpL1rNDOp6-Delete_File.json")
    dc = node(df, "Delete File Chunks")
    q = dc["parameters"]["query"]
    assert "uploaded_files" in q and 'AS "allowed"' in q and "EXISTS (SELECT 1 FROM own)" in q, "Delete File Chunks must be owner-scoped and report allowed"
    # V4 (2026-09-28): a file NOT registered to the caller is taken (Register Upload) or deleted (Delete File) only on
    # PROOF — every workflow id it is known under is the token's. ONE generated predicate in both queries (substrings
    # alone passed the pre-V4 delete, whose fallback trusted SOME own chunk); the whole queries equal the generator's.
    assert q == mig.DELETE_CHUNKS_SQL and dc["parameters"]["options"].get("queryReplacement") == mig.DELETE_CHUNKS_PARAMS, \
        "Delete File Chunks is stale against apply-auth-gate.py (re-run it)"
    assert mig.file_evidence("$1", "$4") in q and "(SELECT proof FROM file_proof)" in q, "Delete File Chunks must decide an unregistered file by file_proof"
    rq = reg["parameters"]["query"]
    assert mig.file_evidence("$1", "$7") in rq and "(select proof from file_proof)" in rq, "Register Upload must adopt an owner-less row only by file_proof"
    assert reg["parameters"]["options"]["queryReplacement"].rstrip(" }]").endswith("JSON.stringify($('Auth Gate').first().json.crAuth.workflows || [])"), \
        "Register Upload's 7th value must be the token's workflows read from Auth Gate ($json there is the last extract's output)"
    assert edges(df, "Authorized?", 0) == [{"node": "Delete File Chunks", "type": "main", "index": 0}], "storage delete must not run in parallel with the chunk delete"
    assert edges(df, "Delete File Chunks") == [{"node": "Allowed?", "type": "main", "index": 0}]
    assert {c["node"] for c in edges(df, "Allowed?", 0)} == {"Respond", "Delete Stored Original"} and edges(df, "Allowed?", 1) == [{"node": "Respond Forbidden", "type": "main", "index": 0}]
    print("OK  Upload File / Delete File (ownership registry + owner-scoped delete)")


def check_create():
    for f in ("XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json"):
        wf = load(f)
        assert edges(wf, "Restamp RAG Chunks") == [{"node": "Bind Workflow Id", "type": "main", "index": 0}]
        assert edges(wf, "Bind Workflow Id") == [{"node": "Send New Workflows Id", "type": "main", "index": 0}]
        q = node(wf, "Bind Workflow Id")["parameters"]["query"]
        assert "workflow_id = excluded.workflow_id" in q and "owner_id = coalesce(bot_profiles.owner_id, excluded.owner_id)" in q and "$1 <> '-1'" in q
        print(f"OK  {f} (workflow id + owner bound to the profile)")


def reachable(wf, start, cut=None):
    """Node names reachable from `start` along main edges, never following the edge `cut` = (src, output index)."""
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        for i, out in enumerate(wf["connections"].get(n, {}).get("main", [])):
            if cut and (n, i) == cut:
                continue
            for c in out:
                if c["node"] not in seen:
                    seen.add(c["node"])
                    todo.append(c["node"])
    return seen


def check_delete_ownership(mig):
    """V12 (iteration 2026-09-28, 2026-10-07): the two DELETING webhooks decide ownership in the database — the token's
    lists hold LIVE rows only (a retired bot's owner was refused its own delete) and live 12 h (a stale token could name a
    profile another install has since claimed). Delete Bot Files: every deleting node lies behind Owned?(0); success only
    on real counters; the stored originals go first. Set Workflow State: delete/deactivate behind Owned?(0) via Owner Item,
    activate on the token rule, the delete idempotent."""
    E = lambda n: {"node": n, "type": "main", "index": 0}
    dbf = load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json")
    for w, label, params in ((dbf, "Delete Bot Files", mig.DBF_OWNERSHIP_PARAMS),
                             (load("0DP79STI3pafXO9B-Set_Workflow_State.json"), "Set Workflow State", mig.SWS_OWNERSHIP_PARAMS)):
        do = node(w, "Delete Ownership")
        assert do["parameters"]["query"] == mig.DELETE_OWNERSHIP_SQL and do["parameters"]["options"]["queryReplacement"] == params, \
            f"V12: {label} Delete Ownership is stale against apply-auth-gate.py"
        assert do.get("onError") == "continueRegularOutput" and do.get("alwaysOutputData") is True, f"V12: {label}: a DB fault must reach Owned? as an item (503)"
        assert node(w, "Owned?")["parameters"]["conditions"]["conditions"][0]["leftValue"] == mig.OWNED, f"V12: {label} Owned? must test allowed === true"
        assert edges(w, "Delete Ownership") == [E("Owned?")] and edges(w, "Owned?", 1) == [E("Respond Owner Refused")], f"V12: {label} Owned? routing"
        rr = node(w, "Respond Owner Refused")["parameters"]
        assert rr["options"].get("responseCode") == mig.OWNER_REFUSED_CODE and rr["responseBody"] == mig.OWNER_REFUSED_BODY, \
            f"V12: {label}: a refusal is 403, a DB fault 503 (the client treats 403 as final)"
    # Delete Bot Files: the gate decides nothing about ids; every deleting node behind Owned?(0)
    assert mig.GATED["lmjYsdNcQA2IE5rl-Delete_Bot_Files.json"]["rule"] == {"legacy": True}, "V12: Delete Bot Files' gate must not decide ownership from the token"
    assert edges(dbf, "Authorized?", 0) == [E("Delete Ownership")], f"V12: Delete Bot Files' Authorized? must feed ONLY Delete Ownership: {edges(dbf, 'Authorized?', 0)}"
    assert edges(dbf, "Owned?", 0) == [E("List Bot Originals")], "V12: Delete Bot Files' Owned?(0) must feed List Bot Originals"
    deleting = {"Retire Bot Profiles", "Delete Bot Chunks", "Delete Chat Memory", "Delete Stored Original", "Respond"}
    leak = deleting & reachable(dbf, "Webhook", cut=("Owned?", 0))
    assert not leak, f"V12: {sorted(leak)} reachable without Owned?(0)"
    for n, cond_value in (("Originals Deleted?", mig.ORIGINALS_DELETED), ("Deletion Settled?", mig.DELETION_SETTLED), ("Has Original?", mig.HAS_ORIGINAL)):
        assert node(dbf, n)["parameters"]["conditions"]["conditions"][0]["leftValue"] == cond_value, f"V12: {n} condition is stale"
    assert node(dbf, "Split File Ids")["parameters"]["jsCode"] == mig.SPLIT_FILE_IDS_JS and node(dbf, "Originals Settled")["parameters"]["jsCode"] == mig.ORIGINALS_SETTLED_JS, \
        "V12: Split File Ids / Originals Settled are stale"
    assert edges(dbf, "Originals Deleted?", 0) == [E("Retire Bot Profiles")] and edges(dbf, "Originals Deleted?", 1) == [E("Respond Delete Failed")], \
        "V12: the stored originals must be settled BEFORE anything else is deleted"
    assert edges(dbf, "Delete Chat Memory") == [E("Deletion Settled?")] and edges(dbf, "Deletion Settled?", 0) == [E("Respond")] and \
        edges(dbf, "Deletion Settled?", 1) == [E("Respond Delete Failed")] and edges(dbf, "Respond") == [], \
        "V12: Delete Bot Files answers success only past Deletion Settled? (and nothing runs after the answer)"
    for n in ("Retire Bot Profiles", "Delete Bot Chunks", "Delete Chat Memory", "List Bot Originals"):
        assert node(dbf, n).get("onError") == "continueRegularOutput" and node(dbf, n).get("alwaysOutputData") is True, \
            f"V12: {n}: an error must reach the verdict as an item, never a bare execution failure"
    resp = node(dbf, "Delete Stored Original")["parameters"]["options"].get("response", {}).get("response", {})
    assert resp.get("fullResponse") is True and resp.get("neverError") is True, "V12: Delete Stored Original must report its status"
    assert node(dbf, "Respond Delete Failed")["parameters"]["options"].get("responseCode") == 503, "V12: a failed delete is 503 (the client retries)"
    # Set Workflow State
    sws = load("0DP79STI3pafXO9B-Set_Workflow_State.json")
    assert mig.GATED["0DP79STI3pafXO9B-Set_Workflow_State.json"]["rule"] == {"legacy": True}, "V12: Set Workflow State's gate must not decide ownership from the token"
    assert edges(sws, "Authorized?", 0) == [E("Ownership Route?")], f"V12: Set Workflow State's Authorized? must feed ONLY Ownership Route?: {edges(sws, 'Authorized?', 0)}"
    route = node(sws, "Ownership Route?")["parameters"]["conditions"]["conditions"][0]["leftValue"]
    assert route == mig.OWNERSHIP_ROUTE and "'activate'" not in route, "V12: only deactivate/delete may be routed to Delete Ownership (activate stays on the token rule)"
    assert node(sws, "Token Owns?")["parameters"]["conditions"]["conditions"][0]["leftValue"] == mig.TOKEN_OWNS, "V12: Token Owns? is stale"
    assert edges(sws, "Ownership Route?", 0) == [E("Delete Ownership")] and edges(sws, "Ownership Route?", 1) == [E("Token Owns?")], "V12: Ownership Route? routing"
    assert edges(sws, "Owned?", 0) == [E("Owner Item")] and edges(sws, "Owner Item") == [E("Validate")] and \
        node(sws, "Owner Item")["parameters"]["jsCode"] == mig.OWNER_ITEM_JS, "V12: Owned?(0) must hand Validate the request again (Owner Item)"
    assert edges(sws, "Token Owns?", 0) == [E("Validate")] and edges(sws, "Token Owns?", 1) == [E("Respond Forbidden")] and \
        node(sws, "Respond Forbidden")["parameters"]["options"].get("responseCode") == 403, "V12: Token Owns? routing"
    into_validate = sorted(src for src, outs in sws["connections"].items() for out in outs.get("main", []) for c in out if c["node"] == "Validate")
    assert into_validate == ["Owner Item", "Token Owns?"], f"V12: Validate reachable only past an ownership decision: {into_validate}"
    dw = node(sws, "Delete Workflow")
    r = dw["parameters"]["options"].get("response", {}).get("response", {})
    assert r.get("fullResponse") is True and r.get("neverError") is True and dw.get("onError") == "continueRegularOutput", "V12: Delete Workflow must report its status"
    assert node(sws, "Workflow Gone?")["parameters"]["conditions"]["conditions"][0]["leftValue"] == mig.WORKFLOW_GONE and \
        edges(sws, "Delete Workflow") == [E("Workflow Gone?")] and edges(sws, "Workflow Gone?", 0) == [E("Respond OK")] and \
        edges(sws, "Workflow Gone?", 1) == [E("Respond Delete Failed")], "V12: the delete must be idempotent (404 = gone) and report a failure"
    print("OK  Delete Bot Files / Set Workflow State (V12: database ownership, honest answer, idempotent delete)")


def check_protected():
    wf = load("0DP79STI3pafXO9B-Set_Workflow_State.json")
    js = node(wf, "Validate")["parameters"]["jsCode"]
    assert "'crAuthSession001'" in js and "'crWappiProxy0001'" in js, "SetWorkflowState must protect the two new infra workflows"
    print("OK  Set Workflow State (protected list carries Auth Session + Wappi Proxy)")


def main():
    global WF
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=DEFAULT_WF)
    WF = ap.parse_args().dir
    mig = load_migration()
    try:
        check_gated(mig); check_proxy(); check_session(); check_bots(); check_files(mig); check_create(); check_delete_ownership(mig); check_protected()
    except AssertionError as e:
        print(f"AUTH GATE FAIL: {e}")
        sys.exit(1)
    print("ALL AUTH GATE ASSERTS PASSED")


if __name__ == "__main__":
    main()
