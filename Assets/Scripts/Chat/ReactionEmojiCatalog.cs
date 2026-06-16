/// <summary>
/// Curated set of common reaction emoji (raw unicode), shown in the "+" full picker.
/// Includes the six quick-bar emoji. Order is the display order in the grid. Rendered
/// to TMP sprites at display time via UnicodeEmojiConverter; the same emoji system
/// (EmojiPatchService) lazy-loads any sprite not yet in the static atlas.
/// </summary>
public static class ReactionEmojiCatalog
{
    public static readonly string[] All =
    {
        // Faces
        "😀", "😃", "😄", "😁", "😆", "😅", "🤣", "😂",
        "🙂", "😊", "😇", "😍", "😘", "😗", "😋", "😎",
        "🤩", "🥳", "🤔", "😐", "😴", "😮", "😢", "😭",
        "😡", "😱", "😨", "🥺", "😬", "🤯", "🤗", "😤",
        "🙄", "😏",
        // Hands & gestures
        "👍", "👎", "👏", "🙌", "🙏", "💪", "👌", "✌️",
        "🤞", "👋", "🤝", "🤙",
        // Hearts & symbols
        "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍",
        "💔", "💕", "💯", "🔥", "✨", "🎉", "🎊", "⭐",
        "👀", "💀",
    };
}
