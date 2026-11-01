#!/usr/bin/env python3
"""Real-path probe (docs/audits/2026-10-29-price-tool/README.md): the e2e step of retired_bot_reconnects_after_purchase — the moved
dialog + «Напомните, во сколько … и сколько он стоит?» + a price-list row under the clone — asked of REAL n8n clones on the staging
stand (webhook → … → AI Agent → OpenAI through n8n's own LangChain client), many times, the variants in parallel (so their
trials interleave in time). The eval proxy cannot see this failure: the same body through it searched in 100+ of 100+ trials
while the real path missed 8 of 18 the same evening.

Each variant gets its own owner (a bought business plan), profile and clone, created through the real Create (flowers vertical,
empty About Business / catalog — as e2e-auth creates it), then switched to «Авто». A variant other than `before` patches ITS
clone through the staging API before the trials (the stand's canon is untouched):
    tool:<file>   the «Supabase Vector Store» tool's description := the file's text
    core:<file>   the file's sentence inserted into the clone's prompt right after «нет в каталоге — ищи в прайс-листах.»
Every trial is a NEW chat seeded with the e2e dialog (2 memory rows). Recorded per trial: did the price-list tool run, the first
model call's input tokens, the reply, the outcome; nothing else is changed. Cleanup: clones switched off and deleted ≥ 35 s later,
files and fixtures deleted (e2e-auth's own helpers).

--question conflict asks e2e-reply's V9 question of a VERTICAL clone instead (auto_parts, the catalog at 3200 through Create, a
price-list row at 3900, no dialog) and also records whether the reconciler's «Цены расходятся» reached the model and whether the
reply named the price-list price alone (e2e-reply's rule, every space kind stripped).

    python3 docs/audits/2026-10-29-price-tool/real-path-probe.py --out <dir> --trials 16 \
        --variant before --variant tool=tool:docs/audits/2026-10-29-price-tool/fix-tool-description.txt
    python3 docs/audits/2026-10-29-price-tool/real-path-probe.py --out <dir> --trials 16 --question conflict \
        --variant before --variant C2=core:docs/audits/2026-10-29-price-tool/fix-core-sentence-c2.txt
"""
import argparse
import importlib.util
import json
import os
import random
import threading
import time
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
spec = importlib.util.spec_from_file_location("e2e_auth", os.path.join(REPO, "Tools", "n8n", "staging", "e2e-auth.py"))
E = importlib.util.module_from_spec(spec)
spec.loader.exec_module(E)

# --question reask (the e2e step) | conflict (e2e-reply's V9 scenario on a VERTICAL clone: catalog 3200, price row 3900)
QUESTIONS = {
    "reask": {"vertical": ("flowers", "Цветы"), "products": "", "price_row": "Букет «Нежность» — 18 500 ₸ (15 роз)",
              "dialog": (("human", "Здравствуйте! Можно букет «Нежность» с доставкой в пятницу к 18:00?"),
                         ("ai", "Да, доставим букет «Нежность» в пятницу к 18:00 по адресу Абая 10.")),
              "question": "Напомните, во сколько в пятницу вы привезёте букет «Нежность» и сколько он стоит?"},
    "conflict": {"vertical": ("auto_parts", "Автозапчасти"),
                 "products": "Product1: Фильтр масляный Toyota 90915-YZZE1\nProduct1 Price: 3200 тг\nProduct1 Description: Оригинал",
                 "price_row": "Фильтр масляный 90915-YZZE1 — 3900 тг, оригинал", "dialog": (),
                 "question": "Camry 2019, фильтр масляный 90915-YZZE1 сколько?"},
}
Q = None    # the chosen QUESTIONS entry (main)
CORE_ANCHOR = "нет в каталоге — ищи в прайс-листах."
TOOL_NODE = "Supabase Vector Store"


def patch_clone(wid, kind, text):
    st, wf = E.admin_api("GET", f"/workflows/{wid}")
    assert st == 200, f"GET {wid}: {st}"
    nodes = wf["nodes"]
    if kind == "tool":
        node = next(n for n in nodes if n["name"] == TOOL_NODE)
        node["parameters"]["description"] = text
    elif kind == "core":
        agent = next(n for n in nodes if n["name"] == "AI Agent")
        sm = agent["parameters"]["options"]["systemMessage"]
        assert sm.count(CORE_ANCHOR) == 1, f"the anchor occurs {sm.count(CORE_ANCHOR)} times in the clone's prompt"
        agent["parameters"]["options"]["systemMessage"] = sm.replace(CORE_ANCHOR, CORE_ANCHOR + " " + text)
    else:
        raise SystemExit(f"unknown patch kind {kind!r}")
    st, _ = E.admin_api("PUT", f"/workflows/{wid}", {"name": wf["name"], "nodes": nodes, "connections": wf["connections"], "settings": wf.get("settings", {})})
    st2, after = E.admin_api("GET", f"/workflows/{wid}")
    assert st == 200 and after.get("active") and after.get("activeVersionId") == after.get("versionId"), \
        f"patched clone {wid} not published: PUT {st}, active {after.get('active')}"


def create_clone(o, pid, key):
    """One Create WA as the client sends it from settings (e2e-auth line_create's fields), with the question's vertical and catalog."""
    vid, vlabel = Q["vertical"]
    fields = {"Name": f"pt-{key}", "BusinessType": vlabel, "BusinessTypeId": vid, "Business": "", "Prompt": "",
              "ProductsList": Q["products"], "ServicesList": "", "AppUserID": o.app_user_id, "BotKey": key, "ReplyMode": "semi",
              "ReplyModeVersion": str(int(time.time() * 1000)), "WhatsappProfileId": pid, "TelegramWorkflowId": "-1", "CreateOrigin": "edit"}
    st, d = E.hook_form("CreateWhatsappWorkflow", o.token, fields)
    o.refresh()
    return st, d, (d.get("id") if isinstance(d, dict) else None)


def setup_variant(run_id, name):
    o = E.Owner(f"pt{name}", run_id)
    pid = o.add_profile("whatsapp")
    st, _ = E.rc_purchase(f"{run_id}{name}", o.app_user_id)
    assert st == 200, f"purchase {name}: {st}"
    o.refresh()
    st, d, wid = create_clone(o, pid, f"Bot-pt{name}-{run_id}")
    assert wid, f"create {name}: {st} {d}"
    st, d = E.hook("SetReplyMode", o.token, {"profileIds": [pid], "chatId": "*", "suppressed": False, "version": int(time.time() * 1000)})
    assert st == 200, f"SetReplyMode {name}: {st} {d}"
    fid = str(uuid.uuid4())
    E.pg(f"insert into documents (content, metadata) values ('{Q['price_row']}', jsonb_build_object('botWaId', '{wid}', 'botTgId', '-1', 'fileId', '{fid}'));"
         f"insert into uploaded_files (file_id, owner_id, bot_wa_id, content_type, file_name) values ('{fid}', '{o.install_id}', '{wid}', 'products', 'price.txt');")
    return {"owner": o, "pid": pid, "wid": wid, "fid": fid}


def one_trial(v, name, i, run_id):
    chat = f"7701{random.randrange(10_000_000):07d}@c.us"
    pid, wid = v["pid"], v["wid"]
    for kind, text in Q["dialog"]:
        E.pg(f"insert into n8n_chat_histories (session_id, message) values ('{pid}:{chat}', jsonb_build_object('type', '{kind}', "
             f"'content', '{text}', 'additional_kwargs', '{{}}'::jsonb, 'response_metadata', '{{}}'::jsonb));")
    t = int(time.time())
    mid = f"pt-{run_id}-{name}-{i}"
    E.http("POST", f"{E.MOCK}/__mock/messages", {"profile_id": pid, "chat_id": chat, "messages": [
        {"id": mid, "chatId": chat, "from": chat, "body": Q["question"], "time": t, "fromMe": False, "type": "chat"}]})
    since = E.newest_execution_id(wid)
    t0 = time.time()
    E.http("POST", f"{E.BASE}/webhook/{pid}", E.bot_event(pid, chat, mid, Q["question"], t), timeout=30)
    exs = E.clone_executions(wid, since, wait_s=180)
    ex = exs[0] if exs else {}
    rd = ((ex.get("data") or {}).get("resultData") or {}).get("runData") or {}
    openai = rd.get("OpenAI") or []
    first = (((openai[0].get("data") or {}).get("ai_languageModel") or [[{}]])[0][0].get("json") or {}) if openai else {}
    reply = next((it.get("output") for it in E.node_items(ex, "AI Agent")), "") or ""
    tool_out = json.dumps([r.get("data") for r in rd.get(TOOL_NODE) or []], ensure_ascii=False)
    flat = "".join(reply.split())
    return {"variant": name, "i": i, "t_start": round(t0, 1), "chat": chat, "execution": ex.get("id"), "outcome": E.exec_outcome(ex),
            "searched": len(rd.get(TOOL_NODE) or []) > 0, "openai_calls": len(openai),
            "first_prompt_tokens": (first.get("tokenUsage") or {}).get("promptTokens"), "reply": reply,
            # conflict: the reconciler's mark reached the model; the reply named the price-list price alone (e2e-reply's rule)
            "marked": "Цены расходятся" in tool_out,
            "picked_list_price_alone": "3900" in flat and "3200" not in flat and "расход" not in reply.lower()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--trials", type=int, default=16)
    ap.add_argument("--variant", action="append", required=True, help="before | NAME=tool:<file> | NAME=core:<file>")
    ap.add_argument("--question", choices=sorted(QUESTIONS), default="reask")
    a = ap.parse_args()
    global Q
    Q = QUESTIONS[a.question]
    run_id = uuid.uuid4().hex[:10]
    E.wait_ready()
    variants = {}
    for spec_ in a.variant:
        name, _, patch = spec_.partition("=")
        kind, _, path = patch.partition(":")
        variants[name] = {"kind": kind or None, "text": open(path, encoding="utf-8").read().strip() if path else None}
    results, lock = [], threading.Lock()
    try:
        for name, cfg in variants.items():
            cfg.update(setup_variant(run_id, name))
            if cfg["kind"]:
                patch_clone(cfg["wid"], cfg["kind"], cfg["text"])
            print(f"   {name}: clone {cfg['wid']} on {cfg['pid']}" + (f" patched ({cfg['kind']})" if cfg["kind"] else ""), flush=True)

        def worker(name):
            for i in range(a.trials):
                r = one_trial(variants[name], name, i, run_id)
                with lock:
                    results.append(r)
                    print(f"   {name:8} {i:2d} searched={r['searched']!s:5} tokens={r['first_prompt_tokens']} outcome={r['outcome']} | "
                          f"{r['reply'][:90]!r}", flush=True)
        threads = [threading.Thread(target=worker, args=(n,)) for n in variants]
        for th in threads:
            th.start()
        for th in threads:
            th.join()
    finally:
        os.makedirs(a.out, exist_ok=True)
        with open(os.path.join(a.out, f"real-path-{run_id}.json"), "w", encoding="utf-8") as fh:
            json.dump({"run_id": run_id, "question_kind": a.question, "question": Q["question"], "price_row": Q["price_row"],
                       "dialog": Q["dialog"], "vertical": Q["vertical"][0], "products": Q["products"],
                       "variants": {n: {"kind": c["kind"], "text": c["text"], "clone": c.get("wid")} for n, c in variants.items()},
                       "trials": sorted(results, key=lambda r: r["t_start"])}, fh, ensure_ascii=False, indent=1)
        E.drop_clones(*[c["pid"] for c in variants.values() if c.get("pid")])
        E.drop_files(*[c["fid"] for c in variants.values() if c.get("fid")])
        for o in E.OWNERS:
            try:
                E.fixture(*(["delete", "--app-user-id", o.app_user_id] + [x for p in o.profiles for x in ("--profile-id", p)]))
            except Exception as e:
                print("   cleanup failed:", o.tag, e)
        E.flush_deferred_deletes()
    for name in variants:
        rs = [r for r in results if r["variant"] == name]
        print(f"{name}: searched {sum(r['searched'] for r in rs)}/{len(rs)}, outcome sent {sum(r['outcome'] == 'sent' for r in rs)}/{len(rs)}"
              + (f", price-list price alone {sum(r['picked_list_price_alone'] for r in rs)}/{len(rs)}" if a.question == "conflict" else ""))


if __name__ == "__main__":
    main()
