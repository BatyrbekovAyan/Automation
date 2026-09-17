#!/usr/bin/env python3
"""Stage-4 (A5/A6) semantic verifier: the ONE history's writers and their sources, the memory rows'
LangChain shape + provenance marks, no double write, the handoff pause in the gate, the tool wiring
and its context fields, the notifier's privacy, the app webhook's owner scoping, and the integrations
(Create webhook types, Dashboard event override, Delete Bot Files wipe, Set Workflow State protection,
proxy recorder after the response).

    python3 Tools/n8n/verify-history-handoff.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
BOTS = ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json")


def fail(msg):
    print("HISTORY/HANDOFF FAIL:", msg)
    sys.exit(1)


def check(cond, msg):
    if not cond:
        fail(msg)


def load(fname):
    return json.load(open(os.path.join(WF, fname), encoding="utf-8"))


def node(wf, name):
    n = next((n for n in wf["nodes"] if n["name"] == name), None)
    check(n is not None, f"missing node {name!r}")
    return n


MEM_MARK = "'additional_kwargs', jsonb_build_object('source'"
NO_DOUBLE = "x.message->'additional_kwargs'->>'message_id' = $3"


def verify_bot(f):
    wf = load(f)
    q = node(wf, "Claim Event")["parameters"]["query"]
    check("insert into chat_history_events" in q and "'client'" in q and "do nothing" in q, f"{f}: Claim Event must record the client row (idempotent)")
    check("n8n_chat_histories (session_id, message)" not in q, f"{f}: Claim Event must NOT write memory (the memory node does on a normal run)")
    q = node(wf, "Record Suppressed")["parameters"]["query"]
    for frag in ("'client'", MEM_MARK, "'human'", NO_DOUBLE, "where exists (select 1 from ev)"):
        check(frag in q, f"{f}: Record Suppressed lacks {frag!r}")
    q = node(wf, "Record Outgoing")["parameters"]["query"]
    for frag in ("'owner_phone'", "'outgoing_api'", "do nothing", "where exists (select 1 from ev where source = 'owner_phone')", NO_DOUBLE, "$2 not like '%@g.us'"):
        check(frag in q, f"{f}: Record Outgoing lacks {frag!r}")
    q = node(wf, "Record Send")["parameters"]["query"]
    check("'ai'" in q and "do update set source = 'ai'" in q and "$4 = 'sent'" in q, f"{f}: Record Send must record the sent reply as an ai row (upgrading a webhook-first row)")
    q = node(wf, "Record Cancel")["parameters"]["query"]
    check("(message->'additional_kwargs'->>'source') is null" in q, f"{f}: Record Cancel must never drop owner rows")
    check("$8::boolean and nullif($9, '') is not null" in q and NO_DOUBLE in q, f"{f}: Record Cancel must record the human row when the agent never ran, without doubles")
    js = node(wf, "Cancel Label")["parameters"]["jsCode"]
    check("recordHuman = prev === 'If Quota Allows' || prev === 'Generated?'" in js, f"{f}: Cancel Label must flag the human row only for no-agent paths")
    q = node(wf, "Read Reply Mode")["parameters"]["query"]
    check("or coalesce((select handoff_paused from chat_reply_state" in q and "coalesce(\n    (select suppressed from reply_mode_flags" in q,
          f"{f}: Read Reply Mode must OR the handoff pause while keeping absence → reply")
    # routing + binding
    for name, frag in (("Is Incoming?", "wh_type === 'incoming_message'"), ("Is Outgoing?", "'outgoing_message_phone', 'outgoing_message_api'"),
                       ("Outgoing Bound?", "split('/').pop() === String($json.body.messages[0].profile_id)")):
        check(frag in node(wf, name)["parameters"]["conditions"]["conditions"][0]["leftValue"], f"{f}: {name} condition lacks {frag!r}")
    # the tool: context from the run, content from the model, target = the canonical sub-workflow
    t = node(wf, "Create Handoff")
    check(t["type"] == "@n8n/n8n-nodes-langchain.toolWorkflow" and t["parameters"]["workflowId"]["value"] == "crHandoffReq0001", f"{f}: Create Handoff target")
    v = t["parameters"]["workflowInputs"]["value"]
    for k in ("profile_id", "chat_id", "source_message_id"):
        check("$('Webhook').first().json.body.messages[0]" in v[k] and "$fromAI" not in v[k], f"{f}: tool input {k} must come from the run, never the model")
    for k in ("reason", "name", "phone", "summary"):
        check("$fromAI(" in v[k], f"{f}: tool input {k} must be filled by the model")
    check(wf["connections"]["Create Handoff"]["ai_tool"] == [[{"node": "AI Agent", "type": "ai_tool", "index": 0}]], f"{f}: tool not wired")
    sm = node(wf, "AI Agent")["parameters"]["options"]["systemMessage"]
    check("##create_handoff##" in sm and "ok:true" in sm, f"{f}: system prompt lacks the tool rule")
    print("OK ", f)


def verify_handoff_workflows():
    req = load("crHandoffReq0001-Handoff_Request.json")
    q = node(req, "Upsert Handoff")["parameters"]["query"]
    for frag in ("on conflict (profile_id, chat_id, source_message_id) do update", "(xmax = 0) as created", "handoff_paused = true"):
        check(frag in q, f"Handoff Request lacks {frag!r}")
    js = node(req, "Tool Result")["parameters"]["jsCode"]
    check("ok: false, error: 'db_error'" in js and "ok: true" in js, "Tool Result must distinguish a written row from a failure")
    check(node(req, "When Executed by Another Workflow")["parameters"]["inputSource"] == "workflowInputs", "Handoff Request trigger inputs")
    ntf = load("crHandoffNtfy001-Handoff_Notifier.json")
    q = node(ntf, "Due Notifications")["parameters"]["query"]
    for frag in ("for update skip locked", "notify_attempts = h.notify_attempts + 1", "notify_attempts < 6", "notify_next_at = now() + interval '10 minutes'", "status <> 'done'"):
        check(frag in q, f"Handoff Notifier lacks {frag!r}")
    for name in ("Compose Notice", "Settle Notice"):
        check(node(ntf, name)["parameters"].get("mode") == "runOnceForEachItem", f"{name} must run per item (one notice per due request)")
    js = node(ntf, "Compose Notice")["parameters"]["jsCode"]
    check("row.summary" not in js and "row.data" not in js and "row.body" not in js, "the owner notice must not carry client text")
    check("const text = 'Choose Reply: ' + what + ' (чат …' + tail + ')." in js and "slice(-4)" in js,
          "the owner notice is built ONLY from the reason word and a masked chat tail")
    q = node(ntf, "Record Notify")["parameters"]["query"]
    check("notify_state = $2" in q and "handoff_requests" in q and "delete" not in q.lower(), "Record Notify must only update the notify state")
    api = load("crHandoffsApi001-Handoffs.json")
    check("crAuth" in node(api, "Auth Gate")["parameters"]["jsCode"] and '"profiles": ["profileIds"]' in node(api, "Auth Gate")["parameters"]["jsCode"] and '"legacy": false' in node(api, "Auth Gate")["parameters"]["jsCode"],
          "Handoffs webhook must be token-only and profile-scoped")
    js = node(api, "Validate")["parameters"]["jsCode"]
    check("owned.has(x)" in js, "Handoffs Validate must re-intersect with the token's profiles")
    q = node(api, "Handoffs Query")["parameters"]["query"]
    for frag in ("h.profile_id in (select profile_id from p)", "handoff_paused = false", "o.status <> 'done' and o.id <> d.id"):
        check(frag in q, f"Handoffs Query lacks {frag!r}")
    print("OK  handoff workflows (request / notifier / app webhook)")


def verify_integrations():
    for f in ("XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json"):
        jb = node(load(f), "Set Wappi Webhook Types")["parameters"]["jsonBody"]
        check(all(t in jb for t in ("incoming_message", "outgoing_message_phone", "outgoing_message_api")) and "delivery_status" not in jb, f"{f}: webhook types")
    d = load("2htWSV5IHO8E2CgB-Dashboard_Outcomes.json")
    check("'Владелец: '" in node(d, "Find Changed Sessions")["parameters"]["query"], "classifier transcript must label owner rows")
    q = node(d, "Fetch Outcomes")["parameters"]["query"]
    for frag in ("FULL OUTER JOIN h", "'event' ELSE 'classifier' END AS source", "h.reason = 'order' THEN 'order_collected' ELSE 'owner_needed'", "r.status <> 'done'"):
        check(frag in q, f"Fetch Outcomes lacks {frag!r}")
    q = node(load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json"), "Delete Chat Memory")["parameters"]["query"]
    for t in ("chat_history_events", "handoff_requests", "reply_events", "chat_reply_state"):
        check(f"DELETE FROM public.{t}" in q, f"Delete Bot Files must wipe {t}")
    rb = node(load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json"), "Respond")["parameters"]["responseBody"]
    for k in ("deletedHistoryEvents", "deletedHandoffs", "deletedReplyEvents", "deletedChatStates"):
        check(k in rb, f"Delete Bot Files response must report {k}")
    st = load("0DP79STI3pafXO9B-Set_Workflow_State.json")
    check(any(all(i in n["parameters"].get("jsCode", "") for i in ("'crHandoffReq0001'", "'crHandoffNtfy001'", "'crHandoffsApi001'")) for n in st["nodes"]),
          "Set Workflow State must protect the handoff workflow ids")
    px = load("crWappiProxy0001-Wappi_Proxy.json")
    check(px["connections"]["Respond"]["main"][0] == [{"node": "Owner Send?", "type": "main", "index": 0}], "proxy: the recorder must run AFTER the response")
    q = node(px, "Record Owner Send")["parameters"]["query"]
    check("'owner_app'" in q and MEM_MARK in q and NO_DOUBLE in q, "proxy: Record Owner Send must write an owner_app row + a marked memory row, no doubles")
    print("OK  integrations (Create types / Dashboard / Delete / Set State / proxy)")


def main():
    for f in BOTS:
        verify_bot(f)
    verify_handoff_workflows()
    verify_integrations()
    print("ALL HISTORY/HANDOFF ASSERTS PASSED")


if __name__ == "__main__":
    main()
