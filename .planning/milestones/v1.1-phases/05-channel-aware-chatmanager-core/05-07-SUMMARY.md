---
phase: 05-channel-aware-chatmanager-core
plan: 07
subsystem: ui
tags: [unity, csharp, tapi, telegram, chat-pipeline, media-presentation, sticker, video-note, gif, rounded-corners, tmp]
gap_closure: true

# Dependency graph
requires:
  - phase: 05-06
    provides: "TelegramMediaType.Refine / TelegramMediaShape.Resolve seams + ApplyTelegramMediaShape (the Telegram-gated Normalize block these flags are minted in); coarse-but-correct classification the treatments refine"
  - phase: 05-02
    provides: "ChatManager.ActiveChannel — the channel gate that keeps all three signals Telegram-only"
provides:
  - "TelegramMediaType.TgsStickerMime + application/x-tgsticker → Sticker rule: a .tgs animated sticker classifies as Sticker (never a document card)"
  - "TelegramVideoNoteHeuristic.IsVideoNote — pure кружок detection (square + video.mp4 + 0<duration≤60; is_round deliberately ignored — unreliable per SHAPES.md Q2)"
  - "isGif (JSON, RawMessage) + isVideoNote (derived) carried through all four pipeline layers (RawMessage → NormalizedMessage → MessageViewModel → MessageItemView), minted channel-gated in ApplyTelegramMediaShape"
  - "Three Telegram-only MessageItemView treatments: .tgs borderless placeholder + «Стикер» (no futile download), circular video-note bubble (half-side radius) + duration badge, 'GIF' corner badge on the video pipeline"
affects: [phase-8-device-uat]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Telegram-only presentation flags are minted ONLY inside ApplyTelegramMediaShape (Telegram-gated + media-gated) — the heuristic itself is channel-blind/pure; the call site is the WhatsApp regression net (05-06 architecture)"
    - "Bubble overlays (badges/labels) are created at bind under MediaContainer via find-child-by-name → create-if-absent → toggle-per-vm; every graphic child maskable=true + raycastTarget=false; toggled INACTIVE when the flag is false so recycled bubbles never leak"
    - "Overlays are non-layout-affecting (sit ON the media, never widen the time row) — no AdjustTextBubbleSize re-run"

key-files:
  created:
    - Assets/Scripts/Chat/TelegramVideoNoteHeuristic.cs
    - Assets/Tests/Editor/Chat/TelegramVideoNoteHeuristicTests.cs
  modified:
    - Assets/Scripts/Chat/TelegramMediaType.cs
    - Assets/Scripts/Chat/RawMessage.cs
    - Assets/Scripts/Chat/NormalizedMessage.cs
    - Assets/Scripts/UI/MessageViewModel.cs
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Scripts/UI/MessageItemView.cs
    - Assets/Tests/Editor/Chat/TelegramMessageTypeTests.cs
    - Assets/Tests/Editor/Chat/TelegramMediaNormalizeTests.cs

key-decisions:
  - ".tgs never downloads: the bytes are gzipped Lottie (undecodable in Unity), so the branch renders the deliberate placeholder immediately and skips LoadStickerViaDownload — no wasted request, no dead-end in ShowStickerLoadFailed. Native .tgs animation stays a v2 candidate."
  - "IsVideoNote ignores is_round BY DESIGN — the capture proved it false for a genuine кружок on both messages/get and messages/id/get (SHAPES.md Q2). Detection = square + default name video.mp4 + duration≤60; the false positive (a square regular video renders round) is the accepted cosmetic trade recorded in 05-HUMAN-UAT gap 2."
  - "The circular note is the existing video pipeline + a half-side ImageWithRoundedCorners radius on a 1:1-pinned bubble — no new masking machinery, tap-to-play untouched; inline autoplay deliberately out of v1.1 scope."
  - "isVideoNote/isGif live on MessageViewModel as flat primitives so ChatHistoryCache (JsonUtility) persists them — treatments survive chat reopens without re-Normalize."
  - "Overlay pills reuse ImageWithRoundedCorners via direct AddComponent (the file already imports Nobi.UiRoundedCorners — no Type.GetType/assembly-scan fragility)."

patterns-established:
  - "Pattern: presentation-signal minting = pure channel-blind seam + Telegram-gated call site; the full pre-existing suite is the WhatsApp byte-identical proof"
  - "Pattern: GetOrCreateOverlayPill — shared create-or-reuse corner-anchored maskable pill (translucent rounded bg + centered white TMP) for all bubble badges"

requirements-completed: [CHAT-03]

# Metrics
duration: 25min (plus a human-action checkpoint pause: Editor lock held at the test gate)
completed: 2026-07-14
---

# Phase 5 Plan 07: Telegram Media Presentation Treatments Summary

**The three device-UAT presentation gaps are flipped in code: a .tgs animated sticker renders as a deliberate borderless sticker placeholder + «Стикер» (never a document card), a video note (кружок) renders as a circular chrome-free bubble with a duration badge + tap-to-play, and a GIF keeps the video pipeline with a bold "GIF" corner badge — all Telegram-only via two new 4-layer flags + a mimetype rule, WhatsApp byte-identical, 988/988 EditMode green.**

## Performance

- **Duration:** ~25 min active execution (started 2026-07-14T15:11:53Z, completed 2026-07-14T15:37Z), spanning a human-action checkpoint (owner closed the Unity Editor to free the project lock for the headless run)
- **Tasks:** 3 (Task 1 TDD seams + wiring; Task 2 view treatments; Task 3 suite gate + docs)
- **Files modified:** 12 (2 created + 2 generated .meta, 8 modified)

## Accomplishments

- **Gap 1 (.tgs → sticker placeholder):** `TelegramMediaType.Refine` now maps `application/x-tgsticker` → `MessageType.Sticker` (new `TgsStickerMime` const). `MessageItemView`'s new pre-Sticker branch keys on `vm.mimeType == TgsStickerMime`: borderless square via `SetupMaskedLayout(1,1,true)`, paints `stickerPlaceholder` white/preserveAspect, adds a centered «Стикер» pill, and **never** calls `LoadStickerViaDownload` (undecodable Lottie). Tap-to-open preserved.
- **Gap 2 (кружок → circular + duration):** new pure `TelegramVideoNoteHeuristic.IsVideoNote(baseType, fileName, mediaInfo)` — true iff raw type `"video"` + file name `"video.mp4"` + square dims (>0) + 0<duration≤60; `is_round` deliberately unread (broken on both endpoints). In the Video branch the bubble ratio pins to 1:1 and `ImageWithRoundedCorners.radius` becomes half the side (square → circle); a bottom-center duration pill formats via the audio `"{0:D1}:{1:D2}"` precedent. Existing tap-to-play (VideoController) untouched; autoplay = v2 polish.
- **Gap 3 (GIF badge):** `RawMessage.isGif` (`[JsonProperty("isGif")]`, absent ⇒ false) rides the tapi flag (GIFs arrive `type:"sticker"` + `isGif:true` + `video/mp4`, refined to Video since 05-06). The video pipeline is untouched; a bold "GIF" pill anchors top-left. No filename chrome.
- **4-layer pipeline integrity (chat-data-flow):** `isGif` starts at RawMessage (JSON), `isVideoNote` is derived at Normalize; both live on NormalizedMessage + MessageViewModel (flat, JsonUtility-persisted), are minted ONLY inside `ApplyTelegramMediaShape` (inside the `ActiveChannel==Telegram` block), and are copied in `CreateViewModel`.
- **WhatsApp regression net:** all signals default false and are minted only behind the Telegram gate — the full pre-existing suite stayed green. Final: **988/988 EditMode, 0 failed, 0 inconclusive** (966 baseline + 22 new: 5 Refine, 13 IsVideoNote, 4 isGif binding).

## Task Commits

1. **Task 1: Detection seams + 4-layer flag wiring** — `e7dafa6` (feat)
2. **Task 2: MessageItemView treatments** — `9194111` (feat)

**Plan metadata:** _(final docs commit — this SUMMARY + STATE + ROADMAP + REQUIREMENTS + 05-HUMAN-UAT + PLAN)_

## TDD Gate Compliance

Task 1 was `tdd="true"`, but the RED phase could not be **separately observed or committed**: the Unity Editor held the project lock for the entire authoring window, so the headless runner refused to launch (`exit 2`) until the owner closed the Editor at the Task-3 checkpoint. The RED state was structural rather than observed — the new tests reference `TelegramVideoNoteHeuristic` and `TelegramMediaType.TgsStickerMime`, which did not exist before the production edits, so a tests-only tree cannot compile (the same compile-error RED 05-06 recorded). Consequence: there is **no separate `test(...)` commit**; tests + implementation landed together in `e7dafa6`, and the RED→GREEN transition was verified only in aggregate (966 pre-plan baseline → 988 green including all 22 new cases on the first post-lock run).

## Files Created/Modified

- `Assets/Scripts/Chat/TelegramVideoNoteHeuristic.cs` (created) — pure, null-tolerant кружок heuristic; documents WHY is_round is ignored.
- `Assets/Tests/Editor/Chat/TelegramVideoNoteHeuristicTests.cs` (created) — 13-case matrix: canonical true, 60/61 boundary, non-square, non-default name, document base type, GIF-shaped, is_round:false ignored, null/empty media_info, zero/fractional duration, null args. Synthetic PII-free JSON.
- `Assets/Scripts/Chat/TelegramMediaType.cs` — `TgsStickerMime` const + `.tgs`→Sticker rule (after the Chat/Reaction guard, before the video/audio prefixes).
- `Assets/Scripts/Chat/RawMessage.cs` — `isGif` flag (JsonProperty, follows the isReply convention).
- `Assets/Scripts/Chat/NormalizedMessage.cs` / `Assets/Scripts/UI/MessageViewModel.cs` — `isVideoNote` + `isGif` (default false ⇒ WhatsApp copy byte-identical; VM fields flat for ChatHistoryCache persistence).
- `Assets/Scripts/Main/ChatManager.cs` — flags minted in `ApplyTelegramMediaShape` (Telegram-gated + media-gated) + copied in `CreateViewModel`. `ResolveMessageType` needed no change; Sticker ∈ `IsMediaMessageType`, so the .tgs reaches `ApplyTelegramMediaShape` and gets its mime stamped for the view to key on.
- `Assets/Scripts/UI/MessageItemView.cs` — .tgs placeholder branch; note ratio pin + half-side radius in `SetupMaskedLayout`; `ApplyTelegramMediaOverlays` + three toggles + shared `GetOrCreateOverlayPill` (all children maskable=true, raycastTarget=false, inactive when flag false).
- `Assets/Tests/Editor/Chat/TelegramMessageTypeTests.cs` — +5 (.tgs→Sticker, literal mime value, webp sticker stays Sticker, sticker+video/mp4→Video, Chat never reclassified by .tgs mime).
- `Assets/Tests/Editor/Chat/TelegramMediaNormalizeTests.cs` — +4 isGif JSON-binding cases (true / absent⇒false / explicit false / full observed GIF shape).

## Decisions Made

See frontmatter `key-decisions`. Headline: .tgs never downloads (placeholder is the deliberate v1.1 target, native Lottie = v2); is_round is ignored by design; the circle is the existing video pipeline + half-side corner radius, not new masking machinery.

## Deviations from Plan

None — plan executed exactly as written. (The Task-3 pause was the plan's own Editor-lock human-action gate, not a deviation; resolved when the owner closed the Editor.)

## Issues Encountered

- **Editor lock at the test gate (expected, handled per plan):** the Unity Editor was open during authoring, so `Tools/run-tests-headless.sh` refused (exit 2). Returned the plan's `checkpoint:human-action`; owner closed the Editor; the first post-lock full run was green (988/988) and generated the two new files' `.meta` during import. Also documented under TDD Gate Compliance.

## Known Stubs

- **.tgs sticker placeholder is a deliberate design, not incomplete wiring:** the artwork itself is not rendered (gzipped Lottie is undecodable in Unity without an rlottie-class plugin). The placeholder (sticker glyph + «Стикер») IS this plan's specified v1.1 target per 05-HUMAN-UAT gap 1; native .tgs animation is recorded there as the v2 candidate. No other stubs — both badges and the circular treatment render real data (`vm.duration`, live flags).

## Threat Register Coverage

- **T-0507-01 (DoS, pure seams):** mitigated — `IsVideoNote` is null-tolerant (null/malformed media_info, null baseType/fileName ⇒ false, never throws; unit-tested), same guarantee as `TelegramMediaShape.Resolve`; the `.tgs` rule is a plain string compare inside the existing null-guarded `Refine`.
- **T-0507-02 (Spoofing, cosmetic-only):** accepted per plan — a forged mimetype/isGif flips only the cosmetic treatment inside the media-render boundary.
- **T-0507-03 (Info disclosure, fixtures):** honored — all 22 new test cases use synthetic PII-free JSON; real samples stay in the gitignored `Tools/tapi/samples/`.
- **T-0507-04 (Recycled-bubble leak):** mitigated — every overlay is explicitly SetActive(false) when its signal is absent (`ApplyTelegramMediaOverlays` runs on every `SetupMaskedLayout`), so a recycled bubble cannot carry a prior badge.
- No new threat surface: no endpoints, auth, secrets, schema, or scene/prefab changes; render treatments read only already-Normalized fields.

## User Setup Required

None for code. **On-device visual confirmation of the three treatments is the remaining Phase-8 device-UAT item** — this plan is verified headlessly (988/988) + by grep (pipeline completeness, channel gating, treatment keying), not by eye. Device pass should confirm: .tgs bubble reads as a deliberate sticker (not a failure state), the кружок circle crops cleanly with a legible duration pill, and the GIF badge sits correctly over live thumbnails.

## Next Phase Readiness

- All three 05-HUMAN-UAT gaps are resolved-in-code; the file's gap statuses are flipped accordingly.
- Phase 8 device UAT gains three visual checks (above) alongside the existing TG media download-by-id / reactions / reply items.

## Self-Check: PASSED

Both created files + both generated .meta exist on disk; commits `e7dafa6` and `9194111` are in the log; full EditMode suite 988/988 green (0 failed, 0 inconclusive); grep matrix (TgsStickerMime / IsVideoNote / isGif / CreateViewModel copies / vm.* keys / maskable=true) all present.

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-14*
