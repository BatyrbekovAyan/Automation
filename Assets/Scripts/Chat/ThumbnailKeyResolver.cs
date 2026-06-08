using System;

/// <summary>
/// Pure (UnityEngine-free, EditMode-testable) resolver for which cache key a media bubble
/// should load its thumbnail from.
///
/// The <c>thumb://{id}</c> / <c>vthumb://{id}</c> cache files are keyed by message id, so they
/// survive even when a VM's <c>thumbnailUrl</c> pointer gets blanked (e.g. an aged Wappi payload
/// overwrote it). When the VM still carries a populated pointer we honor it; otherwise we
/// reconstruct the id-stable keys and load whichever file is actually on disk — preferring our
/// own HD native frame (<c>vthumb://</c>) over the server thumbnail (<c>thumb://</c>). Returns
/// null when nothing is available, so the caller falls back to a placeholder / recovery fetch.
/// </summary>
public static class ThumbnailKeyResolver
{
    /// <summary>
    /// Returns the cache key to load, or null if none is available.
    /// <paramref name="isCached"/> reports whether a reconstructed key has a file on disk
    /// (inject MediaCacheManager.IsImageCached at the call site; a fake in tests).
    /// </summary>
    public static string Resolve(string thumbnailUrl, string messageId, Func<string, bool> isCached)
    {
        // A populated pointer wins — honors a healthy VM (and base64:// inline thumbnails).
        if (!string.IsNullOrEmpty(thumbnailUrl)) return thumbnailUrl;

        if (string.IsNullOrEmpty(messageId) || isCached == null) return null;

        string vthumb = "vthumb://" + messageId;
        if (isCached(vthumb)) return vthumb;

        string thumb = "thumb://" + messageId;
        if (isCached(thumb)) return thumb;

        return null;
    }
}
