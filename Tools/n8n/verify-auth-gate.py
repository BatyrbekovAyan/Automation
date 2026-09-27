#!/usr/bin/env python3
"""Structural-assert verifier for the A1 auth layer (audit stage 1). No network.

Pins, per canonical JSON:
  * every CLIENT webhook is fronted by Auth Gate -> Authorized? and its deny branch (401 / Set),
    with the gate code generated from the CURRENT auth/gate.js + token.js and the workflow's RULE;
  * nothing hangs off the Webhook node except the gate (a stray direct edge would bypass it);
  * the gate code never reads the secret from anything but $env, never logs the token;
  * Wappi Proxy: allow-list present, no legacy mode, ownership check for every op with a profile,
    executions of successful calls not stored;
  * Auth Session: bootstrap cap, secret hash compare, single-round-trip refresh, token TTL;
  * bot templates: the callback binding condition (profile_id == webhook path);
  * Upload File validates the decoded multipart ids (session ownership, GUID fileId) and only then registers
    the upload BEFORE the ingest chain; Delete File deletes chunks and storage only when `allowed`;
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
    assert call["parameters"]["url"] == "={{ $json.url }}" and call["parameters"]["method"] == "={{ $json.method }}", "Wappi Call not driven by Build Request"
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
    assert "array_agg" in vi and "bot_profiles" in vi, "refresh must load ownership in the same statement"
    it = node(wf, "Issue Token")["parameters"]["jsCode"]
    assert "crAuth.sign(" in it and "$env.CR_AUTH_SECRET" in it and "12 * 3600" in it.replace("43200", "12 * 3600"), "token must be signed with the env secret and carry a 12 h TTL"
    rt = node(wf, "Route")["parameters"]["jsCode"]
    assert "sha256Hex(out.installSecret)" in rt and "randomSecret(32)" in rt, "bootstrap must mint and hash the install secret server-side"
    vc = node(wf, "Verify Claim Code")["parameters"]["query"]
    assert "claim_code_expires_at > now()" in vc and "claim_pending_by = $2" in vc and "(owner_id is null or owner_id <> $2)" in vc, \
        "claim verify must be scoped to the claiming install, expiring, and allow a proven transfer"
    print("OK  crAuthSession001-Auth_Session.json (cap, hash compare, single round-trip refresh, TTL, claim scoping)")


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
    q = node(df, "Delete File Chunks")["parameters"]["query"]
    assert "uploaded_files" in q and 'AS "allowed"' in q and "EXISTS (SELECT 1 FROM own)" in q, "Delete File Chunks must be owner-scoped and report allowed"
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
        check_gated(mig); check_proxy(); check_session(); check_bots(); check_files(mig); check_create(); check_protected()
    except AssertionError as e:
        print(f"AUTH GATE FAIL: {e}")
        sys.exit(1)
    print("ALL AUTH GATE ASSERTS PASSED")


if __name__ == "__main__":
    main()
