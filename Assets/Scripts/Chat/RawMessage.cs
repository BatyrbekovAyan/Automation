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
    public string stanzaId;   // For reactions: id of the target message being reacted to.
    public string from;       // Reactor jid (group aggregation keys on this, not senderName).

    [JsonProperty("isReply")]
    public bool isReply;        // True when this message replies to another.

    [JsonProperty("reply_message")]
    public JToken replyMessage; // Snapshot of the quoted message (id/type/body/caption/...).

    [JsonProperty("delivery_status")]
    public string deliveryStatusRaw;

    public JToken body;   // Contains the encrypted URL (bad)
    public JToken s3Info; // Contains the hosted URL (good)

    [JsonProperty("media_info")] // ⬅️ Add this
    public JToken mediaInfo;

    // Telegram (tapi) media carries file name + mime as FLAT top-level fields (WhatsApp
    // carries them inside the body JObject). Read only on the Telegram Normalize path.
    public string mimetype;

    [JsonProperty("file_name")]
    public string fileName;

    // Telegram GIFs (animations) arrive type:"sticker" + isGif:true + mimetype:"video/mp4"
    // (SHAPES.md Q2 / 05-HUMAN-UAT gap 3). Refine routes them into the video pipeline; this flag
    // (default false, absent key => false) drives the "GIF" badge overlay. Read only on the
    // Telegram path — WhatsApp never sends it.
    [JsonProperty("isGif")]
    public bool isGif;

    // Telegram (tapi) reactions ride ON the target message: an array of
    // {reaction,count,user_id,contact_name,type:"emoji"}, null when unreacted. WhatsApp
    // reactions arrive as separate type:"reaction" rows instead — read only on the Telegram path.
    [JsonProperty("reactions")]
    public JToken reactions;
}