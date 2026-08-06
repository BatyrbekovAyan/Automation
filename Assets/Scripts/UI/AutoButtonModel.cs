/// <summary>
/// Pure decision seam for the chats-header «Авто» button (2026-08 top-bar
/// restyle, docs/design/ui-restyle/chats-topbar-spec.md). Semi-auto is the
/// silent default state — the UI never names it; the button only switches the
/// autopilot ON (mode 0, «Авто») and OFF (mode 1).
///
/// Encodes the two deliberate behaviours the spec pins:
/// <list type="bullet">
///   <item>DEFAULT: a bot that never saved a value is semi-auto (owner-approved
///         flip from the pre-restyle Авто default).</item>
///   <item>CONFIRM ASYMMETRY: only ENABLING asks for confirmation (the bot
///         starts messaging real clients); disabling is instant.</item>
/// </list>
/// No MonoBehaviour, no namespace — flat Assets/Scripts/UI/ pure-seam style
/// (ChannelSwitcherModel precedent) so the matrix is EditMode-testable.
/// </summary>
public static class AutoButtonModel
{
    /// <summary>Stored-value default for bots with no persisted reply mode.</summary>
    public const ReplyModeToggleBinder.ReplyMode DefaultMode = ReplyModeToggleBinder.ReplyMode.Semi;

    /// <summary>True when the autopilot is engaged (the bot answers clients itself).</summary>
    public static bool IsAutoOn(ReplyModeToggleBinder.ReplyMode mode) =>
        mode == ReplyModeToggleBinder.ReplyMode.Auto;

    /// <summary>The mode a tap moves to from <paramref name="current"/>.</summary>
    public static ReplyModeToggleBinder.ReplyMode Toggled(ReplyModeToggleBinder.ReplyMode current) =>
        IsAutoOn(current) ? ReplyModeToggleBinder.ReplyMode.Semi : ReplyModeToggleBinder.ReplyMode.Auto;

    /// <summary>
    /// Whether a tap from <paramref name="current"/> must pass the confirm popup.
    /// Only the enabling direction confirms — turning auto OFF just hands the
    /// wheel back to the owner and commits instantly.
    /// </summary>
    public static bool ConfirmRequired(ReplyModeToggleBinder.ReplyMode current) => !IsAutoOn(current);
}
