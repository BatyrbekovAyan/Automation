#!/usr/bin/env python3
"""STAGING scenarios for audit stage 5 (S1, S3–S8) — the Suggest Replies workflow on the private
n8n with A1 session tokens: the knowledge contract, subject recovery + exact match, the idempotent
daily count, the server clock, and the error kinds an authenticated caller gets.

    python3 Tools/n8n/staging/e2e-suggest.py
    python3 Tools/n8n/staging/e2e-suggest.py --only exact

Scenarios tagged [llm] call the real model and assert loosely on content.
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
from e2e import (Ctx, api, execution, fixture, http, latest_executions, node_output, wait_executions_settle, BASE)  # noqa: E402
_spec = importlib.util.spec_from_file_location("e2e_auth", os.path.join(HERE, "e2e-auth.py"))
e2e_auth = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(e2e_auth)

SUGGEST_WF = "9PTyYcelRQI7bGDb"
STG_WA = "stgwab77f282533c"   # the derived staging WA bot's workflow id (RAG scope key)


def psql(sql):
    out = subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", sql], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


def knowledge(items, contacts=None, description="", timezone="Asia/Almaty", documents=0):
    return {"v": 1, "version": "v-" + str(int(time.time())), "timezone": timezone, "tzSource": "bot" if timezone else "none",
            "description": description, "contacts": contacts or {}, "items": items, "documents": documents}


def payload(owner, pid, msgs, know=None, catalog="", request_key=None, steer=None, last=None, bot_wa=STG_WA, vertical="auto_parts"):
    return {"v": 1, "requestSeq": 1, "profileId": pid, "chatId": "77010001234@c.us", "botWaId": bot_wa, "botTgId": "-1", "channel": "whatsapp",
            "businessTypeId": vertical, "businessName": "Запчасти Астана", "ownerPrompt": "", "catalog": catalog,
            "steerTowardText": steer, "lastIncomingText": last, "messages": msgs, "businessKnowledge": "", "now": "1999-01-01 00:00, среда",
            "pickStats": "", "appUserId": owner.app_user_id, "knowledge": know, "requestKey": request_key}


def m(role, text, ts=1754800000):
    return {"role": role, "text": text, "ts": ts}


def call(owner, body, timeout=60):
    since = max([int(e["id"]) for e in latest_executions(SUGGEST_WF, 0, 50)] or [0])
    st, d = e2e_auth.hook("SuggestReplies", owner.token, body) if timeout == 60 else http("POST", f"{BASE}/webhook/SuggestReplies", body, headers={"Authorization": f"Bearer {owner.token}"}, timeout=timeout)
    rows = wait_executions_settle(SUGGEST_WF, since, 1, 90)
    d_exec = execution(rows[-1]["id"]) if rows else {}
    return st, d, d_exec


def owner_for(ctx, status="trialing"):
    o = e2e_auth.Owner("sg", ctx.run_id)
    if status != "trialing":
        psql(f"update subscribers set status = '{status}' where app_user_id = '{o.app_user_id}';")
    pid = o.add_profile("whatsapp")
    psql(f"update bot_profiles set workflow_id = '{STG_WA}' where profile_id = '{pid}';")
    o.refresh()
    return o, pid


SCENARIOS = []


def scenario(id_, title):
    def deco(fn):
        SCENARIOS.append((id_, title, fn))
        return fn
    return deco


@scenario("knowledge_budget_and_server_time_reach_the_prompt", "S1/S2/S7: the composed prompt carries the item DESCRIPTION, the contacts after a long description, the server clock in the bot's zone — and never the phone's clock")
def knowledge_budget(ctx):
    owner, pid = owner_for(ctx)
    long_desc = "Мы продаём запчасти для японских авто с 2009 года. " * 60
    know = knowledge([{"kind": "product", "name": "Фильтр масляный 90915-YZZE1", "price": "3200 тг", "description": "Оригинал Toyota, цена без установки"}],
                     {"phone": "+7 777 000 11 22", "hours": "9–20", "address": "ул. Абая 1"}, long_desc, "Asia/Almaty", 1)
    st, d, ex = call(owner, payload(owner, pid, [m("client", "какой адрес и до скольки работаете?")], know=know))
    prep = node_output(ex, "Prep") or {}
    asm = node_output(ex, "Assemble") or {}
    fenced = asm.get("fencedData", "")
    sysp = asm.get("systemPrompt", "")
    ok = (st == 200 and "Оригинал Toyota" in fenced and "Адрес: ул. Абая 1" in fenced and "1999-01-01" not in fenced
          and prep.get("tzSource") == "bot" and re.search(r"\b20\d\d-\d\d-\d\d \d\d:\d\d, ", str(prep.get("now")))
          and "ОПИСАНИЕ БИЗНЕСА СОКРАЩЕНО" in sysp and "серверные часы" in sysp and "description_truncated" in (prep.get("knowledgeFlags") or []))
    return ok, f"http={st} tz={prep.get('tzSource')} now={prep.get('now')!r} flags={prep.get('knowledgeFlags')} desc_in_fenced={'Оригинал Toyota' in fenced} addr_in_fenced={'Адрес: ул. Абая 1' in fenced} phone_clock_leaked={'1999' in fenced}"


@scenario("short_follow_up_recovers_subject_and_exact_match [llm]", "S4: «а сколько стоит?» after the exact model — the retrieval query carries the subject, the article is matched EXACTLY in the price list, the cards quote that price")
def subject_exact(ctx):
    owner, pid = owner_for(ctx)
    psql(f"insert into documents (content, metadata) values ('Фильтр масляный 90915-YZZE1 Toyota Camry 2019 — 3200 тг, оригинал', '{{\"botWaId\": \"{STG_WA}\", \"fileId\": \"e2e-{ctx.run_id}\"}}'::jsonb);")
    psql(f"insert into documents (content, metadata) values ('Фильтр масляный 90915-10004 Toyota Camry 2007 — 2100 тг, аналог', '{{\"botWaId\": \"{STG_WA}\", \"fileId\": \"e2e-{ctx.run_id}\"}}'::jsonb);")
    try:
        msgs = [m("client", "Нужен масляный фильтр 90915-YZZE1 на Camry 2019", 1), m("business", "Есть, оригинал.", 2), m("client", "а сколько стоит?", 3)]
        st, d, ex = call(owner, payload(owner, pid, msgs, know=knowledge([], {}, "Запчасти для Toyota", "Asia/Almaty", 1)))
        prep = node_output(ex, "Prep") or {}
        asm = node_output(ex, "Assemble") or {}
        exact = node_output(ex, "Exact Match") or {}
        cards = (d or {}).get("suggestions") or []
        fenced = asm.get("fencedData", "")
        ok = (st == 200 and "Camry 2019" in str(prep.get("queryText")) and "90915-YZZE1" in (prep.get("exactTokens") or [])
              and "90915-YZZE1" in str(exact.get("content", "")) and "3200" in fenced and any("3200" in c.get("text", "") for c in cards))
        return ok, f"http={st} query={str(prep.get('queryText'))[:80]!r} tokens={prep.get('exactTokens')} exact_row={'90915-YZZE1' in str(exact.get('content',''))} cards={[c.get('text','')[:50] for c in cards]}"
    finally:
        psql(f"delete from documents where metadata->>'fileId' = 'e2e-{ctx.run_id}';")


@scenario("same_request_key_is_counted_once", "S3: a retry with the SAME request key (timeout / re-open) does not increment the daily counter; a new key does")
def idempotent_count(ctx):
    owner, pid = owner_for(ctx)
    know = knowledge([{"kind": "product", "name": "Свеча зажигания", "price": "1500 тг", "description": ""}])
    body = payload(owner, pid, [m("client", "свечи есть?")], know=know, request_key="0123456789abcdef0123456789abcdef", bot_wa="-1")
    call(owner, body)
    n1 = psql(f"select coalesce(sum(n), 0) from suggestion_counts where app_user_id = '{owner.app_user_id}';")
    call(owner, body)
    n2 = psql(f"select coalesce(sum(n), 0) from suggestion_counts where app_user_id = '{owner.app_user_id}';")
    body["requestKey"] = "fedcba9876543210fedcba9876543210"
    call(owner, body)
    n3 = psql(f"select coalesce(sum(n), 0) from suggestion_counts where app_user_id = '{owner.app_user_id}';")
    keys = psql(f"select count(*) from suggestion_request_keys where app_user_id = '{owner.app_user_id}';")
    ok = int(n2) == int(n1) and int(n3) == int(n1) + 1 and keys == "2"
    return ok, f"counts after 1st/retry/new = {n1}/{n2}/{n3} (retry must not add, a new key adds 1), keys={keys}"


@scenario("authenticated_refusals_are_named", "S8: an expired subscription and an unknown account answer with their KIND for a token-bearing caller; a bad body answers bad_request")
def refusals(ctx):
    owner, pid = owner_for(ctx, status="expired")
    st1, d1, _ = call(owner, payload(owner, pid, [m("client", "цена?")], bot_wa="-1"))
    other = e2e_auth.Owner("sx", ctx.run_id)
    psql(f"delete from subscribers where app_user_id = '{other.app_user_id}';")
    st2, d2, _ = call(other, payload(other, "-1", [m("client", "цена?")], bot_wa="-1"))
    st3, d3, _ = call(owner, {"v": 1, "appUserId": owner.app_user_id})
    ok = (st1 == 200 and d1.get("error") == "subscription" and st2 == 200 and d2.get("error") == "unknown_account" and st3 == 200 and d3.get("error") == "bad_request")
    return ok, f"expired={d1.get('error')} unknown={d2.get('error')} bad={d3.get('error')}"


@scenario("legacy_caller_keeps_opaque_error", "S8/N-4: a token-less legacy call (compat window) still gets only generation_failed — no account oracle")
def legacy_opaque(ctx):
    st, d = http("POST", f"{BASE}/webhook/SuggestReplies", {"v": 1, "requestSeq": 1, "profileId": "p", "chatId": "c", "botWaId": "-1", "botTgId": "-1", "channel": "whatsapp",
                                                            "businessTypeId": "flowers", "businessName": "x", "catalog": "", "messages": [m("client", "цена?")], "appUserId": f"probe-nope-{ctx.run_id}"})
    ok = st in (200, 401) and ((st == 200 and d.get("error") == "generation_failed") or st == 401)
    return ok, f"http={st} error={(d or {}).get('error') if isinstance(d, dict) else d}"


@scenario("execution_deadline_is_set", "S3: the workflow carries a 20-s server deadline shorter than the client's 22 s")
def deadline(ctx):
    wf = api(f"/workflows/{SUGGEST_WF}")[1]
    t = ((wf or {}).get("settings") or {}).get("executionTimeout")
    ok = t == 20
    return ok, f"executionTimeout={t} (client waits 22 s)"


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
