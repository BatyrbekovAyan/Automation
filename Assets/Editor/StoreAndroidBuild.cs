#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// The Google Play build entry (2026-09-06): one method that produces a RELEASE .aab with the
/// store settings applied, the RevenueCat Android dependency resolved, and the upload key taken
/// from the environment — so a build made on another machine, or headless, cannot silently
/// emit an unsigned .apk with whatever the local Editor last had ticked.
///
///   Unity -batchmode -nographics -projectPath . -buildTarget Android
///         -executeMethod StoreAndroidBuild.BuildAab [-aabPath /abs/out.aab] -logFile build.log
///
/// Keystore: Player Settings keeps only the keystore PATH; passwords must never land in
/// ProjectSettings.asset (LFS-tracked, committed). They come from the environment —
/// CR_UPLOAD_KEYSTORE (path), CR_UPLOAD_KEYSTORE_PASS, CR_UPLOAD_KEY_ALIAS, CR_UPLOAD_KEY_PASS.
/// With all four set the build signs with the upload key; with none set it signs with the debug
/// key and SAYS so (Play rejects a debug-signed bundle at upload). Play App Signing holds the
/// app signing key; the upload key is replaceable through Play support if it is ever lost.
///
/// Dependencies: PurchasesWrapper.java imports com.revenuecat.purchases.*, which reaches the
/// build only through EDM4U writing the package's RevenueCatDependencies.xml into
/// mainTemplate.gradle's **DEPS** block. The resolver is invoked here by reflection (the EDM4U
/// assembly is a UPM package, so a hard reference would break compilation the day it moves) and
/// <see cref="StoreBillingKeyGuard"/> refuses a release build whose template still lacks it.
///
/// RELEASE on purpose: <see cref="BuildOptions.None"/>. Exits the batch process with 0/1 itself.
/// </summary>
public static class StoreAndroidBuild
{
    private const string PathArg = "-aabPath";

    [MenuItem("Tools/Store/Build Android App Bundle")]
    public static void BuildAab()
    {
        string output = ArgAfter(PathArg)
                        ?? Path.GetFullPath($"../Builds/Android/ChooseReply-{PlayerSettings.bundleVersion}-{PlayerSettings.Android.bundleVersionCode}.aab");
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        StoreAndroidSettingsApplier.Apply();
        ApplyKeystoreFromEnvironment();
        ResolveAndroidDependencies();

        EditorUserBuildSettings.buildAppBundle = true;
        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };

        Debug.Log($"[StoreAndroidBuild] release .aab → {output}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildResult result = report.summary.result;
        Debug.Log($"[StoreAndroidBuild] result={result} errors={report.summary.totalErrors} " +
                  $"size={report.summary.totalSize / (1024 * 1024)} MB time={report.summary.totalTime}");

        if (Application.isBatchMode)
            EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
    }

    private static void ApplyKeystoreFromEnvironment()
    {
        string keystore = Environment.GetEnvironmentVariable("CR_UPLOAD_KEYSTORE");
        string keystorePass = Environment.GetEnvironmentVariable("CR_UPLOAD_KEYSTORE_PASS");
        string alias = Environment.GetEnvironmentVariable("CR_UPLOAD_KEY_ALIAS");
        string keyPass = Environment.GetEnvironmentVariable("CR_UPLOAD_KEY_PASS");

        bool all = !string.IsNullOrEmpty(keystore) && !string.IsNullOrEmpty(keystorePass)
                   && !string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(keyPass);
        if (!all)
        {
            Debug.LogWarning("[StoreAndroidBuild] CR_UPLOAD_KEYSTORE / _PASS / CR_UPLOAD_KEY_ALIAS / _PASS not all set — " +
                             "signing with the DEBUG key. Play refuses a debug-signed bundle at upload; fine for a device pass.");
            return;
        }
        if (!File.Exists(keystore))
            throw new BuildFailedException($"[StoreAndroidBuild] keystore not found: {keystore}");

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystore;
        PlayerSettings.Android.keystorePass = keystorePass;
        PlayerSettings.Android.keyaliasName = alias;
        PlayerSettings.Android.keyaliasPass = keyPass;
        Debug.Log($"[StoreAndroidBuild] signing with upload key '{alias}' from {Path.GetFileName(keystore)}");
    }

    /// <summary>
    /// GooglePlayServices.PlayServicesResolver.ResolveSync(true) via reflection. A missing
    /// resolver is reported, not fatal — StoreBillingKeyGuard still refuses a release build whose
    /// template carries no RevenueCat dependency.
    /// </summary>
    private static void ResolveAndroidDependencies()
    {
        Type resolver = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("GooglePlayServices.PlayServicesResolver", false))
            .FirstOrDefault(t => t != null);
        MethodInfo resolveSync = resolver?.GetMethod("ResolveSync", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(bool) }, null);
        if (resolveSync == null)
        {
            Debug.LogWarning("[StoreAndroidBuild] External Dependency Manager not found — run Assets → External " +
                             "Dependency Manager → Android Resolver → Resolve by hand before building.");
            return;
        }

        bool ok = (bool)resolveSync.Invoke(null, new object[] { true });
        Debug.Log($"[StoreAndroidBuild] Android dependency resolution {(ok ? "succeeded" : "FAILED")}.");
    }

    private static string ArgAfter(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }
}
#endif
