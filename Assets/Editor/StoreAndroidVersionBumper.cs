#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Google Play needs a strictly increasing versionCode per upload (kit §1.6). This is the
/// Android twin of «Bump iOS Build Number»: it increments PlayerSettings.Android.bundleVersionCode
/// THROUGH the Editor and saves, because editing ProjectSettings.asset on disk while the Editor is
/// open is overwritten by the Editor's in-memory copy on its next save.
///   Unity -batchmode -nographics -projectPath . -executeMethod StoreAndroidVersionBumper.BumpHeadless -quit
/// </summary>
public static class StoreAndroidVersionBumper
{
    [MenuItem("Tools/Store Compliance/Bump Android Version Code")]
    public static void Bump()
    {
        int from = PlayerSettings.Android.bundleVersionCode;
        PlayerSettings.Android.bundleVersionCode = from + 1;
        AssetDatabase.SaveAssets();
        Debug.Log($"[StoreAndroidVersionBumper] AndroidBundleVersionCode {from} → {from + 1} (bundleVersion {PlayerSettings.bundleVersion})");
    }

    public static void BumpHeadless()
    {
        Bump();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
#endif
