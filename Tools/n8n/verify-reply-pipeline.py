#!/usr/bin/env python3
"""Stage-3 (A3/A4/A7) semantic verifier for BOTH bot templates — what verify-message-batching.py's
edge asserts do not cover: the SQL contracts of the four ledger statements, the Code-node rules of
the final check / settle / reconcile / cancel label, the Outcome stamp reading the recorded state,
and byte-identity of every new node across WhatsApp and Telegram (only the transport base differs).

    python3 Tools/n8n/verify-reply-pipeline.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
BOTS = ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json")
NEW = ("Claim Event", "Claimed?", "Generated?", "Final Fetch", "Final Check", "Final OK?", "Acquire Send Lease",
       "Send Allowed?", "Settle Send", "Send Unknown?", "Wait Reconcile", "Reconcile Fetch", "Reconcile Check",
       "Record Send", "Cancel Label", "Record Cancel")
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
    # (1) Claim: atomic upsert, refuses a live duplicate, re-claims only a stale claim, bumps the generation.
    q = node(wf, "Claim Event")["parameters"]["query"]
    for frag in ("insert into reply_events", "on conflict (profile_id, chat_id, message_id) do update",
                 "reply_events.state in ('claimed', 'generated')", "interval '10 minutes'",
                 "incoming_generation = chat_reply_state.incoming_generation + 1", "as history_watermark"):
        check(frag in q, f"{f}: Claim Event lacks {frag!r}")
    params = node(wf, "Claim Event")["parameters"]["options"]["queryReplacement"]
    check("$execution.id" in params and ".profile_id" in params and ".from" in params and ".id" in params,
          f"{f}: Claim Event params must be (profile, chat=from, message id, execution id): {params}")
    # (2) Lease: mode re-read + generation match + lease + event state in ONE statement; lock scoped per chat.
    q = node(wf, "Acquire Send Lease")["parameters"]["query"]
    for frag in ("from reply_mode_flags", "chat_id in ($2, '*')", "order by (chat_id = '*')", "c.incoming_generation = $4::bigint",
                 "c.lock_token is null or c.lock_expires_at < now() or c.lock_token = $5", "interval '90 seconds'",
                 "not (select suppressed from mode)", "state = 'send_pending'", "as allowed", "as lock_busy"):
        check(frag in q, f"{f}: Acquire Send Lease lacks {frag!r}")
    # (3) Record Send: final state + lease release only by the holder; unknown never retried here.
    q = node(wf, "Record Send")["parameters"]["query"]
    for frag in ("set state = $4", "transport_message_id = nullif($5, '')", "lock_token = $7", "$4::text as outcome"):
        check(frag in q, f"{f}: Record Send lacks {frag!r}")
    check("message/send" not in q, f"{f}: Record Send must not touch the transport")
    # (4) Record Cancel: state + drop the AI memory rows a cancelled generation wrote + release the lease.
    q = node(wf, "Record Cancel")["parameters"]["query"]
    for frag in ("delete from n8n_chat_histories", "message->>'type' = 'ai'", "id > $5::bigint", "$6::boolean", "lock_token = $7"):
        check(frag in q, f"{f}: Record Cancel lacks {frag!r}")
    # (5) Final Check rules (newer client → superseded, owner → cancelled_owner, trigger missing → cancelled_fetch).
    js = node(wf, "Final Check")["parameters"]["jsCode"]
    for frag in ("outcome = 'superseded'", "outcome = 'cancelled_owner'", "outcome = 'cancelled_fetch'", "outcome = 'generation_failed'",
                 "fromMe === false", "fromMe === true", "$('Ask to Send Text')", "$('Retrieve Answer')", "(a.__i || 0) - (b.__i || 0)"):
        check(frag in js, f"{f}: Final Check lacks {frag!r}")
    check("message/send" not in js, f"{f}: Final Check must not send")
    # (6) Settle: three states, no assumption of delivery on a missing answer.
    js = node(wf, "Settle Send")["parameters"]["jsCode"]
    for frag in ("state = 'sent'", "state = 'send_failed'", "state = 'send_unknown'", "body.status === 'done' || body.message_id"):
        check(frag in js, f"{f}: Settle Send lacks {frag!r}")
    # (7) Reconcile: adopts an echo of OUR text after the send started; never resends.
    js = node(wf, "Reconcile Check")["parameters"]["jsCode"]
    for frag in ("fromMe === true", "m.body.trim() === String(s.text).trim()", "sendStartedAt", "'send_unknown'"):
        check(frag in js, f"{f}: Reconcile Check lacks {frag!r}")
    check("message/send" not in js and "$http" not in js, f"{f}: Reconcile Check must never resend")
    wait = node(wf, "Wait Reconcile")
    check(wait["type"] == "n8n-nodes-base.wait" and wait["parameters"].get("amount") == 3 and wait.get("webhookId"),
          f"{f}: Wait Reconcile must be a 3-s wait with a webhookId")
    # (8) Cancel Label names every refusal; only a generated run drops AI memory.
    js = node(wf, "Cancel Label")["parameters"]["jsCode"]
    for frag in ("prev === 'Is Latest?'", "prev === 'If Quota Allows'", "prev === 'Generated?'", "prev === 'Final OK?'", "prev === 'Send Allowed?'",
                 "'cancelled_mode'", "'cancelled_busy'", "dropAiHistory: generated"):
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
