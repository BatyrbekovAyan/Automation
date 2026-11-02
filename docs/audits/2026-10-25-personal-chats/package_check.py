#!/usr/bin/env python3
"""«Личные и рабочие чаты», пакет выкладки, шаг 0.1 — канон РАБОЧЕЙ КОПИИ = отрепетированный пакет (офлайн, ни одного
запроса; читает только файлы и git этой копии).

Инструменты выкладки берут рабочую копию, а не коммит: deploy-canonical.py выкладывает каждый Tools/n8n/workflows/*.json
рабочей копии, update-clones.py читает из неё файлы шаблонов. Поэтому сверяется рабочая копия — графами
(verify-deployed.graph_hash) против канона, которым прод обслуживается с 2026-11-01 (PROD_REF, `git archive`, офлайн):
  - 12 workflow пакета: граф «до» (= прод) и «после» (= рабочая копия) — ровно таблица README «Что меняется на сервере»;
  - остальные workflow канона: граф и ключи настроек, которые задаёт канон, одинаковы на обеих сторонах (в пакет не входят);
  - других новых или пропавших файлов нет;
  - штампы шаблонов для живых клонов (update-clones.template_stamp) — «до» и «после», как в шаге 6.1;
  - свои скрипты и SQL пакета (*.py / *.sql этой папки) — ровно те версии, по которым написан README: id объекта git
    каждого (`git hash-object`, тот же id показывает `git ls-tree`) = таблица OWN ниже; нет ни пропавших, ни лишних. Свой
    собственный id этот файл нести не может (id — хэш этого самого текста): его закрепляет README, шаг 0.1 (c), до запуска.
Код 0 и PACKAGE OK — рабочая копия и есть отрепетированный пакет; иначе FAIL и код 1 — СТОП (не выкладывать).

  package_check.py
"""
import importlib.util
import io
import json
import os
import subprocess
import sys
import tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
N8N = os.path.join(REPO, "Tools", "n8n")
_spec = importlib.util.spec_from_file_location("verify_deployed", os.path.join(N8N, "verify-deployed.py"))
vd = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(vd)
uc = vd.uc

PROD_REF = "40bbf80b"          # the canon prod serves since 2026-11-01 (docs/audits/2026-11-01-prod-after-c2.txt)
# workflow id → (graph at PROD_REF — None for a new workflow, graph of the package) — the README's table, byte for byte
PACKAGE = {
    "0DP79STI3pafXO9B": ("dcce761854bd", "2a2b9eb5eba7"),   # Set Workflow State
    "crAudienceDec001": (None, "4e3736a6bc7e"),             # Decide Audience (new)
    "crChatAudience01": (None, "30f1fc89207c"),             # Chat Audience (new)
    "XuvOp7TxOImOAmlj": ("789086fca8ff", "58f80a16aafb"),   # Create WA
    "Uz6HBBUpAiUqVysB": ("fed4629fee42", "80e93626bd01"),   # Create TG
    "lmjYsdNcQA2IE5rl": ("ab64a6555282", "95b6407aa939"),   # Delete Bot Files
    "fXYpCXPKw92EzRz8": ("35be6f37abeb", "6abfa98ea38c"),   # Profile Lifecycle Sweep
    "crWappiProxy0001": ("c8bba5e407f4", "21204c214a4a"),   # Wappi Proxy
    "9PTyYcelRQI7bGDb": ("fc2f19c5c996", "84d59c9c26bb"),   # Suggest Replies
    "2htWSV5IHO8E2CgB": ("5f4cd7fb634d", "13b0fa2cca2e"),   # Dashboard Outcomes
    "4wYitz5ek30SVNlT": ("60aa43640382", "3c53710fa0b6"),   # WhatsApp template
    "4VN3gsFaC2HUYmcc": ("2433180b00c9", "a1e3c21f0e79"),   # Telegram template
}
# channel → (template stamp at PROD_REF = the live clones' graph today, the stamp after update-clones)
TEMPLATE_STAMPS = {"whatsapp": ("c0a7c5186fb2", "5c4c33e27468"), "telegram": ("62e09511fc94", "cc77e05b537a")}
# The package's own scripts and SQL (this folder) → their git blob id, the versions the README is written for: a later
# commit or an uncommitted edit of one of them, a missing one or a new *.py / *.sql here is a FAIL. This file is not in the
# table — it cannot carry its own id; the README pins it (step 0.1 (c)) before it runs.
OWN = {
    "canary-baseline.sql": "42266c2c0b69eb86d8d468db24e3cb79cf068831",
    "canary.py": "bb809c2de00cfc61ccdda3c2cee6002af2bd8e36",
    "canary.sql": "a5fe107635e15db0869820ada693829150770771",
    "health.sql": "7eb8efe1e5b6a2ffb690160d9d77b893dc55ddc4",
    "newwf.py": "8ffbcc8f9dfc915b804bd0c1c6ecfbcef03d647c",
    "postcheck.sql": "d8804a1cc48a9abed257d5c0761b61a5055a7fd9",
    "precheck.sql": "a1cf93cb7c79f2a606690bbce8dd8fc2f4b015c4",
    "readback.py": "7e546273ac349796ccb79d96776f744fc322de31",
    "rollback.py": "c1ddccf87189b544f97423af936268b9a3a2089f",
    "smoke.py": "931f0be7dfaa53b7ee41f357b40b2616cb3724be",
    "stand-canary.py": "31e312eb9c3da540ca92e6bf00483398609f04f3",
    "watch.py": "4a1118c0a94da2c5e54c393f275e29ef172f0706",
}
SELF = os.path.basename(os.path.abspath(__file__))


def canon_at(ref):
    """{file name: workflow} of Tools/n8n/workflows at a commit of this repository (git archive, nothing written)."""
    try:
        raw = subprocess.run(["git", "-C", REPO, "archive", ref, "Tools/n8n/workflows"], capture_output=True, check=True).stdout
    except (OSError, subprocess.CalledProcessError) as e:
        sys.exit(f"FAIL — cannot read the canon at {ref} from git in {REPO}: {e}")
    out = {}
    with tarfile.open(fileobj=io.BytesIO(raw)) as t:
        for m in t.getmembers():
            if m.isfile() and m.name.endswith(".json"):
                out[os.path.basename(m.name)] = json.load(t.extractfile(m))
    return out


def working_canon():
    folder = os.path.join(N8N, "workflows")
    return {f: json.load(open(os.path.join(folder, f), encoding="utf-8")) for f in sorted(os.listdir(folder)) if f.endswith(".json")}


def wid_of(name):
    return name.split("-", 1)[0]


def settings_of(wf):
    s = (wf or {}).get("settings") or {}
    return {k: s[k] for k in vd.CANONICAL_SETTINGS if k in s}


def own_ids():
    """{file: git blob id} of every *.py / *.sql in this folder except this script — the README, the rehearsal, the deploy's
    own outputs and other docs are not code. `git hash-object` reads the files and writes nothing."""
    names = sorted(f for f in os.listdir(HERE)
                   if f.endswith((".py", ".sql")) and f != SELF and os.path.isfile(os.path.join(HERE, f)))
    if not names:
        return {}
    try:
        out = subprocess.run(["git", "-C", HERE, "hash-object", "--", *names], capture_output=True, check=True,
                             text=True).stdout.split()
    except (OSError, subprocess.CalledProcessError) as e:
        sys.exit(f"FAIL — cannot hash the package's own files with git in {HERE}: {e}")
    return dict(zip(names, out))


def main():
    before, after = canon_at(PROD_REF), working_canon()
    ok = True
    print(f"canon at {PROD_REF}: {len(before)} files; working copy {N8N}/workflows: {len(after)} files")
    seen, unchanged = set(), 0
    for name in sorted(set(before) | set(after)):
        wid = wid_of(name)
        seen.add(wid)
        old, new = before.get(name), after.get(name)
        g_old = vd.graph_hash(old) if old is not None else None
        g_new = vd.graph_hash(new) if new is not None else None
        if wid in PACKAGE:
            want_old, want_new = PACKAGE[wid]
            good = g_old == want_old and g_new == want_new
            print(f"  {'OK  ' if good else 'FAIL'} package    {name}  graph {g_old or '—'} -> {g_new or '—'}"
                  f"  (README {want_old or '—'} -> {want_new})  settings {json.dumps(settings_of(new), sort_keys=True)}")
        else:
            good = old is not None and new is not None and g_old == g_new and settings_of(old) == settings_of(new)
            unchanged += good
            if not good:
                print(f"  FAIL not in the package but differs: {name}  graph {g_old or '—'} -> {g_new or '—'}"
                      f"  settings {json.dumps(settings_of(old), sort_keys=True)} -> {json.dumps(settings_of(new), sort_keys=True)}")
        ok &= good
    missing = sorted(set(PACKAGE) - seen)
    if missing:
        ok = False
        print(f"  FAIL package workflows absent from both sides: {missing}")
    print(f"  unchanged (graph and canonical settings equal at {PROD_REF} and in the working copy): {unchanged}")
    for ch, (want_old, want_new) in TEMPLATE_STAMPS.items():
        name = uc.TEMPLATES[ch]
        s_old = uc.template_stamp(before[name]) if name in before else None
        s_new = uc.template_stamp(after[name]) if name in after else None
        good = s_old == want_old and s_new == want_new
        ok &= good
        print(f"  {'OK  ' if good else 'FAIL'} clones [{ch}] template stamp {s_old} -> {s_new}  (README {want_old} -> {want_new})")
    have = own_ids()
    for name in sorted(set(have) | set(OWN)):
        got, want = have.get(name), OWN.get(name)
        good = got is not None and got == want
        ok &= good
        print(f"  {'OK  ' if good else 'FAIL'} own        {name}  {got or 'missing'}"
              + ("" if good else f"  (want {want or 'nothing: not a file of the package'})"))
    print("PACKAGE OK" if ok else "FAIL — the working copy is not the rehearsed package: STOP, do not deploy")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
