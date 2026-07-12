using System;

/// <summary>
/// Pure, side-effect-free classification of Wappi tapi auth response `detail` strings.
/// Isolates the auth state-machine branch logic (2fa vs auth_success vs error) so it is
/// unit-testable in EditMode, following the WhatsAppSyncGate / CrossChatResponseGuard
/// pure-seam precedent. No logging, no PII, no I/O — never throws on malformed input.
/// </summary>
public static class TelegramAuthResponseParser
{
    // RED stub — intentionally unimplemented so the first test run fails.
    public static string ExtractDetail(string responseBody) => null;

    public static bool IsTwoFactor(string detail) => false;

    public static bool IsAuthSuccess(string detail) => false;
}
