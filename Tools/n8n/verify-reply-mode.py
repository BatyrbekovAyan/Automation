#!/usr/bin/env python3
"""Structural-assert verifier for the A2 confirmable reply mode (audit stage 2). No network.

Pins:
  * Set Reply Mode: Validate emits ONE item with a version (legacy → server "now"); the upsert is ONE
    statement over every requested profile, guarded `where reply_mode_flags.version < excluded.version`,
    reporting applied/suppressed/version per profile; Shape Response never turns a DB error into
    success; malformed body → HTTP 400; the response code is driven by the shaped `_status`.
  * Create WhatsApp/Telegram: `Seed Reply Mode` sits strictly BETWEEN `Get Created Workflow Id` and
    `Activate Created Workflow` (initial mode before any auto-reply is possible), writes only when the
    client states `ReplyMode`, and is version-guarded; Activate resolves the id by node name.
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
    assert "return [{ json: { invalid: false, profileIds: ids, chatId: b.chatId, suppressed: b.suppressed, version, legacy: !hasVersion } }]" in js, \
        f"{f}: Validate must emit ONE item carrying the version (no per-profile fan-out)"
    assert "Date.now()" in js, f"{f}: a legacy body (no version) must get a server-assigned version"
    up = node(wf, "Upsert Reply Mode")
    q = sql(up["parameters"]["query"])
    assert "jsonb_array_elements_text($1::jsonb)" in q, f"{f}: upsert must expand the whole profile list in ONE statement"
    assert "where reply_mode_flags.version < excluded.version" in q, f"{f}: upsert must be version-guarded (older desire never overwrites newer)"
    assert 'as "profileId"' in q and "as applied" in q and "returning profile_id, suppressed, version" in q, f"{f}: upsert must report per-profile applied/suppressed/version"
    assert up.get("alwaysOutputData") is True and up.get("onError") == "continueRegularOutput", f"{f}: upsert must surface a DB error as an item (never a swallowed 500)"
    sh = node(wf, "Shape Response")["parameters"]["jsCode"]
    assert "error: 'db_error', _status: 500" in sh and "allApplied" in sh, f"{f}: Shape Response must fail closed on db error and compute allApplied"
    assert node(wf, "Respond Error")["parameters"]["options"].get("responseCode") == 400, f"{f}: malformed body must answer 400"
    assert node(wf, "Respond")["parameters"]["options"].get("responseCode") == "={{ $json._status || 200 }}", f"{f}: Respond must use the shaped status"
    assert edge(wf, "Upsert Reply Mode") == [{"node": "Shape Response", "type": "main", "index": 0}]
    assert edge(wf, "Shape Response") == [{"node": "Respond", "type": "main", "index": 0}]
    assert edge(wf, "If invalid?", 0) == [{"node": "Respond Error", "type": "main", "index": 0}]
    assert edge(wf, "If invalid?", 1) == [{"node": "Upsert Reply Mode", "type": "main", "index": 0}]
    print(f"OK  {f} (versioned atomic upsert, per-profile report, 400/500 codes)")


def check_create(f, field):
    wf = load(f)
    seed = node(wf, "Seed Reply Mode")
    q = sql(seed["parameters"]["query"])
    assert "insert into reply_mode_flags" in q and "$2 in ('semi', 'auto')" in q and "where reply_mode_flags.version < excluded.version" in q, \
        f"{f}: Seed Reply Mode must write only a stated mode, version-guarded"
    assert "$1 <> '-1'" in q, f"{f}: seed must skip the unauthed sentinel"
    assert field in seed["parameters"]["options"]["queryReplacement"] and "ReplyMode" in seed["parameters"]["options"]["queryReplacement"]
    assert edge(wf, "Get Created Workflow Id") == [{"node": "Seed Reply Mode", "type": "main", "index": 0}], f"{f}: seed must follow Get Created Workflow Id"
    assert edge(wf, "Seed Reply Mode") == [{"node": "Activate Created Workflow", "type": "main", "index": 0}], f"{f}: activation must come AFTER the seed"
    act = node(wf, "Activate Created Workflow")["parameters"]["url"]
    assert "$('Get Created Workflow Id').first().json.id" in act, f"{f}: Activate must resolve the id by node name (the seed node drops the item)"
    print(f"OK  {f} (initial reply mode seeded before activation)")


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
