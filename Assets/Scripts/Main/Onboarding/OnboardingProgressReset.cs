using UnityEngine;

/// <summary>
/// Clears the «Первые шаги» progress latches when the LAST bot is deleted.
///
/// The checklist describes the first bot (<see cref="FirstStepsCard"/> reads BotsParent
/// child 0), but its milestone latches are GLOBAL PlayerPrefs keys living outside the
/// per-bot "BotN…" namespace (<see cref="OnboardingKeys"/>). <c>Bot.DeleteBot</c> only
/// clears that namespace, so the latches used to outlive the bot that earned them:
/// deleting the only bot and creating another re-showed the card with the deleted bot's
/// rows already checked.
///
/// Reset rule: only when NO bots remain. While another bot is still on the roster the
/// checklist keeps tracking it, and the row-4 first-reply latch — a global fact that
/// cannot be re-derived from any bot's state — must not regress just because a secondary
/// bot was removed.
///
/// Two keys are deliberately NOT cleared:
/// <see cref="OnboardingKeys.Seen"/> (the welcome carousel is a once-per-install moment,
/// not onboarding progress — <see cref="OnboardingGate.ShouldAutoFlagSeen"/> exists
/// precisely so deleting every bot never resurfaces it) and
/// <see cref="OnboardingKeys.ChecklistDone"/> (spec: the card never resurfaces after 4/4).
///
/// Pure rule + key list are static and public so EditMode tests pin them without a
/// MonoBehaviour (analog: <see cref="OnboardingGate"/> / <see cref="FirstStepsChecklist"/>).
/// </summary>
public static class OnboardingProgressReset
{
    /// <summary>The latches cleared by <see cref="Clear"/>, in checklist row order.</summary>
    public static readonly string[] Keys =
    {
        OnboardingKeys.ChannelConnectedSeen,   // row 2 — «Подключить мессенджер»
        OnboardingKeys.PriceListUploadedSeen,  // row 3 — «Загрузить прайс-лист»
        OnboardingKeys.FirstBotReplySeen,      // row 4 — «Получить первый ответ бота»
    };

    /// <summary>Pure rule: onboarding progress restarts only once the roster is empty.</summary>
    public static bool ShouldReset(int remainingBots) => remainingBots <= 0;

    /// <summary>Deletes every latch in <see cref="Keys"/> and flushes.</summary>
    public static void Clear()
    {
        foreach (string key in Keys) PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// <c>Bot.DeleteBot</c> entry point: clears the latches when the deleted bot was the
    /// last one. <paramref name="remainingBots"/> is the count AFTER the deleted card has
    /// left BotsParent (Bot.DeleteBot detaches before <c>Destroy</c> precisely so this
    /// count is truthful in the same frame).
    /// </summary>
    public static void OnBotDeleted(int remainingBots)
    {
        if (ShouldReset(remainingBots)) Clear();
    }
}
