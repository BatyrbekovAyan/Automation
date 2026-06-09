using System;

/// <summary>
/// Pure (UnityEngine-free, EditMode-testable) detector for the WebP container
/// signature. WhatsApp stickers are always WebP, so the sticker render path uses this
/// to refuse painting non-WebP bytes — a mis-sourced photo from the media pipeline —
/// with sticker styling. That mis-paint was the "random photo shown inside a sticker
/// bubble" symptom: <c>Texture2D.LoadImage</c> happily decodes a JPEG/PNG and the
/// sticker branch renders it transparent + 1:1.
///
/// A WebP file is a RIFF container whose form type is "WEBP":
/// bytes 0-3 = "RIFF", bytes 4-7 = little-endian file size, bytes 8-11 = "WEBP".
/// </summary>
public static class WebPSignature
{
    /// <summary>
    /// True iff <paramref name="data"/> begins with a RIFF/WEBP container header.
    /// Returns false for null, fewer than 12 bytes, or any non-WebP payload
    /// (JPEG, PNG, RIFF/WAVE, …). Never throws.
    /// </summary>
    public static bool IsWebP(byte[] data)
    {
        if (data == null || data.Length < 12) return false;

        return data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P';
    }
}
