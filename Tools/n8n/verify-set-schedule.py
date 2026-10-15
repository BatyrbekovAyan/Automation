#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Structural verifier for the schedule webhook crSetSchedule001 (POST /webhook/SetSchedule) — B1 «расписание бота»
(2026-10-14). No network. Behaviour lives in tests/test_set_schedule.py (the real Validate / statement / Shape Response
on real SQL, and this verifier's negatives on scratch copies); this file pins what that behaviour rests on:

  (1) the workflow IS build-set-schedule.py's output (edit the generator, never the JSON);
  (2) no node is disabled (n8n hands a disabled node's input on untouched — a disabled gate admits everyone, a disabled
      Validate hands the raw body to the statement, a disabled Shape Response answers whatever came in);
  (3) the gate is TOKEN ONLY and profile-scoped: RULE {profiles: [profileIds], legacy: false}; the Webhook feeds nothing
      but the gate; refused calls answer 401 / 403;
  (4) Validate re-intersects the ids with the token's profiles, drops the sentinels, de-duplicates, takes at most ten, and
      checks every field (enabled boolean, daysMask 0..127, startMin / endMin 0..1439, an IANA-shaped zone, a positive
      integer version); a refusal answers 400 bad_request with the reason;
  (5) the statement is the generator's: ONE version-guarded upsert over jsonb_array_elements_text of the first parameter,
      a per-profile report (applied, version); it runs ONLY through Authorized? true and Valid? true, continues on error
      and feeds Shape Response alone;
  (6) Shape Response answers success only on one well-formed row per requested profile, the trigger's refusal as 400
      bad_timezone, any other fault as 500 db_unavailable; Respond answers its status;
  (7) crSetSchedule001 is a protected infra id: build-client-webhooks.PROTECTED_IDS and the four dependents regenerated
      from it (Set Workflow State, Create WA/TG, Delete Bot Files, Profile Lifecycle Sweep);
  (8) the migration creates bot_schedule as the spec states — the checks, the zone trigger, RLS on, client roles revoked.

    python3 Tools/n8n/verify-set-schedule.py              # the canonical workflows
    python3 Tools/n8n/verify-set-schedule.py --dir DIR    # another set of exports (mutation tests)
"""
import argparse
import importlib.util
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


B = _module("build_set_schedule", "build-set-schedule.py")
BCW = _module("build_client_webhooks", "build-client-webhooks.py")
EXPECTED = ("Webhook", "Auth Gate", "Authorized?", "Respond 401", "Validate", "Valid?", "Respond 400", "Upsert Schedule",
            "Shape Response", "Respond")
# The four dependents of build-client-webhooks.PROTECTED_IDS and the nodes that carry the protected list there (the SQL
# exclusion lists of clone_lineage / apply-sweep-clone-deactivate, Set Workflow State's own Validate).
DEPENDENTS = {"0DP79STI3pafXO9B-Set_Workflow_State.json": ("Validate",),
              "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json": ("Clone Plan", "Bind Workflow Id"),
              "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json": ("Clone Plan", "Bind Workflow Id"),
              "lmjYsdNcQA2IE5rl-Delete_Bot_Files.json": ("List Bot Originals", "Retire Bot Profiles", "Delete Bot Chunks", "Delete Chat Memory"),
              "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json": ("Clone Retirements",)}
MIGRATION = os.path.join(HERE, "sql", "2026-10-14-bot-schedule.sql")


def targets(wf, src):
    return [[e["node"] for e in (out or [])] for out in (wf["connections"].get(src) or {}).get("main", [])]


def sources(wf, dst):
    return sorted((s, i) for s, c in wf["connections"].items() for i, out in enumerate((c or {}).get("main") or [])
                  for e in (out or []) if e.get("node") == dst)


def reachable(wf, start, cut=None):
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for i, out in enumerate((wf["connections"].get(n) or {}).get("main") or []):
            if cut and (n, i) == cut:
                continue
            todo.extend(e["node"] for e in (out or []))
    return seen


def condition(wf, name):
    n = next((x for x in wf["nodes"] if x["name"] == name), None)
    conds = (((n or {}).get("parameters") or {}).get("conditions") or {}).get("conditions") or [{}]
    return conds[0].get("leftValue") if len(conds) == 1 else None


def check(wf_dir):
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)

    path = os.path.join(wf_dir, B.WF_FILE)
    if not os.path.exists(path):
        return [f"(1) {B.WF_FILE} is missing — run build-set-schedule.py"]
    raw = open(path, encoding="utf-8").read()
    wf = json.loads(raw)
    N = {n["name"]: n for n in wf.get("nodes", [])}

    # (1) the generator's output
    need(raw == B.text(), f"(1) {B.WF_FILE} differs from build-set-schedule.py's output — edit the generator, never the JSON")
    need(wf.get("id") == B.WF_ID and sorted(N) == sorted(EXPECTED), f"(1) the workflow's id / node set changed: {wf.get('id')} {sorted(N)}")
    if sorted(N) != sorted(EXPECTED):
        return fails

    # (2) nothing disabled
    for name, n in N.items():
        need(not n.get("disabled"), f"(2) {name} is disabled — n8n hands its input on untouched")

    # (3) the gate: token only, profile-scoped
    g = N["Auth Gate"]["parameters"].get("jsCode", "")
    need(f"const RULE = {json.dumps(B.RULE, ensure_ascii=False)};" in g and '"legacy": false' in g and '"profiles": ["profileIds"]' in g,
         "(3) Auth Gate must be token ONLY (legacy false) and scope profileIds to the token's profiles")
    need(targets(wf, "Webhook") == [["Auth Gate"]] and targets(wf, "Auth Gate") == [["Authorized?"]],
         f"(3) the Webhook must feed the gate alone: {targets(wf, 'Webhook')} / {targets(wf, 'Auth Gate')}")
    need(condition(wf, "Authorized?") == "={{ $json.crAuth.ok }}" and targets(wf, "Authorized?") == [["Validate"], ["Respond 401"]],
         f"(3) Authorized?: true → Validate, false → Respond 401: {targets(wf, 'Authorized?')}")
    r401 = N["Respond 401"]["parameters"]
    need(r401.get("responseBody") == B.RESPOND_401_BODY and (r401.get("options") or {}).get("responseCode") == B.RESPOND_401_CODE,
         "(3) Respond 401 must answer 403 for a forbidden profile and 401 otherwise, success:false")

    # (4) Validate
    v = N["Validate"]["parameters"].get("jsCode", "")
    for frag, why in (("const owned = new Set((wh.crAuth && wh.crAuth.profiles) || []);", "the token's profiles"),
                      ("x !== '' && x !== '-1' && owned.has(x)", "the sentinels dropped, the rest re-intersected with the token"),
                      (f".slice(0, {B.MAX_PROFILES});", f"at most {B.MAX_PROFILES} ids"),
                      ("Array.from(new Set(", "de-duplicated ids"),
                      ("if (typeof b.enabled !== 'boolean') return bad('enabled');", "enabled a boolean"),
                      ("if (!within(b.daysMask, 0, 127)) return bad('daysMask');", "daysMask 0..127"),
                      ("if (!within(b.startMin, 0, 1439)) return bad('startMin');", "startMin 0..1439"),
                      ("if (!within(b.endMin, 0, 1439)) return bad('endMin');", "endMin 0..1439"),
                      (f"if (tz.length > {B.TZ_MAX_LEN} || !/{B.TZ_PATTERN.replace('/', chr(92) + '/')}/.test(tz)) return bad('timeZone');",
                       "the IANA shape of the zone"),
                      ("if (!version) return bad('version');", "a positive integer version")):
        need(frag in v, f"(4) Validate lacks {why}: {frag!r}")
    need(condition(wf, "Valid?") == "={{ $json.ok }}" and targets(wf, "Valid?") == [["Upsert Schedule"], ["Respond 400"]],
         f"(4) Valid?: true → Upsert Schedule, false → Respond 400: {targets(wf, 'Valid?')}")
    r400 = N["Respond 400"]["parameters"]
    need(r400.get("responseBody") == B.RESPOND_400_BODY and (r400.get("options") or {}).get("responseCode") == 400,
         "(4) Respond 400 must answer 400 bad_request with the reason")

    # (5) the statement
    up = N["Upsert Schedule"]
    q = up["parameters"].get("query", "")
    need(up.get("type") == "n8n-nodes-base.postgres" and up["parameters"].get("operation") == "executeQuery" and q == B.UPSERT_SQL,
         "(5) Upsert Schedule's query differs from build-set-schedule.UPSERT_SQL — edit the generator, never the JSON")
    need("where bot_schedule.version < excluded.version" in q and "jsonb_array_elements_text($1::jsonb)" in q
         and q.count("insert into bot_schedule") == 1 and "on conflict (profile_id) do update" in q,
         "(5) Upsert Schedule must be ONE version-guarded upsert over the requested ids (a stale version never overwrites)")
    need(re.search(r'\(up\.profile_id is not null\) as applied', q) and '"profileId"' in q,
         "(5) Upsert Schedule must report, per profile, whether it was applied")
    need((up["parameters"].get("options") or {}).get("queryReplacement") == B.UPSERT_PARAMS,
         "(5) Upsert Schedule's parameters must be Validate's checked values (the single array form)")
    need(up.get("onError") == "continueRegularOutput" and up.get("alwaysOutputData") is True,
         "(5) Upsert Schedule must continue on error with output — a fault is judged by Shape Response (400 / 500), never a crash")
    need(sources(wf, "Upsert Schedule") == [("Valid?", 0)] and targets(wf, "Upsert Schedule") == [["Shape Response"]],
         f"(5) Upsert Schedule entered from {sources(wf, 'Upsert Schedule')}, feeding {targets(wf, 'Upsert Schedule')}")
    for gate in (("Authorized?", 0), ("Valid?", 0)):
        need("Upsert Schedule" not in reachable(wf, "Webhook", cut=gate),
             f"(5) Upsert Schedule is reachable without passing {gate[0]} true — a refused call could still write")

    # (6) the answer
    s = N["Shape Response"]["parameters"].get("jsCode", "")
    need(s == B.SHAPE_JS, "(6) Shape Response differs from build-set-schedule.SHAPE_JS")
    need("rows.every((r) => r.error === undefined && typeof r.profileId === 'string' && typeof r.applied === 'boolean')" in s
         and "rows.length === requested.length" in s,
         "(6) Shape Response must answer success only on one well-formed row per requested profile")
    need(f"said.includes('{B.TZ_MARKER}')" in s and "error: 'bad_timezone', _status: 400" in s and "error: 'db_unavailable', _status: 500" in s,
         "(6) Shape Response must answer the trigger's refusal as 400 bad_timezone and every other fault as 500 db_unavailable")
    need(targets(wf, "Shape Response") == [["Respond"]] and sources(wf, "Respond") == [("Shape Response", 0)],
         "(6) Shape Response must feed Respond alone")
    rp = N["Respond"]["parameters"]
    need((rp.get("options") or {}).get("responseCode") == "={{ $json._status || 200 }}" and "_status" in rp.get("responseBody", ""),
         "(6) Respond must answer Shape Response's status and hide _status")

    # (7) protected infra id
    need(B.WF_ID in BCW.PROTECTED_IDS, f"(7) {B.WF_ID} is missing from build-client-webhooks.PROTECTED_IDS (protected infra ids)")
    for f, holders in DEPENDENTS.items():
        p = os.path.join(wf_dir, f)
        dep = json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {"nodes": []}
        for h in holders:
            n = next((x for x in dep["nodes"] if x["name"] == h), {})
            text = (n.get("parameters") or {}).get("query") or (n.get("parameters") or {}).get("jsCode") or ""
            need(f"'{B.WF_ID}'" in text,
                 f"(7) {f} / {h} does not carry {B.WF_ID} in its protected list — regenerate it from build-client-webhooks.PROTECTED_IDS")

    # (8) the table (read without its `--` comments: they explain, the statements decide)
    m = open(MIGRATION, encoding="utf-8").read() if os.path.exists(MIGRATION) else ""
    m = "\n".join(re.sub(r"--.*$", "", line) for line in m.split("\n"))
    need("create table if not exists public.bot_schedule (" in m
         and re.search(r"profile_id text primary key", m) and re.search(r"enabled\s+boolean not null", m)
         and re.search(r"days_mask\s+int not null check \(days_mask between 0 and 127\)", m)
         and re.search(r"start_min\s+int not null check \(start_min between 0 and 1439\)", m)
         and re.search(r"end_min\s+int not null check \(end_min between 0 and 1439\)", m)
         and re.search(r"tz\s+text not null default 'Asia/Almaty'", m) and re.search(r"version\s+bigint not null default 0", m)
         and re.search(r"updated_at timestamptz", m),
         "(8) sql/2026-10-14-bot-schedule.sql must create bot_schedule (profile_id pk, enabled, days_mask 0..127, start_min / "
         "end_min 0..1439, tz default Asia/Almaty, version, updated_at)")
    need("create or replace function public.bot_schedule_tz_check()" in m and "pg_catalog.pg_timezone_names" in m
         and f"raise exception '{B.TZ_MARKER}: %'" in m and "security definer" not in m
         and "drop trigger if exists bot_schedule_tz_check on public.bot_schedule;" in m
         and "before insert or update on public.bot_schedule" in m,
         "(8) the zone trigger (bot_schedule_tz_check, before insert or update, pg_timezone_names, the bad-timezone message, "
         "not security definer, re-runnable) is missing or changed")
    need("alter table public.bot_schedule enable row level security;" in m
         and "revoke all on table public.bot_schedule from anon, authenticated;" in m,
         "(8) bot_schedule must have RLS on and the client roles revoked")
    return fails


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=os.path.join(HERE, "workflows"))
    a = ap.parse_args()
    fails = check(a.dir)
    for f in fails:
        print(f"VERIFY SET SCHEDULE FAIL [{f.split(')')[0].strip('(')}] {f}")
    if fails:
        print(f"VERIFY SET SCHEDULE FAILED: {len(fails)} finding(s)")
        sys.exit(1)
    print("ALL SET SCHEDULE ASSERTS PASSED")


if __name__ == "__main__":
    main()
