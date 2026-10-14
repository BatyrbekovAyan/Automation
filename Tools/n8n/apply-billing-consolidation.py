#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""A2 «защита платящих» (2026-10-14) — idempotent owner of the billing consolidation SQL in RevenueCat Events
(ZGYr6srzS3rSSXHp): writes billing_consolidation.py's texts into the `query` of exactly two nodes, resolved by NAME —

  Consolidate Aliases   ← CONSOLIDATE_SQL(RC_CONSOLIDATE)
  Transfer Subscriber   ← TRANSFER_SQL

Nothing else in that JSON is generated: Map Event, the queryReplacement arrays, the wiring, the gate and the upsert stay
hand-authored (verify-telegram-parity.py / verify-billing-consolidation.py pin them). Edit the SQL in
billing_consolidation.py, then run this; a hand edit of either query is a verifier failure.

    python3 Tools/n8n/apply-billing-consolidation.py          # write the module's SQL into workflows/ (re-runnable)
    python3 Tools/n8n/apply-billing-consolidation.py --check  # exit 0 = the canon holds the module's SQL, 2 = stale
"""
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
RC_EVENTS = "ZGYr6srzS3rSSXHp-RevenueCat_Events.json"
POSTGRES = "n8n-nodes-base.postgres"

_spec = importlib.util.spec_from_file_location("billing_consolidation", os.path.join(HERE, "billing_consolidation.py"))
BC = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(BC)


def owned():
    """{node name: the query text it must hold} — the whole of what this generator writes."""
    return {"Consolidate Aliases": BC.CONSOLIDATE_SQL(BC.RC_CONSOLIDATE), "Transfer Subscriber": BC.TRANSFER_SQL}


def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as fh:
        return json.load(fh)


def save(fname, wf):
    with open(os.path.join(WF, fname), "w", encoding="utf-8") as fh:
        json.dump(wf, fh, indent=2, ensure_ascii=False)
        fh.write("\n")


def target(wf, name):
    """The ONE Postgres executeQuery node called `name` — anything else is refused, never written over."""
    hits = [n for n in wf["nodes"] if n.get("name") == name]
    assert len(hits) == 1, f"{RC_EVENTS}: expected exactly one node {name!r}, found {len(hits)}"
    n = hits[0]
    assert n.get("type") == POSTGRES and (n.get("parameters") or {}).get("operation", "executeQuery") == "executeQuery", \
        f"{RC_EVENTS}: {name!r} is not a Postgres executeQuery node — refusing to write its query"
    return n


def patch(wf):
    changed = []
    for name, sql in owned().items():
        p = target(wf, name)["parameters"]
        if p.get("query") != sql:
            p["query"] = sql
            changed.append(name)
    return changed


def stale(wf):
    """Names of the owned nodes whose query differs from the module's text."""
    return [name for name, sql in owned().items() if target(wf, name)["parameters"].get("query") != sql]


def main():
    wf = load(RC_EVENTS)
    if "--check" in sys.argv:
        bad = stale(wf)
        print("billing consolidation:", "OK" if not bad else f"STALE in {RC_EVENTS}: {', '.join(bad)}")
        sys.exit(2 if bad else 0)
    changed = patch(wf)
    if changed:
        save(RC_EVENTS, wf)
    print(f"{'patched ' + ', '.join(changed) if changed else 'up to date'}  {RC_EVENTS}")


if __name__ == "__main__":
    main()
