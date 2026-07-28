---
phase: 06-channel-switcher-ui
plan: 02
subsystem: ui
tags: [unity, csharp, editor-builder, headless-build, channel-switcher, nav-restructure, topbar-pill, serialized-object, roundedcorners]

# Dependency graph
requires:
  - phase: 06-01
    provides: "ChannelSwitcherView binder (serialized-ref field-name contract: waChipButton/tgChipButton, waChipFill/tgChipFill, waLabel/tgLabel, waChipIcon/tgChipIcon) + BottomTabManager.BotsTabIndex==2 locked by TabIndexShiftTests"
  - phase: 05-02
    provides: "ChatManager.SetActiveChannel + ActiveChannel + per-bot channel persistence + per-channel empty states the pill drives"
provides:
  - "ChannelSwitcherBuilder — idempotent, re-run-safe headless Editor builder: builds the WhatsApp|Telegram segmented pill into Screen_Whatsapp/ChatsPanel/TopBar/CenterZone, stamps all 6 ChannelSwitcherView refs via SerializedObject, and performs the nav restructure in one pass"
  - "Tools/run-editor-builder.sh — Editor-closed headless -executeMethod runner (lock guard + success-sentinel verdict), the builder counterpart to run-tests-headless.sh"
  - "Main.unity mutated: switcher pill under an active CenterZone; Telegram bottom tab + Screen_Telegram + TelegramTab deleted; tab 0 relabelled «Чаты»; 4-tab bar"
  - "06-HUMAN-UAT.md — open owner visual-pass gate (SWITCH-01..04)"
affects: [phase-7-suggestions-dashboard]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Headless scene-mutating Editor builder run via `Unity -batchmode -executeMethod X.BuildHeadless -quit`, verdict gated on a grepped success sentinel (not Unity's exit code); scene committed immediately (parallel-scene-clobber discipline)"
    - "Guarded, idempotent SerializedObject array surgery: delete BottomTabManager.tabs[1] ONLY when it verifiably is the Telegram tab (tabName=='Telegram' || screenPanel.name=='Screen_Telegram'), so a re-run after the Dashboard shifted into index 1 never deletes the wrong tab"
    - "Two-independent-fills segmented pill (vs ModeToggle's single moving thumb): each chip = transparent raycast Button over a brand RoundedCorners fill whose alpha the binder toggles"
  patterns-established:
    - "Pattern 1: a TopBar control ships its logic + binder in plan N (pure seam + event-driven view), then a plan N+1 headless Editor builder builds the visible pill and stamps the binder's declared serialized-ref contract by name — verified by grepping Main.unity for the objects + non-zero fileIDs"
    - "Pattern 2: destructive scene surgery is committed only after structural grep asserts pass (payload present + intended deletions gone + all critical objects survive by net-deletion diff), immediately, in one commit with the builder that produced it"

key-files:
  created:
    - Assets/Editor/ChannelSwitcherBuilder.cs
    - Tools/run-editor-builder.sh
    - .planning/phases/06-channel-switcher-ui/06-HUMAN-UAT.md
  modified:
    - Assets/Editor/NavRestructureBuilder.cs
    - Assets/Scenes/Main.unity

key-decisions:
  - "Nav restructure folded into ChannelSwitcherBuilder.BuildInternal (CONTEXT §Claude's Discretion) rather than a separate NavRestructureBuilder method — one headless pass builds the pill and mutates the tabs array."
  - "Text-only chips (no leading brand icons) for v1, mirroring the neighbouring ModeToggle exactly; waChipIcon/tgChipIcon left unstamped (the binder null-guards them). CONTEXT explicitly allows text-only."
  - "Two independent per-chip fills instead of one moving thumb — required by the 06-01 binder contract (waChipFill/tgChipFill each toggled to alpha 1/0), and it reads as a standard iOS segmented control (neutral #EFEFF0 track, brand-fill selected chip)."
  - "Guarded tab-1 deletion + captured tabRoot/screenPanel refs destroyed only on a positive Telegram match — re-run-safe against the post-restructure 4-tab array (T-06-06)."

requirements-completed: [SWITCH-01, SWITCH-04]

# Metrics
duration: 10min
completed: 2026-07-13
---

# Phase 6 Plan 02: Channel Switcher Builder + Nav Restructure (Headless) Summary

**A headless, idempotent `ChannelSwitcherBuilder` that builds the WhatsApp|Telegram segmented pill into the TopBar `CenterZone` and stamps all six `ChannelSwitcherView` refs by SerializedObject, then guardedly removes the Telegram bottom tab + `Screen_Telegram` + `TelegramTab` and relabels tab 0 «Чаты» — run via a new Editor-closed `run-editor-builder.sh`, scene committed immediately (`8f1d25f`), EditMode suite 900/900 green against the 4-tab scene.**

## Performance

- **Duration:** ~10 min
- **Started:** 2026-07-13T10:49:20Z
- **Completed:** 2026-07-13T11:00:13Z
- **Tasks:** 3 (Task 1 authored + Task 2 executed the same build → one bundled `feat` commit; Task 3 docs)
- **Files modified:** 5 (3 created, 2 modified)

## Accomplishments

- **`ChannelSwitcherBuilder`** — a `[MenuItem("Tools/Channel Switcher/Build")]` + `BuildHeadless()` (`-executeMethod` target) builder that, in one `BuildInternal` pass:
  - **Pill:** finds `Screen_Whatsapp/ChatsPanel/TopBar/CenterZone`, activates it, drops the unused `Title`, and builds a neutral `#EFEFF0` rounded track (340×76, r=38) holding two chips (162×64) — each a transparent raycast `Button` over a brand-coloured `RoundedCorners` fill (WA `#25D366` / TG `#2AABEE`, r=32) and a header-font label («WhatsApp» / «Telegram», 28pt, characterSpacing −2, no wrap). Idioms copied verbatim from `ReplyModeToggleBuilder` (null sprite + AppDomain-scanned `ImageWithRoundedCorners`, header font by GUID, `SetRef`, `FindByNameIncludeInactive`, `DestroyAllByName`).
  - **Refs:** adds `ChannelSwitcherView` to the root and stamps all six required serialized refs (`waChipButton`/`tgChipButton`/`waChipFill`/`tgChipFill`/`waLabel`/`tgLabel`) via `SerializedObject`; the two optional icon refs are left null (text-only v1).
  - **Nav restructure:** guarded removal of `BottomTabManager.tabs[1]` (verified Telegram), captures + `DestroyImmediate`s the `TelegramTab` root and `Screen_Telegram`, relabels tab 0 «Чаты» (inspector `tabName` + scene TMP `m_text`).
- **`Tools/run-editor-builder.sh`** — mirrors `run-tests-headless.sh`: resolves Unity from `ProjectVersion.txt` (env `UNITY` override), refuses with a clear message when a non-batch Editor holds the project lock, runs `-batchmode -nographics -executeMethod ChannelSwitcherBuilder.BuildHeadless -quit`, and returns exit 0 **only** when the `[ChannelSwitcherBuilder] Headless build + save complete` sentinel is in `builder.log` **and** Unity exited 0.
- **`NavRestructureBuilder.ReorderScreens`** — dropped the `Screen_Telegram` entry (the string no longer appears anywhere in the file) so future runs don't warn about the deleted screen.
- **Headless run + verified scene:** builder ran clean (`tabs 5 → 4`, sentinel present); structural grep asserts all passed (pill + `WaChip`/`TgChip` present; `CenterZone` now `m_IsActive: 1`; `Screen_Telegram`/fileID `163358610`/`TelegramTab` all gone; 4-tab array with `Чаты`/`Сводка`/`Bots`/`Profile`; tab-0 label TMP `m_text` = «Чаты»; all six binder refs non-zero and un-crossed — waLabel→"WhatsApp", tgLabel→"Telegram"). Scene committed immediately.
- **EditMode suite:** re-ran headless — **900/900 green** (incl. `TabIndexShiftTests` against the now-real 4-tab scene, closing the 06-01 lockstep).

## Task Commits

1. **Task 1 (author) + Task 2 (execute):** `8f1d25f` (feat) — builder + runner + `ReorderScreens` prune authored, then executed headlessly; scene + `ChannelSwitcherBuilder.cs`+`.meta` + `NavRestructureBuilder.cs` + `run-editor-builder.sh` committed together. *(Bundled by plan design: the new `.cs.meta` is generated by the Task 2 headless import, so source + generated meta + mutated scene commit as one coherent unit.)*
2. **Task 3:** `ee0df30` (docs) — `06-HUMAN-UAT.md` open visual-pass gate.

**Plan metadata:** _(final docs commit — this SUMMARY + STATE + ROADMAP + REQUIREMENTS)_

## Files Created/Modified

- `Assets/Editor/ChannelSwitcherBuilder.cs` (created) — the pill builder + guarded nav restructure + `BuildHeadless` entry.
- `Tools/run-editor-builder.sh` (created) — Editor-closed headless `-executeMethod` runner with sentinel verdict.
- `.planning/phases/06-channel-switcher-ui/06-HUMAN-UAT.md` (created) — 6-point 1080×2400 owner checklist + deferred-polish section.
- `Assets/Editor/NavRestructureBuilder.cs` (modified) — removed `Screen_Telegram` from the `ReorderScreens` order array.
- `Assets/Scenes/Main.unity` (modified) — pill under active `CenterZone` (`ChannelSwitcherView` refs stamped); Telegram tab + `Screen_Telegram` + `TelegramTab` removed; tab 0 «Чаты»; 4-tab bar.

## Decisions Made

- **Nav restructure lives inside `ChannelSwitcherBuilder`** (CONTEXT §Claude's Discretion) — a single `BuildInternal` does pill + tab surgery in one headless pass. `NavRestructureBuilder` was touched only to prune its `ReorderScreens` list (its hardcoded tabs[2]/[3] indices are a one-time already-run migration, left untouched per 06-01's note).
- **Text-only chips, no leading icons** — mirrors the ModeToggle precedent exactly and fits the 360-wide slot cleanly; `waChipIcon`/`tgChipIcon` intentionally unstamped (binder null-guards them). This is the CONTEXT-endorsed v1 choice, not a stub.
- **Two independent chip fills, not a moving thumb** — dictated by the 06-01 binder's `waChipFill`/`tgChipFill` alpha-toggle contract; renders as a neutral-track iOS segmented control with a brand-filled selected chip. WhatsApp starts filled as a sensible static default; the binder re-resolves both on the first `OnEnable`.
- **Guarded, capture-then-destroy tab removal** — reads `tabs[1]` and deletes only on a positive Telegram match (`tabName`/`screenPanel.name`), capturing the `TelegramTab` + `Screen_Telegram` GameObjects before the array delete and `DestroyImmediate`-ing them after (T-06-06). A defensive re-check handles the object-reference delete-twice quirk (unneeded here — `TabData` is a managed class, confirmed `5 → 4` in one delete).

## Deviations from Plan

**None — plan executed exactly as written.** No Rule 1–4 auto-fixes were required. The builder, runner, and restructure landed on-contract; the one reworded comment (removing the literal `Screen_Telegram` from a `NavRestructureBuilder` comment so the plan's `! grep -q "Screen_Telegram"` assert passes) is a correctness detail of the plan's own verify command, not a scope change. The bundling of Task 1's authored files with Task 2's commit is the plan's own design (Task 1 = author-not-execute; Task 2 = execute + commit scene immediately with the editor files).

## Issues Encountered

- **`Screen_Telegram` literal in a comment** — my first `ReorderScreens` edit left a comment mentioning `Screen_Telegram`, which would have tripped the plan's `! grep -q "Screen_Telegram"` assert. Reworded the comment to omit the literal; assert passes (0 occurrences).
- **Benign Main.unity churn** — the headless save produced the expected large diff (128+5 `RoundedCorners`/`IndependentRoundedCorners` material names + 3 SDF material instances re-serialized, block reorders). Verified per the project memory: computed **net** GameObject deletions = exactly `Title`, `TelegramTab`, `Screen_Telegram` + their cascade children (the Telegram tab's `Icon` 399057317 + 2 orphaned TMP material instances); confirmed every critical object (`EventSystem`, `PanelPrivacy`, `ActionMenu`, all screens, `BottomNavPanel`) still exists (≥1). No unintended deletion.
- **Batch-mode log noise** — `[Licensing::Module] Error: Access token is unavailable`, `Native extension … not found`, `[usbmuxd] Error` are standard headless-batch noise, unrelated to the build; the success sentinel and clean exit 0 are authoritative.

## User Setup Required

None — no external service configuration. The remaining work is the **owner visual/device pass** in `06-HUMAN-UAT.md` (open gate; closes the phase).

## Next Phase Readiness

- **Phase 6 is code-complete.** SWITCH-01 (observable TopBar switcher wired to the binder) and SWITCH-04 (Telegram tab + `Screen_Telegram` removed, tab 0 «Чаты») are delivered in the scene and marked. 06-01's `BotsTabIndex==2` now matches the real 4-tab scene — the lockstep is closed.
- **Only the owner UAT gate remains** before phase close (visual polish is unobservable headless). Phase 7 (suggestions/dashboard Telegram inclusion) can build on the channel concept now that the switcher is live.
- **No new threat surface** — pure scene mutation; no network endpoints, auth paths, file access, or schema changes.

## Threat Register Coverage

- **T-06-04 (Tampering, builder deleting/rewiring the wrong scene object):** mitigated — idempotent `DestroyAllByName`; post-build grep asserts (pill present, `Screen_Telegram`+fileID `163358610` absent, 4-tab array, «Чаты» label); net-deletion diff confirmed only `Title`/`TelegramTab`/`Screen_Telegram`+cascade removed; committed only after all asserts passed, immediately.
- **T-06-05 (DoS, tab-index shift breaks SwitchTab targets):** mitigated — 06-01's `BotsTabIndex==2` + `TabIndexShiftTests` now run against the real 4-tab scene; `defaultTabIndex:0` still lands on `Screen_Whatsapp`; suite 900/900 green.
- **T-06-06 (Tampering, re-run deletes the wrong tab):** mitigated — removal guarded to `tabs[1].tabName=="Telegram" || screenPanel.name=="Screen_Telegram"`; skips + logs when already restructured; `Screen_Telegram`/`TelegramTab` destroys null-guarded.

## Known Stubs

None. The `waChipIcon`/`tgChipIcon` serialized refs are intentionally left null (text-only v1, CONTEXT-endorsed, ModeToggle precedent); the binder null-guards them, so this is a declared design choice, not an unwired-data stub. All six required refs are stamped with non-zero fileIDs and the pill drives `ChatManager.SetActiveChannel` end-to-end.

## Self-Check: PASSED

- All created files present on disk: `ChannelSwitcherBuilder.cs` (+`.meta`), `Tools/run-editor-builder.sh`, `06-HUMAN-UAT.md`, `06-02-SUMMARY.md`; modified `NavRestructureBuilder.cs` + `Main.unity`.
- Both task commits present in git log: `8f1d25f` (feat — builder + runner + scene), `ee0df30` (docs — UAT gate).
- Headless build sentinel present; structural scene asserts all passed; EditMode suite **900/900 green** against the 4-tab scene.

---
*Phase: 06-channel-switcher-ui*
*Completed: 2026-07-13*
