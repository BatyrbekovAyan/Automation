#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

public class AddUniformTypeIdentifiers
{
    [PostProcessBuild(999)]
    public static void OnPostprocessBuild(BuildTarget buildTarget, string path)
    {
        if (buildTarget == BuildTarget.iOS)
        {
            string projPath = PBXProject.GetPBXProjectPath(path);
            PBXProject proj = new PBXProject();
            proj.ReadFromString(File.ReadAllText(projPath));

            // We add it to the UnityFramework target because that's where your error is
            string targetGuid = proj.GetUnityFrameworkTargetGuid();

            // Add the UniformTypeIdentifiers framework
            proj.AddFrameworkToProject(targetGuid, "UniformTypeIdentifiers.framework", false); // false means it's required, not optional

            File.WriteAllText(projPath, proj.WriteToString());
        }
    }
}
#endif