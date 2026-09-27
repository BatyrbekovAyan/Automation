#!/usr/bin/env python3
"""STAGING-ONLY: swap the model the staging Suggest Replies calls, for eval model comparisons.

Rewrites the DERIVED copy Tools/n8n/workflows-staging/9PTyYcelRQI7bGDb-Suggest_Replies.json
(gitignored — bootstrap.py re-derives it from the canonical file, which resets this swap) and
re-imports just that workflow: stop server → import → publish → start. The canonical workflow in
Tools/n8n/workflows/ is never touched, so nothing here can reach prod.

    python3 Tools/n8n/eval/panel-model.py gpt-5.4-mini --reasoning-effort none
    python3 Tools/n8n/eval/panel-model.py gpt-4o-mini            # back to the canonical request shape
    python3 Tools/n8n/eval/run-eval.py --label panel-x --skip-auto --repeat-all --repeat 3

Every model newer than the gpt-4o/4.1 family rejects `max_tokens` with a 400 unsupported_parameter
(probed 2026-09-25 on gpt-5.4-mini and gpt-6-luna), so for them the body carries
`max_completion_tokens` instead — the canonical workflow needs the same change before a real switch.
Stop any eval run that is in flight first: the swap restarts the staging server.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
N8N = os.path.dirname(HERE)
STAGING = os.path.join(N8N, "staging")
WF_ID = "9PTyYcelRQI7bGDb"
DERIVED = os.path.join(N8N, "workflows-staging", f"{WF_ID}-Suggest_Replies.json")
LEGACY = ("gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1", "gpt-4.1-nano")


def patch(body, model, effort):
    """Idempotent: normalise back to the canonical shape, then apply the target model's shape."""
    body = re.sub(r'"model":"[^"]+"', f'"model":"{model}"', body, count=1)
    body = re.sub(r'"reasoning_effort":"[^"]+",', "", body)
    body = body.replace('"max_completion_tokens":', '"max_tokens":')
    if model not in LEGACY:
        body = body.replace('"max_tokens":', '"max_completion_tokens":')
        if effort:
            body = body.replace(f'"model":"{model}",', f'"model":"{model}","reasoning_effort":"{effort}",', 1)
    return body


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("--reasoning-effort", choices=["none", "minimal", "low", "medium", "high"])
    a = ap.parse_args()
    if not os.path.exists(DERIVED):
        sys.exit(f"{DERIVED} not found — run staging/bootstrap.py first")
    wf = json.load(open(DERIVED, encoding="utf-8"))
    patched = 0
    for node in wf["nodes"]:
        if node["name"] in ("LLM", "LLM Retry"):
            node["parameters"]["jsonBody"] = patch(node["parameters"]["jsonBody"], a.model, a.reasoning_effort)
            patched += 1
    if patched != 2:
        sys.exit(f"expected the LLM and LLM Retry nodes, found {patched} — Suggest Replies changed shape")
    for forbidden in ("n8n.choosereply.com", "wappi.pro"):
        if forbidden in json.dumps(wf):
            sys.exit(f"REFUSED: the derived Suggest Replies references {forbidden}")
    with open(DERIVED, "w", encoding="utf-8") as fh:
        json.dump(wf, fh, indent=2, ensure_ascii=False)
    run = lambda *cmd: subprocess.run(list(cmd), check=True, capture_output=True, text=True)
    run(sys.executable, os.path.join(STAGING, "bootstrap.py"), "--stop")
    run(os.path.join(STAGING, "n8n-staging.sh"), "import:workflow", f"--input={DERIVED}")
    run(os.path.join(STAGING, "n8n-staging.sh"), "publish:workflow", f"--id={WF_ID}")
    run(sys.executable, os.path.join(STAGING, "bootstrap.py"), "--restart")
    # The server answers /rest/settings before the published webhook is registered — the first eval
    # request after a swap got a 404 «webhook is not registered» (2026-09-25). An empty POST is refused
    # by the workflow's own validation long before the LLM, so it is a free readiness probe.
    base = os.environ.get("N8N_STAGING_BASE", "http://localhost:5678").rstrip("/")
    for _ in range(60):
        req = urllib.request.Request(f"{base}/webhook/SuggestReplies", data=b"{}", method="POST", headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=10) as r:
                status = r.status
        except urllib.error.HTTPError as e:
            status = e.code
        except Exception:
            status = None
        if status not in (None, 404):
            break
        time.sleep(1)
    else:
        sys.exit("the staging SuggestReplies webhook never registered after the restart")
    body = next(n for n in wf["nodes"] if n["name"] == "LLM")["parameters"]["jsonBody"]
    print("staging Suggest Replies now calls:", body[1:body.index('"messages"')])


if __name__ == "__main__":
    main()
