/// <summary>
/// Pure decision logic for the account owner's ("me") outgoing reaction. Given the
/// long-pressed message and the tapped emoji, produces the ReactionEvent to apply
/// locally — whose .emoji is also the body to POST. Tapping the emoji you already
/// reacted with toggles it off (empty emoji == removal); a different emoji replaces it.
/// Static/pure so it is unit-testable without a MonoBehaviour.
/// </summary>
public static class OutgoingReaction
{
    /// <summary>Reactor key the owner's reactions are stored under (matches ReactionParser.ReactorKey).</summary>
    public const string MeReactorKey = "me";

    /// <summary>The emoji the owner currently has on this message, or null if none.</summary>
    public static string CurrentMyEmoji(MessageViewModel target)
    {
        var reactions = target?.reactions;
        if (reactions == null) return null;
        for (int i = 0; i < reactions.Count; i++)
            if (reactions[i] != null && reactions[i].reactorKey == MeReactorKey)
                return reactions[i].emoji;
        return null;
    }

    /// <summary>The event to apply locally and send (empty emoji when toggling the current one off).</summary>
    public static ReactionEvent Resolve(MessageViewModel target, string tappedEmoji, long timeUnix)
    {
        string current = CurrentMyEmoji(target);
        bool toggleOff = !string.IsNullOrEmpty(current) && current == tappedEmoji;
        return new ReactionEvent
        {
            targetId   = target != null ? target.messageId : null,
            emoji      = toggleOff ? "" : tappedEmoji,
            reactorKey = MeReactorKey,
            senderName = "Me",
            fromMe     = true,
            time       = timeUnix
        };
    }
}
