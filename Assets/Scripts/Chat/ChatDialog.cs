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
    public int unread_count;
}