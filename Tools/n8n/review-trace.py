#!/usr/bin/env python3
"""Read-only trace of a device session against PROD n8n: every GetUsage call (app user id →
plan/quota/reserve the server answered) and every RevenueCat Events webhook (store events) in
a UTC window. Built for App Review forensics (2026-09-13): the reviewer's session leaves
GetUsage calls but NO store events when a purchase never reached StoreKit.

NO LIVE TRAFFIC: only GET /api/v1/executions with the admin key from Tools/n8n/.secrets/.

    python3 Tools/n8n/review-trace.py 2026-09-15T00:00 2026-09-16T00:00
"""
import datetime as dt
import json
import os
import sys
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.environ.get("N8N_BASE_URL", "https://n8n.choosereply.com").rstrip("/")
KEY = os.environ.get("N8N_API_KEY") or open(os.path.join(HERE, ".secrets", "prod-api-key.txt")).read().strip()
WF_GET_USAGE = "jtbssfzXbOxwTK4k"
WF_RC_EVENTS = "ZGYr6srzS3rSSXHp"


def get(path, **q):
    url = f"{BASE}/api/v1{path}" + ("?" + urllib.parse.urlencode(q) if q else "")
    req = urllib.request.Request(url, headers={"X-N8N-API-KEY": KEY, "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode())


def ts(s):
    return dt.datetime.fromisoformat(s.replace("Z", "+00:00"))


def list_execs(wf, lo, hi):
    cursor, rows = None, []
    while True:
        q = {"workflowId": wf, "limit": 250}
        if cursor:
            q["cursor"] = cursor
        page = get("/executions", **q)
        data = page.get("data", [])
        rows += [e for e in data if lo <= ts(e["startedAt"]) < hi]
        if not data or ts(data[-1]["startedAt"]) < lo:
            break
        cursor = page.get("nextCursor")
        if not cursor:
            break
    return sorted(rows, key=lambda e: e["startedAt"])


def leaf(e, node, key):
    run = (e.get("data") or {}).get("resultData", {}).get("runData", {})
    for r in run.get(node, []):
        for br in (r.get("data") or {}).get("main") or []:
            for it in br or []:
                cur = it.get("json", {})
                for k in key.split("."):
                    cur = cur.get(k) if isinstance(cur, dict) else None
                if cur is not None:
                    return cur
    return None


def main():
    lo = dt.datetime.fromisoformat(sys.argv[1]).replace(tzinfo=dt.timezone.utc)
    hi = dt.datetime.fromisoformat(sys.argv[2]).replace(tzinfo=dt.timezone.utc)
    print(f"=== Get_Usage {lo.isoformat()} .. {hi.isoformat()} ===")
    for e in list_execs(WF_GET_USAGE, lo, hi):
        d = get(f"/executions/{e['id']}", includeData="true")
        uid = leaf(d, "Webhook", "body.appUserId")
        body = {k: leaf(d, "Respond 200", k) for k in ("plan", "status", "quota", "topupBalance")}
        print(f'  {e["startedAt"][5:19]}  #{e["id"]}  {uid}  -> {body}')
    print(f"=== RevenueCat_Events {lo.isoformat()} .. {hi.isoformat()} ===")
    for e in list_execs(WF_RC_EVENTS, lo, hi):
        d = get(f"/executions/{e['id']}", includeData="true")
        ev = leaf(d, "Webhook", "body.event") or {}
        print(f'  {e["startedAt"][5:19]}  #{e["id"]}  {ev.get("type")}  {ev.get("product_id")}  '
              f'env={ev.get("environment")}  cc={ev.get("country_code")}  user={ev.get("app_user_id")}  '
              f'aliases={len(ev.get("aliases") or [])}')


if __name__ == "__main__":
    main()
