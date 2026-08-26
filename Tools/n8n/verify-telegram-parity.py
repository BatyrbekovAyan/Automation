#!/usr/bin/env python3
"""Structural-assert verifier for the Phase 4 Telegram-parity edits.

Proves the four canonical n8n workflow JSONs carry the Telegram-parity fixes and
that the RAG re-stamp nodes are injection-safe. Run before deploying to dev n8n
(the owner's 04-HUMAN-UAT step); no live n8n / network needed.

Usage:
  python3 Tools/n8n/verify-telegram-parity.py            # verify the committed workflows/
  python3 Tools/n8n/verify-telegram-parity.py --dir DIR  # verify a prod re-export (go/no-go)

--dir DIR overrides the workflow directory (default: the committed workflows/ next to this
script). Point it at a prod re-export so the SAME structural asserts gate a prod import —
catching a UI round-trip strip (dropped ai_embedding wiring, stripped top-level id, dropped
mark_all guard, etc.). Absent --dir, behavior is byte-identical to before.

Exits 0 and prints "ALL PARITY ASSERTS PASSED" when every assert holds.
Exits 1 with "PARITY FAIL: <reason>" naming the first violated assert.
"""
import argparse
import json
import os
import re
import sys

# Resolve workflow paths from this script's own location so cwd does not matter.
# WF defaults to the committed workflows/ dir; main() reassigns it from --dir so the same
# asserts can gate a prod re-export. load() reads whatever WF points at.
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WF = os.path.join(HERE, "workflows")
WF = DEFAULT_WF

TG_BOT = "4VN3gsFaC2HUYmcc-Telegram_Bot.json"
WA_BOT = "4wYitz5ek30SVNlT-WhatsApp_Bot.json"
CREATE_TG = "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json"
CREATE_WA = "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json"
SUGGEST = "9PTyYcelRQI7bGDb-Suggest_Replies.json"
RC_EVENTS = "ZGYr6srzS3rSSXHp-RevenueCat_Events.json"

# The executeQuery Postgres credential shared by Dashboard_Outcomes / Delete_File /
# Delete_Bot_Files. The re-stamp nodes MUST use this, NOT the memoryPostgresChat cred.
# Since ec15832 dev carries a single Postgres cred (vvRrFiEXzLVqKjOx); 1H5xlpFSESU4w6JH
# no longer exists there — the negative assert below stays to catch re-introduction.
PG_EXECUTEQUERY_CRED = "vvRrFiEXzLVqKjOx"
PG_MEMORY_CRED = "1H5xlpFSESU4w6JH"

# Expected node-name set of the Telegram bot template (insertion/rename/drop guard; the
# index positions the orchestrator patches are asserted separately via nodes[0]/nodes[5]).
# History: Phase 4 parity baseline = 24 nodes; Phase 9 suppression gate +2 (Read Reply
# Mode, Suppressed?); Phase 10 debounce splice +4 (Debounce Wait, Fetch Recent,
# Latest+Combine, Is Latest?); billing Task 9 dialog-metering splice +3 (Count Dialog,
# Quota Decision, If Quota Allows). A future splice must extend this set deliberately.
TG_BOT_NODE_NAMES = {
    "Webhook", "HTTP Request", "Transcribe Audio", "Text", "Audio", "AI Agent",
    "Input type", "Mark Read", "Typing", "Reading Pause", "Typing Pause",
    "Pause Before Reading", "Ask to Send Text", "Input type2", "Count Output Words",
    "Count Input Words", "Download Audio", "Listening Pause", "Chat Memory", "If",
    "Read Reply Mode", "Suppressed?",
    "OpenAI", "Supabase Vector Store", "Retrieve Answer", "OpenAI Embedding",
    "Debounce Wait", "Fetch Recent", "Latest+Combine", "Is Latest?",
    "Count Dialog", "Quota Decision", "If Quota Allows",
}


def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as fh:
        return json.load(fh)


def node(nodes, name):
    for n in nodes:
        if n.get("name") == name:
            return n
    raise AssertionError(f"node '{name}' not found")


def sql_code(query):
    """The query with `--` comments stripped.

    These queries are heavily commented -- deliberately, they carry the reasoning -- and the
    comments name the very tables and clauses the asserts below look for. Asserting against
    the raw text would let a comment satisfy a check about executable SQL (and did: the
    suggestions gate's own comment explaining that it never touches dialog_counts tripped
    the assert that it never touches dialog_counts). Every content assert reads this.
    """
    return "\n".join(re.sub(r"--.*$", "", line) for line in query.split("\n"))


def js_code(jscode):
    """Same idea as sql_code, for the Code nodes: strip `//` line comments before asserting
    on content. These node bodies carry their reasoning too, and prose that NAMES a symbol
    would otherwise satisfy (or, for a must-NOT-appear assert, break) a check about code."""
    return "\n".join(re.sub(r"//.*$", "", line) for line in jscode.split("\n"))


def check_telegram_bot():
    f = TG_BOT
    wf = load(f)
    ns = wf["nodes"]

    # (vi) node set + order invariant: Set Fields patches nodes[0]/nodes[5] by index.
    names = [n["name"] for n in ns]
    assert len(names) == len(TG_BOT_NODE_NAMES), \
        f"{f}: node count {len(names)} != {len(TG_BOT_NODE_NAMES)} (insertion invariant broken)"
    missing = TG_BOT_NODE_NAMES - set(names)
    extra = set(names) - TG_BOT_NODE_NAMES
    assert not missing and not extra, \
        f"{f}: node-name set drift (missing={sorted(missing)}, extra={sorted(extra)})"
    assert ns[0]["name"] == "Webhook", f"{f}: nodes[0] is '{ns[0]['name']}', expected 'Webhook'"
    assert ns[5]["name"] == "AI Agent", f"{f}: nodes[5] is '{ns[5]['name']}', expected 'AI Agent'"

    # (i) outbound HTTP nodes post to tapi bases, zero api/sync remains.
    url_send = node(ns, "HTTP Request")["parameters"]["url"]
    url_read = node(ns, "Mark Read")["parameters"]["url"]
    url_type = node(ns, "Typing")["parameters"]["url"]
    assert url_send == "https://wappi.pro/tapi/sync/message/send", f"{f}: send url wrong: {url_send}"
    assert url_read == "https://wappi.pro/tapi/sync/message/mark/read", f"{f}: mark-read url wrong: {url_read}"
    assert url_type == "https://wappi.pro/tapi/sync/chats/typing/start", f"{f}: typing url wrong: {url_type}"
    for u in (url_send, url_read, url_type):
        assert "/api/sync/" not in u, f"{f}: api/sync base still present in {u}"

    # (ii) Mark Read must not carry the undocumented mark_all query param.
    read_qp = node(ns, "Mark Read")["parameters"].get("queryParameters", {}).get("parameters", [])
    qp_names = {p.get("name") for p in read_qp}
    assert "mark_all" not in qp_names, f"{f}: Mark Read still has mark_all query param"
    assert "profile_id" in qp_names, f"{f}: Mark Read lost its profile_id query param"

    # (iii) both Input type Switch nodes route type:"text" (combinator 'or' with a text match).
    for sw_name in ("Input type", "Input type2"):
        sw = node(ns, sw_name)
        first_rule = sw["parameters"]["rules"]["values"][0]
        assert first_rule["outputKey"] == "Text", f"{f}: {sw_name} first rule is not the Text output"
        conds = first_rule["conditions"]
        rights = {c.get("rightValue") for c in conds["conditions"]}
        assert "text" in rights, f"{f}: {sw_name} Text rule does not match 'text'"
        assert "chat" in rights, f"{f}: {sw_name} Text rule dropped the 'chat' match"
        assert conds["combinator"] == "or", f"{f}: {sw_name} Text rule combinator is not 'or'"

    # (iv) Listening Pause resolves length_seconds fallback.
    pause = node(ns, "Listening Pause")["parameters"]["amount"]
    assert "length_seconds" in pause, f"{f}: Listening Pause missing length_seconds fallback"
    assert "media_info.duration + 2" not in pause, f"{f}: Listening Pause still uses the naive duration expr"

    # (v) Chat Memory sessionKey keys on chatId, not from.
    skey = node(ns, "Chat Memory")["parameters"]["sessionKey"]
    assert skey.rstrip().endswith("chatId }}"), f"{f}: sessionKey does not end with chatId: {skey}"
    assert ".from }}" not in skey, f"{f}: sessionKey still references .from: {skey}"

    # (vii) vector-store retrieval filter key unchanged (botTgId).
    sup = node(ns, "Supabase Vector Store")
    mv = sup["parameters"]["options"]["metadata"]["metadataValues"]
    assert len(mv) == 1 and mv[0]["name"] == "botTgId", f"{f}: retrieve filter key not single botTgId: {mv}"

    print(f"OK  {f}")


def check_restamp_orchestrator(f, jsonb_key, opposite_field):
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    # (i) Restamp RAG Chunks postgres node with the executeQuery credential.
    r = node(ns, "Restamp RAG Chunks")
    assert r["type"] == "n8n-nodes-base.postgres", f"{f}: Restamp RAG Chunks is not a postgres node"
    assert r["parameters"].get("operation") == "executeQuery", f"{f}: Restamp op is not executeQuery"
    cred = r["credentials"]["postgres"]["id"]
    assert cred == PG_EXECUTEQUERY_CRED, \
        f"{f}: Restamp cred {cred} != executeQuery cred {PG_EXECUTEQUERY_CRED}"
    assert cred != PG_MEMORY_CRED, \
        f"{f}: Restamp uses the memoryPostgresChat cred {PG_MEMORY_CRED} (wrong credential)"

    # (ii)+(iii) SQL is parameterized ($1/$2), targets the right jsonb key, no interpolation.
    q = r["parameters"]["query"]
    assert f"jsonb_set(metadata, '{jsonb_key}'" in q, f"{f}: jsonb_set target is not {jsonb_key}: {q}"
    assert "$1" in q and "$2" in q, f"{f}: SQL not parameterized with $1/$2: {q}"
    assert "{{" not in q, f"{f}: SQL string contains a '{{{{' interpolation (injection risk): {q}"

    # (ii-b) -1/'' sentinel guard: a '-1' or '' opposite-channel id must match zero rows,
    # otherwise a single-channel create claims shared fully-unauthed chunks from OTHER bots.
    assert "$2 <> '-1'" in q, f"{f}: Restamp SQL missing the -1 sentinel guard: {q}"
    assert "$2 <> ''" in q, f"{f}: Restamp SQL missing the empty-string sentinel guard: {q}"

    # (ii-c) queryReplacement binding: exactly two comma-separated segments where only the
    # LEADING '=' marks expression mode. A stray '=' after the comma is literal text and
    # corrupts $2 to '=<id>' (permanent 0-row no-op). Exact match also catches swapped or
    # wrong opposite-channel field names.
    qr = r["parameters"]["options"]["queryReplacement"]
    expected_qr = ("={{ $('Get Created Workflow Id').item.json.id }},"
                   "{{ $('Unity Webhook').first().json.body." + opposite_field + " }}")
    assert qr == expected_qr, \
        f"{f}: queryReplacement format wrong (stray '=' after comma or wrong bindings): {qr}"

    # robustness: a 0-row UPDATE or DB error must not break the response chain.
    assert r.get("alwaysOutputData") is True, f"{f}: Restamp alwaysOutputData not true"
    assert r.get("onError") == "continueRegularOutput", f"{f}: Restamp onError not continueRegularOutput"

    # (iv) wiring: Set Wappi Webhook Types -> Restamp RAG Chunks -> Send New Workflows Id (terminal).
    swwt = conns["Set Wappi Webhook Types"]["main"][0][0]["node"]
    assert swwt == "Restamp RAG Chunks", f"{f}: Set Wappi Webhook Types -> {swwt}, expected Restamp RAG Chunks"
    nxt = conns["Restamp RAG Chunks"]["main"][0][0]["node"]
    assert nxt == "Send New Workflows Id", f"{f}: Restamp RAG Chunks -> {nxt}, expected Send New Workflows Id"
    assert "Send New Workflows Id" not in conns, \
        f"{f}: Send New Workflows Id has an outgoing connection (must stay the terminal/response node)"

    # (v) Unity Webhook responseMode still lastNode.
    uw = node(ns, "Unity Webhook")
    assert uw["parameters"].get("responseMode") == "lastNode", f"{f}: Unity Webhook responseMode changed"

    print(f"OK  {f}")


# Cloud-shaped canonical credential ids, pinned exactly (review round 2 — the URL-only
# check in round 1 let a copy carrying dev credential ids + active:true +
# settings.binaryMode slip through cleanly, since none of those dimensions is a dev URL
# string). Values confirmed against the committed canonical files, both identical.
CANON_N8N_API_CRED = "X1k4igOAG65Fb3oz"      # n8nApi ("n8n account")
CANON_N8N_APIKEY_CRED = "RV7m661NMLPXEcvm"   # httpHeaderAuth ("n8nAPIKey") on the 3 API nodes
CANON_N8N_BEARER_CRED = "nfU8CbYjPssGjEZA"   # httpBearerAuth ("Bearer Auth account")
CANON_WAPPI_CRED = "EuhhqAaV56DpoqAN"        # httpHeaderAuth ("WappiAuthToken")
# CANON_POSTGRES_CRED reuses PG_EXECUTEQUERY_CRED (vvRrFiEXzLVqKjOx), already defined above —
# single source of truth rather than a second literal that could drift from it.

API_NODE_NAMES = ("Get Sample Workflow", "Create Workflow", "Activate Created Workflow")
WAPPI_NODE_NAMES = ("Set Wappi Webhook", "Set Wappi Webhook Types", "Delete Refused Profile")
POSTGRES_NODE_NAMES = (
    "Restamp RAG Chunks", "Ensure Subscriber", "Count Channels",
    "Retire Same Bot Slot", "Register Profile",
)


def check_canonical_export_invariant(f):
    """Task 8 fix-round regression guard (hardened in review round 2): the canonical
    export must NEVER absorb dev-only values — the standing invariant documented in
    Tools/n8n/fix-orchestrator-settings.py's docstring and Tools/n8n/apply-dev-config.py
    (dev is DERIVED from canonical by rewriting bagkz -> localhost + remapping
    credential ids, never the reverse) and established in commit d594f17. A raw dev
    re-export corrupted these two files once (Task 8's first pass, caught in review).

    Round 1 only checked for the ABSENCE of dev strings (localhost/trycloudflare) and
    the PRESENCE of "bagkz.app.n8n.cloud" as a substring of 3 node urls. A reviewer's
    negative test showed a copy carrying dev CREDENTIAL ids + active:true +
    settings.binaryMode still passed cleanly — none of those dimensions is a "dev URL
    string", so the round-1 asserts never looked at them. This version pins every
    dimension of that same regression, per file:
      - every credential id on every node that has one (the 3 n8n-API httpRequest
        nodes, the 2 Wappi httpRequest nodes, the 5 Postgres nodes) to its EXACT
        canonical value — not "looks like a URL", not "isn't a known dev id";
      - `active is False` (identity, not just falsy);
      - `settings == {"executionOrder": "v1"}` by exact dict equality, which rules out
        ANY stray key (binaryMode, availableInMCP, or one not yet seen) rather than
        naming binaryMode specifically;
      - a POSITIVE assert that Set Wappi Webhook's callback-url query parameter starts
        with the canonical bagkz webhook host — checking only for the ABSENCE of
        "trycloudflare" would still pass an ngrok-style (or any other non-bagkz)
        tunnel host, since that literal string never appears in one.
    """
    wf = load(f)
    ns = wf["nodes"]
    text = json.dumps(wf)

    assert "localhost:5678" not in text, f"{f}: canonical export contains a dev localhost URL"
    assert "trycloudflare" not in text, f"{f}: canonical export contains a dev tunnel URL"

    for name in API_NODE_NAMES:
        n = node(ns, name)
        url = n["parameters"].get("url", "")
        assert "bagkz.app.n8n.cloud" in url, \
            f"{f}: {name}'s url is not the canonical bagkz host: {url!r}"
        creds = n.get("credentials") or {}
        assert creds.get("n8nApi", {}).get("id") == CANON_N8N_API_CRED, \
            f"{f}: {name}'s n8nApi credential id is not canonical: {creds.get('n8nApi')}"
        assert creds.get("httpHeaderAuth", {}).get("id") == CANON_N8N_APIKEY_CRED, \
            f"{f}: {name}'s httpHeaderAuth (n8nAPIKey) credential id is not canonical: {creds.get('httpHeaderAuth')}"
        assert creds.get("httpBearerAuth", {}).get("id") == CANON_N8N_BEARER_CRED, \
            f"{f}: {name}'s httpBearerAuth credential id is not canonical: {creds.get('httpBearerAuth')}"

    for name in WAPPI_NODE_NAMES:
        creds = node(ns, name).get("credentials") or {}
        assert creds.get("httpHeaderAuth", {}).get("id") == CANON_WAPPI_CRED, \
            f"{f}: {name}'s WappiAuthToken credential id is not canonical: {creds.get('httpHeaderAuth')}"

    for name in POSTGRES_NODE_NAMES:
        creds = node(ns, name).get("credentials") or {}
        assert creds.get("postgres", {}).get("id") == PG_EXECUTEQUERY_CRED, \
            f"{f}: {name}'s postgres credential id is not canonical: {creds.get('postgres')}"

    assert wf["active"] is False, f"{f}: active is not False: {wf['active']!r}"
    assert wf["settings"] == {"executionOrder": "v1"}, \
        f"{f}: settings is not the clean canonical dict (binaryMode/availableInMCP stowaway?): {wf['settings']!r}"

    # Positive assert (not just "no trycloudflare") — an ngrok-style dev tunnel host
    # would pass a purely-negative trycloudflare check.
    swh = node(ns, "Set Wappi Webhook")
    url_param = next(
        (p["value"] for p in swh["parameters"]["queryParameters"]["parameters"] if p["name"] == "url"),
        None,
    )
    assert url_param is not None, f"{f}: Set Wappi Webhook has no 'url' query parameter"
    assert url_param.lstrip("=").startswith("https://bagkz.app.n8n.cloud/webhook/"), \
        f"{f}: Set Wappi Webhook's callback url is not the canonical bagkz webhook host: {url_param!r}"

    print(f"OK  {f} (canonical-export invariant)")


DIALOG_METERING_NODES = ("Count Dialog", "Quota Decision", "If Quota Allows")


def check_dialog_metering(f):
    """Billing Task 9: per-day dialog metering + quota enforcement, spliced after
    Is Latest?'s not-aborted (debounce-settled) branch -- so a debounced duplicate
    fragment can never double-count -- and before the reply-generation chain
    (Input type -> ... -> AI Agent) -- so a quota-blocked NEW dialog never triggers an
    auto-reply. Shared by both bot templates: this file has always been scoped to
    Telegram-PARITY structural asserts (check_telegram_bot() is where TG's own node-set
    is pinned; WhatsApp_Bot never got a mirror check_whatsapp_bot() since WA was the
    already-trusted original TG was built to match), but the billing gate itself ships
    identically to both, so this one function verifies the shape on whichever template
    filename it's given rather than duplicating a second full node-set pin for WA.
    """
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    for name in DIALOG_METERING_NODES:
        node(ns, name)  # raises AssertionError if missing

    # (i) Is Latest?'s NOT-aborted branch (index 1) now feeds Count Dialog, not Input
    # type directly -- debounced duplicates are filtered by Is Latest? BEFORE metering
    # ever runs, so a resend of the same fragment can't count twice.
    is_latest_false = conns["Is Latest?"]["main"][1]
    assert is_latest_false == [{"node": "Count Dialog", "type": "main", "index": 0}], \
        f"{f}: Is Latest?'s not-aborted branch does not feed Count Dialog: {is_latest_false}"

    # (ii) Count Dialog -> Quota Decision -> If Quota Allows, linear.
    assert conns["Count Dialog"]["main"][0] == [{"node": "Quota Decision", "type": "main", "index": 0}], \
        f"{f}: Count Dialog does not feed Quota Decision: {conns['Count Dialog']}"
    assert conns["Quota Decision"]["main"][0] == [{"node": "If Quota Allows", "type": "main", "index": 0}], \
        f"{f}: Quota Decision does not feed If Quota Allows: {conns['Quota Decision']}"

    # (iii) If Quota Allows: TRUE (allowed) -> Input type (the reply-generation chain);
    # FALSE (blocked) -> nothing, matching Suppressed?/Is Latest?'s own dead-end idiom
    # for "do not auto-reply."
    iqa = conns["If Quota Allows"]["main"]
    assert iqa[0] == [{"node": "Input type", "type": "main", "index": 0}], \
        f"{f}: If Quota Allows true-branch does not feed Input type: {iqa[0]}"
    assert iqa[1] == [], f"{f}: If Quota Allows false-branch is not a dead end: {iqa[1]}"

    # (iv) Count Dialog is fail-open: a Supabase outage or an unregistered profile (0
    # rows from the `me` CTE) must never silence the bot -- mirrors Restamp RAG
    # Chunks/Count Channels' established onError+alwaysOutputData contract (Tasks 8/9).
    cd = node(ns, "Count Dialog")
    assert cd["type"] == "n8n-nodes-base.postgres", f"{f}: Count Dialog is not a postgres node"
    assert cd.get("onError") == "continueRegularOutput", f"{f}: Count Dialog onError not continueRegularOutput"
    assert cd.get("alwaysOutputData") is True, f"{f}: Count Dialog alwaysOutputData not true"
    cred = cd["credentials"]["postgres"]["id"]
    assert cred == PG_EXECUTEQUERY_CRED, \
        f"{f}: Count Dialog postgres credential is not the shared executeQuery cred: {cred}"

    # (v) queryReplacement is the array-form ={{ [...] }} expression (Task 7's null-
    # stringification lesson: a comma-joined multi-fragment form turns a null/undefined
    # element into the literal text "null" once interpolated into the surrounding SQL).
    qr = cd["parameters"]["options"]["queryReplacement"]
    assert qr.startswith("={{ [") and qr.rstrip().endswith("] }}"), \
        f"{f}: Count Dialog queryReplacement is not the single array-literal form: {qr!r}"

    # (vi) the insert is conditional on being allowed -- a suppressed NEW dialog must
    # never consume quota (insert-then-suppress would silently burn a slot on every
    # rejected message instead of leaving it unconsumed).
    q = sql_code(cd["parameters"]["query"])
    assert "insert into dialog_counts" in q, f"{f}: Count Dialog query does not insert into dialog_counts"
    assert "where not exists" in q and "used from usage_now" in q, \
        f"{f}: Count Dialog insert does not look conditional on quota/existing: {q}"

    # (vi-b) TOP-UP RESERVE SEMANTICS (Task 17a; owner decision 2026-08-26, spec §2).
    # The top-up used to be ADDED to the monthly quota and never consumed, so one 3900₸
    # purchase raised the quota by 500 every month forever. It is now a reserve: spent one
    # dialog at a time, and only once the BASE quota is gone. Four things make that true,
    # and each is asserted rather than described, because losing any one silently restores
    # the old free-forever behaviour or lets the balance go negative:
    #   1. the quota CTE is the BASE plan number ONLY -- the old `+ case when me.status in
    #      ('active','trialing') then me.topup_balance` term must be GONE. (Get Usage
    #      reports the same base number as `quota` and the column as `topupBalance`.)
    #   2. the decrement is an UPDATE on subscribers, guarded `topup_balance > 0` -- the
    #      row lock it takes is what serialises two racing new chats, and READ COMMITTED
    #      re-evaluates that guard against the freshly committed row, so the loser is
    #      refused instead of driving the balance to -1.
    #   3. it fires ONLY for a new dialog (`not exists (... existing)`) that is already
    #      over quota (`>= (select q from quota)`) -- a continuation or an under-quota
    #      dialog must cost nothing.
    #   4. `allowed` counts a consumed reserve unit: `on conflict do nothing` can swallow
    #      OUR insert when a racer inserted the same key first, and that conflict PROVES
    #      the row exists -- without this term such a race would spend a unit AND refuse.
    assert "then me.topup_balance" not in q, \
        f"{f}: Count Dialog still ADDS topup_balance into the quota -- the top-up is a " \
        f"reserve since 2026-08-26, not a permanent quota bump"
    assert "update subscribers" in q and "topup_balance = s.topup_balance - 1" in q, \
        f"{f}: Count Dialog does not consume the top-up reserve"
    assert "and s.topup_balance > 0" in q, \
        f"{f}: the reserve decrement is not guarded `topup_balance > 0` under the row lock"
    reserve = q[q.index("update subscribers"):q.index("returning s.topup_balance")]
    missing = [c for c in ("not exists (select 1 from existing)", ">= (select q from quota)")
               if c not in reserve]
    assert not missing, \
        f"{f}: the reserve decrement is not scoped to a NEW, over-quota dialog -- missing " \
        f"{missing} from its WHERE (a continuation or an under-quota dialog would burn a unit)"
    assert "in ('active','trialing')" in reserve, \
        f"{f}: the reserve decrement is not gated on a consuming status"
    assert "or exists (select 1 from reserve)" in q, \
        f"{f}: the dialog insert does not accept a reserve-funded dialog"
    assert "exists(select 1 from reserve)) as allowed" in q, \
        f"{f}: `allowed` does not count a consumed reserve unit -- an on-conflict race " \
        f"would spend the unit and still refuse the reply"

    # (vii) PAYLOAD CONTINUITY (fix round, 2026-08-21) -- Count Dialog is a Postgres
    # executeQuery node: its output is JUST the SQL result columns, not a merge with
    # its input, so by the time Quota Decision runs, the incoming message's
    # .body.messages[0]/combinedText/abort are already gone. If Quota Decision's own
    # return value doesn't put them back, Input type (immediately downstream, reading
    # bare $json.body.messages[0].type) fails every Switch rule and falls to the
    # "please send text messages" branch for EVERY allowed (and fail-open!) message --
    # this is exactly the bug a live pinned execution caught (2026-08-21): the original
    # shipped Quota Decision returned a bare {allowed,used,plan,status} object, so
    # Input type's Switch silently mis-routed on every non-blocked message. Quota
    # Decision must therefore (a) rebuild its item from the upstream enriched payload --
    # $('Latest+Combine'), not bare $input or $('Webhook'), because Latest+Combine is
    # what computed combinedText/abort in the first place -- and (b) carry an EXPLICIT
    # pairedItem, matching Latest+Combine's own established rule that every node below
    # the debounce splice needs an unbroken paired-item chain for $('Webhook').item to
    # keep resolving in Mark Read/Typing/Chat Memory/the send HTTP Request.
    qd = node(ns, "Quota Decision")
    jscode = qd["parameters"]["jsCode"]
    assert "Latest+Combine" in jscode, \
        f"{f}: Quota Decision does not reference Latest+Combine -- it will drop the " \
        f"incoming message payload and misroute every allowed/fail-open reply"
    assert "pairedItem" in jscode, \
        f"{f}: Quota Decision does not set an explicit pairedItem"

    print(f"OK  {f} (dialog-metering wiring)")


def check_dialog_metering_shared():
    """The two bot templates must carry the BYTE-IDENTICAL metering SQL and Code.

    This is load-bearing for the WhatsApp template specifically, and worth stating plainly:
    only the Telegram template is `availableInMCP`, so only IT can be exercised with pinned
    n8n-mcp executions (Task 9 / Task 15b / this task all pin TG live). WhatsApp's own gate
    cannot be reached without a real authorized Wappi profile and a real inbound message --
    Fetch Recent hard-aborts on Wappi's 400 for a synthetic profile_id, so the chain
    dead-ends before Count Dialog. The standard of proof for WA is therefore: the SQL and
    Code it runs are the SAME BYTES that were proven live on TG. That argument is only worth
    anything if something CHECKS the bytes -- otherwise a one-template edit drifts silently.
    """
    tg, wa = load(TG_BOT), load(WA_BOT)
    for name, field in (("Count Dialog", "query"), ("Quota Decision", "jsCode")):
        a = node(tg["nodes"], name)["parameters"][field]
        b = node(wa["nodes"], name)["parameters"][field]
        assert a == b, (f"{name}.{field} differs between the Telegram and WhatsApp "
                        f"templates -- only TG can be pinned-tested live, so WA's proof IS "
                        f"byte-equality with it")
    print("OK  both bot templates share byte-identical metering SQL + Code")


def check_model_id(f):
    """Billing Task 13: both bot templates' AI Agent must run on the pinned mini-class
    model (gpt-5.4-mini, live-verified against the real OpenAI model catalog + real API
    calls tagging back gpt-5.4-mini-2026-03-17 -- see task-13-report.md). This asserts
    the OpenAI [lmChatOpenAi] node's model id directly, closing the gap the dialog-
    metering/canonical-export asserts don't cover: a future accidental reversion to an
    older model (or a stray edit landing on the wrong alias) would otherwise slip
    through this gate silently.
    """
    wf = load(f)
    ns = wf["nodes"]
    openai_nodes = [n for n in ns if n.get("type") == "@n8n/n8n-nodes-langchain.lmChatOpenAi"]
    assert len(openai_nodes) == 1, \
        f"{f}: expected exactly 1 lmChatOpenAi node, found {len(openai_nodes)}"
    model_value = openai_nodes[0]["parameters"]["model"]["value"]
    assert model_value == "gpt-5.4-mini", \
        f"{f}: OpenAI node model id is {model_value!r}, expected 'gpt-5.4-mini'"
    print(f"OK  {f} (model id pinned)")


def check_suggest_replies():
    f = SUGGEST
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    def filter_keys(name):
        n = node(ns, name)
        mv = n["parameters"]["options"]["metadata"]["metadataValues"]
        return [m["name"] for m in mv]

    # (i)+(ii) single-key filters, one per channel, never ORed in one node.
    tg_keys = filter_keys("Retrieve RAG TG")
    wa_keys = filter_keys("Retrieve RAG")
    assert tg_keys == ["botTgId"], f"{f}: Retrieve RAG TG filter not single botTgId: {tg_keys}"
    assert wa_keys == ["botWaId"], f"{f}: Retrieve RAG filter not single botWaId: {wa_keys}"

    # (ii-b) the new vector store MUST have its embeddings input (a vector-store node
    # without ai_embedding hard-fails at runtime; the n8n UI round-trip can drop it).
    emb_targets = {c["node"] for c in conns["Embeddings"]["ai_embedding"][0]}
    assert {"Retrieve RAG", "Retrieve RAG TG"} <= emb_targets, \
        f"{f}: Embeddings ai_embedding targets missing a Retrieve node: {emb_targets}"

    # (ii-c) both retrieve nodes feed Assemble (a dropped main connection dead-ends the path).
    for retr in ("Retrieve RAG TG", "Retrieve RAG"):
        nxt = conns[retr]["main"][0][0]["node"]
        assert nxt == "Assemble", f"{f}: {retr} -> {nxt}, expected Assemble"

    # (iii) channel branch on the RAG path.
    ictg = node(ns, "If channel TG?")
    assert ictg["type"] == "n8n-nodes-base.if", f"{f}: If channel TG? is not an If node"
    assert conns["If skipRag?"]["main"][1][0]["node"] == "If channel TG?", \
        f"{f}: If skipRag? false-branch does not route to If channel TG?"
    tg_true = conns["If channel TG?"]["main"][0][0]["node"]
    tg_false = conns["If channel TG?"]["main"][1][0]["node"]
    assert tg_true == "Retrieve RAG TG", f"{f}: If channel TG? true-branch -> {tg_true}, expected Retrieve RAG TG"
    assert tg_false == "Retrieve RAG", f"{f}: If channel TG? false-branch -> {tg_false}, expected Retrieve RAG"

    # (iv) Prep jsCode references channel + botTgId.
    prep = node(ns, "Prep")["parameters"]["jsCode"]
    assert "channel" in prep, f"{f}: Prep jsCode does not reference channel"
    assert "botTgId" in prep, f"{f}: Prep jsCode does not reference botTgId"

    # (v) Assemble copy no longer WhatsApp-specific.
    assemble = node(ns, "Assemble")["parameters"]["jsCode"]
    assert "со своего WhatsApp" not in assemble, f"{f}: Assemble still says «со своего WhatsApp»"

    # (vi) D10 relevance anchor (08-13, commit fa2ac8c): the shared Assemble prompt must
    # pin all 4 cards to the newest incoming client message and surface it inside the
    # fenced data block as lastClientMessage. Any deploy path that regenerates Assemble
    # from a pre-08-13 source drops this silently — asserting it here makes both runbook
    # gates (step-1 committed pre-flight, step-7 prod re-export) catch a revert.
    assert "РЕЛЕВАНТНОСТЬ (ГЛАВНОЕ)" in assemble, \
        f"{f}: Assemble prompt missing the D10 «РЕЛЕВАНТНОСТЬ (ГЛАВНОЕ)» directive"
    assert "lastClientMessage" in assemble, \
        f"{f}: Assemble fenced block missing the D10 lastClientMessage anchor"

    # (vii) SUBSCRIPTION GATE + DAILY CAP (Task 17a; owner decision 2026-08-26, spec §5.3).
    # «Вместе» suggestions are FREE (they never consume a dialog) but they are NOT free to
    # serve: /webhook/SuggestReplies is unauthenticated and every call spends LLM tokens.
    # Before this task an EXPIRED account -- the very population the quota enforcement
    # routes INTO the panel -- had unlimited free generations. The gate refuses expired and
    # unknown ids, deliberately ALLOWS over-quota (the panel IS the fallback), and caps
    # requests per account per day.
    for name in ("Suggestion Gate", "Gate Decision", "If Gate Allows"):
        node(ns, name)

    # (vii-a) POSITION IS THE WHOLE POINT: the gate hangs off If invalid?'s valid branch,
    # so a refusal costs ZERO LLM and never reaches retrieval. If this edge ever points at
    # If skipRag? again, refused traffic silently starts paying for itself.
    assert conns["If invalid?"]["main"][1] == \
        [{"node": "Suggestion Gate", "type": "main", "index": 0}], \
        f"{f}: If invalid?'s valid branch does not enter the subscription gate"
    assert conns["Suggestion Gate"]["main"][0] == \
        [{"node": "Gate Decision", "type": "main", "index": 0}], f"{f}: gate chain broken"
    assert conns["Gate Decision"]["main"][0] == \
        [{"node": "If Gate Allows", "type": "main", "index": 0}], f"{f}: gate chain broken"
    ga = conns["If Gate Allows"]["main"]
    assert ga[0] == [{"node": "If skipRag?", "type": "main", "index": 0}], \
        f"{f}: If Gate Allows true-branch does not resume the normal path: {ga[0]}"
    assert ga[1] == [{"node": "Build Response", "type": "main", "index": 0}], \
        f"{f}: If Gate Allows false-branch must go straight to Build Response (the " \
        f"existing generation_failed envelope), reaching no LLM node: {ga[1]}"

    # (vii-b) fail-open contract, identical to Count Dialog's: a Supabase outage must never
    # kill the panel, so onError + alwaysOutputData, and the shared executeQuery credential.
    gate = node(ns, "Suggestion Gate")
    assert gate["type"] == "n8n-nodes-base.postgres", f"{f}: Suggestion Gate is not postgres"
    assert gate.get("onError") == "continueRegularOutput", \
        f"{f}: Suggestion Gate onError not continueRegularOutput"
    assert gate.get("alwaysOutputData") is True, \
        f"{f}: Suggestion Gate alwaysOutputData not true"
    assert gate["credentials"]["postgres"]["id"] == PG_EXECUTEQUERY_CRED, \
        f"{f}: Suggestion Gate uses the wrong postgres credential"
    qr = gate["parameters"]["options"]["queryReplacement"]
    assert qr.startswith("={{ [") and qr.rstrip().endswith("] }}"), \
        f"{f}: Suggestion Gate queryReplacement is not the single array-literal form: {qr!r}"
    assert re.search(r"\bappUserId\b", qr), \
        f"{f}: Suggestion Gate is not keyed by appUserId: {qr!r}"

    # (vii-c) the SQL itself: values, not node presence. Suggestions must stay free
    # (dialog_counts is never touched), the cap must be a real per-day counter, and `grace`
    # must be allowed alongside active/trialing -- refusing grace would cut the panel off
    # from exactly the owners who are being asked to pay.
    gq = sql_code(gate["parameters"]["query"])
    assert "dialog_counts" not in gq, \
        f"{f}: the suggestions gate touches dialog_counts -- suggestions are FREE " \
        f"(owner decision 2026-08-26); they must never consume a dialog"
    assert "insert into suggestion_counts" in gq and "n = suggestion_counts.n + 1" in gq, \
        f"{f}: the gate does not increment the daily counter atomically"
    assert "in ('active','trialing','grace')" in gq, \
        f"{f}: the gate's entitled-status set is not exactly active/trialing/grace"
    assert "<= 100" in gq, f"{f}: the gate carries no daily cap"
    assert "Asia/Almaty" in gq, \
        f"{f}: the gate's day boundary is not the Asia/Almaty one dialog_counts uses"

    # (vii-d) payload continuity, the Task 9 trap again: a Postgres node emits ONLY its
    # query result and DROPS the incoming item, so Gate Decision must rebuild from Prep or
    # everything downstream (If skipRag?/Assemble) reads undefined.
    gd = js_code(node(ns, "Gate Decision")["parameters"]["jsCode"])
    assert "$('Prep')" in gd, \
        f"{f}: Gate Decision does not rebuild the payload from Prep -- the Postgres node " \
        f"above it drops the incoming item"
    assert "pairedItem" in gd, f"{f}: Gate Decision does not set an explicit pairedItem"
    assert "row.allowed === undefined" in gd, \
        f"{f}: Gate Decision does not fail open on a DB error"

    # (vii-e) the client contract is UNCHANGED: a refusal reuses the existing
    # generation_failed envelope (the panel has no new state for this), with `reason` as an
    # extra diagnostic key only.
    # \b on both sides: a rename to appUserIdX would satisfy a bare substring test.
    assert re.search(r"\bappUserId\b", prep), \
        f"{f}: Prep does not carry appUserId -- the gate would refuse every request"
    br = js_code(node(ns, "Build Response")["parameters"]["jsCode"])
    assert "generation_failed" in br, f"{f}: Build Response lost the error envelope"
    # ... and the reason must NOT ride the wire (review N-4, 2026-08-26). This endpoint is
    # unauthenticated, so `unknown_account` vs `subscription_expired` vs `daily_cap` would
    # tell anyone who guessed an app_user_id whether that account exists and what state it is
    # in. The reason lives on Gate Decision's output, where the execution log keeps it for
    # debugging and the probe asserts it; the client never read it (SuggestRepliesResponse
    # has no such field). An `out.reason = ...` reappearing here is the regression to catch.
    assert "gateReason" not in br, \
        f"{f}: Build Response puts the refusal reason on the wire -- an unauthenticated " \
        f"endpoint must not confirm whether a guessed app_user_id exists or its state"
    assert "gateReason" in gd, \
        f"{f}: Gate Decision no longer records a reason -- a refusal would be undebuggable"

    print(f"OK  {f}")


def sql_code(query):
    """SQL with `--` comments stripped, line-wise. The RC queries carry long RU comment
    blocks that name the very identifiers these asserts hunt for -- a raw substring test
    would pass (or fail) on prose. No string literal in these queries contains `--`."""
    return "\n".join(line.split("--", 1)[0] for line in query.splitlines())


def check_rc_transfer_carry_gate():
    """Task 17a fix round (review N-1): Transfer Subscriber's `carried` CTE must move
    dialog_counts ONLY for destinations whose snapshot the statement itself ACCEPTED
    (the `accepted` CTE, derived from moved's RETURNING) -- never straight off `to_ids`.
    Ungated, a refused snapshot's used-rows land on a live trial and exhaust it instantly
    (200 carried onto a 150 quota, reproduced live pre-fix). The probe's T3 catches this
    only against a live instance; this assert is the committed, offline guard.
    The ALIAS path (Consolidate Aliases) is DELIBERATELY ungated -- same human, no
    snapshot at all -- so this check is scoped to Transfer Subscriber alone."""
    wf = load(RC_EVENTS)
    q = sql_code(node(wf["nodes"], "Transfer Subscriber")["parameters"]["query"])
    assert re.search(r"\baccepted as \(", q), \
        f"{RC_EVENTS}: Transfer Subscriber lost the `accepted` CTE -- the usage carry " \
        f"has nothing to gate on"
    m = re.search(r"\bcarried as \((.*?)\n\)", q, re.DOTALL)
    assert m, f"{RC_EVENTS}: Transfer Subscriber lost the `carried` CTE"
    carried = m.group(1)
    assert re.search(r"\bfrom accepted\b", carried), \
        f"{RC_EVENTS}: `carried` no longer drives off `accepted` -- usage would move even " \
        f"when the snapshot was refused"
    assert not re.search(r"\bto_ids\b", carried), \
        f"{RC_EVENTS}: `carried` reads `to_ids` directly -- that is the exact pre-fix " \
        f"defect (review N-1): a refused snapshot's dialog_counts dumped onto the destination"
    print(f"OK  {RC_EVENTS} (usage carry gated on snapshot acceptance)")


REFUSED_DELETE_NODE = "Delete Refused Profile"


def check_refused_create_deletes_profile(f, wappi_base, profile_field):
    """Task 19 (live incident 2026-08-26 15:12): a Create refused for lack of a channel slot
    must DELETE the profile the owner just authorized, before answering `channel_limit`.

    The refusal lands AFTER Wappi pairing -- the client only calls this webhook once the
    channel is authorized -- so a bare refusal strands a real, AUTHORIZED profile at 23₽/day
    that NO sweep can reap: the hourly orphan sweep only deletes UNAUTHORIZED profiles, and
    Profile Lifecycle Sweep drives off `bot_profiles`, where the refusal branch (correctly)
    never wrote a row. On 2026-08-26 the owner's manual in-app bot delete is what cleaned it;
    a real user would not know to do that.

    Every assert below is a way that fix can silently rot:
      - the node moved to the ALLOWED branch  => deletes the profile it just accepted;
      - the node stopped feeding Respond Channel Limit => the webhook's `lastNode` response
        becomes the Wappi delete's own body (the client keys on {success,error});
      - onError/alwaysOutputData dropped      => a Wappi hiccup turns a clean refusal into an
        n8n execution error, i.e. the client's channel_limit handling never runs;
      - the url lost its channel base or its `.first()` binding => deletes nothing, or reads
        an item that does not exist on this branch.
    """
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    n = node(ns, REFUSED_DELETE_NODE)
    # Task 19's own trap, turned into a structural assert (review minor, 2026-08-27): a
    # DISABLED n8n node still appears in runData passing its input through, so every other
    # assert here (type/url/wiring) passes while the delete never fires. Presence is not proof.
    assert not n.get("disabled"), \
        f"{f}: {REFUSED_DELETE_NODE} is disabled -- it would pass input through in runData " \
        f"while never calling Wappi, and the refused profile strands again"
    assert n["type"] == "n8n-nodes-base.httpRequest", \
        f"{f}: {REFUSED_DELETE_NODE} is not an httpRequest node: {n['type']}"
    assert n["parameters"].get("method") == "POST", \
        f"{f}: {REFUSED_DELETE_NODE} is not a POST: {n['parameters'].get('method')}"

    expected_url = ("=" + wappi_base + "/profile/delete?profile_id="
                    "{{ $('Unity Webhook').first().json.body." + profile_field + " }}")
    url = n["parameters"].get("url")
    assert url == expected_url, \
        f"{f}: {REFUSED_DELETE_NODE} url wrong (channel base / profile field / binding):\n" \
        f"  got:      {url!r}\n  expected: {expected_url!r}"

    # Best-effort, by construction: a failed delete must never change the refusal response.
    assert n.get("onError") == "continueRegularOutput", \
        f"{f}: {REFUSED_DELETE_NODE} onError is {n.get('onError')!r}, not continueRegularOutput " \
        f"-- a Wappi error would abort the execution and the client would never see channel_limit"
    assert n.get("alwaysOutputData") is True, \
        f"{f}: {REFUSED_DELETE_NODE} alwaysOutputData not true -- an empty output would leave " \
        f"Respond Channel Limit unexecuted, and `lastNode` would answer with nothing"

    # Wiring: If Slot Limit[TRUE = over limit] -> delete -> Respond Channel Limit (terminal).
    over_limit = [c["node"] for c in conns["If Slot Limit"]["main"][0]]
    assert over_limit == [REFUSED_DELETE_NODE], \
        f"{f}: If Slot Limit's over-limit branch goes to {over_limit}, expected [{REFUSED_DELETE_NODE!r}]"
    nxt = [c["node"] for c in conns[REFUSED_DELETE_NODE]["main"][0]]
    assert nxt == ["Respond Channel Limit"], \
        f"{f}: {REFUSED_DELETE_NODE} -> {nxt}, expected ['Respond Channel Limit']"
    assert "Respond Channel Limit" not in conns, \
        f"{f}: Respond Channel Limit gained an outgoing connection (it must stay the terminal " \
        f"`lastNode` response)"

    # …and NOT on the allowed branch, in any position.
    under_limit = [c["node"] for c in conns["If Slot Limit"]["main"][1]]
    assert REFUSED_DELETE_NODE not in under_limit, \
        f"{f}: {REFUSED_DELETE_NODE} sits on the ALLOWED branch -- that deletes the profile the " \
        f"gate just accepted"
    for src, wiring in conns.items():
        if src in ("If Slot Limit", REFUSED_DELETE_NODE):
            continue
        for branch in wiring.get("main", []):
            targets = [c["node"] for c in branch]
            assert REFUSED_DELETE_NODE not in targets, \
                f"{f}: {REFUSED_DELETE_NODE} is also reachable from {src!r} -- the delete must " \
                f"hang off the refusal branch ALONE"

    # The refusal body itself is untouched: the client keys on exactly these two fields, and a
    # Set node that started merging its input would let the Wappi delete's payload ride along.
    resp = node(ns, "Respond Channel Limit")
    assert resp["type"] == "n8n-nodes-base.set", f"{f}: Respond Channel Limit is not a set node"
    assigned = {a["name"]: a["value"] for a in resp["parameters"]["assignments"]["assignments"]}
    assert assigned == {"success": False, "error": "channel_limit"}, \
        f"{f}: refusal response changed: {assigned!r} (the client keys on success/channel_limit)"
    assert resp["parameters"].get("includeOtherFields") is not True, \
        f"{f}: Respond Channel Limit now merges its input -- the delete's Wappi payload would " \
        f"leak into the refusal body"

    print(f"OK  {f} (refused create deletes the stranded profile)")


def main():
    global WF
    ap = argparse.ArgumentParser(
        description="Structural-assert verifier for the Telegram-parity workflow edits."
    )
    ap.add_argument(
        "--dir",
        default=DEFAULT_WF,
        help="workflow directory to verify (default: the committed workflows/ next to this "
             "script). Point at a prod re-export dir to run the parity asserts as a "
             "post-import go/no-go.",
    )
    args = ap.parse_args()
    WF = args.dir
    try:
        check_telegram_bot()
        check_restamp_orchestrator(CREATE_TG, "{botTgId}", "WhatsappWorkflowId")
        check_restamp_orchestrator(CREATE_WA, "{botWaId}", "TelegramWorkflowId")
        check_canonical_export_invariant(CREATE_WA)
        check_canonical_export_invariant(CREATE_TG)
        check_refused_create_deletes_profile(CREATE_WA, "https://wappi.pro/api", "WhatsappProfileId")
        check_refused_create_deletes_profile(CREATE_TG, "https://wappi.pro/tapi", "TelegramProfileId")
        check_dialog_metering(TG_BOT)
        check_dialog_metering(WA_BOT)
        check_dialog_metering_shared()
        check_model_id(TG_BOT)
        check_model_id(WA_BOT)
        check_suggest_replies()
        check_rc_transfer_carry_gate()
    except AssertionError as e:
        print(f"PARITY FAIL: {e}")
        sys.exit(1)
    except (OSError, KeyError, IndexError, json.JSONDecodeError) as e:
        print(f"PARITY FAIL: unexpected structural error: {e}")
        sys.exit(1)
    print("ALL PARITY ASSERTS PASSED")


if __name__ == "__main__":
    main()
