using NUnit.Framework;
using Newtonsoft.Json;

/// <summary>
/// The tapi reply endpoint (POST tapi/sync/message/reply) takes a body of exactly
/// {body, message_id} — NO recipient (the message_id implies the chat) and NO
/// quoted_message_id (that is the WhatsApp message/send reply mechanism). These
/// pure serialization tests lock the WappiSendReplyRequest DTO shape.
/// </summary>
public class TelegramReplyRequestTests
{
    [Test]
    public void ReplyRequest_Serializes_BodyAndMessageId()
    {
        var req = new WappiSendReplyRequest { body = "hi", message_id = "ABC123" };
        string json = JsonConvert.SerializeObject(req);
        StringAssert.Contains("\"body\":\"hi\"", json);
        StringAssert.Contains("\"message_id\":\"ABC123\"", json);
    }

    [Test]
    public void ReplyRequest_HasNoRecipient()
    {
        var req = new WappiSendReplyRequest { body = "hi", message_id = "ABC123" };
        string json = JsonConvert.SerializeObject(req);
        Assert.IsFalse(json.Contains("recipient"), json);
    }

    [Test]
    public void ReplyRequest_HasNoQuotedMessageId()
    {
        var req = new WappiSendReplyRequest { body = "hi", message_id = "ABC123" };
        string json = JsonConvert.SerializeObject(req);
        Assert.IsFalse(json.Contains("quoted_message_id"), json);
    }
}
