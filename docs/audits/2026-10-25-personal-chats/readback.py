#!/usr/bin/env python3
"""«Личные и рабочие чаты», пакет выкладки, шаг 6б — явное чтение настроек назад (только чтение).

Журнал выполнений — единственная защита «текст личного чата не оседает в журнале n8n»: успешные выполнения тех workflow,
что видят текст или имя чата, не сохраняются (`saveDataSuccessExecution: "none"`). Настройки — вне графа, поэтому их читаем
отдельно, по одному workflow, даже когда verify-deployed.py их уже сравнил:
  - оба шаблона, Suggest Replies, Decide Audience, Chat Audience, Wappi Proxy — saveDataSuccessExecution;
  - Suggest Replies — ещё executionTimeout (20 с; без него панель ждёт до таймаута инстанса);
  - каждый живой клон ИИ-сотрудника — saveDataSuccessExecution шаблона его канала (update-clones.TEMPLATE_SETTINGS).
Ожидание берётся из канона этой рабочей копии; на стенде (localhost) — из его производной копии (там шаблоны и Suggest
Replies успешные выполнения хранят: derive-staging.py, чтобы сценарии читали их назад).

  readback.py --base URL          # код 0 — всё совпало, 1 — нет
"""
import argparse
import importlib.util
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
N8N = os.path.join(REPO, "Tools", "n8n")
_spec = importlib.util.spec_from_file_location("deploy_canonical", os.path.join(N8N, "deploy-canonical.py"))
dc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(dc)
uc, vd = dc.uc, dc.vd

IDS = ("4wYitz5ek30SVNlT", "4VN3gsFaC2HUYmcc", "9PTyYcelRQI7bGDb", "crAudienceDec001", "crChatAudience01", "crWappiProxy0001")
KEYS = {"9PTyYcelRQI7bGDb": ("saveDataSuccessExecution", "executionTimeout")}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--base", required=True)
    a = ap.parse_args()
    files = {wid: v for wid, v in dc.canonical_files().items() if wid in IDS}
    if dc.is_staging(a.base):
        files = dc.staging_canon(files)
    ok = True
    for wid in IDS:
        name, want_settings = os.path.basename(files[wid][0]), files[wid][1].get("settings") or {}
        st, live = uc.call(a.base, "GET", f"/workflows/{wid}")
        got = (live or {}).get("settings") or {}
        for key in KEYS.get(wid, ("saveDataSuccessExecution",)):
            good = st == 200 and key in want_settings and got.get(key) == want_settings[key]
            ok &= good
            print(f"{wid} {name}: {key} = {got.get(key)!r} (want {want_settings.get(key)!r}) {'OK' if good else 'FAIL'}")
    canonical_ids = set(dc.canonical_files())
    clones = []
    for w in uc.list_workflows(a.base):
        if w.get("isArchived") or w.get("id") in canonical_ids:
            continue
        st, full = uc.call(a.base, "GET", f"/workflows/{w['id']}")
        if st == 200 and uc.is_clone(full, canonical_ids):
            clones.append(full)
    for full in sorted(clones, key=lambda x: x["id"]):
        ch = uc.channel_of(full)
        want = uc.template_settings(uc.canonical(ch, a.base))
        got = full.get("settings") or {}
        for key, value in want.items():
            good = got.get(key) == value
            ok &= good
            print(f"clone {full['id']} {full.get('name')!r} [{ch}] active={full.get('active')}: {key} = {got.get(key)!r} "
                  f"(want {value!r}) {'OK' if good else 'FAIL'}")
    print(f"clones read: {len(clones)}")
    print("READ-BACK OK" if ok else "READ-BACK FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
