#!/usr/bin/env python3
"""STAGING scenarios for audit stage 3 (A3 context change, A4 once-only processing, A7 honest send
states) — real n8n runs of the migrated bot templates against the private Postgres and the Wappi MOCK.
Every scenario states the CORRECT behaviour and reads the ledger (reply_events / chat_reply_state), the
Outcome stamp and the mock's recorded transport calls.

    python3 Tools/n8n/staging/e2e-reply.py                  # all (WA + TG)
    python3 Tools/n8n/staging/e2e-reply.py --only owner     # substring filter
    python3 Tools/n8n/staging/e2e-reply.py --channel tg     # one channel

Requires the same environment as e2e.py (bootstrap.py with --bot wa:stgwa001 --bot tg:stgtg001).
"""
import argparse
import os
import subprocess
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from e2e import (Ctx, execution, fixture, http, latest_executions, mock, mock_total, node_output, ran,  # noqa: E402
                 send_calls, wait_executions_settle, workflow_id_by_name, BASE, WA_PROFILE, TG_PROFILE)

CHANNELS = {"wa": ("whatsapp", WA_PROFILE, f"STAGING WA {WA_PROFILE}", "@c.us"),
            "tg": ("telegram", TG_PROFILE, f"STAGING TG {TG_PROFILE}", "")}


def psql(sql):
    out = subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", sql], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


def event(profile_id, chat, mid, body, t, from_me=False, mtype="chat"):
    return {"messages": [{"id": mid, "chatId": chat, "from": chat, "profile_id": profile_id, "body": body, "type": mtype,
                          "time": t, "fromMe": from_me, "senderName": "client"}]}


def post(profile_id, ev):
    return http("POST", f"{BASE}/webhook/{profile_id}", ev, timeout=30)


def ledger(profile_id, chat, mid):
    return psql(f"select state || '|' || coalesce(transport_message_id, '-') || '|' || coalesce(error, '-') from reply_events "
                f"where profile_id = '{profile_id}' and chat_id = '{chat}' and message_id = '{mid}';")


def chat_state(profile_id, chat):
    return psql(f"select incoming_generation || '|' || coalesce(lock_token, '-') from chat_reply_state where profile_id = '{profile_id}' and chat_id = '{chat}';")


def ai_rows(profile_id, chat):
    return int(psql(f"select count(*) from n8n_chat_histories where session_id = '{profile_id}:{chat}' and message->>'type' = 'ai';") or 0)


def ai_marks(profile_id, chat):
    """V6: the unmarked (bot) ai rows' stamps — execution id present?, delivery, transport id — in id order."""
    out = psql(f"select coalesce(string_agg((message->'additional_kwargs' ? 'execution_id')::text || ',' || coalesce(message->'additional_kwargs'->>'delivery', '-') "
               f"|| ',' || coalesce(message->'additional_kwargs'->>'transport_message_id', '-'), ';' order by id), '') from n8n_chat_histories "
               f"where session_id = '{profile_id}:{chat}' and message->>'type' = 'ai' and (message->'additional_kwargs'->>'source') is null;")
    return [tuple(x.split(',')) for x in out.split(';') if x]


def ai_contents(profile_id, chat):
    """T6: the bot's own memory rows' CONTENT (what the model reads), in id order."""
    out = psql(f"select coalesce(string_agg(message->>'content', E'\\x1f' order by id), '') from n8n_chat_histories "
               f"where session_id = '{profile_id}:{chat}' and message->>'type' = 'ai' and (message->'additional_kwargs'->>'source') is null;")
    return [x for x in out.split("\x1f") if x]


def human_rows(profile_id, chat):
    return int(psql(f"select count(*) from n8n_chat_histories where session_id = '{profile_id}:{chat}' and message->>'type' = 'human';") or 0)


def ask_text(ch):
    """The canned media answer exactly as the canonical template holds it (Ask to Send Text; '=' marks the value)."""
    import json
    f = {"wa": "4wYitz5ek30SVNlT-WhatsApp_Bot.json", "tg": "4VN3gsFaC2HUYmcc-Telegram_Bot.json"}[ch]
    wf = json.load(open(os.path.join(os.path.dirname(HERE), "workflows", f), encoding="utf-8"))
    n = next(x for x in wf["nodes"] if x["name"] == "Ask to Send Text")
    return n["parameters"]["assignments"]["assignments"][0]["value"].lstrip("=")


class Bot:
    """One scenario's bot + chat on one channel; the run-scoped chat id keeps scenarios independent."""

    def __init__(self, ctx, ch, tag):
        self.channel, self.pid, wf_name, suffix = CHANNELS[ch]
        self.wf_id = workflow_id_by_name(wf_name)
        if not self.wf_id:
            raise RuntimeError(f"{wf_name} not imported on staging")
        self.chat = f"chat-{tag}-{ctx.run_id}{suffix}"
        ctx.bot(self.pid, self.channel)
        fixture("set-mode", "--profile-id", self.pid, "--suppressed", "false")
        self.since_exec = max([int(e["id"]) for e in latest_executions(self.wf_id, 0, 50)] or [0])
        self.since_calls = mock_total()

    def history(self, rows):
        mock("POST", "/__mock/messages", {"profile_id": self.pid, "chat_id": self.chat,
                                          "messages": [dict(r, chatId=self.chat, **({"from": self.chat} if not r.get("fromMe") else {"from": "me"})) for r in rows]})

    def incoming(self, row):
        mock("POST", "/__mock/incoming", {"profile_id": self.pid, "chat_id": self.chat, "message": dict(row, chatId=self.chat)})

    def post(self, mid, body, t, times=1, parallel=True, mtype="chat"):
        ev = event(self.pid, self.chat, mid, body, t, mtype=mtype)
        results = []
        if parallel:
            ts = [threading.Thread(target=lambda: results.append(post(self.pid, ev))) for _ in range(times)]
            for x in ts:
                x.start()
            for x in ts:
                x.join()
        else:
            for _ in range(times):
                results.append(post(self.pid, ev))
                time.sleep(1.0)
        return [s for s, _ in results]

    def settle(self, n, wait_s=150):
        rows = wait_executions_settle(self.wf_id, self.since_exec, n, wait_s)
        out = []
        for r in rows:
            d = execution(r["id"])
            out.append({"id": r["id"], "status": d.get("status"), "outcome": (node_output(d, "Outcome") or {}).get("outcome"),
                        "last": (d.get("data") or {}).get("resultData", {}).get("lastNodeExecuted"), "data": d})
        return out

    def sends(self):
        calls, _ = send_calls(self.since_calls)
        return calls

    def all_send_calls(self):
        st, d = mock("GET", f"/__mock/calls?since={self.since_calls}&path=message/send")
        return d["calls"]


def simple_trigger(ctx, ch, tag, body="Сколько стоит букет?"):
    b = Bot(ctx, ch, tag)
    t = int(time.time())
    mid = f"{tag}-{ctx.run_id}"
    b.history([{"id": mid, "body": body, "time": t, "fromMe": False, "type": "chat"}])
    return b, mid, t


SCENARIOS = []


def scenario(id_, title):
    def deco(fn):
        SCENARIOS.append((id_, title, fn))
        return fn
    return deco


def for_channels(id_, title):
    """Register the scenario once per channel (the stage-3 acceptance: the same checks pass for WA and TG)."""
    def deco(fn):
        for ch in ("wa", "tg"):
            SCENARIOS.append((f"{id_}[{ch}]", title, (lambda c, ch=ch: fn(c, ch))))
        return fn
    return deco


@for_channels("dup_parallel_once", "A4: the same event delivered twice IN PARALLEL → one send, one 'duplicate' stamp, ledger = sent")
def dup_parallel(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "dpar")
    b.post(mid, "Сколько стоит букет?", t, times=2, parallel=True)
    ex = b.settle(2)
    outs = sorted(e["outcome"] or "?" for e in ex)
    led = ledger(b.pid, b.chat, mid)
    ok = len(b.sends()) == 1 and outs == ["duplicate", "sent"] and led.startswith("sent|")
    return ok, f"sends={len(b.sends())} outcomes={outs} ledger={led}"


@for_channels("dup_sequential_once", "A4: the same event re-delivered 1 s later (first run still in flight) → one send, second refused before any side effect")
def dup_sequential(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "dseq")
    b.post(mid, "Сколько стоит букет?", t, times=2, parallel=False)
    ex = b.settle(2)
    dup = [e for e in ex if e["outcome"] == "duplicate"]
    side_effects = any(ran(e["data"], n) for e in dup for n in ("Debounce Wait", "AI Agent", "Mark Read", "Typing", "HTTP Request"))
    ok = len(b.sends()) == 1 and len(dup) == 1 and not side_effects and ledger(b.pid, b.chat, mid).startswith("sent|")
    return ok, f"sends={len(b.sends())} outcomes={[e['outcome'] for e in ex]} dup_side_effects={side_effects} ledger={ledger(b.pid, b.chat, mid)}"


@for_channels("restart_stale_claim_reclaims", "A4: a claim older than 10 min (n8n died mid-flight) is re-claimed on re-delivery and replied; a 1-min-old claim is a duplicate")
def restart_reclaim(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "rst")
    psql(f"insert into reply_events (profile_id, chat_id, message_id, state, updated_at) values ('{b.pid}', '{b.chat}', '{mid}', 'claimed', now() - interval '11 minutes');")
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1)
    first = (len(b.sends()), ex[0]["outcome"] if ex else None, ledger(b.pid, b.chat, mid))
    # a second, RECENT claim by "another run" must be refused
    mid2 = f"rst2-{ctx.run_id}"
    b.incoming({"id": mid2, "body": "А доставка?", "time": t + 1, "fromMe": False, "type": "chat"})
    psql(f"insert into reply_events (profile_id, chat_id, message_id, state, updated_at) values ('{b.pid}', '{b.chat}', '{mid2}', 'claimed', now() - interval '1 minute');")
    b.since_exec = max([int(e["id"]) for e in latest_executions(b.wf_id, 0, 50)] or [0])
    b.post(mid2, "А доставка?", t + 1)
    ex2 = b.settle(1, wait_s=40)
    ok = first[0] == 1 and first[1] == "sent" and first[2].startswith("sent|") and len(b.sends()) == 1 and ex2 and ex2[0]["outcome"] == "duplicate"
    return ok, f"stale: sends={first[0]} outcome={first[1]} ledger={first[2]}; recent: outcome={ex2[0]['outcome'] if ex2 else None} total_sends={len(b.sends())}"


@for_channels("two_chats_parallel_independent", "A4: two DIFFERENT chats at the same moment → two sends (the lease is per chat, never per bot)")
def two_chats(ctx, ch):
    b1, m1, t = simple_trigger(ctx, ch, "c1")
    b2 = Bot(ctx, ch, "c2")
    m2 = f"c2-{ctx.run_id}"
    b2.history([{"id": m2, "body": "Есть пионы?", "time": t, "fromMe": False, "type": "chat"}])
    b2.since_exec = b1.since_exec
    b2.since_calls = b1.since_calls
    ts = [threading.Thread(target=lambda: b1.post(m1, "Сколько стоит букет?", t)), threading.Thread(target=lambda: b2.post(m2, "Есть пионы?", t))]
    for x in ts:
        x.start()
    for x in ts:
        x.join()
    ex = b1.settle(2)
    recipients = sorted(c["body"].get("recipient") or c["body"].get("=recipient") for c in b1.sends())
    ok = len(b1.sends()) == 2 and [e["outcome"] for e in ex].count("sent") == 2
    return ok, f"sends={len(b1.sends())} recipients={recipients} outcomes={[e['outcome'] for e in ex]}"


def _mid_flight(ctx, ch, tag, inject, expect_outcome, expect_ledger_prefix):
    b, mid, t = simple_trigger(ctx, ch, tag)
    b.post(mid, "Сколько стоит букет?", t)
    time.sleep(10.5)          # past the 8-s debounce + fetch: the run is now inside the LLM / pauses
    inject(b, t)
    ex = b.settle(1)
    led = ledger(b.pid, b.chat, mid)
    ok = len(b.sends()) == 0 and ex and ex[0]["outcome"] == expect_outcome and led.startswith(expect_ledger_prefix) and ai_rows(b.pid, b.chat) == 0
    return ok, f"sends={len(b.sends())} outcome={ex[0]['outcome'] if ex else None} ledger={led} ai_memory_rows={ai_rows(b.pid, b.chat)} last={ex[0]['last'] if ex else None}"


@for_channels("newer_incoming_during_llm_supersedes", "A3: a NEWER client message while the answer is being generated → the stale text is cancelled before the send, its AI memory dropped")
def newer_during_llm(ctx, ch):
    return _mid_flight(ctx, ch, "nll", lambda b, t: b.incoming({"id": f"nll2-{ctx.run_id}", "body": "Точнее: 25 роз", "time": t + 11, "fromMe": False, "type": "chat"}),
                       "superseded", "superseded|")


@for_channels("owner_reply_before_send_cancels", "A3: the OWNER answers while the bot is composing → no auto-send, cancelled_owner, memory dropped")
def owner_before_send(ctx, ch):
    return _mid_flight(ctx, ch, "own", lambda b, t: b.incoming({"id": f"own2-{ctx.run_id}", "body": "Да, 25000", "time": t + 11, "fromMe": True, "type": "chat", "from": "me"}),
                       "cancelled_owner", "cancelled_owner|")


@for_channels("auto_off_mid_flight_cancels", "A3: «Вместе» switched on while the answer is being generated → the lease re-reads the mode, no send, cancelled_mode")
def auto_off(ctx, ch):
    return _mid_flight(ctx, ch, "off", lambda b, t: fixture("set-mode", "--profile-id", b.pid, "--suppressed", "true"),
                       "cancelled_mode", "cancelled_mode|")


@for_channels("typing_failure_still_sends", "A7: the typing indicator fails (HTTP 500) → the reply is still sent")
def typing_fail(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "typ")
    mock("POST", "/__mock/fault", {"match": "typing", "mode": "status", "status": 500})
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1)
    typing_calls = [c for c in (mock("GET", f"/__mock/calls?since={b.since_calls}&path=typing")[1] or {}).get("calls", [])]
    marks = ai_marks(b.pid, b.chat)        # V6: the real memory node's row, stamped right after the agent, marked sent
    ok = len(b.sends()) == 1 and ex and ex[0]["outcome"] == "sent" and any(c.get("status") == 500 for c in typing_calls) \
        and len(marks) == 1 and marks[0][:2] == ("true", "sent")
    return ok, f"sends={len(b.sends())} outcome={ex[0]['outcome'] if ex else None} typing_statuses={[c.get('status') for c in typing_calls]} ai_marks={marks}"


@for_channels("expired_lock_does_not_block", "A4: a lease left behind by a dead run (expired) does not block the next reply; the lock is released after the send")
def expired_lock(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "exp")
    psql(f"insert into chat_reply_state (profile_id, chat_id, incoming_generation, lock_token, lock_expires_at) values ('{b.pid}', '{b.chat}', 0, 'dead-run', now() - interval '1 minute');")
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1)
    st = chat_state(b.pid, b.chat)
    ok = len(b.sends()) == 1 and ex and ex[0]["outcome"] == "sent" and st.endswith("|-")
    return ok, f"sends={len(b.sends())} outcome={ex[0]['outcome'] if ex else None} chat_state(gen|lock)={st}"


@for_channels("live_lock_blocks_send", "A4: a LIVE lease held by another run of the same chat → this run does not send (cancelled_busy), nothing is resent later")
def live_lock(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "bsy")
    b.post(mid, "Сколько стоит букет?", t)
    time.sleep(10.5)
    psql(f"insert into chat_reply_state (profile_id, chat_id, incoming_generation, lock_token, lock_expires_at) values ('{b.pid}', '{b.chat}', 1, 'other-run', now() + interval '5 minutes') "
         f"on conflict (profile_id, chat_id) do update set lock_token = 'other-run', lock_expires_at = now() + interval '5 minutes';")
    ex = b.settle(1)
    led = ledger(b.pid, b.chat, mid)
    ok = len(b.sends()) == 0 and ex and ex[0]["outcome"] == "cancelled_busy" and led.startswith("cancelled_busy|")
    return ok, f"sends={len(b.sends())} outcome={ex[0]['outcome'] if ex else None} ledger={led}"


@for_channels("accept_then_drop_reconciles_sent", "A7: the transport accepts the message but the HTTP answer is lost → reconciled as sent from the echo, NO resend")
def accept_drop(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "drp")
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "accept_then_drop"})
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1)
    led = ledger(b.pid, b.chat, mid)
    calls = b.all_send_calls()
    rc = node_output(ex[0]["data"], "Reconcile Check") if ex else None
    marks = ai_marks(b.pid, b.chat)          # V6: the reply's memory row is stamped and marked delivered with the echo's id
    ok = len(calls) == 1 and ex and ex[0]["outcome"] == "sent" and led.startswith("sent|") and rc and rc.get("reconciled") is True \
        and marks == [("true", "sent", led.split("|")[1])]
    return ok, f"send_calls={len(calls)} outcome={ex[0]['outcome'] if ex else None} ledger={led} reconciled={rc and rc.get('reconciled')} ai_marks={marks}"


@for_channels("send_500_is_send_failed", "A7: the transport answers HTTP 500 → send_failed recorded with the error, exactly one attempt")
def send_500(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "f500")
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "status", "status": 500})
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1)
    led = ledger(b.pid, b.chat, mid)
    # R7: the refused reply never reached the client — the memory node's ai row goes (content = the agent's output on
    # this very n8n runtime), the client's own row stays
    ai, hu = ai_rows(b.pid, b.chat), human_rows(b.pid, b.chat)
    ok = len(b.all_send_calls()) == 1 and ex and ex[0]["outcome"] == "send_failed" and led.startswith("send_failed|-|http 500") and ai == 0 and hu == 1
    return ok, f"send_calls={len(b.all_send_calls())} outcome={ex[0]['outcome'] if ex else None} ledger={led} memory ai/human={ai}/{hu}"


@for_channels("send_timeout_reconciles_from_echo", "A7: the transport answers after our 30-s timeout (message really went out) → send_unknown → echo found → sent, no resend")
def send_timeout(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "tmo")
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "timeout", "delay": 40})
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1, wait_s=200)
    led = ledger(b.pid, b.chat, mid)
    settle = node_output(ex[0]["data"], "Settle Send") if ex else None
    ok = len(b.all_send_calls()) == 1 and ex and ex[0]["outcome"] == "sent" and settle and settle.get("state") == "send_unknown"
    return ok, f"send_calls={len(b.all_send_calls())} settle={settle and settle.get('state')} outcome={ex[0]['outcome'] if ex else None} ledger={led}"


@for_channels("dropped_without_echo_stays_send_unknown", "A7: the connection dies before the transport processed anything → no echo → the state STAYS send_unknown (visible in the ledger), no resend; T6: the memory row carries «[доставка не подтверждена] »")
def dropped_no_echo(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "nul")
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "drop"})
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1)
    led = ledger(b.pid, b.chat, mid)
    rc = node_output(ex[0]["data"], "Reconcile Check") if ex else None
    ai = ai_rows(b.pid, b.chat)            # R7 decision: send_unknown KEEPS the reply in memory (usually it was delivered)
    marks = ai_marks(b.pid, b.chat)        # V6: … and the row SAYS so — stamped by this run, delivery unknown
    texts = ai_contents(b.pid, b.chat)     # T6 (V6-unknown): … in the content the MODEL reads too — the undelivered mark, once
    ok = len(b.all_send_calls()) == 1 and ex and ex[0]["outcome"] == "send_unknown" and led.startswith("send_unknown|-|") and rc and rc.get("reconciled") is False \
        and ai == 1 and marks == [("true", "unknown", "-")] \
        and len(texts) == 1 and texts[0].startswith("[доставка не подтверждена] ") and not texts[0][len("[доставка не подтверждена] "):].startswith("[доставка")
    return ok, f"send_calls={len(b.all_send_calls())} outcome={ex[0]['outcome'] if ex else None} ledger={led} reconciled={rc and rc.get('reconciled')} ai_memory_rows={ai} ai_marks={marks} ai_contents={[t[:60] for t in texts]}"


@for_channels("reordered_webhooks_one_reply", "R6: two client messages whose webhooks arrive in REVERSE order (the older one claimed last, inside the newer one's debounce) → exactly one reply, by the newer message's run")
def reordered(ctx, ch):
    b = Bot(ctx, ch, "ro")
    t = int(time.time())
    ma, mb = f"roA-{ctx.run_id}", f"roB-{ctx.run_id}"
    b.history([{"id": ma, "body": "Здравствуйте", "time": t, "fromMe": False, "type": "chat"},
               {"id": mb, "body": "Сколько стоит букет из 25 роз?", "time": t + 1, "fromMe": False, "type": "chat"}])
    b.post(mb, "Сколько стоит букет из 25 роз?", t + 1)
    time.sleep(1.5)            # B's run has claimed; the late, OLDER A is claimed now — before B's lease
    b.post(ma, "Здравствуйте", t)
    ex = b.settle(2)
    la, lb = ledger(b.pid, b.chat, ma), ledger(b.pid, b.chat, mb)
    ok = len(b.sends()) == 1 and lb.startswith("sent|") and la.startswith("superseded|")
    return ok, f"sends={len(b.sends())} ledger A={la} B={lb} outcomes={[e['outcome'] for e in ex]}"


@for_channels("crossed_reconcile_stays_send_unknown", "R10: the send's answer is lost and the reconcile fetch comes back CROSSED (the neighbour chat's window, with the same canned text) → send_unknown, never the neighbour's id")
def crossed_reconcile(ctx, ch):
    b = Bot(ctx, ch, "crx")
    t = int(time.time())
    mid = f"crx-{ctx.run_id}"
    b.history([{"id": mid, "body": "https://mock/photo.jpg", "time": t, "fromMe": False, "type": "image"}])
    nb = f"chat-crxn-{ctx.run_id}{CHANNELS[ch][3]}"
    mock("POST", "/__mock/messages", {"profile_id": b.pid, "chat_id": nb, "messages": [
        {"id": f"nbin-{ctx.run_id}", "chatId": nb, "from": nb, "body": "фото", "type": "image", "time": t, "fromMe": False},
        {"id": f"nbout-{ctx.run_id}", "chatId": nb, "from": "me", "body": ask_text(ch), "type": "chat", "time": t + 600, "fromMe": True}]})
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "drop"})                       # nothing reaches the client
    mock("POST", "/__mock/fault", {"match": "messages/get", "mode": "cross", "cross_chat": nb, "after": "message/send", "times": 3})
    b.post(mid, "https://mock/photo.jpg", t, mtype="image")
    ex = b.settle(1)
    led = ledger(b.pid, b.chat, mid)
    rc = node_output(ex[0]["data"], "Reconcile Check") if ex else None
    ai_hist = psql(f"select count(*) from chat_history_events where profile_id = '{b.pid}' and chat_id = '{b.chat}' and source = 'ai';")
    ok = len(b.all_send_calls()) == 1 and ex and ex[0]["outcome"] == "send_unknown" and led.startswith("send_unknown|-|") \
        and rc and rc.get("reconciled") is False and (rc.get("foreignFetched") or 0) > 0 and ai_hist == "0"
    return ok, (f"send_calls={len(b.all_send_calls())} outcome={ex[0]['outcome'] if ex else None} ledger={led} "
                f"reconciled={rc and rc.get('reconciled')} foreignFetched={rc and rc.get('foreignFetched')} ai_history_rows={ai_hist}")


def _same_second_peer(ctx, ch, tag, wappi_lists_peer_first):
    """V5: A's Final Fetch is answered with [A] alone (the mock computes the window BEFORE its injected 6-s delay);
    during that delay a SAME-second message B enters the chat and its webhook is claimed — after A's snapshot, before
    A's lease. The lease must refuse, re-read once, and let Wappi's order on a snapshot holding BOTH pick the one
    reply."""
    b = Bot(ctx, ch, tag)
    t = int(time.time())
    ma, mb = f"{tag}A-{ctx.run_id}", f"{tag}B-{ctx.run_id}"
    ra = {"id": ma, "body": "Здравствуйте", "time": t, "fromMe": False, "type": "chat"}
    rb = {"id": mb, "body": "Сколько стоит букет из 25 роз?", "time": t, "fromMe": False, "type": "chat"}
    b.history([ra])
    mock("POST", "/__mock/fault", {"match": "messages/get", "mode": "timeout", "delay": 6, "times": 1, "after": "typing/start", "profile_id": b.pid})
    b.post(ma, ra["body"], t)
    deadline, armed = time.time() + 150, False
    while time.time() < deadline and not armed:          # A's Final Fetch is now inside the injected delay
        st, d = mock("GET", f"/__mock/calls?since={b.since_calls}&path=messages/get")
        armed = any(c.get("fault") == "timeout" for c in (d or {}).get("calls", []))
        time.sleep(0.3)
    if not armed:
        return False, "A's Final Fetch never reached the injected delay"
    b.history([rb, ra] if wappi_lists_peer_first else [ra, rb])
    b.post(mb, rb["body"], t)
    ex = b.settle(2, wait_s=240)
    la, lb = ledger(b.pid, b.chat, ma), ledger(b.pid, b.chat, mb)
    run_a = next((e for e in ex if (node_output(e["data"], "Outcome") or {}).get("messageId") == ma), None)
    rd = ((run_a or {}).get("data") or {}).get("data", {}).get("resultData", {}).get("runData", {}) if run_a else {}
    sends = b.sends()
    detail = {"sends": len(sends), "ledger_A": la, "ledger_B": lb, "A_final_checks": len(rd.get("Final Check") or []),
              "A_rechecked": "Recheck Fetch" in rd, "outcomes": [e["outcome"] for e in ex]}
    return sends, la, lb, rd, detail, b


@for_channels("same_second_peer_after_final_fetch", "V5: a SAME-second message claimed after A's Final Fetch → A's lease refuses, A re-reads once; Wappi lists B first → exactly one reply, by B; A's draft leaves memory")
def same_second_peer(ctx, ch):
    r = _same_second_peer(ctx, ch, "ssp", True)
    if len(r) == 2:
        return r
    sends, la, lb, rd, detail, b = r
    detail["ai_memory_rows"] = ai_rows(b.pid, b.chat)
    ok = len(sends) == 1 and la.startswith("superseded|") and lb.startswith("sent|") and detail["A_rechecked"] \
        and detail["A_final_checks"] == 2 and detail["ai_memory_rows"] == 1
    return ok, detail


@for_channels("same_second_peer_wappi_lists_older", "V5: the same race, but the re-read lists A first → exactly one reply, by A after its re-read (the send after the loop reaches the right chat and profile)")
def same_second_peer_a_first(ctx, ch):
    r = _same_second_peer(ctx, ch, "ssq", False)
    if len(r) == 2:
        return r
    sends, la, lb, rd, detail, b = r
    call = sends[0] if sends else {}
    body = call.get("body") or {}
    detail["recipient"] = body.get("recipient") or body.get("=recipient")
    detail["profile_id"] = (call.get("query") or {}).get("profile_id")
    ok = len(sends) == 1 and la.startswith("sent|") and lb.startswith("superseded|") and detail["A_rechecked"] \
        and detail["recipient"] == b.chat and detail["profile_id"] == b.pid
    return ok, detail


def _lost_send_with_echo(ctx, ch, tag, echo, pre_sql=None):
    """V7: the transport accepts the canned media ask but the answer is lost; the echo row the mock appends carries `echo`."""
    b = Bot(ctx, ch, tag)
    t = int(time.time())
    mid = f"{tag}-{ctx.run_id}"
    b.history([{"id": mid, "body": "https://mock/photo.jpg", "time": t, "fromMe": False, "type": "image"}])
    if pre_sql:
        psql(pre_sql(b))
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "accept_then_drop", "echo": echo})
    b.post(mid, "https://mock/photo.jpg", t, mtype="image")
    ex = b.settle(1)
    rc = node_output(ex[0]["data"], "Reconcile Check") if ex else None
    return b, mid, ex, rc, ledger(b.pid, b.chat, mid)


@for_channels("unmarked_echo_stays_send_unknown", "V7: the answer is lost and the only row with our text carries NO API mark (no from_where, no task id — isFromAPI alone proves nothing) → send_unknown, never adopted")
def unmarked_echo(ctx, ch):
    b, mid, ex, rc, led = _lost_send_with_echo(ctx, ch, "unm", {"from_where": "", "task_id": "", "isFromAPI": True})
    ok = len(b.all_send_calls()) == 1 and ex and ex[0]["outcome"] == "send_unknown" and led.startswith("send_unknown|-|") \
        and rc and rc.get("reconciled") is False and "unattributed" in (rc.get("error") or "")
    return ok, f"send_calls={len(b.all_send_calls())} outcome={ex[0]['outcome'] if ex else None} ledger={led} reconcile_error={rc and rc.get('error')}"


@for_channels("owner_app_same_text_stays_send_unknown", "V7: the answer is lost and the only same-text row is the OWNER's app send (owner_app in the one history, API-marked like ours) → send_unknown; the owner row keeps its source, last_sent stays empty")
def owner_app_same_text(ctx, ch):
    own = f"oapown-{ctx.run_id}"
    b, mid, ex, rc, led = _lost_send_with_echo(
        ctx, ch, "oap", {"id": own},
        pre_sql=lambda b: (f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id) "
                           f"values ('{b.pid}', '{b.chat}', '{own}', 'owner_app', 'owner text', 'chat', now(), 'proxy-run');"))
    src = psql(f"select source from chat_history_events where profile_id = '{b.pid}' and chat_id = '{b.chat}' and message_id = '{own}';")
    last = psql(f"select coalesce(last_sent_message_id, '-') from chat_reply_state where profile_id = '{b.pid}' and chat_id = '{b.chat}';")
    ok = len(b.all_send_calls()) == 1 and ex and ex[0]["outcome"] == "send_unknown" and led.startswith("send_unknown|-|") \
        and src == "owner_app" and last == "-" and rc and "owner_app" in (rc.get("error") or "")
    return ok, f"outcome={ex[0]['outcome'] if ex else None} ledger={led} owner_row_source={src} last_sent={last} reconcile_error={rc and rc.get('error')}"


@for_channels("media_message_gets_the_ru_ask_for_text", "a photo/document/sticker never reaches the model: the canned ask is Russian (+ Kazakh line), sent once, through the same send chain (stage 6, 2026-09-22)")
def media_ask(ctx, ch):
    b = Bot(ctx, ch, "media")
    t = int(time.time())
    mid = f"media-{ctx.run_id}"
    b.history([{"id": mid, "body": "https://mock/photo.jpg", "time": t, "fromMe": False, "type": "image"}])
    b.post(mid, "https://mock/photo.jpg", t, mtype="image")
    rows = b.settle(1)
    sends = b.sends()
    body = (sends[0].get("body") or {}).get("body", "") if sends else ""
    # the ledger too: a multi-line text through Record Send (R7 part 2 — a $N in an SQL comment left it send_pending)
    led = ledger(b.pid, b.chat, mid)
    ok = len(rows) == 1 and len(sends) == 1 and body.startswith("Напишите, пожалуйста, текстом") and "Мәтінмен" in body and "Please send" not in body \
        and led.startswith("sent|")
    return ok, {"executions": [(r["id"], r["status"], r["outcome"], r["last"]) for r in rows], "sends": len(sends), "body": body[:80], "ledger": led}


_td = None


def test_dialog_module():
    """e2e-test-dialog.py's BotVersions (edit a staging bot's PUBLISHED prompt, restore it) — loaded on first use."""
    global _td
    if _td is None:
        import importlib.util
        spec = importlib.util.spec_from_file_location("e2e_test_dialog", os.path.join(HERE, "e2e-test-dialog.py"))
        _td = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_td)
    return _td


def tool_outputs(exec_data, node):
    """What an AI sub-node handed the agent (its ai_tool output items) in every run of one execution."""
    out = []
    for run in ((exec_data.get("data") or {}).get("resultData", {}).get("runData", {}).get(node, []) or []):
        for br in (run.get("data") or {}).get("ai_tool") or []:
            for it in br or []:
                out.append(it.get("json") or {})
    return out


V9_CATALOG = ("\n\nProducts:\nProduct1: Фильтр масляный Toyota 90915-YZZE1\nProduct1 Price: 3200 тг\nProduct1 Description: Оригинал\n\n"
              "Services:\n")
V9_ROW = "Фильтр масляный 90915-YZZE1 — 3900 тг, оригинал"
V9_QUESTION = "Camry 2019, фильтр масляный 90915-YZZE1 сколько?"


@for_channels("price_conflict_reaches_the_agent_marked [llm]", "V9: the clone's price-list tool runs the Price List Search sub-workflow with the clone's PUBLISHED prompt — a price-list row at 3900 against its catalog's 3200 reaches the agent with «[Сверка с каталогом: … Цены расходятся.]», and the reply does not pick 3900 alone")
def price_conflict(ctx, ch):
    b = Bot(ctx, ch, "v9pc")
    key = "botWaId" if ch == "wa" else "botTgId"
    tag = f"v9pc-{ctx.run_id}-{ch}"
    psql(f"insert into documents (content, metadata) values ('{V9_ROW}', '{{\"{key}\": \"{b.wf_id}\", \"fileId\": \"{tag}\"}}'::jsonb);")
    try:
        with test_dialog_module().BotVersions(b.wf_id) as bv:
            wf = bv.put(bv.base_prompt + V9_CATALOG, publish=True)
            if wf.get("activeVersionId") != wf.get("versionId"):
                bv.set_active(False)
                bv.set_active(True)
                wf = bv.read()
            premise = "Product1 Price: 3200" in test_dialog_module().agent_prompt((wf.get("activeVersion") or {}).get("nodes"))
            t = int(time.time())
            mid = f"v9pc-{ctx.run_id}"
            b.history([{"id": mid, "body": V9_QUESTION, "time": t, "fromMe": False, "type": "chat"}])
            b.post(mid, V9_QUESTION, t)
            rows = b.settle(1, wait_s=180)
    finally:
        psql(f"delete from documents where metadata->>'fileId' = '{tag}';")
    data = rows[0]["data"] if rows else {}
    docs = [d for o in tool_outputs(data, "Supabase Vector Store") for d in (o.get("documents") or [])]
    marked = any(V9_ROW in d and "[Сверка с каталогом:" in d and "3200" in d and "3900" in d and "Цены расходятся" in d for d in docs)
    sends = b.sends()
    body = (sends[0].get("body") or {}).get("body", "") if sends else ""
    flat = body.replace(" ", "").replace("\u00a0", "")
    picked = "3900" in flat and "3200" not in flat and "расход" not in body.lower()
    ok = premise and marked and len(sends) == 1 and not picked
    return ok, {"premise_published_catalog": premise, "tool_calls": len(tool_outputs(data, "Supabase Vector Store")), "marked": marked,
                "documents": [d[:160] for d in docs][:2], "sends": len(sends), "reply": body[:200], "picked_3900_alone": picked}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--channel", choices=["wa", "tg"])
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for id_, title, _ in SCENARIOS:
            print(f"{id_:44} {title}")
        return
    st, _ = http("GET", f"{BASE}/healthz")
    if st != 200:
        sys.exit(f"staging n8n not reachable at {BASE}")
    mock("POST", "/__mock/reset")
    ctx = Ctx()
    passed = failed = 0
    try:
        for id_, title, fn in SCENARIOS:
            if a.only and a.only not in id_:
                continue
            if a.channel and f"[{a.channel}]" not in id_:
                continue
            mock("POST", "/__mock/reset")
            t0 = time.time()
            try:
                ok, detail = fn(ctx)
            except Exception as e:
                ok, detail = False, f"EXCEPTION {e!r}"
            passed += ok
            failed += (not ok)
            print(f"{'PASS' if ok else 'FAIL'}  {id_} ({time.time() - t0:.0f}s) — {title}", flush=True)
            print(f"      {detail}", flush=True)
    finally:
        ctx.cleanup()
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
