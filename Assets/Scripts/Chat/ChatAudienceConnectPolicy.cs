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

    // The number's digits, or "" when the text is not a number at all.
    private static string NumberDigits(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return "";
        foreach (char c in number)
            if (NumberCharacters.IndexOf(c) < 0) return "";
        return WappiProxyRequest.DigitsOnly(number);
    }
}
