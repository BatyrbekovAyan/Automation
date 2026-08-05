using UnityEngine;

/// <summary>
/// Pure mapping for the bot card's activation footer: label text/color per
/// switch state, plus the switch handle's rest-offset geometry. Kept free of
/// MonoBehaviour so EditMode tests cover it (same pattern as ScrollFabMath).
/// </summary>
public static class BotSwitchFooter
{
    /// <summary>Gap between the handle's edge and the track's edge at rest.</summary>
    public const float HandleEdgeInset = 5f;

    // Theme-routed: this stamp runs on every activation refresh and used to
    // write the light literals over the label's ThemedColor binding — «Бот
    // работает» came out near-black on the dark card. The roles keep the same
    // on/off hierarchy (secondary ink when running, tertiary when paused).
    private static Color OnColor  => Theme.Color(ThemeRole.InkSecondary);
    private static Color OffColor => Theme.Color(ThemeRole.InkTertiary);

    public static string TextFor(bool isOn) => isOn ? "Бот работает" : "Бот на паузе";

    public static Color ColorFor(bool isOn) => isOn ? OnColor : OffColor;

    /// <summary>
    /// Distance from track centre to the handle's rest point on either side —
    /// replaces the old magic "-30 * width / 160" which was tuned to the
    /// original 100×40 track and under-travels any other size.
    /// </summary>
    public static float RestOffset(float trackWidth, float handleWidth) =>
        (trackWidth - handleWidth) / 2f - HandleEdgeInset;
}
