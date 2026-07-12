using System;

/// <summary>
/// Pure, unit-testable chat-list time selection used by ChatManager.ParseChatsJson.
/// Prefers <c>last_timestamp</c> (WhatsApp + Telegram); falls back to Telegram's
/// <c>last_time</c> when the primary is empty/unparseable. Both are RFC3339 strings
/// parsed via <see cref="DateTimeOffset.TryParse(string, out DateTimeOffset)"/>, so a
/// wrong-typed or absent field yields 0 and never throws (T-0503-02).
/// Extracted per the WhatsAppSyncGate/CrossChatResponseGuard pure-seam precedent.
/// </summary>
public static class ChatDialogTime
{
    /// <summary>Unix seconds of the freshest parseable timestamp, or 0 when neither parses.</summary>
    public static long Resolve(string lastTimestamp, string lastTime)
    {
        if (DateTimeOffset.TryParse(lastTimestamp, out var primary)) return primary.ToUnixTimeSeconds();
        if (DateTimeOffset.TryParse(lastTime, out var fallback)) return fallback.ToUnixTimeSeconds();
        return 0;
    }
}
