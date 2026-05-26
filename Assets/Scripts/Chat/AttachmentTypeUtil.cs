using System;
using System.Collections.Generic;
using System.IO;

public static class AttachmentTypeUtil
{
    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic"
    };

    private static readonly Dictionary<string, string> MimeByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg",  "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png",  "image/png" },
        { ".gif",  "image/gif" },
        { ".webp", "image/webp" },
        { ".heic", "image/heic" },
        { ".mp4",  "video/mp4" },
        { ".mov",  "video/quicktime" },
        { ".pdf",  "application/pdf" },
        { ".doc",  "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".xls",  "application/vnd.ms-excel" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".txt",  "text/plain" },
        { ".zip",  "application/zip" }
    };

    public static bool IsImageExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    public static string MimeFromExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return null;
        return MimeByExtension.TryGetValue(ext, out var mime) ? mime : null;
    }

    public static AttachmentKind GalleryKindFromPath(string path)
    {
        return IsImageExtension(path) ? AttachmentKind.GalleryImage : AttachmentKind.GalleryVideo;
    }
}
