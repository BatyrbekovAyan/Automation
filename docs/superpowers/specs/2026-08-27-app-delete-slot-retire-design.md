# App-delete slot retire — design

**Date:** 2026-08-27 · **Status:** approved (owner brief pre-authorized end-to-end implementation)
**Problem class:** billing correctness — deleting a bot in the app never retires its Supabase
`bot_profiles` rows, so the dead bot occupies a channel slot forever.

## Problem

Confirmed live on prod 2026-08-27: deleting bot «16» left its `bot_profiles` row alive
(`profile_id dec53892-d97f`, `bot_key Bot1`, `deleted_at null`); the next create was refused with
`error=channel_limit` and the row had to be retired by hand (`Tools/n8n/.secrets/fix-subscriber.py`).

Mechanics today:

- The Create handlers (`XuvOp7TxOImOAmlj` / `Uz6HBBUpAiUqVysB`) register a `bot_profiles` row per
  authed channel (`Register Profile`), and `Count Channels` counts `deleted_at is null` rows against
  the plan slot limit (`Compute Slot Limit`: trial 3 / start 1 / business 3 / network 5;
  expired/grace 0).
- Rows are retired only by: `Retire Same Bot Slot` (same app_user_id+channel+bot_key re-auths — the
  change-number path), and the two Profile Lifecycle Sweep branches.
- The app's delete path (`Bot.DeleteBot` → `Manager.DeleteBotFilesOnServer` →
  `/webhook/DeleteBotFiles` with `{botWaId, botTgId}`) deletes RAG chunks + stored originals only.
  `bot_profiles` is untouched.

## Approaches considered

**(a) Synchronous retire on the existing DeleteBotFiles webhook** — extend the JSON payload the app
already sends at delete time with the bot's Wappi profile ids (+ appUserId for audit), and retire
matching rows in the same workflow execution. **Chosen.**

**(b) Scheduled-sweep reconcile** — already exists: Profile Lifecycle Sweep Branch B retires alive
rows whose profile is absent from Wappi's `profile/all/get`. Three deliberate properties make it not
the fix for this gap: it **excludes `status='active'` owners entirely** (reviewed safety decision —
never mass-retire a paying customer's rows on Wappi-list ambiguity; the empty-list floor and the
proportional retire cap exist because that list is untrustworthy), it has a 1-hour age guard, and it
runs every 6h. So a paying owner's deleted bot is *never* reconciled, and even a trial owner who
deletes a bot to create a new one is blocked up to ~7h — fatal UX on the start plan (limit 1), where
delete-then-recreate is the *only* way to make a different bot. Extending Branch B to active owners
would reverse the reviewed fail-safe polarity for the exact accounts where a false retire costs real
money. Rejected as the primary fix; the sweep stays untouched as the backstop for non-active owners.

**(c) Self-healing gate** — on a `channel_limit` refusal, re-verify the counted rows against Wappi
live and recount. Rejected: puts a Wappi round-trip and a duplicate of Branch B's liveness logic
(with all its truncation traps) into the hot create path, and a truncated `profile/all/get` at gate
time could retire an ACTIVE owner's live rows — the exact failure the sweep's caps exist to prevent.

## Decision — (a), exact match by profile_id

The app knows *exactly* which profiles it is deleting (`Bot.whatsappProfileId` /
`telegramProfileId`), `bot_profiles.profile_id` is the primary key, and the delete moment is the
one place with zero ambiguity — no Wappi dependency, no rate concern, works for `status='active'`
owners, and frees the slot before the user can even reach the create wizard again.

**Match by `profile_id` only, never by `app_user_id`.** `BillingIdentity.AppUserId` falls back to
`SystemInfo.deviceUniqueIdentifier` while RevenueCat is still initializing, and RC anonymous ids can
rotate across reinstalls — a row created under one identity and deleted under another would MISS an
`app_user_id`-scoped retire, which is precisely the class of leak this fix closes. `appUserId` is
still sent, for the execution log's audit trail only.

## Server change — `Delete Bot Files` (`lmjYsdNcQA2IE5rl`) only

New chain: `Webhook` → **`Retire Bot Profiles`** → `Delete Bot Chunks` → `Respond` →
`Split File Ids` → `Delete Stored Original`.

- **`Retire Bot Profiles`** (Postgres, first after the webhook so a chunks-delete failure can never
  block the slot release; `onError: continueRegularOutput` so a billing-DB failure can never block
  the chunks sweep):

  ```sql
  with retired as (
    update bot_profiles
       set deleted_at = now(), deleted_reason = 'app_delete'
     where deleted_at is null
       and profile_id in ($1, $2)
       and profile_id not in ('-1', '')
     returning profile_id
  )
  select count(*)::int as "retiredProfiles" from retired;
  ```

  `queryReplacement` is ONE array expression (RevenueCat Events gotcha — comma-joined fragments
  stringify null): `={{ [ $json.body.waProfileId || '-1', $json.body.tgProfileId || '-1' ] }}`.
  The `|| '-1'` fallback makes an OLD client build (payload without the new fields) a clean no-op;
  the `('-1','')` guard is defense in depth on top of Register Profile's own M-3 guard. The final
  SELECT guarantees exactly one output item, so the chain can never die on a zero-row update
  (an n8n Postgres node emitting zero items would strand the webhook without a response).
- **`Delete Bot Chunks`**: `queryReplacement` re-pointed from `$json.body.*` to
  `$('Webhook').first().json.body.*` — its input item is now the retire node's output (which, on a
  tolerated retire error, is an error item with no `body` at all).
- **`Respond`** gains `"retiredProfiles": $('Retire Bot Profiles').first().json.retiredProfiles ?? null`
  (null = the retire node errored, distinguishable from 0 = nothing to retire).
- `deleted_reason = 'app_delete'` follows the established snake_case reason convention
  (`trial_expiry`, `churn_grace`, `liveness`; column from `Tools/n8n/sql/2026-08-21-deleted-reason.sql`).
- `Retire Same Bot Slot`, `Register Profile`, `Count Channels`, both sweeps: **untouched**.

## App change — Unity

- New pure seam `Assets/Scripts/Main/DeleteBotFilesPayload.cs`:
  `Compose(waWorkflowId, tgWorkflowId, waProfileId, tgProfileId, appUserId)` → JSON string or null.
  Normalizes null/empty → `"-1"` (the `Bot.UnauthedProfileSentinel` convention), returns null only
  when **all four** ids are sentinel (a bot with zero server-side trace). Today's guard is
  workflow-ids-only, which would skip the webhook for a bot whose channel authed but whose
  CreateWorkflow response was lost (row registered server-side, id still `"-1"` client-side).
- `Manager.DeleteBotFilesOnServer` gains the two profile-id parameters, composes via the seam with
  `BillingIdentity.AppUserId`, and posts the composed payload; `Bot.DeleteBot` passes
  `whatsappProfileId` / `telegramProfileId`. Same JSON-body POST (`Content-Type: application/json`),
  no multipart concerns.
- EditMode tests `Assets/Tests/Editor/Chat/DeleteBotFilesPayloadTests.cs` pin the composed VALUE
  (exact JSON for a canonical case — the composed-output gate), the sentinel normalization, the
  all-sentinel null, and the profile-only case that the old guard would have skipped.

## Probe — `probe-billing.py` Part 11, `--app-delete-retire`

Proves the brief's sequence value-level, on a start-plan (limit 1) **`status='active'`** subscriber —
active because that is the owner class BOTH sweep branches deliberately never touch, so the webhook
retire is the only thing that can pass this probe:

1. Seed `probe_user_20` (`plan='start'`, `status='active'`, period end +30d) via an EPHEMERAL
   webhook→Postgres harness (the `fix-subscriber.py` pattern: random 48-hex path, created via the
   n8n API, deleted in `finally` — same posture as the TASK16 harness, never left up).
2. Create №1 (fake WA profile id) → allowed; the clone + `bot_profiles` row land before the expected
   `Set Wappi Webhook` abort (Part 2's documented behavior). Read the clone's workflow id back by
   name via the API — the delete fire then carries it as a realistic `botWaId`.
3. Create №2 → must be the byte-exact `{"success": false, "error": "channel_limit"}`.
4. Fire `/webhook/DeleteBotFiles` exactly as the app would (`botWaId` = clone id, `botTgId` `"-1"`,
   `waProfileId` = fake profile, `tgProfileId` `"-1"`, `appUserId`) → response must be exactly
   `{"success": true, "deletedChunks": 0, "deletedFiles": 0, "retiredProfiles": 1}`.
5. SQL read-back: the row has `deleted_at is not null` AND `deleted_reason = 'app_delete'`.
6. Create №3 → NOT `channel_limit`, plus SQL read-back: exactly one alive row again (the new fake).
7. Cleanup: probe rows by SQL, ZZZ-named clones via the n8n API, harness deleted in `finally`.

API key resolution for the probe: `N8N_API_KEY` env → `Tools/n8n/.secrets/prod-api-key.txt` when
the base is choosereply.com → `secrets.json` `n8nAPIKey` (the dev fallback).

## Deployment order

1. Canonical `Tools/n8n/workflows/lmjYsdNcQA2IE5rl-Delete_Bot_Files.json` updated first.
2. PUT to prod `https://n8n.choosereply.com/api/v1/workflows/lmjYsdNcQA2IE5rl` (body
   `{name, nodes, connections, settings}` — the rotate-tunnel.py precedent; credential ids are
   preserved on prod, so the canonical refs bind as-is).
3. Run the probe against prod; clean up. (Local dev n8n gets the same JSON via the normal
   import flow when next used; dev and prod share the same Supabase, so the DB semantics are
   identical.)
4. Docs: README workflow-table row + CLAUDE.md webhook bullet updated to the new body/behavior.

## Residual gaps (accepted, documented)

- **Process killed mid-delete** on an active plan: the fire-and-forget webhook may be lost; the
  sweep never covers active owners, so the row lingers until hand-retire or plan lapse. Rare
  (delete is a foreground action; the POST races only a same-second swipe-kill) and bounded.
- **Channel logout** (`profile/logout`) keeps the row alive holding the slot; deliberate-ish — the
  bot still exists and re-auth reclaims the same slot via Register Profile's on-conflict resurrect.
  Long-term: the hourly orphan sweep deletes the unauthorized profile, then Branch B retires the row
  for non-active owners.
- **Unauthenticated webhook**: retiring needs a valid profile_id (unguessable UUID). Same v1
  posture as every other app webhook; folded into the existing "request auth at prod hardening"
  follow-up in the README.
- Register Profile's on-conflict resurrect leaves stale `deleted_reason` residue on a revived row —
  pre-existing, and unreachable from `'app_delete'` (an app-deleted profile is deleted on Wappi, so
  its profile_id can never re-register). Left alone.

## Rollback

Server: re-PUT the previous canonical JSON (one workflow, additive change). App: old clients are
unaffected either way (missing fields no-op); a rolled-back server simply ignores the new fields.
