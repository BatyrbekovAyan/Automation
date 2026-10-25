#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Structural verifier for «Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md,
plan docs/superpowers/plans/2026-10-25-personal-chats.md). No network. Behaviour lives in tests/test_*.py (the real SQL, the real
Code nodes, the n8n expression engine); this file pins what that behaviour rests on. One GROUP per piece of the feature — a
function `(directory) -> (findings, one-line summary)` registered in GROUPS — and every task of the plan that adds a
piece adds its group here, so `python3 Tools/n8n/verify-personal-chats.py` is the one command that answers «is the server half
still wired as designed».

  wipe — Task 8: the Profile Lifecycle Sweep's «Wipe Personal Chats» (apply-personal-wipe.py; audience.WIPE_SQL):
      (1) the node is a Postgres executeQuery whose query IS audience.WIPE_SQL — no hand edit, no n8n expression, no parameter;
      (2) the sweep's own Postgres credential (the canonical one), fail-open (onError continueRegularOutput), not disabled — a
          disabled node hands its input on untouched, i.e. silently does nothing;
      (3) exactly one such node, a target of Every 6 Hours exactly once and entered from NOTHING else (not behind Sweep Config,
          not behind a dry-run If: the step deletes by the age of the owner's decision, not by the state of a profile), terminal,
          unreachable from Sweep Config;
      (4) the statement keeps its safety shape: it deletes from the eight per-chat tables and from nothing else (never
          dialog_counts — the tariff usage —, never the per-bot rows, never the decision itself), the chat's own reply_mode_flags
          row only (never the bot-wide '*' row), takes the work list locked and skipping what is locked, at most WIPE_BATCH
          oldest first, owner decisions only, not yet wiped, older than WIPE_DAYS written as hours, and marks wiped_at.
  readers — Task 5: the three server places outside the bot templates that write or read a chat pass only a BUSINESS chat
      (audience.BUSINESS_SQL — never «not personal»):
      (1) the Wappi Proxy's Record Owner Send opens with the business word keyed on its profile and chat parameters, both
          inserts (history, memory) stand behind it, the answer names it;
      (2) Suggest Replies: Suggestion Gate gets Prep's profileId and chatId as its third and fourth parameters, opens with the
          business word keyed on them, both counter inserts and the verdict stand behind it; Gate Decision opens the panel
          only on the positive word (never fails open) and labels the refusal personal_chat before any account reason; Build
          Response names personal_chat to an authenticated session caller only;
      (3) Dashboard Outcomes: Find Changed Sessions keeps only business sessions (the session split at its FIRST colon) inside
          `changed`, before its LIMIT; Fetch Outcomes returns verdicts and open handoffs of business chats only.
  webhook — Task 7: the app's ChatAudience webhook crChatAudience01 (build-chat-audience.py; behaviour in
      tests/test_chat_audience.py):
      (1) the workflow IS the generator's output, with exactly its nodes, none disabled; successful executions not saved;
      (2) the gate is TOKEN ONLY and scopes profileIds / profileId to the token; Validate re-intersects with the token, drops
          the sentinels, keeps the limits (20 profiles, 50 chats), refuses the reply mode's wildcard '*', an empty / over-long
          / whitespace chat id, a wrong kind or version; a refused call reaches no statement and no Wappi call;
      (3) the op fork is three Ifs on op; List / Set / Connected / Backfill carry audience.py's statements exactly (Backfill
          composed with apply-history-handoff.py's MEMORY_ROW and OWNER_CONTENT_SQL), every statement continues on error and
          its Shape node judges the rows' positive fields;
      (4) the owner's answer goes out BEFORE the tail: Replay Items is entered from Respond Set alone, and with that one edge
          cut nothing of the tail is reachable;
      (5) the Wappi reads (Recent Messages, Status): the canonical Wappi credential, fullResponse + neverError, at most 10 s,
          only the two Wappi bases, messages/get with limit 10 / order desc, continue on error; mark_all nowhere;
      (6) the replay: only through Replay? (the backfill's committed row AND the plan's positive word), POST to the bot's own
          webhook on localhost, no credential, neverError, at most 10 s; Replay Plan runs per item, takes FRESH_HOURS from
          audience.py, checks the page is this profile's and chat's, hands on only a row whose own `from` is the chat (never a
          group member's message — the templates' group guard «If» from == chatId must still see the real sender, so the
          message keeps that `from`), never replays a voice message, builds the message in ONE function; Phone embeds
          build-handoff-workflows.OWNER_PHONE_JS verbatim and needs the echo and the boolean flag;
      (7) crChatAudience01 is a protected infra id: build-client-webhooks.PROTECTED_IDS, staging/bootstrap.py
          ACTIVE_CANONICAL, Set Workflow State and the dependents regenerated from the list.

Every group carries its own tripwire (WIPE_MUTATIONS for `wipe`, READER_MUTATIONS for `readers`, WEBHOOK_MUTATIONS for
`webhook`): a list of (label, mutation, the message the checks must produce); a normal run applies each one to a copy of a
PASSING workflow and fails if the checks accept it — a tripwire that accepts a broken sweep is no tripwire.

    python3 Tools/n8n/verify-personal-chats.py                 # every group
    python3 Tools/n8n/verify-personal-chats.py --only wipe     # one group
    python3 Tools/n8n/verify-personal-chats.py --dir DIR       # another set of exports (mutation tests)
"""
import argparse
import copy
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


AUD = _module("audience", "audience.py")

SWEEP_FILE = "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json"
TRIGGER = "Every 6 Hours"
WIPE_NODE = "Wipe Personal Chats"
# The sweep's canonical Postgres credential — written here on its own (not read from the generator that wrote the node), and
# required to be the very one the sweep's older nodes use.
CANONICAL_PG = {"postgres": {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}}
# What the step deletes from: the set Delete Chat Memory clears per CHAT — the per-bot rows (bot_schedule,
# audience_connections), the tariff usage (dialog_counts) and the decision itself (chat_audience) are not chat data.
CHAT_TABLES = ("chat_history_events", "chat_reply_state", "conversation_outcomes", "handoff_requests", "n8n_chat_histories",
               "reply_events", "reply_mode_flags", "suggestion_sets")
NEVER_NAMED = ("dialog_counts", "bot_schedule", "audience_connections", "bot_profiles")


def node(wf, name):
    return next((n for n in wf.get("nodes", []) if n.get("name") == name), None)


def targets(wf, src):
    return [[e["node"] for e in (out or [])] for out in (wf["connections"].get(src) or {}).get("main", [])]


def sources(wf, dst):
    return sorted((s, i) for s, c in wf["connections"].items() for i, out in enumerate((c or {}).get("main") or [])
                  for e in (out or []) if e.get("node") == dst)


def reachable(wf, start, cut=None):
    """The nodes reachable from `start`; `cut` = (node, output index) is an output not followed."""
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


def code_of(sql):
    """The statement without its `--` comments (they explain, the statement decides; the comment names dialog_counts on purpose)."""
    return "\n".join(re.sub(r"--.*$", "", line) for line in sql.split("\n"))


# ================================================================================================ group: wipe
def check_wipe(sweep, sql=None):
    """Findings for the Sweep's wipe node. `sql` is the statement the node must carry (default: audience.WIPE_SQL)."""
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)
        return cond

    expected = AUD.WIPE_SQL if sql is None else sql
    names = [n.get("name") for n in sweep.get("nodes", [])]
    if not need(names.count(WIPE_NODE) == 1, f"expected exactly one node {WIPE_NODE!r}, found {names.count(WIPE_NODE)}"):
        return fails
    n = node(sweep, WIPE_NODE)
    p = n.get("parameters") or {}
    q = p.get("query") or ""

    # (1) the statement, as the module wrote it
    need(n.get("type") == "n8n-nodes-base.postgres" and p.get("operation") == "executeQuery",
         f"{WIPE_NODE} must be a Postgres executeQuery node: {n.get('type')} {p.get('operation')}")
    need(q == expected, f"{WIPE_NODE}'s query differs from audience.WIPE_SQL — edit the module and re-run apply-personal-wipe.py, never the JSON")
    need(not q.lstrip().startswith("=") and "{{" not in q, f"{WIPE_NODE}'s query must be plain text — no n8n expression (a dry-run flag read there would gate it)")
    need(not (p.get("options") or {}).get("queryReplacement"), f"{WIPE_NODE} takes no parameters: {p.get('options')}")

    # (2) credential, fail-open, enabled
    ref = node(sweep, "Candidates")
    need(n.get("credentials") == CANONICAL_PG and bool(ref) and n.get("credentials") == ref.get("credentials"),
         f"{WIPE_NODE} must run on the canonical Postgres credential the sweep's other nodes use {CANONICAL_PG}: {n.get('credentials')}")
    need(n.get("onError") == "continueRegularOutput",
         f"{WIPE_NODE} must continue on error (onError continueRegularOutput): the statement is one transaction, a failed run changes "
         f"nothing and the next run takes the same work list: {n.get('onError')}")
    need(not n.get("disabled"), f"{WIPE_NODE} is disabled — n8n hands a disabled node's input on untouched, so the wipe would silently never run")

    # (3) the wiring: straight on the trigger, entered from nothing else, terminal
    from_trigger = (targets(sweep, TRIGGER) or [[]])[0]
    need(from_trigger.count(WIPE_NODE) == 1, f"{WIPE_NODE} must be a target of {TRIGGER} exactly once: {targets(sweep, TRIGGER)}")
    need(sources(sweep, WIPE_NODE) == [(TRIGGER, 0)],
         f"{WIPE_NODE} must be entered ONLY from {TRIGGER} — not behind Sweep Config or a dry-run gate: {sources(sweep, WIPE_NODE)}")
    need(WIPE_NODE not in reachable(sweep, "Sweep Config"),
         f"{WIPE_NODE} is reachable from Sweep Config — the dry-run flags live there and must never gate this step")
    successors = (sweep["connections"].get(WIPE_NODE) or {}).get("main") or []
    need(all(not out for out in successors), f"{WIPE_NODE} must be terminal — nothing runs after it: {sweep['connections'].get(WIPE_NODE)}")

    # (4) the safety shape of the statement (read without its comments)
    flat = " ".join(code_of(expected).lower().split())
    deleted = sorted(re.findall(r"delete from (\w+)", flat))
    need(deleted == sorted(CHAT_TABLES),
         f"the statement must delete from exactly the per-chat tables {sorted(CHAT_TABLES)}, once each: {deleted}")
    for name in NEVER_NAMED:
        need(name not in flat, f"the statement must never touch {name} (the tariff usage / per-bot rows / the decision are not a chat's data)")
    need("delete from chat_audience" not in flat, "the statement must never delete the decision itself — the chat has to stay personal")
    flags = re.search(r"delete from reply_mode_flags .*?returning 1", flat)
    need(bool(flags) and "<> '*'" in flags.group(0),
         "the reply_mode_flags delete must leave the bot-wide row (chat id '*') alone — it is the bot's default, not a chat's data")
    need("for update skip locked" in flat,
         "the work list must be taken `for update skip locked` — a row the owner's write holds is left alone, never waited for and never wiped")
    need(f"limit {AUD.WIPE_BATCH}" in flat and "order by a.personal_since" in flat,
         f"the batch must be the {AUD.WIPE_BATCH} oldest decisions (order by personal_since, limit {AUD.WIPE_BATCH})")
    need("a.source = 'owner'" in flat and "a.kind = 'personal'" in flat and "a.wiped_at is null" in flat and "a.personal_since <=" in flat,
         "the work list must be the OWNER's personal decisions with a clock, not wiped yet, old enough (source / kind / wiped_at / personal_since)")
    need(f"now() - interval '{AUD.WIPE_DAYS * 24} hours'" in flat,
         f"the delay must be WIPE_DAYS ({AUD.WIPE_DAYS}) written as {AUD.WIPE_DAYS * 24} hours")
    need(flat.count("update chat_audience") == 1 and "set wiped_at = now()" in flat,
         "exactly one update must mark the decisions wiped (set wiped_at = now())")
    need("$" not in expected, "the statement takes no parameter — no dollar sign anywhere")
    return fails


def _on_wipe(mutate):
    def m(sweep):
        mutate(node(sweep, WIPE_NODE))
    return m


def _set(key, value):
    return _on_wipe(lambda n: n.__setitem__(key, copy.deepcopy(value)))


def _drop(key):
    return _on_wipe(lambda n: n.pop(key))


def _trigger_edge(present):
    def m(sweep):
        outs = sweep["connections"][TRIGGER]["main"][0]
        outs[:] = [e for e in outs if e["node"] != WIPE_NODE]
        if present:
            outs.append({"node": WIPE_NODE, "type": "main", "index": 0})
    return m


def _gate(src, index):
    """Move the wipe behind `src`'s output `index` (a gate): out of the trigger, into src."""
    def m(sweep):
        _trigger_edge(False)(sweep)
        outs = sweep["connections"][src]["main"]
        while len(outs) <= index:
            outs.append([])
        outs[index].append({"node": WIPE_NODE, "type": "main", "index": 0})
    return m


def _successor(sweep):
    sweep["connections"][WIPE_NODE] = {"main": [[{"node": "Stamp Deleted", "type": "main", "index": 0}]]}


def _duplicate(sweep):
    twin = copy.deepcopy(node(sweep, WIPE_NODE))
    twin["id"] = "00000000-0000-0000-0000-000000000000"
    sweep["nodes"].append(twin)


def _sql(old, new):
    """A change to the STATEMENT: the node carries it and the checks are told to expect it, so only the safety shape can object."""
    def m(sweep):
        base = AUD.WIPE_SQL
        assert old in base, f"mutation anchor not found in audience.WIPE_SQL: {old[:70]!r}"
        changed = base.replace(old, new, 1)
        node(sweep, WIPE_NODE)["parameters"]["query"] = changed
        return changed
    return m


WIPE_MUTATIONS = (
    ("the node disabled", _set("disabled", True), "is disabled"),
    ("the node's type changed", _set("type", "n8n-nodes-base.set"), "must be a Postgres executeQuery node"),
    ("onError removed (a failing run would stop the sweep)", _drop("onError"), "must continue on error"),
    ("onError set to stop", _set("onError", "stopWorkflow"), "must continue on error"),
    ("another Postgres credential", _set("credentials", {"postgres": {"id": "dev0000000000000", "name": "Postgres Dev"}}),
     "canonical Postgres credential"),
    ("the query edited by hand", _on_wipe(lambda n: n["parameters"].__setitem__("query", n["parameters"]["query"] + "\n-- by hand")),
     "differs from audience.WIPE_SQL"),
    ("the query made an n8n expression", _on_wipe(lambda n: n["parameters"].__setitem__("query", "=" + n["parameters"]["query"])),
     "plain text"),
    ("a parameter list added", _on_wipe(lambda n: n["parameters"].__setitem__("options", {"queryReplacement": "={{ $json.x }}"})),
     "takes no parameters"),
    ("the trigger's edge removed", _trigger_edge(False), "must be a target of Every 6 Hours exactly once"),
    ("the trigger's edge doubled", lambda s: s["connections"][TRIGGER]["main"][0].append({"node": WIPE_NODE, "type": "main", "index": 0}),
     "must be a target of Every 6 Hours exactly once"),
    ("behind Sweep Config", _gate("Sweep Config", 0), "ONLY from Every 6 Hours"),
    ("behind a dry-run If", _gate("Trial/Churn Dry Run?", 1), "ONLY from Every 6 Hours"),
    ("a successor wired", _successor, "must be terminal"),
    ("the node duplicated", _duplicate, "expected exactly one node"),
    ("the statement also deletes dialog_counts",
     _sql("), marked as (", "), usage as (\n  delete from dialog_counts d using batch b\n  where d.chat_id = b.chat_id\n  returning 1\n), marked as ("),
     "must never touch dialog_counts"),
    ("the statement deletes the decision itself",
     _sql("), marked as (", "), gone as (\n  delete from chat_audience x using batch b\n  where x.profile_id = b.profile_id and x.chat_id = b.chat_id\n  returning 1\n), marked as ("),
     "never delete the decision itself"),
    ("the statement also deletes the bot's schedule",
     _sql("), marked as (", "), sched as (\n  delete from bot_schedule x using batch b\n  where x.profile_id = b.profile_id\n  returning 1\n), marked as ("),
     "must never touch bot_schedule"),
    ("a per-chat table dropped from the wipe", _sql("delete from suggestion_sets g using batch b", "select 1 from suggestion_sets g, batch b"),
     "must delete from exactly the per-chat tables"),
    ("the bot-wide flag no longer spared", _sql(" and f.chat_id <> '*'", ""), "bot-wide row"),
    ("the work list not locked", _sql("  for update skip locked\n", "\n"), "for update skip locked"),
    ("the work list locked but waited for", _sql("for update skip locked", "for update"), "for update skip locked"),
    ("the batch limit removed", _sql(f"  limit {AUD.WIPE_BATCH}\n", "\n"), "the batch must be"),
    ("the oldest-first order removed", _sql("  order by a.personal_since, a.profile_id, a.chat_id\n", "\n"), "the batch must be"),
    ("the owner filter removed", _sql("a.source = 'owner' and ", ""), "the work list must be the OWNER's"),
    ("the already-wiped filter removed", _sql(" and a.wiped_at is null", ""), "the work list must be the OWNER's"),
    ("the delay shortened", _sql(f"interval '{AUD.WIPE_DAYS * 24} hours'", "interval '24 hours'"), "the delay must be WIPE_DAYS"),
    ("the update no longer marks the chats wiped", _sql("set wiped_at = now(), updated_at = now()", "set updated_at = now()"),
     "must mark the decisions wiped"),
)


def self_check(sweep, check, mutations):
    """Every mutation of a PASSING workflow must be rejected with its message — else the tripwire is broken."""
    broken = []
    for label, mutate, expect in mutations:
        bad = copy.deepcopy(sweep)
        try:
            sql = mutate(bad)
        except (AssertionError, KeyError, ValueError, IndexError) as e:
            broken.append(f"tripwire self-check: mutation {label!r} does not apply ({type(e).__name__}: {e})")
            continue
        got = check(bad, sql)
        if not any(expect in m for m in got):
            broken.append(f"tripwire self-check: mutation {label!r} was not rejected with {expect!r} (got {got})")
    return broken


def group_wipe(directory):
    path = os.path.join(directory, SWEEP_FILE)
    if not os.path.exists(path):
        return [f"{SWEEP_FILE} is missing"], ""
    with open(path, encoding="utf-8") as fh:
        sweep = json.load(fh)
    fails = check_wipe(sweep)
    if not fails:
        fails = self_check(sweep, check_wipe, WIPE_MUTATIONS)
    return fails, (f"wipe: {WIPE_NODE} = audience.WIPE_SQL, a target of {TRIGGER} and of nothing else (no dry-run gate), terminal, "
                   f"fail-open; the statement keeps its safety shape; tripwire self-check: {len(WIPE_MUTATIONS)} mutations rejected")


# ================================================================================================ group: readers
# Task 5: the three server places outside the bot templates that write or read a chat pass only a BUSINESS chat — the
# one predicate audience.BUSINESS_SQL (a decision that holds and says business; never «not personal»). Behaviour:
# tests/test_personal_readers.py.
PROXY_FILE = "crWappiProxy0001-Wappi_Proxy.json"
SUGGEST_FILE = "9PTyYcelRQI7bGDb-Suggest_Replies.json"
DASH_FILE = "2htWSV5IHO8E2CgB-Dashboard_Outcomes.json"
READER_FILES = (PROXY_FILE, SUGGEST_FILE, DASH_FILE)
BUSINESS_WORD = "(select business from aud)"
SESSION_SPLIT = ("split_part(c.session_id, ':', 1)", "substr(c.session_id, position(':' in c.session_id) + 1)")


def _aud_head(profile, chat):
    """How a gated statement opens: ONE business word for the chat, from audience.BUSINESS_SQL keyed on (profile, chat)."""
    return "with aud as (\n  select " + AUD.BUSINESS_SQL(profile, chat) + " as business\n)"


def _inserts(code):
    """Each `insert into …` of a statement, up to its `on conflict` / `returning` — the part where its condition stands."""
    out = []
    for part in re.split(r"\binsert into\b", code)[1:]:
        m = re.search(r"\bon conflict\b|\breturning\b", part)
        out.append(part[:m.start()] if m else part)
    return out


def _array_items(expr):
    m = re.fullmatch(r"=\{\{\s*\[(.*)\]\s*\}\}", (expr or "").strip(), re.S)
    return [x.strip() for x in m.group(1).split(",")] if m else []


def _block(js, head):
    """The `{ … }` block that `head` opens (brace-matched), '' when `head` is missing."""
    if head not in js:
        return ""
    i = js.index(head) + len(head) - 1
    depth = 0
    for j in range(i, len(js)):
        depth += {"{": 1, "}": -1}.get(js[j], 0)
        if depth == 0:
            return js[i:j + 1]
    return ""


def check_readers(bundle, sql=None):
    """Findings for the three readers. `bundle` = {file name: workflow}; `sql` is unused (the self-check's signature)."""
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)
        return cond

    def query(fname, name):
        n = node(bundle.get(fname) or {}, name)
        need(n is not None, f"{fname}: node {name!r} is missing")
        return n or {"parameters": {}}

    # (1) the proxy's Record Owner Send — the owner's app sends (owner_app), after the response
    rec = query(PROXY_FILE, "Record Owner Send")
    code = code_of(rec["parameters"].get("query") or "")
    need(_aud_head("$1", "$2") in code, "proxy: Record Owner Send must open with the business word for the chat it records — "
                                         "audience.BUSINESS_SQL on the first two parameters (profile id, chat id)")
    ins = _inserts(code)
    need(len(ins) == 2 and all(re.search(r"\bwhere\s*\(select business from aud\)", i) and not re.search(r"\bvalues\s*\(", i)
                               for i in ins),
         "proxy: every insert of Record Owner Send (the history AND the memory) must stand behind `where (select business from aud)`")
    need(BUSINESS_WORD + " as business" in code, "proxy: Record Owner Send's answer must name the business word (why nothing was written)")

    # (2) Suggest Replies — Suggestion Gate, Gate Decision, Build Response
    gate = query(SUGGEST_FILE, "Suggestion Gate")
    code = code_of(gate["parameters"].get("query") or "")
    need(_aud_head("$3", "$4") in code, "panel: Suggestion Gate must open with the business word for the chat — audience.BUSINESS_SQL "
                                         "on the third and fourth parameters (profile id, chat id)")
    items = _array_items((gate["parameters"].get("options") or {}).get("queryReplacement"))
    need(len(items) == 4 and items[2].startswith("$json.profileId") and items[3].startswith("$json.chatId"),
         f"panel: Suggestion Gate's parameters must hand on Prep's profileId and chatId as the third and fourth: {items}")
    ins = _inserts(code)
    need(len(ins) == 2 and all(re.search(r"\bwhere\s*\(select business from aud\) and\b", i) for i in ins),
         "panel: every counter insert of Suggestion Gate (the request key AND the daily counter) must stand behind the business word")
    need(re.search(r"\(\(select business from aud\) and [^;]*\) as allowed", code) is not None and BUSINESS_WORD + " as business" in code,
         "panel: Suggestion Gate's verdict (allowed) must require the business word, and the row must carry it")
    gd = query(SUGGEST_FILE, "Gate Decision")["parameters"].get("jsCode") or ""
    need("const business = row.business === true;" in gd and "const allowed = business && row.allowed === true;" in gd,
         "panel: Gate Decision may open the panel only on the statement's positive business word — never fail open")
    label = "else if (!business) gateReason = failOpen ? 'audience_unknown' : 'personal_chat';"
    need(label in gd and "else if (row.known !== true)" in gd and gd.index(label) < gd.index("else if (row.known !== true)"),
         "panel: Gate Decision must label a non-business chat personal_chat (a failed read audience_unknown) before any account reason")
    br = query(SUGGEST_FILE, "Build Response")["parameters"].get("jsCode") or ""
    session = _block(br, "if (authMode === 'session') {")
    need("r === 'personal_chat' ? 'personal_chat'" in session and br.count("'personal_chat'") == session.count("'personal_chat'"),
         "panel: Build Response must name personal_chat to an authenticated session caller only (a legacy caller keeps generation_failed)")

    # (3) Dashboard Outcomes — the classifier's input and what the app gets
    fc = code_of(query(DASH_FILE, "Find Changed Sessions")["parameters"].get("query") or "")
    biz = AUD.BUSINESS_SQL("s.profile_id", "s.chat_id")
    need("split_part(h.session_id, ':', 1) AS profile_id" in fc and "substr(h.session_id, position(':' in h.session_id) + 1) AS chat_id" in fc,
         "«Сводка»: Find Changed Sessions must split a session at its FIRST colon (profile, chat)")
    need("changed AS (" in fc and "LIMIT 21" in fc and fc.count(biz) == 1
         and fc.index("changed AS (") < fc.find(biz) < fc.index("LIMIT 21"),
         "«Сводка»: Find Changed Sessions must keep only business sessions — audience.BUSINESS_SQL on the session's profile and chat, "
         "inside `changed` and before its LIMIT (the classifier's batch and the total count business sessions only)")
    fo = code_of(query(DASH_FILE, "Fetch Outcomes")["parameters"].get("query") or "")
    o_cte, h_cte = fo.split("), h AS (", 1) if "), h AS (" in fo else (fo, "")
    need(AUD.BUSINESS_SQL(*SESSION_SPLIT) in o_cte,
         "«Сводка»: Fetch Outcomes must return a verdict only for a business chat (audience.BUSINESS_SQL on the session split)")
    need(AUD.BUSINESS_SQL("r.profile_id", "r.chat_id") in h_cte,
         "«Сводка»: Fetch Outcomes must return an open handoff only for a business chat (audience.BUSINESS_SQL on the request's chat)")
    return fails


def _on(fname, name, key, old, new):
    """Replace `old` by `new` in one parameter of one node of the bundle (the anchor must be there)."""
    def m(bundle):
        p = node(bundle[fname], name)["parameters"]
        target = p["options"] if key == "queryReplacement" else p
        assert old in target[key], f"mutation anchor not found in {name}.{key}: {old[:70]!r}"
        target[key] = target[key].replace(old, new, 1)
    return m


_GATE_DECISION_OPEN = "const allowed = business && row.allowed === true;"
READER_MUTATIONS = (
    ("the proxy's history insert ungated", _on(PROXY_FILE, "Record Owner Send", "query", "  where (select business from aud)\n  on conflict", "  on conflict"),
     "every insert of Record Owner Send"),
    ("the proxy's memory insert ungated",
     _on(PROXY_FILE, "Record Owner Send", "query", "  where (select business from aud)\n    and not exists", "  where not exists"),
     "every insert of Record Owner Send"),
    ("the proxy's business word keyed on the message id", _on(PROXY_FILE, "Record Owner Send", "query", "aud.chat_id = $2", "aud.chat_id = $3"),
     "must open with the business word for the chat it records"),
    ("the proxy asks «not personal»", _on(PROXY_FILE, "Record Owner Send", "query", "= 'business', false)", "<> 'personal', true)"),
     "must open with the business word for the chat it records"),
    ("the gate counts the request key in any chat",
     _on(SUGGEST_FILE, "Suggestion Gate", "query", "now() where (select business from aud) and ", "now() where "),
     "every counter insert of Suggestion Gate"),
    ("the gate counts the day in any chat",
     _on(SUGGEST_FILE, "Suggestion Gate", "query", "  where (select business from aud) and (select ok from entitled) and ($2",
         "  where (select ok from entitled) and ($2"),
     "every counter insert of Suggestion Gate"),
    ("the gate allows without the business word",
     _on(SUGGEST_FILE, "Suggestion Gate", "query", "((select business from aud) and (select ok from entitled)", "((select ok from entitled)"),
     "verdict (allowed) must require the business word"),
    ("the gate is not told the chat",
     _on(SUGGEST_FILE, "Suggestion Gate", "queryReplacement", ", $json.profileId || '', $json.chatId || ''", ""),
     "hand on Prep's profileId and chatId"),
    ("Gate Decision fails open again",
     _on(SUGGEST_FILE, "Gate Decision", "jsCode", _GATE_DECISION_OPEN, "const allowed = failOpen ? true : row.allowed === true;"),
     "never fail open"),
    ("Gate Decision drops the personal_chat label",
     _on(SUGGEST_FILE, "Gate Decision", "jsCode", "  else if (!business) gateReason = failOpen ? 'audience_unknown' : 'personal_chat';\n", ""),
     "label a non-business chat personal_chat"),
    ("Build Response names personal_chat to every caller",
     _on(SUGGEST_FILE, "Build Response", "jsCode", "  let authMode = 'legacy';",
         "  if (j.gateReason === 'personal_chat') return [{ json: { v: 1, requestSeq, suggestions: [], error: 'personal_chat' } }];\n"
         "  let authMode = 'legacy';"),
     "personal_chat to an authenticated session caller only"),
    ("Build Response forgets personal_chat",
     _on(SUGGEST_FILE, "Build Response", "jsCode", " : r === 'personal_chat' ? 'personal_chat'", ""),
     "personal_chat to an authenticated session caller only"),
    ("the classifier reads every session",
     _on(DASH_FILE, "Find Changed Sessions", "query", "    AND " + AUD.BUSINESS_SQL("s.profile_id", "s.chat_id") + "\n", ""),
     "Find Changed Sessions must keep only business sessions"),
    ("the classifier's filter keyed on the session id",
     _on(DASH_FILE, "Find Changed Sessions", "query", "aud.chat_id = s.chat_id", "aud.chat_id = s.session_id"),
     "Find Changed Sessions must keep only business sessions"),
    ("«Сводка» returns every verdict",
     _on(DASH_FILE, "Fetch Outcomes", "query", "\n    AND " + AUD.BUSINESS_SQL(*SESSION_SPLIT), ""),
     "return a verdict only for a business chat"),
    ("«Сводка» returns every open handoff",
     _on(DASH_FILE, "Fetch Outcomes", "query", "\n    AND " + AUD.BUSINESS_SQL("r.profile_id", "r.chat_id"), ""),
     "return an open handoff only for a business chat"),
)


def group_readers(directory):
    bundle = {}
    for fname in READER_FILES:
        path = os.path.join(directory, fname)
        if not os.path.exists(path):
            return [f"{fname} is missing"], ""
        with open(path, encoding="utf-8") as fh:
            bundle[fname] = json.load(fh)
    fails = check_readers(bundle)
    if not fails:
        fails = self_check(bundle, check_readers, READER_MUTATIONS)
    return fails, (f"readers: the proxy's Record Owner Send, the panel's gate (+ Gate Decision, Build Response) and «Сводка» "
                   f"(Find Changed Sessions, Fetch Outcomes) pass only business chats (audience.BUSINESS_SQL); tripwire self-check: "
                   f"{len(READER_MUTATIONS)} mutations rejected")


# ================================================================================================ group: webhook
# Task 7: the app's ChatAudience webhook (build-chat-audience.py). Behaviour: tests/test_chat_audience.py.
CRA = _module("build_chat_audience", "build-chat-audience.py")
BCW = _module("build_client_webhooks", "build-client-webhooks.py")
BOOT = _module("staging_bootstrap", os.path.join("staging", "bootstrap.py"))
WEBHOOK_FILE = CRA.WF_FILE
SET_STATE_FILE = "0DP79STI3pafXO9B-Set_Workflow_State.json"
PROTECTED_KEY, ACTIVE_KEY = "<build-client-webhooks.PROTECTED_IDS>", "<staging/bootstrap.py ACTIVE_CANONICAL>"
# The dependents of build-client-webhooks.PROTECTED_IDS and the nodes that carry the protected list there.
PROTECTED_DEPENDENTS = {SET_STATE_FILE: ("Validate",),
                        "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json": ("Clone Plan", "Bind Workflow Id"),
                        "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json": ("Clone Plan", "Bind Workflow Id"),
                        "lmjYsdNcQA2IE5rl-Delete_Bot_Files.json": ("List Bot Originals", "Retire Bot Profiles", "Delete Bot Chunks",
                                                                   "Delete Chat Memory"),
                        SWEEP_FILE: ("Clone Retirements",)}
WEBHOOK_NODES = ("Webhook", "Auth Gate", "Authorized?", "Respond 401", "Validate", "Valid?", "Respond 400", "Is List?", "Is Set?",
                 "Is Connected?", "List", "Shape List", "Respond", "Set", "Shape Set", "Respond Set", "Replay Items", "Chat Channel",
                 "Has Channel?", "Recent Messages", "Replay Plan", "Backfill", "Replay?", "Replay", "Channel", "Channel Known?",
                 "Status", "Phone", "Phone OK?", "Connected", "Shape Connected")
TAIL = ("Replay Items", "Chat Channel", "Has Channel?", "Recent Messages", "Replay Plan", "Backfill", "Replay?", "Replay")
# Written here on their own — not read from the generator that wrote the nodes.
WAPPI_CRED = {"httpHeaderAuth": {"id": "EuhhqAaV56DpoqAN", "name": "WappiAuthToken"}}
WAPPI_BASES = {"https://wappi.pro/api/", "https://wappi.pro/tapi/"}
REPLAY_HOST = "http://localhost:5678/webhook/"
STATEMENTS = {"List": ("audience.LIST_SQL", AUD.LIST_SQL), "Set": ("audience.OWNER_SET_SQL", AUD.OWNER_SET_SQL),
              "Connected": ("audience.CONNECTED_SQL", AUD.CONNECTED_SQL),
              "Backfill": ("audience.BACKFILL_SQL", AUD.BACKFILL_SQL(CRA.AHH.MEMORY_ROW, CRA.AHH.OWNER_CONTENT_SQL))}


def _cut_edge(wf, src, out_index, dst):
    """The workflow's connections with ONE edge removed (for «with this edge cut, is X still reachable»)."""
    w = {"connections": copy.deepcopy(wf["connections"])}
    outs = (w["connections"].get(src) or {}).get("main") or []
    if out_index < len(outs):
        outs[out_index] = [e for e in (outs[out_index] or []) if e.get("node") != dst]
    return w


def _condition(wf, name):
    n = node(wf, name) or {}
    conds = (((n.get("parameters") or {}).get("conditions") or {}).get("conditions")) or [{}]
    return conds[0].get("leftValue") if len(conds) == 1 else None


def _hosts(text):
    return set(re.findall(r"https?://[^'\"\s)]+", text or ""))


def check_webhook(bundle, sql=None):
    """Findings for the ChatAudience webhook. `bundle` = {file name: workflow, PROTECTED_KEY: ids, ACTIVE_KEY: ids};
    `sql` is unused (the self-check's signature)."""
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)
        return cond

    wf = bundle.get(WEBHOOK_FILE) or {"nodes": [], "connections": {}}
    names = sorted(n.get("name") for n in wf.get("nodes", []))
    need(json.dumps(wf, indent=2, ensure_ascii=False) + "\n" == CRA.text(),
         f"{WEBHOOK_FILE} differs from build-chat-audience.py's output — edit the generator, never the JSON")
    if not need(names == sorted(WEBHOOK_NODES), f"the webhook's node set changed: {names}"):
        return fails
    N = {n["name"]: n for n in wf["nodes"]}
    P = {k: (N[k].get("parameters") or {}) for k in N}
    for name, n in N.items():
        need(not n.get("disabled"), f"{name} is disabled — n8n hands a disabled node's input on untouched")
    need((wf.get("settings") or {}).get("saveDataSuccessExecution") == "none",
         "settings.saveDataSuccessExecution must be 'none' — a successful execution must not keep the chats it read")

    # (2) the gate, Validate
    gate = P["Auth Gate"].get("jsCode", "")
    need('"legacy": false' in gate and '"profiles": ["profileIds", "profileId"]' in gate
         and f"const RULE = {json.dumps(CRA.RULE, ensure_ascii=False)};" in gate,
         "Auth Gate must be token ONLY (legacy false) and scope profileIds and profileId to the token's profiles")
    need(targets(wf, "Webhook") == [["Auth Gate"]] and targets(wf, "Auth Gate") == [["Authorized?"]]
         and _condition(wf, "Authorized?") == "={{ $json.crAuth.ok }}" and targets(wf, "Authorized?") == [["Validate"], ["Respond 401"]],
         "the Webhook must feed the gate alone, and Authorized? send true to Validate, false to Respond 401")
    v = P["Validate"].get("jsCode", "")
    need("const owned = new Set((wh.crAuth && wh.crAuth.profiles) || []);" in v
         and "typeof x === 'string' && x !== '' && x !== '-1' && owned.has(x)" in v,
         "Validate must drop the sentinels and re-intersect every id with the token's profiles")
    need(f"b.profileIds.length > {CRA.MAX_LIST_PROFILES}) return bad('profileIds')" in v
         and f"b.chats.length > {CRA.MAX_SET_CHATS}) return bad('chats')" in v
         and "Array.from(new Set(b.profileIds.filter(mine)))" in v,
         f"Validate must keep the limits ({CRA.MAX_LIST_PROFILES} profiles, {CRA.MAX_SET_CHATS} chats) and de-duplicate the profiles")
    need(f"id === '{CRA.WILDCARD_CHAT}') return bad('chatId')" in v,
         "Validate must refuse the reply mode's wildcard '*' as a chat id (it names every chat)")
    need("/\\s/.test(id)" in v and f"id.length > {CRA.MAX_CHAT_ID}" in v and "id === ''" in v,
         f"Validate must refuse an empty chat id, one over {CRA.MAX_CHAT_ID} characters and one with whitespace")
    need("if (c.kind !== 'personal' && c.kind !== 'business') return bad('kind');" in v, "Validate must accept only the two kinds")
    need("Number.isSafeInteger(v) && v > 0" in v and "if (!version) return bad('version');" in v,
         "Validate must accept only a positive safe integer version")
    need("return bad('op');" in v and _condition(wf, "Valid?") == "={{ $json.ok }}" and targets(wf, "Valid?") == [["Is List?"], ["Respond 400"]],
         "Valid? must send a refused body to Respond 400 and only a valid one on (and Validate refuse an unknown op)")
    for gate_edge in (("Authorized?", 0), ("Valid?", 0)):
        reach = reachable(wf, "Webhook", cut=gate_edge)
        need(not reach & {"List", "Set", "Channel", "Connected", "Recent Messages", "Status", "Replay"},
             f"a statement or a Wappi call is reachable without passing {gate_edge[0]} true: {sorted(reach & set(N))}")

    # (3) the op fork, the statements, the Shape nodes
    fork = {"Is List?": ("list", [["List"], ["Is Set?"]]), "Is Set?": ("set", [["Set"], ["Is Connected?"]]),
            "Is Connected?": ("connected", [["Channel"], ["Respond 400"]])}
    for name, (op, outs) in fork.items():
        need(_condition(wf, name) == f"={{{{ $json.op === '{op}' }}}}" and targets(wf, name) == outs,
             f"the op fork: {name} must test op === '{op}' and route {outs}: {targets(wf, name)}")
    for name, (label, text) in STATEMENTS.items():
        n = N[name]
        need(n.get("type") == "n8n-nodes-base.postgres" and P[name].get("operation") == "executeQuery" and P[name].get("query") == text,
             f"{name}'s query differs from {label} — edit the module, never the JSON")
        need(n.get("onError") == "continueRegularOutput" and n.get("alwaysOutputData") is True and n.get("credentials") == CANONICAL_PG,
             f"{name} must continue on error with output on the canonical Postgres credential — its Shape node judges the rows")
    for name in ("Chat Channel", "Channel"):
        need(N[name].get("onError") == "continueRegularOutput" and N[name].get("credentials") == CANONICAL_PG,
             f"{name} must continue on error on the canonical Postgres credential")
    need(sources(wf, "List") == [("Is List?", 0)] and sources(wf, "Set") == [("Is Set?", 0)]
         and sources(wf, "Channel") == [("Is Connected?", 0)] and sources(wf, "Connected") == [("Phone OK?", 0)],
         "each statement must be entered from its own gate alone (List ← Is List?, Set ← Is Set?, Channel ← Is Connected?, "
         "Connected ← Phone OK?)")
    sl, ss, sc = P["Shape List"].get("jsCode", ""), P["Shape Set"].get("jsCode", ""), P["Shape Connected"].get("jsCode", "")
    need("rows.length === asked.length" in sl and "Array.isArray(r.decisions)" in sl and "num(r.connected_at_ms)" in sl
         and "version: num(d.version) || 0" in sl,
         "Shape List must answer success only on one row per asked profile with its decisions, the numbers as numbers")
    need("rows.length === asked" in ss and "typeof r.applied === 'boolean'" in ss and "typeof r.became_business === 'boolean'" in ss
         and "Number(r.version)" in ss,
         "Shape Set must answer success only on one well-formed row per distinct chat, the version as a number")
    need("typeof row.restarted !== 'boolean'" in sc and "Number.isSafeInteger(ms) && ms > 0" in sc
         and "done({ success: false, error: 'not_authorized' }, 409)" in sc and "done({ success: false, error: 'wappi_unavailable' }, 502)" in sc
         and "reason: 'profileId' }, 400)" in sc,
         "Shape Connected must answer 409 / 502 / 400 / 500 by what happened and success only on one row with a positive "
         "moment and a boolean restarted")
    need(sources(wf, "Respond") == [("Shape Connected", 0), ("Shape List", 0)] and sources(wf, "Shape Connected")
         == [("Channel Known?", 1), ("Connected", 0), ("Phone OK?", 1)],
         f"Respond must answer Shape List and Shape Connected alone, Shape Connected judge every way out of connected: "
         f"{sources(wf, 'Respond')} {sources(wf, 'Shape Connected')}")

    # (4) the answer before the tail
    need(sources(wf, "Respond Set") == [("Shape Set", 0)] and targets(wf, "Shape Set") == [["Respond Set"]]
         and sources(wf, "Replay Items") == [("Respond Set", 0)],
         "the tail must start only once the answer is on the wire: Replay Items entered from Respond Set alone (before the answer)")
    reach = reachable(_cut_edge(wf, "Respond Set", 0, "Replay Items"), "Webhook")
    need(not reach & set(TAIL), f"with Respond Set → Replay Items cut, the tail is still reachable (before the answer): "
                                f"{sorted(reach & set(TAIL))}")
    ri = P["Replay Items"].get("jsCode", "")
    need("if (shaped.success !== true) return [];" in ri and "r.became_business !== true" in ri,
         "Replay Items must start nothing for a failed set and hand on only the chats the statement says became business")

    # (5) the Wappi reads
    for name, path in (("Recent Messages", "sync/messages/get"), ("Status", "sync/get/status")):
        n, p = N[name], P[name]
        resp = ((p.get("options") or {}).get("response") or {}).get("response") or {}
        timeout = (p.get("options") or {}).get("timeout")
        need(n.get("type") == "n8n-nodes-base.httpRequest" and n.get("credentials") == WAPPI_CRED
             and resp.get("fullResponse") is True and resp.get("neverError") is True
             and isinstance(timeout, int) and 0 < timeout <= 10000
             and n.get("onError") == "continueRegularOutput" and _hosts(p.get("url")) == WAPPI_BASES and f"'{path}'" in p.get("url", ""),
             f"{name} must be a Wappi read: the canonical Wappi credential, fullResponse + neverError, at most 10 s, continue on "
             f"error, only the two Wappi bases ({path})")
    rq = P["Recent Messages"].get("jsonQuery", "")
    need(f"limit: {CRA.RECENT_LIMIT}" in rq and "order: 'desc'" in rq and "chat_id: $json.chat_id" in rq,
         "Recent Messages must read limit 10 and order desc of THIS chat (the page the backfill takes, newest first)")
    need("mark_all" not in json.dumps(wf, ensure_ascii=False),
         "mark_all must appear nowhere in the webhook — reading a chat must never mark it read")
    need(sources(wf, "Recent Messages") == [("Has Channel?", 0)] and sources(wf, "Replay Plan") == [("Recent Messages", 0)]
         and sources(wf, "Backfill") == [("Replay Plan", 0)],
         "the tail must run Has Channel? → Recent Messages → Replay Plan → Backfill in this order")

    # (6) the replay, the plan, the phone
    rp = N["Replay"]
    rpp = P["Replay"]
    resp = ((rpp.get("options") or {}).get("response") or {}).get("response") or {}
    timeout = (rpp.get("options") or {}).get("timeout")
    need(_hosts(rpp.get("url")) == {REPLAY_HOST} and "encodeURIComponent($('Replay Plan').item.json.profileId)" in rpp.get("url", "")
         and rpp.get("method") == "POST",
         f"Replay must POST to the bot's own webhook on localhost ({REPLAY_HOST}<profile id>) — never a public host")
    need(not rp.get("credentials") and rpp.get("authentication") in (None, "none"),
         "Replay must carry no credential (a localhost call to the bot's webhook needs none — never the Wappi token)")
    need(resp.get("neverError") is True and isinstance(timeout, int) and 0 < timeout <= 10000 and rp.get("onError") == "continueRegularOutput",
         "Replay must be neverError, at most 10 s, continue on error")
    need("$('Replay Plan').item.json.message" in rpp.get("jsonBody", "") and sources(wf, "Replay") == [("Replay?", 0)],
         "Replay must send the plan's message and run only through Replay? true")
    need(_condition(wf, "Replay?") == "={{ typeof $json.added === 'number' && $('Replay Plan').item.json.replay === true }}",
         "Replay? must require the backfill's committed row (added, a number) AND the plan's positive word (replay true)")
    plan = P["Replay Plan"].get("jsCode", "")
    types = re.search(r"^const REPLAY_TYPES = (\[[^\]]*\]);$", plan, re.M)
    try:
        types = json.loads(types.group(1)) if types else []
    except ValueError:
        types = []
    need(P["Replay Plan"].get("mode") == "runOnceForEachItem" and f"const FRESH_HOURS = {AUD.FRESH_HOURS};" in plan
         and "FRESH_HOURS * 3600" in plan and "function replayMessage(" in plan and plan.count("function replayMessage(") == 1
         and "newest.fromMe !== false" in plan and {"chat", "text"} <= set(types) and "ptt" not in types
         and "body.uuid !== profileId" in plan and "m.chatId !== chatId" in plan and "body.status === 'done'" in plan,
         "Replay Plan must run per item, take FRESH_HOURS from audience.py, check the page is this profile's and chat's, hand on "
         "only the client's fresh message (never a voice message) and build it in ONE function")
    need("else if (String(newest.from) !== chatId) why = 'not_from_chat';" in plan and "    from: String(row.from),\n" in plan,
         "Replay Plan must hand on only a row whose own `from` is the chat and keep that `from` in the message — the templates' "
         "only group guard («If» from == chatId) must still see the real sender: a group member's message is never replayed")
    ph = P["Phone"].get("jsCode", "")
    need(CRA.HW.OWNER_PHONE_JS in ph and "phone: error ? '' : ownerPhone(wapi, status)" in ph and "status.profile_id !== v.profileId" in ph
         and "status.authorized === false) error = 'not_authorized'" in ph and "status.authorized !== true" in ph,
         "Phone must embed build-handoff-workflows.OWNER_PHONE_JS verbatim and count only an answer that echoes this profile "
         "with the boolean authorized")
    need(_condition(wf, "Phone OK?") == "={{ $json.ok === true }}" and _condition(wf, "Channel Known?")
         == "={{ $json.channel === 'whatsapp' || $json.channel === 'telegram' }}",
         "Phone OK? and Channel Known? must judge positive fields (the phone's ok, a known channel)")

    # (7) protected everywhere
    need(CRA.WF_ID in (bundle.get(PROTECTED_KEY) or []), f"{CRA.WF_ID} is missing from build-client-webhooks.PROTECTED_IDS (protected)")
    need(CRA.WF_ID in (bundle.get(ACTIVE_KEY) or []), f"{CRA.WF_ID} is missing from staging/bootstrap.py ACTIVE_CANONICAL (protected)")
    for fname, holders in PROTECTED_DEPENDENTS.items():
        dep = bundle.get(fname) or {"nodes": []}
        for h in holders:
            p = (node(dep, h) or {}).get("parameters") or {}
            need(f"'{CRA.WF_ID}'" in (p.get("query") or p.get("jsCode") or ""),
                 f"{fname} / {h} does not carry {CRA.WF_ID} in its protected list — regenerate it from build-client-webhooks.PROTECTED_IDS")
    return fails


def _wh(name, key, old, new):
    """Replace `old` by `new` in one text parameter of one webhook node (the anchor must be there)."""
    def m(bundle):
        p = node(bundle[WEBHOOK_FILE], name)["parameters"]
        target = p["options"] if key == "queryReplacement" else p
        assert old in target[key], f"mutation anchor not found in {name}.{key}: {old[:70]!r}"
        target[key] = target[key].replace(old, new, 1)
    return m


def _cond(name, old, new):
    """Replace `old` by `new` in an If node's (single) condition."""
    def m(bundle):
        c = node(bundle[WEBHOOK_FILE], name)["parameters"]["conditions"]["conditions"][0]
        assert old in c["leftValue"], f"mutation anchor not found in {name}'s condition: {old[:70]!r}"
        c["leftValue"] = c["leftValue"].replace(old, new, 1)
    return m


def _wh_node(name, key, value):
    def m(bundle):
        n = node(bundle[WEBHOOK_FILE], name)
        if value is None:
            n.pop(key)
        else:
            n[key] = copy.deepcopy(value)
    return m


def _rewire(src, out_index, dsts):
    def m(bundle):
        bundle[WEBHOOK_FILE]["connections"][src]["main"][out_index] = [{"node": d, "type": "main", "index": 0} for d in dsts]
    return m


def _drop_id(key):
    def m(bundle):
        assert CRA.WF_ID in bundle[key]
        bundle[key] = [x for x in bundle[key] if x != CRA.WF_ID]
    return m


def _unprotect(fname, holder):
    def m(bundle):
        p = node(bundle[fname], holder)["parameters"]
        k = "query" if "query" in p else "jsCode"
        assert f"'{CRA.WF_ID}'" in p[k], f"{fname} / {holder} carries no {CRA.WF_ID}"
        p[k] = p[k].replace(f"'{CRA.WF_ID}', ", "").replace(f",\n  '{CRA.WF_ID}'", "").replace(f", '{CRA.WF_ID}'", "")
    return m


WEBHOOK_MUTATIONS = (
    ("the gate opened to token-less calls", _wh("Auth Gate", "jsCode", '"legacy": false', '"legacy": true'), "token ONLY"),
    ("the gate no longer scopes profileId", _wh("Auth Gate", "jsCode", '"profiles": ["profileIds", "profileId"]', '"profiles": ["profileIds"]'),
     "token ONLY"),
    ("Validate without the re-intersection", _wh("Validate", "jsCode", " && owned.has(x)", ""), "re-intersect"),
    ("Validate keeps the sentinels", _wh("Validate", "jsCode", "x !== '' && x !== '-1' && ", ""), "re-intersect"),
    ("Validate takes the wildcard as a chat", _wh("Validate", "jsCode", " || id === '*') return bad('chatId')", ") return bad('chatId')"),
     "wildcard"),
    ("Validate takes whitespace in a chat id", _wh("Validate", "jsCode", " || /\\s/.test(id)", ""), "whitespace"),
    ("Validate without the list limit", _wh("Validate", "jsCode", "b.profileIds.length > 20", "b.profileIds.length > 2000"), "limits"),
    ("Validate without the set limit", _wh("Validate", "jsCode", "b.chats.length > 50", "b.chats.length > 5000"), "limits"),
    ("Validate without the version check", _wh("Validate", "jsCode", "if (!version) return bad('version');", ""), "version"),
    ("a refused body reaches the statements", _rewire("Valid?", 1, ["Is List?"]), "without passing Valid?"),
    ("an unauthorised call reaches Validate", _rewire("Authorized?", 1, ["Validate"]), "Authorized? send true to Validate"),
    ("List's statement edited by hand", _wh("List", "query", "order by i.ord", "order by i.ord desc"), "differs from audience.LIST_SQL"),
    ("Set without the version guard", _wh("Set", "query", " or t.version < excluded.version", " or true"),
     "differs from audience.OWNER_SET_SQL"),
    ("Connected's statement edited by hand", _wh("Connected", "query", "then now() else c.connected_at", "then c.connected_at else c.connected_at"),
     "differs from audience.CONNECTED_SQL"),
    ("Backfill composed without the owner mark", _wh("Backfill", "query", "Владелец: ", ""), "differs from audience.BACKFILL_SQL"),
    ("Set fails closed", _wh_node("Set", "onError", None), "must continue on error"),
    ("the tail hung off Shape Set", _rewire("Shape Set", 0, ["Respond Set", "Replay Items"]), "before the answer"),
    ("the tail moved before the answer", lambda b: (_rewire("Shape Set", 0, ["Replay Items"])(b), _rewire("Replay Plan", 0, ["Respond Set"])(b)),
     "before the answer"),
    ("the replay skips the backfill", _rewire("Replay Plan", 0, ["Replay?"]), "Has Channel? → Recent Messages → Replay Plan → Backfill"),
    ("the replay sent to the public host", _wh("Replay", "url", "http://localhost:5678/webhook/", "https://n8n.choosereply.com/webhook/"),
     "localhost"),
    ("the replay carries the Wappi token", _wh_node("Replay", "credentials", WAPPI_CRED), "no credential"),
    ("Replay? without the backfill's row", _cond("Replay?", "typeof $json.added === 'number' && ", ""), "Replay? must require"),
    ("Replay? without the plan's word", _cond("Replay?", " && $('Replay Plan').item.json.replay === true", ""), "Replay? must require"),
    ("Phone OK? lets any verdict through", _cond("Phone OK?", "$json.ok === true", "true"), "Phone OK? and Channel Known?"),
    ("Recent Messages without neverError", lambda b: node(b[WEBHOOK_FILE], "Recent Messages")["parameters"]["options"]["response"]["response"]
     .__setitem__("neverError", False), "Recent Messages must be a Wappi read"),
    ("Recent Messages waits 30 s", lambda b: node(b[WEBHOOK_FILE], "Recent Messages")["parameters"]["options"].__setitem__("timeout", 30000),
     "Recent Messages must be a Wappi read"),
    ("Recent Messages on another credential", _wh_node("Recent Messages", "credentials", {"httpHeaderAuth": {"id": "dev0000000000000",
                                                                                                          "name": "Wappi Dev"}}),
     "Recent Messages must be a Wappi read"),
    ("Recent Messages marks the chat read", _wh("Recent Messages", "jsonQuery", "order: 'desc'", "order: 'desc', mark_all: true"), "mark_all"),
    ("Recent Messages reads the oldest first", _wh("Recent Messages", "jsonQuery", "order: 'desc'", "order: 'asc'"), "limit 10 and order desc"),
    ("Status on another host", _wh("Status", "url", "https://wappi.pro/api/", "https://wappi.example/api/"), "Status must be a Wappi read"),
    ("Phone without the echo", _wh("Phone", "jsCode", "else if (status.profile_id !== v.profileId) error = 'wappi_unavailable';\n", ""),
     "Phone must embed"),
    ("Phone reads the top-level phone only", _wh("Phone", "jsCode", "phone: error ? '' : ownerPhone(wapi, status)",
                                                 "phone: error ? '' : String(status.phone || '').replace(/\\D/g, '')"),
     "Phone must embed"),
    ("Replay Plan replays a voice message", _wh("Replay Plan", "jsCode", '"chat", "text", ', '"chat", "text", "ptt", '), "Replay Plan must"),
    ("Replay Plan replays a group member's message", _wh("Replay Plan", "jsCode", "else if (String(newest.from) !== chatId) why = 'not_from_chat';\n", ""),
     "own `from` is the chat"),
    ("the replay rewrites the sender to the chat", _wh("Replay Plan", "jsCode", "    from: String(row.from),\n", "    from: chatId,\n"),
     "own `from` is the chat"),
    ("Replay Plan without the freshness", _wh("Replay Plan", "jsCode", " && time >= Math.floor(Date.now() / 1000) - FRESH_HOURS * 3600", ""),
     "Replay Plan must"),
    ("Replay Plan takes a crossed page", _wh("Replay Plan", "jsCode", " && !(typeof body.uuid === 'string' && body.uuid !== '' && body.uuid !== profileId)", ""),
     "Replay Plan must"),
    ("Shape Set calls any row a success", _wh("Shape Set", "jsCode", "typeof r.became_business === 'boolean'", "true"), "Shape Set must"),
    ("Shape Connected without restarted", _wh("Shape Connected", "jsCode", " || typeof row.restarted !== 'boolean'", ""), "Shape Connected must"),
    ("Auth Gate disabled", _wh_node("Auth Gate", "disabled", True), "Auth Gate is disabled"),
    ("Replay Plan disabled", _wh_node("Replay Plan", "disabled", True), "Replay Plan is disabled"),
    ("successful executions saved", lambda b: b[WEBHOOK_FILE]["settings"].__setitem__("saveDataSuccessExecution", "all"),
     "saveDataSuccessExecution"),
    ("the node moved by hand", lambda b: node(b[WEBHOOK_FILE], "Respond Set").__setitem__("position", [0, 0]),
     "differs from build-chat-audience.py's output"),
    ("dropped from PROTECTED_IDS", _drop_id(PROTECTED_KEY), "missing from build-client-webhooks.PROTECTED_IDS"),
    ("dropped from ACTIVE_CANONICAL", _drop_id(ACTIVE_KEY), "missing from staging/bootstrap.py ACTIVE_CANONICAL"),
    ("Set Workflow State no longer protects it", _unprotect(SET_STATE_FILE, "Validate"), f"{SET_STATE_FILE} / Validate does not carry"),
    ("the sweep's clone branch no longer spares it", _unprotect(SWEEP_FILE, "Clone Retirements"), f"{SWEEP_FILE} / Clone Retirements"),
)


def group_webhook(directory):
    bundle = {}
    for fname in (WEBHOOK_FILE,) + tuple(PROTECTED_DEPENDENTS):
        path = os.path.join(directory, fname)
        if not os.path.exists(path):
            return [f"{fname} is missing"], ""
        with open(path, encoding="utf-8") as fh:
            bundle[fname] = json.load(fh)
    bundle[PROTECTED_KEY] = list(BCW.PROTECTED_IDS)
    bundle[ACTIVE_KEY] = list(BOOT.ACTIVE_CANONICAL)
    fails = check_webhook(bundle)
    if not fails:
        fails = self_check(bundle, check_webhook, WEBHOOK_MUTATIONS)
    return fails, (f"webhook: {CRA.WF_ID} = build-chat-audience.py's output — token-only gate, Validate (wildcard, limits), the op "
                   f"fork, audience.py's statements, the answer before the tail, Wappi reads without mark_all, the replay to "
                   f"localhost only after the backfill, the A5 phone rule, protected everywhere; tripwire self-check: "
                   f"{len(WEBHOOK_MUTATIONS)} mutations rejected")


GROUPS = {"wipe": group_wipe, "readers": group_readers, "webhook": group_webhook}


def run(directory=None, only=None):
    """([(group, finding)], [summary of each group that passed]) over the chosen groups."""
    d = directory or os.path.join(HERE, "workflows")
    found, passed = [], []
    for name, group in GROUPS.items():
        if only and only != name:
            continue
        fails, summary = group(d)
        found += [(name, f) for f in fails]
        if not fails:
            passed.append(summary)
    return found, passed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir")
    ap.add_argument("--only", choices=sorted(GROUPS))
    a = ap.parse_args()
    fails, passed = run(a.dir, a.only)
    for summary in passed:
        print("OK  " + summary)
    for group, msg in fails:
        print(f"FAIL [{group}] {msg}")
    if fails:
        print(f"personal chats verify: {len(fails)} problem(s)")
        sys.exit(1)
    print("personal chats verify: OK")


if __name__ == "__main__":
    main()
