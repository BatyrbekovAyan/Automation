---
phase: 11-first-run-onboarding-flow
plan: 05
subsystem: ui
tags: [onboarding, trust, auth, success-moment, scene-builder, editor-tooling, telegram-parity, unity]

# Dependency graph
requires:
  - phase: 11-first-run-onboarding-flow (plan 03)
    provides: proven builder envelope (OnboardingScreenBuilder helpers, font GUIDs, tokens, escaped-unicode scene-verification method)
  - phase: 11-first-run-onboarding-flow (plan 04)
    provides: Manager's 10 per-channel waSuccess*/tgSuccess* [SerializeField] fields (the builder↔component stamping contract) + ShowInteractiveSuccessMoment consuming them
  - phase: existing
    provides: Manager-owned CodePanel/SuccessOverlay scene objects, NavRestructureBuilder helper idioms, resvg render pipeline (render_hero.js)
provides:
  - "«Это безопасно» trust cards live in Main.unity as the LAST child of BOTH auth code panels (WhatsApp + Telegram), channel-specific verbatim copy, green lock icon (Image+sprite)"
  - "TWO independent «Загрузить прайс-лист»/«Позже» success sheets — one per SuccessOverlay — with animated green check, stamped onto all 10 Manager waSuccess*/tgSuccess* fields"
  - "OnboardingAuthBlocksBuilder: idempotent [MenuItem]+headless editor builder for re-running/recalibrating the auth blocks"
  - "SuccessCheckPop: reusable self-contained OnEnable DOScale 0.9→1 OutBack pop component"
  - "Lock.png trust padlock + Tools/lock_icon.svg source + render_lock_icon.js (resvg pipeline)"
affects: [first-steps-card, first-run-device-uat]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Index-safe injection into hardcoded-GetChild panels: DestroyAllByName + SetAsLastSibling so existing sibling indices never shift"
    - "Scene-mutating builder run through the OPEN Editor (orchestrator mcp-unity execute_menu_item + save_scene) with the scene committed immediately in its own commit"
    - "Self-contained OnEnable animation component (SuccessCheckPop) attached by the builder — celebratory motion without touching the Manager god-object"
    - "White-on-transparent icon source rendered from an in-repo SVG via resvg (render_lock_icon.js), tinted by Image.color at build time"

key-files:
  created:
    - Assets/Editor/OnboardingAuthBlocksBuilder.cs
    - Assets/Scripts/Main/Onboarding/SuccessCheckPop.cs
    - Assets/Images/Icons/Lock.png
    - Tools/lock_icon.svg
    - Tools/render_lock_icon.js
  modified:
    - Assets/Scenes/Main.unity
    - Assets/Images/Icons/Lock.png.meta

key-decisions:
  - "Trust cards append via SetAsLastSibling under panels that lay out with a VerticalLayoutGroup — the card lands at the stack bottom AND Manager's GetChild(3/4/5)/(3) stay valid (verified: both code panels went 9→10 children with TrustBlock last)"
  - "Check animation delivered as a new self-contained SuccessCheckPop (OnEnable DOScale 0.9→1 OutBack) attached by the builder — Plan 04's coroutine has no check field, and editing Manager for an animation hook would have exceeded scene-plan scope"
  - "No lock sprite existed in the project (only a shield) — rendered a purpose-built padlock from Tools/lock_icon.svg via the established resvg pipeline rather than misusing the shield or a TMP glyph"
  - "Scene committed alone+immediately (parallel-scene-clobber rule) — environment constraint overrode the plan's single two-file commit, mirroring 11-03's documented precedent"

patterns-established:
  - "Injecting UI into index-addressed hand-built panels: teardown-by-name, rebuild, SetAsLastSibling, then prove child order via the panel RT's m_Children array"

requirements-completed: [ONB-02, ONB-03, ONB-05]

# Metrics
duration: ~23 min
completed: 2026-07-18
---

# Phase 11 Plan 05: Auth Trust Blocks + Per-Channel Success CTAs Summary

**Both auth code panels now carry a green «Это безопасно» trust card (channel-specific verbatim copy, tinted padlock, appended index-safe as the LAST child so Manager's hardcoded GetChild(3/4/5)/(3) auth flow is byte-identical) and both SuccessOverlays host their OWN «Загрузить прайс-лист»/«Позже» sheet with an animated check — all 10 waSuccess*/tgSuccess* Manager fields stamped, scene committed immediately, suite green at 1165/1165.**

## Performance

- **Duration:** ~23 min (16:14:07Z → ~16:37Z, including one checkpoint round-trip for the builder run)
- **Started:** 2026-07-18T16:14:07Z
- **Completed:** 2026-07-18T16:37:00Z
- **Tasks:** 2 (both auto; Task 2's builder execution went through a checkpoint resolved by the orchestrator's mcp-unity)
- **Files modified:** 10 (5 created + metas, Main.unity)

## Accomplishments

- **Builder (Task 1):** `OnboardingAuthBlocksBuilder` clones the phase-proven envelope (NavRestructureBuilder helpers, font GUIDs, deferred RoundedCorners bake, `DestroyAllByName` idempotency) and injects into the EXISTING Manager-owned auth panels, resolved through Manager's own serialized refs (never by name guessing). Trust cards: bordered #F2F8F2/#DCEDDD rounded card, pale-green disc + `Lock.png` padlock tinted #1F8A46 (Image+sprite, no TMP glyph), «Это безопасно» title + channel-specific verbatim body; zero QR/linked-code strings anywhere in the builder. Success sheets: opaque 880×1160 card per SuccessOverlay with check disc (+`SuccessCheckPop`), title/body, Primary CTA + ghost «Позже» — two fully independent clusters because the panels live in separate hierarchies.
- **Index safety (T-11-05-01):** both code panels lay children out with a VerticalLayoutGroup; the trust card carries a LayoutElement and is appended `SetAsLastSibling`, so it renders at the stack bottom and the saved scene proves WA children 9→10 / TG 9→10 with `TrustBlock` LAST — `Manager.cs` untouched this plan, all `GetChild(3)/(4)/(5)` + `GetChild(3)` references still present (21 total).
- **Stamping (T-11-05-04):** all 10 `waSuccess*`/`tgSuccess*` fields stamped via SerializedObject and verified non-zero in the saved scene YAML; the WA cluster's fileIDs child under SuccessOverlay 325548482 and the TG cluster's under 1587102504 (father-RT verified per sheet).
- **Check animation:** `SuccessCheckPop` — a self-contained `OnEnable` DOScale 0.9→1 OutBack (SetLink'd, kill-safe) — attached to each check disc by the builder, so every «Бот подключён!» show re-pops without any Manager edit.
- **Scene (Task 2):** built through the open Editor (orchestrator ran `Tools/Onboarding/Build Auth Blocks` + save via mcp-unity; both console sentinels, zero errors) and committed immediately. Verbatim copy verified in scene YAML via the 11-03 escaped-unicode method, corrected for two serializer quirks (see Issues).
- **Zero regression (ONB-05):** EditMode suite 1165/1165 green on a fresh recompile after the code landed (editor-asm stamp 15:48:52Z → 16:15:44Z), and again after the scene save (data-only — stamp correctly unchanged).

## Task Commits

1. **Task 1: builder + SuccessCheckPop + Lock.png (+ SVG source/renderer)** - `9de7101` (feat)
2. **Task 2: scene build — trust cards + success sheets + stamps** - `9494452` (feat — Main.unity + Lock.png.meta, committed immediately after the builder run per the parallel-scene-clobber rule)

**Plan metadata:** committed separately with STATE/ROADMAP/REQUIREMENTS updates (docs).

## Files Created/Modified

- `Assets/Editor/OnboardingAuthBlocksBuilder.cs` - Idempotent [MenuItem "Tools/Onboarding/Build Auth Blocks"] + `BuildHeadless` (exact sentinel `[OnboardingAuthBlocksBuilder] Headless build + save complete`); trust cards + two success sheets + 10-field Manager stamp.
- `Assets/Scripts/Main/Onboarding/SuccessCheckPop.cs` - OnEnable DOScale 0.9→1 OutBack pop for the green check (self-contained, no serialized refs).
- `Assets/Images/Icons/Lock.png` - 512² white-on-transparent padlock (LFS), tinted green by the builder's Image.
- `Tools/lock_icon.svg` + `Tools/render_lock_icon.js` - Vector source + resvg renderer (render_hero.js pipeline).
- `Assets/Scenes/Main.unity` - TrustBlock ×2 (last child of each code panel), SuccessCta ×2 (one per SuccessOverlay), 10 Manager stamps.
- `Assets/Images/Icons/Lock.png.meta` - Builder-enforced Sprite/Single import.

## Decisions Made

- **Last-child append under a VerticalLayoutGroup** satisfies both constraints at once: the VLG positions the card at the visual bottom of the code panel, and the sibling index of every pre-existing child is untouched (the plan's central threat, T-11-05-01).
- **`SuccessCheckPop` over a Manager edit** — the must-have "green check animates DOScale 0.9→1 OutBack" had no owner: Plan 04's `ShowInteractiveSuccessMoment` holds no check reference. A scene-attached OnEnable component delivers the animation on every show without touching Manager.cs (out of this plan's file scope).
- **Purpose-rendered padlock** — the only security-ish sprite in the project is a shield outline (`Assets/Images/New/Security.png`); the spec explicitly calls for a lock. Rendered one via the in-repo resvg pipeline; white source + Image tint keeps the colour a builder token.
- **`feat` for the scene commit** (plan's literal snippet said `docs`) — a scene mutation is product UI, and 11-03's scene commit set the `feat` precedent; the acceptance criterion (subject contains `11-05`) holds.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical] Added `SuccessCheckPop` so the check actually animates**
- **Found during:** Task 1
- **Issue:** The plan requires the check "tagged so Plan 04 can DOScale 0.9→1 OutBack it", but Plan 04 (already executed) exposes no check field and animates nothing — the must-have truth "The green check animates DOScale 0.9→1 OutBack" would have shipped unmet.
- **Fix:** New self-contained `SuccessCheckPop` MonoBehaviour (OnEnable pop, kill-safe, SetLink) attached to each check disc by the builder.
- **Files modified:** Assets/Scripts/Main/Onboarding/SuccessCheckPop.cs (+ builder attach)
- **Verification:** Component GUID present ×2 in the saved scene; suite 1165/1165.
- **Committed in:** `9de7101`

**2. [Rule 3 - Blocking] No lock sprite existed — rendered `Lock.png` via the resvg pipeline**
- **Found during:** Task 1
- **Issue:** Spec mandates a lock icon as Image+sprite; the project has no padlock asset (only a shield outline), and TMP glyphs are a named anti-pattern.
- **Fix:** Authored `Tools/lock_icon.svg` (rounded shackle, keyhole knockout) + `render_lock_icon.js` on the established `@resvg/resvg-js` pipeline → `Assets/Images/Icons/Lock.png` (512², white-on-transparent, LFS); visually verified tinted-green-on-card before committing.
- **Files modified:** Tools/lock_icon.svg, Tools/render_lock_icon.js, Assets/Images/Icons/Lock.png(.meta)
- **Committed in:** `9de7101` (meta flip in `9494452`)

**3. [Environment override] Builder run through the open Editor; scene commit split from the builder commit**
- **Found during:** Task 2
- **Issue:** The plan's primary path (`Tools/run-editor-builder.sh`) requires a closed Editor; the Editor was open (PID 1327) and this executor cannot drive Unity menus. Also the plan's acceptance wanted `Main.unity` + builder in ONE commit, while the environment's scene-commit discipline requires the scene alone+immediately.
- **Resolution:** Checkpoint → orchestrator ran the menu item + save via mcp-unity (both sentinels, zero console errors) → scene committed alone+immediately (`9494452`); the builder had its own per-task commit (`9de7101`). Both subjects carry `11-05`. Identical to 11-03's documented pattern.
- **Impact:** None on outcome; all acceptance checks pass.

---

**Total deviations:** 2 auto-fixed (1 missing-critical, 1 blocking asset gap) + 1 environment-driven execution-path substitution. No scope change; all plan behavior delivered as written.

## Issues Encountered

- **First bridge freshness gate false-passed:** the initial poll compared a truncated baseline stamp lexically and matched the STALE summary. Re-armed with the exact stale stamp + dll-epoch comparison; the fresh run then proved 1165/1165 on the new assemblies (15:48:52Z → 16:15:44Z).
- **Two scene-YAML serializer quirks broke the verbatim-copy probe** (extends 11-03's escaped-unicode method): Unity serializes Latin-1-range chars as `\xAB`/`\xBB` (the «» guillemets), NOT `«`; and a YAML double-quoted line fold means ONE SPACE, so fold-normalization must replace `\n\s+` with `' '`, not `''`. With both corrections all six copy probes match exactly (trust bodies ×1 each — channel-specific; success copy ×2).
- **`Lock.png` first-imported as spriteMode Multiple** — the builder's `EnsureIconImportSettings` flipped it to Single before `LoadSprite`. Safe by construction: brand-new asset, zero pre-existing references (the 05-12/11-03 sub-sprite gotcha does not apply).

## Known Stubs

None — both trust cards and both success sheets are fully built, copy-complete, and wired: the 10 Manager fields are stamped, `ShowInteractiveSuccessMoment` (Plan 04) drives the buttons/labels at runtime, and the check pops via `SuccessCheckPop`. Visual calibration on device/Game view rides the phase's normal UAT tail.

## Threat Model Compliance

All four `mitigate` dispositions applied: T-11-05-01 (last-child append; WA/TG code panels 9→10 children with TrustBlock LAST; `Manager.cs` untouched, all GetChild lines present; suite green), T-11-05-02 (owner-approved verbatim copy only, zero QR strings in the builder — grep-proven), T-11-05-03 (Main.unity committed alone+immediately in the builder-run task), T-11-05-04 (two clusters built + all 5 wa* AND all 5 tg* stamps verified non-zero in the saved scene). No new threat surface — client-only static UI, no network/auth/schema changes.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ONB-02 complete: both channels' code panels reassure at the scariest step with channel-true copy.
- ONB-03 fully complete (logic 11-04 + scene 11-05): the interactive «Бот подключён!» moment now has real per-channel buttons/labels; end-to-end flow (auth → moment → deep-link) is device-UAT-ready.
- Remaining phase plans (first-steps card et al.) build on an unchanged auth flow; the builder is idempotent, so visual recalibration is a re-run away.

## Self-Check: PASSED

- All created files present on disk (builder + SuccessCheckPop + Lock.png + SVG/renderer, each with .meta where applicable).
- Both task commits present in git history (`9de7101`, `9494452`).
- All acceptance criteria re-run and PASS: TrustBlock ×2 last-child, SuccessCta ×2 with correct fathers, 10 non-zero stamps, verbatim copy probes, zero builder QR strings, Manager.cs GetChild intact; EditMode suite 1165/1165 green after both the code compile and the scene save.

---
*Phase: 11-first-run-onboarding-flow*
*Completed: 2026-07-18*
