---
phase: 08-device-uat-milestone-closeout
plan: 28
subsystem: ui
tags: [telegram, whatsapp, sync-window, playerprefs, syncing-cover, late-auth, gap-closure]

requires:
  - phase: 08-19 (D13a Telegram post-creation cover parity)
    provides: per-channel {bot}TelegramSyncUntil window + SyncUntilSuffixFor(Telegram) key contract + shared SyncingState cover gate (IsChannelSyncing) + Bot.DeleteBot key teardown
provides:
  - Late-Telegram-auth stamp of {bot}TelegramSyncUntil (same 300s window) in ShowAuthSuccess settings-reauth branch, so an existing-WhatsApp bot gets the Telegram post-creation sync cover when Telegram is authorized later (D16)
affects: [08-29 (round-5 device re-verify of D16), any future late-auth WhatsApp cover opt-in]

tech-stack:
  added: []
  patterns:
    - "Late-channel auth window stamp mirrors the wizard stamp (CreateBotFromForm) but is Telegram-gated (authPage == TelegramAuth) to preserve the WhatsApp byte-identical invariant"

key-files:
  created: []
  modified:
    - Assets/Scripts/Main/Manager.cs

key-decisions:
  - "Stamp ONLY on late TELEGRAM auth, NOT on late WhatsApp auth (documented parity decision, IN-01): a WhatsApp late-auth stamp would newly surface a WhatsApp cover where none has ever shown — an unrequested behaviour change that breaks the WhatsApp byte-identical milestone invariant"
  - "Reuse the already-tested {bot}TelegramSyncUntil key (SyncUntilSuffixFor(Telegram); Bot.DeleteBot already clears it) — no new key, no orphan, no new teardown"
  - "Stamp at the ShowAuthSuccess settings-reauth branch (the late-auth completion site reached by GetTelegramProfileStatus in both code + QR flows), not in the ChatManager sync gate or SyncingView — the gate + cover were already built in 08-19 and fire off the stamped key"

patterns-established:
  - "Late-channel post-creation window stamps live at the auth-completion site (ShowAuthSuccess), channel-gated, mirroring the wizard stamp verbatim"

requirements-completed: []

duration: ~2min
completed: 2026-07-20
---

# Phase 08 Plan 28: Late-Channel Telegram Sync Cover (D16) Summary

**A bot that already has WhatsApp now gets the Telegram post-creation sync cover when Telegram is authorized later — `ShowAuthSuccess`'s settings-reauth branch stamps `{bot}TelegramSyncUntil` (same 300s window) on a late Telegram auth, while WhatsApp stays byte-identical (no late-auth WhatsApp stamp, documented parity decision).**

## Performance

- **Duration:** ~2 min (single-file, single-branch stamp through the already-built 08-19 sync gate)
- **Started:** 2026-07-20T16:53:45Z
- **Completed:** 2026-07-20T16:56:18Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- Closed **D16** (round-4 device NEW, promotion of the documented 08-19 late-channel follow-up): the late-Telegram-auth success path now stamps `{bot}TelegramSyncUntil` (same 300s `ChatManager.WhatsAppSyncWindowSeconds`), so the shared `SyncingState` cover fires when the user opens the newly-authed Telegram channel on an existing-WhatsApp bot.
- WhatsApp is byte-identical: no late-auth WhatsApp stamp added (only the pre-existing wizard WhatsApp stamp survives) — the documented parity decision (IN-01) that keeps the milestone's WhatsApp byte-identical invariant.
- Reused the already-tested key contract (`SyncUntilSuffixFor(Telegram) == "TelegramSyncUntil"`, pinned by 08-19 `ChannelSyncGateTests`; `Bot.DeleteBot` already clears the key) — no new key, no orphan, no new teardown, no ChatManager/SyncingView change.

## Task Commits

Each task was committed atomically:

1. **Task 1: Stamp {bot}TelegramSyncUntil on late Telegram auth (settings re-auth)** — `75f87a8` (feat)

**Plan metadata:** (final docs commit — this SUMMARY + STATE.md + ROADMAP.md)

## Files Created/Modified

- `Assets/Scripts/Main/Manager.cs` — `ShowAuthSuccess` settings-reauth branch (`else if (!isCreatingBot && Manager.openBot != null)`) now stamps `{bot}TelegramSyncUntil` (now + `ChatManager.WhatsAppSyncWindowSeconds`, same 300s) guarded by `if (authPage == TelegramAuth)`, BEFORE the existing `StartCoroutine(ShowInteractiveSuccessMoment(...))`. Mirrors the wizard Telegram stamp at `Manager.cs:1490-1497` verbatim. No WhatsApp stamp added here (parity decision). The wizard stamps, the `moreAuthSteps` branch, `ShowInteractiveSuccessMoment`, and all ChatManager/SyncingView code are untouched.

## Decisions Made

- **Telegram-only late-auth stamp (parity decision, deliberate — per IN-01):** stamped ONLY on late TELEGRAM auth (`authPage == TelegramAuth`), NOT on late WhatsApp auth. Stamping WhatsApp here would newly surface a WhatsApp cover where none has ever shown — an unrequested behaviour change that breaks the WhatsApp byte-identical invariant this milestone holds. 08-19 kept exact no-stamp parity on both channels; D16 breaks it in Telegram's favour ONLY, because the owner reported the missing Telegram cover and the WhatsApp half is neither reported nor wanted (a future opt-in change if ever desired).
- **Stamp site = `ShowAuthSuccess` settings-reauth branch:** the late-auth completion reached by `GetTelegramProfileStatus → ShowAuthSuccess(TelegramAuth, ...)` in both the code and QR flows, where `Manager.openBot.name` is the key prefix. The sync gate + cover (built 08-19) already fire off the stamped key via `IsChannelSyncing` + `SyncingView.OnEnable` catch-up — no change needed there.

## Deviations from Plan

None - plan executed exactly as written. The single-branch stamp was inserted verbatim per the plan's `<action>`; all acceptance-criteria greps and the full suite passed on the first attempt.

## Testing

- **Baseline (pre-change):** full EditMode suite **1181/1181 green** (in-Editor `ClaudeTestBridge`, `test-summary.json` `finishedAt 12095.27`, `editorAssemblyWrittenUtc 16:49:23Z`), read fresh before editing.
- **Post-change:** full EditMode suite **1181/1181 Passed, 0 failures** — delta 0 as expected (coroutine/PlayerPrefs glue through the already-tested `SyncUntilSuffixFor`/`IsChannelSyncing` key contract; no new test). New run detected via `finishedAt` change (`12095.27 → 12416.97`).
- **Freshness gate (runtime-only edit):** verified via **Assembly-CSharp.dll mtime** (`1784566484` = 21:54:44 local), which postdates the edit epoch (`1784566473` = 21:54:33) by 11s → the Manager.cs change is compiled into the runtime assembly the suite ran against. `editorAssemblyWrittenUtc` correctly false-stales at `16:49:23Z` for a runtime-only change (per the plan's explicit note), so the dll mtime is the authoritative freshness signal here.

## Acceptance Criteria Verification

- `grep -cn "Manager.openBot.name + \"TelegramSyncUntil\"" Manager.cs` → **1** (the new late-auth stamp). ✅
- `grep -cn "\"TelegramSyncUntil\"" Manager.cs` → **2** total (wizard stamp + new late-auth stamp). ✅
- `grep -cn "newBot.name + \"WhatsappSyncUntil\"|openBot.name + \"WhatsappSyncUntil\"" Manager.cs` → **1** (ONLY the wizard WhatsApp stamp — NO new late-auth WhatsApp stamp; WhatsApp byte-identical). ✅
- Stamp is inside `else if (!isCreatingBot && Manager.openBot != null)` (line 1690), guarded by `if (authPage == TelegramAuth)` (line 1703), stamp at line 1708, precedes `StartCoroutine(ShowInteractiveSuccessMoment(...))` (line 1717). ✅
- Compiles clean (Assembly-CSharp.dll recompiled fresh; suite green). ✅

## Known Stubs

None — the change is a single PlayerPrefs write on an existing per-bot key namespace, no placeholder text, no hardcoded empty values wired to UI, no unwired data source.

## Threat Flags

None — no new network endpoint, secret, auth path, file access, or schema change. The one surface (a `{bot}TelegramSyncUntil` PlayerPrefs write) is inside the plan's threat model (T-08-28-01 fail-safe: fire gates require a valid active Telegram profile before the cover paints, so a stale window can't paint over the connect empty-state; T-08-28-02/03 accepted — no PII, one write on a rare user-driven auth-success event). WhatsApp behaviour is byte-identical.

## Issues Encountered

None — the in-Editor bridge picked up the trigger promptly (trigger consumed within seconds, run completed in ~24s) and returned a fresh green on the first attempt. The `editorAssemblyWrittenUtc` false-stale for runtime-only edits was anticipated by the plan and handled by falling back to the Assembly-CSharp.dll mtime freshness signal.

## Next Phase Readiness

- **08-29** (consolidated owner device re-verify of D2-view/D15/D16 on one build) can proceed for the D16 half: on-device, a WhatsApp-first bot that authorizes Telegram later should now show the Telegram post-creation sync cover when the user opens the Telegram channel; a late WhatsApp auth should show no new cover (WhatsApp byte-identical). That device confirmation rides 08-29.
- No blockers introduced. The sync gate + cover were already built in 08-19; this plan only added the missing late-auth stamp.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*

## Self-Check: PASSED

- `Assets/Scripts/Main/Manager.cs` exists on disk (modified file) + `08-28-SUMMARY.md` created.
- Task commit `75f87a8` exists in git log; contains ONLY `Assets/Scripts/Main/Manager.cs` (+20 lines, no deletions).
- Acceptance greps re-verified post-commit: 1 new `Manager.openBot.name + "TelegramSyncUntil"` stamp; 2 total `"TelegramSyncUntil"`; 1 `WhatsappSyncUntil` stamp (wizard only, WhatsApp byte-identical); stamp Telegram-gated inside the settings-reauth branch, before `ShowInteractiveSuccessMoment`.
- Full EditMode suite 1181/1181 green FRESH (Assembly-CSharp.dll mtime postdates the edit; runtime-only edit → dll mtime is the authoritative freshness signal, `editorAssemblyWrittenUtc` correctly false-stales).
