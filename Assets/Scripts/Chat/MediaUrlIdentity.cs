using System;

/// <summary>
/// Pure (UnityEngine-free, EditMode-testable) identity check for media URLs.
///
/// Wappi stores each media file once under a uuid filename but hands out different
/// addresses for it over time: the /media/download recovery file_link on first open,
/// then a hosted s3 URL on later syncs. The full strings (and therefore their MD5
/// cache keys — see MediaCacheManager.GetFilePathFromUrl) differ, yet the
/// query-stripped last path segment — the file uuid — names the same stored file.
/// SameFile is the gate MediaCacheManager.TryAliasCachedImage relies on to carry
/// already-downloaded bytes across such an address change.
///
/// Deliberately strict: both sides must be http(s) URLs with a real path, and the
/// tail must be uuid-grade (>= MinTailLength chars). Short generic segments
/// ("download", "media") never match, so two unrelated endpoint URLs can't be
/// declared the same file.
/// </summary>
public static class MediaUrlIdentity
{
    // A Wappi/s3 file tail is a uuid (36 chars), usually with an extension; generic
    // endpoint segments are far shorter. 16 keeps every real file id and rejects
    // every generic path word.
    private const int MinTailLength = 16;

    /// <summary>
    /// True only when both URLs name the same underlying stored file: identical
    /// uuid-grade, query-stripped last path segment. False for null/empty, non-http
    /// schemes (base64://, thumb://), host-only URLs, and short generic tails.
    /// </summary>
    public static bool SameFile(string a, string b)
    {
        string tailA = FileTail(a);
        return tailA != null && string.Equals(tailA, FileTail(b), StringComparison.Ordinal);
    }

    private static string FileTail(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        int schemeEnd;
        if (url.StartsWith("https://", StringComparison.Ordinal)) schemeEnd = 8;
        else if (url.StartsWith("http://", StringComparison.Ordinal)) schemeEnd = 7;
        else return null;

        int queryStart = url.IndexOf('?');
        string path = queryStart >= 0 ? url.Substring(0, queryStart) : url;
        path = path.TrimEnd('/');

        int lastSlash = path.LastIndexOf('/');
        if (lastSlash < schemeEnd) return null; // host-only URL — no file path to compare

        string tail = path.Substring(lastSlash + 1);
        return tail.Length >= MinTailLength ? tail : null;
    }
}
