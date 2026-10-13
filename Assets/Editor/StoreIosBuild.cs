#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// The App Store build entry (2026-10-13): the iOS twin of <see cref="StoreAndroidBuild"/>. Exports a
/// RELEASE Xcode project with the store settings applied, so a headless export cannot pick up whatever
/// the local Editor last had ticked (a Development Build, the Simulator SDK after a demo-video build).
///
///   Unity -batchmode -nographics -projectPath . -buildTarget iOS
///         -executeMethod StoreIosBuild.ExportXcode [-xcodePath /abs/dir] -logFile build.log
///
/// Output: ../Builds/Built - iOS Automation (the folder every App Store upload so far came from), APPENDED
/// when a Unity Xcode project is already there — Xcode-side signing settings survive. An Append build merges
/// the previous Info.plist; <see cref="FixIOSBuildSettings"/> re-stamps the purpose strings LAST
/// (callbackOrder 1000), so check them after the export:
///   plutil -p "../Builds/Built - iOS Automation/Info.plist" | grep UsageDescription
/// The build number is NOT bumped here — that stays the explicit «Bump iOS Build Number» step before an upload.
/// Archive and upload are Xcode's (Product → Archive → Distribute → App Store Connect), with the owner's
/// signing. Exits the batch process with 0/1 itself.
/// </summary>
public static class StoreIosBuild
{
    private const string PathArg = "-xcodePath";
    private const string DefaultRelativePath = "../Builds/Built - iOS Automation";

    [MenuItem("Tools/Store/Export iOS Xcode Project")]
    public static void ExportXcode()
    {
        string output = Path.GetFullPath(ArgAfter(PathArg) ?? DefaultRelativePath);

        StoreIosSettingsApplier.Apply();
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        EditorUserBuildSettings.development = false;

        BuildOptions options = BuildOptions.None;
        bool append = File.Exists(Path.Combine(output, "Unity-iPhone.xcodeproj", "project.pbxproj"));
        if (append) options |= BuildOptions.AcceptExternalModificationsToPlayer;

        var buildOptions = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = output,
            target = BuildTarget.iOS,
            options = options,
        };

        Debug.Log($"[StoreIosBuild] release Xcode project ({(append ? "append" : "replace")}) → {output}; " +
                  $"version {PlayerSettings.bundleVersion} build {PlayerSettings.iOS.buildNumber}");
        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
        BuildResult result = report.summary.result;
        Debug.Log($"[StoreIosBuild] result={result} errors={report.summary.totalErrors} time={report.summary.totalTime}");

        if (Application.isBatchMode)
            EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
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
