using System;

[Serializable]
public class ChatSender
{
    public bool isMe;
    public string pushname; // sender's display name (used for group chat-list row prefixes)
}
