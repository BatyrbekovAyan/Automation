# Chat-Open Phase Model & Bubble Memory Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reshape chat-open into Prep → Slide → Populate with hard gates between phases, and add an owned-resource ledger to `MessageItemView` so dynamic `Texture2D` and `Sprite` objects are freed on bubble destroy — fixing the after-6-9-opens OOM crash.

**Architecture:** A `ChatOpenPhase` enum drives a state machine on `ChatManager`. `SelectChat` synchronously destroys old bubbles (which frees their owned textures via `OnDestroy`), then starts `OpenChatRoutine` — a coroutine that loads cache, parses, splits the first screen, queues the in-flight sync, waits a literal 300 ms from tap time, then triggers `SwipeToBack.SlideInToMessages`. The slide-in's callback flips phase to `Populate`, fires `OnBatchMessagesLoaded`, and drains the queued sync result. `SwipeToBack`'s existing `IsSliding` flag covers slide-in, slide-out, and (newly) the user's finger-drag portion of swipe-back. Bubbles get freed on both chat-switch and slide-out completion.

**Tech Stack:** Unity 6 (6000.3.9f1) C#, coroutines (`IEnumerator` + `yield return`), TextMeshPro, Unity UI (`Image`, `ScrollRect`, `LayoutGroup`).

**Spec:** [docs/superpowers/specs/2026-05-22-chat-open-phase-model-design.md](docs/superpowers/specs/2026-05-22-chat-open-phase-model-design.md)

**Testing approach:** This codebase tests pure logic via Unity Test Runner (`Assets/Tests/Editor/Chat/...`) but has no UI-lifecycle tests. The changes here are all `MonoBehaviour` lifecycle and animation timing — not testable that way. Verification is done in two passes: (a) compile-clean check via Unity Editor auto-recompile + the `.claude/hooks/validate-cs.sh` hook, and (b) manual UAT against the acceptance criteria in the spec (open chat, swipe back, rapid re-tap, heavy-media chat opened 10×, etc.).

---

## Task 1: Remove dormant `spriteMemoryCache` from `MediaCacheManager`

The in-memory sprite cache has zero callers. Carrying it as dead code is a liability — if a future change reintroduces it without coordinating with the new ownership model in `MessageItemView`, an `Image` consuming a cached sprite would crash once `OnDestroy` destroys its underlying texture. Remove it now.

**Files:**
- Modify: `Assets/Scripts/Chat/MediaCacheManager.cs`

- [ ] **Step 1: Open `Assets/Scripts/Chat/MediaCacheManager.cs`. Delete the three field declarations.**

Find lines 12–14:

```csharp
    private const int MaxMemorySpriteCount = 100;
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, Sprite>>> spriteMemoryCache = new();
    private readonly LinkedList<KeyValuePair<string, Sprite>> spriteAccessOrder = new();
```

Delete all three lines.

- [ ] **Step 2: Delete both unused methods.**

Find `GetSpriteFromMemory` (around line 65) and `StoreSpriteInMemory` (around line 76). Delete both methods entirely.

- [ ] **Step 3: Trim the matching cache-clear lines from `EnsureBotScoped`.**

In `EnsureBotScoped` (around line 54), find:

```csharp
        cachedUrlBotId = activeBotId;
        urlPathCache.Clear();
        spriteMemoryCache.Clear();
        spriteAccessOrder.Clear();
    }
```

Remove the two `spriteMemoryCache.Clear()` and `spriteAccessOrder.Clear()` lines. Keep `urlPathCache.Clear()`.

- [ ] **Step 4: Verify no callers remain.**

Run:

```bash
grep -rn "spriteMemoryCache\|spriteAccessOrder\|GetSpriteFromMemory\|StoreSpriteInMemory\|MaxMemorySpriteCount" Assets/Scripts/
```

Expected: zero matches.

- [ ] **Step 5: Compile via Unity (or trust the editor auto-recompile). Then commit.**

```bash
git add Assets/Scripts/Chat/MediaCacheManager.cs
git commit -m "$(cat <<'EOF'
chore(media): remove dormant in-memory sprite cache

Zero callers in the codebase. Carrying it as dead code is a liability
once MessageItemView starts destroying its own textures — a cached
Sprite whose backing Texture2D got destroyed would render garbage on
iOS. File-cache path (IsImageCached / SaveImageToCache / LoadImageFromCache)
unchanged.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add owned-resource ledger to `MessageItemView`

Introduce the tracking machinery. Nothing routes through it yet — that's the next task. Keeping this isolated lets the diff stay readable.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (add to top of class, near the existing private fields around line 194)

- [ ] **Step 1: Add the imports and ledger field.**

Find the existing `using` block at the top of `MessageItemView.cs` (around line 1):

```csharp
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Nobi.UiRoundedCorners; 
using WebP; 
```

Add `using System.Collections.Generic;` after `using System.Collections;`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Nobi.UiRoundedCorners; 
using WebP; 
```

- [ ] **Step 2: Add the ledger field near other private fields.**

Find the existing private-field block (around line 184–194, just before `void Awake()`):

```csharp
    [SerializeField] private MessageViewModel currentVm;

    /// <summary>
    /// Read-only access to the message this bubble is currently bound to.
    /// Used by MessageListView for tail-merging, date-separator placement,
    /// and pagination boundary checks. Never null after the first Bind().
    /// </summary>
    public MessageViewModel BoundVm => currentVm;
    private float defaultFontSize = -1f;
    private bool hideBubble = false;
    private bool isJumboEmoji = false;
    private bool currentShowTail;
    private bool floatingTimeConfigured = false;

    private string _mainMessageOriginalText;
    private AudioSource audioSource;
    private RectTransform rectTransform;
    private TextMeshProUGUI downloadButtonText;
    private Sprite fullScreenSprite;
    private Button retryButton;
```

Add the ledger right above `_mainMessageOriginalText`:

```csharp
    /// <summary>
    /// Dynamically-created Texture2D and Sprite objects this bubble owns. Populated by TrackOwned
    /// as media loads; freed by DisposeOwned (called at the start of each ApplyTextureAspectFill
    /// and from OnDestroy). Project-asset sprites (stickerPlaceholder, playIcon, etc.) are NOT
    /// added here — they are not ours to destroy.
    /// </summary>
    private readonly List<UnityEngine.Object> _ownedDisposables = new List<UnityEngine.Object>();

    private string _mainMessageOriginalText;
```

- [ ] **Step 3: Add `TrackOwned` and `DisposeOwned` helpers.**

Find `void Awake()` (around line 202). Insert these two methods directly above it:

```csharp
    /// <summary>
    /// Records a dynamically-created Texture2D or Sprite as owned by this bubble. Returns the
    /// same reference for easy chaining: `var spr = TrackOwned(Sprite.Create(...));`.
    /// Pass nulls freely — they are ignored.
    /// </summary>
    private T TrackOwned<T>(T obj) where T : UnityEngine.Object
    {
        if (obj != null) _ownedDisposables.Add(obj);
        return obj;
    }

    /// <summary>
    /// Destroys every tracked Texture2D and Sprite. Safe to call repeatedly; safe to call
    /// after OnDestroy. Unity defers Destroy until end of frame, so any Image still
    /// referencing one of these in the current frame finishes rendering before the
    /// destruction lands — provided the caller has reassigned Image.sprite first.
    /// </summary>
    private void DisposeOwned()
    {
        for (int i = 0; i < _ownedDisposables.Count; i++)
        {
            if (_ownedDisposables[i] != null) Destroy(_ownedDisposables[i]);
        }
        _ownedDisposables.Clear();
    }

    void Awake()
```

(The `void Awake()` line should remain right where it was; we're only inserting above it.)

- [ ] **Step 4: Add the `OnDestroy` hook.**

`MessageItemView` currently has no `OnDestroy`. Add one right after `OnDisable` (around line 254). Find:

```csharp
    void OnDisable()
    {
        // ... existing OnDisable body ...

        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
    }

    private void SubscribeToEmojiReady()
```

Insert between `OnDisable` and `SubscribeToEmojiReady`:

```csharp
    void OnDestroy()
    {
        DisposeOwned();
    }

    private void SubscribeToEmojiReady()
```

- [ ] **Step 5: Verify Unity recompiles cleanly.**

The validate-cs.sh hook will run on save. If Unity is open, watch the Console for compile errors. There should be none — the new methods and field don't touch any existing code yet.

- [ ] **Step 6: Commit.**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): add owned-resource ledger to MessageItemView

Adds _ownedDisposables list + TrackOwned/DisposeOwned helpers + OnDestroy
hook. Nothing routes through this yet — the next task migrates
ApplyTextureAspectFill and the texture-load sites. Project assets
(stickerPlaceholder, playIcon, etc.) stay outside the ledger and are
never destroyed by this view.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Route `ApplyTextureAspectFill` through the ledger

`ApplyTextureAspectFill` is the central sprite-create point. Every dynamic-image code path flows into it. Wiring it to `TrackOwned`/`DisposeOwned` covers the bulk of the leak.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (around line 2435)

- [ ] **Step 1: Replace the entire `ApplyTextureAspectFill` body.**

Find the existing method (around line 2435):

```csharp
    void ApplyTextureAspectFill(Texture2D tex, bool isSticker, float targetRatio)
    {
        messageImage.color = Color.white;
        
        tex.wrapMode = TextureWrapMode.Clamp;

        if (isSticker)
        {
            messageImage.type = Image.Type.Simple;
            messageImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            fullScreenSprite = messageImage.sprite;
            messageImage.preserveAspect = true;
        }
        else
        {
            float imageRatio = (float)tex.width / tex.height;
            int cropW = tex.width;
            int cropH = tex.height;
            int x = 0;
            int y = 0;

            if (Mathf.Abs(imageRatio - targetRatio) > 0.01f) 
            {
                if (imageRatio > targetRatio)
                {
                    cropW = Mathf.RoundToInt(tex.height * targetRatio);
                    x = (tex.width - cropW) / 2;
                }
                else
                {
                    cropH = Mathf.RoundToInt(tex.width / targetRatio);
                    y = (tex.height - cropH) / 2;
                }

                Color[] pixels = tex.GetPixels(x, y, cropW, cropH);
                Texture2D croppedTex = new Texture2D(cropW, cropH, tex.format, false);
                croppedTex.wrapMode = TextureWrapMode.Clamp;
                croppedTex.SetPixels(pixels);
                croppedTex.Apply();

                messageImage.type = Image.Type.Simple;
                messageImage.sprite = Sprite.Create(croppedTex, new Rect(0, 0, cropW, cropH), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                
                fullScreenSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
            else
            {
                messageImage.type = Image.Type.Simple;
                messageImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                fullScreenSprite = messageImage.sprite;
            }

            messageImage.preserveAspect = false; 
        }

        StartCoroutine(ForceRebuildRoutine());
    }
```

Replace it with:

```csharp
    void ApplyTextureAspectFill(Texture2D tex, bool isSticker, float targetRatio)
    {
        // Free any visuals from a previous load cycle on this bubble. The Image still references
        // the old sprite for one more frame; we reassign messageImage.sprite below before yielding,
        // so Unity's end-of-frame Destroy never catches a sprite that's about to be rendered.
        DisposeOwned();
        TrackOwned(tex);

        messageImage.color = Color.white;
        tex.wrapMode = TextureWrapMode.Clamp;

        if (isSticker)
        {
            Sprite spr = TrackOwned(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect));
            messageImage.type = Image.Type.Simple;
            messageImage.sprite = spr;
            fullScreenSprite = spr;
            messageImage.preserveAspect = true;
        }
        else
        {
            float imageRatio = (float)tex.width / tex.height;
            int cropW = tex.width;
            int cropH = tex.height;
            int x = 0;
            int y = 0;

            if (Mathf.Abs(imageRatio - targetRatio) > 0.01f)
            {
                if (imageRatio > targetRatio)
                {
                    cropW = Mathf.RoundToInt(tex.height * targetRatio);
                    x = (tex.width - cropW) / 2;
                }
                else
                {
                    cropH = Mathf.RoundToInt(tex.width / targetRatio);
                    y = (tex.height - cropH) / 2;
                }

                Color[] pixels = tex.GetPixels(x, y, cropW, cropH);
                Texture2D croppedTex = TrackOwned(new Texture2D(cropW, cropH, tex.format, false));
                croppedTex.wrapMode = TextureWrapMode.Clamp;
                croppedTex.SetPixels(pixels);
                croppedTex.Apply();

                Sprite bubbleSpr = TrackOwned(Sprite.Create(croppedTex, new Rect(0, 0, cropW, cropH), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect));
                Sprite fullSpr   = TrackOwned(Sprite.Create(tex,        new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect));

                messageImage.type = Image.Type.Simple;
                messageImage.sprite = bubbleSpr;
                fullScreenSprite = fullSpr;
            }
            else
            {
                Sprite spr = TrackOwned(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect));
                messageImage.type = Image.Type.Simple;
                messageImage.sprite = spr;
                fullScreenSprite = spr;
            }

            messageImage.preserveAspect = false;
        }

        StartCoroutine(ForceRebuildRoutine());
    }
```

- [ ] **Step 2: Compile-check.**

Save the file. Unity should recompile cleanly. The validate-cs.sh hook runs automatically.

- [ ] **Step 3: Commit.**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): route ApplyTextureAspectFill through owned-resource ledger

Every Sprite.Create call site inside the method now tracks its result.
The incoming Texture2D is also tracked (it's the bubble's texture for
this load cycle). DisposeOwned at entry frees prior-cycle visuals.

This is the central sprite-create point — every media bubble flows
through it via SmartMediaRoutine → DownloadSmartHDBytes /
ShowSmartThumbnail / TryDecodeSticker / LoadBase64Image, so this single
change covers the bulk of the texture leak.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Free Texture2Ds when `LoadImage` returns false

When `tex.LoadImage(bytes)` fails (corrupt bytes), the temp `Texture2D` was allocated but never passed to `ApplyTextureAspectFill` — so it's never tracked, and it leaks. Five call sites need an explicit `Destroy(tex)` in the else branch.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (lines 1593, 1672, 2034, 2250, 2305, 2321, 2344, 2350)

- [ ] **Step 1: Cache-intercept path (around line 1593).**

Find:

```csharp
            string filePath = MediaCacheManager.Instance.GetFilePathFromUrl(targetUrl);
            byte[] bytes = System.IO.File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(2, 2);

            if (tex.LoadImage(bytes))
            {
                ApplyTextureAspectFill(tex, false, bubbleRatio);
            }
            else
            {
                // THE FIX: If the cached file is corrupt, trigger the fallback!
                HandleFinalFailure(isManual, false);
            }
```

Replace with:

```csharp
            string filePath = MediaCacheManager.Instance.GetFilePathFromUrl(targetUrl);
            byte[] bytes = System.IO.File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(2, 2);

            if (tex.LoadImage(bytes))
            {
                ApplyTextureAspectFill(tex, false, bubbleRatio);
            }
            else
            {
                Destroy(tex);
                // THE FIX: If the cached file is corrupt, trigger the fallback!
                HandleFinalFailure(isManual, false);
            }
```

- [ ] **Step 2: HD download success path (around line 1672).**

Find:

```csharp
            byte[] imageBytes = www.downloadHandler.data;
            Texture2D tex = new Texture2D(2, 2);
            
            if (tex.LoadImage(imageBytes)) 
            {
                MediaCacheManager.Instance.SaveImageToCache(url, imageBytes);
                ApplyTextureAspectFill(tex, false, bubbleRatio);
            }
            else
            {
                // --- THE FIX: The bytes downloaded, but they are corrupt/not an image! ---
                // Show the download button so the user can try again!
                HandleFinalFailure(isManual, false);
            }
```

Replace with:

```csharp
            byte[] imageBytes = www.downloadHandler.data;
            Texture2D tex = new Texture2D(2, 2);
            
            if (tex.LoadImage(imageBytes)) 
            {
                MediaCacheManager.Instance.SaveImageToCache(url, imageBytes);
                ApplyTextureAspectFill(tex, false, bubbleRatio);
            }
            else
            {
                Destroy(tex);
                // --- THE FIX: The bytes downloaded, but they are corrupt/not an image! ---
                // Show the download button so the user can try again!
                HandleFinalFailure(isManual, false);
            }
```

- [ ] **Step 3: Universal cache intercept (around line 2034).**

Find:

```csharp
                else 
                {
                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, false, bubbleRatio);
                }
```

Replace with:

```csharp
                else 
                {
                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, false, bubbleRatio);
                    else Destroy(tex);
                }
```

- [ ] **Step 4: Manual download retry path (around line 2250).**

Find:

```csharp
        if (vm.isSticker) TryDecodeSticker(bytes, targetRatio);
        else
        {
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, vm.isSticker, targetRatio);
        }
    }
```

Replace with:

```csharp
        if (vm.isSticker) TryDecodeSticker(bytes, targetRatio);
        else
        {
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, vm.isSticker, targetRatio);
            else Destroy(tex);
        }
    }
```

- [ ] **Step 5: WebP-or-Image loader (around line 2305).**

Find:

```csharp
            if (isSticker) TryDecodeSticker(bytes, targetRatio);
            else
            {
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, false, targetRatio);
            }
        }
    }
```

Replace with:

```csharp
            if (isSticker) TryDecodeSticker(bytes, targetRatio);
            else
            {
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes)) ApplyTextureAspectFill(tex, false, targetRatio);
                else Destroy(tex);
            }
        }
    }
```

- [ ] **Step 6: Base64 path (around line 2321).**

Find:

```csharp
            byte[] bytes = Convert.FromBase64String(base64);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes)) 
            {
                ApplyTextureAspectFill(tex, isSticker, targetRatio);
            }
        } catch (Exception e) { 
```

Replace with:

```csharp
            byte[] bytes = Convert.FromBase64String(base64);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes)) 
            {
                ApplyTextureAspectFill(tex, isSticker, targetRatio);
            }
            else
            {
                Destroy(tex);
            }
        } catch (Exception e) { 
```

- [ ] **Step 7: Sticker fallback paths (around lines 2344 and 2350).**

Find the `TryDecodeSticker` method (around line 2331):

```csharp
    void TryDecodeSticker(byte[] rawBytes, float targetRatio)
    {
        try 
        {
            byte[] staticBytes = GetFirstFrameOfWebP(rawBytes);
            Texture2D tex = Texture2DExt.CreateTexture2DFromWebP(staticBytes, true, false, out Error error);
            
            if (error == Error.Success && tex != null) 
            {
                ApplyTextureAspectFill(tex, true, targetRatio);
            }
            else
            {
                Texture2D fallbackTex = new Texture2D(2, 2);
                if (fallbackTex.LoadImage(rawBytes)) ApplyTextureAspectFill(fallbackTex, true, targetRatio);
            }
        } 
        catch (Exception)
        {
            Texture2D fallbackTex = new Texture2D(2, 2);
            if (fallbackTex.LoadImage(rawBytes)) ApplyTextureAspectFill(fallbackTex, true, targetRatio);
        }
    }
```

Replace with:

```csharp
    void TryDecodeSticker(byte[] rawBytes, float targetRatio)
    {
        try 
        {
            byte[] staticBytes = GetFirstFrameOfWebP(rawBytes);
            Texture2D tex = Texture2DExt.CreateTexture2DFromWebP(staticBytes, true, false, out Error error);
            
            if (error == Error.Success && tex != null) 
            {
                ApplyTextureAspectFill(tex, true, targetRatio);
            }
            else
            {
                if (tex != null) Destroy(tex);
                Texture2D fallbackTex = new Texture2D(2, 2);
                if (fallbackTex.LoadImage(rawBytes)) ApplyTextureAspectFill(fallbackTex, true, targetRatio);
                else Destroy(fallbackTex);
            }
        } 
        catch (Exception)
        {
            Texture2D fallbackTex = new Texture2D(2, 2);
            if (fallbackTex.LoadImage(rawBytes)) ApplyTextureAspectFill(fallbackTex, true, targetRatio);
            else Destroy(fallbackTex);
        }
    }
```

- [ ] **Step 8: Compile-check.**

Save. Unity recompiles. validate-cs.sh runs. No errors expected.

- [ ] **Step 9: Commit.**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
fix(messages): destroy orphaned Texture2Ds when LoadImage fails

Six call sites allocated Texture2D before LoadImage, then never freed
the temp when LoadImage returned false. Each leak is small per-event
but compounds over hours of use. Explicit Destroy in the else branch.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Track link-preview texture and sprite

The link-preview path doesn't flow through `ApplyTextureAspectFill`. It creates its own `Texture2D` (from cache or web fetch) and its own `Sprite.Create`. Both leak today.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (around lines 2683, 2700, 2752)

- [ ] **Step 1: Track the downloaded texture.**

Find the existing block (around line 2683):

```csharp
        Texture2D downloadedTex = null;
        
        // Try to load the image IF one was provided
        if (!string.IsNullOrEmpty(scrapedImage))
        {
            // 1. CHECK CACHE FIRST: Do we already have this image saved on the phone?
            downloadedTex = MediaCacheManager.Instance.LoadImageFromCache(scrapedImage);

            // 2. ONLY DOWNLOAD IF MISSING: If it's not on the hard drive, fetch it from the web!
            if (downloadedTex == null)
            {
                using UnityWebRequest www = UnityWebRequest.Get(scrapedImage);
                yield return www.SendWebRequest();
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    byte[] imageBytes = www.downloadHandler.data;
                    downloadedTex = new Texture2D(2, 2);
                    
                    if (downloadedTex.LoadImage(imageBytes))
                    {
                        // 3. SAVE FOR LATER: Write the bytes to the hard drive so we never have to download it again!
                        MediaCacheManager.Instance.SaveImageToCache(scrapedImage, imageBytes);
                    }
                    else
                    {
                        downloadedTex = null;
                    }
                }
            }
        }
```

Replace with:

```csharp
        Texture2D downloadedTex = null;
        
        // Try to load the image IF one was provided
        if (!string.IsNullOrEmpty(scrapedImage))
        {
            // 1. CHECK CACHE FIRST: Do we already have this image saved on the phone?
            downloadedTex = MediaCacheManager.Instance.LoadImageFromCache(scrapedImage);

            // 2. ONLY DOWNLOAD IF MISSING: If it's not on the hard drive, fetch it from the web!
            if (downloadedTex == null)
            {
                using UnityWebRequest www = UnityWebRequest.Get(scrapedImage);
                yield return www.SendWebRequest();
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    byte[] imageBytes = www.downloadHandler.data;
                    downloadedTex = new Texture2D(2, 2);
                    
                    if (downloadedTex.LoadImage(imageBytes))
                    {
                        // 3. SAVE FOR LATER: Write the bytes to the hard drive so we never have to download it again!
                        MediaCacheManager.Instance.SaveImageToCache(scrapedImage, imageBytes);
                    }
                    else
                    {
                        Destroy(downloadedTex);
                        downloadedTex = null;
                    }
                }
            }

            if (downloadedTex != null) TrackOwned(downloadedTex);
        }
```

- [ ] **Step 2: Track the link-preview sprite.**

Find (around line 2749):

```csharp
        if (downloadedTex != null)
        {
            // IMAGE FOUND: Show the image, hide the text link!
            linkPreviewImage.sprite = Sprite.Create(downloadedTex, new Rect(0, 0, downloadedTex.width, downloadedTex.height), new Vector2(0.5f, 0.5f));
            linkPreviewImage.color = Color.white;
            linkPreviewImage.gameObject.SetActive(true);
```

Replace with:

```csharp
        if (downloadedTex != null)
        {
            // IMAGE FOUND: Show the image, hide the text link!
            linkPreviewImage.sprite = TrackOwned(Sprite.Create(downloadedTex, new Rect(0, 0, downloadedTex.width, downloadedTex.height), new Vector2(0.5f, 0.5f)));
            linkPreviewImage.color = Color.white;
            linkPreviewImage.gameObject.SetActive(true);
```

- [ ] **Step 3: Compile-check and commit.**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
fix(messages): track link-preview texture and sprite as owned

Link-preview path doesn't flow through ApplyTextureAspectFill — it
creates its own Texture2D (from cache or fetch) and its own Sprite.
Both now go into the bubble's owned ledger so OnDestroy frees them.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Extend `IsSliding` to cover the swipe-back drag, and add `OnSlideOutComplete`

Today `IsSliding` only flips during `SnapToPosition`. The user's finger-drag portion of swipe-back runs unguarded — heavy work (bubble spawn from late-arriving sync, HD decode completions) can race the drag and drop frames. Also add the `OnSlideOutComplete` event that `MessageListView` will subscribe to in Task 13.

**Files:**
- Modify: `Assets/Scripts/Chat/SwipeToBack.cs`

- [ ] **Step 1: Add the `OnSlideOutComplete` static event.**

Find the existing static declarations (around line 35–42):

```csharp
    /// <summary>
    /// True whenever a slide animation is running (in, out, or swipe-back
    /// snap). Read by MessageItemView.AcquireDecodeSlot and
    /// ChatManager.SyncLatestMessages to pause their heavy main-thread work
    /// during slides — image decode (~30ms each) and JSON parse (~100-300ms)
    /// would otherwise drop frames and make the slide look laggy.
    /// </summary>
    public static bool IsSliding { get; private set; }
```

Insert after it:

```csharp
    public static bool IsSliding { get; private set; }

    /// <summary>
    /// Fires the frame after a slide-out finishes and the chat panel is deactivated.
    /// Subscribers (currently MessageListView) free per-chat state — destroying
    /// bubbles here recovers their owned textures immediately instead of waiting
    /// for the next chat-open to clear them.
    /// </summary>
    public static event System.Action OnSlideOutComplete;
```

- [ ] **Step 2: Flip `IsSliding` true at the start of a horizontal-right drag.**

Find `OnBeginDrag` (around line 140):

```csharp
        if (isMostlyHorizontal && isSwipingRight)
        {
            isHorizontalDrag = true;
            if (snapCoroutine != null) StopCoroutine(snapCoroutine);
            
            if (chatScrollRect != null) chatScrollRect.vertical = false;
            if (chatListPanel) chatListPanel.gameObject.SetActive(true); // Ensure background is visible
        }
```

Add the `IsSliding = true` line:

```csharp
        if (isMostlyHorizontal && isSwipingRight)
        {
            // Lock out swipe-back during the slide-in animation. Mid-tween cancellation
            // looks janky and would add state to SnapToPosition. The slide is brief
            // (~300 ms) so the lockout window is short. Slide-OUT itself is fine to
            // interact with (the user IS the one driving slide-out via drag); we only
            // block when phase is Slide AND the panel is moving in (anchoredPosition.x
            // approaching 0 from screenWidth).
            float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
            bool isSlidingIn = ChatManager.Instance != null
                && ChatManager.Instance.Phase == ChatManager.ChatOpenPhase.Slide
                && chatPanelToSlide.anchoredPosition.x < screenWidth - 1f;
            if (isSlidingIn)
            {
                isHorizontalDrag = false;
                dragDecided = true;
                dragStartTime = Time.unscaledTime;
                dragStartPos = eventData.position;
                return;
            }

            isHorizontalDrag = true;
            IsSliding = true;
            if (snapCoroutine != null) StopCoroutine(snapCoroutine);
            
            if (chatScrollRect != null) chatScrollRect.vertical = false;
            if (chatListPanel) chatListPanel.gameObject.SetActive(true); // Ensure background is visible
        }
```

- [ ] **Step 3: Fire `OnSlideOutComplete` after the slide-out snap finishes.**

Find the existing `triggerBack` branch in `SnapToPosition` (around line 295):

```csharp
        if (triggerBack)
        {
            chatPanelToSlide.gameObject.SetActive(false);
            onSwipeComplete?.Invoke();
        }
        else
        {
            if (chatListPanel != null) chatListPanel.gameObject.SetActive(false);
        }
```

Replace with:

```csharp
        if (triggerBack)
        {
            chatPanelToSlide.gameObject.SetActive(false);
            onSwipeComplete?.Invoke();
            OnSlideOutComplete?.Invoke();
        }
        else
        {
            if (chatListPanel != null) chatListPanel.gameObject.SetActive(false);
        }
```

(`onSwipeComplete` is the existing UnityEvent; `OnSlideOutComplete` is the new static event for code subscribers.)

- [ ] **Step 4: Compile-check and commit.**

```bash
git add Assets/Scripts/Chat/SwipeToBack.cs
git commit -m "$(cat <<'EOF'
feat(chat): extend IsSliding to cover swipe-back drag + add OnSlideOutComplete

IsSliding now flips true at the start of a horizontal-right drag, not
just during the snap. This closes the gap where bubble-spawn and decode
work could race the user's finger-drag portion of swipe-back.

OnSlideOutComplete fires after the slide-out snap finishes and the
chat panel is deactivated. MessageListView will subscribe to it to
destroy bubbles on every back-navigation (frees their owned textures).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Remove unused `PrepareForPrespawn` from `SwipeToBack`

The new phase model never activates the chat panel during Prep — `SwipeToBack.SlideInToMessages` is the only entry point. `PrepareForPrespawn` is dead code (it was part of the abandoned pre-window approach).

**Files:**
- Modify: `Assets/Scripts/Chat/SwipeToBack.cs`

- [ ] **Step 1: Delete the `PrepareForPrespawn` method.**

Find the method (around line 62):

```csharp
    /// <summary>
    /// Activates the chat panel off-screen so its child coroutines (mainly
    /// UpdateListRoutine and SmartMediaRoutine) can run pre-spawn work
    /// before the slide-in animation begins. The panel is also hidden via
    /// CanvasGroup alpha=0 as a belt-and-suspenders against any first-frame
    /// position lag on initial activation.
    /// </summary>
    public void PrepareForPrespawn()
    {
        var cg = chatPanelToSlide.GetComponent<CanvasGroup>();
        if (cg == null) cg = chatPanelToSlide.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;

        if (canvas != null)
        {
            float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
            chatPanelToSlide.anchoredPosition = new Vector2(screenWidth, chatPanelToSlide.anchoredPosition.y);
        }

        if (snapCoroutine != null) StopCoroutine(snapCoroutine);

        chatPanelToSlide.gameObject.SetActive(true);
        if (chatListPanel) chatListPanel.gameObject.SetActive(true);
    }
```

Delete the entire block.

- [ ] **Step 2: Verify no callers remain.**

Run:

```bash
grep -rn "PrepareForPrespawn" Assets/Scripts/
```

Expected: zero matches.

- [ ] **Step 3: Compile-check and commit.**

```bash
git add Assets/Scripts/Chat/SwipeToBack.cs
git commit -m "$(cat <<'EOF'
chore(chat): remove unused PrepareForPrespawn

The new phase model never activates the chat panel during Prep —
SlideInToMessages is the sole entry point that activates it, atomically
with starting the animation. PrepareForPrespawn was left over from the
abandoned pre-window approach.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Add `ChatOpenPhase` state machine and pending buffers to `ChatManager`

The state machine. No behavior change yet — fields and helpers are added for upcoming tasks to consume.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Add the enum at the top of the class (just below the existing constants block).**

Find the section around line 30–48 — after `FirstScreenPointBudget` and `GetMessageTypeWeight`, before `FirstScreenMessageCount`. Insert:

```csharp
    /// <summary>
    /// Three-phase chat-open state machine. Prep runs cache load and queues sync results
    /// without touching UI. Slide is the slide-in animation with all heavy main-thread
    /// work gated. Populate fires OnBatchMessagesLoaded and drains queued sync results.
    /// Idle is the steady state (chat list visible, or chat fully open and settled).
    /// Slide-out is also represented by Idle — IsSliding handles its own gating.
    /// </summary>
    public enum ChatOpenPhase { Idle, Prep, Slide, Populate }

    /// <summary>
    /// Public read-only access to the chat-open phase. Subscribers (MessageListView,
    /// MessageItemView.AcquireDecodeSlot, SyncLatestMessages) gate their heavy work on this.
    /// </summary>
    public ChatOpenPhase Phase => _phase;
    private ChatOpenPhase _phase = ChatOpenPhase.Idle;
```

- [ ] **Step 2: Add the pending buffers and the open-routine handle.**

Find the existing per-chat state block (around line 124–154):

```csharp
    // State
    public int currentPage = 1;
    private string currentChatId;

    /// <summary>
    /// The MessageViewModel list currently powering the open chat's bubbles.
    /// ...
    /// </summary>
    private List<MessageViewModel> _activeChatCache;

    /// <summary>
    /// Cached messages that haven't been rendered yet.
    /// ...
    /// </summary>
    private List<MessageViewModel> _cachedQueue;

    /// <summary>
    /// The in-flight SyncLatestMessages coroutine for the current chat.
    /// ...
    /// </summary>
    private Coroutine _activeSync;
```

Insert after `_activeSync`:

```csharp
    /// <summary>
    /// The in-flight OpenChatRoutine. Held so SelectChat can cancel a Prep-phase open
    /// when the user taps another chat before the 300 ms timer elapses.
    /// </summary>
    private Coroutine _activeOpen;

    /// <summary>
    /// First-screen batch staged during Prep, fired via OnBatchMessagesLoaded at the
    /// start of Populate. Null until Prep populates it; reset on SelectChat.
    /// </summary>
    private List<MessageViewModel> _pendingFirstBatch;

    /// <summary>
    /// Brand-new messages from SyncLatestMessages that arrived before Populate began.
    /// Fired via OnLiveMessagesReceived during Populate, after OnBatchMessagesLoaded.
    /// Null when no queued result is waiting.
    /// </summary>
    private List<MessageViewModel> _pendingLiveSyncMessages;
```

- [ ] **Step 3: Compile-check and commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "$(cat <<'EOF'
feat(chat): add ChatOpenPhase state machine + pending buffers

Introduces the Phase enum + Idle/Prep/Slide/Populate states + pending
buffers for first-batch and queued sync results. No behavior change
yet — SelectChat / OpenChatRoutine / Populate transitions land in the
next task.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Implement `OpenChatRoutine` (cache-present and no-cache paths)

The coroutine that owns Prep. Loads cache from disk, sorts, splits the first screen, queues sync, waits the 300 ms lead-in, then triggers the slide.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Add `OpenChatRoutine` and the `PopulateBubbles` callback. Place them right after `SyncLatestMessages` (around line 693).**

Find the line that closes `SyncLatestMessages` (around line 693):

```csharp
    public void LoadNextPage()
```

Insert above it (after the closing brace of `SyncLatestMessages`):

```csharp
    /// <summary>
    /// Phase A (Prep) of chat-open. Runs cache load + sort + first-screen split synchronously
    /// inside the coroutine, kicks off sync (whose results buffer into _pendingLiveSyncMessages),
    /// then waits until 300 ms has elapsed from tap time before triggering the slide-in animation.
    /// On slide-in completion the callback transitions to Phase C (Populate).
    /// </summary>
    private IEnumerator OpenChatRoutine(string chatId, float tapTime)
    {
        const float PrepDurationSeconds = 0.300f;

        ChatOpenLog("OpenChatRoutine entry (Prep)");

        // On device only — releasing orphaned natives can take 30-80 ms but Prep has the
        // budget. Editor skips this; the cost shows up as iteration friction in play mode.
        if (!Application.isEditor)
        {
            Resources.UnloadUnusedAssets();
        }

        List<MessageViewModel> cachedMessages = ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId);

        // Always load the outbox — populates OutboxStore's in-memory _byChatId map so
        // tap-to-retry's Find() can resolve the tempId, even if the message cache was
        // purged but the outbox file survived.
        var unresolved = Outbox.GetFor(chatId);

        if (cachedMessages != null && cachedMessages.Count > 0)
        {
            // Promote stale-Pending cached messages to Failed for any tempId still in
            // the outbox. An unresolved entry means the in-flight POST from a previous
            // session never completed — without this pass the user would see a phantom
            // clock that never resolves.
            if (unresolved.Count > 0)
            {
                var unresolvedIds = new HashSet<string>();
                foreach (var entry in unresolved) unresolvedIds.Add(entry.tempId);

                foreach (var msg in cachedMessages)
                {
                    if (!msg.isIncoming && unresolvedIds.Contains(msg.messageId))
                        msg.deliveryStatus = DeliveryStatus.Failed;
                }
            }

            cachedMessages.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));
            foreach (var msg in cachedMessages) seenMessageIds.Add(msg.messageId);
            _activeChatCache = cachedMessages;

            int initialCount = FirstScreenMessageCount(cachedMessages);
            if (cachedMessages.Count > initialCount)
            {
                _pendingFirstBatch = cachedMessages.GetRange(0, initialCount);
                _cachedQueue = cachedMessages.GetRange(initialCount, cachedMessages.Count - initialCount);
            }
            else
            {
                _pendingFirstBatch = cachedMessages;
                _cachedQueue = new List<MessageViewModel>();
            }

            // Kick off sync. Its callback fires OnLiveMessagesReceived only after Populate
            // begins (gated by Phase != Populate inside SyncLatestMessages).
            if (_activeSync != null) StopCoroutine(_activeSync);
            _activeSync = StartCoroutine(SyncLatestMessages(chatId, cachedMessages));
        }
        else
        {
            // No cache: kick the network fetch. Its callback writes _pendingFirstBatch
            // and _cachedQueue if the response arrives before slide-in completes; otherwise
            // the slide reveals an empty content and the bubbles land in Populate.
            StartCoroutine(GetMessagesRoutine(chatId, 1, (newMessages, hasMore) =>
            {
                if (chatId != currentChatId) return; // stale fetch — user switched chats

                if (newMessages.Count > 0)
                    ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, newMessages);

                _activeChatCache = newMessages;

                newMessages.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));

                int initialCount = FirstScreenMessageCount(newMessages);
                if (newMessages.Count > initialCount)
                {
                    _pendingFirstBatch = newMessages.GetRange(0, initialCount);
                    _cachedQueue = newMessages.GetRange(initialCount, newMessages.Count - initialCount);
                }
                else
                {
                    _pendingFirstBatch = newMessages;
                    _cachedQueue = new List<MessageViewModel>();
                }

                // If we're already in Populate by the time the fetch returns, fire immediately.
                if (_phase == ChatOpenPhase.Populate)
                {
                    OnBatchMessagesLoaded?.Invoke(_pendingFirstBatch, false, hasMore);
                    _pendingFirstBatch = null;
                }
            }));
        }

        // Wait until 300 ms has elapsed since the tap. If Prep finished early, this is
        // intentional lead-in so the slide doesn't start before the user's eye has had
        // time to register the row tap.
        while (Time.realtimeSinceStartup - tapTime < PrepDurationSeconds)
        {
            yield return null;
        }

        ChatOpenLog("Prep complete, starting slide");
        _phase = ChatOpenPhase.Slide;

        if (SwipeToBack.Instance != null)
        {
            SwipeToBack.Instance.SlideInToMessages(() =>
            {
                ChatOpenLog("Slide-in complete, entering Populate");
                PopulateBubbles(chatId);
            });
        }
        else
        {
            // No SwipeToBack instance (shouldn't happen in production but safe fallback):
            // skip the animation and go straight to Populate.
            MessageListPanel.SetActive(true);
            PopulateBubbles(chatId);
        }
    }

    /// <summary>
    /// Phase C (Populate). Fires OnBatchMessagesLoaded with the staged first batch,
    /// then drains any sync results that landed during Prep or Slide.
    /// </summary>
    private void PopulateBubbles(string chatId)
    {
        if (currentChatId != chatId)
        {
            // User switched chats during the slide. SelectChat already reset state for
            // the new chat — bail out cleanly.
            return;
        }

        _phase = ChatOpenPhase.Populate;

        if (_pendingFirstBatch != null)
        {
            ChatOpenLog($"Fire OnBatchMessagesLoaded ({_pendingFirstBatch.Count} msgs)");
            OnBatchMessagesLoaded?.Invoke(_pendingFirstBatch, false, true);
            _pendingFirstBatch = null;
        }

        if (_pendingLiveSyncMessages != null && _pendingLiveSyncMessages.Count > 0)
        {
            ChatOpenLog($"Drain pending sync ({_pendingLiveSyncMessages.Count} new)");
            OnLiveMessagesReceived?.Invoke(_pendingLiveSyncMessages);
            _pendingLiveSyncMessages = null;
        }
    }

    public void LoadNextPage()
```

- [ ] **Step 2: Compile-check.**

Unity may warn about `OpenChatRoutine` / `PopulateBubbles` being unused — that's expected until Task 10 wires `SelectChat` to call them. Save and verify no actual errors.

- [ ] **Step 3: Commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "$(cat <<'EOF'
feat(chat): implement OpenChatRoutine + PopulateBubbles

OpenChatRoutine owns Phase A (Prep): cache load, sort, first-screen
split, sync kickoff, then a 300 ms wall-clock wait before triggering
SlideInToMessages. PopulateBubbles is the slide-in completion callback
that fires OnBatchMessagesLoaded and drains any sync results that
landed during Prep/Slide.

Not wired into SelectChat yet — next task replaces the existing inline
slide-then-load flow with a call into OpenChatRoutine.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Rewire `SelectChat` to enter Prep and start `OpenChatRoutine`

Replace the current inline "wake panel → fire OnChatSelected → SlideInToMessages → LoadMessagesForChat" with "fire OnChatSelected → set phase → start OpenChatRoutine".

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Replace the body of `SelectChat`.**

Find the existing method (around line 286):

```csharp
    public void SelectChat(string chatId)
    {
        if (ScrollClickBlocker.IsBlocking) return;

        _chatOpenStartTime = Time.realtimeSinceStartup;
        ChatOpenLog("SelectChat (tap registered)");

        // Optimistic local reset — match WhatsApp's instant feel.
        // If the next sync returns a non-zero count, the badge re-appears.
        if (chatLookup.TryGetValue(chatId, out var selectedVm))
        {
            bool hadUnread = selectedVm.UnreadCount > 0;
            selectedVm.UpdateUnreadCount(0);

            // Persist read state to Wappi so the badge does not re-appear on next sync.
            if (hadUnread)
            {
                StartCoroutine(MarkChatAsRead(chatId));
            }
        }

        currentChatId = chatId;
        currentPage = 1;
        seenMessageIds.Clear();

        // --- 1. WAKE UP THE PANEL ---
        // Activate the chat panel so its scripts run OnEnable and subscribe
        // to ChatManager events before OnChatSelected fires.
        if (SwipeToBack.Instance != null && SwipeToBack.Instance.chatPanelToSlide != null)
        {
            SwipeToBack.Instance.chatPanelToSlide.gameObject.SetActive(true);
        }
        else
        {
            MessageListPanel.SetActive(true);
        }

        // --- 2. CLEAR THE OLD CHAT ---
        OnChatSelected?.Invoke(chatId);

        // --- 3. SLIDE FIRST, THEN LOAD ---
        // Sequential. Slide animates with nothing else running on the main
        // thread (IsSliding flag in SnapToPosition pauses decode + sync work),
        // then LoadMessagesForChat fires from the slide-in callback. Trying
        // to pre-spawn during a 250ms pre-window caused a one-frame panel
        // flash on first open and editor frame instability skewed the slide
        // duration. Sequential is the reliable shape.
        if (SwipeToBack.Instance != null)
        {
            SwipeToBack.Instance.SlideInToMessages(() =>
            {
                ChatOpenLog("Slide-in complete");
                LoadMessagesForChat(chatId);
            });
        }
        else
        {
            LoadMessagesForChat(chatId);
        }
    }
```

Replace with:

```csharp
    public void SelectChat(string chatId)
    {
        if (ScrollClickBlocker.IsBlocking) return;

        // Lock out re-taps while the slide-in animation is running. If we allowed a new
        // SelectChat during Slide, the new OpenChatRoutine would later trigger another
        // SlideInToMessages which snaps the panel off-screen — a visible jump while the
        // first slide is still finishing. Slide is brief (~300 ms); the lockout is short.
        if (_phase == ChatOpenPhase.Slide) return;

        float tapTime = Time.realtimeSinceStartup;
        _chatOpenStartTime = tapTime;
        ChatOpenLog("SelectChat (tap registered)");

        // Cancel any in-flight open. If the user re-tapped during Prep we restart from
        // scratch with the new chat. (Slide-phase re-taps are blocked above.)
        if (_activeOpen != null) StopCoroutine(_activeOpen);
        _pendingFirstBatch = null;
        _pendingLiveSyncMessages = null;

        // Optimistic local reset — match WhatsApp's instant feel. If the next sync returns
        // a non-zero count, the badge re-appears.
        if (chatLookup.TryGetValue(chatId, out var selectedVm))
        {
            bool hadUnread = selectedVm.UnreadCount > 0;
            selectedVm.UpdateUnreadCount(0);

            if (hadUnread)
            {
                StartCoroutine(MarkChatAsRead(chatId));
            }
        }

        currentChatId = chatId;
        currentPage = 1;
        seenMessageIds.Clear();
        _activeChatCache = null;
        _cachedQueue = null;

        // Fire OnChatSelected so MessageListView clears its bubbles synchronously. Each
        // destroyed bubble's OnDestroy releases its owned Texture2D + Sprite refs — this
        // is the leak fix's enforcement point.
        OnChatSelected?.Invoke(chatId);

        // Enter Prep. The panel is NOT activated here — SlideInToMessages (inside
        // OpenChatRoutine, after the 300 ms wait) is the sole activation point.
        _phase = ChatOpenPhase.Prep;
        _activeOpen = StartCoroutine(OpenChatRoutine(chatId, tapTime));
    }
```

- [ ] **Step 2: Delete the now-dead `LoadMessagesForChat` method.**

Its job is now split between `OpenChatRoutine` and `PopulateBubbles`. Find the method (around line 348):

```csharp
    // --- NEW: The heavy lifting is now safely isolated here! ---
    private void LoadMessagesForChat(string chatId)
    {
        // ... entire body ...
    }
```

Delete the entire method.

- [ ] **Step 3: Confirm no remaining callers.**

```bash
grep -rn "LoadMessagesForChat" Assets/Scripts/
```

Expected: zero matches.

- [ ] **Step 4: Compile-check and commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "$(cat <<'EOF'
refactor(chat): rewire SelectChat through OpenChatRoutine and phase model

SelectChat now: (1) cancels any in-flight open, (2) resets per-chat
state and fires OnChatSelected (which destroys old bubbles and frees
their textures via OnDestroy), (3) sets Phase = Prep, (4) starts
OpenChatRoutine.

LoadMessagesForChat is gone — its work is split between OpenChatRoutine
(Prep) and PopulateBubbles (Populate). The panel is no longer activated
inside SelectChat; SlideInToMessages does it atomically with the
animation start, which closes the prior pre-window flash bug.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Widen `SyncLatestMessages` gate and buffer its results

`SyncLatestMessages` currently waits on `IsSliding` only. Widen to wait on `_phase != Populate`, and route its `OnLiveMessagesReceived` fire through `_pendingLiveSyncMessages` so a result that arrives during Prep gets queued instead of firing into an empty UI.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Replace the existing slide-wait block in `SyncLatestMessages`.**

Find (around line 497–506):

```csharp
        // Park sync processing while any slide animation is running. The
        // JsonConvert.DeserializeObject + foreach + CreateViewModel pass
        // below costs ~100-300ms on phone hardware — landing it mid-slide
        // would stall the animation by ~5-10 frames. Capped at 500ms so a
        // stuck slide can't block sync indefinitely.
        float syncWaitStart = Time.realtimeSinceStartup;
        while (SwipeToBack.IsSliding && Time.realtimeSinceStartup - syncWaitStart < 0.5f)
        {
            yield return null;
        }

        // Re-check chat-id after the wait — user may have switched chats
        // during the slide we were waiting for.
        if (currentChatId != chatId) yield break;
```

Replace with:

```csharp
        // Park sync processing while the chat-open phase has not yet reached Populate
        // (covers Prep, Slide, plus any future intermediate phases). The
        // JsonConvert.DeserializeObject + foreach + CreateViewModel pass below costs
        // ~100-300ms on phone hardware — landing it during Prep or mid-slide would stall
        // the animation by ~5-10 frames. Capped at 500ms so a stuck phase transition
        // can't block sync indefinitely.
        float syncWaitStart = Time.realtimeSinceStartup;
        while (_phase != ChatOpenPhase.Populate && _phase != ChatOpenPhase.Idle
               && Time.realtimeSinceStartup - syncWaitStart < 0.5f)
        {
            yield return null;
        }

        // Re-check chat-id after the wait — user may have switched chats during the
        // phase we were waiting on.
        if (currentChatId != chatId) yield break;
```

(The `Idle` case covers the post-settle steady state — sync re-fired by a long-running listener would resume cleanly.)

- [ ] **Step 2: Route the `OnLiveMessagesReceived` fire through the pending buffer.**

Find the existing brand-new-messages fire (around line 683):

```csharp
            // Append only the brand-new messages — AppendLiveMessagesRoutine
            // slides them in at the bottom without re-spawning the cached
            // bubbles already on screen.
            ChatOpenLog($"OnLiveMessagesReceived fire ({brandNew.Count} new)");
            OnLiveMessagesReceived?.Invoke(brandNew);
        }
```

Replace with:

```csharp
            // Brand-new messages: queue if we're not yet in Populate (Prep or Slide
            // would spawn into an empty/closing list). Otherwise fire immediately.
            if (_phase == ChatOpenPhase.Populate || _phase == ChatOpenPhase.Idle)
            {
                ChatOpenLog($"OnLiveMessagesReceived fire ({brandNew.Count} new)");
                OnLiveMessagesReceived?.Invoke(brandNew);
            }
            else
            {
                ChatOpenLog($"OnLiveMessagesReceived queued ({brandNew.Count} new, phase={_phase})");
                if (_pendingLiveSyncMessages == null) _pendingLiveSyncMessages = new List<MessageViewModel>();
                _pendingLiveSyncMessages.AddRange(brandNew);
            }
        }
```

- [ ] **Step 3: Compile-check and commit.**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "$(cat <<'EOF'
feat(chat): SyncLatestMessages gates on phase + buffers live results

Wait condition widens from IsSliding to (_phase != Populate && != Idle),
keeping the 500 ms cap. New messages arriving during Prep or Slide go
into _pendingLiveSyncMessages instead of firing OnLiveMessagesReceived
into a not-yet-populated list. PopulateBubbles drains the buffer right
after OnBatchMessagesLoaded so the order stays cache-then-live.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: Widen `AcquireDecodeSlot` gate to include the Prep phase

Image decoders should pause during Prep too, not just during slide. Prep'd bubbles haven't been spawned yet — there's no reason to spend frame budget decoding for a panel that isn't on screen.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (around line 1533)

- [ ] **Step 1: Update `AcquireDecodeSlot`.**

Find:

```csharp
private static IEnumerator AcquireDecodeSlot()
{
    while (true)
    {
        // Pause decodes entirely during slide animations — a 30ms texture
        // decode lands roughly in the middle of every other slide frame and
        // drops the animation to ~25fps. Resumes the instant the slide
        // releases the gate.
        if (SwipeToBack.IsSliding)
        {
            yield return null;
            continue;
        }

        int currentFrame = Time.frameCount;
        if (currentFrame != _decodeFrame)
        {
            _decodeFrame = currentFrame;
            _decodesThisFrame = 0;
        }
        if (_decodesThisFrame < MaxDecodesPerFrame)
        {
            _decodesThisFrame++;
            yield break;
        }
        yield return null;
    }
}
```

Replace with:

```csharp
private static IEnumerator AcquireDecodeSlot()
{
    while (true)
    {
        // Pause decodes entirely during slide animations and during the Prep phase.
        // During Prep the panel isn't visible — decoding now is wasted work that may
        // be cancelled if the user re-taps. During Slide a 30ms texture decode lands
        // mid-tween frame and drops the animation framerate.
        bool inSlide = SwipeToBack.IsSliding;
        bool inPrep = ChatManager.Instance != null && ChatManager.Instance.Phase == ChatManager.ChatOpenPhase.Prep;
        if (inSlide || inPrep)
        {
            yield return null;
            continue;
        }

        int currentFrame = Time.frameCount;
        if (currentFrame != _decodeFrame)
        {
            _decodeFrame = currentFrame;
            _decodesThisFrame = 0;
        }
        if (_decodesThisFrame < MaxDecodesPerFrame)
        {
            _decodesThisFrame++;
            yield break;
        }
        yield return null;
    }
}
```

- [ ] **Step 2: Compile-check and commit.**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(messages): AcquireDecodeSlot also pauses during Prep phase

During Prep the chat panel isn't on screen — running texture decodes
now is wasted work that may be discarded if the user re-taps another
chat before the slide starts. Decodes resume the instant the phase
transitions to Slide (where the IsSliding gate already holds) and then
to Populate.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 13: Subscribe `MessageListView` to `OnSlideOutComplete`

When the user swipes back, destroy the bubbles immediately so their textures get freed. Today they live until the next `OnChatSelected` fires.

**Files:**
- Modify: `Assets/Scripts/UI/MessageListView.cs`

- [ ] **Step 1: Subscribe in `OnEnable`.**

Find the existing `OnEnable` (around line 56):

```csharp
    void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected += OnChatSelected;
            ChatManager.Instance.OnBatchMessagesLoaded += HandleBatchMessages;
            ChatManager.Instance.OnLiveMessagesReceived += HandleLiveMessages;
        }
        
        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScroll);
        }
        
        if (loadingMessagesSpinner)
        {
            loadingMessagesSpinner.SetActive(false);
        }
    }
```

Add the new subscription:

```csharp
    void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected += OnChatSelected;
            ChatManager.Instance.OnBatchMessagesLoaded += HandleBatchMessages;
            ChatManager.Instance.OnLiveMessagesReceived += HandleLiveMessages;
        }

        SwipeToBack.OnSlideOutComplete += HandleSlideOutComplete;

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScroll);
        }
        
        if (loadingMessagesSpinner)
        {
            loadingMessagesSpinner.SetActive(false);
        }
    }
```

- [ ] **Step 2: Unsubscribe in `OnDisable`.**

Find the existing `OnDisable` (around line 76):

```csharp
    void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected -= OnChatSelected;
            ChatManager.Instance.OnBatchMessagesLoaded -= HandleBatchMessages;
            ChatManager.Instance.OnLiveMessagesReceived -= HandleLiveMessages;
        }
        
        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScroll);
        }
    }
```

Add the unsubscribe:

```csharp
    void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected -= OnChatSelected;
            ChatManager.Instance.OnBatchMessagesLoaded -= HandleBatchMessages;
            ChatManager.Instance.OnLiveMessagesReceived -= HandleLiveMessages;
        }

        SwipeToBack.OnSlideOutComplete -= HandleSlideOutComplete;

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScroll);
        }
    }
```

- [ ] **Step 3: Add the handler.**

Insert just before `void OnChatSelected(string chatId)` (around line 91):

```csharp
    /// <summary>
    /// Fires from SwipeToBack after a slide-out snap finishes. Destroys all spawned
    /// bubbles immediately — each MessageItemView.OnDestroy frees its owned textures
    /// and sprites, so the memory of the chat the user just left is recovered now
    /// rather than waiting for the next chat open to clear it.
    /// </summary>
    void HandleSlideOutComplete()
    {
        StopAllCoroutines();
        Clear();
        activeChatId = null;
        isInitialLoadInProgress = false;
        pendingLiveMessages.Clear();
    }

    void OnChatSelected(string chatId)
```

- [ ] **Step 4: Compile-check and commit.**

```bash
git add Assets/Scripts/UI/MessageListView.cs
git commit -m "$(cat <<'EOF'
feat(messages): destroy bubbles on slide-out complete

Subscribes to SwipeToBack.OnSlideOutComplete. When the user swipes back
to the chat list, after the slide-out snap finishes, every bubble's
GameObject is destroyed — which triggers its OnDestroy and frees its
owned Texture2D + Sprite refs. Memory of the just-exited chat is
recovered immediately instead of leaking until the next chat open.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 14: Replace `isInitialLoadInProgress` with phase-based gating

The local flag was a stand-in for what the phase model now expresses globally. Replace it.

**Files:**
- Modify: `Assets/Scripts/UI/MessageListView.cs`

- [ ] **Step 1: Update `HandleLiveMessages`.**

Find (around line 182):

```csharp
    // --- UPDATED: Route live messages to the dedicated appending routine ---
    void HandleLiveMessages(List<MessageViewModel> newMessages)
    {
        if (newMessages == null || newMessages.Count == 0) return;

        // If the initial cache UpdateListRoutine is still spawning bubbles,
        // queue these and drain after it completes. Running both routines
        // in parallel made batch 2 settle ~280ms instead of ~120ms because
        // AppendLiveMessagesRoutine's synchronous ForceRebuildLayoutImmediate
        // raced UpdateListRoutine's per-batch rebuild.
        if (isInitialLoadInProgress)
        {
            pendingLiveMessages.AddRange(newMessages);
            return;
        }

        var sortedMessages = newMessages.OrderBy(x => x.timestamp).ToList();

        // Use the new appending routine instead of the batch loading routine!
        StartCoroutine(AppendLiveMessagesRoutine(sortedMessages));
    }
```

Replace with:

```csharp
    // --- UPDATED: Route live messages to the dedicated appending routine ---
    void HandleLiveMessages(List<MessageViewModel> newMessages)
    {
        if (newMessages == null || newMessages.Count == 0) return;

        // If the initial cache UpdateListRoutine is still spawning bubbles, queue these
        // and drain after it completes. Running both routines in parallel made batch 2
        // settle ~280ms instead of ~120ms because AppendLiveMessagesRoutine's synchronous
        // ForceRebuildLayoutImmediate raced UpdateListRoutine's per-batch rebuild.
        //
        // The Phase check in ChatManager.SyncLatestMessages already filters out Prep/Slide
        // arrivals (they get queued in _pendingLiveSyncMessages and drained by
        // PopulateBubbles AFTER OnBatchMessagesLoaded). isInitialLoadInProgress here
        // covers the in-process window from "OnBatchMessagesLoaded fired" to
        // "UpdateListRoutine actually finished spawning everything" — which the phase
        // model alone does NOT cover because phase becomes Populate at the start of
        // OnBatchMessagesLoaded, not the end of UpdateListRoutine.
        if (isInitialLoadInProgress)
        {
            pendingLiveMessages.AddRange(newMessages);
            return;
        }

        var sortedMessages = newMessages.OrderBy(x => x.timestamp).ToList();
        StartCoroutine(AppendLiveMessagesRoutine(sortedMessages));
    }
```

(No behavior change in `HandleLiveMessages` itself — `isInitialLoadInProgress` still gates the in-process spawn window. The phase model covers Prep/Slide upstream. Documenting WHY both gates exist.)

- [ ] **Step 2: Compile-check and commit.**

```bash
git add Assets/Scripts/UI/MessageListView.cs
git commit -m "$(cat <<'EOF'
docs(messages): clarify dual gating between phase model and isInitialLoadInProgress

The phase check in ChatManager.SyncLatestMessages already buffers
Prep/Slide arrivals. isInitialLoadInProgress covers the orthogonal
window from 'OnBatchMessagesLoaded fired' to 'UpdateListRoutine done
spawning'. Both gates are load-bearing; comment explains why.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 15: Smoke-test the full flow in Unity Editor

Manual verification pass against the spec's acceptance criteria. Not a code task — execute against a running Unity Editor.

**Files:**
- None modified.

- [ ] **Step 1: Open the Unity Editor.**

Load `Assets/Scenes/Main.unity`. Confirm zero compile errors in the Console.

- [ ] **Step 2: Enter Play mode and sign in to a bot with chat history.**

Hit Play. Wait for chat list to populate.

- [ ] **Step 3: Open a text-only chat. Watch the Console.**

Expected: `ChatOpenLog` lines show `SelectChat (tap registered)` → `OpenChatRoutine entry (Prep)` → `Prep complete, starting slide` (roughly 300 ms from tap) → `Slide-in complete, entering Populate` → `Fire OnBatchMessagesLoaded`. Slide animation should be smooth.

- [ ] **Step 4: Swipe back. Re-open the same chat. Re-swipe. Repeat 3 times.**

Expected: each open feels identical. Each slide is smooth. No console errors. `SwipeToBack.OnSlideOutComplete` fires and `MessageListView` clears bubbles each time.

- [ ] **Step 5: Open a media-heavy chat. Swipe back. Repeat 10 times.**

Expected: no crash. No "Out of memory" warnings. Bubbles appear progressively after each slide.

Open Window → Analysis → Profiler if it's not already open. Watch the Memory view (Detailed mode). Take a sample at the chat list, then a sample after opening + closing the heavy-media chat 5 times. Texture memory should return to baseline between opens (within a few MB tolerance for ChatHistoryCache and the disk-cache file handles).

- [ ] **Step 6: Rapid re-tap test. Tap Chat A, then immediately tap Chat B within 100 ms.**

Expected: Chat B opens cleanly with its slide. Chat A's bubbles never appear. Console shows the second `OpenChatRoutine entry` aborts/restarts the first.

- [ ] **Step 7: Swipe-back-during-slide test. Open a chat, then immediately attempt to swipe back during the slide-in animation.**

Expected: the swipe input is ignored until slide-in finishes. Slide-in completes, bubbles paint, then a swipe-back works normally. (Per spec: simpler-than-mid-tween-cancellation behavior.)

- [ ] **Step 8: Document any deviations from expected behavior.**

Note them in a scratch text file. If the implementation is solid, move on. If something is off, file as a bug and iterate.

- [ ] **Step 9: Stop the editor. No commit (no code changes this task).**

---

## Task 16: Device build + memory profile UAT

Final pre-merge gate. The editor catches functional bugs; the device catches memory bugs. The spec's primary crash report is device-only.

**Files:**
- None modified.

- [ ] **Step 1: Build for the target device.**

Run:

```bash
Unity -batchmode -nographics -projectPath . -buildTarget Android -quit
```

(Or use Unity Hub → Build Settings → Build And Run for an interactive build.)

- [ ] **Step 2: Install on the test device used for the original repro (the one that crashed at 6–9 opens).**

Sideload the APK (Android) or install via Xcode (iOS).

- [ ] **Step 3: Reproduce the prior crash scenario.**

Open the heavy-media chat 10 times in a row, swiping back between each. Watch the device's memory indicator (Android: Settings → Developer Options → Running services, or use `adb shell dumpsys meminfo <package>`).

Expected: app does not crash. Memory growth across 10 opens stays under ~50 MB net (some growth is expected from chat-list cache and disk-cache file handles; what we're checking is no unbounded growth from leaked textures).

- [ ] **Step 4: General-use UAT.**

Spend 5 minutes using the app normally — open chats, scroll, send messages, swipe back, switch bots. Look for:
- Crashes
- Visible pink boxes / missing sprites (would indicate a destroy-too-early bug)
- Stuck loading spinners
- Late-arriving live messages that don't render

- [ ] **Step 5: Final commit (if any tweaks needed) and ship.**

If everything checks out, the branch is ready to merge. If issues surfaced, file follow-up tasks against the relevant earlier task and iterate.

```bash
# Only if changes were needed during UAT
git add -A
git commit -m "$(cat <<'EOF'
fix(chat): <specific UAT-driven fix>

Discovered during device UAT for chat-open phase model.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```
