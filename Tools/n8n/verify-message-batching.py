#!/usr/bin/env python3
"""Structural-assert verifier for the Phase 10 message-batching / debounce splice.

Proves BOTH canonical bot templates carry the identical pre-generation debounce+combine
stage that `apply-message-batching.py` splices onto the Phase-9 `Suppressed?` FALSE branch
(BATCH-01/02), and that the debounce winner reaches the reply chain ONLY through the billing
gate spliced after it (Count Dialog -> Quota Decision -> If Quota Allows, Billing Task 9).
These are exactly the subtle fail-safe properties a UI round-trip or a bad edit would silently
drop — a dropped body re-emit, an added `mark_all`, a broken rewire, or
a channel-specific Code body. Run before deploying to dev n8n (owner's 10-03 step); no live
n8n / network needed.

Usage:
  python3 Tools/n8n/verify-message-batching.py            # verify the committed workflows/
  python3 Tools/n8n/verify-message-batching.py --dir DIR  # verify a prod re-export (go/no-go)

--dir DIR overrides the workflow directory (default: the committed workflows/ next to this
script). Point it at a prod re-export so the SAME structural asserts gate a prod import.
Absent --dir, behavior is byte-identical to before.

Latest+Combine's crossed-response filter keeps a row of THIS chat — the webhook's chatId or, on WhatsApp only, the chat's
@lid code from the webhook's whatsapp_chat_id (audience.CHAT_IDENTITY_JS, with the template's channel on ONE line); a
tripwire proves the checks reject the code not accepted, any code accepted, the code accepted on Telegram and a code
without @lid accepted.

Exits 0 and prints "ALL BATCHING ASSERTS PASSED" when every assert holds.
Exits 1 with "BATCHING FAIL: <reason>" naming the first violated assert.
"""
import argparse
import copy
import json
import os
import re
import sys
import uuid

# Resolve workflow paths from this script's own location so cwd does not matter.
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WF = os.path.join(HERE, "workflows")
WF = DEFAULT_WF

WA_BOT = "4wYitz5ek30SVNlT-WhatsApp_Bot.json"
TG_BOT = "4VN3gsFaC2HUYmcc-Telegram_Bot.json"

# (template, its channel base). Only the base differs between the two — everything else in
# the spliced stage is byte-identical (the Code node is channel-agnostic: chat || text).
BOTS = [
    (WA_BOT, "https://wappi.pro/api/sync/"),
    (TG_BOT, "https://wappi.pro/tapi/sync/"),
]

WAPPI_CRED_ID = "EuhhqAaV56DpoqAN"       # WappiAuthToken, already bound in both templates
DEBOUNCE_AMOUNT = 8                       # ~8s in-memory Wait window
FETCH_MAX_TRIES = 3                       # Fetch Recent hot-path retry (review WR-03)
FETCH_WAIT_MS = 1000
TEXT_VALUE = "={{ $json.combinedText ?? $json.body.messages[0].body }}"
# Terminal OUTCOME stamp (audit stage 0.5, apply-outcome-stamps.py): every branch that used to be a
# bare dead-end now ends at this single Code node, and NOTHING else may hang off those branches.
OUTCOME_EDGE = {"node": "Outcome", "type": "main", "index": 0}
OUTCOME_LABELS = ("filtered", "suppressed", "superseded", "quota_denied", "sent")

# The chat's own rows (2026-10-29): Latest+Combine drops a row only when it names ANOTHER chat — a row of THIS chat names it
# by the webhook's chatId or, on WhatsApp only, by the chat's @lid code (the webhook's whatsapp_chat_id; Wappi lists every
# row of some WhatsApp chats under it). audience.CHAT_IDENTITY_JS, embedded verbatim, with the template's channel on ONE
# line; the rule's own clauses are pinned here as well.
TEMPLATE_CHANNEL = {WA_BOT: "whatsapp", TG_BOT: "telegram"}
IDENTITY_CLAUSES = ('const CODE_SUFFIX = "@lid";',
                    "const lid = (channel === 'whatsapp' && typeof code === 'string' && code.length > CODE_SUFFIX.length\n"
                    "    && code.endsWith(CODE_SUFFIX) && code !== chatId) ? code : '';",
                    "return { code: lid, names: (id) => id === chatId || (lid !== '' && id === lid) };")
LC_IDENTITY_LINES = ("const thisChat = chatIdentity(CHANNEL, triggeringChatId, wh.body.messages[0].whatsapp_chat_id);",
                     "const isForeign = (m) => m && m.chatId && triggeringChatId && !thisChat.names(m.chatId);",
                     "const fetched = fetchedAll.filter(m => !isForeign(m));",
                     "const foreignFetched = fetchedAll.length - fetched.length;")
# (label, old, new, the templates the mutation applies to) — the tripwire's own list
LC_IDENTITY_MUTATIONS = (
    ("the code not accepted: the code not handed in", "wh.body.messages[0].whatsapp_chat_id);", "'');", (WA_BOT, TG_BOT)),
    ("the code not accepted: the old filter", "!thisChat.names(m.chatId)", "m.chatId !== triggeringChatId", (WA_BOT, TG_BOT)),
    ("any code accepted", "id === chatId || (lid !== '' && id === lid)", "id === chatId || String(id).endsWith(CODE_SUFFIX)",
     (WA_BOT, TG_BOT)),
    ("any id accepted", "names: (id) => id === chatId || (lid !== '' && id === lid)", "names: (id) => true", (WA_BOT, TG_BOT)),
    ("accepted on Telegram: the channel guard dropped", "channel === 'whatsapp' && ", "", (WA_BOT, TG_BOT)),
    ("accepted on Telegram: the template says whatsapp", "const CHANNEL = 'telegram';", "const CHANNEL = 'whatsapp';", (TG_BOT,)),
    ("not accepted on WhatsApp: the template says telegram", "const CHANNEL = 'whatsapp';", "const CHANNEL = 'telegram';", (WA_BOT,)),
    ("a code without @lid accepted", "\n    && code.endsWith(CODE_SUFFIX)", "", (WA_BOT, TG_BOT)),
    ("the chat id taken for a code", " && code !== chatId)", ")", (WA_BOT, TG_BOT)),
)


_AUD = []


def _audience():
    """audience.py — the ONE home of CHAT_IDENTITY_JS (loaded once)."""
    if not _AUD:
        import importlib.util
        spec = importlib.util.spec_from_file_location("audience", os.path.join(HERE, "audience.py"))
        m = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(m)
        _AUD.append(m)
    return _AUD[0]


def latest_combine_identity_problems(js, f):
    """What is wrong with the chat-identity rule in one template's Latest+Combine body — [] when nothing."""
    aud = _audience()
    out = []
    b, e = js.find(aud.CHAT_IDENTITY_BEGIN), js.find(aud.CHAT_IDENTITY_END)
    embedded = js[b:e + len(aud.CHAT_IDENTITY_END)] if 0 <= b < e else ""
    if js.count(aud.CHAT_IDENTITY_BEGIN) != 1 or embedded != aud.CHAT_IDENTITY_JS:
        out.append(f"{f}: Latest+Combine must embed audience.CHAT_IDENTITY_JS verbatim, once")
    missing = [c for c in IDENTITY_CLAUSES if c not in embedded]
    if missing:
        out.append(f"{f}: Latest+Combine's chatIdentity does not state the rule (the chat id, or WhatsApp's @lid code that is "
                   f"not the chat id; nothing else): missing {missing[0]!r}")
    channels = re.findall(r"^const CHANNEL = '([a-z]*)';$", js, re.M)
    if channels != [TEMPLATE_CHANNEL[f]]:
        out.append(f"{f}: Latest+Combine must say the template's channel on ONE line, const CHANNEL = '{TEMPLATE_CHANNEL[f]}' "
                   f"(a code counts on WhatsApp only): {channels}")
    lacking = [x for x in LC_IDENTITY_LINES if js.count(x) != 1]
    if lacking:
        out.append(f"{f}: Latest+Combine must drop a row only when it names another chat (a row without a chatId kept): "
                   f"lacks {lacking[0]!r}")
    if "m.chatId !== triggeringChatId" in js:
        out.append(f"{f}: Latest+Combine still compares a row's chatId with the webhook's chatId alone (the @lid code is dropped)")
    return out


def latest_combine_identity_tripwire(js, f):
    """Every mutation of LC_IDENTITY_MUTATIONS must be rejected by latest_combine_identity_problems; returns how many."""
    rejected = 0
    for label, old, new, files in LC_IDENTITY_MUTATIONS:
        if f not in files:
            continue
        assert_that(old in js, f"{f}: tripwire anchor of {label!r} not found in Latest+Combine")
        assert_that(latest_combine_identity_problems(js.replace(old, new), f),
                    f"{f}: chat identity tripwire accepted a broken Latest+Combine ({label})")
        rejected += 1
    return rejected


def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as fh:
        return json.load(fh)


def node(nodes, name):
    for n in nodes:
        if n.get("name") == name:
            return n
    raise AssertionError(f"node '{name}' not found")


def assert_that(cond, reason):
    if not cond:
        raise AssertionError(reason)


def is_uuid(value):
    if not isinstance(value, str) or not value:
        return False
    try:
        uuid.UUID(value)
    except ValueError:
        return False
    return True


def check_bot(f, base):
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    # (1) the 4 spliced nodes exist (node() raises a named AssertionError if any is missing).
    dw = node(ns, "Debounce Wait")
    fr = node(ns, "Fetch Recent")
    lc = node(ns, "Latest+Combine")
    il = node(ns, "Is Latest?")

    # (2) Debounce Wait is an n8n Wait with amount == 8 (< 65s -> in-memory resume).
    assert_that(dw["type"] == "n8n-nodes-base.wait", f"{f}: Debounce Wait is not a wait node")
    assert_that(dw["parameters"].get("amount") == DEBOUNCE_AMOUNT,
                f"{f}: Debounce Wait amount != {DEBOUNCE_AMOUNT}")
    # ...and carries a webhookId like every other Wait node in both templates (review IN-03): dead
    # weight at 8s, but the resume URL n8n registers once DEBOUNCE_SECONDS is tuned to >= 65s.
    # Presence + uuid shape only, deliberately NOT an exact value: this same verifier gates a prod
    # RE-EXPORT via --dir, and an importer re-minting the id must not read as a no-go.
    assert_that(is_uuid(dw.get("webhookId")),
                f"{f}: Debounce Wait has no valid webhookId (breaks webhook-resume if the window is tuned >= 65s)")

    # (3) Fetch Recent: GET <base>messages/get, cred EuhhqAaV56DpoqAN, query params exactly
    #     {profile_id, chat_id, limit} with NO mark_all (Pitfall 5 — marking read during the
    #     wait would defeat the deliberate downstream humanizer Mark Read).
    assert_that(fr["type"] == "n8n-nodes-base.httpRequest", f"{f}: Fetch Recent is not an httpRequest node")
    assert_that(fr["parameters"].get("method") == "GET", f"{f}: Fetch Recent is not a GET")
    assert_that(fr["parameters"].get("url") == base + "messages/get",
                f"{f}: Fetch Recent url wrong: {fr['parameters'].get('url')}")
    cred = fr.get("credentials", {}).get("httpHeaderAuth", {}).get("id")
    assert_that(cred == WAPPI_CRED_ID, f"{f}: Fetch Recent cred {cred} != {WAPPI_CRED_ID}")
    qp_names = [p.get("name") for p in fr["parameters"].get("queryParameters", {}).get("parameters", [])]
    assert_that(qp_names == ["profile_id", "chat_id", "limit"],
                f"{f}: Fetch Recent query params {qp_names} != ['profile_id','chat_id','limit']")
    assert_that("mark_all" not in qp_names,
                f"{f}: Fetch Recent carries mark_all (would mark the chat read during the wait — Pitfall 5)")
    # Hot-path retry: an un-retried transient Wappi failure drops the customer's message
    # outright (review WR-03).
    assert_that(fr.get("retryOnFail") is True, f"{f}: Fetch Recent is not retryOnFail (hot reply path)")
    assert_that(fr.get("maxTries") == FETCH_MAX_TRIES, f"{f}: Fetch Recent maxTries != {FETCH_MAX_TRIES}")
    assert_that(fr.get("waitBetweenTries") == FETCH_WAIT_MS,
                f"{f}: Fetch Recent waitBetweenTries != {FETCH_WAIT_MS}")

    # (4) Latest+Combine jsCode: .first() not .item (paired-item safety, Pitfall 1/anti-pattern);
    #     a time sort (Pitfall 3); the chat||text channel-agnostic test (Pitfall 4); and it
    #     re-emits the webhook body with abort + combinedText (Pitfall 1 — the big one).
    assert_that(lc["type"] == "n8n-nodes-base.code", f"{f}: Latest+Combine is not a code node")
    js = lc["parameters"]["jsCode"]
    assert_that("$('Webhook').first().json" in js,
                f"{f}: Latest+Combine does not reach back via $('Webhook').first().json")
    assert_that("$('Webhook').item" not in js,
                f"{f}: Latest+Combine uses the fragile .item back-reference (breaks across Wait+HTTP)")
    assert_that(".sort(" in js and "time" in js, f"{f}: Latest+Combine does not sort the fetch by time")
    assert_that("m.type === 'chat' || m.type === 'text'" in js,
                f"{f}: Latest+Combine is not channel-agnostic (missing chat || text)")
    assert_that("...wh" in js and "abort" in js and "combinedText" in js,
                f"{f}: Latest+Combine does not re-emit body ({{ ...wh, abort, combinedText }})")
    # An empty combine run must stay NULLISH: `''` would win the Text node's `??` fallback and
    # feed the agent an empty prompt (review WR-01).
    assert_that(r"combinedText = parts.length > 0 ? parts.join('\n') : null;" in js,
                f"{f}: Latest+Combine joins an empty run to '' (defeats the Text node ?? fallback)")
    # Crossed-response guard: rows naming a FOREIGN chat are dropped before the is-latest /
    # combine computation, so a crossed messages/get can never decide either (review WR-03) — and a row
    # of THIS chat names it by the webhook's chatId or, on WhatsApp only, by its @lid code (2026-10-29).
    problems = latest_combine_identity_problems(js, f)
    assert_that(not problems, problems[0] if problems else "")
    rejected = latest_combine_identity_tripwire(js, f)
    assert_that("foreignFetched" in js,
                f"{f}: Latest+Combine does not emit foreignFetched (crossing observability)")
    # Explicit paired-item linkage: every node below the splice resolves $('Webhook').item
    # through this Code node (review WR-04; same idiom as the orchestrators' Vertical Prompt).
    assert_that("pairedItem: { item: 0 }" in js,
                f"{f}: Latest+Combine omits an explicit pairedItem (downstream $('Webhook').item is implicit)")

    # Is Latest? boolean condition reads $json.abort.
    cond = il["parameters"]["conditions"]["conditions"][0]
    assert_that(cond["leftValue"] == "={{ $json.abort }}", f"{f}: Is Latest? condition does not read $json.abort")
    assert_that(cond["operator"].get("operation") == "true", f"{f}: Is Latest? operator is not boolean-true")

    # (5) Connections prove the debounce sits AFTER the Phase-9 gate and only the winner proceeds:
    #     Suppressed? main[1] -> Debounce Wait; the 3-node chain; Is Latest? main[1] (winner) ->
    #     the billing gate (Count Dialog -> Quota Decision -> If Quota Allows, Billing Task 9) ->
    #     If Quota Allows main[0] -> Input type, AND Is Latest? main[0] == [] (the aborted fragment
    #     dead-ends) AND If Quota Allows main[1] == [] (a quota-refused dialog never auto-replies).
    #     Suppressed? main[0] stays a dead-end. Until 2026-09-17 this verifier still expected the
    #     pre-billing direct edge Is Latest? -> Input type and failed on every run; the contract is
    #     the gated path, not "any path" — every hop is pinned by name so a UI round-trip that drops
    #     the gate (winner straight to Input type) reads as a FAIL again.
    # Stage 3 (A4): the reply branch first CLAIMS the event (Claim Event -> Claimed?), and only a
    # first-time claim enters the debounce; a re-delivered event is refused before any side effect.
    assert_that(conns["Suppressed?"]["main"][1] == [{"node": "Claim Event", "type": "main", "index": 0}],
                f"{f}: Suppressed? main[1] must route to Claim Event (stage 3): {conns['Suppressed?']['main'][1]}")
    assert_that(conns["Claim Event"]["main"][0] == [{"node": "Claimed?", "type": "main", "index": 0}],
                f"{f}: Claim Event must chain to Claimed?")
    assert_that(conns["Claimed?"]["main"][0] == [{"node": "Debounce Wait", "type": "main", "index": 0}],
                f"{f}: Claimed? main[0] (first claim) does not route to Debounce Wait (debounce not after the claim)")
    assert_that(conns["Claimed?"]["main"][1] == [OUTCOME_EDGE],
                f"{f}: Claimed? main[1] (duplicate) must end ONLY at the Outcome stamp: {conns['Claimed?']['main'][1]}")
    ce = node(ns, "Claim Event")
    assert_that(ce["type"] == "n8n-nodes-base.postgres" and "on conflict (profile_id, chat_id, message_id)" in ce["parameters"]["query"],
                f"{f}: Claim Event is not the reply_events upsert")
    # Stage 4 (A6): a suppressed («Вместе») incoming is still RECORDED (history + memory) before the stamp.
    assert_that(conns["Suppressed?"]["main"][0] == [{"node": "Record Suppressed", "type": "main", "index": 0}]
                and conns["Record Suppressed"]["main"][0] == [OUTCOME_EDGE],
                f"{f}: Suppressed? main[0] must be Record Suppressed → Outcome (stage 4): {conns['Suppressed?']['main'][0]}")
    # Stage 4 (A6): event routing in front of the group/binding filter — incoming → If; outgoing (phone/api)
    # → Outgoing Bound? → Record Outgoing → Outcome; anything else → Outcome ('ignored').
    assert_that(conns["Webhook"]["main"][0] == [{"node": "Is Incoming?", "type": "main", "index": 0}], f"{f}: Webhook must feed Is Incoming? (stage 4)")
    assert_that(conns["Is Incoming?"]["main"] == [[{"node": "If", "type": "main", "index": 0}], [{"node": "Is Outgoing?", "type": "main", "index": 0}]],
                f"{f}: Is Incoming? branches wrong: {conns['Is Incoming?']['main']}")
    assert_that(conns["Is Outgoing?"]["main"] == [[{"node": "Outgoing Bound?", "type": "main", "index": 0}], [OUTCOME_EDGE]],
                f"{f}: Is Outgoing? branches wrong: {conns['Is Outgoing?']['main']}")
    assert_that(conns["Outgoing Bound?"]["main"] == [[{"node": "Record Outgoing", "type": "main", "index": 0}], [OUTCOME_EDGE]]
                and conns["Record Outgoing"]["main"][0] == [OUTCOME_EDGE],
                f"{f}: Outgoing Bound? / Record Outgoing wiring wrong")
    assert_that(conns.get("Create Handoff", {}).get("ai_tool") == [[{"node": "AI Agent", "type": "ai_tool", "index": 0}]],
                f"{f}: Create Handoff tool is not wired into the AI Agent")
    assert_that(conns["Debounce Wait"]["main"][0][0]["node"] == "Fetch Recent",
                f"{f}: Debounce Wait does not chain to Fetch Recent")
    assert_that(conns["Fetch Recent"]["main"][0][0]["node"] == "Latest+Combine",
                f"{f}: Fetch Recent does not chain to Latest+Combine")
    assert_that(conns["Latest+Combine"]["main"][0][0]["node"] == "Is Latest?",
                f"{f}: Latest+Combine does not chain to Is Latest?")
    assert_that(conns["Is Latest?"]["main"][1] == [{"node": "Count Dialog", "type": "main", "index": 0}],
                f"{f}: Is Latest? main[1] (winner) does not proceed to the billing gate (Count Dialog): "
                f"{conns['Is Latest?']['main'][1]}")
    assert_that(conns["Is Latest?"]["main"][0] == [{"node": "Cancel Label", "type": "main", "index": 0}],
                f"{f}: Is Latest? main[0] (aborted fragment) must be recorded via Cancel Label (stage 3): {conns['Is Latest?']['main'][0]}")
    assert_that(conns["Count Dialog"]["main"][0] == [{"node": "Quota Decision", "type": "main", "index": 0}],
                f"{f}: Count Dialog does not chain to Quota Decision: {conns['Count Dialog']['main'][0]}")
    assert_that(conns["Quota Decision"]["main"][0] == [{"node": "If Quota Allows", "type": "main", "index": 0}],
                f"{f}: Quota Decision does not chain to If Quota Allows: {conns['Quota Decision']['main'][0]}")
    assert_that(conns["If Quota Allows"]["main"][0] == [{"node": "Input type", "type": "main", "index": 0}],
                f"{f}: If Quota Allows main[0] (allowed) does not proceed to Input type: "
                f"{conns['If Quota Allows']['main'][0]}")
    assert_that(conns["If Quota Allows"]["main"][1] == [{"node": "Cancel Label", "type": "main", "index": 0}],
                f"{f}: If Quota Allows main[1] (quota refused) must be recorded via Cancel Label (stage 3): {conns['If Quota Allows']['main'][1]}")
    assert_that(conns["Cancel Label"]["main"][0] == [{"node": "Record Cancel", "type": "main", "index": 0}]
                and conns["Record Cancel"]["main"][0] == [OUTCOME_EDGE],
                f"{f}: Cancel Label -> Record Cancel -> Outcome chain broken")
    # The winner must reach Input type ONLY through the gate: no direct edge may bypass it.
    direct = [c for outs in conns["Is Latest?"]["main"] for c in outs if c["node"] == "Input type"]
    assert_that(direct == [],
                f"{f}: Is Latest? still carries a direct edge to Input type that bypasses the billing gate")

    # (6) The Text set node injects combinedText with the single-message fallback.
    txt = node(ns, "Text")["parameters"]["assignments"]["assignments"][0]["value"]
    assert_that(txt == TEXT_VALUE, f"{f}: Text value wrong: {txt}")

    # (8) Outcome stamp (stage 0.5): one Code node, reached from exactly the five terminal branches,
    #     labelling each by the branch that reached it; the send's own output is stamped too, so
    #     «sent» means "the transport call returned", never "the LLM produced text".
    oc = node(ns, "Outcome")
    assert_that(oc["type"] == "n8n-nodes-base.code", f"{f}: Outcome is not a code node")
    ojs = oc["parameters"]["jsCode"]
    for label in OUTCOME_LABELS:
        assert_that(f"'{label}'" in ojs, f"{f}: Outcome stamp lacks the label {label!r}")
    assert_that("$execution.id" in ojs and "$prevNode.name" in ojs,
                f"{f}: Outcome stamp must carry the execution id as correlationId and derive the label from $prevNode")
    assert_that(conns["If"]["main"][1] == [OUTCOME_EDGE], f"{f}: If main[1] (foreign/group filtered) must end at Outcome: {conns['If']['main'][1]}")
    # Stage 3 (A3/A7): the send is preceded by Final Fetch -> Final Check -> Final OK? -> Acquire Send
    # Lease -> Peer Recheck? -> Send Allowed? and followed by Settle Send -> [reconcile] -> Record Send -> Outcome.
    for src, dst in (("Retrieve Answer", "Final Fetch"), ("Ask to Send Text", "Final Fetch"), ("Final Fetch", "Final Check"),
                     ("Final Check", "Final OK?"), ("Acquire Send Lease", "Peer Recheck?"), ("Wait Peer Recheck", "Recheck Fetch"),
                     ("Recheck Fetch", "Final Check"), ("HTTP Request", "Settle Send"),
                     ("Settle Send", "Send Unknown?"), ("Wait Reconcile", "Reconcile Fetch"), ("Reconcile Fetch", "Owner Row?"), ("Owner Row?", "Reconcile Check"),
                     ("Reconcile Check", "Record Send"), ("Record Send", "Outcome"), ("AI Agent", "Generated?")):
        assert_that(conns[src]["main"][0] == [{"node": dst, "type": "main", "index": 0}],
                    f"{f}: {src} must chain ONLY to {dst}: {conns[src]['main'][0]}")
    assert_that(conns["Final OK?"]["main"] == [[{"node": "Acquire Send Lease", "type": "main", "index": 0}], [{"node": "Cancel Label", "type": "main", "index": 0}]],
                f"{f}: Final OK? branches wrong: {conns['Final OK?']['main']}")
    # V5: the lease's one re-read (Peer Recheck? true) loops back through Wait Peer Recheck → Recheck Fetch → Final Check
    assert_that(conns["Peer Recheck?"]["main"] == [[{"node": "Wait Peer Recheck", "type": "main", "index": 0}], [{"node": "Send Allowed?", "type": "main", "index": 0}]],
                f"{f}: Peer Recheck? branches wrong: {conns['Peer Recheck?']['main']}")
    assert_that(conns["Send Allowed?"]["main"] == [[{"node": "HTTP Request", "type": "main", "index": 0}], [{"node": "Cancel Label", "type": "main", "index": 0}]],
                f"{f}: Send Allowed? branches wrong: {conns['Send Allowed?']['main']}")
    assert_that(conns["Send Unknown?"]["main"] == [[{"node": "Wait Reconcile", "type": "main", "index": 0}],
                                                    [{"node": "Record Send", "type": "main", "index": 0}]],
                f"{f}: Send Unknown? branches wrong: {conns['Send Unknown?']['main']}")
    # V6: Generated?[yes] → Stamp Draft (ties the agent's memory row to the execution) → Pause Before Reading
    assert_that(conns["Generated?"]["main"] == [[{"node": "Stamp Draft", "type": "main", "index": 0}], [{"node": "Cancel Label", "type": "main", "index": 0}]],
                f"{f}: Generated? branches wrong: {conns['Generated?']['main']}")
    assert_that(conns["Stamp Draft"]["main"] == [[{"node": "Pause Before Reading", "type": "main", "index": 0}]],
                f"{f}: Stamp Draft must chain ONLY to Pause Before Reading: {conns['Stamp Draft']['main']}")
    send = node(ns, "HTTP Request")
    body = [p for p in send["parameters"]["bodyParameters"]["parameters"] if p["name"] == "body"][0]["value"]
    assert_that(body == "={{ $('Final Check').first().json.text }}", f"{f}: the send body must be the text the Final Check re-emitted: {body}")
    assert_that(send.get("onError") == "continueRegularOutput" and send["parameters"]["options"]["response"]["response"].get("neverError") is True
                and send["parameters"]["options"]["response"]["response"].get("fullResponse") is True,
                f"{f}: the send must never throw and must expose the full response (Settle Send classifies it)")
    lease = node(ns, "Acquire Send Lease")["parameters"]["query"]
    # personal chats (2026-10-25): the same statement re-reads whether the chat is still a business chat
    for frag in ("from reply_mode_flags", "incoming_generation = $4::bigint", "lock_expires_at = now() + interval", "state = 'send_pending'",
                 "from chat_audience", "and not (select personal from aud)"):
        assert_that(frag in lease, f"{f}: Acquire Send Lease lacks {frag!r} (mode / audience re-read, generation, lease and event state must be ONE statement)")
    for name in ("Mark Read", "Typing"):
        assert_that(node(ns, name).get("onError") == "continueRegularOutput", f"{f}: {name} must fail open (decorative action)")
    assert_that(node(ns, "AI Agent").get("onError") == "continueRegularOutput", f"{f}: AI Agent must not abort the run (Generated? records generation_failed)")
    for name, cap in (("Count Input Words", "12"), ("Count Output Words", "20")):
        assert_that(f"Math.min(" in node(ns, name)["parameters"]["jsCode"] and cap in node(ns, name)["parameters"]["jsCode"],
                    f"{f}: {name} pause is not clamped (delay budget)")
    assert_that("Outcome" not in conns or conns["Outcome"].get("main", [[]])[0] == [],
                f"{f}: Outcome must be terminal (it feeds {conns.get('Outcome')})")

    print(f"OK  {f} (Latest+Combine's own rows: chat identity tripwire, {rejected} mutations rejected)")
    return wf


def check_cross_template(wa, tg):
    # (7) Cross-template identity: the Latest+Combine jsCode is byte-identical (channel-agnostic)
    #     and Fetch Recent differs ONLY in the base (api/sync vs tapi/sync).
    #     The one exception is the line that names the template's channel (each template's own is pinned in check_bot).
    js_wa = node(wa["nodes"], "Latest+Combine")["parameters"]["jsCode"].replace("const CHANNEL = 'whatsapp';", "const CHANNEL = '__CH__';")
    js_tg = node(tg["nodes"], "Latest+Combine")["parameters"]["jsCode"].replace("const CHANNEL = 'telegram';", "const CHANNEL = '__CH__';")
    assert_that(js_wa == js_tg, "Latest+Combine jsCode differs between templates beyond the channel line (must be channel-agnostic)")

    fr_wa = copy.deepcopy(node(wa["nodes"], "Fetch Recent")["parameters"])
    fr_tg = copy.deepcopy(node(tg["nodes"], "Fetch Recent")["parameters"])
    assert_that(fr_wa.get("url") == "https://wappi.pro/api/sync/messages/get",
                f"WhatsApp Fetch Recent url wrong: {fr_wa.get('url')}")
    assert_that(fr_tg.get("url") == "https://wappi.pro/tapi/sync/messages/get",
                f"Telegram Fetch Recent url wrong: {fr_tg.get('url')}")
    fr_wa["url"] = fr_tg["url"] = ""   # blank the sole allowed difference, then require equality
    assert_that(fr_wa == fr_tg, "Fetch Recent differs between templates beyond the channel base url")

    # (7b) Outcome stamp jsCode differs ONLY by the channel constant.
    o_wa = node(wa["nodes"], "Outcome")["parameters"]["jsCode"].replace("'whatsapp'", "'__CH__'")
    o_tg = node(tg["nodes"], "Outcome")["parameters"]["jsCode"].replace("'telegram'", "'__CH__'")
    assert_that(o_wa == o_tg, "Outcome stamp jsCode differs between templates beyond the channel constant")

    print("OK  cross-template identity")


def main():
    global WF
    ap = argparse.ArgumentParser(
        description="Structural-assert verifier for the Phase 10 message-batching / debounce splice."
    )
    ap.add_argument(
        "--dir",
        default=DEFAULT_WF,
        help="workflow directory to verify (default: the committed workflows/ next to this "
             "script). Point at a prod re-export dir to run the batching asserts as a "
             "post-import go/no-go.",
    )
    args = ap.parse_args()
    WF = args.dir
    try:
        spliced = {f: check_bot(f, base) for f, base in BOTS}
        check_cross_template(spliced[WA_BOT], spliced[TG_BOT])
    except AssertionError as e:
        print(f"BATCHING FAIL: {e}")
        sys.exit(1)
    except (OSError, KeyError, IndexError, json.JSONDecodeError) as e:
        print(f"BATCHING FAIL: unexpected structural error: {e}")
        sys.exit(1)
    print("ALL BATCHING ASSERTS PASSED")


if __name__ == "__main__":
    main()
