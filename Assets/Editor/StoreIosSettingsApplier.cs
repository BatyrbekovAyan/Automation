#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// iOS store-submission Player Settings (2026-09-01, App Store audit §06) — one
/// idempotent menu action so the values are code-reviewable and survive machine
/// switches instead of living only in a hand-edited ProjectSettings.asset:
///
///   • Target Device → iPhone Only. It was 2 (Universal) by accident: App Review runs
///     Universal apps on an iPad Air 13", where the 1080×1920 portrait phone UI would
///     be judged (classic 4.0/2.1 rejection driver), and App Store Connect then demands
///     mandatory 13" iPad screenshots nobody planned. iPhone-only apps still run
///     letterboxed on iPad with zero extra assets.
///
///   • Camera usage description → the real RU purpose string. Must stay IDENTICAL to
///     ProjectSettings/NativeCamera.json's CameraUsageDescription — the NativeCamera
///     post-build overwrites the plist with its own setting, so the two sources must
///     agree (5.1.1(i): purpose-specific wording; a RU-only app must not surface an
///     English template prompt).
///
///   • Microphone usage description → EMPTY. Nothing records audio: the composer's mic
///     button is force-hidden and NativeCamera only takes stills — the old string
///     («Required to record Voice Messages») claimed a feature the app does not have
///     (5.1.1 metadata mismatch). Empty = Unity omits the plist key entirely;
///     NativeCamera.json's MicrophoneUsageDescription is likewise "". Restore BOTH when
///     voice messages actually ship.
/// </summary>
public static class StoreIosSettingsApplier
{
    // Single source for the camera purpose string — mirror any edit into
    // ProjectSettings/NativeCamera.json (see the class doc).
    private const string CameraPurposeRu =
        "Камера нужна, чтобы сфотографировать товар или документ и отправить в чат.";

    [MenuItem("Tools/Store Compliance/Apply iOS Store Settings")]
    public static void Apply()
    {
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
        PlayerSettings.iOS.cameraUsageDescription = CameraPurposeRu;
        PlayerSettings.iOS.microphoneUsageDescription = "";
        AssetDatabase.SaveAssets();
        Debug.Log("[StoreIosSettingsApplier] Applied: iPhone-only, RU camera purpose, empty mic string (saved).");
    }
}
#endif
