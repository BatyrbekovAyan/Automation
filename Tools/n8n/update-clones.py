#!/usr/bin/env python3
"""Stage 7.5 — managed update of EXISTING bot clones to the canonical templates, without recreating
any owner's bot and without losing what identifies it.

A clone = a workflow n8n's Create WA/TG made from a template: the template's graph plus four identity
fields (the Create workflow's `Set Fields` writes exactly these): the workflow NAME, the Webhook
node's PATH (= the Wappi profile id; the webhookId beside it), the AI Agent's systemMessage (the
bot's composed prompt), and the workflow SETTINGS. Everything else is the template. So an update =
the CURRENT canonical template with the clone's identity fields carried over, plus the clone's
credential ids per credential TYPE (prod and staging bind different ids). Files, mode rows, history,
billing and the workflow id are untouched — they live in the database and are keyed by
profile/workflow id, both preserved.

The prompt is only HALF identity (review V10, 2026-09-28). Set Fields composes it as
`[=]Business Type: <label>\n\n<HEAD>\n` + `\nAdditional Instructions: …`: the tail from the FIRST marker on is
the owner's (instructions, About Business, products, services) and is carried over byte for byte; the HEAD is
shared template content — the Create/Edit `Vertical Prompt` map's `vertical + _core + _universal`, or the
template's own prompt for a legacy id — and a clone keeps the head of the map that was live when it was created
or last saved. The head is judged by CONTENT against the prompt history (every committed Create/Edit PROMPTS map
and every committed template prompt, read from git — `prompt_book`), never by the `Business Type:` label, which is
free text: at_canonical (today's map) · behind (an EARLIER map's head of vertical v → refreshed to today's v, the
same head the next Edit save would write) · generic / generic_behind (the template's own prompt, today's / an
earlier one → refreshed) · custom (no map and no template ever wrote it: edited by hand, or no Create/Edit grammar)
→ NEVER replaced automatically; only an explicit `--set-vertical <workflowId>=<vertical id>` does it. A clone with
a behind head (stored or published) is NOT up to date, whatever its graph.

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
    python3 Tools/n8n/update-clones.py --only <workflowId> --set-vertical <workflowId>=flowers     # a CUSTOM head, by an operator's decision

--apply without --yes refuses. The default is a dry-run that prints, per clone, the node diff, the prompt plan
(«prompt: auto_parts behind (head of <sha>) → refreshed N→M chars, owner tail K chars kept» / «custom — kept
verbatim») and the identity fields it would preserve, plus the prompt map version it applies. Run it only AFTER
the new Create/Edit are deployed: a save through an older Create/Edit map would put the old head back. After a
PUT on an active clone the tool reads back whether n8n PUBLISHED the update (n8n 2.36 publishes a PUT on an
active workflow by default) and only re-publishes with deactivate → activate when it did not — so a live bot's
webhook is not unregistered for nothing. It then READS BACK every updated workflow and verifies: node set ==
canonical, identity fields preserved, prompt == the planned one (stored and published), stamp present, active
state as before.
"""
import argparse
import copy
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request
import importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
WF = os.path.join(HERE, "workflows")
TEMPLATES = {"whatsapp": "4wYitz5ek30SVNlT-WhatsApp_Bot.json", "telegram": "4VN3gsFaC2HUYmcc-Telegram_Bot.json"}
# The Create workflow of each channel — its Vertical Prompt map is what a clone's head is compared with TODAY; the
# Edit twins write the same map and join the history.
CREATE = {"whatsapp": "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "telegram": "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json"}
EDIT = {"whatsapp": "3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json", "telegram": "TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json"}
PROMPT_BEHIND = ("behind", "generic_behind")
_ip_spec = importlib.util.spec_from_file_location("inject_prompts", os.path.join(HERE, "inject-prompts.py"))
ip = importlib.util.module_from_spec(_ip_spec)   # the ONE owner of the head composition (parse/compose seam)
_ip_spec.loader.exec_module(ip)
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


# ---------------------------------------------------------------------------------------------- the prompt head (V10)
_PROMPT_BOOK = None


def _git(args, data=None):
    """git in the repository this tool lives in. Any failure is FATAL: without the history every head that is not
    today's would read as «custom» and a stale prompt would pass as the owner's own."""
    try:
        return subprocess.run(["git", "-C", REPO, *args], input=data, capture_output=True, check=True).stdout
    except (OSError, subprocess.CalledProcessError) as e:
        detail = getattr(e, "stderr", b"") or b""
        raise SystemExit(f"cannot read the prompt history from git in {REPO}: {e} {detail.decode(errors='replace').strip()[:200]} "
                         "— clone prompts cannot be judged without it (run from a full git checkout)")


def _committed_versions(rel_paths):
    """[(commit, path, bytes)] of every committed version of the files, oldest first (one `git cat-file --batch`)."""
    if _git(["rev-parse", "--is-shallow-repository"]).strip() == b"true":
        raise SystemExit("shallow git clone: the prompt history is incomplete (git fetch --unshallow) — clone prompts cannot be judged")
    pairs = []
    for rel in rel_paths:
        pairs += [(sha, rel) for sha in _git(["log", "--format=%H", "--reverse", "--", rel]).decode().split()]
    if not pairs:
        raise SystemExit(f"no committed history for {rel_paths} in {REPO} — clone prompts cannot be judged")
    blob = _git(["cat-file", "--batch"], "".join(f"{sha}:{rel}\n" for sha, rel in pairs).encode())
    out, pos = [], 0
    for sha, rel in pairs:
        nl = blob.index(b"\n", pos)
        header = blob[pos:nl].decode()
        pos = nl + 1
        if header.endswith(" missing"):
            continue
        size = int(header.split()[2])
        out.append((sha, rel, blob[pos:pos + size]))
        pos += size + 1
    return out


def prompt_book():
    """Today's heads and every head a committed map or template ever wrote (built once per process).

    current[ch][v]  — the head Create of channel ch composes TODAY for vertical v (the working-tree map)
    generic[ch]     — the head it composes today for an id without a vertical prompt (template prompt[1:])
    known[head]     — {kind: vertical|generic, vertical, since}: every head of every committed version of the four
                      Create/Edit maps and of both templates' prompts; `since` = the first commit that carried it."""
    global _PROMPT_BOOK
    if _PROMPT_BOOK is not None:
        return _PROMPT_BOOK
    rel = lambda f: os.path.relpath(os.path.join(WF, f), REPO)
    current, generic, known = {}, {}, {}
    for ch in TEMPLATES:
        cmap = ip.prompts_of_workflow(json.load(open(os.path.join(WF, CREATE[ch]), encoding="utf-8")))
        if not cmap:
            raise SystemExit(f"{CREATE[ch]} carries no Vertical Prompt map — run inject-prompts.py")
        current[ch] = cmap
        generic[ch] = ip.generic_head(json.load(open(os.path.join(WF, TEMPLATES[ch]), encoding="utf-8")))
    map_files = [rel(f) for ch in TEMPLATES for f in (CREATE[ch], EDIT[ch])]
    tpl_files = [rel(TEMPLATES[ch]) for ch in TEMPLATES]
    introduced = None   # the first commit of Create WA whose map is today's — the version this run applies
    for sha, path, data in _committed_versions(map_files + tpl_files):
        wf = json.loads(data)
        if path in tpl_files:
            h = ip.generic_head(wf)
            if h and h not in known:
                known[h] = {"kind": "generic", "vertical": None, "since": sha[:8]}
            continue
        m = ip.prompts_of_workflow(wf) or {}
        for v, h in m.items():
            if h not in known or known[h]["kind"] != "vertical":
                known[h] = {"kind": "vertical", "vertical": v, "since": sha[:8]}
        if introduced is None and path == map_files[0] and m == current["whatsapp"]:
            introduced = sha[:8]
    for ch in TEMPLATES:   # today's heads are known even before they are committed
        for v, h in current[ch].items():
            known.setdefault(h, {"kind": "vertical", "vertical": v, "since": "working tree"})
        if generic[ch]:
            known.setdefault(generic[ch], {"kind": "generic", "vertical": None, "since": "working tree"})
    digest = hashlib.sha256(json.dumps(current["whatsapp"], sort_keys=True, ensure_ascii=False).encode()).hexdigest()[:12]
    _PROMPT_BOOK = {"current": current, "generic": generic, "known": known, "map_hash": digest,
                    "map_ref": f"as introduced in {introduced}" if introduced else "of the working tree (not committed yet)"}
    return _PROMPT_BOOK


def system_message(wf):
    a = node(wf or {}, "AI Agent")
    return (((a or {}).get("parameters") or {}).get("options") or {}).get("systemMessage")


def prompt_state(sm, channel):
    """What a clone's head IS, by content: at_canonical | behind | generic | generic_behind | custom (+ vertical,
    the label as written, `since` = the first commit of the map that wrote a behind head, a note for custom)."""
    book = prompt_book()
    out = {"state": "custom", "vertical": None, "label": None, "since": None, "note": None}
    parts = ip.parse_system_message(sm)
    if parts is None:
        out["note"] = "no Create/Edit grammar (Business Type line + Additional Instructions marker)"
        return out
    out["label"] = parts["label"]
    head = parts["head"]
    for v, h in (book["current"].get(channel) or {}).items():
        if head == h:
            return dict(out, state="at_canonical", vertical=v)
    if head == book["generic"].get(channel):
        return dict(out, state="generic")
    known = book["known"].get(head)
    if known:
        return dict(out, state="behind" if known["kind"] == "vertical" else "generic_behind", vertical=known["vertical"], since=known["since"])
    out["note"] = ("the head matches no committed Create/Edit map and no template prompt — edited by hand, or written by a "
                   "map newer than this checkout (pull first)")
    return out


def refreshed_prompt(sm, channel, set_vertical=None):
    """The prompt the clone should carry: a behind head → today's head of the same vertical (generic → today's
    template prompt); `set_vertical` → today's head of that vertical (the operator's explicit decision for a custom
    head); otherwise the prompt unchanged. Always the same label line, the same leading '=' and the owner tail."""
    parts = ip.parse_system_message(sm)
    if parts is None:
        return sm
    book = prompt_book()
    cur = book["current"].get(channel) or {}
    if set_vertical:
        if set_vertical not in cur:
            raise SystemExit(f"--set-vertical: unknown vertical id {set_vertical!r} (known: {sorted(cur)})")
        return ip.compose_system_message(parts, cur[set_vertical])
    st = prompt_state(sm, channel)
    if st["state"] == "behind" and st["vertical"] in cur:
        return ip.compose_system_message(parts, cur[st["vertical"]])
    if st["state"] == "generic_behind" and book["generic"].get(channel):
        return ip.compose_system_message(parts, book["generic"][channel])
    return sm


def clone_state(full, tpl_stamp):
    """One clone judged by its graphs AND its prompt head: the stored (draft) version and the published one against
    the template and today's map; what the sticky merely CLAIMS is reported beside it, never trusted. A custom head
    is reported and never counted; a behind head (stored or published) makes the clone not up to date."""
    graph = graph_stamp(full)
    claimed = stamp_of(full)
    pub = published_graph(full)
    published_ok = published_matches(full, lambda g: graph_stamp(g) == tpl_stamp)
    ch = channel_of(full)
    ps = prompt_state(system_message(full), ch)
    prompt_ok = ps["state"] not in PROMPT_BEHIND
    published_prompt_ok = published_matches(full, lambda g: prompt_state(system_message(g), ch)["state"] not in PROMPT_BEHIND)
    if pub is not None:
        pub_state = prompt_state(system_message(pub), ch)["state"]
    elif full.get("active") and full.get("activeVersionId") and full.get("activeVersionId") == full.get("versionId"):
        pub_state = ps["state"]
    else:
        pub_state = None
    judged = lambda st: None if st in (None, "custom") else st not in PROMPT_BEHIND
    return {"graph_stamp": graph, "published_stamp": graph_stamp(pub) if pub is not None else None, "stamp": claimed,
            "stamp_consistent": claimed is None or claimed == graph, "at_canonical": graph == tpl_stamp,
            "published_at_canonical": published_ok is not False,
            "prompt_state": ps["state"], "prompt_vertical": ps["vertical"], "prompt_label": ps["label"],
            "prompt_since": ps["since"], "prompt_note": ps["note"], "prompt_at_canonical": judged(ps["state"]),
            "published_prompt_state": pub_state, "published_prompt_at_canonical": judged(pub_state),
            "up_to_date": graph == tpl_stamp and published_ok is not False and prompt_ok and published_prompt_ok is not False,
            "archived": bool(full.get("isArchived"))}


def credentials_by_type(wf):
    out = {}
    for n in wf.get("nodes", []):
        for ctype, ref in (n.get("credentials") or {}).items():
            out.setdefault(ctype, ref)
    return out


def build_target(clone, tpl, set_vertical=None):
    """The canonical template wearing the clone's identity (name, webhook path/id, settings, credentials by type) and
    its prompt — with a behind head brought to today's map (`refreshed_prompt`), the owner tail byte for byte."""
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
        ch = channel_of(clone) or channel_of(tpl)
        ta["parameters"].setdefault("options", {})["systemMessage"] = refreshed_prompt(sm, ch, set_vertical)
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
    pub = published_graph(got)
    if clone_was_active and pub is not None:
        if graph_stamp(pub) != graph_stamp(target):
            problems.append("published graph is not the updated one")
        if system_message(pub) != system_message(target):
            problems.append("published prompt is not the updated one")
    return (not problems), ("; ".join(problems) if problems else "ok")


def serves(got, target):
    """Does n8n already SERVE `target` (active, published version = stored version, and — when the instance reports
    it — the served graph and prompt are the target's)? Then no deactivate → activate is needed."""
    av, vid = got.get("activeVersionId"), got.get("versionId")
    if not (got.get("active") and av and vid and av == vid):
        return False
    pub = published_graph(got)
    return pub is None or (graph_stamp(pub) == graph_stamp(target) and system_message(pub) == system_message(target))


def prompt_plan(full, target, ch, cs, forced):
    """One dry-run line: what happens to the clone's prompt."""
    before, after = system_message(full) or "", system_message(target) or ""
    parts = ip.parse_system_message(before)
    tail = len(parts["tail"]) if parts else 0
    st, v = cs["prompt_state"], cs["prompt_vertical"]
    if forced:
        return f"prompt: {st} → set to {forced} by --set-vertical ({len(before)}→{len(after)} chars), owner tail {tail} chars kept"
    if before != after:
        what = f"{v} head" if st == "behind" else "generic template head"
        return f"prompt: {what} behind (head of {cs['prompt_since']}) → refreshed to today's map ({len(before)}→{len(after)} chars), owner tail {tail} chars kept"
    if st == "custom":
        return f"prompt: custom — kept verbatim ({cs['prompt_note']}; replace only with --set-vertical {full.get('id')}=<vertical id>)"
    if st in PROMPT_BEHIND:
        return f"prompt: {st} ({v}) but today's map has no head for it — kept; decide by hand"
    return f"prompt: {st}{'/' + v if v else ''} — kept"


def parse_set_vertical(items):
    """--set-vertical <workflowId>=<vertical id> (repeatable) → {workflowId: vertical id}."""
    out = {}
    for it in items:
        wid, sep, vid = it.partition("=")
        if not sep or not wid or vid not in ip.VERTICALS:
            sys.exit(f"--set-vertical expects <workflowId>=<vertical id>, vertical id one of {ip.VERTICALS}: {it!r}")
        out[wid] = vid
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=os.environ.get("N8N_BASE_URL", DEFAULT_BASE))
    ap.add_argument("--only", action="append", default=[], help="workflow id(s) to consider")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--yes", action="store_true", help="required with --apply")
    ap.add_argument("--set-vertical", action="append", default=[], metavar="WID=ID",
                    help="replace a CUSTOM prompt head with today's head of this vertical (an operator's decision)")
    a = ap.parse_args()
    base = a.base.rstrip("/")
    if a.apply and not a.yes:
        sys.exit("--apply requires --yes (an explicit, per-run authorisation)")
    forced_ids = parse_set_vertical(a.set_vertical)
    tpls = {ch: canonical(ch, base) for ch in TEMPLATES}
    stamps = {ch: template_stamp(tpls[ch]) for ch in tpls}
    canonical_ids = {json.load(open(os.path.join(WF, f), encoding="utf-8")).get("id") for f in os.listdir(WF) if f.endswith(".json")}
    book = prompt_book()
    print(f"base {base} · mode {'APPLY' if a.apply else 'dry-run'} · template stamps {stamps}")
    print(f"prompt map applied: Create/Edit Vertical Prompt {book['map_ref']} (sha256 {book['map_hash']}) — deploy the matching "
          f"Create/Edit BEFORE this run, or the next owner save would put an older head back")
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
        forced = forced_ids.get(full["id"])
        if forced and ip.parse_system_message(system_message(full)) is None:
            print(f"! {full['id']} --set-vertical ignored: the prompt carries no Create/Edit grammar to put a head into")
            forced = None
        if forced and refreshed_prompt(system_message(full), ch, forced) == system_message(full):
            forced = None   # already that vertical's head of today
        prompt_info = f"prompt {cs['prompt_state']}{'/' + cs['prompt_vertical'] if cs['prompt_vertical'] else ''}"
        if cs["up_to_date"] and not forced:   # by GRAPH and PROMPT HEAD (stored + published), never by the sticky alone
            summary["up_to_date"] += 1
            note = "" if cs["stamp"] in (None, stamp) else f"; sticky claims {cs['stamp']}"
            custom = f"; {prompt_plan(full, full, ch, cs, None)}" if cs["prompt_state"] == "custom" else ""
            print(f"= {full['id']} {full['name']!r} [{ch}] up to date ({stamp}, graph-verified, {prompt_info}{note}){custom}")
            continue
        if not cs["stamp_consistent"]:
            print(f"! {full['id']} {full['name']!r} sticky claims {cs['stamp']} but the graph is {cs['graph_stamp']} (template {stamp})")
        if cs["at_canonical"] and not cs["published_at_canonical"]:
            print(f"! {full['id']} {full['name']!r} stored graph is canonical but the PUBLISHED one is {cs['published_stamp']} — the apply re-publishes it")
        if cs["prompt_state"] not in PROMPT_BEHIND and cs["published_prompt_state"] in PROMPT_BEHIND:
            print(f"! {full['id']} {full['name']!r} stored prompt head is current but the PUBLISHED one is behind — the apply re-publishes it")
        target = build_target(full, tpls[ch], set_vertical=forced)
        added, removed, changed, conn = node_diff(full, target)
        print(f"~ {full['id']} {full['name']!r} [{ch}] active={full.get('active')} → +{len(added)} -{len(removed)} ~{len(changed)} conn={'changed' if conn else 'same'}")
        print(f"    add: {added}\n    remove: {removed}\n    change: {changed}")
        print(f"    {prompt_plan(full, target, ch, cs, forced)}")
        if cs["prompt_label"] is not None and cs["prompt_vertical"] and cs["prompt_label"] != ip.BUSINESS_TYPE_LABELS.get(cs["prompt_vertical"]):
            print(f"    label {cs['prompt_label']!r} is not the app's label for {cs['prompt_vertical']} "
                  f"({ip.BUSINESS_TYPE_LABELS.get(cs['prompt_vertical'])!r}) — informational, the head's content decides")
        print(f"    keep: path={node(full, 'Webhook')['parameters']['path']!r} label line + owner tail, creds={sorted(credentials_by_type(full).keys())}")
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
            # n8n 2.36 publishes a PUT on an active workflow by default: read back first and re-publish (deactivate →
            # activate, a window in which the bot's webhook is unregistered) ONLY when the served version lags.
            st2, now = call(base, "GET", f"/workflows/{full['id']}")
            if st2 == 200 and serves(now, target):
                print("    published by the PUT (served version = the update) — no deactivate/activate window")
            else:
                print("    the PUT did not publish the update — re-publishing (deactivate → activate)")
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
