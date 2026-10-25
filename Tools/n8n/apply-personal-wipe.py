#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §4.6 and decision 12,
plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 8) — the Profile Lifecycle Sweep's step that, WIPE_DAYS (two)
days after the OWNER made a chat personal, deletes what the assistant stored about it. Idempotent patch of the canonical
workflow; this file OWNS the node «Wipe Personal Chats»:

  Profile Lifecycle Sweep (fXYpCXPKw92EzRz8):
    Every 6 Hours ──> Wipe Personal Chats      one Postgres node = audience.WIPE_SQL (no parameters)

  * It hangs straight on the trigger, like «Expire Learning Rows» (apply-learning-a.py), and sits right under it on the
    canvas, above branch A (apply-sweep-clone-deactivate.py keeps that one the lowest). It is NOT behind «Sweep Config» and
    no dry-run flag gates it: the step deletes by the age of the OWNER's decision, not by the state of a profile, and a
    dry run would only postpone a deletion the owner already waited two days for.
  * It is terminal and fail-open (onError continueRegularOutput): a failed run changes nothing (the statement is one
    transaction) and the next run, six hours later, takes the same work list again.
  * The statement, the delay and the batch are audience.py's (WIPE_SQL, WIPE_DAYS, WIPE_BATCH); tests/test_personal_wipe.py
    runs it on real SQL, verify-personal-chats.py pins the node.

The patch fixes the node's own fields (query, type, credential, onError, not disabled) and the one edge from the trigger; where
the node sits on the canvas is left to whoever moved it.

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
ANCHOR = "Expire Learning Rows"         # the node it sits under
ROW = 160                               # canvas rows between two stacked nodes of this branch
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}     # the canonical Postgres credential every node of the sweep uses
# what the patch enforces on the node itself, and what must not be on it (a disabled node hands its input on untouched)
OWNED = ("parameters", "type", "typeVersion", "credentials", "onError")
FORBIDDEN = ("disabled",)


def node_id():
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "personal-wipe:wipe-personal-chats"))


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def position_for(wf):
    """Right under «Expire Learning Rows»; without it, one row below where apply-learning-a.py puts that one (under the trigger,
    one column to the right)."""
    anchor = node(wf, ANCHOR)
    if anchor is not None:
        return [anchor["position"][0], anchor["position"][1] + ROW]
    trig = node(wf, TRIGGER)
    return [trig["position"][0] + 220, trig["position"][1] + 400 + ROW]


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
        want = desired(cur["position"])
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
