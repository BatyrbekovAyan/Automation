# Outgoing Video Upload Progress Ring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ring the outgoing video bubble's play button with a WhatsApp-style radial progress indicator that fills 0→100% across convert + encode + upload, with a center cancel (X) that aborts the in-flight send and removes the bubble.

**Architecture:** Progress and cancel are event-driven through `ChatManager` (two new events) and wired into the single shared send coroutine `PostMediaMessageRoutine`, so both the optimistic send and tap-to-retry get them for free. A pure, unit-tested `SendProgress(phase, sub)` maps each pipeline phase onto one continuous 0..1. The bubble view (`MessageItemView`) subscribes to progress, shows a procedurally-generated ring + X sprite (no asset files), and self-destructs on a new `OnMessageRemoved` event. One editor builder adds the ring + cancel nodes to the outgoing prefab.

**Tech Stack:** Unity 6 (6000.3.9f1) C#, coroutines + `UnityWebRequest`, `Image` Filled/Radial360, DOTween (`DOFillAmount`), NUnit EditMode tests, `[MenuItem]` editor builder.

---

## Environment notes (read before executing)

- **Tests + compile run in the user's open Unity Editor**, not from CLI. Wherever a step says "Run test" or "verify compile," that means: switch to Unity, let it recompile, and use **Window ▸ General ▸ Test Runner ▸ EditMode**. Do not invent a `dotnet test` / `Unity -batchmode` command.
- **Commits require per-task consent.** Each task ends with a commit step — ask the user before running it. Stage the `.cs` file **and** its Unity-generated `.meta` (new files only generate a `.meta` after Unity imports them, so import in the Editor first).
- **`MessageItemView` is shared by the incoming AND outgoing prefabs.** Every new serialized ref (`uploadRing`, `cancelButton`, `cancelIcon`, `playIconObject`) will be **null on the incoming prefab**. All new code paths must null-guard these refs so incoming bubbles are unaffected. The editor builder (Task 7) only touches `MessageTextOutgoing.prefab`.
- **`SendProgress` / `SendPhase` are `public`** (not `internal`). The EditMode test compiles into `Assembly-CSharp-Editor`, a separate assembly from the runtime `Assembly-CSharp`; `internal` members would be invisible to it. Confirmed against the existing `AttachmentDisplayFormat` (public, tested the same way).

## File map

| File | Change |
|---|---|
| `Assets/Scripts/Main/ChatManager.MediaSend.cs` | Add `SendPhase` enum + `SendProgress` pure fn; `MediaSendContext` + `_inFlight` registry; progress + cancel checks in `PostMediaMessageRoutine`; `CancelMediaSend` + `TryDeleteTemp`. |
| `Assets/Scripts/Main/ChatManager.cs` | Add `OnMediaSendProgress` + `OnMessageRemoved` events. |
| `Assets/Scripts/Chat/VideoConverter.cs` | Add optional `Action<float> onProgress` to `Convert`. |
| `Assets/Scripts/UI/MessageListView.cs` | Subscribe `OnMessageRemoved`; add `HandleMessageRemoved`. |
| `Assets/Scripts/UI/MessageItemView.cs` | Add refs + procedural ring/X sprites + progress/cancel handlers + show/hide ring; hide on terminal status. |
| `Assets/Prefabs/MessageTextOutgoing.prefab` | Add `UploadRing` + `CancelButton` under `playOverlay` (via builder). |
| `Assets/Editor/UploadRingBuilder.cs` | **New.** One-shot `[MenuItem]` builder for the prefab nodes. |
| `Assets/Tests/Editor/Chat/MediaSendProgressTests.cs` | **New.** EditMode tests for `SendProgress`. |

---

## Task 1: `SendProgress` pure function + EditMode tests (TDD)

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.MediaSend.cs` (add enum + static fn after `WappiVideoCapBytes`, ~line 18)
- Test: `Assets/Tests/Editor/Chat/MediaSendProgressTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/MediaSendProgressTests.cs`:

```csharp
using NUnit.Framework;

public class MediaSendProgressTests
{
    // Phase floors/ceilings on the whole-pipeline 0..1 axis (30 / 10 / 60 split).
    [TestCase(ChatManager.SendPhase.Convert, 0f, 0.00f)]
    [TestCase(ChatManager.SendPhase.Convert, 1f, 0.30f)]
    [TestCase(ChatManager.SendPhase.Encode,  0f, 0.30f)]
    [TestCase(ChatManager.SendPhase.Encode,  1f, 0.40f)]
    [TestCase(ChatManager.SendPhase.Upload,  0f, 0.40f)]
    [TestCase(ChatManager.SendPhase.Upload,  1f, 1.00f)]
    public void SendProgress_PhaseBoundaries_MapToPipelineFractions(
        ChatManager.SendPhase phase, float sub, float expected)
    {
        Assert.AreEqual(expected, ChatManager.SendProgress(phase, sub), 1e-4f);
    }

    [TestCase(ChatManager.SendPhase.Convert, -0.5f, 0.00f)] // clamps low
    [TestCase(ChatManager.SendPhase.Convert,  2.0f, 0.30f)] // clamps high
    [TestCase(ChatManager.SendPhase.Upload,  -1.0f, 0.40f)]
    [TestCase(ChatManager.SendPhase.Upload,   5.0f, 1.00f)]
    public void SendProgress_ClampsSubProgress(
        ChatManager.SendPhase phase, float sub, float expected)
    {
        Assert.AreEqual(expected, ChatManager.SendProgress(phase, sub), 1e-4f);
    }

    [Test]
    public void SendProgress_IsMonotonicAcrossPhases()
    {
        float convertMid = ChatManager.SendProgress(ChatManager.SendPhase.Convert, 0.5f);
        float convertEnd = ChatManager.SendProgress(ChatManager.SendPhase.Convert, 1f);
        float encodeEnd  = ChatManager.SendProgress(ChatManager.SendPhase.Encode, 1f);
        float uploadMid  = ChatManager.SendProgress(ChatManager.SendPhase.Upload, 0.5f);
        float uploadEnd  = ChatManager.SendProgress(ChatManager.SendPhase.Upload, 1f);

        Assert.Less(convertMid, convertEnd);
        Assert.LessOrEqual(convertEnd, encodeEnd);
        Assert.Less(encodeEnd, uploadMid);
        Assert.Less(uploadMid, uploadEnd);
        Assert.AreEqual(1f, uploadEnd, 1e-4f);
    }

    [Test]
    public void SendProgress_UnknownPhase_ReturnsZero()
    {
        Assert.AreEqual(0f, ChatManager.SendProgress((ChatManager.SendPhase)999, 0.5f), 1e-4f);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

In Unity: let it import the new file, then **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All**.
Expected: the `MediaSendProgress*` tests fail to compile / are missing — `ChatManager.SendProgress` and `ChatManager.SendPhase` do not exist yet.

- [ ] **Step 3: Write the minimal implementation**

In `Assets/Scripts/Main/ChatManager.MediaSend.cs`, insert directly after the `WappiVideoCapBytes` const (after line 18):

```csharp
    /// <summary>
    /// Send-pipeline phase. Convert (iOS transcode) and Upload are the slow,
    /// measurable phases; Encode is a fast opaque base64 Task that snaps.
    /// Public so the EditMode test assembly (Assembly-CSharp-Editor) can see it.
    /// </summary>
    public enum SendPhase { Convert, Encode, Upload }

    /// <summary>
    /// Maps (phase, intra-phase 0..1) onto the whole-pipeline 0..1 fill the ring
    /// shows: Convert 0→0.30, Encode 0.30→0.40, Upload 0.40→1.00. Pure + public
    /// for unit testing; touches no Unity state.
    /// </summary>
    public static float SendProgress(SendPhase phase, float sub) => phase switch
    {
        SendPhase.Convert => 0.00f + 0.30f * Mathf.Clamp01(sub),
        SendPhase.Encode  => 0.30f + 0.10f * Mathf.Clamp01(sub),
        SendPhase.Upload  => 0.40f + 0.60f * Mathf.Clamp01(sub),
        _ => 0f,
    };
```

- [ ] **Step 4: Run the test to verify it passes**

In Unity: recompile, then **Test Runner ▸ EditMode ▸ Run All**.
Expected: all `MediaSendProgress*` tests PASS (14 cases).

- [ ] **Step 5: Commit** *(ask for consent first)*

```bash
git add Assets/Scripts/Main/ChatManager.MediaSend.cs \
        Assets/Tests/Editor/Chat/MediaSendProgressTests.cs \
        Assets/Tests/Editor/Chat/MediaSendProgressTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(chat): add SendProgress phase→pipeline mapping + EditMode tests

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Two new `ChatManager` events

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (after the `OnMessageMediaRefreshed` declaration, line 123)

- [ ] **Step 1: Add the events**

In `Assets/Scripts/Main/ChatManager.cs`, insert after line 123 (the `OnMessageMediaRefreshed` event), before `public event Action<string> OnActiveBotChanged;`:

```csharp

    /// <summary>
    /// Fires repeatedly during an outgoing media send with whole-pipeline
    /// progress in 0..1 (see SendProgress). tempId matches the optimistic
    /// bubble's MessageViewModel.messageId until the server ack swaps it.
    /// Video bubbles render this as a radial ring; other kinds ignore it.
    /// </summary>
    public event Action<string, float> OnMediaSendProgress;

    /// <summary>
    /// Fires when a single message must be removed from the open transcript
    /// (currently only a cancelled in-flight media send). Carries the bubble's
    /// current messageId (the send tempId). Fills the gap left by there being
    /// no per-message removal — OnLiveMessagesReceived only ever adds.
    /// </summary>
    public event Action<string> OnMessageRemoved;
```

- [ ] **Step 2: Verify compile**

In Unity: recompile. Expected: Console shows 0 errors (unused events warn-free; C# events are fine undeclared-unused).

- [ ] **Step 3: Commit** *(ask for consent first)*

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "$(cat <<'EOF'
feat(chat): add OnMediaSendProgress + OnMessageRemoved events

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Surface convert progress from `VideoConverter`

**Files:**
- Modify: `Assets/Scripts/Chat/VideoConverter.cs` (the `Convert` coroutine, lines 28-57)

- [ ] **Step 1: Add the `onProgress` parameter and invocations**

Replace the entire `Convert` method (lines 28-57) with:

```csharp
    public static IEnumerator Convert(string inputPath, string outputPath, long maxBytes,
                                      Action<string> onResult, Action<string> onError,
                                      Action<float> onProgress = null)
    {
#if UNITY_IOS && !UNITY_EDITOR
        int jobId = _StartVideoConvert(inputPath, outputPath, maxBytes);
        int status = _PollVideoConvert(jobId);
        while (status == 0)
        {
            onProgress?.Invoke(_PollVideoConvertProgress(jobId));
            yield return null;
            status = _PollVideoConvert(jobId);
        }

        if (status == 2)
        {
            string message = Marshal.PtrToStringAnsi(_VideoConvertError(jobId)) ?? "video conversion failed";
            _FreeVideoConvertJob(jobId);
            onError?.Invoke(message);
            yield break;
        }

        // status 1 = converted to outputPath; status 3 = already deliverable, use original.
        onProgress?.Invoke(1f);
        string resolved = status == 3 ? inputPath : outputPath;
        _FreeVideoConvertJob(jobId);
        onResult?.Invoke(resolved);
#else
        // Editor + Android: no native converter — convert is instant, so snap the
        // ring to the convert ceiling, then pass the original through unchanged.
        onProgress?.Invoke(1f);
        onResult?.Invoke(inputPath);
        yield break;
#endif
    }
```

- [ ] **Step 2: Verify compile**

In Unity: recompile. Expected: 0 errors. (The existing call site in `PostMediaMessageRoutine` still compiles — `onProgress` is optional.)

- [ ] **Step 3: Commit** *(ask for consent first)*

```bash
git add Assets/Scripts/Chat/VideoConverter.cs
git commit -m "$(cat <<'EOF'
feat(chat): surface native convert progress via VideoConverter onProgress

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Progress + cancel in `PostMediaMessageRoutine` + `CancelMediaSend`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.MediaSend.cs`

This task adds an in-flight registry, wires progress events into all three phases, checks a `cancelled` flag at every phase boundary, converts the upload to a non-blocking poll, and adds the public `CancelMediaSend` entry point. Verification is by compile + manual (the upload/cancel paths need a device — covered in Task 8); there is no new EditMode test here because `UnityWebRequest` and the native converter don't run in EditMode.

- [ ] **Step 1: Add the in-flight registry**

In `Assets/Scripts/Main/ChatManager.MediaSend.cs`, insert directly after the `SendProgress` method you added in Task 1:

```csharp
    /// <summary>
    /// Tracks a running media send so CancelMediaSend can reach into it.
    /// request is non-null only during the upload phase (so Abort() can kill the
    /// in-flight POST); cancelled is set by CancelMediaSend and checked at every
    /// phase boundary so we stop without firing a Failed tick.
    /// </summary>
    private sealed class MediaSendContext
    {
        public UnityWebRequest request;
        public bool cancelled;
    }

    private readonly Dictionary<string, MediaSendContext> _inFlight = new();
```

- [ ] **Step 2: Replace `PostMediaMessageRoutine` with the progress + cancel version**

Replace the entire method (lines 213-332) with:

```csharp
    private IEnumerator PostMediaMessageRoutine(OutboxStore.OutboxEntry entry, string sendCacheRoot)
    {
        if (entry == null) yield break;

        var ctx = new MediaSendContext();
        _inFlight[entry.tempId] = ctx;
        try
        {
            var kind = (AttachmentKind)entry.attachmentKind;
            string url = WappiMediaRequestFactory.EndpointFor(kind, entry.profileId);
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError($"[Wappi] no media endpoint for kind {kind}");
                OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
                yield break;
            }

            // --- video: ensure MP4/H.264 before upload (Wappi/WhatsApp reject .mov/HEVC) ---
            string uploadPath = entry.mediaPath;
            string convertedTemp = null;   // the temp .mp4 written this attempt, if any (deleted on success)
            if (kind == AttachmentKind.GalleryVideo)
            {
                string convertedPath = System.IO.Path.Combine(Application.temporaryCachePath, $"send_{entry.tempId}.mp4");
                bool   convertOk     = false;
                string convertResult = null;
                string convertErr    = null;
                yield return VideoConverter.Convert(entry.mediaPath, convertedPath, WappiVideoCapBytes,
                    r => { convertOk = true; convertResult = r; },
                    e => { convertErr = e; },
                    p => OnMediaSendProgress?.Invoke(entry.tempId, SendProgress(SendPhase.Convert, p)));

                // Cancelled mid-convert: the native job can't be hard-killed, so we drop
                // its result here and let CancelMediaSend handle bubble/temp cleanup.
                if (ctx.cancelled) yield break;

                if (!convertOk || string.IsNullOrEmpty(convertResult))
                {
                    Debug.LogError($"[Wappi] video convert failed for {entry.mediaPath}: {convertErr}");
                    OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
                    yield break;
                }

                uploadPath = convertResult;
                convertedTemp = uploadPath != entry.mediaPath ? uploadPath : null;

                if (!System.IO.File.Exists(uploadPath))
                {
                    Debug.LogError($"[Wappi] converted file missing at {uploadPath}");
                    OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
                    yield break;
                }

                long convertedBytes = new System.IO.FileInfo(uploadPath).Length;
                if (convertedBytes > WappiVideoCapBytes)
                {
                    Debug.LogWarning($"[Wappi] video still {convertedBytes} bytes after conversion (cap {WappiVideoCapBytes}); failing send");
                    OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
                    yield break;
                }
            }

            // --- off-thread read + base64 (no frame hitch / no OOM stall on the main thread) ---
            var encodeTask = Base64Encoder.EncodeFileAsync(uploadPath);
            yield return new WaitUntil(() => encodeTask.IsCompleted);
            if (ctx.cancelled) yield break;   // discard the encode result on cancel
            if (encodeTask.IsFaulted || string.IsNullOrEmpty(encodeTask.Result))
            {
                Debug.LogError($"[Wappi] media encode failed for {entry.mediaPath}: {encodeTask.Exception?.Message}");
                OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
                yield break;
            }
            // Encode is opaque (one Task), so the ring snaps to the encode ceiling.
            OnMediaSendProgress?.Invoke(entry.tempId, SendProgress(SendPhase.Encode, 1f));

            string body = WappiMediaRequestFactory.BuildBody(kind, entry.chatId, entry.text, entry.fileName, encodeTask.Result);

            using UnityWebRequest www = new UnityWebRequest(url, "POST");
            www.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
            www.timeout = 300;   // media uploads carry multi-MB base64 bodies; 30s (text default) is too short

            // Non-blocking upload poll so we can surface byte-level progress and let
            // CancelMediaSend Abort() the request mid-flight.
            ctx.request = www;
            var op = www.SendWebRequest();
            while (!op.isDone)
            {
                OnMediaSendProgress?.Invoke(entry.tempId, SendProgress(SendPhase.Upload, www.uploadProgress));
                yield return null;
            }
            ctx.request = null;

            // A cancel during upload Abort()s www (result becomes ConnectionError); the
            // cancelled flag distinguishes "user cancelled" from a real network failure so
            // we don't flash a Failed tick on a bubble that's about to be removed.
            if (ctx.cancelled) yield break;

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
                if (convertedTemp != null)
                {
                    try { System.IO.File.Delete(convertedTemp); } catch { /* best-effort cleanup */ }
                }
                OnMessageStatusChanged?.Invoke(entry.tempId, resp.message_id, DeliveryStatus.Sent);
            }
            else
            {
                Debug.LogWarning($"[Wappi] media send returned non-done status: {www.downloadHandler.text}");
                OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
            }
        }
        finally
        {
            // Unregister on every exit path (success, failure, cancel, exception).
            _inFlight.Remove(entry.tempId);
        }
    }
```

> **Note on the try/finally:** C# forbids `yield return` inside a `try` that has a `catch`, but allows it inside a `try`/`finally`. The outer block has only `finally`. The inner `try { resp = ... } catch { ... }` contains no `yield`, so it stays legal. The `using UnityWebRequest www` declaration compiles to its own try/finally (no catch) — also legal with yields.
>
> **Deliberate: no `StopCoroutine` on cancel.** The spec's edge-case prose mentions "stop the coroutine," but this plan intentionally does *not* — it lets the routine run to its next phase-boundary `if (ctx.cancelled) yield break;` instead. Hard-stopping mid-convert would skip `VideoConverter.Convert`'s `_FreeVideoConvertJob(jobId)` and leak the native AVAssetExportSession job. Letting the convert coroutine finish naturally (it frees the job, then we yield-break) is the cleaner cleanup. Do not "fix" this by adding `StopCoroutine`.

- [ ] **Step 3: Add `CancelMediaSend` + `TryDeleteTemp`**

In the same file, insert these methods directly after `PostMediaMessageRoutine` (before `SeedImageCache`):

```csharp
    /// <summary>
    /// Aborts an in-flight media send and removes its optimistic bubble + outbox
    /// entry entirely (WhatsApp-style cancel — no "cancelled" placeholder). Safe to
    /// call with an unknown/finished tempId (no-op). Called from the bubble's X button.
    /// </summary>
    public void CancelMediaSend(string tempId)
    {
        if (string.IsNullOrEmpty(tempId)) return;
        if (!_inFlight.TryGetValue(tempId, out var ctx)) return;   // already finished / never tracked

        ctx.cancelled = true;
        // Abort the upload immediately if we're mid-POST; the loop's cancelled check
        // then suppresses the Failed fire. Convert/encode can't be hard-killed — the
        // cancelled flag discards their result at the next phase boundary.
        ctx.request?.Abort();

        string cacheRoot = GetCacheRoot();
        var outboxEntry = Outbox.Find(tempId);
        string chatId = outboxEntry != null ? outboxEntry.chatId : currentChatId;

        // Remove from cache, outbox, and the seen-set so the message is gone everywhere.
        List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(cacheRoot, chatId);
        cached.RemoveAll(m => m.messageId == tempId);
        ChatHistoryCache.SaveHistory(cacheRoot, chatId, cached);
        Outbox.RemoveAt(cacheRoot, chatId, tempId);
        seenMessageIds.Remove(tempId);

        // Best-effort delete the staged source + any converted temp.
        TryDeleteTemp(System.IO.Path.Combine(Application.temporaryCachePath, $"staged_video_{tempId}.mov"));
        TryDeleteTemp(System.IO.Path.Combine(Application.temporaryCachePath, $"send_{tempId}.mp4"));

        OnMessageRemoved?.Invoke(tempId);
    }

    private static void TryDeleteTemp(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch (Exception ex) { Debug.LogWarning($"[ChatManager] temp delete failed for {path}: {ex.Message}"); }
    }
```

- [ ] **Step 4: Verify compile**

In Unity: recompile. Expected: 0 errors. Re-run **Test Runner ▸ EditMode** — the Task 1 tests still pass (no signature change to `SendProgress`).

- [ ] **Step 5: Commit** *(ask for consent first)*

```bash
git add Assets/Scripts/Main/ChatManager.MediaSend.cs
git commit -m "$(cat <<'EOF'
feat(chat): emit send progress + support cancel in media send routine

Adds an in-flight registry, fires OnMediaSendProgress across convert/encode/
upload, converts the upload to a non-blocking poll, and adds CancelMediaSend
which aborts + removes the bubble/outbox/temp files and fires OnMessageRemoved.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: `MessageListView` removes a cancelled bubble

**Files:**
- Modify: `Assets/Scripts/UI/MessageListView.cs` (OnEnable ~line 100, OnDisable ~line 126, add method near `HandleLiveMessages`)

- [ ] **Step 1: Subscribe / unsubscribe `OnMessageRemoved`**

In `OnEnable`, after line 100 (`ChatManager.Instance.OnLiveMessagesReceived += HandleLiveMessages;`), add:

```csharp
            ChatManager.Instance.OnMessageRemoved += HandleMessageRemoved;
```

In `OnDisable`, after line 126 (`ChatManager.Instance.OnLiveMessagesReceived -= HandleLiveMessages;`), add:

```csharp
            ChatManager.Instance.OnMessageRemoved -= HandleMessageRemoved;
```

- [ ] **Step 2: Add `HandleMessageRemoved`**

Insert directly after the `HandleLiveMessages` method (after line 366):

```csharp
    // Destroys the bubble for a cancelled in-flight send. There may be a single
    // match (the optimistic bubble), but we scan defensively. Mirrors the
    // clear-list destroy + ForceRebuild pattern used elsewhere in this view.
    void HandleMessageRemoved(string tempId)
    {
        if (string.IsNullOrEmpty(tempId) || content == null) return;

        bool removed = false;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            var view = child.GetComponent<MessageItemView>();
            if (view != null && view.BoundVm != null && view.BoundVm.messageId == tempId)
            {
                Destroy(child.gameObject);
                removed = true;
            }
        }

        if (removed)
            LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
    }
```

- [ ] **Step 3: Verify compile**

In Unity: recompile. Expected: 0 errors. (`LayoutRebuilder` resolves via the existing `using UnityEngine.UI;` at line 2.)

- [ ] **Step 4: Commit** *(ask for consent first)*

```bash
git add Assets/Scripts/UI/MessageListView.cs
git commit -m "$(cat <<'EOF'
feat(chat): destroy bubble on OnMessageRemoved (cancelled send)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: `MessageItemView` ring + cancel rendering

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs`

All new serialized refs are null on the incoming prefab; every method below null-guards them. The ring/X are drawn from procedurally-generated sprites (cached statically) so there are no asset files and no reliance on TMP icon glyphs.

- [ ] **Step 1: Add the DOTween import**

At the top of the file, after line 7 (`using UnityEngine.UI;`), add:

```csharp
using DG.Tweening;
```

- [ ] **Step 2: Add the serialized refs + sprite cache fields**

In the `[Header("Media Controls")]` block, after line 23 (`public GameObject playOverlay;`), add:

```csharp
    [SerializeField] private Image uploadRing;          // radial fill ring (procedural annulus sprite)
    [SerializeField] private Button cancelButton;       // X center; aborts the in-flight send
    [SerializeField] private Image cancelIcon;          // X glyph child of cancelButton (procedural sprite)
    [SerializeField] private GameObject playIconObject; // the "Play" child; hidden while uploading
    private static Sprite _ringSprite;
    private static Sprite _cancelSprite;
    private Tween _ringTween;
```

- [ ] **Step 3: Add the procedural sprite builders**

Add these two static methods near the bottom of the class (e.g. directly before the closing brace, after `UpdateRetryButton`'s region — exact placement inside the class body is fine):

```csharp
    // White annulus (ring) so a radial-filled Image reads as a ring, not a pie wedge.
    // The ring sits on the play overlay's existing dark backdrop (no new scrim added).
    // stroke/size are visual-tune values — confirm on device in Task 8 (spec target
    // ≈6px on the 1080-ref canvas; 7px at 128px tex over a 150px Image ≈ 8px, tune down
    // if it reads heavy). Cached once per process.
    private static Sprite BuildRingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        const int   size   = 128;
        const float outer  = 63f;   // 1px margin inside the texture
        const float stroke = 7f;    // tunable; see comment above
        float inner = outer - stroke;
        var center  = new Vector2(size * 0.5f, size * 0.5f);
        var pixels  = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                // 1px feather on both rims: 0 outside `outer`, 0 inside `inner`, 1 in the band.
                float alpha = Mathf.Min(Mathf.Clamp01(outer - d), Mathf.Clamp01(d - inner));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(pixels);
        tex.Apply();
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _ringSprite.name = "ProcRing";
        return _ringSprite;
    }

    // White "X" glyph centered in a 128px texture. Cached once per process.
    private static Sprite BuildCancelSprite()
    {
        if (_cancelSprite != null) return _cancelSprite;
        const int   size = 128;
        const float half = 3f;                  // half stroke width (px)
        float lo = size * 0.30f, hi = size * 0.70f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inBox = x >= lo && x <= hi && y >= lo && y <= hi;
                // Perpendicular distance to each diagonal: |x-y|/√2 and |x+y-size|/√2.
                float d = Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x + y - size)) * 0.70710677f;
                float alpha = inBox ? Mathf.Clamp01(half - d + 0.5f) : 0f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(pixels);
        tex.Apply();
        _cancelSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _cancelSprite.name = "ProcCancelX";
        return _cancelSprite;
    }
```

- [ ] **Step 4: Add the progress + show/hide handlers**

Add these methods near `HandleStatusChanged` (e.g. directly after `HandleStatusChanged`, after line 381):

```csharp
    private void HandleSendProgress(string tempId, float progress)
    {
        if (currentVm == null || currentVm.isIncoming) return;
        if (currentVm.messageId != tempId) return;
        if (currentVm.type != MessageType.Video) return;
        ShowUploadRing(progress);
    }

    // Shows the ring + X over the video thumbnail and animates the fill to `progress`.
    // No-ops on the incoming prefab (uploadRing is null there).
    private void ShowUploadRing(float progress)
    {
        if (uploadRing == null) return;

        if (uploadRing.sprite == null)
        {
            uploadRing.sprite        = BuildRingSprite();
            uploadRing.type          = Image.Type.Filled;
            uploadRing.fillMethod    = Image.FillMethod.Radial360;
            uploadRing.fillOrigin    = (int)Image.Origin360.Top;
            uploadRing.fillClockwise = true;
        }
        if (cancelIcon != null && cancelIcon.sprite == null) cancelIcon.sprite = BuildCancelSprite();

        if (playOverlay != null) playOverlay.SetActive(true);
        if (playIconObject != null) playIconObject.SetActive(false);
        uploadRing.gameObject.SetActive(true);
        if (cancelButton != null) cancelButton.gameObject.SetActive(true);

        float target = Mathf.Clamp01(progress);
        _ringTween?.Kill();
        // DOFillAmount smooths byte-level jumps; ~0.15s per the design spec.
        _ringTween = uploadRing.DOFillAmount(target, 0.15f).SetEase(Ease.OutQuad);
    }

    // Restores the play icon and removes the ring + X. No-ops on the incoming prefab.
    private void HideUploadRing()
    {
        _ringTween?.Kill();
        _ringTween = null;
        if (uploadRing != null)
        {
            uploadRing.fillAmount = 0f;
            uploadRing.gameObject.SetActive(false);
        }
        if (cancelButton != null) cancelButton.gameObject.SetActive(false);
        if (playIconObject != null) playIconObject.SetActive(true);
    }
```

- [ ] **Step 5: Subscribe / unsubscribe progress + wire cancel in OnEnable / OnDisable**

In `OnEnable`, after line 237 (`ChatManager.Instance.OnMessageMediaRefreshed += HandleMediaRefreshed;`), add:

```csharp
            ChatManager.Instance.OnMediaSendProgress += HandleSendProgress;
```

Still in `OnEnable`, after the `ChatManager.Instance != null` block (after line 238's closing brace), add the cancel-button wiring:

```csharp

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() =>
            {
                if (ScrollClickBlocker.IsBlocking) return;
                if (currentVm != null && ChatManager.Instance != null)
                    ChatManager.Instance.CancelMediaSend(currentVm.messageId);
            });
        }
```

In `OnDisable`, after line 253 (`ChatManager.Instance.OnMessageMediaRefreshed -= HandleMediaRefreshed;`), add:

```csharp
            ChatManager.Instance.OnMediaSendProgress -= HandleSendProgress;
```

Still in `OnDisable`, after the `retryButton` cleanup block (after line 260's closing brace), add:

```csharp

        if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
        _ringTween?.Kill();
        _ringTween = null;
```

- [ ] **Step 6: Hide the ring on terminal delivery status**

In `SetDeliveryStatus` (line 3357), after line 3360 (`currentVm.deliveryStatus = newStatus;`), add:

```csharp

        // Terminal states end the upload: drop the ring + X, restore the play icon.
        if (newStatus == DeliveryStatus.Sent || newStatus == DeliveryStatus.Failed)
            HideUploadRing();
```

- [ ] **Step 7: Show the ring on initial bind of an in-flight video**

In `ShowSmartThumbnail`, replace line 1827:

```csharp
        if (vm.type == MessageType.Video) playOverlay.SetActive(true);
```

with:

```csharp
        if (vm.type == MessageType.Video)
        {
            playOverlay.SetActive(true);
            // A just-staged / reopened outgoing video still uploading shows the ring + X
            // at zero fill so it reads as in-progress; a delivered one shows the play icon.
            if (!vm.isIncoming && vm.deliveryStatus == DeliveryStatus.Pending)
                ShowUploadRing(0f);
            else
                HideUploadRing();
        }
```

- [ ] **Step 8: Verify compile**

In Unity: recompile. Expected: 0 errors. The new fields show in the inspector but are unwired until Task 7 builds them into the prefab — that's expected.

- [ ] **Step 9: Commit** *(ask for consent first)*

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(chat): render upload ring + cancel X on outgoing video bubbles

Subscribes to OnMediaSendProgress, draws a procedural radial ring + X over the
play overlay, animates fill via DOTween, wires the cancel button to
CancelMediaSend, and restores the play icon on Sent/Failed. All refs null-guarded
so the shared incoming prefab is unaffected.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Editor builder for the prefab nodes + run it

**Files:**
- Create: `Assets/Editor/UploadRingBuilder.cs`
- Modify (via the builder): `Assets/Prefabs/MessageTextOutgoing.prefab`

- [ ] **Step 1: Write the builder**

Create `Assets/Editor/UploadRingBuilder.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot builder that adds the outgoing video upload progress ring + cancel (X)
/// under playOverlay in MessageTextOutgoing.prefab and wires the MessageItemView
/// serialized refs (uploadRing, cancelButton, cancelIcon, playIconObject).
/// Idempotent: re-running first removes the prior generated children.
/// Run via: Tools ▸ Chat ▸ Build Upload Ring (Outgoing).
/// </summary>
public static class UploadRingBuilder
{
    private const string PrefabPath = "Assets/Prefabs/MessageTextOutgoing.prefab";

    [MenuItem("Tools/Chat/Build Upload Ring (Outgoing)")]
    public static void Build()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) { Debug.LogError($"[UploadRingBuilder] could not load {PrefabPath}"); return; }

        try
        {
            var view = root.GetComponent<MessageItemView>();
            if (view == null) { Debug.LogError("[UploadRingBuilder] no MessageItemView on prefab root"); return; }

            var so = new SerializedObject(view);
            var overlayGo = so.FindProperty("playOverlay").objectReferenceValue as GameObject;
            if (overlayGo == null) { Debug.LogError("[UploadRingBuilder] playOverlay ref is null on prefab"); return; }
            Transform overlay = overlayGo.transform;

            // The "Play" child is the play icon we toggle off while uploading.
            Transform playChild = overlay.Find("Play");
            if (playChild == null) Debug.LogWarning("[UploadRingBuilder] 'Play' child not found under playOverlay; playIconObject will be null");

            // Idempotent re-run: drop any previously generated nodes.
            DestroyIfExists(overlay, "UploadRing");
            DestroyIfExists(overlay, "CancelButton");

            // --- radial ring ---
            var ringGo = new GameObject("UploadRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ringGo.transform.SetParent(overlay, false);
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.sizeDelta        = new Vector2(150f, 150f);
            ringRt.anchoredPosition = Vector2.zero;
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.color         = Color.white;
            ringImg.raycastTarget = false;
            ringImg.type          = Image.Type.Filled;
            ringImg.fillMethod    = Image.FillMethod.Radial360;
            ringImg.fillOrigin    = (int)Image.Origin360.Top;
            ringImg.fillClockwise = true;
            ringImg.fillAmount    = 0f;
            ringGo.SetActive(false);   // runtime shows it when a send starts

            // --- cancel button (transparent hit area) + X icon child ---
            var cancelGo = new GameObject("CancelButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            cancelGo.transform.SetParent(overlay, false);
            var cancelRt = (RectTransform)cancelGo.transform;
            cancelRt.sizeDelta        = new Vector2(88f, 88f);   // ≥44dp touch target
            cancelRt.anchoredPosition = Vector2.zero;
            var cancelHit = cancelGo.GetComponent<Image>();
            cancelHit.color         = new Color(0f, 0f, 0f, 0f);  // invisible but raycast-able
            cancelHit.raycastTarget = true;

            var iconGo = new GameObject("CancelIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(cancelGo.transform, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.sizeDelta        = new Vector2(44f, 44f);
            iconRt.anchoredPosition = Vector2.zero;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.color         = Color.white;
            iconImg.raycastTarget = false;
            cancelGo.SetActive(false);

            // --- wire serialized refs ---
            so.FindProperty("uploadRing").objectReferenceValue     = ringImg;
            so.FindProperty("cancelButton").objectReferenceValue   = cancelGo.GetComponent<Button>();
            so.FindProperty("cancelIcon").objectReferenceValue     = iconImg;
            so.FindProperty("playIconObject").objectReferenceValue = playChild != null ? playChild.gameObject : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[UploadRingBuilder] UploadRing + CancelButton added and wired on MessageTextOutgoing.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void DestroyIfExists(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }
}
```

- [ ] **Step 2: Verify compile**

In Unity: recompile. Expected: 0 errors, and a new menu item appears: **Tools ▸ Chat ▸ Build Upload Ring (Outgoing)**.

- [ ] **Step 3: Run the builder**

In Unity: click **Tools ▸ Chat ▸ Build Upload Ring (Outgoing)**.
Expected Console log: `[UploadRingBuilder] UploadRing + CancelButton added and wired on MessageTextOutgoing.prefab`.
Then open `MessageTextOutgoing.prefab`, select the root, and confirm in the MessageItemView inspector that `Upload Ring`, `Cancel Button`, `Cancel Icon`, and `Play Icon Object` are all assigned.

- [ ] **Step 4: Commit** *(ask for consent first — stage the new script + its .meta + the modified prefab)*

```bash
git add Assets/Editor/UploadRingBuilder.cs \
        Assets/Editor/UploadRingBuilder.cs.meta \
        Assets/Prefabs/MessageTextOutgoing.prefab
git commit -m "$(cat <<'EOF'
feat(chat): add UploadRingBuilder + ring/cancel nodes on outgoing prefab

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Manual on-device verification (iOS)

Upload progress (`www.uploadProgress`) and native convert progress do **not** run in the Editor, so the full ring only animates on device. Verify on a real iOS build.

- [ ] **Step 1: Build & deploy to an iOS device** (Xcode, from the Unity iOS export).

- [ ] **Step 2: Happy path — send a multi-MB video.**
  Expected: bubble appears with the ring at ~0; ring fills smoothly through convert (to ~30%), snaps at encode (~40%), then climbs to 100% during upload; ring + X disappear and the play icon returns when the bubble flips to Sent (single grey→blue tick).

- [ ] **Step 3: Cancel mid-convert.**
  Tap the X while the ring is in the convert band. Expected: bubble disappears immediately; **no** Failed tick flashes; the chat-list preview reverts to the prior last message. (A late-finishing native job may briefly recreate `send_{tempId}.mp4` in `Library/Caches` — purgeable, acceptable.)

- [ ] **Step 4: Cancel mid-upload.**
  Tap the X while the ring is in the upload band. Expected: bubble disappears immediately; no Failed tick; the in-flight POST is aborted.

- [ ] **Step 5: Temp-file cleanup.**
  After a cancel, confirm `staged_video_{tempId}.mov` and `send_{tempId}.mp4` are gone from `Application.temporaryCachePath` (or only a late-convert `.mp4` remains, per Step 3).

- [ ] **Step 6: Retry path.**
  Force a send to fail (e.g. airplane mode), let it show the Failed tick + retry, then tap retry with connectivity back. Expected: the ring + X reappear and run again (retry re-enters `PostMediaMessageRoutine`).

- [ ] **Step 7: Regression — image & document sends.**
  Send a photo and a document. Expected: no ring appears (the ring renders only for `MessageType.Video`); their existing spinner/behavior is unchanged.

- [ ] **Step 8: Regression — incoming video bubble.**
  Open a chat with an incoming video. Expected: the incoming bubble's play button behaves exactly as before (the new refs are null on the incoming prefab, so all ring code no-ops).

---

## Notes for the executor

- **Editor-run steps:** all "verify compile" and "run test" steps happen in the user's open Unity Editor; there is no headless CLI path here.
- **Commits need consent:** confirm with the user before each commit step. New `.cs`/`.meta` files only generate their `.meta` after Unity imports them — import first, then stage both.
- **Order matters:** Tasks 1-3 are independent leaves; Task 4 depends on 1-3; Task 6 depends on 2; Task 7 depends on 6 (the serialized fields must exist before the builder can wire them). Keep the order.
