#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Headless iOS SIMULATOR build for the App Review demo video
/// (docs/store/demo-video-script.md, «Режим A»). Command line:
///
///   Unity -batchmode -nographics -projectPath . -buildTarget iOS
///         -executeMethod DemoSimulatorBuild.Build -demoBuildPath "/abs/output/dir" -logFile build.log
///
/// Why a script and not the Build Settings window: the Editor must be CLOSED for a headless run
/// (single-instance project lock), and the SDK flip is the one thing that must never leak —
/// an archive for App Store Connect made from a project left on Simulator SDK fails/rejects.
/// So the flip is scoped to this method: Simulator SDK is set only for the duration of
/// BuildPlayer and the previous value is restored in <c>finally</c>, then saved.
///
/// RELEASE on purpose (<see cref="BuildOptions.None"/>): a Development Build paints its
/// watermark into the recording (caught on the IAP screenshots 2026-09-01).
///
/// Exits the batch process with 0/1 itself — the plain <c>-quit</c> exit code does not reflect
/// a failed BuildPlayer, so the shell would report a broken build as success.
/// </summary>
public static class DemoSimulatorBuild
{
    private const string PathArg = "-demoBuildPath";

    [MenuItem("Tools/Store/Build iOS Simulator (demo video)")]
    public static void Build()
    {
        string output = ArgAfter(PathArg) ?? Path.GetFullPath("../Builds/Build - iOS Sim");
        iOSSdkVersion previousSdk = PlayerSettings.iOS.sdkVersion;
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
        BuildResult result = BuildResult.Unknown;
        try
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.iOS,
                extraScriptingDefines = new[] { "DEMO_SIMULATOR" },   // gates SimulatorRenderingGuard
                // Append when the Xcode project already exists: a replace would also wipe the
                // DerivedData kept inside it (the ~1h IL2CPP object cache on this machine).
                options = Directory.Exists(output)
                    ? BuildOptions.AcceptExternalModificationsToPlayer
                    : BuildOptions.None,
            };
            Debug.Log($"[DemoSimBuild] iOS Simulator SDK, release → {output}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            result = report.summary.result;
            Debug.Log($"[DemoSimBuild] result={result} errors={report.summary.totalErrors} " +
                      $"time={report.summary.totalTime}");
        }
        finally
        {
            PlayerSettings.iOS.sdkVersion = previousSdk;
            AssetDatabase.SaveAssets();
            Debug.Log($"[DemoSimBuild] iOS SDK restored to {previousSdk}");
        }

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
