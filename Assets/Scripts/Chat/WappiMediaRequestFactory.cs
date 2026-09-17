using System;
using Newtonsoft.Json;

/// <summary>
/// Pure mapping from an AttachmentKind to its Wappi media-send endpoint and
/// JSON body. No UnityWebRequest, no I/O — kept separate so it is unit-testable.
/// Contract confirmed from the Wappi dashboard: POST, profile_id as query param,
/// b64_file is raw base64 (no data: prefix). file_name is only sent for documents.
/// </summary>
public static class WappiMediaRequestFactory
{
    /// <summary>
    /// WhatsApp back-compat overload — resolves the media-send URL on the
    /// WhatsApp base so existing WhatsApp call sites and tests stay byte-identical.
    /// </summary>
    public static string EndpointFor(AttachmentKind kind, string profileId) =>
        EndpointFor(kind, profileId, ChatChannel.WhatsApp);

    /// <summary>
    /// Channel-aware media-send endpoint. Routes through <see cref="WappiEndpoints.Sync"/>
    /// so Telegram lands on the tapi base while WhatsApp keeps its api base.
    /// </summary>
    public static string EndpointFor(AttachmentKind kind, string profileId, ChatChannel channel)
    {
        string path = kind switch
        {
            AttachmentKind.Photo or AttachmentKind.GalleryImage => "message/img/send",
            AttachmentKind.GalleryVideo                         => "message/video/send",
            AttachmentKind.Document                             => "message/document/send",
            _                                                   => null
        };
        return path == null ? null : WappiEndpoints.Sync(channel, $"{path}?profile_id={profileId}");
    }

    public static string NormalizeRecipient(string chatId) => ChatIdFormat.Recipient(chatId);

    /// <summary>The Wappi Proxy <c>media.send</c> kind for an attachment (img | video | document), null when unsendable.</summary>
    public static string ProxyKind(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Photo or AttachmentKind.GalleryImage => "img",
        AttachmentKind.GalleryVideo                         => "video",
        AttachmentKind.Document                             => "document",
        _                                                   => null
    };

    /// <summary>
    /// The proxy request arguments for a media send (A1): the same recipient/caption/file_name/b64
    /// contract as <see cref="BuildBody"/>, plus the profile and the kind the server maps to the endpoint.
    /// </summary>
    public static WappiProxyMediaArgs BuildProxyArgs(AttachmentKind kind, string profileId, string chatId,
                                                     string caption, string fileName, string b64)
        => new WappiProxyMediaArgs
        {
            profile_id = profileId,
            kind       = ProxyKind(kind),
            recipient  = NormalizeRecipient(chatId),
            caption    = caption ?? "",
            b64_file   = b64,
            file_name  = kind == AttachmentKind.Document
                         ? (string.IsNullOrEmpty(fileName) ? "file" : fileName)
                         : null
        };

    public static string BuildBody(AttachmentKind kind, string chatId, string caption,
                                   string fileName, string b64)
    {
        var req = new WappiSendMediaRequest
        {
            recipient = NormalizeRecipient(chatId),
            caption   = caption ?? "",
            b64_file  = b64,
            file_name = kind == AttachmentKind.Document
                        ? (string.IsNullOrEmpty(fileName) ? "file" : fileName)
                        : null
        };
        return JsonConvert.SerializeObject(req,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }
}

[Serializable]
public class WappiProxyMediaArgs
{
    public string profile_id;
    public string kind;
    public string recipient;
    public string caption;
    public string file_name;
    public string b64_file;
}

[Serializable]
public class WappiSendMediaRequest
{
    public string recipient;
    public string caption;
    public string file_name;
    public string b64_file;
}
