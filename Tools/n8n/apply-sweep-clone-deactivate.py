#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""V12 (iteration 2026-09-28): the Profile Lifecycle Sweep retires a bot whose trial or subscription ended — it deletes the
Wappi profile and marks the bot_profiles row (`trial_expiry` / `churn_grace`) — but left the bot's clone workflow ACTIVE:
an «active» workflow serving nobody, counted as live by every inventory, still receiving clone updates with the owner's
prompt in it. This generator adds ONE branch to the sweep that DEACTIVATES such a clone (never deletes it: the owner's
data stays stored until they delete the bot in the app or ask support — owner decision 2026-10-07):

    Sweep Config -> Retired Clones -> Clone Dry Run? --true--> Log Would Deactivate
                                                     --false-> Has Clone? --true--> Deactivate Clone -> Clone Off?
                                                                 --true--> Stamp Clone Deactivated ; --false--> Stamp Clone Kept

  * Retired Clones picks rows the SWEEP retired (trial_expiry / churn_grace) whose bound workflow is not yet confirmed
    inactive (`clone_deactivated_id`, sql/2026-10-07-retired-clone-deactivate.sql) and that no LIVE row still drives.
    The liveness branch is never a source: there the retirement is inferred from Wappi's list, and a false positive
    would switch off a bot that still works. app_delete rows are the app's own business (it deletes the clone).
  * Has Clone? re-checks the id in the expression (shape, never a protected infra id) before the admin API is called.
  * Deactivate Clone calls n8n's own API on localhost with the `n8n Admin API` credential (`__HDR_CRED__` in the
    committed file, bound per instance like Set Workflow State / Test Dialog); 200 or 404 = off, then stamped. Anything
    else leaves the row unstamped, so the next run retries it.
  * The branch sits BELOW the trial/churn and liveness rows on the canvas: n8n v1 runs sibling branches top-down, so a
    row retired earlier in the same run is picked in that run.
  * T5 (F1 + F2): a row the bot's line SUPERSEDED is skipped — its clone is booked in clone_retirements and branch A
    below deletes it; deactivating it first would be a wasted call.

T5 (F1 + F2, plan docs/audits/2026-10-09-followups/F1F2-plan.md §1.6, F1 §2.6 A) adds branch A «Clone Retirements»,
BELOW the V12 branch — no replaced clone is left undeleted:

    Sweep Config -> Clone Retirements -> Retirements Dry Run? --true--> Log Would Remove
                                                              --false-> Has Retired Clone? --true--> Delete Retired Clone
                    -> Retired Clone Gone? --true--> Stamp Retirement Removed ; --false--> Stamp Retirement Kept

  * Clone Retirements = clone_lineage.CLONE_RETIREMENTS_SQL: the clones booked in clone_retirements (Create's replaced
    clones and failed discards, a deleted bot's line) once they are ten minutes old — never one a LIVE bot_profiles row
    drives, never a protected or malformed id; least tried first, 50 per run.
  * Retirements Dry Run? reads Sweep Config.retirementsDryRun and deletes only on an explicit false (a missing flag never
    deletes). The canon carries RETIREMENTS_DRY_RUN below: true for the first day on prod (plan §6 step 5), then flip
    it to False here, re-run and deploy (step 12) once the dry-run log is clean.
  * Has Retired Clone? re-checks the id (shape, never a protected id) before the admin API; Delete Retired Clone is a
    DELETE on localhost with the `n8n Admin API` credential (`__HDR_CRED__`), status reported (neverError); 200 or 404
    = gone → removed_at (never picked again), anything else → attempts + 1 and the status / error, picked next run.

    python3 Tools/n8n/apply-sweep-clone-deactivate.py          # patch the canonical JSON
    python3 Tools/n8n/apply-sweep-clone-deactivate.py --check  # exit 2 when a branch is missing or stale
"""
import copy
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import clone_lineage as cl  # noqa: E402  (branch A's SQL lives with the rest of the bot's line)

PATH = os.path.join(HERE, "workflows", "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json")
RETIREMENTS_DRY_RUN = True      # plan §6: true for the first day on prod, then False (one line here, re-run, deploy)
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}
ADMIN_CRED = {"id": "__HDR_CRED__", "name": "n8n Admin API"}


def protected_ids():
    spec = importlib.util.spec_from_file_location("bcw", os.path.join(HERE, "build-client-webhooks.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    # + Set Workflow State itself and the support relay (Validate adds them at run time)
    return sorted(set(m.PROTECTED_IDS) | {"0DP79STI3pafXO9B", "olmqWMfcFpCEcpiE"})


PROTECTED = protected_ids()
RETIRED_CLONES_SQL = """-- V12 (iteration 2026-09-28): clones of bots THIS sweep retired (trial or subscription over) that are not yet
-- confirmed inactive. Never a liveness row (that retirement is inferred from Wappi's list; a false positive must not
-- switch off a working bot), never app_delete (the app deletes its clone), never a workflow a LIVE row still drives,
-- never a protected infra id. clone_deactivated_id holds the workflow last confirmed inactive: a failed call is picked
-- again next run, a deactivated one never. T5: never a row the bot's line superseded — its clone is booked in
-- clone_retirements and branch A deletes it. Read-only; at most 50 per run, oldest retirement first.
select bp.profile_id, bp.workflow_id, bp.deleted_reason
from bot_profiles bp
where bp.deleted_at is not null
  and bp.deleted_reason in ('trial_expiry', 'churn_grace')
  and bp.superseded_by is null
  and bp.workflow_id ~ '^[A-Za-z0-9]+$' and length(bp.workflow_id) between 8 and 36
  and bp.workflow_id not in (""" + ", ".join("'%s'" % i for i in PROTECTED) + """)
  and bp.clone_deactivated_id is distinct from bp.workflow_id
  and not exists (select 1 from bot_profiles live where live.workflow_id = bp.workflow_id and live.deleted_at is null)
order by bp.deleted_at
limit 50;"""
HAS_CLONE = ("={{ ((id) => typeof id === 'string' && id.length >= 8 && id.length <= 36 && /^[A-Za-z0-9]+$/.test(id) && !"
             + json.dumps(PROTECTED) + ".includes(id))($json.workflow_id) }}")
DEACTIVATE_URL = "=http://localhost:5678/api/v1/workflows/{{ $json.workflow_id }}/deactivate"
CLONE_OFF = "={{ [200, 404].includes(Number($json.statusCode)) }}"
STAMP_SQL = """-- V12: the clone this row is bound to is confirmed inactive (or gone) — remember WHICH one, so it is never called
-- again and a later re-bound clone is still picked. Parameters, in order: profile id, workflow id.
update bot_profiles set clone_deactivated_id = $2
where profile_id = $1 and workflow_id = $2
returning profile_id, workflow_id;"""
STAMP_PARAMS = "={{ [ $('Retired Clones').item.json.profile_id, $('Retired Clones').item.json.workflow_id ] }}"

# ---- T5 branch A: the booked clones (clone_lineage) are deleted, confirmed and stamped.
RETIREMENTS_DRY = "={{ $('Sweep Config').first().json.retirementsDryRun !== false }}"     # only an explicit false deletes
HAS_RETIRED_CLONE = ("={{ ((id) => typeof id === 'string' && /^[A-Za-z0-9]{8,36}$/.test(id) && !"
                     + json.dumps(cl.PROTECTED) + ".includes(id))($json.workflow_id) }}")
DELETE_RETIRED_URL = "=http://localhost:5678/api/v1/workflows/{{ $json.workflow_id }}"
RETIRED_GONE = "={{ [200, 404].includes(Number($json.statusCode)) }}"
STAMP_REMOVED_PARAMS = "={{ [ $('Clone Retirements').item.json.workflow_id, String($json.statusCode) ] }}"
STAMP_KEPT_PARAMS = ("={{ [ $('Clone Retirements').item.json.workflow_id, String($json.statusCode ?? (($json.error && "
                     "$json.error.message) || 'no response')).slice(0, 200) ] }}")
DRY_FLAG = {"id": "cfg-retirements-dry-run", "name": "retirementsDryRun", "value": RETIREMENTS_DRY_RUN, "type": "boolean"}


def nid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "crsweep-clone:" + name))


def if_node(name, left, pos):
    return {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                          "conditions": [{"id": nid(name + ":cond"), "leftValue": left, "rightValue": "",
                                                          "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                          "combinator": "and"}, "options": {}},
            "id": nid(name), "name": name, "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": pos}


def set_node(name, assignments, pos):
    return {"parameters": {"mode": "manual", "assignments": {"assignments": [
                {"id": nid(name + ":" + k), "name": k, "value": v, "type": t} for k, v, t in assignments]}, "options": {}},
            "id": nid(name), "name": name, "type": "n8n-nodes-base.set", "typeVersion": 3.4, "position": pos}


def desired_nodes(y):
    return [
        {"parameters": {"operation": "executeQuery", "query": RETIRED_CLONES_SQL, "options": {}},
         "id": nid("Retired Clones"), "name": "Retired Clones", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
         "position": [-460, y], "credentials": {"postgres": dict(PG_CRED)}},
        if_node("Clone Dry Run?", "={{ $('Sweep Config').first().json.dryRun }}", [-260, y]),
        set_node("Log Would Deactivate", [("dryRun", True, "boolean"), ("action", "would_deactivate_clone", "string"),
                                          ("profileId", "={{ $json.profile_id }}", "string"), ("workflowId", "={{ $json.workflow_id }}", "string")],
                 [-260, y + 160]),
        if_node("Has Clone?", HAS_CLONE, [-40, y]),
        {"parameters": {"method": "POST", "url": DEACTIVATE_URL, "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
                        "sendBody": True, "contentType": "json", "specifyBody": "json", "jsonBody": "{}",
                        "options": {"timeout": 20000, "response": {"response": {"fullResponse": True, "neverError": True}}}},
         "id": nid("Deactivate Clone"), "name": "Deactivate Clone", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
         "position": [160, y], "credentials": {"httpHeaderAuth": dict(ADMIN_CRED)}, "onError": "continueRegularOutput"},
        if_node("Clone Off?", CLONE_OFF, [360, y]),
        {"parameters": {"operation": "executeQuery", "query": STAMP_SQL, "options": {"queryReplacement": STAMP_PARAMS}},
         "id": nid("Stamp Clone Deactivated"), "name": "Stamp Clone Deactivated", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
         "position": [560, y - 80], "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"},
        set_node("Stamp Clone Kept", [("profileId", "={{ $('Retired Clones').item.json.profile_id }}", "string"),
                                      ("workflowId", "={{ $('Retired Clones').item.json.workflow_id }}", "string"),
                                      ("deactivated", False, "boolean"), ("statusCode", "={{ $json.statusCode ?? null }}", "string")],
                 [560, y + 80]),
    ]


def retirement_nodes(y):
    """T5 branch A, one row below the V12 branch."""
    return [
        {"parameters": {"operation": "executeQuery", "query": cl.CLONE_RETIREMENTS_SQL, "options": {}},
         "id": nid("Clone Retirements"), "name": "Clone Retirements", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
         "position": [-460, y], "credentials": {"postgres": dict(PG_CRED)}},
        if_node("Retirements Dry Run?", RETIREMENTS_DRY, [-260, y]),
        set_node("Log Would Remove", [("dryRun", True, "boolean"), ("action", "would_delete_clone", "string"),
                                      ("workflowId", "={{ $json.workflow_id }}", "string"), ("reason", "={{ $json.reason }}", "string"),
                                      ("attempts", "={{ $json.attempts }}", "number")],
                 [-260, y + 160]),
        if_node("Has Retired Clone?", HAS_RETIRED_CLONE, [-40, y]),
        {"parameters": {"method": "DELETE", "url": DELETE_RETIRED_URL, "authentication": "genericCredentialType",
                        "genericAuthType": "httpHeaderAuth",
                        "options": {"timeout": 20000, "response": {"response": {"fullResponse": True, "neverError": True}}}},
         "id": nid("Delete Retired Clone"), "name": "Delete Retired Clone", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
         "position": [160, y], "credentials": {"httpHeaderAuth": dict(ADMIN_CRED)}, "onError": "continueRegularOutput"},
        if_node("Retired Clone Gone?", RETIRED_GONE, [360, y]),
        {"parameters": {"operation": "executeQuery", "query": cl.STAMP_RETIREMENT_REMOVED_SQL,
                        "options": {"queryReplacement": STAMP_REMOVED_PARAMS}},
         "id": nid("Stamp Retirement Removed"), "name": "Stamp Retirement Removed", "type": "n8n-nodes-base.postgres",
         "typeVersion": 2.6, "position": [560, y - 80], "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True,
         "onError": "continueRegularOutput"},
        {"parameters": {"operation": "executeQuery", "query": cl.STAMP_RETIREMENT_KEPT_SQL,
                        "options": {"queryReplacement": STAMP_KEPT_PARAMS}},
         "id": nid("Stamp Retirement Kept"), "name": "Stamp Retirement Kept", "type": "n8n-nodes-base.postgres",
         "typeVersion": 2.6, "position": [560, y + 80], "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True,
         "onError": "continueRegularOutput"},
    ]


WIRES = {
    "Retired Clones": [["Clone Dry Run?"]],
    "Clone Dry Run?": [["Log Would Deactivate"], ["Has Clone?"]],
    "Has Clone?": [["Deactivate Clone"]],
    "Deactivate Clone": [["Clone Off?"]],
    "Clone Off?": [["Stamp Clone Deactivated"], ["Stamp Clone Kept"]],
    # T5 branch A
    "Clone Retirements": [["Retirements Dry Run?"]],
    "Retirements Dry Run?": [["Log Would Remove"], ["Has Retired Clone?"]],
    "Has Retired Clone?": [["Delete Retired Clone"]],
    "Delete Retired Clone": [["Retired Clone Gone?"]],
    "Retired Clone Gone?": [["Stamp Retirement Removed"], ["Stamp Retirement Kept"]],
}
BRANCH_A_BELOW = 400        # canvas rows below Retired Clones (n8n v1 runs sibling branches top-down)


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def ensure_flag(wf):
    """T5: Sweep Config carries retirementsDryRun (= RETIREMENTS_DRY_RUN), next to the other branches' dryRun."""
    cfg = node(wf, "Sweep Config")["parameters"]["assignments"]["assignments"]
    cur = next((a for a in cfg if a.get("name") == DRY_FLAG["name"]), None)
    if cur == DRY_FLAG:
        return False
    if cur is None:
        cfg.append(dict(DRY_FLAG))
    else:
        cfg[cfg.index(cur)] = dict(DRY_FLAG)
    return True


def patch(wf):
    changed = False
    lowest = max(n["position"][1] for n in wf["nodes"] if n["type"] != "n8n-nodes-base.stickyNote")
    existing = node(wf, "Retired Clones")
    y = existing["position"][1] if existing else lowest + 300
    changed |= ensure_flag(wf)
    for spec in desired_nodes(y) + retirement_nodes(y + BRANCH_A_BELOW):
        cur = node(wf, spec["name"])
        if cur is None:
            wf["nodes"].append(spec)
            changed = True
            continue
        for k, v in spec.items():          # the committed file always carries the credential placeholder
            if cur.get(k) != v:
                cur[k] = copy.deepcopy(v)
                changed = True
    for src, outs in WIRES.items():
        want = {"main": [[{"node": t, "type": "main", "index": 0} for t in out] for out in outs]}
        if wf["connections"].get(src) != want:
            wf["connections"][src] = want
            changed = True
    cfg = wf["connections"].setdefault("Sweep Config", {"main": [[]]})["main"]
    for first in ("Retired Clones", "Clone Retirements"):
        if not any(c["node"] == first for c in cfg[0]):
            cfg[0].append({"node": first, "type": "main", "index": 0})
            changed = True
    return changed


def main():
    wf = json.load(open(PATH, encoding="utf-8"))
    if "--check" in sys.argv:
        stale = patch(copy.deepcopy(wf))
        print("sweep clone deactivate:", "STALE (re-run apply-sweep-clone-deactivate.py)" if stale else "OK")
        sys.exit(2 if stale else 0)
    if patch(wf):
        with open(PATH, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
        print("patched  " + os.path.basename(PATH))
    else:
        print("up to date  " + os.path.basename(PATH))


if __name__ == "__main__":
    main()
