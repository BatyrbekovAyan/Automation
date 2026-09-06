#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Android store-submission Player Settings (2026-09-05, Google Play track) — the
/// Android twin of <see cref="StoreIosSettingsApplier"/>: one idempotent menu action so
/// the values are code-reviewable and survive machine switches instead of living only in
/// a hand-edited ProjectSettings.asset. Headless entry: <see cref="ApplyHeadless"/>.
///
///   • Target API level → 36 (Android 16). Google Play requires NEW apps to target API 36
///     from 2026-08-31 (updates to existing apps: ≥ 35). The project shipped on
///     «Automatic (highest installed)», which is only 36 on a machine that has the
///     android-36 platform installed — pinning it makes the requirement explicit and
///     machine-independent. Min SDK stays 25.
///
///   • Application category → «productivity» (and the deprecated Is Game flag OFF). Unity
///     6.3 enables «Application Category: Game» for new projects, which writes
///     android:appCategory="game" into the manifest and — per Unity's own docs — exists
///     to exempt GAMES from Android 16 behaviour changes. Choose Reply is a business
///     messaging tool; declaring it a game is a mis-declaration Play can act on.
///
///   • Build App Bundle → ON. Play accepts only .aab uploads for new apps. This lives in
///     EditorUserBuildSettings (Library/, per machine), so the applier re-asserts it.
///
///   • Adaptive launcher icon → the two layers in Assets/Images/Icon_android_{bg,fg}.png,
///     rendered by Tools/icon-lab/appicon/android.js from the SAME concept as the master
///     Assets/Images/Icon.png (foreground = the mark at ×0.63 around the centre so the
///     108dp→72dp mask never clips it; background = the master's gradient stretched to
///     the mask's visible window). Without adaptive layers Android 8+ launchers shrink
///     the square legacy icon inside a white disc. Every adaptive slot (432…81) gets the
///     same pair; Unity downsamples.
///
/// NOT touched here, deliberately: the upload keystore (a credential the owner creates —
/// Play App Signing makes any upload key fine, but generating one silently would leave
/// its password nowhere), target architectures (ARMv7+ARM64 is what Play wants),
/// minification (off — the app reflects into TMP/plugins).
/// </summary>
public static class StoreAndroidSettingsApplier
{
    public const string BackgroundLayerPath = "Assets/Images/Icon_android_bg.png";
    public const string ForegroundLayerPath = "Assets/Images/Icon_android_fg.png";
    public const string AppCategory = "productivity";

    [MenuItem("Tools/Store Compliance/Apply Android Store Settings")]
    public static void Apply()
    {
        var target = NamedBuildTarget.Android;

        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
        if ((int)PlayerSettings.Android.minSdkVersion < 25)
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)25;

#pragma warning disable 618 // androidIsGame is obsolete in favour of appCategory; clear the legacy flag too
        PlayerSettings.Android.androidIsGame = false;
#pragma warning restore 618
        PlayerSettings.Android.appCategory = AppCategory;

        EditorUserBuildSettings.buildAppBundle = true;

        ApplyAdaptiveIcon(target);

        AssetDatabase.SaveAssets();
        Debug.Log($"[StoreAndroidSettingsApplier] Applied: targetSdk={PlayerSettings.Android.targetSdkVersion}, " +
                  $"minSdk={PlayerSettings.Android.minSdkVersion}, appCategory='{PlayerSettings.Android.appCategory}', " +
                  $"buildAppBundle={EditorUserBuildSettings.buildAppBundle}, package={PlayerSettings.GetApplicationIdentifier(target)} (saved).");

        if (!PlayerSettings.Android.useCustomKeystore)
            Debug.LogWarning("[StoreAndroidSettingsApplier] No custom keystore: the build signs with the debug key, " +
                             "which Play rejects for upload. Owner action — create an upload keystore " +
                             "(docs/store/play-console.md) before the first .aab.");
    }

    /// <summary>Batch-mode entry: Unity -batchmode -executeMethod StoreAndroidSettingsApplier.ApplyHeadless -quit</summary>
    public static void ApplyHeadless() => Apply();

    private static void ApplyAdaptiveIcon(NamedBuildTarget target)
    {
        var background = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundLayerPath);
        var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(ForegroundLayerPath);
        if (background == null || foreground == null)
        {
            Debug.LogWarning($"[StoreAndroidSettingsApplier] Adaptive icon layers missing ({BackgroundLayerPath} / " +
                             $"{ForegroundLayerPath}) — render them with `node Tools/icon-lab/appicon/android.js` " +
                             "and re-run. Icon slots left untouched.");
            return;
        }

        PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(target, AndroidPlatformIconKind.Adaptive);
        for (int i = 0; i < icons.Length; i++)
        {
            // Layer order follows the Player Settings inspector for the Adaptive kind:
            // layer 0 = Background, layer 1 = Foreground.
            icons[i].SetTextures(background, foreground);
        }
        PlayerSettings.SetPlatformIcons(target, AndroidPlatformIconKind.Adaptive, icons);
        Debug.Log($"[StoreAndroidSettingsApplier] Adaptive icon: {icons.Length} slots ← bg {Path.GetFileName(BackgroundLayerPath)} + fg {Path.GetFileName(ForegroundLayerPath)}.");
    }
}
#endif
