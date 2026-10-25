using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты»: the «Кто из них клиенты?» screen is owed for a channel of a bot — set when the channel of a new
/// bot is paired for the first time (the wizard, or the channel first added in the settings) and again after another number
/// is paired; cleared when the owner is done with the screen, and when the bot is deleted (a bot created anew owes it
/// again). Per-bot PlayerPrefs flags (bot-persistence): <c>{bot}AudienceOnboardingWhatsapp</c> /
/// <c>{bot}AudienceOnboardingTelegram</c>. Only the flags live here; showing the screen is the folder's.
/// </summary>
public static class PersonalChatsOnboarding
{
    public const string WhatsappSuffix = "AudienceOnboardingWhatsapp";
    public const string TelegramSuffix = "AudienceOnboardingTelegram";

    public static string FlagKey(string botName, bool telegram) => $"{botName}{(telegram ? TelegramSuffix : WhatsappSuffix)}";

    /// <summary>The screen is owed for this channel of the bot (saved at once: a pairing is a moment worth keeping).</summary>
    public static void Request(string botName, bool telegram)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.SetInt(FlagKey(botName, telegram), 1);
        PlayerPrefs.Save();
    }

    public static bool IsRequested(string botName, bool telegram) =>
        !string.IsNullOrEmpty(botName) && PlayerPrefs.GetInt(FlagKey(botName, telegram), 0) == 1;

    /// <summary>Nothing is owed any more for this channel of the bot.</summary>
    public static void Clear(string botName, bool telegram)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.DeleteKey(FlagKey(botName, telegram));
        PlayerPrefs.Save();
    }
}
