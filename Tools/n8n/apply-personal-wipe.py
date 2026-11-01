#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §4.6 and decision 12,
plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 8) — the Profile Lifecycle Sweep's step that, WIPE_DAYS
(two) days after a business chat turned personal (the owner made it personal, the rule did after another number, or a
reconnect landed a personal decision over a business period — the clock personal_since, audience.OWNER_SET_SQL /
DECIDE_WRITE_SQL / clone_lineage.BIND_WORKFLOW_SQL), deletes what the assistant stored about it — never for a computed
decision a return to the previous number voided whose clock the mistaken account started (audience.RESTORE_VOIDED_SQL).
Idempotent patch of the canonical workflow; this file OWNS the node «Wipe Personal Chats»:

  Profile Lifecycle Sweep (fXYpCXPKw92EzRz8):
    Every 6 Hours ──> Wipe Personal Chats      one Postgres node = audience.WIPE_SQL (no parameters)

  * It hangs straight on the trigger, like «Expire Learning Rows» (apply-learning-a.py). It is NOT behind «Sweep Config» and
    no dry-run flag gates it: the step deletes by the age of a chat's personal clock, not by the state of a profile, and a
    dry run would only postpone a deletion that has already waited two days.
  * It is the FIRST branch the trigger runs: n8n v1 runs a node's branches one after another, the topmost on the canvas
    first (on a tie the leftmost), and an unhandled error in any branch ends the run — several nodes of the Sweep Config
    subtree have no onError, so a wipe placed below that subtree would silently stop whenever it fails. The node therefore
    sits one row ABOVE the highest other branch of the trigger, in Sweep Config's column (branch A stays the lowest,
    apply-sweep-clone-deactivate.py).
  * It is terminal and fail-open (onError continueRegularOutput): a failed run changes nothing (the statement is one
    transaction) and the next run, six hours later, takes the same work list again.
  * The statement, the delay and the batch are audience.py's (WIPE_SQL, WIPE_DAYS, WIPE_BATCH); tests/test_personal_wipe.py
    runs it on real SQL, verify-personal-chats.py pins the node (the first branch included).

The patch fixes the node's own fields (query, type, credential, onError, not disabled), its POSITION (where it sits decides
when n8n runs it) and the one edge from the trigger.

    python3 Tools/n8n/apply-personal-wipe.py            # patch workflows/ in place (re-runnable)
    python3 Tools/n8n/apply-personal-wipe.py --check    # exit 2 when the node is missing or stale
"""
import copy
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import audience  # noqa: E402  (the statement lives with the rest of the chat decisions)

PATH = os.path.join(HERE, "workflows", "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json")
NODE = "Wipe Personal Chats"
TRIGGER = "Every 6 Hours"
COLUMN = "Sweep Config"                 # the node whose column it sits in (the trigger's first column of branches)
ROW = 160                               # canvas rows between two stacked nodes of this branch
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}     # the canonical Postgres credential every node of the sweep uses
# what the patch enforces on the node itself, and what must not be on it (a disabled node hands its input on untouched)
OWNED = ("parameters", "type", "typeVersion", "position", "credentials", "onError")
FORBIDDEN = ("disabled",)


def node_id():
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "personal-wipe:wipe-personal-chats"))


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def position_for(wf):
    """One row ABOVE the highest other branch of the trigger (so n8n v1 runs the wipe first), in Sweep Config's column —
    without Sweep Config, one column right of the trigger. Without another branch, one row above the trigger."""
    trig = node(wf, TRIGGER)
    others = [node(wf, e["node"]) for out in (wf["connections"].get(TRIGGER) or {}).get("main") or [] for e in (out or [])
              if e.get("node") != NODE]
    tops = [n["position"][1] for n in others if n is not None]
    column = node(wf, COLUMN)
    x = column["position"][0] if column is not None else trig["position"][0] + 220
    return [x, (min(tops) if tops else trig["position"][1]) - ROW]


def desired(position):
    return {"parameters": {"operation": "executeQuery", "query": audience.WIPE_SQL, "options": {}},
            "id": node_id(), "name": NODE, "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
            "position": list(position), "credentials": {"postgres": dict(PG_CRED)}, "onError": "continueRegularOutput"}


def patch_sweep(wf):
    """Bring the Sweep forward in place; True when anything changed."""
    assert node(wf, TRIGGER) is not None, f"{TRIGGER} missing from the Sweep"
    changed = False
    cur = node(wf, NODE)
    if cur is None:
        wf["nodes"].append(desired(position_for(wf)))
        changed = True
    else:
        want = desired(position_for(wf))
        for key in OWNED:
            if cur.get(key) != want[key]:
                cur[key] = copy.deepcopy(want[key])
                changed = True
        for key in FORBIDDEN:
            if key in cur:
                del cur[key]
                changed = True
    main = wf["connections"].setdefault(TRIGGER, {"main": [[]]}).setdefault("main", [[]])
    if not main:
        main.append([])
    if not any(e.get("node") == NODE for e in main[0]):
        main[0].append({"node": NODE, "type": "main", "index": 0})
        changed = True
    return changed


def check_sweep(wf):
    """True when the node and its edge are as the patch would leave them."""
    return not patch_sweep(copy.deepcopy(wf))


def main():
    with open(PATH, encoding="utf-8") as fh:
        wf = json.load(fh)
    if "--check" in sys.argv:
        stale = not check_sweep(wf)
        print("personal wipe:", "STALE (re-run apply-personal-wipe.py)" if stale else "OK")
        sys.exit(2 if stale else 0)
    if patch_sweep(wf):
        with open(PATH, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
        print("patched  " + os.path.basename(PATH))
    else:
        print("up to date  " + os.path.basename(PATH))


if __name__ == "__main__":
    main()
