#!/usr/bin/env python3
"""Structural-assert verifier for the Phase 4 Telegram-parity edits.

Proves the four canonical n8n workflow JSONs carry the Telegram-parity fixes and
that the RAG re-stamp nodes are injection-safe. Run before deploying to dev n8n
(the owner's 04-HUMAN-UAT step); no live n8n / network needed.

Usage:
  python3 Tools/n8n/verify-telegram-parity.py            # verify the committed workflows/
  python3 Tools/n8n/verify-telegram-parity.py --dir DIR  # verify a prod re-export (go/no-go)

--dir DIR overrides the workflow directory (default: the committed workflows/ next to this
script). Point it at a prod re-export so the SAME structural asserts gate a prod import —
catching a UI round-trip strip (dropped ai_embedding wiring, stripped top-level id, dropped
mark_all guard, etc.). Absent --dir, behavior is byte-identical to before.

Exits 0 and prints "ALL PARITY ASSERTS PASSED" when every assert holds.
Exits 1 with "PARITY FAIL: <reason>" naming the first violated assert.
"""
import argparse
import json
import os
import sys

# Resolve workflow paths from this script's own location so cwd does not matter.
# WF defaults to the committed workflows/ dir; main() reassigns it from --dir so the same
# asserts can gate a prod re-export. load() reads whatever WF points at.
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WF = os.path.join(HERE, "workflows")
WF = DEFAULT_WF

TG_BOT = "4VN3gsFaC2HUYmcc-Telegram_Bot.json"
CREATE_TG = "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json"
CREATE_WA = "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json"
SUGGEST = "9PTyYcelRQI7bGDb-Suggest_Replies.json"

# The executeQuery Postgres credential shared by Dashboard_Outcomes / Delete_File /
# Delete_Bot_Files. The re-stamp nodes MUST use this, NOT the memoryPostgresChat cred.
PG_EXECUTEQUERY_CRED = "vvRrFiEXzLVqKjOx"
PG_MEMORY_CRED = "1H5xlpFSESU4w6JH"

# Pre-edit node count of the Telegram bot template (order invariant guard).
TG_BOT_NODE_COUNT = 24


def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as fh:
        return json.load(fh)


def node(nodes, name):
    for n in nodes:
        if n.get("name") == name:
            return n
    raise AssertionError(f"node '{name}' not found")


def check_telegram_bot():
    f = TG_BOT
    wf = load(f)
    ns = wf["nodes"]

    # (vi) node count + order invariant: Set Fields patches nodes[0]/nodes[5] by index.
    assert len(ns) == TG_BOT_NODE_COUNT, \
        f"{f}: node count {len(ns)} != {TG_BOT_NODE_COUNT} (order/insertion invariant broken)"
    assert ns[0]["name"] == "Webhook", f"{f}: nodes[0] is '{ns[0]['name']}', expected 'Webhook'"
    assert ns[5]["name"] == "AI Agent", f"{f}: nodes[5] is '{ns[5]['name']}', expected 'AI Agent'"

    # (i) outbound HTTP nodes post to tapi bases, zero api/sync remains.
    url_send = node(ns, "HTTP Request")["parameters"]["url"]
    url_read = node(ns, "Mark Read")["parameters"]["url"]
    url_type = node(ns, "Typing")["parameters"]["url"]
    assert url_send == "https://wappi.pro/tapi/sync/message/send", f"{f}: send url wrong: {url_send}"
    assert url_read == "https://wappi.pro/tapi/sync/message/mark/read", f"{f}: mark-read url wrong: {url_read}"
    assert url_type == "https://wappi.pro/tapi/sync/chats/typing/start", f"{f}: typing url wrong: {url_type}"
    for u in (url_send, url_read, url_type):
        assert "/api/sync/" not in u, f"{f}: api/sync base still present in {u}"

    # (ii) Mark Read must not carry the undocumented mark_all query param.
    read_qp = node(ns, "Mark Read")["parameters"].get("queryParameters", {}).get("parameters", [])
    qp_names = {p.get("name") for p in read_qp}
    assert "mark_all" not in qp_names, f"{f}: Mark Read still has mark_all query param"
    assert "profile_id" in qp_names, f"{f}: Mark Read lost its profile_id query param"

    # (iii) both Input type Switch nodes route type:"text" (combinator 'or' with a text match).
    for sw_name in ("Input type", "Input type2"):
        sw = node(ns, sw_name)
        first_rule = sw["parameters"]["rules"]["values"][0]
        assert first_rule["outputKey"] == "Text", f"{f}: {sw_name} first rule is not the Text output"
        conds = first_rule["conditions"]
        rights = {c.get("rightValue") for c in conds["conditions"]}
        assert "text" in rights, f"{f}: {sw_name} Text rule does not match 'text'"
        assert "chat" in rights, f"{f}: {sw_name} Text rule dropped the 'chat' match"
        assert conds["combinator"] == "or", f"{f}: {sw_name} Text rule combinator is not 'or'"

    # (iv) Listening Pause resolves length_seconds fallback.
    pause = node(ns, "Listening Pause")["parameters"]["amount"]
    assert "length_seconds" in pause, f"{f}: Listening Pause missing length_seconds fallback"
    assert "media_info.duration + 2" not in pause, f"{f}: Listening Pause still uses the naive duration expr"

    # (v) Chat Memory sessionKey keys on chatId, not from.
    skey = node(ns, "Chat Memory")["parameters"]["sessionKey"]
    assert skey.rstrip().endswith("chatId }}"), f"{f}: sessionKey does not end with chatId: {skey}"
    assert ".from }}" not in skey, f"{f}: sessionKey still references .from: {skey}"

    # (vii) vector-store retrieval filter key unchanged (botTgId).
    sup = node(ns, "Supabase Vector Store")
    mv = sup["parameters"]["options"]["metadata"]["metadataValues"]
    assert len(mv) == 1 and mv[0]["name"] == "botTgId", f"{f}: retrieve filter key not single botTgId: {mv}"

    print(f"OK  {f}")


def check_restamp_orchestrator(f, jsonb_key, opposite_field):
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    # (i) Restamp RAG Chunks postgres node with the executeQuery credential.
    r = node(ns, "Restamp RAG Chunks")
    assert r["type"] == "n8n-nodes-base.postgres", f"{f}: Restamp RAG Chunks is not a postgres node"
    assert r["parameters"].get("operation") == "executeQuery", f"{f}: Restamp op is not executeQuery"
    cred = r["credentials"]["postgres"]["id"]
    assert cred == PG_EXECUTEQUERY_CRED, \
        f"{f}: Restamp cred {cred} != executeQuery cred {PG_EXECUTEQUERY_CRED}"
    assert cred != PG_MEMORY_CRED, \
        f"{f}: Restamp uses the memoryPostgresChat cred {PG_MEMORY_CRED} (wrong credential)"

    # (ii)+(iii) SQL is parameterized ($1/$2), targets the right jsonb key, no interpolation.
    q = r["parameters"]["query"]
    assert f"jsonb_set(metadata, '{jsonb_key}'" in q, f"{f}: jsonb_set target is not {jsonb_key}: {q}"
    assert "$1" in q and "$2" in q, f"{f}: SQL not parameterized with $1/$2: {q}"
    assert "{{" not in q, f"{f}: SQL string contains a '{{{{' interpolation (injection risk): {q}"

    # (ii-b) -1/'' sentinel guard: a '-1' or '' opposite-channel id must match zero rows,
    # otherwise a single-channel create claims shared fully-unauthed chunks from OTHER bots.
    assert "$2 <> '-1'" in q, f"{f}: Restamp SQL missing the -1 sentinel guard: {q}"
    assert "$2 <> ''" in q, f"{f}: Restamp SQL missing the empty-string sentinel guard: {q}"

    # (ii-c) queryReplacement binding: exactly two comma-separated segments where only the
    # LEADING '=' marks expression mode. A stray '=' after the comma is literal text and
    # corrupts $2 to '=<id>' (permanent 0-row no-op). Exact match also catches swapped or
    # wrong opposite-channel field names.
    qr = r["parameters"]["options"]["queryReplacement"]
    expected_qr = ("={{ $('Get Created Workflow Id').item.json.id }},"
                   "{{ $('Unity Webhook').first().json.body." + opposite_field + " }}")
    assert qr == expected_qr, \
        f"{f}: queryReplacement format wrong (stray '=' after comma or wrong bindings): {qr}"

    # robustness: a 0-row UPDATE or DB error must not break the response chain.
    assert r.get("alwaysOutputData") is True, f"{f}: Restamp alwaysOutputData not true"
    assert r.get("onError") == "continueRegularOutput", f"{f}: Restamp onError not continueRegularOutput"

    # (iv) wiring: Set Wappi Webhook Types -> Restamp RAG Chunks -> Send New Workflows Id (terminal).
    swwt = conns["Set Wappi Webhook Types"]["main"][0][0]["node"]
    assert swwt == "Restamp RAG Chunks", f"{f}: Set Wappi Webhook Types -> {swwt}, expected Restamp RAG Chunks"
    nxt = conns["Restamp RAG Chunks"]["main"][0][0]["node"]
    assert nxt == "Send New Workflows Id", f"{f}: Restamp RAG Chunks -> {nxt}, expected Send New Workflows Id"
    assert "Send New Workflows Id" not in conns, \
        f"{f}: Send New Workflows Id has an outgoing connection (must stay the terminal/response node)"

    # (v) Unity Webhook responseMode still lastNode.
    uw = node(ns, "Unity Webhook")
    assert uw["parameters"].get("responseMode") == "lastNode", f"{f}: Unity Webhook responseMode changed"

    print(f"OK  {f}")


def check_suggest_replies():
    f = SUGGEST
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    def filter_keys(name):
        n = node(ns, name)
        mv = n["parameters"]["options"]["metadata"]["metadataValues"]
        return [m["name"] for m in mv]

    # (i)+(ii) single-key filters, one per channel, never ORed in one node.
    tg_keys = filter_keys("Retrieve RAG TG")
    wa_keys = filter_keys("Retrieve RAG")
    assert tg_keys == ["botTgId"], f"{f}: Retrieve RAG TG filter not single botTgId: {tg_keys}"
    assert wa_keys == ["botWaId"], f"{f}: Retrieve RAG filter not single botWaId: {wa_keys}"

    # (ii-b) the new vector store MUST have its embeddings input (a vector-store node
    # without ai_embedding hard-fails at runtime; the n8n UI round-trip can drop it).
    emb_targets = {c["node"] for c in conns["Embeddings"]["ai_embedding"][0]}
    assert {"Retrieve RAG", "Retrieve RAG TG"} <= emb_targets, \
        f"{f}: Embeddings ai_embedding targets missing a Retrieve node: {emb_targets}"

    # (ii-c) both retrieve nodes feed Assemble (a dropped main connection dead-ends the path).
    for retr in ("Retrieve RAG TG", "Retrieve RAG"):
        nxt = conns[retr]["main"][0][0]["node"]
        assert nxt == "Assemble", f"{f}: {retr} -> {nxt}, expected Assemble"

    # (iii) channel branch on the RAG path.
    ictg = node(ns, "If channel TG?")
    assert ictg["type"] == "n8n-nodes-base.if", f"{f}: If channel TG? is not an If node"
    assert conns["If skipRag?"]["main"][1][0]["node"] == "If channel TG?", \
        f"{f}: If skipRag? false-branch does not route to If channel TG?"
    tg_true = conns["If channel TG?"]["main"][0][0]["node"]
    tg_false = conns["If channel TG?"]["main"][1][0]["node"]
    assert tg_true == "Retrieve RAG TG", f"{f}: If channel TG? true-branch -> {tg_true}, expected Retrieve RAG TG"
    assert tg_false == "Retrieve RAG", f"{f}: If channel TG? false-branch -> {tg_false}, expected Retrieve RAG"

    # (iv) Prep jsCode references channel + botTgId.
    prep = node(ns, "Prep")["parameters"]["jsCode"]
    assert "channel" in prep, f"{f}: Prep jsCode does not reference channel"
    assert "botTgId" in prep, f"{f}: Prep jsCode does not reference botTgId"

    # (v) Assemble copy no longer WhatsApp-specific.
    assemble = node(ns, "Assemble")["parameters"]["jsCode"]
    assert "со своего WhatsApp" not in assemble, f"{f}: Assemble still says «со своего WhatsApp»"

    print(f"OK  {f}")


def main():
    global WF
    ap = argparse.ArgumentParser(
        description="Structural-assert verifier for the Telegram-parity workflow edits."
    )
    ap.add_argument(
        "--dir",
        default=DEFAULT_WF,
        help="workflow directory to verify (default: the committed workflows/ next to this "
             "script). Point at a prod re-export dir to run the parity asserts as a "
             "post-import go/no-go.",
    )
    args = ap.parse_args()
    WF = args.dir
    try:
        check_telegram_bot()
        check_restamp_orchestrator(CREATE_TG, "{botTgId}", "WhatsappWorkflowId")
        check_restamp_orchestrator(CREATE_WA, "{botWaId}", "TelegramWorkflowId")
        check_suggest_replies()
    except AssertionError as e:
        print(f"PARITY FAIL: {e}")
        sys.exit(1)
    except (OSError, KeyError, IndexError, json.JSONDecodeError) as e:
        print(f"PARITY FAIL: unexpected structural error: {e}")
        sys.exit(1)
    print("ALL PARITY ASSERTS PASSED")


if __name__ == "__main__":
    main()
