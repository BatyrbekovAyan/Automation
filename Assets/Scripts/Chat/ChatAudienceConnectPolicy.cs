/// <summary>
/// «Личные и рабочие чаты»: what a confirmed pairing means for «Кто из них клиенты?». The screen is owed once per channel of
/// a bot — when the channel is connected for the first time, and again when it now carries another number (another number
/// is a new connection: the server restarts the moment, the computed decisions of the old number stop holding). A
/// re-pairing of the same number is neither. Pure; the wizard (a new bot: always first) and the settings decide with it.
/// </summary>
public static class ChatAudienceConnectPolicy
{
    // Characters a phone number may be written with; anything else (a stale status blob an old build stored) is no number.
    private const string NumberCharacters = "+0123456789 -() ";

    /// <summary>
    /// The channel had no profile before this pairing and none the server retired: a first connection. A previous or a
    /// retired profile means the channel was connected on this bot before — a reconnection.
    /// </summary>
    public static bool IsFirstConnection(string previousProfileId, string retiredProfileId) =>
        !ChatAudienceRequest.IsRealProfileId(previousProfileId) && !ChatAudienceRequest.IsRealProfileId(retiredProfileId);

    /// <summary>The digits of both numbers are known and differ — another account was paired.</summary>
    public static bool NumberChanged(string storedNumber, string authedNumber)
    {
        string stored = NumberDigits(storedNumber), authed = NumberDigits(authedNumber);
        return stored.Length > 0 && authed.Length > 0 && stored != authed;
    }

    /// <summary>
    /// A confirmed pairing in the settings owes «Кто из них клиенты?»: the channel was never seen on this bot (its first
    /// connection — whatever ids or number the phone kept from before), or it now carries another number.
    /// </summary>
    public static bool OwesOnboarding(bool channelSeen, string storedNumber, string authedNumber) =>
        !channelSeen || NumberChanged(storedNumber, authedNumber);

    /// <summary>
    /// The server's answer to a connection report says the number changed: its own word (<paramref name="restarted"/> — it
    /// moved the moment), or a moment later than the one mirrored for the same profile — a report retried after its first
    /// answer was lost moves nothing any more and says false. Nothing mirrored proves nothing: a first answer is no restart.
    /// </summary>
    public static bool NumberRestarted(bool restarted, long? mirroredConnectedAtMs, long answeredConnectedAtMs) =>
        restarted || (mirroredConnectedAtMs > 0 && answeredConnectedAtMs > mirroredConnectedAtMs);

    /// <summary>The number's digits, or "" when the text is not a number at all (the settings, the folder's search).</summary>
    public static string NumberDigits(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return "";
        foreach (char c in number)
            if (NumberCharacters.IndexOf(c) < 0) return "";
        return WappiProxyRequest.DigitsOnly(number);
    }
}
