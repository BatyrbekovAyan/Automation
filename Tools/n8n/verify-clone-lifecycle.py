#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Structural verifier for the bot's line in Create WhatsApp/Telegram (F1 + F2, task T4 of
docs/audits/2026-10-09-followups/F1F2-plan.md; the graph is written by apply-clone-lifecycle.py, plan §1.4). No network.

Behaviour lives in tests/test_create_clone.py (the graph end to end) and tests/test_clone_lineage_sql.py (the SQL); this
file pins the SHAPE that behaviour rests on, so a later generator or a hand edit cannot quietly break it:

  (1) the clone chain is entered only through the reply-mode seed gate and the plan: `Create Workflow` and
      `Activate Created Workflow` are reachable from `Unity Webhook` ONLY through `Seed Saved?`[0] AND `Plan Read?`[0];
      the template is read only from `Reuse?`[1] / `Bound Clone Gone?`[0]; a foreign profile is refused before both;
  (2) the answer with an id goes out ONLY on the bind's positive marker: with `Bound?`[0] cut, `Send New Workflows Id` is
      unreachable; every «no» after the clone exists (`Activated?`, `Webhook Set?`, `Types Set?`, `Bound?`) goes to
      `Discard?`, whose yes deletes the NEW clone (`Discard New Clone` → `Discarded?` → journal on failure) and every
      way out answers `Respond Create Failed`; a reused clone is never discarded (`Discard?` requires created === true);
  (3) every n8n API / Wappi call after the plan (Probe, Activate, Wappi webhook + types, Delete Replaced, Discard) runs
      with fullResponse + neverError + onError continueRegularOutput and is followed by an If on its `statusCode`;
  (4) the Postgres nodes decide on positive markers: Clone Plan (`planned === true`, retried, always answers),
      Bind (`bound === true` and the Clone Id, retried, always answers), each statement = clone_lineage's;
  (5) no refusal carries an id: every terminal Respond is a Set of exactly {success:false, error:<word>} (no other
      fields, no input merged); the success answer is `Send New Workflows Id` (executeOnce) = {id, created, adoptedProfiles};
  (6) every n8n API call goes to http://localhost:5678/api/v1/workflows with exactly the `n8n Admin API` placeholder
      credential (`__HDR_CRED__`, bound per instance by deploy-canonical.py);
  (7) every protected-id list in the graph (Reuse?, Discard?, Replaced Items, the SQL) equals
      build-client-webhooks.PROTECTED_IDS + Set Workflow State + the support relay;
  (8) `PreviousProfileId` / `CreateOrigin` are NOT in the Auth Gate RULE (proven in the database by Clone Plan, never by
      the token — the V12 / M5 lesson) and the RULE is apply-auth-gate.py's;
  (9) only a REUSED clone may skip activation (`Already Active?` requires created === false), the retired
      `Restamp RAG Chunks` is gone, and the hot path holds as many Postgres nodes as before the line (seven);
  (10) WhatsApp and Telegram are the same graph (node names, types and connections);
  (11) T5 — the Profile Lifecycle Sweep's branch A deletes what the line booked: `Clone Retirements` = clone_lineage's
      statement, fed by `Sweep Config` only; the DELETE is reachable ONLY past `Retirements Dry Run?`[1] (which deletes on
      an explicit `retirementsDryRun === false` only) and `Has Retired Clone?`[0] (a clone-shaped id outside the protected
      list); it is a localhost DELETE with the `n8n Admin API` placeholder, its status reported and judged by
      `Retired Clone Gone?` (200/404 → Stamp Retirement Removed, else → Stamp Retirement Kept, both clone_lineage's);
      branch A sits below every other branch; V12 `Retired Clones` skips superseded rows.

    python3 Tools/n8n/verify-clone-lifecycle.py                  # the canonical workflows
    python3 Tools/n8n/verify-clone-lifecycle.py --dir DIR        # another set of exports (mutation tests)
    python3 Tools/n8n/verify-clone-lifecycle.py --rev 5ac9b2f9   # the canon of a git revision (must fail before T3)
"""
import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


CL = _module("clone_lineage", "clone_lineage.py")
BCW = _module("bcw", "build-client-webhooks.py")
GATE = _module("apply_auth_gate", "apply-auth-gate.py")
PROTECTED = sorted(set(BCW.PROTECTED_IDS) | {"0DP79STI3pafXO9B", "olmqWMfcFpCEcpiE"})
ADMIN = {"httpHeaderAuth": {"id": "__HDR_CRED__", "name": "n8n Admin API"}}
LOCAL_API = "http://localhost:5678/api/v1/workflows"
FILES = {CL.CHANNELS[ch]["create_file"]: ch for ch in CL.CHANNELS}
SWEEP_FILE = "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json"
SWEEPGEN = _module("apply_sweep_clone_deactivate", "apply-sweep-clone-deactivate.py")
CHECKED_CALLS = ("Probe Bound Clone", "Activate Created Workflow", "Set Wappi Webhook", "Set Wappi Webhook Types",
                 "Delete Replaced Clone", "Discard New Clone")
API_NODES = ("Get Sample Workflow", "Create Workflow", "Probe Bound Clone", "Activate Created Workflow",
             "Delete Replaced Clone", "Discard New Clone")
STATUS_GATES = {"Probe Bound Clone": "Bound Clone Usable?", "Activate Created Workflow": "Activated?",
                "Set Wappi Webhook": "Webhook Set?", "Set Wappi Webhook Types": "Types Set?", "Discard New Clone": "Discarded?"}
NO_AFTER_CLONE = (("Activated?", 1), ("Webhook Set?", 1), ("Types Set?", 1), ("Bound?", 1))
HOT_PATH_PG = {"Ensure Subscriber", "Count Channels", "Retire Same Bot Slot", "Register Profile", "Seed Reply Mode",
               "Clone Plan", "Bind Workflow Id"}
REFUSAL_WORDS = {"Respond Plan Unavailable": "clone_plan_unavailable", "Respond Profile Owned": "profile_owned",
                 "Respond Clone Unavailable": "clone_unavailable", "Respond Create Failed": "create_failed",
                 "Respond Reply Mode Not Saved": "reply_mode_not_saved", "Respond Channel Limit": "channel_limit"}


def node(wf, name):
    n = next((x for x in wf["nodes"] if x["name"] == name), None)
    assert n is not None, f"node {name!r} missing"
    return n


def edge(wf, src, idx=0):
    outs = wf["connections"].get(src, {}).get("main", [])
    return [e["node"] for e in (outs[idx] if idx < len(outs) else []) or []]


def predecessors(wf, dst):
    return {(src, i) for src, c in wf["connections"].items() for i, out in enumerate(c.get("main", []) or [])
            for e in (out or []) if e["node"] == dst}


def reachable(wf, start, cut=()):
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for i, out in enumerate(wf["connections"].get(n, {}).get("main", []) or []):
            if (n, i) not in cut:
                todo.extend(e["node"] for e in (out or []))
    return seen


def cond(wf, name):
    c = node(wf, name)["parameters"]["conditions"]
    assert len(c["conditions"]) == 1 and c["conditions"][0]["operator"]["operation"] == "true", \
        f"{name}: must be ONE boolean-true condition (the whole decision in one expression)"
    return c["conditions"][0]["leftValue"]


def lists_in(text):
    """Every JSON array literal of 24-character-ish id lists embedded in an expression / code body."""
    return [json.loads(m) for m in re.findall(r'\[(?:"[A-Za-z0-9]{8,36}"(?:, ?)?)+\]', text)]


def assignments(n):
    return {a["name"]: a["value"] for a in n["parameters"]["assignments"]["assignments"]}


def check(fname, wf):
    """Every section runs; returns [(section, message)] for the ones that fail."""
    ch = FILES[fname]
    c = CL.CHANNELS[ch]
    names = {n["name"] for n in wf["nodes"]}
    fails = []

    sections = []
    def section_0():
        # (9) the retired node
        assert "Restamp RAG Chunks" not in names, f"{fname}: Restamp RAG Chunks is retired (its -1 rule lives in Bind)"
    sections.append(('(9) the retired node', section_0))

    def section_1():
        # (1) entry: seed gate → plan → (foreign refused) → reuse | template
        for target in ("Create Workflow", "Activate Created Workflow", "Get Sample Workflow", "Reused Clone Id"):
            assert target in reachable(wf, "Unity Webhook"), f"{fname}: {target} unreachable"
            for gate in (("Seed Saved?", 0), ("Plan Read?", 0), ("Legacy Owned?", 1)):
                assert target not in reachable(wf, "Unity Webhook", cut={gate}), \
                    f"{fname}: {target} is reachable around {gate[0]}[{gate[1]}] — the clone chain must pass the seed gate, the plan " \
                    f"and the foreign-profile refusal"
        assert predecessors(wf, "Clone Plan") == {("Seed Saved?", 0)}, f"{fname}: Seed Saved?[0] must be the ONLY way into Clone Plan"
        assert predecessors(wf, "Get Sample Workflow") == {("Reuse?", 1), ("Bound Clone Gone?", 0)}, \
            f"{fname}: the template is read only when nothing is reused: {sorted(predecessors(wf, 'Get Sample Workflow'))}"
        assert edge(wf, "Legacy Owned?", 0) == ["Respond Profile Owned"], f"{fname}: a foreign profile must be refused (profile_owned)"
        assert "$json.planned === true" in cond(wf, "Plan Read?"), f"{fname}: Plan Read? must read the plan's positive marker"
        assert "$json.foreign === true" in cond(wf, "Legacy Owned?"), f"{fname}: Legacy Owned? must read `foreign`"
        assert edge(wf, "Plan Read?", 1) == ["Respond Plan Unavailable"], f"{fname}: a missing plan must be refused, nothing created"
        assert edge(wf, "Bound Clone Gone?", 1) == ["Respond Clone Unavailable"], \
            f"{fname}: a bound clone of unknown state must be refused (never a second clone on its path)"
        usable = cond(wf, "Bound Clone Usable?")
        assert "Number(r.statusCode) === 200" in usable and "r.body.isArchived !== true" in usable and "n.name === 'Webhook'" in usable \
            and "n.parameters.path === pid" in usable and c["profile_field"] in usable, \
            f"{fname}: Bound Clone Usable? must prove 200, not archived, the Webhook path = this profile: {usable}"
        gone = cond(wf, "Bound Clone Gone?")
        assert "=== 404" in gone and "isArchived === true" in gone, f"{fname}: only a 404 or an archived clone may be replaced: {gone}"
    sections.append(('(1) entry: seed gate → plan → (foreign refused) → reuse | te', section_1))

    def section_2():
        # (2) the answer only on the positive bind; every «no» after the clone discards the NEW clone
        assert "Send New Workflows Id" in reachable(wf, "Unity Webhook") and \
            "Send New Workflows Id" not in reachable(wf, "Unity Webhook", cut={("Bound?", 0)}), \
            f"{fname}: Send New Workflows Id (the id answer) must be reachable ONLY through Bound?[0]"
        for src, idx in NO_AFTER_CLONE:
            assert edge(wf, src, idx) == ["Discard?"], f"{fname}: {src}[{idx}] must go to Discard? (the new clone must not survive): {edge(wf, src, idx)}"
        assert edge(wf, "Discard?", 0) == ["Discard New Clone"] and edge(wf, "Discard?", 1) == ["Respond Create Failed"], \
            f"{fname}: Discard? must delete the new clone or answer create_failed"
        dlv = cond(wf, "Discard?")
        assert "c.created === true" in dlv, f"{fname}: Discard? must never discard a REUSED clone: {dlv}"
        assert edge(wf, "Discard New Clone") == ["Discarded?"] and edge(wf, "Discarded?", 0) == ["Respond Create Failed"] \
            and edge(wf, "Discarded?", 1) == ["Record Discard Failure"] and edge(wf, "Record Discard Failure") == ["Respond Create Failed"], \
            f"{fname}: a failed discard must be journaled (Record Discard Failure), then create_failed"
        assert "[200, 404].includes(Number($json.statusCode))" in cond(wf, "Discarded?"), f"{fname}: Discarded? must accept only 200/404"
        rec = node(wf, "Record Discard Failure")
        assert rec["parameters"]["query"] == CL.RECORD_DISCARD_FAILURE_SQL and rec.get("alwaysOutputData") is True \
            and rec.get("onError") == "continueRegularOutput", f"{fname}: Record Discard Failure must be clone_lineage's and always answer"
        # Bound? → the booked replaced clones are deleted, then the answer
        assert edge(wf, "Bound?", 0) == ["Replaced Items"] and edge(wf, "Replaced Items") == ["Has Replaced?"] \
            and edge(wf, "Has Replaced?", 0) == ["Delete Replaced Clone"] and edge(wf, "Has Replaced?", 1) == ["Send New Workflows Id"] \
            and edge(wf, "Delete Replaced Clone") == ["Send New Workflows Id"], f"{fname}: Bound?[0] → Replaced Items → Delete → Send"
    sections.append(('(2) the answer only on the positive bind; every «no» after t', section_2))

    def section_3():
        # (3) checked calls
        for name in CHECKED_CALLS:
            n = node(wf, name)
            resp = (n["parameters"].get("options") or {}).get("response", {}).get("response", {})
            assert resp.get("fullResponse") is True and resp.get("neverError") is True and n.get("onError") == "continueRegularOutput", \
                f"{fname}: {name} must run with fullResponse + neverError + onError continueRegularOutput (a failure is a status, not a stop)"
        for call, gate in STATUS_GATES.items():
            assert edge(wf, call) == [gate] and "statusCode" in cond(wf, gate), f"{fname}: {call} must be followed by {gate} on its statusCode"
        for name in ("Activated?", "Webhook Set?", "Types Set?"):
            assert "Number($json.statusCode) === 200" in cond(wf, name), f"{fname}: {name} must require 200"
    sections.append(('(3) checked calls', section_3))

    def section_4():
        # (4) the Postgres decisions
        plan, bind = node(wf, "Clone Plan"), node(wf, "Bind Workflow Id")
        assert plan["parameters"]["query"] == CL.CLONE_PLAN_SQL, f"{fname}: Clone Plan is not clone_lineage.CLONE_PLAN_SQL"
        assert bind["parameters"]["query"] == CL.BIND_WORKFLOW_SQL[ch], f"{fname}: Bind Workflow Id is not clone_lineage.BIND_WORKFLOW_SQL[{ch}]"
        for n in (plan, bind):
            assert n.get("retryOnFail") is True and int(n.get("maxTries", 3)) >= 2 and n.get("alwaysOutputData") is True \
                and n.get("onError") == "continueRegularOutput", f"{fname}: {n['name']} must retry, continue on error and always answer"
        assert edge(wf, "Clone Plan") == ["Plan Read?"] and edge(wf, "Bind Workflow Id") == ["Bound?"], \
            f"{fname}: Clone Plan / Bind must feed their marker gates directly"
        bv = cond(wf, "Bound?")
        assert "$json.bound === true" in bv and "$json.workflow_id === $('Clone Id').first().json.id" in bv, \
            f"{fname}: Bound? must read the positive marker AND the bound id: {bv}"
        qr = plan["parameters"]["options"]["queryReplacement"]
        for piece in ("crAuth.mode", "crAuth.installId", c["profile_field"], f"'{ch}'", "BotKey", "CreateOrigin", "PreviousProfileId"):
            assert piece in qr, f"{fname}: Clone Plan's parameters lost {piece}: {qr}"
        bqr = bind["parameters"]["options"]["queryReplacement"]
        for piece in (c["profile_field"], "$('Clone Id').first().json.id", f"'{ch}'", "crAuth.installId", ".replace_ids", ".pred_profiles",
                      c["other_workflow_field"]):
            assert piece in bqr, f"{fname}: Bind's parameters lost {piece}: {bqr}"
    sections.append(('(4) the Postgres decisions', section_4))

    def section_5():
        # (5) refusals carry no id; the success answer
        terminals = {n for n in names if not any(edge(wf, n, i) for i in range(len(wf["connections"].get(n, {}).get("main", []) or [])))}
        for name in names:
            if name.startswith("Respond") and name in reachable(wf, "Seed Saved?"):
                n = node(wf, name)
                assert name in terminals, f"{fname}: {name} must be a terminal node (responseMode lastNode)"
                assert n["type"] == "n8n-nodes-base.set" and not n["parameters"].get("includeOtherFields"), \
                    f"{fname}: {name} must be a Set that merges no input (no id can leak)"
                assert assignments(n) == {"success": False, "error": REFUSAL_WORDS.get(name)}, \
                    f"{fname}: {name} must answer exactly {{success:false, error:{REFUSAL_WORDS.get(name)!r}}}: {assignments(n)}"
        send = node(wf, "Send New Workflows Id")
        assert "Send New Workflows Id" in terminals and send.get("executeOnce") is True and not send["parameters"].get("includeOtherFields"), \
            f"{fname}: Send New Workflows Id must be the terminal, executeOnce, merging nothing"
        sa = assignments(send)
        assert set(sa) == {"id", "created", "adoptedProfiles"} and "$('Clone Id').first().json.id" in sa["id"] \
            and "adopted_profiles" in sa["adoptedProfiles"], f"{fname}: the success answer must be {{id, created, adoptedProfiles}}: {sa}"
        assert node(wf, "Unity Webhook")["parameters"].get("responseMode") == "lastNode", f"{fname}: responseMode must stay lastNode"
    sections.append(('(5) refusals carry no id; the success answer', section_5))

    def section_6():
        # (6) n8n API: localhost + the placeholder credential; nothing else in the file calls n8n's API
        for name in API_NODES:
            n = node(wf, name)
            assert n["parameters"]["url"].lstrip("=").startswith(LOCAL_API), f"{fname}: {name} must call {LOCAL_API}: {n['parameters']['url']}"
            assert n.get("credentials") == ADMIN, f"{fname}: {name} must use exactly the `n8n Admin API` placeholder: {n.get('credentials')}"
        others = [n["name"] for n in wf["nodes"] if n["type"] == "n8n-nodes-base.httpRequest" and n["name"] not in API_NODES
                  and "/api/v1/" in str(n["parameters"].get("url", ""))]
        assert not others, f"{fname}: n8n API calls outside the checked nodes: {others}"
    sections.append(('(6) n8n API: localhost + the placeholder credential; nothing', section_6))

    def section_7():
        # (7) protected ids
        assert CL.PROTECTED == PROTECTED, f"clone_lineage.PROTECTED drifted from build-client-webhooks.PROTECTED_IDS (+ 2): {CL.PROTECTED}"
        for name, text in (("Reuse?", cond(wf, "Reuse?")), ("Discard?", cond(wf, "Discard?")),
                           ("Replaced Items", node(wf, "Replaced Items")["parameters"]["jsCode"])):
            found = lists_in(text)
            assert found and all(sorted(x) == PROTECTED for x in found), \
                f"{fname}: {name}'s protected list is not build-client-webhooks.PROTECTED_IDS + Set Workflow State + the support relay"
        for name in ("Clone Plan", "Bind Workflow Id", "Record Discard Failure"):
            q = node(wf, name)["parameters"]["query"]
            assert all(f"'{p}'" in q for p in PROTECTED), f"{fname}: {name}'s SQL does not exclude every protected id"
    sections.append(('(7) protected ids', section_7))

    def section_8():
        # (8) the gate's RULE: PreviousProfileId / CreateOrigin are proven by the plan, never by the token
        gate_code = node(wf, "Auth Gate")["parameters"]["jsCode"]
        m = re.search(r"const RULE = (\{.*?\});\n", gate_code)
        assert m, f"{fname}: the Auth Gate RULE is unreadable"
        rule = json.loads(m.group(1))
        assert rule == GATE.GATED[fname]["rule"], f"{fname}: the Auth Gate RULE is not apply-auth-gate.py's: {rule}"
        flat = json.dumps(rule)
        assert "PreviousProfileId" not in flat and "CreateOrigin" not in flat, \
            f"{fname}: PreviousProfileId / CreateOrigin must NOT be in the gate RULE (a retired profile is not in the token: Clone Plan proves it)"
    sections.append(("(8) the gate's RULE: PreviousProfileId / CreateOrigin are pr", section_8))

    def section_9():
        # (9) activation skipped only for a reused clone; the Postgres cost of the hot path
        assert edge(wf, "Clone Id") == ["Already Active?"] and edge(wf, "Already Active?", 1) == ["Activate Created Workflow"] \
            and edge(wf, "Already Active?", 0) == ["Set Wappi Webhook"] and edge(wf, "Activate Created Workflow") == ["Activated?"], \
            f"{fname}: Clone Id → Already Active? → (reused + serving: Set Wappi Webhook | Activate)"
        aa = cond(wf, "Already Active?")
        assert "$json.created === false" in aa and "$json.active === true" in aa, \
            f"{fname}: only a REUSED clone the probe saw active may skip activation: {aa}"
        on_path = {n for n in reachable(wf, "Unity Webhook") if "Send New Workflows Id" in reachable(wf, n)}
        hot_path = {n["name"] for n in wf["nodes"] if n["type"] == "n8n-nodes-base.postgres" and n["name"] in on_path}
        assert hot_path == HOT_PATH_PG, f"{fname}: the hot path's Postgres nodes changed (each ≈ 1.9 s on prod): {sorted(hot_path)}"
    sections.append(('(9) activation skipped only for a reused clone; the Postgres', section_9))

    for label, fn in sections:
        try:
            fn()
        except AssertionError as e:
            fails.append((label, str(e)))
        except Exception as e:      # a node the section needs is missing (a mutated or older graph)
            fails.append((label, f"{fname}: unexpected structural error: {type(e).__name__}: {e}"))
    return fails

def check_sweep(sw):
    """(11) T5: the sweep's branch A — every check runs; returns [message] of the failed ones."""
    fails = []

    def must(ok, msg):
        if not ok:
            fails.append("T5: " + msg)

    try:
        names = {n["name"] for n in sw["nodes"]}
        branch = ("Clone Retirements", "Retirements Dry Run?", "Log Would Remove", "Has Retired Clone?", "Delete Retired Clone",
                  "Retired Clone Gone?", "Stamp Retirement Removed", "Stamp Retirement Kept")
        missing = [n for n in branch if n not in names]
        if missing:
            return [f"T5: the sweep's branch A is missing {missing}"]
        must(node(sw, "Clone Retirements")["parameters"]["query"] == CL.CLONE_RETIREMENTS_SQL,
             "Clone Retirements must be clone_lineage.CLONE_RETIREMENTS_SQL (never a live-driven, protected or young clone)")
        must(predecessors(sw, "Clone Retirements") == {("Sweep Config", 0)}, "Clone Retirements must be fed by Sweep Config only")
        must(cond(sw, "Retirements Dry Run?") == SWEEPGEN.RETIREMENTS_DRY and "!== false" in cond(sw, "Retirements Dry Run?"),
             "Retirements Dry Run? must delete on an explicit retirementsDryRun === false only")
        flag = [a for a in assignments_list(node(sw, "Sweep Config")) if a.get("name") == "retirementsDryRun"]
        must(len(flag) == 1 and flag[0].get("type") == "boolean" and isinstance(flag[0].get("value"), bool),
             "Sweep Config must carry the boolean retirementsDryRun")
        must(edge(sw, "Clone Retirements") == ["Retirements Dry Run?"] and edge(sw, "Retirements Dry Run?", 0) == ["Log Would Remove"]
             and edge(sw, "Retirements Dry Run?", 1) == ["Has Retired Clone?"] and edge(sw, "Has Retired Clone?", 0) == ["Delete Retired Clone"]
             and edge(sw, "Has Retired Clone?", 1) == [] and edge(sw, "Delete Retired Clone") == ["Retired Clone Gone?"]
             and edge(sw, "Retired Clone Gone?", 0) == ["Stamp Retirement Removed"]
             and edge(sw, "Retired Clone Gone?", 1) == ["Stamp Retirement Kept"], "branch A wiring")
        for cut in (("Retirements Dry Run?", 1), ("Has Retired Clone?", 0)):
            must("Delete Retired Clone" not in reachable(sw, "Every 6 Hours", cut={cut}),
                 f"Delete Retired Clone reachable without {cut[0]}[{cut[1]}]")
        has = cond(sw, "Has Retired Clone?")
        must(has == SWEEPGEN.HAS_RETIRED_CLONE and lists_in(has) == [PROTECTED],
             "Has Retired Clone? must re-check the id shape and the protected list before the admin API")
        d = node(sw, "Delete Retired Clone")
        resp = (d["parameters"].get("options") or {}).get("response", {}).get("response", {})
        must(d["parameters"].get("method") == "DELETE" and d["parameters"].get("url") == "=" + LOCAL_API + "/{{ $json.workflow_id }}"
             and d.get("credentials") == ADMIN and resp.get("fullResponse") is True and resp.get("neverError") is True
             and d.get("onError") == "continueRegularOutput",
             "Delete Retired Clone must be a localhost DELETE with the n8n Admin API placeholder, reporting its status")
        must(cond(sw, "Retired Clone Gone?") == SWEEPGEN.RETIRED_GONE, "Retired Clone Gone? must stamp only 200/404 as gone")
        for n, sql in (("Stamp Retirement Removed", CL.STAMP_RETIREMENT_REMOVED_SQL), ("Stamp Retirement Kept", CL.STAMP_RETIREMENT_KEPT_SQL)):
            must(node(sw, n)["parameters"]["query"] == sql, f"{n} must be clone_lineage's statement")
        others = [n["position"][1] for n in sw["nodes"] if n["name"] not in branch and n["type"] != "n8n-nodes-base.stickyNote"]
        must(node(sw, "Clone Retirements")["position"][1] > max(others), "branch A must sit below every other branch")
        must("and bp.superseded_by is null" in node(sw, "Retired Clones")["parameters"]["query"],
             "V12 Retired Clones must skip superseded rows (the line booked their clone)")
    except AssertionError as e:
        fails.append(f"T5: {e}")
    return fails


def assignments_list(n):
    return n["parameters"]["assignments"]["assignments"]


def check_parity(wa, tg):
    """(10) the two Create graphs are one graph: the same node names, types, flags and connections."""
    sig = lambda wf: sorted((n["name"], n["type"], n.get("typeVersion"), n.get("onError"), n.get("retryOnFail"),
                             n.get("alwaysOutputData"), n.get("executeOnce")) for n in wf["nodes"])
    assert sig(wa) == sig(tg), "Create WA / TG node sets differ: " + str(sorted(set(map(str, sig(wa))) ^ set(map(str, sig(tg)))))[:600]
    assert wa["connections"] == tg["connections"], "Create WA / TG connections differ"


def load_set(directory=None, rev=None):
    out = {}
    for fname in tuple(FILES) + (SWEEP_FILE,):
        if rev:
            text = subprocess.run(["git", "-C", ROOT, "show", f"{rev}:Tools/n8n/workflows/{fname}"], capture_output=True,
                                  text=True, check=True).stdout
            out[fname] = json.loads(text)
        else:
            out[fname] = json.load(open(os.path.join(directory or os.path.join(HERE, "workflows"), fname), encoding="utf-8"))
    return out


def run(directory=None, rev=None, quiet=False):
    """[(file, message)] of every failed check — every check runs (one red check never hides the next)."""
    wfs = load_set(directory, rev)
    fails = []
    got = check_sweep(wfs.pop(SWEEP_FILE))
    fails += [(SWEEP_FILE, msg) for msg in got]
    if not got and not quiet:
        print(f"OK  {SWEEP_FILE} (T5: branch A deletes the booked clones past the dry-run gate; V12 skips superseded rows)")
    for fname, wf in wfs.items():
        got = check(fname, wf)
        fails += [(f"{fname} {label}", msg) for label, msg in got]
        if not got and not quiet:
            print(f"OK  {fname} (the bot's line: plan → reuse | create → bind on its marker → discard on every no)")
    try:
        check_parity(*[wfs[f] for f in FILES])
        if not quiet:
            print("OK  Create WA / TG are one graph")
    except AssertionError as e:
        fails.append(("parity", str(e)))
    return fails


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir")
    ap.add_argument("--rev")
    a = ap.parse_args()
    fails = run(a.dir, a.rev)
    for f, m in fails:
        print(f"CLONE LIFECYCLE FAIL [{f}]: {m}")
    print("ALL CLONE LIFECYCLE ASSERTS PASSED" if not fails else f"CLONE LIFECYCLE FAILED: {len(fails)} check(s)")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
