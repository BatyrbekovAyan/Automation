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
          not behind a dry-run If: the step deletes by the age of a chat's personal clock, not by the state of a profile),
          terminal, unreachable from Sweep Config, and the FIRST branch n8n runs from Every 6 Hours — strictly above every
          other branch on the canvas: n8n v1 runs a node's branches one after another, the topmost first, and one unhandled
          error ends the run (nodes of the Sweep Config subtree have no onError), so a wipe placed below would silently
          stop whenever an earlier branch fails;
      (4) the statement keeps its safety shape: it deletes from the per-chat tables — clone_lineage.MOVED_TABLES minus the
          per-bot rows and the decision, derived so a new per-chat table on the line cannot escape it — and from nothing else (never
          dialog_counts — the tariff usage —, never the per-bot rows, never the decision itself), the chat's own reply_mode_flags
          row only (never the bot-wide '*' row), takes the work list locked and skipping what is locked, at most WIPE_BATCH
          oldest first, personal decisions only (never a business row) with a clock — the owner's and a computed one that
          replaced a business row alike, never narrowed by source —, not yet wiped, older than WIPE_DAYS written as hours, and
          marks wiped_at.
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
      (5) the Wappi reads (Chat Card, Recent Messages, Status): the canonical Wappi credential, fullResponse + neverError,
          at most 10 s, continue on error; Recent Messages and Status only on the two Wappi bases, messages/get with limit
          10 / order desc; the chat card only WhatsApp's chats/filter by the digits of the chat id, asked only for a
          WhatsApp chat known by its number (Need Card?); mark_all nowhere; the tail runs Has Channel? → Need Card? →
          [Chat Card →] Chat Code → Recent Messages → Replay Plan → Backfill;
      (6) the replay: only through Replay? (the backfill's committed row AND the plan's positive word), POST to the bot's own
          webhook on localhost, no credential, neverError, at most 10 s; Chat Code runs per item and takes the code (chat.LID)
          only off a card it was handed, from an answer that counts and the dialog whose id IS the chat id, and only an @lid
          code; Replay Plan runs per item, reads the chat off Chat Code, takes FRESH_HOURS from audience.py, counts a row as
          the chat's when its chatId is the chat id or its code, checks the page is this profile's and chat's, hands on only
          a row whose own `from` is the chat by the same rule (never a group member's message — the templates' group guard
          «If» from == chatId drops it), names the chat by the decision's id (chatId = from) with the code beside it on
          WhatsApp (whatsapp_chat_id), never replays a voice message, builds the message in ONE function; Phone embeds
          build-handoff-workflows.OWNER_PHONE_JS verbatim and needs the echo and the boolean flag;
      (7) crChatAudience01 is a protected infra id: build-client-webhooks.PROTECTED_IDS, staging/bootstrap.py
          ACTIVE_CANONICAL, Set Workflow State and the dependents regenerated from the list.
  decide — Task 3: the bots' Decide Audience sub-workflow crAudienceDec001 (build-decide-audience.py; behaviour in
      tests/test_decide_audience.py):
      (1) the workflow IS the generator's output, with exactly its nodes, none disabled; successful executions not saved;
      (2) the Execute Workflow trigger declares exactly the nine inputs with their types — no contactName (the webhook's
          contact_name is no contact fact: Wappi fills it from the person's own profile name too);
      (3) every edge is the designed one, so every way out ends in Shape, the last node (n8n hands the caller its items); a
          stop of Can Decide? reaches no Wappi call and no write; each statement is entered from its own gate alone —
          Ensure Connection only without a moment, Write Decision only through Persist?, Backfill only through Became
          Business?;
      (4) every If judges a positive field (a number, go, needCard, needHistory, persist, the written row) — never «the
          node continued»; Became Business? reads the ROW: kind business with a source (default / owner);
      (5) the two Wappi reads only (Chat Card, Recent Messages): GET, the canonical WappiAuthToken credential, fullResponse
          + neverError, at most 10 s, continue on error, the Wappi bases; Telegram's card = chats/get limit 20 / offset 0 /
          order desc, WhatsApp's = chats/filter by client_name; the history = this chat's newest page of
          audience.HISTORY_LIMIT rows (larger than the backfill's audience.RECENT_LIMIT); mark_all nowhere, order asc
          nowhere (Telegram's tapi ignores asc);
      (6) Plan Facts, Read Card and Decide embed audience.RULE_JS verbatim; no Code node reads contact_name; Plan Facts
          stops for audience.SETTLE_MINUTES and asks no card for an @lid chat; Read Card takes the dialog whose id IS the
          chat id and the WhatsApp contact from contact.FullName; Decide reads the history off the one page (its trigger,
          its code, the full-page rule at HISTORY_LIMIT) and hands its newest RECENT_LIMIT rows on unsorted (the
          backfill's slice); Shape answers business only on the written row (one
          with a source). No node before Write Decision emits kind, reason or source — the fields of its row — and
          Decide hands its verdict on as verdictKind / verdictReason: when Write Decision throws before its statement,
          n8n hands that input item on unchanged, and the business gates must never take it for the row;
      (7) exactly three Postgres nodes, each carrying audience.py's statement exactly, continuing on error;
      (8) crAudienceDec001 is a protected infra id everywhere (PROTECTED_IDS, ACTIVE_CANONICAL before the staging-only eval
          runner, Set Workflow State, the dependents regenerated from the list).

Every group carries its own tripwire (WIPE_MUTATIONS for `wipe`, READER_MUTATIONS for `readers`, WEBHOOK_MUTATIONS for
`webhook`, DECIDE_MUTATIONS for `decide`): a list of (label, mutation, the message the checks must produce); a normal run
applies each one to a copy of a PASSING workflow and fails if the checks accept it — a tripwire that accepts a broken sweep
is no tripwire.

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
CL = _module("clone_lineage", "clone_lineage.py")

SWEEP_FILE = "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json"
TRIGGER = "Every 6 Hours"
WIPE_NODE = "Wipe Personal Chats"
# The sweep's canonical Postgres credential — written here on its own (not read from the generator that wrote the node), and
# required to be the very one the sweep's older nodes use.
CANONICAL_PG = {"postgres": {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}}
# What the step deletes from: the tables the bot's line moves and Delete Chat Memory clears (clone_lineage.MOVED_TABLES),
# minus what is not a chat's data — the per-bot rows (bot_schedule, audience_connections) and the decision itself
# (chat_audience). Derived, never listed: a per-chat table added to the line is a table the wipe must empty too, and the
# check below then fails until WIPE_SQL deletes from it. The tariff usage (dialog_counts) is not on the line at all.
NOT_CHAT_DATA = ("bot_schedule", "chat_audience", "audience_connections")
CHAT_TABLES = tuple(t for t in CL.MOVED_TABLES if t not in NOT_CHAT_DATA)
assert "dialog_counts" not in CHAT_TABLES, "dialog_counts is the tariff usage — the wipe must never delete from it"
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


def v1_order(wf, src):
    """The targets of `src` (every main output) in the order n8n's v1 engine runs them: it sorts a node's targets by canvas
    position — the topmost first, on a tie the leftmost — and runs each branch to its end before it starts the next
    (n8n-core execution-engine/workflow-execute.js: nodesToAdd.sort by position, then unshift onto the execution stack)."""
    names = [e["node"] for out in (wf["connections"].get(src) or {}).get("main") or [] for e in (out or [])]
    pos = lambda n: tuple(((node(wf, n) or {}).get("position") or [0, 0])[:2])
    return sorted(dict.fromkeys(names), key=lambda n: (pos(n)[1], pos(n)[0]))


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
    # the FIRST branch of the trigger: n8n v1 runs the trigger's branches one after another, topmost first, and an unhandled
    # error in any of them ends the run — strictly above every other branch, so no tie-break on the canvas decides it
    order = v1_order(sweep, TRIGGER)
    wipe_y = ((node(sweep, WIPE_NODE) or {}).get("position") or [0, 0])[1]
    need(order[:1] == [WIPE_NODE] and all(((node(sweep, o) or {}).get("position") or [0, 0])[1] > wipe_y
                                          for o in order if o != WIPE_NODE),
         f"{WIPE_NODE} must be the FIRST branch n8n runs from {TRIGGER} — strictly above every other branch on the canvas "
         f"(n8n v1 runs them top-down, and an unhandled error in an earlier branch ends the run before the wipe): {order}")

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
    need("a.kind = 'personal'" in flat and "a.wiped_at is null" in flat and "a.personal_since <=" in flat,
         "the work list must be the PERSONAL decisions with a clock, not wiped yet, old enough (kind / wiped_at / personal_since) — "
         "never a business row")
    batch = re.search(r"with batch as \(.*?\), hist as \(", flat)
    need(bool(batch) and "source" not in batch.group(0),
         "the work list must not be narrowed by source: a computed personal decision that replaced a business row (after another "
         "number) carries the clock too, and its stored business period must be wiped like the owner's")
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
    ("the wipe moved below Sweep Config (it would run after that subtree)", _set("position", [-680, 760]), "must be the FIRST branch"),
    ("the wipe level with Sweep Config (only the tie-break would decide)",
     lambda s: node(s, WIPE_NODE).__setitem__("position", [node(s, WIPE_NODE)["position"][0] - 220, node(s, "Sweep Config")["position"][1]]),
     "must be the FIRST branch"),
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
    ("the personal filter removed (a business row would be wiped)", _sql("a.kind = 'personal' and ", ""),
     "the work list must be the PERSONAL decisions"),
    ("the work list narrowed to the owner's decisions", _sql("where a.kind = 'personal'", "where a.source = 'owner' and a.kind = 'personal'"),
     "must not be narrowed by source"),
    ("the work list narrowed to the computed decisions", _sql("where a.kind = 'personal'", "where a.source <> 'owner' and a.kind = 'personal'"),
     "must not be narrowed by source"),
    ("the already-wiped filter removed", _sql(" and a.wiped_at is null", ""), "the work list must be the PERSONAL decisions"),
    ("the clock filter removed", _sql("    and a.personal_since <= now() - interval", "    and now() > now() - interval"),
     "the work list must be the PERSONAL decisions"),
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
    return fails, (f"wipe: {WIPE_NODE} = audience.WIPE_SQL, a target of {TRIGGER} and of nothing else (no dry-run gate), the first "
                   f"branch it runs, terminal, fail-open; the statement keeps its safety shape; tripwire self-check: "
                   f"{len(WIPE_MUTATIONS)} mutations rejected")


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
                 "Has Channel?", "Need Card?", "Chat Card", "Chat Code", "Recent Messages", "Replay Plan", "Backfill", "Replay?",
                 "Replay", "Channel", "Channel Known?", "Status", "Phone", "Phone OK?", "Connected", "Shape Connected")
TAIL = ("Replay Items", "Chat Channel", "Has Channel?", "Need Card?", "Chat Card", "Chat Code", "Recent Messages", "Replay Plan",
        "Backfill", "Replay?", "Replay")
# Written here on their own — not read from the generator that wrote the nodes.
WAPPI_CRED = {"httpHeaderAuth": {"id": "EuhhqAaV56DpoqAN", "name": "WappiAuthToken"}}
WAPPI_BASES = {"https://wappi.pro/api/", "https://wappi.pro/tapi/"}
WA_CARD_URL = "https://wappi.pro/api/sync/chats/filter"
CHAT_DIGITS = "String($json.chat_id).split('@')[0].replace(/[^0-9]/g, '')"
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
    need(f"limit: {AUD.RECENT_LIMIT}" in rq and "order: 'desc'" in rq and "chat_id: $json.chat_id" in rq,
         "Recent Messages must read limit 10 and order desc of THIS chat (the page the backfill takes, newest first)")
    cn, cp = N["Chat Card"], P["Chat Card"]
    cresp = ((cp.get("options") or {}).get("response") or {}).get("response") or {}
    ctimeout = (cp.get("options") or {}).get("timeout")
    need(cn.get("type") == "n8n-nodes-base.httpRequest" and cn.get("credentials") == WAPPI_CRED and cp.get("method") == "GET"
         and cresp.get("fullResponse") is True and cresp.get("neverError") is True
         and isinstance(ctimeout, int) and 0 < ctimeout <= 10000 and cn.get("onError") == "continueRegularOutput",
         "Chat Card must be a Wappi read: GET, the canonical Wappi credential, fullResponse + neverError, at most 10 s, continue on error")
    need(cp.get("url") == WA_CARD_URL
         and cp.get("jsonQuery") == "={{ JSON.stringify({ profile_id: $json.profile_id, client_name: " + CHAT_DIGITS + " }) }}",
         f"Chat Card must ask WhatsApp's chat list ({WA_CARD_URL}) of THIS profile, filtered by the digits of the chat id — "
         "nothing else (the filter never searches a code)")
    need(_condition(wf, "Need Card?") == "={{ $json.channel === 'whatsapp' && !String($json.chat_id).endsWith('@lid') && "
         + CHAT_DIGITS + " !== '' }}",
         "Need Card? must ask the card only for a WhatsApp chat known by its number (never Telegram, never a chat whose id is "
         "its @lid code, never an id without digits)")
    need("mark_all" not in json.dumps(wf, ensure_ascii=False),
         "mark_all must appear nowhere in the webhook — reading a chat must never mark it read")
    need(sources(wf, "Need Card?") == [("Has Channel?", 0)] and targets(wf, "Has Channel?") == [["Need Card?"]]
         and targets(wf, "Need Card?") == [["Chat Card"], ["Chat Code"]] and sources(wf, "Chat Card") == [("Need Card?", 0)]
         and sources(wf, "Chat Code") == [("Chat Card", 0), ("Need Card?", 1)] and targets(wf, "Chat Card") == [["Chat Code"]]
         and sources(wf, "Recent Messages") == [("Chat Code", 0)] and sources(wf, "Replay Plan") == [("Recent Messages", 0)]
         and sources(wf, "Backfill") == [("Replay Plan", 0)],
         "the tail must run Has Channel? → Need Card? → [Chat Card →] Chat Code → Recent Messages → Replay Plan → Backfill "
         "in this order")

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
         and "body.uuid !== profileId" in plan and "body.status === 'done'" in plan,
         "Replay Plan must run per item, take FRESH_HOURS from audience.py, check the page is this profile's and chat's, hand on "
         "only the client's fresh message (never a voice message) and build it in ONE function")
    need("const req = $('Chat Code').item.json;" in plan
         and "const chat = chatIdentity(channel, chatId, req.chat_lid);" in plan and "const ours = chat.names;" in plan
         and plan.count(AUD.CHAT_IDENTITY_JS) == 1 and plan.count(AUD.CHAT_IDENTITY_BEGIN) == 1
         and "m.chatId !== '' && !ours(m.chatId)" in plan and "!rows.some(foreign)" in plan,
         "Replay Plan must read the chat off Chat Code and count a row as the chat's when its chatId is the chat id or its code "
         "(audience.CHAT_IDENTITY_JS verbatim) — a row is the chat's by those two ids only, any other makes the page nothing")
    need("else if (!ours(String(newest.from))) why = 'not_from_chat';" in plan,
         "Replay Plan must hand on only a row whose own `from` is the chat (its id or its code) — a group member's message "
         "names the member there and is never replayed (the templates' only group guard «If» from == chatId)")
    need("    from: chatId,\n" in plan and "    chatId: chatId,\n" in plan
         and "  if (channel === 'whatsapp') message.whatsapp_chat_id = chatLid;\n" in plan
         and "replayMessage(channel, profileId, chatId, chat.code, newest)" in plan,
         "the replayed message names the chat by the decision's id (chatId = from — the key of the templates' group guard and "
         "mode read), and on WhatsApp carries the code as whatsapp_chat_id, as the incoming webhook does")
    cc = P["Chat Code"].get("jsCode", "")
    need(P["Chat Code"].get("mode") == "runOnceForEachItem" and "const req = $('Chat Channel').item.json;" in cc
         and "if ($prevNode.name === 'Chat Card') {" in cc and "idOf(d) === req.chat_id" in cc
         and "body.uuid !== req.profile_id" in cc and "body.status === 'done'" in cc and "Number(res.statusCode) === 200" in cc
         and "card.chat.LID" in cc and "chatLid = chatIdentity(req.channel, req.chat_id, lid).code;" in cc
         and cc.count(AUD.CHAT_IDENTITY_JS) == 1 and cc.count(AUD.CHAT_IDENTITY_BEGIN) == 1
         and "chat_lid: chatLid" in cc,
         "Chat Code must run per item and take the code only off the card it was handed (Chat Card), from an answer that counts "
         "(200, done, this profile's) and the dialog whose id IS the chat id, and only an @lid code — anything else is no code")
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
    ("Backfill stores the assistant's API sends where it worked",
     _wh("Backfill", "query", " and (select worked from worked))", " and false)"), "differs from audience.BACKFILL_SQL"),
    ("Set fails closed", _wh_node("Set", "onError", None), "must continue on error"),
    ("the tail hung off Shape Set", _rewire("Shape Set", 0, ["Respond Set", "Replay Items"]), "before the answer"),
    ("the tail moved before the answer", lambda b: (_rewire("Shape Set", 0, ["Replay Items"])(b), _rewire("Replay Plan", 0, ["Respond Set"])(b)),
     "before the answer"),
    ("the replay skips the backfill", _rewire("Replay Plan", 0, ["Replay?"]), "the tail must run"),
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
    ("Replay Plan replays a group member's message", _wh("Replay Plan", "jsCode", "else if (!ours(String(newest.from))) why = 'not_from_chat';\n", ""),
     "own `from` is the chat"),
    ("the replay hands on the row's own sender", _wh("Replay Plan", "jsCode", "    from: chatId,\n", "    from: String(row.from),\n"),
     "names the chat by the decision's id"),
    ("the replay leaves the code out", _wh("Replay Plan", "jsCode", "  if (channel === 'whatsapp') message.whatsapp_chat_id = chatLid;\n", ""),
     "names the chat by the decision's id"),
    ("Replay Plan counts any row as the chat's", _wh("Replay Plan", "jsCode", "const ours = chat.names;", "const ours = (id) => true;"),
     "count a row as the chat's"),
    ("Replay Plan ignores the code", _wh("Replay Plan", "jsCode", "chatIdentity(channel, chatId, req.chat_lid)", "chatIdentity(channel, chatId, '')"),
     "count a row as the chat's"),
    ("Replay Plan takes any code", _wh("Replay Plan", "jsCode", "id === chatId || (lid !== '' && id === lid)",
                                       "id === chatId || String(id).endsWith(CODE_SUFFIX)"), "count a row as the chat's"),
    ("the replay carries no code", _wh("Replay Plan", "jsCode", "replayMessage(channel, profileId, chatId, chat.code, newest)",
                                       "replayMessage(channel, profileId, chatId, '', newest)"), "names the chat by the decision's id"),
    ("Replay Plan reads the chat off Chat Channel", _wh("Replay Plan", "jsCode", "const req = $('Chat Code').item.json;",
                                                         "const req = $('Chat Channel').item.json;"), "read the chat off Chat Code"),
    ("Chat Code takes any dialog", _wh("Chat Code", "jsCode", "idOf(d) === req.chat_id", "idOf(d) !== ''"), "Chat Code must"),
    ("Chat Code takes a crossed card", _wh("Chat Code", "jsCode",
                                           " && !(typeof body.uuid === 'string' && body.uuid !== '' && body.uuid !== req.profile_id)", ""),
     "Chat Code must"),
    ("Chat Code takes a LID that is no code", _wh("Chat Code", "jsCode", "chatLid = chatIdentity(req.channel, req.chat_id, lid).code;",
                                                  "chatLid = lid;"), "Chat Code must"),
    ("Chat Code takes a code without @lid", _wh("Chat Code", "jsCode", "\n    && code.endsWith(CODE_SUFFIX)", ""), "Chat Code must"),
    ("Chat Code takes a code on Telegram", _wh("Chat Code", "jsCode", "channel === 'whatsapp' && ", ""), "Chat Code must"),
    ("Chat Code reads any input as a card", _wh("Chat Code", "jsCode", "if ($prevNode.name === 'Chat Card') {", "if (true) {"),
     "Chat Code must"),
    ("Chat Code runs once for all items", _wh_node("Chat Code", "parameters", {"jsCode": CRA.CHAT_CODE_JS}), "Chat Code must"),
    ("the card asked on Telegram", _cond("Need Card?", "$json.channel === 'whatsapp' && ", ""), "Need Card? must"),
    ("the card asked for a code", _cond("Need Card?", "!String($json.chat_id).endsWith('@lid') && ", ""), "Need Card? must"),
    ("the card asked by the whole chat id", _wh("Chat Card", "jsonQuery", "client_name: " + CHAT_DIGITS, "client_name: $json.chat_id"),
     "Chat Card must ask"),
    ("the card asked of Telegram's list", _wh("Chat Card", "url", "https://wappi.pro/api/", "https://wappi.pro/tapi/"), "Chat Card must ask"),
    ("Chat Card without neverError", lambda b: node(b[WEBHOOK_FILE], "Chat Card")["parameters"]["options"]["response"]["response"]
     .__setitem__("neverError", False), "Chat Card must be a Wappi read"),
    ("Chat Card on another credential", _wh_node("Chat Card", "credentials", {"httpHeaderAuth": {"id": "dev0000000000000",
                                                                                                "name": "Wappi Dev"}}),
     "Chat Card must be a Wappi read"),
    ("Chat Card marks the chat read", _wh("Chat Card", "jsonQuery", "profile_id: $json.profile_id, ", "profile_id: $json.profile_id, mark_all: true, "),
     "mark_all"),
    ("the card skipped", _rewire("Has Channel?", 0, ["Recent Messages"]), "the tail must run"),
    ("Chat Code skipped on the card's way", _rewire("Chat Card", 0, ["Recent Messages"]), "the tail must run"),
    ("Chat Code disabled", _wh_node("Chat Code", "disabled", True), "Chat Code is disabled"),
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
                   f"fork, audience.py's statements, the answer before the tail, Wappi reads without mark_all, the WhatsApp "
                   f"chat card's @lid code, the replay to localhost only after the backfill, the A5 phone rule, protected "
                   f"everywhere; tripwire self-check: {len(WEBHOOK_MUTATIONS)} mutations rejected")


# ================================================================================================ group: decide
# Task 3: the bots' Decide Audience sub-workflow crAudienceDec001 (build-decide-audience.py). Behaviour:
# tests/test_decide_audience.py.
DEC = _module("build_decide_audience", "build-decide-audience.py")
DECIDE_FILE = DEC.WF_FILE
DECIDE_TRIGGER = "When Executed by Another Workflow"
# Written here on their own — not read from the generator that wrote the workflow.
DECIDE_INPUTS = [{"name": "channel", "type": "string"}, {"name": "profileId", "type": "string"},
                 {"name": "chatId", "type": "string"}, {"name": "chatLid", "type": "string"},
                 {"name": "messageId", "type": "string"}, {"name": "chatType", "type": "string"},
                 {"name": "isBot", "type": "boolean"}, {"name": "connectedAtMs", "type": "number"},
                 {"name": "connectionPhone", "type": "string"}]
DECIDE_NODES = (DECIDE_TRIGGER, "Has Connection?", "Ensure Connection", "Plan Facts", "Can Decide?", "Need Card?", "Chat Card",
                "Read Card", "Need History?", "Recent Messages", "Decide", "Persist?", "Write Decision", "Became Business?",
                "Backfill", "Shape")
# Every edge (source, output, target): every way out of the graph ends in Shape, and nothing else is wired.
DECIDE_EDGES = sorted([
    (DECIDE_TRIGGER, 0, "Has Connection?"),
    ("Has Connection?", 0, "Plan Facts"), ("Has Connection?", 1, "Ensure Connection"), ("Ensure Connection", 0, "Plan Facts"),
    ("Plan Facts", 0, "Can Decide?"), ("Can Decide?", 0, "Need Card?"), ("Can Decide?", 1, "Shape"),
    ("Need Card?", 0, "Chat Card"), ("Need Card?", 1, "Need History?"), ("Chat Card", 0, "Read Card"),
    ("Read Card", 0, "Need History?"), ("Need History?", 0, "Recent Messages"), ("Need History?", 1, "Decide"),
    ("Recent Messages", 0, "Decide"), ("Decide", 0, "Persist?"), ("Persist?", 0, "Write Decision"), ("Persist?", 1, "Shape"),
    ("Write Decision", 0, "Became Business?"), ("Became Business?", 0, "Backfill"), ("Became Business?", 1, "Shape"),
    ("Backfill", 0, "Shape")])
DECIDE_CONDITIONS = {
    "Has Connection?": "={{ typeof $json.connectedAtMs === 'number' && Number.isSafeInteger($json.connectedAtMs) "
                       "&& $json.connectedAtMs > 0 }}",
    "Can Decide?": "={{ $json.go === true }}",
    "Need Card?": "={{ $json.needCard === true }}",
    "Need History?": "={{ $json.needHistory === true }}",
    "Persist?": "={{ $json.persist === true }}",
    "Became Business?": "={{ $json.kind === 'business' && ($json.source === 'default' || $json.source === 'owner') }}",
}
# The fields only Write Decision's ROW carries (DECIDE_WRITE_SQL answers kind, reason, source). When Write Decision throws
# before its statement, n8n hands its INPUT item on unchanged — Decide's — so no node before it may emit any of them, or
# the business gates (Became Business?, Shape) could take that item for the row.
ROW_FIELDS = ("kind", "reason", "source")
BEFORE_THE_WRITE = ("Plan Facts", "Read Card", "Decide")
DECIDE_STATEMENTS = {
    "Ensure Connection": ("audience.ENSURE_CONNECTION_SQL", AUD.ENSURE_CONNECTION_SQL, "={{ [ $json.profileId ] }}",
                          ("Has Connection?", 1)),
    "Write Decision": ("audience.DECIDE_WRITE_SQL", AUD.DECIDE_WRITE_SQL,
                       "={{ [ $json.profileId, $json.chatId, $json.verdictKind, $json.verdictReason ] }}", ("Persist?", 0)),
    "Backfill": ("audience.BACKFILL_SQL", AUD.BACKFILL_SQL(DEC.AHH.MEMORY_ROW, DEC.AHH.OWNER_CONTENT_SQL),
                 "={{ [ $('Decide').first().json.profileId, $('Decide').first().json.chatId, "
                 "$('Decide').first().json.messageId, JSON.stringify($('Decide').first().json.page) ] }}",
                 ("Became Business?", 0)),
}
DECIDE_READS = {"Chat Card": ("https://wappi.pro/tapi/sync/chats/get", "https://wappi.pro/api/sync/chats/filter"),
                "Recent Messages": ("sync/messages/get",)}
RULE_CARRIERS = ("Plan Facts", "Read Card", "Decide")


def _edges(wf):
    return sorted((s, i, e.get("node")) for s, c in wf["connections"].items() for i, out in enumerate((c or {}).get("main") or [])
                  for e in (out or []))


def _js_code(js):
    """A Code body without its `//` comments (they explain; the code decides — the comments name contact_name on purpose)."""
    return "\n".join(re.sub(r"(^|\s)//.*$", "", line) for line in (js or "").split("\n"))


def check_decide(bundle, sql=None):
    """Findings for the Decide Audience sub-workflow. `bundle` = {file name: workflow, PROTECTED_KEY: ids, ACTIVE_KEY: ids};
    `sql` is unused (the self-check's signature)."""
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)
        return cond

    wf = bundle.get(DECIDE_FILE) or {"nodes": [], "connections": {}}
    N = {n.get("name"): n for n in wf.get("nodes", [])}
    P = {k: (N[k].get("parameters") or {}) for k in N}
    flat = json.dumps(wf, ensure_ascii=False)

    # (7) first — it must be said even when the node set changed: three statements, each entered from its own gate alone
    pg = sorted(k for k, n in N.items() if n.get("type") == "n8n-nodes-base.postgres")
    need(pg == sorted(DECIDE_STATEMENTS),
         f"Decide Audience must hold exactly three Postgres nodes (Ensure Connection, Write Decision, Backfill) — each costs "
         f"≈ 1.9 s on the first message of a chat: {pg}")
    # (1) the generator's output, exactly its nodes, none disabled, successful executions not saved
    need(json.dumps(wf, indent=2, ensure_ascii=False) + "\n" == DEC.text(),
         f"{DECIDE_FILE} differs from build-decide-audience.py's output — edit the generator, never the JSON")
    need((wf.get("settings") or {}).get("saveDataSuccessExecution") == "none" and (wf.get("settings") or {}).get("executionOrder") == "v1",
         "settings.saveDataSuccessExecution must be 'none' (a successful execution must not keep the chat list and the page it "
         "read) and executionOrder v1")
    if not need(sorted(N) == sorted(DECIDE_NODES), f"the node set changed: {sorted(N)}"):
        return fails
    for name, n in N.items():
        need(not n.get("disabled"), f"{name} is disabled — n8n hands a disabled node's input on untouched")

    # (2) the trigger and its nine inputs
    t, tp = N[DECIDE_TRIGGER], P[DECIDE_TRIGGER]
    values = ((tp.get("workflowInputs") or {}).get("values")) or []
    need(t.get("type") == "n8n-nodes-base.executeWorkflowTrigger" and t.get("typeVersion", 0) >= 1.1
         and tp.get("inputSource") == "workflowInputs" and values == DECIDE_INPUTS,
         f"the trigger must declare exactly the nine inputs {[v['name'] for v in DECIDE_INPUTS]} with their types: {values}")
    need(not any((v.get("name") or "").lower() in ("contactname", "contact_name") for v in values),
         "no contactName input: the webhook's contact_name is no contact fact — Wappi fills it from the person's own profile "
         "name too, so a stranger would read as a saved contact")

    # (3) the graph: every edge, and the gates that keep calls and writes behind them
    need(_edges(wf) == DECIDE_EDGES, f"the graph changed — every way out must end in Shape: {_edges(wf)}")
    need(not ((wf["connections"].get("Shape") or {}).get("main") or [[]])[0],
         "Shape must be terminal: n8n hands the calling template the LAST node's items")
    reach = reachable(wf, DECIDE_TRIGGER, cut=("Can Decide?", 0))
    need(not reach & {"Chat Card", "Recent Messages", "Write Decision", "Backfill"},
         f"Can Decide?: a stop (malformed input, no moment, the settling window) reaches a Wappi call or a write: {sorted(reach)}")
    for name, (label, text, params, gate) in DECIDE_STATEMENTS.items():
        need(sources(wf, name) == [gate], f"{name} must be entered from {gate[0]} output {gate[1]} alone: {sources(wf, name)}")
        need(name not in reachable(wf, DECIDE_TRIGGER, cut=gate),
             f"{name} is reachable with {gate[0]} output {gate[1]} cut — it must run only behind that gate")

    # (4) the Ifs judge positive fields
    for name, expr in DECIDE_CONDITIONS.items():
        conds = ((P[name].get("conditions") or {}).get("conditions")) or []
        op = (conds[0].get("operator") or {}) if len(conds) == 1 else {}
        need(len(conds) == 1 and conds[0].get("leftValue") == expr and op.get("type") == "boolean" and op.get("operation") == "true",
             f"{name} must judge a positive field — exactly {expr}, never «the node continued»: {conds}")

    # (5) the Wappi reads
    https = sorted(k for k, n in N.items() if n.get("type") == "n8n-nodes-base.httpRequest")
    need(https == sorted(DECIDE_READS), f"the only HTTP nodes must be the two Wappi reads: {https}")
    for name, paths in DECIDE_READS.items():
        n, p = N[name], P[name]
        resp = ((p.get("options") or {}).get("response") or {}).get("response") or {}
        timeout = (p.get("options") or {}).get("timeout")
        need(n.get("type") == "n8n-nodes-base.httpRequest" and p.get("method") == "GET" and n.get("credentials") == WAPPI_CRED
             and resp.get("fullResponse") is True and resp.get("neverError") is True
             and isinstance(timeout, int) and 0 < timeout <= 10000 and n.get("onError") == "continueRegularOutput"
             and n.get("alwaysOutputData") is True and _hosts(p.get("url"))
             and all(any(h.startswith(b) for b in WAPPI_BASES) for h in _hosts(p.get("url")))
             and all(x in (p.get("url") or "") for x in paths),
             f"{name} must be a Wappi read: GET, the canonical WappiAuthToken credential, fullResponse + neverError, at most "
             f"10 s, continue on error, only the Wappi bases ({', '.join(paths)})")
        need(not re.search(r"\basc\b", json.dumps(p, ensure_ascii=False), re.I),
             f"{name} asks order asc — never: Telegram's tapi ignores asc (it re-sorts the newest page), so the oldest message "
             f"would be the incoming one and an old acquaintance would look new")
    need("mark_all" not in flat.lower(), "mark_all must appear nowhere in Decide Audience — reading a chat must never mark it read")
    card = P["Chat Card"].get("jsonQuery", "")
    need("{ profile_id: $json.profileId, limit: 20, offset: 0, order: 'desc' }" in card,
         "Chat Card on Telegram must read the first page of chats/get: limit 20, offset 0, order desc")
    need("{ profile_id: $json.profileId, client_name: $json.clientName }" in card,
         "Chat Card on WhatsApp must filter chats/filter by client_name — the digits of the chat id (Plan Facts' clientName)")
    rq = P["Recent Messages"].get("jsonQuery", "")
    need(f"chat_id: $json.chatId, limit: {AUD.HISTORY_LIMIT}, order: 'desc'" in rq and AUD.HISTORY_LIMIT > AUD.RECENT_LIMIT,
         f"Recent Messages must read THIS chat's newest page: limit audience.HISTORY_LIMIT ({AUD.HISTORY_LIMIT}, larger than the "
         f"backfill's {AUD.RECENT_LIMIT}), order desc")

    # (6) the Code nodes: the rule verbatim, the window, the card, the page, the answer
    for name in RULE_CARRIERS:
        need(P[name].get("jsCode", "").count(AUD.RULE_JS) == 1, f"{name} must embed audience.RULE_JS verbatim, once")
    for name in RULE_CARRIERS + ("Shape",):
        code = _js_code(P[name].get("jsCode", ""))
        need("contact_name" not in code and "contactName" not in code,
             f"{name} reads the webhook's contact_name — the WhatsApp contact is the chat card's contact.FullName only")
    pf = P["Plan Facts"].get("jsCode", "")
    need(f"const SETTLE_MINUTES = {AUD.SETTLE_MINUTES};" in pf and "if (Date.now() < plan.connectedAtMs + SETTLE_MINUTES * 60000) {" in pf,
         f"Plan Facts must stop for audience.SETTLE_MINUTES ({AUD.SETTLE_MINUTES}) after the connection moment")
    need("if ($('Ensure Connection').isExecuted) {" in pf and "$('When Executed by Another Workflow').first().json" in pf,
         "Plan Facts must take the moment from the input, or from Ensure Connection only when it ran (isExecuted)")
    need("const LID_SUFFIX = \"@lid\";" in pf and "} else if (chatId.endsWith(LID_SUFFIX)) {" in pf
         and "plan.facts = Object.assign({}, given, { contact: false });" in pf,
         "Plan Facts must ask no card for a chat known only by its @lid code — its contact is false, its history decides")
    need("digits(chatId.split('@')[0]) === own" in pf and "plan.clientName = digits(chatId.split('@')[0]);" in pf,
         "Plan Facts must know WhatsApp's chat with oneself by the digits of the connected number, and filter the card by the "
         "digits of the chat id")
    rc = P["Read Card"].get("jsCode", "")
    need("idOf(d) === plan.chatId" in rc and "body.uuid !== plan.profileId" in rc and "body.status === 'done'" in rc,
         "Read Card must take the dialog whose id IS the chat id (the filter matches names and last messages too) of a page "
         "that is this profile's")
    need("typeof contact.FullName === 'string' && contact.FullName.trim() !== ''" in rc,
         "Read Card's WhatsApp contact must be contact.FullName — a name from the phone's address book")
    dj = P["Decide"].get("jsCode", "")
    need(f"const HISTORY_LIMIT = {AUD.HISTORY_LIMIT};" in dj and f"const RECENT_LIMIT = {AUD.RECENT_LIMIT};" in dj
         and "String(m.id) === plan.messageId" in dj
         and "time(m) * 1000 < plan.connectedAtMs" in dj and "facts.history = rows.length < HISTORY_LIMIT ? false : null;" in dj
         and "const chat = chatIdentity(plan.channel, plan.chatId, plan.chatLid);" in dj
         and "typeof m.chatId === 'string' && m.chatId !== '' && !chat.names(m.chatId));" in dj and "body.uuid !== plan.profileId" in dj
         and "rows.every(ours)" in dj and "persist: verdict.reason !== 'unknown'" in dj and "page = rows.slice(0, RECENT_LIMIT);" in dj,
         "Decide must read the history off the one page: this profile's and this chat's (its code counts), holding the trigger, "
         "a row older than the connection → true, a page shorter than HISTORY_LIMIT → false, a full one of new rows → unknown; "
         "persist only a known reason; hand the newest RECENT_LIMIT rows of the page on as Wappi returned them")
    need(dj.count(AUD.CHAT_IDENTITY_JS) == 1 and dj.count(AUD.CHAT_IDENTITY_BEGIN) == 1,
         "Decide must embed audience.CHAT_IDENTITY_JS verbatim, once — a row is the chat's by its id or WhatsApp's @lid code")
    sh = P["Shape"].get("jsCode", "")
    need("const row = $('Write Decision').first().json || {};" in sh
         and "const written = row.source === 'default' || row.source === 'owner';" in sh
         and "if (written && row.kind === 'business') return done('business', 'business');" in sh
         and sh.count("done('business'") == 1,
         "Shape must answer business ONLY on the row Write Decision returned saying business — a row carries a source "
         "(default / owner); the item Write Decision hands on when it throws before its statement does not")
    for name in BEFORE_THE_WRITE:
        own = _js_code(P[name].get("jsCode", "").replace(AUD.RULE_JS, ""))
        emitted = sorted(set(re.findall(r"(?<![\w.$])(" + "|".join(ROW_FIELDS) + r")\s*:", own)))
        need(not emitted,
             f"{name} emits {emitted} — a field of Write Decision's row: when Write Decision throws before its statement it "
             f"hands on its input unchanged, and the business gates must never take that item for the row")
    need("verdictKind: verdict.kind" in dj and "verdictReason: verdict.reason" in dj,
         "Decide must hand its verdict on as verdictKind / verdictReason (never as the row's kind / reason)")

    # (7) the statements
    for name, (label, text, params, gate) in DECIDE_STATEMENTS.items():
        n = N[name]
        need(P[name].get("operation") == "executeQuery" and P[name].get("query") == text,
             f"{name}'s query differs from {label} — edit the module, never the JSON")
        need((P[name].get("options") or {}).get("queryReplacement") == params, f"{name}'s parameters must be exactly {params}")
        need(n.get("onError") == "continueRegularOutput" and n.get("alwaysOutputData") is True and n.get("credentials") == CANONICAL_PG,
             f"{name} must continue on error with output on the canonical Postgres credential — Shape judges the rows")

    # (8) protected everywhere
    need(DEC.WF_ID in (bundle.get(PROTECTED_KEY) or []), f"{DEC.WF_ID} is missing from build-client-webhooks.PROTECTED_IDS (protected)")
    active = bundle.get(ACTIVE_KEY) or []
    need(DEC.WF_ID in active and "stgEvalRunner001" in active and active.index(DEC.WF_ID) < active.index("stgEvalRunner001"),
         f"{DEC.WF_ID} is missing from staging/bootstrap.py ACTIVE_CANONICAL (before the staging-only eval runner)")
    for fname, holders in PROTECTED_DEPENDENTS.items():
        dep = bundle.get(fname) or {"nodes": []}
        for h in holders:
            p = (node(dep, h) or {}).get("parameters") or {}
            need(f"'{DEC.WF_ID}'" in (p.get("query") or p.get("jsCode") or ""),
                 f"{fname} / {h} does not carry {DEC.WF_ID} in its protected list — regenerate it from build-client-webhooks.PROTECTED_IDS")
    return fails


def _dec(name, key, old, new):
    """Replace `old` by `new` in one text parameter of one Decide Audience node (the anchor must be there)."""
    def m(bundle):
        p = node(bundle[DECIDE_FILE], name)["parameters"]
        target = p["options"] if key == "queryReplacement" else p
        assert old in target[key], f"mutation anchor not found in {name}.{key}: {old[:70]!r}"
        target[key] = target[key].replace(old, new, 1)
    return m


def _dec_cond(name, expr):
    def m(bundle):
        node(bundle[DECIDE_FILE], name)["parameters"]["conditions"]["conditions"][0]["leftValue"] = expr
    return m


def _dec_node(name, key, value):
    def m(bundle):
        n = node(bundle[DECIDE_FILE], name)
        if value is None:
            n.pop(key)
        else:
            n[key] = copy.deepcopy(value)
    return m


def _dec_response(name, key):
    def m(bundle):
        node(bundle[DECIDE_FILE], name)["parameters"]["options"]["response"]["response"].pop(key)
    return m


def _dec_rewire(src, out_index, dsts):
    def m(bundle):
        outs = bundle[DECIDE_FILE]["connections"].setdefault(src, {"main": []})["main"]
        while len(outs) <= out_index:
            outs.append([])
        outs[out_index] = [{"node": d, "type": "main", "index": 0} for d in dsts]
    return m


def _dec_extra_postgres(bundle):
    wf = bundle[DECIDE_FILE]
    extra = copy.deepcopy(node(wf, "Write Decision"))
    extra.update(name="Read Decision Again", id="00000000-0000-0000-0000-00000000dec1", position=[2420, 600])
    wf["nodes"].append(extra)
    wf["connections"]["Decide"] = {"main": [[{"node": "Read Decision Again", "type": "main", "index": 0}]]}
    wf["connections"]["Read Decision Again"] = {"main": [[{"node": "Persist?", "type": "main", "index": 0}]]}


def _dec_input(value):
    def m(bundle):
        node(bundle[DECIDE_FILE], DECIDE_TRIGGER)["parameters"]["workflowInputs"]["values"].append(copy.deepcopy(value))
    return m


def _dec_drop_id(key):
    def m(bundle):
        assert DEC.WF_ID in bundle[key]
        bundle[key] = [x for x in bundle[key] if x != DEC.WF_ID]
    return m


def _dec_unprotect(fname, holder):
    def m(bundle):
        p = node(bundle[fname], holder)["parameters"]
        k = "query" if "query" in p else "jsCode"
        assert f"'{DEC.WF_ID}'" in p[k], f"{fname} / {holder} carries no {DEC.WF_ID}"
        p[k] = p[k].replace(f"'{DEC.WF_ID}', ", "").replace(f",\n  '{DEC.WF_ID}'", "").replace(f", '{DEC.WF_ID}'", "")
    return m


_RULE_EDIT = ("if (f.contact) return personal('contact');", "if (f.contact === 'yes') return personal('contact');")
DECIDE_MUTATIONS = (
    ("the card read without neverError", _dec_response("Chat Card", "neverError"), "Chat Card must be a Wappi read"),
    ("the history read without neverError", _dec_response("Recent Messages", "neverError"), "Recent Messages must be a Wappi read"),
    ("the history read without fullResponse", _dec_response("Recent Messages", "fullResponse"), "Recent Messages must be a Wappi read"),
    ("mark_all in the history URL", _dec("Recent Messages", "url", "'sync/messages/get'", "'sync/messages/get?mark_all=true'"), "mark_all"),
    ("mark_all in the card query", _dec("Chat Card", "jsonQuery", "client_name: $json.clientName", "client_name: $json.clientName, mark_all: true"),
     "mark_all"),
    ("Persist? by «the node continued»", _dec_cond("Persist?", "={{ $json !== undefined }}"), "Persist? must judge a positive field"),
    ("Became Business? by Decide's verdict", _dec_cond("Became Business?", "={{ $('Decide').first().json.verdictKind === 'business' }}"),
     "Became Business? must judge a positive field"),
    ("Became Business? without the row's source", _dec_cond("Became Business?", "={{ $json.kind === 'business' }}"),
     "Became Business? must judge a positive field"),
    ("Became Business? on a field Decide emits", _dec_cond("Became Business?", "={{ $json.verdictKind === 'business' }}"),
     "Became Business? must judge a positive field"),
    ("Decide emits the row's kind again", _dec("Decide", "jsCode", "verdictKind: verdict.kind,", "kind: verdict.kind, verdictKind: verdict.kind,"),
     "Decide emits ['kind']"),
    ("Decide hands the verdict on as the row's fields", _dec("Decide", "jsCode", "verdictKind: verdict.kind,\n                  verdictReason: verdict.reason,",
                                                             "kind: verdict.kind,\n                  reason: verdict.reason,"),
     "Decide emits ['kind', 'reason']"),
    ("Decide emits a source", _dec("Decide", "jsCode", "persist: verdict.reason !== 'unknown', page }",
                                   "persist: verdict.reason !== 'unknown', page, source: 'default' }"), "Decide emits ['source']"),
    ("Plan Facts emits a source", _dec("Plan Facts", "jsCode", "clientName: '', facts: null,", "clientName: '', source: 'default', facts: null,"),
     "Plan Facts emits ['source']"),
    ("Shape takes any kind business for the row", _dec("Shape", "jsCode", "if (written && row.kind === 'business')", "if (row.kind === 'business')"),
     "Shape must answer business ONLY"),
    ("Has Connection? lets a string moment through", _dec_cond("Has Connection?", "={{ !!$json.connectedAtMs }}"),
     "Has Connection? must judge a positive field"),
    ("Can Decide? by «the node continued»", _dec_cond("Can Decide?", "={{ $json.go !== false }}"), "Can Decide? must judge a positive field"),
    ("an extra Postgres node", _dec_extra_postgres, "exactly three Postgres nodes"),
    ("order asc in the history read", _dec("Recent Messages", "jsonQuery", "order: 'desc'", "order: 'asc'"), "Recent Messages asks order asc"),
    ("the plan's oldest message (limit 1, asc)", _dec("Recent Messages", "jsonQuery", f"limit: {AUD.HISTORY_LIMIT}, order: 'desc'",
                                                      "limit: 1, order: 'asc'"), "Recent Messages asks order asc"),
    ("order asc in the card read", _dec("Chat Card", "jsonQuery", "offset: 0, order: 'desc'", "offset: 0, order: 'asc'"),
     "Chat Card asks order asc"),
    ("a contactName input", _dec_input({"name": "contactName", "type": "string"}), "no contactName input"),
    ("a tenth input", _dec_input({"name": "senderName", "type": "string"}), "exactly the nine inputs"),
    ("isBot declared as a string", lambda b: node(b[DECIDE_FILE], DECIDE_TRIGGER)["parameters"]["workflowInputs"]["values"][6].__setitem__(
        "type", "string"), "exactly the nine inputs"),
    ("the WhatsApp card without client_name", _dec("Chat Card", "jsonQuery", ", client_name: $json.clientName", ""), "filter chats/filter by client_name"),
    ("the Telegram card on another page", _dec("Chat Card", "jsonQuery", "limit: 20, offset: 0", "limit: 50, offset: 0"),
     "first page of chats/get"),
    ("the history page shrunk", _dec("Recent Messages", "jsonQuery", f"limit: {AUD.HISTORY_LIMIT},", "limit: 5,"),
     "limit audience.HISTORY_LIMIT"),
    ("the history page cut back to the backfill's slice (a longer new conversation stays undecided for good)",
     _dec("Recent Messages", "jsonQuery", f"limit: {AUD.HISTORY_LIMIT},", f"limit: {AUD.RECENT_LIMIT},"), "limit audience.HISTORY_LIMIT"),
    ("the history read of another chat", _dec("Recent Messages", "jsonQuery", "chat_id: $json.chatId", "chat_id: $json.chatLid"),
     "limit audience.HISTORY_LIMIT"),
    ("the card read waits 30 s", lambda b: node(b[DECIDE_FILE], "Chat Card")["parameters"]["options"].__setitem__("timeout", 30000),
     "Chat Card must be a Wappi read"),
    ("the history read on another credential", _dec_node("Recent Messages", "credentials", {"httpHeaderAuth": {"id": "dev0000000000000",
                                                                                                        "name": "Wappi Dev"}}),
     "Recent Messages must be a Wappi read"),
    ("the card read on another host", _dec("Chat Card", "url", "https://wappi.pro/api/", "https://wappi.example/api/"), "Chat Card must be a Wappi read"),
    ("Decide's rule edited", _dec("Decide", "jsCode", *_RULE_EDIT), "Decide must embed audience.RULE_JS"),
    ("Plan Facts' rule edited", _dec("Plan Facts", "jsCode", *_RULE_EDIT), "Plan Facts must embed audience.RULE_JS"),
    ("Read Card's rule edited", _dec("Read Card", "jsCode", *_RULE_EDIT), "Read Card must embed audience.RULE_JS"),
    ("Read Card reads the webhook's contact_name", _dec("Read Card", "jsCode", "const facts = Object.assign({}, plan.facts);",
                                                       "const facts = Object.assign({}, plan.facts, { contact: !!plan.contact_name });"),
     "Read Card reads the webhook's contact_name"),
    ("Read Card takes the push name for a contact", _dec("Read Card", "jsCode", "typeof contact.FullName === 'string' && contact.FullName.trim()",
                                                        "typeof contact.PushName === 'string' && contact.PushName.trim()"),
     "contact.FullName"),
    ("Read Card takes the first dialog", _dec("Read Card", "jsCode", "idOf(d) === plan.chatId", "idOf(d) !== ''"),
     "the dialog whose id IS the chat id"),
    ("the settling window shortened", _dec("Plan Facts", "jsCode", f"const SETTLE_MINUTES = {AUD.SETTLE_MINUTES};", "const SETTLE_MINUTES = 5;"),
     "Plan Facts must stop for audience.SETTLE_MINUTES"),
    ("Plan Facts asks a card for a code", _dec("Plan Facts", "jsCode", "} else if (chatId.endsWith(LID_SUFFIX)) {",
                                               "} else if (chatId.endsWith('@nothing')) {"), "no card for a chat known only by its @lid code"),
    ("Decide counts a page without the trigger", _dec("Decide", "jsCode", "String(m.id) === plan.messageId", "true"),
     "Decide must read the history off the one page"),
    ("Decide calls a full page of new rows the whole chat",
     _dec("Decide", "jsCode", "facts.history = rows.length < HISTORY_LIMIT ? false : null;", "facts.history = false;"),
     "Decide must read the history off the one page"),
    ("Decide judges a short page by the backfill's slice",
     _dec("Decide", "jsCode", "facts.history = rows.length < HISTORY_LIMIT ? false : null;",
          "facts.history = rows.length < RECENT_LIMIT ? false : null;"), "Decide must read the history off the one page"),
    ("Decide counts rows of any chat", _dec("Decide", "jsCode", " && rows.every(ours)", ""), "Decide must read the history off the one page"),
    ("Decide ignores the code", _dec("Decide", "jsCode", "chatIdentity(plan.channel, plan.chatId, plan.chatLid)",
                                     "chatIdentity(plan.channel, plan.chatId, '')"), "Decide must read the history off the one page"),
    ("Decide takes any code", _dec("Decide", "jsCode", "id === chatId || (lid !== '' && id === lid)",
                                   "id === chatId || String(id).endsWith(CODE_SUFFIX)"), "Decide must embed audience.CHAT_IDENTITY_JS"),
    ("Decide takes a code on Telegram", _dec("Decide", "jsCode", "channel === 'whatsapp' && ", ""), "Decide must embed audience.CHAT_IDENTITY_JS"),
    ("Decide takes a code without @lid", _dec("Decide", "jsCode", "\n    && code.endsWith(CODE_SUFFIX)", ""),
     "Decide must embed audience.CHAT_IDENTITY_JS"),
    ("Decide sorts the page before the backfill", _dec("Decide", "jsCode", "page = rows.slice(0, RECENT_LIMIT);",
                                                       "page = rows.slice(0, RECENT_LIMIT).reverse();"),
     "Decide must read the history off the one page"),
    ("Decide hands the whole history page to the backfill", _dec("Decide", "jsCode", "page = rows.slice(0, RECENT_LIMIT);", "page = rows;"),
     "Decide must read the history off the one page"),
    ("Decide writes an unknown", _dec("Decide", "jsCode", "persist: verdict.reason !== 'unknown'", "persist: true"),
     "Decide must read the history off the one page"),
    ("Shape says business on Decide's verdict", _dec("Shape", "jsCode", "const row = $('Write Decision').first().json || {};",
                                                     "const row = $('Decide').first().json || {};"), "Shape must answer business ONLY"),
    ("Write Decision edited by hand", _dec("Write Decision", "query", "'default', $4, now(), now()", "'owner', $4, now(), now()"),
     "differs from audience.DECIDE_WRITE_SQL"),
    ("Backfill handed a second read's page", _dec("Backfill", "queryReplacement", "JSON.stringify($('Decide').first().json.page)",
                                                  "JSON.stringify($json.messages)"), "Backfill's parameters must be exactly"),
    ("Backfill drops the owner's app sends everywhere", _dec("Backfill", "query", " and (select worked from worked))", ")"),
     "differs from audience.BACKFILL_SQL"),
    ("Write Decision fails closed", _dec_node("Write Decision", "onError", None), "Write Decision must continue on error"),
    ("Ensure Connection on the true branch", lambda b: (_dec_rewire("Has Connection?", 0, ["Ensure Connection"])(b),
                                                        _dec_rewire("Has Connection?", 1, ["Plan Facts"])(b)),
     "Ensure Connection must be entered from Has Connection? output 1 alone"),
    ("Write Decision reachable without Persist?", _dec_rewire("Decide", 0, ["Persist?", "Write Decision"]),
     "Write Decision must be entered from Persist? output 0 alone"),
    ("Backfill without Became Business?", _dec_rewire("Write Decision", 0, ["Became Business?", "Backfill"]),
     "Backfill must be entered from Became Business? output 0 alone"),
    ("a stop through the card", _dec_rewire("Can Decide?", 1, ["Need Card?"]), "a stop (malformed input"),
    ("Shape not the last node", _dec_rewire("Shape", 0, ["Has Connection?"]), "Shape must be terminal"),
    ("successful executions saved", lambda b: b[DECIDE_FILE]["settings"].__setitem__("saveDataSuccessExecution", "all"), "saveDataSuccessExecution"),
    ("Decide disabled", _dec_node("Decide", "disabled", True), "Decide is disabled"),
    ("the node moved by hand", lambda b: node(b[DECIDE_FILE], "Shape").__setitem__("position", [0, 0]),
     "differs from build-decide-audience.py's output"),
    ("dropped from PROTECTED_IDS", _dec_drop_id(PROTECTED_KEY), "missing from build-client-webhooks.PROTECTED_IDS"),
    ("dropped from ACTIVE_CANONICAL", _dec_drop_id(ACTIVE_KEY), "missing from staging/bootstrap.py ACTIVE_CANONICAL"),
    ("Set Workflow State no longer protects it", _dec_unprotect(SET_STATE_FILE, "Validate"), f"{SET_STATE_FILE} / Validate does not carry"),
    ("Create WA's clone plan no longer spares it", _dec_unprotect("XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "Clone Plan"),
     "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json / Clone Plan does not carry"),
)


def group_decide(directory):
    bundle = {}
    for fname in (DECIDE_FILE,) + tuple(PROTECTED_DEPENDENTS):
        path = os.path.join(directory, fname)
        if not os.path.exists(path):
            return [f"{fname} is missing"], ""
        with open(path, encoding="utf-8") as fh:
            bundle[fname] = json.load(fh)
    bundle[PROTECTED_KEY] = list(BCW.PROTECTED_IDS)
    bundle[ACTIVE_KEY] = list(BOOT.ACTIVE_CANONICAL)
    fails = check_decide(bundle)
    if not fails:
        fails = self_check(bundle, check_decide, DECIDE_MUTATIONS)
    return fails, (f"decide: {DEC.WF_ID} = build-decide-audience.py's output — nine inputs (no contactName), every edge ending in "
                   f"Shape, positive-field gates, three statements of audience.py each behind its gate, two Wappi reads "
                   f"(neverError, 10 s, never mark_all, never asc), RULE_JS verbatim, business only on the written row, "
                   f"protected everywhere; tripwire self-check: {len(DECIDE_MUTATIONS)} mutations rejected")


GROUPS = {"wipe": group_wipe, "readers": group_readers, "webhook": group_webhook, "decide": group_decide}


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
