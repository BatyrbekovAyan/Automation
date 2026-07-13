/// <summary>
/// Pure, side-effect-free decision seam for the TopBar channel switcher. Given the
/// chip being rendered, the currently active channel, and each channel's connectivity,
/// it returns whether that chip is <see cref="ChannelChipState.Selected"/> (the active
/// segment) and/or <see cref="ChannelChipState.Muted"/> (its own channel is unconnected).
///
/// No MonoBehaviour, no namespace — matches the flat Assets/Scripts/UI/ pure-seam style
/// (TabRefreshGate / ChannelResolver precedent), so the whole matrix is unit-testable in
/// EditMode without a scene.
/// </summary>
public static class ChannelSwitcherModel
{
    /// <summary>
    /// Decide a single chip's visual state.
    /// <list type="bullet">
    ///   <item>Selected == the chip IS the active channel (pure equality; connectivity-independent).</item>
    ///   <item>Muted == the chip's OWN channel is not connected (WhatsApp chip ⇐ !waConnected;
    ///         Telegram chip ⇐ !tgConnected).</item>
    /// </list>
    /// A chip can be BOTH selected and muted (the active channel sits on a disconnected
    /// profile) — muted is never suppressed by selection.
    /// </summary>
    public static ChannelChipState StateFor(ChatChannel chip, ChatChannel active, bool waConnected, bool tgConnected)
    {
        bool selected = chip == active;                                   // equality only
        bool ownChannelConnected = chip == ChatChannel.Telegram ? tgConnected : waConnected;
        bool muted = !ownChannelConnected;                               // connectivity only — never suppressed by selection
        return new ChannelChipState(selected, muted);
    }
}

/// <summary>
/// Per-chip visual decision produced by <see cref="ChannelSwitcherModel.StateFor"/>:
/// is this segment the active (selected) one, and is its channel disconnected (muted)?
/// </summary>
public readonly struct ChannelChipState
{
    /// <summary>True when this chip's channel is the active one (drives the filled look).</summary>
    public readonly bool Selected;

    /// <summary>True when this chip's own channel has no connected profile (drives the ~40% fade).</summary>
    public readonly bool Muted;

    public ChannelChipState(bool selected, bool muted)
    {
        Selected = selected;
        Muted = muted;
    }
}
