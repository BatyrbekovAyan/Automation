#!/usr/bin/env python3
"""ТОЛЬКО СТЕНД. Репетиция canary пакета «Личных и рабочих чатов» (README, шаг 9) — то, что на проде проверяет владелец
телефонами на тестовом ИИ-сотруднике, здесь сыграно запросами к стенду (n8n 2.36.7 http://localhost:5678, Wappi-мок,
Postgres pg.sh); запросы к любой другой базе не делаются (константа BASE ниже).

Главное, чего не проверяют сценарии e2e-audience: ИИ-сотрудник, СОЗДАННЫЙ ЧЕРЕЗ ПУБЛИЧНЫЙ API (Create WA/TG — как новые
боты новой сборки и клоны на проде), вызывает подпроцесс crAudienceDec001, импортированный CLI и опубликованный через API
(шаги 4а–4г пакета); и сами SQL шагов 9.0 и 9.5 — как их выполнит владелец (тот же текст, собранный canary.py, через
Postgres-узел n8n — так, как это делает .secrets/sql-oneoff.py). На каждом канале:
  1. ИИ-сотрудник в «Авто» — Create через настоящий вебхук (клон создаёт и включает сам Create); его линия создана 2 часа
     назад (первые 30 минут позади); строки подключения нет — её создаст Decide Audience (как у «2» на проде: старая сборка
     о подключении не сообщает);
  2. «до выкладки»: сохранённый контакт уже писал этому ИИ-сотруднику, и сервер это записал (история, память, журнал
     ответа — сутки назад), как «2» записывал до выкладки;
  3. 9.0 — отметка (canary-baseline.sql), ответ сохраняется в файл, как это сделает tee;
  4. 9.1 — пишет номер, которого никогда не было в контактах → в выполнении клона отработал Decide Audience (business),
     записано business / default / new, ИИ-сотрудник ответил один раз; затем режим «Выбрать» (SetReplyMode '*');
  5. 9.2 — уже в «Выбрать» пишет сохранённый контакт → personal / default / contact, ответа нет, о чате не записано
     ничего НОВОГО (сохранённое «до выкладки» на месте);
  6. 9.3 — панель подсказок для личного чата: отказ personal_chat, модель не запускалась; list приложения → 200;
  7. 9.5 — canary.sql (собранный canary.py из файла отметки): решения после отметки — ровно эти два, personal_after
     0 / 0 / 0, хотя у контакта лежат строки «до выкладки» (они — в personal_rows_before), business_after > 0, в журнале
     ответа после отметки есть sent;
  8. контрольный провал: строка истории, памяти и журнала ответа личного чата, записанные ПОСЛЕ решения, — canary.sql
     видит каждую (personal_after 1 / 1 / 1); затем они удаляются.

    python3 docs/audits/2026-10-25-personal-chats/stand-canary.py      # нужен окружению e2e-audience.py: pg.sh, мок, bootstrap
"""
import importlib.util
import json
import os
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
STAGING = os.path.join(REPO, "Tools", "n8n", "staging")
OUT = os.path.join(REPO, "Tools", "test-output", "task18")


def _load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), os.path.join(STAGING, name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


ea = _load("e2e-audience")
if not ea.BASE.startswith("http://localhost:"):
    sys.exit(f"stand only — refusing base {ea.BASE}")
ex = ea.e2e_auth


def pg_node(sql, tag):
    """One SQL text through a TEMPORARY n8n workflow (webhook → Postgres node on the «Postgres Staging» credential) — the
    way .secrets/sql-oneoff.py runs the package's SQL on prod — and the line sql-oneoff.py would print: «<status> <body>».
    The workflow is switched off at once and deleted ≥ 35 s later, in one batch at the end (insights rule)."""
    st, creds = ex.admin_api("GET", "/credentials?limit=100")
    cred = next((c for c in (creds or {}).get("data", []) if c.get("name") == "Postgres Staging" and c.get("type") == "postgres"), None)
    if st != 200 or cred is None:
        raise RuntimeError(f"no «Postgres Staging» credential (GET /credentials {st})")
    path = f"stand-canary-sql-{tag}-{secrets.token_hex(6)}"
    wf = {"name": f"ZZZ TEMP stand-canary sql {tag} (auto-delete)", "settings": {"executionOrder": "v1"},
          "nodes": [{"id": "w1", "name": "Hook", "type": "n8n-nodes-base.webhook", "typeVersion": 2, "position": [0, 0],
                     "parameters": {"httpMethod": "POST", "path": path, "responseMode": "lastNode", "options": {}}, "webhookId": path},
                    {"id": "p1", "name": "Q", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": [220, 0],
                     "parameters": {"operation": "executeQuery", "query": sql, "options": {}},
                     "credentials": {"postgres": {"id": cred["id"], "name": cred["name"]}}}],
          "connections": {"Hook": {"main": [[{"node": "Q", "type": "main", "index": 0}]]}}}
    st, created = ex.admin_api("POST", "/workflows", wf)
    if st != 200 or not isinstance(created, dict) or not created.get("id"):
        raise RuntimeError(f"temporary SQL workflow not created: {st} {str(created)[:200]}")
    wid = created["id"]
    try:
        ex.admin_api("POST", f"/workflows/{wid}/activate")
        status, body = 0, ""
        for _ in range(10):
            time.sleep(1.0)
            req = urllib.request.Request(f"{ea.BASE}/webhook/{path}", data=b"{}", method="POST",
                                         headers={"Content-Type": "application/json"})
            try:
                with urllib.request.urlopen(req, timeout=60) as r:
                    status, body = r.status, r.read().decode()
            except urllib.error.HTTPError as e:
                status, body = e.code, e.read().decode()
            if status != 404:
                break
        return f"{status} {body}"
    finally:
        ex.defer_delete(wid)


def run_canary_sql(baseline_file, tag):
    """9.5 exactly as the README runs it: canary.py composes the text from the 9.0 answer, the Postgres node runs it."""
    composed = subprocess.run([sys.executable, os.path.join(HERE, "canary.py"), baseline_file], capture_output=True, text=True)
    if composed.returncode != 0:
        raise RuntimeError(f"canary.py refused: {composed.stderr.strip()}")
    line = pg_node(composed.stdout, tag)
    status, _, body = line.partition(" ")
    return status, (json.loads(body) if status == "200" else body)


def seed_before_deploy(pid, chat, tag):
    """What the server stored about a chat BEFORE the deploy (every chat was recorded then): two history rows, two memory
    rows, one ledger row — a day old. canary.sql must not count them."""
    ea.psql(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, created_at) values "
            f"('{pid}', '{chat}', '{tag}a', 'client', 'x', now() - interval '1 day'), "
            f"('{pid}', '{chat}', '{tag}b', 'ai', 'y', now() - interval '1 day');"
            f"insert into n8n_chat_histories (session_id, message) values "
            f"('{pid}:{chat}', '{{\"type\": \"human\", \"content\": \"x\"}}'::jsonb), "
            f"('{pid}:{chat}', '{{\"type\": \"ai\", \"content\": \"y\"}}'::jsonb);"
            f"insert into reply_events (profile_id, chat_id, message_id, state, received_at, updated_at) values "
            f"('{pid}', '{chat}', '{tag}a', 'sent', now() - interval '1 day', now() - interval '1 day');")


def leak_after_decision(pid, chat, tag):
    """The control: one history, memory and ledger row of the personal chat written NOW (after its decision) — what
    canary.sql exists to catch. Returns the memory row's id (for the removal)."""
    ea.psql(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body) values ('{pid}', '{chat}', '{tag}', 'client', 'x');"
            f"insert into reply_events (profile_id, chat_id, message_id, state) values ('{pid}', '{chat}', '{tag}', 'claimed');")
    return ea.psql(f"insert into n8n_chat_histories (session_id, message) values "
                   f"('{pid}:{chat}', '{{\"type\": \"human\", \"content\": \"x\"}}'::jsonb) returning id;").split()[0]


def remove_leak(pid, chat, tag, memory_id):
    ea.psql(f"delete from chat_history_events where profile_id = '{pid}' and chat_id = '{chat}' and message_id = '{tag}';"
            f"delete from reply_events where profile_id = '{pid}' and chat_id = '{chat}' and message_id = '{tag}';"
            f"delete from n8n_chat_histories where id = {int(memory_id)};")


def canary(ctx, ch):
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
        seed_before_deploy(pid, contact, f"cypre-{ch}-{ctx.run_id}")
        contact_before = ea.stored(pid, contact)
        # 9.0 — the mark, saved the way `tee` saves it
        baseline_line = pg_node(open(os.path.join(HERE, "canary-baseline.sql"), encoding="utf-8").read(), f"b{ch}")
        os.makedirs(OUT, exist_ok=True)
        baseline_file = os.path.join(OUT, f"fix1-canary-baseline-{ch}.txt")
        with open(baseline_file, "w", encoding="utf-8") as fh:
            fh.write(baseline_line + "\n")
        out["baseline"] = baseline_line
        since_calls = ea.mock_total()
        since_exec = max([int(e["id"]) for e in ea.latest_executions(wid, 0, 50)] or [0])
        results = {}
        for label, chat, text, extra in (("new", new, "Здравствуйте! Сколько стоит букет из 25 роз?", {"contact_name": "Клиент"}),
                                         ("contact", contact, "Привет, ты дома?", {"contact_name": "Айгерим Сохранённая"})):
            if label == "contact":   # 9.1 done: back to «Выбрать» before 9.2, as the README does it
                rst, rd = ex.hook("SetReplyMode", o.token, {"profileIds": [pid], "chatId": "*", "suppressed": True,
                                                            "version": int(time.time() * 1000)})
                rd = rd if isinstance(rd, dict) else {}
                out["mode_vybrat"] = (rst, rd.get("allApplied"), [a.get("suppressed") for a in rd.get("applied") or []])
            mid, t = f"cy{label}-{ch}-{ctx.run_id}", int(time.time())
            ea.mock("POST", "/__mock/messages", {"profile_id": pid, "chat_id": chat, "messages": [ea.row(ch, chat, mid, False, text, t)]})
            fields = {"chat_type": chat_type, "is_bot": False, **(extra if ch == "wa" else {})}
            http_st, _ = ea.http("POST", f"{ea.BASE}/webhook/{pid}", ea.e2e_reply.event(pid, chat, mid, text, t, **fields), timeout=30)
            rows = ea.wait_executions_settle(wid, since_exec, 1, 180)
            last = ea.execution(rows[-1]["id"]) if rows else {}
            since_exec = max([since_exec] + [int(r["id"]) for r in rows])
            results[label] = {"webhook": http_st, "execution": rows[-1]["id"] if rows else None,
                              "mode_suppressed": (ea.node_output(last, "Read Reply Mode") or {}).get("suppressed"),
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
        # 9.5 — as the owner runs it
        cst, cy = run_canary_sql(baseline_file, f"c{ch}")
        # the control: rows of the personal chat written after its decision must be seen
        leak_tag = f"cyleak-{ch}-{ctx.run_id}"
        memory_id = leak_after_decision(pid, contact, leak_tag)
        try:
            lst2, leak = run_canary_sql(baseline_file, f"l{ch}")
        finally:
            remove_leak(pid, contact, leak_tag, memory_id)
        contact_after = ea.stored(pid, contact)
    finally:
        ex.drop_clones(*o.profiles)
    n, c = results["new"], results["contact"]
    cy = cy if isinstance(cy, dict) else {}
    leak = leak if isinstance(leak, dict) else {}
    my_decisions = sorted((x.get("kind"), x.get("source"), x.get("reason"), x.get("chats")) for x in cy.get("decisions") or []
                          if x.get("profile_id") == pid)
    my_connection = [x for x in cy.get("connections") or [] if x.get("profile_id") == pid]
    before = cy.get("personal_rows_before") or {}
    canary_ok = (cst == "200"
                 and my_decisions == [("business", "default", "new", 1), ("personal", "default", "contact", 1)]
                 and cy.get("personal_after") == {"history": 0, "memory": 0, "ledger": 0}
                 and (cy.get("business_after") or {}).get("history", 0) > 0 and (cy.get("business_after") or {}).get("memory", 0) > 0
                 and (cy.get("ledger_after") or {}).get("sent", 0) >= 1
                 and before.get("history", 0) >= 2 and before.get("memory", 0) >= 2 and before.get("ledger", 0) >= 1
                 and len(my_connection) == 1 and my_connection[0].get("number_known") is False)
    leak_ok = lst2 == "200" and leak.get("personal_after") == {"history": 1, "memory": 1, "ledger": 1}
    ok = (out["create"][0] == 200 and out.get("mode_vybrat") == (200, True, [True])
          and n["mode_suppressed"] is False and c["mode_suppressed"] is True
          and n["outcome"] == "sent" and n["decide_ran"] and n["decide"] == {"audience": "business", "outcome": "business"}
          and n["decision"] == "business|default|new" and n["sends"] == 1
          and c["outcome"] == "personal_contact" and c["decide"] == {"audience": "personal", "outcome": "personal_contact"}
          and c["decision"] == "personal|default|contact" and c["sends"] == 0
          and c["stored"] == contact_before == contact_after
          and panel == {"http": 200, "error": "personal_chat", "cards": 0, "llm_ran": False, "gate": "personal_chat"}
          and lst == 200 and listed == [("contact", "personal", "default", "contact"), ("new", "business", "default", "new")]
          and connection == ("0", "-") and mine.get("connectedAt") is not None
          and canary_ok and leak_ok)
    return ok, {**out, "profile": pid, "new": {k: v for k, v in n.items() if k != "stored"}, "contact": c,
                "contact stored before 9.0 / after the canary": (contact_before, contact_after), "panel": panel,
                "list": (lst, listed, mine.get("connectedAt")), "connection(moment-created s, phone)": connection,
                "canary.sql (9.5)": {"status": cst, "decisions of this profile": my_decisions, "connection": my_connection,
                                     **{k: cy.get(k) for k in ("personal_after", "business_after", "ledger_after", "personal_rows_before")},
                                     "verdict": "OK" if canary_ok else "FAIL"},
                "canary.sql after a leak written after the decision": {"status": lst2, "personal_after": leak.get("personal_after"),
                                                                        "verdict": "SEEN" if leak_ok else "MISSED"}}


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
