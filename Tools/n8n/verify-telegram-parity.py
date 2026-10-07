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

Every check runs even when an earlier one fails (one red check must not hide the rest).
Exits 0 and prints "ALL PARITY ASSERTS PASSED" when every assert holds.
Exits 1 with one "PARITY FAIL: <reason>" line per failed check, then a "PARITY FAILED: n of m" summary.

  python3 Tools/n8n/verify-telegram-parity.py --self-test   # [--dir DIR]

--self-test copies the workflow directory to a temp dir, proves the unmodified copy passes every check, then
applies one bypass of the channel-limit refusal at a time to Create WA and to Create TG (Delete -> Respond with
the retire skipped, the retire continuing into workflow creation, its WHERE without `workflow_id is null`, …)
and requires each to fail a check naming that file. Exit 0 only when every case holds.
"""
import argparse
import contextlib
import io
import json
import os
import re
import shutil
import sys
import tempfile

# Resolve workflow paths from this script's own location so cwd does not matter.
# WF defaults to the committed workflows/ dir; main() reassigns it from --dir so the same
# asserts can gate a prod re-export. load() reads whatever WF points at.
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WF = os.path.join(HERE, "workflows")
WF = DEFAULT_WF

# Canonical server host. Since the 2026-08-27 migration the canonical JSONs carry the
# self-hosted prod instance; the old n8n Cloud workspace (bagkz.app.n8n.cloud) is DELETED, so
# its host must be ABSENT from every canonical export (a stale re-export would resurrect it).
# The verifier pins the exact host on purpose: "any https URL" would pass an ngrok/other tunnel.
CANON_HOST = "https://n8n.choosereply.com"
LEGACY_DELETED_HOST = "bagkz.app.n8n.cloud"

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
# Quota Decision, If Quota Allows); audit 2026-09 stage 0.5 +1 (Outcome, appended LAST so the
# nodes[0]/nodes[5] orchestrator indices hold). A future splice must extend this set deliberately.
TG_BOT_NODE_NAMES = {
    "Webhook", "HTTP Request", "Transcribe Audio", "Text", "Audio", "AI Agent",
    "Input type", "Mark Read", "Typing", "Reading Pause", "Typing Pause",
    "Pause Before Reading", "Ask to Send Text", "Input type2", "Count Output Words",
    "Count Input Words", "Download Audio", "Listening Pause", "Chat Memory", "If",
    "Read Reply Mode", "Suppressed?",
    "OpenAI", "Supabase Vector Store", "Retrieve Answer", "OpenAI Embedding",
    "Debounce Wait", "Fetch Recent", "Latest+Combine", "Is Latest?",
    "Count Dialog", "Quota Decision", "If Quota Allows",
    "Outcome",   # audit stage 0.5 terminal outcome stamp (apply-outcome-stamps.py)
    # audit stage 3 (apply-reply-pipeline-v2.py): claim, generation guard, final check + lease + settle,
    # reconcile, records — +16
    "Claim Event", "Claimed?", "Generated?", "Final Fetch", "Final Check", "Final OK?", "Acquire Send Lease",
    "Send Allowed?", "Settle Send", "Send Unknown?", "Wait Reconcile", "Reconcile Fetch", "Reconcile Check",
    "Record Send", "Cancel Label", "Record Cancel",
    # audit stage 4 (apply-history-handoff.py): event routing, outgoing/suppressed recording, the handoff tool — +6
    "Is Incoming?", "Is Outgoing?", "Outgoing Bound?", "Record Outgoing", "Record Suppressed", "Create Handoff",
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

    # (iv) wiring: Set Wappi Webhook Types -> Restamp RAG Chunks -> Bind Workflow Id (A1, 2026-09-17:
    #      binds the created clone id + owning install to the profile row) -> Send New Workflows Id (terminal).
    swwt = conns["Set Wappi Webhook Types"]["main"][0][0]["node"]
    assert swwt == "Restamp RAG Chunks", f"{f}: Set Wappi Webhook Types -> {swwt}, expected Restamp RAG Chunks"
    nxt = conns["Restamp RAG Chunks"]["main"][0][0]["node"]
    assert nxt == "Bind Workflow Id", f"{f}: Restamp RAG Chunks -> {nxt}, expected Bind Workflow Id"
    bind = node(ns, "Bind Workflow Id")
    assert bind["type"] == "n8n-nodes-base.postgres" and bind["credentials"]["postgres"]["id"] == PG_EXECUTEQUERY_CRED, \
        f"{f}: Bind Workflow Id must be a postgres node on the executeQuery cred"
    assert "workflow_id = excluded.workflow_id" in bind["parameters"]["query"] and "$1 <> '-1'" in bind["parameters"]["query"], \
        f"{f}: Bind Workflow Id SQL must set workflow_id and guard the -1 sentinel"
    nxt2 = conns["Bind Workflow Id"]["main"][0][0]["node"]
    assert nxt2 == "Send New Workflows Id", f"{f}: Bind Workflow Id -> {nxt2}, expected Send New Workflows Id"
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

# R8-deletefail (2026-09-28, apply-reply-mode-v2.py): `Delete Unseeded Workflow` is RETIRED — the seed gate now stands
# before the clone is created, so a refused create has no clone to delete (verify-reply-mode.py pins the topology).
API_NODE_NAMES = ("Get Sample Workflow", "Create Workflow", "Activate Created Workflow")
WAPPI_NODE_NAMES = ("Set Wappi Webhook", "Set Wappi Webhook Types", "Delete Refused Profile")
POSTGRES_NODE_NAMES = (
    "Restamp RAG Chunks", "Ensure Subscriber", "Count Channels",
    "Retire Same Bot Slot", "Register Profile",
    "Retire Refused Profile",   # 2026-09-24 slot-count hotfix (apply-slot-count-fix.py)
    "Seed Reply Mode",          # A2 seed (apply-reply-mode-v2.py)
)


def check_canonical_export_invariant(f):
    """Task 8 fix-round regression guard (hardened in review round 2): the canonical
    export must NEVER absorb dev-only values — the standing invariant documented in
    Tools/n8n/fix-orchestrator-settings.py's docstring and Tools/n8n/apply-dev-config.py
    (dev is DERIVED from canonical by rewriting the canonical host -> localhost + remapping
    credential ids, never the reverse) and established in commit d594f17. A raw dev
    re-export corrupted these two files once (Task 8's first pass, caught in review).

    Round 1 only checked for the ABSENCE of dev strings (localhost/trycloudflare) and
    the PRESENCE of the canonical host as a substring of 3 node urls. A reviewer's
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
        with the canonical webhook host (CANON_HOST) — checking only for the ABSENCE of
        "trycloudflare" would still pass an ngrok-style (or any other foreign) tunnel
        host, since that literal string never appears in one;
      - the DELETED legacy Cloud host must be absent (2026-09-17: the verifier itself had
        gone stale on the old host after the prod migration and failed every run).
    """
    wf = load(f)
    ns = wf["nodes"]
    text = json.dumps(wf)

    assert "localhost:5678" not in text, f"{f}: canonical export contains a dev localhost URL"
    assert "trycloudflare" not in text, f"{f}: canonical export contains a dev tunnel URL"
    assert LEGACY_DELETED_HOST not in text, \
        f"{f}: canonical export still carries the DELETED legacy Cloud host {LEGACY_DELETED_HOST}"

    for name in API_NODE_NAMES:
        n = node(ns, name)
        url = n["parameters"].get("url", "")
        # n8n stores an expression-valued url with a leading "=" — strip it before the prefix test.
        assert url.lstrip("=").startswith(CANON_HOST + "/api/v1/"), \
            f"{f}: {name}'s url is not the canonical host's REST API ({CANON_HOST}/api/v1/): {url!r}"
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
    assert url_param.lstrip("=").startswith(CANON_HOST + "/webhook/"), \
        f"{f}: Set Wappi Webhook's callback url is not the canonical webhook host ({CANON_HOST}/webhook/): {url_param!r}"

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
    # FALSE (blocked) -> ONLY the Outcome stamp (stage 0.5), matching Suppressed?/Is Latest?'s
    # idiom for "do not auto-reply" — nothing on that branch may generate or send.
    iqa = conns["If Quota Allows"]["main"]
    assert iqa[0] == [{"node": "Input type", "type": "main", "index": 0}], \
        f"{f}: If Quota Allows true-branch does not feed Input type: {iqa[0]}"
    assert iqa[1] == [{"node": "Cancel Label", "type": "main", "index": 0}], \
        f"{f}: If Quota Allows false-branch must be recorded via Cancel Label (stage 3): {iqa[1]}"

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

    # (ii-c) both retrieve nodes feed the exact-match hop (stage 5) which feeds Assemble on BOTH branches
    # (a dropped main connection dead-ends the path).
    hop = conns["Has Exact Tokens?"]["main"]
    assert hop[0][0]["node"] == "Exact Match" and hop[1][0]["node"] == "Assemble" and conns["Exact Match"]["main"][0][0]["node"] == "Assemble", \
        f"{f}: Has Exact Tokens? / Exact Match must both end at Assemble: {hop}"
    for retr in ("Retrieve RAG TG", "Retrieve RAG"):
        nxt = conns[retr]["main"][0][0]["node"]
        assert nxt == "Has Exact Tokens?", f"{f}: {retr} -> {nxt}, expected Has Exact Tokens? (stage 5 exact-match hop)"

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
    # Stage 5 (S8): the refusal KIND may ride the wire ONLY for an authenticated caller (A1 session
    # token — the account is proven, so the N-4 oracle concern does not apply); a legacy caller keeps
    # the opaque envelope. Both halves are asserted.
    assert "if (authMode === 'session') {" in br and "if (j.invalid) error = 'bad_request';" in br and "else if (j.gateReason) {" in br, \
        f"{f}: Build Response must gate EVERY named refusal (bad_request included) on an authenticated session"
    assert "let error = 'generation_failed';" in br and "authMode = 'legacy'" in br, \
        f"{f}: Build Response must keep the opaque generation_failed default for legacy callers"
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
REFUSED_RETIRE_NODE = "Retire Refused Profile"
REFUSAL_RESPONSE_NODE = "Respond Channel Limit"
# The over-limit branch, in order (Task 19 + the 2026-09-24 slot-count hotfix, apply-slot-count-fix.py). Nothing
# else may be reachable from If Slot Limit[TRUE], and none of it from the allowed branch.
REFUSAL_CHAIN = (REFUSED_DELETE_NODE, REFUSED_RETIRE_NODE, REFUSAL_RESPONSE_NODE)


def _main_targets(conns, src):
    """Every main output of `src` as a list of target names ([] when it has no outgoing connection)."""
    return [[c["node"] for c in branch] for branch in (conns.get(src) or {}).get("main", [])]


def _reachable(conns, start_nodes):
    seen, stack = set(), list(start_nodes)
    while stack:
        n = stack.pop()
        if n in seen:
            continue
        seen.add(n)
        for branch in _main_targets(conns, n):
            stack.extend(branch)
    return seen


def _pg_statements(query):
    """The statements of `query` with comments, string literals, quoted identifiers and dollar-quoted bodies
    blanked — a Python mirror of n8n's own stripStringsAndComments + splitStatements
    (n8n-nodes-base 2.36, nodes/Postgres/v2/helpers/utils.js)."""
    out, i, n = [], 0, len(query)
    while i < n:
        c, nxt = query[i], query[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "*":
            end = query.find("*/", i + 2)
            i = n if end == -1 else end + 2
            out.append(" ")
        elif c == "-" and nxt == "-":
            end = query.find("\n", i + 2)
            i = n if end == -1 else end
            out.append(" ")
        elif c in ("'", '"'):
            j = i + 1
            while j < n:
                if query[j] == c and j + 1 < n and query[j + 1] == c:
                    j += 2
                    continue
                if query[j] == c:
                    break
                j += 1
            i = j + 1
            out.append(c + c)
        elif c == "$" and re.match(r"\$[A-Za-z_][A-Za-z0-9_]*\$|\$\$", query[i:]):
            tag = re.match(r"\$[A-Za-z_][A-Za-z0-9_]*\$|\$\$", query[i:]).group(0)
            end = query.find(tag, i + len(tag))
            i = n if end == -1 else end + len(tag)
            out.append(" ")
        else:
            out.append(c)
            i += 1
    return [s.strip() for s in "".join(out).split(";") if s.strip()]


def _top_level_command(statement):
    """SELECT/INSERT/UPDATE/DELETE/MERGE outside any parentheses (n8n's findTopLevelCommand):
    `with x as (update …) select …` is a SELECT; `with x as (…) update …` is an UPDATE."""
    depth = 0
    for tok in re.finditer(r"[()]|[A-Za-z_][A-Za-z0-9_]*", statement):
        t = tok.group(0)
        if t == "(":
            depth += 1
        elif t == ")":
            depth = max(0, depth - 1)
        elif depth == 0 and t.upper() in ("SELECT", "INSERT", "UPDATE", "DELETE", "MERGE"):
            return t.upper()
    return None


def _emits_on_zero_rows(pg_node):
    """Does this executeQuery node hand the next node an item when its statement touches NO row? n8n: always with
    alwaysOutputData; below typeVersion 2.3 always ({success:true}); from 2.3 only when not every statement is a
    top-level SELECT (utils.js configureQueryRunner / isSelectQuery). A refusal whose retire matched nothing (the
    common case: no A1 row for the profile) must still reach Respond Channel Limit."""
    if pg_node.get("alwaysOutputData") is True:
        return True
    if float(pg_node.get("typeVersion") or 0) < 2.3:
        return True
    stmts = _pg_statements(pg_node["parameters"].get("query", ""))
    return bool(stmts) and not all(_top_level_command(s) == "SELECT" for s in stmts)


def check_refused_create_deletes_profile(f, wappi_base, profile_field):
    """Task 19 (live incident 2026-08-26 15:12) + the slot-count hotfix (2026-09-24, apply-slot-count-fix.py).

    A Create refused for lack of a channel slot must (1) DELETE the profile the owner just authorized at Wappi,
    (2) RETIRE its bot_profiles row (the proxy's Bind Profile writes it at profile CREATION since A1), then
    (3) answer `channel_limit` — and must never continue into workflow creation. Chain:
    If Slot Limit[TRUE] -> Delete Refused Profile -> Retire Refused Profile -> Respond Channel Limit (terminal).

    Why each half: without (1) a real, AUTHORIZED profile strands at 23₽/day (the orphan sweep only reaps
    UNAUTHORIZED ones); without (2) the refused row keeps counting as an occupied channel — the owner's `c` grew
    1 -> 2 -> 3 on 2026-09-24 and a legit channel is refused even after a slot frees up. Every assert below is a way
    the chain can rot into a leak or a LIMIT BYPASS: the over-limit branch (or anything after it) reaching workflow
    creation; the retire hanging off the ALLOWED branch; the retire's WHERE widened past the refused, unbound row
    (owner-keyed, or without `workflow_id is null`, it frees a LIVE bot's slot whenever the Wappi delete failed);
    the retire disabled, fail-closed, or emitting no item (then `lastNode` answers with nothing).
    """
    wf = load(f)
    ns = wf["nodes"]
    conns = wf["connections"]

    def need(name):   # node() with the FILE in the message: a missing retire must say which Create lost it
        found = [x for x in ns if x.get("name") == name]
        assert found, f"{f}: node '{name}' not found -- the refusal chain {' -> '.join(REFUSAL_CHAIN)} is incomplete"
        return found[0]

    # --- (1) Delete Refused Profile (Task 19 contract, unchanged) --------------------------------------------
    n = need(REFUSED_DELETE_NODE)
    # Task 19's own trap, turned into a structural assert (review minor, 2026-08-27): a DISABLED n8n node still
    # appears in runData passing its input through, so every other assert here passes while the delete never fires.
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
        f"{f}: {REFUSED_DELETE_NODE} alwaysOutputData not true -- an empty output would leave the rest of " \
        f"the refusal chain unexecuted, and `lastNode` would answer with nothing"

    # --- (2) Retire Refused Profile: best-effort, always emits, scoped to the refused UNBOUND row ----------------
    r = need(REFUSED_RETIRE_NODE)
    assert not r.get("disabled"), \
        f"{f}: {REFUSED_RETIRE_NODE} is disabled -- the refused profile's row keeps counting as a channel"
    assert r["type"] == "n8n-nodes-base.postgres", f"{f}: {REFUSED_RETIRE_NODE} is not a postgres node: {r['type']}"
    assert r["parameters"].get("operation") == "executeQuery", \
        f"{f}: {REFUSED_RETIRE_NODE} is not executeQuery: {r['parameters'].get('operation')!r}"
    assert r.get("onError") == "continueRegularOutput", \
        f"{f}: {REFUSED_RETIRE_NODE} onError is {r.get('onError')!r}, not continueRegularOutput -- a Postgres " \
        f"error would abort the execution (or fork an error output) and the client would never see channel_limit"
    assert _emits_on_zero_rows(r), \
        f"{f}: {REFUSED_RETIRE_NODE} can emit NO item on a zero-row result (typeVersion {r.get('typeVersion')}, " \
        f"alwaysOutputData {r.get('alwaysOutputData')!r}, top-level SELECT) -- {REFUSAL_RESPONSE_NODE} would not run"
    stmts = _pg_statements(r["parameters"].get("query", ""))
    assert len(stmts) == 1, f"{f}: {REFUSED_RETIRE_NODE} must be ONE statement, got {len(stmts)}"
    q = re.sub(r"\s+", " ", stmts[0].lower()).strip()
    raw = re.sub(r"\s+", " ", sql_code(r["parameters"]["query"]).lower()).strip().rstrip(";").strip()
    assert q.startswith("update bot_profiles set ") and " where " in raw, \
        f"{f}: {REFUSED_RETIRE_NODE} is no longer `update bot_profiles set … where …`: {raw!r}"
    set_part, where = raw.split(" where ", 1)
    for must in ("profile_id = nullif($1,'')", "deleted_at is null", "workflow_id is null"):
        assert must in where, \
            f"{f}: {REFUSED_RETIRE_NODE} WHERE lost `{must}` -- it could retire a row other than the refused, " \
            f"unbound one (a LIVE bot's slot freed = limit bypass): {where!r}"
    for banned in (" or ", "app_user_id", "owner_id", "select", " in ("):
        assert banned not in where, \
            f"{f}: {REFUSED_RETIRE_NODE} WHERE is widened past the refused profile ({banned.strip()!r}): {where!r}"
    assert "deleted_reason = 'channel_limit'" in set_part and "deleted_at = now()" in set_part, \
        f"{f}: {REFUSED_RETIRE_NODE} must set deleted_at = now(), deleted_reason = 'channel_limit': {set_part!r}"
    want_qr = "={{ [ $('Unity Webhook').first().json.body." + profile_field + " ] }}"
    got_qr = (r["parameters"].get("options") or {}).get("queryReplacement")
    assert got_qr == want_qr, \
        f"{f}: {REFUSED_RETIRE_NODE} queryReplacement wrong (channel field / binding):\n" \
        f"  got:      {got_qr!r}\n  expected: {want_qr!r}"

    # --- (3) Wiring: the exact chain, one output each, a terminal response, nothing else reachable ---------------
    slot = _main_targets(conns, "If Slot Limit")
    assert len(slot) == 2, f"{f}: If Slot Limit has {len(slot)} outputs, expected 2 (over limit / allowed)"
    assert slot[0] == [REFUSED_DELETE_NODE], \
        f"{f}: If Slot Limit's over-limit branch goes to {slot[0]}, expected [{REFUSED_DELETE_NODE!r}]"
    for src, dst in zip(REFUSAL_CHAIN, REFUSAL_CHAIN[1:]):
        got = _main_targets(conns, src)
        assert got == [[dst]], f"{f}: {src} -> {got}, expected [[{dst!r}]] (one output, one target)"
    assert _main_targets(conns, REFUSAL_RESPONSE_NODE) in ([], [[]]), \
        f"{f}: {REFUSAL_RESPONSE_NODE} gained an outgoing connection (it must stay the terminal `lastNode` response)"
    reach = _reachable(conns, slot[0])
    assert reach == set(REFUSAL_CHAIN), \
        f"{f}: the over-limit branch reaches {sorted(reach - set(REFUSAL_CHAIN))} beyond the refusal chain -- a " \
        f"refused create would continue into workflow creation (limit bypass)"
    leak = _reachable(conns, slot[1]) & set(REFUSAL_CHAIN)
    assert not leak, \
        f"{f}: {sorted(leak)} reachable from the ALLOWED branch -- the delete/retire would hit the profile the " \
        f"gate just accepted"
    parents = {REFUSED_DELETE_NODE: "If Slot Limit", REFUSED_RETIRE_NODE: REFUSED_DELETE_NODE,
               REFUSAL_RESPONSE_NODE: REFUSED_RETIRE_NODE}
    for src in conns:
        for branch in _main_targets(conns, src):
            for tgt in branch:
                if tgt in parents:
                    assert src == parents[tgt], \
                        f"{f}: {tgt} is also fed by {src!r} -- the refusal chain must hang off If Slot Limit ALONE"

    # --- (4) The refusal body itself: the client keys on exactly these two fields ------------------------------
    resp = need(REFUSAL_RESPONSE_NODE)
    assert resp["type"] == "n8n-nodes-base.set", f"{f}: {REFUSAL_RESPONSE_NODE} is not a set node"
    assert not resp.get("disabled"), f"{f}: {REFUSAL_RESPONSE_NODE} is disabled"
    assigned = {a["name"]: a["value"] for a in resp["parameters"]["assignments"]["assignments"]}
    assert assigned == {"success": False, "error": "channel_limit"}, \
        f"{f}: refusal response changed: {assigned!r} (the client keys on success/channel_limit)"
    assert resp["parameters"].get("includeOtherFields") is not True, \
        f"{f}: {REFUSAL_RESPONSE_NODE} now merges its input -- the delete/retire payload would leak into the " \
        f"refusal body"

    print(f"OK  {f} (refused create deletes the profile, retires its row, answers channel_limit)")


def checks():
    """Every check, in order. main() runs ALL of them — one red check must never hide the ones after it."""
    return [
        ("telegram bot template", check_telegram_bot),
        ("restamp TG", lambda: check_restamp_orchestrator(CREATE_TG, "{botTgId}", "WhatsappWorkflowId")),
        ("restamp WA", lambda: check_restamp_orchestrator(CREATE_WA, "{botWaId}", "TelegramWorkflowId")),
        ("canonical export WA", lambda: check_canonical_export_invariant(CREATE_WA)),
        ("canonical export TG", lambda: check_canonical_export_invariant(CREATE_TG)),
        ("refusal chain WA", lambda: check_refused_create_deletes_profile(CREATE_WA, "https://wappi.pro/api", "WhatsappProfileId")),
        ("refusal chain TG", lambda: check_refused_create_deletes_profile(CREATE_TG, "https://wappi.pro/tapi", "TelegramProfileId")),
        ("dialog metering TG", lambda: check_dialog_metering(TG_BOT)),
        ("dialog metering WA", lambda: check_dialog_metering(WA_BOT)),
        ("dialog metering shared", check_dialog_metering_shared),
        ("model id TG", lambda: check_model_id(TG_BOT)),
        ("model id WA", lambda: check_model_id(WA_BOT)),
        ("suggest replies", check_suggest_replies),
        ("revenuecat transfer", check_rc_transfer_carry_gate),
    ]


def run_checks(wf_dir, quiet=False):
    """Run every check against `wf_dir`; return [(label, message)] for the ones that failed."""
    global WF
    saved, WF = WF, wf_dir
    failures = []
    try:
        for label, fn in checks():
            try:
                if quiet:
                    with contextlib.redirect_stdout(io.StringIO()):
                        fn()
                else:
                    fn()
            except AssertionError as e:
                failures.append((label, str(e)))
            except Exception as e:   # a mutated/partial export: report it and keep going
                failures.append((label, f"unexpected structural error: {type(e).__name__}: {e}"))
    finally:
        WF = saved
    return failures


# --- --self-test: prove on mutated COPIES that a bypass of the channel-limit refusal is still detected ------------
def _link(*targets):
    return {"main": [[{"node": t, "type": "main", "index": 0} for t in targets]]}


def _nd(wf, name):
    return next(n for n in wf["nodes"] if n["name"] == name)


def _m_old_wiring(wf, own, other):            # the pre-hotfix chain: no retire, Delete -> Respond directly
    wf["nodes"] = [n for n in wf["nodes"] if n["name"] != REFUSED_RETIRE_NODE]
    wf["connections"].pop(REFUSED_RETIRE_NODE, None)
    wf["connections"][REFUSED_DELETE_NODE] = _link(REFUSAL_RESPONSE_NODE)


def _m_delete_to_respond(wf, own, other):     # retire still present but bypassed
    wf["connections"][REFUSED_DELETE_NODE] = _link(REFUSAL_RESPONSE_NODE)
    wf["connections"].pop(REFUSED_RETIRE_NODE, None)


def _m_retire_into_creation(wf, own, other):  # the refusal continues into workflow creation
    wf["connections"][REFUSED_RETIRE_NODE] = _link("Retire Same Bot Slot")


def _m_fanout_into_creation(wf, own, other):
    wf["connections"][REFUSED_DELETE_NODE] = _link(REFUSED_RETIRE_NODE, "Retire Same Bot Slot")


def _m_over_limit_to_creation(wf, own, other):
    wf["connections"]["If Slot Limit"]["main"][0] = [{"node": "Retire Same Bot Slot", "type": "main", "index": 0}]


def _m_retire_disabled(wf, own, other):
    _nd(wf, REFUSED_RETIRE_NODE)["disabled"] = True


def _m_retire_fail_closed(wf, own, other):
    _nd(wf, REFUSED_RETIRE_NODE).pop("onError", None)


def _m_retire_error_output_to_creation(wf, own, other):
    _nd(wf, REFUSED_RETIRE_NODE)["onError"] = "continueErrorOutput"
    wf["connections"][REFUSED_RETIRE_NODE]["main"].append([{"node": "Retire Same Bot Slot", "type": "main", "index": 0}])


def _m_where_without_workflow_guard(wf, own, other):
    p = _nd(wf, REFUSED_RETIRE_NODE)["parameters"]
    p["query"] = p["query"].replace(" and workflow_id is null", "")


def _m_where_by_owner(wf, own, other):
    _nd(wf, REFUSED_RETIRE_NODE)["parameters"]["query"] = (
        "update bot_profiles\nset deleted_at = now(), deleted_reason = 'channel_limit'\nwhere app_user_id = "
        "(select app_user_id from bot_profiles where profile_id = nullif($1,'')) and deleted_at is null and workflow_id is null;")


def _m_retire_on_allowed_branch(wf, own, other):
    wf["connections"]["Register Profile"]["main"][0].append({"node": REFUSED_RETIRE_NODE, "type": "main", "index": 0})


def _m_retire_cte_select(wf, own, other):     # a top-level SELECT: zero rows -> no item -> no response
    _nd(wf, REFUSED_RETIRE_NODE)["parameters"]["query"] = (
        "with r as (update bot_profiles set deleted_at = now(), deleted_reason = 'channel_limit' where profile_id = "
        "nullif($1,'') and deleted_at is null and workflow_id is null returning 1) select * from r;")


def _m_retire_other_channel_field(wf, own, other):
    _nd(wf, REFUSED_RETIRE_NODE)["parameters"]["options"]["queryReplacement"] = \
        "={{ [ $('Unity Webhook').first().json.body." + other + " ] }}"


def _m_retire_dev_credential(wf, own, other):
    _nd(wf, REFUSED_RETIRE_NODE)["credentials"]["postgres"]["id"] = "devPgCred000001"


def _m_respond_not_terminal(wf, own, other):
    wf["connections"][REFUSAL_RESPONSE_NODE] = _link("Get Sample Workflow")


def _m_respond_merges_input(wf, own, other):
    _nd(wf, REFUSAL_RESPONSE_NODE)["parameters"]["includeOtherFields"] = True


SELF_TEST_MUTATIONS = [_m_old_wiring, _m_delete_to_respond, _m_retire_into_creation, _m_fanout_into_creation,
                       _m_over_limit_to_creation, _m_retire_disabled, _m_retire_fail_closed,
                       _m_retire_error_output_to_creation, _m_where_without_workflow_guard, _m_where_by_owner,
                       _m_retire_on_allowed_branch, _m_retire_cte_select, _m_retire_other_channel_field,
                       _m_retire_dev_credential, _m_respond_not_terminal, _m_respond_merges_input]


def self_test(src_dir):
    """Control: the unmodified copy passes EVERY check (a verifier red on everything detects nothing). Then each
    mutation, applied to ONE Create workflow at a time (WA and TG), must fail a check that names that file."""
    tmp = tempfile.mkdtemp(prefix="parity-selftest-")
    bad = 0
    try:
        for x in os.listdir(src_dir):
            if x.endswith(".json"):
                shutil.copy(os.path.join(src_dir, x), tmp)
        control = run_checks(tmp, quiet=True)
        if control:
            bad += 1
            print("SELF-TEST FAIL control: the unmodified set does not pass -- " + "; ".join(f"[{l}] {m}" for l, m in control)[:600])
        else:
            print("ok   control: the unmodified set passes every check")
        channels = {CREATE_WA: ("WhatsappProfileId", "TelegramProfileId"), CREATE_TG: ("TelegramProfileId", "WhatsappProfileId")}
        for fn in SELF_TEST_MUTATIONS:
            for fname, (own, other) in channels.items():
                path = os.path.join(tmp, fname)
                pristine = open(path, encoding="utf-8").read()
                wf = json.loads(pristine)
                fn(wf, own, other)
                with open(path, "w", encoding="utf-8") as fh:
                    json.dump(wf, fh, ensure_ascii=False)
                try:
                    hits = [(l, m) for l, m in run_checks(tmp, quiet=True) if fname in m]
                finally:
                    with open(path, "w", encoding="utf-8") as fh:
                        fh.write(pristine)
                tag = f"{fn.__name__[3:]} [{'WA' if fname == CREATE_WA else 'TG'}]"
                if hits:
                    print(f"ok   {tag}: caught by [{hits[0][0]}] {hits[0][1].splitlines()[0][:150]}")
                else:
                    bad += 1
                    print(f"SELF-TEST FAIL {tag}: NOT detected -- the refusal bypass would pass this verifier")
    finally:
        shutil.rmtree(tmp)
    total = 1 + 2 * len(SELF_TEST_MUTATIONS)
    print(f"SELF-TEST {'PASSED' if not bad else 'FAILED'}: {total - bad}/{total} cases")
    return bad == 0


def main():
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
    ap.add_argument("--self-test", action="store_true",
                    help="prove on mutated temp copies of --dir that a bypass of the channel-limit refusal is detected")
    args = ap.parse_args()
    if args.self_test:
        sys.exit(0 if self_test(args.dir) else 1)
    failures = run_checks(args.dir)
    for label, msg in failures:
        print(f"PARITY FAIL: [{label}] {msg}")
    if failures:
        print(f"PARITY FAILED: {len(failures)} of {len(checks())} checks ({', '.join(l for l, _ in failures)})")
        sys.exit(1)
    print("ALL PARITY ASSERTS PASSED")


if __name__ == "__main__":
    main()
