/// <summary>
/// Semantic colour roles a themed element can bind to. A role names WHAT the
/// colour means, never which theme it belongs to — the same role resolves to a
/// different value under the light and dark <see cref="ThemeAsset"/>.
///
/// Deliberately NOT here: WhatsApp green, Telegram blue and the activation-switch
/// ON green. Those carry identity/meaning and never change with the theme — they
/// live as constants on <see cref="Theme.Fixed"/>.
/// </summary>
public enum ThemeRole
{
    // Surfaces
    Background,        // screen ground behind cards/lists
    Surface,           // card / row / bar fill
    Hairline,          // decorative separators (no contrast floor)
    Border,            // decorative card outline (no contrast floor)
    InputBorder,       // border that IS the affordance (input wells, icon buttons) — ≥3:1

    // Ink
    InkPrimary,
    InkSecondary,
    InkTertiary,

    // Accent («Чернильный»)
    AccentFill,        // filled buttons, active pills, unread badges
    AccentText,        // links, active tab labels/underlines — ≥4.5:1 on Surface
    AccentOnFill,      // label sitting on AccentFill — ≥4.5:1 on it

    // Controls
    SwitchOffTrack,    // activation switch OFF track (ON is Theme.Fixed.SwitchOnGreen)

    // Dashboard outcome statuses (light/dark siblings of one semantic hue)
    StatusOrderCollected,
    StatusOwnerNeeded,
    StatusInDialog,
    StatusClientSilent,
    StatusQuestionClosed,

    // Semantic moments
    Destructive,       // «Удалить …» — never confusable with AccentFill
    PositiveBg,        // success pill background (e.g. «+8», «Онлайн»)
    PositiveInk,       // ink on PositiveBg

    // Chat thread
    ChatWallpaper,
    BubbleIncoming,
    BubbleOutgoing,
}
