---
phase: 08-device-uat-milestone-closeout
plan: 20
subsystem: ui
tags: [telegram, whatsapp, chat-list, sync-pill-removal, scene-surgery, deletion-only]

# Dependency graph
requires:
  - phase: 08-16 (round-2 device re-verify)
    provides: D13 owner decision "Cover only, remove pill" (this plan = half b, D13b)
  - phase: 08-19 (D13a)
    provides: the WhatsApp-parity Telegram post-creation cover that supersedes the pill
  - phase: 08-09 / 08-12
    provides: the pill being removed (indicator + events 08-09; min-visible gate 08-12)
provides:
  - "D9 «Синхронизация…» pill fully removed: scene object + ChatListSyncIndicator.cs + ChatListSyncIndicatorGate.cs + ChatListSyncIndicatorBuilder.cs + ChatListSyncIndicatorGateTests.cs (+ all .meta) + ChatManager pill-only plumbing (OnChatListSyncStart/OnChatListSyncEnd events + invokes, IsChatListSyncing getter)"
  - "_chatListSyncing field + duplicate-sync guard (RefreshActiveBotChats) + all three switch/clear resets PRESERVED — grep-proven sole-subscriber boundary before deletion"
affects: [08-21 device re-verify]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Supersede-and-delete: when a parity affordance (08-19 cover) replaces a stopgap widget, remove the widget end-to-end (scene + runtime + editor + tests + events) in one plan, with an evidence-before-delete consumer grep guarding shared state"

key-files:
  created: []
  modified:
    - Assets/Scenes/Main.unity
    - Assets/Scripts/Main/ChatManager.cs
  deleted:
    - Assets/Scripts/UI/ChatListSyncIndicator.cs (+ .meta)
    - Assets/Scripts/Chat/ChatListSyncIndicatorGate.cs (+ .meta)
    - Assets/Editor/ChatListSyncIndicatorBuilder.cs (+ .meta; deleted AFTER its new Remove entry stripped the scene object)
    - Assets/Tests/Editor/Chat/ChatListSyncIndicatorGateTests.cs (+ .meta)

key-decisions:
  - "Scene mutation went through the Editor (orchestrator mcp-unity delete_gameobject + save_scene), NOT a text edit of Main.unity — the open Editor held the pill in its in-memory scene and would have re-materialized it on the next save"
  - "Corrected the plan's stale test-baseline arithmetic: expected full suite = 1142 (08-19's 1134 + 8) − 6 deleted gate tests = 1136, not the plan's 1124 − 6 = 1118"
  - "Stopped compile-retry attempts at the fix limit: the blocking failure is an environmental Unity Bee BuildProgram crash (exit 134, Interop.Sys.GetGroups overflow) with ZERO C# errors — no repo change can fix it; documented recovery instead"

patterns-established: []

requirements-completed: []

# Metrics
duration: ~55min (incl. ~5min orchestrator scene checkpoint + ~15min compile-crash triage)
completed: 2026-07-17
---

# Phase 8 Plan 20: Remove the D9 «Синхронизация…» Pill (D13b) Summary

**The D9 Telegram sync pill is gone end-to-end — scene object stripped via the open Editor, four scripts + tests deleted, ChatManager's pill-only events/getter removed — while the `_chatListSyncing` duplicate-sync guard and its three resets are preserved byte-identically; the 08-19 WhatsApp-parity cover is now Telegram's sole sync affordance.**

## Performance

- **Duration:** ~55 min wall (execution ~35 min; orchestrator-assisted scene checkpoint ~5 min; Bee-crash triage + retries ~15 min)
- **Started:** 2026-07-17T15:40Z (20:40 local)
- **Completed:** 2026-07-17T16:37Z (21:37 local)
- **Tasks:** 2
- **Files modified:** 2 modified + 8 deleted (4 .cs + 4 .meta)

## Accomplishments

- **Task 1:** Added a delete-only `Remove`/`RemoveHeadless` entry to `ChatListSyncIndicatorBuilder` (reusing `DestroyAllByName` + the name constants), then the scene object `Screen_Whatsapp/ChatsPanel/ChatListSyncIndicator` (fileID 1662871242 + Spinner/Label children) was removed through the OPEN Editor by the orchestrator via mcp-unity (`delete_gameobject` + `save_scene`) and committed immediately — no sibling clobber (GUID-verified).
- **Task 2:** Deleted all four pill files (+ .meta) via `git rm` and surgically stripped the pill-only ChatManager plumbing; `_chatListSyncing` and every consumer the plan ordered preserved is intact and grep-verified.

## Consumer-Boundary Grep (evidence-before-delete, required by plan)

`grep -rn "OnChatListSyncStart\|OnChatListSyncEnd\|IsChatListSyncing" Assets --include="*.cs"` (pre-deletion) matched ONLY:

- `Assets/Scripts/UI/ChatListSyncIndicator.cs` — the sole real subscriber/reader (18 hits: subscribe/unsubscribe, catch-up read, comments) — **deleted by this plan**
- `Assets/Tests/Editor/Chat/ChatListSyncIndicatorGateTests.cs` — 1 comment hit — **deleted by this plan**
- `Assets/Scripts/Chat/ChatListSyncIndicatorGate.cs` — 1 comment hit — **deleted by this plan**
- `Assets/Scripts/Main/ChatManager.cs` — the declarations/invocations themselves — **removed by this plan**

No other consumer existed → the events + getter were provably pill-only. Post-deletion, the same grep (plus `ChatListSyncIndicator`) returns **0 hits** across all `Assets` `.cs`.

## Preserved `_chatListSyncing` Evidence (required by plan)

Post-surgery grep:

- `ChatManager.cs:431` — `private bool _chatListSyncing;` (field, docstring updated to name all three reset sites)
- `ChatManager.cs:435` — `_chatListSyncing = true;` at `SyncAllChats` start (invoke line deleted, assignment kept)
- `ChatManager.cs:470` — `_chatListSyncing = false;` in `SyncAllChats`' `finally` (invoke line deleted, assignment kept)
- `ChatManager.BotState.cs:344` — `if (_chatListSyncing) return; // collapse duplicate syncs` — **the duplicate-sync guard, untouched** (plan cited :311; actual line 344 after 08-19's edits — same code)
- Resets untouched: `ChatManager.BotState.cs:130`, `ChatManager.Channel.cs:80`, `ChatManager.PrivacyClear.cs:83`

Net ChatManager.cs diff across the plan: **2 insertions, 23 deletions** — the two events + XML docs, the getter + XML doc, the two `?.Invoke()` lines, plus the 2-line docstring accuracy fix. `SyncingView.cs`, `WhatsAppSyncGate.cs`, and `ChatManager.BotState.cs` are diff-empty vs 08-19 HEAD → WhatsApp byte-identical (threat T-08-20-02 upheld).

## Baseline Arithmetic (corrected, required by plan)

- The plan's stated `1124 − 6 = 1118` was **stale** (1124 was the round-2 count; the plan itself instructed "if the pre-count differs, recount and re-derive").
- Last CONFIRMED fresh full-suite green: **1134/1134** (08-19's pre-RED run, 2026-07-17T15:23Z). 08-19 added 8 tests (its filtered GREEN 31/31 verified fresh) → expected pre-08-20 full suite = **1142** (never confirmed — 08-19's armed full run stalled unfocused and was subsequently killed; see Testing).
- This plan deletes `ChatListSyncIndicatorGateTests.cs` = exactly **6 `[Test]` methods** (counted in-file before `git rm`; externally corroborated by the 15:38Z filtered run reporting that suite as `total="6" passed="6"`).
- **Expected full suite after this plan: 1142 − 6 = 1136.**

## Task Commits

1. **Task 1: strip pill scene object (+ builder Remove entry)** — `1f28310` (chore) — committed by the orchestrator immediately after the mcp-unity mutation+save (parallel-session clobber protection); exactly `Assets/Scenes/Main.unity` + `Assets/Editor/ChatListSyncIndicatorBuilder.cs`.
2. **Task 2: delete pill files + ChatManager surgery** — `d2c800a` (chore) — 9 files, 647 pure deletions (8 pill files incl. .meta in the same commit — no separate .meta follow-up needed — + ChatManager.cs).
3. **Task 2 follow-up: `_chatListSyncing` docstring accuracy** — `5185620` (chore) — plan-sanctioned docstring update (names all three reset sites); doubled as the forced-reimport lever for the compile-crash retry (see Issues).

**Plan metadata:** docs commit (this SUMMARY + STATE).

## Files Created/Modified

- `Assets/Scenes/Main.unity` — `ChatListSyncIndicator` GameObject (+ Spinner/Label children) removed from `Screen_Whatsapp/ChatsPanel`. Verified: pill name grep 0 (was 2), pill script GUID `2a5df4fe73799411fa70f4b07ea1a99d` 0 (was 1); siblings intact — SyncingView GUID `51b4bed0…` ×1, EmptyStateView GUID `90d6c66e…` ×1, ChannelSwitcherView ×1.
- `Assets/Scripts/Main/ChatManager.cs` — pill-only plumbing removed; `_chatListSyncing` + assignments preserved; field docstring now names all three reset sites (SetActiveBot / SetActiveChannel / privacy-clear).
- `Assets/Editor/ChatListSyncIndicatorBuilder.cs` — gained `Remove`/`RemoveHeadless`/`RemoveInternal`/`CountByName` in Task 1 (committed `1f28310`), then deleted in Task 2 after its Remove entry had done its job (plan-ordered sequence).
- Deleted: `ChatListSyncIndicator.cs`, `ChatListSyncIndicatorGate.cs`, `ChatListSyncIndicatorBuilder.cs`, `ChatListSyncIndicatorGateTests.cs` + all four `.meta` files.

## Decisions Made

- **Scene mutation through the open Editor, never a text edit.** The Editor held the pill in its in-memory scene; a YAML edit of Main.unity would have been silently reverted on the Editor's next save. The orchestrator ran `delete_gameobject` + `save_scene` via mcp-unity (Option A of my checkpoint — no recompile dependency), mirroring how 08-09 originally stamped the same object.
- **Baseline corrected to 1136** (see arithmetic above) — coordinator-flagged, evidence-recounted.
- **Stopped compile-retry at the fix-attempt limit** — the crash is environmental (see Issues), not addressable from the repo.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Unity script compile latched FAILED by an environmental Bee BuildProgram crash — 2 fix attempts, then documented per fix-limit**
- **Found during:** Task 2 (test-verification step)
- **Issue:** The in-Editor bridge reported `CompilationFailed`, but `Editor.log` contains **zero `error CS` lines** — the compile failure is `Internal build system error. BuildProgram exited with code 134` (`System.OverflowException at Interop.Sys.GetGroups()` in `Bee.Tools.HostPlatform..cctor`), i.e. Unity's build program crashed at process spawn, before compiling any C#. Unity latches `scriptCompilationFailed=true`, and the bridge (correctly) refuses to run tests on stale assemblies.
- **Fix attempts:** (1) re-armed the trigger — no-op (the bridge only issues `AssetDatabase.Refresh()`, and with no changed files Unity never retries the compile; instant `CompilationFailed` again). (2) Forced a real reimport via the plan-sanctioned docstring update (`5185620`) + re-arm — Unity spawned a fresh BuildProgram which crashed identically (Editor.log crash count 1→2). Stopped per the 3-attempt discipline; a third identical retry would be re-running the build hoping it fixes itself.
- **Files modified:** `Assets/Scripts/Main/ChatManager.cs` (the doc-only commit `5185620`)
- **Verification:** repo state grep-verified consistent (0 dangling refs); compile confirmation pending environment recovery (below).
- **Committed in:** `5185620`

---

**Total deviations:** 1 (Rule 3, environmental — handled to the fix limit, then documented). **Impact:** none on the code change itself; only the fresh-green confirmation is deferred. No scope creep.

## Known Stubs

None — deletion-only plan; no placeholder text, no empty-value wiring, no unwired data sources introduced.

## Threat Flags

None — no new network endpoints, auth paths, file access, or schema changes. Threat register upheld: T-08-20-01 (`_chatListSyncing` guard preserved, grep-proven), T-08-20-02 (WhatsApp byte-identical — SyncingView/WhatsAppSyncGate/BotState diff-empty), T-08-20-03 (idempotent delete-only scene pass, immediate commit, sibling GUIDs verified ×1 each).

## Issues Encountered

- **08-19's in-flight full-suite run never completed** (read per 08-19's handoff instruction FIRST: `status: running, total: 0` at session start). It died to the Bee crash + this plan's recompile — so 08-19's expected 1142/1142 remains unconfirmed; the next confirmable number is this plan's 1136.
- **Environmental Unity compile crash (persists as of 16:37Z):** two identical Bee BuildProgram crashes (21:22 and 21:27 local) latched the Editor's compile state to failed with zero C# errors. Compiles were healthy at 21:19:11 local (successful editor-assembly write), so the condition arose mid-session in the Editor's process context. **Recovery (for the orchestrator/user):**
  1. Restart the Unity Editor (most robust — respawns the build toolchain), or try mcp-unity `recompile_scripts` first (may fail the same way if the process-spawn condition persists).
  2. After a successful compile: `: > Temp/claude/run-tests.trigger`, keep the Editor focused.
  3. Freshness gate: `Library/ScriptAssemblies/Assembly-CSharp.dll` mtime must postdate **21:27:09 local (16:27:09Z)** (last .cs edit, `5185620`); the summary must postdate the dll. **Expected: 1136/1136.**
  - Note: the user's Editor console currently shows the bridge's red "Scripts failed to compile" error — it is NOT a code error; do not chase phantom CS errors.

## Testing

- **EditMode suite: NOT green this session — honestly PENDING** (same protocol as 08-17/08-18). Blocked exclusively by the environmental Bee crash above; the repo's post-change state is grep-verified consistent (0 references to any deleted symbol in any `Assets` `.cs`) and the ChatManager seams were visually verified.
- **Expected on recovery: 1136/1136** (= 1142 − 6, arithmetic above).
- Both prior attempts' evidence and the recovery procedure are recorded above; device confirmation of the pill's absence (TG list shows the 08-19 cover, no pill; WhatsApp unchanged) rides **08-21**.

## Next Phase Readiness

- **08-21 (device re-verify)** can proceed once the Editor compiles again: it covers D13 end-to-end on device (fresh TG bot → cover, no pill; WhatsApp byte-identical) plus D2-ext and D12's ENTRY-log pivot.
- The first successful post-recovery full-suite run doubles as this plan's 1136 confirmation AND supersedes 08-19's unconfirmed 1142.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*

## Self-Check: PASSED

- Deleted files verified gone from disk (all 4 .cs + 4 .meta report "No such file").
- Modified files exist: `Assets/Scenes/Main.unity`, `Assets/Scripts/Main/ChatManager.cs`, this SUMMARY.
- Commits exist: `1f28310` (Task 1, orchestrator), `d2c800a` (Task 2), `5185620` (docstring follow-up) — per-commit file lists contain ONLY plan files (no `git add -A`; parallel-session commit `94f0df9` interleaved harmlessly between plans).
- Acceptance greps re-verified post-commit: scene 0/0 + siblings 1/1/1; ChatManager pill plumbing 0; `_chatListSyncing` field + both assignments + BotState guard + 3 resets present; Assets-wide dangling refs 0.
- Test gate: NOT green — explicitly and honestly reported as pending environment recovery (expected 1136/1136), with the recovery procedure documented above.
