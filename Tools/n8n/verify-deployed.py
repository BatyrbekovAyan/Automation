#!/usr/bin/env python3
"""Stage 7 — READ-ONLY read-back of an n8n instance against the canonical set. Prod is the default base and this
tool never writes. Exit 2 when anything is wrong (so it doubles as the post-deploy gate: «local PASS ≠ verified
production»); exit 0 means every check below held.

Canonical workflows (Tools/n8n/workflows/*.json) — each must be:
  * present and NOT archived;
  * the same graph as the committed JSON — node set, name/type/version/parameters, the non-default behaviour
    keys (disabled, onError, alwaysOutputData, …) and the connections — in the stored (draft) version AND in the
    PUBLISHED version n8n serves (`activeVersion`); the ONLY tolerated difference is an entry of SUBSTITUTIONS
    (Support Message's recipient chat id), which is also REQUIRED to be bound outside staging;
  * free of unbound `__PLACEHOLDER__` tokens (credential ids, parameters) outside staging;
  * active exactly when expected: everything runs except the two clone TEMPLATES (the prod inventory, and the
    same contract as staging/bootstrap.py ACTIVE_CANONICAL).
Bot clones — judged by their REAL graphs (stored + published) against the template's graph_stamp
(update-clones.py, one shared definition); the `Template Version` sticky is reported («sticky claims X, graph is
Y») but never trusted. A fresh Create-made clone has no sticky and is AT-CANONICAL. ARCHIVED clones are listed
apart and never counted: the owner retired them and n8n refuses PUT on them. An inactive, non-archived clone IS
counted — the owner re-enables it with the channel toggles, which publishes its stored graph.
Clone prompts (review V10, 2026-09-28) — the HEAD of the stored AND the published prompt, judged by content against
the prompt history (update-clones.prompt_state): a head of an EARLIER Create/Edit map or template prompt is
PROMPT-BEHIND and counts like a graph behind (exit 2) — the graph alone does not carry new shared instructions into
an old bot's prompt; a head no map ever wrote is reported as custom (kept verbatim by update-clones) and NOT
counted. The `Business Type:` label is printed beside the vertical and never decides.

    python3 Tools/n8n/verify-deployed.py                      # prod (Tools/n8n/.secrets/prod-api-key.txt)
    python3 Tools/n8n/verify-deployed.py --base http://localhost:5678
    python3 Tools/n8n/verify-deployed.py --json report.json
"""
import argparse
import copy
import hashlib
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import importlib.util
_spec = importlib.util.spec_from_file_location("update_clones", os.path.join(HERE, "update-clones.py"))
uc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(uc)
WF = os.path.join(HERE, "workflows")
# The ONLY parameters a deployed canonical copy may legitimately differ in: workflow id -> [(node, parameter path,
# committed placeholder)]. The live value is put back to the placeholder before hashing, so every OTHER difference —
# parameters, connections, typeVersion, behaviour keys — still fails. Credential ids are outside the graph hash, so
# Set Workflow State's `__HDR_CRED__` (a credential id) needs NO entry at all.
SUBSTITUTIONS = {"olmqWMfcFpCEcpiE": [("Send To Support Chat", ("chatId",), "__CHAT_ID__")]}
# Activity contract: every canonical workflow runs, except the two bot templates (they exist only to be cloned).
EXPECTED_INACTIVE = {f.split("-", 1)[0] for f in uc.TEMPLATES.values()}
PLACEHOLDER = re.compile(r"__[A-Z][A-Z0-9_]*__")
STAGING_ONLY_PREFIX = "stg"


def graph_hash(wf, keep_identity=True):
    """Hash of a canonical workflow's graph (uc.node_signature per node + connections). Also used by
    deploy-canonical.py to decide «up to date» and to read back after a PUT."""
    nodes = []
    for n in uc.real_nodes(wf):
        m = uc.node_signature(n)
        if not keep_identity and n.get("name") == "Webhook":
            m["parameters"]["path"] = ""
        nodes.append(m)
    blob = json.dumps({"nodes": sorted(nodes, key=lambda x: x["name"]), "connections": wf.get("connections")}, sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(blob.encode()).hexdigest()[:12]


def unsubstitute(live, wid):
    """(copy of `live` with each documented substitution reset to its placeholder, whether EVERY one was bound)."""
    t, bound = copy.deepcopy(live), True
    for node_name, path, placeholder in SUBSTITUTIONS.get(wid, []):
        n = uc.node(t, node_name)
        if n is None:
            bound = False
            continue
        holder = n.setdefault("parameters", {})
        for k in path[:-1]:
            holder = holder.setdefault(k, {})
        value = holder.get(path[-1])
        if not isinstance(value, str) or not value.strip() or PLACEHOLDER.fullmatch(value.strip()):
            bound = False
        holder[path[-1]] = placeholder
    return t, bound


def unbound_placeholders(wf):
    """Credential ids / parameters still carrying a committed __PLACEHOLDER__ (a deploy that forgot to bind)."""
    out = []
    for n in wf.get("nodes") or []:
        for ctype, ref in (n.get("credentials") or {}).items():
            if isinstance(ref, dict) and PLACEHOLDER.fullmatch(str(ref.get("id") or "")):
                out.append(f"{n.get('name')}.credentials.{ctype}")
        for m in sorted(set(PLACEHOLDER.findall(json.dumps(n.get("parameters") or {})))):
            out.append(f"{n.get('name')}.parameters {m}")
    return out


def check_canonical(wid, wf, full, staging):
    """One canonical workflow's row: facts + the list of problems (empty = OK)."""
    row = {"active": bool(full.get("active")), "expected_active": wid not in EXPECTED_INACTIVE,
           "archived": bool(full.get("isArchived"))}
    row["names_match"] = sorted(n["name"] for n in uc.real_nodes(full)) == sorted(n["name"] for n in uc.real_nodes(wf))
    want = graph_hash(wf)
    draft, bound = unsubstitute(full, wid)
    row["graph_hash_live"], row["graph_hash_canonical"] = graph_hash(draft), want
    row["params_match"] = row["graph_hash_live"] == want
    pub = uc.published_graph(full)   # the graph n8n SERVES (None: inactive, or not reported by this n8n)
    if pub is not None:
        pub_cmp, pub_bound = unsubstitute(pub, wid)
        bound = bound and pub_bound
        row["graph_hash_published"] = graph_hash(pub_cmp)
    row["published_match"] = uc.published_matches(full, lambda g: graph_hash(unsubstitute(g, wid)[0]) == want)
    av, vid = full.get("activeVersionId"), full.get("versionId")
    row["published_is_stored"] = (av == vid) if (av and vid) else None
    problems = []
    if row["archived"]:
        problems.append("archived")
    if not row["names_match"]:
        problems.append("node set differs")
    if not row["params_match"]:
        problems.append("stored graph differs")
    if row["published_match"] is False:
        problems.append("PUBLISHED graph differs" if pub is not None else "published version is not the stored one")
    if row["active"] != row["expected_active"]:
        problems.append("inactive (must run)" if row["expected_active"] else "active (a template must stay inactive)")
    if not staging:   # staging leaves Support Message's chat id/credential dangling on purpose (derive-staging.py)
        if wid in SUBSTITUTIONS:
            row["substitution_bound"] = bound
            if not bound:
                problems.append("documented substitution not applied (placeholder still in place)")
        row["unbound_placeholders"] = sorted(set(unbound_placeholders(full) + unbound_placeholders(pub or {})))
        if row["unbound_placeholders"]:
            problems.append("unbound placeholders " + ", ".join(row["unbound_placeholders"]))
    return row, problems


def clone_verdict(c):
    """One clone's verdict: graph (stored, published) and prompt head (stored, published) — the label never decides."""
    parts = []
    if not c["at_canonical"]:
        parts.append("BEHIND")
    elif not c["published_at_canonical"]:
        parts.append("UNPUBLISHED (stored graph canonical, published is not)")
    ps, v = c.get("prompt_state"), c.get("prompt_vertical")
    if ps in uc.PROMPT_BEHIND:
        parts.append(f"PROMPT-BEHIND ({v or 'generic template prompt'}, head of {c.get('prompt_since')})")
    elif c.get("published_prompt_state") in uc.PROMPT_BEHIND:
        parts.append("PROMPT-UNPUBLISHED (stored head current, the published head is behind)")
    verdict = " ".join(parts) or "AT-CANONICAL"
    info = f"  prompt={ps}{('/' + v) if v else ''}"
    if ps == "custom":
        info += f" (custom prompt — kept verbatim by update-clones, not counted: {c.get('prompt_note')})"
    label = c.get("prompt_label")
    if v and label is not None and label != uc.ip.BUSINESS_TYPE_LABELS.get(v):
        info += f" (label {label!r}, the app labels {v} {uc.ip.BUSINESS_TYPE_LABELS.get(v)!r} — informational, the head decides)"
    claim = "" if c["stamp_consistent"] else f"  (sticky claims {c['stamp']}, graph is {c['graph_stamp']})"
    return verdict + info + claim


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=os.environ.get("N8N_BASE_URL", uc.DEFAULT_BASE))
    ap.add_argument("--json")
    a = ap.parse_args()
    base = a.base.rstrip("/")
    # On staging the deployed copies are DERIVED (hosts → mock/localhost, Supabase writers neutralised): compare
    # against the same derivation.
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
            if derive:
                derive.isolate_supabase_writers(wf)   # staging copies never write to the real Supabase (R3, derive-staging.py)
                derive.isolate_supabase_readers(wf)   # … and the price search reads the staging stub (V9, derive-staging.py)
            canon[wf["id"]] = (f, wf)
    live = {w["id"]: w for w in uc.list_workflows(base)}
    report = {"base": base, "canonical": [], "clones": [], "archived": [], "unknown": []}
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
        facts, problems = check_canonical(wid, wf, full, staging)
        row.update(facts)
        row["status"] = "OK" if not problems else "DIFF"
        if problems:
            row["note"] = "; ".join(problems)
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
            row = {"id": wid, "name": full.get("name"), "channel": ch, "active": bool(full.get("active")),
                   "nodes": len(uc.real_nodes(full)), **uc.clone_state(full, tpl_stamps.get(ch))}
            (report["archived"] if row["archived"] else report["clones"]).append(row)
        else:
            report["unknown"].append({"id": wid, "name": full.get("name"), "active": bool(full.get("active")),
                                      "archived": bool(full.get("isArchived"))})
    behind = sum(1 for c in report["clones"] if not c["up_to_date"])
    prompt_behind = sum(1 for c in report["clones"] if c.get("prompt_state") in uc.PROMPT_BEHIND
                        or c.get("published_prompt_state") in uc.PROMPT_BEHIND)
    custom = sum(1 for c in report["clones"] if c.get("prompt_state") == "custom")
    print(f"base {base}: canonical {len(report['canonical'])} (diff {diffs}), clones {len(report['clones'])} "
          f"(behind template {behind}, of them prompt head behind {prompt_behind}; custom prompts {custom}, not counted), "
          f"archived clones {len(report['archived'])} (not counted), other {len(report['unknown'])}")
    if report["clones"] or report["archived"]:   # the prompt history (git) is needed only to judge clones
        book = uc.prompt_book()
        print(f"  prompt map = Create/Edit Vertical Prompt {book['map_ref']} (sha256 {book['map_hash']})")
    for r in report["canonical"]:
        print(f"  {r['status']:7} {r['id']} {r['file']}  active={r.get('active')} names={r.get('names_match')} "
              f"graph={r.get('params_match')} published={r.get('published_match')}{('  (' + r['note'] + ')') if r.get('note') else ''}")
    for c in report["clones"]:
        print(f"  clone   {c['id']} {c['name']!r} [{c['channel']}] active={c['active']} nodes={c['nodes']} "
              f"graph={c['graph_stamp']} published={c['published_stamp']} sticky={c['stamp']} {clone_verdict(c)}")
    for c in report["archived"]:
        print(f"  archived {c['id']} {c['name']!r} [{c['channel']}] graph={c['graph_stamp']} — retired by the owner, not counted")
    if a.json:
        json.dump(report, open(a.json, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("wrote", a.json)
    sys.exit(2 if (diffs or behind) else 0)


if __name__ == "__main__":
    main()
