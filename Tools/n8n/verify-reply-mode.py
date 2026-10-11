#!/usr/bin/env python3
"""Structural-assert verifier for the A2 confirmable reply mode (audit stage 2). No network.

Pins:
  * Set Reply Mode: Validate emits ONE item with a version (legacy → server "now") and a decision time (R4-clock:
    server now − decisionAgeMs, else the version); the upsert is ONE
    statement over every requested profile, guarded `where reply_mode_flags.version < excluded.version`,
    reporting applied/suppressed/version per profile; Shape Response never turns a DB error into
    success; malformed body → HTTP 400; the response code is driven by the shaped `_status`.
  * Create WhatsApp/Telegram: `Seed Reply Mode` writes the initial mode only when the client states
    `ReplyMode`, version-guarded, and answers a positive `seeded` marker; `Seed Saved?` gates on it. R8-deletefail
    (iteration 2026-09-28): the gate stands BEFORE the clone exists — the seed's only predecessors are the two
    entries into the clone chain (`Register Profile`[0], `If Has AppUserID?`[1]), `Seed Saved?`[0] is the ONLY way
    into the clone chain, and with that one edge cut neither `Create Workflow` nor `Activate Created Workflow` is
    reachable from `Unity Webhook`; the false branch answers reply_mode_not_saved directly (nothing to delete: no
    compensating `Delete Unseeded Workflow`, whose own failure orphaned the clone). F1 + F2 (T3, 2026-10-11): the chain
    starts at `Clone Plan` (apply-clone-lifecycle.py) — the plan may REUSE the bound clone, so Activate is guarded as
    well as Create; Activate resolves the id through `Clone Id` (behaviour: tests/test_create_seed.py,
    tests/test_create_clone.py).
  * Bot templates: Read Reply Mode's coalesce(absence → false) is UNCHANGED (locked semantics).

    python3 Tools/n8n/verify-reply-mode.py [--dir DIR]
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")


def load(f):
    return json.load(open(os.path.join(WF, f), encoding="utf-8"))


def node(wf, name):
    n = next((x for x in wf["nodes"] if x["name"] == name), None)
    assert n is not None, f"node {name!r} missing"
    return n


def edge(wf, src, idx=0):
    return wf["connections"].get(src, {}).get("main", [[]])[idx]


def sql(q):
    return "\n".join(re.sub(r"--.*$", "", line) for line in q.split("\n"))


def check_set_reply_mode():
    f = "SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json"
    wf = load(f)
    js = node(wf, "Validate")["parameters"]["jsCode"]
    assert "return [{ json: { invalid: false, profileIds: ids, chatId: b.chatId, suppressed: b.suppressed, version, decided, legacy: !hasVersion } }]" in js, \
        f"{f}: Validate must emit ONE item carrying the version and the decision time (no per-profile fan-out)"
    assert "Date.now()" in js, f"{f}: a legacy body (no version) must get a server-assigned version"
    # R4-clock: the decision is dated on THIS clock from the client's age; without a valid age, by the version (as before)
    assert "const decided = hasAge ? Math.max(0, Date.now() - Number(age)) : version;" in js \
        and "const hasAge = hasVersion && ((Number.isInteger(age) && age >= 0)" in js, \
        f"{f}: Validate must date the decision as server now − decisionAgeMs, falling back to the version (R4-clock)"
    qr = up_params(wf)
    assert qr.rstrip().endswith("$json.version, $json.decided ] }}"), f"{f}: the decision time must be the upsert's fifth parameter: {qr}"
    up = node(wf, "Upsert Reply Mode")
    q = sql(up["parameters"]["query"])
    assert "jsonb_array_elements_text($1::jsonb)" in q, f"{f}: upsert must expand the whole profile list in ONE statement"
    assert "where reply_mode_flags.version < excluded.version" in q, f"{f}: upsert must be version-guarded (older desire never overwrites newer)"
    assert 'as "profileId"' in q and "as applied" in q and "returning profile_id, suppressed, version" in q, f"{f}: upsert must report per-profile applied/suppressed/version"
    assert up.get("alwaysOutputData") is True and up.get("onError") == "continueRegularOutput", f"{f}: upsert must surface a DB error as an item (never a swallowed 500)"
    check_resume_side_effects(q, f)
    sh = node(wf, "Shape Response")["parameters"]["jsCode"]
    assert "error: 'db_error', _status: 500" in sh and "allApplied" in sh, f"{f}: Shape Response must fail closed on db error and compute allApplied"
    assert node(wf, "Respond Error")["parameters"]["options"].get("responseCode") == 400, f"{f}: malformed body must answer 400"
    assert node(wf, "Respond")["parameters"]["options"].get("responseCode") == "={{ $json._status || 200 }}", f"{f}: Respond must use the shaped status"
    assert edge(wf, "Upsert Reply Mode") == [{"node": "Shape Response", "type": "main", "index": 0}]
    assert edge(wf, "Shape Response") == [{"node": "Respond", "type": "main", "index": 0}]
    assert edge(wf, "If invalid?", 0) == [{"node": "Respond Error", "type": "main", "index": 0}]
    assert edge(wf, "If invalid?", 1) == [{"node": "Upsert Reply Mode", "type": "main", "index": 0}]
    print(f"OK  {f} (versioned atomic upsert, per-profile report, 400/500 codes)")


def up_params(wf):
    return node(wf, "Upsert Reply Mode")["parameters"].get("options", {}).get("queryReplacement", "")


def check_resume_side_effects(q, f="Set Reply Mode"):
    """R4 (2026-09-27): a chat-scoped «Авто» resolves handoffs only when it is APPLIED, and only requests created no
    later than the command's version. Every side-effect UPDATE must read the applied rows (`resumed`, built from
    `up`), never the raw request `req` — a stale or replayed version is not applied and must change nothing.
    R4-clock (2026-09-28): the boundary is the DECISION time Validate computed (the fifth parameter, `decided`), never
    the version — the version is the phone's wall clock and only orders.
    The behaviour itself is pinned by tests/test_reply_mode.py (real SQL); this is the cheap structural tripwire."""
    assert "$5::bigint as decided" in q, f"{f}: the request must carry the decision time as a parameter (R4-clock)"
    assert re.search(r"resumed as \(\s*select up\.profile_id, r\.chat_id, up\.version, r\.decided\s+from up join req r on r\.profile_id = up\.profile_id\s+"
                     r"where r\.chat_id <> '\*' and up\.suppressed = false", q), \
        f"{f}: the resume set must be the APPLIED chat-scoped «Авто» rows (from up) with their decision time, never '*' and never the raw request"
    for table in ("handoff_requests", "chat_reply_state"):
        m = re.search(r"update " + table + r"\b(.*?)returning", q, re.S)
        assert m, f"{f}: side-effect update on {table} missing"
        body = m.group(1)
        assert "from resumed r" in body and not re.search(r"\bfrom req\b", body), \
            f"{f}: the {table} side effect must key off the applied rows (from resumed), not the request (R4)"
    closed = re.search(r"update handoff_requests\b(.*?)returning", q, re.S).group(1)
    assert "(extract(epoch from h.created_at) * 1000)::bigint <= r.decided" in closed and "r.version" not in closed, \
        f"{f}: an applied «Авто» may close only requests created no later than its DECISION time (R4, R4-clock)"
    unpause = re.search(r"update chat_reply_state\b(.*?)returning", q, re.S).group(1)
    assert "(extract(epoch from o.created_at) * 1000)::bigint > r.decided" in unpause and "not exists" in unpause and "r.version" not in unpause, \
        f"{f}: a request created after the decision must keep the chat paused (R4, R4-clock)"


ENTRIES_INTO_CLONE_CHAIN = {("If Has AppUserID?", 1), ("Register Profile", 0)}   # legacy (no AppUserID) / slot-allowed
CLONE_CHAIN = "Clone Plan"     # F1 + F2 (T3): the first node of the clone chain (apply-clone-lifecycle.py)


def predecessors(wf, dst):
    """Every (source, output index) whose main output connects to `dst`."""
    return {(src, i) for src, c in wf["connections"].items() for i, out in enumerate(c.get("main", []) or [])
            for e in (out or []) if e["node"] == dst}


def reachable(wf, start, cut=()):
    """Node names reachable from `start` over main edges, with the (source, output index) edges in `cut` removed."""
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


def check_create(f, field, wf=None):
    wf = wf if wf is not None else load(f)
    seed = node(wf, "Seed Reply Mode")
    q = sql(seed["parameters"]["query"])
    assert "insert into reply_mode_flags" in q and "$2 in ('semi', 'auto')" in q and "where reply_mode_flags.version < excluded.version" in q, \
        f"{f}: Seed Reply Mode must write only a stated mode, version-guarded"
    assert "$1 <> '-1'" in q, f"{f}: seed must skip the unauthed sentinel"
    assert field in seed["parameters"]["options"]["queryReplacement"] and "ReplyMode" in seed["parameters"]["options"]["queryReplacement"]
    # R8 (2026-09-27): going live only on the statement's POSITIVE marker. The seed continues on error (so a failure
    # can be answered), which is exactly why nothing may connect it straight onward: a Postgres error item, an error
    # thrown outside the query runner (the INPUT item passes on) and an empty result ({}) all flow on output 0.
    assert "select true as seeded" in q and "returning 1" in q, f"{f}: the seed must answer a positive `seeded` marker once it completed (R8)"
    assert seed.get("retryOnFail") is True and seed.get("maxTries", 0) >= 2, f"{f}: a transient seed failure must be retried (R8)"
    assert edge(wf, "Seed Reply Mode") == [{"node": "Seed Saved?", "type": "main", "index": 0}], \
        f"{f}: the seed must feed the marker gate, never the clone chain directly (R8)"
    gate = node(wf, "Seed Saved?")["parameters"]["conditions"]
    lv = gate["conditions"][0]["leftValue"]
    assert len(gate["conditions"]) == 1 and gate["conditions"][0]["operator"]["operation"] == "true" \
        and "$json.seeded === true" in lv and "!== 'semi'" in lv, f"{f}: Seed Saved? must pass only on the marker (or a non-«Вместе» create): {lv!r}"
    # R8-deletefail (2026-09-28): the gate stands BEFORE the clone exists, so a failed seed never creates one.
    assert predecessors(wf, "Seed Reply Mode") == ENTRIES_INTO_CLONE_CHAIN, \
        f"{f}: the seed must be entered from exactly {sorted(ENTRIES_INTO_CLONE_CHAIN)} — an entry wired past it skips the '*' row " \
        f"(«Авто» silently unseeded): {sorted(predecessors(wf, 'Seed Reply Mode'))}"
    assert edge(wf, "Seed Saved?", 0) == [{"node": CLONE_CHAIN, "type": "main", "index": 0}], \
        f"{f}: the clone chain (Clone Plan → reuse | template read → Create) must start only AFTER the saved marker (R8-deletefail)"
    assert predecessors(wf, CLONE_CHAIN) == {("Seed Saved?", 0)}, \
        f"{f}: Seed Saved?[0] must be the ONLY way into {CLONE_CHAIN}: {sorted(predecessors(wf, CLONE_CHAIN))}"
    for target in ("Create Workflow", "Activate Created Workflow"):
        assert target in reachable(wf, "Unity Webhook") \
            and target not in reachable(wf, "Unity Webhook", cut={("Seed Saved?", 0)}), \
            f"{f}: {target} must be reachable from Unity Webhook ONLY through Seed Saved?[0] (R8-deletefail; F1: the plan " \
            f"may reuse the bound clone, so its activation is guarded too)"
    assert predecessors(wf, "Get Sample Workflow") <= {("Reuse?", 1), ("Bound Clone Gone?", 0)}, \
        f"{f}: only the plan's create branches may read the template: {sorted(predecessors(wf, 'Get Sample Workflow'))}"
    assert edge(wf, "Clone Id") == [{"node": "Already Active?", "type": "main", "index": 0}] \
        and edge(wf, "Already Active?", 1) == [{"node": "Activate Created Workflow", "type": "main", "index": 0}], \
        f"{f}: the clone (created, or reused and not yet serving) goes straight to activation (the seed already ran)"
    assert not any(n["name"] == "Delete Unseeded Workflow" for n in wf["nodes"]) and "Delete Unseeded Workflow" not in wf["connections"], \
        f"{f}: no compensating DELETE may remain — a refused create has no clone to delete (R8-deletefail)"
    assert edge(wf, "Seed Saved?", 1) == [{"node": "Respond Reply Mode Not Saved", "type": "main", "index": 0}], \
        f"{f}: an unsaved «Вместе» mode must be answered reply_mode_not_saved"
    assert not wf["connections"].get("Respond Reply Mode Not Saved", {}).get("main", [[]])[0], f"{f}: the refusal must be the LAST node (responseMode lastNode)"
    body = {a["name"]: a["value"] for a in node(wf, "Respond Reply Mode Not Saved")["parameters"]["assignments"]["assignments"]}
    assert body == {"success": False, "error": "reply_mode_not_saved"}, f"{f}: refusal body must carry no id: {body}"
    act = node(wf, "Activate Created Workflow")["parameters"]["url"]
    assert "$('Clone Id').first().json.id" in act, f"{f}: Activate must resolve the id by node name (Clone Id)"
    print(f"OK  {f} (initial reply mode seeded before the clone exists; a clone only on the saved marker)")


def check_gate_read():
    for f in ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json"):
        q = sql(node(load(f), "Read Reply Mode")["parameters"]["query"])
        assert "coalesce(" in q and "false" in q and "chat_id in ($2, '*')" in q, f"{f}: Read Reply Mode's locked absence→reply semantics changed"
    print("OK  bot templates (gate read unchanged: absence → reply)")


def main():
    global WF
    ap = argparse.ArgumentParser(); ap.add_argument("--dir", default=WF)
    WF = ap.parse_args().dir
    try:
        check_set_reply_mode()
        check_create("XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "WhatsappProfileId")
        check_create("Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json", "TelegramProfileId")
        check_gate_read()
    except AssertionError as e:
        print(f"REPLY MODE FAIL: {e}")
        sys.exit(1)
    print("ALL REPLY MODE ASSERTS PASSED")


if __name__ == "__main__":
    main()
