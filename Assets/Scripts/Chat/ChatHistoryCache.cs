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

        string messagesDir = Path.Combine(baseDir, "messages");
        if (!Directory.Exists(messagesDir)) Directory.CreateDirectory(messagesDir);

        string path = Path.Combine(messagesDir, $"{chatId}.json");

        MessageListWrapper wrapper = new MessageListWrapper { messages = messages };
        string json = JsonUtility.ToJson(wrapper);

        File.WriteAllText(path, json);
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

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            MessageListWrapper wrapper = JsonUtility.FromJson<MessageListWrapper>(json);

            if (wrapper != null && wrapper.messages != null)
            {
                return wrapper.messages;
            }
        }

        return new List<MessageViewModel>();
    }
}
