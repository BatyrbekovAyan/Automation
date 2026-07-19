#!/usr/bin/env python3
"""Deploy + export the shared "Set Reply Mode" n8n workflow from its canonical JSON.

The 13th canonical workflow: a shared, always-active webhook (POST /webhook/SetReplyMode)
that persists the semi-auto «Авто/Вместе» suppression flag. It takes an untrusted JSON body
`{ profileIds:[...], chatId:"*"|"<id>", suppressed:bool }`, validates it in a Code node
(non-empty string[] profileIds with sentinels dropped, non-empty string chatId, real boolean
suppressed), routes a malformed body straight to a `bad_request` response BEFORE any DB write,
and otherwise fans out to ONE item per surviving profileId so the Postgres node upserts one
row per profile into `reply_mode_flags` (on conflict do update). The app POSTs here on a
bot-default flip, a per-chat toggle, and a chat-open re-assert; the bot templates' gate reads
the same table (SUP-01..SUP-04).

This script does NOT generate the workflow. The single source of truth is the committed
canonical export `Tools/n8n/workflows/Set_Reply_Mode.json` (provisional filename — the real
n8n id is assigned on first deploy and the file is renamed <id>-Set_Reply_Mode.json in 09-04).
Deploy imports that JSON verbatim and rebinds ONLY the credential ids for the target instance.

Usage:
  python3 Tools/n8n/build-set-reply-mode.py --dry-run             # print the exact payload; no network
  python3 Tools/n8n/build-set-reply-mode.py                       # POST-create on the target, then activate
  python3 Tools/n8n/build-set-reply-mode.py --update <id>         # PUT the same content onto an existing id
  python3 Tools/n8n/build-set-reply-mode.py --export <id> <out.json>

Reads the n8n API key from Assets/StreamingAssets/secrets.json (n8nAPIKey) or env
N8N_API_KEY; targets the local dev instance (http://localhost:5678) unless N8N_BASE_URL
is set (prod: https://bagkz.app.n8n.cloud).

Credential binding (C5 — load-bearing): TWO credentials are both named "Postgres"
(`1H5xlpFSESU4w6JH` = the bot-template Chat Memory DB where reply_mode_flags lives, and
`vvRrFiEXzLVqKjOx` = the Dashboard/RAG DB). A by-NAME lookup is ambiguous, so this deployer
binds the Postgres credential by explicit id, defaulting to `1H5xlpFSESU4w6JH` (the same DB
the gate reads). Override for a different target:
  --postgres-cred ID / env N8N_POSTGRES_CRED_ID   Postgres credential id on the target
"""
import argparse
import json
import os
import sys
import time
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SECRETS = os.path.join(REPO, "Assets/StreamingAssets/secrets.json")
BASE = os.environ.get("N8N_BASE_URL", "http://localhost:5678").rstrip("/")
CANONICAL = os.path.join(HERE, "workflows", "Set_Reply_Mode.json")

# The Chat Memory Postgres credential id — the DB `reply_mode_flags` lives on and the bot
# templates' gate reads. Bound by explicit id (never by ambiguous name — C5) and used as the
# default when no --postgres-cred flag / N8N_POSTGRES_CRED_ID env is supplied.
DEFAULT_POSTGRES_CRED_ID = "1H5xlpFSESU4w6JH"

# Credential NAME pins per type (name is what prod recreates in the Credentials UI). The id
# is bound by CRED_OVERRIDES (always set — see main()) so resolve_cred never guesses by name.
CRED_PINS = {
    "postgres": (DEFAULT_POSTGRES_CRED_ID, "Postgres"),
}
OVERRIDE_HINT = {
    "postgres": "--postgres-cred / env N8N_POSTGRES_CRED_ID",
}

# Explicit credential-id overrides. main() ALWAYS fills "postgres" (flag > env > default id)
# because the two "Postgres"-named creds make a by-name lookup ambiguous — an override entry
# short-circuits resolve_cred() and binds the id verbatim. Maps cred_type -> id.
CRED_OVERRIDES = {}


def api_key():
    k = os.environ.get("N8N_API_KEY")
    if k:
        return k
    with open(SECRETS) as f:
        return json.load(f)["n8nAPIKey"]


def resolve_cred(cred_type):
    """Return (id, name) for a credential.

    For this workflow every credential type is bound by an explicit CRED_OVERRIDES id
    (main() always sets "postgres" — flag > env > DEFAULT_POSTGRES_CRED_ID) because two
    credentials share the name "Postgres" and a by-name lookup is ambiguous. The pinned name
    from CRED_PINS is kept for traceability. A type with no override AND no pin is a hard
    error (refuse to guess).
    """
    if cred_type not in CRED_PINS:
        raise SystemExit(
            f"credential type {cred_type!r} has no CRED_PINS entry — add a pin "
            f"(+ an override flag/env) so it can be rebound."
        )
    _dev_ref_id, want_name = CRED_PINS[cred_type]
    override = CRED_OVERRIDES.get(cred_type)
    if override:
        return override, want_name
    raise SystemExit(
        f"no credential id for {cred_type!r} — pass {OVERRIDE_HINT[cred_type]} "
        f"(the id of the {want_name!r} credential on the target)."
    )


def req(method, path, body=None):
    url = f"{BASE}/api/v1{path}"
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(url, data=data, method=method)
    r.add_header("X-N8N-API-KEY", api_key())
    r.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(r, timeout=30) as resp:
            raw = resp.read().decode()
            return resp.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        return e.code, {"error": e.read().decode()}


def canonical_payload():
    """Load the committed canonical JSON and rebind credential ids for the target.

    Returns the exact {name, nodes, connections, settings} deploy payload. Node content is
    NEVER regenerated here — the canonical file is the single source of truth. Only per-instance
    credential bindings are rewritten, and only for types pinned in CRED_PINS; a credential type
    in the file but NOT in CRED_PINS is a hard error (its id would otherwise ride along silently).
    """
    with open(CANONICAL, encoding="utf-8") as fh:
        wf = json.load(fh)
    resolved = {}  # cred_type -> (id, name); resolve once per type
    for nd in wf["nodes"]:
        for cred_type in list(nd.get("credentials", {})):
            if cred_type not in CRED_PINS:
                raise SystemExit(
                    f"canonical workflow binds credential type {cred_type!r} on node "
                    f"{nd['name']!r} which has no CRED_PINS entry — add a pin (+ an "
                    f"override flag/env) so prod replication can rebind it."
                )
            if cred_type not in resolved:
                resolved[cred_type] = resolve_cred(cred_type)
            cid, cname = resolved[cred_type]
            nd["credentials"][cred_type] = {"id": cid, "name": cname}
    return {
        "name": wf["name"],
        "nodes": wf["nodes"],
        "connections": wf["connections"],
        "settings": wf.get("settings", {"executionOrder": "v1"}),
    }


def deploy(update_id=None, id_file=None, dry_run=False):
    payload = canonical_payload()
    if dry_run:
        # Offline preview of exactly what would be PUT/POSTed (creds already rebound).
        print(json.dumps(payload, indent=2, ensure_ascii=False))
        return None
    if update_id:
        code, resp = req("PUT", f"/workflows/{update_id}", payload)
        wid = update_id
        action = "updated"
    else:
        code, resp = req("POST", "/workflows", payload)
        wid = resp.get("id")
        action = "created"
    if code not in (200, 201) or not wid:
        print(f"DEPLOY FAILED (HTTP {code}): {json.dumps(resp)[:800]}")
        sys.exit(1)
    print(f"workflow {action}: id={wid}")
    # Set Reply Mode is the SHARED always-active webhook (not a per-bot bot) — activating it
    # on deploy is by design (the per-bot INACTIVE-clone policy applies to bot clones only).
    ac, ar = req("POST", f"/workflows/{wid}/activate")
    if ac != 200:
        print(f"ACTIVATE FAILED (HTTP {ac}): {json.dumps(ar)[:400]}")
        sys.exit(1)
    print(f"activated: {wid}")
    if id_file:
        with open(id_file, "w") as f:
            f.write(wid)
    time.sleep(2)  # let the production webhook path register
    print(f"webhook: {BASE}/webhook/SetReplyMode")
    return wid


def export_canonical(wid, out_path):
    code, wf = req("GET", f"/workflows/{wid}")
    if code != 200:
        print(f"EXPORT GET FAILED (HTTP {code})")
        sys.exit(1)
    canonical = {
        "name": wf["name"],
        "nodes": wf["nodes"],
        "connections": wf["connections"],
        "settings": wf.get("settings", {"executionOrder": "v1"}),
        "staticData": wf.get("staticData"),
        "pinData": wf.get("pinData") or {},
        "triggerCount": wf.get("triggerCount", 1),
        "meta": wf.get("meta", {}) or {},
        "id": wf["id"],
        "active": wf.get("active", True),
    }
    with open(out_path, "w") as f:
        json.dump(canonical, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"exported canonical -> {out_path}")


def main():
    ap = argparse.ArgumentParser(
        description="Deploy/export the Set Reply Mode workflow from its committed canonical JSON."
    )
    ap.add_argument("--update", dest="update_id", default=None,
                    help="PUT the canonical content onto this existing workflow id "
                         "(creds rebound in place); default is POST-create.")
    ap.add_argument("--dry-run", action="store_true",
                    help="print the exact deploy payload (creds rebound) and exit; no network.")
    ap.add_argument("--id-file", default=None)
    ap.add_argument("--export", nargs=2, metavar=("ID", "OUT"), default=None)
    ap.add_argument(
        "--postgres-cred", default=None,
        help="target Postgres credential id to bind (else env N8N_POSTGRES_CRED_ID, else the "
             f"default {DEFAULT_POSTGRES_CRED_ID}). Bound by id because two creds are named "
             "'Postgres' (C5) — never resolved by name.",
    )
    args = ap.parse_args()
    # flag > env > default; always set so resolve_cred never guesses by name (C5).
    CRED_OVERRIDES["postgres"] = (
        args.postgres_cred
        or os.environ.get("N8N_POSTGRES_CRED_ID")
        or DEFAULT_POSTGRES_CRED_ID
    )
    if args.export:
        export_canonical(args.export[0], args.export[1])
        return
    deploy(update_id=args.update_id, id_file=args.id_file, dry_run=args.dry_run)


if __name__ == "__main__":
    main()
