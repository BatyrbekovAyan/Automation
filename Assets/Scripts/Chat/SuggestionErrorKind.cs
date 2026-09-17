/// <summary>
/// S8 (audit stage 5): the panel tells access, subscription, daily-cap, timeout and generation
/// failures apart from a deliberate abstain — and never shows n8n internals. Wire values are
/// the server's `error` strings (the caller is authenticated since A1, so the reason is safe to
/// return); transport failures are classified client-side.
/// </summary>
public static class SuggestionErrorKind
{
    public const string Network = "network";
    public const string Timeout = "timeout";
    public const string Unauthorized = "unauthorized";
    public const string Subscription = "subscription";
    public const string DailyCap = "daily_cap";
    public const string UnknownAccount = "unknown_account";
    public const string BadRequest = "bad_request";
    public const string Generation = "generation_failed";

    /// <summary>A server `error` value → a known kind (unknown strings collapse to generation — never a raw server string in the UI).</summary>
    public static string FromServer(string error)
    {
        switch (error)
        {
            case Unauthorized: case Subscription: case DailyCap: case UnknownAccount: case BadRequest: case Timeout: case Generation:
                return error;
            default:
                return Generation;
        }
    }

    /// <summary>A failed UnityWebRequest → a kind: 401/403 → unauthorized; a client-side timeout → timeout; else network.</summary>
    public static string FromTransport(long responseCode, string error)
    {
        if (responseCode == 401 || responseCode == 403) return Unauthorized;
        if (!string.IsNullOrEmpty(error) && error.IndexOf("timeout", System.StringComparison.OrdinalIgnoreCase) >= 0) return Timeout;
        if (responseCode == 504 || responseCode == 408) return Timeout;
        return Network;
    }
}

/// <summary>RU copy of the panel's error state per kind (pinned by FontGlyphCoverageTests).</summary>
public static class SuggestionErrorCopy
{
    public const string Default = "Не удалось загрузить";
    public const string NetworkText = "Нет связи с сервером";
    public const string TimeoutText = "Сервер не ответил вовремя";
    public const string UnauthorizedText = "Нет доступа, переоткройте приложение";
    public const string SubscriptionText = "Подписка неактивна";
    public const string DailyCapText = "Дневной лимит подсказок исчерпан";
    public const string UnknownAccountText = "Аккаунт не найден";

    public static string Text(string kind)
    {
        switch (kind)
        {
            case SuggestionErrorKind.Network: return NetworkText;
            case SuggestionErrorKind.Timeout: return TimeoutText;
            case SuggestionErrorKind.Unauthorized: return UnauthorizedText;
            case SuggestionErrorKind.Subscription: return SubscriptionText;
            case SuggestionErrorKind.DailyCap: return DailyCapText;
            case SuggestionErrorKind.UnknownAccount: return UnknownAccountText;
            default: return Default;
        }
    }
}
