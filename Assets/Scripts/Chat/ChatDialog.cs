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
    public bool isArchived;
    public bool isDeleted;
    public int unread_count;
    public string last_message_id;
    public string last_message_type;
    public string last_message_delivery_status;
    public ChatSender last_message_sender;
}