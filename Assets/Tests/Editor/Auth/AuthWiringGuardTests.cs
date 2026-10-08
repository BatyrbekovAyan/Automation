using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Source-invariant guard for A1 (audit 2026-09, stage 1). EditMode cannot send a request, so
/// the transport contract is pinned at the source: the client holds NO Wappi credential and talks
/// to Wappi ONLY through the server-side proxy, and every n8n call carries the session token via
/// the one helper that stamps it. A new call site that bypasses either fails here, not on device.
/// </summary>
public class AuthWiringGuardTests
{
    private static string Scripts => Path.Combine(Application.dataPath, "Scripts");

    private static string[] AllScripts() => Directory.GetFiles(Scripts, "*.cs", SearchOption.AllDirectories);

    private static string Rel(string path) => path.Substring(Application.dataPath.Length + 1).Replace('\\', '/');

    /// <summary>Source with line comments stripped — the guards judge code, not the prose explaining it.</summary>
    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");

    [Test]
    public void NoScriptEverReadsAWappiToken()
    {
        var hits = AllScripts().Where(f => Code(f).Contains("wappiAuthToken")).Select(Rel).ToList();
        Assert.IsEmpty(hits, "the shared Wappi token must not exist in the client (A1)");
    }

    [Test]
    public void OnlyThePureUrlSeamMentionsTheWappiHost()
    {
        // WappiEndpoints stays as the pure, tested path builder; no transport file may build a wappi.pro URL.
        var hits = AllScripts().Where(f => Code(f).Contains("wappi.pro")).Select(Rel)
            .Where(p => p != "Scripts/Chat/WappiEndpoints.cs").ToList();
        Assert.IsEmpty(hits, "a client file builds a direct Wappi URL — every Wappi call goes through WappiProxy");
    }

    [Test]
    public void TransportFilesNeverUseThePureUrlSeam()
    {
        string[] transport =
        {
            "Scripts/Main/Manager.cs", "Scripts/Main/BotSettings.Auth.cs", "Scripts/Main/ChatManager.cs",
            "Scripts/Main/ChatManager.DeleteChat.cs", "Scripts/Main/ChatManager.MediaSend.cs",
            "Scripts/Main/ChatManager.QuoteResolve.cs", "Scripts/Main/ChatManager.ReactionResolve.cs",
            "Scripts/Main/ChatManager.ReactionSend.cs",
        };
        foreach (var t in transport)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, t));
            Assert.IsFalse(text.Contains("WappiEndpoints.Sync("), $"{t} builds a Wappi URL — it must call WappiProxy.Send/NewRequest instead");
            Assert.IsFalse(text.Contains("EndpointFor("), $"{t} uses the legacy media endpoint builder");
        }
    }

    [Test]
    public void TheAuthorizationHeaderIsStampedInExactlyOnePlace()
    {
        var literal = AllScripts().Where(f => Code(f).Contains("SetRequestHeader(\"Authorization\"")).Select(Rel).ToList();
        Assert.IsEmpty(literal, "a call site sets Authorization itself — go through AuthorizedRequest.Send / WappiProxy.Send");
        string helper = Code(Path.Combine(Scripts, "Auth/AuthorizedRequest.cs"));
        Assert.IsTrue(helper.Contains("public const string HeaderName = \"Authorization\"") && helper.Contains("SetRequestHeader(HeaderName, \"Bearer \" + token)"),
            "AuthorizedRequest.Stamp must be the one place that sets the Bearer header");
        var byConst = AllScripts().Where(f => Code(f).Contains("SetRequestHeader(AuthorizedRequest.HeaderName") || Code(f).Contains("SetRequestHeader(HeaderName")).Select(Rel).ToList();
        CollectionAssert.AreEquivalent(new[] { "Scripts/Auth/AuthorizedRequest.cs" }, byConst, "only AuthorizedRequest may use the header constant");
    }

    [Test]
    public void EveryDirectSendIsAnAllowListedPublicOrSignedUrlDownload()
    {
        // Files allowed to call SendWebRequest() themselves: signed/public media URLs (no credential
        // exists for them), the session client itself, the two helpers, and the quit-time
        // fire-and-forget (no coroutine can run in OnApplicationQuit) plus the media send that keeps
        // its request for progress/Abort (session ensured explicitly before NewRequest).
        string[] allowed =
        {
            "Scripts/Auth/AuthClient.cs", "Scripts/Auth/AuthorizedRequest.cs", "Scripts/Chat/WappiProxy.cs",
            "Scripts/UI/MessageItemView.cs", "Scripts/UI/ChatItemView.cs", "Scripts/Chat/MessageHeaderView.cs",
            "Scripts/Chat/EmojiPatchService.cs", "Scripts/Chat/LinkScraper.cs", "Scripts/Main/Secrets.cs",
            "Scripts/Main/Manager.cs", "Scripts/Main/ChatManager.MediaSend.cs",
        };
        var offenders = AllScripts()
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\.SendWebRequest\(\)"))
            .Select(Rel).Where(p => !allowed.Contains(p)).ToList();
        Assert.IsEmpty(offenders, "a file sends a request directly — route it through AuthorizedRequest.Send or add it here deliberately");

        // Manager.cs: only the quit-time settle may send directly (it cannot yield), stamped by WappiProxy.NewRequest.
        string manager = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/Manager.cs"));
        int direct = Regex.Matches(manager, @"\.SendWebRequest\(\)").Count;
        Assert.AreEqual(2, direct, "Manager.cs may only send directly from SettlePendingProfilesBeforeQuit (two profile deletes)");
        Assert.IsTrue(manager.Contains("whatsappDelete = WappiProxy.NewRequest(WappiProxy.Op.ProfileDelete"), "quit-time delete must go through the proxy");
    }

    [Test]
    public void EveryN8nWebhookSiteGoesThroughAuthorizedRequest()
    {
        // A file that BUILDS a webhook request: mentions a /webhook/ path in code and constructs UnityWebRequests.
        var files = AllScripts().Where(f => Code(f).Contains("/webhook/") && Code(f).Contains("UnityWebRequest")).Select(Rel)
            .Where(p => !p.StartsWith("Scripts/Auth/") && p != "Scripts/Chat/WappiProxy.cs" && p != "Scripts/Main/N8nWebhookRequests.cs").ToList();
        Assert.IsNotEmpty(files);
        foreach (var f in files)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, f));
            Assert.IsTrue(text.Contains("AuthorizedRequest.Send(") || text.Contains("WappiProxy.Send(") || text.Contains("WappiProxy.NewRequest("),
                $"{f} calls an n8n webhook without AuthorizedRequest / WappiProxy");
        }
    }

    /// <summary>
    /// R11 (2026-09-27): a refresh answered with a transient server fault (a DB outage became a bare 401) wiped the
    /// install — the secret is unrecoverable and every bot then had to be claimed back one by one. The credentials may
    /// be cleared ONLY on the server's explicit revoked/unknown verdict, decided by the pure policy.
    /// </summary>
    [Test]
    public void TheInstallIsClearedOnlyOnAnExplicitRevokedVerdict()
    {
        string client = Code(Path.Combine(Scripts, "Auth/AuthClient.cs"));
        Assert.IsFalse(Regex.IsMatch(client, @"responseCode\s*==\s*401\s*&&\s*op\s*==\s*""refresh"""),
            "a bare 401 on refresh must not decide the wipe — ask SessionRefreshPolicy.ShouldDiscardInstall");
        var calls = Regex.Matches(client, @"ClearInstall\(\);");
        Assert.AreEqual(1, calls.Count, "ClearInstall() must have exactly one call site");
        int at = calls[0].Index;
        int guard = client.LastIndexOf("SessionRefreshPolicy.ShouldDiscardInstall(", at, System.StringComparison.Ordinal);
        Assert.IsTrue(guard >= 0 && at - guard < 400 && client.Substring(guard, at - guard).Contains("N8nResponse.ErrorOf("),
            "ClearInstall() must sit directly under `if (SessionRefreshPolicy.ShouldDiscardInstall(op, status, N8nResponse.ErrorOf(body)))`");
    }

    /// <summary>
    /// R2 (2026-09-27): the freshness check must compare against the billing id the last session op ASKED for —
    /// a server-declined id (billingConflict, u = null) must not trigger a refresh on every request.
    /// </summary>
    [Test]
    public void EnsureSession_ComparesAgainstTheRequestedBillingId_AndRecordsIt()
    {
        string client = Code(Path.Combine(Scripts, "Auth/AuthClient.cs"));
        Assert.IsTrue(client.Contains("SessionRefreshPolicy.IdentityChanged(Claims, BillingIdentity.AppUserId, PlayerPrefs.GetString(KeyRequestedBillingId"),
            "EnsureSession must pass the last requested billing id");
        Assert.IsTrue(client.Contains("PlayerPrefs.SetString(KeyRequestedBillingId, SessionRefreshPolicy.RequestedIdToRecord(op, requested))")
                      && client.Contains("[\"appUserId\"] = requested"),
            "the id recorded as requested must be the one the session op sent (none for a bootstrap — V3: it binds nothing)");
        Assert.IsTrue(client.Contains("jo.Value<bool?>(\"billingConflict\")"), "AuthClient must read billingConflict");
    }

    /// <summary>
    /// V3 (2026-09-28): the server binds a billing id only on a refresh. A bootstrap that named one must refresh in the
    /// same op — otherwise the first billing-bound call after a fresh install would go out with no billing identity.
    /// </summary>
    [Test]
    public void ABootstrapIsFollowedByItsOwnRefresh_AndADeclinedIdIsRetriedOnResume()
    {
        string client = Code(Path.Combine(Scripts, "Auth/AuthClient.cs"));
        int record = client.IndexOf("SessionRefreshPolicy.RequestedIdToRecord(op, requested)", System.StringComparison.Ordinal);
        int follow = client.IndexOf("if (SessionRefreshPolicy.RefreshAfterBootstrap(op, requested))", System.StringComparison.Ordinal);
        Assert.IsTrue(record >= 0 && follow > record, "the bootstrap's follow-up refresh must be decided right after recording the request");
        Assert.IsTrue(Regex.IsMatch(client, @"if \(SessionRefreshPolicy\.RefreshAfterBootstrap\(op, requested\)\)\s*\{\s*op = ""refresh"";\s*continue;"),
            "the follow-up must re-enter the loop as a refresh");
        Assert.IsTrue(Regex.IsMatch(client, @"for \(int attempt = 0; attempt < 3; attempt\+\+\)"), "room for refused-refresh → bootstrap → refresh");
        Assert.IsTrue(client.Contains("void OnApplicationFocus(bool hasFocus)")
                      && client.Contains("SessionRefreshPolicy.ShouldRetryBillingOnResume(hasFocus, HasInstall, _inFlight, Claims, BillingIdentity.AppUserId"),
            "a declined billing id must be retried on return to the app (support revoke → the id follows without a 10 h wait)");
    }

    /// <summary>
    /// V3 (owner decision 2026-10-06): «Удалить все данные» keeps the install's server identity — the ONLY DeleteAll in
    /// the app runs inside AuthClient.PreserveAcrossWipe, which restores the credentials synchronously and saves once
    /// after they are back; ownership is re-read after the wipe (the token still lists the retired bots).
    /// </summary>
    [Test]
    public void TheFullWipe_KeepsTheInstall_AndRereadsOwnership()
    {
        var wipers = AllScripts().Where(f => Regex.IsMatch(Code(f), @"PlayerPrefs\.DeleteAll\b")).Select(Rel).ToList();
        CollectionAssert.AreEqual(new[] { "Scripts/Main/ProfileSubPages.Account.cs" }, wipers,
            "PlayerPrefs.DeleteAll outside the full wipe would drop the install credentials (V3)");
        string account = Code(Path.Combine(Scripts, "Main/ProfileSubPages.Account.cs"));
        int start = account.IndexOf("private void FinishWipe()", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(start, 0, "FinishWipe not found");
        string body = account.Substring(start);
        int preserve = body.IndexOf("AuthClient.PreserveAcrossWipe(() => PlayerPrefs.DeleteAll());", System.StringComparison.Ordinal);
        int refresh = body.IndexOf("ForceRefresh()", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(preserve, 0, "PlayerPrefs.DeleteAll must run inside AuthClient.PreserveAcrossWipe(...)");
        Assert.AreEqual(1, Regex.Matches(account, @"PlayerPrefs\.DeleteAll\b").Count, "exactly one DeleteAll in the account page");
        Assert.IsTrue(refresh > preserve, "ownership must be re-read after the wipe (the token still lists the retired bots)");
        string client = Code(Path.Combine(Scripts, "Auth/AuthClient.cs"));
        int p = client.IndexOf("public static void PreserveAcrossWipe(", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(p, 0);
        string pw = client.Substring(p, client.IndexOf("public bool Owns(", p, System.StringComparison.Ordinal) - p);
        int read = pw.IndexOf("PlayerPrefs.GetString(KeyInstallSecret", System.StringComparison.Ordinal);
        int wipe = pw.IndexOf("wipe?.Invoke()", System.StringComparison.Ordinal);
        int restore = pw.IndexOf("PlayerPrefs.SetString(KeyInstallSecret, secret)", System.StringComparison.Ordinal);
        int save = pw.LastIndexOf("PlayerPrefs.Save()", System.StringComparison.Ordinal);
        Assert.IsTrue(read >= 0 && wipe > read && restore > wipe && save > restore,
            "the credentials are read before the wipe, written back after it, and only then saved");
    }

    [Test]
    public void SecretsShipOnlyTheRevenueCatPublicKeysAndTheBaseUrl()
    {
        string secrets = File.ReadAllText(Path.Combine(Scripts, "Main/Secrets.cs"));
        Assert.IsFalse(secrets.Contains("public string wappiAuthToken"), "SecretsData must not carry the Wappi token");
        string example = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "secrets.json.example"));
        Assert.IsFalse(example.Contains("wappiAuthToken"), "secrets.json.example must not advertise the removed key");
    }
}
