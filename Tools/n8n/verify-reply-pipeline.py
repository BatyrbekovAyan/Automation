#!/usr/bin/env python3
"""Stage-3 (A3/A4/A7) semantic verifier for BOTH bot templates — what verify-message-batching.py's
edge asserts do not cover: the SQL contracts of the four ledger statements, the Code-node rules of
the final check / settle / reconcile / cancel label, the Outcome stamp reading the recorded state,
and byte-identity of every new node across WhatsApp and Telegram (only the transport base differs).

    python3 Tools/n8n/verify-reply-pipeline.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
BOTS = ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json")
NEW = ("Claim Event", "Claimed?", "Generated?", "Final Fetch", "Final Check", "Final OK?", "Acquire Send Lease",
       "Send Allowed?", "Settle Send", "Send Unknown?", "Wait Reconcile", "Reconcile Fetch", "Reconcile Check",
       "Record Send", "Cancel Label", "Record Cancel", "Peer Recheck?", "Wait Peer Recheck", "Recheck Fetch", "Stamp Draft", "Owner Row?")
STATES = ("claimed", "generated", "send_pending", "sent", "send_failed", "send_unknown", "superseded", "cancelled_owner",
          "cancelled_mode", "cancelled_busy", "cancelled_fetch", "generation_failed", "quota_denied", "suppressed")


def fail(msg):
    print("REPLY PIPELINE FAIL:", msg)
    sys.exit(1)


def check(cond, msg):
    if not cond:
        fail(msg)


def node(wf, name):
    n = next((n for n in wf["nodes"] if n["name"] == name), None)
    check(n is not None, f"missing node {name!r}")
    return n


def verify(fname):
    wf = json.load(open(os.path.join(WF, fname), encoding="utf-8"))
    f = fname
    # (1) Claim: atomic upsert, refuses a live duplicate, re-claims only a stale claim; the generation counts
    #     NEWER MESSAGES (R6): it bumps only on a strictly newer Wappi time ($8) or an unknown one.
    q = node(wf, "Claim Event")["parameters"]["query"]
    for frag in ("insert into reply_events", "on conflict (profile_id, chat_id, message_id) do update",
                 "reply_events.state in ('claimed', 'generated')", "interval '10 minutes'",
                 "nullif($8::bigint, 0)", "excluded.newest_message_time > chat_reply_state.newest_message_time",
                 "excluded.newest_message_time is null or chat_reply_state.newest_message_time is null",
                 "newest_message_time = greatest(chat_reply_state.newest_message_time, excluded.newest_message_time)",
                 "as history_watermark"):
        check(frag in q, f"{f}: Claim Event lacks {frag!r}")
    # negative (R6): an unconditional bump counts claim ARRIVALS — a late older message then cancels the only reply
    check("incoming_generation = chat_reply_state.incoming_generation + 1" not in q,
          f"{f}: Claim Event bumps the generation on every claim (R6: a late older message refuses the newer reply)")
    params = node(wf, "Claim Event")["parameters"]["options"]["queryReplacement"]
    check("$execution.id" in params and ".profile_id" in params and ".from" in params and ".id" in params,
          f"{f}: Claim Event params must be (profile, chat=from, message id, execution id): {params}")
    check(params.rstrip().endswith("Number(m.time) || 0 ])($('Webhook').first().json.body.messages[0]) }}"),
          f"{f}: Claim Event $8 must be the message's own time (0 = unknown), never the server clock: {params}")
    # (2) Lease: mode re-read + generation match + lease + event state in ONE statement; lock scoped per chat.
    q = node(wf, "Acquire Send Lease")["parameters"]["query"]
    for frag in ("from reply_mode_flags", "chat_id in ($2, '*')", "order by (chat_id = '*')", "c.incoming_generation = $4::bigint",
                 "c.lock_token is null or c.lock_expires_at < now() or c.lock_token = $5", "interval '90 seconds'",
                 "not (select suppressed from mode)", "state = 'send_pending'", "as allowed", "as lock_busy",
                 # V5: a claimed SAME-second client message missing from the decided-on snapshot refuses the lease (only
                 # on a complete snapshot, only while its run may still answer) and asks for ONE re-read
                 "from reply_events r", "h.source = 'client'", "h.sent_at = to_timestamp($7::bigint)", "$7::bigint > 0",
                 "jsonb_array_elements_text($6::jsonb)", "r.message_id <> $3", "$9::boolean",
                 "r.state in ('claimed', 'generated', 'send_pending', 'sent', 'send_unknown')",
                 "and not (select unseen from peer)", "as peer_unseen", "not $8::boolean", "as recheck"):
        check(frag in q, f"{f}: Acquire Send Lease lacks {frag!r}")
    # negative (V5): the same second, never a range — a strictly newer message is the generation's job, and a '>='
    # against a server clock (a webhook without a time) would refuse on unrelated rows
    check("h.sent_at >=" not in q and "h.sent_at <=" not in q, f"{f}: Acquire Send Lease must match the trigger's own second only")
    lp = node(wf, "Acquire Send Lease")["parameters"]["options"]["queryReplacement"]
    check(lp.rstrip().endswith("JSON.stringify($json.seenIds || []), Number($('Webhook').first().json.body.messages[0].time) || 0, "
                               "$json.recheck === true, $json.seenComplete === true ] }}"),
          f"{f}: Acquire Send Lease $6..$9 must be (seen ids, the trigger's own time or 0, second pass, complete) — never undefined: {lp}")
    # V5 coupling: the peer rule reads the client row Claim Event writes in the same statement, timed by the
    # message's own Wappi time — if Claim stops writing it that way the rule silently switches off
    cq = node(wf, "Claim Event")["parameters"]["query"]
    check("values ($1, $2, $3, 'client', $5, $6, to_timestamp($7::bigint), $4)" in cq,
          f"{f}: Claim Event must write the client history row with sent_at = to_timestamp(the message time) (V5 peer rule)")
    check("excluded.newest_message_time >= chat_reply_state.newest_message_time" not in cq,
          f"{f}: Claim Event must keep the STRICT newer rule (V5/R6: '>=' loses both replies at an equal second)")
    pr = node(wf, "Peer Recheck?")
    check(pr["type"] == "n8n-nodes-base.if" and pr["parameters"]["conditions"]["conditions"][0]["leftValue"] == "={{ $json.recheck === true }}",
          f"{f}: Peer Recheck? must branch on the lease's recheck flag")
    cn = wf["connections"]
    E = lambda t: {"node": t, "type": "main", "index": 0}
    check(cn["Acquire Send Lease"]["main"] == [[E("Peer Recheck?")]], f"{f}: Acquire Send Lease must lead ONLY to Peer Recheck?")
    check(cn["Peer Recheck?"]["main"] == [[E("Wait Peer Recheck")], [E("Send Allowed?")]], f"{f}: Peer Recheck? branches wrong")
    check(cn["Wait Peer Recheck"]["main"] == [[E("Recheck Fetch")]] and cn["Recheck Fetch"]["main"] == [[E("Final Check")]],
          f"{f}: the re-read must go Wait Peer Recheck → Recheck Fetch → Final Check")
    wpr, dw = node(wf, "Wait Peer Recheck"), node(wf, "Debounce Wait")
    check(wpr["type"] == "n8n-nodes-base.wait" and wpr.get("webhookId")
          and isinstance(wpr["parameters"].get("amount"), int) and wpr["parameters"]["amount"] >= dw["parameters"]["amount"] + 4,
          f"{f}: Wait Peer Recheck must outlast the peer's debounce + fetch + one PG node (≥ Debounce Wait + 4 s) and carry a webhookId")
    rf = node(wf, "Recheck Fetch")
    qp = {p["name"]: p["value"] for p in rf["parameters"]["queryParameters"]["parameters"]}
    check(rf["parameters"]["url"].endswith("messages/get") and ".chatId }}" in qp["chat_id"] and ".profile_id }}" in qp["profile_id"],
          f"{f}: Recheck Fetch must re-read the trigger's chat")
    check(rf["parameters"]["options"].get("timeout", 0) <= 10000 and not rf.get("retryOnFail") and rf.get("onError") == "continueRegularOutput",
          f"{f}: Recheck Fetch must be short (≤ 10 s) without retries and fail into the Final Check (cancelled_fetch)")
    # (3) Record Send: final state + lease release only by the holder; unknown never retried here.
    q = node(wf, "Record Send")["parameters"]["query"]
    for frag in ("set state = $4", "transport_message_id = nullif($5, '')", "lock_token = $7", "$4::text as outcome",
                 # R7/V6: send_failed drops ITS OWN memory row only — the row Stamp Draft tied to this execution
                 "where (message->'additional_kwargs'->>'execution_id') = $7\n    and $4 = 'send_failed'", "id > $9::bigint",
                 "(message->'additional_kwargs'->>'source') is null",
                 # V6: sent / send_unknown keep the row and mark it — delivery sent (+ transport id) or unknown
                 "and $4 <> 'send_failed'", "case when $4 = 'sent' then 'sent' else 'unknown' end", "'transport_message_id', nullif($5, '')",
                 "jsonb_typeof(message->'additional_kwargs') = 'object'", "as marked_ai_rows"):
        check(frag in q, f"{f}: Record Send lacks {frag!r}")
    check("message/send" not in q, f"{f}: Record Send must not touch the transport")
    check("'send_unknown'" not in q, f"{f}: Record Send must keep a send_unknown reply in memory (iteration decision R7)")
    # negative (V6): a row chosen by its TEXT may be another run's identical reply — never select memory rows by content
    check("min(id)" not in q and "message->>'content' = $" not in q,
          f"{f}: Record Send selects a memory row by text/min(id) (V6: another run's identical sent reply is deleted or confirmed)")
    check(node(wf, "Record Send")["parameters"]["options"]["queryReplacement"].rstrip().endswith(
        "$('Final Check').first().json.text, $('Final Check').first().json.historyWatermark ] }}"),
        f"{f}: Record Send $8/$9 must be the Final Check text and watermark")
    # (4) Record Cancel: state + drop the AI memory rows a cancelled generation wrote + release the lease.
    q = node(wf, "Record Cancel")["parameters"]["query"]
    for frag in ("delete from n8n_chat_histories", "message->>'type' = 'ai'", "id > $5::bigint", "$6::boolean", "lock_token = $7",
                 # V6: only the row Stamp Draft tied to THIS execution
                 "where (message->'additional_kwargs'->>'execution_id') = $7\n    and $6::boolean"):
        check(frag in q, f"{f}: Record Cancel lacks {frag!r}")
    # negative (R7): a delete of EVERY unmarked ai row above the watermark also removes a newer run's reply
    check("delete from n8n_chat_histories\n  where $6::boolean" not in q,
          f"{f}: Record Cancel deletes every ai row above the watermark (R7: a newer run's sent reply is lost from memory)")
    # negative (V6): by text (or min(id) over text) it deletes another run's identical — possibly SENT — reply
    check("min(id)" not in q and "message->>'content' = $" not in q,
          f"{f}: Record Cancel selects the memory row by text/min(id) (V6: a newer run's same-text sent reply is deleted)")
    # V6: Stamp Draft ties the agent's memory row to this execution right after the agent, never a marked/stamped row
    sd = node(wf, "Stamp Draft")
    q = sd["parameters"]["query"]
    for frag in ("update n8n_chat_histories", "'execution_id', $4::text", "'event_id', $3::text", "'delivery', 'draft'",
                 "jsonb_typeof(message->'additional_kwargs') = 'object'", "id > $5::bigint", "message->>'type' = 'ai'",
                 "(message->'additional_kwargs'->>'source') is null", "(message->'additional_kwargs'->>'execution_id') is null",
                 "message->>'content' = $6", "as draft_row_id"):
        check(frag in q, f"{f}: Stamp Draft lacks {frag!r}")
    check("delete" not in q.lower(), f"{f}: Stamp Draft must only mark, never delete")
    # V6-concurrent: the candidate is LOCKED as it is chosen (a row another run's stamp holds is skipped, never waited
    # for and overwritten), and the UPDATE re-checks on the row it writes that nobody stamped it meanwhile
    check("order by id\n              limit 1\n              for update skip locked)" in q,
          f"{f}: Stamp Draft must pick its row with order by id limit 1 for update skip locked (V6-concurrent)")
    head, _, outer = q.partition("for update skip locked)")
    check(all(frag in outer for frag in ("(message->'additional_kwargs'->>'execution_id') is null", "message->>'content' = $6",
                                         "(message->'additional_kwargs'->>'source') is null", "id > $5::bigint",
                                         "session_id = $1 || ':' || $2", "message->>'type' = 'ai'"))
          and outer.index("execution_id') is null") < outer.index("returning id"),
          f"{f}: Stamp Draft's UPDATE must re-check every condition (not stamped, own text, above the watermark) on the row it writes")
    # negative (V6-concurrent): an aggregate pick (min/max over the unstamped rows) cannot be locked — two runs choose the
    # same row, the second waits on the first's lock and then overwrites its execution_id (review 2026-10-11)
    check(not re.search(r"\b(min|max)\s*\(\s*id\s*\)", q),
          f"{f}: Stamp Draft picks its row with min/max(id) (V6-concurrent: the same row is stamped twice)")
    sp = sd["parameters"]["options"]["queryReplacement"]
    check("$execution.id" in sp and "$('Claim Event').first().json.history_watermark" in sp
          and sp.rstrip().endswith("String($('AI Agent').first().json.output || '') ])($('Webhook').first().json.body.messages[0]) }}"),
          f"{f}: Stamp Draft params must be (profile, chat, event id, execution id, the claim's watermark, the agent's output): {sp}")
    check(sd.get("onError") == "continueRegularOutput" and sd.get("alwaysOutputData") is True,
          f"{f}: Stamp Draft must never stop the reply (a failed stamp only means nothing is deleted later)")
    E = lambda t: {"node": t, "type": "main", "index": 0}
    check(wf["connections"]["Generated?"]["main"] == [[E("Stamp Draft")], [E("Cancel Label")]]
          and wf["connections"]["Stamp Draft"]["main"] == [[E("Pause Before Reading")]],
          f"{f}: Stamp Draft must sit right after the agent: Generated?[yes] → Stamp Draft → Pause Before Reading")
    check(node(wf, "Record Cancel")["parameters"]["options"]["queryReplacement"].rstrip().endswith("$json.humanText, $json.aiText ] }}"),
          f"{f}: Record Cancel $10 must be the Cancel Label's aiText")
    # (5) Final Check rules (newer client → superseded, owner → cancelled_owner, trigger missing → cancelled_fetch).
    js = node(wf, "Final Check")["parameters"]["jsCode"]
    for frag in ("outcome = 'superseded'", "outcome = 'cancelled_owner'", "outcome = 'cancelled_fetch'", "outcome = 'generation_failed'",
                 "fromMe === false", "fromMe === true", "$('Ask to Send Text')", "$('Retrieve Answer')", "(a.__i || 0) - (b.__i || 0)",
                 # V5: what the snapshot saw, whether it was complete for the trigger's second, and the second pass
                 "const seenIds = fetched.", "fetchedAll.length < 15", "Number(m.time || 0) < Number(trig.time || 0)",
                 "$('Acquire Send Lease').first().json.peer_unseen === true", "seenIds, seenComplete, recheck"):
        check(frag in js, f"{f}: Final Check lacks {frag!r}")
    check("message/send" not in js, f"{f}: Final Check must not send")
    # (6) Settle: three states, no assumption of delivery on a missing answer.
    js = node(wf, "Settle Send")["parameters"]["jsCode"]
    for frag in ("state = 'sent'", "state = 'send_failed'", "state = 'send_unknown'", "body.status === 'done' || body.message_id"):
        check(frag in js, f"{f}: Settle Send lacks {frag!r}")
    # (7) Reconcile: adopts an echo of OUR text after the send started; never resends. R10: foreign chatId rows
    #     dropped, the trigger must be in the window, the echo newer than it, exactly one distinct candidate.
    js = node(wf, "Reconcile Check")["parameters"]["jsCode"]
    for frag in ("fromMe === true", "m.body.trim() === String(s.text).trim()", "sendStartedAt", "'send_unknown'",
                 "m.chatId && trig.chatId && m.chatId !== trig.chatId", "fetched.findIndex(m => m.id === trig.id)",
                 "trigIdx < 0 ? [] : fetched.slice(0, trigIdx)", "const one = cands.length === 1 ? cands[0] : null;", "m.from_where !== 'phone'",
                 # V7: positive attribution — the window's profile (uuid), the row's profile, the webhook phone type, the
                 # transport's API mark, and the one history's verdict on the candidate (Owner Row? → $json.sources)
                 "$('Reconcile Fetch').first().json", "norm(fetch.uuid)", "scope !== pid", "norm(m.profile_id) !== pid",
                 "m.wh_type !== 'outgoing_message_phone'", "m.from_where === 'api'", "m.wh_type === 'outgoing_message_api'",
                 "typeof m.task_id === 'string' && m.task_id.trim() !== ''", "$json.sources",
                 "const hit = one && (known === 'outgoing_api' || (known === '' && viaApi(one))) ? one : null;"):
        check(frag in js, f"{f}: Reconcile Check lacks {frag!r}")
    # negative (R10): the first same-text outgoing row anywhere in the response is not proof of OUR delivery
    check("rows.find(m => m.body.trim() === String(s.text).trim()" not in js,
          f"{f}: Reconcile Check adopts the first same-text row of any chat (R10: a crossed row becomes a false «sent»)")
    # negative (V7): «exactly one non-phone candidate» alone is the old NEGATIVE filter (an owner's manual/app row, a row of
    # another profile became a false «sent»), and isFromAPI is set on tapi phone rows too — never an attribution
    check("const hit = cands.length === 1 ? cands[0] : null" not in js, f"{f}: Reconcile Check adopts a single candidate without attribution (V7)")
    check("m.isFromAPI" not in js and "isFromAPI ===" not in js, f"{f}: Reconcile Check must not read isFromAPI as an API mark (V7)")
    orow = node(wf, "Owner Row?")
    q = orow["parameters"]["query"]
    for frag in ("from chat_history_events h", "h.profile_id = $1 and h.chat_id = $2", "jsonb_array_elements_text($3::jsonb)",
                 "jsonb_object_agg(h.message_id, h.source)", "as sources"):
        check(frag in q, f"{f}: Owner Row? lacks {frag!r}")
    check(not any(w in q.lower() for w in ("insert ", "update ", "delete ")), f"{f}: Owner Row? must be read-only")
    check(orow.get("onError") == "continueRegularOutput" and orow.get("alwaysOutputData") is True,
          f"{f}: Owner Row? must not stop the reconcile (a failed read = no facts, the Code node decides)")
    op = orow["parameters"]["options"]["queryReplacement"]
    check(".profile_id, " in op and ".from, JSON.stringify(" in op and "$json.messages" in op,
          f"{f}: Owner Row? params must be (profile, chat, the ids of the Reconcile Fetch window): {op}")
    cn = wf["connections"]
    E = lambda t: {"node": t, "type": "main", "index": 0}
    check(cn["Reconcile Fetch"]["main"] == [[E("Owner Row?")]] and cn["Owner Row?"]["main"] == [[E("Reconcile Check")]]
          and cn["Reconcile Check"]["main"] == [[E("Record Send")]],
          f"{f}: the reconcile branch must be Reconcile Fetch → Owner Row? → Reconcile Check → Record Send")
    check("message/send" not in js and "$http" not in js, f"{f}: Reconcile Check must never resend")
    wait = node(wf, "Wait Reconcile")
    check(wait["type"] == "n8n-nodes-base.wait" and wait["parameters"].get("amount") == 3 and wait.get("webhookId"),
          f"{f}: Wait Reconcile must be a 3-s wait with a webhookId")
    # (8) Cancel Label names every refusal; only a generated run drops AI memory.
    js = node(wf, "Cancel Label")["parameters"]["jsCode"]
    for frag in ("prev === 'Is Latest?'", "prev === 'If Quota Allows'", "prev === 'Generated?'", "prev === 'Final OK?'", "prev === 'Send Allowed?'",
                 "'cancelled_mode'", "'cancelled_busy'", "dropAiHistory: generated && aiText !== null",
                 "$('AI Agent').first().json.output", "aiText: aiText || ''"):
        check(frag in js, f"{f}: Cancel Label lacks {frag!r}")
    # (9) Outcome reads the recorded state first; every ledger state has a label.
    js = node(wf, "Outcome")["parameters"]["jsCode"]
    check("typeof resp.outcome === 'string' && resp.outcome" in js, f"{f}: Outcome must read the recorded state from the incoming row")
    check("'Claimed?': 'duplicate'" in js, f"{f}: Outcome lacks the duplicate label")
    # (10) Transport call: body from the Final Check, never throws, full response, bounded timeout.
    send = node(wf, "HTTP Request")
    check(send["parameters"]["options"]["timeout"] == 30000 and send.get("alwaysOutputData") is True, f"{f}: send timeout/alwaysOutputData")
    # (11) Ledger states in SQL and in the state check list agree.
    sql = open(os.path.join(HERE, "sql", "2026-09-17-reply-events.sql"), encoding="utf-8").read()
    for st in STATES:
        check(f"'{st}'" in sql, f"reply-events SQL lacks state {st!r}")
    used = {"claimed", "send_pending", "sent", "send_failed", "send_unknown", "superseded", "cancelled_owner", "cancelled_mode",
            "cancelled_busy", "cancelled_fetch", "generation_failed", "quota_denied"}
    check(used <= set(STATES), "workflow states outside the SQL check list")
    # (12) Fetches are bounded and retried; they go to the same chat as the trigger.
    for name in ("Final Fetch", "Reconcile Fetch"):
        n = node(wf, name)
        check(n["parameters"]["url"].endswith("messages/get") and n.get("retryOnFail") is True and n.get("maxTries") == 3,
              f"{f}: {name} must be a retried messages/get")
        qp = {p["name"]: p["value"] for p in n["parameters"]["queryParameters"]["parameters"]}
        check(".chatId }}" in qp["chat_id"] and ".profile_id }}" in qp["profile_id"], f"{f}: {name} must fetch the trigger's chat")
    # (13) pg-promise substitutes $N inside SQL comments too: a multi-line value inlined into a `--` line escapes the
    #      comment and kills the statement (R7 part 2) — no placeholder may appear in a comment of any Postgres node.
    for n in wf["nodes"]:
        q = n.get("parameters", {}).get("query")
        if n.get("type") == "n8n-nodes-base.postgres" and isinstance(q, str):
            for line in q.split("\n"):
                if "--" in line:
                    check(not re.search(r"\$\d", line[line.index("--"):]),
                          f"{f}: {n['name']} has a $N placeholder inside an SQL comment (pg-promise fills it there): {line.strip()[:120]}")
    print("OK ", f)
    return wf


def main():
    wa = verify(BOTS[0])
    tg = verify(BOTS[1])
    def strip(x):   # drop per-workflow ids (node ids, condition ids, webhookIds), positions and credentials
        if isinstance(x, dict):
            return {k: strip(v) for k, v in x.items() if k not in ("id", "position", "webhookId", "credentials")}
        return [strip(v) for v in x] if isinstance(x, list) else x
    for name in NEW:
        a = json.dumps(strip(node(wa, name)), sort_keys=True).replace("https://wappi.pro/api/sync/", "__BASE__")
        b = json.dumps(strip(node(tg, name)), sort_keys=True).replace("https://wappi.pro/tapi/sync/", "__BASE__")
        check(a == b, f"node {name!r} differs between templates beyond the transport base")
    lc_wa = node(wa, "Latest+Combine")["parameters"]["jsCode"]
    lc_tg = node(tg, "Latest+Combine")["parameters"]["jsCode"]
    check(lc_wa == lc_tg, "Latest+Combine differs between templates")
    print("OK  cross-template identity of the stage-3 nodes")
    print("ALL REPLY PIPELINE ASSERTS PASSED")


if __name__ == "__main__":
    main()
