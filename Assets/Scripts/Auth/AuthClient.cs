using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Owns the device's server session (A1, stage 1): the install credentials minted by
/// <c>/webhook/AuthSession</c> and the current capability token. Lives on a GameObject nothing
/// ever deactivates (the UploadCenter pattern) so a refresh cannot die with a screen.
///
/// Storage: PlayerPrefs (app-private storage; per-install, revocable server-side — unlike the
/// shared Wappi token this replaces, extracting it exposes ONE install's own data only).
/// Concurrency: a single in-flight session op; concurrent callers of <see cref="EnsureSession"/>
/// wait for it instead of starting their own.
/// </summary>
public class AuthClient : MonoBehaviour
{
    public const string KeyInstallId = "CrAuth.InstallId";
    public const string KeyInstallSecret = "CrAuth.InstallSecret";
    public const string KeyToken = "CrAuth.Token";
    public const string Path = "/webhook/AuthSession";
    public const int RequestTimeoutSeconds = 30;

    private static AuthClient instance;

    public static AuthClient Instance
    {
        get
        {
            if (instance != null) return instance;
            if (!Application.isPlaying) return null;
            instance = new GameObject(nameof(AuthClient)).AddComponent<AuthClient>();
            return instance;
        }
    }

    public static AuthClient Existing => instance != null ? instance : null;

    public string Token { get; private set; } = "";
    public InstallSession.TokenClaims Claims { get; private set; }
    public string InstallId => PlayerPrefs.GetString(KeyInstallId, "");
    public bool HasInstall => !SessionRefreshPolicy.NeedsBootstrap(InstallId, PlayerPrefs.GetString(KeyInstallSecret, ""));
    /// <summary>Last session op failed (network / server) — callers may retry later; nothing is wiped.</summary>
    public string LastError { get; private set; }

    private bool _inFlight;

    private void Awake()
    {
        instance ??= this;
        Token = PlayerPrefs.GetString(KeyToken, "");
        Claims = InstallSession.TryParse(Token, out var c) ? c : null;
    }

    public bool Owns(string profileId)
        => Claims != null && !string.IsNullOrEmpty(profileId) && Array.IndexOf(Claims.Profiles, profileId) >= 0;

    /// <summary>A token the server just re-issued inside another response (Wappi Proxy profile.add).</summary>
    public void AdoptToken(string token)
    {
        if (!InstallSession.TryParse(token, out var claims)) return;
        Token = token;
        Claims = claims;
        PlayerPrefs.SetString(KeyToken, token);
        PlayerPrefs.Save();
    }

    /// <summary>Bootstrap or refresh only when needed; returns immediately on a fresh, identity-matching token.</summary>
    public IEnumerator EnsureSession()
    {
        if (_inFlight) { while (_inFlight) yield return null; yield break; }
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool fresh = HasInstall && !string.IsNullOrEmpty(Token)
                     && !SessionRefreshPolicy.ShouldRefresh(now, Claims)
                     && !SessionRefreshPolicy.IdentityChanged(Claims, BillingIdentity.AppUserId);
        if (fresh) yield break;
        yield return RunSessionOp();
    }

    /// <summary>Unconditional refresh (after a 401, after a Create that changed ownership).</summary>
    public IEnumerator ForceRefresh()
    {
        if (_inFlight) { while (_inFlight) yield return null; yield break; }
        yield return RunSessionOp();
    }

    private IEnumerator RunSessionOp()
    {
        _inFlight = true;
        try
        {
            string op = HasInstall ? "refresh" : "bootstrap";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var body = new JObject
                {
                    ["op"] = op,
                    ["appUserId"] = BillingIdentity.AppUserId ?? "",
                    ["platform"] = Application.platform.ToString(),
                    ["appVersion"] = Application.version,
                };
                if (op == "refresh")
                {
                    body["installId"] = InstallId;
                    body["installSecret"] = PlayerPrefs.GetString(KeyInstallSecret, "");
                }
                using var www = NewJsonPost($"{Manager.n8nBaseUrl}{Path}", body.ToString(Formatting.None));
                yield return www.SendWebRequest();

                if (www.responseCode == 401 && op == "refresh")
                {
                    // The server no longer knows this install (revoked / wiped): the only way back is a new
                    // install. Its bots are then unowned until the owner claims them — never silently re-bound.
                    Debug.LogWarning("[AuthClient] refresh refused (401) — install revoked or unknown; bootstrapping a new install");
                    ClearInstall();
                    op = "bootstrap";
                    continue;
                }
                if (www.result != UnityWebRequest.Result.Success)
                {
                    LastError = $"[{www.responseCode}] {www.error}";
                    Debug.LogWarning($"[AuthClient] {op} failed {LastError} — keeping the current session state");
                    yield break;
                }
                if (!Apply(op, www.downloadHandler.text))
                {
                    LastError = "bad session response";
                    Debug.LogWarning($"[AuthClient] {op}: unexpected response body");
                }
                yield break;
            }
        }
        finally
        {
            _inFlight = false;
        }
    }

    private bool Apply(string op, string json)
    {
        JObject jo;
        try { jo = JObject.Parse(json); } catch { return false; }
        if (jo.Value<bool?>("success") != true || string.IsNullOrEmpty(jo.Value<string>("token"))) return false;
        if (op == "bootstrap")
        {
            string id = jo.Value<string>("installId"), secret = jo.Value<string>("installSecret");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret)) return false;
            PlayerPrefs.SetString(KeyInstallId, id);
            PlayerPrefs.SetString(KeyInstallSecret, secret);
        }
        AdoptToken(jo.Value<string>("token"));
        LastError = null;
        return true;
    }

    private void ClearInstall()
    {
        PlayerPrefs.DeleteKey(KeyInstallId);
        PlayerPrefs.DeleteKey(KeyInstallSecret);
        PlayerPrefs.DeleteKey(KeyToken);
        PlayerPrefs.Save();
        Token = "";
        Claims = null;
    }

    // ---- legacy-profile claims (profiles created before A1 have no owner) ----------------------

    public sealed class ClaimResult
    {
        public bool Success;
        public string Error;          // already_owned | claim_code_rejected | unauthorized | ...
        public string Method;         // owned | code | reauth
        public long HttpStatus;
    }

    /// <summary>op ∈ claim.start | claim.verify (code) | claim.status; the response's token is adopted.</summary>
    public IEnumerator Claim(string op, string profileId, ChatChannel channel, string code, Action<ClaimResult> done)
    {
        while (_inFlight) yield return null;
        if (!HasInstall) yield return EnsureSession();
        var body = new JObject
        {
            ["op"] = op,
            ["installId"] = InstallId,
            ["installSecret"] = PlayerPrefs.GetString(KeyInstallSecret, ""),
            ["appUserId"] = BillingIdentity.AppUserId ?? "",
            ["profileId"] = profileId ?? "",
            ["channel"] = channel == ChatChannel.Telegram ? "telegram" : "whatsapp",
            ["code"] = code ?? "",
        };
        using var www = NewJsonPost($"{Manager.n8nBaseUrl}{Path}", body.ToString(Formatting.None));
        yield return www.SendWebRequest();
        var result = new ClaimResult { HttpStatus = www.responseCode };
        try
        {
            var jo = JObject.Parse(www.downloadHandler?.text ?? "");
            result.Success = jo.Value<bool?>("success") == true;
            result.Error = jo.Value<string>("error");
            result.Method = jo.Value<string>("claimMethod");
            if (result.Success && !string.IsNullOrEmpty(jo.Value<string>("token"))) AdoptToken(jo.Value<string>("token"));
        }
        catch
        {
            result.Success = false;
            result.Error = result.Error ?? (www.result == UnityWebRequest.Result.Success ? "bad_response" : www.error);
        }
        done?.Invoke(result);
    }

    private static UnityWebRequest NewJsonPost(string url, string json)
    {
        var www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");   // libcurl would otherwise stamp x-www-form-urlencoded
        www.timeout = RequestTimeoutSeconds;
        return www;
    }
}
