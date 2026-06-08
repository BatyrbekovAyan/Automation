using System;

/// <summary>
/// Pure (UnityEngine-free, EditMode-testable) sanitizer for the base64
/// <c>JPEGThumbnail</c> strings Wappi embeds in media message bodies. Servers send
/// these inconsistently across message types — sometimes with a
/// <c>data:image/jpeg;base64,</c> prefix, embedded whitespace/newlines, the URL-safe
/// (<c>-</c>/<c>_</c>) alphabet, or stripped padding — so a raw
/// <see cref="Convert.FromBase64String"/> throws or yields nothing for some messages.
/// That turned a perfectly good server preview into a black placeholder. This
/// normalizes every known variant into decodable bytes and returns <c>false</c>
/// (with <paramref name="bytes"/> = null) when the payload is missing or truly
/// undecodable, so callers can skip staging a thumbnail that would only render black.
/// </summary>
public static class JpegThumbnailDecoder
{
    /// <summary>
    /// Sanitizes <paramref name="raw"/> and decodes it to JPEG bytes. Returns true
    /// and sets <paramref name="bytes"/> on success; returns false (bytes = null)
    /// for null/empty/whitespace-only input or any payload that can't be decoded.
    /// Never throws.
    /// </summary>
    public static bool TryDecodeBase64(string raw, out byte[] bytes)
    {
        bytes = null;
        if (string.IsNullOrEmpty(raw)) return false;

        string b64 = raw;

        // 1. Strip a data-URI prefix ("data:image/jpeg;base64,<payload>"). The base64
        //    alphabet never contains a comma, so the first comma unambiguously marks
        //    the end of the prefix.
        int comma = b64.IndexOf(',');
        if (comma >= 0) b64 = b64.Substring(comma + 1);

        // 2. Drop any whitespace the server wrapped the payload with (line breaks,
        //    spaces, tabs). FromBase64String tolerates some of this, but not all.
        b64 = b64.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
        if (b64.Length == 0) return false;

        // 3. Translate the URL-safe alphabet back to the standard one.
        b64 = b64.Replace('-', '+').Replace('_', '/');

        // 4. Restore '=' padding to a multiple of 4. A remainder of 1 is never a valid
        //    base64 length, so reject it outright rather than feeding garbage to decode.
        int remainder = b64.Length % 4;
        if (remainder == 1) return false;
        if (remainder > 0) b64 = b64.PadRight(b64.Length + (4 - remainder), '=');

        try
        {
            byte[] decoded = Convert.FromBase64String(b64);
            if (decoded == null || decoded.Length == 0) return false;
            bytes = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
