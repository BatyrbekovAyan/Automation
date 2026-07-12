using NUnit.Framework;
using Newtonsoft.Json;

/// <summary>
/// tapi requires a recipient in the reaction body ({body, message_id, recipient});
/// WhatsApp's reaction body is only {body, message_id}. The recipient field is
/// serialized only when set (NullValueHandling.Ignore), so WhatsApp stays
/// byte-identical. These pure serialization tests lock that shape.
/// </summary>
public class TelegramReactionRequestTests
{
    [Test]
    public void WhatsApp_Reaction_OmitsRecipient()
    {
        var req = new WappiSendReactionRequest { body = "❤️", message_id = "M1" };
        string json = JsonConvert.SerializeObject(req);
        Assert.IsFalse(json.Contains("recipient"), json);
    }

    [Test]
    public void Telegram_Reaction_IncludesRecipient()
    {
        var req = new WappiSendReactionRequest { body = "❤️", message_id = "M1", recipient = "89323786" };
        string json = JsonConvert.SerializeObject(req);
        StringAssert.Contains("recipient", json);
        StringAssert.Contains("89323786", json);
    }

    [Test]
    public void EmptyBody_RemovesReaction_RecipientStillOmittedWhenNull()
    {
        // Empty body removes the reaction on both channels; a WhatsApp removal
        // still omits recipient.
        var req = new WappiSendReactionRequest { body = "", message_id = "M1" };
        string json = JsonConvert.SerializeObject(req);
        StringAssert.Contains("\"body\":\"\"", json);
        Assert.IsFalse(json.Contains("recipient"), json);
    }
}
