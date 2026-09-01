#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

public static class FixIOSBuildSettings
{
    // iPad left the target device family for store submission (targetDevice: iPhone only),
    // so Unity stops EMITTING these — but an APPEND build keeps whatever the older universal
    // project put in the Resources phase, and Xcode then fails the build outright with
    // "Build input file cannot be found: .../LaunchScreen-iPad.storyboard". Cleaned below,
    // guarded by an on-disk existence check so a legitimately present file is never dropped.
    static readonly string[] IPadLaunchResources =
    {
        "LaunchScreen-iPad.storyboard",
        "LaunchScreen-iPad.png",
        "LaunchScreen-iPadPortrait.png",
        "LaunchScreen-iPadLandscape.png",
    };

    [PostProcessBuild(1000)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS)
            return;

        string pbxPath = PBXProject.GetPBXProjectPath(path);
        var pbx = new PBXProject();
        pbx.ReadFromFile(pbxPath);

#if UNITY_2019_3_OR_NEWER
        string mainTarget = pbx.GetUnityMainTargetGuid();
#else
        string mainTarget = pbx.TargetGuidByName("Unity-iPhone");
#endif

        // 🔥 REMOVE entitlements completely
        pbx.SetBuildProperty(mainTarget, "CODE_SIGN_ENTITLEMENTS", "");
        pbx.SetBuildProperty(mainTarget, "CODE_SIGN_ENTITLEMENTS[sdk=iphoneos*]", "");

        DropDanglingIPadLaunchResources(pbx, path);

        pbx.WriteToFile(pbxPath);

        // Export compliance (store audit 2026-09-01): the app is HTTPS-only —
        // UnityWebRequest rides the OS-provided TLS, which is "standard encryption"
        // and exempt. Answering in the plist keeps every TestFlight / App Store upload
        // from stalling on the manual export-compliance questionnaire. Revisit only if
        // a plugin ever bundles its own crypto (OpenSSL/mbedTLS class).
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromString(File.ReadAllText(plistPath));
        plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        File.WriteAllText(plistPath, plist.WriteToString());
    }

    /// <summary>
    /// Removes iPad launch-screen entries the append build inherited from the pre-iPhone-only
    /// project, but ONLY when the file is genuinely absent from the generated Xcode folder.
    /// </summary>
    static void DropDanglingIPadLaunchResources(PBXProject pbx, string buildPath)
    {
        foreach (string resource in IPadLaunchResources)
        {
            if (File.Exists(Path.Combine(buildPath, resource)))
                continue;

            string fileGuid = pbx.FindFileGuidByProjectPath(resource);
            if (string.IsNullOrEmpty(fileGuid))
                continue;

            pbx.RemoveFile(fileGuid);
            UnityEngine.Debug.Log($"[FixIOSBuildSettings] Dropped dangling Xcode reference to missing '{resource}'.");
        }
    }
}
#endif
