using Newtonsoft.Json.Linq;
using NUnit.Framework;

// The six origin_* fields ride the SAME proxy call as the message (OPS meta section, never jsonBody).
public class SendOriginWireTests
{
    private static JObject Args() => JObject.FromObject(new { profile_id = "p", body = "текст", recipient = "7701", quoted_message_id = (string)null });

    [Test]
    public void NullOrigin_AddsNothing()
    {
        var a = Args();
        SendOriginWire.Append(a, null);
        Assert.IsNull(a["origin_kind"]);
        SendOriginWire.Append(null, new SendOrigin());   // no throw
    }

    [Test]
    public void Card_WritesAllSix()
    {
        var a = Args();
        SendOriginWire.Append(a, new SendOrigin { kind = SendOriginKind.Card, index = 2, requestKey = "0123456789abcdef", editPercent = 0, top1Match = false, setSize = 4 });
        Assert.AreEqual("card", (string)a["origin_kind"]);
        Assert.AreEqual(2, (int)a["origin_index"]);
        Assert.AreEqual("0123456789abcdef", (string)a["origin_key"]);
        Assert.AreEqual(0, (int)a["origin_edit"]);
        Assert.AreEqual(0, (int)a["origin_top1"]);
        Assert.AreEqual(4, (int)a["origin_set"]);
        Assert.AreEqual("текст", (string)a["body"], "the message itself is untouched");
    }

    [Test]
    public void Typed_HasNoIndexOrEdit_ButKeyTopAndSet()
    {
        var a = Args();
        SendOriginWire.Append(a, new SendOrigin { kind = SendOriginKind.Typed, requestKey = "abc", top1Match = true, setSize = 3 });
        Assert.AreEqual("typed", (string)a["origin_kind"]);
        Assert.AreEqual(JTokenType.Null, a["origin_index"].Type);
        Assert.AreEqual(JTokenType.Null, a["origin_edit"].Type);
        Assert.AreEqual(1, (int)a["origin_top1"]);
        Assert.AreEqual(3, (int)a["origin_set"]);
    }

    [Test]
    public void None_IsKindNoneWithNulls()
    {
        var a = Args();
        SendOriginWire.Append(a, new SendOrigin());
        Assert.AreEqual("none", (string)a["origin_kind"]);
        foreach (string k in new[] { "origin_index", "origin_key", "origin_edit", "origin_top1", "origin_set" })
            Assert.AreEqual(JTokenType.Null, a[k].Type, k);
    }
}
