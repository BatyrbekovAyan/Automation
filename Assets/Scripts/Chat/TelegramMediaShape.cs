using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

/// <summary>
/// Pure resolver for a Telegram (tapi) media message's metadata. tapi delivers media with
/// <c>body:null</c> and <c>s3Info:{}</c> (SHAPES.md Q1) — the bytes arrive later via
/// <c>message/media/download</c> by id (placeholder-first UX), so there is no inline URL to
/// read. File name + mime are flat top-level fields; dimensions / size / duration live in
/// the flat <c>media_info</c> object (<c>{width,height,size,duration,...}</c>).
///
/// The WhatsApp Normalize branches read these off the <c>body</c> JObject, which is null on
/// tapi, so they no-op for Telegram — this seam supplies them instead. Unit-testable and
/// null-tolerant: a missing <c>media_info</c> or any absent field degrades to a safe default
/// (aspect 1.0, size/duration 0) and never throws. Duration is parsed as a double and rounded
/// half-up (<see cref="MidpointRounding.AwayFromZero"/> — not the banker's-rounding default)
/// because tapi reports fractional seconds (e.g. 11.4, 31.484).
/// </summary>
public static class TelegramMediaShape
{
    public readonly struct Result
    {
        public readonly string FileName;
        public readonly string MimeType;
        public readonly long   FileSize;
        public readonly int    Duration;      // seconds (media_info.duration, rounded)
        public readonly float  AspectRatio;   // width/height from media_info, else 1.0

        public Result(string fileName, string mimeType, long fileSize, int duration, float aspectRatio)
        {
            FileName = fileName;
            MimeType = mimeType;
            FileSize = fileSize;
            Duration = duration;
            AspectRatio = aspectRatio;
        }
    }

    public static Result Resolve(string fileName, string mimetype, JToken mediaInfo)
    {
        long fileSize = 0;
        int duration = 0;
        float aspectRatio = 1.0f;

        if (mediaInfo is JObject info)
        {
            float width = ParseFloat(info["width"]);
            float height = ParseFloat(info["height"]);
            if (width > 0 && height > 0) aspectRatio = width / height;

            fileSize = ParseLong(info["size"]);

            double seconds = ParseDouble(info["duration"]);
            // Half-up, matching the display expectation (Math.Round's default is banker's
            // rounding — 12.5 would round DOWN to 12; 05-06-REVIEW IN-01).
            if (seconds > 0) duration = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
        }

        return new Result(fileName, mimetype, fileSize, duration, aspectRatio);
    }

    private static float ParseFloat(JToken token) =>
        token != null && float.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float value)
            ? value : 0f;

    private static long ParseLong(JToken token) =>
        token != null && long.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out long value)
            ? value : 0L;

    private static double ParseDouble(JToken token) =>
        token != null && double.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
            ? value : 0d;
}
