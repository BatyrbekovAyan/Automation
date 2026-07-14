using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Incoming-video thumbnail extraction, split out of ChatManager (mirrors
/// ChatManager.MediaSend.cs / .BotState.cs). For every server-sourced video, natively
/// extracts a frame from its remote URL and caches it under the VideoThumbKey-minted
/// key ("vthumb://{id}" on WhatsApp; TG-namespaced on Telegram — see VideoThumbKey),
/// replacing Wappi's server JPEGThumbnail so previews never depend on the server providing one.
/// iOS does the extraction; Editor + Android fall back to the server thumb (see
/// VideoThumbnailExtractor). Extraction coroutines run on 'this', so SetActiveBot's
/// StopAllCoroutines cancels in-flight work on a bot switch; ClearVideoThumbQueue then
/// resets the bookkeeping.
/// </summary>
public partial class ChatManager
{
    // STRICTLY 1: concurrent extraction is what cross-contaminated thumbnails on first chat
    // entry (a video permanently showing another video's decoded frame). Every per-message
    // pairing in this file is id-keyed and provably correct in isolation — the crossing
    // happened below us, where several native decoder jobs + Wappi /media/download re-fetches
    // ran simultaneously. Because the on-disk vthumb file is the durable de-dup, one wrong
    // frame written during that burst was never re-extracted. Serial extraction removes the
    // burst entirely; the scratch-file commit (VideoThumbFiles) defuses timed-out zombie jobs
    // that would otherwise still overlap the next job's write.
    private const int    VideoThumbMaxConcurrent = 1;
    private const double VideoThumbTimeSec       = 1.0;   // grab ~1s in (clamped to duration native-side)

    // Unique suffix per extraction attempt — every native job writes to its own .part scratch
    // file, so an abandoned (timed-out) job can never write into a path a later attempt uses.
    private static int _extractAttemptSeq;

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
    // Videos whose media link is still valid (playable) but whose preview FRAME could not be
    // extracted — the decoder choked on the codec (HEVC/.mov) or extraction timed out. These are
    // NOT gone; they play fine, they just have no thumbnail. The bubble shows a dark card + play
    // button instead of an endless recovery spinner, and we stop re-attempting extraction for
    // them. In-session only — cleared on bot switch.
    private readonly HashSet<string> _thumbUnextractableIds = new();

    /// <summary>True when this message's media is confirmed unfetchable (gone from WhatsApp).</summary>
    public bool IsMediaUnavailable(string messageId) =>
        !string.IsNullOrEmpty(messageId) && _unavailableMediaIds.Contains(messageId);

    /// <summary>True once the user has manually retried this unavailable media (→ show expired card).</summary>
    public bool HasRetriedUnavailable(string messageId) =>
        !string.IsNullOrEmpty(messageId) && _retriedUnavailableIds.Contains(messageId);

    /// <summary>True when this video's preview frame couldn't be extracted but the media is still
    /// playable — the bubble shows a dark card + play button rather than a recovery spinner.</summary>
    public bool HasUnextractableThumbnail(string messageId) =>
        !string.IsNullOrEmpty(messageId) && _thumbUnextractableIds.Contains(messageId);

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
    /// <returns>
    /// True when a recovery is now in flight for this message (RunVideoThumbExtraction will fire
    /// OnMessageMediaRefreshed when it settles) — the caller should show the recovery spinner.
    /// False when nothing was queued (non-video, no thumbnail possible, or already parked as
    /// playable-but-unextractable / unavailable / already-cached) — the caller should render a
    /// static card instead of an endless spinner. Returning the right answer here is what stops
    /// the recovery spinner being orphaned on a re-bind.
    /// </returns>
    public bool EnqueueIncomingVideoThumb(MessageViewModel vm)
    {
        if (vm == null || vm.type != MessageType.Video || string.IsNullOrEmpty(vm.messageId)) return false;
        if (MediaCacheManager.Instance == null) return false;

        // Already confirmed unfetchable this session — don't hammer /media/download again.
        // The bubble shows the download panel; only a manual tap (RetryUnavailableMedia) retries.
        if (_unavailableMediaIds.Contains(vm.messageId)) return false;

        // Frame extraction already failed for this still-playable video — don't re-attempt; the
        // bubble shows a dark card + play button (HasUnextractableThumbnail) rather than spinning.
        if (_thumbUnextractableIds.Contains(vm.messageId)) return false;

        string vthumbUrl = VideoThumbKey.For(ActiveChannel, GetActiveProfileId(), vm.chatId, vm.messageId);

        // Durable de-dup: our frame was extracted in a prior session and persists on disk.
        if (MediaCacheManager.Instance.IsImageCached(vthumbUrl))
        {
            if (vm.thumbnailUrl != vthumbUrl)
            {
                vm.thumbnailUrl = vthumbUrl;
                OnMessageMediaRefreshed?.Invoke(vm);
            }
            return false;   // frame already on disk — caller paints it, no spinner
        }

        // With no url we can only recover by re-fetching one at extraction time, which needs a
        // native extractor. No url AND no extractor (e.g. Editor) => nothing we can do.
        bool haveUrl = !string.IsNullOrEmpty(vm.videoUrl);
        if (!haveUrl && !VideoThumbnailExtractor.IsSupported) return false;

        _videoThumbQueue ??= new VideoThumbQueue(VideoThumbMaxConcurrent);
        if (_videoThumbQueue.TryEnqueue(vm.messageId))
        {
            _pendingThumbVms[vm.messageId] = vm;
            PumpVideoThumbQueue();
        }
        // Newly enqueued OR already in-flight from a prior bind: a recovery is pending and will
        // fire OnMessageMediaRefreshed when it settles. (Every COMPLETED outcome — success,
        // unextractable, unavailable — is caught by the early-returns above, so reaching here
        // always means in-flight.)
        return true;
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
        string vthumbUrl = VideoThumbKey.For(ActiveChannel, GetActiveProfileId(), vm.chatId, messageId);
        // Compute (and ensure the media dir exists) before the native atomic write.
        string finalPath = MediaCacheManager.Instance.GetFilePathFromUrl(vthumbUrl);

        // Aged outgoing videos arrive with no usable videoUrl (Wappi drops s3Info once a sent
        // video is delivered). Re-fetch a fresh signed link up front — the same /media/download
        // the play path uses — so the native extractor has something to read.
        if (string.IsNullOrEmpty(vm.videoUrl) || !vm.videoUrl.StartsWith("http"))
            yield return RefetchVideoUrl(messageId, vm);

        // Each attempt extracts into its own scratch .part file; only a confirmed success is
        // promoted into the renderable vthumb path (Commit below). A native job that outlives
        // its 30s C# timeout keeps running and eventually writes its frame — into an orphaned
        // scratch file nobody renders, instead of the final cache key.
        bool ok = false;
        string tempPath = VideoThumbFiles.TempPathFor(finalPath, ++_extractAttemptSeq);
        if (!string.IsNullOrEmpty(vm.videoUrl) && vm.videoUrl.StartsWith("http"))
        {
            yield return VideoThumbnailExtractor.Extract(
                vm.videoUrl, tempPath, VideoThumbTimeSec,
                _   => ok = true,
                err => Debug.LogWarning($"[ChatManager] video thumb extract failed for {messageId}: {err}"));

            // A signed link that was already on the VM may be expired (403s). Re-fetch once
            // more and retry — only where a native extractor exists.
            if (!ok && VideoThumbnailExtractor.IsSupported)
            {
                VideoThumbFiles.Discard(tempPath);
                yield return RefetchVideoUrl(messageId, vm);
                if (!string.IsNullOrEmpty(vm.videoUrl) && vm.videoUrl.StartsWith("http"))
                {
                    // Fresh scratch path: the timed-out first job may still write its old
                    // file later — the retry must never share a target with it.
                    tempPath = VideoThumbFiles.TempPathFor(finalPath, ++_extractAttemptSeq);
                    yield return VideoThumbnailExtractor.Extract(
                        vm.videoUrl, tempPath, VideoThumbTimeSec,
                        _   => ok = true,
                        err => Debug.LogWarning($"[ChatManager] video thumb retry failed for {messageId}: {err}"));
                }
            }
        }

        // Classify the terminal outcome. Whatever it is, we MUST re-bind the bubble below: the
        // recovery spinner ShowSmartThumbnail raised is cleared ONLY by a re-bind, so any path
        // that returns here without firing OnMessageMediaRefreshed orphans the spinner forever.
        bool gotUsableUrl = !string.IsNullOrEmpty(vm.videoUrl) && vm.videoUrl.StartsWith("http");

        if (!ok) VideoThumbFiles.Discard(tempPath);

        if (ok && VideoThumbFiles.Commit(tempPath, finalPath))
        {
            // Success: native frame extracted. The frame is upright (display orientation), so its
            // pixel dimensions are the ground-truth bubble aspect — overriding whatever Normalize
            // guessed from server dims (often missing -> square, or rotation-raw -> landscape).
            // Same thumbnail-as-source-of-truth principle the outgoing send path uses.
            vm.thumbnailUrl = vthumbUrl;
            float thumbAspect = ReadImageFileAspect(finalPath);
            if (thumbAspect > 0f) vm.aspectRatio = thumbAspect;
            UpdateCachedThumbnailUrl(cacheRoot, vm.chatId, messageId, vthumbUrl, vm.aspectRatio);
        }
        else if (gotUsableUrl)
        {
            // Extraction failed but the media link is still valid: the video is playable, it just
            // has no preview frame (decoder choked on the codec, or extraction timed out). Park it
            // so the bubble re-binds to a dark card + play button instead of an endless spinner,
            // and we stop re-attempting extraction for it this session.
            _thumbUnextractableIds.Add(messageId);
        }
        else if (VideoThumbnailExtractor.IsSupported)
        {
            // No usable link after re-fetching AND extraction failed: /media/download couldn't
            // retrieve the media (400 "download media error" = expired/deleted on WhatsApp). Mark
            // it unavailable so the bubble switches to the download panel and we stop auto-retrying.
            // Only conclude this where a native extractor exists (on Editor a url-less video is
            // simply un-extractable, not necessarily gone).
            _unavailableMediaIds.Add(messageId);
        }

        _pendingThumbVms.Remove(messageId);
        _videoThumbQueue.Complete(messageId);

        // Always re-bind — this single unconditional invoke guarantees the recovery spinner
        // resolves on EVERY outcome (success → thumbnail; playable-but-no-frame → play card;
        // gone → download panel; Editor/no-op → dark card), closing the orphaned-spinner bug.
        OnMessageMediaRefreshed?.Invoke(vm);

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
        _thumbUnextractableIds.Clear();
    }
}

/// <summary>
/// Pure mint for the synthetic video-preview cache key (ChannelResolver/ChannelCachePath
/// precedent: pure seam co-located with its consumer). WhatsApp keeps the legacy global
/// "vthumb://{messageId}" — stanza ids are globally unique, so existing WA caches stay
/// byte-identical and valid. Telegram message ids are 1-5 digit per-account/per-channel
/// COUNTERS (SHAPES.md), so the same numeric id can name different videos across two TG
/// bots, or across a channel post and a private chat in one account — and MediaCacheManager
/// is a single global disk cache, so an un-namespaced key would silently and durably paint
/// another message's frame (05-06-REVIEW WR-02). The TG key therefore namespaces by
/// profile + chat. Null parts coalesce to "" so the key stays deterministic.
/// </summary>
public static class VideoThumbKey
{
    public static string For(ChatChannel channel, string profileId, string chatId, string messageId) =>
        channel == ChatChannel.Telegram
            ? $"vthumb://tg/{profileId ?? ""}/{chatId ?? ""}/{messageId}"
            : "vthumb://" + messageId;
}
