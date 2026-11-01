#!/usr/bin/env python3
"""ТОЛЬКО СТЕНД. Репетиция canary пакета «Личных и рабочих чатов» (README, шаг 9) — то, что на проде проверяет владелец
телефонами на тестовом ИИ-сотруднике, здесь сыграно запросами к стенду (n8n 2.36.7 http://localhost:5678, Wappi-мок,
Postgres pg.sh); запросы к любой другой базе не делаются (константа BASE ниже).

Главное, чего не проверяют сценарии e2e-audience: ИИ-сотрудник, СОЗДАННЫЙ ЧЕРЕЗ ПУБЛИЧНЫЙ API (Create WA/TG — как новые
боты новой сборки и клоны на проде), вызывает подпроцесс crAudienceDec001, импортированный CLI и опубликованный через API
(шаги 4а–4г пакета). На каждом канале:
  1. ИИ-сотрудник в «Авто» — Create через настоящий вебхук (клон создаёт и включает сам Create); его линия создана 2 часа
     назад (первые 30 минут позади); строки подключения нет — её создаст Decide Audience (как у «2» на проде: старая сборка
     о подключении не сообщает);
  2. пишет номер, которого никогда не было в контактах → в выполнении клона отработал Decide Audience (business), записано
     business / default / new, ИИ-сотрудник ответил один раз;
  3. пишет сохранённый контакт → personal / default / contact, ответа нет, о чате не хранится ничего;
  4. панель подсказок для личного чата — отказ personal_chat, модель не запускалась;
  5. list приложения (токен владельца этого ИИ-сотрудника) → 200 и ровно эти два решения + момент подключения.
Затем (без этого скрипта) — health.sql и canary.sql пакета через Postgres-узел n8n.

    python3 docs/audits/2026-10-25-personal-chats/stand-canary.py      # нужен окружению e2e-audience.py: pg.sh, мок, bootstrap
"""
import importlib.util
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
STAGING = os.path.join(REPO, "Tools", "n8n", "staging")


def _load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), os.path.join(STAGING, name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


ea = _load("e2e-audience")
if not ea.BASE.startswith("http://localhost:"):
    sys.exit(f"stand only — refusing base {ea.BASE}")
ex = ea.e2e_auth


def canary(ctx, ch):
    channel = ea.CHANNELS[ch][0]
    o, pid = ex.line_owner({"run_id": ctx.run_id}, f"cy{ch}", ch)
    key = f"Bot-cy{ch}-{ctx.run_id}"
    st, d, wid, _ = ex.line_create(o, ch, pid, key, mode="auto")
    out = {"create": (st, (d or {}).get("created") if isinstance(d, dict) else d, wid)}
    if not wid:
        return False, out
    try:
        ea.psql(f"update bot_profiles set created_at = now() - interval '2 hours' where profile_id = '{pid}';")
        ea.psql(f"delete from audience_connections where profile_id = '{pid}';")
        chat_type = ea.CHANNELS[ch][3]
        new, contact = ea.chat_id(ch, ctx, "cyn"), ea.chat_id(ch, ctx, "cyc")
        ea.mock("POST", "/__mock/audience", {"profile_id": pid, "clear_faults": True, "lid_rows": {},
                                              "dialogs": [ea.stranger(ch, new), ea.saved_contact(ch, contact)]})
        since_calls = ea.mock_total()
        since_exec = max([int(e["id"]) for e in ea.latest_executions(wid, 0, 50)] or [0])
        results = {}
        for label, chat, text, extra in (("new", new, "Здравствуйте! Сколько стоит букет из 25 роз?", {"contact_name": "Клиент"}),
                                         ("contact", contact, "Привет, ты дома?", {"contact_name": "Айгерим Сохранённая"})):
            mid, t = f"cy{label}-{ch}-{ctx.run_id}", int(time.time())
            ea.mock("POST", "/__mock/messages", {"profile_id": pid, "chat_id": chat, "messages": [ea.row(ch, chat, mid, False, text, t)]})
            fields = {"chat_type": chat_type, "is_bot": False, **(extra if ch == "wa" else {})}
            http_st, _ = ea.http("POST", f"{ea.BASE}/webhook/{pid}", ea.e2e_reply.event(pid, chat, mid, text, t, **fields), timeout=30)
            rows = ea.wait_executions_settle(wid, since_exec, 1, 180)
            last = ea.execution(rows[-1]["id"]) if rows else {}
            since_exec = max([since_exec] + [int(r["id"]) for r in rows])
            results[label] = {"webhook": http_st, "execution": rows[-1]["id"] if rows else None,
                              "outcome": (ea.node_output(last, "Outcome") or {}).get("outcome"),
                              "decide_ran": ea.ran(last, "Decide Audience"),
                              "decide": ea.node_output(last, "Decide Audience") if ea.ran(last, "Decide Audience") else None,
                              "decision": ea.decision(pid, chat), "sends": len(ea.sends_to(since_calls, pid, chat)),
                              "stored": ea.stored(pid, chat)}
        sst, sd, sex = ea.suggest(o, ch, pid, contact)
        panel = {"http": sst, "error": (sd or {}).get("error"), "cards": len((sd or {}).get("suggestions") or []),
                 "llm_ran": ea.ran(sex, "LLM"), "gate": (ea.node_output(sex, "Gate Decision") or {}).get("gateReason")}
        lst, by = ea.audience_list(o, pid)
        mine = by.get(pid) or {}
        listed = sorted((x["chatId"] == new and "new" or x["chatId"] == contact and "contact" or "other", x["kind"], x["source"], x["reason"])
                        for x in mine.get("decisions") or [])
        connection = ea.connection(pid)
    finally:
        ex.drop_clones(*o.profiles)
    n, c = results["new"], results["contact"]
    ok = (out["create"][0] == 200
          and n["outcome"] == "sent" and n["decide_ran"] and n["decide"] == {"audience": "business", "outcome": "business"}
          and n["decision"] == "business|default|new" and n["sends"] == 1
          and c["outcome"] == "personal_contact" and c["decide"] == {"audience": "personal", "outcome": "personal_contact"}
          and c["decision"] == "personal|default|contact" and c["sends"] == 0 and c["stored"] == ea.NOTHING
          and panel == {"http": 200, "error": "personal_chat", "cards": 0, "llm_ran": False, "gate": "personal_chat"}
          and lst == 200 and listed == [("contact", "personal", "default", "contact"), ("new", "business", "default", "new")]
          and connection == ("0", "-") and mine.get("connectedAt") is not None)
    return ok, {**out, "profile": pid, "new": {k: v for k, v in n.items() if k != "stored"}, "contact": c, "panel": panel,
                "list": (lst, listed, mine.get("connectedAt")), "connection(moment-created s, phone)": connection}


def main():
    st, _ = ea.http("GET", f"{ea.BASE}/healthz")
    if st != 200:
        sys.exit(f"staging n8n not reachable at {ea.BASE}")
    missing = ea.wait_ready()
    if missing:
        print(f"   not registered after the wait: {missing}", flush=True)
    ctx = ea.Ctx()
    ctx.identity()
    passed = failed = 0
    try:
        for ch in ("wa", "tg"):
            t0 = time.time()
            try:
                ok, detail = canary(ctx, ch)
            except Exception as e:     # noqa: BLE001 — reported as a failure, the cleanup below still runs
                ok, detail = False, f"EXCEPTION {e!r}"
            passed += ok
            failed += not ok
            print(f"{'PASS' if ok else 'FAIL'}  canary_api_clone[{ch}] ({time.time() - t0:.0f}s)", flush=True)
            print(f"      {json.dumps(detail, ensure_ascii=False, default=str)}", flush=True)
    finally:
        ctx.cleanup()
        for o in ex.OWNERS:
            try:
                ea.fixture(*(["delete", "--app-user-id", o.app_user_id] + [x for p in o.profiles for x in ("--profile-id", p)]))
            except Exception as e:      # noqa: BLE001
                print(f"   cleanup {o.tag} failed:", e)
        print(f"   cleanup: {len(ex.OWNERS)} owner fixture(s)")
        ex.flush_deferred_deletes()
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
