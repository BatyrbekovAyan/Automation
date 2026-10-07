#!/usr/bin/env python3
"""Read-only baseline metrics of RETAINED n8n executions (bots + Suggest Replies), anonymised.

Classifies each retained execution by the LAST node that ran (the pipeline's own terminal
branches double as outcome labels until the explicit outcome stamps of the 2026-09 remediation
land) and reports counts + latency percentiles per outcome. NO message text, ids or phone
numbers are printed or written — only node names, statuses, timings and counts.

NO LIVE TRAFFIC: only GET /api/v1/executions (list + per-id includeData) with the admin key.
Executions on prod are pruned after 7 days (EXECUTIONS_DATA_MAX_AGE), so the sample is the
last week at most; the JSON output records the window it actually covered.

    python3 Tools/n8n/baseline-metrics.py                       # prod (Tools/n8n/.secrets/prod-api-key.txt)
    N8N_BASE_URL=http://localhost:5678 N8N_API_KEY=... python3 Tools/n8n/baseline-metrics.py
    python3 Tools/n8n/baseline-metrics.py --out docs/audits/baseline-metrics-YYYY-MM-DD.json
"""
import argparse
import datetime as dt
import json
import os
import statistics
import sys
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.environ.get("N8N_BASE_URL", "https://n8n.choosereply.com").rstrip("/")
KEY = os.environ.get("N8N_API_KEY") or open(os.path.join(HERE, ".secrets", "prod-api-key.txt")).read().strip()

# Bot-pipeline terminal node -> outcome label (see docs/audits/implementation-progress.md, stage 0.5).
# Templates carrying the `Outcome` stamp (apply-outcome-stamps.py) report their label directly; this
# map is the fallback for executions of clones that predate the stamp.
BOT_OUTCOME_BY_LAST_NODE = {
    "If": "filtered",               # from != chatId (group / foreign) -> dead-end
    "Read Reply Mode": "error",     # gate read never completed
    "Suppressed?": "suppressed",    # «Вместе» -> dead-end
    "Debounce Wait": "in_progress",
    "Fetch Recent": "error",
    "Latest+Combine": "error",
    "Is Latest?": "superseded",     # older fragment of a burst -> dead-end
    "Count Dialog": "error",
    "Quota Decision": "error",
    "If Quota Allows": "quota_denied",
    "HTTP Request": "sent",         # Wappi message/send returned (transport accepted)
    # Stage 3 (apply-reply-pipeline-v2.py): every run ends at the Outcome stamp, which now carries the
    # recorded ledger state (duplicate | superseded | cancelled_owner | cancelled_mode | cancelled_busy |
    # cancelled_fetch | generation_failed | sent | send_failed | send_unknown); these are the fallbacks
    # for a run that died inside the new stretch.
    "Claim Event": "error", "Claimed?": "duplicate", "Generated?": "generation_failed",
    "Final Fetch": "error", "Final Check": "error", "Final OK?": "error", "Acquire Send Lease": "error",
    "Send Allowed?": "error", "Settle Send": "error", "Send Unknown?": "error", "Wait Reconcile": "in_progress",
    "Reconcile Fetch": "error", "Reconcile Check": "error", "Record Send": "error", "Cancel Label": "error", "Record Cancel": "error",
    # iteration 2026-09-28: V5's one re-read of a same-second peer
    "Peer Recheck?": "error", "Wait Peer Recheck": "in_progress", "Recheck Fetch": "error",
}
SUGGEST_OUTCOME_BY_LAST_NODE = {"Respond": "responded"}


def get(path, **q):
    url = f"{BASE}/api/v1{path}" + ("?" + urllib.parse.urlencode(q) if q else "")
    req = urllib.request.Request(url, headers={"X-N8N-API-KEY": KEY, "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode())


def list_execs(wf, limit_pages=8):
    cursor, rows = None, []
    for _ in range(limit_pages):
        q = {"workflowId": wf, "limit": 250}
        if cursor:
            q["cursor"] = cursor
        page = get("/executions", **q)
        rows += page.get("data", [])
        cursor = page.get("nextCursor")
        if not cursor:
            break
    return rows


def ts(s):
    return dt.datetime.fromisoformat(s.replace("Z", "+00:00"))


def pct(values, p):
    if not values:
        return None
    vs = sorted(values)
    k = max(0, min(len(vs) - 1, int(round((p / 100.0) * (len(vs) - 1)))))
    return round(vs[k], 3)


def summarise(name, wf_id, outcome_map, suggest=False):
    execs = list_execs(wf_id)
    per_outcome = {}
    stage_times = {}
    window = [None, None]
    for e in execs:
        started = e.get("startedAt")
        if started:
            window[0] = min(window[0] or started, started)
            window[1] = max(window[1] or started, started)
        try:
            d = get(f"/executions/{e['id']}", includeData="true")
        except Exception as ex:  # a pruned record mid-run: skip, count it
            per_outcome.setdefault("unavailable", {"n": 0, "durations": []})["n"] += 1
            continue
        rd = (d.get("data") or {}).get("resultData", {})
        last = rd.get("lastNodeExecuted")
        status = d.get("status")
        outcome = outcome_map.get(last, f"other:{last}")
        stamp = None
        for run in rd.get("runData", {}).get("Outcome", []) or []:
            for br in (run.get("data") or {}).get("main") or []:
                for it in br or []:
                    stamp = (it.get("json") or {}).get("outcome") or stamp
        if stamp:
            outcome = stamp
        if status not in ("success", "finished"):
            outcome = f"status:{status}"
        if suggest and last == "Respond":
            # Distinguish the client-visible envelopes without reading any card text.
            out = None
            for run in rd.get("runData", {}).get("Build Response", []):
                for br in (run.get("data") or {}).get("main") or []:
                    for it in br or []:
                        out = it.get("json") or out
            if out is not None:
                if out.get("error"):
                    outcome = "generation_failed"
                elif out.get("abstain"):
                    outcome = "abstain"
                else:
                    outcome = f"cards:{len(out.get('suggestions') or [])}"
        dur = None
        if d.get("startedAt") and d.get("stoppedAt"):
            dur = (ts(d["stoppedAt"]) - ts(d["startedAt"])).total_seconds()
        slot = per_outcome.setdefault(outcome, {"n": 0, "durations": []})
        slot["n"] += 1
        if dur is not None:
            slot["durations"].append(dur)
        for node, runs in rd.get("runData", {}).items():
            for run in runs:
                et = run.get("executionTime")
                if isinstance(et, (int, float)):
                    stage_times.setdefault(node, []).append(et / 1000.0)
    outcomes = {}
    for k, v in per_outcome.items():
        ds = v["durations"]
        outcomes[k] = {"n": v["n"], "p50_s": pct(ds, 50), "p95_s": pct(ds, 95), "max_s": pct(ds, 100)}
    stages = {n: {"n": len(v), "p50_s": pct(v, 50), "p95_s": pct(v, 95), "max_s": pct(v, 100)}
              for n, v in sorted(stage_times.items())}
    return {"workflow": name, "workflowId": wf_id, "executions": len(execs),
            "window": {"from": window[0], "to": window[1]}, "outcomes": outcomes, "stages": stages}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", help="write the anonymised JSON summary here")
    ap.add_argument("--bots", nargs="*", default=None,
                    help="bot workflow ids to sample (default: every ACTIVE or clone bot found by name pattern)")
    args = ap.parse_args()
    wfs = get("/workflows", limit=100).get("data", [])
    by_id = {w["id"]: w for w in wfs}
    canonical_names = {"WhatsApp Bot", "Telegram Bot"}
    bots = args.bots or [w["id"] for w in wfs if len(w.get("nodes", [])) == 33 and w["name"] not in canonical_names]
    report = {"capturedAt": dt.datetime.now(dt.timezone.utc).isoformat(), "base": BASE, "workflows": []}
    for wid in bots:
        w = by_id.get(wid, {"name": wid})
        print(f"sampling bot {w['name']!r} ({wid}) ...", file=sys.stderr)
        report["workflows"].append(summarise(w["name"], wid, BOT_OUTCOME_BY_LAST_NODE))
    sug = next((w for w in wfs if w["name"] == "Suggest Replies"), None)
    if sug:
        print("sampling Suggest Replies ...", file=sys.stderr)
        report["workflows"].append(summarise(sug["name"], sug["id"], SUGGEST_OUTCOME_BY_LAST_NODE, suggest=True))
    for r in report["workflows"]:
        print(f"\n== {r['workflow']} ({r['workflowId']}): {r['executions']} executions, window {r['window']['from']} .. {r['window']['to']}")
        for k, v in sorted(r["outcomes"].items(), key=lambda kv: -kv[1]["n"]):
            print(f"   {k:22} n={v['n']:4}  p50={v['p50_s']}s  p95={v['p95_s']}s  max={v['max_s']}s")
        hot = {n: v for n, v in r["stages"].items() if n in ("AI Agent", "Fetch Recent", "Debounce Wait", "HTTP Request", "Mark Read", "Typing", "Reading Pause", "Typing Pause", "Retrieve RAG", "Retrieve RAG TG", "LLM", "LLM Retry", "Suggestion Gate", "Count Dialog", "Read Reply Mode")}
        for n, v in hot.items():
            print(f"   stage {n:16} n={v['n']:4}  p50={v['p50_s']}s  p95={v['p95_s']}s  max={v['max_s']}s")
    if args.out:
        with open(args.out, "w", encoding="utf-8") as fh:
            json.dump(report, fh, ensure_ascii=False, indent=1)
        print(f"\nwrote {args.out}")


if __name__ == "__main__":
    main()
