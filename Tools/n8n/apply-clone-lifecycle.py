#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""F1 + F2 «линия бота» — the Create WhatsApp/Telegram GRAPH (task T3 of docs/audits/2026-10-09-followups/F1F2-plan.md, §1.4).

Create becomes «ensure exactly one bound clone for this channel of this bot». The SQL that decides it lives in
clone_lineage.py (T2); this generator wires it into both canonical Create workflows, after the reply-mode seed gate (R8):

  Seed Saved?[0] → Clone Plan (PG, retry ×3, continue, always output) → Plan Read? (planned === true)
    [1] → Respond Plan Unavailable {success:false, error:'clone_plan_unavailable'}
    [0] → Legacy Owned? (foreign === true) → [0] Respond Profile Owned {error:'profile_owned'}
                                           → [1] Reuse? (own_id well formed, not protected)
          Reuse?[0] → Probe Bound Clone (GET, localhost) → Bound Clone Usable? (200, not archived, its Webhook path = profile)
                [0] → Reused Clone Id {id: own_id, created: false, active: the probe's body.active} → Clone Id
                [1] → Bound Clone Gone? (404, or 200 + archived) → [0] Get Sample Workflow … ; [1] Respond Clone Unavailable
          Reuse?[1] → Get Sample Workflow → Set Fields → Create Workflow → Get Created Workflow Id {created: true} → Clone Id
  Clone Id → Already Active? (a REUSED clone the probe saw active) → [0] Set Wappi Webhook …
                                                                    [1] Activate Created Workflow → Activated? → Set Wappi Webhook
           → Webhook Set? → Set Wappi Webhook Types → Types Set?
           → Bind Workflow Id (ONE statement: bind + move the bot's data + book the replaced clones; retry ×3)
           → Bound? (bound === true && workflow_id === Clone Id)
                 [0] → Replaced Items → Has Replaced? → [0] Delete Replaced Clone → Send New Workflows Id ; [1] → Send …
                 [1] → Discard?
  every «no» after Clone Id → Discard? (created === true, a well-formed unprotected id) → Discard New Clone → Discarded?
        (200/404) → [1] Record Discard Failure (PG, clone_retirements 'discard_failed') → Respond Create Failed
  Send New Workflows Id (executeOnce): {id, created, adoptedProfiles}; every refusal is {success:false, error} WITHOUT an id
  (the client's contract, plan §1.7: CreateWorkflowResponse).

Why `Already Active?`: on n8n 2.36.7 activating an ACTIVE workflow is not a no-op — WorkflowService.activateWorkflow
removes it from the active manager and adds it again (its webhook unregistered in between) and writes a publish-history
record; a reused clone that already serves is therefore not activated again (plan §3 T3, risk 2).

Rules this generator keeps (verify-clone-lifecycle.py pins them, T4):
  * a decision is taken on a POSITIVE fact only (R8, F3): Clone Plan's `planned`, Bind's `bound`, an HTTP statusCode —
    every n8n API and Wappi call runs with fullResponse + neverError + onError continueRegularOutput, an If on its status
    follows. Never «the node continued».
  * every n8n API call goes to http://localhost:5678 with the `n8n Admin API` credential (`__HDR_CRED__`, bound per
    instance by deploy-canonical.py; staging remaps it by name) — no DNS / Caddy on the hot path.
  * protected ids (build-client-webhooks.PROTECTED_IDS + Set Workflow State + the support relay, via clone_lineage) are
    never reused, deleted or discarded — in the SQL and again in the If expressions before a call.
  * as many Postgres nodes on the hot path as before (each ≈ 1.9 s on prod): + Clone Plan, − Restamp RAG Chunks (its
    pre-F1 '-1' rule lives in Bind now). The retired node is removed.

Ownership (generators rewrite each other otherwise): this file owns every node above except the seed chain (Seed Reply
Mode / Seed Saved? / Respond Reply Mode Not Saved and the Seed Saved? edges — apply-reply-mode-v2.py, whose CLONE_CHAIN is
Clone Plan), Set Fields (inject-prompts.py composes its systemMessage) and the jsonBody of Set Wappi Webhook Types
(apply-history-handoff.py). apply-auth-gate.py no longer writes Bind Workflow Id (a pointer is left there). Migration
rule for an older canon: every edge into Get Sample Workflow from anything but Reuse?[1] / Bound Clone Gone?[0] is
retargeted to Clone Plan, so the plan can never be bypassed.

    python3 Tools/n8n/apply-clone-lifecycle.py           # patch both canonical Create workflows in place (re-runnable)
    python3 Tools/n8n/apply-clone-lifecycle.py --check   # exit 2 when either is missing or stale
"""
import copy
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import clone_lineage as cl  # noqa: E402

WF = os.path.join(HERE, "workflows")
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}
WAPPI_CRED = {"id": "EuhhqAaV56DpoqAN", "name": "WappiAuthToken"}
ADMIN_CRED = {"id": "__HDR_CRED__", "name": "n8n Admin API"}       # bound per instance (deploy-canonical / staging by name)
API = "http://localhost:5678/api/v1/workflows"
CALLBACK = "https://n8n.choosereply.com/webhook/"
TEMPLATE = {"whatsapp": "4wYitz5ek30SVNlT", "telegram": "4VN3gsFaC2HUYmcc"}
WAPPI = {"whatsapp": "https://wappi.pro/api", "telegram": "https://wappi.pro/tapi"}
PROT_JS = json.dumps(cl.PROTECTED)
ID_OK = "/^[A-Za-z0-9]{8,36}$/.test(%s) && !" + PROT_JS + ".includes(%s)"
HTTP_OPTIONS = lambda timeout: {"timeout": timeout, "response": {"response": {"fullResponse": True, "neverError": True}}}
RETIRED_NODES = ("Restamp RAG Chunks",)
CLONE_CHAIN_ENTRIES = {("Reuse?", 1), ("Bound Clone Gone?", 0)}   # the only edges allowed into Get Sample Workflow

REFUSALS = {"Respond Plan Unavailable": "clone_plan_unavailable", "Respond Profile Owned": "profile_owned",
            "Respond Clone Unavailable": "clone_unavailable", "Respond Create Failed": "create_failed"}

REPLACED_ITEMS_JS = """// CREATE / Replaced Items (generated by Tools/n8n/apply-clone-lifecycle.py, F1 + F2): one item per clone the bind
// booked for deletion — Bind Workflow Id's retired_ids: the replaced predecessors' clones, the profile's own binding that
// is gone or archived, a binding a concurrent Create overwrote — each well formed, never protected, never the clone just
// bound; or ONE {none} item when there is nothing to delete (a node without items would end the run without an answer).
// The clones are already in clone_retirements: a DELETE that fails here is the sweep's to finish.
const PROTECTED = __PROT__;
const bound = String($('Clone Id').first().json.id || '');
const ids = $('Bind Workflow Id').first().json.retired_ids;
const todo = [...new Set((Array.isArray(ids) ? ids : []).filter((w) =>
  typeof w === 'string' && /^[A-Za-z0-9]{8,36}$/.test(w) && !PROTECTED.includes(w) && w !== bound))];
return todo.length ? todo.map((workflowId) => ({ json: { workflowId }, pairedItem: { item: 0 } }))
                   : [{ json: { none: true }, pairedItem: { item: 0 } }];""".replace("__PROT__", PROT_JS)

# Canvas, relative to Register Profile (a node no generator moves): Clone Plan takes the seat Get Sample Workflow had, so
# apply-reply-mode-v2.py's seed seats (relative to its CLONE_CHAIN) stay where they were.
LAYOUT = {
    "Clone Plan": (340, 0), "Plan Read?": (520, 0), "Respond Plan Unavailable": (700, 420), "Legacy Owned?": (700, 0),
    "Respond Profile Owned": (880, 420), "Reuse?": (880, 0), "Probe Bound Clone": (1060, -280),
    "Bound Clone Usable?": (1240, -280), "Reused Clone Id": (1420, -380), "Bound Clone Gone?": (1420, -200),
    "Respond Clone Unavailable": (1600, -200), "Get Sample Workflow": (1060, 0), "Set Fields": (1240, 0),
    "Create Workflow": (1420, 0), "Get Created Workflow Id": (1600, 0), "Clone Id": (1780, 0), "Already Active?": (1870, -160),
    "Activate Created Workflow": (1960, 0), "Activated?": (2140, 0), "Set Wappi Webhook": (2320, 0), "Webhook Set?": (2500, 0),
    "Set Wappi Webhook Types": (2680, 0), "Types Set?": (2860, 0), "Bind Workflow Id": (3040, 0), "Bound?": (3220, 0),
    "Replaced Items": (3400, 0), "Has Replaced?": (3580, 0), "Delete Replaced Clone": (3760, -140),
    "Send New Workflows Id": (3940, 0), "Discard?": (3220, 340), "Discard New Clone": (3400, 340), "Discarded?": (3580, 340),
    "Record Discard Failure": (3760, 500), "Respond Create Failed": (3940, 340),
}

WIRES = {
    "Clone Plan": [["Plan Read?"]],
    "Plan Read?": [["Legacy Owned?"], ["Respond Plan Unavailable"]],
    "Legacy Owned?": [["Respond Profile Owned"], ["Reuse?"]],
    "Reuse?": [["Probe Bound Clone"], ["Get Sample Workflow"]],
    "Probe Bound Clone": [["Bound Clone Usable?"]],
    "Bound Clone Usable?": [["Reused Clone Id"], ["Bound Clone Gone?"]],
    "Bound Clone Gone?": [["Get Sample Workflow"], ["Respond Clone Unavailable"]],
    "Reused Clone Id": [["Clone Id"]],
    "Get Sample Workflow": [["Set Fields"]],
    "Set Fields": [["Create Workflow"]],
    "Create Workflow": [["Get Created Workflow Id"]],
    "Get Created Workflow Id": [["Clone Id"]],
    "Clone Id": [["Already Active?"]],
    "Already Active?": [["Set Wappi Webhook"], ["Activate Created Workflow"]],
    "Activate Created Workflow": [["Activated?"]],
    "Activated?": [["Set Wappi Webhook"], ["Discard?"]],
    "Set Wappi Webhook": [["Webhook Set?"]],
    "Webhook Set?": [["Set Wappi Webhook Types"], ["Discard?"]],
    "Set Wappi Webhook Types": [["Types Set?"]],
    "Types Set?": [["Bind Workflow Id"], ["Discard?"]],
    "Bind Workflow Id": [["Bound?"]],
    "Bound?": [["Replaced Items"], ["Discard?"]],
    "Replaced Items": [["Has Replaced?"]],
    "Has Replaced?": [["Delete Replaced Clone"], ["Send New Workflows Id"]],
    "Delete Replaced Clone": [["Send New Workflows Id"]],
    "Discard?": [["Discard New Clone"], ["Respond Create Failed"]],
    "Discard New Clone": [["Discarded?"]],
    "Discarded?": [["Respond Create Failed"], ["Record Discard Failure"]],
    "Record Discard Failure": [["Respond Create Failed"]],
}
TERMINAL = ("Send New Workflows Id",) + tuple(REFUSALS)
KEY_ORDER = ("parameters", "id", "name", "type", "typeVersion", "position", "credentials")


def nid(fname, *parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "clone-lifecycle:" + fname + ":" + ":".join(parts)))


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def wh(field):
    return "$('Unity Webhook').first().json.body." + field


def if_node(fname, name, left):
    return {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                          "conditions": [{"id": nid(fname, name, "cond"), "leftValue": left, "rightValue": "",
                                                          "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                          "combinator": "and"}, "options": {}},
            "name": name, "type": "n8n-nodes-base.if", "typeVersion": 2.2}


def set_node(fname, name, assignments, **flags):
    out = {"parameters": {"assignments": {"assignments": [
               {"id": aid or nid(fname, name, k), "name": k, "value": v, "type": t} for k, v, t, aid in assignments]}, "options": {}},
           "name": name, "type": "n8n-nodes-base.set", "typeVersion": 3.4}
    out.update(flags)
    return out


def pg_node(name, query, params, **flags):
    out = {"parameters": {"operation": "executeQuery", "query": query, "options": {"queryReplacement": params}},
           "name": name, "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "credentials": {"postgres": dict(PG_CRED)}}
    out.update(flags)
    return out


def api_node(name, method, url, timeout, **flags):
    params = {"url": url, "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
              "options": HTTP_OPTIONS(timeout)}
    if method != "GET":
        params = {"method": method, **params}
    out = {"parameters": params, "name": name, "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
           "credentials": {"httpHeaderAuth": dict(ADMIN_CRED)}, "onError": "continueRegularOutput"}
    out.update(flags)
    return out


def desired(fname, channel, wf):
    """Every node this generator owns, fully specified (ids and positions are filled in by apply())."""
    c = cl.CHANNELS[channel]
    pf, of = c["profile_field"], c["other_workflow_field"]
    clone_id = "$('Clone Id').first().json.id"
    plan = "$('Clone Plan').first().json"
    cur = {n["name"]: n for n in wf["nodes"]}
    aid = lambda name, key: next((a["id"] for a in ((cur.get(name) or {}).get("parameters", {}).get("assignments", {})
                                                     .get("assignments", [])) if a.get("name") == key), None)
    spec = [
        pg_node("Clone Plan", cl.CLONE_PLAN_SQL,
                "={{ [ String($('Auth Gate').first().json.crAuth.mode || ''), String($('Auth Gate').first().json.crAuth.installId || ''), "
                f"String({wh(pf)} || ''), '{channel}', String({wh('BotKey')} || ''), String({wh('CreateOrigin')} || ''), "
                f"String({wh('PreviousProfileId')} || '') ] }}}}",
                retryOnFail=True, maxTries=3, waitBetweenTries=1000, onError="continueRegularOutput", alwaysOutputData=True),
        if_node(fname, "Plan Read?", "={{ $json.planned === true }}"),
        if_node(fname, "Legacy Owned?", "={{ $json.foreign === true }}"),
        if_node(fname, "Reuse?", "={{ ((id) => typeof id === 'string' && " + ID_OK % ("id", "id") + ")($json.own_id) }}"),
        api_node("Probe Bound Clone", "GET", "=" + API + "/{{ " + plan + ".own_id }}", 10000,
                 retryOnFail=True, maxTries=2, waitBetweenTries=1000),
        if_node(fname, "Bound Clone Usable?",
                "={{ ((r, id, pid) => Number(r.statusCode) === 200 && !!r.body && typeof r.body === 'object' && r.body.isArchived !== true "
                "&& r.body.id === id && (Array.isArray(r.body.nodes) ? r.body.nodes : []).some((n) => !!n && n.name === 'Webhook' "
                f"&& !!n.parameters && n.parameters.path === pid))($json, {plan}.own_id, String({wh(pf)} || '')) }}}}"),
        if_node(fname, "Bound Clone Gone?",
                "={{ Number($json.statusCode) === 404 || (Number($json.statusCode) === 200 && !!$json.body && $json.body.isArchived === true) }}"),
        set_node(fname, "Reused Clone Id", [("id", "={{ " + plan + ".own_id }}", "string", None), ("created", False, "boolean", None),
                                            ("active", "={{ !!$json.body && $json.body.active === true }}", "boolean", None)]),
        {"parameters": {"url": f"{API}/{TEMPLATE[channel]}", "authentication": "genericCredentialType",
                        "genericAuthType": "httpHeaderAuth", "options": {}},
         "name": "Get Sample Workflow", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
         "credentials": {"httpHeaderAuth": dict(ADMIN_CRED)}},
        {"parameters": {"method": "POST", "url": API, "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
                        "sendBody": True, "specifyBody": "json", "jsonBody": "={{ $json }}", "options": {}},
         "name": "Create Workflow", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
         "credentials": {"httpHeaderAuth": dict(ADMIN_CRED)}},
        set_node(fname, "Get Created Workflow Id", [("id", "={{ $json.id }}", "string", aid("Get Created Workflow Id", "id")),
                                                    ("created", True, "boolean", None)]),
        set_node(fname, "Clone Id", [("id", "={{ $json.id }}", "string", None), ("created", "={{ $json.created === true }}", "boolean", None),
                                     ("active", "={{ $json.active === true }}", "boolean", None)]),
        if_node(fname, "Already Active?", "={{ $json.created === false && $json.active === true }}"),
        api_node("Activate Created Workflow", "POST", "=" + API + "/{{ " + clone_id + " }}/activate", 20000),
        if_node(fname, "Activated?", "={{ Number($json.statusCode) === 200 }}"),
        {"parameters": {"method": "POST", "url": f"{WAPPI[channel]}/webhook/url/set", "authentication": "genericCredentialType",
                        "genericAuthType": "httpHeaderAuth", "sendQuery": True,
                        "queryParameters": {"parameters": [{"name": "profile_id", "value": "={{ " + wh(pf) + " }}"},
                                                           {"name": "url", "value": "=" + CALLBACK + "{{ " + wh(pf) + " }}"}]},
                        "options": HTTP_OPTIONS(20000)},
         "name": "Set Wappi Webhook", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
         "credentials": {"httpHeaderAuth": dict(WAPPI_CRED)}, "onError": "continueRegularOutput"},
        if_node(fname, "Webhook Set?", "={{ Number($json.statusCode) === 200 }}"),
        {"parameters": {"method": "POST", "url": f"{WAPPI[channel]}/webhook/types/set", "authentication": "genericCredentialType",
                        "genericAuthType": "httpHeaderAuth", "sendQuery": True,
                        "queryParameters": {"parameters": [{"name": "profile_id", "value": "={{ " + wh(pf) + " }}"}]},
                        "sendBody": True, "specifyBody": "json",
                        # apply-history-handoff.py owns the webhook types list: kept as it stands
                        "jsonBody": ((cur.get("Set Wappi Webhook Types") or {}).get("parameters") or {}).get("jsonBody"),
                        "options": HTTP_OPTIONS(20000)},
         "name": "Set Wappi Webhook Types", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
         "credentials": {"httpHeaderAuth": dict(WAPPI_CRED)}, "onError": "continueRegularOutput"},
        if_node(fname, "Types Set?", "={{ Number($json.statusCode) === 200 }}"),
        pg_node("Bind Workflow Id", cl.BIND_WORKFLOW_SQL[channel],
                f"={{{{ [ String({wh(pf)} || ''), String({clone_id} || ''), String({wh('AppUserID')} || ''), '{channel}', "
                "String($('Auth Gate').first().json.crAuth.installId || ''), "
                f"($('Clone Id').first().json.created === true ? String({plan}.own_id || '') : ''), "
                f"JSON.stringify(Array.isArray({plan}.replace_ids) ? {plan}.replace_ids : []), "
                f"JSON.stringify(Array.isArray({plan}.pred_profiles) ? {plan}.pred_profiles : []), "
                f"String({wh(of)} || '') ] }}}}",
                retryOnFail=True, maxTries=3, waitBetweenTries=1000, onError="continueRegularOutput", alwaysOutputData=True),
        if_node(fname, "Bound?", "={{ $json.bound === true && $json.workflow_id === " + clone_id + " }}"),
        {"parameters": {"jsCode": REPLACED_ITEMS_JS}, "name": "Replaced Items", "type": "n8n-nodes-base.code", "typeVersion": 2},
        if_node(fname, "Has Replaced?", "={{ typeof $json.workflowId === 'string' && $json.workflowId !== '' }}"),
        api_node("Delete Replaced Clone", "DELETE", "=" + API + "/{{ $json.workflowId }}", 20000),
        set_node(fname, "Send New Workflows Id",
                 [("id", "={{ " + clone_id + " }}", "string", aid("Send New Workflows Id", "id")),
                  ("created", "={{ $('Clone Id').first().json.created === true }}", "boolean", None),
                  ("adoptedProfiles", "={{ (Array.isArray($('Bind Workflow Id').first().json.adopted_profiles) ? "
                                      "$('Bind Workflow Id').first().json.adopted_profiles : []).filter((p) => typeof p === 'string') }}",
                   "array", None)],
                 executeOnce=True),
        if_node(fname, "Discard?", "={{ ((c) => !!c && c.created === true && typeof c.id === 'string' && " + ID_OK % ("c.id", "c.id")
                + ")($('Clone Id').first().json) }}"),
        api_node("Discard New Clone", "DELETE", "=" + API + "/{{ " + clone_id + " }}", 20000,
                 retryOnFail=True, maxTries=2, waitBetweenTries=1000),
        if_node(fname, "Discarded?", "={{ [200, 404].includes(Number($json.statusCode)) }}"),
        pg_node("Record Discard Failure", cl.RECORD_DISCARD_FAILURE_SQL,
                f"={{{{ [ String({clone_id} || ''), String({wh(pf)} || ''), String($('Auth Gate').first().json.crAuth.installId || '') ] }}}}",
                onError="continueRegularOutput", alwaysOutputData=True),
    ]
    for name, err in REFUSALS.items():
        spec.append(set_node(fname, name, [("success", False, "boolean", None), ("error", err, "string", None)]))
    return spec


def retire(wf):
    changed = False
    before = len(wf["nodes"])
    wf["nodes"] = [n for n in wf["nodes"] if n["name"] not in RETIRED_NODES]
    changed |= len(wf["nodes"]) != before
    for name in RETIRED_NODES:
        changed |= wf["connections"].pop(name, None) is not None
    for c in wf["connections"].values():
        for i, out in enumerate(c.get("main", []) or []):
            keep = [e for e in (out or []) if e["node"] not in RETIRED_NODES]
            if keep != (out or []):
                c["main"][i] = keep
                changed = True
    return changed


def patch(fname, channel, wf):
    """Bring one Create workflow to the line's graph; returns whether anything changed."""
    before = json.dumps(wf, sort_keys=True)
    anchor = node(wf, "Register Profile")
    assert anchor is not None and node(wf, "Seed Saved?") is not None and node(wf, "Set Fields") is not None, \
        f"{fname}: Register Profile / Seed Saved? / Set Fields missing — not a Create workflow of this shape"
    x0, y0 = anchor["position"]
    for spec in desired(fname, channel, wf):
        cur = node(wf, spec["name"])
        full = dict(spec)
        full["id"] = cur["id"] if cur else nid(fname, spec["name"])
        dx, dy = LAYOUT[spec["name"]]
        full["position"] = [x0 + dx, y0 + dy]
        ordered = {k: full[k] for k in KEY_ORDER if k in full}
        ordered.update({k: v for k, v in full.items() if k not in ordered})
        if cur is None:
            wf["nodes"].append(ordered)
        else:
            wf["nodes"][wf["nodes"].index(cur)] = ordered
    retire(wf)
    # migration: nothing enters the clone chain around the plan
    for src, c in wf["connections"].items():
        for i, out in enumerate(c.get("main", []) or []):
            for e in out or []:
                if e["node"] == "Get Sample Workflow" and (src, i) not in CLONE_CHAIN_ENTRIES and src not in WIRES:
                    e["node"] = "Clone Plan"
    for src, outs in WIRES.items():
        wf["connections"][src] = {"main": [[{"node": d, "type": "main", "index": 0} for d in out] for out in outs]}
    for name in TERMINAL:
        wf["connections"].pop(name, None)
    return json.dumps(wf, sort_keys=True) != before


def files():
    return {c["create_file"]: ch for ch, c in cl.CHANNELS.items()}


def main():
    check = "--check" in sys.argv
    stale = []
    for fname, channel in files().items():
        path = os.path.join(WF, fname)
        wf = json.load(open(path, encoding="utf-8"))
        work = copy.deepcopy(wf)
        changed = patch(fname, channel, work)
        if check:
            if changed:
                stale.append(fname)
            continue
        if changed:
            with open(path, "w", encoding="utf-8") as fh:
                json.dump(work, fh, indent=2, ensure_ascii=False)
                fh.write("\n")
        print(f"{'patched' if changed else 'up to date'}  {fname}")
    if check:
        print("clone lifecycle:", "OK" if not stale else f"STALE in {stale}")
        sys.exit(2 if stale else 0)


if __name__ == "__main__":
    main()
