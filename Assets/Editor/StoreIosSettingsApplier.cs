#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// iOS store-submission Player Settings (2026-09-01, App Store audit §06) — one
/// idempotent menu action so the values are code-reviewable and survive machine
/// switches instead of living only in a hand-edited ProjectSettings.asset. Headless
/// entry: <see cref="ApplyHeadless"/>.
///
///   • Target Device → iPhone Only. It was 2 (Universal) by accident: App Review runs
///     Universal apps on an iPad Air 13", where the 1080×1920 portrait phone UI would
///     be judged (classic 4.0/2.1 rejection driver), and App Store Connect then demands
///     mandatory 13" iPad screenshots nobody planned. iPhone-only apps still run
///     letterboxed on iPad with zero extra assets.
///
///   • The four purpose strings — camera, microphone, photo library, photo additions —
///     live HERE as constants (the single source of truth, 2026-09-06). Apply() copies
///     the camera/microphone ones into Player Settings and regenerates the three
///     yasirkula settings files (ProjectSettings/NativeCamera.json, NativeGallery.json,
///     NativeShare.json) from them, and <c>FixIOSBuildSettings</c> re-stamps all four
///     keys into Info.plist LAST. Two mechanisms made the 2026-09-06 upload ship English
///     template prompts despite correct-looking settings: (1) yasirkula's three
///     post-processors each write their own defaults — NativeShare had NO settings file
///     and its single string lands on BOTH photo keys, overwriting NativeGallery's RU
///     text; (2) an Append build MERGES the previous Info.plist, so a key written once
///     (the August NativeCamera microphone default) survives every later build even
///     after its source went empty. Neither can win against a post-process stamp.
///
///   • Microphone string is LOAD-BEARING even though nothing records audio:
///     Assets/Plugins/iOS/EnableIOSAudio.m switches the audio session to
///     AVAudioSessionCategoryPlayAndRecord when a voice message plays with the phone at
///     the ear (receiver routing needs a record-capable category), iOS raises the
///     microphone prompt for that, and without the key it terminates the app. The old
///     «empty = key omitted» decision would have crashed at-the-ear playback on the first
///     non-Append build. Keep it truthful: it explains the routing and says no recording.
///
///   • Photo-additions string: the share sheet (NativeShare in MessageItemView) offers the
///     system «Сохранить изображение» action, which saves on the app's behalf and crashes
///     without NSPhotoLibraryAddUsageDescription.
///
/// Pinned by IosPurposeStringsPremiseTests (constants RU, files mirror them, Player
/// Settings match, native premises still hold, post-process stamps all four).
/// </summary>
public static class StoreIosSettingsApplier
{
    public const string CameraPurposeRu =
        "Камера нужна, чтобы сфотографировать товар или документ и отправить в чат.";

    public const string MicrophonePurposeRu =
        "Микрофон нужен системе, чтобы воспроизводить голосовые через разговорный динамик, " +
        "когда телефон у уха. Приложение звук не записывает.";

    public const string PhotoLibraryPurposeRu =
        "Доступ к фото нужен, чтобы прикрепить изображение или прайс-лист к сообщению.";

    public const string PhotoLibraryAddPurposeRu =
        "Нужно, чтобы сохранять фото из чата в галерею, если вы выберете «Сохранить изображение».";

    private const string NativeCameraSettingsPath = "ProjectSettings/NativeCamera.json";
    private const string NativeGallerySettingsPath = "ProjectSettings/NativeGallery.json";
    private const string NativeShareSettingsPath = "ProjectSettings/NativeShare.json";

    // Field names are the plugins' own (JsonUtility round-trips by name): see
    // NCPostProcessBuild.Settings, NGPostProcessBuild.Settings, NSPostProcessBuild.Settings.
    // Every field is listed so a missing one can never fall back to the plugin default.
    [System.Serializable]
    private class NativeCameraSettings
    {
        public bool AutomatedSetup = true;
        public string CameraUsageDescription = CameraPurposeRu;
        public string MicrophoneUsageDescription = MicrophonePurposeRu;
    }

    [System.Serializable]
    private class NativeGallerySettings
    {
        public bool AutomatedSetup = true;
        public string PhotoLibraryUsageDescription = PhotoLibraryPurposeRu;
        public string PhotoLibraryAdditionsUsageDescription = PhotoLibraryAddPurposeRu;
        public bool DontAskLimitedPhotosPermissionAutomaticallyOnIos14 = true;
    }

    // NativeShare writes its ONE string onto both photo keys — it must stay silent and let
    // NativeGallery's two strings (and the final stamp) own them.
    [System.Serializable]
    private class NativeShareSettings
    {
        public bool AutomatedSetup = true;
        public string PhotoLibraryUsageDescription = "";
    }

    [MenuItem("Tools/Store Compliance/Apply iOS Store Settings")]
    public static void Apply()
    {
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
        PlayerSettings.iOS.cameraUsageDescription = CameraPurposeRu;
        PlayerSettings.iOS.microphoneUsageDescription = MicrophonePurposeRu;
        WritePluginSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[StoreIosSettingsApplier] Applied: iPhone-only, RU camera/microphone purpose strings, " +
                  "NativeCamera/NativeGallery/NativeShare settings regenerated (saved).");
    }

    /// <summary>Batch-mode entry: Unity -batchmode -executeMethod StoreIosSettingsApplier.ApplyHeadless -quit</summary>
    public static void ApplyHeadless() => Apply();

    /// <summary>
    /// Every App Store Connect upload needs a higher CFBundleVersion than the last one;
    /// Unity regenerates Info.plist from Player Settings on each build, so the number must
    /// be bumped HERE (a value typed into Xcode is overwritten by the next Unity build).
    /// </summary>
    [MenuItem("Tools/Store Compliance/Bump iOS Build Number")]
    public static void BumpIosBuildNumber()
    {
        string current = PlayerSettings.iOS.buildNumber;
        if (!int.TryParse(current, out int number))
            throw new System.InvalidOperationException(
                $"[StoreIosSettingsApplier] iOS build number '{current}' is not an integer — set it by hand once.");

        PlayerSettings.iOS.buildNumber = (number + 1).ToString();
        AssetDatabase.SaveAssets();
        Debug.Log($"[StoreIosSettingsApplier] iOS build number {number} -> {number + 1} " +
                  $"(version {PlayerSettings.bundleVersion}).");
    }

    /// <summary>Batch-mode entry: Unity -batchmode -executeMethod StoreIosSettingsApplier.BumpIosBuildNumberHeadless -quit</summary>
    public static void BumpIosBuildNumberHeadless() => BumpIosBuildNumber();

    private static void WritePluginSettings()
    {
        File.WriteAllText(NativeCameraSettingsPath, JsonUtility.ToJson(new NativeCameraSettings(), true));
        File.WriteAllText(NativeGallerySettingsPath, JsonUtility.ToJson(new NativeGallerySettings(), true));
        File.WriteAllText(NativeShareSettingsPath, JsonUtility.ToJson(new NativeShareSettings(), true));
    }
}
#endif
