/// <summary>
/// Pure decision seam for the SYSTEM keyboard's appearance (the native IME
/// chrome, which the app cannot paint itself — only ask the OS to darken).
///
/// Two inputs, one rule: the app theme sets the baseline, and a screen that is
/// ALWAYS dark regardless of theme (the attachment preview, whose caption field
/// floats over full-bleed media) may force dark on top. Nothing ever forces
/// LIGHT — a light keyboard over the dark preview was the bug that introduced
/// the force in the first place — so the combination is a plain OR.
///
/// Flat pure-seam style (ChannelSwitcherModel / AutoButtonModel precedent) so
/// the precedence is EditMode-testable without a device.
/// </summary>
public static class KeyboardAppearancePolicy
{
    /// <summary>
    /// True when the system keyboard should render dark.
    /// <paramref name="forcedDark"/> is an always-dark screen's override.
    /// </summary>
    public static bool ShouldBeDark(ThemeMode mode, bool forcedDark) =>
        forcedDark || mode == ThemeMode.Dark;
}
