#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Stage 7.7 — push the canonical exports of EXISTING workflows to an n8n instance, one PUT each, with
the same discipline as update-clones.py: dry-run by default, explicit --apply --yes, the pre-update
versionId printed as the rollback pointer, an active workflow re-published (deactivate → activate),
and a READ-BACK after every PUT (graph hash == canonical, activeVersionId == versionId when active).
Scope: workflows that already exist on the instance (DIFF in verify-deployed.py). New workflows are
imported with `n8n import:workflow` (ids must be preserved) — not here. The two placeholder workflows
(Set Workflow State `__HDR_CRED__`, Support Message `__TG_CRED__`/`__CHAT_ID__`) are deployed by
build-client-webhooks.py --update and are SKIPPED here unless --include-placeholders. A workflow whose Postgres SQL
fails verify-sql-comments.py (V1: a parameter that could leave its literal) is refused before the first request.
    python3 Tools/n8n/deploy-canonical.py                       # dry-run on prod: what differs, what would be PUT
    python3 Tools/n8n/deploy-canonical.py --only <id> --apply --yes
    python3 Tools/n8n/deploy-canonical.py --apply --yes         # every DIFF canonical workflow, in file order
"""
import argparse
import copy
import glob
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("uc", os.path.join(HERE, "update-clones.py"))
uc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(uc)
_vspec = importlib.util.spec_from_file_location("vd", os.path.join(HERE, "verify-deployed.py"))
vd = importlib.util.module_from_spec(_vspec)
_vspec.loader.exec_module(vd)
_gspec = importlib.util.spec_from_file_location("sql_guard", os.path.join(HERE, "verify-sql-comments.py"))
sql_guard = importlib.util.module_from_spec(_gspec)
_gspec.loader.exec_module(sql_guard)

PLACEHOLDER_IDS = {"0DP79STI3pafXO9B", "olmqWMfcFpCEcpiE"}   # build-client-webhooks.py --update owns these


def canonical_files():
    out = {}
    for path in sorted(glob.glob(os.path.join(HERE, "workflows", "*.json"))):
        wf = json.load(open(path, encoding="utf-8"))
        wid = wf.get("id") or os.path.basename(path).split("-", 1)[0]
        out[wid] = (path, wf)
    return out


def sql_guard_findings(files, only):
    """V1 (2026-09-28): Postgres-node SQL that would let a parameter leave its literal (verify-sql-comments.py) is never
    deployed — checked for every workflow this run may PUT, BEFORE the first request to the instance."""
    out = []
    for wid, (path, wf) in files.items():
        if only and wid not in only:
            continue
        found, _ = sql_guard.check_workflow(wf)
        out += [(os.path.basename(path), *f) for f in found]
    return out


def body_from(wf):
    """The PUT body: name, nodes, connections, settings — never id/active/meta/staticData/tags (read-only on PUT)."""
    return {"name": wf["name"], "nodes": copy.deepcopy(wf["nodes"]),
            "connections": copy.deepcopy(wf.get("connections") or {}), "settings": copy.deepcopy(wf.get("settings") or {})}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=os.environ.get("N8N_BASE_URL", uc.DEFAULT_BASE))
    ap.add_argument("--only", action="append", default=[])
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--yes", action="store_true")
    ap.add_argument("--include-placeholders", action="store_true")
    a = ap.parse_args()
    if a.apply and not a.yes:
        sys.exit("--apply needs --yes")
    base = a.base.rstrip("/")
    files = canonical_files()
    refused = sql_guard_findings(files, a.only)
    if refused:
        for f, node, rule, line, token, excerpt in refused:
            print(f"SQL GUARD: {f} :: {node} :: line {line} :: {rule} {token}: {excerpt}")
        sys.exit("refusing to deploy: a Postgres node fails verify-sql-comments.py (V1) — fix its generator / the canon first")
    summary = {"considered": 0, "missing": 0, "up_to_date": 0, "would_update": 0, "updated": 0, "failed": 0}
    for wid, (path, canon) in files.items():
        if a.only and wid not in a.only:
            continue
        if wid in PLACEHOLDER_IDS and not a.include_placeholders:
            continue
        if wid.startswith("stg"):
            continue
        summary["considered"] += 1
        st, cur = uc.call(base, "GET", f"/workflows/{wid}")
        if st != 200:
            summary["missing"] += 1
            print(f"MISSING {wid} {os.path.basename(path)} — import it with n8n import:workflow, not here")
            continue
        if vd.graph_hash(cur) == vd.graph_hash(canon):
            summary["up_to_date"] += 1
            print(f"= {wid} '{cur.get('name')}' up to date")
            continue
        was_active = bool(cur.get("active"))
        cur_names = {n["name"] for n in cur["nodes"]}
        new_names = {n["name"] for n in canon["nodes"]}
        print(f"~ {wid} '{cur.get('name')}' active={was_active} nodes {len(cur['nodes'])} → {len(canon['nodes'])} "
              f"(+{len(new_names - cur_names)} -{len(cur_names - new_names)})")
        print(f"    rollback pointer: versionId {cur.get('versionId')} (activeVersionId {cur.get('activeVersionId')})")
        if not a.apply:
            summary["would_update"] += 1
            continue
        st, resp = uc.call(base, "PUT", f"/workflows/{wid}", body_from(canon))
        if st != 200:
            summary["failed"] += 1
            print(f"    PUT failed {st}: {str(resp)[:300]}")
            continue
        if was_active:
            uc.call(base, "POST", f"/workflows/{wid}/deactivate")
            st3, r3 = uc.call(base, "POST", f"/workflows/{wid}/activate")
            if st3 != 200:
                print(f"    re-activate failed {st3}: {str(r3)[:200]}")
        st4, got = uc.call(base, "GET", f"/workflows/{wid}")
        problems = []
        if st4 != 200:
            problems.append(f"read-back GET {st4}")
        else:
            if vd.graph_hash(got) != vd.graph_hash(canon):
                problems.append("graph differs from canonical after PUT")
            if bool(got.get("active")) != was_active:
                problems.append(f"active changed: {was_active} → {got.get('active')}")
            if was_active and got.get("activeVersionId") and got.get("activeVersionId") != got.get("versionId"):
                problems.append("published version is not the updated one (activeVersionId != versionId)")
        if problems:
            summary["failed"] += 1
            print("    read-back: FAILED — " + "; ".join(problems))
        else:
            summary["updated"] += 1
            print("    read-back: ok")
    print(json.dumps(summary))
    sys.exit(1 if summary["failed"] else 0)


if __name__ == "__main__":
    main()
