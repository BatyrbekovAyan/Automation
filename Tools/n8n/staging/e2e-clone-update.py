#!/usr/bin/env python3
"""Stage 7.5 acceptance on STAGING: OLD clones are created through the REST API like Create WA/TG does,
activated, then brought to the canonical template by update-clones.py — dry-run first, then apply, then
an idempotent re-run — and finally exercised: an incoming event must be answered through the stage-3/4
chain (ledger row, one send) with the clone's webhook path and the owner's part of the prompt preserved.

Two scenarios:
  * clone_update_old_template_to_canonical — the pre-remediation WhatsApp template (git 95221844) with a
    prompt in no Create/Edit grammar: the graph is replaced, the prompt is CUSTOM and kept byte for byte.
  * clone_update_refreshes_stale_vertical_head[wa|tg] (review V10) — a vertical bot exactly as Create
    composed it on the prod canon of 2026-09-24 (git f08546f1: that template's graph, that map's auto_parts
    head, an owner tail): after the apply the graph is canonical AND the prompt is exactly what Create
    composes today for the same form (evaluated by n8n's own engine), the owner tail byte-identical, the
    PUBLISHED prompt the same; the re-run is a no-op.

    python3 Tools/n8n/staging/e2e-clone-update.py                 # all
    python3 Tools/n8n/staging/e2e-clone-update.py --only vertical  # substring filter
"""
import argparse
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
sys.path.insert(0, os.path.join(N8N, "tests"))
from e2e import (Ctx, execution, http, latest_executions, mock, mock_total, node_output, wait_executions_settle, BASE)  # noqa: E402
from harness import expr  # noqa: E402  (n8n's own expression engine — the staging install = the prod version)
_spec = importlib.util.spec_from_file_location("derive", os.path.join(HERE, "derive-staging.py"))
derive = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(derive)
_spec2 = importlib.util.spec_from_file_location("uc", os.path.join(N8N, "update-clones.py"))
uc = importlib.util.module_from_spec(_spec2)
_spec2.loader.exec_module(uc)
OLD_REF = "95221844"
PROD_REF = "f08546f1"   # the canon deployed to prod on 2026-09-24 (the epoch of the head V10 is about)
KEY = json.load(open(os.path.join(N8N, ".secrets", "staging-n8n.json")))["apiKey"]
SM_FIELD = "nodes[5].parameters.options.systemMessage"


def rest(method, path, body=None):
    return http(method, f"{BASE}/api/v1{path}", body, headers={"X-N8N-API-KEY": KEY})


def psql(sql):
    out = subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", sql], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


def at_ref(ref, fname):
    if ref is None:
        return json.load(open(os.path.join(N8N, "workflows", fname), encoding="utf-8"))
    return json.loads(subprocess.run(["git", "-C", REPO, "show", f"{ref}:Tools/n8n/workflows/{fname}"],
                                     capture_output=True, text=True, check=True).stdout)


def created_sm(channel, body, ref=None):
    """What Create at `ref` (None = today) composes for this form — its Set Fields expression, its own map, its
    template's prompt as the legacy fallback — evaluated by n8n's engine."""
    create = at_ref(ref, uc.CREATE[channel])
    vp = (uc.ip.prompts_of_workflow(create) or {}).get(body["BusinessTypeId"], "")
    e = next(a["value"] for a in uc.node(create, "Set Fields")["parameters"]["assignments"]["assignments"] if a["name"] == SM_FIELD)
    return expr(e, at_ref(ref, uc.TEMPLATES[channel]), {"Unity Webhook": {"body": body}, "Vertical Prompt": {"verticalPrompt": vp}})


def staging_clone(ref, channel, profile, prompt):
    """The template at `ref`, staging-derived (hosts → mock, credentials by name), wearing a profile and a prompt."""
    text = subprocess.run(["git", "-C", REPO, "show", f"{ref}:Tools/n8n/workflows/{uc.TEMPLATES[channel]}"], capture_output=True, text=True, check=True).stdout
    wf = json.loads(derive.derive_file(text))
    creds = derive.staging_credentials()
    missing = set()
    for n in wf["nodes"]:
        derive.remap(n, creds, missing)
    for n in wf["nodes"]:
        if n["name"] == "Webhook":
            n["parameters"]["path"] = profile
            n["webhookId"] = str(uuid.uuid4())
        if n["name"] == "AI Agent":
            n["parameters"]["options"]["systemMessage"] = prompt
    return {"name": f"STAGING OLD CLONE {profile}", "nodes": wf["nodes"], "connections": wf["connections"], "settings": {"executionOrder": "v1"}}


def updater(*args):
    return subprocess.run([sys.executable, os.path.join(N8N, "update-clones.py"), "--base", BASE, *args], capture_output=True, text=True)


def run_update(ctx, channel, ref, profile, prompt, chat, question, planned_marker):
    """Create + activate the old clone, dry-run → apply → re-run, then answer one incoming message. Returns
    (results, the workflow as read back after the apply); the clone is deleted in `cleanup`."""
    st, created = rest("POST", "/workflows", staging_clone(ref, channel, profile, prompt))
    assert st in (200, 201) and created.get("id"), f"create old clone: {st} {str(created)[:200]}"
    wid = created["id"]
    ctx.cleanup_wids.append(wid)
    st, _ = rest("POST", f"/workflows/{wid}/activate")
    assert st == 200, f"activate: {st}"
    ctx.bot(profile, channel)
    results = {"created": wid}
    dry = updater("--only", wid)
    results["dry_run"] = dry.stdout.strip().splitlines()[-1]
    assert '"would_update": 1' in results["dry_run"] and planned_marker in dry.stdout, f"dry-run did not plan the update: {dry.stdout[-600:]} {dry.stderr[-200:]}"
    results["prompt_plan"] = next((l.strip() for l in dry.stdout.splitlines() if l.strip().startswith("prompt:")), None)
    assert rest("GET", f"/workflows/{wid}")[1].get("active") is True
    ap = updater("--only", wid, "--apply", "--yes")
    results["apply"] = ap.stdout.strip().splitlines()[-1]
    results["publish"] = next((l.strip() for l in ap.stdout.splitlines() if "publish" in l and "rollback" not in l), None)
    assert '"updated": 1' in results["apply"] and "read-back: ok" in ap.stdout, f"apply failed: {ap.stdout[-600:]} {ap.stderr[-300:]}"
    again = updater("--only", wid, "--apply", "--yes")
    results["rerun"] = again.stdout.strip().splitlines()[-1]
    assert '"up_to_date": 1' in results["rerun"] and '"updated": 0' in results["rerun"], f"re-run was not a no-op: {again.stdout[-400:]}"
    got = rest("GET", f"/workflows/{wid}")[1]
    canon_here = uc.canonical(channel, BASE)   # the staging-derived template is what the updater applied here
    results["path_kept"] = uc.node(got, "Webhook")["parameters"]["path"] == profile
    results["canonical_names"] = sorted(n["name"] for n in uc.real_nodes(got)) == sorted(n["name"] for n in uc.real_nodes(canon_here))
    results["stamped"] = uc.stamp_of(got) == uc.template_stamp(canon_here)
    results["active_after"] = got.get("active")
    results["served_is_stored"] = got.get("activeVersionId") == got.get("versionId")
    # the updated clone answers through the new chain
    time.sleep(3)
    t = int(time.time())
    mid = f"old-{channel[:2]}-{ctx.run_id}"
    mock("POST", "/__mock/messages", {"profile_id": profile, "chat_id": chat, "messages": [{"id": mid, "chatId": chat, "from": chat, "body": question, "type": "chat", "time": t, "fromMe": False}]})
    since_exec = max([int(e["id"]) for e in latest_executions(wid, 0, 50)] or [0])
    since_calls = mock_total()
    st, _ = http("POST", f"{BASE}/webhook/{profile}", {"messages": [{"id": mid, "chatId": chat, "from": chat, "profile_id": profile, "body": question, "type": "chat", "time": t, "fromMe": False, "wh_type": "incoming_message"}]}, timeout=30)
    rows = wait_executions_settle(wid, since_exec, 1, 150)
    d = execution(rows[-1]["id"]) if rows else {}
    outcome = (node_output(d, "Outcome") or {}).get("outcome")
    sends = [c for c in mock("GET", f"/__mock/calls?since={since_calls}&path=message/send")[1]["calls"] if not c.get("dropped")]
    ledger = psql(f"select state || '|' || coalesce(error, '-') from reply_events where profile_id = '{profile}' and message_id = '{mid}';")
    results.update({"webhook": st, "outcome": outcome, "sends": len(sends), "ledger": ledger})
    run = ((d.get("data") or {}).get("resultData") or {}).get("runData") or {}
    errors = {n: " — ".join(str(x)[:160] for x in ((r.get("error") or {}).get("message"), (r.get("error") or {}).get("description")) if x)
              for n, rs in run.items() for r in rs if r.get("error")}
    if errors:   # diagnostics only (e.g. the model provider refusing) — the verdict below does not change
        results["node_errors"] = errors
    results["answered"] = st == 200 and outcome == "sent" and len(sends) == 1 and ledger.startswith("sent|")
    return results, got


def old_template_custom_prompt(ctx):
    """The pre-remediation graph is replaced; the prompt (no Create/Edit grammar → custom) is kept byte for byte."""
    profile = f"stgold{ctx.run_id[:8]}"
    prompt = "Business Type: Цветы\n\nТЕСТОВЫЙ ПРОМПТ СТАРОГО КЛОНА — не должен потеряться при обновлении.\n\nAbout Business: Цветы у дома\n\nProducts:\nProduct1: Роза\nProduct1 Price: 500 тг"
    chat = f"77019{int(time.time()) % 100000:05d}@c.us"
    r, got = run_update(ctx, "whatsapp", OLD_REF, profile, prompt, chat, "Роза сколько стоит?", "Claim Event")
    r["prompt_kept"] = uc.node(got, "AI Agent")["parameters"]["options"]["systemMessage"] == prompt
    r["custom_reported"] = bool(r.get("prompt_plan")) and "custom" in r["prompt_plan"]
    ok = all([r["path_kept"], r["prompt_kept"], r["custom_reported"], r["canonical_names"], r["stamped"], r["active_after"] is True, r["answered"]])
    return ok, r


def vertical_head_refreshed(ctx, channel):
    """A vertical clone of the prod epoch: graph AND head refreshed, the owner tail kept, the served version = it."""
    profile = f"stgv10{channel[:2]}{ctx.run_id[:6]}"
    form = {"BusinessType": "Автозапчасти", "BusinessTypeId": "auto_parts",
            "Prompt": "Отвечай коротко.\nAdditional Instructions: этот абзац тоже владельца — не трогать.",
            "Business": "About Business:\nАвтозапчасти у рынка «Алтын», Алматы\n\nКонтакты:\nТелефон: +7 701 000 00 00",
            "ProductsList": "Product1: Колодки 1234-ABC\nProduct1 Price: 18000", "ServicesList": "Service1: Подбор по VIN"}
    old_sm = created_sm(channel, form, PROD_REF)
    want = created_sm(channel, form)
    assert old_sm != want, "premise: the prod-epoch head differs from today's"
    chat = f"77019{int(time.time()) % 100000:05d}@c.us" if channel == "whatsapp" else f"chat-v10-{ctx.run_id}"
    r, got = run_update(ctx, channel, PROD_REF, profile, old_sm, chat, "Колодки 1234-ABC есть? Сколько стоят?", "auto_parts head behind")
    sm_now = uc.node(got, "AI Agent")["parameters"]["options"]["systemMessage"]
    pub = (got.get("activeVersion") or {})
    pub_sm = uc.system_message(pub) if pub.get("nodes") else sm_now
    mark = uc.ip.MARKER
    r["prompt_is_create_today"] = sm_now == want
    r["owner_tail_kept"] = sm_now[sm_now.index(mark):] == old_sm[old_sm.index(mark):]
    r["published_prompt_is_it"] = pub_sm == want
    r["state_after"] = uc.prompt_state(sm_now, channel)["state"]
    ok = all([r["path_kept"], r["prompt_is_create_today"], r["owner_tail_kept"], r["published_prompt_is_it"], r["state_after"] == "at_canonical",
              r["canonical_names"], r["stamped"], r["active_after"] is True, r["served_is_stored"], r["answered"]])
    return ok, r


SCENARIOS = [("clone_update_old_template_to_canonical", old_template_custom_prompt),
             ("clone_update_refreshes_stale_vertical_head[wa]", lambda c: vertical_head_refreshed(c, "whatsapp")),
             ("clone_update_refreshes_stale_vertical_head[tg]", lambda c: vertical_head_refreshed(c, "telegram"))]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    failed = 0
    for name, fn in SCENARIOS:
        if a.only and a.only not in name:
            continue
        ctx = Ctx()
        ctx.cleanup_wids = []
        try:
            ok, r = fn(ctx)
        except AssertionError as e:
            ok, r = False, {"assert": str(e)[:900]}
        finally:
            for wid in ctx.cleanup_wids:
                rest("POST", f"/workflows/{wid}/deactivate")
                rest("DELETE", f"/workflows/{wid}")
            ctx.cleanup()
        failed += 0 if ok else 1
        print(("PASS" if ok else "FAIL"), name)
        print("     ", json.dumps(r, ensure_ascii=False))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
