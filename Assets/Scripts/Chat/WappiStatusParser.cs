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
    /// True with <paramref name="code"/> set when the body carries a non-empty scalar "code" —
    /// the WhatsApp pairing code the user types into WhatsApp (<c>auth/code</c>).
    ///
    /// Replaces a hard-coded <c>Substring(startIndex, 9)</c> that assumed the canonical
    /// 9-character "XXXX-XXXX" shape: a SHORTER code (or a truncated body) threw
    /// <see cref="System.ArgumentOutOfRangeException"/> mid-coroutine, stranding the
    /// full-screen LoadingPanel with the request button already disabled. Returns the code
    /// VERBATIM at whatever length the server sent, so a longer code is no longer silently
    /// truncated and a shorter one no longer drags in trailing JSON.
    ///
    /// False (code="") when the key is absent, blank, non-scalar, or the JSON is invalid —
    /// the caller then leaves the code label untouched instead of dying.
    /// </summary>
    public static bool TryGetCode(string json, out string code)
    {
        code = "";
        var root = TryParse(json);
        if (root == null) return false;

        string raw = AsScalarString(root["code"]);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        code = raw.Trim();
        return true;
    }

    /// <summary>
    /// True with <paramref name="status"/> set when the body carries a non-empty scalar "status"
    /// (Wappi's ubiquitous <c>"done"</c> / error marker).
    ///
    /// Replaces a fixed <c>Substring(startIndex, 4)</c> read that assumed at least four characters
    /// followed <c>"status":"</c> — a shorter or truncated value threw
    /// <see cref="System.ArgumentOutOfRangeException"/>. Comparing the whole value is also more
    /// honest than a 4-char prefix test, which matched any status merely STARTING with "done".
    /// </summary>
    public static bool TryGetStatus(string json, out string status)
    {
        status = "";
        var root = TryParse(json);
        if (root == null) return false;

        string raw = AsScalarString(root["status"]);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        status = raw.Trim();
        return true;
    }

    /// <summary>
    /// True with <paramref name="profileId"/> set when the body carries a non-empty scalar
    /// "profile_id" (the <c>profile/add</c> response for both channels).
    ///
    /// Replaces a `"profile_id":` +14 offset scan bounded by `","status":`, which had two
    /// failure modes: if the server ever emitted <c>status</c> BEFORE <c>profile_id</c> the
    /// length went negative and threw — killing a nested coroutine so its awaiting parent
    /// (the creation wizard / resend-recreate) never resumed and the LoadingPanel hung
    /// forever; and the hard-coded +14 assumed the compact <c>"profile_id":"</c> form, so a
    /// pretty-printed body stored an id with a LEADING QUOTE, silently breaking every later
    /// Wappi call for that bot with no trace at the point of corruption.
    /// </summary>
    public static bool TryGetProfileId(string json, out string profileId)
    {
        profileId = "";
        var root = TryParse(json);
        if (root == null) return false;

        string raw = AsScalarString(root["profile_id"]);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        profileId = raw.Trim();
        return true;
    }

    /// <summary>
    /// True with <paramref name="png"/> set to the DECODED image bytes when
    /// <paramref name="key"/> holds a base64 PNG — WhatsApp <c>qr/get</c> returns it under
    /// "qrCode" as a <c>data:image/png;base64,…</c> URI, Telegram <c>auth/qr</c> returns raw
    /// base64 under "detail". An optional <c>data:…;base64,</c> prefix is stripped either way.
    ///
    /// Both the extraction AND the decode are guarded: the old code sliced between two literal
    /// tokens (throwing a negative-length Substring if they ever appeared out of order) and then
    /// called <c>Convert.FromBase64String</c> unguarded on the SUCCESS path, so a malformed or
    /// non-base64 payload threw <see cref="System.FormatException"/> and killed the QR coroutine.
    /// Anything unusable — bad JSON, missing key, non-base64 (e.g. Telegram's <c>detail:"2fa"</c>
    /// or an <c>auth_success</c> string) — now simply returns false and the caller retries.
    /// </summary>
    public static bool TryGetQrPng(string json, string key, out byte[] png)
    {
        png = null;
        if (string.IsNullOrEmpty(key)) return false;

        var root = TryParse(json);
        if (root == null) return false;

        string raw = AsScalarString(root[key]);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        raw = raw.Trim();

        // Strip a data URI prefix if present ("data:image/png;base64,AAA…").
        int comma = raw.IndexOf(',');
        if (raw.StartsWith("data:") && comma >= 0 && comma + 1 < raw.Length)
            raw = raw.Substring(comma + 1);

        if (string.IsNullOrWhiteSpace(raw)) return false;

        try { png = System.Convert.FromBase64String(raw); }
        catch (System.FormatException) { png = null; return false; }

        return png.Length > 0;
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
