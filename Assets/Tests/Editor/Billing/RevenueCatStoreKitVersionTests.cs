using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// SOURCE guard: RevenueCat must be configured with StoreKit 1 on iOS (App Review rejection №2,
/// 2026-09-15: «the in-app purchases were not responsive» on iPad Air 11-inch (M3), iPadOS 26.6).
///
/// Reproduced on the iPad Air (M4) simulator, iOS 26.5, iPhone-compatibility window: with
/// StoreKit 2 the purchase reaches storekitd and then never presents a sheet nor calls back —
/// the paywall stays busy forever, which is precisely what the reviewer saw. The same build with
/// StoreKit 1 raised the sandbox sign-in sheet within a second. EditMode cannot run the SDK, so
/// this pins the configuration line itself; RevenueCatBackend's comment carries the evidence.
/// </summary>
public class RevenueCatStoreKitVersionTests
{
    [Test]
    public void RevenueCat_is_configured_with_StoreKit1()
    {
        string path = Path.Combine(Application.dataPath, "Scripts/Billing/RevenueCatBackend.cs");
        Assert.IsTrue(File.Exists(path), "RevenueCatBackend.cs moved — update RevenueCatStoreKitVersionTests");
        string source = File.ReadAllText(path);

        var calls = Regex.Matches(source, @"\.SetStoreKitVersion\(\s*Purchases\.StoreKitVersion\.(\w+)\s*\)");
        Assert.AreEqual(1, calls.Count, "exactly one SetStoreKitVersion call is expected in FinishConfigure");
        Assert.AreEqual("StoreKit1", calls[0].Groups[1].Value,
            "StoreKit 2 purchases hang without a sheet in iPad compatibility mode on iPadOS 26 (App Review 2026-09-15); " +
            "StoreKit 1 is the presentation path that works — see the comment above the call");
    }
}
