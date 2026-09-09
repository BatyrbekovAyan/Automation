using System;
using System.IO;
using NUnit.Framework;
using Unity.Android.Types;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Pins the native debug-symbol decision behind the Google Play build (2026-09-08).
///
/// Build 1 reached Play Console with `debugSymbolLevel 'none'` in its Gradle project, so the
/// console warned «no debug symbols» and every native crash in Android vitals would have stayed
/// a bare address. The setting is NOT in ProjectSettings.asset: it lives in
/// Library/EditorUserBuildSettings.asset (per machine, like App Bundle), so a value ticked in one
/// Editor's inspector is worth nothing on the next machine or in a headless build —
/// <see cref="StoreAndroidSettingsApplier"/> re-asserts it on every Apply. These tests hold the
/// choice together across sessions:
///
///   • SYMBOL TABLE — not None (unsymbolicated vitals) and not Full (DWARF sections add tens of
///     MB per ABI to the .aab and only a local debugger reads them);
///   • packed INTO the bundle (BUNDLE-METADATA/com.android.tools.build.debugsymbols/), so Play
///     picks the symbols up with the upload and there is no separate symbols.zip to forget — which
///     only works for an App Bundle build, hence the applier's buildAppBundle pin must land first;
///   • the pin reaches the LIVE build settings through the Unity 6 API
///     (UserBuildSettings.DebugSymbols), never the deprecated androidCreateSymbols;
///   • the headless «Applied:» line reports it, so a build log proves the value.
/// </summary>
public class AndroidDebugSymbolsPremiseTests
{
    private static string ApplierSource =>
        File.ReadAllText(Path.Combine(Application.dataPath, "Editor/StoreAndroidSettingsApplier.cs"));

    [Test]
    public void Level_is_the_symbol_table_not_none_nor_full()
        => Assert.AreEqual(DebugSymbolLevel.SymbolTable, StoreAndroidSettingsApplier.SymbolLevel,
            "None leaves Android vitals unsymbolicated (build 1); Full adds DWARF the store never reads.");

    [Test]
    public void Format_packs_the_symbols_into_the_bundle()
        => Assert.AreEqual(DebugSymbolFormat.IncludeInBundle, StoreAndroidSettingsApplier.SymbolFormat,
            "A separate symbols.zip is an upload step the owner can forget; in-bundle rides the .aab itself.");

    [Test]
    public void ApplyDebugSymbols_writes_the_pins_into_the_live_build_settings()
    {
        DebugSymbolLevel level0 = UserBuildSettings.DebugSymbols.level;
        DebugSymbolFormat format0 = UserBuildSettings.DebugSymbols.format;
        try
        {
            // Build 1's state: nothing pinned, Unity's own defaults.
            UserBuildSettings.DebugSymbols.level = DebugSymbolLevel.None;
            UserBuildSettings.DebugSymbols.format = DebugSymbolFormat.LegacyExtensions;

            StoreAndroidSettingsApplier.ApplyDebugSymbols();

            Assert.AreEqual(DebugSymbolLevel.SymbolTable, UserBuildSettings.DebugSymbols.level);
            Assert.AreEqual(DebugSymbolFormat.IncludeInBundle, UserBuildSettings.DebugSymbols.format);
        }
        finally
        {
            UserBuildSettings.DebugSymbols.level = level0;
            UserBuildSettings.DebugSymbols.format = format0;
        }
    }

    [Test]
    public void Apply_pins_the_bundle_first_then_the_symbols_and_reports_them()
    {
        string source = ApplierSource;

        int bundle = source.IndexOf("EditorUserBuildSettings.buildAppBundle = true;", StringComparison.Ordinal);
        int symbols = source.IndexOf("ApplyDebugSymbols();", StringComparison.Ordinal);
        Assert.That(bundle, Is.GreaterThanOrEqualTo(0), "Apply() must keep pinning App Bundle ON");
        Assert.That(symbols, Is.GreaterThan(bundle),
            "IncludeInBundle only exists for an App Bundle build — pin the bundle first, then the symbols");

        int applied = source.IndexOf("[StoreAndroidSettingsApplier] Applied:", StringComparison.Ordinal);
        Assert.That(applied, Is.GreaterThanOrEqualTo(0), "the existing «Applied:» line must stay");
        int saved = source.IndexOf("(saved).", applied, StringComparison.Ordinal);
        Assert.That(saved, Is.GreaterThan(applied));
        int report = source.IndexOf("debugSymbols=", StringComparison.Ordinal);
        Assert.That(report, Is.InRange(applied, saved),
            "the «Applied:» line must report the symbol pin so a headless build log proves the value");

        Assert.That(source, Does.Not.Contain("androidCreateSymbols"),
            "deprecated in Unity 6 — the pin goes through UserBuildSettings.DebugSymbols");
    }
}
