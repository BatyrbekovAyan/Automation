using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Converts a picked video to an MP4 (H.264/AAC) file suitable for Wappi upload.
/// On iOS this calls a native AVAssetExportSession plugin (VideoConverter.mm) and
/// polls it from a coroutine. In the Editor and on Android there is no native
/// converter, so it passes the original path through unchanged — meaning Android
/// video sends remain unconverted (and will fail) until a future Media3 phase.
/// </summary>
public static class VideoConverter
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int    _StartVideoConvert(string inPath, string outPath, long maxBytes);
    [DllImport("__Internal")] private static extern int    _PollVideoConvert(int jobId);   // 0 run, 1 done, 2 fail, 3 use-original
    [DllImport("__Internal")] private static extern IntPtr _VideoConvertError(int jobId);
    [DllImport("__Internal")] private static extern void   _FreeVideoConvertJob(int jobId);
    [DllImport("__Internal")] private static extern float  _PollVideoConvertProgress(int jobId);
#endif

    /// <summary>
    /// Yields until conversion finishes. Invokes onResult with the path to upload
    /// (the converted .mp4, or the original path when no conversion is needed), or
    /// invokes onError with a message. Never throws.
    /// </summary>
    public static IEnumerator Convert(string inputPath, string outputPath, long maxBytes,
                                      Action<string> onResult, Action<string> onError)
    {
#if UNITY_IOS && !UNITY_EDITOR
        int jobId = _StartVideoConvert(inputPath, outputPath, maxBytes);
        int status = _PollVideoConvert(jobId);
        while (status == 0)
        {
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
        string resolved = status == 3 ? inputPath : outputPath;
        _FreeVideoConvertJob(jobId);
        onResult?.Invoke(resolved);
#else
        // Editor + Android: no native converter — pass the original through unchanged.
        onResult?.Invoke(inputPath);
        yield break;
#endif
    }
}
