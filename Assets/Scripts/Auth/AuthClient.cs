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
    /// <summary>The billing id the last successful bootstrap/refresh ASKED for (R2: the server may decline it).</summary>
    public const string KeyRequestedBillingId = "CrAuth.RequestedBillingId";
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
    /// <summary>
    /// R2: the server declined the billing id this install asked for because another install holds it — the token
    /// carries no billing identity, so billing-bound calls (usage, suggestions, a new bot) are refused until it is
    /// released. V3 (2026-09-28): ONLY a revoke of the holder releases it (support), never «the holder has no live
    /// bot». Not an identity change; a return to the app retries it once (<see cref="OnApplicationFocus"/>).
    /// </summary>
    public bool BillingConflict { get; private set; }

    private bool _inFlight;
    private long _lastBillingRetrySec;

    private void Awake()
    {
        instance ??= this;
        Token = PlayerPrefs.GetString(KeyToken, "");
        Claims = InstallSession.TryParse(Token, out var c) ? c : null;
    }

    /// <summary>
    /// V3: back in the app with a declined billing id (billingConflict) — ask again once: the holding install may have
    /// been revoked by support since, and the id then follows on this refresh instead of the planned one ~10 h later.
    /// </summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!SessionRefreshPolicy.ShouldRetryBillingOnResume(hasFocus, HasInstall, _inFlight, Claims, BillingIdentity.AppUserId, now, _lastBillingRetrySec)) return;
        _lastBillingRetrySec = now;
        StartCoroutine(ForceRefresh());
    }

    /// <summary>
    /// V3 (owner decision 2026-10-06): «Удалить все данные» wipes bots, history and settings but KEEPS this install's
    /// server identity — the RevenueCat id survives a PlayerPrefs wipe in the SDK's own storage, and a second install
    /// naming it would be refused (billing ids bind to one install). Restores the credentials synchronously, before any
    /// code can read CrAuth.* again and bootstrap a second install, and saves ONCE after they are back (the wipe itself
    /// must not save — the disk never holds a wipe without them).
    /// </summary>
    public static void PreserveAcrossWipe(Action wipe)
    {
        string id = PlayerPrefs.GetString(KeyInstallId, ""), secret = PlayerPrefs.GetString(KeyInstallSecret, "");
        string token = PlayerPrefs.GetString(KeyToken, ""), requested = PlayerPrefs.GetString(KeyRequestedBillingId, "");
        wipe?.Invoke();
        if (!SessionRefreshPolicy.NeedsBootstrap(id, secret))
        {
            PlayerPrefs.SetString(KeyInstallId, id);
            PlayerPrefs.SetString(KeyInstallSecret, secret);
            if (!string.IsNullOrEmpty(token)) PlayerPrefs.SetString(KeyToken, token);
            PlayerPrefs.SetString(KeyRequestedBillingId, requested);
        }
        PlayerPrefs.Save();
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
                     && !SessionRefreshPolicy.IdentityChanged(Claims, BillingIdentity.AppUserId, PlayerPrefs.GetString(KeyRequestedBillingId, ""));
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
            // ≤ 3 round trips: a refused refresh → bootstrap → the bootstrap's own refresh (V3)
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string requested = BillingIdentity.AppUserId ?? "";
                var body = new JObject
                {
                    ["op"] = op,
                    ["appUserId"] = requested,
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

                if (SessionRefreshPolicy.ShouldDiscardInstall(op, www.responseCode, N8nResponse.ErrorOf(www.downloadHandler?.text)))
                {
                    // The server's explicit verdict: it no longer knows this install (revoked / wiped). The only
                    // way back is a new install; its bots stay unowned until the owner claims them — never silently
                    // re-bound. Any other failure (a bare 401, 503 db_unavailable, network) keeps the credentials (R11).
                    Debug.LogWarning("[AuthClient] refresh refused (401 install_revoked) — bootstrapping a new install");
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
                else
                {
                    // asked for; a declined id is not re-asked each call. A bootstrap records none (V3: it binds nothing).
                    PlayerPrefs.SetString(KeyRequestedBillingId, SessionRefreshPolicy.RequestedIdToRecord(op, requested));
                    PlayerPrefs.Save();
                    if (SessionRefreshPolicy.RefreshAfterBootstrap(op, requested))
                    {
                        op = "refresh";                 // V3: the billing id is bound only once the new install proves its secret
                        continue;
                    }
                    if (BillingConflict) Debug.LogWarning("[AuthClient] the billing id is held by another install — no billing identity on this token (billingConflict)");
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
        BillingConflict = jo.Value<bool?>("billingConflict") == true;
        LastError = null;
        return true;
    }

    private void ClearInstall()
    {
        PlayerPrefs.DeleteKey(KeyInstallId);
        PlayerPrefs.DeleteKey(KeyInstallSecret);
        PlayerPrefs.DeleteKey(KeyToken);
        PlayerPrefs.DeleteKey(KeyRequestedBillingId);
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
