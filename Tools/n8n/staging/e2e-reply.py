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
    ok = len(b.sends()) == 1 and ex and ex[0]["outcome"] == "sent" and any(c.get("status") == 500 for c in typing_calls)
    return ok, f"sends={len(b.sends())} outcome={ex[0]['outcome'] if ex else None} typing_statuses={[c.get('status') for c in typing_calls]}"


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
    ok = len(calls) == 1 and ex and ex[0]["outcome"] == "sent" and led.startswith("sent|") and rc and rc.get("reconciled") is True
    return ok, f"send_calls={len(calls)} outcome={ex[0]['outcome'] if ex else None} ledger={led} reconciled={rc and rc.get('reconciled')}"


@for_channels("send_500_is_send_failed", "A7: the transport answers HTTP 500 → send_failed recorded with the error, exactly one attempt")
def send_500(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "f500")
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "status", "status": 500})
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1)
    led = ledger(b.pid, b.chat, mid)
    ok = len(b.all_send_calls()) == 1 and ex and ex[0]["outcome"] == "send_failed" and led.startswith("send_failed|-|http 500")
    return ok, f"send_calls={len(b.all_send_calls())} outcome={ex[0]['outcome'] if ex else None} ledger={led}"


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


@for_channels("dropped_without_echo_stays_send_unknown", "A7: the connection dies before the transport processed anything → no echo → the state STAYS send_unknown (visible in the ledger), no resend")
def dropped_no_echo(ctx, ch):
    b, mid, t = simple_trigger(ctx, ch, "nul")
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "drop"})
    b.post(mid, "Сколько стоит букет?", t)
    ex = b.settle(1)
    led = ledger(b.pid, b.chat, mid)
    rc = node_output(ex[0]["data"], "Reconcile Check") if ex else None
    ok = len(b.all_send_calls()) == 1 and ex and ex[0]["outcome"] == "send_unknown" and led.startswith("send_unknown|-|") and rc and rc.get("reconciled") is False
    return ok, f"send_calls={len(b.all_send_calls())} outcome={ex[0]['outcome'] if ex else None} ledger={led} reconciled={rc and rc.get('reconciled')}"


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
    ok = len(rows) == 1 and len(sends) == 1 and body.startswith("Напишите, пожалуйста, текстом") and "Мәтінмен" in body and "Please send" not in body
    return ok, {"executions": [(r["id"], r["status"], r["outcome"], r["last"]) for r in rows], "sends": len(sends), "body": body[:80]}


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
