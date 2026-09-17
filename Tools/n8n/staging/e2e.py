#!/usr/bin/env python3
"""STAGING end-to-end scenarios for the audit remediation (stages 0.3/0.4 → 3): real n8n nodes, the
private Postgres, the Wappi MOCK as transport. Each scenario states the CORRECT behaviour; the
ones tagged `defect` are RED on the current implementation on purpose.

    python3 Tools/n8n/staging/e2e.py                 # every scenario
    python3 Tools/n8n/staging/e2e.py --only bot_dup  # substring filter
    python3 Tools/n8n/staging/e2e.py --list

Requires: staging n8n up (bootstrap.py), pg.sh started, wappi-mock.py running, a staging WA bot
imported as --bot wa:stgwa001 (its webhook path == profile id). Fixture identities are created per
run (probe-<runid>) and deleted by that exact id at the end.
"""
import argparse
import json
import os
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
N8N_DIR = os.path.dirname(HERE)
BASE = os.environ.get("N8N_STAGING_BASE", "http://localhost:5678").rstrip("/")
MOCK = os.environ.get("WAPPI_MOCK_BASE", "http://127.0.0.1:8765").rstrip("/")
KEY = json.load(open(os.path.join(N8N_DIR, ".secrets", "staging-n8n.json"))).get("apiKey", "")
WA_PROFILE = os.environ.get("STAGING_WA_PROFILE", "stgwa001")
TG_PROFILE = os.environ.get("STAGING_TG_PROFILE", "stgtg001")
FIX = [sys.executable, os.path.join(HERE, "fixtures.py")]


def http(method, url, body=None, headers=None, timeout=60):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method,
                                 headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw


def api(path, **q):
    url = f"{BASE}/api/v1{path}" + ("?" + urllib.parse.urlencode(q) if q else "")
    return http("GET", url, headers={"X-N8N-API-KEY": KEY})


import urllib.parse  # noqa: E402  (after http helpers for readability)


def mock(method, path, body=None):
    return http(method, MOCK + path, body)


def fixture(*args):
    out = subprocess.run(FIX + list(args), capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(f"fixture {' '.join(args)} failed: {out.stderr.strip()}")
    return out.stdout.strip()


def latest_executions(workflow_id, since_id=0, limit=20):
    st, page = api("/executions", workflowId=workflow_id, limit=limit, includeData="false")
    rows = [e for e in (page or {}).get("data", []) if int(e["id"]) > since_id]
    return sorted(rows, key=lambda e: int(e["id"]))


def execution(exec_id):
    st, d = api(f"/executions/{exec_id}", includeData="true")
    return d


def node_output(exec_data, node):
    out = None
    for run in ((exec_data.get("data") or {}).get("resultData", {}).get("runData", {}).get(node, []) or []):
        for br in (run.get("data") or {}).get("main") or []:
            for it in br or []:
                out = it.get("json") or out
    return out


def ran(exec_data, node):
    return node in ((exec_data.get("data") or {}).get("resultData", {}).get("runData", {}) or {})


def workflow_id_by_name(name):
    st, page = api("/workflows", limit=100)
    for w in (page or {}).get("data", []):
        if w["name"] == name:
            return w["id"]
    return None


def wait_executions_settle(workflow_id, since_id, expect_n, timeout_s):
    """Wait until expect_n executions after since_id exist and none is running/waiting."""
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        rows = latest_executions(workflow_id, since_id)
        if len(rows) >= expect_n and all(r.get("status") not in ("running", "waiting", "new") for r in rows):
            return rows
        time.sleep(2)
    return latest_executions(workflow_id, since_id)


def send_calls(since_total=0):
    st, d = mock("GET", f"/__mock/calls?since={since_total}&path=message/send")
    return [c for c in d["calls"] if not c.get("dropped")], d["total"]


def mock_total():
    st, d = mock("GET", "/__mock/calls?since=0")
    return d["total"]


# ---------------------------------------------------------------------------------------------
class Ctx:
    def __init__(self):
        self.run_id = uuid.uuid4().hex[:12]
        self.app_user_id = None
        self.profiles = []

    def identity(self, status="trialing"):
        if not self.app_user_id:
            self.app_user_id = fixture("create-subscriber", "--status", status, "--run-id", self.run_id)
        return self.app_user_id

    def bot(self, profile_id, channel="whatsapp"):
        self.identity()
        fixture("register-profile", "--app-user-id", self.app_user_id, "--profile-id", profile_id, "--channel", channel)
        fixture("set-mode", "--profile-id", profile_id, "--suppressed", "false")
        if profile_id not in self.profiles:
            self.profiles.append(profile_id)

    def cleanup(self):
        if self.app_user_id:
            args = ["delete", "--app-user-id", self.app_user_id]
            for p in self.profiles:
                args += ["--profile-id", p]
            print("   cleanup:", fixture(*args))


def wa_event(profile_id, chat, mid, body, t, from_me=False, mtype="chat"):
    return {"messages": [{"id": mid, "chatId": chat, "from": chat, "profile_id": profile_id, "body": body,
                          "type": mtype, "time": t, "fromMe": from_me, "senderName": "client"}]}


def post_event(profile_id, event):
    return http("POST", f"{BASE}/webhook/{profile_id}", event, timeout=30)


def run_bot_scenario(ctx, name, history, trigger, post_times=1, expect_sends=1, wait_s=120, extra_history_after_post=None, suppressed=False):
    wf_id = workflow_id_by_name(f"STAGING WA {WA_PROFILE}")
    if not wf_id:
        return False, "staging WA bot not imported"
    chat = f"chat-{ctx.run_id}@c.us"
    ctx.bot(WA_PROFILE)
    fixture("set-mode", "--profile-id", WA_PROFILE, "--suppressed", "true" if suppressed else "false")
    hist = [dict(h, chatId=chat, **({"from": chat} if not h.get("fromMe") else {"from": "me"})) for h in history]
    mock("POST", "/__mock/messages", {"profile_id": WA_PROFILE, "chat_id": chat, "messages": hist})
    since_exec = max([int(e["id"]) for e in latest_executions(wf_id, 0, 50)] or [0])
    since_calls = mock_total()
    event = wa_event(WA_PROFILE, chat, trigger["id"], trigger["body"], trigger["time"])
    results = []
    threads = [threading.Thread(target=lambda: results.append(post_event(WA_PROFILE, event))) for _ in range(post_times)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    if extra_history_after_post:
        time.sleep(1.5)  # inside the 8 s debounce window
        for h in extra_history_after_post:
            mock("POST", "/__mock/incoming", {"profile_id": WA_PROFILE, "chat_id": chat, "message": dict(h, chatId=chat)})
    rows = wait_executions_settle(wf_id, since_exec, post_times, wait_s)
    sends, _ = send_calls(since_calls)
    lasts = []
    for r in rows:
        d = execution(r["id"])
        lasts.append(((d.get("data") or {}).get("resultData", {}).get("lastNodeExecuted"), d.get("status")))
    ok = len(sends) == expect_sends
    return ok, f"sends={len(sends)} (expected {expect_sends}); executions={[(r['id'], l) for r, l in zip(rows, lasts)]}; webhook={[s for s, _ in results]}"


SCENARIOS = []


def scenario(id_, title, defect=None):
    def deco(fn):
        SCENARIOS.append((id_, title, defect, fn))
        return fn
    return deco


@scenario("sr_no_identity_refused", "SuggestReplies without appUserId → refusal envelope, LLM never runs")
def sr_no_identity(ctx):
    since = max([int(e["id"]) for e in latest_executions("9PTyYcelRQI7bGDb", 0, 50)] or [0])
    payload = {"v": 1, "requestSeq": 1, "profileId": "p", "chatId": "probe@c.us", "botWaId": "-1", "botTgId": "-1",
               "channel": "whatsapp", "businessTypeId": "flowers", "businessName": "Цветы", "catalog": "• Букет — 1000 тг",
               "messages": [{"role": "client", "text": "сколько стоит букет?", "ts": 1}]}
    st, body = http("POST", f"{BASE}/webhook/SuggestReplies", payload)
    rows = wait_executions_settle("9PTyYcelRQI7bGDb", since, 1, 30)
    d = execution(rows[-1]["id"]) if rows else {}
    ok = (st == 200 and body.get("error") == "generation_failed" and not body.get("abstain") and body.get("suggestions") == []
          and rows and not ran(d, "LLM"))
    return ok, f"http={st} body={body} llm_ran={ran(d, 'LLM') if rows else 'n/a'} gate={(node_output(d, 'Gate Decision') or {}).get('gateReason') if rows else 'n/a'}"


@scenario("sr_unknown_identity_refused", "SuggestReplies with an unknown appUserId → refusal, LLM never runs")
def sr_unknown(ctx):
    since = max([int(e["id"]) for e in latest_executions("9PTyYcelRQI7bGDb", 0, 50)] or [0])
    payload = {"v": 1, "requestSeq": 1, "profileId": "p", "chatId": "probe@c.us", "botWaId": "-1", "botTgId": "-1",
               "channel": "whatsapp", "businessTypeId": "flowers", "businessName": "Цветы", "catalog": "• Букет — 1000 тг",
               "appUserId": f"probe-unknown-{ctx.run_id}",
               "messages": [{"role": "client", "text": "сколько стоит букет?", "ts": 1}]}
    st, body = http("POST", f"{BASE}/webhook/SuggestReplies", payload)
    rows = wait_executions_settle("9PTyYcelRQI7bGDb", since, 1, 30)
    d = execution(rows[-1]["id"]) if rows else {}
    ok = st == 200 and body.get("error") == "generation_failed" and rows and not ran(d, "LLM")
    return ok, f"http={st} body={body} gate={(node_output(d, 'Gate Decision') or {}).get('gateReason') if rows else 'n/a'}"


@scenario("sr_identity_gets_cards", "SuggestReplies with a fixture subscriber → 1–4 cards (real LLM call)")
def sr_cards(ctx):
    uid = ctx.identity()
    payload = {"v": 1, "requestSeq": 1, "profileId": "p", "chatId": "probe@c.us", "botWaId": "-1", "botTgId": "-1",
               "channel": "whatsapp", "businessTypeId": "flowers", "businessName": "Цветы Астана", "catalog": "• Букет 25 роз — 25000 тг",
               "appUserId": uid, "messages": [{"role": "client", "text": "сколько стоит букет из 25 роз?", "ts": 1}]}
    st, body = http("POST", f"{BASE}/webhook/SuggestReplies", payload, timeout=90)
    cards = (body or {}).get("suggestions") or []
    ok = st == 200 and 1 <= len(cards) <= 4 and not body.get("error")
    return ok, f"http={st} cards={len(cards)} error={body.get('error')} first={cards[0]['text'][:60] if cards else None}"


@scenario("setreplymode_invalid_is_4xx", "SetReplyMode with an invalid body must answer a 4xx, not 200 + success:false", defect="A2 (2026-09-16: HTTP 200 success:false)")
def srm_invalid(ctx):
    st, body = http("POST", f"{BASE}/webhook/SetReplyMode", {})
    ok = 400 <= st < 500 and isinstance(body, dict) and body.get("success") is False
    return ok, f"http={st} body={body}"


@scenario("setreplymode_valid_writes_row", "SetReplyMode with a valid body → success:true, written:1, row present")
def srm_valid(ctx):
    pid = f"stg-srm-{ctx.run_id}"
    ctx.profiles.append(pid)
    st, body = http("POST", f"{BASE}/webhook/SetReplyMode", {"profileIds": [pid], "chatId": "*", "suppressed": True})
    row = subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", f"select suppressed from reply_mode_flags where profile_id = '{pid}' and chat_id = '*';"], capture_output=True, text=True).stdout.strip()
    ok = st == 200 and body.get("success") is True and body.get("written") == 1 and row == "t"
    return ok, f"http={st} body={body} row={row!r}"


@scenario("bot_single_incoming_sends_once", "one incoming text → exactly one send on the mock transport")
def bot_single(ctx):
    t = int(time.time())
    return run_bot_scenario(ctx, "single", [{"id": f"a-{ctx.run_id}", "body": "Сколько стоит букет?", "time": t, "fromMe": False, "type": "chat"}],
                            {"id": f"a-{ctx.run_id}", "body": "Сколько стоит букет?", "time": t}, post_times=1, expect_sends=1)


@scenario("bot_duplicate_delivery_sends_once", "the SAME event delivered twice → still exactly one send", defect="A4 (2026-09-16: both deliveries proceed)")
def bot_dup(ctx):
    t = int(time.time())
    return run_bot_scenario(ctx, "dup", [{"id": f"d-{ctx.run_id}", "body": "Есть пионы?", "time": t, "fromMe": False, "type": "chat"}],
                            {"id": f"d-{ctx.run_id}", "body": "Есть пионы?", "time": t}, post_times=2, expect_sends=1)


@scenario("bot_human_reply_during_debounce_aborts", "the owner answers inside the debounce window → no auto-send", defect="A3 (2026-09-16: manual reply does not abort)")
def bot_human(ctx):
    t = int(time.time())
    return run_bot_scenario(ctx, "human", [{"id": f"h-{ctx.run_id}", "body": "Вы работаете?", "time": t, "fromMe": False, "type": "chat"}],
                            {"id": f"h-{ctx.run_id}", "body": "Вы работаете?", "time": t}, post_times=1, expect_sends=0,
                            extra_history_after_post=[{"id": f"own-{ctx.run_id}", "body": "Да, до 20:00", "time": t + 1, "fromMe": True, "type": "chat", "from": "me"}])


@scenario("bot_newer_incoming_supersedes", "a newer client message inside the window → the old fragment does not send by itself")
def bot_newer(ctx):
    t = int(time.time())
    # Only the OLD fragment's webhook is posted; the newer row appears in the fetch → abort. (The newer
    # fragment's own webhook, which would send, is deliberately not posted here.)
    return run_bot_scenario(ctx, "newer", [{"id": f"n1-{ctx.run_id}", "body": "Привет", "time": t, "fromMe": False, "type": "chat"}],
                            {"id": f"n1-{ctx.run_id}", "body": "Привет", "time": t}, post_times=1, expect_sends=0,
                            extra_history_after_post=[{"id": f"n2-{ctx.run_id}", "body": "Сколько стоит?", "time": t + 1, "fromMe": False, "type": "chat"}])


@scenario("bot_suppressed_mode_no_send", "«Вместе» (suppressed=true) → execution ends at the gate, no send")
def bot_suppressed(ctx):
    t = int(time.time())
    return run_bot_scenario(ctx, "suppressed", [{"id": f"s-{ctx.run_id}", "body": "Цена?", "time": t, "fromMe": False, "type": "chat"}],
                            {"id": f"s-{ctx.run_id}", "body": "Цена?", "time": t}, post_times=1, expect_sends=0, wait_s=30, suppressed=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for id_, title, defect, _ in SCENARIOS:
            print(f"{id_:42} {title}{'  [defect: ' + defect + ']' if defect else ''}")
        return
    if not KEY:
        sys.exit("no staging API key in Tools/n8n/.secrets/staging-n8n.json — run bootstrap.py")
    st, _ = http("GET", f"{BASE}/healthz")
    if st != 200:
        sys.exit(f"staging n8n not reachable at {BASE}")
    st, _ = mock("GET", "/__mock/state")
    if st != 200:
        sys.exit(f"wappi mock not reachable at {MOCK}")
    ctx = Ctx()
    passed = failed = 0
    try:
        for id_, title, defect, fn in SCENARIOS:
            if a.only and a.only not in id_:
                continue
            t0 = time.time()
            try:
                ok, detail = fn(ctx)
            except Exception as e:
                ok, detail = False, f"EXCEPTION {e!r}"
            passed += ok
            failed += (not ok)
            print(f"{'PASS' if ok else 'FAIL'}  {id_} ({time.time() - t0:.1f}s) — {title}{'  [audit ' + defect + ']' if (defect and not ok) else ''}")
            print(f"      {detail}")
    finally:
        ctx.cleanup()
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
