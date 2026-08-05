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
    public void Defaults_MatchTheShippedLightPalette()
    {
        // Byte-exact anchors to «Чернильный» on the «Петроль» ground — the
        // owner-chosen palette, transcribed from the verified generator dump.
        // Code defaults MUST mirror ThemeAssetsBuilder.SeedLight, so a drift
        // between them fails here rather than shipping two different lights.
        var t = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        Assert.AreEqual("F4F8F8", ColorUtility.ToHtmlStringRGB(t.background));
        Assert.AreEqual("243A7A", ColorUtility.ToHtmlStringRGB(t.accentFill));
        Assert.AreEqual("08181B", ColorUtility.ToHtmlStringRGB(t.inkPrimary));
        Assert.AreEqual("4C6265", ColorUtility.ToHtmlStringRGB(t.inkSecondary));
        Assert.AreEqual("E3EDED", ColorUtility.ToHtmlStringRGB(t.hairline));
        // The doodle wallpaper is a locked authored asset — the palette flip
        // must NOT move it (the generator's mock-only darkening is rejected).
        Assert.AreEqual("F5F2EA", ColorUtility.ToHtmlStringRGB(t.chatWallpaper));
    }

    [Test]
    public void LightAndDark_DifferOnEveryStructuralRole()
    {
        // A theme switch is only meaningful if the grounds and inks actually move.
        Theme.ResetForTests();
        ThemePrefs.GetInt = (key, def) => def;
        var light = Theme.Light;
        var dark = Theme.Dark;
        foreach (var role in new[] { ThemeRole.Background, ThemeRole.Surface, ThemeRole.Hairline,
                                     ThemeRole.InkPrimary, ThemeRole.InkSecondary, ThemeRole.AccentFill })
            Assert.AreNotEqual(ColorUtility.ToHtmlStringRGB(light.Resolve(role)),
                               ColorUtility.ToHtmlStringRGB(dark.Resolve(role)),
                               $"{role} must differ between light and dark");
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
        // The chat-row unread accent. Darkened in phase 3 so white badge text
        // clears 4.5:1 (was #26B25A at 2.76:1).
        Assert.AreEqual("17803F", ColorUtility.ToHtmlStringRGB(Theme.Fixed.UnreadAccentWhatsApp));
    }

    [Test]
    public void UnreadAccent_IsFixed_NotThemeable_AndSurvivesChannelSwap()
    {
        // It is one end of the CHANNEL accent pair: WhatsApp keeps it byte-identical,
        // Telegram maps it to brand blue. Theming it would make the two channels
        // disagree about what "unread" looks like.
        var wa = ChannelAccent.Resolve(ChatChannel.WhatsApp, Theme.Fixed.UnreadAccentWhatsApp);
        Assert.AreEqual(Theme.Fixed.UnreadAccentWhatsApp, wa, "WhatsApp must pass through unchanged");

        var tg = ChannelAccent.Resolve(ChatChannel.Telegram, Theme.Fixed.UnreadAccentWhatsApp);
        Assert.AreEqual("2AABEE", ColorUtility.ToHtmlStringRGB(tg));

        // Same value under either theme — it is a constant, not a token.
        var light = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        var dark = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        Theme.OverrideForTests(light, dark, ThemeMode.Light);
        var underLight = Theme.Fixed.UnreadAccentWhatsApp;
        Theme.SetMode(ThemeMode.Dark);
        Assert.AreEqual(underLight, Theme.Fixed.UnreadAccentWhatsApp);
    }

    [Test]
    public void ReadTimeColour_ResolvesToInkSecondary_AndFollowsTheTheme()
    {
        // ChatItemView's read-state timestamp now reads InkSecondary instead of a
        // local #666666 constant. Light must still be exactly #666666 (no-op today),
        // and the dark theme must actually move it.
        var light = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        var dark = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        dark.inkSecondary = new Color(0.6f, 0.65f, 0.72f, 1f);
        Theme.OverrideForTests(light, dark, ThemeMode.Light);

        Assert.AreEqual("4C6265", ColorUtility.ToHtmlStringRGB(Theme.Color(ThemeRole.InkSecondary)),
            "light read-time is the «Петроль» secondary ink");

        Theme.SetMode(ThemeMode.Dark);
        Assert.AreNotEqual("4C6265", ColorUtility.ToHtmlStringRGB(Theme.Color(ThemeRole.InkSecondary)));
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
        // the facade must still hand back a usable asset rather than null.
        //
        // Asserted against the CODE DEFAULT rather than a hard-coded hex. Those
        // defaults mirror ThemeAssetsBuilder.SeedLight (pinned by
        // Defaults_MatchTheShippedLightPalette), so this stays true whichever
        // branch runs — and, unlike a literal, it does not have to be rewritten
        // every time the palette is deliberately flipped.
        Theme.ResetForTests();
        ThemePrefs.GetInt = (key, def) => def; // fresh install
        Assert.IsNotNull(Theme.Active);

        var codeDefault = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        Assert.AreEqual(ColorUtility.ToHtmlStringRGB(codeDefault.accentFill),
                        ColorUtility.ToHtmlStringRGB(Theme.Color(ThemeRole.AccentFill)));
    }

    [Test]
    public void ToggleContract_RestoringPersistedStateDoesNotRewriteIt()
    {
        // The Profile row restores its position from the persisted mode on Awake.
        // If that restore went through the normal onValueChanged path it would call
        // SetMode and write the value straight back — harmless when equal, but it
        // would also fire Changed and repaint every binding for nothing. The page
        // uses SetIsOnQuiet; this pins the underlying invariant it relies on.
        var light = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        var dark = Track(ScriptableObject.CreateInstance<ThemeAsset>());
        Theme.OverrideForTests(light, dark, ThemeMode.Dark);

        int fired = 0;
        Theme.Changed += () => fired++;

        Theme.SetMode(ThemeMode.Dark);   // what a re-assert of the persisted value looks like
        Assert.AreEqual(0, fired, "re-asserting the current mode must not repaint");

        Theme.SetMode(ThemeMode.Light);
        Assert.AreEqual(1, fired);
        Assert.AreEqual(ThemeMode.Light, ThemePrefs.Mode, "the flip must persist for next launch");
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
