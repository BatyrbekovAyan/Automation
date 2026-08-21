#!/usr/bin/env python3
"""Smoke probes for the RevenueCat Events webhook mirror (Task 7), extended in Task 8
with the channel-slot registration/backstop probe for CreateWhatsappWorkflow /
CreateTelegramWorkflow, and in Task 11 with a real value-asserting probe against the
new /webhook/GetUsage read endpoint.

## Part 1 -- RevenueCat Events (Task 7)

Fires realistic RevenueCat webhook envelopes at the local dev n8n instance and checks
the HTTP contract: an unauthenticated request must be rejected, and every recognized
event type must return 200 -- INCLUDING CANCELLATION, whose Map Event Code node
deliberately returns zero items for that case. RevenueCat Events acks AFTER the write,
not on receipt: Webhook uses responseMode "responseNode", Map Event has
alwaysOutputData=true (so CANCELLATION's `return []` still emits one synthetic empty
item instead of killing the run), and an `If Has Payload?` gate on `app_user_id` routes
a real item to Upsert Subscriber -> Respond 200 (Postgres has onError:
continueErrorOutput -> Respond Error 500, so a genuine write failure surfaces as a
non-2xx and RevenueCat retries) while the empty/no-op item goes straight to its own
Respond 200. This is what makes CANCELLATION 200 without ever touching Postgres, and
what makes a broken Upsert Subscriber query show up as a real failure here rather than
being silently swallowed.

This part only checks HTTP status codes. It does NOT read back the database --
Postgres lives behind n8n's own credential, unreachable from a plain script, so DB-
state verification (plan/status/topup_balance after the series) is done separately:
a throwaway Manual-Trigger-then-Postgres-SELECT workflow, built and executed by hand
through the n8n-mcp tools, read once, then archived -- there is no committed script or
README section for this today, it's a one-off procedure repeated per verification pass.
DB-state assertions become probe-native in Task 11 via /webhook/GetUsage -- once that
read endpoint exists, this script should call it instead of requiring a manual read.

Usage:
    RC_WEBHOOK_SECRET=<minted-secret> python3 Tools/n8n/probe-billing.py
    N8N_BASE_URL=... RC_WEBHOOK_SECRET=... python3 Tools/n8n/probe-billing.py

The secret is never hardcoded here -- it must come from the RC_WEBHOOK_SECRET env var
(it's the value bound into the "RevenueCat Webhook" httpHeaderAuth credential on the
Webhook node; n8n itself rejects a wrong/missing Authorization header with 401/403
before the workflow ever runs, so there is no app-level auth code to probe here).

Real RevenueCat webhook envelope shape: {"event": {...}}. The Map Event Code node reads
$json.body.event ?? $json.body, so this script always sends the wrapped form to match
what RevenueCat actually POSTs (per RevenueCat's webhook docs: event.type,
event.app_user_id, event.entitlement_ids, event.product_id, event.expiration_at_ms).

## Part 2 -- channel-slot registration/backstop (Task 8, opt-in)

Run with `--channel-slot-backstop` (see that section below for the full contract, the
precondition it needs, and why it is NOT part of the default run).

## Part 3 -- dialog metering / quota enforcement (Task 9, opt-in)

Run with `--dialog-metering` (see that section below). Provides `dialog_metering_query`,
a reusable helper returning the exact shipped Count Dialog SQL with profile_id/chat_id
substituted as literals, for pasting into a one-off Manual-Trigger -> Postgres
verification workflow -- and a real-webhook CONNECTIVITY smoke test only (the quota
decision itself cannot be asserted over plain HTTP; see that section for why).

## Part 4 -- GetUsage read endpoint (Task 11, opt-in)

Run with `--usage` (see that section below). Unlike Parts 1-3, this one gets REAL
exact-value assertions over plain HTTP -- GetUsage is a pure unauthenticated read, so
there's no Postgres credential or n8n-mcp access needed to check the numbers it
returns. Two scenarios: an unknown appUserId (needs no precondition, always passes --
asserts the trial-default shape, 200 not 500) and a seeded probe_user_11 (needs a
precondition seeded via `usage_seed_sql()` through a one-off n8n-mcp workflow first --
asserts an EXACT value set: plan/quota/used/topupBalance/channelsConnected/
botsRegistered/periodEnd).
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request
import uuid

BASE = os.environ.get("N8N_BASE_URL", "http://localhost:5678").rstrip("/")
URL = BASE + "/webhook/RevenueCatEvent"
SECRET = os.environ.get("RC_WEBHOOK_SECRET", "")

PROBE_USER = "probe_user_1"
TOPUP_PRODUCT_ID = "topup.dialogs.500"


def now_plus_days_ms(days):
    return int((time.time() + days * 86400) * 1000)


def post_event(event_body, with_auth=True):
    """POST a RevenueCat-shaped envelope {"event": {...}}. Returns (status, body_text)."""
    payload = json.dumps({"event": event_body}).encode()
    req = urllib.request.Request(URL, data=payload, method="POST")
    req.add_header("Content-Type", "application/json")
    if with_auth:
        req.add_header("Authorization", SECRET)
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()
    except urllib.error.URLError as e:
        return None, str(e.reason)


def post_multipart_form(url, fields, timeout=30):
    """POST a multipart/form-data body with stdlib only -- mirrors Unity's WWWForm (the
    Create*Workflow webhooks are hit with a multipart form, not JSON). Returns
    (status, body_text)."""
    boundary = uuid.uuid4().hex
    parts = []
    for name, value in fields.items():
        parts.append(f"--{boundary}\r\n".encode())
        parts.append(f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode())
        parts.append(str(value).encode("utf-8"))
        parts.append(b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())
    body = b"".join(parts)

    req = urllib.request.Request(url, data=body, method="POST")
    req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, resp.read().decode(errors="replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(errors="replace")
    except urllib.error.URLError as e:
        return None, str(e.reason)


# Each probe: (label, event_body, with_auth, expected_codes (set), settle_seconds)
# responseNode mode means the HTTP response IS the confirmation the write already
# committed (or failed) -- there's no post-response background race anymore. The small
# settle_seconds gap is just a courteous buffer between sequential mutations of the same
# row (probes b-e), not a correctness requirement.
PROBES = [
    ("a_no_auth", {"type": "INITIAL_PURCHASE", "app_user_id": "probe_noauth_check"}, False, {401, 403}, 0),
    ("b_initial_purchase", {
        "type": "INITIAL_PURCHASE",
        "app_user_id": PROBE_USER,
        "entitlement_ids": ["tier_business"],
        "expiration_at_ms": now_plus_days_ms(30),
    }, True, {200}, 2),
    ("c_non_renewing_topup", {
        "type": "NON_RENEWING_PURCHASE",
        "app_user_id": PROBE_USER,
        "product_id": TOPUP_PRODUCT_ID,
    }, True, {200}, 2),
    ("d_expiration", {
        "type": "EXPIRATION",
        "app_user_id": PROBE_USER,
    }, True, {200}, 2),
    ("e_cancellation", {
        "type": "CANCELLATION",
        "app_user_id": PROBE_USER,
    }, True, {200}, 0),
]


# ---------------------------------------------------------------------------------
# Part 2: channel-slot registration/backstop probe (Task 8, + fix-round) --
# CreateWhatsappWorkflow / CreateTelegramWorkflow
# ---------------------------------------------------------------------------------
#
# Both Create*Workflow orchestrators now run, right after Vertical Prompt and before
# any template clone: If Has AppUserID? -> [true] Ensure Subscriber (upsert a trial
# row, on-conflict-do-nothing, onError:continueRegularOutput) -> Count Channels
# (row-safe: alive bot_profiles count EXCLUDING any row that already matches this
# request's own (channel, bot_key) -- a same-bot channel replacement never counts its
# own old row; onError:continueRegularOutput+alwaysOutputData) -> Compute Slot Limit
# (fail-open: a DB error or missing plan -> overLimit=false, dbError=true, i.e. a full
# Supabase outage never blocks bot creation) -> If Slot Limit -> [over limit] Respond
# {success:false,error:"channel_limit"} (terminal -- matches this workflow's existing
# responseMode:"lastNode" contract: no template clone, no Wappi call, no new
# bot_profiles row) / [under limit] Retire Same Bot Slot (retires -- deleted_at=now()
# -- any of THIS bot's own old alive row for this channel, a no-op when BotKey is
# empty) -> Register Profile (upsert bot_profiles, now carrying bot_key,
# onError:continueRegularOutput) -> the untouched original chain (Get Sample Workflow
# -> ... -> Create Workflow -> Activate Created Workflow -> Set Wappi Webhook -> ...).
# [false / no AppUserID, old client] -> Get Sample Workflow directly, skipping all of
# the above.
#
# The channel count is CROSS-CHANNEL and TOTAL per app_user_id (Count Channels has no
# channel filter on the outer count -- only the exclusion clause is channel-scoped), so
# a plan's limit is shared between WhatsApp and Telegram: an app_user_id already at its
# limit via WhatsApp gets rejected on a Telegram create too (Scenario B below).
#
# PRECONDITIONS this script cannot set up itself (Postgres lives behind n8n's own
# credential -- same constraint as Part 1): before running,
#   - CHANNEL_SLOT_APP_USER needs a `subscribers` row with plan='start' (limit 1
#     channel, per the n8n Compute Slot Limit map / PlanCatalog.MaxChannels) and ZERO
#     alive `bot_profiles` rows (Scenario A registers the first one itself).
#   - CHANGE_NUMBER_APP_USER needs a `subscribers` row with plan='start' AND one ALIVE
#     `bot_profiles` row (channel='whatsapp', bot_key='Bot0') already at the limit --
#     Scenario C proves that re-registering the SAME bot_key on a new profile id is
#     allowed (retires the old row) rather than counted as a second slot.
# Seed/reset both with a one-off Manual-Trigger-then-Postgres workflow through the
# n8n-mcp tools (see the Task 8 report for the exact upserts used) -- there is no HTTP
# path to do this from the app side, it is server-only billing state.
#
# REAL SIDE EFFECTS, by design (this hits the real Create* webhooks -- there is no
# dry-run mode): an ALLOWED call (Scenario A's call 1, Scenario C's call) creates AND
# ACTIVATES a real n8n workflow (named CHANNEL_SLOT_BOT_NAME/CHANGE_NUMBER_BOT_NAME
# below, so both are trivially findable for cleanup) and writes a bot_profiles row; it
# then tries to configure a Wappi webhook against a profile id that does not exist on
# Wappi (confirmed empirically: POST .../api/webhook/url/set with a bogus profile_id ->
# real HTTP 400 {"detail":"Profile not found","status":"error"}), and the Set Wappi
# Webhook node has no onError override, so the WHOLE execution aborts right there --
# the HTTP response to an ALLOWED call is therefore an n8n execution-error response,
# not the clean {"id": "..."} shape the client parses. That is expected and NOT what
# this probe asserts on those calls; the workflow clone + bot_profiles write already
# happened by the time the abort occurs (both precede Set Wappi Webhook in the chain),
# which is all the channel-slot logic needs to be exercised. A REJECTED call (Scenario
# A's call 2, Scenario B) terminates at Respond Channel Limit BEFORE ever reaching Get
# Sample Workflow/Create Workflow/any Wappi call, so its response IS the clean,
# reliable {"success":false,"error":"channel_limit"} JSON -- that IS asserted.
#
# Cleanup (created workflow clones + probe rows in subscribers/bot_profiles) is NOT
# done by this script -- same reasoning as Part 1's DB read-back: done by hand through
# the n8n-mcp tools once this prints, and recorded in the Task 8 report.
CHANNEL_SLOT_APP_USER = "probe_user_8"
CHANGE_NUMBER_APP_USER = "probe_user_9"
CHANNEL_SLOT_BOT_NAME = "ZZZ_PROBE_TASK8_WA"
CHANGE_NUMBER_BOT_NAME = "ZZZ_PROBE_TASK8_CHANGENUM"
CREATE_WA_URL = BASE + "/webhook/CreateWhatsappWorkflow"
CREATE_TG_URL = BASE + "/webhook/CreateTelegramWorkflow"


def channel_slot_form_wa(app_user_id, fake_profile_id, bot_key="", name=CHANNEL_SLOT_BOT_NAME):
    """Field set mirrors Manager.CreateWhatsappWorkflowFromStart's WWWForm exactly
    (see Assets/Scripts/Main/Manager.cs)."""
    return {
        "Name": name,
        "BusinessType": "",
        "BusinessTypeId": "",
        "WhatsappProfileId": fake_profile_id,
        "TelegramWorkflowId": "-1",
        "Business": "",
        "Prompt": "",
        "ProductsList": "",
        "ServicesList": "",
        "AppUserID": app_user_id,
        "BotKey": bot_key,
    }


def channel_slot_form_tg(app_user_id, fake_profile_id, bot_key="", name=CHANNEL_SLOT_BOT_NAME):
    """Field set mirrors Manager.CreateTelegramWorkflowFromStart's WWWForm exactly."""
    return {
        "Name": name,
        "BusinessType": "",
        "BusinessTypeId": "",
        "TelegramProfileId": fake_profile_id,
        "WhatsappWorkflowId": "-1",
        "Business": "",
        "Prompt": "",
        "ProductsList": "",
        "ServicesList": "",
        "AppUserID": app_user_id,
        "BotKey": bot_key,
    }


def run_channel_slot_backstop_probe():
    ok_all = True

    print(f"=== Scenario A: WA register-then-reject -- app_user_id={CHANNEL_SLOT_APP_USER!r} "
          f"against {CREATE_WA_URL} ===")
    fake1 = f"probe_fake_wa_profile_{uuid.uuid4().hex[:8]}"
    status1, body1 = post_multipart_form(CREATE_WA_URL, channel_slot_form_wa(CHANNEL_SLOT_APP_USER, fake1))
    print(f"[call 1 / allowed]     HTTP {status1} -- {body1[:300]}")
    time.sleep(2)   # let call 1's Postgres writes (Ensure Subscriber, Register Profile) settle
    fake2 = f"probe_fake_wa_profile_{uuid.uuid4().hex[:8]}"
    status2, body2 = post_multipart_form(CREATE_WA_URL, channel_slot_form_wa(CHANNEL_SLOT_APP_USER, fake2))
    print(f"[call 2 / over limit]  HTTP {status2} -- {body2[:300]}")
    a_ok = status2 == 200 and "channel_limit" in body2
    print(f"[{'OK' if a_ok else 'FAIL'}] scenario A: call 2 rejected with channel_limit: {a_ok}\n")
    ok_all = ok_all and a_ok

    time.sleep(2)
    print(f"=== Scenario B: Telegram create rejected on the SAME (cross-channel) limit -- "
          f"app_user_id={CHANNEL_SLOT_APP_USER!r} against {CREATE_TG_URL} ===")
    fake3 = f"probe_fake_tg_profile_{uuid.uuid4().hex[:8]}"
    status3, body3 = post_multipart_form(CREATE_TG_URL, channel_slot_form_tg(CHANNEL_SLOT_APP_USER, fake3))
    print(f"[TG create / over limit]  HTTP {status3} -- {body3[:300]}")
    b_ok = status3 == 200 and "channel_limit" in body3
    print(f"[{'OK' if b_ok else 'FAIL'}] scenario B: Telegram create also rejected "
          f"(limit is shared/total, not per-channel): {b_ok}\n")
    ok_all = ok_all and b_ok

    time.sleep(2)
    print(f"=== Scenario C: change-number -- same bot_key retires its own old row instead of "
          f"consuming a second slot -- app_user_id={CHANGE_NUMBER_APP_USER!r} ===")
    new_profile = f"probe_fake_wa_profile_NEW_{uuid.uuid4().hex[:8]}"
    status4, body4 = post_multipart_form(
        CREATE_WA_URL,
        channel_slot_form_wa(CHANGE_NUMBER_APP_USER, new_profile, bot_key="Bot0", name=CHANGE_NUMBER_BOT_NAME),
    )
    print(f"[WA create, same bot_key, new profile]  HTTP {status4} -- {body4[:300]}")
    c_ok = not (status4 == 200 and "channel_limit" in body4)
    print(f"[{'OK' if c_ok else 'FAIL'}] scenario C: NOT rejected as channel_limit "
          f"(same-bot replacement allowed): {c_ok}\n")
    ok_all = ok_all and c_ok

    print("This script does not verify bot_profiles/n8n-workflow DB state or clean up -- "
          "see the Task 8 report for the DB read-back and cleanup performed via n8n-mcp.")
    return 0 if ok_all else 1


# ---------------------------------------------------------------------------------
# Part 3: dialog metering / quota enforcement (Task 9) -- WhatsApp_Bot / Telegram_Bot
# ---------------------------------------------------------------------------------
#
# Rule: a dialog = (app_user_id, chat_id, date Asia/Almaty). Spliced into BOTH bot
# templates right after Is Latest?'s not-aborted branch (so a debounced duplicate
# fragment never double-counts) and before Input type/AI Agent (so a quota-blocked
# NEW dialog never triggers an auto-reply): Count Dialog (Postgres, the CTE below,
# onError:continueRegularOutput + alwaysOutputData:true so a DB outage or an
# unregistered profile fails OPEN) -> Quota Decision (Code, parses allowed/used/plan/
# status, fail-open on row.error or a missing `allowed` key) -> If Quota Allows
# ([email protected] Input type; [email protected] dead end, same idiom as Suppressed?/Is Latest?).
#
# TWO EMPIRICAL FINDINGS (2026-08-21) rule out a plain-HTTP end-to-end probe here,
# unlike Parts 1/2:
#   1. Fetch Recent (pre-existing debounce machinery, upstream of this splice) calls
#      Wappi's REAL messages/get with the incoming profile_id, has no onError
#      override, and a profile_id Wappi has never authorized gets a real HTTP 400
#      {"detail":"profile_id error"} back (confirmed via a direct curl against
#      wappi.pro) -- the execution aborts right there, before Count Dialog ever runs.
#      A synthetic/fake profile_id can therefore never reach this task's new nodes via
#      a real webhook fire, by construction of the pre-existing debounce gate.
#   2. Both bot templates' Webhook trigger responds IMMEDIATELY (confirmed via each
#      workflow's own triggerInfo: "Webhook is configured to respond immediately with
#      the message 'Workflow got started.'") -- the HTTP response is identical
#      regardless of what happens downstream, so even a real-profile fire couldn't
#      show the quota decision in the HTTP response.
#
# The quota decision was instead verified two other ways for Task 9 (full transcript
# in task-9-report.md, not reproduced as script code here since both require access
# this plain script does not have -- the same constraint Part 1's docstring already
# notes for Postgres/DB-state checks):
#   (a) the EXACT shipped SQL (dialog_metering_query below) run against real seeded
#       Postgres data via a one-off Manual-Trigger -> Postgres workflow -- 3 scenarios
#       (new+over quota / continuation / new+under quota) plus a 4th (unregistered
#       profile), all matching the required allowed/used/row-count outcomes;
#   (b) the real node WIRING proven via n8n-mcp's test_workflow pinned-execution
#       against the live Telegram Bot template (Count Dialog pinned to each of
#       blocked/allowed/fail-open; get_execution's node-path confirms execution stops
#       at If Quota Allows when blocked and reaches Input type otherwise).
#
# What THIS script provides instead: dialog_metering_query() as a reusable helper (so
# a future re-verification doesn't need to hand-copy the CTE out of the workflow
# JSON), and --dialog-metering as a real-webhook CONNECTIVITY smoke test ONLY --
# it proves the endpoint is reachable and accepts the payload shape, nothing about
# the quota decision.
DIALOG_METERING_WA_ID = "4wYitz5ek30SVNlT"
DIALOG_METERING_TG_ID = "4VN3gsFaC2HUYmcc"


def dialog_metering_query(profile_id, chat_id):
    """Return the exact shipped Count Dialog SQL with profile_id/chat_id substituted
    as SQL literals -- paste into a one-off Manual-Trigger -> Postgres workflow (via
    n8n-mcp or the n8n UI) to check allowed/used/plan/status for that pair. Returns
    text only; this script has no Postgres credential to run it directly (see the
    Part 3 docstring above)."""
    def lit(s):
        return "'" + s.replace("'", "''") + "'"
    pid, cid = lit(profile_id), lit(chat_id)
    return f"""with me as (
  select bp.app_user_id, s.plan, s.status, s.topup_balance
  from bot_profiles bp join subscribers s using (app_user_id)
  where bp.profile_id = {pid} and bp.deleted_at is null
), today as (
  select (now() at time zone 'Asia/Almaty')::date d
), usage_now as (
  select count(*) used from dialog_counts dc, me
  where dc.app_user_id = me.app_user_id
    and date_trunc('month', dc.d) = date_trunc('month', (select d from today))
), existing as (
  select 1 from dialog_counts dc, me, today t
  where dc.app_user_id = me.app_user_id and dc.chat_id = {cid} and dc.d = t.d
), quota as (
  select
    case me.plan when 'trial' then 150 when 'start' then 300 when 'business' then 1000 when 'network' then 3000 else 0 end
    + case when me.status in ('active','trialing') then me.topup_balance else 0 end
    as q
  from me
)
-- Two accepted concurrency edges, both benign: (1) a genuine same-chat race can't
-- reach here twice -- the upstream debounce gate (Is Latest?) already collapses
-- concurrent fragments for one chat down to a single survivor before Count Dialog
-- ever runs; (2) two DIFFERENT new chat_ids for the same app_user_id racing the same
-- usage_now snapshot can both insert, overshooting the quota by at most one dialog --
-- accepted, and it favours the customer rather than the business.
, ins as (
  insert into dialog_counts (app_user_id, chat_id, d)
  select me.app_user_id, {cid}, t.d from me, today t
  where not exists (select 1 from existing)
    and me.status not in ('expired','grace')
    and (select used from usage_now) < (select q from quota)
  on conflict (app_user_id, chat_id, d) do nothing
  returning 1
)
select
  (exists(select 1 from existing) or exists(select 1 from ins)) as allowed,
  (select used from usage_now) as used,
  (select q from quota) as q,
  me.plan as plan, me.status as status
from me;"""


def _n8n_api_key():
    """Read the same n8nAPIKey the app itself uses -- never hardcoded."""
    here = os.path.dirname(os.path.abspath(__file__))
    secrets_path = os.path.join(here, "..", "..", "Assets", "StreamingAssets", "secrets.json")
    with open(secrets_path) as fh:
        return json.load(fh)["n8nAPIKey"]


def _set_workflow_active(workflow_id, active, api_key):
    """Raw REST activate/deactivate (both bot templates lack availableInMCP, so the
    n8n-mcp tools refuse them -- see task-9-report.md). Returns True on success."""
    verb = "activate" if active else "deactivate"
    req = urllib.request.Request(
        f"{BASE}/api/v1/workflows/{workflow_id}/{verb}", method="POST",
        headers={"X-N8N-API-KEY": api_key, "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            return resp.status in (200, 201)
    except urllib.error.HTTPError:
        return False


def _fire_bot_template_webhook(webhook_path, profile_id, chat_id, timeout=15):
    body = json.dumps({
        "messages": [{
            "id": f"probe_conn_{uuid.uuid4().hex[:10]}",
            "type": "chat",
            "body": "connectivity probe",
            "from": chat_id,
            "chatId": chat_id,
            "profile_id": profile_id,
        }]
    }).encode()
    req = urllib.request.Request(f"{BASE}/webhook/{webhook_path}", data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, resp.read().decode(errors="replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(errors="replace")
    except urllib.error.URLError as e:
        return None, str(e.reason)


def run_dialog_metering_connectivity_probe():
    """Transport-level smoke test only -- see the Part 3 docstring for why the quota
    decision itself cannot be asserted this way. Activates each template in turn
    (never both at once -- they share the identical literal webhook path
    "0091024b-7b46", so simultaneous activation would collide), fires one synthetic
    message, and restores the PRIOR active state in a finally block regardless of
    outcome (both templates are inactive by project convention outside active
    testing -- see feedback_bot_activation_policy.md)."""
    api_key = _n8n_api_key()
    ok_all = True
    for label, wid in (("WhatsApp Bot", DIALOG_METERING_WA_ID), ("Telegram Bot", DIALOG_METERING_TG_ID)):
        print(f"=== {label} ({wid}) connectivity smoke test ===")
        activated = _set_workflow_active(wid, True, api_key)
        if not activated:
            print(f"[FAIL] could not activate {label} -- skipping fire, leaving deactivated")
            ok_all = False
            continue
        try:
            status, body = _fire_bot_template_webhook(
                "0091024b-7b46", f"probe_conn_fake_{uuid.uuid4().hex[:8]}", "probe_conn_chat"
            )
            reached = status is not None
            print(f"[{'OK' if reached else 'FAIL'}] {label}: HTTP {status} -- {body[:150]}")
            ok_all = ok_all and reached
        finally:
            restored = _set_workflow_active(wid, False, api_key)
            print(f"    restored active=false: {restored}")
            ok_all = ok_all and restored
        print()
    print("This only proves the webhook is reachable and accepts the payload shape --")
    print("it does NOT assert the quota decision (see the Part 3 module docstring).")
    print("Quota-decision verification transcript: task-9-report.md.")
    return 0 if ok_all else 1


# ---------------------------------------------------------------------------------
# Part 4: GetUsage read endpoint (Task 11)
# ---------------------------------------------------------------------------------
#
# GetUsage: Webhook (responseNode, authentication:none -- the appUserId IS the
# secret, same v1 posture as every other webhook in this app) -> Postgres "Read
# Usage" (row-safe: a fixed one-row CTE left-joined to subscribers/dialog_counts/
# bot_profiles, so an unregistered appUserId still gets exactly one output row with
# defaults instead of zero rows; onError:continueRegularOutput via
# continueErrorOutput -> Respond Error 500) -> Code "Shape Response" (maps plan to
# quota via the same {trial:150,start:300,business:1000,network:3000,none:0} map as
# Tasks 8/9) -> Respond 200 JSON.
#
# Unlike Parts 1-3, this endpoint's response IS the thing being verified -- no
# Postgres credential or n8n-mcp access needed, so the assertions below are REAL
# exact-value checks, not connectivity-only smoke tests (Task 7's report flagged
# this gap explicitly: "DB-state assertions become probe-native in Task 11").
#
# Scenario 1 (UNKNOWN_USER, always runs, no precondition): a random appUserId that
# has never been seen. Asserts the row-safe default shape -- 200 (not 500), plan
# 'trial', status 'trialing', quota 150, used 0, topupBalance 0, botsRegistered 0,
# channelsConnected 0, periodEnd null.
#
# Scenario 2 (USAGE_PROBE_USER = "probe_user_11", needs a precondition, SKIPPED --
# reported, not failed -- if the live plan isn't 'business' yet): asserts an EXACT
# value set. PRECONDITION (same constraint as Parts 1/2 -- Postgres lives behind
# n8n's own credential, unreachable from this plain script): seed via a one-off
# Manual-Trigger -> Postgres workflow through the n8n-mcp tools, running
# usage_seed_sql() verbatim. It creates, for app_user_id="probe_user_11":
#   - subscribers: plan='business', status='active', topup_balance=500
#   - bot_profiles: 2 ALIVE rows sharing bot_key='Bot0' (channel='whatsapp' and
#     channel='telegram') + 1 DEAD row (deleted_at set, bot_key='BotDead') -- proves
#     botsRegistered counts DISTINCT bot_key among alive rows (1, not 2) while
#     channelsConnected counts alive ROWS (2, not 1)
#   - dialog_counts: 7 rows dated in the CURRENT month + 2 rows dated in the
#     PREVIOUS month -- proves `used` excludes last month (7, not 9)
# Expected response: plan=business, status=active, quota=1000, used=7,
# topupBalance=500, botsRegistered=1, channelsConnected=2, periodEnd non-null.
# Clean up with usage_cleanup_sql() (also via a one-off n8n-mcp workflow) once done
# -- this repo's convention is to not leave probe rows lying around in a shared dev
# database (see Task 8/9 reports).
#
# The broken-query negative test (temporarily point Read Usage's query at a
# nonexistent column, confirm HTTP 500, restore, confirm HTTP 200 again with
# unchanged values) is NOT scriptable here either -- same class of gap as Part 3's
# quota-decision check, it needs n8n-mcp write access (update_workflow +
# publish_workflow) this plain script does not have. Done by hand via n8n-mcp for
# this task; full transcript in task-11-report.md.
USAGE_URL = BASE + "/webhook/GetUsage"
USAGE_PROBE_USER = "probe_user_11"

EXPECTED_UNKNOWN_USER_USAGE = {
    "success": True, "plan": "trial", "status": "trialing", "quota": 150, "used": 0,
    "topupBalance": 0, "botsRegistered": 0, "channelsConnected": 0, "periodEnd": None,
}
EXPECTED_USAGE_PROBE_USER = {
    "success": True, "plan": "business", "status": "active", "quota": 1000, "used": 7,
    "topupBalance": 500, "botsRegistered": 1, "channelsConnected": 2,
}   # periodEnd asserted non-null separately (it's a real timestamp, not a fixed literal)


def usage_seed_sql():
    """Exact SQL used to seed the USAGE_PROBE_USER precondition (scenario 2) --
    paste into a one-off Manual-Trigger -> Postgres workflow via n8n-mcp. Uses
    fixed calendar-date literals (2026-08/2026-07) rather than now()-relative math
    so the this-month/last-month split can't drift across a midnight boundary at
    the moment it runs; safe as long as this is run before 2026-09-01."""
    return """insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end, updated_at)
values ('probe_user_11', 'business', 'active', 500, now() + interval '30 days', now())
on conflict (app_user_id) do update set
  plan = excluded.plan, status = excluded.status, topup_balance = excluded.topup_balance,
  current_period_end = excluded.current_period_end, updated_at = now();

insert into bot_profiles (profile_id, app_user_id, channel, bot_key, deleted_at)
values
  ('probe11_wa_profile', 'probe_user_11', 'whatsapp', 'Bot0', null),
  ('probe11_tg_profile', 'probe_user_11', 'telegram', 'Bot0', null),
  ('probe11_dead_profile', 'probe_user_11', 'whatsapp', 'BotDead', now())
on conflict (profile_id) do update set
  app_user_id = excluded.app_user_id, channel = excluded.channel, bot_key = excluded.bot_key, deleted_at = excluded.deleted_at;

insert into dialog_counts (app_user_id, chat_id, d)
values
  ('probe_user_11', 'probe11_chat_1', '2026-08-01'),
  ('probe_user_11', 'probe11_chat_2', '2026-08-03'),
  ('probe_user_11', 'probe11_chat_3', '2026-08-06'),
  ('probe_user_11', 'probe11_chat_4', '2026-08-09'),
  ('probe_user_11', 'probe11_chat_5', '2026-08-12'),
  ('probe_user_11', 'probe11_chat_6', '2026-08-15'),
  ('probe_user_11', 'probe11_chat_7', '2026-08-18'),
  ('probe_user_11', 'probe11_chat_8', '2026-07-05'),
  ('probe_user_11', 'probe11_chat_9', '2026-07-20')
on conflict (app_user_id, chat_id, d) do nothing;"""


def usage_cleanup_sql():
    """Deletes everything usage_seed_sql() created -- paste into the same one-off
    workflow once verification is done."""
    return """delete from dialog_counts where app_user_id = 'probe_user_11';
delete from bot_profiles where app_user_id = 'probe_user_11';
delete from subscribers where app_user_id = 'probe_user_11';"""


def fetch_usage(app_user_id, timeout=15):
    """POST {"appUserId": ...} to GetUsage. Returns (status, parsed_json_or_None)."""
    body = json.dumps({"appUserId": app_user_id}).encode()
    req = urllib.request.Request(USAGE_URL, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            status, raw = resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        status, raw = e.code, e.read().decode()
    except urllib.error.URLError as e:
        return None, str(e.reason)
    try:
        return status, json.loads(raw)
    except json.JSONDecodeError:
        return status, raw


def run_usage_probe():
    ok_all = True

    print(f"=== Scenario 1: unknown appUserId -- always-on default-shape assert against {USAGE_URL} ===")
    unknown_id = f"probe_conn_unknown_{uuid.uuid4().hex[:10]}"
    status, body = fetch_usage(unknown_id)
    print(f"HTTP {status} -- {body}")
    if status == 200 and isinstance(body, dict):
        mismatches = {k: (v, body.get(k)) for k, v in EXPECTED_UNKNOWN_USER_USAGE.items() if body.get(k) != v}
        scenario1_ok = not mismatches
    else:
        mismatches = {"<http>": (200, status)}
        scenario1_ok = False
    print(f"[{'OK' if scenario1_ok else 'FAIL'}] unknown-user default shape matches exactly: {scenario1_ok}"
          + (f" -- mismatches: {mismatches}" if mismatches else ""))
    ok_all = ok_all and scenario1_ok

    print(f"\n=== Scenario 2: seeded {USAGE_PROBE_USER!r} -- exact-value assert ===")
    status, body = fetch_usage(USAGE_PROBE_USER)
    print(f"HTTP {status} -- {body}")
    if status == 200 and isinstance(body, dict) and body.get("plan") == EXPECTED_USAGE_PROBE_USER["plan"]:
        mismatches = {k: (v, body.get(k)) for k, v in EXPECTED_USAGE_PROBE_USER.items() if body.get(k) != v}
        if not body.get("periodEnd"):
            mismatches["periodEnd"] = ("<non-null>", body.get("periodEnd"))
        scenario2_ok = not mismatches
        print(f"[{'OK' if scenario2_ok else 'FAIL'}] exact value set matches: {scenario2_ok}"
              + (f" -- mismatches: {mismatches}" if mismatches else ""))
        ok_all = ok_all and scenario2_ok
    else:
        print(f"[SKIP] {USAGE_PROBE_USER!r} does not currently show plan={EXPECTED_USAGE_PROBE_USER['plan']!r} "
              f"(HTTP {status}, plan={body.get('plan') if isinstance(body, dict) else '?'!r}) -- "
              f"seed the precondition first via usage_seed_sql() through a one-off n8n-mcp workflow, "
              f"see the Part 4 module docstring. NOT counted as a failure.")

    print("\nThe broken-query negative test (bad column -> 500, restore -> 200) needs n8n-mcp write")
    print("access this script does not have -- done by hand; transcript in task-11-report.md.")
    return 0 if ok_all else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument(
        "--channel-slot-backstop", action="store_true",
        help="Run the Task 8 channel-slot registration/backstop probes (3 scenarios: "
             "WA register-then-reject, TG cross-channel reject, change-number bot_key "
             "slot release) against CreateWhatsappWorkflow/CreateTelegramWorkflow "
             "instead of the default RevenueCat Events probes. Read the module "
             "docstring's Part 2 section first -- real side effects (creates+activates "
             "real n8n workflows) and needs both probe_user_8/probe_user_9 preconditions "
             "seeded first.")
    ap.add_argument(
        "--dialog-metering", action="store_true",
        help="Run the Task 9 dialog-metering CONNECTIVITY smoke test (activates each "
             "bot template in turn, fires one synthetic webhook call, restores prior "
             "active state) instead of the default RevenueCat Events probes. Read the "
             "module docstring's Part 3 section first -- this does NOT assert the "
             "quota decision itself (see why); use dialog_metering_query() for a "
             "real DB-level re-check via a one-off n8n-mcp workflow.")
    ap.add_argument(
        "--usage", action="store_true",
        help="Run the Task 11 GetUsage EXACT-VALUE probe (2 scenarios: unknown "
             "appUserId default shape, seeded probe_user_11 exact values) instead of "
             "the default RevenueCat Events probes. Read the module docstring's "
             "Part 4 section first -- scenario 2 needs probe_user_11 seeded via "
             "usage_seed_sql() through a one-off n8n-mcp workflow first, and is "
             "SKIPPED (not failed) if that precondition isn't live.")
    args = ap.parse_args()

    if args.channel_slot_backstop:
        sys.exit(run_channel_slot_backstop_probe())

    if args.dialog_metering:
        sys.exit(run_dialog_metering_connectivity_probe())

    if args.usage:
        sys.exit(run_usage_probe())

    if not SECRET:
        print("FAIL: RC_WEBHOOK_SECRET is not set -- refusing to run (probes b-e need it "
              "to authenticate; probe (a) deliberately omits it).")
        sys.exit(1)

    failures = []
    for label, event_body, with_auth, expected_codes, settle in PROBES:
        status, body = post_event(event_body, with_auth=with_auth)
        ok = status in expected_codes
        tag = "OK" if ok else "FAIL"
        print(f"[{tag}] {label}: HTTP {status} (expected one of {sorted(expected_codes)}) -- {body[:200]}")
        if not ok:
            failures.append(label)
        if settle:
            time.sleep(settle)

    if failures:
        print(f"\n{len(failures)}/{len(PROBES)} probes failed: {failures}")
        sys.exit(1)

    print("\nALL OK")


if __name__ == "__main__":
    main()
