#!/usr/bin/env python3
"""Smoke probes for the RevenueCat_Events webhook mirror (Task 7, extended in Task 8-9).

Fires realistic RevenueCat webhook envelopes at the local dev n8n instance and checks
the HTTP contract: an unauthenticated request must be rejected, and every recognized
event type must return 200 -- INCLUDING CANCELLATION, whose Map Event Code node
deliberately returns zero items (n8n skips all downstream nodes for that run; see
RevenueCat_Events' Webhook node, responseMode "onReceived" -- the ack is sent the
instant the webhook fires, decoupled from whatever Map Event/Upsert Subscriber do
afterwards, which is what makes a zero-item run still answer 200 rather than 500).

This script only checks HTTP status codes. It does NOT read back the database --
Postgres lives behind n8n's own credential, unreachable from a plain script, so DB-
state verification (plan/status/topup_balance after the series) is a separate step:
a one-off Manual Trigger -> Postgres SELECT workflow, run and archived manually (see
Tools/n8n/README.md's Import/export section for the same one-off pattern used by the
2026-08-21 billing schema migration).

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
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

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


# Each probe: (label, event_body, with_auth, expected_codes (set), settle_seconds)
# settle_seconds gives the onReceived-mode background write a moment to land before
# the NEXT probe fires (probes b-e mutate the same row in sequence and must not race).
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


def main():
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
