# Attach Sheet Part B — Preview Screen + Caption + Optimistic Staging — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After `AttachSheet.OnPicked` fires, present a full-screen preview/caption screen; on Send, stage an in-memory-only `Pending` bubble in the active chat via a new `ChatManager.StageLocalMedia` method. No persistence, no Wappi upload — those are part "c".

**Architecture:** A `AttachmentPreviewScreen` MonoBehaviour subscribes to `AttachSheet.OnPicked`. It owns three kind-specific panels (Image / Video / Document) and a caption + Send / Back bottom bar. On Send it calls `ChatManager.StageLocalMedia(pick, caption)`, which pre-seeds `MediaCacheManager` with a synthetic `staged://...` URL so existing bubble views render the staged message unchanged, then fires `OnLiveMessagesReceived` only — no `ChatHistoryCache` / `Outbox` writes. The screen is built by an editor menu item following the existing `AttachSheetBuilder` pattern.

**Tech Stack:** Unity 6 (6000.3.9f1), URP, TMPro, DOTween, NUnit (editor tests), `NativeGallery` plugin (`Assets/Plugins/`), existing `DeferredDismissInputField` + `KeyboardAwarePanel` components, existing `MediaCacheManager` singleton.

**Spec:** [docs/superpowers/specs/2026-05-28-attach-sheet-preview-caption-design.md](../specs/2026-05-28-attach-sheet-preview-caption-design.md)

---

## File Structure

**New files**

- `Assets/Scripts/Chat/AttachmentDisplayFormat.cs` — pure static helpers (`HumanReadableBytes`, `ShortMime`). NUnit-testable, no Unity dependencies beyond `System`.
- `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` — MonoBehaviour controller for the preview screen.
- `Assets/Editor/AttachmentPreviewScreenBuilder.cs` — `[MenuItem]` editor builder that constructs the screen hierarchy and wires `[SerializeField]` refs. Idempotent.
- `Assets/Tests/Editor/Chat/AttachmentDisplayFormatTests.cs` — NUnit parametric tests for the format helpers.

**Modified files**

- `Assets/Scripts/Main/ChatManager.cs` — add `StageLocalMedia(AttachmentPick, string)` public method + four private helpers (`SeedImageCache`, `SeedVideoThumbCache`, `ReadImageAspect`, `ReadVideoMetadata`). Placed in the same region as `SendTextMessage` (around line 1312).

**Untouched (intentionally — verify after each task that these files are NOT modified)**

- `Assets/Scripts/Chat/AttachSheet.cs`
- `Assets/Scripts/Chat/MessagesBottomPanel.cs`
- `Assets/Scripts/UI/MessageItemView.cs`
- `Assets/Scripts/Chat/MediaCacheManager.cs`

---

## Task 1: Format helpers + tests (TDD)

Pure, no Unity deps. TDD-able.

**Files:**
- Create: `Assets/Scripts/Chat/AttachmentDisplayFormat.cs`
- Create: `Assets/Tests/Editor/Chat/AttachmentDisplayFormatTests.cs`

- [ ] **Step 1.1: Write the failing tests**

Create `Assets/Tests/Editor/Chat/AttachmentDisplayFormatTests.cs`:

```csharp
using NUnit.Framework;

public class AttachmentDisplayFormatTests
{
    // ── HumanReadableBytes ────────────────────────────────────────

    [TestCase(0L,           "<1 KB")]
    [TestCase(512L,         "<1 KB")]
    [TestCase(1023L,        "<1 KB")]
    [TestCase(1024L,        "1 KB")]
    [TestCase(1500L,        "1 KB")]
    [TestCase(10240L,       "10 KB")]
    [TestCase(1048576L,     "1.0 MB")]
    [TestCase(1500000L,     "1.4 MB")]
    [TestCase(15728640L,    "15.0 MB")]
    [TestCase(1073741824L,  "1.0 GB")]
    [TestCase(1610612736L,  "1.5 GB")]
    public void HumanReadableBytes_Returns_Expected(long bytes, string expected)
    {
        Assert.AreEqual(expected, AttachmentDisplayFormat.HumanReadableBytes(bytes));
    }

    [TestCase(-1L,          "<1 KB")]
    [TestCase(long.MinValue, "<1 KB")]
    public void HumanReadableBytes_NegativeOrZero_ReturnsLessThanOneKb(long bytes, string expected)
    {
        Assert.AreEqual(expected, AttachmentDisplayFormat.HumanReadableBytes(bytes));
    }

    // ── ShortMime ─────────────────────────────────────────────────

    [TestCase(null,                                                                                  "")]
    [TestCase("",                                                                                    "")]
    [TestCase("no-slash",                                                                            "")]
    [TestCase("application/pdf",                                                                     "PDF")]
    [TestCase("image/jpeg",                                                                          "JPEG")]
    [TestCase("image/png",                                                                           "PNG")]
    [TestCase("video/mp4",                                                                           "MP4")]
    [TestCase("video/quicktime",                                                                     "QUICKTIME")]
    [TestCase("text/plain",                                                                          "PLAIN")]
    [TestCase("application/zip",                                                                     "ZIP")]
    [TestCase("application/msword",                                                                  "MSWORD")]
    [TestCase("application/vnd.openxmlformats-officedocument.wordprocessingml.document",             "DOCX")]
    [TestCase("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",                   "XLSX")]
    public void ShortMime_Returns_Expected(string mime, string expected)
    {
        Assert.AreEqual(expected, AttachmentDisplayFormat.ShortMime(mime));
    }
}
```

- [ ] **Step 1.2: Run tests to verify they fail**

In the Unity editor: `Window > General > Test Runner > EditMode > Run All`.
Expected: 24 tests, all FAIL with `AttachmentDisplayFormat does not exist in the current context` or similar compile error.

- [ ] **Step 1.3: Implement `AttachmentDisplayFormat`**

Create `Assets/Scripts/Chat/AttachmentDisplayFormat.cs`:

```csharp
using System.Globalization;

public static class AttachmentDisplayFormat
{
    private const long KB = 1024L;
    private const long MB = KB * 1024L;
    private const long GB = MB * 1024L;

    public static string HumanReadableBytes(long bytes)
    {
        if (bytes < KB) return "<1 KB";
        if (bytes < MB) return $"{bytes / KB} KB";
        if (bytes < GB) return ((double)bytes / MB).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return ((double)bytes / GB).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    public static string ShortMime(string mime)
    {
        if (string.IsNullOrEmpty(mime)) return "";
        int slash = mime.LastIndexOf('/');
        if (slash < 0 || slash == mime.Length - 1) return "";

        string suffix = mime.Substring(slash + 1);

        // Compatibility overrides for the Office Open XML long-form MIMEs.
        if (suffix.Equals("vnd.openxmlformats-officedocument.wordprocessingml.document",
                          System.StringComparison.OrdinalIgnoreCase)) return "DOCX";
        if (suffix.Equals("vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                          System.StringComparison.OrdinalIgnoreCase)) return "XLSX";

        return suffix.ToUpperInvariant();
    }
}
```

- [ ] **Step 1.4: Run tests to verify they pass**

In Unity Test Runner: `Run All`.
Expected: 24 tests, all PASS.

- [ ] **Step 1.5: Commit**

```bash
git add Assets/Scripts/Chat/AttachmentDisplayFormat.cs Assets/Scripts/Chat/AttachmentDisplayFormat.cs.meta \
        Assets/Tests/Editor/Chat/AttachmentDisplayFormatTests.cs Assets/Tests/Editor/Chat/AttachmentDisplayFormatTests.cs.meta
git commit -m "feat(chat): AttachmentDisplayFormat helpers + NUnit tests

Pure static helpers for the preview screen's document tile:
HumanReadableBytes (B/KB/MB/GB) and ShortMime (suffix uppercased,
with DOCX/XLSX overrides for Office Open XML long-form MIMEs)."
```

---

## Task 2: `ChatManager.StageLocalMedia` + helpers

Adds the optimistic-staging surface. No tests — this method orchestrates the singleton `MediaCacheManager` + `NativeGallery` calls and fires a Unity event. Verification is via the end-to-end smoke test in Task 5.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (around line 1312, near `SendTextMessage`)

- [ ] **Step 2.1: Locate the insertion point**

Open `Assets/Scripts/Main/ChatManager.cs`. Find the `SendTextMessage` method (around line 1312). The new `StageLocalMedia` block goes immediately AFTER `PostTextMessageRoutine` ends (around line 1455 — verify by reading the file).

- [ ] **Step 2.2: Add the public `StageLocalMedia` method**

Insert this block after `PostTextMessageRoutine`:

```csharp
/// <summary>
/// Part "b" optimistic-staging for media attachments. Builds a
/// MessageViewModel from the AttachmentPick + caption, pre-seeds the
/// image/video thumbnail into MediaCacheManager under a synthetic
/// "staged://" URL so existing bubble views render unchanged, then
/// fires OnLiveMessagesReceived. Does NOT persist (no ChatHistoryCache,
/// no Outbox) and does NOT upload to Wappi — part "c" replaces this body
/// with the real upload + persist path.
/// </summary>
public void StageLocalMedia(AttachmentPick pick, string caption)
{
    if (string.IsNullOrEmpty(currentChatId)) return;
    if (pick == null || string.IsNullOrEmpty(pick.Path)) return;

    string tempId = "staging_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    seenMessageIds.Add(tempId);

    var vm = new MessageViewModel
    {
        messageId      = tempId,
        chatId         = currentChatId,
        senderName     = "Me",
        isIncoming     = false,
        timestamp      = now,
        text           = caption ?? "",
        mimeType       = pick.MimeType,
        fileName       = pick.FileName,
        fileSize       = pick.FileSizeBytes,
        deliveryStatus = DeliveryStatus.Pending,
    };

    switch (pick.Kind)
    {
        case AttachmentKind.Photo:
        case AttachmentKind.GalleryImage:
            vm.type        = MessageType.Image;
            vm.mediaUrl    = SeedImageCache(pick.Path, tempId);
            vm.aspectRatio = ReadImageAspect(pick.Path);
            break;

        case AttachmentKind.GalleryVideo:
            vm.type        = MessageType.Video;
            vm.mediaUrl    = SeedVideoThumbCache(pick.Path, tempId);
            vm.videoUrl    = "file://" + pick.Path;
            var meta = ReadVideoMetadata(pick.Path);
            vm.aspectRatio = meta.aspect;
            vm.duration    = meta.durationSec;
            break;

        case AttachmentKind.Document:
            vm.type = MessageType.Document;
            // No mediaUrl/videoUrl — document bubble uses fileName + fileSize + mimeType.
            break;
    }

    OnLiveMessagesReceived?.Invoke(new System.Collections.Generic.List<MessageViewModel> { vm });
}

private string SeedImageCache(string localPath, string tempId)
{
    string syntheticUrl = $"staged://image/{tempId}";
    try
    {
        byte[] bytes = System.IO.File.ReadAllBytes(localPath);
        MediaCacheManager.Instance.SaveImageToCache(syntheticUrl, bytes);
    }
    catch (System.Exception ex)
    {
        Debug.LogWarning($"[ChatManager] SeedImageCache failed for {localPath}: {ex.Message}");
    }
    return syntheticUrl;
}

private string SeedVideoThumbCache(string localPath, string tempId)
{
    string syntheticUrl = $"staged://thumb/{tempId}";
    Texture2D thumb = null;
    try
    {
        thumb = NativeGallery.GetVideoThumbnail(localPath);
        if (thumb == null) return syntheticUrl;
        byte[] png = thumb.EncodeToPNG();
        MediaCacheManager.Instance.SaveImageToCache(syntheticUrl, png);
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

private float ReadImageAspect(string path)
{
    Texture2D tex = null;
    try
    {
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        tex = new Texture2D(2, 2);
        if (!tex.LoadImage(bytes)) return 1.0f;
        return tex.height > 0 ? (float)tex.width / tex.height : 1.0f;
    }
    catch { return 1.0f; }
    finally
    {
        if (tex != null) UnityEngine.Object.Destroy(tex);
    }
}

private (float aspect, int durationSec) ReadVideoMetadata(string path)
{
    try
    {
        var props = NativeGallery.GetVideoProperties(path);
        float aspect = props.height > 0 ? (float)props.width / props.height : 1.0f;
        int durationSec = (int)(props.duration / 1000);
        return (aspect, durationSec);
    }
    catch { return (1.0f, 0); }
}
```

- [ ] **Step 2.3: Verify ChatManager compiles**

Switch to the Unity editor. Wait for the auto-compile (bottom-right spinner). Console must show zero errors.

If errors appear:
- `AttachmentPick / AttachmentKind not found` → ensure Task 1 commit landed and the assembly has refreshed; both types live in `Assets/Scripts/Chat/AttachmentPick.cs` already (from part a).
- `seenMessageIds / OnLiveMessagesReceived not found` → verify the method body landed inside the `ChatManager` class (look for the closing brace just below it).

- [ ] **Step 2.4: Smoke-test from a temporary editor menu**

Create a throwaway editor script for a one-tap smoke test. This file gets DELETED in step 2.6 — it is not part of the shipped code.

Create `Assets/Editor/_TempStageLocalMediaSmoke.cs`:

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class _TempStageLocalMediaSmoke
{
    [MenuItem("Tools/Attach Sheet/_DEBUG Stage Sample Document")]
    public static void StageSampleDocument()
    {
        if (ChatManager.Instance == null) { Debug.LogError("Need Play Mode."); return; }
        var pick = new AttachmentPick
        {
            Kind          = AttachmentKind.Document,
            Path          = "/tmp/sample.pdf",
            FileName      = "sample.pdf",
            MimeType      = "application/pdf",
            FileSizeBytes = 1_500_000
        };
        ChatManager.Instance.StageLocalMedia(pick, "test caption");
        Debug.Log("[smoke] StageLocalMedia called.");
    }
}
#endif
```

- [ ] **Step 2.5: Run the smoke test**

In the Unity editor:
1. Open `Assets/Scenes/Main.unity` if not already open.
2. Press Play.
3. Sign in / select an active bot if needed so a chat list loads.
4. Open any chat (so `currentChatId` is set).
5. Menu: `Tools > Attach Sheet > _DEBUG Stage Sample Document`.

Expected:
- Console logs `[smoke] StageLocalMedia called.`
- A document bubble (file icon, filename `sample.pdf`, size `1.4 MB`) appears at the bottom of the chat with caption "test caption".
- Bubble has the Pending delivery-status indicator (spinning clock tick).
- Switching chats / bots and returning: bubble is GONE (in-memory only).

If bubble does not appear: check console for `[ChatManager]` warnings and verify `OnLiveMessagesReceived` has a subscriber (`MessageListView` should be live when a chat is open).

- [ ] **Step 2.6: Remove the temporary smoke test**

```bash
rm Assets/Editor/_TempStageLocalMediaSmoke.cs
rm Assets/Editor/_TempStageLocalMediaSmoke.cs.meta 2>/dev/null || true
```

- [ ] **Step 2.7: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): ChatManager.StageLocalMedia for in-memory media staging

Adds optimistic-only staging path for picked attachments. Pre-seeds
MediaCacheManager under staged://image/{tempId} (and staged://thumb/{...}
for video first-frame) so existing bubble views render unchanged, then
fires OnLiveMessagesReceived. No ChatHistoryCache / Outbox writes, no
Wappi upload — part b only. Part c replaces this body with the real
upload + persistence path."
```

---

## Task 3: `AttachmentPreviewScreen` MonoBehaviour

The screen controller. No unit tests — pure Unity UI work. Verification is via the smoke test at the end of this task plus the end-to-end test in Task 5.

**Files:**
- Create: `Assets/Scripts/Chat/AttachmentPreviewScreen.cs`

- [ ] **Step 3.1: Create the script**

Create `Assets/Scripts/Chat/AttachmentPreviewScreen.cs`:

```csharp
using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AttachmentPreviewScreen : MonoBehaviour
{
    [Serializable]
    public struct MimeIconEntry
    {
        public string mimePrefix;
        public Sprite sprite;
    }

    [Header("References — wired by AttachmentPreviewScreenBuilder")]
    [SerializeField] private AttachSheet attachSheet;
    [SerializeField] private GameObject  root;
    [SerializeField] private CanvasGroup rootCanvasGroup;
    [SerializeField] private GameObject  imagePanel;
    [SerializeField] private GameObject  videoPanel;
    [SerializeField] private GameObject  documentPanel;
    [SerializeField] private RawImage    imagePreview;
    [SerializeField] private RawImage    videoPreview;
    [SerializeField] private GameObject  videoPlayOverlay;
    [SerializeField] private GameObject  videoDurationBadge;
    [SerializeField] private TextMeshProUGUI videoDurationLabel;
    [SerializeField] private TextMeshProUGUI documentFileName;
    [SerializeField] private TextMeshProUGUI documentFileSize;
    [SerializeField] private Image       documentIcon;
    [SerializeField] private DeferredDismissInputField captionField;
    [SerializeField] private Button      sendButton;
    [SerializeField] private Button      backButton;

    [Header("MIME → icon mapping (first prefix match wins)")]
    [SerializeField] private List<MimeIconEntry> mimeIcons = new List<MimeIconEntry>();
    [SerializeField] private Sprite documentFallbackIcon;

    [Header("Tween")]
    [SerializeField] private float fadeDuration = 0.18f;

    private AttachmentPick _currentPick;
    private Texture2D      _currentPreviewTexture;
    private Tween          _fadeTween;

    void Awake()
    {
        if (root != null) root.SetActive(false);
        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha          = 0f;
            rootCanvasGroup.interactable   = false;
            rootCanvasGroup.blocksRaycasts = false;
        }
    }

    void OnEnable()
    {
        if (attachSheet != null) attachSheet.OnPicked += Show;
        if (sendButton  != null) sendButton.onClick.AddListener(OnSendTapped);
        if (backButton  != null) backButton.onClick.AddListener(OnBackTapped);
    }

    void OnDisable()
    {
        if (attachSheet != null) attachSheet.OnPicked -= Show;
        if (sendButton  != null) sendButton.onClick.RemoveListener(OnSendTapped);
        if (backButton  != null) backButton.onClick.RemoveListener(OnBackTapped);

        _fadeTween?.Kill();
        ReleasePreviewTexture();
        _currentPick = null;
    }

    public void Show(AttachmentPick pick)
    {
        if (pick == null) return;
        _currentPick = pick;

        ReleasePreviewTexture();
        if (imagePanel    != null) imagePanel.SetActive(false);
        if (videoPanel    != null) videoPanel.SetActive(false);
        if (documentPanel != null) documentPanel.SetActive(false);

        switch (pick.Kind)
        {
            case AttachmentKind.Photo:
            case AttachmentKind.GalleryImage:
                PopulateImagePanel(pick);
                break;
            case AttachmentKind.GalleryVideo:
                PopulateVideoPanel(pick);
                break;
            case AttachmentKind.Document:
                PopulateDocumentPanel(pick);
                break;
        }

        if (captionField != null) captionField.text = "";
        if (sendButton   != null) sendButton.interactable = true;

        if (root != null) root.SetActive(true);
        FadeTo(1f, blocksRaycasts: true);
    }

    private void PopulateImagePanel(AttachmentPick pick)
    {
        if (imagePanel == null || imagePreview == null) return;
        imagePanel.SetActive(true);

        _currentPreviewTexture = LoadTextureFromFile(pick.Path);
        imagePreview.texture = _currentPreviewTexture;
    }

    private void PopulateVideoPanel(AttachmentPick pick)
    {
        if (videoPanel == null || videoPreview == null) return;
        videoPanel.SetActive(true);

        Texture2D thumb = null;
        try { thumb = NativeGallery.GetVideoThumbnail(pick.Path); }
        catch (Exception ex) { Debug.LogWarning($"[AttachmentPreviewScreen] thumb extract failed: {ex.Message}"); }

        _currentPreviewTexture = thumb;
        videoPreview.texture = thumb;

        int durationSec = 0;
        try
        {
            var props = NativeGallery.GetVideoProperties(pick.Path);
            durationSec = (int)(props.duration / 1000);
        }
        catch { durationSec = 0; }

        if (videoPlayOverlay != null) videoPlayOverlay.SetActive(true);
        if (videoDurationBadge != null) videoDurationBadge.SetActive(durationSec > 0);
        if (videoDurationLabel != null && durationSec > 0)
            videoDurationLabel.text = $"{durationSec / 60:D1}:{durationSec % 60:D2}";
    }

    private void PopulateDocumentPanel(AttachmentPick pick)
    {
        if (documentPanel == null) return;
        documentPanel.SetActive(true);

        if (documentFileName != null) documentFileName.text = pick.FileName ?? "";

        string sizeText = AttachmentDisplayFormat.HumanReadableBytes(pick.FileSizeBytes);
        string mimeText = AttachmentDisplayFormat.ShortMime(pick.MimeType);
        if (documentFileSize != null)
            documentFileSize.text = string.IsNullOrEmpty(mimeText) ? sizeText : $"{sizeText} · {mimeText}";

        if (documentIcon != null) documentIcon.sprite = SpriteForMime(pick.MimeType);
    }

    private Sprite SpriteForMime(string mime)
    {
        if (string.IsNullOrEmpty(mime)) return documentFallbackIcon;
        foreach (var entry in mimeIcons)
        {
            if (string.IsNullOrEmpty(entry.mimePrefix)) continue;
            if (mime.StartsWith(entry.mimePrefix, StringComparison.OrdinalIgnoreCase))
                return entry.sprite != null ? entry.sprite : documentFallbackIcon;
        }
        return documentFallbackIcon;
    }

    private static Texture2D LoadTextureFromFile(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            return tex;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AttachmentPreviewScreen] LoadTextureFromFile failed: {ex.Message}");
            return null;
        }
    }

    private void OnSendTapped()
    {
        if (_currentPick == null) return;
        if (sendButton != null) sendButton.interactable = false;

        string caption = captionField != null ? (captionField.text ?? "").Trim() : "";
        var pick = _currentPick;

        if (ChatManager.Instance != null)
            ChatManager.Instance.StageLocalMedia(pick, caption);
        else
            Debug.LogWarning("[AttachmentPreviewScreen] ChatManager.Instance is null; cannot stage.");

        if (captionField != null && captionField.isFocused) captionField.DeactivateInputField();
        Close();
    }

    private void OnBackTapped()
    {
        if (captionField != null && captionField.isFocused) captionField.DeactivateInputField();
        Close();
    }

    private void Close()
    {
        FadeTo(0f, blocksRaycasts: false, onComplete: () =>
        {
            if (root != null) root.SetActive(false);
            ReleasePreviewTexture();
            _currentPick = null;
        });
    }

    private void FadeTo(float targetAlpha, bool blocksRaycasts, Action onComplete = null)
    {
        if (rootCanvasGroup == null)
        {
            onComplete?.Invoke();
            return;
        }

        rootCanvasGroup.interactable   = targetAlpha > 0f;
        rootCanvasGroup.blocksRaycasts = blocksRaycasts;

        _fadeTween?.Kill();
        _fadeTween = DOTween.To(
                () => rootCanvasGroup.alpha,
                v  => rootCanvasGroup.alpha = v,
                targetAlpha,
                fadeDuration)
            .SetEase(Ease.OutQuad)
            .OnComplete(() => onComplete?.Invoke());
    }

    private void ReleasePreviewTexture()
    {
        if (_currentPreviewTexture != null)
        {
            UnityEngine.Object.Destroy(_currentPreviewTexture);
            _currentPreviewTexture = null;
        }
        if (imagePreview != null) imagePreview.texture = null;
        if (videoPreview != null) videoPreview.texture = null;
    }
}
```

- [ ] **Step 3.2: Verify the script compiles**

In Unity, wait for auto-compile. Console must show zero errors.

If `DeferredDismissInputField` doesn't resolve: confirm it exists at `Assets/Scripts/Chat/DeferredDismissInputField.cs`. If `NativeGallery` doesn't resolve: confirm the plugin is in `Assets/Plugins/NativeGallery/`.

- [ ] **Step 3.3: Commit**

```bash
git add Assets/Scripts/Chat/AttachmentPreviewScreen.cs Assets/Scripts/Chat/AttachmentPreviewScreen.cs.meta
git commit -m "feat(chat): AttachmentPreviewScreen MonoBehaviour

Full-screen preview/caption controller. Subscribes to AttachSheet.OnPicked,
routes to one of three kind-specific panels (Image / Video-thumbnail /
Document tile), accepts an optional caption, and on Send calls
ChatManager.StageLocalMedia. Fade in/out via DOTween (CanvasGroup alpha).
Texture cleanup tracked via _currentPreviewTexture, released on close
and OnDisable. All [SerializeField] refs left unwired — the builder
in the next commit fills them."
```

---

## Task 4: Editor builder

Programmatic scene construction. Verification: run the menu item, inspect hierarchy in scene view, verify inspector refs.

**Files:**
- Create: `Assets/Editor/AttachmentPreviewScreenBuilder.cs`

- [ ] **Step 4.1: Create the builder**

Create `Assets/Editor/AttachmentPreviewScreenBuilder.cs`:

```csharp
#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AttachmentPreviewScreenBuilder
{
    private const string ScreenName    = "AttachmentPreviewScreen";
    private const string RootName      = "Root";

    // Layout — canvas-space px at the project's 1080×2400 reference resolution.
    private const float TopBarHeight       = 88f;
    private const float BottomBarMinHeight = 88f;
    private const float CaptionFieldHeight = 64f;
    private const float SendButtonSize     = 88f;
    private const float BackButtonSize     = 88f;
    private const float DocCardWidth       = 360f;
    private const float DocCardHeight      = 220f;
    private const float DocIconSize        = 56f;
    private const float PlayOverlaySize    = 80f;
    private const float PlayIconSize       = 56f;

    private static readonly Color RootBg         = new Color(0.055f, 0.078f, 0.086f); // #0E1416
    private static readonly Color BarBg          = new Color(0.118f, 0.145f, 0.157f); // #1E2528
    private static readonly Color CaptionFieldBg = new Color(0.165f, 0.196f, 0.212f); // #2A3236
    private static readonly Color SendGreen      = new Color(0.145f, 0.827f, 0.400f); // #25D366
    private static readonly Color White          = Color.white;
    private static readonly Color SubtleText     = new Color(0.604f, 0.631f, 0.651f); // #9AA1A6
    private static readonly Color PlaceholderText = new Color(0.435f, 0.455f, 0.475f); // #6F7479
    private static readonly Color PlayOverlayBg  = new Color(0f, 0f, 0f, 0.50f);

    [MenuItem("Tools/Attach Sheet/Build Preview Screen")]
    public static void Build()
    {
        var existingPreview = Object.FindFirstObjectByType<AttachmentPreviewScreen>(FindObjectsInactive.Include);
        Transform parent;
        if (existingPreview != null)
        {
            parent = existingPreview.transform.parent;
            Object.DestroyImmediate(existingPreview.gameObject);
        }
        else
        {
            var attachSheet = Object.FindFirstObjectByType<AttachSheet>(FindObjectsInactive.Include);
            if (attachSheet == null)
            {
                Debug.LogError("[AttachmentPreviewScreenBuilder] AttachSheet not found in scene. Build the AttachSheet first via Tools > Attach Sheet > Build.");
                return;
            }
            var canvas = attachSheet.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[AttachmentPreviewScreenBuilder] AttachSheet has no Canvas ancestor.");
                return;
            }
            parent = canvas.rootCanvas.transform;
        }

        // ── ScriptHolder (always-on) + Root (toggled) ────────────────
        var screenGo = new GameObject(ScreenName, typeof(RectTransform), typeof(AttachmentPreviewScreen));
        screenGo.transform.SetParent(parent, false);
        var screenRt = (RectTransform)screenGo.transform;
        Stretch(screenRt);

        var rootGo = new GameObject(RootName, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        rootGo.transform.SetParent(screenGo.transform, false);
        rootGo.SetActive(false);
        var rootRt = (RectTransform)rootGo.transform;
        Stretch(rootRt);
        var rootBg = rootGo.GetComponent<Image>();
        rootBg.color = RootBg;
        rootBg.raycastTarget = true;
        var rootCg = rootGo.GetComponent<CanvasGroup>();
        rootCg.alpha = 0f;
        rootCg.interactable = false;
        rootCg.blocksRaycasts = false;

        // ── TopBar ────────────────────────────────────────────────────
        var topBar = NewChild(rootGo.transform, "TopBar", typeof(RectTransform));
        var topBarRt = (RectTransform)topBar.transform;
        topBarRt.anchorMin = new Vector2(0f, 1f);
        topBarRt.anchorMax = new Vector2(1f, 1f);
        topBarRt.pivot     = new Vector2(0.5f, 1f);
        topBarRt.sizeDelta = new Vector2(0f, TopBarHeight);
        topBarRt.anchoredPosition = Vector2.zero;

        var backBtnGo = NewChild(topBar.transform, "BackButton",
                                  typeof(RectTransform), typeof(Image), typeof(Button));
        var backRt = (RectTransform)backBtnGo.transform;
        backRt.anchorMin = new Vector2(0f, 0.5f);
        backRt.anchorMax = new Vector2(0f, 0.5f);
        backRt.pivot     = new Vector2(0f, 0.5f);
        backRt.sizeDelta = new Vector2(BackButtonSize, BackButtonSize);
        backRt.anchoredPosition = new Vector2(24f, 0f);
        var backImg = backBtnGo.GetComponent<Image>();
        backImg.color = White;
        backImg.raycastTarget = true;
        var backBtn = backBtnGo.GetComponent<Button>();
        var backNav = backBtn.navigation; backNav.mode = Navigation.Mode.None; backBtn.navigation = backNav;

        var titleGo = NewChild(topBar.transform, "Title",
                                typeof(RectTransform), typeof(TextMeshProUGUI));
        var titleRt = (RectTransform)titleGo.transform;
        Stretch(titleRt);
        var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
        titleTmp.text          = "Preview";
        titleTmp.fontSize      = 32f;
        titleTmp.color         = White;
        titleTmp.alignment     = TextAlignmentOptions.Center;
        titleTmp.raycastTarget = false;

        // ── BottomBar (incl. KeyboardAwarePanel + DeferredDismissInputField + SendButton) ──
        var bottomBar = NewChild(rootGo.transform, "BottomBar",
                                  typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup),
                                  typeof(KeyboardAwarePanel));
        var bottomRt = (RectTransform)bottomBar.transform;
        bottomRt.anchorMin = new Vector2(0f, 0f);
        bottomRt.anchorMax = new Vector2(1f, 0f);
        bottomRt.pivot     = new Vector2(0.5f, 0f);
        bottomRt.sizeDelta = new Vector2(0f, BottomBarMinHeight);
        bottomRt.anchoredPosition = Vector2.zero;
        var bottomBg = bottomBar.GetComponent<Image>();
        bottomBg.color = BarBg;
        bottomBg.raycastTarget = true;
        var hl = bottomBar.GetComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(32, 32, 24, 24);
        hl.spacing = 24;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth      = true;
        hl.childControlHeight     = true;
        hl.childForceExpandWidth  = false;
        hl.childForceExpandHeight = false;

        // Caption field
        var captionGo = NewChild(bottomBar.transform, "CaptionField",
                                  typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var captionImg = captionGo.GetComponent<Image>();
        captionImg.color = CaptionFieldBg;
        captionImg.raycastTarget = true;
        var captionLe = captionGo.GetComponent<LayoutElement>();
        captionLe.flexibleWidth   = 1;
        captionLe.minHeight       = CaptionFieldHeight;
        captionLe.preferredHeight = CaptionFieldHeight;

        var captionField = captionGo.AddComponent<DeferredDismissInputField>();
        captionField.lineType = TMP_InputField.LineType.MultiLineNewline;
        captionField.textViewport = MakeTextArea(captionGo.transform, out var textComp, out var placeholderComp);
        captionField.textComponent = textComp;
        captionField.placeholder   = placeholderComp;

        // Send button
        var sendBtnGo = NewChild(bottomBar.transform, "SendButton",
                                  typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        var sendImg = sendBtnGo.GetComponent<Image>();
        sendImg.color = SendGreen;
        sendImg.raycastTarget = true;
        var sendLe = sendBtnGo.GetComponent<LayoutElement>();
        sendLe.minWidth = sendLe.preferredWidth = SendButtonSize;
        sendLe.minHeight = sendLe.preferredHeight = SendButtonSize;
        var sendBtn = sendBtnGo.GetComponent<Button>();
        var sendNav = sendBtn.navigation; sendNav.mode = Navigation.Mode.None; sendBtn.navigation = sendNav;

        // ── ContentArea (sits between TopBar and BottomBar) ──────────
        var contentGo = NewChild(rootGo.transform, "ContentArea", typeof(RectTransform));
        var contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 0f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot     = new Vector2(0.5f, 0.5f);
        contentRt.offsetMin = new Vector2(0f, BottomBarMinHeight);
        contentRt.offsetMax = new Vector2(0f, -TopBarHeight);

        // ── ImagePanel ───────────────────────────────────────────────
        var imagePanel = NewChild(contentGo.transform, "ImagePanel", typeof(RectTransform));
        StretchWithPad((RectTransform)imagePanel.transform, 48);
        var imagePreviewGo = NewChild(imagePanel.transform, "RawImage",
                                       typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        Stretch((RectTransform)imagePreviewGo.transform);
        var imagePreview = imagePreviewGo.GetComponent<RawImage>();
        imagePreview.color = White;
        var imageArf = imagePreviewGo.GetComponent<AspectRatioFitter>();
        imageArf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        imagePanel.SetActive(false);

        // ── VideoPanel ───────────────────────────────────────────────
        var videoPanel = NewChild(contentGo.transform, "VideoPanel", typeof(RectTransform));
        StretchWithPad((RectTransform)videoPanel.transform, 48);
        var videoPreviewGo = NewChild(videoPanel.transform, "RawImage",
                                       typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        Stretch((RectTransform)videoPreviewGo.transform);
        var videoPreview = videoPreviewGo.GetComponent<RawImage>();
        videoPreview.color = White;
        var videoArf = videoPreviewGo.GetComponent<AspectRatioFitter>();
        videoArf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        var playOverlayGo = NewChild(videoPanel.transform, "PlayOverlay",
                                      typeof(RectTransform), typeof(Image));
        var playRt = (RectTransform)playOverlayGo.transform;
        playRt.anchorMin = new Vector2(0.5f, 0.5f);
        playRt.anchorMax = new Vector2(0.5f, 0.5f);
        playRt.pivot     = new Vector2(0.5f, 0.5f);
        playRt.sizeDelta = new Vector2(PlayOverlaySize, PlayOverlaySize);
        var playBg = playOverlayGo.GetComponent<Image>();
        playBg.color = PlayOverlayBg;
        playBg.raycastTarget = false;

        var playIconGo = NewChild(playOverlayGo.transform, "PlayIcon",
                                   typeof(RectTransform), typeof(Image));
        var playIconRt = (RectTransform)playIconGo.transform;
        playIconRt.anchorMin = new Vector2(0.5f, 0.5f);
        playIconRt.anchorMax = new Vector2(0.5f, 0.5f);
        playIconRt.pivot     = new Vector2(0.5f, 0.5f);
        playIconRt.sizeDelta = new Vector2(PlayIconSize, PlayIconSize);
        var playIcon = playIconGo.GetComponent<Image>();
        playIcon.color = White;
        playIcon.raycastTarget = false;

        var durationBadge = NewChild(videoPanel.transform, "DurationBadge",
                                      typeof(RectTransform), typeof(Image));
        var dbRt = (RectTransform)durationBadge.transform;
        dbRt.anchorMin = new Vector2(1f, 0f);
        dbRt.anchorMax = new Vector2(1f, 0f);
        dbRt.pivot     = new Vector2(1f, 0f);
        dbRt.sizeDelta = new Vector2(96f, 36f);
        dbRt.anchoredPosition = new Vector2(-16f, 16f);
        var dbBg = durationBadge.GetComponent<Image>();
        dbBg.color = PlayOverlayBg;
        dbBg.raycastTarget = false;

        var durationLabelGo = NewChild(durationBadge.transform, "Label",
                                        typeof(RectTransform), typeof(TextMeshProUGUI));
        Stretch((RectTransform)durationLabelGo.transform);
        var durationLabel = durationLabelGo.GetComponent<TextMeshProUGUI>();
        durationLabel.text = "0:00";
        durationLabel.fontSize = 24f;
        durationLabel.color = White;
        durationLabel.alignment = TextAlignmentOptions.Center;
        durationLabel.raycastTarget = false;
        videoPanel.SetActive(false);

        // ── DocumentPanel ────────────────────────────────────────────
        var documentPanel = NewChild(contentGo.transform, "DocumentPanel", typeof(RectTransform));
        var docRt = (RectTransform)documentPanel.transform;
        docRt.anchorMin = new Vector2(0.5f, 0.5f);
        docRt.anchorMax = new Vector2(0.5f, 0.5f);
        docRt.pivot     = new Vector2(0.5f, 0.5f);
        docRt.sizeDelta = new Vector2(DocCardWidth, DocCardHeight);
        var docCardGo = NewChild(documentPanel.transform, "Card",
                                  typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        var docCardRt = (RectTransform)docCardGo.transform;
        Stretch(docCardRt);
        var docCardBg = docCardGo.GetComponent<Image>();
        docCardBg.color = BarBg;
        docCardBg.raycastTarget = false;
        var docVl = docCardGo.GetComponent<VerticalLayoutGroup>();
        docVl.padding = new RectOffset(24, 24, 24, 24);
        docVl.spacing = 12;
        docVl.childAlignment = TextAnchor.MiddleCenter;
        docVl.childControlWidth      = true;
        docVl.childControlHeight     = false;
        docVl.childForceExpandWidth  = true;
        docVl.childForceExpandHeight = false;

        var docIconGo = NewChild(docCardGo.transform, "Icon",
                                  typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var docIconLe = docIconGo.GetComponent<LayoutElement>();
        docIconLe.minWidth = docIconLe.preferredWidth = DocIconSize;
        docIconLe.minHeight = docIconLe.preferredHeight = DocIconSize;
        var docIconImg = docIconGo.GetComponent<Image>();
        docIconImg.color = White;
        docIconImg.raycastTarget = false;

        var docNameGo = NewChild(docCardGo.transform, "FileName",
                                  typeof(RectTransform), typeof(TextMeshProUGUI));
        var docName = docNameGo.GetComponent<TextMeshProUGUI>();
        docName.text = "filename.pdf";
        docName.fontSize = 32f;
        docName.fontStyle = FontStyles.Bold;
        docName.color = White;
        docName.alignment = TextAlignmentOptions.Center;
        docName.enableWordWrapping = false;
        docName.overflowMode = TextOverflowModes.Ellipsis;
        docName.raycastTarget = false;

        var docSizeGo = NewChild(docCardGo.transform, "FileSize",
                                  typeof(RectTransform), typeof(TextMeshProUGUI));
        var docSize = docSizeGo.GetComponent<TextMeshProUGUI>();
        docSize.text = "0 B";
        docSize.fontSize = 24f;
        docSize.color = SubtleText;
        docSize.alignment = TextAlignmentOptions.Center;
        docSize.raycastTarget = false;
        documentPanel.SetActive(false);

        // ── Wire serialized refs ─────────────────────────────────────
        var screen = screenGo.GetComponent<AttachmentPreviewScreen>();
        var so = new SerializedObject(screen);

        SetObjectRef(so, "attachSheet",       Object.FindFirstObjectByType<AttachSheet>(FindObjectsInactive.Include));
        SetObjectRef(so, "root",              rootGo);
        SetObjectRef(so, "rootCanvasGroup",   rootCg);
        SetObjectRef(so, "imagePanel",        imagePanel);
        SetObjectRef(so, "videoPanel",        videoPanel);
        SetObjectRef(so, "documentPanel",     documentPanel);
        SetObjectRef(so, "imagePreview",      imagePreview);
        SetObjectRef(so, "videoPreview",      videoPreview);
        SetObjectRef(so, "videoPlayOverlay",  playOverlayGo);
        SetObjectRef(so, "videoDurationBadge", durationBadge);
        SetObjectRef(so, "videoDurationLabel", durationLabel);
        SetObjectRef(so, "documentFileName",  docName);
        SetObjectRef(so, "documentFileSize",  docSize);
        SetObjectRef(so, "documentIcon",      docIconImg);
        SetObjectRef(so, "captionField",      captionField);
        SetObjectRef(so, "sendButton",        sendBtn);
        SetObjectRef(so, "backButton",        backBtn);

        // Seed the MIME-icon list with empty slots — user drops sprites in inspector.
        var mimeIconsProp = so.FindProperty("mimeIcons");
        mimeIconsProp.arraySize = 0;
        AddMimeIconEntry(mimeIconsProp, "application/pdf");
        AddMimeIconEntry(mimeIconsProp, "application/vnd.openxmlformats-officedocument");
        AddMimeIconEntry(mimeIconsProp, "application/msword");
        AddMimeIconEntry(mimeIconsProp, "application/vnd.ms-excel");
        AddMimeIconEntry(mimeIconsProp, "image/");
        AddMimeIconEntry(mimeIconsProp, "video/");
        AddMimeIconEntry(mimeIconsProp, "text/");

        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(screenGo.scene);
        Debug.Log("[AttachmentPreviewScreenBuilder] Built AttachmentPreviewScreen. Assign sprite refs (back/send/play/doc icons) in the inspector.");
    }

    // ── helpers ───────────────────────────────────────────────────

    private static GameObject NewChild(Transform parent, string name, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
    }

    private static void StretchWithPad(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
        rt.pivot     = new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// Build the TMP_InputField's text + placeholder children. Mirrors what
    /// Unity's "GameObject > UI > Input Field (TMP)" menu produces.
    /// </summary>
    private static RectTransform MakeTextArea(Transform parent, out TMP_Text text, out TMP_Text placeholder)
    {
        var areaGo = NewChild(parent, "Text Area", typeof(RectTransform), typeof(RectMask2D));
        var areaRt = (RectTransform)areaGo.transform;
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(20f, 12f);
        areaRt.offsetMax = new Vector2(-20f, -12f);

        var placeholderGo = NewChild(areaGo.transform, "Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        Stretch((RectTransform)placeholderGo.transform);
        var ph = placeholderGo.GetComponent<TextMeshProUGUI>();
        ph.text          = "Add a caption…";
        ph.fontSize      = 28f;
        ph.color         = PlaceholderText;
        ph.fontStyle     = FontStyles.Italic;
        ph.alignment     = TextAlignmentOptions.Left;
        ph.raycastTarget = false;
        placeholder = ph;

        var textGo = NewChild(areaGo.transform, "Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        Stretch((RectTransform)textGo.transform);
        var tx = textGo.GetComponent<TextMeshProUGUI>();
        tx.text          = "";
        tx.fontSize      = 28f;
        tx.color         = White;
        tx.alignment     = TextAlignmentOptions.Left;
        tx.raycastTarget = false;
        text = tx;

        return areaRt;
    }

    private static void AddMimeIconEntry(SerializedProperty arrayProp, string prefix)
    {
        arrayProp.arraySize++;
        var element = arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1);
        var prefixProp = element.FindPropertyRelative("mimePrefix");
        var spriteProp = element.FindPropertyRelative("sprite");
        prefixProp.stringValue = prefix;
        spriteProp.objectReferenceValue = null;
    }

    private static void SetObjectRef(SerializedObject so, string propertyName, Object value)
    {
        var p = so.FindProperty(propertyName);
        if (p == null)
        {
            Debug.LogWarning($"[AttachmentPreviewScreenBuilder] Property {propertyName} not found on {so.targetObject}");
            return;
        }
        p.objectReferenceValue = value;
        if (value == null)
            Debug.LogWarning($"[AttachmentPreviewScreenBuilder] {so.targetObject.GetType().Name}.{propertyName} was set to null — assign manually in the inspector.");
    }
}
#endif
```

- [ ] **Step 4.2: Verify the builder compiles**

In Unity, wait for auto-compile. Console must show zero errors.

- [ ] **Step 4.3: Run the builder**

In Unity:
1. Open `Assets/Scenes/Main.unity`.
2. Menu: `Tools > Attach Sheet > Build Preview Screen`.
3. Console should log `[AttachmentPreviewScreenBuilder] Built AttachmentPreviewScreen. Assign sprite refs (back/send/play/doc icons) in the inspector.`

If error `AttachSheet not found in scene`: run `Tools > Attach Sheet > Build` first.

- [ ] **Step 4.4: Verify the scene hierarchy**

In the Hierarchy window, find `AttachmentPreviewScreen` as a top-level child of the root Canvas. Expand it:

```
AttachmentPreviewScreen   (active)
└─ Root                   (INactive at scene start — this is correct)
    ├─ TopBar
    │   ├─ BackButton
    │   └─ Title
    ├─ BottomBar          (has KeyboardAwarePanel)
    │   ├─ CaptionField   (has DeferredDismissInputField + Text Area)
    │   └─ SendButton
    └─ ContentArea
        ├─ ImagePanel     (inactive)
        ├─ VideoPanel     (inactive)
        └─ DocumentPanel  (inactive)
```

Select `AttachmentPreviewScreen` and verify the inspector shows the `AttachmentPreviewScreen` component with all serialized refs populated (no `Missing` or `None`), EXCEPT the sprite refs which are intentionally empty (user assigns later).

- [ ] **Step 4.5: Temporarily activate Root to spot-check layout**

In the Hierarchy, select `AttachmentPreviewScreen/Root` and toggle the active checkbox in the inspector. In the Game view at 1080×2400:

- Full-screen dark background (`#0E1416`).
- TopBar at top with white square (placeholder back button) on left, "Preview" centered.
- BottomBar at bottom with caption pill (dark grey rounded) and green square (placeholder send button) on right.
- ContentArea blank (all three panels inactive). This is correct.

Toggle Root back to inactive. The CanvasGroup alpha is 0 from `Awake`, so it would appear invisible at runtime anyway — but for visual inspection here, temporarily set alpha to 1 in the inspector, then back to 0 when done.

- [ ] **Step 4.6: Save the scene**

`File > Save` (Cmd+S). The scene asset (`Assets/Scenes/Main.unity`) will have the new hierarchy.

- [ ] **Step 4.7: Commit**

```bash
git add Assets/Editor/AttachmentPreviewScreenBuilder.cs Assets/Editor/AttachmentPreviewScreenBuilder.cs.meta \
        Assets/Scenes/Main.unity
git commit -m "feat(chat): AttachmentPreviewScreenBuilder + scene wiring

Editor menu item Tools > Attach Sheet > Build Preview Screen that
constructs the full hierarchy (TopBar/ContentArea/BottomBar with the
three kind panels) at 1080x2400, wires all [SerializeField] refs via
SerializedObject, and seeds the mimeIcons list with the seven MIME
prefixes (sprite slots left empty for the user to assign). Idempotent:
deletes any existing AttachmentPreviewScreen before rebuilding."
```

---

## Task 5: End-to-end manual smoke test (all three kinds)

No code changes — exercise the full pick → preview → caption → send loop in the editor and on at least one device.

**Files:** none (verification only)

- [ ] **Step 5.1: Editor smoke — Document**

1. Open `Assets/Scenes/Main.unity`. Press Play.
2. Sign in / select an active bot; open any chat.
3. Tap the plus (+) button in the chat input bar → AttachSheet slides up.
4. Tap "Document" → native file picker opens → choose any local file (e.g., a PDF).
5. AttachSheet closes; AttachmentPreviewScreen fades in over ~0.18s.
6. Document tile shows in the centre: file icon (will be missing — sprite not yet assigned), filename, "X.X MB · PDF".
7. Tap the caption field (placeholder "Add a caption…"); the keyboard rises and the BottomBar floats above it.
8. Type "test caption" and tap Send (green square).
9. Preview fades out; a Document bubble appears at the bottom of the chat with the caption, sized correctly, with the Pending status indicator.

Pass criteria: no console errors, bubble renders, no stuck UI state. Acceptable: missing sprite glyphs (back/send/play/doc) render as solid coloured squares because sprite slots are empty — this is wired-but-unstyled, not broken.

- [ ] **Step 5.2: Editor smoke — Image (Gallery)**

1. Still in Play mode (or re-enter). Open a chat.
2. Tap + → "Gallery" → pick a photo from the device's gallery (use editor's mocked picker — `NativeGallery` provides one — or test on device if simulator is unavailable).
3. AttachmentPreviewScreen fades in showing the photo aspect-fit in the ContentArea.
4. Add a caption "image test" → Send.
5. Image bubble appears in the chat with the caption.

Pass criteria: photo renders at correct aspect ratio; bubble shows the image content (not a placeholder); status is Pending.

If the image doesn't render in the bubble: check Console for `[MediaCacheManager]` or `[MessageItemView]` warnings. The pre-seed in `SeedImageCache` should have written the bytes; verify `Application.persistentDataPath/<bot>/media/*.jpg` contains the new file.

- [ ] **Step 5.3: Editor smoke — Video (Gallery)**

1. Tap + → "Gallery" → pick a video.
2. AttachmentPreviewScreen shows the first-frame thumbnail, centered play overlay glyph (or solid box if sprite unassigned), and duration badge ("0:NN" format) if `GetVideoProperties` returned a duration.
3. Send with empty caption (allowed).
4. Video bubble appears in the chat with the thumbnail and the play affordance from the existing bubble views.

Pass criteria: thumbnail visible, duration badge correct, bubble renders. Acceptable: tap Play on the bubble — Risk #9 from the spec — may or may not play the local `file://` video; document the result in step 5.7.

- [ ] **Step 5.4: Back-button behaviour**

1. Tap + → any picker → preview opens → tap Back (chevron top-left).
2. Preview fades out, no bubble appears.
3. Repeat with a caption typed in the field — same result, silent discard (Q3 decision).
4. Re-open by picking again — caption field starts empty (no carryover).

- [ ] **Step 5.5: In-memory-only verification**

1. Stage one image bubble via the full pick → Send loop.
2. Without closing Play mode, switch to a different chat in the same bot.
3. Switch back to the original chat.
4. The staged bubble is GONE (in-memory only, no cache write — Q1 decision).

If the bubble persists: the `StageLocalMedia` body is incorrectly writing to `ChatHistoryCache`. Re-read Task 2; no `ChatHistoryCache.SaveHistory` call should be present.

- [ ] **Step 5.6: Texture cleanup verification (Profiler optional)**

Pick → Send / Back / Pick / Send / Back five times in quick succession. Open `Window > Analysis > Profiler > Memory > Detailed`. Texture count should NOT grow per cycle (within ±1 of the steady-state). If it climbs by one per cycle, `ReleasePreviewTexture` isn't firing on Close — check the `FadeTo` `onComplete` chain.

- [ ] **Step 5.7: Device smoke (iOS or Android, your choice)**

Build to a device (Android preferred for quicker iteration). Repeat steps 5.1–5.5. Specifically verify:

- Caption field keyboard rises smoothly and the BottomBar follows it (KeyboardAwarePanel-on-BottomBar wiring).
- Tap Send → the keyboard collapses BEFORE the screen fades.
- The AttachSheet from part "a" still behaves identically — opens/closes the keyboard as before; this PR didn't regress it.
- Tap Play on a staged video bubble: note whether `VideoPlayer` plays the local `file://` video. Record the outcome — if it plays, no further action; if it errors/no-ops, file a part-"c" follow-up to gate Play on `!Pending` status.

- [ ] **Step 5.8: Commit verification checkpoint (no new code)**

Run:

```bash
git status
git diff --stat HEAD~4..HEAD
```

Expected `--stat` summary (4 commits from this plan):
- `Assets/Scripts/Chat/AttachmentDisplayFormat.cs` — new
- `Assets/Tests/Editor/Chat/AttachmentDisplayFormatTests.cs` — new
- `Assets/Scripts/Main/ChatManager.cs` — modified
- `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` — new
- `Assets/Editor/AttachmentPreviewScreenBuilder.cs` — new
- `Assets/Scenes/Main.unity` — modified
- (Plus the `.meta` siblings for each new file.)

No other files modified. `AttachSheet.cs`, `MessagesBottomPanel.cs`, `MessageItemView.cs`, `MediaCacheManager.cs` must NOT appear in the diff.

If unwanted files appear: revert them with `git checkout HEAD~4 -- <path>` (careful — that's destructive for files added by this branch only; if any of those files were edited as a deliberate part of the plan, leave them).

---

## Done

All five tasks complete. The user can now: tap + → AttachSheet → pick photo/video/document → preview with caption → Send → in-memory `Pending` bubble appears in chat. Bubble vanishes on chat/bot switch. No Wappi upload yet — that's part "c", which will replace the body of `ChatManager.StageLocalMedia` with the real upload + persist path (per the §11 handoff contract in the spec).
