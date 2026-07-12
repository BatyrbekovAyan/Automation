using System;
using NUnit.Framework;

/// <summary>
/// Covers the pure chat-list time-selection seam (ChatDialogTime.Resolve) that
/// ParseChatsJson uses. WhatsApp + Telegram both send last_timestamp; Telegram
/// additionally sends last_time. When last_timestamp is empty/unparseable but
/// last_time is a valid RFC3339 string, fall back to it. Neither parses => 0.
/// Both are parsed via DateTimeOffset.TryParse (a wrong-typed/absent field can
/// never throw — T-0503-02).
/// </summary>
public class ChatDialogTimeFallbackTests
{
    private static long Epoch(string rfc3339) =>
        DateTimeOffset.Parse(rfc3339).ToUnixTimeSeconds();

    [Test]
    public void Resolve_ValidLastTimestamp_Wins()
    {
        const string primary = "2024-01-15T10:30:00Z";
        const string fallback = "2020-05-05T00:00:00Z";
        Assert.AreEqual(Epoch(primary), ChatDialogTime.Resolve(primary, fallback));
    }

    [Test]
    public void Resolve_EmptyLastTimestamp_FallsBackToLastTime()
    {
        const string fallback = "2024-06-01T08:15:30Z";
        Assert.AreEqual(Epoch(fallback), ChatDialogTime.Resolve("", fallback));
    }

    [Test]
    public void Resolve_UnparseableLastTimestamp_FallsBackToLastTime()
    {
        const string fallback = "2023-12-31T23:59:59Z";
        Assert.AreEqual(Epoch(fallback), ChatDialogTime.Resolve("not-a-date", fallback));
    }

    [Test]
    public void Resolve_BothEmpty_ReturnsZero()
    {
        Assert.AreEqual(0L, ChatDialogTime.Resolve("", ""));
    }

    [Test]
    public void Resolve_BothNull_ReturnsZero()
    {
        Assert.AreEqual(0L, ChatDialogTime.Resolve(null, null));
    }

    [Test]
    public void Resolve_BothUnparseable_ReturnsZero()
    {
        Assert.AreEqual(0L, ChatDialogTime.Resolve("garbage", "also-garbage"));
    }
}
