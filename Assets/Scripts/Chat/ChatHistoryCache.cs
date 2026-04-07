using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ChatHistoryCache
{
    // Unity needs a "wrapper" class to save Lists to JSON
    [System.Serializable]
    private class MessageListWrapper
    {
        public List<MessageViewModel> messages;
    }

    /// <summary>
    /// Saves a list of messages to the phone's hard drive.
    /// </summary>
    public static void SaveHistory(string chatId, List<MessageViewModel> messages)
    {
        // Creates a unique file for every chat, e.g., "chat_79001234567.json"
        string path = Path.Combine(Application.persistentDataPath, $"chat_{chatId}.json");
        
        MessageListWrapper wrapper = new MessageListWrapper { messages = messages };
        string json = JsonUtility.ToJson(wrapper);
        
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads the chat history instantly from the hard drive.
    /// </summary>
    public static List<MessageViewModel> LoadHistory(string chatId)
    {
        string path = Path.Combine(Application.persistentDataPath, $"chat_{chatId}.json");
        
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            MessageListWrapper wrapper = JsonUtility.FromJson<MessageListWrapper>(json);
            
            if (wrapper != null && wrapper.messages != null)
            {
                return wrapper.messages;
            }
        }
        
        // Return an empty list if this is a brand new chat we've never opened before
        return new List<MessageViewModel>(); 
    }
}