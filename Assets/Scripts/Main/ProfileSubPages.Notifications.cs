using System;
using Automation.BotSettingsUI;
using UnityEngine;

// Profile → Уведомления: three PlayerPrefs-backed switches wired to real
// in-app behavior (sound/vibration via NotificationFx, badges via the
// ChatItemView gate). No push row — there is no push infrastructure (Q2).
public partial class ProfileSubPages
{
    [Header("Notifications page")]
    [SerializeField] private ToggleRow soundToggle;
    [SerializeField] private ToggleRow vibrationToggle;
    [SerializeField] private ToggleRow unreadBadgeToggle;

    private void WireNotifications()
    {
        WireToggle(soundToggle, value => NotifPrefs.SoundEnabled = value);
        WireToggle(vibrationToggle, value => NotifPrefs.VibrationEnabled = value);
        WireToggle(unreadBadgeToggle, value =>
        {
            NotifPrefs.UnreadBadgeEnabled = value;
            RefreshVisibleChatRows();
        });
    }

    private static void WireToggle(ToggleRow row, Action<bool> apply)
    {
        if (row != null) row.Toggle.onValueChanged.AddListener(value => apply(value));
    }

    private void RefreshNotificationToggles()
    {
        if (soundToggle != null) soundToggle.SetIsOnQuiet(NotifPrefs.SoundEnabled);
        if (vibrationToggle != null) vibrationToggle.SetIsOnQuiet(NotifPrefs.VibrationEnabled);
        if (unreadBadgeToggle != null) unreadBadgeToggle.SetIsOnQuiet(NotifPrefs.UnreadBadgeEnabled);
    }

    // Badge visibility is read at bind time — poke every row so the list
    // reflects the switch immediately, not on the next sync.
    private static void RefreshVisibleChatRows()
    {
        if (ChatManager.Instance == null) return;
        foreach (var vm in ChatManager.Instance.Chats)
            vm?.NotifyUpdated();
    }
}
