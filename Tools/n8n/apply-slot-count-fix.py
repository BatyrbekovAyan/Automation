#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""2026-09-24 hotfix (found on prod right after the A1 deploy): the Wappi Proxy's `Bind Profile` registers a
bot_profiles row the moment a profile is CREATED (owner-bound from birth — A1), but `Count Channels` in the
Create WA/TG workflows counts every live row of the app user — including the profile being created, which
before A1 was registered only AFTER the slot check. Two consequences: an off-by-one for every plan (Start's
single channel is refused as «1 >= 1»), and a refused profile — deleted at Wappi by `Delete Refused Profile` —
keeps a live row that inflates the count on the next attempt (the owner's three tries counted 1, 2, 3).
Fix, idempotent, both Create workflows: (1) `Count Channels` excludes the profile being created ($4);
(2) a `Retire Refused Profile` Postgres node after `Delete Refused Profile` marks the row deleted
(`deleted_reason = 'channel_limit'`) so nothing lingers. `--check` verifies without writing.
    python3 Tools/n8n/apply-slot-count-fix.py          # patch the canonical JSONs
    python3 Tools/n8n/apply-slot-count-fix.py --check  # assert the fix is present
"""
import copy
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TARGETS = {
    "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json": ("whatsapp", "WhatsappProfileId"),
    "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json": ("telegram", "TelegramProfileId"),
}
EXCLUDE_LINE = "    and bp.profile_id is distinct from nullif($4,'')\n"
RETIRE_SQL = ("-- 2026-09-24: a profile refused for the channel limit was already deleted at Wappi (node above); retire its\n"
              "-- A1-registered row too, or it keeps counting as an occupied channel on the next attempt.\n"
              "update bot_profiles\n"
              "set deleted_at = now(), deleted_reason = 'channel_limit'\n"
              "where profile_id = nullif($1,'') and deleted_at is null and workflow_id is null;")


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def patch(wf, channel, id_field):
    changed = False
    cc = node(wf, "Count Channels")
    q = cc["parameters"]["query"]
    if EXCLUDE_LINE.strip() not in q:
        marker = "    and bp.deleted_at is null\n"
        assert marker in q, "Count Channels: deleted_at marker"
        cc["parameters"]["query"] = q.replace(marker, marker + EXCLUDE_LINE, 1)
        changed = True
    qr = cc["parameters"]["options"]["queryReplacement"]
    want = ("={{ [ $('Unity Webhook').first().json.body.AppUserID, '%s', $('Unity Webhook').first().json.body.BotKey, "
            "$('Unity Webhook').first().json.body.%s ] }}" % (channel, id_field))
    if qr != want:
        assert "BotKey ] }}" in qr, "Count Channels: queryReplacement shape"
        cc["parameters"]["options"]["queryReplacement"] = want
        changed = True
    if node(wf, "Retire Refused Profile") is None:
        drp = node(wf, "Delete Refused Profile")
        retire = {
            "parameters": {"operation": "executeQuery", "query": RETIRE_SQL,
                           "options": {"queryReplacement": "={{ [ $('Unity Webhook').first().json.body.%s ] }}" % id_field}},
            "id": "retire-refused-%s" % channel, "name": "Retire Refused Profile",
            "type": "n8n-nodes-base.postgres", "typeVersion": cc["typeVersion"],
            "position": [drp["position"][0] + 200, drp["position"][1]],
            "credentials": copy.deepcopy(cc["credentials"]), "onError": "continueRegularOutput",
        }
        wf["nodes"].append(retire)
        conns = wf["connections"]
        assert conns["Delete Refused Profile"]["main"][0][0]["node"] == "Respond Channel Limit"
        conns["Delete Refused Profile"] = {"main": [[{"node": "Retire Refused Profile", "type": "main", "index": 0}]]}
        conns["Retire Refused Profile"] = {"main": [[{"node": "Respond Channel Limit", "type": "main", "index": 0}]]}
        changed = True
    return changed


def check(wf, channel, id_field):
    cc = node(wf, "Count Channels")
    assert EXCLUDE_LINE.strip() in cc["parameters"]["query"], "Count Channels excludes the profile being created"
    assert cc["parameters"]["options"]["queryReplacement"].endswith("body.%s ] }}" % id_field), "Count Channels passes the profile id as $4"
    r = node(wf, "Retire Refused Profile")
    assert r and "deleted_reason = 'channel_limit'" in r["parameters"]["query"] and "workflow_id is null" in r["parameters"]["query"]
    assert r["credentials"] == cc["credentials"], "same Postgres credential as Count Channels"
    assert wf["connections"]["Delete Refused Profile"]["main"][0][0]["node"] == "Retire Refused Profile"
    assert wf["connections"]["Retire Refused Profile"]["main"][0][0]["node"] == "Respond Channel Limit"


def main():
    only_check = "--check" in sys.argv
    for fname, (channel, id_field) in TARGETS.items():
        path = os.path.join(HERE, "workflows", fname)
        wf = json.load(open(path, encoding="utf-8"))
        if only_check:
            check(wf, channel, id_field)
            print(f"OK  {fname}")
            continue
        changed = patch(wf, channel, id_field)
        check(wf, channel, id_field)
        if changed:
            json.dump(wf, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
            open(path, "a", encoding="utf-8").write("\n")
        print(f"{'CHANGED' if changed else 'up-to-date'}  {fname}")


if __name__ == "__main__":
    main()
