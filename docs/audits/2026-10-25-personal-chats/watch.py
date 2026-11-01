#!/usr/bin/env python3
"""«Личные и рабочие чаты», пакет выкладки, шаг 8 — наблюдение после выкладки (только чтение).

Успешные выполнения шаблонов, клонов, Suggest Replies, Decide Audience и Chat Audience НЕ сохраняются — смотрим ошибочные
(они сохраняются все) у каждого workflow пакета и по всему инстансу с начала выкладки. С --sweep — выполнения Profile Lifecycle
Sweep (его успешные выполнения сохраняются) с начала выкладки и ответ узла «Wipe Personal Chats» в каждом: {wiped_chats,
deleted_rows} — или ошибка узла (он идёт дальше при ошибке, поэтому ошибка видна только здесь).

Начало выкладки — строка ISO-времени UTC в файле deploy-start.txt рядом с этим файлом (его пишет шаг 0) или --since.

  watch.py --base URL [--since ISO]                 # ошибки с начала выкладки
  watch.py --base URL [--since ISO] --sweep         # выполнения Sweep и ответ «Wipe Personal Chats»
  (--workflow WID — другой workflow вместо Sweep: репетиция читает так выполнение пробы с тем же узлом)
"""
import argparse
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
N8N = os.path.join(REPO, "Tools", "n8n")
_spec = importlib.util.spec_from_file_location("update_clones", os.path.join(N8N, "update-clones.py"))
uc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(uc)

PACKAGE = ("crAudienceDec001", "crChatAudience01", "0DP79STI3pafXO9B", "XuvOp7TxOImOAmlj", "Uz6HBBUpAiUqVysB", "lmjYsdNcQA2IE5rl",
           "fXYpCXPKw92EzRz8", "crWappiProxy0001", "9PTyYcelRQI7bGDb", "2htWSV5IHO8E2CgB")
SWEEP = "fXYpCXPKw92EzRz8"
WIPE = "Wipe Personal Chats"


def since_of(a):
    if a.since:
        return a.since
    path = os.path.join(HERE, "deploy-start.txt")
    if not os.path.exists(path):
        sys.exit(f"no --since and no {path} (README step 0 writes it)")
    return open(path).read().strip()


def errors(base, since, wid=None):
    q = "/executions?status=error&limit=100" + (f"&workflowId={wid}" if wid else "")
    st, d = uc.call(base, "GET", q)
    rows = [(e.get("workflowId"), e.get("id"), e.get("startedAt")) for e in (d or {}).get("data", []) if (e.get("startedAt") or "") >= since]
    return st, rows


def node_items(ex, name):
    runs = ((((ex or {}).get("data") or {}).get("resultData") or {}).get("runData") or {}).get(name) or []
    out = []
    for run in runs:
        if run.get("error"):
            out.append({"error": (run["error"] or {}).get("message")})
        for branch in ((run.get("data") or {}).get("main") or []):
            for it in branch or []:
                out.append(it.get("json") or {})
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--base", required=True)
    ap.add_argument("--since")
    ap.add_argument("--sweep", action="store_true")
    ap.add_argument("--workflow", default=SWEEP)
    a = ap.parse_args()
    since = since_of(a)
    if not a.sweep:
        bad = False
        for wid in PACKAGE:
            st, rows = errors(a.base, since, wid)
            bad |= st != 200 or bool(rows)
            print(f"{wid} GET {st} | error executions since {since}: {rows}")
        st, rows = errors(a.base, since)
        bad |= st != 200 or bool(rows)
        print(f"whole instance GET {st} | error executions since {since}: {rows}")
        sys.exit(1 if bad else 0)
    st, d = uc.call(a.base, "GET", f"/executions?workflowId={a.workflow}&limit=20")
    rows = sorted((e for e in (d or {}).get("data", []) if (e.get("startedAt") or "") >= since), key=lambda e: e.get("startedAt") or "")
    print(f"{a.workflow} GET {st} | executions since {since}: {len(rows)}")
    bad = st != 200
    for e in rows:
        st2, ex = uc.call(a.base, "GET", f"/executions/{e['id']}?includeData=true")
        items = node_items(ex, WIPE) if st2 == 200 else []
        ran = bool(items)
        good = st2 == 200 and ran and all("error" not in it and isinstance(it.get("wiped_chats"), int) for it in items)
        bad |= not good
        print(f"  execution {e['id']} {e.get('startedAt')} status {e.get('status')} | {WIPE}: "
              f"{json.dumps(items, ensure_ascii=False) if ran else 'did not run'} | {'OK' if good else 'LOOK'}")
    if not rows:
        print("  no execution yet: the Sweep ticks every 6 hours (00:00 / 06:00 / 12:00 / 18:00 Asia/Almaty) — run this again after the next tick")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
