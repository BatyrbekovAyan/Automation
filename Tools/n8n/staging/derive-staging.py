#!/usr/bin/env python3
"""Derive the isolated STAGING workflow set from the canonical Tools/n8n/workflows/*.json.

Staging = a private n8n home (default ~/.n8n-staging) + a private Postgres + the Wappi MOCK
(Tools/n8n/staging/wappi-mock.py). This script never touches the canonical files; it writes the
derived copies to Tools/n8n/workflows-staging/ (gitignored) and refuses to produce anything that
could reach production or a real customer:

  * every n8n self-call  https://n8n.choosereply.com/{api/v1,webhook}/ -> http://localhost:<port>/...
  * every Wappi call     https://wappi.pro/                             -> <mock base>/
  * credentials are re-bound BY NAME to the staging instance's own credentials; the Postgres
    binding is remapped to the credential named "Postgres Staging" and the run ABORTS if that
    credential does not exist (a staging set bound to the shared prod DB is the failure mode
    this guard exists for).
  * --bot wa:<profileId>[:<name>] / --bot tg:<profileId>[:<name>] additionally emits a STAGING
    BOT CLONE the way the Create handlers do (template + webhook path = profile id + name),
    with a deterministic id so re-runs upsert instead of multiplying workflows.

    python3 Tools/n8n/staging/derive-staging.py --bot wa:stgwa001 --bot tg:stgtg001
    N8N_STAGING_HOME=~/.n8n-staging N8N_STAGING_PORT=5678 WAPPI_MOCK_BASE=http://127.0.0.1:8765 ...
Then (n8n stopped):  <staging n8n> import:workflow --separate --input=Tools/n8n/workflows-staging/
"""
import argparse
import hashlib
import json
import os
import sqlite3
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
SRC = os.path.join(REPO, "Tools/n8n/workflows")
OUT = os.path.join(REPO, "Tools/n8n/workflows-staging")
HOME = os.path.expanduser(os.environ.get("N8N_STAGING_HOME", "~/.n8n-staging"))
PORT = os.environ.get("N8N_STAGING_PORT", "5678")
MOCK = os.environ.get("WAPPI_MOCK_BASE", "http://127.0.0.1:8765").rstrip("/")

CANON_HOST = "https://n8n.choosereply.com"
LOCAL_HOST = f"http://localhost:{PORT}"
WAPPI = "https://wappi.pro"
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


def derive_file(text):
    text = text.replace(CANON_HOST + "/api/v1", LOCAL_HOST + "/api/v1")
    text = text.replace(CANON_HOST + "/webhook/", LOCAL_HOST + "/webhook/")
    text = text.replace(WAPPI + "/", MOCK + "/")
    text = text.replace("/activate ", "/activate")
    return text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bot", action="append", default=[], help="wa:<profileId>[:<name>] or tg:<profileId>[:<name>]")
    ap.add_argument("--prompt-file", help="optional systemMessage text file for the staging bot clones")
    args = ap.parse_args()

    creds = staging_credentials()
    if PG_STAGING_NAME not in creds:
        sys.exit(f"REFUSED: staging n8n has no credential named {PG_STAGING_NAME!r} — a derived set would bind the "
                 f"shared production Postgres. Import it first (staging/README.md).")
    os.makedirs(OUT, exist_ok=True)
    for stale in os.listdir(OUT):
        if stale.endswith(".json"):
            os.remove(os.path.join(OUT, stale))
    missing = set()
    manifest = {"n8nHome": HOME, "n8nBase": LOCAL_HOST, "wappiMock": MOCK, "files": [], "bots": []}
    for f in sorted(x for x in os.listdir(SRC) if x.endswith(".json")):
        wf = json.loads(derive_file(open(os.path.join(SRC, f), encoding="utf-8").read()))
        for node in wf.get("nodes", []):
            remap(node, creds, missing)
        wf["active"] = False
        text = json.dumps(wf, ensure_ascii=False)
        for forbidden in (CANON_HOST, WAPPI, "wappi.pro"):
            if forbidden in text:
                sys.exit(f"REFUSED: {f} still references {forbidden} after derivation")
        with open(os.path.join(OUT, f), "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
        manifest["files"].append(f)
    prompt = open(args.prompt_file, encoding="utf-8").read() if args.prompt_file else None
    for spec in args.bot:
        parts = spec.split(":", 2)
        channel, profile_id = parts[0], parts[1]
        name = parts[2] if len(parts) > 2 else f"STAGING {channel.upper()} {profile_id}"
        tpl = json.loads(derive_file(open(os.path.join(SRC, TEMPLATES[channel]), encoding="utf-8").read()))
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
