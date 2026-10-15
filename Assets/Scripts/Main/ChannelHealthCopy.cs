/// <summary>
/// RU copy for a channel the device marked unauthorised (A4 «защита платящих», 2026-10-14): the bot card's subline while a
/// connected channel's Wappi status says <c>authorized:false</c> (<see cref="ChannelHealth"/>). One line, destructive ink,
/// ranked below a channel being connected and a channel the server retired (<see cref="BotCardModel.SublineKind(bool, bool, bool, bool, bool)"/>).
/// Pure seam — registered in FontGlyphCoverageTests, so every glyph exists in the static SF Pro atlases («·» and «ё» yes).
/// <see cref="ProfileRetirementCopy"/> stays the retired channel's copy.
/// </summary>
public static class ChannelHealthCopy
{
    public const string UnauthorizedSubline = "Канал отключён · переподключите в настройках";
}
