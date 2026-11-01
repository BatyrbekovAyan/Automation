#!/usr/bin/env python3
"""Stage-4 (A5/A6) semantic verifier: the ONE history's writers and their sources, the memory rows'
LangChain shape + provenance marks, no double write, the handoff pause in the gate, the tool wiring
and its context fields, the notifier's privacy and its owner-number rule (2026-10-15: Telegram
account.phone first), the app webhook's owner scoping, and the integrations (Create webhook types,
Dashboard event override, Delete Bot Files wipe, Set Workflow State protection, proxy recorder after
the response). B1 «расписание бота» (2026-10-14): the schedule is one more term of the mode gate —
check_schedule_gate (the term and its absence rule, Read Reply Mode fail-closed and enabled, off_hours
handed on and stamped, the lease never reading it), self-checked on every run. «Личные и рабочие чаты»
(2026-10-25): check_audience_gate — Read Reply Mode reads the chat's decision and the connection in the same
statement and stands ALONE between If and Audience Known?; the fork's conditions read positive fields (Decided
Business?: the sub-workflow's own answer); Decide Audience maps exactly the inputs its sub-workflow declares; from the
Webhook, with the two business edges cut, nothing but the routing, the gate, Record Outgoing (business-only in its own
statement) and the Outcome stamp is reachable — no send, no Wappi call, no write; self-checked on every run.
verify_journal (live check D5): both templates and Suggest Replies keep no successful execution on prod, their staging
copies keep all, a new clone inherits the setting (Create's Set Fields) and update-clones.py brings an old one to it;
self-checked on every run.

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

_AU = None


def au():
    """audience.py — the ONE home of «is this a business chat» (the decision that holds) and the connection columns."""
    global _AU
    if _AU is None:
        _AU = _tool("audience.py", "audience")
    return _AU


# «Личные и рабочие чаты» (2026-10-25, spec §4.2 / §4.3, plan Task 4): the templates decide at message entry whether the chat
# is a client's. Read Reply Mode — still ONE Postgres node, still fail-closed — reads the decision that holds and the
# connection; it alone stands between the group filter If and Audience Known?. Audience Known? / Business? / Decided
# Business? are single boolean conditions on positive fields; Decided Business? needs the sub-workflow's OWN answer (its
# outcome too: Decide Audience continues on error, and n8n then hands Read Reply Mode's row — with an audience column — on).
# Decide Audience calls crAudienceDec001 with exactly the inputs that sub-workflow declares (types checked, nothing
# converted), and Suppressed? reads Read Reply Mode's row. Reachability, the property itself: from the Webhook, with
# Business?[true] and Decided Business?[true] cut, only the routing Ifs, the gate, Record Outgoing (business-only in its own
# statement) and the Outcome stamp are reachable — no send, typing, Mark Read or other Wappi call, no Postgres write, no
# memory write, no agent.
AUDIENCE_GATE = ("Read Reply Mode", "Audience Known?", "Business?", "Decide Audience", "Decided Business?")
DECIDE_FILE = "crAudienceDec001-Decide_Audience.json"
NOT_BUSINESS_REACH = {"Webhook", "Is Incoming?", "If", "Is Outgoing?", "Outgoing Bound?", "Record Outgoing", "Outcome"} | set(AUDIENCE_GATE)
BOOLEAN_TRUE = {"type": "boolean", "operation": "true", "singleValue": True}


def callee_inputs(directory):
    """[(name, type)] the Decide Audience sub-workflow's trigger declares — read off its canonical JSON in `directory`."""
    with open(os.path.join(directory, DECIDE_FILE), encoding="utf-8") as fh:
        sub = json.load(fh)
    trig = next(n for n in sub["nodes"] if n["type"] == "n8n-nodes-base.executeWorkflowTrigger")
    return [(v["name"], v["type"]) for v in trig["parameters"]["workflowInputs"]["values"]]


def check_audience_gate(wf, f, declared):
    """See above. `declared` = the sub-workflow's [(input, type)]. Raises AssertionError «<file>: audience gate: …»
    (tests/test_reply_audience.py holds the scratch negatives and the behaviour on real SQL)."""
    names = {n["name"]: n for n in wf["nodes"]}
    tag = f"{f}: audience gate:"
    for name in AUDIENCE_GATE + ("Suppressed?", "Outcome", "If", "Record Outgoing"):
        assert name in names, f"{tag} missing node {name!r}"
        assert not names[name].get("disabled"), f"{tag} {name} is disabled — n8n hands a disabled node's input on, the gate stops gating"
    rm = names["Read Reply Mode"]
    q = rm["parameters"].get("query", "")
    assert rm["type"] == "n8n-nodes-base.postgres" and "\n" + au().AUDIENCE_SQL("$1", "$2") + " as audience,\n" in q, \
        f"{tag} Read Reply Mode lacks the audience column (audience.AUDIENCE_SQL of the profile and the chat) — the decision is read in the mode read itself"
    assert "\n" + au().CONNECTION_SQL("$1") + ",\n" in q, f"{tag} Read Reply Mode lacks the connection columns (audience.CONNECTION_SQL)"
    assert rm.get("onError") is None and not rm.get("alwaysOutputData"), f"{tag} Read Reply Mode must stay fail-closed (no onError / alwaysOutputData)"
    feeders = lambda dst: sorted((s, i) for s, c in wf["connections"].items() for i, out in enumerate((c or {}).get("main") or [])
                                 for e in (out or []) if e["node"] == dst)
    edges = lambda src: [[e["node"] for e in (out or [])] for out in (wf["connections"].get(src) or {}).get("main") or []]
    assert edges("If")[:1] == [["Read Reply Mode"]] and feeders("Read Reply Mode") == [("If", 0)] \
        and edges("Read Reply Mode") == [["Audience Known?"]] and feeders("Audience Known?") == [("Read Reply Mode", 0)], \
        f"{tag} between If and Audience Known? stands Read Reply Mode alone — ONE Postgres node (If → {edges('If')[:1]}, " \
        f"Read Reply Mode → {edges('Read Reply Mode')}, Audience Known? fed by {feeders('Audience Known?')})"
    reach = _reachable(wf, "Webhook", cut={("Business?", 0), ("Decided Business?", 0)})
    assert reach <= NOT_BUSINESS_REACH, \
        f"{tag} {sorted(reach - NOT_BUSINESS_REACH)} reachable from the Webhook without passing Business?[true] or Decided Business?[true] — " \
        "a personal or undecided chat would get a send, a Wappi call or a write"
    assert set(AUDIENCE_GATE) | {"Outcome"} <= reach, f"{tag} the gate is not on the path from the Webhook: {sorted(reach)}"
    hh_ = hh()
    channel = hh_.CHANNEL.get(os.path.basename(f), "")
    for name, want in (("Audience Known?", hh_.AUDIENCE_KNOWN_EXPR), ("Business?", hh_.business_expr(channel)),
                       ("Decided Business?", hh_.decided_business_expr(channel)), ("Suppressed?", hh_.SUPPRESSED_EXPR)):
        n = names[name]
        conds = n["parameters"].get("conditions") or {}
        cl = conds.get("conditions") or []
        assert n["type"] == "n8n-nodes-base.if" and len(cl) == 1 and cl[0].get("leftValue") == want \
            and cl[0].get("operator") == BOOLEAN_TRUE and conds.get("combinator") == "and", \
            f"{tag} {name} must be the one boolean condition {want!r}: {cl}"
    # Rule step 1 at the gate, read independently of the generator's text: on Telegram Business? and Decided Business? refuse
    # a webhook that is not a person — the webhook's is_bot, and EVERY chat type audience.TG_NONHUMAN_TYPES names (taken from
    # audience.py, never a copy) — whatever decision is stored; on WhatsApp, whose rule reads no chat type, neither is read.
    m_ = "$('Webhook').first().json.body.messages[0]"
    for name in ("Business?", "Decided Business?"):
        lv = (((names[name]["parameters"].get("conditions") or {}).get("conditions") or [{}])[0]).get("leftValue") or ""
        if channel == "telegram":
            assert m_ + ".is_bot !== true" in lv and json.dumps(list(au().TG_NONHUMAN_TYPES)) + ".includes(String(" + m_ + ".chat_type" in lv, \
                f"{tag} {name} must refuse a Telegram webhook that is not a person (is_bot, a chat type of audience.TG_NONHUMAN_TYPES " \
                f"{list(au().TG_NONHUMAN_TYPES)}) whatever decision is stored: {lv}"
        else:
            assert "chat_type" not in lv and "is_bot" not in lv, \
                f"{tag} {name} must not read the webhook's chat type or is_bot on {channel or 'this channel'} (its rule reads no chat type): {lv}"
    for src, want in (("Audience Known?", [["Business?"], ["Decide Audience"]]), ("Business?", [["Suppressed?"], ["Outcome"]]),
                      ("Decide Audience", [["Decided Business?"]]), ("Decided Business?", [["Suppressed?"], ["Outcome"]])):
        assert edges(src) == want, f"{tag} {src} must lead to {want}: {edges(src)}"
    assert feeders("Suppressed?") == [("Business?", 0), ("Decided Business?", 0)], \
        f"{tag} Suppressed? must be entered from Business?[true] and Decided Business?[true] alone: {feeders('Suppressed?')}"
    assert feeders("Decide Audience") == [("Audience Known?", 1)], f"{tag} Decide Audience must be entered from Audience Known?[false] alone"
    da = names["Decide Audience"]
    p = da["parameters"]
    mapping = p.get("workflowInputs") or {}
    assert da["type"] == "n8n-nodes-base.executeWorkflow" and (da.get("typeVersion") or 1) >= 1.2 \
        and (p.get("workflowId") or {}).get("value") == "crAudienceDec001" and (p.get("workflowId") or {}).get("mode") == "id" \
        and p.get("source", "database") == "database" and p.get("mode", "once") == "once" \
        and (p.get("options") or {}).get("waitForSubWorkflow", True) is True, \
        f"{tag} Decide Audience must call crAudienceDec001 from the database, once per run, waiting for its answer: {p.get('workflowId')}"
    assert da.get("onError") == "continueRegularOutput" and da.get("alwaysOutputData") is True, \
        f"{tag} Decide Audience must continue on error and always hand an item on (the run ends at the stamp, never at an error)"
    assert [(s.get("id"), s.get("type")) for s in mapping.get("schema") or []] == declared \
        and not any(s.get("removed") for s in mapping.get("schema") or []), \
        f"{tag} Decide Audience's schema must be exactly the inputs crAudienceDec001 declares {declared}: {mapping.get('schema')}"
    assert mapping.get("mappingMode") == "defineBelow" and mapping.get("attemptToConvertTypes") is False \
        and mapping.get("convertFieldsToString") is False, f"{tag} Decide Audience must check the declared types, converting nothing"
    want = hh_.decide_inputs(hh_.CHANNEL.get(os.path.basename(f), ""))
    assert mapping.get("value") == want, f"{tag} Decide Audience must map apply-history-handoff.decide_inputs: {mapping.get('value')}"
    js = names["Outcome"]["parameters"].get("jsCode", "")
    for frag in ("'Business?': 'personal'", "'Decided Business?': 'personal_unknown'",
                 "(prev === 'Decided Business?' ? (DECIDE_PERSONAL.includes(resp.outcome) ? resp.outcome : byPrev[prev]) : null)",
                 "const outcome = personal !== null ? personal\n"):
        assert frag in js, f"{tag} the Outcome stamp lacks {frag!r} (a personal chat is stamped by the branch that ended it)"


def _audience_tripwires(wf, f, declared):
    """The audience-gate checks must reject what they exist to catch — on copies, on every run."""
    def link(*targets):
        return [{"node": t, "type": "main", "index": 0} for t in targets]

    def rewire(src, out, *targets):
        def m(w):
            w["connections"][src]["main"][out] = link(*targets)
        return m

    def cond(name, expr):
        def m(w):
            next(n for n in w["nodes"] if n["name"] == name)["parameters"]["conditions"]["conditions"][0]["leftValue"] = expr
        return m

    def key(name, k, v):
        def m(w):
            next(n for n in w["nodes"] if n["name"] == name)[k] = v
        return m

    def no_audience(w):
        p = next(n for n in w["nodes"] if n["name"] == "Read Reply Mode")["parameters"]
        p["query"] = p["query"].replace(au().AUDIENCE_SQL("$1", "$2") + " as audience,", "null::text as audience,")

    def extra_postgres(w):
        extra = copy.deepcopy(next(n for n in w["nodes"] if n["name"] == "Read Reply Mode"))
        extra["name"], extra["id"] = "Extra Read", "tripwire-extra-read"
        w["nodes"].append(extra)
        w["connections"]["Read Reply Mode"]["main"][0] = link("Extra Read")
        w["connections"]["Extra Read"] = {"main": [link("Audience Known?")]}

    def raw_bigint(w):
        v = next(n for n in w["nodes"] if n["name"] == "Decide Audience")["parameters"]["workflowInputs"]["value"]
        v["connectedAtMs"] = "={{ $('Read Reply Mode').first().json.connected_at_ms }}"

    wrong = (("a write reachable from Audience Known?[false] around Decided Business?[true]",
              rewire("Decide Audience", 0, "Decided Business?", "Record Suppressed")),
             ("Mark Read on the personal branch", rewire("Business?", 1, "Outcome", "Mark Read")),
             ("a claim around the gate from the incoming branch", rewire("Is Incoming?", 0, "If", "Claim Event")),
             ("Read Reply Mode without the audience column", no_audience),
             ("an extra Postgres node between If and Audience Known?", extra_postgres),
             ("Decided Business? without the outcome guard", cond("Decided Business?", "={{ $json.audience === 'business' }}")),
             ("Suppressed? reading $json", cond("Suppressed?", "={{ $json.suppressed }}")),
             ("Decide Audience failing the run", key("Decide Audience", "onError", None)),
             ("the bigint handed on as text", raw_bigint),
             ("Read Reply Mode failing open", key("Read Reply Mode", "onError", "continueRegularOutput")))
    # rule step 1 at the gate: the Telegram term dropped or narrowed, or the WhatsApp gate reading a chat type
    term, types = hh().TG_PERSON_TERM, json.dumps(list(au().TG_NONHUMAN_TYPES))
    narrowed = term.replace(types, json.dumps([t for t in au().TG_NONHUMAN_TYPES if t != "bot"]))
    if hh().CHANNEL.get(os.path.basename(f)) == "telegram":
        wrong += (("the Telegram Business? without the not-a-person term", cond("Business?", "={{ $json.audience === 'business' }}")),
                  ("the Telegram Decided Business? without the not-a-person term",
                   cond("Decided Business?", "={{ $json.audience === 'business' && $json.outcome === 'business' }}")),
                  ("the Telegram Business? forgetting the bot type",
                   cond("Business?", "={{ $json.audience === 'business' && " + narrowed + " }}")),
                  ("the Telegram Decided Business? ignoring is_bot",
                   cond("Decided Business?", "={{ $json.audience === 'business' && $json.outcome === 'business' && "
                        + term.replace(".is_bot !== true", ".is_bot !== 'never'") + " }}")))
    else:
        wrong += (("the WhatsApp Business? reading the chat type",
                   cond("Business?", "={{ $json.audience === 'business' && " + term + " }}")),)
    for label, mutate in wrong:
        bad = copy.deepcopy(wf)
        mutate(bad)
        try:
            check_audience_gate(bad, f, declared)
        except AssertionError:
            continue
        fail(f"personal chats tripwire accepted a broken audience gate ({label}) in {f}")
    if hh().CHANNEL.get(os.path.basename(f)) == "telegram":
        # the generator narrowed too (its term without the bot type, the canon regenerated from it): the equality with the
        # generator's text passes, so only the check read off audience.py may catch it
        saved = hh().TG_PERSON_TERM
        bad = copy.deepcopy(wf)
        try:
            hh().TG_PERSON_TERM = narrowed
            cond("Business?", hh().business_expr("telegram"))(bad)
            cond("Decided Business?", hh().decided_business_expr("telegram"))(bad)
            try:
                check_audience_gate(bad, f, declared)
            except AssertionError:
                return
            fail(f"personal chats tripwire accepted a gate generated from a term without the bot type in {f}")
        finally:
            hh().TG_PERSON_TERM = saved

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
# the stamp names off_hours only on Record Suppressed's OWN row: a failure outside the query runner hands the mode read's
# row on (R8), off_hours true but never recorded — that keeps the old label (personal chats come first: their stamp is the
# branch that ended them)
OFF_HOURS_STAMP = "const outcome = personal !== null ? personal\n  : (resp.outcome === 'suppressed' && resp.off_hours === true) ? 'off_hours'"


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
    # what leads from Read Reply Mode to Suppressed? is the audience gate's (check_audience_gate)
    assert edges("Suppressed?") == ["Record Suppressed"] and edges("Record Suppressed") == ["Outcome"], \
        f"{f}: the mode gate must be Suppressed?[true] → Record Suppressed → Outcome (B1)"
    rs = names["Record Suppressed"]["parameters"]
    assert rs.get("query", "").rstrip().endswith("$8::boolean as off_hours;") \
        and (rs.get("options") or {}).get("queryReplacement", "").endswith(", " + OFF_HOURS_PASSTHROUGH + " ])($('Webhook').first().json.body.messages[0]) }}"), \
        f"{f}: Record Suppressed must hand the mode read's off_hours on (its last value, answered back) for the stamp (B1)"
    assert OFF_HOURS_STAMP in names["Outcome"]["parameters"].get("jsCode", ""), \
        f"{f}: Outcome must stamp off_hours only on a RECORDED suppression (Record Suppressed's own row hands it on — B1: «вне графика»)"
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

    def js(name, old, new):
        def m(w):
            p = next(n for n in w["nodes"] if n["name"] == name)["parameters"]
            assert old in p["jsCode"], f"tripwire anchor not found in {name}"
            p["jsCode"] = p["jsCode"].replace(old, new)
        return m

    wrong = (("the term removed", q("Read Reply Mode", off, "false")),
             ("absence read as closed", q("Read Reply Mode", " and s.enabled), false)", " and s.enabled), true)")),
             ("fail-open read", key("Read Reply Mode", "onError", "continueRegularOutput")),
             ("a disabled read", key("Read Reply Mode", "disabled", True)),
             ("a stamp without the recorded-suppression guard",
              js("Outcome", "(resp.outcome === 'suppressed' && resp.off_hours === true) ? 'off_hours'", "resp.off_hours === true ? 'off_hours'")),
             ("the lease reads the schedule", q("Acquire Send Lease", "as suppressed\n), aud as (", "or " + off + " as suppressed\n), aud as (")))
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
    # personal chats first: the gate at message entry decides whether anything below runs at all
    try:
        declared = callee_inputs(WF)
    except (OSError, StopIteration, KeyError, ValueError) as e:
        fail(f"{f}: audience gate: the Decide Audience sub-workflow ({DECIDE_FILE}) is unreadable: {type(e).__name__}: {e}")
    try:
        check_audience_gate(wf, f, declared)
    except AssertionError as e:
        fail(str(e))
    _audience_tripwires(wf, f, declared)
    q = node(wf, "Claim Event")["parameters"]["query"]
    check("insert into chat_history_events" in q and "'client'" in q and "do nothing" in q, f"{f}: Claim Event must record the client row (idempotent)")
    check("n8n_chat_histories (session_id, message)" not in q, f"{f}: Claim Event must NOT write memory (the memory node does on a normal run)")
    q = node(wf, "Record Suppressed")["parameters"]["query"]
    for frag in ("'client'", MEM_MARK, "'human'", NO_DOUBLE, "where exists (select 1 from ev)"):
        check(frag in q, f"{f}: Record Suppressed lacks {frag!r}")
    q = node(wf, "Record Outgoing")["parameters"]["query"]
    for frag in ("'owner_phone'", "'outgoing_api'", "do nothing", "where exists (select 1 from ev where source = 'owner_phone')", NO_DOUBLE, "$2 not like '%@g.us'"):
        check(frag in q, f"{f}: Record Outgoing lacks {frag!r}")
    # personal chats: only a business chat is recorded — the history row stands behind the business word, so the memory
    # row (behind the history row) does too; anything else is named ignored_personal
    check(q.startswith("--") and "with aud as (\n  select " + au().BUSINESS_SQL("$1", "$2") + " as business\n)" in q
          and "  where $2 not like '%@g.us' and (select business from aud)\n" in q
          and "when not (select business from aud) then 'ignored_personal'" in q,
          f"{f}: Record Outgoing must record business chats only (audience.BUSINESS_SQL) and name the rest ignored_personal")
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
          and re.search(r'\(SELECT count\(\*\)::int FROM schedule\)\s+AS "deletedSchedules"', q),
          "B1: Delete Bot Files must wipe bot_schedule for every profile of the line (Delete Chat Memory) and count it as deletedSchedules")
    # «Личные и рабочие чаты» (2026-10-25): the chat decisions and the connection moment go with the bot the same way —
    # every profile of the line, the same statement, each counted under its own name (Deletion Settled? reads both).
    # Pinned here, after the T5 line check, for the reason the B1 pin gives: a pre-T5 copy must still be reported as the
    # T5 failure it is (tests/test_clone_retirements.py T5 N1) — the early table list above stops at its first miss.
    for table, alias, cte, counter in (("chat_audience", "a", "audience", "deletedAudience"),
                                       ("audience_connections", "cn", "connections", "deletedConnections")):
        check(f"DELETE FROM public.{table} {alias}\n  WHERE {alias}.profile_id IN (SELECT profile_id FROM valid)" in q
              and re.search(r'\(SELECT count\(\*\)::int FROM ' + cte + r'\)\s+AS "' + counter + '"', q),
              f"personal chats: Delete Bot Files must wipe {table} for every profile of the line (Delete Chat Memory) and "
              f"count it as {counter}")
    rb = node(load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json"), "Respond")["parameters"]["responseBody"]
    for k in ("deletedHistoryEvents", "deletedHandoffs", "deletedReplyEvents", "deletedChatStates", "deletedSchedules",
              "deletedAudience", "deletedConnections"):
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


# Personal chats (live check D5, 2026-10-28): prod saved every successful bot execution WITH its webhook body for 7 days — a
# personal chat's text and name included. Both templates and Suggest Replies keep no successful execution; errors are kept as
# before. A clone carries its template's setting — Create WA/TG's Set Fields copies the template's settings into a new clone,
# update-clones.py brings an existing one to it (TEMPLATE_SETTINGS) — and the staging copies keep everything
# (staging/derive-staging.py), so a staging scenario can still read an execution back. Both sides pinned here.
JOURNAL_OFF = (BOTS[0], BOTS[1], "9PTyYcelRQI7bGDb-Suggest_Replies.json")
CREATES = ("XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json")


def check_success_journal(files):
    """`files` = {file name: workflow} of the whole set. Raises AssertionError «<file>: journal: …»."""
    for f in JOURNAL_OFF:
        s = files[f].get("settings") or {}
        assert s.get("saveDataSuccessExecution") == "none", \
            f"{f}: journal: a successful execution must not be kept on prod (saveDataSuccessExecution 'none', got {s.get('saveDataSuccessExecution')!r})"
        assert s.get("saveDataErrorExecution", "all") == "all", f"{f}: journal: an error execution must still be kept (saveDataErrorExecution)"
    derive = _tool(os.path.join("staging", "derive-staging.py"), "derive_staging")
    for f, wf in files.items():
        staged = json.loads(derive.derive_file(json.dumps(wf, ensure_ascii=False)))
        if f in JOURNAL_OFF:
            got = (staged.get("settings") or {}).get("saveDataSuccessExecution")
            assert got == "all", f"{f}: journal: its staging copy must keep its successful executions (derive-staging.py), got {got!r}"
        else:
            assert (staged.get("settings") or {}) == (wf.get("settings") or {}), f"{f}: journal: the staging derivation must not change its settings"
    for f in CREATES:
        sf = node(files[f], "Set Fields")["parameters"]
        a = next((x for x in sf["assignments"]["assignments"] if x["name"] == "settings"), None)
        assert a is not None and "$json.settings" in a["value"] and "settings" in [s.strip() for s in sf.get("includeFields", "").split(",")], \
            f"{f}: journal: Create's Set Fields must hand a new clone the template's settings"
    uc = _tool("update-clones.py", "update_clones")
    assert "saveDataSuccessExecution" in getattr(uc, "TEMPLATE_SETTINGS", ()), \
        "update-clones.py: journal: an existing clone must be brought to the template's saveDataSuccessExecution (TEMPLATE_SETTINGS)"


def _journal_tripwires(files):
    """The journal checks must reject what they exist to catch — on copies, on every run."""
    def setting(f, k, v):
        def m(fs):
            s = fs[f].setdefault("settings", {})
            if v is None:
                s.pop(k, None)
            else:
                s[k] = v
        return m

    def no_settings_field(f):
        def m(fs):
            a = node(fs[f], "Set Fields")["parameters"]["assignments"]["assignments"]
            a[:] = [x for x in a if x["name"] != "settings"]
        return m

    wrong = (("the WhatsApp template keeps every success", setting(BOTS[0], "saveDataSuccessExecution", "all")),
             ("the Telegram template without the setting", setting(BOTS[1], "saveDataSuccessExecution", None)),
             ("Suggest Replies without the setting", setting(JOURNAL_OFF[2], "saveDataSuccessExecution", None)),
             ("a template that stops keeping its errors", setting(BOTS[0], "saveDataErrorExecution", "none")),
             ("Create WA's Set Fields without the template's settings", no_settings_field(CREATES[0])))
    for label, mutate in wrong:
        bad = copy.deepcopy(files)
        mutate(bad)
        try:
            check_success_journal(bad)
        except AssertionError:
            continue
        fail(f"journal tripwire accepted a broken journal ({label})")


def verify_journal():
    files = {f: load(f) for f in sorted(os.listdir(WF)) if f.endswith(".json")}
    try:
        check_success_journal(files)
    except AssertionError as e:
        fail(str(e))
    _journal_tripwires(files)
    print("OK  journal (prod keeps no successful execution of the bots and the panel; staging keeps them; clones follow)")


def main():
    global WF
    if "--dir" in sys.argv:
        WF = sys.argv[sys.argv.index("--dir") + 1]
    for f in BOTS:
        verify_bot(f)
    verify_handoff_workflows()
    verify_test_dialog()
    verify_integrations()
    verify_journal()
    print("ALL HISTORY/HANDOFF ASSERTS PASSED")


if __name__ == "__main__":
    main()
