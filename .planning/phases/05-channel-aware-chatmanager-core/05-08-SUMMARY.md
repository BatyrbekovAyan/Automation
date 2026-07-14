---
phase: 05-channel-aware-chatmanager-core
plan: 08
subsystem: ui
tags: [unity, csharp, telegram, media-presentation, sticker, video-note, rounded-corners, tmp, bubble-transparency]
gap_closure: true
device_uat_round: 2

# Dependency graph
requires:
  - phase: 05-07
    provides: "TelegramMediaType.TgsStickerMime + the .tgs→Sticker rule; isVideoNote/isGif 4-layer flags; the MessageItemView .tgs branch, кружок circular crop + duration badge, and ApplyTelegramMediaOverlays/GetOrCreateOverlayPill overlay scaffolding this plan refines"
provides:
  - "BubbleTransparencyPolicy.IsTransparent(isSticker, isVideoNote, isPlaceholderActive, hideBubble) — pure seam; a Telegram video note now floats chrome-free like a sticker (native TG shows no bubble around the circle), UNAVAILABLE note/sticker still shows its retry card on a visible bubble"
  - "MessageItemView .tgs branch renders a deliberate sticker-slot-sized (396²) neutral rounded placeholder CARD with its own fill + centered «Стикер» caption (+ guarded mid-gray sticker glyph), replacing the collapsed white-silhouette + tiny pill the device showed"
affects: [phase-8-device-uat]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Bubble chrome-free decision extracted to a pure channel-blind seam (BubbleTransparencyPolicy) with the WhatsApp-regression matrix unit-tested; isVideoNote is Telegram-only + default false so the WhatsApp render path is byte-identical (05-06/05-07 seam precedent)"
    - "Placeholder overlays that must READ on a transparent bubble carry their OWN fill (a self-contained card), never a white tint on the bubble — a white silhouette vanishes against the light paper chat bg"

key-files:
  created:
    - Assets/Scripts/Chat/BubbleTransparencyPolicy.cs
    - Assets/Tests/Editor/Chat/BubbleTransparencyPolicyTests.cs
  modified:
    - Assets/Scripts/UI/MessageItemView.cs

key-decisions:
  - "Video note floats bubble-free by adding isVideoNote to the transparency decision (extracted to BubbleTransparencyPolicy). The !isPlaceholderActive gate is preserved so an UNAVAILABLE note still shows its download/retry card against a visible bubble — mirroring stickers."
  - "The note's timestamp needed NO code change: a video note is MessageType.Video with no caption, so it already routes to the white-text + timeBackground dark-pill media overlay — readable on the now-transparent bubble (no dark-on-video)."
  - "The .tgs placeholder is a self-contained CARD with its own neutral fill (not a white silhouette on the bubble). Root cause of the device 'collapsed to a tiny pill': the old placeholder tinted stickerPlaceholder WHITE and sat on a transparent bubble → invisible against the light paper chat bg. The card carries its own fill + a mid-gray (never white) glyph."
  - "The glyph is GUARDED on stickerPlaceholder != null and tinted mid-gray; text-only «Стикер» is the graceful fallback (project memory: never depend on a sprite/TMP-icon that may not render). Native .tgs (Lottie) animation stays a v2 candidate."
  - "Verification was the project's sanctioned in-Editor bridge (owner's Editor was open, headless refuses on a held lock), gated on editorAssemblyWrittenUtc to rule out the stale-green trap — NOT Tools/run-tests-headless.sh."

patterns-established:
  - "Pattern: a transparent-bubble media placeholder is a self-contained card (own fill + rounded corners + centered caption), maskable + non-raycast, toggled OFF when its signal is absent (T-0507-04 recycled-bubble safety)"

requirements-completed: []  # No formal 05-08-PLAN.md — round-2 device-UAT polish on CHAT-03 (already completed by 05-07)

# Metrics
duration: ~24min active (2 human-action checkpoint pauses: owner's Editor held the project lock at the test gate)
completed: 2026-07-14
---

# Phase 5 Plan 08: Telegram Media Device-UAT Round-2 Polish Summary

**Two owner-device follow-up fixes to the 05-07 Telegram media treatments: a video note (кружок) now floats bubble-free (transparency decision extracted to a pure BubbleTransparencyPolicy seam + isVideoNote; time stays readable via the existing media overlay), and a `.tgs` animated sticker renders a deliberate sticker-slot-sized neutral placeholder CARD with its own fill + «Стикер» caption instead of the collapsed white-silhouette pill — both Telegram-only, WhatsApp byte-identical, 1007/1007 EditMode green.**

## Performance

- **Duration:** ~24 min active execution, spanning two human-action checkpoint pauses (the owner's Unity Editor held the single-instance project lock at the test gate — first at the initial gate, then a transient "Editor closed" misread that I corrected with process/lsof forensics)
- **Started:** 2026-07-14T16:59Z (first edit)
- **Completed:** 2026-07-14T17:23Z
- **Tasks:** 2 fixes
- **Files modified:** 3 (2 created + 2 generated .meta, 1 modified)

## Accomplishments

- **FIX 1 (video note → bubble-free):** Extracted the bubble-transparency decision into a pure, EditMode-testable seam `BubbleTransparencyPolicy.IsTransparent(isSticker, isVideoNote, isPlaceholderActive, hideBubble)`. `UpdateBubbleVisuals` now calls it and passes `currentVm.isVideoNote`, so a кружок renders chrome-free (transparent fill, no border, no tail) — the circle floats on the chat bg like native Telegram, not inside a green rectangle. The `!isPlaceholderActive` gate is preserved: an UNAVAILABLE note still shows its download/retry card against a visible bubble (mirrors stickers). **Verified the time stays readable** — a video note is `MessageType.Video` with no caption, so it already routes to the white-text + `timeBackground` dark-pill media overlay; no dark-on-video, no code change needed for the time path.
- **FIX 2 (.tgs → sticker-sized card):** Replaced the collapsed white-silhouette + tiny «Стикер» pill (the device showed "a sticker with no renderable content") with a deliberate sticker-slot-sized (396²) neutral rounded **card** that carries its OWN fill — so it reads on the transparent sticker bubble. Content = a centered bold «Стикер» caption plus the project sticker glyph tinted **mid-gray** above it (guarded on `stickerPlaceholder != null`; text-only fallback). Root cause of the device bug: the old placeholder tinted the sprite WHITE and sat on a transparent bubble → invisible against the light paper chat bg. Gated exactly on `type==Sticker && mimeType==TgsStickerMime` so WhatsApp/webp stickers are untouched; overlays maskable + non-raycast, toggled OFF when absent (T-0507-04).
- **WhatsApp regression net:** `isVideoNote` is Telegram-only and defaults false; the `.tgs` card gates on the tgs mime (WhatsApp never sends it). No WhatsApp render path changed — proven by the pre-existing suite staying green: **1007/1007 EditMode, 0 failed, 0 inconclusive** (997 baseline + 10 new BubbleTransparencyPolicy cases).

## Task Commits

1. **FIX 1: float Telegram video notes bubble-free** — `72a5909` (fix) — BubbleTransparencyPolicy.cs + BubbleTransparencyPolicyTests.cs + MessageItemView.cs (UpdateBubbleVisuals hunk)
2. **FIX 2: render .tgs stickers as a sized placeholder card** — `a27cf16` (fix) — MessageItemView.cs (constants, .tgs branch, overlay swap, ToggleStickerPlaceholderCard)

**Plan metadata:** _(final docs commit — this SUMMARY + 05-HUMAN-UAT + STATE + ROADMAP)_

_Note: the two fixes share MessageItemView.cs; FIX 1's hunk (UpdateBubbleVisuals) was staged deterministically via `git apply --cached` so each commit is atomic and per-fix. Both intermediate states are compile-consistent._

## Verification

- **Method: the project's sanctioned in-Editor test bridge — NOT `Tools/run-tests-headless.sh`.** The owner's Unity Editor (PID 6897) was open on this project throughout, holding the single-instance project lock (confirmed by `lsof` on `Temp/UnityLockfile`), so the headless runner correctly refuses and the lock must not be deleted. I armed the bridge (empty `Temp/claude/run-tests.trigger`); the owner focused the Editor, which imported the two new files, recompiled, and ran the full EditMode suite.
- **Result: `status: completed`, `overall: Passed`, `total: 1007`, `passed: 1007`, `failed: 0`, `inconclusive: 0`.**
- **Stale-green trap ruled out:** gated on `editorAssemblyWrittenUtc = 2026-07-14T17:06:24Z`, which postdates my last edit (17:01:16Z); `total` rising to 1007 (997 + the 10 new cases) proves the new test file compiled and ran. The `.meta` for both new files were generated by that same bridge import — so the `.cs`+`.meta` commits were unblocked.
- Pixel-perfect appearance of both treatments (exact card colour/rounding, glyph size, note time position over the circle) is the remaining **Phase-8 device re-verify** item — this plan is verified code-correct + suite-green, not by eye.

## Files Created/Modified

- `Assets/Scripts/Chat/BubbleTransparencyPolicy.cs` (created) — pure static policy: `IsTransparent(isSticker, isVideoNote, isPlaceholderActive, hideBubble)`; documents the WhatsApp-regression invariant.
- `Assets/Tests/Editor/Chat/BubbleTransparencyPolicyTests.cs` (created) — 10-case matrix: plain in/out never transparent (WA regression), plain-with-placeholder, sticker ±placeholder, video note ±placeholder, hideBubble forces transparent (even plain, even with placeholder), and both-flags defensive.
- `Assets/Scripts/UI/MessageItemView.cs` (modified) — `UpdateBubbleVisuals` calls the seam with `isVideoNote`; `.tgs` branch clears `messageImage` and defers to the card; new `TgsCard*` design constants; `ApplyTelegramMediaOverlays` swaps `ToggleStickerLabelOverlay` → `ToggleStickerPlaceholderCard` (the self-contained card).

## Decisions Made

See frontmatter `key-decisions`. Headline: the note floats bubble-free via the extracted transparency seam (time already reads via the media overlay), and the `.tgs` placeholder became a self-contained card because a white silhouette on a transparent bubble is invisible on the paper chat bg.

## Deviations from Plan

None — both fixes implemented exactly as specified. The two Editor-lock checkpoint pauses were the environment's own single-instance-lock gate (the second was a transient "Editor closed" misread by the coordinator that I corrected with `ps`/`lsof` evidence before touching anything), not a deviation. No lockfile was deleted and no headless run was forced while a real Editor held the lock.

## Issues Encountered

- **Editor lock at the test gate + a "closed" misread (handled, not a bug):** the owner's Editor stayed open the whole session (started 20:38, never restarted). The headless runner refused as designed. A mid-task "Editor closed, lock free" report was contradicted by live `ps` (PID 6897, STAT `Ss`) + `lsof` (Unity holds `Temp/UnityLockfile`); I pushed back with the evidence rather than delete a held lock, and verification proceeded via the sanctioned in-Editor bridge (which had already produced a fresh green run of the exact changes). The coordinator independently confirmed the forensics and accepted the bridge result.

## Known Stubs

- **The `.tgs` card is a deliberate placeholder, not incomplete wiring:** the gzipped-Lottie artwork is undecodable in Unity without an rlottie-class plugin, so the card (glyph + «Стикер») IS the v1.1 target (unchanged from 05-07's intent, just rendered as a proper sized card now); native `.tgs` animation remains the recorded v2 candidate. No other stubs — the note treatment renders live data (`vm.duration`, live flags).

## Threat Surface

No new threat surface: no endpoints, auth, secrets, schema, or scene/prefab changes. The transparency seam is a pure boolean; the card overlay reads only already-Normalized fields and is maskable + non-raycast + toggled-off-when-absent (T-0507-04 recycled-bubble safety, same guarantee as the 05-07 overlays).

## User Setup Required

None for code. **On-device visual confirmation of both round-2 treatments is the remaining Phase-8 device-UAT item** — the кружок should float bubble-free (no green rectangle) with a legible time pill, and the `.tgs` bubble should read as a deliberate sticker-sized card (not a failure state or a tiny pill).

## Next Phase Readiness

- Both 05-HUMAN-UAT round-2 gaps are resolved-in-code; the file's statuses are flipped accordingly.
- Phase 8 device UAT: two refined visual checks (note bubble-free float; `.tgs` sized card) alongside the existing 05-07 treatment checks and the TG media download-by-id / reactions / reply items.

## Self-Check: PASSED

Both created files + both generated `.meta` exist on disk; commits `72a5909` and `a27cf16` are in the log with only my 5 files across them and zero deletions; the in-Editor bridge suite is 1007/1007 green (0 failed, 0 inconclusive) against an assembly (17:06:24Z) that postdates the edits.

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-14*
