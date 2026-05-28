# Attach Panel — Part C: Wappi Media Send Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a picked image / video / document actually send to WhatsApp through Wappi with full text-send parity — optimistic bubble persisted to `ChatHistoryCache` + `Outbox`, real `message_id` swap, `Pending → Sent`, fail-with-tap-to-retry, and bot-switch safety.

**Architecture:** Mirror the proven text path (`SendTextMessageRoutine` → `PostTextMessageRoutine` → `RetryRoutine`). Relocate `StageLocalMedia` + its seed helpers into a new `ChatManager.MediaSend.cs` partial and give it a persist + enqueue + upload tail. Add a new `PostMediaMessageRoutine` (off-thread base64 + kind-routed Wappi endpoint, identical reconcile to text). Extend `OutboxEntry` with a `kind` discriminator + media fields (append-only). Extract two pure, unit-tested helpers (`Base64Encoder`, `WappiMediaRequestFactory`). Guard over-cap video before staging.

**Tech Stack:** Unity 6 (6000.3.9f1) C#, URP. Coroutine networking via `UnityWebRequest` (no async/await in MonoBehaviours). Newtonsoft `JsonConvert` for request/response. Unity `JsonUtility` for on-disk outbox. EditMode tests via NUnit in the predefined `Assembly-CSharp-Editor` (no asmdef in this project).

**Source spec:** `docs/superpowers/specs/2026-05-29-attach-panel-media-send-design.md`

---

## Conventions for this plan

**Project has no `.asmdef` files.** All runtime scripts compile into `Assembly-CSharp`; everything under an `Editor/` folder compiles into `Assembly-CSharp-Editor`. Unity's Test Framework auto-references NUnit + Newtonsoft into those predefined assemblies, so new EditMode tests just go in `Assets/Tests/Editor/Chat/` with `using NUnit.Framework;` — **do not create or edit any asmdef.**

**Running EditMode tests** (every "run the test" step below):
- Unity Editor → `Window → General → Test Runner` → **EditMode** tab → `Run All` (or right-click the class → Run).
- CLI alternative (project must not be open elsewhere): `Unity -batchmode -runTests -projectPath . -testPlatform EditMode -testResults /tmp/results.xml -quit` then inspect `/tmp/results.xml`.

**"Verify it fails" in Unity:** a test that references a not-yet-created type makes the whole assembly fail to compile (red errors in the Console, Test Runner won't run). Where a step says to stub a type first, the stub lets the assembly compile so you get a genuine **red assertion** instead of a compile wall. Follow the stub-then-real cycle as written.

**Unity recompiles on file save.** After each Create/Edit, return to the Editor, let it recompile, and confirm the Console has **zero** errors before moving on.

**`MessageType`** is the global enum `{ Chat, Image, Video, Audio, Voice, Sticker, Document, Unknown }`. **`AttachmentKind`** is `{ Photo=0, GalleryImage=1, GalleryVideo=2, Document=3 }`. The project uses **no namespaces** — all types are global.

**Commits:** conventional-commit style matching the repo (`feat(chat):`, `refactor(chat):`, `test(chat):`). Commit after every task. Stage only the files the task names.

---

## File structure

**New (runtime):**
- `Assets/Scripts/Chat/Base64Encoder.cs` — static, no Unity dependency. Off-thread file → base64.
- `Assets/Scripts/Chat/WappiMediaRequestFactory.cs` — pure endpoint + JSON-body builder + recipient normalizer. Plus the `[Serializable] WappiSendMediaRequest` DTO.
- `Assets/Scripts/Main/ChatManager.MediaSend.cs` — `partial class ChatManager`. Relocated `StageLocalMedia` + seed helpers, plus new `PostMediaMessageRoutine` and `LastMessagePreview`.

**New (tests):**
- `Assets/Tests/Editor/Chat/Base64EncoderTests.cs`
- `Assets/Tests/Editor/Chat/WappiMediaRequestFactoryTests.cs`
- `Assets/Tests/Editor/Chat/OutboxEntryMediaCompatTests.cs`

**Modified:**
- `Assets/Scripts/Chat/OutboxStore.cs` — append fields to `OutboxEntry`; add `OutboxKind` enum.
- `Assets/Scripts/Main/ChatManager.cs` — remove the relocated `StageLocalMedia` + 4 seed helpers (moved to the partial).
- `Assets/Scripts/Main/ChatManager.Outbox.cs` — branch `RetryRoutine` on `entry.kind`.
- `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` — pre-stage video size guard + runtime `ShowSizeError` label.

**Untouched (do not edit):** `AttachSheet.cs`, `MessagesBottomPanel.cs`, `MessageItemView.cs`, `MediaCacheManager.cs`, all `Assets/Editor/*Builder.cs`.

---

## Task 1: `Base64Encoder` (off-thread file → base64)

**Files:**
- Create: `Assets/Scripts/Chat/Base64Encoder.cs`
- Test: `Assets/Tests/Editor/Chat/Base64EncoderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/Base64EncoderTests.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using NUnit.Framework;

public class Base64EncoderTests
{
    private static string TempPath(string tag) =>
        Path.Combine(Path.GetTempPath(), $"b64enc_{tag}_{Guid.NewGuid():N}.bin");

    [Test]
    public void EncodeFileAsync_KnownBytes_MatchesConvertToBase64()
    {
        string path = TempPath("known");
        byte[] bytes = Encoding.UTF8.GetBytes("hello wappi media");
        File.WriteAllBytes(path, bytes);
        try
        {
            string result = Base64Encoder.EncodeFileAsync(path).GetAwaiter().GetResult();
            Assert.AreEqual(Convert.ToBase64String(bytes), result);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void EncodeFileAsync_EmptyFile_ReturnsEmptyString()
    {
        string path = TempPath("empty");
        File.WriteAllBytes(path, Array.Empty<byte>());
        try
        {
            string result = Base64Encoder.EncodeFileAsync(path).GetAwaiter().GetResult();
            Assert.AreEqual("", result);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void EncodeFileAsync_LargeFile_MatchesConvertToBase64()
    {
        string path = TempPath("large");
        var bytes = new byte[3 * 1024 * 1024];
        new System.Random(42).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        try
        {
            string result = Base64Encoder.EncodeFileAsync(path).GetAwaiter().GetResult();
            Assert.AreEqual(Convert.ToBase64String(bytes), result);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void EncodeFileAsync_MissingFile_TaskFaults()
    {
        string path = TempPath("missing"); // never created
        var task = Base64Encoder.EncodeFileAsync(path);
        Assert.Throws<AggregateException>(() => task.Wait());
        Assert.IsTrue(task.IsFaulted);
    }
}
```

- [ ] **Step 2: Create a stub so it compiles and the assertions fail (genuine red)**

Create `Assets/Scripts/Chat/Base64Encoder.cs`:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;

public static class Base64Encoder
{
    public static Task<string> EncodeFileAsync(string path) => Task.FromResult(string.Empty); // STUB
}
```

- [ ] **Step 3: Run EditMode tests, verify failures**

Run the Test Runner (EditMode). Expected: `EncodeFileAsync_KnownBytes...` and `EncodeFileAsync_LargeFile...` FAIL (stub returns `""`); `EncodeFileAsync_MissingFile_TaskFaults` FAILS (stub never faults); `EncodeFileAsync_EmptyFile...` passes (coincidence). This proves the tests exercise real behavior.

- [ ] **Step 4: Replace the stub with the real implementation**

Edit `Assets/Scripts/Chat/Base64Encoder.cs` — replace the stub body:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Reads a file and base64-encodes it on a thread-pool thread so the ~33%
/// size inflation + large-buffer allocation never blocks a Unity frame.
/// The returned Task faults (FileNotFoundException / IOException) if the
/// file is gone — callers treat a faulted task as a send failure.
/// </summary>
public static class Base64Encoder
{
    public static Task<string> EncodeFileAsync(string path) => Task.Run(() =>
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Convert.ToBase64String(bytes);
    });
}
```

- [ ] **Step 5: Run EditMode tests, verify all pass**

Run the Test Runner (EditMode). Expected: all four `Base64EncoderTests` PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/Base64Encoder.cs Assets/Tests/Editor/Chat/Base64EncoderTests.cs
git commit -m "$(cat <<'EOF'
feat(chat): add off-thread Base64Encoder for media uploads

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

> **Note:** Unity also generates a `.meta` file for each new file (`Base64Encoder.cs.meta`, `Base64EncoderTests.cs.meta`). Stage those too if `git status` shows them — `git add` the directory or the `.meta` paths alongside the `.cs`.

---

## Task 2: `WappiMediaRequestFactory` (endpoint + body builder)

**Files:**
- Create: `Assets/Scripts/Chat/WappiMediaRequestFactory.cs`
- Test: `Assets/Tests/Editor/Chat/WappiMediaRequestFactoryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/WappiMediaRequestFactoryTests.cs`:

```csharp
using NUnit.Framework;

public class WappiMediaRequestFactoryTests
{
    private const string Img = "https://wappi.pro/api/sync/message/img/send?profile_id=PID";
    private const string Vid = "https://wappi.pro/api/sync/message/video/send?profile_id=PID";
    private const string Doc = "https://wappi.pro/api/sync/message/document/send?profile_id=PID";

    [Test]
    public void EndpointFor_Image_UsesImgSend()
    {
        Assert.AreEqual(Img, WappiMediaRequestFactory.EndpointFor(AttachmentKind.Photo, "PID"));
        Assert.AreEqual(Img, WappiMediaRequestFactory.EndpointFor(AttachmentKind.GalleryImage, "PID"));
    }

    [Test]
    public void EndpointFor_Video_UsesVideoSend() =>
        Assert.AreEqual(Vid, WappiMediaRequestFactory.EndpointFor(AttachmentKind.GalleryVideo, "PID"));

    [Test]
    public void EndpointFor_Document_UsesDocumentSend() =>
        Assert.AreEqual(Doc, WappiMediaRequestFactory.EndpointFor(AttachmentKind.Document, "PID"));

    [Test]
    public void NormalizeRecipient_StripsCUs() =>
        Assert.AreEqual("79995579399", WappiMediaRequestFactory.NormalizeRecipient("79995579399@c.us"));

    [Test]
    public void NormalizeRecipient_KeepsGroupAndBare()
    {
        Assert.AreEqual("120363@g.us", WappiMediaRequestFactory.NormalizeRecipient("120363@g.us"));
        Assert.AreEqual("79995579399", WappiMediaRequestFactory.NormalizeRecipient("79995579399"));
    }

    [Test]
    public void BuildBody_Document_IncludesFileName()
    {
        string body = WappiMediaRequestFactory.BuildBody(
            AttachmentKind.Document, "79995579399@c.us", "cap", "report.pdf", "QkFTRTY0");
        StringAssert.Contains("\"recipient\":\"79995579399\"", body);
        StringAssert.Contains("\"caption\":\"cap\"", body);
        StringAssert.Contains("\"file_name\":\"report.pdf\"", body);
        StringAssert.Contains("\"b64_file\":\"QkFTRTY0\"", body);
    }

    [Test]
    public void BuildBody_Image_OmitsFileName()
    {
        string body = WappiMediaRequestFactory.BuildBody(
            AttachmentKind.GalleryImage, "79995579399@c.us", "", null, "QkFTRTY0");
        StringAssert.DoesNotContain("file_name", body);
        StringAssert.Contains("\"caption\":\"\"", body);
        StringAssert.Contains("\"b64_file\":\"QkFTRTY0\"", body);
    }

    [Test]
    public void BuildBody_DocumentMissingName_FallsBackToFile()
    {
        string body = WappiMediaRequestFactory.BuildBody(
            AttachmentKind.Document, "x@c.us", "", "", "Qg==");
        StringAssert.Contains("\"file_name\":\"file\"", body);
    }
}
```

- [ ] **Step 2: Create a stub so it compiles and the assertions fail (genuine red)**

Create `Assets/Scripts/Chat/WappiMediaRequestFactory.cs`:

```csharp
using System;
using Newtonsoft.Json;

public static class WappiMediaRequestFactory
{
    public static string EndpointFor(AttachmentKind kind, string profileId) => null;          // STUB
    public static string NormalizeRecipient(string chatId) => chatId;                          // STUB
    public static string BuildBody(AttachmentKind kind, string chatId, string caption,
                                   string fileName, string b64) => "{}";                       // STUB
}

[Serializable]
public class WappiSendMediaRequest
{
    public string recipient;
    public string caption;
    public string file_name;
    public string b64_file;
}
```

- [ ] **Step 3: Run EditMode tests, verify failures**

Run the Test Runner (EditMode). Expected: every `EndpointFor_*` and every `BuildBody_*` FAILS; `NormalizeRecipient_KeepsGroupAndBare` passes (stub happens to satisfy it) and `NormalizeRecipient_StripsCUs` FAILS. Confirms real coverage.

- [ ] **Step 4: Implement the real factory**

Replace the stub bodies in `Assets/Scripts/Chat/WappiMediaRequestFactory.cs`:

```csharp
using System;
using Newtonsoft.Json;

/// <summary>
/// Pure mapping from an AttachmentKind to its Wappi media-send endpoint and
/// JSON body. No UnityWebRequest, no I/O — kept separate so it is unit-testable.
/// Contract confirmed from the Wappi dashboard: POST, profile_id as query param,
/// b64_file is raw base64 (no data: prefix). file_name is only sent for documents.
/// </summary>
public static class WappiMediaRequestFactory
{
    private const string Base = "https://wappi.pro/api/sync/message/";

    public static string EndpointFor(AttachmentKind kind, string profileId) => kind switch
    {
        AttachmentKind.Photo or AttachmentKind.GalleryImage => $"{Base}img/send?profile_id={profileId}",
        AttachmentKind.GalleryVideo                         => $"{Base}video/send?profile_id={profileId}",
        AttachmentKind.Document                             => $"{Base}document/send?profile_id={profileId}",
        _                                                   => null
    };

    public static string NormalizeRecipient(string chatId) =>
        chatId != null && chatId.EndsWith("@c.us") ? chatId.Replace("@c.us", "") : chatId;

    public static string BuildBody(AttachmentKind kind, string chatId, string caption,
                                   string fileName, string b64)
    {
        var req = new WappiSendMediaRequest
        {
            recipient = NormalizeRecipient(chatId),
            caption   = caption ?? "",
            b64_file  = b64,
            file_name = kind == AttachmentKind.Document
                        ? (string.IsNullOrEmpty(fileName) ? "file" : fileName)
                        : null
        };
        return JsonConvert.SerializeObject(req,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }
}

[Serializable]
public class WappiSendMediaRequest
{
    public string recipient;
    public string caption;
    public string file_name;
    public string b64_file;
}
```

- [ ] **Step 5: Run EditMode tests, verify all pass**

Run the Test Runner (EditMode). Expected: all `WappiMediaRequestFactoryTests` PASS. (`NullValueHandling.Ignore` drops the null `file_name` for image/video, so `BuildBody_Image_OmitsFileName` passes.)

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/WappiMediaRequestFactory.cs Assets/Tests/Editor/Chat/WappiMediaRequestFactoryTests.cs
git commit -m "$(cat <<'EOF'
feat(chat): add WappiMediaRequestFactory for media-send endpoints + body

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Extend `OutboxEntry` with media fields + `OutboxKind`

**Files:**
- Modify: `Assets/Scripts/Chat/OutboxStore.cs:25-34` (the `OutboxEntry` class) and add a top-level `OutboxKind` enum.
- Test: `Assets/Tests/Editor/Chat/OutboxEntryMediaCompatTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/OutboxEntryMediaCompatTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class OutboxEntryMediaCompatTests
{
    [Test]
    public void LegacyTextJson_DeserializesAsTextKind()
    {
        // A pre-part-c outbox entry has none of the media fields.
        string legacy =
            "{\"tempId\":\"sending_1\",\"chatId\":\"79@c.us\",\"text\":\"hi\"," +
            "\"timestamp\":123,\"attemptCount\":1,\"profileId\":\"P\"}";

        var e = JsonUtility.FromJson<OutboxStore.OutboxEntry>(legacy);

        Assert.AreEqual("sending_1", e.tempId);
        Assert.AreEqual("hi", e.text);
        Assert.AreEqual(0, e.kind);                       // missing field defaults to 0
        Assert.AreEqual((int)OutboxKind.Text, e.kind);    // 0 == Text
    }

    [Test]
    public void MediaEntry_RoundTrips()
    {
        var orig = new OutboxStore.OutboxEntry
        {
            tempId = "staging_2", chatId = "79@c.us", text = "cap",
            timestamp = 456, attemptCount = 1, profileId = "P",
            kind = (int)OutboxKind.Media, attachmentKind = (int)AttachmentKind.GalleryVideo,
            mediaPath = "/tmp/v.mp4", mimeType = "video/mp4", fileName = "v.mp4",
            mediaUrl = "", thumbnailUrl = "thumb://staged/staging_2", videoUrl = "file:///tmp/v.mp4",
            aspectRatio = 1.77f, duration = 12
        };

        var rt = JsonUtility.FromJson<OutboxStore.OutboxEntry>(JsonUtility.ToJson(orig));

        Assert.AreEqual((int)OutboxKind.Media, rt.kind);
        Assert.AreEqual((int)AttachmentKind.GalleryVideo, rt.attachmentKind);
        Assert.AreEqual("/tmp/v.mp4", rt.mediaPath);
        Assert.AreEqual("video/mp4", rt.mimeType);
        Assert.AreEqual("v.mp4", rt.fileName);
        Assert.AreEqual("thumb://staged/staging_2", rt.thumbnailUrl);
        Assert.AreEqual("file:///tmp/v.mp4", rt.videoUrl);
        Assert.AreEqual(1.77f, rt.aspectRatio, 0.0001f);
        Assert.AreEqual(12, rt.duration);
    }
}
```

- [ ] **Step 2: Run EditMode tests, verify it fails to compile**

Run the Test Runner (EditMode). Expected: Console shows compile errors — `OutboxKind` does not exist and `OutboxStore.OutboxEntry` has no `kind`/`attachmentKind`/`mediaPath`/etc. This is the red state for a field-shape change.

- [ ] **Step 3: Add the `OutboxKind` enum and the appended fields**

In `Assets/Scripts/Chat/OutboxStore.cs`, add the enum just **above** `public class OutboxStore` (after the `using` block):

```csharp
/// <summary>Discriminator for OutboxEntry. Append-only; persisted as int ordinal.</summary>
public enum OutboxKind { Text = 0, Media = 1 }
```

Then replace the `OutboxEntry` class body (currently lines 25-34) — keep the existing six fields, append the media fields:

```csharp
    /// <summary>
    /// Mutable DTO. Always pass a modified entry back through OutboxStore.Update()
    /// to ensure disk persistence — mutating fields directly on a reference from
    /// GetFor() or Find() will update the in-memory cache but NOT the on-disk file.
    /// </summary>
    [Serializable]
    public class OutboxEntry
    {
        // --- existing (unchanged) ---
        public string tempId;
        public string chatId;
        public string text;          // caption for media entries
        public long   timestamp;
        public int    attemptCount;
        public string profileId;

        // --- appended for part c (append-only; JsonUtility fills missing as default) ---
        public int    kind;            // OutboxKind ordinal; 0 = Text (back-compat default)
        public int    attachmentKind;  // AttachmentKind ordinal (Photo=0..Document=3)
        public string mediaPath;       // upload byte source: staged-JPEG path (image) | pick.Path (video/doc)
        public string mimeType;
        public string fileName;
        public string mediaUrl;        // staged://image/{tempId} or staged://document/{tempId}
        public string thumbnailUrl;    // thumb://staged/{tempId} (video)
        public string videoUrl;        // file://{pick.Path} (video, in-session playback)
        public float  aspectRatio;
        public int    duration;
    }
```

- [ ] **Step 4: Run EditMode tests, verify all pass**

Run the Test Runner (EditMode). Expected: both `OutboxEntryMediaCompatTests` PASS. Also re-run `AttachmentDisplayFormatTests` — still green (no regression).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/OutboxStore.cs Assets/Tests/Editor/Chat/OutboxEntryMediaCompatTests.cs
git commit -m "$(cat <<'EOF'
feat(chat): extend OutboxEntry with media fields + OutboxKind

Append-only fields keep legacy text outbox JSON deserializing as kind=Text.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Relocate `StageLocalMedia` + seed helpers into `ChatManager.MediaSend.cs` (pure move)

This is a **behavior-preserving move** so the diff is clean and reviewable. No logic changes here — the upload tail comes in Task 5. Verification is "compiles with zero errors and no duplicate-definition errors."

**Files:**
- Create: `Assets/Scripts/Main/ChatManager.MediaSend.cs`
- Modify: `Assets/Scripts/Main/ChatManager.cs:1457-1642` (delete the moved members)

- [ ] **Step 1: Create the new partial with the moved members (verbatim)**

Create `Assets/Scripts/Main/ChatManager.MediaSend.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Media-attachment send concerns split out of ChatManager — keeps the
/// god-object trimmer and groups related behavior. Mirrors ChatManager.Outbox.cs
/// and ChatManager.BotState.cs. Houses optimistic staging, the per-kind cache
/// seed helpers, and (Task 5) the Wappi upload + reconcile coroutine.
/// </summary>
public partial class ChatManager
{
    /// <summary>
    /// Part "b" optimistic-staging for media attachments. Builds a
    /// MessageViewModel from the AttachmentPick + caption, pre-seeds the
    /// image/video thumbnail into MediaCacheManager under a synthetic
    /// "staged://" URL so existing bubble views render unchanged, then
    /// fires OnLiveMessagesReceived. Does NOT persist (no ChatHistoryCache,
    /// no Outbox) and does NOT upload to Wappi — part "c" replaces this body
    /// with the real upload + persist path.
    /// </summary>
    public void StageLocalMedia(AttachmentPick pick, string caption, Texture2D preloadedImage = null)
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
                vm.type = MessageType.Image;
                if (preloadedImage != null)
                {
                    vm.mediaUrl    = SeedImageCacheFromTexture(preloadedImage, tempId);
                    vm.aspectRatio = preloadedImage.height > 0
                                   ? (float)preloadedImage.width / preloadedImage.height
                                   : 1f;
                }
                else
                {
                    // Fallback: only reached if AttachmentPreviewScreen.LoadTextureFromFile
                    // returned null (decode failed). SeedImageCache will try the same path
                    // again and likely fail the same way — known performance cliff on the
                    // already-failing path. Part c's outbox-retry caller may legitimately
                    // call StageLocalMedia without a preloaded texture; revisit the
                    // double-decode shape then.
                    var img = SeedImageCache(pick.Path, tempId);
                    vm.mediaUrl    = img.syntheticUrl;
                    vm.aspectRatio = img.aspect;
                }
                break;

            case AttachmentKind.GalleryVideo:
                vm.type         = MessageType.Video;
                vm.thumbnailUrl = SeedVideoThumbCache(pick.Path, tempId);
                vm.videoUrl     = "file://" + pick.Path;
                var meta = ReadVideoMetadata(pick.Path);
                vm.aspectRatio  = meta.aspect;
                vm.duration     = meta.durationSec;
                break;

            case AttachmentKind.Document:
                vm.type = MessageType.Document;
                // Non-empty mediaUrl bypasses MessageItemView's isMissing guard at line 615.
                // The staged:// URL is a placeholder — document bubbles don't decode it, only
                // check it's non-empty. Tap-to-open won't work in part b (no real upload yet)
                // but the bubble visually renders with the file icon + name + size.
                //
                // The document path at MessageItemView.cs:616 also checks
                //   isLinkExpired = vm.expireTime > 0 && vm.expireTime < now
                // which evaluates to false because vm.expireTime stays at 0 here. If a
                // future change defaults vm.expireTime to "now" for staged messages
                // (e.g. for UI timestamp display), the needsDownload guard would flip
                // true and re-break this. Keep expireTime at 0 for staged documents.
                vm.mediaUrl = $"staged://document/{tempId}";
                break;
        }

        OnLiveMessagesReceived?.Invoke(new System.Collections.Generic.List<MessageViewModel> { vm });
    }

    private (string syntheticUrl, float aspect) SeedImageCache(string localPath, string tempId)
    {
        string syntheticUrl = $"staged://image/{tempId}";
        Texture2D tex = null;
        try
        {
            // NativeGallery.LoadImageAtPath decodes HEIC → RGBA natively on iOS.
            // markTextureNonReadable: false so we can EncodeToJPG below.
            tex = NativeGallery.LoadImageAtPath(localPath,
                                                markTextureNonReadable: false,
                                                generateMipmaps: false);
            if (tex == null)
            {
                Debug.LogWarning($"[ChatManager] SeedImageCache: LoadImageAtPath returned null for {localPath}");
                return (syntheticUrl, 1.0f);
            }

            // Re-encode as JPEG so MediaCacheManager's downstream Texture2D.LoadImage
            // (which is JPG/PNG only) reads it back successfully — even if the source
            // file was HEIC.
            byte[] jpgBytes = tex.EncodeToJPG(90);
            string targetPath = MediaCacheManager.Instance.GetFilePathFromUrl(syntheticUrl);
            System.IO.File.WriteAllBytes(targetPath, jpgBytes);

            float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1.0f;
            return (syntheticUrl, aspect);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatManager] SeedImageCache failed for {localPath}: {ex.Message}");
            return (syntheticUrl, 1.0f);
        }
        finally
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    private string SeedImageCacheFromTexture(Texture2D tex, string tempId)
    {
        string syntheticUrl = $"staged://image/{tempId}";
        if (tex == null) return syntheticUrl;
        if (MediaCacheManager.Instance == null)
        {
            Debug.LogWarning("[ChatManager] SeedImageCacheFromTexture: MediaCacheManager.Instance is null");
            return syntheticUrl;
        }
        try
        {
            // Re-encode as JPEG so MediaCacheManager's downstream Texture2D.LoadImage
            // (JPG/PNG only) reads it back successfully. Same scheme as SeedImageCache,
            // but skips the file-path decode entirely because the caller already has
            // a decoded Texture2D in hand.
            byte[] jpgBytes = tex.EncodeToJPG(90);
            string targetPath = MediaCacheManager.Instance.GetFilePathFromUrl(syntheticUrl);
            System.IO.File.WriteAllBytes(targetPath, jpgBytes);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatManager] SeedImageCacheFromTexture failed: {ex.Message}");
        }
        return syntheticUrl;
    }

    private string SeedVideoThumbCache(string localPath, string tempId)
    {
        string syntheticUrl = $"thumb://staged/{tempId}";
        Texture2D thumb = null;
        try
        {
            thumb = NativeGallery.GetVideoThumbnail(localPath);
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
}
```

- [ ] **Step 2: Delete the moved members from `ChatManager.cs`**

In `Assets/Scripts/Main/ChatManager.cs`, delete the block from the doc-comment above `StageLocalMedia` through the end of `ReadVideoMetadata` (lines 1457-1642). The deletion starts at:

```csharp
    /// <summary>
    /// Part "b" optimistic-staging for media attachments. Builds a
```

and ends at the close of `ReadVideoMetadata`:

```csharp
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

Leave `MarkChatAsRead` (which immediately follows) and `PostTextMessageRoutine` (which precedes) intact. The `WappiSendTextRequest` / `WappiSendTextResponse` classes at the bottom of the file stay put.

- [ ] **Step 3: Verify clean compile (no behavior change)**

Return to Unity, let it recompile. Expected: **zero** Console errors. Specifically, no `CS0111` ("type already defines a member called 'StageLocalMedia'") — that error would mean the old copy wasn't fully deleted. Confirm `git diff --stat` shows `ChatManager.cs` shrunk by ~186 lines and `ChatManager.MediaSend.cs` added.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.MediaSend.cs Assets/Scripts/Main/ChatManager.cs
git commit -m "$(cat <<'EOF'
refactor(chat): relocate StageLocalMedia + seed helpers to ChatManager.MediaSend.cs

Behavior-preserving move ahead of the part-c upload tail.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Add the upload tail + `PostMediaMessageRoutine` + `LastMessagePreview`

Now `StageLocalMedia` gains the text-parity tail (persist + last-message + outbox enqueue + dispatch), and we add the network/reconcile coroutine. All edits are in `ChatManager.MediaSend.cs` (created in Task 4). No automated test — verified by clean compile here and by the manual device checklist at the end.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.MediaSend.cs`

- [ ] **Step 1: Expand the `using` block**

In `ChatManager.MediaSend.cs`, replace the top `using` block:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
```

with:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
```

- [ ] **Step 2: Update the `StageLocalMedia` doc-comment (it now persists + uploads)**

The doc-comment moved verbatim in Task 4 still says the method does NOT persist and does NOT upload — accurate between Task 4 and now, but this task makes it false. Replace the summary block directly above `public void StageLocalMedia(...)`:

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
```

with:

```csharp
    /// <summary>
    /// Optimistic media-attachment send (text-path parity). Builds a
    /// MessageViewModel from the AttachmentPick + caption, pre-seeds the
    /// image/video thumbnail into MediaCacheManager under a synthetic
    /// "staged://" URL so existing bubble views render unchanged, persists to
    /// ChatHistoryCache, enqueues a media Outbox entry (tap-to-retry + survives
    /// reopen), fires OnLiveMessagesReceived for the optimistic bubble, then
    /// dispatches PostMediaMessageRoutine to upload to Wappi and reconcile the
    /// temp id → real message_id. Mirrors SendTextMessageRoutine.
    /// </summary>
```

- [ ] **Step 3: Add the profile snapshot + cache-root snapshot at the top of `StageLocalMedia`**

Replace the two guard lines + the `tempId`/`now` lines at the start of `StageLocalMedia`:

```csharp
        if (string.IsNullOrEmpty(currentChatId)) return;
        if (pick == null || string.IsNullOrEmpty(pick.Path)) return;

        string tempId = "staging_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
```

with (insert the snapshot + profile guard before `tempId`):

```csharp
        if (string.IsNullOrEmpty(currentChatId)) return;
        if (pick == null || string.IsNullOrEmpty(pick.Path)) return;

        // Snapshot the originating bot's cache root + profile BEFORE any work so the
        // upload/reconcile lands in the bot the media was sent on, even across a
        // mid-send bot switch (mirrors SendTextMessageRoutine).
        string sendCacheRoot   = GetCacheRoot();
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            Debug.LogWarning("[ChatManager] StageLocalMedia aborted: no valid profile for active bot.");
            return;
        }

        string tempId = "staging_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
```

- [ ] **Step 4: Replace the lone `OnLiveMessagesReceived` tail with the full persist + enqueue + dispatch tail**

At the end of `StageLocalMedia`, replace this single line:

```csharp
        OnLiveMessagesReceived?.Invoke(new System.Collections.Generic.List<MessageViewModel> { vm });
    }
```

with:

```csharp
        // ---- persist (parity with SendTextMessageRoutine) ----
        List<MessageViewModel> cachedList = ChatHistoryCache.LoadHistory(sendCacheRoot, currentChatId);
        cachedList.Add(vm);
        ChatHistoryCache.SaveHistory(sendCacheRoot, currentChatId, cachedList);

        var chatVm = GetChat(currentChatId);
        if (chatVm != null) chatVm.UpdateLastMessage(LastMessagePreview(pick, caption), now);

        // ---- enqueue media outbox entry (tap-to-retry + survives reopen) ----
        // Image byte source is the staged JPEG already on disk in persistentDataPath;
        // video/document point at the original picked path (Light retry durability).
        string mediaPath = (vm.type == MessageType.Image)
            ? MediaCacheManager.Instance.GetFilePathFromUrl(vm.mediaUrl)
            : pick.Path;

        Outbox.Add(new OutboxStore.OutboxEntry
        {
            tempId         = tempId,
            chatId         = currentChatId,
            text           = caption ?? "",
            timestamp      = now,
            attemptCount   = 1,
            profileId      = activeProfileId,
            kind           = (int)OutboxKind.Media,
            attachmentKind = (int)pick.Kind,
            mediaPath      = mediaPath,
            mimeType       = pick.MimeType,
            fileName       = pick.FileName,
            mediaUrl       = vm.mediaUrl,
            thumbnailUrl   = vm.thumbnailUrl,
            videoUrl       = vm.videoUrl,
            aspectRatio    = vm.aspectRatio,
            duration       = vm.duration
        });

        // ---- optimistic UI (unchanged from part b) ----
        OnLiveMessagesReceived?.Invoke(new List<MessageViewModel> { vm });

        // ---- network half on Manager.Instance (bot-switch safe), like SendTextMessage ----
        OutboxStore.OutboxEntry entry = Outbox.Find(tempId);
        MonoBehaviour runner = Manager.Instance != null ? (MonoBehaviour)Manager.Instance : this;
        runner.StartCoroutine(PostMediaMessageRoutine(entry, sendCacheRoot));
    }

    /// <summary>
    /// Chat-list preview text for a staged attachment: a non-empty caption wins,
    /// else a kind label. New helper — the text path passes its text straight into
    /// UpdateLastMessage, so there was no existing formatter to reuse.
    /// </summary>
    private static string LastMessagePreview(AttachmentPick pick, string caption)
    {
        if (!string.IsNullOrEmpty(caption)) return caption;
        switch (pick.Kind)
        {
            case AttachmentKind.GalleryVideo: return "🎞 Video";
            case AttachmentKind.Document:     return "📄 " + (string.IsNullOrEmpty(pick.FileName) ? "Document" : pick.FileName);
            default:                          return "📷 Photo";
        }
    }

    /// <summary>
    /// Network half of an outgoing media send. Shared by the initial optimistic
    /// send (StageLocalMedia) and tap-to-retry (RetryRoutine). Encodes the file
    /// off-thread, POSTs to the kind-specific Wappi endpoint, and reconciles the
    /// temp id → real message_id exactly like PostTextMessageRoutine. Fires
    /// OnMessageStatusChanged on both success and failure; does NOT own outbox
    /// lifecycle beyond the success RemoveAt (callers own the rest).
    /// </summary>
    private IEnumerator PostMediaMessageRoutine(OutboxStore.OutboxEntry entry, string sendCacheRoot)
    {
        if (entry == null) yield break;

        var kind = (AttachmentKind)entry.attachmentKind;
        string url = WappiMediaRequestFactory.EndpointFor(kind, entry.profileId);
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError($"[Wappi] no media endpoint for kind {kind}");
            OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
            yield break;
        }

        // --- off-thread read + base64 (no frame hitch / no OOM stall on the main thread) ---
        var encodeTask = Base64Encoder.EncodeFileAsync(entry.mediaPath);
        yield return new WaitUntil(() => encodeTask.IsCompleted);
        if (encodeTask.IsFaulted || string.IsNullOrEmpty(encodeTask.Result))
        {
            Debug.LogError($"[Wappi] media encode failed for {entry.mediaPath}: {encodeTask.Exception?.Message}");
            OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
            yield break;
        }

        string body = WappiMediaRequestFactory.BuildBody(kind, entry.chatId, entry.text, entry.fileName, encodeTask.Result);

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        www.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Wappi] {url} failed: {www.error}\n{www.downloadHandler?.text}");
            OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
            yield break;
        }

        WappiSendTextResponse resp = null;
        try { resp = JsonConvert.DeserializeObject<WappiSendTextResponse>(www.downloadHandler.text); }
        catch (Exception ex) { Debug.LogError($"[Wappi] media response parse failed: {ex.Message}\n{www.downloadHandler.text}"); }

        if (resp != null && resp.status == "done" && !string.IsNullOrEmpty(resp.message_id))
        {
            // --- identical reconcile to PostTextMessageRoutine ---
            seenMessageIds.Remove(entry.tempId);
            seenMessageIds.Add(resp.message_id);

            List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(sendCacheRoot, entry.chatId);
            for (int i = 0; i < cached.Count; i++)
            {
                if (cached[i].messageId == entry.tempId)
                {
                    cached[i].messageId      = resp.message_id;
                    cached[i].deliveryStatus = DeliveryStatus.Sent;
                    break;
                }
            }
            ChatHistoryCache.SaveHistory(sendCacheRoot, entry.chatId, cached);

            Outbox.RemoveAt(sendCacheRoot, entry.chatId, entry.tempId);
            OnMessageStatusChanged?.Invoke(entry.tempId, resp.message_id, DeliveryStatus.Sent);
        }
        else
        {
            Debug.LogWarning($"[Wappi] media send returned non-done status: {www.downloadHandler.text}");
            OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
        }
    }
```

- [ ] **Step 5: Verify clean compile**

Return to Unity, let it recompile. Expected: **zero** Console errors. `StageLocalMedia`, `LastMessagePreview`, and `PostMediaMessageRoutine` all live in `ChatManager.MediaSend.cs`; they reference `seenMessageIds`, `OnLiveMessagesReceived`, `OnMessageStatusChanged`, `GetActiveProfileId`, `GetCacheRoot`, `GetChat`, `Outbox`, `Manager.wappiAuthToken` — all already defined on the `ChatManager` partial / `Manager`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.MediaSend.cs
git commit -m "$(cat <<'EOF'
feat(chat): upload staged media to Wappi with full text-send parity

StageLocalMedia now persists to ChatHistoryCache, enqueues a media Outbox
entry, and dispatches PostMediaMessageRoutine (off-thread base64 + kind-routed
img/video/document send) which swaps the temp id for the real message_id.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Branch `RetryRoutine` on `entry.kind`

So tapping a failed media bubble re-runs the media POST instead of the text POST.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.Outbox.cs:43-57`

- [ ] **Step 1: Replace the `RetryRoutine` body**

In `Assets/Scripts/Main/ChatManager.Outbox.cs`, replace:

```csharp
    private IEnumerator RetryRoutine(string tempId, OutboxStore.OutboxEntry entry)
    {
        // Snapshot the cache root BEFORE any yield, mirroring SendTextMessageRoutine
        // (line 694) so a same-frame bot switch can't redirect the retry's
        // cache write to the wrong bot's folder.
        string retryCacheRoot = GetCacheRoot();
        try
        {
            yield return PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId, retryCacheRoot);
        }
        finally
        {
            _retriesInFlight.Remove(tempId);
        }
    }
```

with:

```csharp
    private IEnumerator RetryRoutine(string tempId, OutboxStore.OutboxEntry entry)
    {
        // Snapshot the cache root BEFORE any yield, mirroring SendTextMessageRoutine
        // so a same-frame bot switch can't redirect the retry's cache write to the
        // wrong bot's folder.
        string retryCacheRoot = GetCacheRoot();
        try
        {
            if (entry.kind == (int)OutboxKind.Media)
                yield return PostMediaMessageRoutine(entry, retryCacheRoot);
            else
                yield return PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId, retryCacheRoot);
        }
        finally
        {
            _retriesInFlight.Remove(tempId);
        }
    }
```

- [ ] **Step 2: Verify clean compile**

Return to Unity, let it recompile. Expected: **zero** Console errors. `PostMediaMessageRoutine` and `OutboxKind` resolve (defined in Tasks 5 and 3).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.Outbox.cs
git commit -m "$(cat <<'EOF'
feat(chat): route media outbox retries through PostMediaMessageRoutine

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Pre-stage video size guard + runtime `ShowSizeError` label

Reject over-cap videos before staging, with non-silent feedback. The error label is **created at runtime inside `AttachmentPreviewScreen`** — no builder change, no new `[SerializeField]` (the editor builders are out of scope).

**Files:**
- Modify: `Assets/Scripts/Chat/AttachmentPreviewScreen.cs`

- [ ] **Step 1: Add the size cap + guard at the top of `OnSendTapped`**

In `Assets/Scripts/Chat/AttachmentPreviewScreen.cs`, the current `OnSendTapped` begins:

```csharp
    private void OnSendTapped()
    {
        if (_currentPick == null) return;
        if (sendButton != null) sendButton.interactable = false;

        string caption = captionField != null ? (captionField.text ?? "").Trim() : "";
        var pick = _currentPick;
        var preloadedImage = _currentPreviewTexture;   // may be null for video / document
```

Replace that opening with (insert the guard right after the null-pick check, before disabling Send):

```csharp
    private const long MaxVideoUploadBytes = 16L * 1024 * 1024;   // WhatsApp-like; tunable

    private void OnSendTapped()
    {
        if (_currentPick == null) return;

        // Reject over-cap video BEFORE staging. pick.FileSizeBytes is already
        // populated by the picker (used for the document size label), so no extra I/O.
        if (_currentPick.Kind == AttachmentKind.GalleryVideo &&
            _currentPick.FileSizeBytes > MaxVideoUploadBytes)
        {
            ShowSizeError($"Video is too large to send (max {MaxVideoUploadBytes / (1024 * 1024)} MB).");
            if (sendButton != null) sendButton.interactable = true;   // let the user go Back and re-pick
            return;                                                   // do NOT stage, do NOT close
        }

        if (sendButton != null) sendButton.interactable = false;

        string caption = captionField != null ? (captionField.text ?? "").Trim() : "";
        var pick = _currentPick;
        var preloadedImage = _currentPreviewTexture;   // may be null for video / document
```

(The rest of `OnSendTapped` — the `StageLocalMedia` call, caption-field deactivate, `Close()` — is unchanged.)

- [ ] **Step 2: Add the runtime `ShowSizeError` label + its auto-hide**

Add these fields next to the other private fields (near line 46-50, after `_videoPreviewFitter`):

```csharp
    private TextMeshProUGUI _sizeErrorLabel;
    private Tween           _sizeErrorTween;
```

Add these methods (anywhere in the class body, e.g. just after `ReleasePreviewTexture`):

```csharp
    /// <summary>
    /// Shows a transient error line on the preview (e.g. "video too large").
    /// The label is created lazily in code so this needs no builder change and
    /// no serialized reference. Auto-fades after a few seconds.
    /// </summary>
    private void ShowSizeError(string message)
    {
        Debug.LogWarning($"[AttachmentPreviewScreen] {message}");
        EnsureSizeErrorLabel();
        if (_sizeErrorLabel == null) return;

        _sizeErrorLabel.text = message;
        _sizeErrorLabel.gameObject.SetActive(true);
        _sizeErrorLabel.alpha = 1f;

        _sizeErrorTween?.Kill();
        _sizeErrorTween = DOTween.Sequence()
            .AppendInterval(2.5f)
            .Append(DOTween.To(() => _sizeErrorLabel.alpha, v => _sizeErrorLabel.alpha = v, 0f, 0.3f))
            .OnComplete(() => { if (_sizeErrorLabel != null) _sizeErrorLabel.gameObject.SetActive(false); });
    }

    private void EnsureSizeErrorLabel()
    {
        if (_sizeErrorLabel != null) return;
        if (bottomBarRect == null) return;   // built hierarchy missing; skip gracefully

        var go = new GameObject("SizeErrorLabel", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(bottomBarRect, worldPositionStays: false);
        // Stretch horizontally, sit just above the bottom bar, in the thumb zone.
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(16f, 8f);
        rt.offsetMax = new Vector2(-16f, 44f);

        _sizeErrorLabel = go.AddComponent<TextMeshProUGUI>();
        _sizeErrorLabel.fontSize          = 14f;
        _sizeErrorLabel.color             = new Color(1f, 0.42f, 0.42f, 1f);  // soft red
        _sizeErrorLabel.alignment         = TextAlignmentOptions.Center;
        _sizeErrorLabel.raycastTarget     = false;
        _sizeErrorLabel.textWrappingMode  = TextWrappingModes.Normal;
        go.SetActive(false);
    }
```

- [ ] **Step 3: Kill the tween on disable (avoid a tween writing to a destroyed label)**

In `OnDisable`, the current body is:

```csharp
    void OnDisable()
    {
        if (attachSheet != null) attachSheet.OnPicked -= Show;
        if (sendButton  != null) sendButton.onClick.RemoveListener(OnSendTapped);
        if (backButton  != null) backButton.onClick.RemoveListener(OnBackTapped);

        _fadeTween?.Kill();
        ReleasePreviewTexture();
        _currentPick = null;
    }
```

Replace it with (add the `_sizeErrorTween?.Kill();` line):

```csharp
    void OnDisable()
    {
        if (attachSheet != null) attachSheet.OnPicked -= Show;
        if (sendButton  != null) sendButton.onClick.RemoveListener(OnSendTapped);
        if (backButton  != null) backButton.onClick.RemoveListener(OnBackTapped);

        _fadeTween?.Kill();
        _sizeErrorTween?.Kill();
        ReleasePreviewTexture();
        _currentPick = null;
    }
```

- [ ] **Step 4: Verify clean compile**

Return to Unity, let it recompile. Expected: **zero** Console errors. `TextWrappingModes` / `TextAlignmentOptions` resolve via the existing `using TMPro;` at the top of the file; `DOTween` via `using DG.Tweening;`.

> If the installed TMP version predates `textWrappingMode` (a newer TMP API) and the Console flags it, fall back to the deprecated `_sizeErrorLabel.enableWordWrapping = true;` instead — functionally identical for this single-line label.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/AttachmentPreviewScreen.cs
git commit -m "$(cat <<'EOF'
feat(chat): guard over-cap video before staging with inline error label

Rejects >16 MB video pre-stage and surfaces a transient, runtime-built
error line on the preview (no builder change). Keeps the preview open.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification

- [ ] **All EditMode tests green.** Test Runner → EditMode → Run All. Expected: `Base64EncoderTests` (4), `WappiMediaRequestFactoryTests` (8), `OutboxEntryMediaCompatTests` (2), and the pre-existing `AttachmentDisplayFormatTests` all PASS.

- [ ] **Manual / device checklist** (cannot be automated — requires a real WhatsApp profile; from spec §12):
  - Send a real **image**, **video**, and **document** to a test number. Recipient receives each with the caption; the sender bubble flips `Pending → Sent` and the cached id becomes the real `message_id`.
  - **Airplane mode** → bubble goes `Failed` (red). Restore network, tap the bubble → retry → `Sent`.
  - **Reopen the chat** while a send is in flight → the bubble is present (persisted to `ChatHistoryCache`) and resolves to `Sent`.
  - **Switch bots** immediately after tapping Send → the result lands in the originating bot's chat, not the newly-active one.
  - **Over-cap video** (>16 MB) → inline red error appears, preview stays open, no bubble created, no upload.
  - **No duplicate bubble** when the message later returns via background sync (the `seenMessageIds` swap covers this).
  - **In-session document/video retry** works; after an app restart where the OS purged the picked file, retry resolves to `Failed` gracefully (no crash) — image retry always works (staged JPEG is in `persistentDataPath`).

---

## Spec coverage map

| Spec section | Task |
|---|---|
| §6 `Base64Encoder` | Task 1 |
| §6 `WappiMediaRequestFactory` + `WappiSendMediaRequest` | Task 2 |
| §7 `OutboxEntry` extension + `OutboxKind` | Task 3 |
| §6 relocate to `ChatManager.MediaSend.cs` | Task 4 |
| §9.1 `StageLocalMedia` tail + `LastMessagePreview`; §9.2 `PostMediaMessageRoutine` | Task 5 |
| §10 `RetryRoutine` branch | Task 6 |
| §8 pre-stage video size guard + feedback surface | Task 7 |
| §12 tests | Tasks 1-3 (EditMode) + Final verification (manual) |
