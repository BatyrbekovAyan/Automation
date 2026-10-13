#!/usr/bin/env python3
"""Структурные проверки learning A (2026-09-25). Без сети.

  proxy   — Build Request: секция META, цикл по spec.meta, meta в выходе, jsonBody без origin_*;
            Record Owner Send: 6 колонок origin_* в SQL и 6 значений из meta в params.
  suggest — Gate Decision: stage; Assemble: ХОЛОДНЫЙ СТАРТ + ПОСТОРОННИЙ ВОПРОС;
            Respond → Has Set? → Record Set (после ответа, fail-open); никаких новых узлов ДО Respond.
  delete  — Delete Bot Files: CTE `sets` в Delete Chat Memory (по линии), `deletedSuggestionSets` в Deletion Settled? и Respond, после Respond — ничего (V12).
  sweep   — Profile Lifecycle Sweep: Every 6 Hours → Expire Learning Rows (180 дней).
  prompts — _core.md «Границы темы» (без плейсхолдеров; имя — строка «Название» в About Business, пишет Manager.ComposeBusinessKnowledge); _universal.md разрешает честный ответ; Assemble: общее правило ПОСТОРОННИЙ ВОПРОС (LEARNING-A-OFFTOPIC-RULE) + оговорка в ХОЛОДНОМ СТАРТЕ.

    python3 Tools/n8n/verify-learning-a.py            # все группы
    python3 Tools/n8n/verify-learning-a.py --only proxy
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
PROMPTS = os.path.join(HERE, "prompts")
FAILS = []


def load(f):
    with open(os.path.join(WF, f), encoding="utf-8") as fh:
        return json.load(fh)


def node(wf, name):
    n = next((x for x in wf["nodes"] if x["name"] == name), None)
    if n is None:
        FAILS.append(f"node {name!r} missing")
    return n


def edges(wf, src, idx=0):
    outs = wf["connections"].get(src, {}).get("main", [])
    return [e["node"] for e in outs[idx]] if idx < len(outs) else []


def expect(cond, msg):
    if not cond:
        FAILS.append(msg)


def verify_proxy():
    wf = load("crWappiProxy0001-Wappi_Proxy.json")
    br = node(wf, "Build Request")["parameters"]["jsCode"]
    expect("const META = {" in br, "proxy: META allow-list missing")
    expect("'message.send':" in br and "meta: META" in br.split("'message.send':", 1)[1].split("\n", 1)[0], "proxy: message.send has no meta section")
    reply_line = br.split("'message.reply':", 1)[1].split("\n", 1)[0]
    expect("meta: META" in reply_line or "...META" in reply_line, "proxy: message.reply has no meta section")   # reply spreads META + chat_id
    expect("Object.entries(spec.meta || {})" in br, "proxy: meta loop missing")
    expect(re.search(r"return \[\{ json: \{ ok: true, _status: 200, op, channel, profileId: pid, method: spec\.m, url: BASE\[channel\] \+ path, query, meta,", br), "proxy: meta not emitted by Build Request")
    expect("jsonBody: spec.body ? body : {}" in br, "proxy: jsonBody must stay body-only (origin must never reach Wappi)")
    for f in ("origin_kind", "origin_index", "origin_key", "origin_edit", "origin_top1", "origin_set"):
        expect(f"{f}: ['{f}'" in br, f"proxy: META lacks {f}")
    expect("chat_id: ['chat_id', ID, 'opt']" in br.split("'message.reply':", 1)[1].split("\n", 1)[0], "proxy: message.reply meta lacks chat_id")
    rec = node(wf, "Record Owner Send")
    q = rec["parameters"]["query"]
    p = rec["parameters"]["options"]["queryReplacement"]
    for f in ("origin_kind", "origin_index", "origin_key", "origin_edit", "origin_top1", "origin_set"):
        expect(f in q, f"proxy: Record Owner Send SQL lacks {f}")
        expect(f"m.{f}" in p, f"proxy: Record Owner Send params lack meta.{f}")
    expect("$12" in q and "$13" not in q, "proxy: Record Owner Send must take exactly 12 params")
    expect("b.recipient || m.chat_id" in p, "proxy: Record Owner Send must fall back to meta.chat_id for a Telegram reply")
    expect(edges(wf, "Respond") == ["Owner Send?"] and edges(wf, "Owner Send?") == ["Record Owner Send"], "proxy: Respond → Owner Send? → Record Owner Send chain drifted")


def verify_suggest():
    wf = load("9PTyYcelRQI7bGDb-Suggest_Replies.json")
    gd = node(wf, "Gate Decision")["parameters"]["jsCode"]
    expect("LEARNING-A-STAGE-BEGIN" in gd and "stage }" in gd, "suggest: Gate Decision does not emit stage")
    asm = node(wf, "Assemble")["parameters"]["jsCode"]
    expect("ХОЛОДНЫЙ СТАРТ" in asm and "LEARNING-A-OFFTOPIC-RULE" in asm, "suggest: Assemble lacks cold start / the off-topic rule")
    expect("$('Gate Decision').first().json.stage" in asm, "suggest: Assemble must read stage from Gate Decision")
    expect(edges(wf, "Respond") == ["Has Set?"], "suggest: Respond must lead ONLY to Has Set?")
    expect(edges(wf, "Has Set?", 0) == ["Record Set"] and edges(wf, "Has Set?", 1) == [], "suggest: Has Set? true → Record Set, false → nothing")
    rs = node(wf, "Record Set")
    if rs:
        expect(rs["type"] == "n8n-nodes-base.postgres" and rs.get("onError") == "continueRegularOutput", "suggest: Record Set must be a fail-open Postgres node")
        expect("insert into suggestion_sets" in rs["parameters"]["query"] and "on conflict (profile_id, request_key) do nothing" in rs["parameters"]["query"], "suggest: Record Set SQL shape")
        expect(edges(wf, "Record Set") == [], "suggest: Record Set must be terminal")
    before_respond = {"Webhook", "Auth Gate", "Authorized?", "Respond 401", "Prep", "If invalid?", "Suggestion Gate", "Gate Decision", "If Gate Allows", "If skipRag?",
                      "If channel TG?", "Retrieve RAG", "Retrieve RAG TG", "Embeddings", "Has Exact Tokens?", "Exact Match", "Assemble", "LLM", "Validate", "If ok?",
                      "LLM Retry", "Validate 2", "Build Response", "Respond"}
    names = {n["name"] for n in wf["nodes"]}
    expect(names - before_respond <= {"Has Set?", "Record Set"}, f"suggest: unexpected nodes on the hot path: {sorted(names - before_respond - {'Has Set?', 'Record Set'})}")


def verify_delete():
    wf = load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json")
    q = node(wf, "Delete Chat Memory")["parameters"]["query"]
    expect("delete from public.suggestion_sets" in q.lower() and "lineage_profiles" in q, "delete: Delete Chat Memory must delete suggestion_sets by the bot's line (apply-history-handoff.DELETE_NEW_TAIL)")
    expect("deletedSuggestionSets" in node(wf, "Deletion Settled?")["parameters"]["conditions"]["conditions"][0]["leftValue"], "delete: Deletion Settled? must judge deletedSuggestionSets (apply-auth-gate.DELETION_SETTLED)")
    expect('"deletedSuggestionSets"' in node(wf, "Respond")["parameters"]["responseBody"], "delete: Respond must report deletedSuggestionSets")
    expect(edges(wf, "Respond") == [], "delete: nothing may run after the answer (V12) — no Delete Learning Rows node")


def verify_sweep():
    wf = load("fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json")
    expect("Expire Learning Rows" in edges(wf, "Every 6 Hours"), "sweep: Every 6 Hours → Expire Learning Rows missing")
    n = node(wf, "Expire Learning Rows")
    if n:
        q = n["parameters"]["query"]
        expect("interval '180 days'" in q and "suggestion_sets" in q and "origin_key = null" in q and "origin_kind is not null" in q, "sweep: Expire Learning Rows SQL shape")


def verify_prompts():
    core = open(os.path.join(PROMPTS, "_core.md"), encoding="utf-8").read()
    expect("Границы темы." in core and "строка «Название»" in core and "вы человек или бот" in core and "не называй себя человеком" in core, "prompts: _core.md lacks the three-class rule")
    expect("{businessName}" not in core, "prompts: no placeholder in the shared head — V10 classifies heads by content (deviation 13)")
    universal = open(os.path.join(PROMPTS, "_universal.md"), encoding="utf-8").read()
    expect("вы бот / человек" in universal and "за человека себя не выдавай" in universal and "{businessName}" not in universal, "prompts: _universal.md still forbids the honest answer (or carries a placeholder)")
    asm = node(load("9PTyYcelRQI7bGDb-Suggest_Replies.json"), "Assemble")["parameters"]["jsCode"]
    expect("LEARNING-A-OFFTOPIC-RULE" in asm and "РОВНО ОДНУ карточку" in asm and "label «Не по теме»" in asm, "prompts: Assemble lacks the general off-topic rule")
    expect("кроме ПОСТОРОННЕГО ВОПРОСА" in asm, "prompts: cold start must not re-assert «4 варианта» for an off-topic question")
    mgr = open(os.path.join(os.path.dirname(os.path.dirname(HERE)), "Assets", "Scripts", "Main", "Manager.cs"), encoding="utf-8").read()
    expect('Append("Название: ")' in mgr, "prompts: Manager.ComposeBusinessKnowledge does not write the «Название:» line the rule relies on")


GROUPS = {"proxy": verify_proxy, "suggest": verify_suggest, "delete": verify_delete, "sweep": verify_sweep, "prompts": verify_prompts}


def main():
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    for name, fn in GROUPS.items():
        if only and only != name:
            continue
        fn()
    for f in FAILS:
        print("FAIL", f)
    print("learning A verify:", "OK" if not FAILS else f"{len(FAILS)} problem(s)")
    sys.exit(1 if FAILS else 0)


if __name__ == "__main__":
    main()
