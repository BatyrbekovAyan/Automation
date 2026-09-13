using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// SOURCE guard for the paywall's two bottom-bar actions (App Review rejection 2026-09-10,
/// Guideline 2.1(b) «the app failed to complete the in-app purchase»).
///
/// The primary CTA used to be the free-trial line on a fresh install and merely CLOSED the
/// paywall; the purchase lived on a secondary button. A reviewer's sandbox account owns no
/// subscription, so that is exactly the state App Review opens the paywall in — the server
/// trace of the review session shows three paywall visits and not a single StoreKit purchase.
/// <see cref="PaywallRowsTests"/> pins the copy seam; this pins the WIRING, because a seam
/// that says «the CTA is the subscribe form» proves nothing if the button's listener still
/// closes the screen. Same idiom as SendPathWiringTests: brace-match the method, assert on
/// its body — EditMode cannot click a scene button.
/// </summary>
public class PaywallCtaWiringTests
{
    private const string Controller = "Scripts/Billing/PaywallController.cs";

    [Test]
    public void Cta_button_starts_the_purchase()
    {
        string wiring = MethodBody(Controller, "EnsureInit");
        StringAssert.Contains("ctaButton.onClick.AddListener(StartPurchase)", wiring,
            "the primary CTA must go straight to StartPurchase — a branch that can Close() here is the 2026-09-10 rejection");
        StringAssert.DoesNotContain("ctaButton.onClick.AddListener(OnCtaClicked)", wiring,
            "OnCtaClicked was the trial branch that closed the paywall without buying; it must not come back");
    }

    [Test]
    public void Trial_button_only_closes_the_paywall()
    {
        string wiring = MethodBody(Controller, "EnsureInit");
        StringAssert.Contains("trialButton.onClick.AddListener(OnTrialClicked)", wiring);

        string body = MethodBody(Controller, "OnTrialClicked");
        StringAssert.Contains("Close()", body, "the trial takes no card and starts on channel auth — the row just gets out of the way");
        StringAssert.DoesNotContain("StartPurchase", body, "the trial row must never buy: its label promises «бесплатно»");
    }

    [Test]
    public void Purchase_path_no_longer_has_a_trial_branch()
    {
        string body = MethodBody(Controller, "StartPurchase");
        StringAssert.DoesNotContain("IsTrialOffer", body);
        StringAssert.DoesNotContain("Close();\n            return;", body,
            "an early Close() inside the purchase path is the same trap under another name");
    }

    // ---- source helpers -----------------------------------------------------------------

    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update PaywallCtaWiringTests");
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
