using Automation.BotSettingsUI;
using UnityEngine;

// Profile → «Тёмная тема»: the light/dark switch, living as a toggle row on the
// profile's main list rather than behind a sub-page — it is one switch, and
// making the owner drill into a page to reach it would be worse.
//
// All the machinery already exists: ThemePrefs persists the choice, Theme.SetMode
// raises Changed, and every ThemedColor binding repaints itself on that event. So
// this is purely the control surface.
public partial class ProfileSubPages
{
    [Header("Appearance")]
    [SerializeField] private ToggleRow darkThemeToggle;

    private void WireAppearance()
    {
        if (darkThemeToggle == null) return;
        darkThemeToggle.Toggle.onValueChanged.AddListener(isOn =>
            Theme.SetMode(isOn ? ThemeMode.Dark : ThemeMode.Light));
    }

    // SetIsOnQuiet so restoring the persisted state does not re-fire the listener
    // and write the value straight back.
    private void RefreshAppearanceToggle()
    {
        if (darkThemeToggle != null)
            darkThemeToggle.SetIsOnQuiet(Theme.Mode == ThemeMode.Dark);
    }
}
