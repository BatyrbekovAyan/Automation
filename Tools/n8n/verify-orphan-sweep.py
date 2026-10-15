#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Structural verifier for the hourly orphan sweep «Delete Orphan Profiles» (2islisFH7jjLoPQM) — A4 «защита платящих»
(2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md §2.2, §3 A4).

The workflow has NO generator (it was exported from the n8n editor), so A4's change is a hand edit of its JSON and this
file is what holds it. No network. Behaviour lives in tests/test_orphan_sweep.py (the REAL Plan Sweep code over fixture
lists, the REAL statement on temp tables); this file pins what that behaviour rests on:

  (1) a Postgres node `Live Profiles` runs `select profile_id from bot_profiles where deleted_at is null;` — every LIVE
      row, whatever the subscription status — on the canonical Postgres credential (the one the Profile Lifecycle Sweep's
      nodes use), with alwaysOutputData + continue on error, so an empty answer or a failure still reaches Plan Sweep,
      which then protects everything (the fail-safe is decided there, in one place);
  (2) it sits between List TG Profiles and Plan Sweep: List TG Profiles feeds ONLY it, it feeds ONLY Plan Sweep, and
      Plan Sweep has no other input — so the delete branch cannot be reached without the live read;
  (3) Plan Sweep reads the live ids from its input and PROVES the set (every item a row with a non-empty profile id —
      no row, an error item, a passed-on input or an id-less row is «unproven» and protects EVERY profile), and excludes
      the protected ones right before a profile becomes a candidate, counting them (stats/platform protectedLive);
  (4) no node is disabled (a disabled node hands its input on untouched — a disabled Live Profiles silently turns the
      sweep into a no-op, a disabled Plan Sweep or re-check into worse);
  (5) the guards that were there stay: a Wappi list without status done aborts the sweep, a non-boolean `authorized` is
      never acted on, and every delete is preceded by a get/status re-check that must echo the profile id and say false.

MUTATIONS is the tripwire's own list (label, mutation, the message it must produce); a normal run applies each one to a
copy of a PASSING workflow and fails if the checks accept it — a tripwire that accepts a broken sweep is no tripwire.

    python3 Tools/n8n/verify-orphan-sweep.py              # the canonical workflows
    python3 Tools/n8n/verify-orphan-sweep.py --dir DIR    # another set of exports (mutation tests)
"""
import argparse
import copy
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
FILE = "2islisFH7jjLoPQM-Delete_Orphan_Profiles.json"
LIFECYCLE = "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json"
LIVE = "Live Profiles"
LIVE_SQL = "select profile_id from bot_profiles where deleted_at is null;"
EXPECTED = ("Every Hour", "Sweep Config", "List WA Profiles", "List TG Profiles", LIVE, "Plan Sweep", "Split Candidates",
            "Re-check Status", "Confirm Orphan", "Still Unauthorized?", "Delete Profile", "Stamp Result", "Sweep Report")

# The rule, as Plan Sweep states it — (3).
READ_LIVE = "const liveItems = $input.all().map(function (i) { return i.json; });"
PROVE_LIVE = ("const liveProven = liveItems.length > 0 && liveItems.every(function (r) { return !!r && typeof r.profile_id === "
              "'string' && r.profile_id.trim() !== ''; });")
LIVE_SET = "const liveIds = new Set(liveProven ? liveItems.map(function (r) { return r.profile_id.trim(); }) : []);"
EXCLUDE = "if (!liveProven || liveIds.has(id)) { ps.protectedLive += 1; stats.protectedLive += 1; continue; }"
TTL_GATE = "if (ageMs < ttlMs || recentlyActive) { ps.underTtl += 1; continue; }"
CANDIDATE = "candidates.push({"
# (5) the guards that were there before A4
STATUS_ABORT = "if (!src.payload || src.payload.status !== 'done') {"
AMBIGUITY = "if (p.authorized !== false) { stats.skipped.push({"
RECHECK = "const verifyOk = typeof resp.authorized === 'boolean' && resp.profile_id === cand.profileId;"


def node(wf, name):
    return next((n for n in wf.get("nodes", []) if n.get("name") == name), None)


def targets(wf, src):
    return [[e["node"] for e in (out or [])] for out in (wf["connections"].get(src) or {}).get("main", [])]


def sources(wf, dst):
    return sorted((s, i) for s, c in wf["connections"].items() for i, out in enumerate((c or {}).get("main") or [])
                  for e in (out or []) if e.get("node") == dst)


def reachable(wf, start, cut=None):
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for i, out in enumerate((wf["connections"].get(n) or {}).get("main") or []):
            if cut and (n, i) == cut:
                continue
            todo.extend(e["node"] for e in (out or []))
    return seen


def check(wf, lifecycle):
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)
        return cond

    names = [n.get("name") for n in wf.get("nodes", [])]
    for name in EXPECTED:
        need(names.count(name) == 1, f"expected exactly one node {name!r}, found {names.count(name)}")
    if any(names.count(x) != 1 for x in EXPECTED):
        return fails

    # (4) nothing disabled
    for n in wf["nodes"]:
        need(not n.get("disabled"), f"{n['name']} is disabled — n8n hands a disabled node's input on untouched (a disabled "
                                    f"Live Profiles turns the sweep into a silent no-op; a disabled Plan Sweep or re-check worse)")

    # (1) the live read
    lv = node(wf, LIVE)
    p = lv.get("parameters") or {}
    need(lv.get("type") == "n8n-nodes-base.postgres" and p.get("operation") == "executeQuery",
         f"{LIVE} must be a Postgres executeQuery node: {lv.get('type')} {p.get('operation')}")
    need((p.get("query") or "").strip() == LIVE_SQL,
         f"{LIVE} must read every LIVE bot_profiles row (any subscription status): query {p.get('query')!r} != {LIVE_SQL!r}")
    need(not (p.get("options") or {}).get("queryReplacement"), f"{LIVE} takes no parameters: {p.get('options')}")
    ref = node(lifecycle, "Alive Registry") if lifecycle else None
    want_cred = (ref or {}).get("credentials")
    need(bool(want_cred) and lv.get("credentials") == want_cred,
         f"{LIVE} must run on the canonical Postgres credential the Profile Lifecycle Sweep uses {want_cred}: {lv.get('credentials')}")
    need(lv.get("alwaysOutputData") is True and lv.get("onError") == "continueRegularOutput",
         f"{LIVE} must continue on error with output (alwaysOutputData + continueRegularOutput) — an empty answer or a "
         f"failure reaches Plan Sweep, whose fail-safe protects every profile: alwaysOutputData={lv.get('alwaysOutputData')} "
         f"onError={lv.get('onError')}")

    # (2) the wiring: no way to the delete branch around the live read
    need(targets(wf, "List TG Profiles") == [[LIVE]], f"List TG Profiles must feed ONLY {LIVE}: {targets(wf, 'List TG Profiles')}")
    need(targets(wf, LIVE) == [["Plan Sweep"]], f"{LIVE} must feed ONLY Plan Sweep: {targets(wf, LIVE)}")
    need(sources(wf, "Plan Sweep") == [(LIVE, 0)], f"Plan Sweep must be entered ONLY from {LIVE}: {sources(wf, 'Plan Sweep')}")
    need("Delete Profile" not in reachable(wf, "Every Hour", cut=(LIVE, 0)),
         f"Delete Profile is reachable without passing {LIVE} — a sweep could delete without the live read")

    # (3) Plan Sweep proves the live set and excludes it right before a profile becomes a candidate
    js = (node(wf, "Plan Sweep").get("parameters") or {}).get("jsCode") or ""
    need(READ_LIVE in js and LIVE_SET in js, "Plan Sweep must read the live ids from its input (Live Profiles' rows)")
    need(PROVE_LIVE in js,
         "Plan Sweep fail-safe: only rows that EACH carry a non-empty profile id prove the live set — no row (an empty answer), "
         "an error item, a passed-on input or an id-less row must protect EVERY profile")
    need(EXCLUDE in js and TTL_GATE in js and CANDIDATE in js
         and js.index(TTL_GATE) < js.index(EXCLUDE) < js.index(CANDIDATE),
         "Plan Sweep must exclude a live (or unproven) profile right before it becomes a candidate, counting it protectedLive")
    need("protectedLive: 0" in js and js.count("protectedLive: 0") >= 2,
         "Plan Sweep must report protectedLive in its stats and per platform")

    # (5) the guards that were there stay
    need(STATUS_ABORT in js and "throw new Error(" in js, "Plan Sweep must abort (nothing deleted) on a Wappi list without status done")
    need(AMBIGUITY in js, "Plan Sweep must never act on a non-boolean authorized flag")
    conf = (node(wf, "Confirm Orphan").get("parameters") or {}).get("jsCode") or ""
    need(RECHECK in conf and "confirmedOrphan: verifyOk && resp.authorized === false" in conf,
         "Confirm Orphan must re-check get/status (echoed profile id, authorized false) before any delete")
    need(targets(wf, "Re-check Status") == [["Confirm Orphan"]] and targets(wf, "Confirm Orphan") == [["Still Unauthorized?"]]
         and targets(wf, "Still Unauthorized?") == [["Delete Profile"]] and sources(wf, "Delete Profile") == [("Still Unauthorized?", 0)],
         "every delete must pass Re-check Status → Confirm Orphan → Still Unauthorized?")
    return fails


# ---------------------------------------------------------------------------------------------------------------------
# The tripwire's own list: (label, mutation of a workflow dict, the message the checks must produce).
def _plan(wf):
    return node(wf, "Plan Sweep")["parameters"]


def _replace_plan(old, new):
    def m(wf):
        p = _plan(wf)
        assert old in p["jsCode"], f"mutation anchor not found in Plan Sweep: {old[:60]!r}"
        p["jsCode"] = p["jsCode"].replace(old, new, 1)
    return m


def _set(name, key, value):
    def m(wf):
        n = node(wf, name)
        if value is None:
            n.pop(key, None)
        else:
            n[key] = value
    return m


def _bypass(wf):
    wf["connections"]["List TG Profiles"] = {"main": [[{"node": "Plan Sweep", "type": "main", "index": 0}]]}
    wf["connections"].pop(LIVE, None)


def _query(sql):
    def m(wf):
        node(wf, LIVE)["parameters"]["query"] = sql
    return m


MUTATIONS = (
    ("Live Profiles disabled", _set(LIVE, "disabled", True), "Live Profiles is disabled"),
    ("Plan Sweep disabled", _set("Plan Sweep", "disabled", True), "Plan Sweep is disabled"),
    ("Live Profiles bypassed (List TG Profiles feeds Plan Sweep directly)", _bypass, "List TG Profiles must feed ONLY Live Profiles"),
    ("Live Profiles reads retired rows too", _query("select profile_id from bot_profiles;"), "must read every LIVE bot_profiles row"),
    ("Live Profiles reads only paying owners' rows",
     _query("select b.profile_id from bot_profiles b join subscribers s on s.app_user_id = b.app_user_id "
            "where b.deleted_at is null and s.status = 'active';"), "must read every LIVE bot_profiles row"),
    ("Live Profiles stops the run on a database error (no continue)", _set(LIVE, "onError", None), "must continue on error with output"),
    ("Live Profiles on another credential", _set(LIVE, "credentials", {"postgres": {"id": "dev0000000000000", "name": "Postgres Dev"}}),
     "canonical Postgres credential"),
    ("Plan Sweep: the live-row exclusion removed", _replace_plan(EXCLUDE, ""), "right before it becomes a candidate"),
    ("Plan Sweep: the fail-safe removed (an unproven read protects nothing)",
     _replace_plan(PROVE_LIVE + "\n" + LIVE_SET,
                   "const liveProven = true;\nconst liveIds = new Set(liveItems.filter(function (r) { return r && typeof r.profile_id "
                   "=== 'string'; }).map(function (r) { return r.profile_id.trim(); }));"),
     "Plan Sweep fail-safe"),
    ("Plan Sweep: an empty answer trusted as «no live rows»",
     _replace_plan(PROVE_LIVE + "\n" + LIVE_SET,
                   "const liveProven = liveItems.every(function (r) { return !!r && (Object.keys(r).length === 0 || "
                   "(typeof r.profile_id === 'string' && r.profile_id.trim() !== '')); });\nconst liveIds = new Set(liveProven ? "
                   "liveItems.filter(function (r) { return typeof r.profile_id === 'string'; }).map(function (r) { return "
                   "r.profile_id.trim(); }) : []);"),
     "Plan Sweep fail-safe"),
    ("Plan Sweep: the ambiguity rule removed", _replace_plan(AMBIGUITY, "if (false) { stats.skipped.push({"),
     "non-boolean authorized"),
)


def self_check(wf, lifecycle):
    """Every mutation of a PASSING workflow must be rejected with its message — else the tripwire is broken."""
    broken = []
    for label, mutate, expect in MUTATIONS:
        bad = copy.deepcopy(wf)
        try:
            mutate(bad)
        except AssertionError as e:
            broken.append(f"tripwire self-check: mutation {label!r} does not apply ({e})")
            continue
        got = check(bad, lifecycle)
        if not any(expect in m for m in got):
            broken.append(f"tripwire self-check: mutation {label!r} was not rejected with {expect!r} (got {got})")
    return broken


def run(directory=None):
    d = directory or os.path.join(HERE, "workflows")
    with open(os.path.join(d, FILE), encoding="utf-8") as fh:
        wf = json.load(fh)
    lifecycle = None
    if os.path.exists(os.path.join(d, LIFECYCLE)):
        with open(os.path.join(d, LIFECYCLE), encoding="utf-8") as fh:
            lifecycle = json.load(fh)
    fails = check(wf, lifecycle)
    if not fails:
        fails = self_check(wf, lifecycle)
    return fails


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir")
    a = ap.parse_args()
    fails = run(a.dir)
    for m in fails:
        print(f"ORPHAN SWEEP FAIL [{FILE}]: {m}")
    if fails:
        print(f"ORPHAN SWEEP FAILED: {len(fails)} check(s)")
        sys.exit(1)
    print(f"OK  {FILE} (Live Profiles → Plan Sweep: a live bot_profiles row is never deleted, an unproven live set protects "
          f"every profile, nothing disabled, the old guards intact; tripwire self-check: {len(MUTATIONS)} mutations rejected)")
    print("ALL ORPHAN SWEEP ASSERTS PASSED")


if __name__ == "__main__":
    main()
