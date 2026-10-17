/// <summary>What a tap on the open chat's «Авто» chip does (B2 fix round 3, D1).</summary>
public enum SuggestionToggleTap
{
    /// <summary>The owner's decision about the chat: persist its new mode and push it to the server (today's path).</summary>
    Write,
    /// <summary>The panel is up only because of a fallback — hide it for this chat-open, write nothing.</summary>
    HideOnly,
    /// <summary>Undo that dismissal while the fallback still holds — the panel comes back, still nothing written.</summary>
    ShowOnly,
}

/// <summary>
/// B2 fix round 3 (D1, owner ruling 2026-10-17). The chip over an open chat is lit («Вместе») either by the chat's STORED
/// mode — its own override or the bot default — or ONLY by a fallback: the account quota is spent (QuotaFallbackPolicy)
/// or the bot's saved schedule is closed right now (ScheduleFallbackPolicy). A fallback is an effective mode: the
/// owner's chosen mode must come back untouched the moment it ends. So a tap on a chip lit by a fallback alone is not a
/// decision about the chat — it hides the panel for this chat-open and records nothing (no SemiAutoStore write, no
/// server desire); before this rule it wrote a sticky per-chat «Авто» that outlived the night and the quota reset.
/// Tapping a hidden fallback panel back, while the fallback still holds, records nothing either: an explicit «Вместе»
/// written there would keep the bot silent with this client once the window opens. A chip lit by the stored «Вместе»,
/// and every tap outside a fallback, keep today's behaviour.
/// </summary>
public static class SuggestionToggleTapPolicy
{
    /// <param name="desiredOn">The tap's direction: true = show the panel («Вместе»), false = hide it («Авто»).</param>
    /// <param name="storedOn">SemiAutoStore's effective value for the open chat (its override, else the bot default).</param>
    /// <param name="fallbackOn">The quota is spent, or the open bot's schedule is closed right now.</param>
    public static SuggestionToggleTap Decide(bool desiredOn, bool storedOn, bool fallbackOn)
    {
        if (storedOn || !fallbackOn) return SuggestionToggleTap.Write;
        return desiredOn ? SuggestionToggleTap.ShowOnly : SuggestionToggleTap.HideOnly;
    }
}
