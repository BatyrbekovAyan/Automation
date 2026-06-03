# Media Bubble Sizing — WhatsApp Bounding-Box Fit + Video Orientation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Size image/video bubbles by fitting their true aspect into one 810×1080 bounding box (WhatsApp-style), and make outgoing portrait videos get portrait bubbles by sourcing the bubble aspect from the upright thumbnail.

**Architecture:** Extract the pure sizing + orientation math into a new unit-tested helper `MediaBubbleSize` (mirrors the existing `ScrollFabMath` / `UnreadSeparatorPlacement` pattern). `MessageItemView.ResolveMediaSize` becomes a one-line delegator; the two aspect-clamp constants become aliases of the helper's so the bubble-size clamp and the crop-target clamp stay identical. For outgoing video, `ChatManager.MediaSend` derives `vm.aspectRatio` from the upright thumbnail's pixel dimensions, falling back to rotation-corrected metadata.

**Tech Stack:** Unity 6 (C#), NUnit EditMode tests (Unity Test Runner), NativeGallery plugin.

**Spec:** `docs/superpowers/specs/2026-06-02-media-bubble-whatsapp-sizing-orientation-design.md`

---

## Execution Notes (project-specific — read first)

This is a Unity project. The usual CLI test/build loop does **not** apply:

- **No worktrees.** Work on the current branch `feat/incoming-video-thumbnails` (durable user preference).
- **Compilation + `.meta` generation happen in the user's open Unity Editor.** After a new `.cs` file is written, the executing agent cannot generate its Unity `.meta`. Pause and ask the user to let Unity import/compile, then continue.
- **Tests run in the Editor's Test Runner** (Window → General → Test Runner → EditMode → Run All). Batchmode CLI conflicts with the open Editor (single-instance lock), so do **not** try to run tests from the shell. At each "run tests" step, ask the user to run and report PASS/FAIL.
- **Commits stage both the `.cs` and the Unity-generated `.meta`.** Stage explicit paths (never `git add -A`); the working tree has unrelated in-progress changes that must stay uncommitted.

---

## File Structure

- **Create** `Assets/Scripts/Chat/MediaBubbleSize.cs` — pure static helper: `Resolve(aspect)` (bounding-box fit) + `OrientedAspect(rawAspect, rotationDegrees)` (rotation correction) + the four sizing constants. No `UnityEngine` MonoBehaviour dependency; uses only `Vector2`/`Mathf`.
- **Create** `Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs` — EditMode NUnit tests for both methods.
- **Modify** `Assets/Scripts/UI/MessageItemView.cs` — replace the 8-constant Image/Video block (lines 82–90) with two clamp aliases; make `ResolveMediaSize` (lines 152–182) a one-line delegator.
- **Modify** `Assets/Scripts/Main/ChatManager.MediaSend.cs` — `SeedVideoThumbCache` returns `(url, aspect)`; `ReadVideoMetadata` returns rotation-corrected display aspect; the `GalleryVideo` case (lines 113–118) prefers the thumbnail aspect.

---

## Task 1: `MediaBubbleSize.Resolve` — bounding-box sizing + tests

**Files:**
- Create: `Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs`
- Create: `Assets/Scripts/Chat/MediaBubbleSize.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class MediaBubbleSizeTests
{
    private const float D = 1f; // px tolerance (rounding-safe)

    [Test]
    public void Landscape16x9_FillsBoxWidth()
    {
        Vector2 s = MediaBubbleSize.Resolve(1.78f);
        Assert.AreEqual(810f, s.x, D);
        Assert.AreEqual(455f, s.y, D);
    }

    [Test]
    public void Square_FillsBoxWidthAndIsSquare()
    {
        Vector2 s = MediaBubbleSize.Resolve(1.0f);
        Assert.AreEqual(810f, s.x, D);
        Assert.AreEqual(810f, s.y, D);
    }

    [Test]
    public void Portrait3x4_HitsHeightCapAtFullWidth()
    {
        Vector2 s = MediaBubbleSize.Resolve(0.75f);
        Assert.AreEqual(810f, s.x, D);
        Assert.AreEqual(1080f, s.y, D);
    }

    [Test]
    public void Portrait9x16_IsHeightBoundAndNarrower()
    {
        Vector2 s = MediaBubbleSize.Resolve(0.5625f);
        Assert.AreEqual(607.5f, s.x, D);
        Assert.AreEqual(1080f, s.y, D);
    }

    [Test]
    public void Panorama_ClampedToMaxAspect()
    {
        Vector2 s = MediaBubbleSize.Resolve(3.0f); // wider than 16:9 -> clamp 1.78
        Assert.AreEqual(810f, s.x, D);
        Assert.AreEqual(455f, s.y, D);
    }

    [Test]
    public void VeryTall_ClampedToMinAspect()
    {
        Vector2 s = MediaBubbleSize.Resolve(0.3f); // taller than 9:16 -> clamp 0.56
        Assert.AreEqual(604.8f, s.x, D);
        Assert.AreEqual(1080f, s.y, D);
    }

    [Test]
    public void NonPositiveOrNonFinite_TreatedAsSquare()
    {
        Assert.AreEqual(810f, MediaBubbleSize.Resolve(0f).x, D);
        Assert.AreEqual(810f, MediaBubbleSize.Resolve(-2f).x, D);
        Assert.AreEqual(810f, MediaBubbleSize.Resolve(float.NaN).y, D);
    }
}
```

- [ ] **Step 2: Run the test — verify it fails**

Ask the user to run in Unity Test Runner (EditMode). Expected: **compile error / FAIL** — `MediaBubbleSize` does not exist yet.

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Scripts/Chat/MediaBubbleSize.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Pure sizing math for image/video message bubbles. WhatsApp-style: fit the media's
/// (clamped) aspect ratio into one MaxWidth × MaxHeight bounding box, preserving
/// proportion. Aspect = width / height (>1 landscape, <1 portrait). Media wider than
/// MaxAspect or taller than MinAspect is sized to the clamp edge; the caller
/// (ApplyTextureAspectFill) center-crops it to that same clamped ratio.
/// </summary>
public static class MediaBubbleSize
{
    public const float MaxWidth  = 810f;   // box width  (~0.75 × 1080 ref canvas)
    public const float MaxHeight = 1080f;  // box height (portrait cap)
    public const float MinAspect = 0.56f;  // 9:16 — taller is center-cropped
    public const float MaxAspect = 1.78f;  // 16:9 — wider is center-cropped

    public static Vector2 Resolve(float aspect)
    {
        if (!float.IsFinite(aspect) || aspect <= 0f) aspect = 1f;
        aspect = Mathf.Clamp(aspect, MinAspect, MaxAspect);

        float width  = MaxWidth;
        float height = width / aspect;

        if (height > MaxHeight)         // portrait taller than the box → height-bound
        {
            height = MaxHeight;
            width  = height * aspect;   // narrower, taller bubble
        }

        return new Vector2(width, height);
    }
}
```

- [ ] **Step 4: Run the test — verify it passes**

Ask the user to let Unity import the two new files (generates `.meta`), then run EditMode tests. Expected: **all 7 `MediaBubbleSizeTests` PASS**.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/MediaBubbleSize.cs Assets/Scripts/Chat/MediaBubbleSize.cs.meta \
        Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs.meta
git commit -m "feat(chat): add MediaBubbleSize bounding-box sizing helper + tests"
```

---

## Task 2: `MediaBubbleSize.OrientedAspect` — rotation correction + tests

**Files:**
- Modify: `Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs`
- Modify: `Assets/Scripts/Chat/MediaBubbleSize.cs`

- [ ] **Step 1: Write the failing test**

Append these methods inside the `MediaBubbleSizeTests` class in `Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs`:

```csharp
    [Test]
    public void OrientedAspect_NoRotation_Unchanged()
    {
        Assert.AreEqual(1.78f, MediaBubbleSize.OrientedAspect(1.78f, 0f), 0.001f);
    }

    [Test]
    public void OrientedAspect_Rotated90_Inverts()
    {
        Assert.AreEqual(1f / 1.78f, MediaBubbleSize.OrientedAspect(1.78f, 90f), 0.001f);
    }

    [Test]
    public void OrientedAspect_Rotated270_Inverts()
    {
        Assert.AreEqual(1f / 0.5625f, MediaBubbleSize.OrientedAspect(0.5625f, 270f), 0.001f);
    }

    [Test]
    public void OrientedAspect_Rotated180_Unchanged()
    {
        Assert.AreEqual(1.5f, MediaBubbleSize.OrientedAspect(1.5f, 180f), 0.001f);
    }

    [Test]
    public void OrientedAspect_NonPositiveRaw_ReturnsSquare()
    {
        Assert.AreEqual(1f, MediaBubbleSize.OrientedAspect(0f, 90f), 0.001f);
        Assert.AreEqual(1f, MediaBubbleSize.OrientedAspect(-3f, 0f), 0.001f);
    }
```

- [ ] **Step 2: Run the test — verify it fails**

Ask the user to run EditMode tests. Expected: **compile error / FAIL** — `OrientedAspect` does not exist.

- [ ] **Step 3: Write the minimal implementation**

Add this method to `MediaBubbleSize` (in `Assets/Scripts/Chat/MediaBubbleSize.cs`), after `Resolve`:

```csharp
    /// <summary>
    /// Converts a raw frame aspect (width / height of the decoded/stored frame) into the
    /// aspect as displayed, accounting for a quarter-turn rotation flag. A phone portrait
    /// clip is stored as a landscape frame plus rotation = 90/270; its displayed aspect is
    /// the inverse. 0 and 180 leave the aspect unchanged.
    /// </summary>
    public static float OrientedAspect(float rawAspect, float rotationDegrees)
    {
        if (!float.IsFinite(rawAspect) || rawAspect <= 0f) return 1f;
        bool quarterTurned = (rotationDegrees == 90f || rotationDegrees == 270f);
        return quarterTurned ? 1f / rawAspect : rawAspect;
    }
```

- [ ] **Step 4: Run the test — verify it passes**

Ask the user to run EditMode tests. Expected: **all `MediaBubbleSizeTests` PASS** (12 total).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/MediaBubbleSize.cs Assets/Tests/Editor/Chat/MediaBubbleSizeTests.cs
git commit -m "feat(chat): add MediaBubbleSize.OrientedAspect rotation correction + tests"
```

---

## Task 3: Wire the helper into `MessageItemView` (Part A)

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs:82-90` (constant block)
- Modify: `Assets/Scripts/UI/MessageItemView.cs:152-182` (`ResolveMediaSize`)

- [ ] **Step 1: Replace the 8-constant Image/Video block with two clamp aliases**

In `Assets/Scripts/UI/MessageItemView.cs`, replace lines 82–90:

```csharp
    // === Image / Video ===
    private const float ImageLandscapeWidth   = 810f;   // 0.75 × canvas
    private const float ImagePortraitWidth    = 648f;   // 0.60 × canvas
    private const float ImageSquareWidth      = 700f;   // 0.65 × canvas
    private const float ImageMaxHeight        = 1080f;  // tall-portrait clamp
    private const float MinAspectRatio        = 0.56f;  // 9:16
    private const float MaxAspectRatio        = 1.78f;  // 16:9
    private const float AspectLandscapeThreshold = 1.1f; // > → landscape
    private const float AspectPortraitThreshold  = 0.9f; // < → portrait
```

with:

```csharp
    // === Image / Video ===
    // Bubble dimensions + aspect clamp live in MediaBubbleSize (pure, unit-tested).
    // These aliases keep the clamp used for the crop target (passed to
    // ApplyTextureAspectFill) identical to the sizing clamp — one source of truth.
    private const float MinAspectRatio = MediaBubbleSize.MinAspect;  // 9:16
    private const float MaxAspectRatio = MediaBubbleSize.MaxAspect;  // 16:9
```

- [ ] **Step 2: Replace the `ResolveMediaSize` body with a delegator**

In the same file, replace the whole method at lines 152–182:

```csharp
    private Vector2 ResolveMediaSize(float aspect)
    {
        if (!float.IsFinite(aspect) || aspect <= 0f) aspect = 1f;
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

with:

```csharp
    private Vector2 ResolveMediaSize(float aspect) => MediaBubbleSize.Resolve(aspect);
```

- [ ] **Step 3: Compile + run the full EditMode suite**

Ask the user to let Unity compile and run **all** EditMode tests. Expected: **no compile errors; the full suite (including `MediaBubbleSizeTests`) PASSES.** A compile error naming `ImageLandscapeWidth`, `AspectPortraitThreshold`, etc. means a stale reference survived — grep `Assets/Scripts/` for that identifier and remove it.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "refactor(chat): size media bubbles via MediaBubbleSize bounding-box fit"
```

---

## Task 4: Source outgoing video bubble aspect from the upright thumbnail (Part B)

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.MediaSend.cs:395-420` (`SeedVideoThumbCache`)
- Modify: `Assets/Scripts/Main/ChatManager.MediaSend.cs:422-432` (`ReadVideoMetadata`)
- Modify: `Assets/Scripts/Main/ChatManager.MediaSend.cs:113-118` (`GalleryVideo` case)

- [ ] **Step 1: `SeedVideoThumbCache` returns the thumbnail aspect**

In `Assets/Scripts/Main/ChatManager.MediaSend.cs`, replace the whole method at lines 395–420:

```csharp
    private string SeedVideoThumbCache(string localPath, string tempId)
    {
        string syntheticUrl = $"thumb://staged/{tempId}";
        Texture2D thumb = null;
        try
        {
            // markTextureNonReadable: false keeps the thumbnail's pixels CPU-readable so
            // EncodeToPNG below works. NativeGallery's default (true) discards the CPU copy
            // on GPU upload, so EncodeToPNG throws "Texture is not readable" and the thumb
            // never caches — same reason SeedImageCache passes the flag on its decode.
            thumb = NativeGallery.GetVideoThumbnail(localPath, markTextureNonReadable: false);
            if (thumb == null) return syntheticUrl;
            byte[] png = thumb.EncodeToPNG();
            string targetPath = MediaCacheManager.Instance.GetFilePathFromUrl(syntheticUrl);
            System.IO.File.WriteAllBytes(targetPath, png);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatManager] SeedVideoThumbCache failed for {localPath}: {ex.Message}");
        }
        finally
        {
            if (thumb != null) UnityEngine.Object.Destroy(thumb);
        }
        return syntheticUrl;
    }
```

with:

```csharp
    private (string syntheticUrl, float aspect) SeedVideoThumbCache(string localPath, string tempId)
    {
        string syntheticUrl = $"thumb://staged/{tempId}";
        float aspect = 0f; // 0 = unknown (Editor / decode failure) -> caller falls back to metadata
        Texture2D thumb = null;
        try
        {
            // markTextureNonReadable: false keeps the thumbnail's pixels CPU-readable so
            // EncodeToPNG below works. NativeGallery's default (true) discards the CPU copy
            // on GPU upload, so EncodeToPNG throws "Texture is not readable" and the thumb
            // never caches — same reason SeedImageCache passes the flag on its decode.
            thumb = NativeGallery.GetVideoThumbnail(localPath, markTextureNonReadable: false);
            if (thumb == null) return (syntheticUrl, 0f);

            // The thumbnail is decoded upright (display orientation), so its own pixel
            // dimensions are the ground truth for the bubble's aspect — independent of the
            // raw-frame rotation metadata that GetVideoProperties reports.
            if (thumb.height > 0) aspect = (float)thumb.width / thumb.height;

            byte[] png = thumb.EncodeToPNG();
            string targetPath = MediaCacheManager.Instance.GetFilePathFromUrl(syntheticUrl);
            System.IO.File.WriteAllBytes(targetPath, png);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatManager] SeedVideoThumbCache failed for {localPath}: {ex.Message}");
        }
        finally
        {
            if (thumb != null) UnityEngine.Object.Destroy(thumb);
        }
        return (syntheticUrl, aspect);
    }
```

- [ ] **Step 2: `ReadVideoMetadata` returns the rotation-corrected display aspect**

In the same file, replace the whole method at lines 422–432:

```csharp
    private (float aspect, int durationSec, float rotation) ReadVideoMetadata(string path)
    {
        try
        {
            var props = NativeGallery.GetVideoProperties(path);
            float aspect = props.height > 0 ? (float)props.width / props.height : 1.0f;
            int durationSec = (int)(props.duration / 1000);
            return (aspect, durationSec, props.rotation);
        }
        catch { return (1.0f, 0, 0f); }
    }
```

with:

```csharp
    private (float aspect, int durationSec, float rotation) ReadVideoMetadata(string path)
    {
        try
        {
            var props = NativeGallery.GetVideoProperties(path);
            float rawAspect = props.height > 0 ? (float)props.width / props.height : 1.0f;
            // Fallback aspect when no thumbnail decoded: correct the raw-frame aspect for a
            // quarter-turn so a portrait clip stored as a landscape frame still gets a
            // portrait bubble. Thumbnail dims (preferred source) need no correction.
            float aspect = MediaBubbleSize.OrientedAspect(rawAspect, props.rotation);
            int durationSec = (int)(props.duration / 1000);
            return (aspect, durationSec, props.rotation);
        }
        catch { return (1.0f, 0, 0f); }
    }
```

- [ ] **Step 3: Prefer the thumbnail aspect in the `GalleryVideo` case**

In the same file, replace lines 113–118:

```csharp
                vm.thumbnailUrl = SeedVideoThumbCache(stagedVideoPath, tempId);
                vm.videoUrl     = "file://" + stagedVideoPath;
                var meta = ReadVideoMetadata(stagedVideoPath);
                vm.aspectRatio   = meta.aspect;
                vm.duration      = meta.durationSec;
                vm.videoRotation = meta.rotation;
```

with:

```csharp
                var (thumbUrl, thumbAspect) = SeedVideoThumbCache(stagedVideoPath, tempId);
                var meta = ReadVideoMetadata(stagedVideoPath);
                vm.thumbnailUrl  = thumbUrl;
                vm.videoUrl      = "file://" + stagedVideoPath;
                // Upright thumbnail dims are the ground truth; corrected metadata is the
                // fallback (e.g. Editor, or thumbnail decode failure). Both are display-oriented.
                vm.aspectRatio   = thumbAspect > 0f ? thumbAspect : meta.aspect;
                vm.duration      = meta.durationSec;
                vm.videoRotation = meta.rotation; // unchanged; consumed by VideoController only
```

- [ ] **Step 4: Compile + run the full EditMode suite**

Ask the user to let Unity compile and run all EditMode tests. Expected: **no compile errors; full suite PASSES.** (No new unit test here — the native `NativeGallery` calls can't run in EditMode; the testable logic was covered by `OrientedAspect` in Task 2.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.MediaSend.cs
git commit -m "fix(chat): orient outgoing video bubbles from upright thumbnail aspect"
```

---

## Task 5: On-device visual verification (manual — required)

Orientation and native thumbnails cannot be verified in the Editor; they need a device build. This task has no commit.

- [ ] **Step 1: Build to a device** (Android primary) and open a chat.

- [ ] **Step 2: Send and verify each case** — confirm the *bubble shape* matches the media and the thumbnail is upright (not a cropped horizontal strip):
  - Portrait phone video → tall bubble (~608×1080), upright thumbnail. **(the headline fix)**
  - Landscape video → wide bubble (~810×455).
  - Portrait photo → tall bubble; landscape photo → wide bubble; near-square image → ~810×810.

- [ ] **Step 3: Verify fullscreen playback is unchanged** — tap each video; orientation and fit in `VideoController` must be exactly as before (no rotation regression).

- [ ] **Step 4: Report results.** If a portrait video still shows landscape in the *bubble*, capture whether the fullscreen view is also wrong: bubble-only wrong ⇒ thumbnail/aspect wiring; both wrong ⇒ thumbnail itself is sideways (revisit the upright-thumbnail assumption in the spec).

---

## Self-Review

**Spec coverage:**
- Part A bounding-box sizing → Task 1 (helper) + Task 3 (wired in). ✓
- Part B video orientation (thumbnail-preferred, metadata fallback) → Task 2 (`OrientedAspect`) + Task 4 (wiring). ✓
- Retire bucket constants → Task 3 Step 1. ✓
- `VideoController` unchanged → not modified by any task; `vm.videoRotation` left intact (Task 4 Step 3). ✓
- Incoming sizing covered by shared `ResolveMediaSize` → Task 3 (no incoming-specific code). ✓
- EditMode test for the pure function + on-device check → Tasks 1, 2, 5. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every test step shows full test bodies. ✓

**Type consistency:** Helper is `MediaBubbleSize` with `Resolve(float)→Vector2`, `OrientedAspect(float,float)→float`, consts `MaxWidth/MaxHeight/MinAspect/MaxAspect` — referenced identically in Tasks 3 (`MediaBubbleSize.MinAspect/MaxAspect`, `MediaBubbleSize.Resolve`) and 4 (`MediaBubbleSize.OrientedAspect`). `SeedVideoThumbCache` new return `(string syntheticUrl, float aspect)` is destructured as `(thumbUrl, thumbAspect)` in Task 4 Step 3. ✓
