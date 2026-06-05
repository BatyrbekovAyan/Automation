using System.Collections.Generic;

/// <summary>
/// First-paint point budget for the message list. Each message type costs a
/// number of points proportional to the vertical space its bubble occupies;
/// the budget is how many points one screen of viewport holds. On chat open we
/// spawn messages (newest first) until the screen is covered and leave the rest
/// in the cached queue to load on scroll. This stops media-heavy chats from
/// over-spawning items far off-screen during the slide-in animation.
///
/// Pure logic, unit-tested in FirstScreenBudgetTests — ChatManager just calls
/// <see cref="MessageCount"/>.
///
/// Coverage rule: keep adding messages while the screen still has empty space
/// (points &lt; budget). The message that crosses the budget line is partially
/// visible at the top of the viewport, so it IS included — stopping before it
/// (only taking messages that fit entirely within budget) leaves an empty band
/// up to one portrait tall above the newest messages.
/// </summary>
public static class FirstScreenBudget
{
    /// <summary>Points one screen holds. ~Fills a 1080×2400 mobile viewport.</summary>
    public const float PointBudget = 20f;

    // Media weight tracks displayed height: portrait is tallest, square middle,
    // landscape shortest.
    public const float PortraitMedia  = 9f;
    public const float SquareMedia    = 8f;
    public const float LandscapeMedia = 5f;
    public const float StickerWeight  = 4.5f;
    public const float AudioWeight    = 2f;   // also voice + document
    public const float TextWeight     = 1f;   // also unknown

    // "Square" = longer side within this ratio of the shorter (≈ within 15% of
    // 1:1). Anything more elongated counts as portrait or landscape. Media with
    // missing dimensions normalizes to aspect 1.0 upstream, so it lands here.
    public const float SquareAspectTolerance = 1.15f;

    /// <summary>
    /// How many messages from the start of a newest-first list to spawn on first
    /// paint. Adds messages until the screen is covered, including the one that
    /// crosses the budget (it is partially visible). Always returns at least one
    /// for a non-empty list; 0 for null/empty.
    /// </summary>
    public static int MessageCount(IReadOnlyList<MessageViewModel> sortedNewestFirst)
    {
        if (sortedNewestFirst == null || sortedNewestFirst.Count == 0) return 0;

        float points = 0;
        int count = 0;
        foreach (var vm in sortedNewestFirst)
        {
            // Stop once the screen is already covered. The check is on accumulated
            // points (not the prospective next item), so the message that crosses
            // the budget is still spawned — otherwise a tall newest message leaves
            // an empty band at the top of the viewport.
            if (count > 0 && points >= PointBudget) break;
            points += Weight(vm);
            count++;
        }
        return count;
    }

    /// <summary>Point cost of one message, by type and — for image/video — orientation.</summary>
    public static float Weight(MessageViewModel vm)
    {
        switch (vm.type)
        {
            case MessageType.Image:
            case MessageType.Video:
                return MediaWeight(vm);
            case MessageType.Sticker:
                return StickerWeight;
            case MessageType.Audio:
            case MessageType.Voice:
            case MessageType.Document:
                return AudioWeight;
            default: // Chat (text) and Unknown
                return TextWeight;
        }
    }

    private static float MediaWeight(MessageViewModel vm)
    {
        // OrientedAspect corrects rotated phone videos (stored as a landscape
        // frame plus a 90/270 rotation flag) so they're weighed as portrait.
        float aspect = MediaBubbleSize.OrientedAspect(vm.aspectRatio, vm.videoRotation);
        float longSideRatio = aspect >= 1f ? aspect : 1f / aspect;
        if (longSideRatio <= SquareAspectTolerance) return SquareMedia;
        return aspect > 1f ? LandscapeMedia : PortraitMedia;
    }
}
