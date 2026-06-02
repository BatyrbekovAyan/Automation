# Media Bubble Sizing — WhatsApp Bounding-Box Fit + Video Orientation (Design)

**Date:** 2026-06-02
**Status:** Approved (brainstorm) — ready for implementation plan
**Supersedes:** the 3-bucket sizing introduced in `docs/superpowers/specs/2026-05-20-whatsapp-message-bubble-sizing-design.md` (replaces `ResolveMediaSize`'s body; keeps its call plumbing).
**Related (orthogonal):** `docs/superpowers/specs/2026-06-02-incoming-video-thumbnails-design.md` — produces upright incoming thumbnails (`appliesPreferredTrackTransform`), consistent with the orientation principle here.

## Problem

Outgoing image and video bubbles are sized wrong. The headline case: a **portrait video lands in a horizontal or square bubble**. Two independent causes:

1. **Video orientation is dropped from the bubble aspect.** A phone portrait video is stored as a *landscape* frame plus a rotation flag (e.g. 1920×1080 + `rotation=90`). `ReadVideoMetadata` computes `aspect = props.width / props.height` (= 1.78, landscape) and returns `rotation` *separately* ([ChatManager.MediaSend.cs:427-429](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)). `vm.videoRotation` is then consumed **only** by the fullscreen `VideoController` ([MessageItemView.cs:2664-2735](../../../Assets/Scripts/UI/MessageItemView.cs)) — it is never folded into the bubble aspect. So `ResolveMediaSize` receives 1.78 and builds a wide, short bubble for a portrait clip. The cached thumbnail, by contrast, is **upright** (`NativeGallery.GetVideoThumbnail` returns a display-oriented frame; the preview screen sizes from `thumb.width/thumb.height` and renders it correctly — [AttachmentPreviewScreen.cs:153,204](../../../Assets/Scripts/Chat/AttachmentPreviewScreen.cs)). Bubble shape and thumbnail pixels therefore disagree, and `ApplyTextureAspectFill` center-crops a thin horizontal strip out of the upright portrait frame.

2. **The sizing model is bucketed, not continuous.** `ResolveMediaSize` ([MessageItemView.cs:152-182](../../../Assets/Scripts/UI/MessageItemView.cs)) picks one of three fixed widths by aspect band — landscape `810`, square `700`, portrait `648` — so the bubble width *jumps* at the `0.9` / `1.1` thresholds instead of tracking the media's true proportions. This is not how WhatsApp sizes media.

Images are already self-consistent: `vm.aspectRatio` comes from the decoded texture's own pixels and the cached JPEG has those same pixels ([ChatManager.MediaSend.cs:79-81,355](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)), so an image bubble never disagrees with its content. Images therefore need only the sizing-model fix (Part A), not an orientation fix.

## Goal

Match WhatsApp's media-bubble sizing and make every bubble's shape agree with the media it displays:

- Size image and video bubbles by **fitting the true aspect into one bounding box**, cropping only aspect extremes (Part A — applies to incoming *and* outgoing, the shared method).
- Make the outgoing **video** bubble aspect reflect the upright, displayed orientation (Part B).

## Non-Goals

- No change to fullscreen playback (`VideoController`) — see the safety analysis below.
- No separate incoming-video orientation fix. Incoming aspect comes from Wappi's server `width`/`height` (assumed upright, as WhatsApp's API and its `JPEGThumbnail` are); the Part A sizing change covers incoming bubbles. Revisit only if an incoming portrait video is observed rendering landscape.
- No image EXIF-rotation work. Not reported, and image bubbles already match their cached pixels. Out of scope.
- No new sticker / voice / audio / document sizing — those branches of `ResolveContentSize` are untouched.

## Decisions (locked)

| Decision | Choice | Rationale |
|---|---|---|
| Sizing model | Single bounding-box fit (`MaxMediaWidth` × `MaxMediaHeight`), continuous | WhatsApp's behavior; removes the 0.9/1.1 width jumps |
| Box dimensions | `MaxMediaWidth = 810`, `MaxMediaHeight = 1080` | Reuse current magnitudes so only the *curve* changes; user-tunable in Editor |
| Aspect clamp | `0.56` (9:16) … `1.78` (16:9) | Unchanged; extremes are center-cropped by the existing `ApplyTextureAspectFill` |
| Video bubble aspect source | Upright thumbnail's `width/height`, fallback to rotation-corrected metadata | Ground truth = the pixels actually shown; mirrors how images work |
| `VideoController` | Unchanged | `apiAspectRatio` is only used when rotation is unknown; staged clips pass `videoRotation` and the value is ignored (proof below) |
| Scope | Incoming + outgoing for sizing; outgoing for video orientation | Per brainstorm; shared method keeps bubbles consistent |

## Part A — Bounding-Box Sizing

Rewrite the body of `ResolveMediaSize(float aspect)` ([MessageItemView.cs:152-182](../../../Assets/Scripts/UI/MessageItemView.cs)). The call plumbing is unchanged: the caller already clamps `realRatio` into `bubbleRatio` and passes that same value both into layout (via `SetupMaskedLayout` → `ResolveMediaSize`) and into `ApplyTextureAspectFill` as the crop target ([MessageItemView.cs:495-502](../../../Assets/Scripts/UI/MessageItemView.cs)), so bubble shape and crop target stay in lockstep.

```csharp
private const float MaxMediaWidth  = 810f;   // box width  (~0.75 × 1080 ref canvas)
private const float MaxMediaHeight = 1080f;  // box height (portrait cap)
private const float MinAspectRatio = 0.56f;  // 9:16  — taller is center-cropped
private const float MaxAspectRatio = 1.78f;  // 16:9  — wider is center-cropped

private Vector2 ResolveMediaSize(float aspect)
{
    if (!float.IsFinite(aspect) || aspect <= 0f) aspect = 1f;
    aspect = Mathf.Clamp(aspect, MinAspectRatio, MaxAspectRatio);

    float width  = MaxMediaWidth;
    float height = width / aspect;

    if (height > MaxMediaHeight)        // portrait taller than the box → height-bound
    {
        height = MaxMediaHeight;
        width  = height * aspect;       // narrower, taller bubble
    }

    return new Vector2(width, height);
}
```

**Retire** the now-unused constants `ImageLandscapeWidth`, `ImagePortraitWidth`, `ImageSquareWidth`, `AspectLandscapeThreshold`, `AspectPortraitThreshold` (`ImageMaxHeight` is renamed to / replaced by `MaxMediaHeight`).

Resulting bubble dimensions (width × height, px in the 1080 reference canvas):

| Media | Aspect | Old (3-bucket) | New (box fit) |
|---|---|---|---|
| 16:9 landscape | 1.78 | 810 × 455 | 810 × 455 (same) |
| 4:3 landscape | 1.33 | 810 × 608 | 810 × 608 (same) |
| 1:1 square | 1.00 | 700 × 700 | **810 × 810** |
| 4:5 portrait | 0.80 | 648 × 810 | **810 × 1013** |
| 3:4 portrait | 0.75 | 648 × 864 | **810 × 1080** |
| 9:16 portrait | 0.5625 | 608 × 1080 | 608 × 1080 (same) |
| panorama | 2.5 → 1.78 | 810 × 455 + side-crop | 810 × 455 + side-crop |
| tall shot | 0.40 → 0.56 | 605 × 1080 + crop | 605 × 1080 + crop |

The two models **coincide** at the ends — all landscape (width is already `MaxMediaWidth`) and extreme portrait (the `1080` height cap binds in both). They differ in the **middle band (≈0.6–1.1)**, where the old buckets snapped to `700`/`648` and the new fit lets the bubble grow to the box: square and mild-portrait media become larger and track their true proportion smoothly instead of jumping at `0.9`/`1.1`.

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

`vm.aspectRatio` now means **display aspect** for video in all cases. After this change, the portrait-video bubble ratio (≈ 0.56) matches its upright thumbnail (≈ 0.56), so `ApplyTextureAspectFill` does not crop, and Part A sizes it `608 × 1080`.

### Why `VideoController` needs no change

`PlayVideo(url, aspectRatio, videoRotation)` ([VideoController.cs:48-130](../../../Assets/Scripts/Chat/VideoController.cs)) sizes the surface from `vp.texture.width/height` (the raw decoded frame), not from `aspectRatio`. `apiAspectRatio` is read in exactly one place — the `videoRotation == 0` *heuristic* branch ([VideoController.cs:99](../../../Assets/Scripts/Chat/VideoController.cs)).

- Staged clips set `vm.videoRotation` (0/90/180/270). For 90/270 the `videoRotation != 0` branch runs and `apiAspectRatio` is **ignored** → changing it is irrelevant.
- For rotation 0/180 the display aspect **equals** the raw aspect (no width/height swap), so the value passed is unchanged.

Hence no double-correction and no edit to `VideoController`.

## Affected Files

- [Assets/Scripts/UI/MessageItemView.cs](../../../Assets/Scripts/UI/MessageItemView.cs) — rewrite `ResolveMediaSize`; add `MaxMediaWidth`/`MaxMediaHeight`; delete the five retired bucket/threshold constants.
- [Assets/Scripts/Main/ChatManager.MediaSend.cs](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs) — `SeedVideoThumbCache` returns `(url, aspect)`; `ReadVideoMetadata` returns rotation-corrected display aspect; `GalleryVideo` case wires thumbnail-preferred aspect.

No prefab, scene, or `VideoController` changes.

## Edge Cases & Risks

- **Editor / no native thumbnail.** `NativeGallery.GetVideoThumbnail` returns null in-Editor → `thumbAspect == 0` → falls back to `meta.aspect` (rotation-corrected). `GetVideoProperties` is also native, so in pure Editor both may be unavailable; the existing `catch` returns `(1.0f, 0, 0f)` → square bubble. Acceptable; orientation is only verifiable on-device.
- **Thumbnail decode succeeds but `height == 0`.** Guard: treat as unknown (`0f`) and fall back to metadata.
- **180° rotation.** No width/height swap; display aspect == raw aspect. Handled (no inversion).
- **Square-ish video (aspect ≈ 1).** Inversion is a no-op; bubble ≈ 810×810. Fine.
- **3:4 portrait now fills the full 810×1080 box.** Larger than the old 648×864. Intended by the chosen box; tune `MaxMediaWidth`/`MaxMediaHeight` in-Editor if it reads too large.

## Testing & Verification

- **EditMode unit test** for the pure function: assert `ResolveMediaSize` output for 16:9, 4:3, 1:1, 3:4, 9:16, an out-of-range panorama (clamped to 1.78), and an out-of-range tall shot (clamped to 0.56). This is the project's standard EditMode pattern (`Assets/Tests/Editor/Chat/`); `ResolveMediaSize` may need to be made internally testable (e.g. `internal` + `InternalsVisibleTo`, or a small static helper) — decide during planning.
- **On-device visual check (required, can't be done in Editor):** send a portrait phone video → bubble is tall (≈608×1080), thumbnail upright, not a cropped horizontal strip. Repeat for a landscape clip, a portrait photo, a landscape photo, and a near-square image. Confirm fullscreen playback orientation is unchanged for each.

## Open Questions

None.
