# WhatsApp-Style Message Bubble Sizing

**Date**: 2026-05-20
**Status**: Approved, awaiting implementation plan
**Scope**: `Assets/Scripts/UI/MessageItemView.cs`, `Assets/Prefabs/MessageTextIncoming.prefab`, `Assets/Prefabs/MessageTextOutgoing.prefab`

## 1. Problem

`MessageItemView.cs` currently sizes every media message type (Image, Video, Document) at a single hardcoded width — `fixedWidth = 464f`. This is:

- Too narrow for images and videos (WhatsApp uses ~75% of screen width for landscape media; 464 on a 1080 canvas is only ~43%).
- Wrong shape for fixed-format types (voice notes, stickers, audio files, documents each have their own native width in WhatsApp).
- Inconsistent with how WhatsApp actually handles message sizing — which is per-type and aspect-driven, not a one-size-fits-all clamp.

Additionally:

- `maxBubbleWidthPercentage = 0.7` (756px text wrap) is slightly tighter than WhatsApp's ~75%.
- Aspect clamp `0.5–2.0` is looser than WhatsApp's effective `9:16–16:9`, letting extreme shapes through.
- Bubble padding ships as `4/4/4/4` in the prefabs but is overridden to `8/8/8/12` at runtime — the prefab default never actually applies.
- Link preview width is `canvas × 0.55` (absolute, 594px) when it should be relative to the bubble width.

## 2. Goal

Replace the single-fixed-width media model with per-type sizing constants and a single resolver that returns the correct content size for any `MessageType` + aspect ratio. Numbers target WhatsApp parity on a 1080×1920 reference canvas, expressed as percentages of canvas width so they scale on other devices through `CanvasScaler`.

## 3. Scope

**In scope**

- Sizing constants for all 7 message types (Chat, Image, Video, Audio, Voice, Sticker, Document)
- `ResolveContentSize(MessageType, aspect)` resolver method centralising the math
- Aspect-driven image/video sizing with landscape / portrait / square branches
- Caption width follows media width (`mediaWidth - 32`)
- Link preview width relative to bubble width (`bubbleWidth × 0.65`)
- Aspect clamp tightening (`0.56–1.78` = 9:16 to 16:9)
- Prefab padding synced to runtime value (`8/8/8/12`) so defaults match what renders

**Out of scope**

- Timestamp / status-icon positioning logic (already works, untouched)
- Bubble background, corners, colors
- Media download placeholder card behavior
- Reply / quote header layout
- Sticker timestamp inset (`bottomPad = 54`) — existing branch stays as-is
- Prefab restructuring beyond padding
- Automated UI tests — codebase has none for layout; out of scope to bootstrap

## 4. Sizing Constants

All values target the 1080-wide canvas. Declared as `private const float` (or `int` for padding) at the top of `MessageItemView.cs`.

```csharp
// === Bubble container ===
private const float MAX_BUBBLE_WIDTH        = 810f;   // 0.75 × canvas — text + caption ceiling
private const float MIN_BUBBLE_WIDTH        =  90f;   // timestamp + tick — set on text-bubble LayoutElement.minWidth so very short messages (e.g. "ok") still fit an inline timestamp

// === Text bubble padding (LRTB) ===
private const int   BUBBLE_PAD_L            = 8;
private const int   BUBBLE_PAD_R            = 8;
private const int   BUBBLE_PAD_T            = 8;
private const int   BUBBLE_PAD_B            = 12;

// === Image / Video ===
private const float IMAGE_LANDSCAPE_WIDTH   = 810f;   // 0.75 × canvas
private const float IMAGE_PORTRAIT_WIDTH    = 648f;   // 0.60 × canvas
private const float IMAGE_SQUARE_WIDTH      = 700f;   // 0.65 × canvas
private const float IMAGE_MAX_HEIGHT        = 1080f;  // tall-portrait clamp
private const float MIN_ASPECT              = 0.56f;  // 9:16
private const float MAX_ASPECT              = 1.78f;  // 16:9
private const float ASPECT_LANDSCAPE_THRESH = 1.1f;   // > → landscape
private const float ASPECT_PORTRAIT_THRESH  = 0.9f;   // < → portrait

// === Voice / Audio ===
private const float VOICE_WIDTH             = 720f;   // 0.67 × canvas
private const float VOICE_HEIGHT            = 150f;
private const float AUDIO_FILE_WIDTH        = 760f;   // 0.70 × canvas
private const float AUDIO_FILE_HEIGHT       = 190f;

// === Sticker (no bubble bg) ===
private const float STICKER_WIDTH           = 432f;   // 0.40 × canvas
private const float STICKER_HEIGHT          = 432f;

// === Document ===
private const float DOC_WIDTH               = 760f;   // 0.70 × canvas
private const float DOC_MIN_WIDTH           = 480f;
private const float DOC_HEIGHT              = 200f;

// === Caption + link preview ===
private const float CAPTION_INSET           = 32f;    // captionWidth = mediaWidth - inset
private const float LINKPREVIEW_RATIO       = 0.65f;  // × bubbleWidth
```

### Replaces

| Existing value                                  | New constant                     |
|-------------------------------------------------|----------------------------------|
| `maxBubbleWidthPercentage = 0.7f`               | `MAX_BUBBLE_WIDTH = 810f`        |
| `fixedWidth = 464f` (image / video / document)  | per-type widths above            |
| `MaxAspectRatio = 2.0f`                         | `MAX_ASPECT = 1.78f`             |
| `MinAspectRatio = 0.5f`                         | `MIN_ASPECT = 0.56f`             |
| `documentMinWidth = 240f`                       | `DOC_MIN_WIDTH = 480f`           |
| `containerWidth × 0.55f` (link preview)         | `bubbleWidth × LINKPREVIEW_RATIO` |
| Caption clamp `fixedWidth - 16`                 | `mediaWidth - CAPTION_INSET`     |

## 5. Resolver

A single method returns the bubble content size for any message type. All call sites route through this.

```csharp
/// <summary>
/// Resolves the bubble content size for any message type.
/// For text/caption-only bubbles, returns (MAX_BUBBLE_WIDTH, 0) — height is text-driven.
/// For media, returns final width and height after aspect/clamp logic.
/// </summary>
private Vector2 ResolveContentSize(MessageType type, float aspect)
{
    switch (type)
    {
        case MessageType.Image:
        case MessageType.Video:
            return ResolveMediaSize(aspect);

        case MessageType.Sticker:
            return new Vector2(STICKER_WIDTH, STICKER_HEIGHT);

        case MessageType.Voice:
            return new Vector2(VOICE_WIDTH, VOICE_HEIGHT);

        case MessageType.Audio:
            return new Vector2(AUDIO_FILE_WIDTH, AUDIO_FILE_HEIGHT);

        case MessageType.Document:
            return new Vector2(DOC_WIDTH, DOC_HEIGHT);

        default: // Chat, Unknown
            return new Vector2(MAX_BUBBLE_WIDTH, 0f);
    }
}

private Vector2 ResolveMediaSize(float aspect)
{
    aspect = Mathf.Clamp(aspect, MIN_ASPECT, MAX_ASPECT);

    float width, height;

    if (aspect >= ASPECT_LANDSCAPE_THRESH)
    {
        width  = IMAGE_LANDSCAPE_WIDTH;
        height = width / aspect;
    }
    else if (aspect <= ASPECT_PORTRAIT_THRESH)
    {
        width  = IMAGE_PORTRAIT_WIDTH;
        height = width / aspect;

        if (height > IMAGE_MAX_HEIGHT)
        {
            height = IMAGE_MAX_HEIGHT;
            width  = height * aspect;
        }
    }
    else
    {
        width  = IMAGE_SQUARE_WIDTH;
        height = width / aspect;
    }

    return new Vector2(width, height);
}
```

### Call sites to route through the resolver

1. **`DisplayMedia()` aspect block (around line 1240)** — read width/height from `ResolveMediaSize(bubbleRatio)` instead of `fixedWidth`.
2. **`ApplyDynamicLayout(MessageType)` (lines 557, 664, 703)** — set `LayoutElement.preferredWidth/Height` from `ResolveContentSize(type, aspect)`.
3. **Text max-width block (around line 855)** — use `MAX_BUBBLE_WIDTH` directly instead of `containerWidth × maxBubbleWidthPercentage`.
4. **Caption clamp (around line 951)** — `mediaWidth - CAPTION_INSET`, where `mediaWidth` comes from the resolver call earlier in the frame.
5. **Link preview block** — `bubbleWidth × LINKPREVIEW_RATIO` instead of `containerWidth × 0.55f`.
6. **Text bubble `LayoutElement.minWidth`** (in `ApplyDynamicLayout` for `Chat` case) — set to `MIN_BUBBLE_WIDTH` so very short messages still reserve room for an inline timestamp.

## 6. Sticker Special Cases (Preserved)

Stickers keep the three branches already in code, but route their size through `ResolveContentSize(Sticker)`:

1. `bubbleImage.enabled = false` — already done around lines 363 / 718.
2. Fixed `432×432` — overrides aspect, `PreserveAspect = true` on the sticker image.
3. `bottomPad = 54` and `timeBottomInset = 10f` for the timestamp — already in code around lines 741 / 745. **Untouched.**

## 7. Prefab Padding Sync

Both `MessageTextIncoming.prefab` and `MessageTextOutgoing.prefab` ship with bubble `VerticalLayoutGroup` padding `4/4/4/4`. The runtime reset path overrides this to `8/8/8/12`. Update both prefabs so their default padding is `8/8/8/12` — the prefab default never actually rendered before this change, so this is a cosmetic / consistency fix only.

## 8. Migration Notes

**Behaviour changes after landing**:

- Image / video bubbles get **wider** (464 → 648–810).
- Sticker bubbles get **slightly smaller** (464 → 432).
- Voice / audio / document bubbles get **wider** (464 → 720 / 760).
- Text bubbles get **slightly wider** (756 → 810 max).
- Very tall portraits get **slightly tighter cropping** (floor 0.5 → 0.56, ~3% on long edge).
- Very wide panoramas get **harder cropping** (ceiling 2.0 → 1.78, ~12% on long edge).

**No data migration** — all sizing computed at render time. No serialised state changes.

**Rollback** — revert the commit. No persistent state to undo.

## 9. Testing

Manual visual smoke test in Unity Game view at 1080×2400, against a chat with a mix of types:

- Each message type renders at the new width
- Landscape photo → 810 wide
- Portrait photo → 648 wide
- Square photo → 700 wide
- Very tall portrait (>1:1.78) → clamped to 9:16
- Very wide pano (>1.78:1) → clamped to 16:9
- Sticker → no bubble bg, 432×432, timestamp floats below
- Voice note → 720×150
- Document with short and long filename → 760 wide, filename truncates
- Text with link preview → preview is 65% of bubble width
- Long text + image caption → caption width = image width − 32

No automated UI tests — codebase has none for layout, bootstrapping a Unity test rig is out of scope.

## 10. Files Affected

- `Assets/Scripts/UI/MessageItemView.cs` — constants block, `ResolveContentSize`, `ResolveMediaSize`, route ~5 call sites through resolver
- `Assets/Prefabs/MessageTextIncoming.prefab` — bubble padding `4/4/4/4` → `8/8/8/12`
- `Assets/Prefabs/MessageTextOutgoing.prefab` — bubble padding `4/4/4/4` → `8/8/8/12`
