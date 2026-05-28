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
    private const string Base = "https://wappi.pro/api/sync/message/";

    public static string EndpointFor(AttachmentKind kind, string profileId) => kind switch
    {
        AttachmentKind.Photo or AttachmentKind.GalleryImage => $"{Base}img/send?profile_id={profileId}",
        AttachmentKind.GalleryVideo                         => $"{Base}video/send?profile_id={profileId}",
        AttachmentKind.Document                             => $"{Base}document/send?profile_id={profileId}",
        _                                                   => null
    };

    public static string NormalizeRecipient(string chatId) =>
        chatId != null && chatId.EndsWith("@c.us") ? chatId.Replace("@c.us", "") : chatId;

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
public class WappiSendMediaRequest
{
    public string recipient;
    public string caption;
    public string file_name;
    public string b64_file;
}
