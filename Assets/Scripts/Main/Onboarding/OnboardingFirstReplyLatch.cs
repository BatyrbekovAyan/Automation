using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source-side latch for the «Первые шаги» row-4 milestone («Получить первый ответ бота»).
/// Installed once by ChatManager.Awake and never unsubscribed — ChatManager lives for the
/// whole app session on every screen.
///
/// Why not listen from FirstStepsCard: the card sits inside Screen_Bots, which is INACTIVE
/// on the launch (Chats) tab, and its OnEnable/OnDisable subscription only existed while
/// the user was looking at the Bots tab — but batch/live message events only fire while a
/// chat is open on the Chats tab. The two windows never overlapped, so the latch was
/// unreachable by construction. Latching at the event SOURCE hears every delivery.
/// </summary>
public static class OnboardingFirstReplyLatch
{
    /// <summary>
    /// Pure decision: latch when not yet latched and any outgoing (isIncoming==false)
    /// message is present — the spec's proxy for "the bot has replied" (it also covers
    /// the owner's own outgoing message; accepted, T-11-06-03).
    /// </summary>
    public static bool ShouldLatch(bool alreadyLatched, List<MessageViewModel> msgs)
        => !alreadyLatched && msgs != null && msgs.Exists(m => m != null && !m.isIncoming);

    /// <summary>Persists the latch on the first qualifying delivery and live-refreshes
    /// the checklist card when it exists (null-safe: the card may never have been enabled).</summary>
    public static void TryLatch(List<MessageViewModel> msgs)
    {
        bool alreadyLatched = PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0) == 1;
        if (!ShouldLatch(alreadyLatched, msgs)) return;

        PlayerPrefs.SetInt(OnboardingKeys.FirstBotReplySeen, 1);
        PlayerPrefs.Save();
        FirstStepsCard.Instance?.RefreshFromFacts();
    }
}
