#!/usr/bin/env python3
"""Структурные проверки learning B (2026-10-14). Без сети. Exit 1 при любой проблеме."""
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
_spec = importlib.util.spec_from_file_location("apply_learning_b", os.path.join(HERE, "apply-learning-b.py"))
LB = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(LB)
problems = []


def expect(cond, msg):
    if not cond:
        problems.append(msg)
        print("FAIL", msg)


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


wf = json.load(open(os.path.join(WF, "jtbssfzXbOxwTK4k-Get_Usage.json"), encoding="utf-8"))
ru, sr, ag = node(wf, "Read Usage"), node(wf, "Shape Response"), node(wf, "Auth Gate")
q = ru["parameters"]["query"]
params = ru["parameters"].get("options", {}).get("queryReplacement", "")
expect(LB.check(wf), "get usage: apply-learning-b.py --check is not satisfied (stale canon)")
for frag in ("line_bots as (", "line_profiles as (", "sends as (", "readiness as (", "row_number() over (partition by lp.bot_key",
             "chat_id not like '%@g.us'", "chat_id not like '-%'", "coalesce(e.origin_set, 0) > 0", "source = 'owner_app'",
             f"s.rn <= {LB.WINDOW}", f">= {LB.MIN_MATCHES}", f">= {LB.MIN_CHATS}", f">= {LB.MIN_DAYS}", "$3::text = 'session'",
             "bp.deleted_at is null",
             # progress percent (2026-10-23): the best window, the percent, the unlock mark and what is missing
             f"rows between {LB.WINDOW - 1} preceding and current row", f"/ {LB.TARGET_MATCHES}", f"least({LB.UNLOCK_PERCENT - 1},",
             "'progress', r.progress", "'needChats', r.need_chats", f"'unlockAt', {LB.UNLOCK_PERCENT}"):
    expect(frag in q, f"get usage: Read Usage lacks `{frag}`")
# The conditions and the best window are HISTORY-wide: the readiness CTE must join every send, never only the current window.
expect("left join sends s on s.bot_key = lb.bot_key\n" in q and "lb.bot_key and s.rn" not in q,
       "get usage: the readiness CTE must join the WHOLE history (no `rn <= window` on the join) — the conditions and the best window are history-wide")
expect(q.count("-- LEARNING-B-BEGIN") == 1 and q.count("-- LEARNING-B-END") == 1 and q.count("LEARNING-B-COL") == 1, "get usage: marker count")
expect("crAuth.installId" in params and "crAuth.mode" in params and "appUserId" in params,
       "get usage: Read Usage params must carry appUserId + installId + mode (Auth Gate)")
expect("row.readiness" in sr["parameters"]["jsCode"] and "LEARNING-B" in sr["parameters"]["jsCode"], "get usage: Shape Response lacks readiness")
expect(ag is not None and wf["connections"]["Authorized?"]["main"][0][0]["node"] == "Read Usage", "get usage: gate wiring changed")
expect(ru.get("onError") == "continueErrorOutput", "get usage: Read Usage must keep its error output (Respond Error)")
# Deploy precondition (review 2026-10-14, Important #1): the readiness CTE sits INSIDE the plan/quota statement — on a database
# without learning A's columns every Get Usage answers 500 (paywall, quota and «Подписка» lose their snapshot, not just the lock).
ROOT = os.path.dirname(os.path.dirname(HERE))
mig = open(os.path.join(HERE, "sql", "2026-09-25-learning-foundation.sql"), encoding="utf-8").read()
expect("origin_set" in mig and "origin_top1" in mig, "precondition: learning A's migration must declare origin_set/origin_top1 (the CTE reads them)")
plan = open(os.path.join(ROOT, "docs", "superpowers", "plans", "2026-10-14-learning-loop-readiness.md"), encoding="utf-8").read()
expect("information_schema.columns" in plan and "origin_top1" in plan,
       "plan: the deploy checklist must check A's columns on the target BEFORE --apply (without them Get Usage fails entirely)")
claude = open(os.path.join(ROOT, "CLAUDE.md"), encoding="utf-8").read()
expect("требует колонок A" in claude, "CLAUDE.md: the Learning-B bullet must state the A-columns dependency and its blast radius")
rep = open(os.path.join(HERE, "report-learning.py"), encoding="utf-8").read()
expect("apply-learning-b" in rep and "readiness" in rep, "report: report-learning.py must import the module's constants and print readiness (Task 2)")
expect("LB.send_filter(" in rep and "LB.window_columns(" in rep and "LB.readiness_columns(" in rep and "deleted_at is null" in rep,
       "report: READINESS_SQL must be rendered from apply-learning-b's fragments and list only bots with a live row (review #7)")
print("learning B verify:", "OK" if not problems else f"{len(problems)} problem(s)")
sys.exit(1 if problems else 0)
