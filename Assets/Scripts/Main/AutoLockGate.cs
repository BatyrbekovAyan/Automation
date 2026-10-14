using System;
using TMPro;

/// <summary>
/// Learning B: the ONE door to enabling «Авто» by the owner's hand. Open verdict → the ordinary path runs at once;
/// Locked (the server listed the bot and says not ready) → the «Бот ещё учится» sheet, whose «Включить всё равно» runs
/// the same path. Pinned by AutoLockWiringTests: the capsule, RequestEnableAuto (chats header + switcher row) and the
/// per-chat toggle all call this before they commit; DisableAuto and ReconcileFromServer never do.
/// </summary>
public static class AutoLockGate
{
    public static void TryEnable(string botKey, TMP_FontAsset font, bool perChat, Action proceed)
    {
        AutoReadinessEntry entry = AutoReadinessStore.Current(botKey);
        if (AutoReadinessPolicy.Verdict(entry) == AutoReadinessVerdict.Open)
        {
            proceed?.Invoke();
            return;
        }
        AutoLockSheet.Show(font, AutoReadinessPolicy.Progress(entry), perChat, proceed);
    }

    /// <summary>
    /// The per-chat door. A chat of a bot already in «Авто» is a RESUME inside an enabled mode (a handoff pause, a manual
    /// «Вместе» for one client) — the lock never applies to an enabled mode (spec §0), so the ordinary path runs at once.
    /// A chat of a bot in «Вместе» is an enable for one client and meets the lock with the «в этом чате» body.
    /// </summary>
    public static void TryEnableChat(string botKey, ReplyModeToggleBinder.ReplyMode botMode, TMP_FontAsset font, Action proceed)
    {
        if (AutoButtonModel.IsAutoOn(botMode))
        {
            proceed?.Invoke();
            return;
        }
        TryEnable(botKey, font, perChat: true, proceed);
    }
}
