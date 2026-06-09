using System;

/// <summary>
/// What to do with the bytes returned for a sticker download attempt.
/// </summary>
public enum StickerLoadAction
{
    /// <summary>Bytes are a valid WebP sticker — cache, pin and render them.</summary>
    Render,

    /// <summary>Bytes aren't a sticker (Wappi's /media/download returns junk for not-yet-hosted
    /// stickers, and it's unreliable) — re-request after a short delay. Never cache/pin these.</summary>
    Retry,

    /// <summary>Out of retries — show the placeholder and wait for a later sync/reopen to pick up
    /// the real hosted URL.</summary>
    GiveUp
}

/// <summary>
/// Pure (UnityEngine-free, EditMode-testable) policy for a sticker download attempt.
///
/// Context: WhatsApp stickers expose no content hash and no inline body via Wappi (body is null);
/// the only media source is <c>s3Info.url</c>, which is absent until Wappi finishes hosting the
/// sticker. While unhosted, the app falls back to <c>/message/media/download</c>, which is
/// unreliable — it returns the real WebP for some attempts and unrelated junk (e.g. a CSV) for
/// others. The old code cached and pinned whatever came back, turning a transient hosting delay
/// into a permanent empty slot.
///
/// This policy says: render only real WebP bytes; treat anything else (junk or a failed fetch =
/// null bytes) as a retryable miss up to a cap; then give up to the placeholder. Callers must
/// never cache or pin bytes for the Retry/GiveUp outcomes.
/// </summary>
public static class StickerLoadPolicy
{
    /// <summary>
    /// Decides the action for a sticker download attempt.
    /// </summary>
    /// <param name="downloadedBytes">Bytes from this attempt, or null on fetch failure.</param>
    /// <param name="attempt">Zero-based index of this attempt.</param>
    /// <param name="maxAttempts">Total attempts allowed (must be &gt;= 1).</param>
    public static StickerLoadAction Decide(byte[] downloadedBytes, int attempt, int maxAttempts)
    {
        if (WebPSignature.IsWebP(downloadedBytes)) return StickerLoadAction.Render;
        return attempt + 1 < maxAttempts ? StickerLoadAction.Retry : StickerLoadAction.GiveUp;
    }
}
