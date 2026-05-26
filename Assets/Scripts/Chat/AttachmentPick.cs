public enum AttachmentKind
{
    Photo,
    GalleryImage,
    GalleryVideo,
    Document
}

public class AttachmentPick
{
    public AttachmentKind Kind;
    public string Path;
    public string FileName;
    public string MimeType;
    public long   FileSizeBytes;
}
