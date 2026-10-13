using Newtonsoft.Json.Linq;

/// <summary>
/// Puts the six origin_* fields into the proxy args of message.send / message.reply. They are the OPS
/// `meta` section server-side: validated like the body, never forwarded to Wappi. Absent values are
/// JSON null, which the proxy's `take` treats as «not sent» (every field is optional).
/// </summary>
public static class SendOriginWire
{
    public static void Append(JObject args, SendOrigin o)
    {
        if (args == null || o == null) return;
        bool set = o.kind != SendOriginKind.None;
        bool pick = o.kind == SendOriginKind.Card || o.kind == SendOriginKind.Edited;
        args["origin_kind"] = o.KindWire;
        args["origin_index"] = pick ? (JToken)o.index : JValue.CreateNull();
        args["origin_key"] = set && !string.IsNullOrEmpty(o.requestKey) ? (JToken)o.requestKey : JValue.CreateNull();
        args["origin_edit"] = pick ? (JToken)o.editPercent : JValue.CreateNull();
        args["origin_top1"] = set ? (JToken)(o.top1Match ? 1 : 0) : JValue.CreateNull();
        args["origin_set"] = set ? (JToken)o.setSize : JValue.CreateNull();
    }
}
