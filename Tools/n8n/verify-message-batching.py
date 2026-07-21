#!/usr/bin/env python3
"""Structural-assert verifier for the Phase 10 message-batching / debounce splice.

Proves BOTH canonical bot templates carry the identical pre-generation debounce+combine
stage that `apply-message-batching.py` splices onto the Phase-9 `Suppressed?` FALSE branch
(BATCH-01/02). These are exactly the subtle fail-safe properties a UI round-trip or a bad
edit would silently drop — a dropped body re-emit, an added `mark_all`, a broken rewire, or
a channel-specific Code body. Run before deploying to dev n8n (owner's 10-03 step); no live
n8n / network needed.

Usage:
  python3 Tools/n8n/verify-message-batching.py            # verify the committed workflows/
  python3 Tools/n8n/verify-message-batching.py --dir DIR  # verify a prod re-export (go/no-go)

--dir DIR overrides the workflow directory (default: the committed workflows/ next to this
script). Point it at a prod re-export so the SAME structural asserts gate a prod import.
Absent --dir, behavior is byte-identical to before.

Exits 0 and prints "ALL BATCHING ASSERTS PASSED" when every assert holds.
Exits 1 with "BATCHING FAIL: <reason>" naming the first violated assert.
"""
import argparse
import copy
import json
import os
import sys

# Resolve workflow paths from this script's own location so cwd does not matter.
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WF = os.path.join(HERE, "workflows")
WF = DEFAULT_WF

WA_BOT = "4wYitz5ek30SVNlT-WhatsApp_Bot.json"
TG_BOT = "4VN3gsFaC2HUYmcc-Telegram_Bot.json"

# (template, its channel base). Only the base differs between the two — everything else in
# the spliced stage is byte-identical (the Code node is channel-agnostic: chat || text).
BOTS = [
    (WA_BOT, "https://wappi.pro/api/sync/"),
    (TG_BOT, "https://wappi.pro/tapi/sync/"),
]

WAPPI_CRED_ID = "EuhhqAaV56DpoqAN"       # WappiAuthToken, already bound in both templates
DEBOUNCE_AMOUNT = 8                       # ~8s in-memory Wait window
TEXT_VALUE = "={{ $json.combinedText ?? $json.body.messages[0].body }}"


def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as fh:
        return json.load(fh)


def node(nodes, name):
    for n in nodes:
        if n.get("name") == name:
            return n
    raise AssertionError(f"node '{name}' not found")


def assert_that(cond, reason):
    if not cond:
        raise AssertionError(reason)


def check_bot(f, base):
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    # (1) the 4 spliced nodes exist (node() raises a named AssertionError if any is missing).
    dw = node(ns, "Debounce Wait")
    fr = node(ns, "Fetch Recent")
    lc = node(ns, "Latest+Combine")
    il = node(ns, "Is Latest?")

    # (2) Debounce Wait is an n8n Wait with amount == 8 (< 65s -> in-memory resume).
    assert_that(dw["type"] == "n8n-nodes-base.wait", f"{f}: Debounce Wait is not a wait node")
    assert_that(dw["parameters"].get("amount") == DEBOUNCE_AMOUNT,
                f"{f}: Debounce Wait amount != {DEBOUNCE_AMOUNT}")

    # (3) Fetch Recent: GET <base>messages/get, cred EuhhqAaV56DpoqAN, query params exactly
    #     {profile_id, chat_id, limit} with NO mark_all (Pitfall 5 — marking read during the
    #     wait would defeat the deliberate downstream humanizer Mark Read).
    assert_that(fr["type"] == "n8n-nodes-base.httpRequest", f"{f}: Fetch Recent is not an httpRequest node")
    assert_that(fr["parameters"].get("method") == "GET", f"{f}: Fetch Recent is not a GET")
    assert_that(fr["parameters"].get("url") == base + "messages/get",
                f"{f}: Fetch Recent url wrong: {fr['parameters'].get('url')}")
    cred = fr.get("credentials", {}).get("httpHeaderAuth", {}).get("id")
    assert_that(cred == WAPPI_CRED_ID, f"{f}: Fetch Recent cred {cred} != {WAPPI_CRED_ID}")
    qp_names = [p.get("name") for p in fr["parameters"].get("queryParameters", {}).get("parameters", [])]
    assert_that(qp_names == ["profile_id", "chat_id", "limit"],
                f"{f}: Fetch Recent query params {qp_names} != ['profile_id','chat_id','limit']")
    assert_that("mark_all" not in qp_names,
                f"{f}: Fetch Recent carries mark_all (would mark the chat read during the wait — Pitfall 5)")

    # (4) Latest+Combine jsCode: .first() not .item (paired-item safety, Pitfall 1/anti-pattern);
    #     a time sort (Pitfall 3); the chat||text channel-agnostic test (Pitfall 4); and it
    #     re-emits the webhook body with abort + combinedText (Pitfall 1 — the big one).
    assert_that(lc["type"] == "n8n-nodes-base.code", f"{f}: Latest+Combine is not a code node")
    js = lc["parameters"]["jsCode"]
    assert_that("$('Webhook').first().json" in js,
                f"{f}: Latest+Combine does not reach back via $('Webhook').first().json")
    assert_that("$('Webhook').item" not in js,
                f"{f}: Latest+Combine uses the fragile .item back-reference (breaks across Wait+HTTP)")
    assert_that(".sort(" in js and "time" in js, f"{f}: Latest+Combine does not sort the fetch by time")
    assert_that("m.type === 'chat' || m.type === 'text'" in js,
                f"{f}: Latest+Combine is not channel-agnostic (missing chat || text)")
    assert_that("...wh" in js and "abort" in js and "combinedText" in js,
                f"{f}: Latest+Combine does not re-emit body ({{ ...wh, abort, combinedText }})")

    # Is Latest? boolean condition reads $json.abort.
    cond = il["parameters"]["conditions"]["conditions"][0]
    assert_that(cond["leftValue"] == "={{ $json.abort }}", f"{f}: Is Latest? condition does not read $json.abort")
    assert_that(cond["operator"].get("operation") == "true", f"{f}: Is Latest? operator is not boolean-true")

    # (5) Connections prove the debounce sits AFTER the Phase-9 gate and only the winner proceeds:
    #     Suppressed? main[1] -> Debounce Wait; the 3-node chain; Is Latest? main[1] -> Input type
    #     AND main[0] == [] (the aborted fragment dead-ends). Suppressed? main[0] stays a dead-end.
    assert_that(conns["Suppressed?"]["main"][1][0]["node"] == "Debounce Wait",
                f"{f}: Suppressed? main[1] does not route to Debounce Wait (debounce not after the gate)")
    assert_that(conns["Suppressed?"]["main"][0] == [],
                f"{f}: Suppressed? main[0] (suppressed) is no longer a dead-end")
    assert_that(conns["Debounce Wait"]["main"][0][0]["node"] == "Fetch Recent",
                f"{f}: Debounce Wait does not chain to Fetch Recent")
    assert_that(conns["Fetch Recent"]["main"][0][0]["node"] == "Latest+Combine",
                f"{f}: Fetch Recent does not chain to Latest+Combine")
    assert_that(conns["Latest+Combine"]["main"][0][0]["node"] == "Is Latest?",
                f"{f}: Latest+Combine does not chain to Is Latest?")
    assert_that(conns["Is Latest?"]["main"][1][0]["node"] == "Input type",
                f"{f}: Is Latest? main[1] (winner) does not proceed to Input type")
    assert_that(conns["Is Latest?"]["main"][0] == [],
                f"{f}: Is Latest? main[0] (aborted fragment) is not a dead-end")

    # (6) The Text set node injects combinedText with the single-message fallback.
    txt = node(ns, "Text")["parameters"]["assignments"]["assignments"][0]["value"]
    assert_that(txt == TEXT_VALUE, f"{f}: Text value wrong: {txt}")

    print(f"OK  {f}")
    return wf


def check_cross_template(wa, tg):
    # (7) Cross-template identity: the Latest+Combine jsCode is byte-identical (channel-agnostic)
    #     and Fetch Recent differs ONLY in the base (api/sync vs tapi/sync).
    js_wa = node(wa["nodes"], "Latest+Combine")["parameters"]["jsCode"]
    js_tg = node(tg["nodes"], "Latest+Combine")["parameters"]["jsCode"]
    assert_that(js_wa == js_tg, "Latest+Combine jsCode differs between templates (must be channel-agnostic)")

    fr_wa = copy.deepcopy(node(wa["nodes"], "Fetch Recent")["parameters"])
    fr_tg = copy.deepcopy(node(tg["nodes"], "Fetch Recent")["parameters"])
    assert_that(fr_wa.get("url") == "https://wappi.pro/api/sync/messages/get",
                f"WhatsApp Fetch Recent url wrong: {fr_wa.get('url')}")
    assert_that(fr_tg.get("url") == "https://wappi.pro/tapi/sync/messages/get",
                f"Telegram Fetch Recent url wrong: {fr_tg.get('url')}")
    fr_wa["url"] = fr_tg["url"] = ""   # blank the sole allowed difference, then require equality
    assert_that(fr_wa == fr_tg, "Fetch Recent differs between templates beyond the channel base url")

    print("OK  cross-template identity")


def main():
    global WF
    ap = argparse.ArgumentParser(
        description="Structural-assert verifier for the Phase 10 message-batching / debounce splice."
    )
    ap.add_argument(
        "--dir",
        default=DEFAULT_WF,
        help="workflow directory to verify (default: the committed workflows/ next to this "
             "script). Point at a prod re-export dir to run the batching asserts as a "
             "post-import go/no-go.",
    )
    args = ap.parse_args()
    WF = args.dir
    try:
        spliced = {f: check_bot(f, base) for f, base in BOTS}
        check_cross_template(spliced[WA_BOT], spliced[TG_BOT])
    except AssertionError as e:
        print(f"BATCHING FAIL: {e}")
        sys.exit(1)
    except (OSError, KeyError, IndexError, json.JSONDecodeError) as e:
        print(f"BATCHING FAIL: unexpected structural error: {e}")
        sys.exit(1)
    print("ALL BATCHING ASSERTS PASSED")


if __name__ == "__main__":
    main()
