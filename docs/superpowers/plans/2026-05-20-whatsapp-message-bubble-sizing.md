# WhatsApp Message Bubble Sizing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single `fixedWidth = 464f` for all media with per-type sizing constants and a `ResolveContentSize()` resolver, plus tighten aspect clamping, normalise padding, and switch link preview / caption widths to be relative.

**Architecture:** Add a constants block + two resolver methods (`ResolveContentSize`, `ResolveMediaSize`) at the top of `MessageItemView.cs`. Migrate each call site one task at a time, leaving the file in a compilable state between commits. Drop the legacy `fixedWidth` and `maxBubbleWidthPercentage` fields in the final cleanup task. Sync both message prefabs' default padding to the runtime value.

**Tech Stack:** Unity 6 (6000.3.9f1) C#, TextMeshPro, Unity UI (LayoutGroup / LayoutElement / ContentSizeFitter), single-canvas mobile UI at 1080×1920 reference.

**Constant naming:** Project convention is PascalCase for `const` (existing code uses `MinAspectRatio`, `MaxAspectRatio`). New constants follow this convention (e.g., `MaxBubbleWidth`, `ImageLandscapeWidth`), not SCREAMING_SNAKE_CASE.

**Spec:** `docs/superpowers/specs/2026-05-20-whatsapp-message-bubble-sizing-design.md`

---

## Task 1: Add Sizing Constants and Resolver Methods

Lay the foundation. Add new constants and the resolver methods. Leave the legacy `fixedWidth` and `maxBubbleWidthPercentage` fields in place — later tasks migrate call sites, and the final task removes them. The only behaviour change this task introduces is the tightened aspect clamp (`MinAspectRatio`/`MaxAspectRatio` keep their names but pick up new values `0.56` / `1.78`), which existing call sites at lines 350, 1243, 1337 inherit automatically.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (around line 74, after the existing `MinAspectRatio`/`MaxAspectRatio` declarations)

- [ ] **Step 1: Replace the existing aspect ratio constants with the full sizing constants block**

Open `Assets/Scripts/UI/MessageItemView.cs`. Find this block (around line 74):

```csharp
    private const float MinAspectRatio = 0.5f; 
    private const float MaxAspectRatio = 2.0f; 
```

Replace it with this expanded block:

```csharp
    // === Bubble container ===
    private const float MaxBubbleWidth        = 810f;   // 0.75 × 1080 canvas — text + caption ceiling
    private const float MinBubbleWidth        =  90f;   // Set on text-bubble LayoutElement.minWidth so very short messages (e.g. "ok") still fit an inline timestamp

    // === Bubble reset padding (LRTB) — used by ResetBubbleLayoutToDefault and synced into both prefabs ===
    private const int   BubblePadLeft         = 8;
    private const int   BubblePadRight        = 8;
    private const int   BubblePadTop          = 8;
    private const int   BubblePadBottom       = 12;

    // === Image / Video ===
    private const float ImageLandscapeWidth   = 810f;   // 0.75 × canvas
    private const float ImagePortraitWidth    = 648f;   // 0.60 × canvas
    private const float ImageSquareWidth      = 700f;   // 0.65 × canvas
    private const float ImageMaxHeight        = 1080f;  // tall-portrait clamp
    private const float MinAspectRatio        = 0.56f;  // 9:16
    private const float MaxAspectRatio        = 1.78f;  // 16:9
    private const float AspectLandscapeThreshold = 1.1f; // > → landscape
    private const float AspectPortraitThreshold  = 0.9f; // < → portrait

    // === Voice / Audio ===
    private const float VoiceWidth            = 720f;   // 0.67 × canvas
    private const float VoiceHeight           = 150f;
    private const float AudioFileWidth        = 760f;   // 0.70 × canvas
    private const float AudioFileHeight       = 190f;

    // === Sticker (no bubble bg) ===
    private const float StickerWidth          = 432f;   // 0.40 × canvas
    private const float StickerHeight         = 432f;

    // === Document ===
    private const float DocumentWidth         = 760f;   // 0.70 × canvas
    private const float DocumentMinWidth      = 480f;
    private const float DocumentHeight        = 200f;

    // === Caption + link preview ===
    private const float CaptionInset          = 32f;    // captionWidth = mediaWidth - inset
    private const float LinkPreviewRatio      = 0.65f;  // × bubbleWidth
```

Note: `MinAspectRatio` and `MaxAspectRatio` keep their names (so existing call sites at lines 350, 1243, 1337 still compile) but get new values (`0.56` and `1.78`). The behavior change for these is part of this task — existing call sites pick up the new clamp automatically.

- [ ] **Step 2: Add the resolver methods**

Find a good location for new private methods — directly after the constants block is fine, before the `[SerializeField] private MessageViewModel currentVm;` line. Add:

```csharp
    /// <summary>
    /// Resolves the bubble content size for any message type.
    /// For text/caption-only bubbles, returns (MaxBubbleWidth, 0) — height is text-driven.
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
                return new Vector2(StickerWidth, StickerHeight);

            case MessageType.Voice:
                return new Vector2(VoiceWidth, VoiceHeight);

            case MessageType.Audio:
                return new Vector2(AudioFileWidth, AudioFileHeight);

            case MessageType.Document:
                return new Vector2(DocumentWidth, DocumentHeight);

            default: // Chat, Unknown
                return new Vector2(MaxBubbleWidth, 0f);
        }
    }

    private Vector2 ResolveMediaSize(float aspect)
    {
        aspect = Mathf.Clamp(aspect, MinAspectRatio, MaxAspectRatio);

        float width, height;

        if (aspect >= AspectLandscapeThreshold)
        {
            width  = ImageLandscapeWidth;
            height = width / aspect;
        }
        else if (aspect <= AspectPortraitThreshold)
        {
            width  = ImagePortraitWidth;
            height = width / aspect;

            if (height > ImageMaxHeight)
            {
                height = ImageMaxHeight;
                width  = height * aspect;
            }
        }
        else
        {
            width  = ImageSquareWidth;
            height = width / aspect;
        }

        return new Vector2(width, height);
    }
```

- [ ] **Step 3: Verify Unity recompile is clean**

Switch focus to the Unity Editor. Wait for the auto-recompile (bottom-right spinner). Open the Console window — expect zero errors, zero warnings related to the changes. The existing `fixedWidth` and `maxBubbleWidthPercentage` still exist, so all current behavior is preserved.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): add sizing constants and ResolveContentSize resolver

Foundation for WhatsApp-style per-type message sizing. Adds the full
constants block (MaxBubbleWidth, per-type widths, aspect thresholds)
and the ResolveContentSize / ResolveMediaSize methods. Existing
fixedWidth and maxBubbleWidthPercentage fields stay until call sites
are migrated in subsequent commits. MinAspectRatio and MaxAspectRatio
keep their names but get tightened to 0.56 / 1.78.

Spec: docs/superpowers/specs/2026-05-20-whatsapp-message-bubble-sizing-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Route Image/Video/Sticker Sizing Through Resolver

Replace the `fixedWidth`-based sizing in `SetupMaskedLayout` with the aspect-driven `ResolveMediaSize` result. Stickers get their fixed `StickerWidth × StickerHeight` from `ResolveContentSize(Sticker, _)` because their size is type-driven, not aspect-driven.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (lines 1140–1144 in `SetupMaskedLayout`)

- [ ] **Step 1: Locate the existing width-setting block**

In `SetupMaskedLayout`, find this block (around line 1140):

```csharp
        var contLayout = containerTr.GetComponent<LayoutElement>();
        if (!contLayout) contLayout = containerTr.gameObject.AddComponent<LayoutElement>();
        contLayout.ignoreLayout = false;
        contLayout.preferredWidth = fixedWidth;
        contLayout.preferredHeight = fixedWidth / bubbleRatio;
```

- [ ] **Step 2: Replace with resolver-driven sizing**

```csharp
        var contLayout = containerTr.GetComponent<LayoutElement>();
        if (!contLayout) contLayout = containerTr.gameObject.AddComponent<LayoutElement>();
        contLayout.ignoreLayout = false;

        Vector2 mediaSize = isSticker
            ? new Vector2(StickerWidth, StickerHeight)
            : ResolveMediaSize(bubbleRatio);
        contLayout.preferredWidth  = mediaSize.x;
        contLayout.preferredHeight = mediaSize.y;
```

Note: `isSticker` is already a parameter of `SetupMaskedLayout`. We branch on it because sticker size is fixed and ignores `bubbleRatio`.

- [ ] **Step 3: Verify Unity recompile is clean**

Switch to Unity Editor, wait for recompile, check Console — expect zero new errors.

- [ ] **Step 4: Manual visual smoke test**

Open the Main scene, hit Play, navigate into a chat with image messages. Verify:
- Landscape photos render wider than before (~810 vs old ~464)
- Portrait photos are ~648 wide (slightly larger than before)
- Stickers render at 432×432 (smaller than before, still with no bubble bg)
- Aspect-extreme images are tighter (no panoramas wider than 16:9)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): route image/video/sticker sizing through resolver

SetupMaskedLayout now uses ResolveMediaSize for image/video (aspect-
driven) and StickerWidth/Height for stickers (fixed). Replaces the
single 464px fixedWidth used for all three previously. Landscape
images now render at 810px, portrait at 648px, square at 700px,
stickers at 432×432.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Size Voice and Audio Panels

`HandleAudioMedia` enables the audio panel but never sets its preferred width — voice/audio currently use whatever the prefab has serialised on `audioPanel`'s LayoutElement. Add explicit sizing in `ApplyDynamicLayout` so the `Voice` vs `Audio` distinction maps to the spec values.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (the `else if (type == MessageType.Audio || type == MessageType.Voice)` branch in `ApplyDynamicLayout`, around lines 664–702)

- [ ] **Step 1: Locate the audio/voice branch in ApplyDynamicLayout**

Find this branch (around line 664):

```csharp
        else if (type == MessageType.Audio || type == MessageType.Voice)
        {
            bool isDownloadActive = downloadButton != null && downloadButton.gameObject.activeSelf;
            bool isExpiredActive = expiredPlaceholder != null && expiredPlaceholder.activeSelf;
            bool useCardLayout = isDownloadActive || isExpiredActive;
    
            bool hasSenderName = senderNameText != null && senderNameText.gameObject.activeSelf;
            // ... padding/spacing block unchanged ...
        }
```

- [ ] **Step 2: Add audioPanel LayoutElement sizing at the top of the branch**

Insert the sizing block right after `bool useCardLayout = ...;` and before `bool hasSenderName = ...;`:

```csharp
        else if (type == MessageType.Audio || type == MessageType.Voice)
        {
            bool isDownloadActive = downloadButton != null && downloadButton.gameObject.activeSelf;
            bool isExpiredActive = expiredPlaceholder != null && expiredPlaceholder.activeSelf;
            bool useCardLayout = isDownloadActive || isExpiredActive;

            // Only size the audio panel when it's the active visual (not the download/expired card).
            if (!useCardLayout && audioPanel != null && audioPanel.activeSelf)
            {
                var audioLe = audioPanel.GetComponent<LayoutElement>();
                if (audioLe == null) audioLe = audioPanel.AddComponent<LayoutElement>();

                Vector2 size = ResolveContentSize(type, 1f);
                audioLe.preferredWidth  = size.x;
                audioLe.preferredHeight = size.y;
            }

            bool hasSenderName = senderNameText != null && senderNameText.gameObject.activeSelf;
            // ... rest of the branch unchanged ...
        }
```

(Leave the existing padding/spacing logic below this insertion untouched.)

- [ ] **Step 3: Verify Unity recompile is clean**

Switch to Unity Editor, check Console for errors.

- [ ] **Step 4: Manual visual smoke test**

Play scene, open a chat with a voice note. Verify:
- Voice note bubble is now 720×150 (wider than before)
- An audio file (non-voice MIME) shows at 760×190
- Download placeholder for an unloaded voice note still uses the existing card layout (unchanged)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): size voice/audio panels via ResolveContentSize

ApplyDynamicLayout now explicitly sets audioPanel LayoutElement
preferredWidth/Height for Voice (720×150) and Audio (760×190),
overriding whatever the prefab ships with. Only applied when the
audio panel is active — download/expired card states keep their
existing layout.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Cap Document Width with DocumentWidth and DocumentMinWidth

The document branch currently grows the card to `maxTextWidth + 132f` with a 240f minimum, capped by `maxAllowedTextWidth`. Swap the constants to the spec values: ceiling becomes `DocumentWidth` (760), floor becomes `DocumentMinWidth` (480). Filename keeps the existing truncation logic.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (around lines 897–902)

- [ ] **Step 1: Locate the document width block**

Find this block (around line 897):

```csharp
                float finalWidth = maxTextWidth + 132f;
                float minDocWidth = 240f;

                if (finalWidth < minDocWidth) finalWidth = minDocWidth;

                docLayout.preferredWidth = finalWidth;
                mediaWidth = finalWidth;
```

- [ ] **Step 2: Replace with constant-driven caps**

```csharp
                float finalWidth = Mathf.Clamp(maxTextWidth + 132f, DocumentMinWidth, DocumentWidth);

                docLayout.preferredWidth = finalWidth;
                mediaWidth = finalWidth;
```

This single `Mathf.Clamp` collapses the old min/max branches and pins the ceiling at `DocumentWidth` (760) instead of the previous implicit `maxAllowedTextWidth` cap.

- [ ] **Step 3: Verify Unity recompile is clean**

Switch to Unity Editor, check Console.

- [ ] **Step 4: Manual visual smoke test**

Play scene, open a chat with documents. Verify:
- Short filenames: card sits at 480 minimum (slightly wider than old 240)
- Long filenames: card caps at 760, filename truncates with ellipsis
- Mid-length filenames: card grows naturally between 480 and 760

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): cap document bubble width with DocumentWidth/DocumentMinWidth

Document bubbles now clamp between 480 and 760, replacing the previous
240 floor and implicit ~732 ceiling. Long filenames truncate against
the 760 ceiling instead of growing the bubble against the canvas-wide
text cap.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Switch Text Max-Width to MaxBubbleWidth and Add Text-Bubble MinWidth

Two related changes in `ApplyInlineTimeReservation` / the surrounding text-sizing block: (1) replace the `containerWidth * maxBubbleWidthPercentage` calculation with the fixed `MaxBubbleWidth` constant; (2) set `LayoutElement.minWidth = MinBubbleWidth` on the text bubble in the `Chat` branch of `ApplyDynamicLayout` so a single emoji bubble still reserves room for the inline timestamp.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (line 855 for max-width, around line 591 for min-width in Chat branch)

- [ ] **Step 1: Replace text max-width calculation**

Find this block (around line 855):

```csharp
        float maxAllowedTextWidth = (containerWidth * maxBubbleWidthPercentage) - paddingX;
```

Replace with:

```csharp
        float maxAllowedTextWidth = MaxBubbleWidth - paddingX;
```

This drops the screen-relative percentage in favour of the fixed-pixel ceiling. On a 1080 canvas with `paddingX=40`, the old value was `(1080 × 0.7) - 40 = 716`. The new value is `810 - 40 = 770`. Slightly wider, matching the spec.

The `containerWidth` variable above this line is now only used as a fallback inside the link-preview block (Task 7) — leave its declaration in place; it'll still be used.

- [ ] **Step 2: Add min-width on text bubble in Chat branch**

Find the `else if (type == MessageType.Chat)` branch in `ApplyDynamicLayout` (around line 589). At the very start of the branch, before any padding/spacing logic, add:

```csharp
        else if (type == MessageType.Chat)
        {
            // Reserve room for the inline timestamp + tick on very short messages.
            if (bubbleBackground != null)
            {
                var bubbleLe = bubbleBackground.GetComponent<LayoutElement>();
                if (bubbleLe == null) bubbleLe = bubbleBackground.gameObject.AddComponent<LayoutElement>();
                bubbleLe.minWidth = MinBubbleWidth;
            }

            layout.spacing = 4; 
            // ... rest of the Chat branch unchanged ...
        }
```

- [ ] **Step 3: Verify Unity recompile is clean**

Switch to Unity Editor, check Console.

- [ ] **Step 4: Manual visual smoke test**

Play scene, open a chat with mixed text messages. Verify:
- Long text messages wrap slightly later (~810 vs old ~756)
- Single-character messages like "ok" or "👍" reserve ≥90px so the inline time stays on the same line
- Other message types unaffected

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): text bubble uses MaxBubbleWidth ceiling and MinBubbleWidth floor

Text wrap ceiling switches from (containerWidth × 0.7) to MaxBubbleWidth
(810). Chat-type bubbles now also enforce a LayoutElement.minWidth of
MinBubbleWidth (90) so single-emoji / two-char messages still fit the
inline timestamp without breaking onto a new line.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Switch Caption Clamp to mediaWidth - CaptionInset

Three call sites use `fixedWidth ± constant` to clamp caption width under image/video. Replace each with `mediaWidth - CaptionInset`, where `mediaWidth` comes from `ResolveMediaSize` for the current message's aspect ratio.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (around lines 818, 956, 965)

- [ ] **Step 1: Replace the jumbo-emoji caption clamp (line 818)**

Find this block (around line 818):

```csharp
            if ((currentVm.type == MessageType.Image || currentVm.type == MessageType.Video) && !currentVm.isSticker)
            {
                if (messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text))
                {
                    // Force the caption to wrap tightly under the Image!
                    float maxCaptionWidth = fixedWidth + 12f; 
                    textLayout.preferredWidth = Mathf.Min(textLayout.preferredWidth, maxCaptionWidth);
                }
            }
```

Replace with:

```csharp
            if ((currentVm.type == MessageType.Image || currentVm.type == MessageType.Video) && !currentVm.isSticker)
            {
                if (messageText.gameObject.activeSelf && !string.IsNullOrEmpty(messageText.text))
                {
                    // Force the caption to wrap tightly under the Image!
                    float realRatio = currentVm.aspectRatio > 0 ? currentVm.aspectRatio : 1.0f;
                    float captionMediaWidth = ResolveMediaSize(realRatio).x;
                    float maxCaptionWidth = captionMediaWidth - CaptionInset;
                    textLayout.preferredWidth = Mathf.Min(textLayout.preferredWidth, maxCaptionWidth);
                }
            }
```

- [ ] **Step 2: Replace the standard caption clamp (lines 953–966)**

Find this block (around line 953):

```csharp
            if (isMediaCaption)
            {
                // Force the measuring tape to be no wider than the image container!
                availableWidthForText = Mathf.Min(availableWidthForText, fixedWidth - 16f);
            }

            Vector2 wrappedSize = messageText.GetPreferredValues(messageText.text, availableWidthForText, Mathf.Infinity);
            textLayout.preferredWidth = Mathf.Min(wrappedSize.x + 21f, maxAllowedTextWidth); 

            if (isMediaCaption)
            {
                // Clamp the text block so its preferred width physically cannot exceed the image
                textLayout.preferredWidth = Mathf.Min(textLayout.preferredWidth, fixedWidth);
            }
```

Replace with:

```csharp
            float captionMediaWidth = 0f;
            if (isMediaCaption)
            {
                float realRatio = currentVm.aspectRatio > 0 ? currentVm.aspectRatio : 1.0f;
                captionMediaWidth = ResolveMediaSize(realRatio).x;
                // Force the measuring tape to be no wider than the image container!
                availableWidthForText = Mathf.Min(availableWidthForText, captionMediaWidth - CaptionInset);
            }

            Vector2 wrappedSize = messageText.GetPreferredValues(messageText.text, availableWidthForText, Mathf.Infinity);
            textLayout.preferredWidth = Mathf.Min(wrappedSize.x + 21f, maxAllowedTextWidth); 

            if (isMediaCaption)
            {
                // Clamp the text block so its preferred width physically cannot exceed the image
                textLayout.preferredWidth = Mathf.Min(textLayout.preferredWidth, captionMediaWidth);
            }
```

- [ ] **Step 3: Verify Unity recompile is clean**

Switch to Unity Editor, check Console.

- [ ] **Step 4: Manual visual smoke test**

Play scene, open a chat with an image-plus-caption message. Verify:
- Caption now wraps to match the image's actual width (not the old 464)
- Landscape-image captions stretch wider than before (~778 = 810-32)
- Portrait-image captions are narrower (~616 = 648-32)
- Sticker messages still don't have caption behavior (stickers don't carry text)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): caption width follows media width via ResolveMediaSize

Replaces the three fixedWidth-based caption clamps with mediaWidth -
CaptionInset, where mediaWidth comes from ResolveMediaSize for the
current message's aspect ratio. Captions under landscape images now
stretch wider, captions under portraits shrink to match.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Switch Link Preview Width to bubbleWidth × LinkPreviewRatio

Link preview cards currently size against `containerWidth × 0.55f` (absolute, ~594 on 1080). Make the card size relative to the bubble width using `LinkPreviewRatio`. The image-driven branch keeps its aspect math but ceiling-clamps against the bubble.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (around lines 986–1019)

- [ ] **Step 1: Locate the link preview block**

Find this section (around line 986):

```csharp
            float targetWidth = containerWidth * 0.55f; // Safe default

            // --- THE NEW FIX: IMAGE-DRIVEN WIDTH ---
            // We ignore the text length completely. The Image Aspect Ratio controls the card size!
            if (linkPreviewImage != null && linkPreviewImage.gameObject.activeSelf && linkPreviewImage.sprite != null)
            {
                float aspect = (float)linkPreviewImage.sprite.texture.width / linkPreviewImage.sprite.texture.height;
                
                // Base the width on a "Square" image taking up 60% of the screen.
                // Landscape images will scale up (and hit the max limit). Portrait images will scale down.
                float calculatedWidth = (containerWidth * 0.6f) * aspect;
                
                float minCardWidth = containerWidth * 0.45f; // Don't let portrait cards get too skinny
                targetWidth = Mathf.Clamp(calculatedWidth, minCardWidth, maxAllowedTextWidth);
                // ...
            }
            else
            {
                // ...
                float maxTextWidth = Mathf.Max(titleWidth, domainWidth);
                targetWidth = Mathf.Clamp(maxTextWidth, containerWidth * 0.45f, maxAllowedTextWidth);
            }
```

- [ ] **Step 2: Replace the width math**

Replace with this version that uses `MaxBubbleWidth` and `LinkPreviewRatio` instead of `containerWidth × 0.55/0.6/0.45`:

```csharp
            float bubbleCeiling = MaxBubbleWidth - paddingX;
            float targetWidth = bubbleCeiling * LinkPreviewRatio; // Safe default — 65% of the bubble

            // --- IMAGE-DRIVEN WIDTH ---
            // We ignore the text length completely. The Image Aspect Ratio controls the card size!
            if (linkPreviewImage != null && linkPreviewImage.gameObject.activeSelf && linkPreviewImage.sprite != null)
            {
                float aspect = (float)linkPreviewImage.sprite.texture.width / linkPreviewImage.sprite.texture.height;
                
                // Base the width on a "Square" image taking ~75% of the bubble. Landscape
                // images scale up and clamp at the bubble ceiling; portraits scale down to
                // the floor so they don't get spindly.
                float calculatedWidth = (bubbleCeiling * 0.75f) * aspect;
                
                float minCardWidth = bubbleCeiling * 0.55f; // Don't let portrait cards get too skinny
                targetWidth = Mathf.Clamp(calculatedWidth, minCardWidth, bubbleCeiling);
                // ...
            }
            else
            {
                // ...
                float maxTextWidth = Mathf.Max(titleWidth, domainWidth);
                targetWidth = Mathf.Clamp(maxTextWidth, bubbleCeiling * 0.55f, bubbleCeiling);
            }
```

Keep the rest of the block (title/description/domain math) unchanged — they all reference `targetWidth`, which still works.

- [ ] **Step 3: Verify Unity recompile is clean**

Switch to Unity Editor, check Console.

- [ ] **Step 4: Manual visual smoke test**

Play scene, send (or open existing) a chat message containing a URL with link preview. Verify:
- Default card width is ~501 (770 × 0.65) instead of old 594
- Cards with landscape thumbnails grow toward the bubble ceiling (~770)
- Cards with portrait thumbnails clamp at the ~423 floor (770 × 0.55)
- Card never overflows the bubble

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): link preview width relative to bubble, not canvas

Replaces containerWidth × 0.55 / 0.6 / 0.45 with MaxBubbleWidth-derived
ratios (default LinkPreviewRatio = 0.65, image branch up to bubble
ceiling, portrait floor at 0.55 × bubble). Link preview cards now sit
cleanly inside the bubble at all screen sizes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Sync Bubble Padding — Prefabs + Runtime Reset

Three places hold the same `8/8/8/12` padding: both message prefabs (which ship with `4/4/4/4`, never rendering) and `ResetBubbleLayoutToDefault` at line 1224 (hardcoded literal). Sync all three so the constants from Task 1 are the single source of truth and the prefab editor matches what renders.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (line 1224 in `ResetBubbleLayoutToDefault`)
- Modify: `Assets/Prefabs/MessageTextIncoming.prefab` (lines 2563–2566)
- Modify: `Assets/Prefabs/MessageTextOutgoing.prefab` (same VerticalLayoutGroup block — find it with the search command in Step 3)

- [ ] **Step 1: Wire BubblePad constants into ResetBubbleLayoutToDefault**

In `MessageItemView.cs`, find this block (around line 1222):

```csharp
        if (bubbleParent.TryGetComponent<VerticalLayoutGroup>(out var bubbleLayout))
        {
            bubbleLayout.padding = new RectOffset(8, 8, 8, 12);
            bubbleLayout.childForceExpandWidth = true; 
        }
```

Replace the literal-values line with constant references:

```csharp
        if (bubbleParent.TryGetComponent<VerticalLayoutGroup>(out var bubbleLayout))
        {
            bubbleLayout.padding = new RectOffset(BubblePadLeft, BubblePadRight, BubblePadTop, BubblePadBottom);
            bubbleLayout.childForceExpandWidth = true; 
        }
```

Now the `BubblePad*` constants are the single source of truth for the bubble reset padding.

- [ ] **Step 2: Confirm the prefab VLG block in MessageTextIncoming.prefab**

Run:

```bash
grep -n "m_Padding:" /Users/ayan/Projects/Automation/Assets/Prefabs/MessageTextIncoming.prefab
```

Expected output includes a line near `2562`. The block at that location is the bubble's VerticalLayoutGroup (the one with `m_Spacing: 5` just below it).

Read lines 2562–2567 to confirm:

```yaml
  m_Padding:
    m_Left: 4
    m_Right: 4
    m_Top: 4
    m_Bottom: 4
```

- [ ] **Step 3: Edit MessageTextIncoming.prefab padding values**

In that file, change those four lines to:

```yaml
  m_Padding:
    m_Left: 8
    m_Right: 8
    m_Top: 8
    m_Bottom: 12
```

- [ ] **Step 4: Find and edit the same block in MessageTextOutgoing.prefab**

Run:

```bash
grep -n -A 5 "m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.VerticalLayoutGroup" /Users/ayan/Projects/Automation/Assets/Prefabs/MessageTextOutgoing.prefab | head -40
```

Identify the block whose nearby `m_Spacing` is `5` (the bubble's VLG — same structure as incoming). Edit that block's `m_Padding` from `4/4/4/4` to `8/8/8/12`.

- [ ] **Step 5: Verify Unity recompiles and reimports without errors**

Switch to Unity Editor — it should auto-recompile the script and reimport both prefabs. Check Console for zero errors. Open each prefab in the prefab editor (double-click in Project window) and visually confirm the bubble has slightly more interior padding.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextOutgoing.prefab
git commit -m "$(cat <<'EOF'
chore(messages): single-source bubble padding via BubblePad constants

Three places held 8/8/8/12: both message prefabs (which shipped at
4/4/4/4 and never rendered) and ResetBubbleLayoutToDefault (hardcoded
literal). All three now resolve to the BubblePad* constants from
Task 1. Prefab editor view now matches what runs.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Remove Legacy Fields and Final Smoke Test

Drop `public float fixedWidth = 464f;` and `public float maxBubbleWidthPercentage = 0.7f;` — every call site has been migrated. Run a full manual smoke test of every message type.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (lines 55–58)

- [ ] **Step 1: Search for any remaining references to the legacy fields**

```bash
grep -n "fixedWidth\|maxBubbleWidthPercentage" /Users/ayan/Projects/Automation/Assets/Scripts/UI/MessageItemView.cs
```

Expected output: only the two declarations at lines 56 and 58. If anything else shows up, stop — there's an unmigrated call site to fix before deleting the fields.

- [ ] **Step 2: Remove the legacy fields**

Find:

```csharp
    [Header("Settings")]
    public float fixedWidth = 464f;
    [Tooltip("How much of the screen should the text bubble take up before wrapping? (0.7 = 70%)")]
    public float maxBubbleWidthPercentage = 0.7f; 
    public Color incomingColor = Color.white;
```

Replace with:

```csharp
    [Header("Settings")]
    public Color incomingColor = Color.white;
```

- [ ] **Step 3: Verify Unity recompile is clean**

Switch to Unity Editor, wait for recompile, check Console for zero errors and zero warnings. Open both message prefabs and confirm the MessageItemView component Inspector no longer shows the deleted fields.

- [ ] **Step 4: Full manual smoke test**

Open Main scene, hit Play, navigate into a chat that has all message types. Verify every item from the spec's testing section:

- [ ] Text long enough to wrap → wraps at ~810 (770 inside padding)
- [ ] Text short ("ok") → minimum 90 wide so inline time fits
- [ ] Landscape photo (3:2 or wider) → 810 wide, height scales
- [ ] Portrait photo (2:3 or taller) → 648 wide
- [ ] Square photo (~1:1) → 700 wide
- [ ] Very tall portrait (>9:16) → clamped to 9:16
- [ ] Very wide pano (>16:9) → clamped to 16:9
- [ ] Sticker → 432×432, no bubble background, timestamp floats below
- [ ] Voice note → 720×150, waveform fills, time at bottom
- [ ] Audio file (non-voice MIME) → 760×190
- [ ] Document with short filename → 480 floor
- [ ] Document with long filename → 760 cap, filename truncates with ellipsis
- [ ] Text with link preview (with image) → preview ~501–770 depending on aspect, never overflows bubble
- [ ] Text with link preview (no image) → preview ~423–770, scales with text length
- [ ] Image with short caption → caption matches image width, e.g. 648-32=616 under a portrait
- [ ] Image with long caption → caption wraps within mediaWidth − 32

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
refactor(messages): remove legacy fixedWidth and maxBubbleWidthPercentage

All call sites migrated to ResolveContentSize / ResolveMediaSize plus
the new constants block. The two public serialised fields are no
longer referenced anywhere. Drops them from the [Header] block.

Closes the WhatsApp-style message bubble sizing milestone.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review Checklist (Plan Author)

- [x] **Spec coverage** — Every requirement in the spec maps to a task:
  - Constants block → Task 1
  - ResolveContentSize / ResolveMediaSize → Task 1
  - Image/Video/Sticker via resolver → Task 2
  - Voice/Audio fixed sizing → Task 3
  - Document min/max → Task 4
  - Text max-width + min-width → Task 5
  - Caption follows media width → Task 6
  - Link preview relative to bubble → Task 7
  - Prefab padding sync → Task 8
  - Aspect clamp tightening → Task 1 (renamed values in-place)
  - Legacy field removal → Task 9
  - Manual smoke test → Task 9 Step 4

- [x] **No placeholders** — Every step contains exact code, exact paths, exact commands.

- [x] **Type consistency** — `MinAspectRatio` / `MaxAspectRatio` keep their names across tasks. `ResolveMediaSize` always returns `Vector2`. All constants referenced match their declarations in Task 1.

- [x] **Granularity** — Each task is 5–7 small steps, each step is 2–5 minutes. Each task ends in a clean Unity compile and a commit, so the branch can be rolled back at any task boundary.
