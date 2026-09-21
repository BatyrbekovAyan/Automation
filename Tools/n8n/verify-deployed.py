#!/usr/bin/env python3
"""Stage 7 — READ-ONLY read-back of an n8n instance against the canonical set: which canonical workflows
exist, whether their node set and parameter hash match the committed JSON, whether they are active and
their published version is the stored one, and which bot clones carry which template stamp. Prod is
the default base and this tool never writes. Exit 2 when anything differs (so it doubles as the
post-deploy gate: «local PASS ≠ verified production»).

    python3 Tools/n8n/verify-deployed.py                      # prod (Tools/n8n/.secrets/prod-api-key.txt)
    python3 Tools/n8n/verify-deployed.py --base http://localhost:5678
    python3 Tools/n8n/verify-deployed.py --json report.json
"""
import argparse
import copy
import hashlib
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import importlib.util
_spec = importlib.util.spec_from_file_location("update_clones", os.path.join(HERE, "update-clones.py"))
uc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(uc)
WF = os.path.join(HERE, "workflows")
# Workflows whose deployed copy legitimately differs from the canonical file (documented substitutions).
SUBSTITUTED = {"Support Message": "recipient chat id replaces __CHAT_ID__", "Set Workflow State": "admin-API credential id is instance-bound"}
STAGING_ONLY_PREFIX = "stg"


def graph_hash(wf, keep_identity=True):
    t = copy.deepcopy(wf)
    nodes = []
    for n in uc.real_nodes(t):
        m = {"name": n.get("name"), "type": n.get("type"), "typeVersion": n.get("typeVersion"), "parameters": n.get("parameters")}
        if not keep_identity:
            if n.get("name") == "Webhook" and isinstance(m["parameters"], dict):
                m["parameters"] = {**m["parameters"], "path": ""}
        nodes.append(m)
    blob = json.dumps({"nodes": sorted(nodes, key=lambda x: x["name"]), "connections": t.get("connections")}, sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(blob.encode()).hexdigest()[:12]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=os.environ.get("N8N_BASE_URL", uc.DEFAULT_BASE))
    ap.add_argument("--json")
    a = ap.parse_args()
    base = a.base.rstrip("/")
    # On staging the deployed copies are DERIVED (hosts → mock/localhost): compare against the same derivation.
    staging = "localhost" in base or "127.0.0.1" in base
    derive = None
    if staging:
        _s = importlib.util.spec_from_file_location("derive", os.path.join(HERE, "staging", "derive-staging.py"))
        derive = importlib.util.module_from_spec(_s)
        _s.loader.exec_module(derive)
    canon = {}
    for f in sorted(os.listdir(WF)):
        if f.endswith(".json"):
            text = open(os.path.join(WF, f), encoding="utf-8").read()
            wf = json.loads(derive.derive_file(text) if derive else text)
            canon[wf["id"]] = (f, wf)
    live = {w["id"]: w for w in uc.list_workflows(base)}
    report = {"base": base, "canonical": [], "clones": [], "unknown": []}
    diffs = 0
    for wid, (f, wf) in canon.items():
        row = {"id": wid, "file": f, "present": wid in live}
        if wid not in live:
            row["status"] = "MISSING"
            diffs += 1
            report["canonical"].append(row)
            continue
        st, full = uc.call(base, "GET", f"/workflows/{wid}")
        if st != 200:
            row["status"] = f"read failed {st}"; diffs += 1; report["canonical"].append(row); continue
        row["active"] = bool(full.get("active"))
        row["names_match"] = sorted(n["name"] for n in uc.real_nodes(full)) == sorted(n["name"] for n in uc.real_nodes(wf))
        row["graph_hash_live"], row["graph_hash_canonical"] = graph_hash(full), graph_hash(wf)
        row["params_match"] = row["graph_hash_live"] == row["graph_hash_canonical"]
        av, vid = full.get("activeVersionId"), full.get("versionId")
        row["published_is_stored"] = (av == vid) if (av and vid) else None
        substituted = wf.get("name") in SUBSTITUTED
        row["status"] = "OK" if (row["names_match"] and (row["params_match"] or substituted) and row["published_is_stored"] in (True, None)) else "DIFF"
        if substituted and not row["params_match"]:
            row["note"] = SUBSTITUTED[wf.get("name")]
        if row["status"] != "OK":
            diffs += 1
        report["canonical"].append(row)
    tpl_stamps = {ch: uc.template_stamp(uc.canonical(ch, base)) for ch in uc.TEMPLATES}
    for wid, w in live.items():
        if wid in canon:
            continue
        st, full = uc.call(base, "GET", f"/workflows/{wid}")
        if st != 200:
            continue
        if uc.is_clone(full, set(canon)):
            ch = uc.channel_of(full)
            stamp = uc.stamp_of(full)
            report["clones"].append({"id": wid, "name": full.get("name"), "channel": ch, "active": bool(full.get("active")),
                                     "stamp": stamp, "at_canonical": stamp == tpl_stamps.get(ch), "nodes": len(uc.real_nodes(full))})
        else:
            report["unknown"].append({"id": wid, "name": full.get("name"), "active": bool(full.get("active"))})
    behind = sum(1 for c in report["clones"] if not c["at_canonical"])
    print(f"base {base}: canonical {len(report['canonical'])} (diff {diffs}), clones {len(report['clones'])} (behind template {behind}), other {len(report['unknown'])}")
    for r in report["canonical"]:
        print(f"  {r['status']:7} {r['id']} {r['file']}  active={r.get('active')} names={r.get('names_match')} params={r.get('params_match')} published={r.get('published_is_stored')}{('  (' + r['note'] + ')') if r.get('note') else ''}")
    for c in report["clones"]:
        print(f"  clone   {c['id']} {c['name']!r} [{c['channel']}] active={c['active']} nodes={c['nodes']} stamp={c['stamp']} {'AT-CANONICAL' if c['at_canonical'] else 'BEHIND'}")
    if a.json:
        json.dump(report, open(a.json, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("wrote", a.json)
    sys.exit(2 if (diffs or behind) else 0)


if __name__ == "__main__":
    main()
