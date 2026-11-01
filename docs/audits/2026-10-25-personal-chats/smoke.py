#!/usr/bin/env python3
"""«Личные и рабочие чаты», пакет выкладки, шаг 7 — smoke после выкладки.

Без --session ни одна проверка ничего не пишет, даже при нарушенном предусловии: тела без id, поддельный токен отбивается
гейтом, проба Set Workflow State просит `activate` уже ВКЛЮЧЁННОГО защищённого workflow (худшее при сломанной защите —
повторная публикация того же workflow; `deactivate`/`delete` здесь не применяются никогда).
  7.1  GET каждого затронутого вебхука → «Did you mean to make a POST request?» (зарегистрирован; GET выполнения не запускает)
  7.2  POST ChatAudience {} без токена → 401 unauthorized:missing (только токен сессии: legacy-окно его не открывает)
  7.3  POST ChatAudience {} с поддельным токеном → 401 unauthorized:bad_signature
  7.4  POST SetWorkflowState activate crChatAudience01 / crAudienceDec001 без токена → 400 protected_workflow
       (после закрытия legacy-окна CR_AUTH_COMPAT_UNTIL — 401 до Validate: тоже защита)
С --session (пишет ОДНУ строку `installs` — анонимная установка без платёжного id и без ИИ-сотрудников, как smoke выкладки 2026-09-24):
  7.5  AuthSession bootstrap → токен сессии, подписанный секретом этого сервера
  7.6  POST ChatAudience {op: list, profileIds: []} с этим токеном → 400 bad_request / profileIds: гейт принял настоящий токен
       сервера, Validate сработал. Ответ 200 даёт только установка, у которой есть ИИ-сотрудник (canary, шаг 9).

  smoke.py --base URL [--session]          # код 0 — всё как ожидалось
"""
import argparse
import json
import sys
import urllib.error
import urllib.request

FORGED = "v1.eyJpIjoieCJ9.eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHg"     # a token whose signature is not this server's
REGISTERED = ("ChatAudience", "CreateWhatsappWorkflow", "CreateTelegramWorkflow", "DeleteBotFiles", "SuggestReplies",
              "DashboardOutcomes", "WappiProxy", "SetWorkflowState")


def http(method, url, body=None, token=None):
    req = urllib.request.Request(url, method=method)
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    data = None
    if body is not None:
        req.add_header("Content-Type", "application/json")
        data = json.dumps(body).encode()
    try:
        with urllib.request.urlopen(req, data, timeout=60) as r:
            raw = r.read().decode(errors="replace")
            st = r.status
    except urllib.error.HTTPError as e:
        raw, st = e.read().decode(errors="replace"), e.code
    try:
        return st, json.loads(raw) if raw else None
    except ValueError:
        return st, raw


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--base", required=True)
    ap.add_argument("--session", action="store_true")
    a = ap.parse_args()
    hook = lambda p: f"{a.base.rstrip('/')}/webhook/{p}"   # noqa: E731
    ok = True

    def line(label, st, d, good):
        nonlocal ok
        ok &= good
        print(f"{label} -> [{st}] {json.dumps(d, ensure_ascii=False) if not isinstance(d, str) else d[:160]}  {'OK' if good else 'FAIL'}")

    for p in REGISTERED:
        st, d = http("GET", hook(p))
        good = st == 404 and "Did you mean to make a POST request?" in json.dumps(d, ensure_ascii=False)
        line(f"7.1 GET /webhook/{p}", st, "Did you mean to make a POST request?" if good else d, good)
    st, d = http("POST", hook("ChatAudience"), {})
    line("7.2 POST ChatAudience {} (no token)", st, d, st == 401 and (d or {}).get("reason") == "unauthorized:missing")
    st, d = http("POST", hook("ChatAudience"), {}, FORGED)
    line("7.3 POST ChatAudience {} (forged token)", st, d, st == 401 and (d or {}).get("reason") == "unauthorized:bad_signature")
    for wid in ("crChatAudience01", "crAudienceDec001"):
        st, d = http("POST", hook("SetWorkflowState"), {"workflowId": wid, "action": "activate"})
        good = (st == 400 and (d or {}).get("error") == "protected_workflow") or st == 401
        line(f"7.4 POST SetWorkflowState activate {wid} (no token)", st, d, good)
    if a.session:
        st, d = http("POST", hook("AuthSession"), {"op": "bootstrap", "platform": "smoke", "appVersion": "personal-chats-pkg"})
        token = (d or {}).get("token") if isinstance(d, dict) else None
        good = st == 200 and bool(token) and (d or {}).get("success") is True
        line("7.5 AuthSession bootstrap (one anonymous install)", st,
             {"success": (d or {}).get("success"), "installId": (d or {}).get("installId"), "token": "<hidden>" if token else None}, good)
        if token:
            st, d = http("POST", hook("ChatAudience"), {"op": "list", "profileIds": []}, token)
            line("7.6 POST ChatAudience list [] (that session's token)", st, d,
                 st == 400 and (d or {}).get("error") == "bad_request" and (d or {}).get("reason") == "profileIds")
    print("SMOKE OK" if ok else "SMOKE FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
