using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Partial class extension backing the Profile → Конфиденциальность page:
// media-cache size/clear across ALL bot cache dirs, and the full local
// chat-history clear. Deliberately does NOT route through PurgeCacheForBot —
// that path is bot-DELETION semantics (it switches the active bot away).
public partial class ChatManager
{
    private static string BotCacheRootDir =>
        Path.Combine(Application.persistentDataPath, "BotCache");

    /// <summary>
    /// Sums every BotCache/*/media directory, time-sliced so a large cache
    /// never hitches the main thread. Runs on the caller's MonoBehaviour so
    /// ChatManager.StopAllCoroutines can't kill an in-flight size scan.
    /// </summary>
    public IEnumerator ComputeMediaCacheSize(Action<long> done)
    {
        long total = 0;
        int scanned = 0;

        if (Directory.Exists(BotCacheRootDir))
        {
            foreach (string botDir in Directory.GetDirectories(BotCacheRootDir))
            {
                string mediaDir = Path.Combine(botDir, "media");
                if (!Directory.Exists(mediaDir)) continue;

                foreach (string file in Directory.EnumerateFiles(mediaDir))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { /* deleted mid-scan by a concurrent download — skip */ }
                    if (++scanned % 64 == 0) yield return null;
                }
            }
        }

        done?.Invoke(total);
    }

    /// <summary>
    /// Deletes every bot's media cache (images, video thumbs, avatars, link
    /// previews — all self-healing by re-download). The active bot goes through
    /// MediaCacheManager.ClearCache so its url→path memo resets too.
    /// </summary>
    public void ClearAllMediaCaches()
    {
        ClearVideoThumbQueue();

        if (MediaCacheManager.Instance != null)
            MediaCacheManager.Instance.ClearCache();

        if (!Directory.Exists(BotCacheRootDir)) return;

        string activeBot = SanitizeBotId(CurrentBotId);
        foreach (string botDir in Directory.GetDirectories(BotCacheRootDir))
        {
            if (Path.GetFileName(botDir) == activeBot) continue; // already cleared above
            string mediaDir = Path.Combine(botDir, "media");
            if (!Directory.Exists(mediaDir)) continue;
            try { Directory.Delete(mediaDir, recursive: true); }
            catch (Exception e) { Debug.LogWarning($"[ChatManager] Media clear failed for {mediaDir}: {e.Message}"); }
        }
    }

    /// <summary>
    /// Clears all locally cached conversation data for every bot: chat lists,
    /// message history, quote/reaction resolver caches, outboxes, and the raw
    /// response dump. Media stays (that's ClearAllMediaCaches). Server-side
    /// history in WhatsApp/Telegram is untouched; the visible list reloads
    /// fresh from the server afterwards.
    /// </summary>
    public void ClearAllLocalHistory()
    {
        // Stop in-flight work exactly like SetActiveBot does, so a killed
        // coroutine can't resurrect just-deleted cache files.
        if (_syncWaitRoutine != null) { StopCoroutine(_syncWaitRoutine); _syncWaitRoutine = null; }
        StopAllCoroutines();
        _chatFetchesInFlight = 0;
        _chatListSyncing = false;
        ClearVideoThumbQueue();
        ClearMediaDownloadQueue();

        if (Directory.Exists(BotCacheRootDir))
        {
            foreach (string botDir in Directory.GetDirectories(BotCacheRootDir))
                ClearHistoryFilesIn(botDir);
        }

        try
        {
            string responseDump = Path.Combine(Application.persistentDataPath, "response.txt");
            if (File.Exists(responseDump)) File.Delete(responseDump);
        }
        catch (Exception e) { Debug.LogWarning($"[ChatManager] response.txt delete failed: {e.Message}"); }

        // In-memory state that mirrors the deleted files.
        Chats.Clear();
        chatLookup.Clear();
        seenMessageIds.Clear();
        _reactions.Clear();
        _activeChatCache = null;
        _cachedQueue = null;
        _pendingFirstBatch = null;
        _pendingLiveSyncMessages = null;
        _outbox = null;

        OnChatListCleared?.Invoke();
        BeginLoadForActiveBot();
    }

    private static void ClearHistoryFilesIn(string botDir)
    {
        try
        {
            string messagesDir = Path.Combine(botDir, "messages");
            if (Directory.Exists(messagesDir)) Directory.Delete(messagesDir, recursive: true);

            foreach (string name in new[] { "chats.json", "quoted_messages.json", "reaction_targets.json" })
            {
                string path = Path.Combine(botDir, name);
                if (File.Exists(path)) File.Delete(path);
            }

            foreach (string outbox in Directory.GetFiles(botDir, "outbox_*.json"))
                File.Delete(outbox);

            // Resolver caches hold a static in-memory layer keyed by this dir;
            // clearing only the files would re-persist the old map on next Put.
            QuotedMessageCache.Clear(botDir);
            ReactionTargetCache.Clear(botDir);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChatManager] History clear failed for {botDir}: {e.Message}");
        }
    }
}
