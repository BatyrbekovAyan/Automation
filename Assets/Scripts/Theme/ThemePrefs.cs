using System;
using UnityEngine;

/// <summary>Which theme the owner chose in Profile → (appearance row).</summary>
public enum ThemeMode
{
    Light = 0,
    Dark  = 1,
}

/// <summary>
/// PlayerPrefs-backed theme choice. Defaults to Light — today's look — so the
/// foundation ships with zero visible change. Static Func/Action seams follow
/// the NotifPrefs/SemiAutoStore pattern so EditMode tests can swap in an
/// in-memory store without touching real prefs.
/// </summary>
public static class ThemePrefs
{
    public const string ModeKey = "ThemeMode";

    public static Func<string, int, int> GetInt = PlayerPrefs.GetInt;

    public static Action<string, int> SetIntAndSave = (key, value) =>
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
    };

    public static ThemeMode Mode
    {
        get => GetInt(ModeKey, (int)ThemeMode.Light) == (int)ThemeMode.Dark
            ? ThemeMode.Dark
            : ThemeMode.Light;
        set => SetIntAndSave(ModeKey, (int)value);
    }
}
