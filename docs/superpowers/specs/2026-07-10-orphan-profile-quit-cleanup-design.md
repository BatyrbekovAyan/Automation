# Orphaned Wappi Profile — Quit-Time Cleanup (2026-07-10)

## Problem

The bot-creation wizard creates a Wappi profile *before* auth (QR/pairing code). If the
app dies mid-wizard, that profile is orphaned. Today the only cleanup is a sweep in
`Manager.Start()` on the **next** launch — users who never reopen the app leak a profile
(a paid Wappi slot) forever. Ask: delete the orphan **before quitting**.

## Platform reality (constrains everything)

1. **The dominant mobile "quit" runs zero app code.** Swipe-kill from the app switcher
   and OS kills of a suspended process invoke no callback on iOS or Android. Nothing
   client-side can act at that moment.
2. **Backgrounding is NOT abandonment.** The pairing-code auth flow *requires* leaving
   the app: the code is displayed in-app and typed into WhatsApp/Telegram **on the same
   phone**. Deleting the pending profile in `OnApplicationPause(true)` would destroy the
   profile the user is actively authorizing — breaking the main happy path.
3. `OnApplicationQuit` fires on: Editor play-stop, desktop quit, Android clean Activity
   finish. It effectively **never fires on iOS** (no "Exit on Suspend").

So "delete before quitting" is implementable only for the clean-quit subset. Everything
else must be covered by the (kept) next-launch sweep, and — for users who never return —
by a future server-side TTL sweep (flagged as follow-up, out of scope here).

## Existing mechanism (kept, formalized)

A persistent "pending profile ledger" already exists as 4 global PlayerPrefs keys:
`lastCreated{Whatsapp,Telegram}ProfileId` (+ `...Saved`, default 1). `profile/add`
success writes id + Saved=0 (pending); a workflow claiming the profile writes Saved=1;
`profile/delete` success resets. `Manager.Start()` sweeps `Saved==0 && id!="-1"`.

Invariant: **Saved==0 ⇒ the profile is not referenced by any completed bot state and is
safe to delete at any time.** (Quit during the post-auth finalize window forfeits the
profile; the bot card survives unauthed and the user re-auths — this is the existing
sweep semantic, unchanged.)

## Approaches considered

- **A. Delete on `OnApplicationPause(true)`** — rejected: breaks pairing-code auth (#2).
- **B. Native schedulers (Android WorkManager / iOS BGTaskScheduler)** — WorkManager
  genuinely survives process death, but needs a native plugin + token storage; iOS has
  no reliable equivalent. Rejected for now; a server-side TTL sweep covers both
  platforms from one place and also covers crashes/battery-death. Follow-up.
- **C. Settle the ledger at quit time + keep the sweep** — chosen. `OnApplicationQuit`
  is the last moment code can run; fire the pending `profile/delete` requests there and
  block briefly (≤2 s) so the native transport can push them out. Same invariant as the
  sweep, executed earlier. Cheap, no new state, honest about coverage.

## Design (C)

1. **`PendingProfileLedger`** (new, `Assets/Scripts/Main/PendingProfileLedger.cs`) —
   static wrapper owning the 4 legacy keys **verbatim** (old installs' pending state
   still settles). API per channel: `MarkPending(id)` / `MarkClaimed()` /
   `ClearIfMatches(id)` / `TryGetPending(out id)`. Every write calls
   `PlayerPrefs.Save()` — an unflushed ledger entry after a process kill is an orphan
   the sweep can never find. `ClearIfMatches` (vs. the old unconditional clear) stops an
   unrelated delete (e.g. `Bot.DeleteBot`) from wiping a different pending entry.
2. **Manager.cs** — replace all raw key sites with ledger calls (create/delete
   coroutines, workflow claim sites, recreate-for-new-code claim, `Start()` sweep).
3. **Quit hook** — `Manager.OnApplicationQuit()` → `SettlePendingProfilesBeforeQuit()`:
   read ledger; issue `profile/delete` (WA `api/`, TG `tapi/`) with auth header;
   spin-wait (`Thread.Sleep(25)`) on `isDone` with a 2 s Stopwatch cap (coroutines are
   dead on the quit path; UnityWebRequest I/O runs on native threads, so `isDone` still
   flips); on confirmed success `ClearIfMatches` so the next-launch sweep doesn't
   re-fire; dispose (aborts anything still in flight — the OS was about to kill it
   anyway).
4. **`Manager.OnApplicationPause(true)`** — `PlayerPrefs.Save()` only (flush ledger +
   everything else before a potential kill). Explicitly **no deletion** here.
5. **Bug fix (load-bearing for the invariant):** `CreateTelegramWorkflowFromStart` had
   its success check inverted (`==` with the correct `!=` commented out beside it — a
   leftover debug flip). On success it deleted the just-authorized Telegram profile and
   never claimed the ledger; on failure it marked the bot active. Restored to match the
   WhatsApp twin.

## Error handling

- Quit-time delete fails/times out → ledger untouched → next-launch sweep retries.
- Delete succeeded but ledger clear didn't persist (kill inside the ≤2 s window) →
  sweep re-fires once, Wappi errors, ledger stays pending; retried each launch. Known
  bounded residue (can't verify Wappi's "profile not found" response shape to clear on
  it safely).
- `profile/add` response in flight at quit → id never reached the ledger; invisible to
  the client forever. Only a server-side TTL sweep can cover this.

## Testing

EditMode contract tests for the ledger (`Assets/Tests/Editor/Chat/PendingProfileLedgerTests.cs`,
matching UploadedFilesStoreTests conventions; snapshot/restore the real global keys
around each test since they aren't namespaced). The quit hook itself is thin glue over
the ledger + existing endpoints; verified manually in Editor (abandon wizard mid-auth →
stop play → log line + profile gone from Wappi dashboard).

## Out of scope / follow-ups

- **Server-side scheduled sweep** (n8n cron: list Wappi profiles, delete unauthorized
  ones older than ~24 h) — the only mechanism that covers "user never opens the app
  again", crashes, and iOS. Flagged as a background task.
  **Built 2026-07-10**: `Delete Orphan Profiles` (id `2islisFH7jjLoPQM`), active hourly on dev
  n8n — canonical JSON + policy/gotchas in `Tools/n8n/workflows/` + `Tools/n8n/README.md`.
- Android WorkManager variant — superseded by the server sweep if built.
