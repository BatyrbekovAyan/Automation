#!/usr/bin/env python3
"""Smoke probes for the RevenueCat Events webhook mirror (Task 7), extended in Task 8
with the channel-slot registration/backstop probe for CreateWhatsappWorkflow /
CreateTelegramWorkflow.

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
# Part 2: channel-slot registration/backstop probe (Task 8) -- CreateWhatsappWorkflow
# ---------------------------------------------------------------------------------
#
# CreateWhatsappWorkflow (id XuvOp7TxOImOAmlj) now runs, right after Vertical Prompt and
# before any template clone: If Has AppUserID? -> [true] Ensure Subscriber (upsert a
# trial row, on-conflict-do-nothing) -> Count Channels (alive bot_profiles count +
# plan/status) -> Compute Slot Limit -> If Slot Limit -> [over limit] Respond
# {success:false,error:"channel_limit"} (terminal -- matches this workflow's existing
# responseMode:"lastNode" contract: no template clone, no Wappi call, no new
# bot_profiles row) / [under limit] Register Profile (upsert bot_profiles) -> the
# untouched original chain (Get Sample Workflow -> ... -> Create Workflow -> Activate
# Created Workflow -> Set Wappi Webhook -> ...). [false / no AppUserID, old client] ->
# Get Sample Workflow directly, skipping all of the above.
#
# PRECONDITION this script cannot set up itself (Postgres lives behind n8n's own
# credential -- same constraint as Part 1): the dev `subscribers` table must already
# carry a row for CHANNEL_SLOT_APP_USER with plan='start' (limit 1 channel, per the n8n
# Compute Slot Limit map / PlanCatalog.MaxChannels) BEFORE running this. Seed/reset it
# with a one-off Manual-Trigger-then-Postgres workflow through the n8n-mcp tools (see
# the Task 8 report for the exact upsert used) -- there is no HTTP path to do this from
# the app side, it is server-only billing state.
#
# REAL SIDE EFFECTS, by design (this hits the real Create* webhook -- there is no
# dry-run mode): call 1 (allowed) creates AND ACTIVATES a real n8n workflow (named
# CHANNEL_SLOT_BOT_NAME below, so it is trivially findable for cleanup) and inserts one
# bot_profiles row; it then tries to configure a Wappi webhook against a profile id that
# does not exist on Wappi (confirmed empirically: POST .../api/webhook/url/set with a
# bogus profile_id -> real HTTP 400 {"detail":"Profile not found","status":"error"}),
# and the Set Wappi Webhook node has no onError override, so the WHOLE execution aborts
# right there -- the HTTP response to THIS call is therefore an n8n execution-error
# response, not the clean {"id": "..."} shape CreateWhatsappWorkflowFromStart parses.
# That is expected and NOT what this probe asserts; the workflow clone + bot_profiles
# row already exist by the time the abort happens (both precede Set Wappi Webhook in
# the chain), which is all the channel-slot logic needs to be exercised. Call 2 (over
# limit) terminates at Respond Channel Limit BEFORE ever reaching Get Sample
# Workflow/Create Workflow/any Wappi call, so its response IS the clean, reliable
# {"success":false,"error":"channel_limit"} JSON -- that IS asserted below.
#
# Cleanup (the created workflow clone + probe rows in subscribers/bot_profiles) is NOT
# done by this script -- same reasoning as Part 1's DB read-back: done by hand through
# the n8n-mcp tools once this prints, and recorded in the Task 8 report.
CHANNEL_SLOT_APP_USER = "probe_user_8"
CHANNEL_SLOT_BOT_NAME = "ZZZ_PROBE_TASK8_WA"
CREATE_WA_URL = BASE + "/webhook/CreateWhatsappWorkflow"


def channel_slot_form(fake_profile_id):
    """Field set mirrors Manager.CreateWhatsappWorkflowFromStart's WWWForm exactly
    (see Assets/Scripts/Main/Manager.cs)."""
    return {
        "Name": CHANNEL_SLOT_BOT_NAME,
        "BusinessType": "",
        "BusinessTypeId": "",
        "WhatsappProfileId": fake_profile_id,
        "TelegramWorkflowId": "-1",
        "Business": "",
        "Prompt": "",
        "ProductsList": "",
        "ServicesList": "",
        "AppUserID": CHANNEL_SLOT_APP_USER,
    }


def run_channel_slot_backstop_probe():
    print(f"channel-slot backstop probe -- app_user_id={CHANNEL_SLOT_APP_USER!r} against "
          f"{CREATE_WA_URL}\n(precondition: subscribers row for this user already "
          f"plan='start' -- see the module docstring's Part 2 section)\n")

    fake1 = f"probe_fake_wa_profile_{uuid.uuid4().hex[:8]}"
    status1, body1 = post_multipart_form(CREATE_WA_URL, channel_slot_form(fake1))
    print(f"[call 1 / allowed]     HTTP {status1} -- {body1[:300]}")

    time.sleep(2)   # let call 1's Postgres writes (Ensure Subscriber, Register Profile) settle

    fake2 = f"probe_fake_wa_profile_{uuid.uuid4().hex[:8]}"
    status2, body2 = post_multipart_form(CREATE_WA_URL, channel_slot_form(fake2))
    print(f"[call 2 / over limit]  HTTP {status2} -- {body2[:300]}")

    ok = status2 == 200 and "channel_limit" in body2
    tag = "OK" if ok else "FAIL"
    print(f"\n[{tag}] call 2 rejected with channel_limit: {ok}")
    print("\nThis script does not verify bot_profiles/n8n-workflow state or clean up -- "
          "see the Task 8 report for the DB read-back and cleanup performed via n8n-mcp.")
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument(
        "--channel-slot-backstop", action="store_true",
        help="Run the Task 8 channel-slot registration/backstop probe against "
             "CreateWhatsappWorkflow instead of the default RevenueCat Events probes. "
             "Read the module docstring's Part 2 section first -- it has real side "
             "effects (creates+activates a real n8n workflow) and needs the "
             "probe_user_8 subscribers row pre-seeded with plan='start'.")
    args = ap.parse_args()

    if args.channel_slot_backstop:
        sys.exit(run_channel_slot_backstop_probe())

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
