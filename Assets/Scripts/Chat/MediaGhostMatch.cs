public static class MediaGhostMatch
{
    // Staged AttachmentKind → the MessageType the server echoes back for that send.
    public static MessageType ToMessageType(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Photo or AttachmentKind.GalleryImage => MessageType.Image,
        AttachmentKind.GalleryVideo                         => MessageType.Video,
        AttachmentKind.Document                             => MessageType.Document,
        _                                                   => MessageType.Unknown,
    };

    // True iff this unresolved entry is a Media send whose attachment kind corresponds
    // to the given server message type. Does NOT check timestamp — the caller owns the
    // ±window + best-delta selection (see ChatManager.BestGhostMatch).
    public static bool IsKindMatch(OutboxStore.OutboxEntry entry, MessageType serverType) =>
        entry != null
        && entry.kind == (int)OutboxKind.Media
        && ToMessageType((AttachmentKind)entry.attachmentKind) == serverType;
}
