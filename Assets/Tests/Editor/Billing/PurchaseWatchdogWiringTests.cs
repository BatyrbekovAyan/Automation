using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// SOURCE guard: every store call a screen can start must arm the watchdog and disarm it in its
/// callback — otherwise a hung StoreKit call leaves that screen busy forever (App Review
/// 2026-09-15). EditMode cannot call StoreKit, so the wiring is pinned the way
/// SendPathWiringTests pins the send path: brace-match the method, assert on its body.
/// </summary>
public class PurchaseWatchdogWiringTests
{
    private const string Paywall = "Scripts/Billing/PaywallController.cs";
    private const string Subscription = "Scripts/Main/ProfileSubPages.Subscription.cs";

    [TestCase(Paywall, "StartPurchase")]
    [TestCase(Paywall, "OnRestoreClicked")]
    [TestCase(Subscription, "OnTopUpClicked")]
    [TestCase(Subscription, "OnRestoreClicked")]
    public void Store_call_arms_and_disarms_the_watchdog(string file, string method)
    {
        string body = MethodBody(file, method);
        StringAssert.Contains("StartStoreWatchdog(", body, $"{method} must arm the watchdog before the store call");
        StringAssert.Contains("StopStoreWatchdog()", body, $"{method}'s callback must disarm the watchdog");
        Assert.Less(body.IndexOf("StartStoreWatchdog("), body.IndexOf("StopStoreWatchdog()"),
            "arm before the call, disarm inside the callback");
    }

    [TestCase(Paywall)]
    [TestCase(Subscription)]
    public void Screen_reads_system_ui_from_lost_focus(string file)
    {
        string body = MethodBody(file, "OnApplicationFocus");
        StringAssert.Contains("_storeSheetSeen = true", body,
            "a StoreKit sheet resigns the app's active state — that is the only 'sheet appeared' signal we have");
    }

    [TestCase(Paywall)]
    [TestCase(Subscription)]
    public void Watchdog_routine_defers_to_the_policy(string file)
    {
        string body = MethodBody(file, "StoreWatchdogRoutine");
        StringAssert.Contains("PurchaseWatchdogPolicy.ShouldGiveUp(", body,
            "the give-up rule lives in the tested policy, not in a private re-derivation");
    }

    // ---- source helpers -----------------------------------------------------------------

    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update PurchaseWatchdogWiringTests");
        string source = File.ReadAllText(path);

        var decl = Regex.Match(source,
            @"^[ \t]*(?:(?:public|private|internal|protected|static)[ \t]+)*(?!return\b)[\w<>.]+[ \t]+" +
            Regex.Escape(method) + @"[ \t]*\(",
            RegexOptions.Multiline);
        Assert.IsTrue(decl.Success, $"declaration of {method} not found in {relativePath}");

        int open = source.IndexOf('{', decl.Index);
        Assert.Greater(open, 0, $"no body after {method}");

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"unbalanced braces after {method}");
        return null;
    }
}
