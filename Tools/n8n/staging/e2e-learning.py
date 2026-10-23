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


def send(owner, pid, extra=None, recipient="77010001234"):
    body = {"op": "message.send", "channel": "whatsapp", "profile_id": pid, "recipient": recipient, "body": "Здравствуйте! Есть в наличии."}
    body.update(extra or {})
    return e2e_auth.proxy(owner.token, body)


def readiness_of(owner, bot_key="Bot0"):
    """(http status, the bot's row of Get Usage `readiness` or None)."""
    st, d = e2e_auth.hook("GetUsage", owner.token, {"appUserId": owner.app_user_id})
    rows = d.get("readiness") if isinstance(d, dict) else None
    return st, next((r for r in rows or [] if r.get("botKey") == bot_key), None)


def shows(row, **want):
    return row is not None and all(row.get(k) == v for k, v in want.items())


def wait_sends(pid, n, timeout=30):
    """Record Owner Send runs AFTER the proxy's Respond, so a history row lands a moment after the send call returned:
    wait until the profile holds n owner_app rows (polling, not a fixed sleep). True when it does."""
    end = time.time() + timeout
    while time.time() < end:
        if int(psql(f"select count(*) from chat_history_events where profile_id = '{pid}' and source = 'owner_app';")) >= n:
            return True
        time.sleep(0.5)
    return False


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


@scenario("set_recorded_after_respond", "A: a successful panel answer writes ONE suggestion_sets row (after Respond, same request_key, stage learning)")
def set_recorded(ctx):
    owner, pid = owner_and_profile(ctx)
    know = knowledge([{"kind": "product", "name": "Свеча зажигания", "price": "1500 тг", "description": ""}])
    body = payload(owner, pid, [m("client", "свечи есть? сколько стоят?")], know=know, request_key=KEY, bot_wa="-1")
    st, d, ex = call(owner, body)
    cards = (d or {}).get("suggestions") or []
    time.sleep(2)
    row = psql(f"select stage||'|'||jsonb_array_length(items)||'|'||coalesce(prompt_version,'∅')||'|'||(usage is not null)::text from suggestion_sets where profile_id = '{pid}' and request_key = '{KEY}';")
    gd = node_output(ex, "Gate Decision") or {}
    ok = st == 200 and cards and row == f"learning|{len(cards)}|A0-2026-09-25|true" and gd.get("stage") == "learning"
    return ok, f"http={st} cards={len(cards)} row={row!r} gate_stage={gd.get('stage')!r}"


@scenario("set_not_recorded_on_refusal", "A: a refused call (expired subscriber → error envelope) writes NO set")
def set_not_recorded(ctx):
    owner, pid = owner_and_profile(ctx, status="expired")
    body = payload(owner, pid, [m("client", "свечи есть?")], know=knowledge([]), request_key="fedcba9876543210fedcba9876543210", bot_wa="-1")
    st, d, _ = call(owner, body)
    time.sleep(2)
    n = psql(f"select count(*) from suggestion_sets where profile_id = '{pid}';")
    ok = (d or {}).get("error") and n == "0"
    return ok, f"http={st} error={(d or {}).get('error')!r} sets={n}"


@scenario("set_not_recorded_on_abstain", "A: an abstain (personal message) writes NO set — inconclusive (not FAIL) when the model produced cards anyway")
def set_not_recorded_abstain(ctx):
    owner, pid = owner_and_profile(ctx)
    body = payload(owner, pid, [m("client", "поехали на рыбалку в выходные?")], know=knowledge([]), request_key="00000000000000000000000000000003", bot_wa="-1")
    st, d, _ = call(owner, body)
    time.sleep(2)
    n = psql(f"select count(*) from suggestion_sets where profile_id = '{pid}';")
    if (d or {}).get("suggestions"):
        return True, f"INCONCLUSIVE: model returned {len(d['suggestions'])} cards (abstain flaky) — set written={n}"
    ok = st == 200 and (d or {}).get("abstain") is True and n == "0"
    return ok, f"http={st} abstain={(d or {}).get('abstain')} sets={n}"


@scenario("stage_trained_when_ten_picks", "A: pickStats summing to ≥ 10 → Gate Decision stage 'trained' and Assemble emits ПРЕДПОЧТЕНИЯ, not ХОЛОДНЫЙ СТАРТ")
def stage_trained(ctx):
    owner, pid = owner_and_profile(ctx)
    body = payload(owner, pid, [m("client", "свечи есть? сколько стоят?")], know=knowledge([{"kind": "product", "name": "Свеча зажигания", "price": "1500 тг", "description": ""}]), request_key="00000000000000000000000000000001", bot_wa="-1")
    body["pickStats"] = "Ответ:7,Уточнить:4"
    st, _, ex = call(owner, body)
    gd = node_output(ex, "Gate Decision") or {}
    sp = (node_output(ex, "Assemble") or {}).get("systemPrompt", "")
    ok = st == 200 and gd.get("stage") == "trained" and "ПРЕДПОЧТЕНИЯ ВЛАДЕЛЬЦА" in sp and "ХОЛОДНЫЙ СТАРТ" not in sp
    return ok, f"http={st} stage={gd.get('stage')!r} prefs={'ПРЕДПОЧТЕНИЯ ВЛАДЕЛЬЦА' in sp} cold={'ХОЛОДНЫЙ СТАРТ' in sp}"


@scenario("delete_bot_clears_sets", "A: DeleteBotFiles for the profile removes its suggestion_sets rows (history rows go with chat_history_events)")
def delete_clears(ctx):
    owner, pid = owner_and_profile(ctx)
    body = payload(owner, pid, [m("client", "свечи есть? сколько стоят?")], know=knowledge([{"kind": "product", "name": "Свеча", "price": "1500 тг", "description": ""}]), request_key=KEY)
    call(owner, body)
    time.sleep(2)
    n0 = psql(f"select count(*) from suggestion_sets where profile_id = '{pid}';")
    st, d = e2e_auth.hook("DeleteBotFiles", owner.token, {"botWaId": "-1", "botTgId": "-1", "waProfileId": pid, "tgProfileId": "-1", "appUserId": owner.app_user_id})
    time.sleep(3)
    n1 = psql(f"select count(*) from suggestion_sets where profile_id = '{pid}';")
    ok = n0 == "1" and st == 200 and (d or {}).get("success") is True and (d or {}).get("deletedSuggestionSets") == 1 and n1 == "0"
    return ok, f"sets before/after = {n0}/{n1}, delete http={st} body={d}"


@scenario("readiness_in_get_usage", "B + progress percent: 11 owner sends with a shown set, card #1 matched, over 3 personal chats on 2 days → Get Usage readiness bestMatches 11, progress 69, ready=false; a group send never counts; the 12th match → bestMatches 12, progress 75, ready=true, unlockAt 70")
def readiness_in_get_usage(ctx):
    owner, pid = owner_and_profile(ctx)
    psql(f"update bot_profiles set bot_key = 'Bot0' where profile_id = '{pid}';")   # a real bot gets it from Create's Register Profile

    def bot0():
        return readiness_of(owner)

    chats = ["77010001001", "77010001002", "77010001003"]
    for i in range(11):
        send(owner, pid, dict(ORIGIN, origin_top1=1), recipient=chats[i % 3])
    time.sleep(2)                                           # Record Owner Send runs after the proxy's Respond
    psql(f"update chat_history_events set created_at = created_at - interval '1 day' where profile_id = '{pid}' and chat_id like '77010001001%';")
    st1, eleven = bot0()
    send(owner, pid, dict(ORIGIN, origin_top1=1), recipient="120363000000@g.us")   # a group: excluded from the history
    time.sleep(2)
    st2, after_group = bot0()
    send(owner, pid, dict(ORIGIN, origin_top1=1), recipient=chats[1])
    time.sleep(2)
    st3, twelve = bot0()
    st_legacy, legacy = e2e_auth.hook("GetUsage", None, {"appUserId": owner.app_user_id})   # compat window: token-less → []
    legacy_ok = st_legacy == 200 and isinstance(legacy, dict) and legacy.get("readiness") == [] and legacy.get("success") is True
    ok = (legacy_ok
          and st1 == 200 and shows(eleven, total=11, matches=11, bestMatches=11, progress=69, ready=False, capped=False, needChats=0, needDays=0)
          and st2 == 200 and shows(after_group, total=11, bestMatches=11, progress=69, ready=False)
          and st3 == 200 and shows(twelve, total=12, matches=12, chats=3, days=2, bestMatches=12, progress=75, ready=True, unlockAt=70, capped=False))
    return ok, f"eleven={eleven} after_group={after_group} twelve={twelve} legacy={st_legacy}/{legacy.get('readiness') if isinstance(legacy, dict) else legacy}"


@scenario("readiness_capped_in_one_chat", "progress percent (spec §3): 12 owner sends with a shown set, card #1 matched, ALL in ONE personal chat (spread over 2 days) → readiness bestMatches 12, progress 69, ready=false, capped=true, needChats 2, needDays 0; a 2nd chat → needChats 1 (still capped, 69); a 3rd chat lifts the cap → ready=true, progress 88, capped=false")
def readiness_capped_in_one_chat(ctx):
    owner, pid = owner_and_profile(ctx)
    psql(f"update bot_profiles set bot_key = 'Bot0' where profile_id = '{pid}';")   # a real bot gets it from Create's Register Profile
    for _ in range(12):
        send(owner, pid, ORIGIN, recipient="77010002001")                          # every send: card #1 matched, ONE personal chat
    seeded1 = wait_sends(pid, 12)
    # two days, so only the CHATS condition stays open (needDays = 0): the first six sends move to yesterday
    psql(f"update chat_history_events set created_at = created_at - interval '1 day' where id in "
         f"(select id from chat_history_events where profile_id = '{pid}' and source = 'owner_app' order by id limit 6);")
    st1, one_chat = readiness_of(owner)
    send(owner, pid, ORIGIN, recipient="77010002002")
    seeded2 = wait_sends(pid, 13)
    st2, two_chats = readiness_of(owner)
    send(owner, pid, ORIGIN, recipient="77010002003")
    seeded3 = wait_sends(pid, 14)
    st3, three_chats = readiness_of(owner)
    ok = (seeded1 and seeded2 and seeded3
          and st1 == 200 and shows(one_chat, total=12, matches=12, chats=1, bestMatches=12, progress=69, ready=False, unlockAt=70, capped=True, needChats=2, needDays=0)
          and st2 == 200 and shows(two_chats, total=13, bestMatches=13, chats=2, progress=69, ready=False, capped=True, needChats=1, needDays=0)
          and st3 == 200 and shows(three_chats, total=14, bestMatches=14, chats=3, progress=88, ready=True, unlockAt=70, capped=False, needChats=0, needDays=0))
    return ok, f"rows_seeded={seeded1}/{seeded2}/{seeded3} one_chat={one_chat} two_chats={two_chats} three_chats={three_chats}"


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
