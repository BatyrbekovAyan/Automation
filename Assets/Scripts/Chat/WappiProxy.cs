using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

/// <summary>
/// Pure composer of a Wappi Proxy request body (A1, stage 1). The client no longer holds the Wappi
/// token: every transport operation is an allow-listed <c>op</c> sent to <c>/webhook/WappiProxy</c>
/// with the session token; the server validates the parameters, checks that the profile belongs
/// to this install and forwards the call with ITS credential. Wappi's status code and JSON body
/// come back unchanged, so the call sites keep their existing parsing.
/// </summary>
public static class WappiProxyRequest
{
    public const string Path = "/webhook/WappiProxy";

    public static string ComposeBody(string op, ChatChannel channel, object args)
    {
        var jo = args == null ? new JObject() : JObject.FromObject(args);
        jo["op"] = op;
        jo["channel"] = ChannelName(channel);
        return jo.ToString(Formatting.None);
    }

    public static string ChannelName(ChatChannel channel) => channel == ChatChannel.Telegram ? "telegram" : "whatsapp";

    /// <summary>Phone as the proxy validates it (digits only, 7–15).</summary>
    public static string DigitsOnly(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>Transport half: builds the UnityWebRequest and sends it through <see cref="AuthorizedRequest"/>.</summary>
public static class WappiProxy
{
    public static class Op
    {
        public const string ProfileAdd = "profile.add";
        public const string ProfileDelete = "profile.delete";
        public const string ProfileLogout = "profile.logout";
        public const string Status = "status";
        public const string Qr = "qr";
        public const string AuthCode = "auth.code";
        public const string TgAuthPhone = "tg.auth.phone";
        public const string TgAuthCode = "tg.auth.code";
        public const string TgAuth2fa = "tg.auth.2fa";
        public const string ChatsFilter = "chats.filter";
        public const string MessagesGet = "messages.get";
        public const string MessagesIdGet = "messages.id.get";
        public const string MediaDownload = "media.download";
        public const string MessageSend = "message.send";
        public const string MessageReply = "message.reply";
        public const string MessageMarkRead = "message.mark_read";
        public const string ChatDelete = "chat.delete";
        public const string MessageReaction = "message.reaction";
        public const string MediaSend = "media.send";
    }

    public static UnityWebRequest NewRequest(string op, ChatChannel channel, object args, int timeoutSeconds = 30)
    {
        var www = new UnityWebRequest($"{Manager.n8nBaseUrl}{WappiProxyRequest.Path}", "POST");
        www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(WappiProxyRequest.ComposeBody(op, channel, args)));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.timeout = timeoutSeconds;
        AuthorizedRequest.Stamp(www);
        return www;
    }

    /// <summary>
    /// Sends one proxy op; the callback receives the finished request (the caller disposes it).
    /// After a successful <c>profile.add</c> the server's response carries a re-issued token that
    /// already lists the new profile — adopted here so the very next call is authorised.
    /// </summary>
    public static IEnumerator Send(string op, ChatChannel channel, object args, Action<UnityWebRequest> done, int timeoutSeconds = 30)
    {
        yield return AuthorizedRequest.Send(
            () => NewRequest(op, channel, args, timeoutSeconds),
            r =>
            {
                if (op == Op.ProfileAdd && r.result == UnityWebRequest.Result.Success)
                    AdoptTokenFrom(r.downloadHandler.text);
                done?.Invoke(r);
            });
    }

    private static void AdoptTokenFrom(string json)
    {
        try
        {
            string token = JObject.Parse(json).Value<string>("token");
            if (!string.IsNullOrEmpty(token)) AuthClient.Existing?.AdoptToken(token);
        }
        catch { /* the Wappi body is what the caller parses; a missing token just means a later refresh */ }
    }
}
