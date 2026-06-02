using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Extracts a single thumbnail frame from a (remote) video URL into outputPath.
/// On iOS this drives the native AVAssetImageGenerator job (VideoThumbnailExtractor.mm)
/// and polls it from a coroutine. In the Editor and on Android there is no native
/// extractor, so it reports onError and the caller keeps the server JPEGThumbnail.
/// Mirrors VideoConverter. Never throws.
/// </summary>
public static class VideoThumbnailExtractor
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int    _StartThumbExtract(string url, string outPath, double timeSec);
    [DllImport("__Internal")] private static extern int    _PollThumbExtract(int jobId);   // 0 run, 1 done, 2 fail
    [DllImport("__Internal")] private static extern IntPtr _ThumbExtractError(int jobId);
    [DllImport("__Internal")] private static extern void   _FreeThumbExtractJob(int jobId);
#endif

    /// <summary>
    /// Yields until extraction finishes. Invokes onResult(outputPath) on success, or
    /// onError(message) on failure or on a platform without a native extractor.
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
        int status = _PollThumbExtract(jobId);
        while (status == 0)
        {
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
#else
        // Editor + Android: no native extractor — caller keeps the server thumbnail.
        onError?.Invoke("no native thumbnail extractor on this platform");
        yield break;
#endif
    }
}
