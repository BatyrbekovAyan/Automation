using System;
using UnityEngine;

/// <summary>
/// PlayerPrefs-backed switches for the Profile → Уведомления page. All three
/// default to ON. Static Func/Action seams follow the SemiAutoStore pattern so
/// EditMode tests can swap in an in-memory store without touching real prefs.
/// Consumers: NotificationFx (sound/vibration on incoming), ChatItemView
/// (unread badge gate).
/// </summary>
public static class NotifPrefs
{
    public const string SoundKey = "NotifSoundEnabled";
    public const string VibrationKey = "NotifVibrationEnabled";
    public const string UnreadBadgeKey = "NotifUnreadBadgeEnabled";

    public static Func<string, int, int> GetInt = PlayerPrefs.GetInt;

    public static Action<string, int> SetIntAndSave = (key, value) =>
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
    };

    public static bool SoundEnabled
    {
        get => GetInt(SoundKey, 1) == 1;
        set => SetIntAndSave(SoundKey, value ? 1 : 0);
    }

    public static bool VibrationEnabled
    {
        get => GetInt(VibrationKey, 1) == 1;
        set => SetIntAndSave(VibrationKey, value ? 1 : 0);
    }

    public static bool UnreadBadgeEnabled
    {
        get => GetInt(UnreadBadgeKey, 1) == 1;
        set => SetIntAndSave(UnreadBadgeKey, value ? 1 : 0);
    }
}
