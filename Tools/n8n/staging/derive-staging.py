#!/usr/bin/env python3
"""Derive the isolated STAGING workflow set from the canonical Tools/n8n/workflows/*.json.

Staging = a private n8n home (default ~/.n8n-staging) + a private Postgres + the Wappi MOCK
(Tools/n8n/staging/wappi-mock.py). This script never touches the canonical files; it writes the
derived copies to Tools/n8n/workflows-staging/ (gitignored) and refuses to produce anything that
could reach production or a real customer:

  * every n8n self-call  https://n8n.choosereply.com/{api/v1,webhook}/ -> http://localhost:<port>/...
  * every Wappi call     https://wappi.pro/                             -> <mock base>/
  * every RevenueCat API call https://api.revenuecat.com/                -> <mock base>/revenuecat/ (A3 identity-heal: the
    mock answers v2 …/customers/<id>/aliases from what a scenario configured; the real API is never asked)
  * credentials are re-bound BY NAME to the staging instance's own credentials; the Postgres
    binding is remapped to the credential named "Postgres Staging" and the run ABORTS if that
    credential does not exist (a staging set bound to the shared prod DB is the failure mode
    this guard exists for).
  * nothing writes to the real Supabase project the staging `Supabase` credential points at: the vector-store
    INSERT becomes an insert into the staging `documents` stub, Storage uploads/deletes become NoOps
    (isolate_supabase_writers) and the run ABORTS if any writer is left (retrieval stays: read-only)
  * --bot wa:<profileId>[:<name>] / --bot tg:<profileId>[:<name>] additionally emits a STAGING
    BOT CLONE the way the Create handlers do (template + webhook path = profile id + name),
    with a deterministic id so re-runs upsert instead of multiplying workflows.
  * --canon-ref <git ref> derives from the canon AT THAT REF (Tools/n8n/workflows/*.json read with `git archive`
    into a temporary directory) instead of the working tree — how a staging scenario is first run against the canon
    BEFORE a change («fails on HEAD», F1F2-plan T10). The derivation rules, isolation guards and STAGING-ONLY workflows
    stay the working tree's; restore with a plain re-run (no --canon-ref).

    python3 Tools/n8n/staging/derive-staging.py --bot wa:stgwa001 --bot tg:stgtg001
    N8N_STAGING_HOME=~/.n8n-staging N8N_STAGING_PORT=5678 WAPPI_MOCK_BASE=http://127.0.0.1:8765 ...
Then (n8n stopped):  <staging n8n> import:workflow --separate --input=Tools/n8n/workflows-staging/
"""
import argparse
import hashlib
import json
import os
import shutil
import sqlite3
import subprocess
import sys
import tempfile

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
SRC = os.path.join(REPO, "Tools/n8n/workflows")
OUT = os.path.join(REPO, "Tools/n8n/workflows-staging")
HOME = os.path.expanduser(os.environ.get("N8N_STAGING_HOME", "~/.n8n-staging"))
PORT = os.environ.get("N8N_STAGING_PORT", "5678")
MOCK = os.environ.get("WAPPI_MOCK_BASE", "http://127.0.0.1:8765").rstrip("/")

CANON_HOST = "https://n8n.choosereply.com"
LOCAL_HOST = f"http://localhost:{PORT}"
WAPPI = "https://wappi.pro"
RC_API = "https://api.revenuecat.com"   # A3 (2026-10-14): Verify Identity's RC Aliases -> the mock's /revenuecat/ routes
TEMPLATES = {"wa": "4wYitz5ek30SVNlT-WhatsApp_Bot.json", "tg": "4VN3gsFaC2HUYmcc-Telegram_Bot.json"}
PG_STAGING_NAME = "Postgres Staging"
# Deliberately ABSENT on staging: the Support Message relay must never reach the owner's real support
# chat, so its Telegram credential is left dangling (the node fails, nothing is sent).
ALLOWED_MISSING = {"Support Bot"}


def staging_credentials():
    db = os.path.join(HOME, ".n8n", "database.sqlite")   # N8N_USER_FOLDER keeps state under <home>/.n8n/
    if not os.path.exists(db):
        sys.exit(f"staging n8n DB not found at {db} — start the staging n8n once (see staging/README.md)")
    con = sqlite3.connect(db)
    rows = con.execute("SELECT id, name, type FROM credentials_entity").fetchall()
    con.close()
    by_name = {}
    for cid, name, ctype in rows:
        by_name.setdefault(name, (cid, ctype))
    return by_name


def remap(node, creds, missing):
    for ctype, ref in (node.get("credentials") or {}).items():
        if not isinstance(ref, dict):
            continue
        name = ref.get("name")
        target = PG_STAGING_NAME if ctype == "postgres" else name
        if target in creds:
            ref["id"] = creds[target][0]
            ref["name"] = target
        else:
            missing.add(target)


def staging_bot_id(channel, profile_id):
    # 16-char n8n-style id, deterministic per (channel, profile) so re-runs upsert.
    h = hashlib.sha1(f"{channel}:{profile_id}".encode()).hexdigest()
    return ("stg" + channel + h)[:16]


SUPABASE_STORAGE = ".supabase.co/storage/"
STAGING_DOCS_SQL = """-- STAGING ONLY (derive-staging.py): stands in for the Supabase Vector Store INSERT so an upload lands in the
-- staging `documents` stub (staging/sql/2026-09-17-documents-stub.sql) instead of the real project; one row per
-- upload (no splitting / embedding — nothing here calls OpenAI either).
insert into documents (content, metadata) values ($1, $2::jsonb) returning id;"""


STAGING_STORAGE_DELETE_JS = """// STAGING ONLY (derive-staging.py): stands in for a Supabase Storage DELETE — nothing is sent; each item gets the
// full response a successful delete gives (fullResponse + neverError: statusCode 200), so the status-judging node after
// it (Delete Bot Files' Originals Settled) sees a settled delete.
return $input.all().map((it, i) => ({ json: { ...it.json, statusCode: 200, body: { message: 'staging: not sent' } }, pairedItem: { item: i } }));"""


def _expr_body(v):
    """'={{ X }}' -> '(X)'; a literal -> its JSON literal (for composing one staging expression)."""
    if isinstance(v, str) and v.startswith("={{") and v.rstrip().endswith("}}"):
        return "(" + v.strip()[3:-2].strip() + ")"
    return json.dumps(v, ensure_ascii=False)


def isolate_supabase_writers(wf):
    """The staging instance carries the dev account's `Supabase` credential (service role on a REAL project), and
    Upload File / Delete File / Delete Bot Files WRITE through it (vector-store insert, storage upload/delete). A
    staging run must never write test data there (remediation rule), so the writers are replaced here — staging
    copies only, the canonical files are untouched:
      * Supabase Vector Store in INSERT mode -> a Postgres insert into the staging `documents` stub carrying the
        same metadata the Data Loader would stamp; its LangChain sub-nodes (embeddings, loader, splitter) go;
      * every non-GET HTTP call to Supabase Storage -> a NoOp of the same name (connections stay valid).
    Retrieval (load / retrieve-as-tool) stays: read-only, filtered by test bot ids no chunk carries (README)."""
    nodes, conns = wf.get("nodes", []), wf.get("connections", {})
    by_name = {n["name"]: n for n in nodes}
    replaced = []
    for n in list(nodes):
        p = n.get("parameters") or {}
        if n["type"] == "n8n-nodes-base.httpRequest" and SUPABASE_STORAGE in str(p.get("url", "")) and str(p.get("method", "GET")).upper() == "DELETE":
            # V12 (2026-10-07): Delete Bot Files judges each storage delete by its status before it deletes anything else
            # (Originals Settled) — a NoOp would answer «no status» = failure. The stand-in answers what a successful
            # delete does; it still sends nothing anywhere.
            nodes[nodes.index(n)] = {"parameters": {"jsCode": STAGING_STORAGE_DELETE_JS}, "id": n["id"], "name": n["name"],
                                     "type": "n8n-nodes-base.code", "typeVersion": 2, "position": n["position"],
                                     "notes": "STAGING: Supabase Storage delete neutralised by derive-staging.py (answers 200)"}
            replaced.append(n["name"])
        elif n["type"] == "n8n-nodes-base.httpRequest" and SUPABASE_STORAGE in str(p.get("url", "")) and str(p.get("method", "GET")).upper() != "GET":
            nodes[nodes.index(n)] = {"parameters": {}, "id": n["id"], "name": n["name"], "type": "n8n-nodes-base.noOp", "typeVersion": 1,
                                     "position": n["position"], "notes": "STAGING: Supabase Storage write neutralised by derive-staging.py"}
            replaced.append(n["name"])
        elif n["type"] == "@n8n/n8n-nodes-langchain.vectorStoreSupabase" and p.get("mode") == "insert":
            subs, frontier = set(), {n["name"]}
            while frontier:                                   # every LangChain sub-node feeding this insert, transitively
                nxt = set()
                for src, outs in conns.items():
                    for kind, lists in outs.items():
                        if kind != "main" and any(c["node"] in frontier for out in lists for c in out) and src not in subs:
                            nxt.add(src)
                subs |= nxt
                frontier = nxt
            meta = {}
            for s_name in subs:
                for mv in (((by_name[s_name].get("parameters") or {}).get("options") or {}).get("metadata") or {}).get("metadataValues", []):
                    meta[mv["name"]] = mv["value"]
            meta_js = "{ " + ", ".join(f"{json.dumps(k)}: {_expr_body(v)}" for k, v in meta.items()) + " }"
            nodes[nodes.index(n)] = {"parameters": {"operation": "executeQuery", "query": STAGING_DOCS_SQL,
                                                    "options": {"queryReplacement": "={{ [ String($json.content || ''), JSON.stringify(" + meta_js + ") ] }}"}},
                                     "id": n["id"], "name": n["name"], "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
                                     "position": n["position"], "credentials": {"postgres": {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}},
                                     "notes": "STAGING: Supabase vector-store insert redirected to the staging documents stub by derive-staging.py"}
            wf["nodes"] = nodes = [x for x in nodes if x["name"] not in subs]
            for s_name in subs:
                conns.pop(s_name, None)
            replaced.append(n["name"] + f" (+{len(subs)} sub-nodes)")
    return replaced


PRICE_SEARCH_ID = "crPriceSearch001"
STAGING_PRICE_SEARCH_SQL = """-- STAGING ONLY (derive-staging.py): the staging `documents` stub has no embeddings, so the price-list search returns
-- the bot's own rows instead — newest first, the same top 10, in the shape the vector store's load mode emits. The first
-- parameter is the metadata key (botWaId / botTgId), the second the bot's workflow id; a sentinel id matches nothing.
select json_build_object('pageContent', content, 'metadata', metadata) as document
from documents
where metadata->>$1 = $2 and $2 not in ('-1', '')
order by id desc
limit 10;"""


def isolate_supabase_readers(wf):
    """V9 (2026-09-28): the price-list search (crPriceSearch001) is what reconciles a price-list row with the catalog, and
    a staging run has to SEE that — but its vector search reads the real Supabase project (no staging bot id owns a chunk
    there) and the staging `documents` stub carries no embeddings. So, in the staging copy of THAT workflow only, the
    search reads the stub through the staging Postgres (same output shape, same filter by key + bot id) and its
    embedding sub-node goes. Every other retrieval keeps reading the real project read-only (README)."""
    if wf.get("id") != PRICE_SEARCH_ID:
        return []
    nodes, conns = wf.get("nodes", []), wf.get("connections", {})
    n = next((x for x in nodes if x["name"] == "Search Price Lists" and x["type"] == "@n8n/n8n-nodes-langchain.vectorStoreSupabase"), None)
    if n is None:
        return []
    subs = {src for src, outs in conns.items() for kind, lists in outs.items()
            if kind != "main" and any(c["node"] == n["name"] for out in lists for c in out)}
    nodes[nodes.index(n)] = {"parameters": {"operation": "executeQuery", "query": STAGING_PRICE_SEARCH_SQL,
                                            "options": {"queryReplacement": "={{ [ $json.filterKey, $json.botId ] }}"}},
                             "id": n["id"], "name": n["name"], "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
                             "position": n["position"], "credentials": {"postgres": {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}},
                             "alwaysOutputData": True, "onError": "continueRegularOutput",
                             "notes": "STAGING: price-list search reads the staging documents stub (derive-staging.py)"}
    wf["nodes"] = [x for x in nodes if x["name"] not in subs]
    for s_name in subs:
        conns.pop(s_name, None)
    return [n["name"] + f" (+{len(subs)} sub-nodes)"]


def supabase_writers_left(wf):
    return [n["name"] for n in wf.get("nodes", [])
            if (n["type"] == "@n8n/n8n-nodes-langchain.vectorStoreSupabase" and (n.get("parameters") or {}).get("mode") in ("insert", "update"))
            or (n["type"] == "n8n-nodes-base.httpRequest" and SUPABASE_STORAGE in str((n.get("parameters") or {}).get("url", ""))
                and str((n.get("parameters") or {}).get("method", "GET")).upper() != "GET")]


def canon_at_ref(ref):
    """Tools/n8n/workflows/*.json exactly as committed at `ref`, extracted into a fresh temporary directory (the caller
    removes it). Fails loudly on an unknown ref or a ref without the canon — never falls back to the working tree."""
    tmp = tempfile.mkdtemp(prefix="canon-ref-")
    arch = subprocess.run(["git", "-C", REPO, "archive", "--format=tar", ref, "Tools/n8n/workflows"], capture_output=True)
    if arch.returncode != 0:
        shutil.rmtree(tmp, ignore_errors=True)
        sys.exit(f"REFUSED: git archive {ref} Tools/n8n/workflows failed: {arch.stderr.decode(errors='replace').strip()}")
    untar = subprocess.run(["tar", "-x", "-C", tmp], input=arch.stdout, capture_output=True)
    src = os.path.join(tmp, "Tools/n8n/workflows")
    if untar.returncode != 0 or not os.path.isdir(src) or not any(f.endswith(".json") for f in os.listdir(src)):
        shutil.rmtree(tmp, ignore_errors=True)
        sys.exit(f"REFUSED: no canonical workflows at {ref}")
    return tmp, src


def derive_file(text):
    text = text.replace(CANON_HOST + "/api/v1", LOCAL_HOST + "/api/v1")
    text = text.replace(CANON_HOST + "/webhook/", LOCAL_HOST + "/webhook/")
    text = text.replace(WAPPI + "/", MOCK + "/")
    text = text.replace(RC_API + "/", MOCK + "/revenuecat/")
    text = text.replace("/activate ", "/activate")
    return text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bot", action="append", default=[], help="wa:<profileId>[:<name>] or tg:<profileId>[:<name>]")
    ap.add_argument("--prompt-file", help="optional systemMessage text file for the staging bot clones")
    ap.add_argument("--canon-ref", help="derive from the canonical workflows committed at this git ref (default: the working tree)")
    args = ap.parse_args()
    tmp = None
    try:
        if args.canon_ref:
            tmp, src = canon_at_ref(args.canon_ref)
            print(f"canon: {args.canon_ref} (git archive -> {src})")
        else:
            src = SRC
        derive(args, src)
    finally:
        if tmp:
            shutil.rmtree(tmp, ignore_errors=True)


def derive(args, src):
    """Write the derived staging set from the canonical workflows in `src` (the working tree or a --canon-ref extract)."""
    creds = staging_credentials()
    if PG_STAGING_NAME not in creds:
        sys.exit(f"REFUSED: staging n8n has no credential named {PG_STAGING_NAME!r} — a derived set would bind the "
                 f"shared production Postgres. Import it first (staging/README.md).")
    os.makedirs(OUT, exist_ok=True)
    for stale in os.listdir(OUT):
        if stale.endswith(".json"):
            os.remove(os.path.join(OUT, stale))
    missing = set()
    manifest = {"n8nHome": HOME, "n8nBase": LOCAL_HOST, "wappiMock": MOCK, "canonRef": args.canon_ref or "worktree", "files": [], "bots": []}
    # Canonical workflows + the STAGING-ONLY ones (Tools/n8n/staging/workflows/, e.g. the stage-6 eval
    # runner) — the latter never reach prod because every deploy tool reads Tools/n8n/workflows/ only.
    STAGING_ONLY = os.path.join(os.path.dirname(os.path.abspath(__file__)), "workflows")
    sources = [(src, f) for f in sorted(x for x in os.listdir(src) if x.endswith(".json"))]
    if os.path.isdir(STAGING_ONLY):
        sources += [(STAGING_ONLY, f) for f in sorted(x for x in os.listdir(STAGING_ONLY) if x.endswith(".json"))]
    for src_dir, f in sources:
        wf = json.loads(derive_file(open(os.path.join(src_dir, f), encoding="utf-8").read()))
        isolated_writers = isolate_supabase_writers(wf)
        if isolated_writers:
            print(f"staging isolation: {f}: {isolated_writers}")
        redirected = isolate_supabase_readers(wf)
        if redirected:
            print(f"staging price search: {f}: {redirected}")
        for node in wf.get("nodes", []):
            remap(node, creds, missing)
        wf["active"] = False
        text = json.dumps(wf, ensure_ascii=False)
        for forbidden in (CANON_HOST, WAPPI, "wappi.pro", RC_API, "api.revenuecat.com"):
            if forbidden in text:
                sys.exit(f"REFUSED: {f} still references {forbidden} after derivation")
        if supabase_writers_left(wf):
            sys.exit(f"REFUSED: {f} would still WRITE to the real Supabase project: {supabase_writers_left(wf)}")
        with open(os.path.join(OUT, f), "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
        manifest["files"].append(f)
    prompt = open(args.prompt_file, encoding="utf-8").read() if args.prompt_file else None
    for spec in args.bot:
        parts = spec.split(":", 2)
        channel, profile_id = parts[0], parts[1]
        name = parts[2] if len(parts) > 2 else f"STAGING {channel.upper()} {profile_id}"
        tpl = json.loads(derive_file(open(os.path.join(src, TEMPLATES[channel]), encoding="utf-8").read()))
        for node in tpl.get("nodes", []):
            remap(node, creds, missing)
        tpl["id"] = staging_bot_id(channel, profile_id)
        tpl["name"] = name
        tpl["active"] = False
        tpl["nodes"][0]["parameters"]["path"] = profile_id     # the Create handler's own convention
        if prompt is not None:
            agent = next(n for n in tpl["nodes"] if n["name"] == "AI Agent")
            agent["parameters"]["options"]["systemMessage"] = "=" + prompt
        out = os.path.join(OUT, f"{tpl['id']}-{name.replace(' ', '_')}.json")
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(tpl, fh, indent=2, ensure_ascii=False)
        manifest["bots"].append({"channel": channel, "profileId": profile_id, "workflowId": tpl["id"],
                                 "webhook": f"{LOCAL_HOST}/webhook/{profile_id}", "file": os.path.basename(out)})
    with open(os.path.join(OUT, "_manifest.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=1, ensure_ascii=False)
    print(f"{len(manifest['files'])} workflows + {len(manifest['bots'])} staging bots -> {OUT}")
    print(f"n8n base {LOCAL_HOST}; Wappi -> {MOCK}; postgres -> {PG_STAGING_NAME!r} ({creds[PG_STAGING_NAME][0]})")
    isolated = sorted(missing & ALLOWED_MISSING)
    missing -= ALLOWED_MISSING
    if isolated:
        print(f"note: left dangling on purpose (isolation): {isolated}")
    if missing:
        print(f"\nWARNING credentials referenced but NOT found in staging (create them, re-run): {sorted(missing)}")
        sys.exit(2)


if __name__ == "__main__":
    main()
