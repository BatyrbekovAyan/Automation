#!/usr/bin/env python3
"""Stage-4 (A5/A6) semantic verifier: the ONE history's writers and their sources, the memory rows'
LangChain shape + provenance marks, no double write, the handoff pause in the gate, the tool wiring
and its context fields, the notifier's privacy and its owner-number rule (2026-10-15: Telegram
account.phone first), the app webhook's owner scoping, and the integrations (Create webhook types,
Dashboard event override, Delete Bot Files wipe, Set Workflow State protection, proxy recorder after
the response). B1 «расписание бота» (2026-10-14): the schedule is one more term of the mode gate —
check_schedule_gate (the term and its absence rule, Read Reply Mode fail-closed and enabled, off_hours
handed on and stamped, the lease never reading it), self-checked on every run.

    python3 Tools/n8n/verify-history-handoff.py
    python3 Tools/n8n/verify-history-handoff.py --dir DIR   # another set of exports (mutation tests)
"""
import copy
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
BOTS = ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json")


def fail(msg):
    print("HISTORY/HANDOFF FAIL:", msg)
    sys.exit(1)


def check(cond, msg):
    if not cond:
        fail(msg)


def load(fname):
    return json.load(open(os.path.join(WF, fname), encoding="utf-8"))


def _clone_lineage():
    import importlib.util
    spec = importlib.util.spec_from_file_location("clone_lineage", os.path.join(HERE, "clone_lineage.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def _tool(fname, modname):
    import importlib.util
    spec = importlib.util.spec_from_file_location(modname, os.path.join(HERE, fname))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


_HH = None


def hh():
    """apply-history-handoff.py — the ONE place of the memory marks (T6): every writer is pinned to its constants."""
    global _HH
    if _HH is None:
        _HH = _tool("apply-history-handoff.py", "apply_history_handoff")
    return _HH


_HW = None


def hw():
    """build-handoff-workflows.py — the generator that owns OWNER_PHONE_JS, the ONE owner-number rule (A5)."""
    global _HW
    if _HW is None:
        _HW = _tool("build-handoff-workflows.py", "build_handoff_workflows")
    return _HW


def node(wf, name):
    n = next((n for n in wf["nodes"] if n["name"] == name), None)
    check(n is not None, f"missing node {name!r}")
    return n


MEM_MARK = "'additional_kwargs', jsonb_build_object('source'"

# F3 (iteration 2026-09-28, final regression 2026-10-08): the notifier's only way from `Due Notifications` to the
# transport and to Record Notify — a gate that passes a real request row alone.
NOTIFIER_DUE = "Due Notifications"
NOTIFIER_GATE = "Due Request?"
NOTIFIER_GATE_EXPR = "={{ Number($json.id) > 0 && typeof $json.profile_id === 'string' && $json.profile_id !== '' }}"


def _edges(wf, src, out=0):
    outs = wf["connections"].get(src, {}).get("main") or []
    return [e["node"] for e in (outs[out] if out < len(outs) else None) or []]


def _reachable(wf, start, cut=frozenset()):
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for i, out in enumerate(wf["connections"].get(n, {}).get("main") or []):
            if (n, i) not in cut:
                todo.extend(e["node"] for e in (out or []))
    return seen


def check_notifier_gate(ntf):
    """F3: `Due Notifications` (alwaysOutputData + continueRegularOutput) answers ONE item on every tick — `{}` when no
    request is due, `{message, error}` when the statement failed, the trigger's own item when the node threw. Until
    2026-10-08 that item went straight to `Owner Phone` (a bare `GET sync/get/status` every 2 minutes) and on to Record
    Notify. Only a real request row (a positive id and a profile id) may reach a transport node or Record Notify.
    Raises AssertionError (tests/test_handoff_notifier.py holds the negative cases)."""
    names = {n["name"]: n for n in ntf["nodes"]}
    trig = next((n["name"] for n in ntf["nodes"] if n["type"] == "n8n-nodes-base.scheduleTrigger"), None)
    assert trig, "notifier: no schedule trigger"
    gate = names.get(NOTIFIER_GATE)
    assert gate is not None and gate["type"] == "n8n-nodes-base.if", f"notifier: missing If gate {NOTIFIER_GATE!r} (F3)"
    conds = gate["parameters"]["conditions"]["conditions"]
    assert len(conds) == 1 and conds[0]["leftValue"] == NOTIFIER_GATE_EXPR \
        and conds[0]["operator"] == {"type": "boolean", "operation": "true", "singleValue": True} \
        and gate["parameters"]["conditions"].get("combinator") == "and", \
        f"notifier: {NOTIFIER_GATE} must pass only a real request row: {conds}"
    outs = ntf["connections"].get(NOTIFIER_DUE, {}).get("main") or []
    assert len(outs) == 1 and _edges(ntf, NOTIFIER_DUE) == [NOTIFIER_GATE], \
        f"notifier: {NOTIFIER_DUE} must feed {NOTIFIER_GATE} alone (F3): {outs}"
    assert _edges(ntf, NOTIFIER_GATE, 0) == ["Owner Phone"], f"notifier: {NOTIFIER_GATE}[true] must lead to Owner Phone alone"
    assert not _edges(ntf, NOTIFIER_GATE, 1), f"notifier: what {NOTIFIER_GATE} refuses must go nowhere: {_edges(ntf, NOTIFIER_GATE, 1)}"
    feeders = {(src, i) for src, c in ntf["connections"].items() for i, out in enumerate(c.get("main") or [])
               for e in (out or []) if e["node"] == "Owner Phone"}
    assert feeders == {(NOTIFIER_GATE, 0)}, f"notifier: Owner Phone must be fed by {NOTIFIER_GATE}[true] alone: {sorted(feeders)}"
    effects = {n["name"] for n in ntf["nodes"] if n["type"] == "n8n-nodes-base.httpRequest"} | {"Record Notify"}
    assert effects <= _reachable(ntf, trig), f"notifier: the notice branch is disconnected: {sorted(effects - _reachable(ntf, trig))}"
    around = effects & _reachable(ntf, trig, cut={(NOTIFIER_GATE, 0)})
    assert not around, f"notifier: {sorted(around)} reachable from the schedule without passing {NOTIFIER_GATE}[true] (F3)"


# A5 (2026-10-15, «защита платящих»): the notice goes to the owner's OWN number by the V2 rule of Auth Session / Claim Decide —
# on Telegram (wapi 'tapi') the AUTHORISED account's phone (status.account.phone) FIRST, the top-level phone (which may be a
# number someone named in auth/phone) only as the fallback; WhatsApp reads the top-level phone; digits only. Until then Compose
# Notice read the top-level phone on both channels. The rule is ONE constant, build-handoff-workflows.py OWNER_PHONE_JS (the
# A4 channel watch embeds the same text): the check below demands that text verbatim AND, independently of that constant,
# the fragments that carry the rule's meaning — so a regenerated-but-wrong rule fails here too.
OWNER_PHONE_FRAGMENTS = (
    "const acct = (wapi === 'tapi' && st.account && typeof st.account === 'object') ? st.account.phone : null;",   # Telegram only: the account object's phone
    "return String(acct || st.phone || '').replace(/\\D/g, '');",                                                  # the account first, the top-level phone the fallback, digits only
    "const wapi = row.channel === 'telegram' ? 'tapi' : 'api';",                                                   # the bot's channel picks the rule
    "const phone = ownerPhone(wapi, body);",                                                                       # the number is taken nowhere else
)
# the wrong rules the tripwire must reject (label, the code it replaces, the wrong code); tests/test_handoff_notifier.py takes
# this list and requires the notifier driver to expose every one of them in behaviour (a pin the driver cannot see is unproven)
OWNER_PHONE_WRONG = (
    ("only the top-level phone (the pre-A5 line)", "const phone = ownerPhone(wapi, body);",
     "const phone = String(body.phone || '').replace(/\\D/g, '');"),
    ("the top-level phone first, the account second", "String(acct || st.phone || '')", "String(st.phone || acct || '')"),
    ("the account first on every channel (WhatsApp included)", "(wapi === 'tapi' && st.account && typeof st.account === 'object')",
     "(st.account && typeof st.account === 'object')"),
    ("the account only, no fallback to the top-level phone", "String(acct || st.phone || '')", "String(acct || '')"),
    ("Telegram never recognised (the channel is always the WhatsApp one)", "const wapi = row.channel === 'telegram' ? 'tapi' : 'api';",
     "const wapi = 'api';"),
    ("no digit-stripping", ".replace(/\\D/g, '');\n}", ";\n}"),
)


def check_owner_phone_rule(ntf):
    """A5: Compose Notice resolves the owner's number by OWNER_PHONE_JS — Telegram: status.account.phone first, then the
    top-level phone; WhatsApp: the top-level phone; digits only — and takes it nowhere else. Raises AssertionError
    (tests/test_handoff_notifier.py holds the behaviour: the real graph, the wrong rules, parity with Claim Decide)."""
    code = next((n["parameters"].get("jsCode", "") for n in ntf["nodes"] if n["name"] == "Compose Notice"), "")
    assert code, "notifier: no Compose Notice code (A5)"
    for frag in OWNER_PHONE_FRAGMENTS:
        assert frag in code, f"notifier: Compose Notice lacks {frag!r} (A5: the owner's number by the V2 rule — account.phone first on Telegram)"
    assert "body.phone" not in code, "notifier: Compose Notice reads the top-level phone outside ownerPhone (A5)"
    assert code.count("function ownerPhone(") == 1, "notifier: Compose Notice must define ownerPhone exactly once (A5)"
    assert hw().OWNER_PHONE_JS in code, "notifier: Compose Notice does not embed build-handoff-workflows.OWNER_PHONE_JS verbatim (A5: one rule for the notifier and the channel watch)"


NO_DOUBLE = "x.message->'additional_kwargs'->>'message_id' = $3"

_BS = None


def bs():
    """bot_schedule.py — the ONE home of the schedule's window predicate (B1): the mode read composes its OFF_HOURS_SQL."""
    global _BS
    if _BS is None:
        _BS = _tool("bot_schedule.py", "bot_schedule")
    return _BS


# B1 «расписание бота» (2026-10-14, spec §4 B1): outside an ENABLED schedule window the bot behaves exactly as in «Вместе».
# The decision is ONE more OR term of Read Reply Mode (+ the same term as `off_hours`), so it outranks a chat's explicit
# «Авто» and costs nothing (no claim, no quota); no row / a disabled schedule is `false` (the LOCKED «absence ⇒ reply»).
# Read Reply Mode stays fail-closed (no onError — a failed read stops the run, it never reads as «reply»); none of the
# gate's nodes may be disabled (n8n hands a disabled node's input on: a disabled Read Reply Mode would answer every
# message, a disabled Record Suppressed would lose the client's message); Acquire Send Lease never reads the schedule (a
# window that closes during the generation never cancels an answer already paid for).
SCHEDULE_GATE_NODES = ("Read Reply Mode", "Suppressed?", "Record Suppressed", "Outcome")
OFF_HOURS_PASSTHROUGH = "$('Read Reply Mode').first().json.off_hours === true"
OFF_HOURS_STAMP = "const outcome = resp.off_hours === true ? 'off_hours'"


def check_schedule_gate(wf, f):
    """B1: see above. Raises AssertionError naming the file and the rule (tests/test_reply_schedule.py holds the scratch
    negatives and the behaviour on real SQL)."""
    names = {n["name"]: n for n in wf["nodes"]}
    for name in SCHEDULE_GATE_NODES:
        assert name in names, f"{f}: missing node {name!r} (the mode gate)"
        assert not names[name].get("disabled"), f"{f}: {name} is disabled — n8n hands its input on, the schedule gate stops gating (B1)"
    off = bs().OFF_HOURS_SQL()
    rm = names["Read Reply Mode"]
    q = rm["parameters"].get("query", "")
    assert "  or " + off + "\n) as suppressed," in q and q.rstrip().endswith("\n" + off + " as off_hours;") and q.count(off) == 2, \
        f"{f}: Read Reply Mode lacks the schedule term (bot_schedule.OFF_HOURS_SQL, an enabled row only, absence → false) in its suppressed OR and as off_hours (B1)"
    assert "coalesce(\n    (select suppressed from reply_mode_flags" in q and "or coalesce((select handoff_paused from chat_reply_state" in q, \
        f"{f}: Read Reply Mode lost the «Вместе» / handoff terms next to the schedule (B1)"
    assert rm.get("onError") is None and not rm.get("alwaysOutputData"), \
        f"{f}: Read Reply Mode must stay fail-closed — no onError / alwaysOutputData (got onError={rm.get('onError')!r}): a failed read must stop the run, never read as «reply» (B1)"
    edges = lambda src, i=0: [e["node"] for e in ((wf["connections"].get(src, {}).get("main") or [[]])[i] or [])]
    assert edges("Read Reply Mode") == ["Suppressed?"] and edges("Suppressed?") == ["Record Suppressed"] and edges("Record Suppressed") == ["Outcome"], \
        f"{f}: the mode gate must be Read Reply Mode → Suppressed?[true] → Record Suppressed → Outcome (B1)"
    rs = names["Record Suppressed"]["parameters"]
    assert rs.get("query", "").rstrip().endswith("$8::boolean as off_hours;") \
        and (rs.get("options") or {}).get("queryReplacement", "").endswith(", " + OFF_HOURS_PASSTHROUGH + " ])($('Webhook').first().json.body.messages[0]) }}"), \
        f"{f}: Record Suppressed must hand the mode read's off_hours on (its last value, answered back) for the stamp (B1)"
    assert OFF_HOURS_STAMP in names["Outcome"]["parameters"].get("jsCode", ""), \
        f"{f}: Outcome must stamp off_hours when Record Suppressed hands it on (B1: «вне графика»)"
    lease = names.get("Acquire Send Lease")
    assert lease is not None and "bot_schedule" not in lease["parameters"].get("query", ""), \
        f"{f}: Acquire Send Lease reads bot_schedule — the lease never reads the schedule (B1: decided at message entry only)"


def _gate_tripwires(wf, f):
    """The schedule-gate checks must reject what they exist to catch — on copies, on every run."""
    off = bs().OFF_HOURS_SQL()

    def q(name, old, new):
        def m(w):
            p = next(n for n in w["nodes"] if n["name"] == name)["parameters"]
            assert old in p["query"], f"tripwire anchor not found in {name}"
            p["query"] = p["query"].replace(old, new)
        return m

    def key(name, k, v):
        def m(w):
            next(n for n in w["nodes"] if n["name"] == name)[k] = v
        return m

    wrong = (("the term removed", q("Read Reply Mode", off, "false")),
             ("absence read as closed", q("Read Reply Mode", " and s.enabled), false)", " and s.enabled), true)")),
             ("fail-open read", key("Read Reply Mode", "onError", "continueRegularOutput")),
             ("a disabled read", key("Read Reply Mode", "disabled", True)),
             ("the lease reads the schedule", q("Acquire Send Lease", "as suppressed\n), cur as (", "or " + off + " as suppressed\n), cur as (")))
    for label, mutate in wrong:
        bad = copy.deepcopy(wf)
        mutate(bad)
        try:
            check_schedule_gate(bad, f)
        except AssertionError:
            continue
        fail(f"B1 tripwire accepted a broken schedule gate ({label}) in {f}")

# R9 + T7: the notifier's one memory/ledger sweep (Settle Lost Sends). R9 settles a lost send_pending as send_unknown and
# never makes an event re-claimable; T7 (follow-ups 2026-10-11) settles the dead run's memory DRAFT in the same statement
# — only drafts (delivery 'draft', no writer's source, an ai row), only through the draft's OWN event row, marked unknown
# only when that event is send_unknown under the SAME execution, dropped only on a dead claim of the SAME execution older
# than 10 minutes or under a re-claimed event once the draft itself is older than 10 minutes.
LOST_SWEEP_FRAGMENTS = (
    "update reply_events\n     set state = 'send_unknown'",
    "where state = 'send_pending' and updated_at < now() - interval '10 minutes'",
    "run_lost",
    "\n     and e.chat_id = substr(h.session_id, position(':' in h.session_id) + 1)\n     and e.message_id = h.message->'additional_kwargs'->>'event_id'",
    "\n   where h.message->>'type' = 'ai'\n     and (h.message->'additional_kwargs'->>'source') is null\n"
    "     and h.message->'additional_kwargs'->>'delivery' = 'draft'\n     and h.message->'additional_kwargs'->>'execution_id' is not null",
    "where h.id = d.id and d.event_state = 'send_unknown' and d.event_run_id = d.run_id",
    "     and ((d.event_run_id = d.run_id and d.event_state in ('claimed', 'generated') and d.event_at < now() - interval '10 minutes')\n"
    "       or (d.event_run_id <> d.run_id and d.created_at < now() - interval '10 minutes'))",
)


def check_lost_send_sweep(q):
    """R9 + T7 rules of Settle Lost Sends (see LOST_SWEEP_FRAGMENTS). Raises AssertionError; tests/test_reply_lost_send.py
    holds the loosened variants and cross-checks them with the scenarios."""
    code = "\n".join(re.sub(r"--.*$", "", line) for line in q.split("\n"))
    for frag in LOST_SWEEP_FRAGMENTS:
        assert frag in code, f"Settle Lost Sends lacks {frag!r}"
    assert re.findall(r"\bset\s+state\s*=\s*'(\w+)'", code) == ["send_unknown"], \
        "Settle Lost Sends may only set state = 'send_unknown' (never makes an event re-claimable)"
    assert code.count("update reply_events") == 1 and "insert into reply_events" not in code and "delete from reply_events" not in code, \
        "Settle Lost Sends must write the ledger only through its one send_unknown update"
    assert "message/send" not in code and "lock_token" not in code, "Settle Lost Sends must not send or touch the lease"
    mark = "|| jsonb_build_object('content', case when left(coalesce(h.message->>'content', ''), " + str(len(hh().UNDELIVERED_MEMORY_PREFIX)) \
        + ") = " + hh().sql_text(hh().UNDELIVERED_MEMORY_PREFIX)
    assert mark in code and "jsonb_build_object('delivery', 'unknown')" in code, "T7: a lost draft must become delivery unknown + the undelivered mark (once)"
    assert code.count("delete from n8n_chat_histories") == 1 and "delete from n8n_chat_histories h\n   using drafts d\n   where h.id = d.id\n" in code, \
        "T7: the only memory delete is of the drafts the drafts CTE selected"
    assert code.count("update n8n_chat_histories") == 1 and "\n    from drafts d\n   where h.id = d.id and" in code, \
        "T7: the only memory update is of the drafts the drafts CTE selected"


def verify_bot(f):
    wf = load(f)
    q = node(wf, "Claim Event")["parameters"]["query"]
    check("insert into chat_history_events" in q and "'client'" in q and "do nothing" in q, f"{f}: Claim Event must record the client row (idempotent)")
    check("n8n_chat_histories (session_id, message)" not in q, f"{f}: Claim Event must NOT write memory (the memory node does on a normal run)")
    q = node(wf, "Record Suppressed")["parameters"]["query"]
    for frag in ("'client'", MEM_MARK, "'human'", NO_DOUBLE, "where exists (select 1 from ev)"):
        check(frag in q, f"{f}: Record Suppressed lacks {frag!r}")
    q = node(wf, "Record Outgoing")["parameters"]["query"]
    for frag in ("'owner_phone'", "'outgoing_api'", "do nothing", "where exists (select 1 from ev where source = 'owner_phone')", NO_DOUBLE, "$2 not like '%@g.us'"):
        check(frag in q, f"{f}: Record Outgoing lacks {frag!r}")
    # T6 (Q1 B): the owner's phone reply enters memory as «Владелец: <text>» — the model reads role + content only
    check(hh().MEMORY_ROW % ("'ai'", hh().OWNER_CONTENT_SQL % "$4", "'owner_phone'", "$3") in q,
          f"{f}: Record Outgoing must write the owner_phone memory row with the owner mark (T6 / Q1 B)")
    check("'content', $4, 'additional_kwargs', jsonb_build_object('source', 'owner_phone'" not in q,
          f"{f}: Record Outgoing writes the owner's text WITHOUT the mark (the model reads it as its own earlier answer, Q1)")
    q = node(wf, "Record Send")["parameters"]["query"]
    check("'ai'" in q and "do update set source = 'ai'" in q and "$4 = 'sent'" in q, f"{f}: Record Send must record the sent reply as an ai row (upgrading a webhook-first row)")
    # T6 (V6-unknown): the row it marks unknown carries the undelivered mark (once); a late sent takes it off
    check("      || jsonb_build_object('content', " + hh().RECORD_SEND_CONTENT_SQL + ")\n  where (message->'additional_kwargs'->>'execution_id') = $7" in q,
          f"{f}: Record Send must put the undelivered mark on an unknown reply's memory row (once) and take it off on sent (T6)")
    sd = node(wf, "Stamp Draft")["parameters"]["query"]
    check("'{content}', to_jsonb(regexp_replace(message->>'content', " + hh().MARKS_LEAD_SQL + ", '')))" in sd,
          f"{f}: Stamp Draft must cut a leading run of memory marks from the row it stamps (T6: the bot never stores a mark of its own)")
    q = node(wf, "Record Cancel")["parameters"]["query"]
    check("(message->'additional_kwargs'->>'source') is null" in q, f"{f}: Record Cancel must never drop owner rows")
    check("(message->'additional_kwargs'->>'source') is null" in node(wf, "Stamp Draft")["parameters"]["query"],
          f"{f}: Stamp Draft must never mark owner/client rows (V6)")
    check("$8::boolean and nullif($9, '') is not null" in q and NO_DOUBLE in q, f"{f}: Record Cancel must record the human row when the agent never ran, without doubles")
    js = node(wf, "Cancel Label")["parameters"]["jsCode"]
    check("recordHuman = prev === 'If Quota Allows' || prev === 'Generated?'" in js, f"{f}: Cancel Label must flag the human row only for no-agent paths")
    q = node(wf, "Read Reply Mode")["parameters"]["query"]
    check("or coalesce((select handoff_paused from chat_reply_state" in q and "coalesce(\n    (select suppressed from reply_mode_flags" in q,
          f"{f}: Read Reply Mode must OR the handoff pause while keeping absence → reply")
    # B1: the schedule is one more term of the same gate (and the tripwire proves the checks still bite)
    try:
        check_schedule_gate(wf, f)
    except AssertionError as e:
        fail(str(e))
    _gate_tripwires(wf, f)
    # routing + binding
    for name, frag in (("Is Incoming?", "wh_type === 'incoming_message'"), ("Is Outgoing?", "'outgoing_message_phone', 'outgoing_message_api'"),
                       ("Outgoing Bound?", "split('/').pop() === String($json.body.messages[0].profile_id)")):
        check(frag in node(wf, name)["parameters"]["conditions"]["conditions"][0]["leftValue"], f"{f}: {name} condition lacks {frag!r}")
    # the tool: context from the run, content from the model, target = the canonical sub-workflow
    t = node(wf, "Create Handoff")
    check(t["type"] == "@n8n/n8n-nodes-langchain.toolWorkflow" and t["parameters"]["workflowId"]["value"] == "crHandoffReq0001", f"{f}: Create Handoff target")
    v = t["parameters"]["workflowInputs"]["value"]
    for k in ("profile_id", "chat_id", "source_message_id"):
        check("$('Webhook').first().json.body.messages[0]" in v[k] and "$fromAI" not in v[k], f"{f}: tool input {k} must come from the run, never the model")
    for k in ("reason", "name", "phone", "summary"):
        check("$fromAI(" in v[k], f"{f}: tool input {k} must be filled by the model")
    check(wf["connections"]["Create Handoff"]["ai_tool"] == [[{"node": "AI Agent", "type": "ai_tool", "index": 0}]], f"{f}: tool not wired")
    sm = node(wf, "AI Agent")["parameters"]["options"]["systemMessage"]
    check("##create_handoff##" in sm and "ok:true" in sm, f"{f}: system prompt lacks the tool rule")
    # T6: a bot with an empty / legacy business type carries THIS prompt (no _core.md) — the marks rule is its last block
    check(sm.endswith(hh().MEMORY_MARKS_RULE) and sm.count("##memory_marks##") == 1,
          f"{f}: the template prompt must end with exactly one ##memory_marks## block of the current text (T6)")
    print("OK ", f)


def verify_handoff_workflows():
    req = load("crHandoffReq0001-Handoff_Request.json")
    q = node(req, "Upsert Handoff")["parameters"]["query"]
    for frag in ("on conflict (profile_id, chat_id, source_message_id) do update", "(xmax = 0) as created", "handoff_paused = true"):
        check(frag in q, f"Handoff Request lacks {frag!r}")
    js = node(req, "Tool Result")["parameters"]["jsCode"]
    check("ok: false, error: 'db_error'" in js and "ok: true" in js, "Tool Result must distinguish a written row from a failure")
    check(node(req, "When Executed by Another Workflow")["parameters"]["inputSource"] == "workflowInputs", "Handoff Request trigger inputs")
    ntf = load("crHandoffNtfy001-Handoff_Notifier.json")
    q = node(ntf, "Due Notifications")["parameters"]["query"]
    for frag in ("for update skip locked", "notify_attempts = h.notify_attempts + 1", "notify_attempts < 6", "notify_next_at = now() + interval '10 minutes'", "status <> 'done'"):
        check(frag in q, f"Handoff Notifier lacks {frag!r}")
    for name in ("Compose Notice", "Settle Notice"):
        check(node(ntf, name)["parameters"].get("mode") == "runOnceForEachItem", f"{name} must run per item (one notice per due request)")
    js = node(ntf, "Compose Notice")["parameters"]["jsCode"]
    check("row.summary" not in js and "row.data" not in js and "row.body" not in js, "the owner notice must not carry client text")
    check("const text = 'Choose Reply: ' + what + ' (чат …' + tail + ')." in js and "slice(-4)" in js,
          "the owner notice is built ONLY from the reason word and a masked chat tail")
    q = node(ntf, "Record Notify")["parameters"]["query"]
    check("notify_state = $2" in q and "handoff_requests" in q and "delete" not in q.lower(), "Record Notify must only update the notify state")
    # F3: nothing due (or a DB fault) never reaches Wappi or Record Notify; the tripwire itself must reject the pre-fix shape
    try:
        check_notifier_gate(ntf)
    except AssertionError as e:
        fail(str(e))
    pre_fix = copy.deepcopy(ntf)
    pre_fix["connections"][NOTIFIER_DUE] = {"main": [[{"node": "Owner Phone", "type": "main", "index": 0}]]}
    try:
        check_notifier_gate(pre_fix)
        fail("F3 tripwire accepted the pre-fix notifier (Due Notifications → Owner Phone)")
    except AssertionError:
        pass
    # A5: the owner's number by the V2 rule; the tripwire itself must reject every wrong rule (first of all: only the top-level phone)
    try:
        check_owner_phone_rule(ntf)
    except AssertionError as e:
        fail(str(e))
    for label, old, new in OWNER_PHONE_WRONG:
        bad = copy.deepcopy(ntf)
        n = node(bad, "Compose Notice")
        check(old in n["parameters"]["jsCode"], f"A5 self-check: the anchor of {label!r} is not in Compose Notice (update OWNER_PHONE_WRONG)")
        n["parameters"]["jsCode"] = n["parameters"]["jsCode"].replace(old, new, 1)
        try:
            check_owner_phone_rule(bad)
            fail(f"A5 tripwire accepted a wrong owner-number rule: {label}")
        except AssertionError:
            pass
    # R9: the same schedule settles lost sends (send_pending > 10 min → send_unknown), in a branch with no transport node;
    # T7: and the dead run's memory draft (unknown + mark after the lease, removed before it) — check_lost_send_sweep
    q = node(ntf, "Settle Lost Sends")["parameters"]["query"]
    try:
        check_lost_send_sweep(q)
    except AssertionError as e:
        fail(str(e))
    try:
        check_lost_send_sweep(q.replace("set state = 'send_unknown'", "set state = 'claimed'"))
        fail("R9 tripwire accepted a sweep that makes an event re-claimable")
    except AssertionError:
        pass
    trig = ntf["connections"]["Every 2 Minutes"]["main"][0]
    check({"node": "Settle Lost Sends", "type": "main", "index": 0} in trig, "Settle Lost Sends must run off the notifier's schedule")
    check(not ntf["connections"].get("Settle Lost Sends"), "Settle Lost Sends must lead nowhere (never to a transport node)")
    check(node(ntf, "Settle Lost Sends")["position"][1] < node(ntf, "Due Notifications")["position"][1],
          "Settle Lost Sends must sit above the notice branch (executionOrder v1 order)")
    api = load("crHandoffsApi001-Handoffs.json")
    check("crAuth" in node(api, "Auth Gate")["parameters"]["jsCode"] and '"profiles": ["profileIds"]' in node(api, "Auth Gate")["parameters"]["jsCode"] and '"legacy": false' in node(api, "Auth Gate")["parameters"]["jsCode"],
          "Handoffs webhook must be token-only and profile-scoped")
    js = node(api, "Validate")["parameters"]["jsCode"]
    check("owned.has(x)" in js, "Handoffs Validate must re-intersect with the token's profiles")
    q = node(api, "Handoffs Query")["parameters"]["query"]
    for frag in ("h.profile_id in (select profile_id from p)", "handoff_paused = false", "o.status <> 'done' and o.id <> d.id"):
        check(frag in q, f"Handoffs Query lacks {frag!r}")
    print("OK  handoff workflows (request / notifier / app webhook)")


def verify_test_dialog():
    td = load("crTestDialog0001-Test_Dialog.json")
    names = {n["name"] for n in td["nodes"]}
    types = {n["type"] for n in td["nodes"]}
    check("Chat Memory" not in names and not any("memory" in t.lower() for t in types), "test dialog must not touch the bot's memory")
    check("Count Dialog" not in names and "HTTP Request" not in names and not any("message/send" in json.dumps(n["parameters"]) for n in td["nodes"]), "test dialog must never send or count a dialog")
    tool = node(td, "Create Handoff")
    check(tool["parameters"]["workflowId"]["value"] == "crHandoffDry0001", "test dialog's handoff tool must target the DRY sub-workflow")
    check("$('Validate').first().json.runId" in tool["parameters"]["workflowInputs"]["value"]["run_id"], "dry tool must carry the run id from the run, not the model")
    g = node(td, "Auth Gate")["parameters"]["jsCode"]
    check('"profiles": ["profileId"]' in g and '"workflows": ["workflowId"]' in g and '"legacy": false' in g, "test dialog keeps the owner check (profile + workflow, token-only)")
    check("api/v1/workflows/{{ $json.workflowId }}" in node(td, "Read Bot Workflow")["parameters"]["url"], "the prompt is read from the bot's own workflow")
    cj = node(td, "Compose")["parameters"]["jsCode"]
    check("wf.activeVersion" in cj and "activeVersionId" in cj and "nodes[5]" not in cj and "published_version_unreadable" in cj,
          "R15: an active bot is tested by its PUBLISHED version (activeVersion), fail-closed, no nodes[5] fallback — behaviour in tests/test_test_dialog.py")
    check("promptVersion" in node(td, "Build Response")["parameters"]["jsCode"], "R15: the response names the tested version (promptVersion)")
    dry = load("crHandoffDry0001-Handoff_Request_Dry.json")
    check("test_dialog_actions" in node(dry, "Record Intent")["parameters"]["query"] and not any("handoff_requests" in json.dumps(n["parameters"]) for n in dry["nodes"]),
          "the dry tool records intent only — never a real request")
    print("OK  test dialog (no memory, no send, no quota, dry handoff, owner check)")


def verify_integrations():
    for f in ("XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json"):
        jb = node(load(f), "Set Wappi Webhook Types")["parameters"]["jsonBody"]
        check(all(t in jb for t in ("incoming_message", "outgoing_message_phone", "outgoing_message_api")) and "delivery_status" not in jb, f"{f}: webhook types")
    d = load("2htWSV5IHO8E2CgB-Dashboard_Outcomes.json")
    check("'Владелец: '" in node(d, "Find Changed Sessions")["parameters"]["query"], "classifier transcript must label owner rows")
    # T6: it labels by source, so it reads the content WITHOUT the memory marks (no «Владелец: Владелец:»)
    fq = node(d, "Find Changed Sessions")["parameters"]["query"]
    check(hh().CONTENT_NEW in fq and fq.count(hh().CONTENT_OLD) == 1, "classifier transcript must read the content without the memory marks (T6)")
    q = node(d, "Fetch Outcomes")["parameters"]["query"]
    for frag in ("FULL OUTER JOIN h", "'event' ELSE 'classifier' END AS source", "h.reason = 'order' THEN 'order_collected' ELSE 'owner_needed'", "r.status <> 'done'"):
        check(frag in q, f"Fetch Outcomes lacks {frag!r}")
    dcm = node(load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json"), "Delete Chat Memory")["parameters"]
    q = dcm["query"]
    for t in ("n8n_chat_histories", "conversation_outcomes", "reply_mode_flags", "chat_history_events", "handoff_requests",
              "reply_events", "chat_reply_state"):
        check(f"DELETE FROM public.{t}" in q, f"Delete Bot Files must wipe {t}")
    # T5 (F1 + F2): the memory of the bot's WHOLE line, by the same fragment and parameters as the other deleting nodes
    cl = _clone_lineage()
    check(q.startswith("--") and cl.DBF_LINE_HEAD in q and "SELECT profile_id FROM lineage_profiles" in q
          and (dcm.get("options") or {}).get("queryReplacement") == cl.DBF_LINE_PARAMS,
          "T5: Delete Chat Memory must delete by the bot's line (clone_lineage.DBF_LINE_HEAD + DBF_LINE_PARAMS), not by the named ids")
    # B1 «расписание бота» (2026-10-14): the schedule goes with the bot — every profile of its line, the same statement,
    # counted as deletedSchedules (Deletion Settled? reads it). Pinned AFTER the T5 line check on purpose: a pre-T5 copy
    # (named ids, no schedule) must still be reported as the T5 failure it is (tests/test_clone_retirements.py T5 N1).
    check("DELETE FROM public.bot_schedule b\n  WHERE b.profile_id IN (SELECT profile_id FROM valid)" in q
          and 'AS "deletedSchedules"' in q,
          "B1: Delete Bot Files must wipe bot_schedule for every profile of the line (Delete Chat Memory) and count it as deletedSchedules")
    rb = node(load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json"), "Respond")["parameters"]["responseBody"]
    for k in ("deletedHistoryEvents", "deletedHandoffs", "deletedReplyEvents", "deletedChatStates", "deletedSchedules"):
        check(k in rb, f"Delete Bot Files response must report {k}")
    st = load("0DP79STI3pafXO9B-Set_Workflow_State.json")
    check(any(all(i in n["parameters"].get("jsCode", "") for i in ("'crHandoffReq0001'", "'crHandoffNtfy001'", "'crHandoffsApi001'", "'crTestDialog0001'", "'crHandoffDry0001'")) for n in st["nodes"]),
          "Set Workflow State must protect the handoff + test-dialog workflow ids")
    px = load("crWappiProxy0001-Wappi_Proxy.json")
    check(px["connections"]["Respond"]["main"][0] == [{"node": "Owner Send?", "type": "main", "index": 0}], "proxy: the recorder must run AFTER the response")
    q = node(px, "Record Owner Send")["parameters"]["query"]
    check("'owner_app'" in q and MEM_MARK in q and NO_DOUBLE in q, "proxy: Record Owner Send must write an owner_app row + a marked memory row, no doubles")
    # T6 (Q1 B): the SAME owner mark as the templates' Record Outgoing (parity: one constant in apply-history-handoff.py)
    check("'content', " + hh().OWNER_CONTENT_SQL % "$4" + ", 'additional_kwargs', jsonb_build_object('source', 'owner_app'" in q,
          "proxy: Record Owner Send must write the owner_app memory row with the owner mark of apply-history-handoff.py (T6)")
    # T6: the one rule for both marks — the «Память» paragraph of _core.md (and so every vertical of the Create/Edit maps)
    core = open(os.path.join(HERE, "prompts", "_core.md"), encoding="utf-8").read()
    para = [l for l in core.splitlines() if l.startswith("Память.")]
    check(len(para) == 1 and para[0].endswith(" " + hh().MEMORY_MARKS_TEXT), "prompts/_core.md: the «Память» paragraph must end with the memory-marks rule (T6)")
    ip = _tool("inject-prompts.py", "inject_prompts")
    for fname in ("XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json",
                  "3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json", "TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json"):
        prompts = ip.prompts_of_workflow(load(fname)) or {}
        check(set(prompts) == set(ip.VERTICALS) and all(p.count(hh().MEMORY_MARKS_TEXT) == 1 for p in prompts.values()),
              f"{fname}: every vertical of the PROMPTS map must carry the memory-marks rule once (inject-prompts.py, T6)")
    print("OK  integrations (Create types / Dashboard / Delete / Set State / proxy)")


def main():
    global WF
    if "--dir" in sys.argv:
        WF = sys.argv[sys.argv.index("--dir") + 1]
    for f in BOTS:
        verify_bot(f)
    verify_handoff_workflows()
    verify_test_dialog()
    verify_integrations()
    print("ALL HISTORY/HANDOFF ASSERTS PASSED")


if __name__ == "__main__":
    main()
