using NUnit.Framework;
using UnityEngine;

public class QuotedFieldsCacheTests
{
    [Test]
    public void MessageViewModel_QuotedFields_SurviveJsonUtility()
    {
        var vm = new MessageViewModel
        {
            messageId = "m1",
            quotedMessageId = "q1",
            quotedSenderName = "You",
            quotedText = "hello",
            quotedType = MessageType.Image,
            quotedThumbnailUrl = "thumb://q1"
        };

        var back = JsonUtility.FromJson<MessageViewModel>(JsonUtility.ToJson(vm));

        Assert.AreEqual("q1", back.quotedMessageId);
        Assert.AreEqual("You", back.quotedSenderName);
        Assert.AreEqual("hello", back.quotedText);
        Assert.AreEqual(MessageType.Image, back.quotedType);
        Assert.AreEqual("thumb://q1", back.quotedThumbnailUrl);
    }
}
