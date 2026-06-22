public static class DeletedChatRule
{
    /// <summary>
    /// Decide whether a chat should be hidden given its per-bot deletion watermark and current
    /// last-message timestamp. A watermarked chat hides while ts is not newer than the watermark.
    /// A chat with no watermark that Wappi reports isDeleted is hidden and adopted (adoptWatermark
    /// = ts) so future newer activity revives it. Everything else shows.
    /// </summary>
    public static bool ShouldHide(bool hasWatermark, long watermark, long ts, bool isDeleted, out long adoptWatermark)
    {
        if (hasWatermark) { adoptWatermark = -1; return ts <= watermark; }
        if (isDeleted)    { adoptWatermark = ts; return true; }
        adoptWatermark = -1; return false;
    }
}
