using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Foundation invariants for the theme layer. These pin the contracts the
/// restyle relies on BEFORE any scene object is bound:
///  - every ThemeRole resolves on a fresh asset (no magenta sentinel escapes),
///  - code defaults == today's app palette (so wiring is provably a no-op),
///  - fixed identity colours are byte-exact and outside the asset,
///  - prefs persist via the injectable seam and default to Light,
///  - SetMode fires Changed exactly once per actual change,
///  - ThemedColor applies on enable, re-applies on switch, preserves the
///    hand-tuned alpha, and unsubscribes on disable.
/// </summary>
public class ThemeFoundationTests
{
    private readonly List<UnityEngine.Object> _cleanup = new();

    private Func<string, int, int> _origGet;
    private Action<string, int> _origSet;
    private Dictionary<string, int> _store;

    [SetUp]
    public void SetUp()
    {
        _origGet = ThemePrefs.GetInt;
        _origSet = ThemePrefs.SetIntAndSave;
        _store = new Dictionary<string, int>();
        ThemePrefs.GetInt = (key, def) => _store.TryGetValue(key, out var v) ? v : def;
        ThemePrefs.SetIntAndSave = (key, value) => _store[key] = value;
        Theme.ResetForTests();
    }

    [TearDown]
    public void TearDown()
    {
        Theme.ResetForTests();
        ThemePrefs.GetInt = _origGet;
        ThemePrefs.SetIntAndSave = _origSet;
        foreach (var o in _cleanup)
            if (o != null) UnityEngine.Object.DestroyImmediate(o);
        _cleanup.Clear();
    }

    private T Track<T>(T obj) where T : UnityEngine.Object
    {
        _cleanup.Add(obj);
        return obj;
    }

    // ---------------------------------------------------------------- asset

    [Test]
    public void EveryRole_ResolvesOnFreshAsset_NoMagentaSentinel()
    {
        var asset = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        foreach (ThemeRole role in Enum.GetValues(typeof(ThemeRole)))
        {
            var c = asset.Resolve(role);
            Assert.AreNotEqual(Color.magenta, c, $"role {role} is unmapped (magenta sentinel)");
            Assert.AreEqual(1f, c.a, 1e-4f, $"role {role} default should be opaque");
        }
    }

    [Test]
    public void Defaults_MatchTodaysAppPalette()
    {
        // Byte-exact anchors to the CURRENT app, so that binding an element to a
        // token is a provable visual no-op. If one of these fails after an
        // intentional palette flip, update the expected values together with it.
        var t = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        Assert.AreEqual("1B7CEB", ColorUtility.ToHtmlStringRGB(t.accentFill));
        Assert.AreEqual("1A1A2E", ColorUtility.ToHtmlStringRGB(t.inkPrimary));
        Assert.AreEqual("65676B", ColorUtility.ToHtmlStringRGB(t.inkSecondary));
        Assert.AreEqual("F5F2EA", ColorUtility.ToHtmlStringRGB(t.chatWallpaper));
        Assert.AreEqual("C5EEB6", ColorUtility.ToHtmlStringRGB(t.bubbleOutgoing));
        Assert.AreEqual("34C759", ColorUtility.ToHtmlStringRGB(t.statusOrderCollected));
        Assert.AreEqual("F57C00", ColorUtility.ToHtmlStringRGB(t.statusOwnerNeeded));
        Assert.AreEqual("007AFF", ColorUtility.ToHtmlStringRGB(t.statusInDialog));
    }

    // ---------------------------------------------------------------- fixed

    [Test]
    public void FixedColours_AreByteExact_AndSingleSourced()
    {
        Assert.AreEqual("25D366", ColorUtility.ToHtmlStringRGB(Theme.Fixed.WhatsAppGreen));
        Assert.AreEqual("34C759", ColorUtility.ToHtmlStringRGB(Theme.Fixed.SwitchOnGreen));
        // Telegram blue must be the SAME value ChannelAccent already uses.
        Assert.AreEqual(ChannelAccent.TelegramBlue, Theme.Fixed.TelegramBlue);
        Assert.AreEqual("2AABEE", ColorUtility.ToHtmlStringRGB(Theme.Fixed.TelegramBlue));
    }

    // ---------------------------------------------------------------- prefs

    [Test]
    public void Prefs_DefaultToLight_AndPersistDark()
    {
        Assert.AreEqual(ThemeMode.Light, ThemePrefs.Mode, "fresh install must be today's look");
        ThemePrefs.Mode = ThemeMode.Dark;
        Assert.AreEqual((int)ThemeMode.Dark, _store[ThemePrefs.ModeKey]);
        Assert.AreEqual(ThemeMode.Dark, ThemePrefs.Mode);
    }

    [Test]
    public void SetMode_Persists_AndFiresChangedOncePerActualChange()
    {
        var light = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        var dark = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        Theme.OverrideForTests(light, dark, ThemeMode.Light);

        int fired = 0;
        Theme.Changed += () => fired++;

        Theme.SetMode(ThemeMode.Light);              // no-op: already light
        Assert.AreEqual(0, fired, "no change → no event");

        Theme.SetMode(ThemeMode.Dark);
        Assert.AreEqual(1, fired);
        Assert.AreEqual(ThemeMode.Dark, ThemePrefs.Mode, "choice must persist");
        Assert.AreSame(dark, Theme.Active);

        Theme.SetMode(ThemeMode.Light);
        Assert.AreEqual(2, fired);
        Assert.AreSame(light, Theme.Active);
    }

    [Test]
    public void MissingAssets_FallBackToCodeDefaults_NeverNull()
    {
        // No overrides, and Resources/Theme may not exist yet in a fresh checkout:
        // the facade must still hand back a usable asset with today's values.
        Theme.ResetForTests();
        ThemePrefs.GetInt = (key, def) => def; // fresh install
        Assert.IsNotNull(Theme.Active);
        Assert.AreEqual("1B7CEB", ColorUtility.ToHtmlStringRGB(Theme.Color(ThemeRole.AccentFill)));
    }

    // ---------------------------------------------------------------- binding
    //
    // EditMode never dispatches Awake/OnEnable/OnDisable to plain MonoBehaviours,
    // so the tests drive the lifecycle explicitly via reflection. The alternative
    // — [ExecuteAlways] on ThemedColor — is deliberately rejected: it would
    // repaint bound objects at scene-OPEN time in the Editor and dirty
    // Main.unity on every load, which the additive-restyle contract forbids.

    private static void Lifecycle(ThemedColor bind, string method) =>
        typeof(ThemedColor)
            .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(bind, null);

    private (GameObject go, Image img, ThemedColor bind) MakeBoundImage(ThemeRole role, float alpha)
    {
        var go = Track(new GameObject("themed"));
        var img = go.AddComponent<Image>();
        var c = img.color; c.a = alpha; img.color = c;
        var bind = go.AddComponent<ThemedColor>();
        bind.Configure(role, img);
        return (go, img, bind);
    }

    [Test]
    public void ThemedColor_AppliesOnEnable_AndPreservesAuthoredAlpha()
    {
        var light = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        light.accentFill = new Color(0.1f, 0.2f, 0.3f, 1f);
        var dark = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        Theme.OverrideForTests(light, dark, ThemeMode.Light);

        var (_, img, bind) = MakeBoundImage(ThemeRole.AccentFill, alpha: 0.5f);
        Lifecycle(bind, "OnEnable");

        Assert.AreEqual(0.1f, img.color.r, 1e-4f);
        Assert.AreEqual(0.2f, img.color.g, 1e-4f);
        Assert.AreEqual(0.3f, img.color.b, 1e-4f);
        Assert.AreEqual(0.5f, img.color.a, 1e-4f, "hand-tuned alpha must survive");
    }

    [Test]
    public void ThemedColor_ReappliesOnThemeSwitch_AndStopsWhenDisabled()
    {
        var light = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        light.surface = new Color(1f, 1f, 1f, 1f);
        var dark = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        dark.surface = new Color(0f, 0f, 0f, 1f);
        Theme.OverrideForTests(light, dark, ThemeMode.Light);

        var (_, img, bind) = MakeBoundImage(ThemeRole.Surface, alpha: 1f);
        Lifecycle(bind, "OnEnable");
        Assert.AreEqual(1f, img.color.r, 1e-4f, "light surface applied on enable");

        Theme.SetMode(ThemeMode.Dark);
        Assert.AreEqual(0f, img.color.r, 1e-4f, "switch must repaint live bindings");

        Lifecycle(bind, "OnDisable");
        Theme.SetMode(ThemeMode.Light);
        Assert.AreEqual(0f, img.color.r, 1e-4f, "disabled binding must not react (unsubscribed)");
    }
}
