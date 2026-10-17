/// <summary>What a tap on the open chat's «Авто» chip does (B2 fix rounds 3–4, D1).</summary>
public enum SuggestionToggleTap
{
    /// <summary>The owner's decision about the chat: persist its new mode and push it to the server (today's path).</summary>
    Write,
    /// <summary>The panel is up only because of a fallback — hide it for this chat-open, write nothing.</summary>
    HideOnly,
    /// <summary>Undo exactly that no-write dismissal while the fallback still holds — the panel comes back, nothing written.</summary>
    ShowOnly,
}

/// <summary>
/// B2 fix round 3 (D1, owner ruling 2026-10-17). The chip over an open chat is lit («Вместе») either by the chat's STORED
/// mode — its own override or the bot default — or ONLY by a fallback: the account quota is spent (QuotaFallbackPolicy)
/// or the bot's saved schedule is closed right now (ScheduleFallbackPolicy). A fallback is an effective mode: the
/// owner's chosen mode must come back untouched the moment it ends. So a tap on a chip lit by a fallback alone is not a
/// decision about the chat — it hides the panel for this chat-open and records nothing (no SemiAutoStore write, no
/// server desire); before this rule it wrote a sticky per-chat «Авто» that outlived the night and the quota reset.
///
/// Round 4: the tap back may skip the write ONLY when it undoes that no-write hide (<c>dismissedWithoutWrite</c>, a
/// this-chat-open latch the controller sets on HideOnly and clears on chat open and on every Write). Every other «on»
/// tap is the owner's «Вместе» and is written: after an off-tap that WROTE «Авто» (a chip lit by the stored «Вместе»,
/// past the confirm), or in the seconds before the minute tick notices the window closing — a silent show there left a
/// per-chat «Авто» on the phone and the server, and the bot answered a client the owner had switched back to «Вместе»
/// once the fallback ended.
/// </summary>
public static class SuggestionToggleTapPolicy
{
    /// <param name="wantOn">The tap's direction: true = show the panel («Вместе»), false = hide it («Авто»).</param>
    /// <param name="storedOn">SemiAutoStore's effective value for the open chat (its override, else the bot default).</param>
    /// <param name="fallbackOn">The quota is spent, or the open bot's schedule is closed right now.</param>
    /// <param name="dismissedWithoutWrite">The panel is down because of a HideOnly in this chat-open — nothing was written.</param>
    public static SuggestionToggleTap Decide(bool wantOn, bool storedOn, bool fallbackOn, bool dismissedWithoutWrite)
    {
        if (!wantOn) return !storedOn && fallbackOn ? SuggestionToggleTap.HideOnly : SuggestionToggleTap.Write;
        return fallbackOn && dismissedWithoutWrite ? SuggestionToggleTap.ShowOnly : SuggestionToggleTap.Write;
    }
}
