using System;

/// <summary>
/// Pure, side-effect-free classification of Wappi tapi auth response `detail` strings.
/// Isolates the auth state-machine branch logic (2fa vs auth_success vs error) so it is
/// unit-testable in EditMode, following the WhatsAppSyncGate / CrossChatResponseGuard
/// pure-seam precedent. No logging, no PII, no I/O — never throws on malformed input.
/// </summary>
public static class TelegramAuthResponseParser
{
    private const string DetailKey = "\"detail\"";

    /// <summary>
    /// Reads the string value of the top-level "detail" field from a Wappi auth response
    /// body. Tolerant substring parsing (mirrors the codebase's existing fragile parse) —
    /// returns "" for malformed input, a missing key, or a non-string value; never throws.
    /// </summary>
    public static string ExtractDetail(string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody)) return "";

        int keyIndex = responseBody.IndexOf(DetailKey, StringComparison.Ordinal);
        if (keyIndex < 0) return "";

        int i = keyIndex + DetailKey.Length;

        // Skip whitespace and the ':' separator between the key and its value.
        while (i < responseBody.Length &&
               (responseBody[i] == ' ' || responseBody[i] == '\t' || responseBody[i] == ':'))
        {
            i++;
        }

        // A valid detail value is a quoted string; anything else -> "" (non-string / malformed).
        if (i >= responseBody.Length || responseBody[i] != '"') return "";
        i++; // move past the opening quote

        int closing = responseBody.IndexOf('"', i);
        if (closing < 0) return "";

        return responseBody.Substring(i, closing - i);
    }

    /// <summary>True only when the account requires a Telegram cloud password (detail == "2fa").</summary>
    public static bool IsTwoFactor(string detail) => detail == "2fa";

    /// <summary>
    /// True when the auth step succeeded. Fail-closed: only a detail that STARTS with
    /// "auth_success" authorizes; every other/empty/malformed detail returns false so the
    /// caller re-prompts.
    /// </summary>
    public static bool IsAuthSuccess(string detail) =>
        !string.IsNullOrEmpty(detail) && detail.StartsWith("auth_success", StringComparison.Ordinal);
}
