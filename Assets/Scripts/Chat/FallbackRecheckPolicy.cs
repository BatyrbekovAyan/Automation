/// <summary>
/// B2 fix round 3 (M1): the open chat's effective mode is re-resolved while the chat stays open — the bot's window closes
/// and opens, the quota runs out or a top-up lands, without the owner reopening the chat. SuggestionsController re-reads
/// its inputs on every live batch and once a minute (<see cref="IsDue"/>) and applies a flip through the show / hide
/// path a server reconcile uses: the closing edge brings the panel up for a night message in an open «Авто» chat, the
/// opening edge takes a night panel away. A panel the owner hid while a fallback held stays hidden for the rest of that
/// fallback (<see cref="KeepDismissal"/>, D1) — never re-raised by the minute tick.
/// </summary>
public static class FallbackRecheckPolicy
{
    /// <summary>How often an open chat re-reads its inputs when no live batch arrives.</summary>
    public const float IntervalSeconds = 60f;

    /// <summary>The panel is up: the chat's stored «Вместе», or a fallback the owner has not hidden for this chat-open.</summary>
    public static bool Effective(bool storedOn, bool fallbackOn, bool dismissed) => storedOn || (fallbackOn && !dismissed);

    /// <summary>A dismissal lasts as long as the fallback it hid — once that ends, the next fallback shows the panel again.</summary>
    public static bool KeepDismissal(bool dismissed, bool fallbackOn) => dismissed && fallbackOn;

    /// <summary>A minute since the last read — or never read, or the clock went back (never blocks).</summary>
    public static bool IsDue(float now, float lastCheckedAt)
        => lastCheckedAt < 0f || now < lastCheckedAt || now - lastCheckedAt >= IntervalSeconds;
}
