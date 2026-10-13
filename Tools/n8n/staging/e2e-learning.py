#!/usr/bin/env python3
"""STAGING scenarios for learning A (2026-09-25): origin meta on the proxy, the shown-set journal,
stage, deletion. Wappi = mock, Postgres = staging.

    python3 Tools/n8n/staging/e2e-learning.py
    python3 Tools/n8n/staging/e2e-learning.py --only origin
"""
import argparse
import importlib.util
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from e2e import (Ctx, http, node_output, BASE)  # noqa: E402


def _load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), os.path.join(HERE, name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


e2e_auth = _load("e2e-auth")
e2e_suggest = _load("e2e-suggest")
psql, payload, knowledge, m, call, owner_for = (e2e_suggest.psql, e2e_suggest.payload, e2e_suggest.knowledge,
                                                e2e_suggest.m, e2e_suggest.call, e2e_suggest.owner_for)
KEY = "0123456789abcdef0123456789abcdef"
ORIGIN = {"origin_kind": "card", "origin_index": 0, "origin_key": KEY, "origin_edit": 0, "origin_top1": 1, "origin_set": 3}
SCENARIOS = []


def require_migration():
    """Fail LOUDLY when the learning-A migration is missing: Record Set / Record Owner Send are fail-open
    Postgres nodes, so a missing table would silently turn every scenario into a false negative."""
    cols = psql("select count(*) from information_schema.columns where table_name = 'chat_history_events' and column_name like 'origin_%';")
    tbl = psql("select to_regclass('public.suggestion_sets') is not null;")
    if cols != "6" or tbl != "t":
        sys.exit(f"learning-A migration not applied on staging (origin cols={cols}, suggestion_sets={tbl}) — run Tools/n8n/staging/pg.sh migrate")


def owner_and_profile(ctx, status="trialing", channel="whatsapp"):
    """Every Owner registers itself in e2e_auth.OWNERS (R2/V3) — the cleanup below reuses that list."""
    if channel == "telegram":
        owner = e2e_auth.Owner("sg", ctx.run_id)
        pid = owner.add_profile("telegram")
        owner.refresh()
    else:
        owner, pid = owner_for(ctx, status=status)
    return owner, pid


def scenario(id_, title):
    def deco(fn):
        SCENARIOS.append((id_, title, fn))
        return fn
    return deco


def send(owner, pid, extra=None):
    body = {"op": "message.send", "channel": "whatsapp", "profile_id": pid, "recipient": "77010001234", "body": "Здравствуйте! Есть в наличии."}
    body.update(extra or {})
    return e2e_auth.proxy(owner.token, body)


@scenario("origin_recorded_on_tg_reply", "A: Telegram message.reply carries origin + chat_id in meta → owner_app row has origin_* AND chat_id, memory session = pid:chat; Wappi body has neither")
def tg_reply(ctx):
    owner, pid = owner_and_profile(ctx, channel="telegram")
    _, since = e2e_auth.mock_calls(0, "message/reply")
    body = {"op": "message.reply", "channel": "telegram", "profile_id": pid, "chat_id": "123456", "body": "Да, есть.", "message_id": "m1"}
    body.update(ORIGIN)
    st, d = e2e_auth.proxy(owner.token, body)
    mid = (d or {}).get("message_id") or ""
    time.sleep(2)
    row = origin_row(pid, mid)
    chat = psql(f"select chat_id from chat_history_events where profile_id = '{pid}' and message_id = '{mid}';")
    mem = psql(f"select count(*) from n8n_chat_histories where session_id = '{pid}:123456' and message->'additional_kwargs'->>'message_id' = '{mid}';")
    calls, _ = e2e_auth.mock_calls(since, "message/reply")
    bodies = [json.dumps(c.get("body") or {}) for c in calls]
    ok = st == 200 and mid and row == f"card|0|{KEY}|0|1|3" and chat == "123456" and mem == "1" and bodies and all("origin_" not in b and "chat_id" not in b for b in bodies)
    return ok, f"http={st} row={row!r} chat_id={chat!r} memory_rows={mem} forwarded={bodies[:1]}"


def origin_row(pid, mid):
    return psql(f"select coalesce(origin_kind,'∅')||'|'||coalesce(origin_index::text,'∅')||'|'||coalesce(origin_key,'∅')||'|'||coalesce(origin_edit::text,'∅')||'|'||coalesce(origin_top1::text,'∅')||'|'||coalesce(origin_set::text,'∅') "
                f"from chat_history_events where profile_id = '{pid}' and message_id = '{mid}';")


@scenario("origin_recorded_on_app_send", "A: message.send with origin meta → owner_app row carries the six origin_* columns")
def origin_recorded(ctx):
    owner, pid = owner_and_profile(ctx)
    st, d = send(owner, pid, ORIGIN)
    mid = (d or {}).get("message_id") or ""
    time.sleep(2)
    row = origin_row(pid, mid)
    ok = st == 200 and mid and row == f"card|0|{KEY}|0|1|3"
    return ok, f"http={st} message_id={mid!r} row={row!r} (want card|0|{KEY}|0|1|3)"


@scenario("origin_absent_is_legacy_ok", "A: message.send WITHOUT origin (compat client) → 200 and all origin_* null")
def origin_absent(ctx):
    owner, pid = owner_and_profile(ctx)
    st, d = send(owner, pid)
    mid = (d or {}).get("message_id") or ""
    time.sleep(2)
    row = origin_row(pid, mid)
    ok = st == 200 and mid and row == "∅|∅|∅|∅|∅|∅"
    return ok, f"http={st} row={row!r}"


@scenario("origin_bogus_is_400_no_transport", "A: origin_kind outside the enum → 400 bad_param and NO Wappi call")
def origin_bogus(ctx):
    owner, pid = owner_and_profile(ctx)
    _, total0 = e2e_auth.mock_calls(0, "message/send")
    st, d = send(owner, pid, {"origin_kind": "bogus"})
    _, total1 = e2e_auth.mock_calls(0, "message/send")
    ok = st == 400 and (d or {}).get("error") == "bad_param" and total1 == total0
    return ok, f"http={st} body={d} mock_sends {total0}->{total1}"


@scenario("wappi_never_sees_origin", "A: the transport body forwarded to Wappi has no origin_* keys")
def wappi_clean(ctx):
    owner, pid = owner_and_profile(ctx)
    _, since = e2e_auth.mock_calls(0, "message/send")
    st, _ = send(owner, pid, ORIGIN)
    calls, _ = e2e_auth.mock_calls(since, "message/send")
    bodies = [json.dumps(c.get("body") or {}) for c in calls]
    ok = st == 200 and bodies and all("origin_" not in b for b in bodies)
    return ok, f"http={st} forwarded_bodies={bodies[:1]}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for id_, title, _ in SCENARIOS:
            print(f"{id_:44} {title}")
        return
    st, _ = http("GET", f"{BASE}/healthz")
    if st != 200:
        sys.exit(f"staging n8n not reachable at {BASE}")
    require_migration()
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
        for o in e2e_auth.OWNERS:   # identities made through e2e_auth.Owner are invisible to Ctx.cleanup() (same loop as e2e-auth.py)
            try:
                print(f"   cleanup {o.tag}:", e2e_auth.fixture(*(["delete", "--app-user-id", o.app_user_id] + [x for pid in o.profiles for x in ("--profile-id", pid)])))
            except Exception as e:
                print(f"   cleanup {o.tag} failed:", e)
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
