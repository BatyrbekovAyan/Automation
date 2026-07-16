---
phase: 08-device-uat-milestone-closeout
plan: 07
subsystem: ui
tags: [telegram, video-note, kruzhok, bubble-transparency, rounded-corners, MessageItemView, media-pipeline]

# Dependency graph
requires:
  - phase: 05-channel-aware-chatmanager-core
    provides: "BubbleTransparencyPolicy seam + isVideoNote (05-08, outgoing-note float); TelegramVideoNoteHeuristic (05-07); кружок circle crop + duration/GIF overlays"
  - phase: 08-device-uat-milestone-closeout
    provides: "08-04 open-chat live poll (presentation now re-binds every ~3s — visual state must survive re-binding)"
provides:
  - "Incoming Telegram video note (кружок) floats bubble-free — the round preview is treated as non-placeholder so the square bubble goes transparent, matching the verified outgoing behavior (D3b)"
  - "Duration-badge pill rounded corners refresh after layout so the left/right ends are round on device instead of sharp (D3a)"
affects: [08-10, telegram-media-presentation, MessageItemView]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Refine the transparency-policy INPUT at the MonoBehaviour call site (per-render Unity state) instead of changing the pure seam — the seam stays byte-identical + unit-tested, the runtime nuance lives in glue"
    - "A masked overlay pill's ImageWithRoundedCorners must be refreshed AFTER its rect is sized (radius is assigned post-AddComponent and never re-bakes on a fixed-size point anchor) — reuse the file's RefreshCorners idiom + a one-frame-deferred, pooled-bubble-guarded second pass"

key-files:
  created: []
  modified:
    - "Assets/Scripts/UI/MessageItemView.cs — UpdateBubbleVisuals (D3b transparency) + ToggleDurationBadgeOverlay/RefreshBadgeCornersDeferred (D3a badge corners)"

key-decisions:
  - "Root cause (D3b) = (ii): vm.isVideoNote is TRUE on the incoming path (the round crop the owner sees proves it), but the note is placeholder-first (tapi body:null + s3Info:{} → download-by-id) unlike the outgoing note 05-08 verified (inline s3Info.url), so isPlaceholderActive stays true under the visible circle and the square bubble stays opaque = the 'white background bubble'"
  - "Fix D3b at the transparency call site, NOT the heuristic: once the note's round media surface (messageImage) is the visible content, force the placeholder input false so the circle floats — card-only states (messageImage hidden) keep the opaque retry card. Note-scoped (isVideoNote), WhatsApp byte-identical"
  - "Fix D3a by refreshing the badge pill's corners after layout (immediate + one-frame-deferred) via the existing RefreshCorners idiom; radius value + layout untouched"

patterns-established:
  - "Transparency-policy call-site refinement: correct the pure seam's boolean input with per-render Unity state (activeInHierarchy) rather than mutating the tested seam"
  - "Overlay-pill corner refresh: RefreshCorners now + a pooled-bubble-guarded deferred pass covers the 0×0-at-creation cold-open case"

requirements-completed: []

# Metrics
duration: 20min
completed: 2026-07-16
---

# Phase 08 Plan 07: Telegram Video-Note Presentation (D3) Summary

**Incoming кружок now floats bubble-free (round preview treated as non-placeholder so the square bubble goes transparent, matching outgoing) and the duration-badge pill's rounded corners refresh after layout so its left/right ends render round on device — both note-scoped, WhatsApp byte-identical, suite 1063/1063 green.**

## Performance

- **Duration:** ~20 min
- **Started:** 2026-07-16T11:31Z (approx)
- **Completed:** 2026-07-16T11:51Z
- **Tasks:** 2
- **Files modified:** 1 (`Assets/Scripts/UI/MessageItemView.cs`)

## Accomplishments

- **D3b diagnosed and fixed** — the incoming video note's white background bubble is gone: once the round media surface is the visible content the bubble floats chrome-free, matching the outgoing note verified in 05-08.
- **D3a fixed** — the кружок duration-badge pill's corners now refresh after its rect is sized, so the left/right ends are round on device instead of sharp.
- **No `ChatManager.cs` edit** — the heuristic (`TelegramVideoNoteHeuristic`) was NOT the cause and was left untouched (`is_round` still ignored); both fixes landed in `MessageItemView.cs` exactly as the plan scoped.

## Root cause (D3b)

**(ii) — `vm.isVideoNote == true` but `isPlaceholderActive` is true under the visible circle.**

The owner sees a *round* video (E1: "round video with white background bubble"), and the circular crop is applied ONLY when `currentVm.isVideoNote` is true (`SetupMaskedLayout`, `rounded.radius = mediaSize.x * 0.5f`). So the heuristic is matching the incoming note — hypothesis (i) (heuristic miss) is ruled out by the visual evidence. Given `isVideoNote == true` and `isSticker == false`, `BubbleTransparencyPolicy.IsTransparent(...)` reduces to `!isPlaceholderActive`, so an opaque bubble means a placeholder was active. The delta from the verified outgoing note: an **outgoing** note (05-08 sample `probe_23368`) carries an inline `s3Info.url`, so its media is present at bind and it never enters a placeholder state → it floats. An **incoming** note arrives `body:null + s3Info:{}` (download-by-id, placeholder-first), so it passes through a download/loading placeholder; once the round preview shows, a lingering placeholder (or a not-yet-cleared expired card) leaves `isPlaceholderActive` true UNDER the visible circle, so the square bubble stays opaque and its corners read as a "white background bubble" around the note. 05-08's `!isPlaceholderActive` gate was verified OUTGOING only — the incoming path is exactly the untested axis.

## Fix

**D3b (`UpdateBubbleVisuals`):** compute `noteMediaShowing = currentVm.isVideoNote && messageImage.gameObject.activeInHierarchy`, and pass `effectivePlaceholderActive = isPlaceholderActive && !noteMediaShowing` into the (unchanged) pure `BubbleTransparencyPolicy` seam. Once the round media surface is the visible content, the circle IS the content and floats regardless of a lingering placeholder GameObject — matching outgoing. Card-only states (`ShowSmartLoadingCard` / `ShowUnavailableMediaPanel`) HIDE `messageImage` (`activeInHierarchy` false), so a genuinely-downloading/unavailable note still shows its opaque retry card (05-08 design preserved). Note-scoped: `isVideoNote` defaults false, so stickers and every WhatsApp bubble get the identical `isPlaceholderActive` input as before. Survives the 08-04 live poll re-binds (evaluated every `UpdateBubbleVisuals`).

**D3a (`ToggleDurationBadgeOverlay` + new `RefreshBadgeCornersDeferred`):** the badge pill's `ImageWithRoundedCorners` bakes its radius against the rect inside `Refresh()`, but `GetOrCreateOverlayPill` assigns the radius AFTER the component's `AddComponent`-time `OnEnable` already baked against radius 0, and a fixed-size point-anchored pill's rect never changes to re-fire `OnRectTransformDimensionsChange` — so the radius was never re-baked and the ends stayed square. After (re)showing the badge, `RefreshCorners(pillRoot)` runs immediately (rect usually already sized) AND once more next frame via `RefreshBadgeCornersDeferred` (covers a cold open where the pill rect is still 0×0 at creation). Reuses the file's `RefreshCorners` idiom (radius rebake + `Validate`/`Refresh` + masked-stencil invalidation). Pooled-bubble safe (T-08-07-02): the deferred pass re-checks the row is still a video note and the pill is still the live active badge before touching it. Radius value + layout unchanged.

## Task Commits

Each task committed atomically (both symptoms of D3 are independent fixes in the same file):

1. **Task 1 (D3b): incoming video-note bubble-free transparency** — `161b540` (fix)
2. **Task 2 (D3a): duration-badge rounded-corner refresh after layout** — `89479db` (fix)

**Plan metadata:** committed separately with SUMMARY + STATE + ROADMAP.

## Files Created/Modified

- `Assets/Scripts/UI/MessageItemView.cs` — `UpdateBubbleVisuals` computes a note-scoped `effectivePlaceholderActive` so an incoming кружок whose round media is showing floats (D3b); `ToggleDurationBadgeOverlay` + new `RefreshBadgeCornersDeferred` coroutine refresh the badge pill's rounded corners after its rect is sized (D3a).

## Decisions Made

- Fixed D3b at the transparency **call site** (refine the seam's boolean input with per-render Unity state) rather than changing the pure `BubbleTransparencyPolicy` seam — the seam stays byte-identical and its 10-case unit matrix stays green, while the incoming-vs-outgoing nuance lives in the MonoBehaviour glue where the Unity runtime state (`activeInHierarchy`) actually lives.
- Did NOT touch `TelegramVideoNoteHeuristic` — the round crop proves the heuristic is matching the incoming note, so no heuristic change (and no new heuristic test) was warranted; `is_round` remains deliberately ignored.

## Deviations from Plan

None — plan executed as written (diagnosis-first). The plan's `files_modified` listed `TelegramVideoNoteHeuristic.cs` + its tests as the files that WOULD change **if** hypothesis (i) held; diagnosis found (ii), so the fix correctly landed in `MessageItemView.cs` only and those two files were not touched (the plan's Task 1 explicitly branches "FIX per finding").

## Issues Encountered

- **Test freshness nuance (not a code issue):** the in-Editor bridge produced a FRESH green (assembly `2026-07-16T11:43:56Z`, **1063/1063 passed, 0 failed**) that compiled BOTH fixes as applied. To honor per-task commits I then reverted Task 2, committed Task 1, and re-applied Task 2 with the **byte-identical** string — so the committed HEAD file (`base + Task1 + Task2`) is provably identical to what that assembly compiled and tested green. A confirming re-run re-executed the suite green against the same assembly but did NOT bump `editorAssemblyWrittenUtc`, because Unity detected the identical content and skipped recompilation. The green therefore genuinely covers the committed logic; the assembly timestamp simply predates the identical-content commit-split. The orchestrator's central verification will re-confirm.

## Test Status

**FRESH green — 1063/1063 EditMode passed, 0 failed, 0 inconclusive** (assembly `2026-07-16T11:43:56Z`, which compiled both committed fixes; content-identical to HEAD). No new tests added — both fixes are MonoBehaviour rendering glue (transparency-input refinement + corner-refresh), and the pure `BubbleTransparencyPolicy` / `TelegramVideoNoteHeuristic` seams were unchanged so their existing suites still guard them. Device confirmation of both symptoms rides **08-10** (UAT B5 badge corners + E1 incoming-note float).

## Next Phase Readiness

- D3 code-complete; device re-verify (E1 incoming кружок floats bubble-free, B5 badge corners round) rides the 08-10 device pass.
- No `ChatManager.cs` exception flagged — the fix stayed entirely in `MessageItemView.cs`, so D5/D7's adjacent-wave ownership of `ChatManager.cs` is unaffected.

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-07-SUMMARY.md`
- FOUND: `Assets/Scripts/UI/MessageItemView.cs`
- FOUND commit `161b540` (Task 1, D3b)
- FOUND commit `89479db` (Task 2, D3a)

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-16*
