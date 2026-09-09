#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Diagnostics APK for adb-driven device debugging (2026-09-09, Android keyboard pass): the
/// RELEASE configuration (so the IL2CPP/Bee object cache of the store build is reused — a
/// Development Build changes the native flags and costs a cold hour on this Mac) plus the
/// CR_DIAGNOSTICS scripting define, which compiles in the device-only logging
/// (KeyboardInset.LogIfChanged and friends). Signed with the DEBUG key, installable over USB
/// without Play. NOT an upload artifact: StoreAndroidBuild is the release path and never
/// carries the define. Player Settings holds the upload keystore PATH with no passwords, so the
/// custom keystore is switched off for the duration of the build and restored afterwards.
///
///   Unity -batchmode -nographics -projectPath . -buildTarget Android -executeMethod DevAndroidBuild.BuildApk -logFile dev.log
///   adb install -r ../Builds/Android/ChooseReply-dev.apk
/// </summary>
public static class DevAndroidBuild
{
    [MenuItem("Tools/Store/Build Android Debug APK (dev, adb)")]
    public static void BuildApk()
    {
        string output = Path.GetFullPath("../Builds/Android/ChooseReply-dev.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        bool customKeystore = PlayerSettings.Android.useCustomKeystore;
        bool appBundle = EditorUserBuildSettings.buildAppBundle;
        PlayerSettings.Android.useCustomKeystore = false;
        EditorUserBuildSettings.buildAppBundle = false;

        BuildResult result;
        try
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "CR_DIAGNOSTICS" },
            };
            Debug.Log($"[DevAndroidBuild] diagnostics .apk (CR_DIAGNOSTICS) → {output}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            result = report.summary.result;
            Debug.Log($"[DevAndroidBuild] result={result} errors={report.summary.totalErrors} time={report.summary.totalTime}");
        }
        finally
        {
            PlayerSettings.Android.useCustomKeystore = customKeystore;
            EditorUserBuildSettings.buildAppBundle = appBundle;
            // BuildPlayer saves ProjectSettings.asset with the keystore switched OFF; without this
            // save the on-disk file keeps that state (seen 2026-09-09) while memory has it restored.
            AssetDatabase.SaveAssets();
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
    }
}
#endif
