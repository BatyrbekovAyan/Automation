#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

public static class FixIOSBuildSettings
{
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
}
#endif
