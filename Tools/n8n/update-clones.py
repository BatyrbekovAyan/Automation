#!/usr/bin/env python3
"""Stage 7.5 — managed update of EXISTING bot clones to the canonical templates, without recreating
any owner's bot and without losing what identifies it.

A clone = a workflow n8n's Create WA/TG made from a template: the template's graph plus four identity
fields (the Create workflow's `Set Fields` writes exactly these): the workflow NAME, the Webhook
node's PATH (= the Wappi profile id; the webhookId beside it), the AI Agent's systemMessage (the
bot's composed prompt: vertical + owner data), and the workflow SETTINGS. Everything else is the
template. So an update = the CURRENT canonical template with the clone's identity fields carried
over, plus the clone's credential ids per credential TYPE (prod and staging bind different ids).
Files, mode rows, history, billing and the workflow id are untouched — they live in the database
and are keyed by profile/workflow id, both preserved.

Idempotent by GRAPH (review D1, 2026-09-27): a clone is up to date when its stored graph AND the graph n8n
publishes (`activeVersion`) both hash to the template's `graph_stamp` — node name/type/version/parameters plus
the non-default behaviour keys (disabled/onError/alwaysOutputData/…) and the connections, with the identity
fields blanked. Such a clone is skipped whether or not it carries a sticky (a fresh Create-made bot has none).
Every updated clone still gets a `Template Version` sticky note (n8n's `meta` is read-only on PUT) holding
`crTemplateVersion: <graph_stamp>`, but that is a label only: a sticky over an edited graph is reported
(«sticky claims X but the graph is Y») and updated. Re-running after a successful apply changes nothing.

    python3 Tools/n8n/update-clones.py                                   # dry-run on the default base (prod, READ-ONLY)
    python3 Tools/n8n/update-clones.py --base http://localhost:5678 --apply --yes          # staging
    python3 Tools/n8n/update-clones.py --only <workflowId> --apply --yes --base https://n8n.choosereply.com   # prod, owner-authorised only

--apply without --yes refuses. The default is a dry-run that prints, per clone, the node diff and the
identity fields it would preserve. After an apply the tool READS BACK every updated workflow and
verifies: node set == canonical, identity fields preserved, stamp present, active state as before.
"""
import argparse
import copy
import hashlib
import json
import os
import re
import sys
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
TEMPLATES = {"whatsapp": "4wYitz5ek30SVNlT-WhatsApp_Bot.json", "telegram": "4VN3gsFaC2HUYmcc-Telegram_Bot.json"}
IDENTITY_NODES = ("Webhook", "AI Agent")
STICKY = "n8n-nodes-base.stickyNote"
STAMP_NODE = "Template Version"


def real_nodes(wf):
    return [n for n in wf.get("nodes", []) if n.get("type") != STICKY]


def stamp_of(wf):
    for n in wf.get("nodes", []):
        if n.get("type") == STICKY and n.get("name") == STAMP_NODE:
            m = re.search(r"crTemplateVersion:\s*([0-9a-f]{12})", n.get("parameters", {}).get("content", ""))
            if m:
                return m.group(1)
    return None


def stamp_node(stamp):
    return {"parameters": {"content": f"## Template Version\ncrTemplateVersion: {stamp}\nUpdated by Tools/n8n/update-clones.py — do not edit by hand.", "height": 120, "width": 320},
            "id": "crTemplateStamp", "name": STAMP_NODE, "type": STICKY, "typeVersion": 1, "position": [-4800, 0]}
DEFAULT_BASE = "https://n8n.choosereply.com"


def api_key(base):
    k = os.environ.get("N8N_API_KEY")
    if k:
        return k.strip()
    if "localhost" in base or "127.0.0.1" in base:
        p = os.path.join(HERE, ".secrets", "staging-n8n.json")
        if os.path.exists(p):
            return json.load(open(p)).get("apiKey", "")
    p = os.path.join(HERE, ".secrets", "prod-api-key.txt")
    if os.path.exists(p):
        return open(p).read().strip()
    sys.exit("no n8n API key: set N8N_API_KEY")


def call(base, method, path, body=None):
    req = urllib.request.Request(f"{base}/api/v1{path}", method=method, headers={"X-N8N-API-KEY": api_key(base), "Accept": "application/json"})
    data = None
    if body is not None:
        req.add_header("Content-Type", "application/json")
        data = json.dumps(body).encode()
    try:
        with urllib.request.urlopen(req, data, timeout=60) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        return e.code, {"error": e.read().decode()[:500]}


def list_workflows(base):
    out, cursor = [], None
    while True:
        st, d = call(base, "GET", "/workflows?limit=200" + (f"&cursor={cursor}" if cursor else ""))
        if st != 200:
            sys.exit(f"list failed {st}: {d}")
        out += d.get("data", [])
        cursor = d.get("nextCursor")
        if not cursor:
            return out


_DERIVE = None


def _derive_module():
    global _DERIVE
    if _DERIVE is None:
        import importlib.util
        spec = importlib.util.spec_from_file_location("derive", os.path.join(HERE, "staging", "derive-staging.py"))
        _DERIVE = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_DERIVE)
    return _DERIVE


def canonical(channel, base=None):
    """The canonical template — derived for STAGING (hosts → mock/localhost, credentials by name) when the base is local,
    so the same tool that will update prod clones is what the staging acceptance exercises."""
    text = open(os.path.join(WF, TEMPLATES[channel]), encoding="utf-8").read()
    if base and ("localhost" in base or "127.0.0.1" in base):
        d = _derive_module()
        wf = json.loads(d.derive_file(text))
        creds, missing = d.staging_credentials(), set()
        for n in wf["nodes"]:
            d.remap(n, creds, missing)
        return wf
    return json.loads(text)


def node(wf, name):
    return next((n for n in wf.get("nodes", []) if n.get("name") == name), None)


def channel_of(wf):
    send = node(wf, "HTTP Request")
    url = (send or {}).get("parameters", {}).get("url", "")
    if "/tapi/" in url:
        return "telegram"
    if "/api/" in url:
        return "whatsapp"
    return None


def is_clone(wf, canonical_ids):
    if wf.get("id") in canonical_ids:
        return False
    return node(wf, "Webhook") is not None and node(wf, "AI Agent") is not None and node(wf, "Chat Memory") is not None and channel_of(wf) is not None


# Node keys that change what a node DOES. n8n omits them at their defaults, so only a NON-default value is part of
# the graph: a node disabled by hand in the editor, or one that lost its onError, is a different graph.
BEHAVIOUR_KEYS = {"disabled": False, "onError": "stopWorkflow", "alwaysOutputData": False, "executeOnce": False,
                  "retryOnFail": False, "maxTries": 3, "waitBetweenTries": 1000, "continueOnFail": False}


def node_signature(n):
    """What a node IS for every graph comparison (this tool, verify-deployed.py, deploy-canonical.py): name, type,
    version, parameters and the non-default behaviour keys. Never node ids, positions, credentials (bound per
    instance) or webhookIds (n8n mints them on create)."""
    m = {"name": n.get("name"), "type": n.get("type"), "typeVersion": n.get("typeVersion"),
         "parameters": copy.deepcopy(n.get("parameters") or {})}
    for k, default in BEHAVIOUR_KEYS.items():
        if k in n and n[k] != default:
            m[k] = n[k]
    return m


def graph_stamp(wf):
    """Content hash of a workflow's REAL graph with the per-bot identity blanked (Webhook path, AI Agent
    systemMessage). The SAME function hashes the template and a live clone — draft or published version — so
    `graph_stamp(clone) == graph_stamp(template)` is the proof a clone runs the canonical graph. A clone made by
    Create WA/TG carries no sticky and is still at canonical; a sticky over an edited graph is not."""
    nodes = []
    for n in real_nodes(wf):
        m = node_signature(n)
        if m["name"] == "Webhook":
            m["parameters"]["path"] = ""
        if m["name"] == "AI Agent":
            m["parameters"].setdefault("options", {})["systemMessage"] = ""
        nodes.append(m)
    blob = json.dumps({"nodes": sorted(nodes, key=lambda x: x["name"]), "connections": wf.get("connections") or {}},
                      sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(blob.encode()).hexdigest()[:12]


def template_stamp(tpl):
    """The version value the `Template Version` sticky carries = the template's graph_stamp. The sticky is only a
    label for humans: every up-to-date decision compares graphs (clone_state)."""
    return graph_stamp(tpl)


def published_graph(full):
    """The graph n8n SERVES for an active workflow — the public API's `activeVersion` (n8n 2.x). None when the
    workflow is not active or the response carries no published version (then callers fall back to ids)."""
    av = full.get("activeVersion")
    if full.get("active") and isinstance(av, dict) and av.get("nodes") is not None:
        return {"nodes": av.get("nodes") or [], "connections": av.get("connections") or {}}
    return None


def published_matches(full, same_graph):
    """Does the SERVED graph satisfy `same_graph(graph) -> bool`? None when nothing is served (inactive) or the
    instance reports neither activeVersion nor version ids; with ids only, published == stored is required."""
    if not full.get("active"):
        return None
    pub = published_graph(full)
    if pub is not None:
        return bool(same_graph(pub))
    av, vid = full.get("activeVersionId"), full.get("versionId")
    if av and vid:
        return av == vid and bool(same_graph(full))
    return None


def clone_state(full, tpl_stamp):
    """One clone judged by its graphs: the stored (draft) graph and the published one against the template; what
    the sticky merely CLAIMS is reported beside it, never trusted."""
    graph = graph_stamp(full)
    claimed = stamp_of(full)
    pub = published_graph(full)
    published_ok = published_matches(full, lambda g: graph_stamp(g) == tpl_stamp)
    return {"graph_stamp": graph, "published_stamp": graph_stamp(pub) if pub is not None else None, "stamp": claimed,
            "stamp_consistent": claimed is None or claimed == graph, "at_canonical": graph == tpl_stamp,
            "published_at_canonical": published_ok is not False,
            "up_to_date": graph == tpl_stamp and published_ok is not False, "archived": bool(full.get("isArchived"))}


def credentials_by_type(wf):
    out = {}
    for n in wf.get("nodes", []):
        for ctype, ref in (n.get("credentials") or {}).items():
            out.setdefault(ctype, ref)
    return out


def build_target(clone, tpl):
    """The canonical template wearing the clone's identity (name, webhook path/id, prompt, settings, credentials by type)."""
    target = copy.deepcopy(tpl)
    creds = credentials_by_type(clone)
    for n in target["nodes"]:
        for ctype in list((n.get("credentials") or {}).keys()):
            if ctype in creds:
                n["credentials"][ctype] = dict(creds[ctype])
    cw, tw = node(clone, "Webhook"), node(target, "Webhook")
    tw["parameters"]["path"] = cw["parameters"]["path"]
    if cw.get("webhookId"):
        tw["webhookId"] = cw["webhookId"]
    ca, ta = node(clone, "AI Agent"), node(target, "AI Agent")
    sm = ((ca.get("parameters") or {}).get("options") or {}).get("systemMessage")
    if sm is not None:
        ta["parameters"].setdefault("options", {})["systemMessage"] = sm
    settings = dict(clone.get("settings") or {})
    settings.setdefault("executionOrder", "v1")
    nodes = real_nodes(target) + [stamp_node(template_stamp(tpl))]
    return {"name": clone["name"], "nodes": nodes, "connections": target["connections"], "settings": settings}


def node_diff(clone, target):
    cn = {n["name"]: n for n in real_nodes(clone)}
    tn = {n["name"]: n for n in real_nodes(target)}
    added = sorted(set(tn) - set(cn))
    removed = sorted(set(cn) - set(tn))
    changed = []
    for name in sorted(set(cn) & set(tn)):
        if node_signature(cn[name]) != node_signature(tn[name]):   # the same per-node definition graph_stamp hashes
            changed.append(name)
    conn_changed = clone.get("connections") != target["connections"]
    return added, removed, changed, conn_changed


def verify_after(base, wid, target, clone_was_active, stamp):
    st, got = call(base, "GET", f"/workflows/{wid}")
    if st != 200:
        return False, f"read-back failed {st}"
    problems = []
    if sorted(n["name"] for n in real_nodes(got)) != sorted(n["name"] for n in real_nodes(target)):
        problems.append("node set differs from canonical")
    if node(got, "Webhook")["parameters"]["path"] != node(target, "Webhook")["parameters"]["path"]:
        problems.append("webhook path changed")
    if node(got, "AI Agent")["parameters"]["options"].get("systemMessage") != node(target, "AI Agent")["parameters"]["options"].get("systemMessage"):
        problems.append("system message changed")
    if stamp_of(got) != stamp:
        problems.append("stamp missing")
    if bool(got.get("active")) != clone_was_active:
        problems.append(f"active state {got.get('active')} != {clone_was_active}")
    av, vid = got.get("activeVersionId"), got.get("versionId")
    if clone_was_active and av and vid and av != vid:
        problems.append("published version is not the updated one (activeVersionId != versionId)")
    return (not problems), ("; ".join(problems) if problems else "ok")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=os.environ.get("N8N_BASE_URL", DEFAULT_BASE))
    ap.add_argument("--only", action="append", default=[], help="workflow id(s) to consider")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--yes", action="store_true", help="required with --apply")
    a = ap.parse_args()
    base = a.base.rstrip("/")
    if a.apply and not a.yes:
        sys.exit("--apply requires --yes (an explicit, per-run authorisation)")
    tpls = {ch: canonical(ch, base) for ch in TEMPLATES}
    stamps = {ch: template_stamp(tpls[ch]) for ch in tpls}
    canonical_ids = {json.load(open(os.path.join(WF, f), encoding="utf-8")).get("id") for f in os.listdir(WF) if f.endswith(".json")}
    print(f"base {base} · mode {'APPLY' if a.apply else 'dry-run'} · template stamps {stamps}")
    rows = list_workflows(base)
    clones = [w for w in rows if (not a.only or w["id"] in a.only) and w.get("id") not in canonical_ids]
    summary = {"clones": 0, "up_to_date": 0, "would_update": 0, "updated": 0, "failed": 0}
    for w in clones:
        st, full = call(base, "GET", f"/workflows/{w['id']}")
        if st == 200 and full.get("isArchived"):
            summary["archived"] = summary.get("archived", 0) + 1
            print(f"- {w['id']} '{w.get('name')}' archived — skipped (n8n refuses PUT on an archived workflow; the owner retired this bot)")
            continue
        if st != 200 or not is_clone(full, canonical_ids):
            continue
        summary["clones"] += 1
        ch = channel_of(full)
        stamp = stamps[ch]
        cs = clone_state(full, stamp)
        if cs["up_to_date"]:   # by GRAPH (stored + published), never by the sticky alone
            summary["up_to_date"] += 1
            note = "" if cs["stamp"] in (None, stamp) else f"; sticky claims {cs['stamp']}"
            print(f"= {full['id']} {full['name']!r} [{ch}] up to date ({stamp}, graph-verified{note})")
            continue
        if not cs["stamp_consistent"]:
            print(f"! {full['id']} {full['name']!r} sticky claims {cs['stamp']} but the graph is {cs['graph_stamp']} (template {stamp})")
        if cs["at_canonical"] and not cs["published_at_canonical"]:
            print(f"! {full['id']} {full['name']!r} stored graph is canonical but the PUBLISHED one is {cs['published_stamp']} — the apply re-publishes it")
        target = build_target(full, tpls[ch])
        added, removed, changed, conn = node_diff(full, target)
        print(f"~ {full['id']} {full['name']!r} [{ch}] active={full.get('active')} → +{len(added)} -{len(removed)} ~{len(changed)} conn={'changed' if conn else 'same'}")
        print(f"    add: {added}\n    remove: {removed}\n    change: {changed}")
        print(f"    keep: path={node(full, 'Webhook')['parameters']['path']!r} prompt={len(node(full, 'AI Agent')['parameters'].get('options', {}).get('systemMessage', '') or '')} chars, creds={sorted(credentials_by_type(full).keys())}")
        if not a.apply:
            summary["would_update"] += 1
            continue
        was_active = bool(full.get("active"))
        print(f"    rollback pointer: versionId {full.get('versionId')} (activeVersionId {full.get('activeVersionId')}) before the update")
        st, resp = call(base, "PUT", f"/workflows/{full['id']}", target)   # name, nodes (+ stamp note), connections, settings — meta/staticData are read-only on PUT
        if st != 200:
            summary["failed"] += 1
            print(f"    PUT failed {st}: {str(resp)[:300]}")
            continue
        if was_active:
            # re-publish the updated graph: an active workflow keeps serving its published version until re-activated
            call(base, "POST", f"/workflows/{full['id']}/deactivate")
            st3, r3 = call(base, "POST", f"/workflows/{full['id']}/activate")
            if st3 != 200:
                print(f"    re-activate failed {st3}: {str(r3)[:200]}")
        ok, msg = verify_after(base, full["id"], target, was_active, stamp)
        summary["updated" if ok else "failed"] += 1
        print(f"    read-back: {msg}")
    print(json.dumps(summary))
    sys.exit(1 if summary["failed"] else 0)


if __name__ == "__main__":
    main()
