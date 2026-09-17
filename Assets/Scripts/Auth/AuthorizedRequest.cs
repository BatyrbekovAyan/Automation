using System;
using System.Collections;
using UnityEngine.Networking;

/// <summary>
/// The ONE place a client request gets its <c>Authorization: Bearer</c> header (A1, stage 1).
/// Every n8n webhook call and every Wappi Proxy call goes through <see cref="Send"/>: it makes
/// sure a session exists (bootstrap / refresh as needed, single-flight), stamps the token, sends,
/// and on a 401 refreshes ONCE and re-sends a freshly built request (a UnityWebRequest cannot be
/// re-sent, hence the factory). A 403 is an ownership refusal and is NOT retried.
///
/// The request the callback receives is NOT disposed — the call site owns it (using-block).
/// </summary>
public static class AuthorizedRequest
{
    public const string HeaderName = "Authorization";

    public static IEnumerator Send(Func<UnityWebRequest> make, Action<UnityWebRequest> done, bool retryOnUnauthorized = true)
    {
        var auth = AuthClient.Instance;
        if (auth != null) yield return auth.EnsureSession();

        UnityWebRequest www = make();
        Stamp(www);
        yield return www.SendWebRequest();

        if (retryOnUnauthorized && www.responseCode == 401 && auth != null)
        {
            www.Dispose();
            yield return auth.ForceRefresh();
            www = make();
            Stamp(www);
            yield return www.SendWebRequest();
        }
        done?.Invoke(www);
    }

    /// <summary>A JSON POST with the explicit Content-Type n8n needs (libcurl would otherwise stamp x-www-form-urlencoded).</summary>
    public static UnityWebRequest JsonPost(string url, string json, int timeoutSeconds)
    {
        var www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json ?? ""));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.timeout = timeoutSeconds;
        return www;
    }

    /// <summary>Stamps the CURRENT token (if any) — for fire-and-forget sites that cannot yield.</summary>
    public static void Stamp(UnityWebRequest www)
    {
        string token = AuthClient.Existing != null ? AuthClient.Existing.Token : null;
        if (!string.IsNullOrEmpty(token))
            www.SetRequestHeader(HeaderName, "Bearer " + token);
    }
}

/// <summary>Pure reader of the n8n webhook envelopes the gates answer with.</summary>
public static class N8nResponse
{
    /// <summary>
    /// True when the body is a JSON object that says <c>"success": false</c> — the shape both the
    /// Create/Edit orchestrators (lastNode mode, HTTP 200) and the auth gate use for refusals.
    /// </summary>
    public static bool IsFailureBody(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            var jo = Newtonsoft.Json.Linq.JObject.Parse(json);
            var s = jo["success"];
            return s != null && s.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && !(bool)s;
        }
        catch
        {
            return false;
        }
    }

    public static string ErrorOf(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return Newtonsoft.Json.Linq.JObject.Parse(json).Value<string>("error"); }
        catch { return null; }
    }
}
