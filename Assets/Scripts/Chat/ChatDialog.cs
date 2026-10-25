using System;

/// <summary>
/// WhatsApp chats/filter: the chat's address-book entry. <see cref="FullName"/> is the name the phone's address book gives
/// the person — empty for a stranger. Wappi's <c>Found</c> flag next to it means nothing and <c>PushName</c> is what the
/// person calls themselves, so neither is declared here.
/// </summary>
[Serializable]
public class ChatDialogContact
{
    public string FullName;
}

/// <summary>
/// Telegram (tapi) chats/filter: the dialog's user card, reduced to the four flags the personal / business rule reads
/// (ChatAudienceRule). The real card has many more fields; JsonUtility skips what is not declared. Keep it to these four: a
/// field named Bot shadows the <c>Bot</c> class inside this class, so nothing here may name that class.
/// </summary>
[Serializable]
public class ChatDialogUser
{
    public bool Contact;
    public bool Bot;
    public bool Support;
    public bool Self;
}

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
    // WhatsApp rows carry `contact`, Telegram rows `user`; each channel leaves the other's out. JsonUtility never leaves
    // either null: an object the row lacks reads as an empty name / all flags false (see ChatAudienceFacts).
    public ChatDialogContact contact;
    public ChatDialogUser user;
}