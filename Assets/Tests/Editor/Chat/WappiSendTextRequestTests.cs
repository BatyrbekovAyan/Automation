using NUnit.Framework;
using Newtonsoft.Json;

public class WappiSendTextRequestTests
{
    [Test]
    public void QuotedMessageId_Null_KeyOmitted()
    {
        var req = new WappiSendTextRequest { body = "hi", recipient = "123" };
        string json = JsonConvert.SerializeObject(req);
        Assert.IsFalse(json.Contains("quoted_message_id"), json);
    }

    [Test]
    public void QuotedMessageId_Set_KeyPresent()
    {
        var req = new WappiSendTextRequest { body = "hi", recipient = "123", quotedMessageId = "ABC123" };
        string json = JsonConvert.SerializeObject(req);
        StringAssert.Contains("quoted_message_id", json);
        StringAssert.Contains("ABC123", json);
    }
}
