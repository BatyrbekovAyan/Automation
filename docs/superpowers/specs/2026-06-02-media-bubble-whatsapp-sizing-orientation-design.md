# Media Bubble Sizing — WhatsApp Bounding-Box Fit + Video Orientation (Design)

**Date:** 2026-06-02
**Status:** Implemented & tuned on-device (2026-06-04). The sizing model below reflects the shipped `MediaBubbleSize` helper, not the original single-box proposal.
**Supersedes:** the 3-bucket sizing introduced in `docs/superpowers/specs/2026-05-20-whatsapp-message-bubble-sizing-design.md` (replaces `ResolveMediaSize`'s body; keeps its call plumbing).
**Related (orthogonal):** `docs/superpowers/specs/2026-06-02-incoming-video-thumbnails-design.md` — produces upright incoming thumbnails (`appliesPreferredTrackTransform`), consistent with the orientation principle here.

## Problem

Outgoing image and video bubbles are sized wrong. The headline case: a **portrait video lands in a horizontal or square bubble**. Two independent causes:

1. **Video orientation is dropped from the bubble aspect.** A phone portrait video is stored as a *landscape* frame plus a rotation flag (e.g. 1920×1080 + `rotation=90`). `ReadVideoMetadata` computes `aspect = props.width / props.height` (= 1.78, landscape) and returns `rotation` *separately* ([ChatManager.MediaSend.cs:427-429](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)). `vm.videoRotation` is then consumed **only** by the fullscreen `VideoController` ([MessageItemView.cs:2664-2735](../../../Assets/Scripts/UI/MessageItemView.cs)) — it is never folded into the bubble aspect. So `ResolveMediaSize` receives 1.78 and builds a wide, short bubble for a portrait clip. The cached thumbnail, by contrast, is **upright** (`NativeGallery.GetVideoThumbnail` returns a display-oriented frame; the preview screen sizes from `thumb.width/thumb.height` and renders it correctly — [AttachmentPreviewScreen.cs:153,204](../../../Assets/Scripts/Chat/AttachmentPreviewScreen.cs)). Bubble shape and thumbnail pixels therefore disagree, and `ApplyTextureAspectFill` center-crops a thin horizontal strip out of the upright portrait frame.

2. **The sizing model is bucketed, not continuous.** `ResolveMediaSize` ([MessageItemView.cs:152-182](../../../Assets/Scripts/UI/MessageItemView.cs)) picks one of three fixed widths by aspect band — landscape `810`, square `700`, portrait `648` — so the bubble width *jumps* at the `0.9` / `1.1` thresholds instead of tracking the media's true proportions. This is not how WhatsApp sizes media.

Images are already self-consistent: `vm.aspectRatio` comes from the decoded texture's own pixels and the cached JPEG has those same pixels ([ChatManager.MediaSend.cs:79-81,355](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)), so an image bubble never disagrees with its content. Images therefore need only the sizing-model fix (Part A), not an orientation fix.

## Goal

Match WhatsApp's media-bubble sizing and make every bubble's shape agree with the media it displays:

- Size image and video bubbles by **fitting the true aspect within orientation-aware max bounds** (wider for landscape, narrower for portrait), cropping only aspect extremes (Part A — applies to incoming *and* outgoing, the shared method).
- Make the outgoing **video** bubble aspect reflect the upright, displayed orientation (Part B).

## Non-Goals

- No change to fullscreen playback (`VideoController`) — see the safety analysis below.
- No separate incoming-video orientation fix *in this spec*. Incoming aspect comes from Wappi's server `width`/`height`; the Part A sizing change covers incoming bubbles. **(Later revisited:** Wappi frequently omits those dims → reloaded videos defaulted to a square `1.0` aspect, so incoming/reloaded video aspect is now derived from the extracted upright native thumbnail in `ChatManager.VideoThumbs.cs` — the same thumbnail-as-source-of-truth principle as Part B.)
- No image EXIF-rotation work. Not reported, and image bubbles already match their cached pixels. Out of scope.
- No new sticker / voice / audio / document sizing — those branches of `ResolveContentSize` are untouched.

## Decisions (locked)

| Decision | Choice | Rationale |
|---|---|---|
| Sizing model | Orientation-split max width + continuous aspect fit | WhatsApp shows landscape wider than portrait; removes the old 0.9/1.1 width jumps |
| Box dimensions | landscape width `810`, portrait/square width `700`; height = width / clamped aspect (portrait tops out ≈1000) | Tuned on-device vs WhatsApp — a single shared width made portrait too big *or* landscape too narrow |
| Aspect clamp | `0.70` (~7:10) … `1.78` (16:9) | Tall clips center-cropped at 0.70 (WhatsApp trims tall media); wide ones at 16:9. Tuned on-device |
| Video bubble aspect source | Upright thumbnail's `width/height`, fallback to rotation-corrected metadata | Ground truth = the pixels actually shown; mirrors how images work |
| `VideoController` | Unchanged | `apiAspectRatio` is only used when rotation is unknown; staged clips pass `videoRotation` and the value is ignored (proof below) |
| Scope | Incoming + outgoing for sizing; outgoing for video orientation | Per brainstorm; shared method keeps bubbles consistent |

## Part A — Bounding-Box Sizing

Rewrite the body of `ResolveMediaSize(float aspect)` ([MessageItemView.cs:152-182](../../../Assets/Scripts/UI/MessageItemView.cs)). The call plumbing is unchanged: the caller already clamps `realRatio` into `bubbleRatio` and passes that same value both into layout (via `SetupMaskedLayout` → `ResolveMediaSize`) and into `ApplyTextureAspectFill` as the crop target ([MessageItemView.cs:495-502](../../../Assets/Scripts/UI/MessageItemView.cs)), so bubble shape and crop target stay in lockstep.

The math lives in a pure, unit-tested helper `Assets/Scripts/Chat/MediaBubbleSize.cs`; `ResolveMediaSize` is a one-line delegator, and `MessageItemView`'s `MinAspectRatio`/`MaxAspectRatio` are aliases of the helper's clamp so the crop target stays in lockstep.

```csharp
public const float MaxWidthLandscape = 810f;  // aspect > 1  — landscape fills more width
public const float MaxWidthPortrait  = 700f;  // aspect <= 1 — narrower so tall portraits aren't overwhelming
public const float MinAspect = 0.70f;         // ~7:10 — taller is center-cropped
public const float MaxAspect = 1.78f;         // 16:9  — wider is center-cropped

public static Vector2 Resolve(float aspect)
{
    if (!float.IsFinite(aspect) || aspect <= 0f) aspect = 1f;
    aspect = Mathf.Clamp(aspect, MinAspect, MaxAspect);

    // Landscape fills the wider width; portrait/square the narrower one. Height follows
    // from the clamped aspect, so the clamp also bounds portrait tallness (700 / 0.70 ≈ 1000).
    float width = aspect > 1f ? MaxWidthLandscape : MaxWidthPortrait;
    return new Vector2(width, width / aspect);
}
```

**Retired** the old bucket constants (`ImageLandscapeWidth`, `ImagePortraitWidth`, `ImageSquareWidth`, `ImageMaxHeight`, `AspectLandscapeThreshold`, `AspectPortraitThreshold`); `ResolveMediaSize` now delegates to `MediaBubbleSize.Resolve`.

Resulting bubble dimensions (width × height, px in the 1080 reference canvas):

| Media | Aspect | Shipped (orientation-split) |
|---|---|---|
| 16:9 landscape | 1.78 | 810 × 455 |
| 4:3 landscape | 1.33 | 810 × 608 |
| 1:1 square | 1.00 | 700 × 700 |
| 4:5 portrait | 0.80 | 700 × 875 |
| 3:4 portrait | 0.75 | 700 × 933 |
| 9:16 portrait | 0.5625 → clamp 0.70 | 700 × 1000 + center-crop |
| panorama | 2.5 → 1.78 | 810 × 455 + side-crop |
| tall shot | 0.40 → 0.70 | 700 × 1000 + center-crop |

Landscape (aspect > 1) uses the wider `810`; portrait and square use the narrower `700`. Anything taller than `0.70` is clamped and center-cropped (WhatsApp trims tall media), so the tallest bubble is `700 × 1000`.

Because the bubble ratio now equals the media's true (clamped) ratio, `ApplyTextureAspectFill` takes its no-crop branch for all in-range media (`|imageRatio − targetRatio| ≤ 0.01`, [MessageItemView.cs:2607](../../../Assets/Scripts/UI/MessageItemView.cs)) and crops only the clamped extremes — exactly WhatsApp's "show it whole, crop only panoramas/very-tall" behavior.

## Part B — Video Bubble Aspect From the Upright Thumbnail

Make `vm.aspectRatio` for outgoing video equal the **displayed** aspect, sourced from the upright thumbnail that the bubble actually renders.

1. **`SeedVideoThumbCache`** ([ChatManager.MediaSend.cs:395-420](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)) already decodes the upright thumbnail `Texture2D`. Change its return to `(string syntheticUrl, float aspect)`, where `aspect = thumb.width / thumb.height` when the thumbnail decoded, else `0f` (unknown — e.g. Editor, or decode failure). All current early-return paths return `(syntheticUrl, 0f)`.

2. **`ReadVideoMetadata`** ([ChatManager.MediaSend.cs:422-432](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)) returns the **display** aspect as its fallback: compute `raw = props.width / props.height`; if `rotation == 90 || rotation == 270`, return `1f / raw` (i.e. `props.height / props.width`); otherwise `raw`. Duration and `rotation` are returned unchanged. This guarantees a correctly-oriented bubble even when no thumbnail exists.

3. **`GalleryVideo` case** ([ChatManager.MediaSend.cs:97-119](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)) prefers the thumbnail aspect, falling back to corrected metadata:

   ```csharp
   var (thumbUrl, thumbAspect) = SeedVideoThumbCache(stagedVideoPath, tempId);
   var meta = ReadVideoMetadata(stagedVideoPath);
   vm.thumbnailUrl  = thumbUrl;
   vm.videoUrl      = "file://" + stagedVideoPath;
   vm.aspectRatio   = thumbAspect > 0f ? thumbAspect : meta.aspect; // display-oriented either way
   vm.duration      = meta.durationSec;
   vm.videoRotation = meta.rotation;                                 // unchanged; for VideoController
   ```

`vm.aspectRatio` now means **display aspect** for video in all cases. After this change, the portrait-video bubble aspect comes from its upright thumbnail (≈ 0.56); Part A then clamps that to the 0.70 floor and `ApplyTextureAspectFill` center-crops to a `700 × 1000` bubble — WhatsApp's trim of very tall clips.

### Why `VideoController` needs no change

`PlayVideo(url, aspectRatio, videoRotation)` ([VideoController.cs:48-130](../../../Assets/Scripts/Chat/VideoController.cs)) sizes the surface from `vp.texture.width/height` (the raw decoded frame), not from `aspectRatio`. `apiAspectRatio` is read in exactly one place — the `videoRotation == 0` *heuristic* branch ([VideoController.cs:99](../../../Assets/Scripts/Chat/VideoController.cs)).

- Staged clips set `vm.videoRotation` (0/90/180/270). For 90/270 the `videoRotation != 0` branch runs and `apiAspectRatio` is **ignored** → changing it is irrelevant.
- For rotation 0/180 the display aspect **equals** the raw aspect (no width/height swap), so the value passed is unchanged.

Hence no double-correction and no edit to `VideoController`.

## Affected Files

- [Assets/Scripts/Chat/MediaBubbleSize.cs](../../../Assets/Scripts/Chat/MediaBubbleSize.cs) — new pure helper: `Resolve` (orientation-split sizing) + `OrientedAspect` (rotation correction). Unit-tested in `Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs`.
- [Assets/Scripts/UI/MessageItemView.cs](../../../Assets/Scripts/UI/MessageItemView.cs) — `ResolveMediaSize` delegates to `MediaBubbleSize.Resolve`; `MinAspectRatio`/`MaxAspectRatio` alias the helper's clamp; the old bucket/threshold constants deleted.
- [Assets/Scripts/Main/ChatManager.MediaSend.cs](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs) — `SeedVideoThumbCache` returns `(url, aspect)`; `ReadVideoMetadata` returns rotation-corrected display aspect; `GalleryVideo` case wires thumbnail-preferred aspect.
- [Assets/Scripts/Main/ChatManager.VideoThumbs.cs](../../../Assets/Scripts/Main/ChatManager.VideoThumbs.cs) — incoming/reloaded video aspect derived from the extracted upright thumbnail (see the revisited Non-Goal above).

No prefab, scene, or `VideoController` changes.

## Edge Cases & Risks

- **Editor / no native thumbnail.** `NativeGallery.GetVideoThumbnail` returns null in-Editor → `thumbAspect == 0` → falls back to `meta.aspect` (rotation-corrected). `GetVideoProperties` is also native, so in pure Editor both may be unavailable; the existing `catch` returns `(1.0f, 0, 0f)` → square bubble. Acceptable; orientation is only verifiable on-device.
- **Thumbnail decode succeeds but `height == 0`.** Guard: treat as unknown (`0f`) and fall back to metadata.
- **180° rotation.** No width/height swap; display aspect == raw aspect. Handled (no inversion).
- **Square-ish video (aspect ≈ 1).** Inversion is a no-op; square uses the portrait width → bubble ≈ 700×700. Fine.
- **3:4 portrait → 700×933.** Tuned down on-device from the original 810×1080 proposal, which read too large next to WhatsApp.

## Testing & Verification

- **EditMode unit tests** (`Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs`): assert `Resolve` for landscape (16:9, 4:3), square, portrait (3:4, the 0.70 cap, 9:16-clamped), an out-of-range panorama (clamped to 1.78), and an out-of-range tall shot (clamped to 0.70); plus `OrientedAspect` for 0/90/180/270°. The "small static helper" route was taken — `MediaBubbleSize` is a plain `public static` class, so no `InternalsVisibleTo` was needed.
- **On-device visual check (done):** portrait video → tall (`700 × 1000`), center-cropped at 0.70, upright thumbnail; landscape → wide (`810`); portrait photo → `700`-wide; all verified against WhatsApp. Fullscreen playback orientation unchanged.

## Open Questions

None.
