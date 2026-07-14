using NUnit.Framework;
using Newtonsoft.Json.Linq;

/// <summary>
/// Covers <see cref="TelegramVideoNoteHeuristic.IsVideoNote"/> — the Telegram-only heuristic that
/// identifies a video note (кружок) when Telegram gives no reliable flag. Grounded in the
/// 2026-07-14 device UAT (SHAPES.md Q2 / 05-HUMAN-UAT gap 2, probe_23368): a genuine note is
/// square + default-named <c>video.mp4</c> + ≤60s, and <c>is_round</c> is UNRELIABLE (false on a
/// real note), so the heuristic must NOT depend on it. All JSON here is SYNTHETIC + PII-free.
/// </summary>
public class TelegramVideoNoteHeuristicTests
{
    private static JToken Info(string json) => JToken.Parse(json);

    // --- Canonical кружок: square, video.mp4, short duration ---
    [Test]
    public void IsVideoNote_SquareShortVideoMp4_True() =>
        Assert.IsTrue(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "video.mp4", Info("{\"width\":400,\"height\":400,\"duration\":2}")));

    // --- 60s is the inclusive boundary (Telegram caps notes at 60s) ---
    [Test]
    public void IsVideoNote_DurationSixtyBoundary_True() =>
        Assert.IsTrue(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "video.mp4", Info("{\"width\":400,\"height\":400,\"duration\":60}")));

    // --- 61s is over the cap => not a note ---
    [Test]
    public void IsVideoNote_DurationOverSixty_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "video.mp4", Info("{\"width\":400,\"height\":400,\"duration\":61}")));

    // --- Not square => a regular landscape/portrait video, not a note ---
    [Test]
    public void IsVideoNote_NonSquare_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "video.mp4", Info("{\"width\":400,\"height\":300,\"duration\":2}")));

    // --- A non-default file name => a user-picked clip, not an auto-recorded note ---
    [Test]
    public void IsVideoNote_NonDefaultFileName_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "clip.mp4", Info("{\"width\":400,\"height\":400,\"duration\":2}")));

    // --- A phone video sent as a document (refined to Video) has base type "document" — NOT a note ---
    [Test]
    public void IsVideoNote_DocumentBaseType_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote(
            "document", "video.mp4", Info("{\"width\":400,\"height\":400,\"duration\":2}")));

    // --- A GIF arrives type:"sticker" (320×180 mp4.mp4) — a GIF is not a note ---
    [Test]
    public void IsVideoNote_GifStickerTyped_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote(
            "sticker", "mp4.mp4", Info("{\"width\":320,\"height\":180,\"duration\":2}")));

    // --- is_round is DELIBERATELY ignored: false on a genuine note must NOT veto detection ---
    [Test]
    public void IsVideoNote_IsRoundFalseIgnored_True() =>
        Assert.IsTrue(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "video.mp4", Info("{\"width\":400,\"height\":400,\"duration\":2,\"is_round\":false}")));

    // --- Null media_info degrades safe (never throws) — T-0507-01 ---
    [Test]
    public void IsVideoNote_NullMediaInfo_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote("video", "video.mp4", null));

    // --- Empty media_info object: no dims => false, never throws ---
    [Test]
    public void IsVideoNote_EmptyMediaInfo_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote("video", "video.mp4", Info("{}")));

    // --- Explicit 0×0 dims: rejected by the positivity guard BEFORE the square equality
    // --- check, so 0 == 0 must never slip through as "square" ---
    [Test]
    public void IsVideoNote_ZeroDims_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "video.mp4", Info("{\"width\":0,\"height\":0,\"duration\":2}")));

    // --- media_info present but NOT an object (array / bare string): rejected by the
    // --- `is JObject` pattern, never throws ---
    [Test]
    public void IsVideoNote_NonObjectMediaInfo_False()
    {
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote("video", "video.mp4", Info("[400,400]")));
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote("video", "video.mp4", Info("\"400x400\"")));
    }

    // --- Zero duration (e.g. a still) is not a note ---
    [Test]
    public void IsVideoNote_ZeroDuration_False() =>
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "video.mp4", Info("{\"width\":400,\"height\":400,\"duration\":0}")));

    // --- Fractional sub-60 duration still counts (tapi reports fractional seconds) ---
    [Test]
    public void IsVideoNote_FractionalDuration_True() =>
        Assert.IsTrue(TelegramVideoNoteHeuristic.IsVideoNote(
            "video", "video.mp4", Info("{\"width\":384,\"height\":384,\"duration\":2.4}")));

    // --- Null base type / file name degrade safe ---
    [Test]
    public void IsVideoNote_NullArgs_False()
    {
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote(null, "video.mp4", Info("{\"width\":400,\"height\":400,\"duration\":2}")));
        Assert.IsFalse(TelegramVideoNoteHeuristic.IsVideoNote("video", null, Info("{\"width\":400,\"height\":400,\"duration\":2}")));
    }
}
