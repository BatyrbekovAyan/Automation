#!/usr/bin/env python3
"""«Личные и рабочие чаты», пакет выкладки, шаг 4 — два НОВЫХ workflow: crAudienceDec001 «Decide Audience» (подпроцесс,
его вызывают шаблоны ИИ-сотрудника) и crChatAudience01 «Chat Audience» (вебхук приложения /webhook/ChatAudience).

  newwf.py copy --target prod|stand [--out DIR]
      Офлайн, без единого запроса. Временные копии для `n8n import:workflow` и их аудит (канон не трогается).
      prod  — канон, в котором учётка WappiAuthToken перепривязана на id прода ZowntFGvApDJ7UzQ (канонического
              EuhhqAaV56DpoqAN на проде нет: CLI-импорт подставляет учётку по имени только в своём проекте —
              a1-deploy-runbook §2.4); Postgres — канонический vvRrFiEXzLVqKjOx, он на проде есть.
      stand — производная копия стенда (тот же вывод, что импортирует bootstrap.py: deploy-canonical.staging_canon).
      Аудит: учётки РОВНО ожидаемые, канонического id WappiAuthToken в тексте нет, граф копии = граф канона = граф пакета,
      id сохранён, workflow выключен, успешные выполнения не сохраняются. Иначе — FAIL и код 1: не импортировать.
  newwf.py creds --base URL
      Только чтение (шаг 0): на сервере есть обе учётки, на которые перепривязана копия (GET /credentials — id, тип, имя;
      значения API не отдаёт). Иначе — FAIL и код 1: импорт не начинать.
  newwf.py get --base URL
      Только чтение: оба workflow есть, оба ВЫКЛЮЧЕНЫ, учётки РОВНО ожидаемые. Иначе — FAIL и код 1: не включать.
  newwf.py activate --base URL --id WID
      POST /workflows/<id>/activate (публикация через API: n8n проверяет граф до того, как начнёт его обслуживать) и
      чтение назад: включён, опубликована сохранённая версия, опубликованный граф = канон, учётки.

Сетевые шаги требуют явного --base: инструмент никуда не ходит по умолчанию. Ключ API — как у verify-deployed.py:
$N8N_API_KEY, иначе Tools/n8n/.secrets/ этой рабочей копии (стенд — staging-n8n.json, прод — prod-api-key.txt).
"""
import argparse
import copy
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

FILES = {"crAudienceDec001": "crAudienceDec001-Decide_Audience.json", "crChatAudience01": "crChatAudience01-Chat_Audience.json"}
# The graphs of the package (verify-deployed.graph_hash of the canon this package was written for): a canon that moved
# since then fails the audit — re-check the package before importing anything.
PACKAGE_GRAPH = {"crAudienceDec001": "4e3736a6bc7e", "crChatAudience01": "30f1fc89207c"}
CANON_WAPPI = "EuhhqAaV56DpoqAN"            # the canonical WappiAuthToken id — absent on prod
PROD = {"WappiAuthToken": ("httpHeaderAuth", "ZowntFGvApDJ7UzQ"), "Postgres": ("postgres", "vvRrFiEXzLVqKjOx")}
DEFAULT_OUT = "/tmp/cr-personal-chats-import"


def canon(wid):
    return json.load(open(os.path.join(N8N, "workflows", FILES[wid]), encoding="utf-8"))


def refs_of(wf):
    return {(t, r.get("id"), r.get("name")) for n in wf.get("nodes", []) for t, r in (n.get("credentials") or {}).items()}


def stand_want():
    """{(type, id, name)} the stand's derived copies carry: WappiAuthToken and «Postgres Staging», by name."""
    creds = uc._derive_module().staging_credentials()          # {name: (id, type)} of the stand's n8n
    return {("httpHeaderAuth", creds["WappiAuthToken"][0], "WappiAuthToken"),
            ("postgres", creds["Postgres Staging"][0], "Postgres Staging")}


def prod_want():
    return {(t, i, name) for name, (t, i) in PROD.items()}


def cmd_copy(a):
    os.makedirs(a.out, exist_ok=True)
    ok = True
    if a.target == "stand":
        derived = dc.staging_canon({wid: (os.path.join(N8N, "workflows", f), canon(wid)) for wid, f in FILES.items()})
        want = stand_want()
    else:
        derived, want = None, prod_want()
    for wid, f in FILES.items():
        c = canon(wid)
        if derived is not None:
            w = derived[wid][1]
        else:
            w = copy.deepcopy(c)
            for n in w["nodes"]:
                for t, ref in (n.get("credentials") or {}).items():
                    if ref.get("id") == CANON_WAPPI:
                        if t != "httpHeaderAuth" or ref.get("name") != "WappiAuthToken":
                            sys.exit(f"{f}: unexpected credential {t} {ref}")
                        ref["id"] = PROD["WappiAuthToken"][1]
        text = json.dumps(w, ensure_ascii=False, indent=2)
        refs = refs_of(w)
        settings = w.get("settings") or {}
        checks = {
            "credentials": refs == want,
            "no canonical WappiAuthToken id": CANON_WAPPI not in text or a.target == "stand" and CANON_WAPPI in {i for _, i, _ in want},
            "canon = package": vd.graph_hash(c) == PACKAGE_GRAPH[wid],
            "id kept": w.get("id") == wid,
            "inactive": w.get("active") is False,
            "no success journal": settings.get("saveDataSuccessExecution") == "none",
        }
        if derived is None:
            checks["copy graph = canon"] = vd.graph_hash(w) == vd.graph_hash(c)
        good = all(checks.values())
        ok &= good
        with open(os.path.join(a.out, f), "w", encoding="utf-8") as fh:
            fh.write(text)
        print(f"{f} {sorted(refs)} graph {vd.graph_hash(w)} (canon {vd.graph_hash(c)}) id {w.get('id')} active {w.get('active')} "
              f"saveDataSuccessExecution {settings.get('saveDataSuccessExecution')!r} -> "
              f"{'OK' if good else 'FAIL — do NOT import: ' + ', '.join(k for k, v in checks.items() if not v)}")
    print(f"copies in {a.out}")
    return 0 if ok else 1


def want_ids(base):
    return {i for _, i, _ in (stand_want() if dc.is_staging(base) else prod_want())}


def cmd_creds(a):
    want = stand_want() if dc.is_staging(a.base) else prod_want()
    st, d = uc.call(a.base, "GET", "/credentials?limit=100")
    have = {(c.get("type"), c.get("id"), c.get("name")) for c in (d or {}).get("data", [])}
    good = st == 200 and want <= have
    print(f"GET /credentials {st} | rows {len(have)} | wanted {sorted(want)} | "
          f"{'OK' if good else 'FAIL — missing ' + str(sorted(want - have))}")
    return 0 if good else 1


def cmd_get(a):
    want, ok = want_ids(a.base), True
    for wid in FILES:
        st, w = uc.call(a.base, "GET", f"/workflows/{wid}")
        ids = {r.get("id") for n in (w or {}).get("nodes", []) for r in (n.get("credentials") or {}).values()}
        good = st == 200 and w.get("active") is False and ids == want
        ok &= good
        print(f"{wid} GET {st} | active {w.get('active')} | creds {sorted(ids)} | "
              f"{'OK' if good else 'FAIL — do NOT activate'}")
    return 0 if ok else 1


def cmd_activate(a):
    if a.id not in FILES:
        sys.exit(f"--id must be one of {sorted(FILES)}")
    if dc.is_staging(a.base):
        want_graph = vd.graph_hash(dc.staging_canon({a.id: (os.path.join(N8N, "workflows", FILES[a.id]), canon(a.id))})[a.id][1])
    else:
        want_graph = vd.graph_hash(canon(a.id))
    st, _ = uc.call(a.base, "POST", f"/workflows/{a.id}/activate", {})
    st2, w = uc.call(a.base, "GET", f"/workflows/{a.id}")
    pub = uc.published_graph(w) if st2 == 200 else None
    pub_graph = vd.graph_hash(pub) if pub is not None else None
    ids = sorted({r.get("id") for n in (w or {}).get("nodes", []) for r in (n.get("credentials") or {}).values()})
    served = w.get("activeVersionId") == w.get("versionId") and w.get("activeVersionId") is not None
    good = st == 200 and st2 == 200 and w.get("active") is True and served and pub_graph == want_graph and set(ids) == want_ids(a.base)
    print(f"{a.id} activate {st} | active {w.get('active')} | published=stored {served} | published graph {pub_graph} "
          f"(canon {want_graph}) | creds {ids} | {'OK' if good else 'FAIL'}")
    return 0 if good else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("copy")
    c.add_argument("--target", choices=("prod", "stand"), required=True)
    c.add_argument("--out", default=DEFAULT_OUT)
    r = sub.add_parser("creds")
    r.add_argument("--base", required=True)
    g = sub.add_parser("get")
    g.add_argument("--base", required=True)
    v = sub.add_parser("activate")
    v.add_argument("--base", required=True)
    v.add_argument("--id", required=True)
    a = ap.parse_args()
    sys.exit({"copy": cmd_copy, "creds": cmd_creds, "get": cmd_get, "activate": cmd_activate}[a.cmd](a))


if __name__ == "__main__":
    main()
