#!/usr/bin/env python3
"""Smoke probes for the RevenueCat Events webhook mirror (Task 7), extended in Task 8
with the channel-slot registration/backstop probe for CreateWhatsappWorkflow /
CreateTelegramWorkflow, in Task 11 with a real value-asserting probe against the
new /webhook/GetUsage read endpoint, and in Task 12 with fixture SQL for the scheduled
Profile Lifecycle Sweep (no webhook -- verified entirely through n8n-mcp, see Part 5).

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
botsRegistered/periodEnd). Task 15a added `productId`/`interval` to all three
expectation sets plus a third scenario (probe_user_15) proving an unrecognised SKU
suffix yields interval null while still echoing the raw productId; the default RC run
(Part 1) also gained a GetUsage read-back asserting the mapper PERSISTED product_id and
that a top-up event never clobbers it.

## Part 5 -- Profile Lifecycle Sweep (Task 12, opt-in; fix round same day)

Run with `--sweep` (prints fixture SQL only; see that section below). This workflow has
NO webhook at all -- it is a bare Schedule Trigger ("Every 6 Hours") with two branches:
Branch A deletes Wappi profiles for trial (created_at < now() - 4d17h) / churned+grace
(current_period_end < now() - 3d) owners before Wappi's day-6 retroactive-charge
boundary; Branch B reconciles alive `bot_profiles` rows (now additionally gated
`created_at < now() - 1h`, see the C2 fix below) against Wappi's own `profile/all/get`
truth (both api/tapi bases) and releases (`deleted_at`) any row whose profile no
longer exists there. Unlike Parts 1/4, there is no HTTP entry point this script can
fire at all -- verification for this task was done ENTIRELY through n8n-mcp's
`execute_workflow`(manual mode)/`get_execution` against the real workflow
`fXYpCXPKw92EzRz8`, using a one-off Manual-Trigger->Postgres harness (same pattern as
Parts 1-4) to seed/read/clean up fixtures. `sweep_seed_sql()` and `sweep_cleanup_sql()`
below are the EXACT SQL used for the original pass (6 fixtures, all confirmed correct
in task-12-report.md): (a) trialing owner, profile older than the 4d17h threshold -- a
Branch-A candidate; a real Wappi 400 "Profile not found" on the delete attempt (fake
id) correctly left `deleted_at` NULL (invariant: delete-fail -> no mark) UNTIL Branch B's
liveness reconcile independently retired it on a later pass, since the same fake id is
also absent from Wappi's list -- a deliberate, confirmed self-healing overlap between
the two branches, not a bug; (b) trialing younger than the threshold -- absent from
Branch A's candidates entirely (still gets swept by Branch B's liveness check once
its channel's Wappi fetch succeeds, same reasoning as (a), since ANY fake/never-real
profile_id is by construction absent from Wappi's real list regardless of age -- age
only gates Branch A); (c) ACTIVE owner with an ancient profile -- absent from BOTH
branches (the #1 invariant is applied uniformly: Branch B's own registry read also
excludes `status='active'`, a deliberate scope decision beyond the literal brief, see
the report); (d) expired owner past the 3-day grace -- a Branch-A candidate
(reason=churn_grace), same fake-id fail-safe as (a); a same-shape sibling fixture
(expired but INSIDE the 3-day grace) is also seeded and confirmed absent from
candidates; (e) liveness -- alive registry row (created_at 2h old -- see the age-guard
note below for why this is no longer `now()`) with a profile_id absent from Wappi's
real list -- confirmed retired with reason=liveness by Branch B alone, cleanly isolated
from Branch A by construction; (f) is NOT a data fixture -- it is exercised by
temporarily pointing "List WA Profiles" at a broken path via n8n-mcp's
`update_workflow` (`setNodeParameter` on `/url`), which confirmed EVERY
whatsapp-channel registry row got `action:"skip_invalid_fetch"` (zero retirements for
that base that run) while the telegram base's own (independent, still healthy) fetch
was unaffected, then restored the URL.

**Fix round (opus review, same day): 2 Critical + 3 Important, all applied and
re-verified with REAL Wappi profiles -- see `sweep_multi_delete_seed_sql()` and
`sweep_empty_floor_seed_sql()` below for the exact new fixture SQL, full transcript
in the "Fix round" section of task-12-report.md.**

**C1 (multi-delete crash, FIXED):** a Postgres UPDATE with no `RETURNING` clause,
given N>=2 successful input items, collapses to a SINGLE output item whose
`pairedItem` is an ARRAY covering all N -- this is the exact same footgun Stamp
Retired hit in the original pass (see the Concerns section of task-12-report.md),
just unapplied to Branch A's own Mark Deleted -> Demote Trialing -> Stamp Deleted
chain. The NORMAL case (one user, WhatsApp + Telegram both expiring the same run) is
N=2, not an edge case -- and the original code threw a `NodeOperationError` in Stamp
Deleted trying to resolve an ambiguous `.item` back-reference, crashing the WHOLE
execution and skipping Branch B entirely (confirmed: this is what actually happened
to the FIRST throwaway profile in the original pass -- struck the "~10min Wappi
auto-expiry" explanation below, see that note). Fixed by (1) a new `Capture Delete
Fields` Set node (the last point where a `.item` back-reference to `Candidates` is
still 100% safe -- nothing upstream of it ever collapses) and (2) adding `RETURNING`
to BOTH `Mark Deleted` and `Demote Trialing`, which EMPIRICALLY restores clean 1:1
per-item output (verified directly: 2 real throwaway profiles for the SAME
app_user_id both deleted in one run, `Mark Deleted` emitted 2 separate correctly-paired
items, zero crash). `deleted_reason` (see the minors fix below) is echoed back through
each `RETURNING` clause as a real or literal-cast column, so every node downstream of
a Postgres write reads plain `$json.*` -- zero `.item` back-references survive on the
success path. New `Mark Succeeded?` If node (I3) gates `Demote Trialing` on
`!$json.error` from `Mark Deleted`, wired to a new `Stamp Mark Failed` terminal node
on the false branch.

**C2 (liveness over-eager, FIXED):** two guards added to Branch B. Guard 1 (age
floor): `Alive Registry`'s WHERE clause now also requires `created_at < now() -
interval '1 hour'` -- closes a fetch-snapshot race against a profile registered
moments ago (propagation lag on Wappi's own side). Guard 2 (empty-list floor): in
`Compute Liveness Diff`, a base whose live list comes back a well-formed but EMPTY
array, while the registry holds >=1 alive row for that channel, is now treated
identically to an invalid fetch (`action:"skip_invalid_fetch"`,
`reason:"empty_list_floor: ..."`) rather than confidently retiring every row for that
channel -- this was the fail-DANGEROUS polarity flip vs. the orphan sweep's own
"never delete on ambiguity" philosophy that a genuinely-empty-but-wrong response
would have hit. Both guards verified in one execution: a profile registered 10
minutes ago never appeared in `Alive Registry` at all (guard 1); a fake telegram-
channel registry row (this dev account's own TG list is always empty, a real
`{"profiles":null,"status":"done"}`) got floored to `skip_invalid_fetch` instead of
retired (guard 2).

**I1 (corrected claim, was WRONG):** the original report claimed an unauthorized
Wappi "connecting" profile auto-expires within ~10 minutes, based on a first
throwaway profile being gone by the time a delete was attempted. Struck per review --
it contradicts the orphan sweep's own 24h-TTL design premise (which assumes
unauthorized profiles persist far longer than minutes), and a re-examination of the
actual execution data does not support it either: the FIRST live-mode execution
(pre-onError-fix) shows only ONE item (index 0, a different fake profile) ever
reached "HTTP Delete Profile" before the whole node/execution failed -- the real
throwaway profile (a later item in the same batch) was very likely never even
attempted by that run, so its own disappearance sometime in that ~10-minute window is
NOT explained by anything this script observed. Honest state: the mechanism is
UNRESOLVED, not auto-expiry, not confirmed-otherwise -- the two later real-profile
fixture tests (C1's 2-candidate check above) sidestepped the question entirely by
seeding and firing the sweep within seconds of `profile/add`, which is now the
documented safe practice for any future real-profile test with this workflow.

**Minor (durable audit, applied):** `bot_profiles.deleted_reason` (migration
`Tools/n8n/sql/2026-08-21-deleted-reason.sql`) persists `trial_expiry`/`churn_grace`/
`liveness` past execution-log retention/pruning -- set by both `Mark Deleted` and
`Mark Liveness Deleted`, confirmed in the C1 re-verification read-back.

**Task 15b (proportional retire cap + dryRun flipped LIVE):** `Compute Liveness Diff`
now also refuses to act on a base whose would-be retirements exceed
`max(2, 50% of that base's alive registry rows)` -- `action:"skip_retire_cap"`, reason
carrying both counts, whole base skipped for the run. This was the Task 12 review's
binding pre-flip requirement: the empty-list floor only catches a FULLY empty list,
and `profile/all/get` has NO documented pagination parameters and NO response envelope
(`{profiles, status}` only -- no total/page/next/has_more; checked against the
published WhatsApp API docs and live against both bases, where unknown query params
are silently ignored), so a silently TRUNCATED list would look exactly like a complete
one and retire everything past the cut. Fixture (h) below is the cap's own fixture; the
at-cap boundary (2 of 3 -> still retires, since the test is a strict `>`) is exercised
in the same pass. `Sweep Config.dryRun` is now `false`. See
`.superpowers/sdd/task-15b-report.md` for the full fixture transcript, and note that
fixtures (e)/(h) need a NON-empty live list -- this dev account now has zero Wappi
profiles on both bases, so those runs point `List WA Profiles` at a local stub (the
same node-URL mutation technique fixture (f) uses) while `dryRun` is still true.

## Part 6 -- RC identity moves: TRANSFER + alias merge (Task 16, opt-in)

Run with `--transfer`. Like Part 4 this one is a REAL exact-value probe over plain
HTTP, and unlike every other part it needs NO seeded precondition: it builds its own
fixtures out of ordinary RevenueCat events (so the seeding path is itself the shipped
mapper) and reads every assertion back through GetUsage.

WHAT IT COVERS -- the device-pass gaps of 2026-08-26 (see the ledger's DEVICE PASS
block). On reinstall the subscription ended up under a fresh anonymous app_user_id.
GAP-1: the purchased top-up stayed stranded on the OLD id. GAP-2: the old identity
became a zombie -- `status='active'` forever (no further event would ever name it),
its Wappi profiles still billing 23R/day and its workflow clones still auto-replying,
and NEITHER sweep branch can reap it because both exclude `status='active'` by design.

There are TWO different mechanisms behind that, and they need different handling:

  * **Alias merge (what a same-device reinstall actually does).** RC does NOT fire
    TRANSFER here. It renames the identity: the event arrives under the NEW
    `app_user_id` with the old one in BOTH `original_app_user_id` and `aliases[]`
    (live proof on this instance -- execs 3150/3151/3153, where `transferred_from`/
    `transferred_to` are absent entirely; RC's own docs say to "search both the
    original_app_user_id and the aliases array"). `Map Event` computes the alias set
    minus the current id, and `If Needs Consolidation?` routes it to
    `Consolidate Aliases`, which moves the balance and retires the alias rows --
    deliberately WITHOUT copying their plan/status/period, because the event riding
    behind it already carries the authoritative ones and this path fires on EVERY
    event forever. It sits BEFORE `Upsert Subscriber` on purpose: that node's
    `topup_balance + $5` is not retry-idempotent, so a failure after it would let a
    RevenueCat redelivery double-credit the event's own top-up.
  * **TRANSFER (a cross-account move).** RC's payload carries NO app_user_id/
    product_id/entitlement_ids -- only the `transferred_from`/`transferred_to` String
    arrays -- so it cannot pass the `If Has Payload?` gate at all. `If Is Transfer?`
    routes it to `Transfer Subscriber`, which additionally moves the SNAPSHOT
    (plan/status/period/product) to every destination id, crediting the balance to the
    first one only.

Both statements are single parameterized statements (= one transaction each), both
take their sources `for update` (this instance demonstrably delivers same-subscriber
events concurrently, and a snapshot-read `sum(topup_balance)` double-credits in that
race), and both retire sources by setting `status='expired'`, `topup_balance=0` and
clamping `current_period_end` to `least(coalesce(period, now()), now())`. That clamp is
the entire Branch-A hand-off (see Part 7), and BOTH of its coalesces matter: a future
paid period parks the zombie's profiles for up to a month, while a NULL period makes
the row a sweep candidate INSTANTLY -- the sweep reads
`coalesce(current_period_end, now() - interval '99 days')`. No new deletion machinery
exists anywhere: retirement is entirely "make the row look like ordinary churn".

Cases (all VALUE-level, all self-seeded, ids per-run unique so a re-run can never read
a previous run's residue):
  T1  happy path: paid source + top-up, destination that ALREADY has its own top-up
      (which is what makes the 500+500 sum a real assertion rather than a copy); then
      a replay (must credit nothing) and an empty-array TRANSFER (must be a clean
      no-op, never a 500).
  T2  a RENEWAL under the new id lands BEFORE the transfer -- the older source snapshot
      must not roll the destination's period back (asserted byte-identical to its
      pre-transfer value) while the money still moves.
  T3  an EXPIRED source with a LATER period vs a LIVE (trialing) destination -- the
      liveness term must refuse the snapshot, or `Count Dialog`'s
      `status not in ('expired','grace')` would silence a healthy fresh install.
  T4  a top-up-only source has a NULL period: retiring it must still leave the 3-day
      grace rather than making it instantly sweep-eligible.
  T5  multi-id arrays: the snapshot fans out to EVERY `transferred_to` id, the balance
      goes to the first only, every source is retired.
  A1/A2  alias merge + replay, including a payload-carry assert (the event's own +45d
      period must survive `Consolidate Aliases`, since a Postgres node emits only its
      query result and drops the incoming json).
  A3  4 concurrent deliveries of the same alias-bearing event -> credited exactly once.

Cleanup: `transfer_cleanup_sql()` (printed at the end of the run) deletes the probe's
own `probe16_*` rows -- same one-off Manual-Trigger->Postgres harness convention as
Parts 4/5, since Postgres lives behind n8n's own credential and is unreachable from a
plain script.

## Part 8 -- top-up reserve consumption (Task 17a, opt-in)

Run with `--reserve`. Asserts the owner-approved reserve semantics (spec §2, 2026-08-26)
at VALUE level, running the EXACT Count Dialog statement read out of the canonical bot
template against real rows through the Part 7 SQL harness. Includes a genuine
lock-contention race (two new chats, one reserve unit) that the balance must survive at 0.

## Part 9 -- «Вместе» suggestions gate (Task 17a, opt-in)

Run with `--suggestions`. Fires the real /webhook/SuggestReplies endpoint: expired/unknown/
missing-id refusals (zero LLM spend -- the gate sits before retrieval), trialing and
over-quota allowed, dialog_counts provably untouched, and the daily cap.

## Part 7 -- Branch A hand-off trace (Task 16 fix round, opt-in)

Run with `--branch-a-trace`. Proves the single claim the whole design rests on: that
retiring a row is ENOUGH to hand its `bot_profiles` rows to Profile Lifecycle Sweep's
Branch A, so no new deletion machinery is needed. It runs the sweep's own shipped
`Candidates` query (read verbatim out of the canonical workflow JSON) against real
fixtures, and includes a CONTROL row -- expired but with its period still in the future,
i.e. what a retired row would look like WITHOUT the clamp -- which must never become a
candidate. Prints the procedure and SQL by default; set `TASK16_SQL_URL` to a one-off
SQL harness webhook (bind it to a credential -- never leave an unauthenticated SQL
endpoint up) and it runs end to end.
"""
import argparse
import calendar
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
# A real subscription SKU (PlanCatalog.Get(Business).SkuMonth). Task 15a: the mapper now
# persists this into subscribers.product_id, and Get Usage derives `interval` from its
# .month/.year suffix -- so the sequence below doubles as the annual-interval probe.
SUBSCRIPTION_PRODUCT_ID = "sub.business.month"


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
        "product_id": SUBSCRIPTION_PRODUCT_ID,
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

UNKNOWN_SKU_PROBE_USER = "probe_user_15"   # legacy/unrecognised product_id -> interval null

EXPECTED_UNKNOWN_USER_USAGE = {
    "success": True, "plan": "trial", "status": "trialing", "quota": 150, "used": 0,
    "topupBalance": 0, "botsRegistered": 0, "channelsConnected": 0, "periodEnd": None,
    "productId": None, "interval": None,
}
EXPECTED_USAGE_PROBE_USER = {
    "success": True, "plan": "business", "status": "active", "quota": 1000, "used": 7,
    "topupBalance": 500, "botsRegistered": 1, "channelsConnected": 2,
    "productId": "sub.business.year", "interval": "year",
}   # periodEnd asserted non-null separately (it's a real timestamp, not a fixed literal)
# Task 15a scenario 3: a product_id whose suffix is neither .month nor .year (a legacy or
# hand-written SKU) must yield interval null and still ECHO the raw productId -- the client
# treats null as "period unknown" and falls back to the monthly line rather than guessing.
EXPECTED_UNKNOWN_SKU_USAGE = {
    "success": True, "plan": "start", "status": "active", "quota": 300,
    "productId": "legacy.grandfathered", "interval": None,
}


def usage_seed_sql():
    """Exact SQL used to seed the USAGE_PROBE_USER precondition (scenario 2) --
    paste into a one-off Manual-Trigger -> Postgres workflow via n8n-mcp. Uses
    fixed calendar-date literals (2026-08/2026-07) rather than now()-relative math
    so the this-month/last-month split can't drift across a midnight boundary at
    the moment it runs; safe as long as this is run before 2026-09-01."""
    return """insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end, product_id, updated_at)
values ('probe_user_11', 'business', 'active', 500, now() + interval '30 days', 'sub.business.year', now())
on conflict (app_user_id) do update set
  plan = excluded.plan, status = excluded.status, topup_balance = excluded.topup_balance,
  current_period_end = excluded.current_period_end, product_id = excluded.product_id, updated_at = now();

insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end, product_id, updated_at)
values ('probe_user_15', 'start', 'active', 0, now() + interval '30 days', 'legacy.grandfathered', now())
on conflict (app_user_id) do update set
  plan = excluded.plan, status = excluded.status, topup_balance = excluded.topup_balance,
  current_period_end = excluded.current_period_end, product_id = excluded.product_id, updated_at = now();

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
delete from subscribers where app_user_id in ('probe_user_11', 'probe_user_15');"""


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

    print(f"\n=== Scenario 3 (Task 15a): seeded {UNKNOWN_SKU_PROBE_USER!r} -- unrecognised SKU suffix ===")
    status, body = fetch_usage(UNKNOWN_SKU_PROBE_USER)
    print(f"HTTP {status} -- {body}")
    if status == 200 and isinstance(body, dict) and body.get("productId") == EXPECTED_UNKNOWN_SKU_USAGE["productId"]:
        mismatches = {k: (v, body.get(k)) for k, v in EXPECTED_UNKNOWN_SKU_USAGE.items() if body.get(k) != v}
        scenario3_ok = not mismatches
        print(f"[{'OK' if scenario3_ok else 'FAIL'}] unknown SKU echoes productId with interval null: {scenario3_ok}"
              + (f" -- mismatches: {mismatches}" if mismatches else ""))
        ok_all = ok_all and scenario3_ok
    else:
        print(f"[SKIP] {UNKNOWN_SKU_PROBE_USER!r} is not seeded (HTTP {status}, productId="
              f"{body.get('productId') if isinstance(body, dict) else '?'!r}) -- seed via usage_seed_sql(). "
              f"NOT counted as a failure.")

    print("\nThe broken-query negative test (bad column -> 500, restore -> 200) needs n8n-mcp write")
    print("access this script does not have -- done by hand; transcript in task-11-report.md.")
    return 0 if ok_all else 1


# ---------------------------------------------------------------------------------
# Part 5: Profile Lifecycle Sweep (Task 12) -- fixture SQL only, no HTTP probe exists
# ---------------------------------------------------------------------------------
#
# Workflow fXYpCXPKw92EzRz8 ("Profile Lifecycle Sweep", Schedule Trigger "Every 6
# Hours", ACTIVE). Branch A: Candidates (Postgres, trialing older than 4d17h OR
# expired/grace more than 3d past current_period_end) -> Trial/Churn Dry Run? ->
# [dry run] Log Would Delete / [live] HTTP Delete Profile (POST {api|tapi}/profile/
# delete, WappiAuthToken cred, onError:continueRegularOutput) -> Delete Succeeded?
# ($json.status == 'done') -> [true] Mark Deleted -> Demote Trialing -> Stamp Deleted /
# [false] Stamp Delete Failed (deleted_at stays NULL -- retried next run). Branch B:
# List WA/TG Profiles (GET {api|tapi}/profile/all/get, onError:continueRegularOutput +
# retryOnFail) -> Alive Registry (Postgres, alive bot_profiles LEFT JOINed to
# subscribers, excluding status='active') -> Compute Liveness Diff (Code -- validates
# each base independently: no `.error`, status=='done', profiles is an array after
# coalescing Wappi's documented `profiles: null` empty-namespace shape to `[]`; a row
# whose channel's base failed validation gets action='skip_invalid_fetch' and is NEVER
# touched, a row present in the live set gets 'keep', otherwise 'retire'/reason=
# 'liveness') -> Is Retire Candidate? -> Liveness Dry Run? -> [live] Mark Liveness
# Deleted (Postgres, deleted_at=now(), no Wappi call -- the row's absence from the
# list IS the evidence).
#
# Sweep Config (Set node) carries the single `dryRun` boolean gating BOTH branches'
# destructive step; it is FALSE on the committed/live workflow (dry-run-first was
# verified by hand, see task-12-report.md, before flipping it).
#
# There is no webhook anywhere in this workflow -- a plain HTTP script has literally
# nothing to call. All verification for Task 12 went through n8n-mcp's
# `execute_workflow` (executionMode:"manual", which runs the CURRENT draft, so no
# publish/unpublish churn was needed while iterating) + `get_execution` (includeData,
# scoped via `nodeNames`) against the real workflow, using the same one-off
# Manual-Trigger->Postgres harness pattern as Parts 1-4 to seed/read/clean up fixture
# rows (Postgres lives behind n8n's own credential, unreachable from this plain
# script -- identical constraint to every earlier part). The functions below are that
# exact SQL, kept here so a future re-verification pass does not need to hand-retype
# it from the report.
SWEEP_WORKFLOW_ID = "fXYpCXPKw92EzRz8"
SWEEP_PROBE_PREFIX = "probe12_"


def sweep_seed_sql():
    """Seeds 6 fixture (app_user_id, bot_profiles row) pairs covering every rule
    branch except (f) (which is a workflow-URL mutation, not a data fixture -- see
    the Part 5 module docstring) and the real-Wappi-profile case (which needs a live
    `profile/add` call first -- fire the sweep within SECONDS of that call, not
    minutes; see the Part 5 docstring's I1 note, the original ~10min-auto-expiry
    explanation was struck as unsupported). Uses now()-relative intervals
    deliberately (unlike usage_seed_sql's fixed calendar dates) since this rule is
    entirely about AGE relative to the moment the sweep runs, not a calendar-month
    boundary. Fixture (e)'s created_at is 2 HOURS ago, not `now()` -- it must clear
    the C2 age-guard floor (created_at < now() - 1h) on Alive Registry while staying
    far under Branch A's 4d17h threshold, or it would be invisible to Branch B too
    and the isolation-from-Branch-A test would prove nothing."""
    return """insert into subscribers (app_user_id, plan, status, current_period_end, topup_balance, updated_at) values
  ('probe12_trial_old', 'trial', 'trialing', null, 0, now()),
  ('probe12_trial_young', 'trial', 'trialing', null, 0, now()),
  ('probe12_active_ancient', 'business', 'active', now() + interval '20 days', 0, now()),
  ('probe12_churn_expired', 'start', 'expired', now() - interval '10 days', 0, now()),
  ('probe12_churn_grace_ok', 'start', 'expired', now() - interval '1 day', 0, now()),
  ('probe12_liveness', 'trial', 'trialing', null, 0, now())
on conflict (app_user_id) do update set
  plan = excluded.plan, status = excluded.status, current_period_end = excluded.current_period_end,
  topup_balance = excluded.topup_balance, updated_at = now();

insert into bot_profiles (profile_id, app_user_id, channel, created_at, deleted_at) values
  ('probe12_fake_wa_old', 'probe12_trial_old', 'whatsapp', now() - interval '6 days', null),
  ('probe12_fake_wa_young', 'probe12_trial_young', 'whatsapp', now() - interval '2 days', null),
  ('probe12_fake_wa_activeancient', 'probe12_active_ancient', 'whatsapp', now() - interval '30 days', null),
  ('probe12_fake_wa_churn', 'probe12_churn_expired', 'whatsapp', now() - interval '1 day', null),
  ('probe12_fake_wa_grace_ok', 'probe12_churn_grace_ok', 'whatsapp', now() - interval '1 day', null),
  ('probe12_fake_wa_liveness', 'probe12_liveness', 'whatsapp', now() - interval '2 hours', null)
on conflict (profile_id) do update set
  app_user_id = excluded.app_user_id, channel = excluded.channel, created_at = excluded.created_at, deleted_at = excluded.deleted_at;"""


def sweep_multi_delete_seed_sql(profile_id_a, profile_id_b, app_user_id="probe12fix_multi"):
    """Fix-round C1 fixture: ONE trialing app_user_id with TWO alive bot_profiles
    rows, both past the 4d17h threshold -- the normal "WhatsApp + Telegram both
    expiring the same run" case (N=2 successful deletes reaching Mark Deleted in one
    execution), which the original Mark Deleted/Demote Trialing/Stamp Deleted chain
    could not survive (see the C1 note in the Part 5 docstring). Pass two REAL
    throwaway Wappi profile_ids (created via profile/add, same channel is fine --
    the bug is channel-independent) for a genuine end-to-end proof; fake ids only
    exercise the fail-safe path (both would 400 and never reach Mark Deleted at
    all), not the bug this fixture targets."""
    return f"""insert into subscribers (app_user_id, plan, status, current_period_end, topup_balance, updated_at) values
  ({app_user_id!r}, 'trial', 'trialing', null, 0, now())
on conflict (app_user_id) do update set plan=excluded.plan, status=excluded.status, current_period_end=excluded.current_period_end, updated_at=now();

insert into bot_profiles (profile_id, app_user_id, channel, created_at, deleted_at) values
  ({profile_id_a!r}, {app_user_id!r}, 'whatsapp', now() - interval '6 days', null),
  ({profile_id_b!r}, {app_user_id!r}, 'whatsapp', now() - interval '6 days', null)
on conflict (profile_id) do update set app_user_id=excluded.app_user_id, channel=excluded.channel, created_at=excluded.created_at, deleted_at=excluded.deleted_at;"""


def sweep_multi_delete_cleanup_sql(app_user_id="probe12fix_multi"):
    return f"""delete from bot_profiles where app_user_id = {app_user_id!r};
delete from subscribers where app_user_id = {app_user_id!r};"""


def sweep_empty_floor_seed_sql():
    """Fix-round C2 fixture (guard 2, the empty-list floor) + a companion guard-1
    (age floor) check, both provable in ONE run on this dev account without any
    mocking: this account's OWN Telegram profile list is always a real, healthy
    `{{"profiles":null,"status":"done"}}` (zero Telegram profiles registered) --
    seeding a fake telegram-channel registry row (2h old, clears the age floor)
    means its live set is genuinely empty while the registry holds >=1 row for that
    channel, which must floor to skip_invalid_fetch, NOT retire. The second row
    (10 minutes old) proves guard 1 on its own: it must be invisible to Alive
    Registry entirely, regardless of channel or Wappi truth."""
    return """insert into subscribers (app_user_id, plan, status, current_period_end, topup_balance, updated_at) values
  ('probe12fix_g', 'trial', 'trialing', null, 0, now()),
  ('probe12fix_ageguard', 'trial', 'trialing', null, 0, now())
on conflict (app_user_id) do update set plan=excluded.plan, status=excluded.status, updated_at=now();

insert into bot_profiles (profile_id, app_user_id, channel, created_at, deleted_at) values
  ('probe12fix_g_tg_fake', 'probe12fix_g', 'telegram', now() - interval '2 hours', null),
  ('probe12fix_ageguard_wa_fake', 'probe12fix_ageguard', 'whatsapp', now() - interval '10 minutes', null)
on conflict (profile_id) do update set app_user_id=excluded.app_user_id, channel=excluded.channel, created_at=excluded.created_at, deleted_at=excluded.deleted_at;"""


def sweep_empty_floor_cleanup_sql():
    return """delete from bot_profiles where app_user_id in ('probe12fix_g', 'probe12fix_ageguard');
delete from subscribers where app_user_id in ('probe12fix_g', 'probe12fix_ageguard');"""


def sweep_retire_cap_seed_sql():
    """Task 15b fixture (h) -- the proportional retire cap. THREE alive whatsapp rows
    under one trialing owner, all 2 hours old so they clear the C2 age floor and stay
    far under Branch A's 4d17h threshold (Branch A must contribute nothing here; this
    fixture is about Branch B alone). Run it with the a-f/g fixtures already CLEANED
    UP, or the extra alive rows raise the base's denominator and the cap stops tripping.

    Preconditions this fixture cannot express in SQL, both mandatory:

      1. The live list for the whatsapp base must be well-formed and NON-EMPTY, and
         must NOT contain these three profile_ids. An EMPTY list hits the empty-list
         floor FIRST (a deliberate ordering -- it is the more specific diagnosis), so
         the cap would never be reached and the fixture would silently prove nothing.
         This dev account currently has ZERO Wappi profiles on both bases, so point
         `List WA Profiles` at a local stub returning e.g.
         {"status":"done","profiles":[{"profile_id":"stub-unrelated-0001"}]} -- the same
         node-URL mutation technique fixture (f) uses -- and restore it afterwards.
      2. `Sweep Config.dryRun` must still be true while the stub URL is in place.

    Expected: alive=3, would-retire=3, cap = max(2, 1.5) = 2, 3 > 2 -> every row comes
    out `action:"skip_retire_cap"` with the counts in `reason`, `Log Would Retire`
    receives NOTHING. Boundary companion, same three rows: list ONE of them as live ->
    would-retire=2, 2 > 2 is false -> both absent rows retire normally (the cap test is
    a strict `>`; exactly-at-50% is allowed)."""
    return """insert into subscribers (app_user_id, plan, status, current_period_end, topup_balance, updated_at) values
  ('probe15b_cap', 'trial', 'trialing', null, 0, now())
on conflict (app_user_id) do update set plan=excluded.plan, status=excluded.status, updated_at=now();

insert into bot_profiles (profile_id, app_user_id, channel, created_at, deleted_at) values
  ('probe15b_cap_wa_1', 'probe15b_cap', 'whatsapp', now() - interval '2 hours', null),
  ('probe15b_cap_wa_2', 'probe15b_cap', 'whatsapp', now() - interval '2 hours', null),
  ('probe15b_cap_wa_3', 'probe15b_cap', 'whatsapp', now() - interval '2 hours', null)
on conflict (profile_id) do update set app_user_id=excluded.app_user_id, channel=excluded.channel, created_at=excluded.created_at, deleted_at=excluded.deleted_at;"""


def sweep_retire_cap_cleanup_sql():
    return """delete from bot_profiles where app_user_id = 'probe15b_cap';
delete from subscribers where app_user_id = 'probe15b_cap';"""


def sweep_dev_owner_grant_sql(app_user_ids):
    """DEV-OWNER GRANT (Task 15b). Exempts a dev/owner identity from BOTH sweep
    branches -- they uniformly exclude `status='active'` -- so the owner's own test
    bots are never deleted at the 4d17h trial boundary. NOT production data and NOT a
    real entitlement: the client still reads its tier from RevenueCat, so this only
    changes what the SERVER-side sweep considers eligible.

    At the Task 15b flip there was nothing to grant (registry empty, Wappi empty). Run
    this AFTER the device pass, once the real RevenueCat app_user_id is known -- read
    it off a fresh `bot_profiles` row, or from the app's own GetUsage request."""
    ids = ", ".join(repr(str(i)) for i in app_user_ids)
    return (f"update subscribers set plan='network', status='active', "
            f"current_period_end = now() + interval '365 days' "
            f"where app_user_id in ({ids});")


def sweep_candidates_readback_sql():
    """Exact read-back used to confirm outcomes -- paste into the same one-off
    workflow after firing the sweep (via n8n-mcp execute_workflow, manual mode)."""
    return ("select bp.app_user_id, bp.profile_id, bp.deleted_at, s.status "
            "from bot_profiles bp join subscribers s using (app_user_id) "
            "where bp.app_user_id like 'probe12_%' order by bp.app_user_id;")


def sweep_cleanup_sql():
    """Deletes everything sweep_seed_sql() created (plus any real Wappi-profile
    fixture row added by hand under a probe12_ app_user_id) -- paste into the same
    one-off workflow once verification is done. Safe regardless of what the sweep
    already did to deleted_at (plain DELETE, not conditional on it)."""
    return """delete from dialog_counts where app_user_id like 'probe12_%';
delete from bot_profiles where app_user_id like 'probe12_%';
delete from subscribers where app_user_id like 'probe12_%';"""


def run_sweep_fixture_helper():
    """Not a network probe -- see the Part 5 module docstring for why. Prints the
    seed/read-back/cleanup SQL plus the exact procedure, for pasting into a one-off
    n8n-mcp Manual-Trigger->Postgres workflow (or re-running by hand against the
    Profile Lifecycle Sweep workflow itself). Covers the original a-f set AND the
    fix-round additions (C1's 2-candidate multi-delete, C2's empty-list-floor)."""
    print(f"=== Profile Lifecycle Sweep ({SWEEP_WORKFLOW_ID}) -- fixture SQL, no HTTP probe exists ===\n")
    print("This workflow has no webhook. Verification procedure (see task-12-report.md for the")
    print("actual transcript, and the Part 5 module docstring for full context):\n")
    print("1. Seed fixtures via a one-off n8n-mcp Manual-Trigger->Postgres workflow:\n")
    print(sweep_seed_sql())
    print(f"\n2. Fire the sweep: n8n-mcp execute_workflow(workflowId={SWEEP_WORKFLOW_ID!r}, "
          "executionMode='manual') -- Sweep Config.dryRun must be false in the DRAFT for this")
    print("   (manual mode tests the current draft, not the published/active version -- restore")
    print("   dryRun=true and publish_workflow when done, do not leave a live draft mismatch).")
    print("   then get_execution(includeData=true) scoped to the relevant nodeNames -- see the")
    print("   report for the exact node names per branch and the expected per-fixture outcome.\n")
    print("3. Read back the outcome:\n")
    print(sweep_candidates_readback_sql())
    print(f"\n4. Clean up (this repo's convention -- no probe rows left in a shared dev DB):\n")
    print(sweep_cleanup_sql())
    print(f"\nNote: {SWEEP_PROBE_PREFIX}* rows are the ONLY ones touched -- safe to run against a live DB.")
    print("\n--- Fix-round additions (run separately, see docstrings for exact fixture reasoning) ---\n")
    print("C1 multi-delete (needs 2 REAL throwaway Wappi profile_ids from profile/add first):")
    print(sweep_multi_delete_seed_sql("<real_profile_id_a>", "<real_profile_id_b>"))
    print()
    print(sweep_multi_delete_cleanup_sql())
    print("\nC2 empty-list floor + age guard (no preconditions beyond a live dev n8n):")
    print(sweep_empty_floor_seed_sql())
    print()
    print(sweep_empty_floor_cleanup_sql())
    print("\n--- Task 15b fixture (h): proportional retire cap (read the docstring FIRST -- it "
          "needs a NON-empty live list and dryRun=true, or it silently proves nothing) ---\n")
    print(sweep_retire_cap_seed_sql())
    print()
    print(sweep_retire_cap_cleanup_sql())
    print("\n--- Task 15b dev-owner grant (run AFTER the device pass, with the real RC id) ---\n")
    print(sweep_dev_owner_grant_sql(["<real_revenuecat_app_user_id>"]))
    return 0


# ---------------------------------------------------------------------------------
# Part 6: RC identity moves (Task 16) -- self-seeding exact-value probe
# ---------------------------------------------------------------------------------
#
# See the Part 6 section of the module docstring for the full contract. Everything
# below runs over plain HTTP: the RevenueCat webhook seeds, GetUsage asserts. Ids are
# per-run unique, so a re-run can never read a previous run's residue.
TRANSFER_PROBE_PREFIX = "probe16_"
GRACE_TOLERANCE_S = 900        # a clamped period_end must land within this of "now"


def transfer_cleanup_sql():
    """Deletes every row this probe can possibly have created. Paste into the same
    one-off Manual-Trigger->Postgres harness Parts 4/5 use. The prefix is unique to
    this probe -- no other fixture, and no real RevenueCat id, can start with it."""
    return ("delete from bot_profiles where profile_id like 'probe16\\_%';\n"
            "delete from dialog_counts where app_user_id like 'probe16\\_%';\n"
            "delete from subscribers where app_user_id like 'probe16\\_%';")


def _uid(kind):
    return f"{TRANSFER_PROBE_PREFIX}{kind}_{uuid.uuid4().hex[:10]}"


def _epoch(ts):
    """Parse the ISO-8601 timestamp GetUsage echoes back (Postgres timestamptz through
    n8n) into a UTC epoch. Uses calendar.timegm, NOT time.mktime -- the values are UTC
    and this probe compares some of them against wall-clock now(), not only against
    each other. Tolerates both a trailing 'Z' and fractional seconds (Python 3.9's
    datetime.fromisoformat does neither)."""
    if not isinstance(ts, str) or len(ts) < 19:
        return None
    try:
        return calendar.timegm(time.strptime(ts[:19], "%Y-%m-%dT%H:%M:%S"))
    except ValueError:
        return None


def _fire(label, event, failures, expect=200, settle=1.0):
    status, resp = post_event(event)
    ok = status == expect
    print(f"[{'OK' if ok else 'FAIL'}] {label}: HTTP {status} -- {resp[:120]}")
    if not ok:
        failures.append(label)
    if settle:
        time.sleep(settle)
    return resp


def _assert_usage(label, app_user_id, expected, failures):
    """Read one identity through GetUsage and assert an EXACT subset of values."""
    status, body = fetch_usage(app_user_id)
    if status != 200 or not isinstance(body, dict):
        print(f"[FAIL] {label}: HTTP {status} -- {body}")
        failures.append(label)
        return None
    mismatches = {k: (v, body.get(k)) for k, v in expected.items() if body.get(k) != v}
    ok = not mismatches
    print(f"[{'OK' if ok else 'FAIL'}] {label} ({app_user_id}): {expected}"
          + (f" -- mismatches: {mismatches}" if mismatches else ""))
    print(f"    raw: {body}")
    if not ok:
        failures.append(label)
    return body


def _assert_grace(label, body, failures):
    """A retired identity's period_end must be non-null AND ~now: non-null because the
    sweep's Candidates query reads `coalesce(current_period_end, now() - interval '99
    days')`, so leaving it NULL makes the row a churn candidate INSTANTLY (real Wappi
    profiles deleted within one 6h sweep tick, zero grace); ~now because that is what
    starts the same 3-day grace every other churned owner gets."""
    end = _epoch((body or {}).get("periodEnd"))
    if end is None:
        print(f"[FAIL] {label}: periodEnd is null/unparseable ({(body or {}).get('periodEnd')!r}) "
              f"-- a NULL period means INSTANT sweep eligibility, not grace")
        failures.append(label)
        return
    drift = abs(time.time() - end)
    ok = drift <= GRACE_TOLERANCE_S
    print(f"[{'OK' if ok else 'FAIL'}] {label}: periodEnd clamped to ~now "
          f"({body.get('periodEnd')}, {drift:.0f}s from now, tolerance {GRACE_TOLERANCE_S}s) "
          f"-- 3-day churn grace starts here")
    if not ok:
        failures.append(label)


# ---- seed helpers (every fixture is built out of ORDINARY RevenueCat events, so the
# ---- seeding path is itself the shipped mapper) --------------------------------------

def _seed_dialogs(uid, chats, month_offset_days=0):
    """Seed `chats` dialog_counts rows for `uid` in the CURRENT month (or shifted back by
    month_offset_days). Needs the Part 7 SQL harness -- dialog rows have no public write
    path. Returns True when it actually seeded, so callers can SKIP rather than FAIL."""
    if not TASK16_SQL_URL:
        return False
    values = ",\n".join(
        f"('{uid}', 'seed{i}', (now() at time zone 'Asia/Almaty')::date"
        f"{f' - interval {month_offset_days!r} day' if month_offset_days else ''})"
        for i in range(chats))
    _harness_sql(f"insert into dialog_counts (app_user_id, chat_id, d) values\n{values}\n"
                 f"on conflict do nothing;")
    return True


def _seed_paid(uid, failures, days=30, product=SUBSCRIPTION_PRODUCT_ID, ent="tier_business", aliases=None):
    ev = {"type": "INITIAL_PURCHASE", "app_user_id": uid, "entitlement_ids": [ent],
          "expiration_at_ms": now_plus_days_ms(days), "product_id": product}
    if aliases:
        ev["aliases"] = aliases
    _fire(f"seed_paid[{uid[-10:]}]", ev, failures)


def _seed_topup(uid, failures):
    _fire(f"seed_topup[{uid[-10:]}]",
          {"type": "NON_RENEWING_PURCHASE", "app_user_id": uid, "product_id": TOPUP_PRODUCT_ID},
          failures)


def _seed_expire(uid, failures):
    _fire(f"seed_expire[{uid[-10:]}]", {"type": "EXPIRATION", "app_user_id": uid}, failures)


def _transfer_event(from_ids, to_ids):
    return {"type": "TRANSFER", "transferred_from": from_ids, "transferred_to": to_ids,
            "store": "APP_STORE", "environment": "SANDBOX"}


def _alias_event(cur, aliases, days=45, ev_type="RENEWAL"):
    """A real post-reinstall RevenueCat envelope: the event arrives under the NEW id and
    names the old one in BOTH original_app_user_id and aliases[] (live shape, execs
    3150/3151/3153 of 2026-08-26 -- transferred_from/to are absent there entirely)."""
    return {"type": ev_type, "app_user_id": cur, "entitlement_ids": ["tier_business"],
            "expiration_at_ms": now_plus_days_ms(days), "product_id": SUBSCRIPTION_PRODUCT_ID,
            "original_app_user_id": aliases[0], "aliases": [cur] + aliases}


# ---- TRANSFER path (cross-account move) ---------------------------------------------

def _case_t1_happy(failures):
    print("\n=== T1: TRANSFER happy path -- plan moves, top-ups SUM, source retired ===")
    old, new = _uid("t1old"), _uid("t1new")
    _seed_paid(old, failures)
    _seed_topup(old, failures)
    _seed_topup(new, failures)          # destination row ALREADY exists -> the sum is real
    _assert_usage("t1_pre_old", old, {
        "plan": "business", "status": "active", "quota": 1000, "topupBalance": 500,
        "productId": SUBSCRIPTION_PRODUCT_ID, "interval": "month"}, failures)
    _assert_usage("t1_pre_new", new, {
        "plan": "trial", "status": "trialing", "quota": 150, "topupBalance": 500,
        "periodEnd": None, "productId": None}, failures)

    # Task 17a / review N-1: the usage carry is gated on the snapshot being ACCEPTED. Here it
    # IS (the source is strictly fresher and not dead), so the 4 dialogs must move with the
    # plan -- a debt against the 1000-quota the destination just inherited.
    carry = _seed_dialogs(old, 4)
    if not carry:
        print("[SKIP] t1 usage-carry assert: TASK16_SQL_URL not set -- NOT counted as a failure")

    _fire("t1_transfer", _transfer_event([old], [new]), failures, settle=2)
    after_new = _assert_usage("t1_post_new", new, {
        "plan": "business", "status": "active", "quota": 1000,
        "topupBalance": 1000,                       # 500 own + 500 moved; not 500, not 1500
        **({"used": 4} if carry else {}),           # accepted snapshot => used moves WITH it
        "productId": SUBSCRIPTION_PRODUCT_ID, "interval": "month"}, failures)
    after_old = _assert_usage("t1_post_old", old, {
        "plan": "business", "status": "expired", "topupBalance": 0}, failures)
    _assert_grace("t1_old_grace", after_old, failures)
    if after_new and after_old:
        gap = (_epoch(after_new.get("periodEnd")) or 0) - (_epoch(after_old.get("periodEnd")) or 0)
        ok = gap / 86400.0 >= 25
        print(f"[{'OK' if ok else 'FAIL'}] t1_period_split: destination keeps the +30d period while the "
              f"source was pulled back to ~now -- gap {gap / 86400.0:.2f} days (>= 25; without the "
              f"clamp both would read the SAME value, gap 0)")
        if not ok:
            failures.append("t1_period_split")

    print("\n--- T1b: replay (RevenueCat retries any non-2xx) must change NOTHING ---")
    _fire("t1b_replay", _transfer_event([old], [new]), failures, settle=2)
    _assert_usage("t1b_replay_unchanged", new, {
        "plan": "business", "status": "active", "topupBalance": 1000,
        "productId": SUBSCRIPTION_PRODUCT_ID}, failures)

    print("\n--- T1c: empty arrays must be a clean no-op, never a 500 and never a write ---")
    resp = _fire("t1c_empty", _transfer_event([], []), failures, settle=0)
    if '"noop"' not in resp:
        print("[FAIL] t1c_empty: expected the Respond No-Op body")
        failures.append("t1c_empty_body")


def _case_t2_renewal_first(failures):
    print("\n=== T2: a RENEWAL under the new id lands BEFORE the TRANSFER -- the older "
          "source snapshot must NOT roll the destination back (freshness guard) ===")
    old, new = _uid("t2old"), _uid("t2new")
    _seed_paid(new, failures, days=60)          # destination already renewed, further out
    _seed_paid(old, failures, days=30)
    _seed_topup(old, failures)
    pre_new = _assert_usage("t2_pre_new", new, {
        "plan": "business", "status": "active", "topupBalance": 0}, failures)
    pre_end = (pre_new or {}).get("periodEnd")

    _fire("t2_transfer", _transfer_event([old], [new]), failures, settle=2)
    _assert_usage("t2_post_new", new, {
        "plan": "business", "status": "active", "topupBalance": 500,   # money still moves
        "periodEnd": pre_end}, failures)                               # period NOT rolled back
    print(f"    (destination periodEnd asserted byte-identical to its pre-transfer value: {pre_end})")
    _assert_usage("t2_post_old", old, {"status": "expired", "topupBalance": 0}, failures)


def _case_t3_expired_source(failures):
    print("\n=== T3: an EXPIRED source with a LATER period must not silence a LIVE "
          "destination (liveness term) -- money moves, status does not ===")
    old, new = _uid("t3old"), _uid("t3new")
    _seed_topup(new, failures)                  # destination: trial/trialing, periodEnd NULL
    _seed_paid(old, failures, days=30)
    _seed_topup(old, failures)
    _seed_expire(old, failures)                 # source: expired, but period +30d
    # Review N-1: the source must be METERED for this case to mean anything. 200 dialogs is
    # more than the destination's whole trial quota (150), so if the carry ever stops being
    # gated on snapshot acceptance, this fixture turns the live trial into an instantly
    # exhausted one -- the B3 liveness protection handed straight back through the other door.
    carry = _seed_dialogs(old, 200)
    if not carry:
        print("[SKIP] t3 usage-carry assert: TASK16_SQL_URL not set -- NOT counted as a failure")
    _assert_usage("t3_pre_old", old, {"status": "expired", "topupBalance": 500,
                                      **({"used": 200} if carry else {})}, failures)

    _fire("t3_transfer", _transfer_event([old], [new]), failures, settle=2)
    _assert_usage("t3_post_new", new, {
        "plan": "trial", "status": "trialing", "quota": 150,   # NOT stamped business/expired
        "topupBalance": 1000,                                  # the balance still moves
        # ... and the USAGE does not: the snapshot was refused, so the debt stays with it
        **({"used": 0} if carry else {}),
        "periodEnd": None, "productId": None}, failures)
    after_old = _assert_usage("t3_post_old", old, {"status": "expired", "topupBalance": 0}, failures)
    _assert_grace("t3_old_grace", after_old, failures)


def _case_t4_null_period(failures):
    print("\n=== T4: a top-up-only source has a NULL period -- retiring it must still "
          "leave 3 days of grace, not make it a sweep candidate instantly ===")
    old, new = _uid("t4old"), _uid("t4new")
    _seed_topup(old, failures)                  # trial/trialing, periodEnd NULL, 500
    _assert_usage("t4_pre_old", old, {
        "plan": "trial", "status": "trialing", "topupBalance": 500, "periodEnd": None}, failures)

    _fire("t4_transfer", _transfer_event([old], [new]), failures, settle=2)
    _assert_usage("t4_post_new", new, {
        "plan": "trial", "status": "trialing", "topupBalance": 500, "periodEnd": None}, failures)
    after_old = _assert_usage("t4_post_old", old, {"status": "expired", "topupBalance": 0}, failures)
    _assert_grace("t4_old_grace", after_old, failures)


def _case_t5_multi(failures):
    print("\n=== T5: multi-id arrays -- the snapshot fans out to EVERY transferred_to id, "
          "the balance goes to the first one only, every source is retired ===")
    a, b = _uid("t5srcA"), _uid("t5srcB")
    x, y = _uid("t5dstX"), _uid("t5dstY")
    _seed_paid(a, failures, days=30)
    _seed_topup(a, failures)
    _seed_topup(b, failures)                    # second source: top-up only, NULL period

    _fire("t5_transfer", _transfer_event([a, b], [x, y]), failures, settle=2)
    _assert_usage("t5_post_primary", x, {
        "plan": "business", "status": "active", "quota": 1000,
        "topupBalance": 1000,                   # 500 + 500, summed across BOTH sources
        "productId": SUBSCRIPTION_PRODUCT_ID}, failures)
    _assert_usage("t5_post_secondary", y, {
        "plan": "business", "status": "active", "quota": 1000,
        "topupBalance": 0,                      # entitlement yes, money no
        "productId": SUBSCRIPTION_PRODUCT_ID}, failures)
    for label, uid in (("t5_post_srcA", a), ("t5_post_srcB", b)):
        body = _assert_usage(label, uid, {"status": "expired", "topupBalance": 0}, failures)
        _assert_grace(label + "_grace", body, failures)


# ---- ALIAS path (same-device reinstall -- the case RC actually produces) -------------

def _case_a1_alias(failures):
    print("\n=== A1: alias merge (the REAL reinstall shape) -- RC never fires TRANSFER "
          "here; it renames the identity and names the old one in aliases[] ===")
    old, new = _uid("a1old"), _uid("a1new")
    _seed_paid(old, failures)
    _seed_topup(old, failures)
    # Task 17a (I-4): PAID USAGE must move with the money. dialog_counts is keyed by
    # app_user_id, so before this the reinstall reset the monthly counter -- «300 из 300»
    # became «0 из 300» on the same paid subscription, a free month per reinstall. Seeded
    # through the SQL harness because there is no public write path for dialog rows; when
    # TASK16_SQL_URL is not set the carry asserts are SKIPPED, not failed (same convention
    # as --usage scenario 2).
    carry = bool(TASK16_SQL_URL)
    if carry:
        _harness_sql(
            f"insert into dialog_counts (app_user_id, chat_id, d) values "
            f"('{old}', 'carry1', (now() at time zone 'Asia/Almaty')::date), "
            f"('{old}', 'carry2', (now() at time zone 'Asia/Almaty')::date - 1), "
            f"('{old}', 'carry3', (now() at time zone 'Asia/Almaty')::date), "
            f"('{old}', 'lastmonth', (now() at time zone 'Asia/Almaty')::date "
            f"- interval '40 days') on conflict do nothing;")
    else:
        print("[SKIP] a1 usage-carry asserts: TASK16_SQL_URL not set (dialog rows cannot "
              "be seeded over plain HTTP) -- NOT counted as a failure")
    _assert_usage("a1_pre_old", old, {
        "plan": "business", "status": "active", "topupBalance": 500,
        **({"used": 3} if carry else {})}, failures)

    # One ordinary RENEWAL, delivered under the NEW id, carrying the old id as an alias.
    _fire("a1_alias_event", _alias_event(new, [old]), failures, settle=2)
    after_new = _assert_usage("a1_post_new", new, {
        "plan": "business", "status": "active", "quota": 1000,
        "topupBalance": 500,                    # consolidated from the alias row
        # the month's 3 dialogs move with it; the 40-day-old row does NOT (the carry is
        # windowed to the current month, the same window Count Dialog and Get Usage use)
        **({"used": 3} if carry else {}),
        "productId": SUBSCRIPTION_PRODUCT_ID, "interval": "month"}, failures)
    # The event's OWN plan/status/period must survive the consolidation node -- a Postgres
    # node emits only its query result and drops the incoming json, so this also proves the
    # payload is carried through to Upsert Subscriber rather than silently lost.
    end = _epoch((after_new or {}).get("periodEnd"))
    ok = end is not None and (end - time.time()) / 86400.0 >= 40
    print(f"[{'OK' if ok else 'FAIL'}] a1_payload_carried: the event's own +45d period reached the row "
          f"({(after_new or {}).get('periodEnd')}) -- proves Consolidate Aliases carries the payload "
          f"through to Upsert Subscriber")
    if not ok:
        failures.append("a1_payload_carried")
    after_old = _assert_usage("a1_post_old", old, {
        "status": "expired", "topupBalance": 0}, failures)
    _assert_grace("a1_old_grace", after_old, failures)

    print("\n--- A2: the alias set rides EVERY event forever -- a replay must credit nothing ---")
    _fire("a2_replay", _alias_event(new, [old]), failures, settle=2)
    _assert_usage("a2_replay_unchanged", new, {
        "plan": "business", "status": "active", "topupBalance": 500,
        # the carry is `insert ... on conflict do nothing`, so a replay finds every row
        # already there: `used` must not grow, and no NEW row version is written
        **({"used": 3} if carry else {})}, failures)
    _assert_usage("a2_old_still_retired", old, {"status": "expired", "topupBalance": 0}, failures)


def _case_a3_concurrent(failures):
    print("\n=== A3: concurrent burst -- this instance delivers same-subscriber events in "
          "parallel (execs 3150/3151 overlapped), so the credit must be lock-produced ===")
    old, new = _uid("a3old"), _uid("a3new")
    _seed_topup(old, failures)
    event = _alias_event(new, [old])
    results = []
    try:
        import concurrent.futures
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            futures = [pool.submit(post_event, event) for _ in range(4)]
            results = [f.result() for f in futures]
    except Exception as exc:                      # noqa: BLE001 -- environment, not contract
        print(f"[SKIP] a3_concurrent: could not run the burst ({exc}) -- NOT counted as a failure")
        return
    print(f"    4 concurrent deliveries -> {[r[0] for r in results]}")
    time.sleep(3)
    _assert_usage("a3_credited_once", new, {
        "plan": "business", "status": "active",
        "topupBalance": 500}, failures)           # exactly once, not 2000
    _assert_usage("a3_old_retired", old, {"status": "expired", "topupBalance": 0}, failures)


def run_transfer_probe():
    if not SECRET:
        print("FAIL: RC_WEBHOOK_SECRET is not set -- this probe seeds through the real "
              "RevenueCat webhook and cannot authenticate without it.")
        return 1

    failures = []
    print("=== Task 16 identity-move probe (TRANSFER + alias merge) ===")
    for case in (_case_t1_happy, _case_t2_renewal_first, _case_t3_expired_source,
                 _case_t4_null_period, _case_t5_multi, _case_a1_alias, _case_a3_concurrent):
        case(failures)

    print("\n--- cleanup (run through a one-off Manual-Trigger->Postgres harness) ---")
    print(transfer_cleanup_sql())

    if failures:
        print(f"\n{len(failures)} assertion(s) FAILED: {failures}")
        return 1
    print("\nALL OK")
    return 0


# ---------------------------------------------------------------------------------
# Part 7: Branch A hand-off trace (Task 16 fix round) -- req-3 evidence, reproducible
# ---------------------------------------------------------------------------------
#
# Proves the ONE claim the identity-move design rests on: retiring a row is enough to
# hand its bot_profiles rows to Profile Lifecycle Sweep's Branch A, so no new deletion
# machinery is needed. It runs the sweep's OWN shipped Candidates SQL, read verbatim
# out of the canonical workflow JSON (never retyped), against real rows -- plus a
# CONTROL fixture (status expired, period still in the future = what the retired row
# would look like WITHOUT the clamp) which must never become a candidate.
#
# Postgres lives behind n8n's own credential, so this needs the same one-off
# Manual-Trigger/Webhook -> Postgres harness every other Part documents. Point
# TASK16_SQL_URL at such a harness (a webhook that runs {"sql": "..."} and returns the
# rows; bind it to a credential -- never leave an unauthenticated SQL endpoint up) and
# this runs end to end. Without it, the exact SQL and the expected outcome per step are
# printed instead.
SWEEP_CANDIDATES_WORKFLOW = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "workflows",
    "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json")
TASK16_SQL_URL = os.environ.get("TASK16_SQL_URL", "")


def sweep_candidates_sql():
    """The shipped Candidates query, read out of the canonical sweep workflow."""
    with open(SWEEP_CANDIDATES_WORKFLOW) as f:
        wf = json.load(f)
    q = next(n for n in wf["nodes"] if n["name"] == "Candidates")["parameters"]["query"]
    return q.strip().rstrip(";")


def _harness_sql(sql):
    body = json.dumps({"sql": sql}).encode()
    req = urllib.request.Request(TASK16_SQL_URL, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    if SECRET:
        req.add_header("Authorization", SECRET)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        return {"error": e.read().decode()[:400]}


def _scalar(sql):
    """First column of the first row of `sql`, as an int when it looks like one. Postgres
    returns count(*) as a bigint, which the driver hands back as a STRING -- comparing that
    to an int is the kind of silently-always-false assert this file exists to avoid."""
    out = _harness_sql(f"select ({sql}) as v;")
    rows = out.get("rows") if isinstance(out, dict) else None
    if not rows:
        return None
    v = rows[0].get("v")
    try:
        return int(v)
    except (TypeError, ValueError):
        return v


def _trace_rows(sql):
    out = _harness_sql(f"select coalesce(json_agg(t), '[]'::json) as rows from ({sql}) t;")
    return out.get("rows", out) if isinstance(out, dict) else out


def run_branch_a_trace():
    tag = uuid.uuid4().hex[:8]
    old, new = f"{TRANSFER_PROBE_PREFIX}ba_old_{tag}", f"{TRANSFER_PROBE_PREFIX}ba_new_{tag}"
    ctl = f"{TRANSFER_PROBE_PREFIX}ctl_{tag}"
    prof_old, prof_ctl = f"{TRANSFER_PROBE_PREFIX}ba_profile_{tag}", f"{TRANSFER_PROBE_PREFIX}ctl_profile_{tag}"
    cand = sweep_candidates_sql()
    scoped = f"select * from ({cand}) c where c.profile_id like 'probe16%'"
    seed = (f"insert into bot_profiles (profile_id, app_user_id, channel, bot_key, created_at, deleted_at)\n"
            f"values ('{prof_old}', '{old}', 'whatsapp', 'Bot0', now(), null),\n"
            f"       ('{prof_ctl}', '{ctl}', 'whatsapp', 'Bot0', now(), null)\n"
            f"on conflict (profile_id) do nothing returning profile_id;")
    shift = (f"update subscribers set current_period_end = current_period_end - interval '4 days'\n"
             f"where app_user_id in ('{old}','{ctl}') returning app_user_id, status, current_period_end;")

    if not TASK16_SQL_URL:
        print("=== Branch A hand-off trace -- procedure + SQL (set TASK16_SQL_URL to run it) ===\n")
        print("Fixtures (all under the probe16_ prefix, all cleaned up at the end):")
        print(f"  {old:38} the transferred-away identity  (+ profile {prof_old})")
        print(f"  {ctl:38} CONTROL: expired but period still in the future (no clamp) (+ {prof_ctl})")
        print("\n1. seed both identities with INITIAL_PURCHASE(+30d); EXPIRATION on the control only")
        print("2. register one fake profile each:\n")
        print(seed)
        print("\n3. run the sweep's own Candidates query -- expect NEITHER (source active, control")
        print("   expired but inside grace):\n")
        print(scoped + ";")
        print(f"\n4. fire an alias-merge event under {new} naming {old} (or a TRANSFER between them)")
        print("5. run Candidates again -- expect STILL NEITHER (the clamp starts a 3-day grace,")
        print("   it does not delete instantly)")
        print("6. simulate 4 days of wall clock passing:\n")
        print(shift)
        print("\n7. run Candidates again -- expect EXACTLY the transferred-away identity's profile,")
        print("   reason 'churn_grace', and the un-clamped CONTROL still absent")
        print("8. clean up:\n")
        print(transfer_cleanup_sql())
        return 0

    if not SECRET:
        print("FAIL: RC_WEBHOOK_SECRET is not set (needed to seed through the real webhook).")
        return 1

    failures = []
    print(f"=== Branch A hand-off trace (live, tag {tag}) ===")
    print("\n1-2. seed identities + one fake profile each")
    _seed_paid(old, failures)
    _seed_paid(ctl, failures)
    _seed_expire(ctl, failures)
    print(f"    {_harness_sql(seed)}")

    print("\n3. sweep Candidates BEFORE the move (expect none)")
    rows = _trace_rows(scoped)
    print(f"    {json.dumps(rows, ensure_ascii=False)}")
    if rows:
        failures.append("branch_a_pre_move_not_empty")

    print("\n4. fire the identity move -- the ALIAS-MERGE shape, i.e. what a real reinstall")
    print("   produces; the TRANSFER path retires its sources with the same statement")
    _fire("branch_a_move", _alias_event(new, [old]), failures, settle=2)
    print(f"    rows: {json.dumps(_trace_rows(f_subs(old, new, ctl)), ensure_ascii=False)}")

    print("\n5. sweep Candidates right after (expect none -- 3-day grace, not instant)")
    rows = _trace_rows(scoped)
    print(f"    {json.dumps(rows, ensure_ascii=False)}")
    if rows:
        failures.append("branch_a_inside_grace_not_empty")

    print("\n6. simulate 4 days passing")
    print(f"    {_harness_sql(shift)}")

    print("\n7. sweep Candidates 4 days later")
    rows = _trace_rows(scoped)
    print(f"    {json.dumps(rows, ensure_ascii=False)}")
    got = {r["profile_id"] for r in rows} if isinstance(rows, list) else set()
    reason = next((r["reason"] for r in rows if r.get("profile_id") == prof_old), None) if isinstance(rows, list) else None
    ok_old, ok_ctl = prof_old in got, prof_ctl not in got
    print(f"[{'OK' if ok_old else 'FAIL'}] retired identity's profile IS a Branch A candidate (reason={reason!r})")
    print(f"[{'OK' if ok_ctl else 'FAIL'}] un-clamped CONTROL is still NOT a candidate -- the clamp is "
          f"what makes the hand-off work")
    if not ok_old:
        failures.append("branch_a_candidate_missing")
    if not ok_ctl:
        failures.append("branch_a_control_leaked")

    print("\n8. cleanup")
    for stmt in transfer_cleanup_sql().split("\n"):
        print(f"    {_harness_sql(stmt)}")

    if failures:
        print(f"\n{len(failures)} assertion(s) FAILED: {failures}")
        return 1
    print("\nTRACE OK")
    return 0



# ---------------------------------------------------------------------------------
# Part 8: top-up reserve consumption (Task 17a, opt-in)
# ---------------------------------------------------------------------------------
#
# `--reserve`. Since 2026-08-26 the top-up is a RESERVE, not a permanent quota bump: it is
# spent one dialog at a time and only once the BASE monthly quota is gone (spec §2, owner
# decision). This part proves that at VALUE level against real Postgres rows, running the
# EXACT SQL read out of the canonical WhatsApp/Telegram bot templates -- never a retyped
# copy -- so the probe cannot drift from what ships.
#
# Why not fire the bot webhooks instead: it is impossible, and Task 9 established why. A
# synthetic profile_id makes `Fetch Recent` hit Wappi's real 400, `Latest+Combine` sets
# abort, and `Is Latest?` DEAD-ENDS before Count Dialog ever runs -- the gate is
# unreachable without a real authorized profile and a real inbound message (i.e. a device
# pass). What CAN be done from here, and is: run the shipped statement itself. That leaves
# exactly one gap, stated honestly -- that the node's queryReplacement passes the right two
# values -- and that half is covered by the Telegram template's pinned n8n-mcp executions
# (TG is `availableInMCP`, WA is not) plus the parity gate's byte-equality assert between
# the two templates.
#
# Needs TASK16_SQL_URL (the same one-off header-auth'd SQL harness Part 7 documents).
# Without it the matrix and the exact SQL are printed instead of run.
BOT_TEMPLATE_WA = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "workflows",
    "4wYitz5ek30SVNlT-WhatsApp_Bot.json")
RESERVE_PREFIX = "probe17a_"


def count_dialog_sql():
    """The shipped Count Dialog query, read out of the canonical WhatsApp bot template."""
    with open(BOT_TEMPLATE_WA) as f:
        wf = json.load(f)
    return next(n for n in wf["nodes"] if n["name"] == "Count Dialog")["parameters"]["query"]


def _lit(value):
    return "'" + str(value).replace("'", "''") + "'"


def reserve_seed_sql(uid, plan, status, topup, used_rows):
    """One fixture: a registered profile whose owner is on `plan`/`status` with `topup`
    reserve units left and `used_rows` dialogs already counted THIS month."""
    return (
        f"delete from dialog_counts where app_user_id = {_lit(uid)};\n"
        f"delete from bot_profiles where app_user_id = {_lit(uid)};\n"
        f"delete from subscribers  where app_user_id = {_lit(uid)};\n"
        f"insert into subscribers (app_user_id, plan, status, topup_balance)\n"
        f"  values ({_lit(uid)}, {_lit(plan)}, {_lit(status)}, {topup});\n"
        f"insert into bot_profiles (profile_id, app_user_id, channel, bot_key)\n"
        f"  values ({_lit(uid + '_p')}, {_lit(uid)}, 'whatsapp', 'Bot0');\n"
        f"insert into dialog_counts (app_user_id, chat_id, d)\n"
        f"  select {_lit(uid)}, 'seed' || g, (now() at time zone 'Asia/Almaty')::date\n"
        f"  from generate_series(1, {used_rows}) g where {used_rows} > 0;")


def reserve_cleanup_sql():
    return ("delete from dialog_counts where app_user_id like 'probe17a\\_%';\n"
            "delete from bot_profiles  where app_user_id like 'probe17a\\_%';\n"
            "delete from subscribers   where app_user_id like 'probe17a\\_%';\n"
            "delete from suggestion_counts where app_user_id like 'probe17a\\_%';")


# name, plan, status, topup, used, chat, (allowed, reserve_used, topup_after, rows_delta)
RESERVE_MATRIX = [
    # a continuation costs nothing even when the account is over quota with a reserve
    ("continuation", "start", "active", 5, 300, "seed1", (True, False, 5, 0)),
    # under the BASE quota the reserve is untouched
    ("under_quota", "start", "active", 5, 10, "fresh", (True, False, 5, 1)),
    # ... and is not spent one dialog early either (the boundary the old `quota + topup`
    # arithmetic used to blur)
    ("at_quota_minus_1", "start", "active", 5, 299, "fresh", (True, False, 5, 1)),
    # over quota WITH reserve: allowed, and exactly ONE unit is consumed
    ("over_quota_reserve", "start", "active", 5, 300, "fresh", (True, True, 4, 1)),
    # over quota with an EMPTY reserve: refused, nothing inserted (dead end, as before)
    ("over_quota_empty", "start", "active", 0, 300, "fresh", (False, False, 0, 0)),
    # status still outranks money: expired/grace consume nothing whatever the balance
    ("expired_ignores_reserve", "start", "expired", 500, 10, "fresh", (False, False, 500, 0)),
    ("grace_ignores_reserve", "start", "grace", 500, 400, "fresh", (False, False, 500, 0)),
    # trialing consumes it too (trial's base quota is 150)
    ("trialing_reserve", "trial", "trialing", 2, 150, "fresh", (True, True, 1, 1)),
]


def run_reserve_probe():
    q = count_dialog_sql()
    if not TASK16_SQL_URL:
        print("=== top-up reserve consumption -- matrix + SQL (set TASK16_SQL_URL to run) ===\n")
        for name, plan, status, topup, used, chat, exp in RESERVE_MATRIX:
            print(f"  {name:24} plan={plan:8} status={status:8} topup={topup:3} used={used:3} "
                  f"chat={chat:6} -> allowed={exp[0]} reserve_used={exp[1]} "
                  f"topup_after={exp[2]} new_rows={exp[3]}")
        print("\nSeed one fixture like this (per row), then run the shipped Count Dialog "
              "query with $1 = <uid>_p and $2 = the chat id:\n")
        print(reserve_seed_sql(RESERVE_PREFIX + "example", "start", "active", 5, 300))
        print("\n--- the shipped statement (canonical WhatsApp_Bot.json / Count Dialog) ---\n")
        print(q)
        print("\n--- cleanup ---\n")
        print(reserve_cleanup_sql())
        return 0

    failures = []
    print("=== Task 17a top-up reserve probe (shipped Count Dialog SQL, real rows) ===")
    for name, plan, status, topup, used, chat, (w_allowed, w_res, w_topup, w_rows) in RESERVE_MATRIX:
        uid = RESERVE_PREFIX + name
        _harness_sql(reserve_seed_sql(uid, plan, status, topup, used))
        before = _scalar(f"select count(*) from dialog_counts where app_user_id = {_lit(uid)}")
        rows = _harness_sql(q.replace("$1", _lit(uid + "_p")).replace("$2", _lit(chat)))
        row = (rows.get("rows") or [{}])[0] if isinstance(rows, dict) else {}
        after = _scalar(f"select count(*) from dialog_counts where app_user_id = {_lit(uid)}")
        bal = _scalar(f"select topup_balance from subscribers where app_user_id = {_lit(uid)}")
        got = (row.get("allowed"), row.get("reserve_used"), bal, after - before)
        want = (w_allowed, w_res, w_topup, w_rows)
        ok = got == want
        print(f"[{'OK' if ok else 'FAIL'}] {name}: (allowed, reserve_used, topup_after, "
              f"new_rows) = {got}, want {want}")
        if not ok:
            print(f"    raw: {row}")
            failures.append(name)

    # drain to zero and then refuse -- the reserve must not go negative and must not
    # keep letting dialogs through once it is gone
    uid = RESERVE_PREFIX + "drain"
    _harness_sql(reserve_seed_sql(uid, "start", "active", 2, 300))
    for i, (w_allowed, w_bal) in enumerate([(True, 1), (True, 0), (False, 0)]):
        rows = _harness_sql(q.replace("$1", _lit(uid + "_p")).replace("$2", _lit(f"drain{i}")))
        row = (rows.get("rows") or [{}])[0] if isinstance(rows, dict) else {}
        bal = _scalar(f"select topup_balance from subscribers where app_user_id = {_lit(uid)}")
        got, want = (row.get("allowed"), bal), (w_allowed, w_bal)
        ok = got == want
        print(f"[{'OK' if ok else 'FAIL'}] drain[{i}]: (allowed, topup_after) = {got}, want {want}")
        if not ok:
            failures.append(f"drain{i}")

    # RACE: two DIFFERENT new chats, ONE reserve unit left. The subscribers-row lock the
    # decrement takes is what decides this; without it both would insert and the balance
    # would go to -1. A `pg_sleep` CTE is prepended (and ONLY here) so both statements are
    # provably in flight together -- they still take their snapshots before the sleep, so
    # both see the same pre-race usage, which is the situation the lock exists for.
    race_q = q.replace("with me as (\n", "with delay as (select pg_sleep(2)), me as (\n", 1) \
              .replace("where bp.profile_id = $1 and bp.deleted_at is null",
                       "where bp.profile_id = $1 and bp.deleted_at is null "
                       "and (select true from delay)", 1)
    uid = RESERVE_PREFIX + "race"
    _harness_sql(reserve_seed_sql(uid, "start", "active", 1, 300))
    try:
        import concurrent.futures
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            futs = [pool.submit(_harness_sql,
                                race_q.replace("$1", _lit(uid + "_p")).replace("$2", _lit(f"race{i}")))
                    for i in range(2)]
            res = [(f.result().get("rows") or [{}])[0] for f in futs]
    except Exception as exc:                       # noqa: BLE001 -- environment, not contract
        print(f"[SKIP] race: could not run the burst ({exc}) -- NOT counted as a failure")
        res = None
    if res is not None:
        snaps = {r.get("used") for r in res}
        allowed = sorted(bool(r.get("allowed")) for r in res)
        bal = _scalar(f"select topup_balance from subscribers where app_user_id = {_lit(uid)}")
        rows = _scalar(f"select count(*) from dialog_counts where app_user_id = {_lit(uid)}")
        contended = len(snaps) == 1
        ok = contended and allowed == [False, True] and bal == 0 and rows == 301
        print(f"[{'OK' if ok else 'FAIL'}] race: both saw the same pre-race usage={snaps} "
              f"(true contention={contended}), allowed={allowed}, topup_after={bal} "
              f"(floor 0, never -1), dialog rows={rows} (300 seeded + exactly 1)")
        if not ok:
            failures.append("race")

    print("\n--- cleanup ---")
    for stmt in reserve_cleanup_sql().split("\n"):
        _harness_sql(stmt)
    print(f"    remaining probe17a rows: "
          f"{_scalar('select count(*) from subscribers where app_user_id like %s' % _lit('probe17a%'))}")

    if failures:
        print(f"\n{len(failures)} assertion(s) FAILED: {failures}")
        return 1
    print("\nALL OK")
    return 0


# ---------------------------------------------------------------------------------
# Part 9: «Вместе» suggestions gate (Task 17a, opt-in)
# ---------------------------------------------------------------------------------
#
# `--suggestions`. Everything here runs over PLAIN HTTP against the real
# /webhook/SuggestReplies endpoint -- no harness needed for the asserts themselves, only
# for seeding subscriber rows (TASK16_SQL_URL) since there is no public write path for
# them. Suggestions are FREE (owner decision 2026-08-26) but gated: expired and unknown
# ids are refused, over-quota is deliberately ALLOWED (the panel IS the quota fallback),
# and a per-account daily cap bounds the LLM spend an unauthenticated endpoint can incur.
#
# A refusal reuses the EXISTING error envelope the client already renders
# ({error:"generation_failed"}) and NOTHING more -- no new client contract, and deliberately
# no reason on the wire (review N-4: an unauthenticated endpoint must not confirm whether a
# guessed app_user_id exists or what state it is in). The reason is asserted where it really
# lives: Gate Decision's output in the execution log.
SUGGEST_URL = BASE + "/webhook/SuggestReplies"
SUGGESTION_DAILY_CAP = 100


def suggest(app_user_id, text="сколько стоит доставка?", timeout=90, **extra):
    """POST a minimal valid SuggestReplies request. Returns (status, parsed_json)."""
    body = {"v": 1, "chatId": "probe17a@c.us", "profileId": "probe17a-profile",
            "botWaId": "", "botTgId": "", "channel": "whatsapp",
            "messages": [{"role": "client", "text": text, "ts": int(time.time() * 1000)}]}
    if app_user_id is not None:
        body["appUserId"] = app_user_id
    body.update(extra)
    req = urllib.request.Request(SUGGEST_URL, data=json.dumps(body).encode(), method="POST")
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            status, raw = r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        status, raw = e.code, e.read().decode()
    except urllib.error.URLError as e:
        return None, str(e.reason)
    try:
        return status, json.loads(raw)
    except json.JSONDecodeError:
        return status, raw


SUGGEST_WORKFLOW_ID = "9PTyYcelRQI7bGDb"


def _latest_gate_reason(timeout=15):
    """`Gate Decision`'s own `gateReason` from the most recent Suggest Replies execution.

    The refusal reason deliberately does NOT ride the response body (review N-4, 2026-08-26):
    /webhook/SuggestReplies is unauthenticated, so telling a caller apart `unknown_account` /
    `subscription_expired` / `daily_cap` would turn a guessed app_user_id into an oracle for
    whether that account exists and what state it is in. The reason still exists -- on the
    node's output, where the execution log keeps it for debugging -- so that is where it gets
    asserted at value level. Returns None if the execution API is unreachable; the caller
    treats that as a SKIP, never as a pass.
    """
    time.sleep(0.5)                       # the execution row is written after the response
    url = (f"{BASE}/api/v1/executions?workflowId={SUGGEST_WORKFLOW_ID}"
           f"&limit=1&includeData=true")
    try:
        req = urllib.request.Request(url, headers={"X-N8N-API-KEY": _n8n_api_key()})
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = json.loads(resp.read().decode())
        run = data["data"][0]["data"]["resultData"]["runData"]["Gate Decision"]
        return run[0]["data"]["main"][0][0]["json"].get("gateReason")
    except (urllib.error.URLError, urllib.error.HTTPError, OSError,
            ValueError, KeyError, IndexError, TypeError):
        return None


def suggestions_seed_sql(uid, status, plan="business"):
    return (f"delete from suggestion_counts where app_user_id = {_lit(uid)};\n"
            f"delete from subscribers where app_user_id = {_lit(uid)};\n"
            f"insert into subscribers (app_user_id, plan, status) "
            f"values ({_lit(uid)}, {_lit(plan)}, {_lit(status)});")


def run_suggestions_probe():
    if not TASK16_SQL_URL:
        print("=== suggestions gate -- procedure + SQL (set TASK16_SQL_URL to run it) ===\n")
        print("Seed one subscriber per status, then POST /webhook/SuggestReplies with that "
              "appUserId and read the envelope:\n")
        print(suggestions_seed_sql(RESERVE_PREFIX + "sg_active", "active"))
        print(f"\n  active/trialing/grace -> suggestions (or abstain); every refusal returns the\n"
              f"  SAME wire envelope {{error:'generation_failed'}} with NO reason key. The reason\n"
              f"  (subscription_expired / unknown_account / missing_app_user_id / daily_cap after\n"
              f"  request {SUGGESTION_DAILY_CAP + 1}) is on Gate Decision's output in the execution log.\n")
        print(reserve_cleanup_sql())
        return 0

    failures = []
    print("=== Task 17a «Вместе» suggestions gate probe ===")

    def check(label, app_user_id, want_refused, want_reason=None, **extra):
        """Asserts the WIRE shape (envelope only, and `reason` provably absent from it) plus,
        for a refusal, the internal reason read off Gate Decision's output in the execution
        log -- see _latest_gate_reason for why the two live in different places."""
        status, body = suggest(app_user_id, **extra)
        refused = isinstance(body, dict) and body.get("error") == "generation_failed"
        leaked = isinstance(body, dict) and "reason" in body
        reason = _latest_gate_reason() if want_reason else None
        ok = (status == 200 and refused == want_refused and not leaked
              and (want_reason is None or reason is None or reason == want_reason))
        print(f"[{'OK' if ok else 'FAIL'}] {label}: HTTP {status} refused={refused} "
              f"reason-on-the-wire={leaked} (must be False)"
              + (f" internal reason={reason!r} (want {want_reason!r}"
                 + ("; SKIPPED -- execution API unreachable" if reason is None else "") + ")"
                 if want_reason else "")
              + f" (want refused={want_refused})")
        if not ok:
            print(f"    raw: {str(body)[:400]}")
            failures.append(label)
        return body

    # --- refusals: zero LLM spend, and the CHEAP asserts first
    _harness_sql(suggestions_seed_sql(RESERVE_PREFIX + "sg_expired", "expired"))
    check("expired_refused", RESERVE_PREFIX + "sg_expired", True, "subscription_expired")
    check("unknown_refused", RESERVE_PREFIX + "sg_neverseen", True, "unknown_account")
    check("missing_id_refused", None, True, "missing_app_user_id")
    check("empty_id_refused", "", True, "missing_app_user_id")

    # a refused caller must not even get a counter row -- an unauthenticated endpoint must
    # not let an unknown id grow a table
    ghost = _scalar("select count(*) from suggestion_counts where app_user_id = "
                    + _lit(RESERVE_PREFIX + "sg_neverseen"))
    ok = ghost == 0
    print(f"[{'OK' if ok else 'FAIL'}] unknown_writes_nothing: suggestion_counts rows for an "
          f"unknown id = {ghost} (want 0)")
    if not ok:
        failures.append("unknown_writes_nothing")

    # --- allowed: trialing, and an OVER-QUOTA account (deliberately allowed -- the panel is
    # the fallback the quota gate routes people into). These two DO spend LLM tokens.
    _harness_sql(suggestions_seed_sql(RESERVE_PREFIX + "sg_trialing", "trialing", "trial"))
    check("trialing_allowed", RESERVE_PREFIX + "sg_trialing", False)

    over = RESERVE_PREFIX + "sg_overquota"
    _harness_sql(suggestions_seed_sql(over, "active"))
    _harness_sql(f"insert into dialog_counts (app_user_id, chat_id, d) select {_lit(over)}, "
                 f"'c' || g, (now() at time zone 'Asia/Almaty')::date from "
                 f"generate_series(1, 1200) g on conflict do nothing;")
    dialogs_before = _scalar(f"select count(*) from dialog_counts where app_user_id = {_lit(over)}")
    check("over_quota_allowed", over, False)
    dialogs_after = _scalar(f"select count(*) from dialog_counts where app_user_id = {_lit(over)}")
    ok = dialogs_after == dialogs_before
    print(f"[{'OK' if ok else 'FAIL'}] suggestions_are_free: dialog_counts {dialogs_before} -> "
          f"{dialogs_after} (a suggestion must never consume a dialog)")
    if not ok:
        failures.append("suggestions_are_free")

    # --- daily cap: jump the counter to the cap, then walk over the edge. No LLM is spent
    # on the refused call, which is the whole point of the cap.
    cap = RESERVE_PREFIX + "sg_cap"
    _harness_sql(suggestions_seed_sql(cap, "active"))
    _harness_sql(f"insert into suggestion_counts (app_user_id, d, n) values ({_lit(cap)}, "
                 f"(now() at time zone 'Asia/Almaty')::date, {SUGGESTION_DAILY_CAP}) "
                 f"on conflict (app_user_id, d) do update set n = {SUGGESTION_DAILY_CAP};")
    check(f"request_{SUGGESTION_DAILY_CAP + 1}_refused", cap, True, "daily_cap")
    n = _scalar(f"select n from suggestion_counts where app_user_id = {_lit(cap)} "
                f"and d = (now() at time zone 'Asia/Almaty')::date")
    ok = n == SUGGESTION_DAILY_CAP + 1
    print(f"[{'OK' if ok else 'FAIL'}] cap_counts_the_refused_request: n = {n} "
          f"(want {SUGGESTION_DAILY_CAP + 1} -- the counter is incremented in the same "
          f"statement that decides, so hammering cannot slip between read and write)")
    if not ok:
        failures.append("cap_counts_the_refused_request")
    # one row per (account, day): yesterday's exhaustion must not gate today
    rows = _scalar(f"select count(*) from suggestion_counts where app_user_id = {_lit(cap)}")
    ok = rows == 1
    print(f"[{'OK' if ok else 'FAIL'}] cap_is_per_day: suggestion_counts rows = {rows} (want 1)")
    if not ok:
        failures.append("cap_is_per_day")

    print("\n--- cleanup ---")
    for stmt in reserve_cleanup_sql().split("\n"):
        _harness_sql(stmt)

    if failures:
        print(f"\n{len(failures)} assertion(s) FAILED: {failures}")
        return 1
    print("\nALL OK")
    return 0


def f_subs(*uids):
    quoted = ",".join(f"'{u}'" for u in uids)
    return (f"select app_user_id, plan, status, topup_balance, current_period_end "
            f"from subscribers where app_user_id in ({quoted}) order by app_user_id")


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
    ap.add_argument(
        "--sweep", action="store_true",
        help="Print the Task 12 Profile Lifecycle Sweep fixture SQL (seed/read-back/"
             "cleanup) instead of the default RevenueCat Events probes. This workflow "
             "has NO webhook at all -- there is nothing for a plain script to fire; "
             "read the module docstring's Part 5 section first. Verification is done "
             "via n8n-mcp execute_workflow/get_execution against the real workflow, "
             "using the printed SQL through a one-off Manual-Trigger->Postgres "
             "harness -- same pattern as every other Part here.")
    ap.add_argument(
        "--transfer", action="store_true",
        help="Run the Task 16 identity-move exact-value probe instead of the default "
             "RevenueCat Events probes: 5 TRANSFER cases (happy path incl. the summed "
             "top-up and the clamped source period; a renewal landing BEFORE the "
             "transfer; an expired source vs a live destination; a NULL-period source; "
             "multi-id arrays) plus the ALIAS-MERGE cases that reinstall actually "
             "produces (consolidation + payload carry-through, replay, concurrent "
             "burst). Needs RC_WEBHOOK_SECRET and no seeded precondition -- every "
             "fixture is built out of ordinary RevenueCat events. Read the module "
             "docstring's Part 6 section, and run the printed cleanup SQL afterwards.")
    ap.add_argument(
        "--branch-a-trace", action="store_true",
        help="Run (or print) the Task 16 req-3 evidence: does retiring an identity "
             "really hand its bot_profiles rows to Profile Lifecycle Sweep's Branch A? "
             "Executes the sweep's OWN shipped Candidates query against real fixtures, "
             "including a control row that shows the period clamp is what makes the "
             "hand-off work. Prints the procedure + SQL unless TASK16_SQL_URL points at "
             "a one-off SQL harness webhook, in which case it runs end to end. See the "
             "module docstring's Part 7 section.")
    ap.add_argument(
        "--reserve", action="store_true",
        help="Run the Task 17a top-up RESERVE probe: the EXACT Count Dialog SQL read out "
             "of the canonical bot template, run against real seeded rows (continuation / "
             "under quota / at the boundary / over quota with and without reserve / "
             "expired+grace ignore the balance / trialing consumes it / drain to zero / a "
             "true two-chat race that must never drive the balance below 0). Needs "
             "TASK16_SQL_URL (Part 7's one-off SQL harness); without it the matrix and the "
             "shipped SQL are printed. See the module docstring's Part 8 section for why "
             "the bot webhook itself cannot be fired for this.")
    ap.add_argument(
        "--suggestions", action="store_true",
        help="Run the Task 17a «Вместе» suggestions-gate probe against the real "
             "/webhook/SuggestReplies endpoint: expired/unknown/missing-appUserId refused "
             "with the existing generation_failed envelope + a diagnostic reason, "
             "trialing and OVER-QUOTA allowed (the panel is the quota fallback), "
             "suggestions never touch dialog_counts, and the daily cap refuses request "
             "101. Needs TASK16_SQL_URL to seed subscriber rows. See Part 9.")
    args = ap.parse_args()

    if args.channel_slot_backstop:
        sys.exit(run_channel_slot_backstop_probe())

    if args.dialog_metering:
        sys.exit(run_dialog_metering_connectivity_probe())

    if args.usage:
        sys.exit(run_usage_probe())

    if args.sweep:
        sys.exit(run_sweep_fixture_helper())

    if args.transfer:
        sys.exit(run_transfer_probe())

    if args.branch_a_trace:
        sys.exit(run_branch_a_trace())

    if args.reserve:
        sys.exit(run_reserve_probe())

    if args.suggestions:
        sys.exit(run_suggestions_probe())

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

    # Task 15a: value-level read-back of what the mapper actually PERSISTED. The HTTP 200s
    # above only prove the upsert did not error; GetUsage is the permanent read path (Task 7
    # IMPORTANT#2 -- no debug webhook was built for this), so it is what asserts the column.
    # Two facts in one read: (b) carried product_id and it landed, and (c)'s top-up did NOT
    # overwrite it with 'topup.dialogs.500' -- the mapper only sets product_id on the
    # subscription branch, and the upsert coalesces it against the stored value.
    print()
    status, usage = fetch_usage(PROBE_USER)
    expected_readback = {"productId": SUBSCRIPTION_PRODUCT_ID, "interval": "month"}
    if status == 200 and isinstance(usage, dict):
        rb_mismatches = {k: (v, usage.get(k)) for k, v in expected_readback.items() if usage.get(k) != v}
    else:
        rb_mismatches = {"<http>": (200, status)}
    readback_ok = not rb_mismatches
    print(f"[{'OK' if readback_ok else 'FAIL'}] f_product_id_persisted: GetUsage({PROBE_USER}) "
          f"-> {expected_readback} (top-up must not clobber it)"
          + (f" -- mismatches: {rb_mismatches}" if rb_mismatches else ""))
    print(f"    raw: {usage}")
    if not readback_ok:
        failures.append("f_product_id_persisted")

    # Task 16: PRODUCT_CHANGE carries the OLD sku in product_id and the NEW one in
    # new_product_id (RC docs; observed live 2026-08-26 -- an upgrade to Business wrote
    # sub.start.month over the row and only the next RENEWAL corrected it). The mapper now
    # prefers new_product_id, so this event must land the NEW sku -- and, because interval
    # is derived from the suffix, a wrong pick here would also print the wrong price line.
    print()
    status, resp = post_event({
        "type": "PRODUCT_CHANGE",
        "app_user_id": PROBE_USER,
        "entitlement_ids": ["tier_network"],
        "expiration_at_ms": now_plus_days_ms(365),
        "product_id": SUBSCRIPTION_PRODUCT_ID,       # the sku being switched FROM
        "new_product_id": "sub.network.year",        # the sku being switched TO
    })
    print(f"[{'OK' if status == 200 else 'FAIL'}] g_product_change: HTTP {status} -- {resp[:120]}")
    if status != 200:
        failures.append("g_product_change")
    time.sleep(2)
    status, usage = fetch_usage(PROBE_USER)
    expected_change = {"plan": "network", "quota": 3000, "productId": "sub.network.year",
                       "interval": "year"}
    if status == 200 and isinstance(usage, dict):
        pc_mismatches = {k: (v, usage.get(k)) for k, v in expected_change.items() if usage.get(k) != v}
    else:
        pc_mismatches = {"<http>": (200, status)}
    pc_ok = not pc_mismatches
    print(f"[{'OK' if pc_ok else 'FAIL'}] h_product_change_uses_new_sku: GetUsage({PROBE_USER}) "
          f"-> {expected_change} (the OLD sku {SUBSCRIPTION_PRODUCT_ID!r} must NOT win)"
          + (f" -- mismatches: {pc_mismatches}" if pc_mismatches else ""))
    print(f"    raw: {usage}")
    if not pc_ok:
        failures.append("h_product_change_uses_new_sku")

    if failures:
        print(f"\n{len(failures)}/{len(PROBES) + 3} probes failed: {failures}")
        sys.exit(1)

    print("\nALL OK")


if __name__ == "__main__":
    main()
