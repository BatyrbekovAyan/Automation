#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Refuses to produce a RELEASE mobile build whose RevenueCat key for that platform is empty
/// (2026-09-05, Google Play track). The runtime side already degrades honestly — a keyless
/// device build gets an uninitialised <see cref="RevenueCatBackend"/>, so nothing can be
/// «bought» for free — but a store binary whose paywall cannot sell is still a broken
/// submission, and the empty androidKey sat unnoticed in secrets.json for weeks because
/// nothing in the build path ever read it. Development builds only warn: they are for
/// device passes, and billing is not always what they are testing.
///
/// Reads Assets/StreamingAssets/secrets.json the way <see cref="Secrets"/> does; only the
/// PRESENCE of the platform key is inspected, nothing is logged from the file.
/// </summary>
public sealed class StoreBillingKeyGuard : IPreprocessBuildWithReport
{
    public enum Verdict { Ok, WarnDevelopment, FailRelease }

    /// <summary>The gradle template EDM4U patches; the RevenueCat artifact it must carry on Android.</summary>
    public const string MainTemplatePath = "Assets/Plugins/Android/mainTemplate.gradle";
    public const string RevenueCatArtifact = "purchases-hybrid-common";

    public int callbackOrder => 0;

    /// <summary>Pure decision: pinned by StoreBillingKeyGuardTests.</summary>
    public static Verdict Decide(bool keyPresent, bool developmentBuild)
    {
        if (keyPresent) return Verdict.Ok;
        return developmentBuild ? Verdict.WarnDevelopment : Verdict.FailRelease;
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        BuildTarget platform = report.summary.platform;
        if (platform != BuildTarget.Android && platform != BuildTarget.iOS) return;

        bool development = (report.summary.options & BuildOptions.Development) != 0;
        bool keyPresent = !string.IsNullOrEmpty(ReadPlatformKey(platform));

        // Android: the native RevenueCat library reaches the build only through EDM4U's patch of
        // mainTemplate.gradle. Without it Configure() throws inside FinishConfigure, the failure is
        // swallowed into permanent trial grace, and the reviewer sees a Buy button that does
        // nothing — silently. A release build must not be producible in that state.
        if (platform == BuildTarget.Android && !development && !GradleTemplateCarriesRevenueCat())
            throw new BuildFailedException(
                $"[StoreBillingKeyGuard] {MainTemplatePath} carries no '{RevenueCatArtifact}' dependency — run " +
                "Assets → External Dependency Manager → Android Resolver → Resolve (or Tools/Store/Build Android App " +
                "Bundle, which resolves first) and commit the patched template.");

        switch (Decide(keyPresent, development))
        {
            case Verdict.Ok:
                return;
            case Verdict.WarnDevelopment:
                Debug.LogWarning($"[StoreBillingKeyGuard] secrets.json has no RevenueCat key for {platform} — " +
                                 "allowed for a Development Build, but the paywall cannot sell in it.");
                return;
            default:
                throw new BuildFailedException(
                    $"[StoreBillingKeyGuard] secrets.json has no RevenueCat key for {platform}. A release build " +
                    "without it ships a paywall that cannot sell anything. Fill revenueCat." +
                    (platform == BuildTarget.Android ? "androidKey" : "iosKey") +
                    " in Assets/StreamingAssets/secrets.json (public SDK key from RevenueCat → Project → API keys), " +
                    "or tick Development Build for a device pass that does not need billing.");
        }
    }

    public static bool GradleTemplateCarriesRevenueCat()
    {
        string path = Path.Combine(Application.dataPath, "..", MainTemplatePath);
        return File.Exists(path) && File.ReadAllText(path).Contains(RevenueCatArtifact);
    }

    private static string ReadPlatformKey(BuildTarget platform)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "secrets.json");
        if (!File.Exists(path)) return "";
        SecretsData data;
        try { data = JsonUtility.FromJson<SecretsData>(File.ReadAllText(path)); }
        catch { return ""; }
        if (data?.revenueCat == null) return "";
        return platform == BuildTarget.Android ? data.revenueCat.androidKey : data.revenueCat.iosKey;
    }
}
#endif
