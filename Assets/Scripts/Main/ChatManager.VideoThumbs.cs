using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Incoming-video thumbnail extraction, split out of ChatManager (mirrors
/// ChatManager.MediaSend.cs / .BotState.cs). For every server-sourced video, natively
/// extracts a frame from its remote URL and caches it under "vthumb://{id}", replacing
/// Wappi's server JPEGThumbnail so previews never depend on the server providing one.
/// iOS does the extraction; Editor + Android fall back to the server thumb (see
/// VideoThumbnailExtractor). Extraction coroutines run on 'this', so SetActiveBot's
/// StopAllCoroutines cancels in-flight work on a bot switch; ClearVideoThumbQueue then
/// resets the bookkeeping.
/// </summary>
public partial class ChatManager
{
    private const int    VideoThumbMaxConcurrent = 4;   // drain a backlog of url-less videos faster
    private const double VideoThumbTimeSec       = 1.0;   // grab ~1s in (clamped to duration native-side)

    private VideoThumbQueue _videoThumbQueue;
    // Live VM instance per queued id, so completion can mutate it + fire OnMessageMediaRefreshed.
    private readonly Dictionary<string, MessageViewModel> _pendingThumbVms = new();

    // Messages whose media Wappi can no longer fetch (/media/download → 400 "download media
    // error" = expired/deleted on WhatsApp). Recovery is impossible: neither a thumbnail nor
    // playback can be produced. We render the download panel (tap-to-retry) for these and stop
    // auto-re-attempting on every sync/scroll. In-session only — cleared on bot switch, so each
    // video makes at most one recovery attempt per launch before being parked.
    private readonly HashSet<string> _unavailableMediaIds = new();
    // Ids the user has already manually retried (and which failed again). These graduate from
    // the tap-to-retry download panel to the terminal "expired/unavailable" placeholder.
    private readonly HashSet<string> _retriedUnavailableIds = new();

    /// <summary>True when this message's media is confirmed unfetchable (gone from WhatsApp).</summary>
    public bool IsMediaUnavailable(string messageId) =>
        !string.IsNullOrEmpty(messageId) && _unavailableMediaIds.Contains(messageId);

    /// <summary>True once the user has manually retried this unavailable media (→ show expired card).</summary>
    public bool HasRetriedUnavailable(string messageId) =>
        !string.IsNullOrEmpty(messageId) && _retriedUnavailableIds.Contains(messageId);

    /// <summary>
    /// Manual tap-to-retry: clears the unavailable mark and re-queues one recovery attempt.
    /// Called by the download-panel tap on an unavailable video bubble. A second failure
    /// promotes the bubble to the terminal expired placeholder (HasRetriedUnavailable).
    /// </summary>
    public void RetryUnavailableMedia(MessageViewModel vm)
    {
        if (vm == null || string.IsNullOrEmpty(vm.messageId)) return;
        _unavailableMediaIds.Remove(vm.messageId);
        _retriedUnavailableIds.Add(vm.messageId);
        // The queue still holds this id as 'known' from the first auto-attempt; without
        // forgetting it, TryEnqueue would no-op and the retry would never run (spinner hangs).
        _videoThumbQueue?.Forget(vm.messageId);
        EnqueueIncomingVideoThumb(vm);
    }

    /// <summary>
    /// Queues a video for native thumbnail extraction / recovery. No-op for non-video or
    /// already-extracted messages. Crucially, this now ALSO handles aged outgoing videos that
    /// arrive with an EMPTY videoUrl: Wappi drops s3Info+JPEGThumbnail once a sent video is
    /// delivered, so the only way to rebuild a preview is to re-fetch a fresh link at
    /// extraction time (RunVideoThumbExtraction). That re-fetch needs a native extractor, so a
    /// url-less video is only queued where one exists. Durable de-dup is the on-disk vthumb
    /// file; in-session de-dup is the queue. Called from CreateViewModel, RefreshCachedMessageMedia,
    /// and the render path (MessageItemView) when a video bubble has no usable thumbnail.
    /// </summary>
    public void EnqueueIncomingVideoThumb(MessageViewModel vm)
    {
        if (vm == null || vm.type != MessageType.Video || string.IsNullOrEmpty(vm.messageId)) return;
        if (MediaCacheManager.Instance == null) return;

        // Already confirmed unfetchable this session — don't hammer /media/download again.
        // The bubble shows the download panel; only a manual tap (RetryUnavailableMedia) retries.
        if (_unavailableMediaIds.Contains(vm.messageId)) return;

        string vthumbUrl = "vthumb://" + vm.messageId;

        // Durable de-dup: our frame was extracted in a prior session and persists on disk.
        if (MediaCacheManager.Instance.IsImageCached(vthumbUrl))
        {
            if (vm.thumbnailUrl != vthumbUrl)
            {
                vm.thumbnailUrl = vthumbUrl;
                OnMessageMediaRefreshed?.Invoke(vm);
            }
            return;
        }

        // With no url we can only recover by re-fetching one at extraction time, which needs a
        // native extractor. No url AND no extractor (e.g. Editor) => nothing we can do.
        bool haveUrl = !string.IsNullOrEmpty(vm.videoUrl);
        if (!haveUrl && !VideoThumbnailExtractor.IsSupported)
        {
            Debug.Log($"[VTHUMB-DBG] enqueue SKIP-no-extractor id={vm.messageId} incoming={vm.isIncoming}");
            return;
        }

        _videoThumbQueue ??= new VideoThumbQueue(VideoThumbMaxConcurrent);
        if (_videoThumbQueue.TryEnqueue(vm.messageId))
        {
            Debug.Log($"[VTHUMB-DBG] enqueue QUEUED id={vm.messageId} incoming={vm.isIncoming} haveUrl={haveUrl}");
            _pendingThumbVms[vm.messageId] = vm;
            PumpVideoThumbQueue();
        }
    }

    private void PumpVideoThumbQueue()
    {
        if (_videoThumbQueue == null) return;
        foreach (string id in _videoThumbQueue.Dispatch())
            StartCoroutine(RunVideoThumbExtraction(id));
    }

    private IEnumerator RunVideoThumbExtraction(string messageId)
    {
        if (!_pendingThumbVms.TryGetValue(messageId, out MessageViewModel vm) || vm == null)
        {
            _videoThumbQueue.Complete(messageId);
            PumpVideoThumbQueue();
            yield break;
        }

        string cacheRoot = GetCacheRoot();
        string vthumbUrl = "vthumb://" + messageId;
        // Compute (and ensure the media dir exists) before the native atomic write.
        string finalPath = MediaCacheManager.Instance.GetFilePathFromUrl(vthumbUrl);

        // Aged outgoing videos arrive with no usable videoUrl (Wappi drops s3Info once a sent
        // video is delivered). Re-fetch a fresh signed link up front — the same /media/download
        // the play path uses — so the native extractor has something to read.
        if (string.IsNullOrEmpty(vm.videoUrl) || !vm.videoUrl.StartsWith("http"))
            yield return RefetchVideoUrl(messageId, vm);

        bool ok = false;
        if (!string.IsNullOrEmpty(vm.videoUrl) && vm.videoUrl.StartsWith("http"))
        {
            yield return VideoThumbnailExtractor.Extract(
                vm.videoUrl, finalPath, VideoThumbTimeSec,
                _   => ok = true,
                err => Debug.LogWarning($"[ChatManager] video thumb extract failed for {messageId}: {err}"));

            // A signed link that was already on the VM may be expired (403s). Re-fetch once
            // more and retry — only where a native extractor exists.
            if (!ok && VideoThumbnailExtractor.IsSupported)
            {
                yield return RefetchVideoUrl(messageId, vm);
                if (!string.IsNullOrEmpty(vm.videoUrl) && vm.videoUrl.StartsWith("http"))
                    yield return VideoThumbnailExtractor.Extract(
                        vm.videoUrl, finalPath, VideoThumbTimeSec,
                        _   => ok = true,
                        err => Debug.LogWarning($"[ChatManager] video thumb retry failed for {messageId}: {err}"));
            }
        }

        // If we still have no usable link after re-fetching, /media/download couldn't retrieve
        // the media (400 "download media error" = expired/deleted on WhatsApp). Mark it
        // unavailable so the bubble switches to the download panel and we stop auto-retrying.
        // Only conclude this where a native extractor exists (on Editor a url-less video is
        // simply un-extractable, not necessarily gone).
        bool gotUsableUrl = !string.IsNullOrEmpty(vm.videoUrl) && vm.videoUrl.StartsWith("http");
        if (!ok && !gotUsableUrl && VideoThumbnailExtractor.IsSupported)
        {
            _unavailableMediaIds.Add(messageId);
            OnMessageMediaRefreshed?.Invoke(vm);   // re-bind → download panel
        }

        Debug.Log($"[VTHUMB-DBG] extraction DONE id={messageId} incoming={vm.isIncoming} ok={ok} gotUrl={gotUsableUrl} unavailable={_unavailableMediaIds.Contains(messageId)}");

        if (ok && System.IO.File.Exists(finalPath))
        {
            vm.thumbnailUrl = vthumbUrl;

            // The extracted frame is upright (display orientation), so its pixel
            // dimensions are the ground-truth bubble aspect — overriding whatever
            // Normalize guessed from server dims (often missing -> square, or
            // rotation-raw -> landscape). Same thumbnail-as-source-of-truth
            // principle the outgoing send path uses (SeedVideoThumbCache).
            float thumbAspect = ReadImageFileAspect(finalPath);
            if (thumbAspect > 0f) vm.aspectRatio = thumbAspect;

            UpdateCachedThumbnailUrl(cacheRoot, vm.chatId, messageId, vthumbUrl, vm.aspectRatio);
            OnMessageMediaRefreshed?.Invoke(vm);
        }

        _pendingThumbVms.Remove(messageId);
        _videoThumbQueue.Complete(messageId);
        PumpVideoThumbQueue();
    }

    /// <summary>
    /// Re-fetches a fresh, decryptable media link for a message via Wappi's /media/download
    /// (the same endpoint the play path uses) and, on success with a real http link, writes it
    /// onto the VM with a 24h expiry. base64:// payloads are skipped — the native extractor
    /// reads from a URL/path, not an inline pseudo-scheme. Yields until the fetch settles.
    /// </summary>
    private IEnumerator RefetchVideoUrl(string messageId, MessageViewModel vm)
    {
        string freshUrl = null;
        bool fetchDone = false;
        DownloadMediaForMessage(messageId,
            url => { freshUrl = url; fetchDone = true; },
            ()  => { fetchDone = true; });
        while (!fetchDone) yield return null;

        // [VTHUMB-DBG] KEY incoming-path diagnostic: what did /media/download hand back?
        // Hypothesis: incoming videos return a base64:// payload the native extractor can't
        // read, leaving them stuck black. This line confirms or refutes that.
        string scheme = string.IsNullOrEmpty(freshUrl) ? "EMPTY/FAIL"
            : freshUrl.StartsWith("http") ? "http/len" + freshUrl.Length
            : freshUrl.StartsWith("base64://") ? "base64/len" + freshUrl.Length
            : "other(" + freshUrl.Substring(0, System.Math.Min(24, freshUrl.Length)) + ")";
        Debug.Log($"[VTHUMB-DBG] refetch id={messageId} incoming={vm.isIncoming} result={scheme}");

        if (!string.IsNullOrEmpty(freshUrl) && freshUrl.StartsWith("http"))
        {
            vm.videoUrl = freshUrl;
            vm.expireTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400;
        }
    }

    private void UpdateCachedThumbnailUrl(string cacheRoot, string chatId, string messageId, string thumbnailUrl, float aspectRatio)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(cacheRoot, chatId);
        for (int i = 0; i < cached.Count; i++)
        {
            if (cached[i].messageId == messageId)
            {
                cached[i].thumbnailUrl = thumbnailUrl;
                // Persist the upright-thumbnail aspect so the correct bubble shape
                // survives app restarts without re-extracting (CreateViewModel copies
                // it back into the VM on the next cache load).
                if (aspectRatio > 0f) cached[i].aspectRatio = aspectRatio;
                ChatHistoryCache.SaveHistory(cacheRoot, chatId, cached);
                return;
            }
        }
    }

    /// <summary>
    /// Decodes the image at <paramref name="path"/> and returns its display aspect
    /// (width / height), or 0 if it can't be read. The extracted video frame is
    /// already upright, so no rotation correction is applied. The temporary decode
    /// texture is destroyed immediately. Never throws.
    /// </summary>
    private static float ReadImageFileAspect(string path)
    {
        Texture2D tex = null;
        try
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes) || tex.height <= 0) return 0f;
            return (float)tex.width / tex.height;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatManager] ReadImageFileAspect failed for {path}: {ex.Message}");
            return 0f;
        }
        finally
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    /// <summary>Resets all queue state. Called from SetActiveBot after StopAllCoroutines
    /// (which already cancels in-flight extraction coroutines running on this).</summary>
    public void ClearVideoThumbQueue()
    {
        _videoThumbQueue?.Clear();
        _pendingThumbVms.Clear();
        _unavailableMediaIds.Clear();
        _retriedUnavailableIds.Clear();
    }
}
