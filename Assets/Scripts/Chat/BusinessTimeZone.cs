using System;

/// <summary>
/// S7 (audit stage 5): the business's timezone, stamped at bot creation (PlayerPrefs
/// <c>{botName}TimeZone</c>) from the device that created it. On iOS/Android/macOS
/// <see cref="TimeZoneInfo.Local"/>.Id is an IANA id («Asia/Almaty»); on Windows it is a
/// display id the server does not accept and falls back from (with a hedge) — never a crash.
/// </summary>
public static class BusinessTimeZone
{
    public static string DeviceIanaId()
    {
        try { return TimeZoneInfo.Local?.Id ?? ""; }
        catch (Exception) { return ""; }
    }

    /// <summary>The bot's stored zone, or null for a legacy bot (created before the stamp).</summary>
    public static string ForBot(string botName)
    {
        string z = UnityEngine.PlayerPrefs.GetString(botName + "TimeZone", "");
        return string.IsNullOrWhiteSpace(z) ? null : z;
    }
}
