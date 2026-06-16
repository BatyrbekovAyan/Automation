using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps DeliveryStatus values to TMP sprite tags from ChatTicks.asset, and
/// parses the Wappi string-typed delivery_status field into the same enum.
/// Sprite names must match the glyph character names in ChatTicks.asset,
/// which ChatTicksFallbackRegistrar registers as a TMP fallback at startup.
/// </summary>
public static class DeliveryTickFormatter
{
    private const string TagPending = "<sprite name=\"tick_pending\">";
    private const string TagSent    = "<sprite name=\"tick_sent\">";
    private const string TagDouble  = "<sprite name=\"tick_double\">";
    private const string TagBlue    = "<sprite name=\"tick_double_blue\">";
    // Failed shows a ⚠️ warning emoji in front of the tick_failed glyph (now a refresh
    // icon in ChatTicks) so users see something is wrong; the whole timeText stays tappable
    // to retry (see MessageItemView.UpdateRetryButton). The emoji's sprite name must be the
    // fully-qualified Twemoji name "26a0-fe0f" (⚠️ is U+26A0 U+FE0F) — the bare "26a0" matches
    // no glyph in the emoji atlas and TMP would render the literal tag as text. The glyph
    // lives in texture-29, a serialized fallback of the default sprite asset, so it resolves
    // by name in the time label exactly like the ChatTicks tick glyphs do.
    //
    // The emoji is wrapped in <size=80%> so it sits a touch smaller than full line-height,
    // closer to the monochrome refresh/tick glyph beside it, without looking shrunken. Its
    // measured advance only weakly affects MessageItemView's inline-time reservation (the
    // <space=…px> appended to the bubble text), so this value is a visual choice, not a
    // layout lever. </size> closes before the refresh glyph so only the emoji is scaled.
    private const string TagFailed  = "<size=80%><sprite name=\"26a0-fe0f\"></size><sprite name=\"tick_failed\">";

    private static readonly HashSet<string> loggedUnknown = new();

    public static string GetSprite(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Pending   => TagPending,
        DeliveryStatus.Sent      => TagSent,
        DeliveryStatus.Delivered => TagDouble,
        DeliveryStatus.Read      => TagBlue,
        DeliveryStatus.Failed    => TagFailed,
        _                        => null,
    };

    public static DeliveryStatus ParseWappiString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DeliveryStatus.None;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "sent":      return DeliveryStatus.Sent;
            case "delivered": return DeliveryStatus.Delivered;
            case "read":      return DeliveryStatus.Read;
            default:
                if (loggedUnknown.Add(raw))
                    Debug.LogWarning($"[DeliveryTickFormatter] Unknown Wappi delivery_status: '{raw}'");
                return DeliveryStatus.None;
        }
    }
}
