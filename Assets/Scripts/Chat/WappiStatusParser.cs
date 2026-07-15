using Newtonsoft.Json.Linq;

/// <summary>
/// Pure, null/parse-tolerant reader for a Wappi <c>get/status</c> JSON body (both the
/// WhatsApp <c>api/sync</c> and Telegram <c>tapi/sync</c> endpoints share the shape).
/// Replaces the fragile hand-rolled substring scans in <see cref="Manager"/> and
/// <see cref="BotSettings"/> that broke on the PRETTY-PRINTED tapi response.
///
/// The tapi body carries TWO "phone" keys — a nested <c>account.phone</c> AND a
/// top-level <c>phone</c> (immediately before <c>platform</c>). The old extractor
/// matched <c>account.phone</c> first, and its no-whitespace <c>","platform":</c>
/// guard never matched the pretty <c>",\n  "platform":</c>, so it grabbed a huge
/// raw-JSON slice and stored THAT as the phone (05-09 device UAT / owner screenshot,
/// bot "53" showed a JSON blob instead of a number).
///
/// JObject-based, so key order and whitespace are irrelevant. Every method swallows
/// malformed input and returns false rather than throwing (mirrors the other pure Chat
/// seams). Telegram is the only wired caller for now; the byte-identical WhatsApp status
/// parses could adopt this later as a safe follow-up.
/// </summary>
public static class WappiStatusParser
{
    // A real phone number never exceeds this; anything longer is a stale JSON blob.
    private const int MaxPlausiblePhoneLength = 20;

    /// <summary>
    /// True with <paramref name="authorized"/> set when the body carries a parseable
    /// boolean "authorized" field (accepts a real <c>bool</c> or a "true"/"false" string).
    /// False (authorized=false) when the field is missing, mistyped, or the JSON is invalid.
    /// </summary>
    public static bool TryGetAuthorized(string json, out bool authorized)
    {
        authorized = false;
        var root = TryParse(json);
        if (root == null) return false;

        var token = root["authorized"];
        if (token == null) return false;

        switch (token.Type)
        {
            case JTokenType.Boolean:
                authorized = token.Value<bool>();
                return true;
            case JTokenType.String when bool.TryParse(token.Value<string>(), out bool parsed):
                authorized = parsed;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// True with <paramref name="phone"/> set to a bare digit string when the body carries a
    /// phone. Prefers the top-level "phone" and falls back to "account.phone"; a single leading
    /// '+' is stripped. False (phone="") when neither key holds a value or the JSON is invalid.
    /// </summary>
    public static bool TryGetPhone(string json, out string phone)
    {
        phone = "";
        var root = TryParse(json);
        if (root == null) return false;

        string raw = AsScalarString(root["phone"]);
        if (string.IsNullOrEmpty(raw))
            raw = AsScalarString((root["account"] as JObject)?["phone"]);
        if (string.IsNullOrEmpty(raw)) return false;

        raw = raw.Trim();
        phone = raw.StartsWith("+") ? raw.Substring(1) : raw;
        // A lone "+" strips to "": the contract promises false-when-no-value, not true+empty.
        if (string.IsNullOrEmpty(phone)) { phone = ""; return false; }
        return true;
    }

    /// <summary>
    /// True only for a short, all-digit value (one optional leading '+'). Rejects empty,
    /// letters, JSON punctuation (<c>{ } " : ,</c>) and anything longer than
    /// <see cref="MaxPlausiblePhoneLength"/> — so a stale raw-JSON blob persisted in
    /// <c>{bot}TelegramNumber</c> reads as implausible and can be dropped without re-auth.
    /// </summary>
    public static bool IsPlausiblePhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string v = value.Trim();
        if (v.Length > MaxPlausiblePhoneLength) return false;

        int start = v[0] == '+' ? 1 : 0;
        if (start >= v.Length) return false; // a lone "+"

        for (int i = start; i < v.Length; i++)
            if (v[i] < '0' || v[i] > '9') return false;

        return true;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static JObject TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); }
        catch { return null; }
    }

    // Reads a token as a string only when it is a JSON string or integer (a phone can
    // arrive either way); anything else (object/array/bool/null) is treated as absent.
    private static string AsScalarString(JToken token)
    {
        if (token == null) return null;
        return token.Type switch
        {
            JTokenType.String => token.Value<string>(),
            JTokenType.Integer => token.Value<long>().ToString(),
            _ => null
        };
    }
}
