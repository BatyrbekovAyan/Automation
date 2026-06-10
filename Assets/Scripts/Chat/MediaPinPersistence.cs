using System.Collections.Generic;

/// <summary>
/// Persists a recovery-pinned media URL into the on-disk chat history.
///
/// MessageItemView.LoadStickerViaDownload pins vm.mediaUrl on the in-memory VM after
/// validating the recovered WebP, but the history file was saved before recovery ran —
/// without a re-save the pin dies with the session, the next open loads an empty
/// mediaUrl, and the sticker's bytes (already in the media cache) are downloaded again.
/// Same load-patch-save shape as ChatManager.UpdateCachedThumbnailUrl uses for video
/// thumbnail pins.
/// </summary>
public static class MediaPinPersistence
{
    /// <summary>
    /// Patches the persisted entry for <paramref name="messageId"/> with the pinned
    /// mediaUrl + expireTime and saves the history. Returns true when an entry was
    /// found (written, or already holding these exact values); false on bad args or
    /// when the message isn't in the on-disk history yet — benign, the pin simply
    /// stays in-memory exactly as before.
    /// </summary>
    public static bool PersistMediaUrl(string baseDir, string chatId, string messageId,
                                       string mediaUrl, long expireTime)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(chatId)
            || string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(mediaUrl)) return false;

        List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(baseDir, chatId);
        for (int i = 0; i < cached.Count; i++)
        {
            if (cached[i] == null || cached[i].messageId != messageId) continue;

            // Idempotency guard: a re-bind can re-run recovery for an already-pinned
            // sticker — don't rewrite the file when nothing changes.
            if (cached[i].mediaUrl == mediaUrl && cached[i].expireTime == expireTime) return true;

            cached[i].mediaUrl = mediaUrl;
            cached[i].expireTime = expireTime;
            ChatHistoryCache.SaveHistory(baseDir, chatId, cached);
            return true;
        }
        return false;
    }
}
