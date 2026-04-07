using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // Make sure this is included

[Serializable]
public class RawMessage
{
    public string id;
    public string type;
    public string chatId;
    public string senderName;
    public bool fromMe;
    public long time;
    public string caption;
    
    public JToken body;   // Contains the encrypted URL (bad)
    public JToken s3Info; // Contains the hosted URL (good)
    
    [JsonProperty("media_info")] // ⬅️ Add this
    public JToken mediaInfo;
}