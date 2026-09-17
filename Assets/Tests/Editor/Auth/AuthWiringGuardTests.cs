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

    [Test]
    public void SecretsShipOnlyTheRevenueCatPublicKeysAndTheBaseUrl()
    {
        string secrets = File.ReadAllText(Path.Combine(Scripts, "Main/Secrets.cs"));
        Assert.IsFalse(secrets.Contains("public string wappiAuthToken"), "SecretsData must not carry the Wappi token");
        string example = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "secrets.json.example"));
        Assert.IsFalse(example.Contains("wappiAuthToken"), "secrets.json.example must not advertise the removed key");
    }
}
