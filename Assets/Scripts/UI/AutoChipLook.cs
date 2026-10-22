using System;

/// <summary>
/// How one «Авто» capsule is painted: the colour ROLE of each of its parts and how much of it the learning fill covers.
/// Roles, not colours — the painter resolves them through Theme.Color at paint time, so a theme switch needs no new paint.
/// Built only by <see cref="AutoChipLook.For"/>.
/// </summary>
public readonly struct AutoChipPaint
{
    internal AutoChipPaint(ThemeRole ring, ThemeRole face, ThemeRole ink, ThemeRole lamp, bool lampSolid,
        ThemeRole fill, float fillFraction)
    {
        Ring = ring;
        Base = face;
        Ink = ink;
        Lamp = lamp;
        LampSolid = lampSolid;
        Fill = fill;
        FillFraction = fillFraction;
    }

    /// <summary>The capsule's outline.</summary>
    public ThemeRole Ring { get; }

    /// <summary>The capsule's face: what the fill grows over, and the core of a hollow lamp.</summary>
    public ThemeRole Base { get; }

    /// <summary>The label.</summary>
    public ThemeRole Ink { get; }

    /// <summary>The lamp's ring — and its core too when <see cref="LampSolid"/>.</summary>
    public ThemeRole Lamp { get; }

    /// <summary>True: a filled lamp (the core takes <see cref="Lamp"/>). False: hollow (the core takes <see cref="Base"/>).</summary>
    public bool LampSolid { get; }

    /// <summary>The learning fill's colour. Always a real role, but only visible while <see cref="FillFraction"/> is above 0.</summary>
    public ThemeRole Fill { get; }

    /// <summary>0..1 of the capsule's inner width the fill covers. 0 = there is no fill.</summary>
    public float FillFraction { get; }
}

/// <summary>
/// The pure rule behind every «Авто» capsule (spec 2026-10-22-learning-progress-percent §4.3): from "is «Авто» ON in this
/// place" and the bot's learning percent (null = no data) to its <see cref="AutoChipPaint"/>. The bot card, the chats
/// header, the bot-switcher row and the open-chat chip all paint through one painter, so they all get the same answer.
/// <list type="bullet">
///   <item>No data: today's look, role for role. OFF = Border ring on Surface, hollow lamp, InkSecondary. ON = PositiveBg,
///         filled lamp, PositiveInk. A bot without learning data must not look any different.</item>
///   <item>Learning, 0..99 %: OFF keeps today's face and gets a LearnFillOff fill over N % of the width. ON swaps the face for
///         LearnTrackOn (the lighter green) and grows a LearnFillOn fill over N % of it. 0 % is learning with an empty
///         fill — NOT "no data": an ON capsule at 0 % is all track.</item>
///   <item>Trained, 100 %: OFF is LearnFillOff all over inside its Border ring (the lamp stays hollow). ON is LearnFillOn all
///         over with no fill layer — nothing is left to grow. In the dark theme LearnFillOn is PositiveBg, so it is exactly
///         today's ON capsule; in the light theme it is the slightly stronger tint, which avoids a visible jump from 99 %.</item>
/// </list>
/// The label and the lamp keep the same roles at every percent. The percent comes from AutoReadinessPolicy.StateFor, already on
/// 0..100; it is clamped here again so a value off the scale can never draw a negative width or a fill that overflows.
/// "Подключить" (no live channel) outranks all of this and is the caller's call. No Unity objects, so the matrix is
/// EditMode-testable (AutoChipLookTests).
/// </summary>
public static class AutoChipLook
{
    private const int Trained = 100;

    public static AutoChipPaint For(bool on, int? percent)
    {
        if (!percent.HasValue) return on ? OnWithoutData() : OffWithoutData();
        int shown = Clamp(percent.Value);
        return on ? OnLearning(shown) : OffLearning(shown);
    }

    /// <summary>
    /// The caption under the capsule: «Учится · 45 %». Null while there is nothing to say — no data, or trained (100 %).
    /// Reads the same clamped number the fill does.
    /// </summary>
    public static string Caption(int? percent)
    {
        if (!percent.HasValue) return null;
        int shown = Clamp(percent.Value);
        return shown >= Trained ? null : AutoReadinessCopy.Caption(shown);
    }

    // The fill role of a "no data" paint names the face's own colour: nothing is drawn, and drawing it anyway changes nothing.
    private static AutoChipPaint OffWithoutData() =>
        new AutoChipPaint(ThemeRole.Border, ThemeRole.Surface, ThemeRole.InkSecondary, ThemeRole.InkTertiary,
            lampSolid: false, fill: ThemeRole.Surface, fillFraction: 0f);

    private static AutoChipPaint OnWithoutData() =>
        new AutoChipPaint(ThemeRole.PositiveBg, ThemeRole.PositiveBg, ThemeRole.PositiveInk, ThemeRole.PositiveInk,
            lampSolid: true, fill: ThemeRole.PositiveBg, fillFraction: 0f);

    // OFF: today's face at every percent, the blue fill on top. At 100 % the fill spans the whole inner width.
    private static AutoChipPaint OffLearning(int percent) =>
        new AutoChipPaint(ThemeRole.Border, ThemeRole.Surface, ThemeRole.InkSecondary, ThemeRole.InkTertiary,
            lampSolid: false, fill: ThemeRole.LearnFillOff, fillFraction: percent / 100f);

    private static AutoChipPaint OnLearning(int percent) => percent >= Trained
        ? new AutoChipPaint(ThemeRole.LearnFillOn, ThemeRole.LearnFillOn, ThemeRole.PositiveInk, ThemeRole.PositiveInk,
            lampSolid: true, fill: ThemeRole.LearnFillOn, fillFraction: 0f)
        : new AutoChipPaint(ThemeRole.LearnTrackOn, ThemeRole.LearnTrackOn, ThemeRole.PositiveInk, ThemeRole.PositiveInk,
            lampSolid: true, fill: ThemeRole.LearnFillOn, fillFraction: percent / 100f);

    private static int Clamp(int percent) => Math.Clamp(percent, 0, Trained);
}
