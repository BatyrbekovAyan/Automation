/// <summary>
/// Single home for building Wappi sync-API URLs, channel-aware.
/// WhatsApp lives under <c>wappi.pro/api/sync/…</c>; Telegram under
/// <c>wappi.pro/tapi/sync/…</c> — the ONLY difference is the base segment.
/// Replaces the hardcoded <c>wappi.pro/api/sync</c> literals scattered across
/// ChatManager and its partials (call sites are rewired in later Phase-5 plans).
/// Pure static — no I/O — so it is unit-testable.
/// </summary>
public static class WappiEndpoints
{
    /// <summary>
    /// Builds a full Wappi sync URL for the given channel.
    /// </summary>
    /// <param name="channel">WhatsApp → <c>api</c>, Telegram → <c>tapi</c>.</param>
    /// <param name="pathAndQuery">The path + query after <c>/sync/</c>,
    /// e.g. <c>"chats/filter?profile_id=abc"</c>.</param>
    public static string Sync(ChatChannel channel, string pathAndQuery) =>
        $"https://wappi.pro/{BasePart(channel)}/sync/{pathAndQuery}";

    private static string BasePart(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? "tapi" : "api";
}
