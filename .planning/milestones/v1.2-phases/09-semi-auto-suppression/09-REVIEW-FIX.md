---
phase: 09-semi-auto-suppression
fixed_at: 2026-07-22T16:40:30Z
review_path: .planning/phases/09-semi-auto-suppression/09-REVIEW.md
iteration: 1
findings_in_scope: 4
fixed: 4
skipped: 0
status: all_fixed
---

# Phase 09: Code Review Fix Report

**Fixed at:** 2026-07-22T16:40:30Z
**Source review:** .planning/phases/09-semi-auto-suppression/09-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 4 (fix_scope: critical_warning — 0 Critical, 4 Warning)
- Fixed: 4
- Skipped: 0

**Suite verification:** Full EditMode suite run via the in-Editor bridge AFTER all four
commits: **1200/1200 passed** (up from 1197 — the +3 are the new `TryGetOverride` tests,
which reference the new runtime API, proving both assemblies compiled fresh).

## Fixed Issues

### WR-01: On-open heal writes inherited bot-default state as a sticky per-chat override row

**Files modified:** `Assets/Scripts/Chat/SemiAutoStore.cs`, `Assets/Scripts/Chat/SuggestionsController.cs`, `Assets/Tests/Editor/Chat/SemiAutoStoreTests.cs`
**Commit:** 97cc79b
**Applied fix:** Added `SemiAutoStore.TryGetOverride(botId, chatId, out bool on)` exposing the
raw tri-state (returns false for raw 0 / inherited). `RestoreForActiveChat` now heals from the
raw tri-state: an explicit override (raw 1 or 2) re-asserts BOTH states (lost «Вместе» AND lost
«back to Авто» writes now self-heal); inherited chats (raw 0) push nothing and rely on the `'*'`
row alone — merely opening a chat can no longer mint a sticky per-chat server row. Corrected the
misleading line-120 comment and the `PushReplyModeForActiveChat` doc header. Added 3 EditMode
tests covering never-set-with-Semi-default (no override), explicit-ON, and explicit-OFF.
LOCKED absence→reply semantics preserved: never-toggled chats still write no per-chat row.

### WR-02: Late channel auth never seeds the `'*'` bot-default row for the new profile

**Files modified:** `Assets/Scripts/Main/Manager.ReplyModeSync.cs`, `Assets/Scripts/Main/Manager.cs`
**Commit:** 3b580f7
**Applied fix:** Added `Manager.SeedReplyModeDefaultForProfile(botId, profileId)` in the
ReplyModeSync partial — guards sentinel/blank ids, no-ops unless
`ReplyModeToggleBinder.GetMode(botId) == Semi` (Semi-only by design: absence already reads as
Авто), then fires the existing fire-and-forget `SyncReplyMode(new[]{profileId}, "*", true)`.
Called from all four workflow-creation success paths in `Manager.cs` (the moment a newly-authed
channel becomes live): `CreateWhatsappWorkflowFromStart`, `CreateWhatsappWorkflowFromEdit`,
`CreateTelegramWorkflowFromStart`, `CreateTelegramWorkflowFromEdit`, each right after the
`PendingProfileLedger.Mark*Claimed()` line. Auth flows are never blocked — the write is a
coroutine on the always-alive Manager singleton.

### WR-03: Set_Reply_Mode Validate does not harden inputs against the queryReplacement comma-split

**Files modified:** `Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json`
**Commit:** 34e186b
**Applied fix:** Hardened the Validate node's jsCode per the review:
`clean(s) = string && 1..128 chars && no comma` applied to every profileId and to chatId, plus
`.slice(0, 10)` fan-out cap (also covers IN-03). Verified: canonical JSON parses, embedded
jsCode is valid JS, and an 8-case behavioral smoke test passed (legit 2-profile and `'*'`
payloads pass through; comma-bearing chatId/profileId, sentinel-only, >128-char id, and
non-boolean suppressed are all rejected; 15-id fan-out capped at 10).
The deployer `Tools/n8n/build-set-reply-mode.py` imports the canonical JSON verbatim (no
embedded copy of the jsCode), so no deployer change was needed.

**OWNER ACTION REQUIRED:** the live dev instance was NOT touched (secrets deny-ruled). To apply
the hardened Validate node live, redeploy with:
`python3 Tools/n8n/build-set-reply-mode.py --update SCLcpn6DMDG3Z4VN`

### WR-04: Pre-existing silent-failure network paths in Manager.cs (advisory, partial by design)

**Files modified:** `Assets/Scripts/Main/Manager.cs`
**Commit:** 332f3a4
**Applied fix:** Scoped to the flagged branches only: `CreateWhatsappProfile` and
`CreateTelegramProfile` now set `www.timeout = 30` and log
`[Create*Profile] [{responseCode}] {url}: {error}` in the previously-empty failure branches —
a failed `profile/add` is no longer traceless. Deliberately NOT applied: (a) a user-visible
retry state (UI work that would balloon beyond the phase scope), and (b) the `UploadFile`
empty branches — that whole path is dead legacy code with no callers (IN-05 recommends
deletion, out of scope for this pass).

---

_Fixed: 2026-07-22T16:40:30Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
