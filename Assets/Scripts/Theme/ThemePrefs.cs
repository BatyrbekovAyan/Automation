using System;
using UnityEngine;

/// <summary>Which theme the owner chose in Profile → (appearance row).</summary>
public enum ThemeMode
{
    Light = 0,
    Dark  = 1,
}

/// <summary>
/// PlayerPrefs-backed theme choice. Defaults to <b>Dark</b> (owner decision,
/// 2026-08-07) — the restyle's foundation shipped Light-default so binding
/// elements one screen at a time stayed a provable visual no-op; now that the
/// whole shell is bound, dark is the intended first impression.
///
/// An owner who EXPLICITLY picked light keeps it: the setter always writes, so
/// a stored 0 wins over this default. Only installs that never touched the
/// switch move to dark on update.
///
/// Static Func/Action seams follow the NotifPrefs/SemiAutoStore pattern so
/// EditMode tests can swap in an in-memory store without touching real prefs.
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
        get => GetInt(ModeKey, (int)ThemeMode.Dark) == (int)ThemeMode.Dark
            ? ThemeMode.Dark
            : ThemeMode.Light;
        set => SetIntAndSave(ModeKey, (int)value);
    }
}
