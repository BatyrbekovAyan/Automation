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

    private static readonly Color OnColor  = new Color32(0x3A, 0x3A, 0x3C, 0xFF);
    private static readonly Color OffColor = new Color32(0x8E, 0x8E, 0x93, 0xFF);

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
