#!/usr/bin/env python3
"""Deploy + export the shared "Suggest Replies" n8n workflow from its canonical JSON.

The 12th canonical workflow: a shared, always-active webhook (POST /webhook/SuggestReplies)
that takes the frozen v1 request contract, short-circuits known-invalid requests straight to
the `generation_failed` payload (unauthenticated webhook — garbage must never cost an LLM
call), optionally runs tenant-scoped RAG pre-retrieval — channel-branched since phase 4:
`If channel TG?` routes to `Retrieve RAG TG` (single `botTgId` filter) or `Retrieve RAG`
(single `botWaId` filter), topK 5 — calls one LLM (gpt-4o-mini, strict structured JSON),
validates the output (exactly 4 distinct enum-labeled moves, hard-clamped, markdown-stripped,
one retry then a safe error payload), and responds in-band echoing requestSeq.

This script does NOT generate the workflow. The single source of truth is the committed
canonical export `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` — the phase-4
channel branch, the 08-13 D10 «РЕЛЕВАНТНОСТЬ» newest-incoming anchor, and every future
review fix land THERE (gated by verify-telegram-parity.py). Deploy imports that JSON
verbatim and rebinds ONLY the credential ids for the target instance. The old
`--stage front/full` Python node literals predated both fixes and deploying them silently
reverted both — they are retired and now fail loudly.

Usage:
  python3 Tools/n8n/build-suggest-replies.py --dry-run             # print the exact payload; no network
  python3 Tools/n8n/build-suggest-replies.py                       # POST-create on the target, then activate
  python3 Tools/n8n/build-suggest-replies.py --update <id>         # PUT the same content onto an existing id
  python3 Tools/n8n/build-suggest-replies.py --export <id> <out.json>

Reads the n8n API key from Assets/StreamingAssets/secrets.json (n8nAPIKey) or env
N8N_API_KEY; targets the local dev instance (http://localhost:5678) unless N8N_BASE_URL
is set (prod: https://bagkz.app.n8n.cloud).

Credential binding per type, in precedence order — an explicit override short-circuits the
lookup; there is NO silent fallback to pinned dev ids (deploying dev ids onto a no-SQLite
Cloud target is exactly the failure the prod runbook's step-5 overrides exist to prevent):
  --openai-cred ID   / env N8N_OPENAI_CRED_ID     OpenAi credential id on the target
  --supabase-cred ID / env N8N_SUPABASE_CRED_ID   Supabase credential id on the target
  else: exact-NAME lookup in the local dev SQLite (misnamed -> loud error listing candidates)
  else: loud error naming the missing flag/env.
"""
import argparse
import json
import os
import sqlite3
import sys
import time
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SECRETS = os.path.join(REPO, "Assets/StreamingAssets/secrets.json")
DEV_DB = os.path.expanduser("~/.n8n/database.sqlite")
BASE = os.environ.get("N8N_BASE_URL", "http://localhost:5678").rstrip("/")
CANONICAL = os.path.join(HERE, "workflows", "9PTyYcelRQI7bGDb-Suggest_Replies.json")

# Credential NAME pins per type. The id is the dev-instance reference (traceability only —
# it is never deployed silently); the NAME is what resolve_cred() looks up in the target's
# SQLite and what prod recreates in the Credentials UI (runbook step 2).
CRED_PINS = {
    "openAiApi": ("WNHwKWlO2E9OClkA", "OpenAi account"),
    "supabaseApi": ("vrywn6AxQMlvbbzC", "Supabase"),
}
OVERRIDE_HINT = {
    "openAiApi": "--openai-cred / env N8N_OPENAI_CRED_ID",
    "supabaseApi": "--supabase-cred / env N8N_SUPABASE_CRED_ID",
}

# Explicit credential-id overrides. main() fills this from the --openai-cred /
# --supabase-cred flags or the N8N_OPENAI_CRED_ID / N8N_SUPABASE_CRED_ID env vars
# (flag > env). Maps cred_type -> id; an entry short-circuits resolve_cred()'s SQLite
# lookup — the intended path for a no-SQLite Cloud deploy. Empty on the dev path.
CRED_OVERRIDES = {}


def api_key():
    k = os.environ.get("N8N_API_KEY")
    if k:
        return k
    with open(SECRETS) as f:
        return json.load(f)["n8nAPIKey"]


def resolve_cred(cred_type):
    """Return (id, name) for a credential, matched by exact NAME from the live DB.

    The wanted name is pinned in CRED_PINS. Precedence: an explicit CRED_OVERRIDES entry
    (prod --*-cred flag / env var) is used verbatim; else the local dev SQLite is searched
    for this type by exact name — a present-but-misnamed credential fails LOUDLY listing
    the candidates (silently binding whichever sorts first would point the workflow at the
    wrong account/project with no error). No override and no readable DB is a hard error
    too: silently deploying the pinned DEV ids onto another instance (Cloud has no SQLite)
    is the exact trap the runbook step-5 overrides exist to close.
    """
    dev_ref_id, want_name = CRED_PINS[cred_type]
    override = CRED_OVERRIDES.get(cred_type)
    if override:
        # Explicit target id (credential recreated BY NAME on the target): use verbatim,
        # keep the pinned name, and skip the SQLite lookup entirely.
        return override, want_name
    if os.path.exists(DEV_DB):
        try:
            con = sqlite3.connect(DEV_DB)
            row = con.execute(
                "SELECT id, name FROM credentials_entity WHERE type=? AND name=? LIMIT 1",
                (cred_type, want_name),
            ).fetchone()
            candidates = None
            if not row:
                candidates = con.execute(
                    "SELECT id, name FROM credentials_entity WHERE type=?", (cred_type,)
                ).fetchall()
            con.close()
        except Exception as e:
            raise SystemExit(
                f"cannot read {DEV_DB} ({e}) and no explicit id for {cred_type!r} — "
                f"pass {OVERRIDE_HINT[cred_type]}."
            )
        if row:
            return row[0], row[1]
        listing = ", ".join(f"{cid} ({cname!r})" for cid, cname in candidates) or "(none of this type)"
        raise SystemExit(
            f"credential type {cred_type!r} named {want_name!r} not found in {DEV_DB}; "
            f"candidates: {listing}. Rename the credential on the instance (or update "
            f"CRED_PINS) — refusing to guess."
        )
    raise SystemExit(
        f"no local n8n SQLite DB at {DEV_DB} and no explicit id for {cred_type!r} — a "
        f"Cloud/remote target must pass {OVERRIDE_HINT[cred_type]} (the id of the "
        f"credential recreated BY NAME as {want_name!r}; see the prod runbook step 2). "
        f"Refusing to silently deploy the pinned DEV id {dev_ref_id!r}."
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

    Returns the exact {name, nodes, connections, settings} deploy payload (the same shape
    the runbook's step-4 imports use). Node content is NEVER regenerated here — the
    canonical file is the single source of truth (channel branching, the D10 relevance
    anchor, every future fix). Only per-instance credential bindings are rewritten, and
    only for types pinned in CRED_PINS; on the dev machine the by-name lookup resolves to
    the ids already in the file, so the payload is byte-identical to the committed
    canonical. A credential type in the file but NOT in CRED_PINS is a hard error —
    otherwise its dev id would ride along silently onto prod.
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
    # Suggest_Replies is the SHARED always-active webhook (not a per-bot bot) — activating
    # it on deploy is by design (bot-activation policy applies to per-bot clones only).
    ac, ar = req("POST", f"/workflows/{wid}/activate")
    if ac != 200:
        print(f"ACTIVATE FAILED (HTTP {ac}): {json.dumps(ar)[:400]}")
        sys.exit(1)
    print(f"activated: {wid}")
    if id_file:
        with open(id_file, "w") as f:
            f.write(wid)
    time.sleep(2)  # let the production webhook path register
    print(f"webhook: {BASE}/webhook/SuggestReplies")
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
        description="Deploy/export the Suggest Replies workflow from its committed canonical JSON."
    )
    # Retired flag kept only to fail loudly (the old runbook command carried --stage full).
    ap.add_argument("--stage", default=None, help=argparse.SUPPRESS)
    ap.add_argument("--update", dest="update_id", default=None,
                    help="PUT the canonical content onto this existing workflow id "
                         "(creds rebound in place); default is POST-create.")
    ap.add_argument("--dry-run", action="store_true",
                    help="print the exact deploy payload (creds rebound) and exit; no network.")
    ap.add_argument("--id-file", default=None)
    ap.add_argument("--export", nargs=2, metavar=("ID", "OUT"), default=None)
    ap.add_argument(
        "--openai-cred", default=None,
        help="target OpenAi credential id to bind (else env N8N_OPENAI_CRED_ID, else "
             "SQLite-by-name, else a loud error). For a Cloud target with no local SQLite.",
    )
    ap.add_argument(
        "--supabase-cred", default=None,
        help="target Supabase credential id to bind (else env N8N_SUPABASE_CRED_ID, else "
             "SQLite-by-name, else a loud error). For a Cloud target with no local SQLite.",
    )
    args = ap.parse_args()
    if args.stage is not None:
        raise SystemExit(
            "--stage front/full is retired: the Python node literals predated the phase-4 "
            "channel branch and the 08-13 D10 «РЕЛЕВАНТНОСТЬ» fix, and deploying them "
            "silently reverted both. The deployer now imports the committed canonical JSON "
            f"({os.path.relpath(CANONICAL, REPO)}) verbatim and only rebinds credential "
            "ids — rerun without --stage (add --dry-run to inspect the payload first)."
        )
    # flag > env; only set an override when one is actually supplied.
    oa_cred = args.openai_cred or os.environ.get("N8N_OPENAI_CRED_ID")
    sb_cred = args.supabase_cred or os.environ.get("N8N_SUPABASE_CRED_ID")
    if oa_cred:
        CRED_OVERRIDES["openAiApi"] = oa_cred
    if sb_cred:
        CRED_OVERRIDES["supabaseApi"] = sb_cred
    if args.export:
        export_canonical(args.export[0], args.export[1])
        return
    deploy(update_id=args.update_id, id_file=args.id_file, dry_run=args.dry_run)


if __name__ == "__main__":
    main()
