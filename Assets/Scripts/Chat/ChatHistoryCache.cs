using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ChatHistoryCache
{
    [System.Serializable]
    private class MessageListWrapper
    {
        public List<MessageViewModel> messages;
    }

    /// <summary>
    /// Saves a list of messages to {baseDir}/messages/{chatId}.json.
    /// </summary>
    public static void SaveHistory(string baseDir, string chatId, List<MessageViewModel> messages)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(chatId)) return;

        // Never persist another chat's messages under this chat's file. A prior Wappi
        // messages/get response crossing (see CrossChatResponseGuard) could have spliced
        // foreign entries into the in-memory list now being saved — drop them at this choke
        // point so the poison can't reach (or survive on) disk.
        StripForeignMessages(messages, chatId);

        // Media floor: aged Wappi payloads come back with empty thumbnail/url for outgoing
        // videos, and naively persisting one wipes a previously-good preview. Carry the
        // existing on-disk media forward into any incoming entry whose fields are empty,
        // so empty never overwrites good for the same message id. Strictly additive — a
        // genuinely fresh (non-empty) url on the incoming side still wins. Single choke
        // point: every SaveHistory caller is covered here.
        if (messages != null)
        {
            MessageMediaMerge.ApplyMediaFloor(messages, LoadHistory(baseDir, chatId));
        }

        string messagesDir = Path.Combine(baseDir, "messages");
        if (!Directory.Exists(messagesDir)) Directory.CreateDirectory(messagesDir);

        string path = Path.Combine(messagesDir, $"{chatId}.json");
        string tmp = path + ".tmp";

        MessageListWrapper wrapper = new MessageListWrapper { messages = messages };
        string json = JsonUtility.ToJson(wrapper);

        try
        {
            File.WriteAllText(tmp, json);

            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ChatHistoryCache] Failed to persist history for {chatId}: {ex.Message}");
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// Loads chat history from {baseDir}/messages/{chatId}.json.
    /// Returns an empty list if the file doesn't exist.
    /// </summary>
    public static List<MessageViewModel> LoadHistory(string baseDir, string chatId)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(chatId))
            return new List<MessageViewModel>();

        string path = Path.Combine(baseDir, "messages", $"{chatId}.json");

        if (!File.Exists(path)) return new List<MessageViewModel>();

        try
        {
            string json = File.ReadAllText(path);
            MessageListWrapper wrapper = JsonUtility.FromJson<MessageListWrapper>(json);

            if (wrapper != null && wrapper.messages != null)
            {
                // Strip any foreign-chat entries left in this file by a prior Wappi
                // messages/get response crossing — the chat renders cache-first, so without
                // this the old poison keeps showing up on every open even after the network
                // guard stops new crossings. Self-heals: the next SaveHistory rewrites it clean.
                StripForeignMessages(wrapper.messages, chatId);
                return wrapper.messages;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatHistoryCache] Corrupted history at {path}: {ex.Message}. Treating as empty.");
        }

        return new List<MessageViewModel>();
    }

    /// <summary>
    /// Deletes {baseDir}/messages/{chatId}.json if present. Null/empty-safe; never throws.
    /// </summary>
    public static void DeleteHistory(string baseDir, string chatId)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(chatId)) return;
        string path = Path.Combine(baseDir, "messages", $"{chatId}.json");
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatHistoryCache] DeleteHistory failed for {chatId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes every message whose own <see cref="MessageViewModel.chatId"/> names a DIFFERENT
    /// chat than this cache file's id — poison written by Wappi's concurrent messages/get response
    /// crossing. Conservative: an empty/absent chatId is kept (legacy entries from before chatId
    /// was reliably populated must not be dropped). Mutates the list in place; returns the count
    /// removed.
    /// </summary>
    private static int StripForeignMessages(List<MessageViewModel> messages, string chatId)
    {
        if (messages == null || string.IsNullOrEmpty(chatId)) return 0;

        int removed = messages.RemoveAll(m =>
            m != null && !string.IsNullOrEmpty(m.chatId) && m.chatId != chatId);

        if (removed > 0)
            Debug.LogWarning($"[ChatHistoryCache] Stripped {removed} foreign-chat message(s) from {chatId} cache.");

        return removed;
    }
}
