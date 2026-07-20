---
phase: 08-device-uat-milestone-closeout
plan: 32
subsystem: ui
tags: [whatsapp, telegram, sync-window, playerprefs, syncing-cover, late-auth, gap-closure, d17]

requires:
  - phase: 08-19 (D13a Telegram post-creation cover parity)
    provides: per-channel {bot}...SyncUntil window + SyncUntilSuffixFor(WhatsApp/Telegram) key contract + shared SyncingState cover gate (IsChannelSyncing) + Bot.DeleteBot key teardown
  - phase: 08-28 (D16 late-channel Telegram sync cover)
    provides: the ShowAuthSuccess settings-reauth late-auth stamp site + the (now superseded) Telegram-only parity decision this plan mirrors + overrides
provides:
  - Late-WhatsApp-auth stamp of {bot}WhatsappSyncUntil (same 300s window) in ShowAuthSuccess settings-reauth branch, so an existing bot gets the WhatsApp post-creation sync cover when WhatsApp is authorized later (D17)
  - Cover parity for BOTH channels on every late-add (owner round-5 scope-override, supersedes the 08-28 Telegram-only parity decision)
affects: [08-33 (round-6 device re-verify of D17 + WA/TG invariants)]

tech-stack:
  added: []
  patterns:
    - "Late-channel auth window stamp mirrors the wizard stamp (CreateBotFromForm) but is channel-gated (authPage == WhatsappAuth / TelegramAuth) so each stamp writes only its own per-channel {bot}...SyncUntil key"

key-files:
  created: []
  modified:
    - Assets/Scripts/Main/Manager.cs

key-decisions:
  - "Cover parity for BOTH channels (D17, owner round-5 scope-override, owner-approved like D14): stamp on late auth of EITHER channel — SUPERSEDES the 08-28 Telegram-only parity decision (IN-01), which deliberately withheld the WhatsApp late-auth stamp to keep WhatsApp byte-identical. The owner overrode the byte-identical check for this behaviour: covers must show for both channels on every late-add."
  - "Reuse the already-tested {bot}WhatsappSyncUntil key (SyncUntilSuffixFor(WhatsApp) == \"WhatsappSyncUntil\"; Bot.DeleteBot already clears it at Bot.cs:203) — no new key, no orphan, no new teardown"
  - "Stamp at the ShowAuthSuccess settings-reauth branch (else if (!isCreatingBot && Manager.openBot != null)) via a sibling else if (authPage == WhatsappAuth), reached by the single GetWhatsappProfileStatus poller for BOTH the QR and pairing-code flows — not in the ChatManager sync gate or SyncingView (the gate + cover were already built in 08-19)"

patterns-established:
  - "Late-channel post-creation window stamps live at the auth-completion site (ShowAuthSuccess), channel-gated per authPage, mirroring the wizard stamp verbatim; both channels now stamped (WhatsApp + Telegram)"

requirements-completed: []

duration: ~3min
completed: 2026-07-20
---

# Phase 08 Plan 32: Late-WhatsApp-Auth Sync Cover Stamp (D17) Summary

**A bot that authorizes WhatsApp late now gets the WhatsApp post-creation sync cover — `ShowAuthSuccess`'s settings-reauth branch gains an `else if (authPage == WhatsappAuth)` sibling that stamps `{bot}WhatsappSyncUntil` (same 300s window) as the exact mirror of the 08-28 late-Telegram stamp, and the superseded Telegram-only parity comment is rewritten to the D17 both-channels rationale (owner scope-override).**

## Performance

- **Duration:** ~3 min (single-file, single-branch stamp through the already-built 08-19/08-28 sync gate + comment rewrite)
- **Started:** 2026-07-20T21:46:13Z
- **Completed:** 2026-07-20T21:49:33Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- Closed **D17** (round-5 owner SCOPE-OVERRIDE, owner-approved like D14): the late-WhatsApp-auth success path now stamps `{bot}WhatsappSyncUntil` (same 300s `ChatManager.WhatsAppSyncWindowSeconds`), so the shared `SyncingState` cover fires when the user opens the newly-authed WhatsApp channel on an existing bot — the exact mirror of the 08-28 late-Telegram stamp.
- Rewrote the superseded 08-28 Telegram-only parity comment to the D17 both-channels rationale: covers show for both WhatsApp and Telegram every time a channel is added late; each stamp is channel-gated by `authPage` and writes only its own per-channel key.
- Telegram late-auth stamp unchanged; reused the already-tested key contract (`SyncUntilSuffixFor(WhatsApp) == "WhatsappSyncUntil"`, pinned by 08-19 `ChannelSyncGateTests`; `Bot.DeleteBot` already clears it) — no new key, no orphan, no new teardown, no ChatManager/SyncingView change.

## Task Commits

Each task was committed atomically:

1. **Task 1: Stamp {bot}WhatsappSyncUntil on late WhatsApp auth + rewrite the cover-parity comment (D17)** — `b30718d` (feat)

**Plan metadata:** (final docs commit — this SUMMARY + STATE.md + ROADMAP.md)

## Files Created/Modified

- `Assets/Scripts/Main/Manager.cs` — `ShowAuthSuccess` settings-reauth branch (`else if (!isCreatingBot && Manager.openBot != null)`) gains an `else if (authPage == WhatsappAuth)` sibling AFTER the existing `if (authPage == TelegramAuth)` block, stamping `{bot}WhatsappSyncUntil` (now + `ChatManager.WhatsAppSyncWindowSeconds`, same 300s), BEFORE the existing `StartCoroutine(ShowInteractiveSuccessMoment(...))`. Mirrors the wizard WhatsApp stamp at `Manager.cs:1478-1485` (but keyed by `Manager.openBot.name`, not `newBot.name`). The superseded `// PARITY DECISION (deliberate)` comment (4 lines) is rewritten to the `// COVER PARITY (D17 ...)` both-channels rationale. The wizard stamps (1478-1497), the `moreAuthSteps` branch, the Telegram stamp, `ShowInteractiveSuccessMoment` (its `useTelegram: authPage == TelegramAuth` arg already resolves to `false` for a WhatsApp re-auth — correct), and all ChatManager/SyncingView code are untouched.

## Decisions Made

- **Both-channels cover parity (D17, owner round-5 scope-override, owner-approved like D14):** stamp on late auth of EITHER channel. This SUPERSEDES the 08-28 Telegram-only parity decision (IN-01), which deliberately withheld the WhatsApp late-auth stamp to preserve the WhatsApp byte-identical invariant. The owner explicitly overrode that check ("should be sync chats cover page for both channels every time they are just added") — so a late WhatsApp auth now surfaces the WhatsApp cover exactly as a late Telegram auth surfaces the Telegram cover.
- **Stamp site = `ShowAuthSuccess` settings-reauth branch, channel-gated by `authPage`:** the late-WhatsApp-auth completion is reached by `GetWhatsappProfileStatus → ShowAuthSuccess(WhatsappAuth, ...)` (Manager.cs:2259) in BOTH the QR and pairing-code flows (neither has a separate success site), where `isCreatingBot == false ⇒ moreAuthSteps` is false ⇒ the `:1690` branch is reached and `Manager.openBot.name` is the key prefix (08-REVIEW IN-02, verified). The sync gate + cover (built 08-19) already fire off the stamped key via `IsChannelSyncing` + `SyncingView.OnEnable` catch-up — no change needed there.
- **Reuse the already-tested WhatsApp key (no new key):** `SyncUntilSuffixFor(WhatsApp) == "WhatsappSyncUntil"` and `Bot.DeleteBot` already clears it (Bot.cs:203) — no new orphan, no new teardown.

## Deviations from Plan

None - plan executed exactly as written. The comment rewrite (A) and the WhatsApp sibling stamp (B) were inserted verbatim per the plan's `<action>`; all six acceptance-criteria greps and the full suite passed on the first attempt.

## Testing

- **Baseline (pre-change):** full EditMode suite **1184/1184 green** (in-Editor `ClaudeTestBridge`, `test-summary.json` `finishedAt 10707.45`), read fresh before editing.
- **Post-change:** full EditMode suite **1184/1184 Passed, 0 failures** — delta 0 as expected (a PlayerPrefs stamp glue through the already-tested `SyncUntilSuffixFor`/`IsChannelSyncing` key contract; no new test — mirrors 08-28/D16). New run detected via `finishedAt` change (`10707.45 → 11270.42`).
- **Freshness gate (runtime-only edit):** verified via **Assembly-CSharp.dll mtime** — forced a recompile by focusing the open Editor (`open -a Unity`), then confirmed the dll mtime advanced to `2026-07-20T21:47:46Z` (epoch `1784584066`), which postdates the edit epoch (`1784583973` = 21:46:13Z) → the Manager.cs change is compiled into the runtime assembly the suite ran against. `editorAssemblyWrittenUtc` correctly false-stales at `21:26:19Z` for a runtime-only change (per the plan's explicit note + project memory), so the dll mtime is the authoritative freshness signal here.

## Acceptance Criteria Verification

- `grep -c 'Manager.openBot.name + "WhatsappSyncUntil"' Manager.cs` → **1** (the new late-auth WhatsApp stamp). ✅
- `grep -c '"WhatsappSyncUntil"' Manager.cs` → **2** total (wizard `newBot.name + "WhatsappSyncUntil"` + new late-auth `Manager.openBot.name + "WhatsappSyncUntil"`). ✅
- `grep -c 'Manager.openBot.name + "TelegramSyncUntil"' Manager.cs` → **1** (late-Telegram stamp unchanged). ✅
- `grep -c "else if (authPage == WhatsappAuth)" Manager.cs` → **1** (new sibling branch inside the settings-reauth block). ✅
- `grep -c "PARITY DECISION (deliberate)" Manager.cs` → **0** (superseded comment rewritten); `grep -c "COVER PARITY (D17" Manager.cs` → **1**. ✅
- Compiles clean (Assembly-CSharp.dll recompiled fresh; suite green). ✅

## Known Stubs

None — the change is a single PlayerPrefs write on an existing per-bot key namespace, no placeholder text, no hardcoded empty values wired to UI, no unwired data source.

## Threat Flags

None — no new network endpoint, secret, auth path, file access, or scene mutation. The one surface (a `{bot}WhatsappSyncUntil` PlayerPrefs write) is inside the plan's threat model (T-08-32-01 fail-safe: the 08-19 WhatsApp sync gate fires the cover only inside the stamped window AND requires a valid active WhatsApp profile, `IsSyncingRawValue` fail-safes on an unparseable value, `Bot.DeleteBot` clears the key on teardown; the stamp writes only on a genuine WhatsApp auth-success settings re-auth with `isCreatingBot == false`. T-08-32-02 accepted — a per-bot unix-ms timestamp under an existing key namespace, no PII, no secret). Telegram behaviour and the wizard stamps are unchanged.

## Issues Encountered

None — the in-Editor bridge picked up the trigger promptly after the recompile (fresh run completed in ~30s) and returned green on the first attempt. The `editorAssemblyWrittenUtc` false-stale for runtime-only edits was anticipated by the plan and handled by focusing the Editor to force a recompile then falling back to the Assembly-CSharp.dll mtime freshness signal.

## Next Phase Readiness

- **08-33** (consolidated owner re-verify of D2-view/WR-02/D17/D15) can proceed for the D17 half: on-device, an existing bot that authorizes WhatsApp late should now show the WhatsApp post-creation sync cover when the user opens the WhatsApp channel — cover parity for both channels on every late-add. Device confirmation rides 08-33 (ONE Android build for the Gate A sweep, D17 cover + WA/TG invariants).
- No blockers introduced. The sync gate + cover were already built in 08-19; 08-28 added the late-Telegram stamp; this plan added the mirrored late-WhatsApp stamp and reconciled the parity comment.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*

## Self-Check: PASSED

- `Assets/Scripts/Main/Manager.cs` exists on disk (modified file) + `08-32-SUMMARY.md` created.
- Task commit `b30718d` exists in git log; contains ONLY `Assets/Scripts/Main/Manager.cs` (+12/-4, no deletions).
- Acceptance greps re-verified post-commit: 1 new `Manager.openBot.name + "WhatsappSyncUntil"` late-auth stamp; 2 total `"WhatsappSyncUntil"` (wizard + late-auth); 1 late-`"TelegramSyncUntil"` stamp unchanged; 1 `else if (authPage == WhatsappAuth)` sibling inside the settings-reauth branch; 0 `PARITY DECISION (deliberate)` (rewritten); 1 `COVER PARITY (D17`.
- Full EditMode suite 1184/1184 green FRESH (Assembly-CSharp.dll mtime `21:47:46Z` postdates the edit `21:46:13Z`; runtime-only edit → dll mtime is the authoritative freshness signal, `editorAssemblyWrittenUtc` correctly false-stales at `21:26:19Z`).
