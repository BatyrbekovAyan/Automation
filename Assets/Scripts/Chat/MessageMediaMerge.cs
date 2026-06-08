using System.Collections.Generic;

/// <summary>
/// Pure (UnityEngine-free, EditMode-testable) "media floor" for cached messages.
///
/// Wappi's messages/get drops <c>JPEGThumbnail</c> and <c>s3Info.url</c> for outgoing
/// videos once they age (verified: only the 2 newest of 19 outgoing videos still carry
/// them; the rest come back with <c>s3Info:{}</c> and no thumbnail). Normalize faithfully
/// turns those aged payloads into empty-media VMs — and persisting one over a previously
/// good entry wipes the preview's thumbnail/url pointer (the bubble then renders black even
/// though the video still plays via an on-demand /media/download re-fetch).
///
/// The floor is applied at the cache-save boundary: for any incoming entry whose media
/// fields are empty, carry the populated values forward from the existing on-disk entry of
/// the same id. It is strictly additive — it only ever FILLS empty fields, so a genuinely
/// fresh url (e.g. an S3 signature refresh) on the incoming side always wins.
/// </summary>
public static class MessageMediaMerge
{
    /// <summary>
    /// Fills <paramref name="incoming"/>'s empty media fields from <paramref name="existing"/>
    /// (same logical message). Never overwrites a populated incoming field. <c>expireTime</c>
    /// only moves together with the url it describes, so expiry never desyncs from its link.
    /// </summary>
    public static void CarryForwardMedia(MessageViewModel incoming, MessageViewModel existing)
    {
        if (incoming == null || existing == null) return;

        // thumbnailUrl is a local, message-id-stable cache key (thumb://{id} / vthumb://{id})
        // — preserving it is pure win: it points at a file that may still be on disk.
        if (string.IsNullOrEmpty(incoming.thumbnailUrl) && !string.IsNullOrEmpty(existing.thumbnailUrl))
            incoming.thumbnailUrl = existing.thumbnailUrl;

        // videoUrl + expireTime travel together (expiry describes the signed link).
        if (string.IsNullOrEmpty(incoming.videoUrl) && !string.IsNullOrEmpty(existing.videoUrl))
        {
            incoming.videoUrl = existing.videoUrl;
            if (incoming.expireTime <= 0 && existing.expireTime > 0)
                incoming.expireTime = existing.expireTime;
        }

        // mediaUrl + expireTime travel together.
        if (string.IsNullOrEmpty(incoming.mediaUrl) && !string.IsNullOrEmpty(existing.mediaUrl))
        {
            incoming.mediaUrl = existing.mediaUrl;
            if (incoming.expireTime <= 0 && existing.expireTime > 0)
                incoming.expireTime = existing.expireTime;
        }

        // The upright extracted-frame aspect (set by native vthumb extraction) is ground
        // truth for the bubble shape; preserve it when the incoming payload lacks dims.
        if (incoming.aspectRatio <= 0f && existing.aspectRatio > 0f)
            incoming.aspectRatio = existing.aspectRatio;
    }

    /// <summary>
    /// Applies the media floor to <paramref name="incoming"/> in place: every entry that also
    /// exists in <paramref name="existing"/> (by messageId) gets its empty media fields filled
    /// from the existing entry. Returns the same <paramref name="incoming"/> list for chaining.
    /// No-op when either list is null/empty. Existing entries are indexed last-wins, which is
    /// fine because the on-disk history is id-unique in practice.
    /// </summary>
    public static List<MessageViewModel> ApplyMediaFloor(
        List<MessageViewModel> incoming, List<MessageViewModel> existing)
    {
        if (incoming == null || existing == null || existing.Count == 0) return incoming;

        var byId = new Dictionary<string, MessageViewModel>(existing.Count);
        foreach (var m in existing)
        {
            if (m == null || string.IsNullOrEmpty(m.messageId)) continue;
            byId[m.messageId] = m;
        }

        foreach (var inc in incoming)
        {
            if (inc == null || string.IsNullOrEmpty(inc.messageId)) continue;
            if (byId.TryGetValue(inc.messageId, out var prev))
                CarryForwardMedia(inc, prev);
        }

        return incoming;
    }
}
