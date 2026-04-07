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
    }
}
#endif
