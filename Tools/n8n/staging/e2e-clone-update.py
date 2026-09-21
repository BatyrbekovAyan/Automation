#!/usr/bin/env python3
"""Stage 7.5 acceptance on STAGING: an OLD clone (the pre-remediation template, git 95221844, derived
for staging) is created through the REST API like Create WA does, activated, then brought to the
canonical template by update-clones.py — dry-run first, then apply, then an idempotent re-run — and
finally exercised: an incoming event must be answered through the stage-3/4 chain (ledger row, one
send) with the clone's webhook path and prompt preserved.

    python3 Tools/n8n/staging/e2e-clone-update.py
"""
import importlib.util
import json
import os
import subprocess
import sys
import time
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
N8N = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(N8N))
sys.path.insert(0, HERE)
from e2e import (Ctx, api, execution, fixture, http, latest_executions, mock, mock_total, node_output, wait_executions_settle, BASE)  # noqa: E402
_spec = importlib.util.spec_from_file_location("derive", os.path.join(HERE, "derive-staging.py"))
derive = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(derive)
_spec2 = importlib.util.spec_from_file_location("uc", os.path.join(N8N, "update-clones.py"))
uc = importlib.util.module_from_spec(_spec2)
_spec2.loader.exec_module(uc)
OLD_REF = "95221844"
KEY = json.load(open(os.path.join(N8N, ".secrets", "staging-n8n.json")))["apiKey"]


def rest(method, path, body=None):
    return http(method, f"{BASE}/api/v1{path}", body, headers={"X-N8N-API-KEY": KEY})


def psql(sql):
    out = subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", sql], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


def main():
    ctx = Ctx()
    profile = f"stgold{ctx.run_id[:8]}"
    # 1. an OLD-shaped clone: the pre-remediation WhatsApp template, staging-derived (hosts → mock, creds by name)
    old_text = subprocess.run(["git", "-C", REPO, "show", f"{OLD_REF}:Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json"], capture_output=True, text=True, check=True).stdout
    wf = json.loads(derive.derive_file(old_text))
    creds = derive.staging_credentials()
    missing = set()
    for n in wf["nodes"]:
        derive.remap(n, creds, missing)
    prompt = "Business Type: Цветы\n\nТЕСТОВЫЙ ПРОМПТ СТАРОГО КЛОНА — не должен потеряться при обновлении.\n\nAbout Business: Цветы у дома\n\nProducts:\nProduct1: Роза\nProduct1 Price: 500 тг"
    for n in wf["nodes"]:
        if n["name"] == "Webhook":
            n["parameters"]["path"] = profile
            n["webhookId"] = str(uuid.uuid4())
        if n["name"] == "AI Agent":
            n["parameters"]["options"]["systemMessage"] = prompt
    body = {"name": f"STAGING OLD CLONE {profile}", "nodes": wf["nodes"], "connections": wf["connections"], "settings": {"executionOrder": "v1"}}
    st, created = rest("POST", "/workflows", body)
    assert st in (200, 201) and created.get("id"), f"create old clone: {st} {str(created)[:200]}"
    wid = created["id"]
    st, _ = rest("POST", f"/workflows/{wid}/activate")
    assert st == 200, f"activate: {st}"
    ctx.bot(profile)
    results = {"created": wid, "old_nodes": len(wf["nodes"])}
    try:
        # 2. dry-run names the change; apply updates; re-run is a no-op
        dry = subprocess.run([sys.executable, os.path.join(N8N, "update-clones.py"), "--base", BASE, "--only", wid], capture_output=True, text=True)
        results["dry_run"] = dry.stdout.strip().splitlines()[-1]
        assert '"would_update": 1' in results["dry_run"] and "Claim Event" in dry.stdout, f"dry-run did not plan the update: {dry.stdout[-400:]} {dry.stderr[-200:]}"
        st_before = rest("GET", f"/workflows/{wid}")[1]
        assert st_before.get("active") is True
        ap = subprocess.run([sys.executable, os.path.join(N8N, "update-clones.py"), "--base", BASE, "--only", wid, "--apply", "--yes"], capture_output=True, text=True)
        results["apply"] = ap.stdout.strip().splitlines()[-1]
        assert '"updated": 1' in results["apply"] and "read-back: ok" in ap.stdout, f"apply failed: {ap.stdout[-500:]} {ap.stderr[-300:]}"
        again = subprocess.run([sys.executable, os.path.join(N8N, "update-clones.py"), "--base", BASE, "--only", wid, "--apply", "--yes"], capture_output=True, text=True)
        results["rerun"] = again.stdout.strip().splitlines()[-1]
        assert '"up_to_date": 1' in results["rerun"] and '"updated": 0' in results["rerun"], f"re-run was not a no-op: {again.stdout[-300:]}"
        # 3. identity preserved
        got = rest("GET", f"/workflows/{wid}")[1]
        results["path_kept"] = uc.node(got, "Webhook")["parameters"]["path"] == profile
        results["prompt_kept"] = uc.node(got, "AI Agent")["parameters"]["options"]["systemMessage"] == prompt
        results["nodes_now"] = len(got["nodes"])
        canon_here = uc.canonical("whatsapp", BASE)   # the staging-derived template is what the updater applied here
        results["canonical_names"] = sorted(n["name"] for n in uc.real_nodes(got)) == sorted(n["name"] for n in uc.real_nodes(canon_here))
        results["stamped"] = uc.stamp_of(got) == uc.template_stamp(canon_here)
        results["active_after"] = got.get("active")
        # 4. the updated clone answers through the new chain
        time.sleep(3)
        chat = f"77019{int(time.time()) % 100000:05d}@c.us"
        t = int(time.time())
        mid = f"old-{ctx.run_id}"
        mock("POST", "/__mock/messages", {"profile_id": profile, "chat_id": chat, "messages": [{"id": mid, "chatId": chat, "from": chat, "body": "Роза сколько стоит?", "type": "chat", "time": t, "fromMe": False}]})
        since_exec = max([int(e["id"]) for e in latest_executions(wid, 0, 50)] or [0])
        since_calls = mock_total()
        st, _ = http("POST", f"{BASE}/webhook/{profile}", {"messages": [{"id": mid, "chatId": chat, "from": chat, "profile_id": profile, "body": "Роза сколько стоит?", "type": "chat", "time": t, "fromMe": False, "wh_type": "incoming_message"}]}, timeout=30)
        rows = wait_executions_settle(wid, since_exec, 1, 150)
        d = execution(rows[-1]["id"]) if rows else {}
        outcome = (node_output(d, "Outcome") or {}).get("outcome")
        sends = [c for c in mock("GET", f"/__mock/calls?since={since_calls}&path=message/send")[1]["calls"] if not c.get("dropped")]
        ledger = psql(f"select state from reply_events where profile_id = '{profile}' and message_id = '{mid}';")
        results.update({"webhook": st, "outcome": outcome, "sends": len(sends), "ledger": ledger})
        ok = all([results["path_kept"], results["prompt_kept"], results["canonical_names"], results["stamped"], results["active_after"] is True,
                  st == 200, outcome == "sent", len(sends) == 1, ledger == "sent"])
        print(("PASS" if ok else "FAIL"), "clone_update_old_template_to_canonical")
        print("     ", json.dumps(results, ensure_ascii=False))
        sys.exit(0 if ok else 1)
    finally:
        rest("POST", f"/workflows/{wid}/deactivate")
        rest("DELETE", f"/workflows/{wid}")
        ctx.cleanup()


if __name__ == "__main__":
    main()
