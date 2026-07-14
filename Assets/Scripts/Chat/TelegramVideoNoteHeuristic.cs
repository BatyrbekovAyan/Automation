using System.Globalization;
using Newtonsoft.Json.Linq;

/// <summary>
/// Pure heuristic that decides whether a Telegram (tapi) media message is a video note
/// (кружок — the round auto-recorded clip). Telegram exposes NO reliable flag for this:
/// <c>media_info.is_round</c> is BROKEN — it comes back <c>false</c> for a genuine note on
/// BOTH messages/get AND messages/id/get (SHAPES.md Q2 / 05-HUMAN-UAT gap 2, probe_23368).
/// So this heuristic DELIBERATELY ignores <c>is_round</c> and infers the note from its
/// invariants instead: a note is always square, default-named <c>video.mp4</c>, and ≤ 60s.
///
/// Channel-blind + pure so it is unit-testable without a live server (mirrors
/// <see cref="TelegramMediaShape"/>). A square regular WhatsApp video would also match, but the
/// SOLE call site lives inside ChatManager's <c>ActiveChannel==Telegram</c> block, so WhatsApp
/// media is never flagged — the same channel-gating regression architecture 05-06 used. The
/// accepted false positive (a square Telegram video rendering round) is documented in
/// 05-HUMAN-UAT gap 2. Null-tolerant: any missing/malformed field ⇒ false, never throws (T-0507-01).
/// </summary>
public static class TelegramVideoNoteHeuristic
{
    private const string VideoType = "video";               // raw tapi type string (lowercase)
    private const string DefaultNoteFileName = "video.mp4"; // Telegram's default note file name
    private const int MaxNoteDurationSeconds = 60;          // Telegram caps video notes at 60s

    public static bool IsVideoNote(string baseType, string fileName, JToken mediaInfo)
    {
        // A note is a genuine video (NOT a phone video sent as a document, and NOT a GIF that
        // arrives sticker-typed) carrying Telegram's default note file name.
        if (baseType != VideoType) return false;
        if (fileName != DefaultNoteFileName) return false;
        if (!(mediaInfo is JObject info)) return false;

        long width = ParseLong(info["width"]);
        long height = ParseLong(info["height"]);
        if (width <= 0 || height <= 0 || width != height) return false;   // notes are square

        // is_round is INTENTIONALLY not read — it is unreliable (see class summary).
        double duration = ParseDouble(info["duration"]);
        return duration > 0 && duration <= MaxNoteDurationSeconds;
    }

    private static long ParseLong(JToken token) =>
        token != null && long.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out long value)
            ? value : 0L;

    private static double ParseDouble(JToken token) =>
        token != null && double.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
            ? value : 0d;
}
