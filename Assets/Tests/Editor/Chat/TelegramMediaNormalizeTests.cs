using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Covers <see cref="TelegramMediaShape.Resolve"/> — the pure resolver that pulls a Telegram
/// (tapi) media message's file name / mime / size / duration / aspect ratio out of the flat
/// top-level fields + the <c>media_info</c> object (SHAPES.md Q1). All JSON here is SYNTHETIC
/// and PII-free; it mirrors the structural shapes recorded in the capture (real samples stay
/// in the gitignored Tools/tapi/samples/). The key invariants: fractional durations round,
/// missing fields degrade to safe defaults, and a null media_info never throws.
/// </summary>
public class TelegramMediaNormalizeTests
{
    private static JToken MediaInfo(string json) => JToken.Parse(json);

    // --- Image: width/height drive aspect ratio; no duration ---
    [Test]
    public void Resolve_Image_AspectFromMediaInfo()
    {
        var info = MediaInfo("{\"width\":600,\"height\":800,\"size\":149087,\"duration\":0}");
        var r = TelegramMediaShape.Resolve("", "image/jpeg", info);

        Assert.AreEqual(0.75f, r.AspectRatio, 0.0001f);
        Assert.AreEqual(149087L, r.FileSize);
        Assert.AreEqual(0, r.Duration);
        Assert.AreEqual("image/jpeg", r.MimeType);
    }

    // --- Video-as-document: fractional duration is rounded to the nearest second ---
    [Test]
    public void Resolve_Video_FractionalDurationRounds()
    {
        var info = MediaInfo("{\"width\":0,\"height\":0,\"size\":204800,\"duration\":11.4}");
        var r = TelegramMediaShape.Resolve("clip.mp4", "video/mp4", info);

        Assert.AreEqual(11, r.Duration);           // 11.4 -> 11
        Assert.AreEqual(204800L, r.FileSize);
        Assert.AreEqual("clip.mp4", r.FileName);
        Assert.AreEqual(1.0f, r.AspectRatio);      // 0x0 dims => default aspect
    }

    [Test]
    public void Resolve_Duration_RoundsHalfUp()
    {
        var r = TelegramMediaShape.Resolve("a", "audio/ogg", MediaInfo("{\"duration\":31.484}"));
        Assert.AreEqual(31, r.Duration);
        var r2 = TelegramMediaShape.Resolve("a", "audio/ogg", MediaInfo("{\"duration\":40.6}"));
        Assert.AreEqual(41, r2.Duration);

        // Midpoints prove half-up (AwayFromZero): banker's rounding would give 12.5 -> 12
        // and the name of this test would lie (05-06-REVIEW IN-01).
        var mid1 = TelegramMediaShape.Resolve("a", "audio/ogg", MediaInfo("{\"duration\":11.5}"));
        Assert.AreEqual(12, mid1.Duration);
        var mid2 = TelegramMediaShape.Resolve("a", "audio/ogg", MediaInfo("{\"duration\":12.5}"));
        Assert.AreEqual(13, mid2.Duration);
    }

    // --- Document: flat file name + mime carried through; size from media_info ---
    [Test]
    public void Resolve_Document_CarriesNameAndMime()
    {
        var info = MediaInfo("{\"size\":98765,\"duration\":0}");
        var r = TelegramMediaShape.Resolve("price-list.pdf", "application/pdf", info);

        Assert.AreEqual("price-list.pdf", r.FileName);
        Assert.AreEqual("application/pdf", r.MimeType);
        Assert.AreEqual(98765L, r.FileSize);
    }

    // --- Missing media_info (null) degrades to defaults, never throws (T-0506-01 DoS) ---
    [Test]
    public void Resolve_NullMediaInfo_Degrades()
    {
        var r = TelegramMediaShape.Resolve("f.bin", "application/octet-stream", null);

        Assert.AreEqual(1.0f, r.AspectRatio);
        Assert.AreEqual(0L, r.FileSize);
        Assert.AreEqual(0, r.Duration);
        Assert.AreEqual("f.bin", r.FileName);
        Assert.AreEqual("application/octet-stream", r.MimeType);
    }

    // --- Empty media_info object: still safe ---
    [Test]
    public void Resolve_EmptyMediaInfo_Degrades()
    {
        var r = TelegramMediaShape.Resolve(null, null, MediaInfo("{}"));

        Assert.AreEqual(1.0f, r.AspectRatio);
        Assert.AreEqual(0L, r.FileSize);
        Assert.AreEqual(0, r.Duration);
        Assert.IsNull(r.FileName);
        Assert.IsNull(r.MimeType);
    }

    // --- Partial dims (only width) must not divide-by-zero; default aspect ---
    [Test]
    public void Resolve_PartialDims_DefaultAspect()
    {
        var r = TelegramMediaShape.Resolve("", "image/png", MediaInfo("{\"width\":600,\"height\":0}"));
        Assert.AreEqual(1.0f, r.AspectRatio);
    }

    // --- Zero duration stays zero (not a spurious value) ---
    [Test]
    public void Resolve_ZeroDuration_StaysZero()
    {
        var r = TelegramMediaShape.Resolve("", "image/jpeg", MediaInfo("{\"width\":100,\"height\":100,\"duration\":0}"));
        Assert.AreEqual(0, r.Duration);
        Assert.AreEqual(1.0f, r.AspectRatio);
    }

    // --- isGif JSON binding (SHAPES.md Q2 / 05-HUMAN-UAT gap 3): a GIF arrives type:"sticker"
    //     + isGif:true + mimetype:"video/mp4". The [JsonProperty("isGif")] annotation must bind
    //     the flag through JsonConvert (the messages parser); an absent key defaults to false. ---
    [Test]
    public void RawMessage_IsGif_BindsTrue()
    {
        var raw = JsonConvert.DeserializeObject<RawMessage>("{\"isGif\":true}");
        Assert.IsTrue(raw.isGif);
    }

    [Test]
    public void RawMessage_IsGif_AbsentDefaultsFalse()
    {
        var raw = JsonConvert.DeserializeObject<RawMessage>("{\"type\":\"video\"}");
        Assert.IsFalse(raw.isGif);
    }

    [Test]
    public void RawMessage_IsGif_ExplicitFalse()
    {
        var raw = JsonConvert.DeserializeObject<RawMessage>("{\"isGif\":false}");
        Assert.IsFalse(raw.isGif);
    }

    // --- The full observed GIF shape binds coherently: sticker-typed, isGif flag, video mime ---
    [Test]
    public void RawMessage_GifShape_BindsAllFields()
    {
        var raw = JsonConvert.DeserializeObject<RawMessage>(
            "{\"type\":\"sticker\",\"isGif\":true,\"mimetype\":\"video/mp4\",\"file_name\":\"mp4.mp4\"}");

        Assert.IsTrue(raw.isGif);
        Assert.AreEqual("sticker", raw.type);
        Assert.AreEqual("video/mp4", raw.mimetype);
        Assert.AreEqual("mp4.mp4", raw.fileName);
    }
}
