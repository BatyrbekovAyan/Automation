#!/usr/bin/env python3
"""Rotate the cloudflared quick-tunnel URL across the local n8n dev setup.

When the trycloudflare hostname changes (every `cloudflared tunnel --url ...`
restart), four places go stale; a missed one silently kills bot replies:

  1. Assets/StreamingAssets/secrets.json  -> n8nBaseUrl
  2. Live local Create handlers' "Set Wappi Webhook" callback URL
     (CreateWhatsappWorkflow / CreateTelegramWorkflow, patched via REST PUT)
  3. Every existing bot's Wappi webhook registration
     (POST wappi.pro/{api|tapi}/webhook/url/set per profile)
  4. n8n itself must be restarted with WEBHOOK_URL=<new tunnel>
     (the script can only detect + warn; restart it yourself)

Usage:
  python3 Tools/n8n/rotate-tunnel.py               # rotate + verify
  python3 Tools/n8n/rotate-tunnel.py --dry-run     # show what would change
  python3 Tools/n8n/rotate-tunnel.py --url https://x.trycloudflare.com
  python3 Tools/n8n/rotate-tunnel.py --active-only # skip inactive bots' Wappi re-point

Idempotent: re-running with an unchanged tunnel reports "already current"
everywhere and just re-verifies. Exit code 0 = all verifications passed.

See Tools/n8n/dev-tunnel.md for the manual flow this automates.
"""

import argparse
import json
import re
import sqlite3
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SECRETS_PATH = REPO / "Assets" / "StreamingAssets" / "secrets.json"
N8N_API = "http://localhost:5678/api/v1"
N8N_DB = Path.home() / ".n8n" / "database.sqlite"
METRICS_PORTS = range(20241, 20246)  # cloudflared picks the first free one

# Local Create handlers whose "Set Wappi Webhook" node embeds the tunnel host.
CREATE_HANDLER_IDS = {"XuvOp7TxOImOAmlj", "Uz6HBBUpAiUqVysB"}
# Clone-source templates: share webhook path 0091024b-7b46, never per-bot.
TEMPLATE_IDS = {"4wYitz5ek30SVNlT", "4VN3gsFaC2HUYmcc"}
# Named handler webhook paths (never per-bot, never re-pointed at Wappi).
HANDLER_PATHS = {
    "CreateWhatsappWorkflow", "CreateTelegramWorkflow",
    "EditWhatsappWorkflow", "EditTelegramWorkflow",
    "UploadFile", "DeleteFile",
}
# Per-bot webhook path == Wappi profile_id (first two UUID groups).
PROFILE_PATH_RE = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}$")
TUNNEL_HOST_RE = re.compile(r"https://([a-z0-9-]+\.trycloudflare\.com)")
CLOUD_WEBHOOK_PREFIX = "https://bagkz.app.n8n.cloud/webhook/"

OK, FAIL, WARN, SKIP = "\033[32mOK\033[0m", "\033[31mFAIL\033[0m", "\033[33mWARN\033[0m", "\033[90mskip\033[0m"


def http(method, url, headers=None, body=None, timeout=15):
    """Return (status, body_text); never raises on HTTP error statuses."""
    req = urllib.request.Request(url, method=method, headers=headers or {})
    data = json.dumps(body).encode() if body is not None else None
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, data=data, timeout=timeout) as resp:
            return resp.status, resp.read().decode(errors="replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(errors="replace")
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        return None, str(e)


def detect_tunnel():
    for port in METRICS_PORTS:
        status, body = http("GET", f"http://127.0.0.1:{port}/quicktunnel", timeout=2)
        if status == 200:
            try:
                host = json.loads(body).get("hostname", "")
            except json.JSONDecodeError:
                continue
            if host:
                return f"https://{host}"
    return None


def n8n_headers(api_key):
    return {"X-N8N-API-KEY": api_key}


def fetch_all_workflows(api_key):
    """Full workflow list (with nodes) via local REST, or None if unreachable."""
    workflows, cursor = [], None
    while True:
        url = f"{N8N_API}/workflows?limit=100" + (f"&cursor={cursor}" if cursor else "")
        status, body = http("GET", url, n8n_headers(api_key), timeout=10)
        if status != 200:
            return None
        page = json.loads(body)
        workflows += page["data"]
        cursor = page.get("nextCursor")
        if not cursor:
            return workflows


def workflows_from_sqlite():
    """Fallback discovery when n8n is down: read workflow_entity directly."""
    if not N8N_DB.exists():
        return None
    con = sqlite3.connect(f"file:{N8N_DB}?mode=ro", uri=True)
    try:
        rows = con.execute("SELECT id, name, active, nodes FROM workflow_entity").fetchall()
    finally:
        con.close()
    return [{"id": r[0], "name": r[1], "active": bool(r[2]), "nodes": json.loads(r[3])}
            for r in rows]


def per_bot_workflows(workflows):
    """[(workflow, profile_id, 'api'|'tapi')] for every per-bot clone."""
    bots = []
    for w in workflows:
        if w["id"] in TEMPLATE_IDS:
            continue
        for node in w["nodes"]:
            if node.get("type") != "n8n-nodes-base.webhook":
                continue
            path = node.get("parameters", {}).get("path", "")
            if path in HANDLER_PATHS or not PROFILE_PATH_RE.match(path):
                continue
            blob = json.dumps(w["nodes"])
            wapi = "tapi" if "wappi.pro/tapi/" in blob else "api"
            bots.append((w, path, wapi))
            break
    return bots


def patch_workflow_nodes(w, new_url):
    """Return (patched_nodes, old_hosts) or (None, ...) if already current."""
    blob = json.dumps(w["nodes"])
    new_host = new_url.removeprefix("https://")
    stale = sorted(set(TUNNEL_HOST_RE.findall(blob)) - {new_host})
    patched = blob
    for host in stale:
        patched = patched.replace(host, new_host)
    # Freshly imported handlers may still point at prod Cloud for the Wappi
    # callback — only that URL, only in the Create handlers.
    if w["id"] in CREATE_HANDLER_IDS and CLOUD_WEBHOOK_PREFIX in patched:
        patched = patched.replace(CLOUD_WEBHOOK_PREFIX, f"{new_url}/webhook/")
        stale.append("bagkz.app.n8n.cloud (webhook callback)")
    if not stale:
        return None, []
    return json.loads(patched), stale


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--url", help="tunnel URL override (skips metrics auto-detect)")
    ap.add_argument("--dry-run", action="store_true", help="report changes without writing")
    ap.add_argument("--active-only", action="store_true",
                    help="re-point Wappi only for ACTIVE per-bot workflows "
                         "(default: all, so reactivated bots keep working)")
    args = ap.parse_args()

    failures, warnings = [], []

    # --- Inputs -------------------------------------------------------------
    if not SECRETS_PATH.exists():
        sys.exit(f"secrets.json not found at {SECRETS_PATH}")
    secrets_raw = SECRETS_PATH.read_text()
    secrets = json.loads(secrets_raw)
    wappi_token = secrets.get("wappiAuthToken", "")
    n8n_key = secrets.get("n8nAPIKey", "")
    old_url = (secrets.get("n8nBaseUrl") or "").rstrip("/")
    if not wappi_token or not n8n_key:
        sys.exit("secrets.json is missing wappiAuthToken or n8nAPIKey")

    new_url = (args.url or "").rstrip("/") or detect_tunnel()
    if not new_url:
        sys.exit("No quick tunnel found on 127.0.0.1:20241-20245.\n"
                 "Start one first:  cloudflared tunnel --url http://localhost:5678\n"
                 "(or pass --url https://<host>.trycloudflare.com)")
    if not new_url.startswith("https://"):
        sys.exit(f"Tunnel URL must be https:// — got {new_url}")

    print(f"Tunnel : {new_url}")
    print(f"Old    : {old_url or '(empty — was targeting prod Cloud)'}")
    if args.dry_run:
        print("Mode   : DRY RUN — nothing will be written\n")
    else:
        print()

    # --- 1) secrets.json n8nBaseUrl ------------------------------------------
    if old_url == new_url:
        print(f"[1] secrets.json n8nBaseUrl            {OK} already current")
    elif args.dry_run:
        print(f"[1] secrets.json n8nBaseUrl            would set {new_url}")
    else:
        secrets["n8nBaseUrl"] = new_url
        text = json.dumps(secrets, indent=2, ensure_ascii=False)
        if secrets_raw.endswith("\n"):
            text += "\n"
        SECRETS_PATH.write_text(text)
        print(f"[1] secrets.json n8nBaseUrl            {OK} updated")

    # --- 2) live local Create handlers (and any other stale workflow) --------
    workflows = fetch_all_workflows(n8n_key)
    n8n_up = workflows is not None
    if not n8n_up:
        warnings.append("n8n REST unreachable — workflow patch SKIPPED; "
                        "re-run this script after restarting n8n")
        print(f"[2] Create handlers' Wappi callback    {WARN} n8n not reachable at {N8N_API}")
        workflows = workflows_from_sqlite()
        if workflows is None:
            sys.exit(f"...and no local DB at {N8N_DB} either — nothing to discover bots from.")
        print(f"    (discovering bots from {N8N_DB} instead)")
    else:
        for w in workflows:
            patched, stale = patch_workflow_nodes(w, new_url)
            label = f"{w['name']} ({w['id']})"
            if patched is None:
                if w["id"] in CREATE_HANDLER_IDS:
                    print(f"[2] {label:38} {OK} already current")
                continue
            if args.dry_run:
                print(f"[2] {label:38} would replace: {', '.join(stale)}")
                continue
            # GET returns settings.binaryMode, but PUT's schema rejects it as an
            # unknown property (n8n API version mismatch) — strip before sending back.
            settings = {k: v for k, v in w["settings"].items() if k != "binaryMode"}
            body = {"name": w["name"], "nodes": patched,
                    "connections": w["connections"], "settings": settings}
            status, resp = http("PUT", f"{N8N_API}/workflows/{w['id']}",
                                n8n_headers(n8n_key), body)
            if status == 200:
                print(f"[2] {label:38} {OK} patched ({', '.join(stale)})")
            else:
                failures.append(f"PUT workflow {w['id']} -> {status}: {resp[:200]}")
                print(f"[2] {label:38} {FAIL} HTTP {status}")

    # --- 3) Wappi webhook registration per existing bot -----------------------
    bots = per_bot_workflows(workflows)
    orphans = set()  # profiles deleted on Wappi but whose workflow clone remains
    if not bots:
        print(f"[3] Wappi re-registration              {SKIP} no per-bot workflows found")
    for w, pid, wapi in bots:
        label = f"bot '{w['name']}' {pid} [{'WA' if wapi == 'api' else 'TG'}{'' if w['active'] else ', inactive'}]"
        if args.active_only and not w["active"]:
            print(f"[3] {label:38} {SKIP} --active-only")
            continue
        target = f"{new_url}/webhook/{pid}"
        if args.dry_run:
            print(f"[3] {label:38} would set {target}")
            continue
        status, resp = http(
            "POST",
            f"https://wappi.pro/{wapi}/webhook/url/set?profile_id={pid}&url={target}",
            {"Authorization": wappi_token})
        if status == 200:
            print(f"[3] {label:38} {OK} -> {target}")
        elif status == 400 and "Profile not found" in resp:
            orphans.add(pid)
            warnings.append(f"profile {pid} (bot '{w['name']}') no longer exists on Wappi "
                            "— orphaned workflow clone, consider deleting it")
            print(f"[3] {label:38} {WARN} profile gone on Wappi (orphan)")
        else:
            failures.append(f"Wappi url/set {pid} -> {status}: {resp[:200]}")
            print(f"[3] {label:38} {FAIL} HTTP {status}: {resp[:120]}")

    # --- 4) n8n WEBHOOK_URL env ------------------------------------------------
    pid_out = subprocess.run(["pgrep", "-f", "bin/n8n"], capture_output=True, text=True)
    n8n_pid = pid_out.stdout.split()[0] if pid_out.stdout.split() else None
    restart_cmd = f"WEBHOOK_URL={new_url} n8n start"
    if not n8n_pid:
        warnings.append(f"n8n is not running — start it with:  {restart_cmd}")
        print(f"[4] n8n WEBHOOK_URL                    {WARN} n8n not running")
    else:
        env_out = subprocess.run(["ps", "eww", "-p", n8n_pid, "-o", "command"],
                                 capture_output=True, text=True).stdout
        m = re.search(r"WEBHOOK_URL=(\S+)", env_out)
        current = m.group(1).rstrip("/") if m else None
        if current == new_url:
            print(f"[4] n8n WEBHOOK_URL                    {OK} already {new_url}")
        else:
            warnings.append(f"n8n (pid {n8n_pid}) runs with WEBHOOK_URL="
                            f"{current or '(unset)'} — RESTART IT:  {restart_cmd}")
            print(f"[4] n8n WEBHOOK_URL                    {WARN} stale: {current or '(unset)'}")

    # --- Verification -----------------------------------------------------------
    print("\n=== Verification ===")
    all_checks_ran = not args.dry_run

    status, _ = http("GET", f"{new_url}/healthz", timeout=10)
    healthz_ok = status == 200
    print(f"tunnel -> n8n /healthz                 {OK if healthz_ok else FAIL} HTTP {status}")
    if not healthz_ok:
        failures.append(f"{new_url}/healthz -> {status} (tunnel or n8n down)")

    for w, pid, wapi in bots:
        if pid in orphans:
            print(f"wappi url/get {pid:24} {SKIP} profile gone on Wappi")
            continue
        status, body = http("GET",
                            f"https://wappi.pro/{wapi}/webhook/url/get?profile_id={pid}",
                            {"Authorization": wappi_token})
        registered = status == 200 and f"{new_url}/webhook/{pid}" in body
        if status == 400 and "Profile not found" in body:
            print(f"wappi url/get {pid:24} {WARN} profile gone on Wappi (orphan)")
            if f"profile {pid}" not in " ".join(warnings):
                warnings.append(f"profile {pid} (bot '{w['name']}') no longer exists on Wappi "
                                "— orphaned workflow clone, consider deleting it")
            continue
        print(f"wappi url/get {pid:24} {OK if registered else FAIL} "
              f"{'points at new tunnel' if registered else f'HTTP {status}: {body[:100]}'}")
        if not registered and not args.dry_run:
            failures.append(f"Wappi url/get {pid}: webhook not on new tunnel ({body[:150]})")

        if not w["active"]:
            print(f"probe {new_url[8:38]}…/webhook/{pid[:13]} {SKIP} workflow inactive")
            continue
        status, body = http("GET", f"{new_url}/webhook/{pid}", timeout=10)
        # A live POST-registered production webhook answers GET with 404 + hint.
        live = status == 404 and "GET" in body
        print(f"probe tunnel /webhook/{pid:13}    {OK if live else FAIL} "
              f"{'404 not-registered-for-GET (webhook live)' if live else f'HTTP {status}: {body[:100]}'}")
        if not live:
            failures.append(f"GET {new_url}/webhook/{pid} -> {status} (expected 404 GET-hint)")

    if n8n_up and healthz_ok:
        for path in sorted(HANDLER_PATHS):
            status, body = http("GET", f"{new_url}/webhook/{path}", timeout=10)
            live = status == 404 and "GET" in body
            print(f"probe tunnel /webhook/{path:22} {OK if live else FAIL}")
            if not live:
                failures.append(f"handler {path} probe -> {status} (is the workflow active?)")

    # --- Summary ------------------------------------------------------------------
    print("\n=== Summary ===")
    for wmsg in warnings:
        print(f"{WARN}  {wmsg}")
    for fmsg in failures:
        print(f"{FAIL}  {fmsg}")
    if args.dry_run:
        print("Dry run — no changes were made.")
    elif not failures and not warnings:
        print(f"{OK}  Rotation complete: secrets, Create handlers, Wappi registrations "
              "and n8n WEBHOOK_URL all point at the new tunnel.")
    elif not failures:
        print("Rotation applied, but heed the warnings above (usually: restart n8n, "
              "then re-run this script to verify).")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
