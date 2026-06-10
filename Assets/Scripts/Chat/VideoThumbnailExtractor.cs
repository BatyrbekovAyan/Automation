using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Extracts a single thumbnail frame from a (remote) video URL into outputPath.
/// On iOS this drives the native AVAssetImageGenerator job (VideoThumbnailExtractor.mm);
/// on Android the native MediaMetadataRetriever job (VideoThumbnailExtractor.java) — both
/// polled from a coroutine. In the Editor there is no native extractor, so it reports
/// onError and the caller keeps the server JPEGThumbnail. Mirrors VideoConverter. Never throws.
/// </summary>
public static class VideoThumbnailExtractor
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int    _StartThumbExtract(string url, string outPath, double timeSec);
    [DllImport("__Internal")] private static extern int    _PollThumbExtract(int jobId);   // 0 run, 1 done, 2 fail
    [DllImport("__Internal")] private static extern IntPtr _ThumbExtractError(int jobId);
    [DllImport("__Internal")] private static extern void   _FreeThumbExtractJob(int jobId);
#elif UNITY_ANDROID && !UNITY_EDITOR
    // Cached for the app lifetime (mirrors AndroidBridge.cs). Drives the Java job/poll API.
    private static readonly AndroidJavaClass _android = new AndroidJavaClass("com.unity.video.VideoThumbnailExtractor");
#endif

    /// <summary>
    /// True only on platforms with a real native extractor (device iOS / Android). In
    /// the Editor and anywhere else Extract reports onError immediately, so callers can
    /// gate retry/re-fetch work on this to avoid pointless network round-trips.
    /// </summary>
    public static bool IsSupported =>
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        true;
#else
        false;
#endif

    // Hard cap on the native poll loop. AVAssetImageGenerator / MediaMetadataRetriever read the
    // remote video over the network to grab a frame; if that read stalls the native job can sit in
    // the "running" state indefinitely, and an uncapped poll loop would hang the recovery coroutine
    // forever — orphaning the bubble's loading spinner and wedging the limited extraction queue.
    private const float ExtractTimeoutSec = 30f;

    /// <summary>
    /// Yields until extraction finishes. Invokes onResult(outputPath) on success, or
    /// onError(message) on failure, timeout, or on a platform without a native extractor.
    /// </summary>
    public static IEnumerator Extract(string url, string outputPath, double timeSec,
                                      Action<string> onResult, Action<string> onError)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(outputPath))
        {
            onError?.Invoke("empty url or output path");
            yield break;
        }
#if UNITY_IOS && !UNITY_EDITOR
        int jobId = _StartThumbExtract(url, outputPath, timeSec);
        float deadline = Time.realtimeSinceStartup + ExtractTimeoutSec;
        int status = _PollThumbExtract(jobId);
        while (status == 0)
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                _FreeThumbExtractJob(jobId);
                onError?.Invoke("thumbnail extraction timed out");
                yield break;
            }
            yield return null;
            status = _PollThumbExtract(jobId);
        }

        if (status == 2)
        {
            string message = Marshal.PtrToStringAnsi(_ThumbExtractError(jobId)) ?? "thumbnail extraction failed";
            _FreeThumbExtractJob(jobId);
            onError?.Invoke(message);
            yield break;
        }

        _FreeThumbExtractJob(jobId);
        onResult?.Invoke(outputPath);
#elif UNITY_ANDROID && !UNITY_EDITOR
        int jobId = _android.CallStatic<int>("startThumbExtract", url, outputPath, timeSec);
        float deadline = Time.realtimeSinceStartup + ExtractTimeoutSec;
        int status = _android.CallStatic<int>("pollThumbExtract", jobId);
        while (status == 0)
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                _android.CallStatic("freeThumbExtractJob", jobId);
                onError?.Invoke("thumbnail extraction timed out");
                yield break;
            }
            yield return null;
            status = _android.CallStatic<int>("pollThumbExtract", jobId);
        }

        if (status == 2)
        {
            string message = _android.CallStatic<string>("thumbExtractError", jobId) ?? "thumbnail extraction failed";
            _android.CallStatic("freeThumbExtractJob", jobId);
            onError?.Invoke(message);
            yield break;
        }

        _android.CallStatic("freeThumbExtractJob", jobId);
        onResult?.Invoke(outputPath);
#else
        // Editor: no native extractor — caller keeps the server thumbnail.
        onError?.Invoke("no native thumbnail extractor on this platform");
        yield break;
#endif
    }
}
