using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pins the premises behind the four iOS purpose strings (App Store audit, 2026-09-06).
///
/// The shipped Info.plist is assembled by FOUR writers — Unity (Player Settings),
/// NativeCamera, NativeGallery, NativeShare — and then MERGED into the previous
/// Info.plist by an Append build, so a value that looks right in one place can still
/// reach the store wrong: build 1 carried an August English microphone default and
/// NativeShare's «save media» text on both photo keys while every visible setting was
/// Russian. The cure is one source (StoreIosSettingsApplier constants) stamped last by
/// FixIOSBuildSettings; these tests hold the pieces together across sessions:
///
///   • the constants are Russian and not a plugin template;
///   • the three plugin settings files mirror them (NativeShare silent — its single
///     string would land on BOTH photo keys);
///   • Player Settings carry the same camera/microphone text and iPhone-only;
///   • the native premises that make two of the keys MANDATORY still hold — the audio
///     session goes PlayAndRecord for at-the-ear voice playback (microphone prompt ⇒
///     key or crash) and the share sheet offers Save Image (photo-add key or crash);
///   • the post-process stamps all four keys and runs after every plugin.
///
/// If a native premise changes (PlayAndRecord removed, share sheet gone), fail here on
/// purpose: decide whether the key can go, then rewrite the test — never blank a
/// constant first.
/// </summary>
public class IosPurposeStringsPremiseTests
{
    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    private static readonly (string name, string value)[] Constants =
    {
        ("camera", StoreIosSettingsApplier.CameraPurposeRu),
        ("microphone", StoreIosSettingsApplier.MicrophonePurposeRu),
        ("photo library", StoreIosSettingsApplier.PhotoLibraryPurposeRu),
        ("photo additions", StoreIosSettingsApplier.PhotoLibraryAddPurposeRu),
    };

    [System.Serializable]
    private class NativeCameraSettings
    {
        public string CameraUsageDescription;
        public string MicrophoneUsageDescription;
    }

    [System.Serializable]
    private class NativeGallerySettings
    {
        public string PhotoLibraryUsageDescription;
        public string PhotoLibraryAdditionsUsageDescription;
    }

    [System.Serializable]
    private class NativeShareSettings
    {
        public string PhotoLibraryUsageDescription = "<missing>";
    }

    [Test]
    public void Constants_are_Russian_and_not_plugin_templates()
    {
        foreach (var (name, value) in Constants)
        {
            Assert.That(value, Does.Match("[А-Яа-яЁё]"), $"{name}: must be Russian (RU-only UI rule)");
            Assert.That(value, Does.Not.Contain("The app"), $"{name}: yasirkula template text leaked");
            Assert.That(value.Trim(), Is.EqualTo(value).And.Not.Empty, $"{name}: no padding, never empty");
        }
    }

    [Test]
    public void Microphone_string_stays_mandatory_while_playback_can_go_PlayAndRecord()
    {
        string native = File.ReadAllText(Path.Combine(Application.dataPath, "Plugins/iOS/EnableIOSAudio.m"));
        Assert.That(native, Does.Contain("AVAudioSessionCategoryPlayAndRecord"),
            "Premise changed: if the earpiece routing no longer needs a record-capable category, " +
            "the microphone key may become optional — re-decide, then rewrite this test.");
        Assert.That(StoreIosSettingsApplier.MicrophonePurposeRu, Is.Not.Empty,
            "PlayAndRecord raises the microphone prompt; without the key iOS terminates the app");
    }

    [Test]
    public void Photo_additions_string_stays_mandatory_while_the_share_sheet_exists()
    {
        string view = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/UI/MessageItemView.cs"));
        Assert.That(view, Does.Contain("new NativeShare()"),
            "Premise changed: no share sheet means the Save Image action is gone — re-decide the key.");
        Assert.That(StoreIosSettingsApplier.PhotoLibraryAddPurposeRu, Is.Not.Empty,
            "the share sheet's Save Image saves on the app's behalf and needs NSPhotoLibraryAddUsageDescription");
    }

    [Test]
    public void Plugin_settings_files_mirror_the_constants()
    {
        var camera = JsonUtility.FromJson<NativeCameraSettings>(ReadProjectFile("ProjectSettings/NativeCamera.json"));
        Assert.AreEqual(StoreIosSettingsApplier.CameraPurposeRu, camera.CameraUsageDescription, "NativeCamera.json camera");
        Assert.AreEqual(StoreIosSettingsApplier.MicrophonePurposeRu, camera.MicrophoneUsageDescription, "NativeCamera.json microphone");

        var gallery = JsonUtility.FromJson<NativeGallerySettings>(ReadProjectFile("ProjectSettings/NativeGallery.json"));
        Assert.AreEqual(StoreIosSettingsApplier.PhotoLibraryPurposeRu, gallery.PhotoLibraryUsageDescription, "NativeGallery.json photo");
        Assert.AreEqual(StoreIosSettingsApplier.PhotoLibraryAddPurposeRu, gallery.PhotoLibraryAdditionsUsageDescription, "NativeGallery.json additions");

        var share = JsonUtility.FromJson<NativeShareSettings>(ReadProjectFile("ProjectSettings/NativeShare.json"));
        Assert.AreEqual("", share.PhotoLibraryUsageDescription,
            "NativeShare writes its ONE string onto both photo keys — it must stay silent");
    }

    [Test]
    public void PlayerSettings_carry_the_same_strings_and_iPhone_only()
    {
        Assert.AreEqual(StoreIosSettingsApplier.CameraPurposeRu, PlayerSettings.iOS.cameraUsageDescription,
            "run Tools/Store Compliance/Apply iOS Store Settings");
        Assert.AreEqual(StoreIosSettingsApplier.MicrophonePurposeRu, PlayerSettings.iOS.microphoneUsageDescription,
            "run Tools/Store Compliance/Apply iOS Store Settings");
        Assert.AreEqual(iOSTargetDevice.iPhoneOnly, PlayerSettings.iOS.targetDevice, "App Review judges Universal apps on iPad");
    }

    [Test]
    public void PostProcess_stamps_all_four_keys_after_every_plugin()
    {
        string source = File.ReadAllText(Path.Combine(Application.dataPath, "Editor/FixIOSBuildSettings.cs"));
        Assert.That(source, Does.Contain("[PostProcessBuild(1000)]"),
            "must run after NativeGallery (1), NativeShare/NativeCamera (default) and NativeFilePicker (99)");
        Assert.That(source, Does.Contain("EnforcePurposeStrings(plist.root)"), "stamp not wired into the build");
        foreach (string key in new[]
                 {
                     "NSCameraUsageDescription", "NSMicrophoneUsageDescription",
                     "NSPhotoLibraryUsageDescription", "NSPhotoLibraryAddUsageDescription",
                 })
            Assert.That(source, Does.Contain($"\"{key}\""), key);
    }

    private static string ReadProjectFile(string relativePath)
    {
        string path = Path.Combine(ProjectRoot, relativePath);
        Assert.That(File.Exists(path), Is.True, $"{relativePath} missing — run Tools/Store Compliance/Apply iOS Store Settings");
        return File.ReadAllText(path);
    }
}
