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
    private const int    VideoThumbMaxConcurrent = 2;
    private const double VideoThumbTimeSec       = 1.0;   // grab ~1s in (clamped to duration native-side)

    private VideoThumbQueue _videoThumbQueue;
    // Live VM instance per queued id, so completion can mutate it + fire OnMessageMediaRefreshed.
    private readonly Dictionary<string, MessageViewModel> _pendingThumbVms = new();

    /// <summary>
    /// Queues an incoming video for native thumbnail extraction. No-op for non-video,
    /// urlless, or already-extracted messages. Durable de-dup is the on-disk vthumb file.
    /// Called from CreateViewModel for every server-sourced message.
    /// </summary>
    public void EnqueueIncomingVideoThumb(MessageViewModel vm)
    {
        if (vm == null || vm.type != MessageType.Video) return;
        if (string.IsNullOrEmpty(vm.videoUrl) || string.IsNullOrEmpty(vm.messageId)) return;
        if (MediaCacheManager.Instance == null) return;

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

        _videoThumbQueue ??= new VideoThumbQueue(VideoThumbMaxConcurrent);
        if (_videoThumbQueue.TryEnqueue(vm.messageId))
        {
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

        bool ok = false;
        yield return VideoThumbnailExtractor.Extract(
            vm.videoUrl, finalPath, VideoThumbTimeSec,
            _   => ok = true,
            err => Debug.LogWarning($"[ChatManager] video thumb extract failed for {messageId}: {err}"));

        if (ok && System.IO.File.Exists(finalPath))
        {
            vm.thumbnailUrl = vthumbUrl;
            UpdateCachedThumbnailUrl(cacheRoot, vm.chatId, messageId, vthumbUrl);
            OnMessageMediaRefreshed?.Invoke(vm);
        }

        _pendingThumbVms.Remove(messageId);
        _videoThumbQueue.Complete(messageId);
        PumpVideoThumbQueue();
    }

    private void UpdateCachedThumbnailUrl(string cacheRoot, string chatId, string messageId, string thumbnailUrl)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(cacheRoot, chatId);
        for (int i = 0; i < cached.Count; i++)
        {
            if (cached[i].messageId == messageId)
            {
                cached[i].thumbnailUrl = thumbnailUrl;
                ChatHistoryCache.SaveHistory(cacheRoot, chatId, cached);
                return;
            }
        }
    }

    /// <summary>Resets all queue state. Called from SetActiveBot after StopAllCoroutines
    /// (which already cancels in-flight extraction coroutines running on this).</summary>
    public void ClearVideoThumbQueue()
    {
        _videoThumbQueue?.Clear();
        _pendingThumbVms.Clear();
    }
}
