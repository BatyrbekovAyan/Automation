using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Media-attachment send concerns split out of ChatManager — keeps the
/// god-object trimmer and groups related behavior. Mirrors ChatManager.Outbox.cs
/// and ChatManager.BotState.cs. Houses optimistic staging, the per-kind cache
/// seed helpers, and the Wappi upload + reconcile coroutine.
/// </summary>
public partial class ChatManager
{
    // Wappi's video endpoint only delivers MP4/H.264 under ~16 MB; a converted
    // file still above this can't be sent (see design spec / project memory).
    private const long WappiVideoCapBytes = 11L * 1024 * 1024;

    /// <summary>
    /// Send-pipeline phase. Convert (iOS transcode) and Upload are the slow,
    /// measurable phases; Encode is a fast opaque base64 Task that snaps.
    /// Public so the EditMode test assembly (Assembly-CSharp-Editor) can see it.
    /// </summary>
    public enum SendPhase { Convert, Encode, Upload }

    /// <summary>
    /// Maps (phase, intra-phase 0..1) onto the whole-pipeline 0..1 fill the ring
    /// shows: Convert 0→0.30, Encode 0.30→0.40, Upload(bytes sent) 0.40→0.90. The
    /// final 0.90→1.00 is the server-ack window (Wappi receiving + forwarding the
    /// clip) which has no progress signal, so the ring holds at 0.90 there and the
    /// view animates the last slice when the Sent event lands. Pure + public for
    /// unit testing; touches no Unity state.
    /// </summary>
    public static float SendProgress(SendPhase phase, float sub) => phase switch
    {
        SendPhase.Convert => 0.00f + 0.30f * Mathf.Clamp01(sub),
        SendPhase.Encode  => 0.30f + 0.10f * Mathf.Clamp01(sub),
        SendPhase.Upload  => 0.40f + 0.50f * Mathf.Clamp01(sub),   // caps at 0.90; ack finishes it
        _ => 0f,
    };

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
    public void StageLocalMedia(AttachmentPick pick, string caption, Texture2D preloadedImage = null)
    {
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

        seenMessageIds.Add(tempId);

        var vm = new MessageViewModel
        {
            messageId      = tempId,
            chatId         = currentChatId,
            senderName     = "Me",
            isIncoming     = false,
            timestamp      = now,
            sequence       = _nextLocalSendSequence++,
            text           = caption ?? "",
            mimeType       = pick.MimeType,
            fileName       = pick.FileName,
            fileSize       = pick.FileSizeBytes,
            deliveryStatus = DeliveryStatus.Pending,
        };

        // The picker reuses one temp path (pickedMedia1.mov) and overwrites it on every
        // pick, so a video clip is moved to a unique per-tempId path below — otherwise
        // every staged video would point at (and play) the last-picked file. Set in the
        // GalleryVideo case; used for both playback (videoUrl) and the upload source.
        string stagedVideoPath = null;

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
                vm.type = MessageType.Video;
                // Move the clip off the shared pickedMedia1.mov to a unique per-tempId path
                // (same Caches volume → instant rename) so each video bubble plays its own
                // file. Fall back to the picked path if the move fails.
                stagedVideoPath = System.IO.Path.Combine(Application.temporaryCachePath, $"staged_video_{tempId}.mov");
                try
                {
                    if (System.IO.File.Exists(stagedVideoPath)) System.IO.File.Delete(stagedVideoPath);
                    System.IO.File.Move(pick.Path, stagedVideoPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ChatManager] staged-video move failed: {ex.Message}; using picked path");
                    stagedVideoPath = pick.Path;
                }
                var (thumbUrl, thumbAspect) = SeedVideoThumbCache(stagedVideoPath, tempId);
                var meta = ReadVideoMetadata(stagedVideoPath);
                vm.thumbnailUrl  = thumbUrl;
                vm.videoUrl      = "file://" + stagedVideoPath;
                // Upright thumbnail dims are the ground truth; corrected metadata is the
                // fallback (e.g. Editor, or thumbnail decode failure). Both are display-oriented.
                vm.aspectRatio   = thumbAspect > 0f ? thumbAspect : meta.aspect;
                vm.duration      = meta.durationSec;
                vm.videoRotation = meta.rotation; // unchanged; consumed by VideoController only
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

        // ---- persist (parity with SendTextMessageRoutine) ----
        List<MessageViewModel> cachedList = ChatHistoryCache.LoadHistory(sendCacheRoot, currentChatId);
        cachedList.Add(vm);
        ChatHistoryCache.SaveHistory(sendCacheRoot, currentChatId, cachedList);

        var chatVm = GetChat(currentChatId);
        if (chatVm != null) chatVm.UpdateLastMessage(LastMessagePreview(pick, caption), now);

        // ---- enqueue media outbox entry (tap-to-retry + survives reopen) ----
        // Image byte source is the staged JPEG already on disk in persistentDataPath;
        // video/document point at the original picked path (light retry durability).
        // A null MediaCacheManager means the image was never seeded to disk
        // (SeedImageCacheFromTexture no-ops on a null Instance), so fall back to the
        // original picked file rather than NRE dereferencing Instance here.
        string mediaPath = (vm.type == MessageType.Image && MediaCacheManager.Instance != null)
            ? MediaCacheManager.Instance.GetFilePathFromUrl(vm.mediaUrl)
            : (stagedVideoPath ?? pick.Path);

        var entry = new OutboxStore.OutboxEntry
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
        };
        Outbox.Add(entry);

        // ---- optimistic UI (unchanged from part b) ----
        OnLiveMessagesReceived?.Invoke(new List<MessageViewModel> { vm });

        // ---- network half on Manager.Instance (bot-switch safe), like SendTextMessage ----
        // entry is the same reference Outbox.Add just stored — no Find round-trip needed.
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
                // We re-convert from the original pick on every attempt (the native "use-as-is"
                // fast-path makes an already-deliverable file nearly free), so we never persist
                // the temp path: it lives in Library/Caches and can be purged between sessions,
                // and entry.mediaPath must keep pointing at the original for retries. convertedTemp
                // is non-null only when a NEW file was written (not the pass-through original).
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
}
