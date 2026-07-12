using System;

[Serializable]
public class ChatDialog
{
    public string id;
    public bool isGroup;
    public string name;
    public string thumbnail;
    public string last_message_data;
    public string last_timestamp;
    // Telegram (tapi) chats/filter fields. JsonUtility leaves them empty for WhatsApp
    // (api never sends them), which keeps the last_time fallback and type-based
    // groupness channel-free. last_time is an RFC3339 string like last_timestamp.
    public string last_time;
    public string type;
    public bool isArchived;
    public bool isDeleted;
    public int unread_count;
    public string last_message_id;
    public string last_message_type;
    public string last_message_delivery_status;
    public ChatSender last_message_sender;
}