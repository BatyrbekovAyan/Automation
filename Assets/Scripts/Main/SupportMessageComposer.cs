using System.Text;

/// <summary>
/// Builds the text sent to the support Telegram chat from the Поддержка sheet.
/// Appends the optional reply contact and an app/device metadata line so the
/// owner can triage without a follow-up question. Pure for EditMode tests.
/// </summary>
public static class SupportMessageComposer
{
    public static string Compose(string message, string contact, string version, string platform, string deviceModel)
    {
        string body = (message ?? "").Trim();
        if (body.Length == 0) return "";

        var sb = new StringBuilder(body);

        string reply = (contact ?? "").Trim();
        if (reply.Length > 0)
            sb.Append("\nКонтакт: ").Append(reply);

        sb.Append("\n— Automation v").Append(version)
          .Append(" · ").Append(platform)
          .Append(" · ").Append(deviceModel);

        return sb.ToString();
    }
}
