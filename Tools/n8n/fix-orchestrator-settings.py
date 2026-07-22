#!/usr/bin/env python3
"""Stop the Create/Edit orchestrators from 400-ing when they clone/update a bot workflow.

Root cause (2026-07-22): n8n 2.27.4 stamps `"binaryMode":"separate"` into a workflow's
STORED settings on every save. The 10-03 template redeploy (REST PUT of both bot templates,
2026-07-21 16:37/16:38 UTC) re-saved them, so the stored template settings are now
`{"executionOrder":"v1","binaryMode":"separate",...}` (the committed canonical files stay
clean `{"executionOrder":"v1"}`). Each Create/Edit orchestrator does
`Get Sample Workflow` (GET template — the response now carries `binaryMode`) -> a `Set` node
with includeFields `"name, nodes, connections, settings"` (verbatim settings passthrough) ->
`Create Workflow` (POST /api/v1/workflows) / `Update Workflow` (PUT). The n8n public write
schema rejects the unknown `binaryMode` property:

    NodeApiError httpCode 400 "request/body/settings must NOT have additional properties"

Creates last succeeded 2026-07-21 08:06 UTC (pre-redeploy) and broke on the first
post-redeploy attempt (dev executions 831 CreateTelegramWorkflow / 832 CreateWhatsappWorkflow,
both status=error, lastNodeExecuted "Create Workflow").

Fix: in every `Set` node that passes `settings` through, override the passed-through
`settings` with a copy that drops ONLY `binaryMode` (a blacklist, not a whitelist —
`availableInMCP` and the rest ARE accepted by the write schema, so they are kept). This is
the same idiom the dev `rotate-tunnel.py` already uses before its own PUT.

Orchestrators patched (all four — the two Edit ones are LATENT: the next app-side edit of a
binaryMode-stamped clone would 400 identically):
  XuvOp7TxOImOAmlj  CreateWhatsappWorkflow   (Set Fields)
  Uz6HBBUpAiUqVysB  CreateTelegramWorkflow   (Set Fields)
  3qax5J9u2qsT9Vao  Edit_Whatsapp_Workflow   (Set Fields, Set Bussiness Type)
  TwWPW3gIyjZS3foR  Edit_Telegram_Workflow   (Set Fields, Set Bussiness Type)

Two modes:
  --canonical   (NO network) patch the 4 committed JSONs in Tools/n8n/workflows/.
                Idempotent by assignment name; NEVER touches URLs. Committed as the source
                of truth (also carries the fix into the future prod one-shot copy).
  --live        (owner-run) GET each live orchestrator by literal id, apply the same
                in-memory Set-node patch, PUT back {name,nodes,connections,settings} with
                `binaryMode` stripped from the orchestrator's OWN settings too (else the PUT
                itself 400s), then re-activate the two Create orchestrators if they came back
                inactive (Edit orchestrators keep their current active state). Idempotent.

CRITICAL: the LIVE dev orchestrators use `http://localhost:5678/api/v1/...` URLs, while the
committed canonical exports carry prod `https://bagkz.app.n8n.cloud/api/v1/...` URLs. NEVER
re-import a canonical file to fix dev — --live edits the live workflow surgically in place.

--live reads the n8n API key from `N8N_API_KEY` or `Assets/StreamingAssets/secrets.json` at
OWNER runtime (this script is never run with secrets by Claude); base from `N8N_BASE_URL`
(default http://localhost:5678).

Usage:
  python3 Tools/n8n/fix-orchestrator-settings.py --canonical   # patch + commit the JSONs
  python3 Tools/n8n/fix-orchestrator-settings.py --live        # owner: repair the dev instance
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
WF_DIR = REPO / "Tools" / "n8n" / "workflows"
SECRETS_PATH = REPO / "Assets" / "StreamingAssets" / "secrets.json"

# (literal id, canonical filename, role). role drives --live re-activation: the two Create
# webhooks must end ACTIVE (the create flow needs them); the Edit webhooks keep their
# current state (re-activated only if the PUT dropped a previously-active one).
ORCHESTRATORS = [
    ("XuvOp7TxOImOAmlj", "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "create"),
    ("Uz6HBBUpAiUqVysB", "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json", "create"),
    ("3qax5J9u2qsT9Vao", "3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json", "edit"),
    ("TwWPW3gIyjZS3foR", "TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json", "edit"),
]

# Override the passed-through settings with a copy that drops ONLY binaryMode.
SETTINGS_ASSIGNMENT_VALUE = (
    "={{ Object.fromEntries(Object.entries($json.settings || {})"
    ".filter(([k]) => k !== 'binaryMode')) }}"
)
URL_RE = re.compile(r"https?://[^\s\"\\]+")


def includes_settings(params):
    """True if this Set node passes `settings` through (includeFields token list)."""
    inc = params.get("includeFields")
    if not isinstance(inc, str):
        return False
    return "settings" in [t.strip() for t in inc.split(",")]


def patch_nodes(wf_id, nodes):
    """Append a binaryMode-stripping `settings` assignment to every Set node that passes
    settings through. Returns (added, already). Idempotent by assignment name."""
    added = already = 0
    for n in nodes:
        if n.get("type") != "n8n-nodes-base.set":
            continue
        params = n.get("parameters", {})
        if not includes_settings(params):
            continue
        assignments = params.setdefault("assignments", {}).setdefault("assignments", [])
        if any(a.get("name") == "settings" for a in assignments):
            already += 1
            continue
        assignments.append({
            # stable uuid5 so re-runs are byte-identical
            "id": str(uuid.uuid5(uuid.NAMESPACE_URL, f"{wf_id}-{n['name']}-settings")),
            "name": "settings",
            "value": SETTINGS_ASSIGNMENT_VALUE,
            "type": "object",
        })
        added += 1
    return added, already


def strip_binary_mode(settings):
    return {k: v for k, v in (settings or {}).items() if k != "binaryMode"}


# --- canonical mode (no network) ---------------------------------------------
def run_canonical():
    total = 0
    for wf_id, fname, _role in ORCHESTRATORS:
        path = WF_DIR / fname
        raw = path.read_text(encoding="utf-8")
        wf = json.loads(raw)
        urls_before = sorted(URL_RE.findall(raw))
        added, already = patch_nodes(wf.get("id", wf_id), wf["nodes"])
        out = json.dumps(wf, indent=2, ensure_ascii=False)
        if raw.endswith("\n"):
            out += "\n"
        # self-check 1: the patch NEVER changes any URL
        assert sorted(URL_RE.findall(out)) == urls_before, f"{fname}: URL set changed!"
        # self-check 2: every settings-passthrough Set node now carries the assignment
        for n in wf["nodes"]:
            if n.get("type") == "n8n-nodes-base.set" and includes_settings(n.get("parameters", {})):
                names = [a.get("name") for a in n["parameters"]["assignments"]["assignments"]]
                assert "settings" in names, f"{fname}: {n['name']!r} missing settings assignment"
        if added:
            path.write_text(out, encoding="utf-8")
        print(f"{'PATCHED' if added else 'ok     '} {fname}: +{added} assignment(s), {already} already present")
        total += added
    # self-check 3: reload from disk and prove the assignment is durable
    for _wf_id, fname, _role in ORCHESTRATORS:
        wf = json.loads((WF_DIR / fname).read_text(encoding="utf-8"))
        for n in wf["nodes"]:
            if n.get("type") == "n8n-nodes-base.set" and includes_settings(n.get("parameters", {})):
                names = [a.get("name") for a in n["parameters"]["assignments"]["assignments"]]
                assert "settings" in names, f"{fname}: disk verify failed for {n['name']!r}"
    print(f"\ncanonical: {total} assignment(s) added across {len(ORCHESTRATORS)} orchestrators; "
          "URLs untouched, disk-verified.")
    return 0


# --- live mode (owner-run) ---------------------------------------------------
def api_key():
    return os.environ.get("N8N_API_KEY") or json.loads(SECRETS_PATH.read_text())["n8nAPIKey"]


def http(method, url, key, body=None, timeout=30):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("X-N8N-API-KEY", key)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            txt = resp.read().decode()
            return resp.status, (json.loads(txt) if txt else {})
    except urllib.error.HTTPError as e:
        return e.code, {"error": e.read().decode()[:300]}
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        return None, {"error": str(e)}


def get_active(base, wf_id, key):
    st, wf = http("GET", f"{base}/api/v1/workflows/{wf_id}", key)
    return (wf.get("active") if st == 200 else None), st, wf


def run_live():
    base = os.environ.get("N8N_BASE_URL", "http://localhost:5678").rstrip("/")
    key = api_key()
    rc = 0
    for wf_id, _fname, role in ORCHESTRATORS:
        was_active, gst, wf = get_active(base, wf_id, key)
        if gst != 200:
            print(f"FAIL   {wf_id}: GET -> HTTP {gst}: {str(wf)[:180]}")
            rc = 1
            continue
        added, already = patch_nodes(wf_id, wf["nodes"])
        payload = {
            "name": wf["name"],
            "nodes": wf["nodes"],
            "connections": wf["connections"],
            "settings": strip_binary_mode(wf.get("settings", {})),  # strip on OWN settings too
        }
        pst, presp = http("PUT", f"{base}/api/v1/workflows/{wf_id}", key, payload)
        if pst not in (200, 201):
            print(f"FAIL   {wf_id} {wf['name']}: PUT -> HTTP {pst}: {str(presp)[:180]}")
            rc = 1
            continue
        now_active, _, _ = get_active(base, wf_id, key)
        # Create webhooks must end active; Edit webhooks preserve their pre-PUT state.
        target = True if role == "create" else was_active
        note = ""
        if target and now_active is False:
            ast, aresp = http("POST", f"{base}/api/v1/workflows/{wf_id}/activate", key)
            if ast == 200:
                now_active, note = True, " (re-activated)"
            else:
                note = f" (ACTIVATE FAILED HTTP {ast}: {str(aresp)[:120]})"
                rc = 1
        verb = "patched" if added else "already-patched"
        print(f"OK     {wf_id} {wf['name']} [{role}]: {verb} (+{added}, {already} existing)  "
              f"active={now_active}{note}")
    print("\nlive: all four orchestrators strip binaryMode from the clone payload. "
          "Re-run app create/edit to confirm the 400 is gone.")
    return rc


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--canonical", action="store_true",
                   help="patch the 4 committed orchestrator JSONs (no network)")
    g.add_argument("--live", action="store_true",
                   help="patch + re-activate the 4 live dev orchestrators (owner-run)")
    args = ap.parse_args()
    sys.exit(run_canonical() if args.canonical else run_live())


if __name__ == "__main__":
    main()
