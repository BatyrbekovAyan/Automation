---
phase: 05-channel-aware-chatmanager-core
slice: 05-07
reviewed: 2026-07-14T16:01:31Z
depth: standard
commits: e7dafa6..9194111
files_reviewed: 10
files_reviewed_list:
  - Assets/Scripts/Chat/TelegramVideoNoteHeuristic.cs
  - Assets/Scripts/Chat/TelegramMediaType.cs
  - Assets/Scripts/Chat/RawMessage.cs
  - Assets/Scripts/Chat/NormalizedMessage.cs
  - Assets/Scripts/UI/MessageViewModel.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Scripts/UI/MessageItemView.cs
  - Assets/Tests/Editor/Chat/TelegramVideoNoteHeuristicTests.cs
  - Assets/Tests/Editor/Chat/TelegramMessageTypeTests.cs
  - Assets/Tests/Editor/Chat/TelegramMediaNormalizeTests.cs
findings:
  critical: 0
  warning: 1
  info: 3
  total: 4
status: issues_found
---

# Phase 5 (05-07): Code Review Report — Telegram media presentation gaps

**Reviewed:** 2026-07-14T16:01:31Z
**Depth:** standard
**Files Reviewed:** 10 (diff `e7dafa6^..9194111`, two feat commits)
**Status:** issues_found (0 CR / 1 WR / 3 IN)

## Summary

Reviewed the three-gap closure: `.tgs` → Sticker + borderless placeholder, video-note heuristic → circular bubble + duration badge, `isGif` → GIF corner badge. The channel-gating architecture is sound — both flags are minted exclusively inside `ApplyTelegramMediaShape` (ChatManager.cs:1695-1696), which is reached only from the `ActiveChannel == ChatChannel.Telegram` block (ChatManager.cs:1568). The heuristic is genuinely null-tolerant and the new tests pin the right boundaries. One warning: the cache-refresh merge (`RefreshCachedMessageMedia`) was not extended for the new presentation fields, so rows cached before this feature — including the exact UAT probe messages that motivated the phase — keep rendering the old presentation forever.

### Scrutiny-point verification (all six requested checks)

**1. Overlay bind-time lifecycle (pooling).** Overlays use create-or-reuse by child name under `MediaContainer`, toggled OFF whenever their signal is absent, and `ApplyTelegramMediaOverlays` runs at the tail of every `SetupMaskedLayout` call (MessageItemView.cs:1917) — so every media re-layout re-evaluates all three. Critically, **MessageListView does not pool rows across messages**: every message is `Instantiate`d (MessageListView.cs:536, 587, 798) and destroyed on clear; re-`Bind` happens only with the same `BoundVm` (tail update at :583, media-refresh events). A flag-off rebind after a flag-on bind can therefore only be the *same* message, whose flags are immutable — the classic pooled stale-badge bug is structurally unreachable today, and the toggle-off logic is correct defense-in-depth for when that changes. Non-media transitions hide the whole `MediaContainer` (badges with it): `SetLayoutToButton` (:2048-2051), `HandleFinalFailure` (:2900-2903), `VisualMediaWaterfall` no-thumb path (:3027). Circular-radius reset across rebinds is safe for the same structural reason, with one latent trap documented in IN-01.

**2. Circular crop radius.** Not a constant and not read from a live rect: `rounded.radius = mediaSize.x * 0.5f` (MessageItemView.cs:1909-1911) where `mediaSize = MediaBubbleSize.Resolve(1.0f)` = 700×700 (square for aspect ≤ 1) — the *same* value written to the container `LayoutElement.preferredWidth/Height` (:1868-1869), so radius and final rect agree by construction. The material is rebaked post-layout: every terminal media path ends in `ForceRebuildRoutine`, whose sync phase runs `ForceRebuildLayoutImmediate` then `RefreshCorners` → `rounded.Validate() + Refresh()` (:2782-2785) before first render. Aspect pinning: the 1:1 pin at :846 flows through `SmartMediaRoutine`; the recompute sites that skip the pin (`OnDownloadClicked` :2114, `ReclampMediaCaption` :1507, `AdjustTextBubbleSize` :1683) still get 1.0 because a flagged note has `aspectRatio` exactly 1.0 by construction (heuristic requires integer `width == height`, and `TelegramMediaShape` computes aspect as width/height). The 444-vs-full-width caption clamp is a non-issue: Telegram notes cannot carry captions, and even a hypothetical captioned note clamps to `Resolve(1.0).x` = the actual media width.

**3. .tgs branch.** Confirmed no download: the branch (MessageItemView.cs:870-892) calls neither `LoadStickerViaDownload` nor `DisplayMedia`; `downloadButton` is disabled (:878) and no coroutine is started, so `HandleFinalFailure`/retry paths can never resurrect one. Placeholder renders via the prefab's existing `messageImage` (maskable by prefab construction); the «Стикер» pill background and TMP label are explicitly `maskable = true` + `raycastTarget = false` in `GetOrCreateOverlayPill`. WhatsApp sticker untouched: the guard requires `vm.mimeType == "application/x-tgsticker"`, and `mimeType` is stamped either Telegram-side (`shape.MimeType` passthrough, ChatManager.cs:1685) or from the WA `body.mimetype` (`image/webp`/null) — the WA payload can never satisfy it, so WA stickers fall through to the pre-existing generic Sticker branch verbatim.

**4. Heuristic safety.** `TelegramVideoNoteHeuristic.IsVideoNote` is fully null-tolerant: null/wrong `baseType` or `fileName` fail the string comparisons; null or non-object `media_info` fails the `is JObject` pattern; malformed numerics fail `TryParse` (InvariantCulture) and degrade to 0. Boundaries: 60s is inclusive (`duration <= 60`, matching Telegram's cap — test-pinned); `width == height == 0` is rejected by `width <= 0 || height <= 0` *before* the equality check; zero/negative duration rejected by `duration > 0`. Minted only at ChatManager.cs:1695 under the Telegram gate. A GIF (raw type `"sticker"`) and a document-sent video (raw type `"document"`) can't collide — the heuristic reads the *raw* type string, both test-pinned.

**5. isGif JSON binding.** `[JsonProperty("isGif")]` on a public field, consistent with the file's conventions (`file_name`, `media_info`). `MessagesResponseRaw` is parsed exclusively via `JsonConvert.DeserializeObject` (ChatManager.cs:592, 1144, 1230). Grep of all `JsonUtility` call sites (OutboxStore, ReactionTargetCache, QuotedMessageCache, ChatHistoryCache, LinkScraper, ChatsResponse, Secrets) shows none deserializes `RawMessage` — no surface where the attribute is silently ignored on the parse path. `MessageViewModel.isVideoNote/isGif` are public fields on a `[Serializable]` class → persisted by `ChatHistoryCache` (JsonUtility); absent keys in old caches default to `false` (consequence in WR-01).

**6. WhatsApp regression sweep.** Every diff hunk audited for WA reachability: `RawMessage.isGif` — read only inside `ApplyTelegramMediaShape`; `TelegramMediaType.Refine` — reached only via `ResolveMessageType`'s Telegram arm (ChatManager.cs:1666-1669); `CreateViewModel` — copies two fields that are always `false` on WA; `MessageItemView` shared code — the `.tgs` guard is unsatisfiable for WA mimeTypes, `if (vm.isVideoNote) bubbleRatio = 1.0f` is a no-op, the radius ternary falls to the original `23f`, and `ApplyTelegramMediaOverlays` performs three `Find()` no-ops creating nothing when all signals are false. No WA render change.

## Warnings

### WR-01: Pre-update cached rows never receive the new presentation fields — UAT re-verification will show the old rendering on the exact probe messages

**File:** `Assets/Scripts/Main/ChatManager.cs:1806-1857` (`RefreshCachedMessageMedia`), interacting with the new fields minted at `:1695-1696`
**Issue:** `ChatHistoryCache` serves cached chats directly (bypassing `CreateViewModel`), and when a background sync re-encounters an already-cached message, `RefreshCachedMessageMedia` merges **only** `mediaUrl`/`videoUrl`/`thumbnailUrl`/`expireTime`. It never propagates `isVideoNote`, `isGif`, `mimeType`, or a re-refined `type`. Consequence for any Telegram chat cached before this build: a video note stays a square plain video, a GIF never gets its badge, and a `.tgs` sticker stays `MessageType.Document` (cached pre-refine) and renders as a document card — *permanently*, until the row ages out of the 100-message cache window or history is cleared. The device that reported 05-HUMAN-UAT gaps 1-3 has these exact messages (e.g. probe_23368) cached, so the owner's re-test of this fix is likely to reproduce the "unfixed" rendering and fail UAT falsely.
**Fix:** Extend the merge to refresh presentation fields when they differ, reusing the existing dirty/re-bind machinery:
```csharp
// after the thumbnailUrl block in RefreshCachedMessageMedia
if (cached.type != refreshed.messageType && refreshed.messageType != MessageType.Unknown)
{
    cached.type = refreshed.messageType;      // e.g. pre-update .tgs cached as Document
    mediaRefreshed = true;
}
if (cached.isVideoNote != refreshed.isVideoNote || cached.isGif != refreshed.isGif
    || !string.Equals(cached.mimeType, refreshed.mimeType, StringComparison.Ordinal))
{
    cached.isVideoNote = refreshed.isVideoNote;
    cached.isGif       = refreshed.isGif;
    cached.mimeType    = refreshed.mimeType;
    mediaRefreshed = true;   // marks cache dirty + fires OnMessageMediaRefreshed → re-bind
}
```
Alternative (blunter): bump the `ChatHistoryCache` file version/key so pre-update caches are discarded once. Either way, add a normalize-vs-cache merge test. If instead the team decides stale-cache staleness is acceptable, document it in 05-HUMAN-UAT so the re-test is done in a *fresh* chat or after «Очистить историю» — otherwise the UAT signal is corrupted.

## Info

### IN-01: Circle/card radius correctness silently depends on downstream `RefreshCorners` — and the stencil-copy staleness check compares size only, not radius

**File:** `Assets/Scripts/UI/MessageItemView.cs:1909-1911` (radius write), `:2858-2861` (`InvalidateStaleMaskMaterial`)
**Issue:** This diff makes `rounded.radius` *vary* between binds for the first time (350 vs 23). `ImageWithRoundedCorners` does not rebake on a runtime `radius` assignment (package `Refresh()` fires only from `OnEnable`/`OnValidate`/`OnRectTransformDimensionsChange`), and `InvalidateStaleMaskMaterial` rebuilds the stencil copy only when the baked **width/height** drift — a radius-only change on an identically-sized rect (note 700×700 → square video 700×700) would keep the stale shape. Today this is unreachable: rows are never reused across messages, same-message rebinds can't flip `isVideoNote`, and every terminal path runs `ForceRebuildRoutine` → `RefreshCorners`. But the invariant is implicit and one refactor away from breaking (introducing row pooling, or any early-return path added to `SetupMaskedLayout` callers).
**Fix:** Two cheap hardenings: (a) compare the radius component too in the staleness check — `|| Mathf.Abs(rendered.GetVector(CornerBakeProp).z - rounded.radius * 2f) > 0.5f`; (b) or call `RefreshCorners(messageImage.gameObject)` immediately after the radius assignment in `SetupMaskedLayout`. Either makes the radius self-consistent instead of convention-consistent.

### IN-02: .tgs placeholder wires a tap listener that can never fire a behavior

**File:** `Assets/Scripts/UI/MessageItemView.cs:884, 888-891`
**Issue:** The branch stages `fullScreenSprite = stickerPlaceholder` and adds a `Button` → `OnVisualClicked(vm)`, but `OnVisualClicked` (:3676-3714) only handles `Video` and `Image` — for `Sticker` it falls through and does nothing. Dead wiring (mirrors the pre-existing regular-sticker branch, so it's consistent, not a regression).
**Fix:** Either drop the button + `fullScreenSprite` staging from the `.tgs` branch, or leave as-is for symmetry with the WebP sticker branch — but don't expect a tap affordance on device.

### IN-03: Minor quality notes in the new overlay code

**File:** `Assets/Scripts/UI/MessageItemView.cs:1965` (duration format), `:1976-2028` (`GetOrCreateOverlayPill`)
**Issue:** (a) `string.Format("{0:D1}:{1:D2}", ...)` — the global csharp-quality rule prefers interpolation: `$"{t.Minutes:D1}:{t.Seconds:D2}"`. (b) `withBackground` is `true` at all three call sites — a dead parameter until a caller needs a bare label. (c) Test-pinning gap: the heuristic handles `{"width":0,"height":0}` (rejected by `width <= 0` before the equality check) and non-object `media_info` (e.g. array/string — rejected by `is JObject`), but neither case is pinned by `TelegramVideoNoteHeuristicTests`; two one-line tests would lock the guard order.
**Fix:** Interpolate the format string; optionally drop `withBackground` until needed; add `IsVideoNote_ZeroDims_False` and `IsVideoNote_NonObjectMediaInfo_False` cases.

---

_Reviewed: 2026-07-14T16:01:31Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
