/// <summary>
/// A parsed Wappi reaction event. targetId == the reacted-to message id (stanzaId).
/// An empty emoji means the reactor removed their reaction.
/// </summary>
public class ReactionEvent
{
    public string targetId;
    public string emoji;
    public string reactorKey;
    public string senderName;
    public bool fromMe;
    public long time;

    public bool IsRemoval => string.IsNullOrEmpty(emoji);
}

/// <summary>
/// Parses RawMessage reaction events. Pure/static so it is unit-testable and
/// callable from the ChatManager message loops without a MonoBehaviour.
/// </summary>
public static class ReactionParser
{
    /// <summary>
    /// Returns a ReactionEvent for a usable reaction, or null when the raw is not a
    /// reaction or lacks a target. Stores the emoji RAW (unconverted) — display layers
    /// convert to a TMP sprite so a not-yet-downloaded emoji survives for re-conversion.
    /// </summary>
    public static ReactionEvent FromRaw(RawMessage raw)
    {
        if (raw == null) return null;
        if (raw.type != "reaction") return null;
        if (string.IsNullOrEmpty(raw.stanzaId)) return null;

        return new ReactionEvent
        {
            targetId = raw.stanzaId,
            emoji = raw.body?.ToString() ?? "",
            reactorKey = ReactorKey(raw.fromMe, raw.from, raw.senderName),
            senderName = raw.senderName,
            fromMe = raw.fromMe,
            time = raw.time
        };
    }

    /// <summary>
    /// Stable identity for "who reacted". Account owner is always "me"; otherwise the
    /// reactor jid (group-safe), falling back to senderName then a constant.
    /// </summary>
    public static string ReactorKey(bool fromMe, string from, string senderName)
    {
        if (fromMe) return "me";
        if (!string.IsNullOrEmpty(from)) return from;
        if (!string.IsNullOrEmpty(senderName)) return senderName;
        return "unknown";
    }
}
