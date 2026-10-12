#!/usr/bin/env python3
"""STAGING scenarios for audit stage 4 (A5 real handoff, A6 one history) — real n8n runs of the
migrated templates, the proxy, the handoff workflows, the private Postgres and the Wappi MOCK.

    python3 Tools/n8n/staging/e2e-handoff.py                 # all
    python3 Tools/n8n/staging/e2e-handoff.py --only notifier # substring filter
    python3 Tools/n8n/staging/e2e-handoff.py --list

Two scenarios drive the REAL model (handoff tool call; the owner's agreement remembered) — they can
flake on model behaviour and are tagged [llm]. Everything else is deterministic.
"""
import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from e2e import (Ctx, execution, fixture, http, latest_executions, mock, mock_total, node_output,  # noqa: E402
                 wait_executions_settle, workflow_id_by_name, BASE, WA_PROFILE, TG_PROFILE)
_spec = importlib.util.spec_from_file_location("e2e_auth", os.path.join(HERE, "e2e-auth.py"))
e2e_auth = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(e2e_auth)

CHANNELS = {"wa": ("whatsapp", WA_PROFILE, f"STAGING WA {WA_PROFILE}", "@c.us"),
            "tg": ("telegram", TG_PROFILE, f"STAGING TG {TG_PROFILE}", "")}


def psql(sql):
    out = subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", sql], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


def history(pid, chat):
    return psql(f"select string_agg(source || ':' || coalesce(body, '') , ' | ' order by id) from chat_history_events where profile_id = '{pid}' and chat_id = '{chat}';")


def memory(pid, chat):
    return psql(f"select string_agg((message->>'type') || '/' || coalesce(message->'additional_kwargs'->>'source', 'agent') || ':' || left(message->>'content', 40), ' | ' order by id) from n8n_chat_histories where session_id = '{pid}:{chat}';")


def memory_count(pid, chat):
    return int(psql(f"select count(*) from n8n_chat_histories where session_id = '{pid}:{chat}';") or 0)


def handoffs(pid, chat):
    return psql(f"select string_agg(id || ':' || status || ':' || reason || ':' || notify_state || ':' || coalesce(data->>'phone', '-'), ' | ' order by id) from handoff_requests where profile_id = '{pid}' and chat_id = '{chat}';")


def paused(pid, chat):
    return psql(f"select coalesce((select handoff_paused from chat_reply_state where profile_id = '{pid}' and chat_id = '{chat}'), false);")


class Bot:
    def __init__(self, ctx, ch, tag):
        self.channel, self.pid, wf_name, suffix = CHANNELS[ch]
        self.wf_id = workflow_id_by_name(wf_name)
        if not self.wf_id:
            raise RuntimeError(f"{wf_name} not imported on staging")
        self.chat = f"7701{int(time.time()) % 100000:05d}{suffix}" if ch == "wa" else f"5{int(time.time()) % 100000000:08d}"
        self.chat = f"chat{tag}{ctx.run_id[:6]}{suffix}" if ch == "tg" else self.chat
        ctx.bot(self.pid, self.channel)
        fixture("set-mode", "--profile-id", self.pid, "--suppressed", "false")
        self.since_exec = max([int(e["id"]) for e in latest_executions(self.wf_id, 0, 50)] or [0])
        self.since_calls = mock_total()

    def mark(self):
        self.since_exec = max([int(e["id"]) for e in latest_executions(self.wf_id, 0, 50)] or [0])
        self.since_calls = mock_total()

    def event(self, mid, body, t, wh_type="incoming_message", from_me=False, profile=None, mtype="chat"):
        m = {"id": mid, "chatId": self.chat, "from": (self.chat if not from_me else "770100000000"), "to": (self.chat if from_me else "770100000000"),
             "profile_id": profile or self.pid, "body": body, "type": mtype, "time": t, "fromMe": from_me, "is_me": from_me,
             "senderName": "owner" if from_me else "client", "wh_type": wh_type}
        return {"messages": [m]}

    def post(self, ev):
        st, _ = http("POST", f"{BASE}/webhook/{self.pid}", ev, timeout=30)
        return st

    def history_seed(self, rows):
        mock("POST", "/__mock/messages", {"profile_id": self.pid, "chat_id": self.chat,
                                          "messages": [dict(r, chatId=self.chat, **({"from": self.chat} if not r.get("fromMe") else {"from": "me"})) for r in rows]})

    def settle(self, n, wait_s=150):
        rows = wait_executions_settle(self.wf_id, self.since_exec, n, wait_s)
        out = []
        for r in rows:
            d = execution(r["id"])
            out.append({"id": r["id"], "status": d.get("status"), "outcome": (node_output(d, "Outcome") or {}).get("outcome"),
                        "last": (d.get("data") or {}).get("resultData", {}).get("lastNodeExecuted"), "data": d})
        return out

    def sends(self):
        st, d = mock("GET", f"/__mock/calls?since={self.since_calls}&path=message/send")
        return [c for c in d["calls"] if not c.get("dropped")]


def owner_for(ctx, bot):
    """An install token that OWNS the staging bot profile (A1 ownership = bot_profiles.owner_id)."""
    o = e2e_auth.Owner("ho", ctx.run_id)
    psql(f"update bot_profiles set owner_id = '{o.install_id}' where profile_id = '{bot.pid}';")
    o.refresh()
    return o


class Dedicated:
    """A profile of this run ALONE, for a scenario whose assertions span the WHOLE profile (the Handoffs list's
    pausedChats) or whose fault a schedule could hand to another profile's row (the notifier sweeps every due
    request). Every other scenario shares the imported staging bot profile, and the [llm] ones leave real state
    there: the baseline run's agent called create_handoff AGAIN on «Спасибо!» (exec 3753) — an open request with a
    pending notice + that chat paused — which the notifier served first (consuming the injected 500) and which
    showed up in pausedChats. No bot workflow is needed here: both scenarios only touch rows and the webhooks.
    Registered through the run's fixture identity, so ctx.cleanup() deletes it with the rest."""

    def __init__(self, ctx, tag, channel="whatsapp", suffix="@c.us"):
        self.channel = channel
        self.pid = f"stg{tag}{ctx.run_id[:8]}"
        self.chat = f"7701{int(time.time()) % 100000:05d}{suffix}"
        ctx.bot(self.pid, channel)


SCENARIOS = []


def scenario(id_, title):
    def deco(fn):
        SCENARIOS.append((id_, title, fn))
        return fn
    return deco


def for_channels(id_, title):
    def deco(fn):
        for ch in ("wa", "tg"):
            SCENARIOS.append((f"{id_}[{ch}]", title, (lambda c, ch=ch: fn(c, ch))))
        return fn
    return deco


@for_channels("outgoing_phone_recorded", "A6: a message the owner sent from the PHONE lands in the one history (owner_phone) and in the agent's memory once, even when re-delivered; T6: the memory row opens with «Владелец: »")
def outgoing_phone(ctx, ch):
    b = Bot(ctx, ch, "op")
    t = int(time.time())
    ev = b.event(f"own-{ctx.run_id}", "Да, завтра в 15:00 ждём вас", t, wh_type="outgoing_message_phone", from_me=True)
    b.post(ev)
    ex = b.settle(1, 40)
    first = (ex[0]["outcome"] if ex else None, history(b.pid, b.chat), memory(b.pid, b.chat))
    b.mark()
    b.post(ev)
    ex2 = b.settle(1, 40)
    # T6 (Q1 B): the history keeps the text as sent, the memory row opens with the owner mark (the model reads content only)
    ok = first[0] == "recorded_owner_phone" and first[1] == "owner_phone:Да, завтра в 15:00 ждём вас" and first[2] == "ai/owner_phone:Владелец: Да, завтра в 15:00 ждём вас" \
        and ex2 and ex2[0]["outcome"] == "outgoing_known" and memory_count(b.pid, b.chat) == 1 and len(b.sends()) == 0
    return ok, f"first={first} redelivery={ex2[0]['outcome'] if ex2 else None} memory_rows={memory_count(b.pid, b.chat)}"


@scenario("outgoing_api_history_only", "A6: an API-sent message (unknown writer) is a history row only — never a memory row (the AI's / the app's own writers own memory)")
def outgoing_api(ctx):
    b = Bot(ctx, "wa", "oa")
    b.post(b.event(f"api-{ctx.run_id}", "Текст через API", int(time.time()), wh_type="outgoing_message_api", from_me=True))
    ex = b.settle(1, 40)
    ok = ex and ex[0]["outcome"] == "recorded_outgoing_api" and history(b.pid, b.chat) == "outgoing_api:Текст через API" and memory_count(b.pid, b.chat) == 0
    return ok, f"outcome={ex[0]['outcome'] if ex else None} history={history(b.pid, b.chat)} memory_rows={memory_count(b.pid, b.chat)}"


@scenario("outgoing_foreign_profile_filtered", "A1/A6: an outgoing event naming ANOTHER profile is filtered before any write")
def outgoing_foreign(ctx):
    b = Bot(ctx, "wa", "of")
    b.post(b.event(f"for-{ctx.run_id}", "Чужое", int(time.time()), wh_type="outgoing_message_phone", from_me=True, profile="someone-else"))
    ex = b.settle(1, 40)
    ok = ex and ex[0]["outcome"] == "filtered" and not history(b.pid, b.chat) and memory_count(b.pid, b.chat) == 0
    return ok, f"outcome={ex[0]['outcome'] if ex else None} history={history(b.pid, b.chat)!r}"


@for_channels("suppressed_incoming_recorded", "A6: in «Вместе» the client's message is still recorded (history + memory human row), once, with no generation")
def suppressed_recorded(ctx, ch):
    b = Bot(ctx, ch, "sr")
    fixture("set-mode", "--profile-id", b.pid, "--suppressed", "true")
    ev = b.event(f"sup-{ctx.run_id}", "Можно завтра к 15?", int(time.time()))
    b.post(ev)
    b.post(ev)
    ex = b.settle(2, 40)
    outs = sorted(e["outcome"] or "?" for e in ex)
    ok = outs == ["suppressed", "suppressed"] and history(b.pid, b.chat) == "client:Можно завтра к 15?" and memory(b.pid, b.chat) == "human/client:Можно завтра к 15?" \
        and len(b.sends()) == 0 and not any(e2 for e in ex for e2 in [1] if "AI Agent" in ((e["data"].get("data") or {}).get("resultData", {}).get("runData", {})))
    return ok, f"outcomes={outs} history={history(b.pid, b.chat)} memory={memory(b.pid, b.chat)} sends={len(b.sends())}"


@scenario("auto_reply_records_client_and_ai_rows", "A6: a normal auto-reply leaves exactly client + ai history rows and the memory node's own human + ai rows (no doubles)")
def auto_rows(ctx):
    b = Bot(ctx, "wa", "ar")
    t = int(time.time())
    b.history_seed([{"id": f"ar-{ctx.run_id}", "body": "Сколько стоит букет?", "time": t, "fromMe": False, "type": "chat"}])
    b.post(b.event(f"ar-{ctx.run_id}", "Сколько стоит букет?", t))
    ex = b.settle(1)
    h = history(b.pid, b.chat)
    kinds = psql(f"select string_agg(source, ',' order by id) from chat_history_events where profile_id = '{b.pid}' and chat_id = '{b.chat}';")
    mem = psql(f"select string_agg((message->>'type') || '/' || coalesce(message->'additional_kwargs'->>'source', 'agent'), ',' order by id) from n8n_chat_histories where session_id = '{b.pid}:{b.chat}';")
    ok = ex and ex[0]["outcome"] == "sent" and kinds == "client,ai" and mem == "human/agent,ai/agent" and len(b.sends()) == 1
    return ok, f"outcome={ex[0]['outcome'] if ex else None} history_sources={kinds} memory={mem} sends={len(b.sends())}"


@scenario("bot_knows_owner_agreement_after_auto_resumes [llm]", "A6 acceptance: owner agreed by phone during «Вместе»; after the explicit return to «Авто» the bot answers with that agreement")
def agreement(ctx):
    b = Bot(ctx, "wa", "ag")
    t = int(time.time())
    fixture("set-mode", "--profile-id", b.pid, "--suppressed", "true")
    b.post(b.event(f"ag1-{ctx.run_id}", "Здравствуйте, можно к вам завтра подъехать?", t))
    b.settle(1, 40)
    b.mark()
    b.post(b.event(f"ag2-{ctx.run_id}", "Да, конечно. Завтра в 15:00 вас ждём, спросите Марину.", t + 5, wh_type="outgoing_message_phone", from_me=True))
    b.settle(1, 40)
    fixture("set-mode", "--profile-id", b.pid, "--suppressed", "false")
    b.mark()
    b.history_seed([{"id": f"ag1-{ctx.run_id}", "body": "Здравствуйте, можно к вам завтра подъехать?", "time": t, "fromMe": False, "type": "chat"},
                    {"id": f"ag2-{ctx.run_id}", "body": "Да, конечно. Завтра в 15:00 вас ждём, спросите Марину.", "time": t + 5, "fromMe": True, "type": "chat"},
                    {"id": f"ag3-{ctx.run_id}", "body": "Напомните, во сколько мне подойти и кого спросить?", "time": t + 10, "fromMe": False, "type": "chat"}])
    b.post(b.event(f"ag3-{ctx.run_id}", "Напомните, во сколько мне подойти и кого спросить?", t + 10))
    ex = b.settle(1)
    sent = [c["body"].get("body", "") for c in b.sends()]
    text = sent[0] if sent else ""
    ok = ex and ex[0]["outcome"] == "sent" and "15" in text and "Марин" in text
    return ok, f"outcome={ex[0]['outcome'] if ex else None} reply={text[:120]!r} memory={memory(b.pid, b.chat)}"


@scenario("handoff_tool_creates_request_once_and_pauses [llm]", "A5 acceptance: the agent CREATES the request through the tool (once, even on re-delivery), the chat is paused, and only the owner's explicit «Авто» resumes it")
def handoff_tool(ctx):
    b = Bot(ctx, "wa", "ht")
    owner = owner_for(ctx, b)
    t = int(time.time())
    text = "Хочу заказать букет из 25 роз на завтра к 10 утра. Меня зовут Айгерим, телефон 87011234567 — передайте, пожалуйста, чтобы со мной связались."
    b.history_seed([{"id": f"ht-{ctx.run_id}", "body": text, "time": t, "fromMe": False, "type": "chat"}])
    ev = b.event(f"ht-{ctx.run_id}", text, t)
    b.post(ev)
    ex = b.settle(1)
    reply = (b.sends()[0]["body"].get("body", "") if b.sends() else "")
    row = handoffs(b.pid, b.chat)
    created = bool(row) and row.count("|") == 0
    said_handoff = bool(re.search(r"перед|свяж", reply, re.I))
    pause1 = paused(b.pid, b.chat)
    # re-delivery: no second request
    b.mark()
    b.post(ev)
    b.settle(1, 40)
    once = handoffs(b.pid, b.chat).count("|") == 0 and bool(handoffs(b.pid, b.chat))
    # a new incoming while paused: no auto-reply
    b.mark()
    b.post(b.event(f"ht2-{ctx.run_id}", "Ещё вопрос: доставка есть?", t + 20))
    ex2 = b.settle(1, 60)
    paused_outcome = ex2[0]["outcome"] if ex2 else None
    # the owner's explicit «Авто» for THIS chat resumes + closes the request
    st, d = e2e_auth.hook("SetReplyMode", owner.token, {"profileIds": [b.pid], "chatId": b.chat, "suppressed": False, "version": int(time.time() * 1000)})
    pause2 = paused(b.pid, b.chat)
    closed = handoffs(b.pid, b.chat)
    b.mark()
    b.history_seed([{"id": f"ht3-{ctx.run_id}", "body": "Спасибо!", "time": t + 40, "fromMe": False, "type": "chat"}])
    b.post(b.event(f"ht3-{ctx.run_id}", "Спасибо!", t + 40))
    ex3 = b.settle(1)
    ok = created and said_handoff and pause1 == "t" and once and paused_outcome == "suppressed" and st == 200 and pause2 == "f" \
        and ":done:" in closed and ex3 and ex3[0]["outcome"] == "sent"
    return ok, (f"request={row} reply={reply[:90]!r} paused={pause1} once={once} paused_incoming={paused_outcome} "
                f"setmode={st} unpaused={pause2 == 'f'} closed={closed} resumed={ex3[0]['outcome'] if ex3 else None}")


@scenario("notifier_delivers_self_message_and_survives_failure", "A5 acceptance: the owner notice is a self-message on the bot's channel (no client text); a failed delivery keeps the request and is retried")
def notifier(ctx):
    b = Dedicated(ctx, "nt")            # the sweep serves every profile's due rows: the fault and the count are THIS profile's
    mock("POST", "/__mock/profile", {"profile_id": b.pid, "authorized": True, "phone": "77010009999"})
    chat2 = b.chat.replace("@c.us", "") + "1@c.us"
    psql(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary, data) values ('{b.pid}', '{b.chat}', 'nt1-{ctx.run_id}', 'order', 'Секретный заказ клиента', '{{\"phone\": \"87011234567\"}}');")
    psql(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary) values ('{b.pid}', '{chat2}', 'nt2-{ctx.run_id}', 'question', 'Вопрос');")
    # consumed by the FIRST send of the sweep FOR THIS PROFILE (rows are served oldest first → nt1)
    mock("POST", "/__mock/fault", {"match": "message/send", "mode": "status", "status": 500, "profile_id": b.pid})
    since = mock_total()
    deadline = time.time() + 170
    states = ""
    while time.time() < deadline:
        states = psql(f"select string_agg(notify_state || '/' || notify_attempts, ',' order by id) from handoff_requests where profile_id = '{b.pid}' and source_message_id like 'nt%-{ctx.run_id}';")
        if states and "pending" not in states:
            break
        time.sleep(5)
    st, d = mock("GET", f"/__mock/calls?since={since}&path=message/send")
    everything = d["calls"]
    calls = [c for c in everything if (c.get("query") or {}).get("profile_id") == b.pid]
    bodies = [c["body"].get("body", "") for c in calls]
    to_self = all(c["body"].get("recipient") == "77010009999" for c in calls)
    # the leak check reads EVERY notice the sweep sent in this window, not only this profile's
    no_leak = all("Секретный" not in x and "87011234567" not in x and b.chat.replace("@c.us", "") not in x
                  for x in (c["body"].get("body", "") for c in everything))
    intact = psql(f"select count(*) from handoff_requests where profile_id = '{b.pid}' and status = 'open' and source_message_id like 'nt%-{ctx.run_id}';") == "2"
    ok = states == "failed/1,sent/1" and len(calls) == 2 and to_self and no_leak and all(x.startswith("Choose Reply:") for x in bodies) and intact
    return ok, f"states={states} send_calls={len(calls)} to_self={to_self} no_leak={no_leak} requests_intact={intact} body={bodies[-1][:80] if bodies else None!r}"


def _draft(pid, chat, run, event, text):
    psql(f"insert into n8n_chat_histories (session_id, message) values ('{pid}:{chat}', jsonb_build_object('type', 'ai', 'content', '{text}', "
         f"'additional_kwargs', jsonb_build_object('execution_id', '{run}', 'event_id', '{event}', 'delivery', 'draft'), 'response_metadata', '{{}}'::jsonb));")


def _drafts(pid, chat):
    return psql(f"select coalesce(string_agg((message->'additional_kwargs'->>'execution_id') || '/' || (message->'additional_kwargs'->>'delivery') || '/' "
                f"|| (message->>'content'), ' | ' order by id), '') from n8n_chat_histories where session_id = '{pid}:{chat}';")


@scenario("lost_send_settled_by_notifier", "R9: a reply left send_pending > 10 min (its run died between the lease and Record Send) becomes send_unknown on the notifier's schedule — no transport call; a live one is untouched; T7: the lost run's memory draft becomes unknown + «[доставка не подтверждена] », a dead claim's draft is removed, a live run's draft stays")
def lost_send(ctx):
    b = Dedicated(ctx, "ls")
    lost, live, dead = f"ls-lost-{ctx.run_id}", f"ls-live-{ctx.run_id}", f"ls-dead-{ctx.run_id}"
    psql(f"insert into reply_events (profile_id, chat_id, message_id, execution_id, state, updated_at) values "
         f"('{b.pid}', '{b.chat}', '{lost}', 'dead-run', 'send_pending', now() - interval '11 minutes'), "
         f"('{b.pid}', '{b.chat}', '{live}', 'live-run', 'send_pending', now() - interval '2 minutes'), "
         f"('{b.pid}', '{b.chat}', '{dead}', 'dead-claim', 'claimed', now() - interval '11 minutes');")
    _draft(b.pid, b.chat, "dead-run", lost, "Букет — 25 000 ₸.")          # T7: the drafts Stamp Draft would have tied to these runs
    _draft(b.pid, b.chat, "live-run", live, "Доставка — 1000 ₸.")
    _draft(b.pid, b.chat, "dead-claim", dead, "Черновик умершего прогона.")
    since = mock_total()
    deadline = time.time() + 170            # the schedule fires every 2 minutes
    row = ""
    while time.time() < deadline:
        row = psql(f"select state || '|' || coalesce(execution_id, '-') || '|' || coalesce(error, '-') from reply_events where profile_id = '{b.pid}' and message_id = '{lost}';")
        if not row.startswith("send_pending"):
            break
        time.sleep(5)
    live_state = psql(f"select state from reply_events where profile_id = '{b.pid}' and message_id = '{live}';")
    st, d = mock("GET", f"/__mock/calls?since={since}&path=message/send")
    mine = [c for c in d["calls"] if (c.get("query") or {}).get("profile_id") == b.pid]
    drafts = _drafts(b.pid, b.chat)
    ok = row.startswith("send_unknown|dead-run|run_lost") and live_state == "send_pending" and not mine \
        and drafts == "dead-run/unknown/[доставка не подтверждена] Букет — 25 000 ₸. | live-run/draft/Доставка — 1000 ₸."
    return ok, f"lost={row} live={live_state} send_calls_for_profile={len(mine)} memory={drafts}"


@scenario("handoffs_webhook_owner_scoped_list_take_done", "A5: the app's list/take/done are token-only and owner-scoped; done lifts the chat's pause")
def api(ctx):
    b = Dedicated(ctx, "hw")            # pausedChats is profile-wide: only this scenario's chat may be paused on it
    owner = owner_for(ctx, b)
    psql(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary) values ('{b.pid}', '{b.chat}', 'hw-{ctx.run_id}', 'question', 'Есть ли доставка?');")
    psql(f"insert into chat_reply_state (profile_id, chat_id, handoff_paused) values ('{b.pid}', '{b.chat}', true) on conflict (profile_id, chat_id) do update set handoff_paused = true;")
    r = {}
    r["notoken"] = e2e_auth.hook("Handoffs", None, {"op": "list", "profileIds": [b.pid]})[0]
    other = e2e_auth.Owner("hx", ctx.run_id)
    r["foreign"] = e2e_auth.hook("Handoffs", other.token, {"op": "list", "profileIds": [b.pid]})[0]
    st, d = e2e_auth.hook("Handoffs", owner.token, {"op": "list", "profileIds": [b.pid]})
    reqs = (d or {}).get("requests") or []
    mine = [x for x in reqs if x["chatId"] == b.chat]
    r["list"] = (st, len(mine), mine[0]["status"] if mine else None, [pc["chatId"] for pc in (d or {}).get("pausedChats", [])] == [b.chat])
    hid = mine[0]["id"] if mine else 0
    st, d = e2e_auth.hook("Handoffs", owner.token, {"op": "take", "profileIds": [b.pid], "id": hid})
    r["take"] = (st, [x["status"] for x in (d or {}).get("requests", []) if x["id"] == hid])
    st, d = e2e_auth.hook("Handoffs", owner.token, {"op": "done", "profileIds": [b.pid], "id": hid})
    r["done"] = (st, [x for x in (d or {}).get("requests", []) if x["id"] == hid], paused(b.pid, b.chat), psql(f"select status from handoff_requests where id = {hid};"))
    ok = r["notoken"] == 401 and r["foreign"] == 403 and r["list"] == (200, 1, "open", True) and r["take"] == (200, ["taken"]) \
        and r["done"][0] == 200 and r["done"][1] == [] and r["done"][2] == "f" and r["done"][3] == "done"
    return ok, json.dumps(r, ensure_ascii=False)


@for_channels("stale_or_replayed_auto_keeps_newer_handoff", "R4: through the real SetReplyMode webhook, a replay of the owner's applied «Авто» and an older «Авто» leave a NEWER request open and the chat paused; a new explicit «Авто» resumes it")
def stale_auto(ctx, ch):
    b = Bot(ctx, ch, "r4")
    owner = owner_for(ctx, b)
    now = int(time.time() * 1000)
    body = lambda v: {"profileIds": [b.pid], "chatId": b.chat, "suppressed": False, "version": v}
    r = {"first": e2e_auth.hook("SetReplyMode", owner.token, body(now - 600_000))}          # the owner resumed this chat 10 min ago
    # the client asks for a human again: the request + pause exactly as Upsert Handoff writes them
    psql(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary) values ('{b.pid}', '{b.chat}', 'r4-{ctx.run_id}', 'question', 'Нужен менеджер');")
    psql(f"insert into chat_reply_state (profile_id, chat_id, handoff_paused) values ('{b.pid}', '{b.chat}', true) on conflict (profile_id, chat_id) do update set handoff_paused = true;")
    r["replay"] = e2e_auth.hook("SetReplyMode", owner.token, body(now - 600_000))          # the queue re-sends the same version
    r["after_replay"] = (paused(b.pid, b.chat), handoffs(b.pid, b.chat))
    r["older"] = e2e_auth.hook("SetReplyMode", owner.token, body(now - 1_200_000))         # a delayed older request
    r["after_older"] = (paused(b.pid, b.chat), handoffs(b.pid, b.chat))
    r["new"] = e2e_auth.hook("SetReplyMode", owner.token, body(int(time.time() * 1000) + 1000))   # the owner's new explicit «Авто»
    r["after_new"] = (paused(b.pid, b.chat), handoffs(b.pid, b.chat))
    applied = lambda x: [a.get("applied") for a in ((x[1] or {}).get("applied") or [])]
    ok = r["first"][0] == 200 and applied(r["first"]) == [True] \
        and r["replay"][0] == 200 and applied(r["replay"]) == [False] and r["after_replay"][0] == "t" and ":open:" in r["after_replay"][1] \
        and r["older"][0] == 200 and applied(r["older"]) == [False] and r["after_older"][0] == "t" and ":open:" in r["after_older"][1] \
        and r["new"][0] == 200 and applied(r["new"]) == [True] and r["after_new"][0] == "f" and ":done:" in r["after_new"][1]
    return ok, json.dumps({k: (v if k.startswith("after") else (v[0], applied(v))) for k, v in r.items()}, ensure_ascii=False)


@for_channels("fast_clock_queued_auto_keeps_newer_handoff", "R4-clock: through the real SetReplyMode webhook, an «Авто» decided 4 min BEFORE a request on a phone 5 min fast (queued, delivered after it) leaves the request open and the chat paused; an immediate «Авто» from the same phone resumes it")
def fast_clock_auto(ctx, ch):
    b = Bot(ctx, ch, "r4c")
    owner = owner_for(ctx, b)
    now = int(time.time() * 1000)
    fast = 300_000                                                                          # the phone's clock runs 5 min ahead
    body = lambda v, age: {"profileIds": [b.pid], "chatId": b.chat, "suppressed": False, "version": v, "decisionAgeMs": age}
    # the server is «Авто» in the gap (no row for the chat): the AI hands over 2 min ago, exactly as Upsert Handoff writes it
    psql(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary, created_at) values ('{b.pid}', '{b.chat}', 'r4c-{ctx.run_id}', 'question', 'Нужен менеджер', now() - interval '2 minutes');")
    psql(f"insert into chat_reply_state (profile_id, chat_id, handoff_paused) values ('{b.pid}', '{b.chat}', true) on conflict (profile_id, chat_id) do update set handoff_paused = true;")
    r = {"queued": e2e_auth.hook("SetReplyMode", owner.token, body(now - 240_000 + fast, 240_000))}   # tapped at real now−4m
    r["after_queued"] = (paused(b.pid, b.chat), handoffs(b.pid, b.chat))
    r["immediate"] = e2e_auth.hook("SetReplyMode", owner.token, body(int(time.time() * 1000) + fast, 0))   # the owner taps «Авто» now
    r["after_immediate"] = (paused(b.pid, b.chat), handoffs(b.pid, b.chat))
    applied = lambda x: [a.get("applied") for a in ((x[1] or {}).get("applied") or [])]
    ok = r["queued"][0] == 200 and applied(r["queued"]) == [True] and r["after_queued"][0] == "t" and ":open:" in r["after_queued"][1] \
        and r["immediate"][0] == 200 and applied(r["immediate"]) == [True] and r["after_immediate"][0] == "f" and ":done:" in r["after_immediate"][1]
    return ok, json.dumps({k: (v if k.startswith("after") else (v[0], applied(v))) for k, v in r.items()}, ensure_ascii=False)


@scenario("dashboard_shows_open_handoff_as_event","A5/A6: «Сводка» reports an open request as a REAL event (source=event) that outranks the classifier")
def dashboard(ctx):
    b = Bot(ctx, "wa", "db")
    owner = owner_for(ctx, b)
    psql(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary) values ('{b.pid}', '{b.chat}', 'db-{ctx.run_id}', 'order', 'Заказ: 25 роз');")
    st, d = e2e_auth.hook("DashboardOutcomes", owner.token, {"profileIds": [b.pid]})
    rows = [o for o in (d or {}).get("outcomes", []) if o.get("chatId") == b.chat]
    ok = st == 200 and len(rows) == 1 and rows[0].get("source") == "event" and rows[0].get("outcome") == "order_collected" and rows[0].get("handoffStatus") == "open" and rows[0].get("summary") == "Заказ: 25 роз"
    return ok, f"http={st} row={rows[0] if rows else None}"


@scenario("proxy_send_records_owner_app_row", "A6: a message the owner sends from the APP (proxy) lands as owner_app in history and as a marked memory row, after the response; T6: the memory row opens with «Владелец: »")
def proxy_send(ctx):
    b = Bot(ctx, "wa", "px")
    owner = owner_for(ctx, b)
    st, d = e2e_auth.proxy(owner.token, {"op": "message.send", "channel": "whatsapp", "profile_id": b.pid, "recipient": b.chat.replace("@c.us", ""), "body": "Спасибо, ждём вас"})
    time.sleep(4)
    # T6 (Q1 B): the history keeps the text as sent, the memory row opens with the owner mark
    ok = st == 200 and d.get("status") == "done" and history(b.pid, b.chat) == "owner_app:Спасибо, ждём вас" and memory(b.pid, b.chat) == "ai/owner_app:Владелец: Спасибо, ждём вас"
    return ok, f"http={st} body={d} history={history(b.pid, b.chat)} memory={memory(b.pid, b.chat)}"


@scenario("delete_bot_wipes_history_and_requests", "stage 4.7: deleting the bot wipes its history, requests, event ledger and chat state (billing counters untouched)")
def delete_bot(ctx):
    b = Bot(ctx, "wa", "dl")
    owner = owner_for(ctx, b)
    psql(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body) values ('{b.pid}', '{b.chat}', 'dl-{ctx.run_id}', 'client', 'x');")
    psql(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason) values ('{b.pid}', '{b.chat}', 'dl-{ctx.run_id}', 'question');")
    psql(f"insert into reply_events (profile_id, chat_id, message_id) values ('{b.pid}', '{b.chat}', 'dl-{ctx.run_id}');")
    psql(f"insert into chat_reply_state (profile_id, chat_id, handoff_paused) values ('{b.pid}', '{b.chat}', true) on conflict do nothing;")
    st, d = e2e_auth.hook("DeleteBotFiles", owner.token, {"botWaId": "-1", "botTgId": "-1", "waProfileId": b.pid, "tgProfileId": "-1", "appUserId": owner.app_user_id})
    left = psql(f"select (select count(*) from chat_history_events where profile_id = '{b.pid}') || '/' || (select count(*) from handoff_requests where profile_id = '{b.pid}') || '/' || (select count(*) from reply_events where profile_id = '{b.pid}') || '/' || (select count(*) from chat_reply_state where profile_id = '{b.pid}');")
    ok = st == 200 and (d or {}).get("success") is True and left == "0/0/0/0" and int((d or {}).get("deletedHistoryEvents", -1)) >= 1 and int((d or {}).get("deletedHandoffs", -1)) >= 1
    return ok, f"http={st} left={left} resp_counts={ {k: v for k, v in (d or {}).items() if k.startswith('deleted')} }"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for id_, title, _ in SCENARIOS:
            print(f"{id_:52} {title}")
        return
    st, _ = http("GET", f"{BASE}/healthz")
    if st != 200:
        sys.exit(f"staging n8n not reachable at {BASE}")
    ctx = Ctx()
    passed = failed = 0
    try:
        for id_, title, fn in SCENARIOS:
            if a.only and a.only not in id_:
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
        for o in e2e_auth.OWNERS:           # every install/subscriber owner_for() and the scenarios minted
            try:
                fixture(*(["delete", "--app-user-id", o.app_user_id] + [x for p in o.profiles for x in ("--profile-id", p)]))
            except Exception as e:
                print(f"   cleanup {o.tag} failed:", e)
        print(f"   cleanup: {len(e2e_auth.OWNERS)} owner fixture(s)")
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
