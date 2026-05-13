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
    private const string TagFailed  = "<sprite name=\"tick_failed\">";

    private static readonly HashSet<string> LoggedUnknown = new();

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
        if (string.IsNullOrEmpty(raw)) return DeliveryStatus.None;
        switch (raw.ToLowerInvariant())
        {
            case "sent":      return DeliveryStatus.Sent;
            case "delivered": return DeliveryStatus.Delivered;
            case "read":      return DeliveryStatus.Read;
            default:
                if (LoggedUnknown.Add(raw))
                    Debug.LogWarning($"[DeliveryTickFormatter] Unknown Wappi delivery_status: '{raw}'");
                return DeliveryStatus.None;
        }
    }
}
