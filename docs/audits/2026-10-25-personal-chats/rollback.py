#!/usr/bin/env python3
"""«Личные и рабочие чаты», пакет выкладки — откат по точке и снятие новых workflow (README, раздел «Откат»). Каждая команда —
одна запись в n8n и чтение назад; без явного --base не работает. Ключ API — как у verify-deployed.py: $N8N_API_KEY, иначе
Tools/n8n/.secrets/ этой рабочей копии (стенд — staging-n8n.json, прод — prod-api-key.txt); он не печатается.

  rollback.py publish --base URL --id WID --version VERSION_ID
      Опубликовать на месте прежнюю версию ВКЛЮЧЁННОГО workflow — точку отката из rollback-pointers.txt (строки
      `rollback pointer: versionId …` шагов 1.2 и 6.2): POST /workflows/<id>/activate {"versionId": …}. Без окна, когда
      вебхука нет: n8n проверяет версию и переключается на неё, при отказе продолжает служить прежняя. Сохранённая версия
      остаётся новой. Выключенный workflow не трогает: шаблоны выключены по контракту, и такая публикация ВКЛЮЧИЛА бы шаблон —
      их откатывают только каноном. Чтение назад: включён, служит указанная версия, граф опубликованной версии.
  rollback.py deactivate --base URL --id crAudienceDec001|crChatAudience01
      Выключить один из двух НОВЫХ workflow пакета (только их). Чтение назад: выключен.
  rollback.py delete --base URL --id crAudienceDec001|crChatAudience01
      Удалить выключенный новый workflow: ждёт 36 с (n8n сбрасывает статистику выполнений каждые 30 с; удалить workflow раньше
      чем через 35 с после его последнего выполнения — бесконечная ошибка «Error while saving insights» до перезапуска n8n),
      затем DELETE и GET → 404. Включённый не удаляет.
"""
import argparse
import importlib.util
import os
import sys
import time

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
N8N = os.path.join(REPO, "Tools", "n8n")
_spec = importlib.util.spec_from_file_location("verify_deployed", os.path.join(N8N, "verify-deployed.py"))
vd = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(vd)
uc = vd.uc

NEW = ("crAudienceDec001", "crChatAudience01")
TEMPLATES = {f.split("-", 1)[0] for f in uc.TEMPLATES.values()}
INSIGHTS_WAIT_S = 36


def cmd_publish(a):
    if a.id in TEMPLATES:
        print(f"{a.id}: REFUSED — a template is inactive by contract; publishing a version would switch it ON. Roll a template "
              "back by its canon (README «Откат»)")
        return 1
    st, before = uc.call(a.base, "GET", f"/workflows/{a.id}")
    if st != 200 or not isinstance(before, dict):
        print(f"{a.id}: GET {st} — nothing done")
        return 1
    if before.get("active") is not True:
        print(f"{a.id}: REFUSED — the workflow is not active; publishing a version would switch it on. Nothing done")
        return 1
    st, answer = uc.call(a.base, "POST", f"/workflows/{a.id}/activate", {"versionId": a.version})
    st2, after = uc.call(a.base, "GET", f"/workflows/{a.id}")
    after = after if isinstance(after, dict) else {}
    pub = uc.published_graph(after) if st2 == 200 else None
    served = after.get("activeVersionId")
    good = st == 200 and st2 == 200 and after.get("active") is True and served == a.version
    print(f"{a.id} publish {st} | active {after.get('active')} | served {served} | stored {after.get('versionId')} | "
          f"published graph {vd.graph_hash(pub) if pub is not None else None} | {'OK' if good else 'FAIL'}")
    if st != 200:
        print(f"    n8n: {str(answer)[:300]}")
    return 0 if good else 1


def cmd_deactivate(a):
    st, _ = uc.call(a.base, "POST", f"/workflows/{a.id}/deactivate")
    st2, w = uc.call(a.base, "GET", f"/workflows/{a.id}")
    w = w if isinstance(w, dict) else {}
    good = st == 200 and st2 == 200 and w.get("active") is False
    print(f"{a.id} deactivate {st} | active {w.get('active')} | {'OK' if good else 'FAIL'}")
    return 0 if good else 1


def cmd_delete(a):
    st, w = uc.call(a.base, "GET", f"/workflows/{a.id}")
    if st == 404:
        print(f"{a.id}: GET 404 — already gone | OK")
        return 0
    if st != 200 or not isinstance(w, dict) or w.get("active") is not False:
        print(f"{a.id}: GET {st} active {(w or {}).get('active') if isinstance(w, dict) else None} — REFUSED: deactivate it first")
        return 1
    print(f"{a.id}: inactive; waiting {INSIGHTS_WAIT_S} s before DELETE (n8n insights flush, ≥ 35 s rule)", flush=True)
    time.sleep(INSIGHTS_WAIT_S)
    st, _ = uc.call(a.base, "DELETE", f"/workflows/{a.id}")
    st2, _ = uc.call(a.base, "GET", f"/workflows/{a.id}")
    good = st == 200 and st2 == 404
    print(f"{a.id} DELETE {st} | GET {st2} | {'OK' if good else 'FAIL'}")
    return 0 if good else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("publish")
    p.add_argument("--base", required=True)
    p.add_argument("--id", required=True)
    p.add_argument("--version", required=True)
    for name in ("deactivate", "delete"):
        c = sub.add_parser(name)
        c.add_argument("--base", required=True)
        c.add_argument("--id", required=True, choices=NEW)
    a = ap.parse_args()
    a.base = a.base.rstrip("/")
    sys.exit({"publish": cmd_publish, "deactivate": cmd_deactivate, "delete": cmd_delete}[a.cmd](a))


if __name__ == "__main__":
    main()
