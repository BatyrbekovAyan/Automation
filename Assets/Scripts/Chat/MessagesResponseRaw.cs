using System;
using System.Collections.Generic;

[Serializable]
public class MessagesResponseRaw
{
    public string status;
    public List<RawMessage> messages;
}