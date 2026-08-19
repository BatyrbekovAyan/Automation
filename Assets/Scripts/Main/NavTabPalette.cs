using UnityEngine;

/// <summary>
/// Which theme role paints a bottom-nav tab, per state.
///
/// Pure and static so the contract is testable without a scene. This is not a
/// styling detail: the bar's glyphs are white-on-transparent PNGs, so the tint
/// this returns is the only thing that makes them visible at all. An untinted
/// icon is invisible on the light theme's white bar — the exact bug this
/// replaced.
/// </summary>
public static class NavTabPalette
{
    /// <summary>
    /// The active tab uses one brand accent across all four tabs, deliberately
    /// replacing the old per-tab <c>activeLabelColor</c> (a blue/blue/indigo/
    /// green mix): the selected tab should read as "the app's colour", not as a
    /// per-section identity. <see cref="ThemeRole.AccentText"/> is the role for
    /// exactly this — active tab labels, held to 4.5:1 against Surface.
    /// </summary>
    public static ThemeRole RoleFor(bool isActive) =>
        isActive ? ThemeRole.AccentText : ThemeRole.InkTertiary;

    /// <summary>The tab's colour resolved against whichever theme is live.</summary>
    public static Color ColorFor(bool isActive) => Theme.Color(RoleFor(isActive));
}
