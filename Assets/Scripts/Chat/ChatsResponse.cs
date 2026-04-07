using System;
using System.Collections.Generic;

[Serializable]
public class ChatsResponse
{
    public string status;
    public List<ChatDialog> dialogs;
}