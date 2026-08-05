using System;
using UnityEngine;

/// <summary>
/// Runtime theme facade. <c>Theme.Color(role)</c> resolves a semantic role
/// against the active <see cref="ThemeAsset"/>; <c>Theme.SetMode</c> switches
/// light/dark, persists the choice via <see cref="ThemePrefs"/> and raises
/// <see cref="Changed"/> so every live <see cref="ThemedColor"/> re-applies.
///
/// Lives in the runtime assembly because runtime stampers (ChatItemView,
/// BotStatusPill, DashboardStatusInfo, MessageItemView …) must be able to read
/// it at bind time — an editor-assembly placement would make that impossible.
///
/// Assets load lazily from Resources/Theme/{Theme_Light,Theme_Dark}. If an
/// asset is missing (fresh checkout before the builder ran, or unit tests),
/// the facade falls back to code defaults — which are today's light values —
/// so it NEVER null-refs and never silently paints magenta.
/// </summary>
public static class Theme
{
    public const string LightResourcePath = "Theme/Theme_Light";
    public const string DarkResourcePath  = "Theme/Theme_Dark";

    /// <summary>Raised after the active theme changes. Subscribers re-pull colours.</summary>
    public static event Action Changed;

    // Test seam: inject in-memory assets so EditMode tests never touch Resources.
    private static ThemeAsset _lightOverride, _darkOverride;
    private static ThemeAsset _light, _dark;
    private static ThemeMode? _mode; // session cache over ThemePrefs

    public static ThemeMode Mode => _mode ??= ThemePrefs.Mode;

    public static ThemeAsset Active => Mode == ThemeMode.Dark ? Dark : Light;

    public static ThemeAsset Light => _lightOverride != null ? _lightOverride
        : _light != null ? _light
        : _light = LoadOrDefault(LightResourcePath);

    public static ThemeAsset Dark => _darkOverride != null ? _darkOverride
        : _dark != null ? _dark
        : _dark = LoadOrDefault(DarkResourcePath);

    /// <summary>The active theme's colour for <paramref name="role"/>.</summary>
    public static Color Color(ThemeRole role) => Active.Resolve(role);

    /// <summary>
    /// Switch theme. Persists (unless <paramref name="persist"/> is false) and
    /// raises <see cref="Changed"/> only on an actual change.
    /// </summary>
    public static void SetMode(ThemeMode mode, bool persist = true)
    {
        if (Mode == mode) return;
        _mode = mode;
        if (persist) ThemePrefs.Mode = mode;
        Changed?.Invoke();
    }

    /// <summary>
    /// Colours that carry IDENTITY or MEANING and therefore never change with
    /// the theme. Keep these out of ThemeAsset on purpose: a palette edit must
    /// not be able to repaint them.
    /// </summary>
    public static class Fixed
    {
        /// <summary>WhatsApp channel identity.</summary>
        public static readonly Color WhatsAppGreen = FromHex("#25D366");

        /// <summary>Telegram channel identity — single-sourced from ChannelAccent.</summary>
        public static readonly Color TelegramBlue = ChannelAccent.TelegramBlue;

        /// <summary>
        /// Activation-switch ON. «Бот работает» must always read as the same
        /// green, in every theme (BotCardFooterBuilder.TrackOnColor today).
        /// </summary>
        public static readonly Color SwitchOnGreen = FromHex("#34C759");

        /// <summary>
        /// The WhatsApp-channel unread accent: chat-row unread pill fill and the
        /// green timestamp tint. Fixed rather than themeable because it is one end
        /// of the CHANNEL accent pair — <see cref="ChannelAccent.Resolve"/> maps it
        /// to Telegram blue on that channel, so theming it would make the two
        /// channels disagree about what "unread" looks like.
        ///
        /// Darkened from #26B25A during the phase-3 redesign: the old value put
        /// white badge text at 2.76:1, well under the 4.5:1 floor. #17803F clears
        /// it at 5.00:1 while staying in the same green family. (#1F8F4A was tried
        /// first and measured 4.13:1 — still failing.) Needs no dark sibling: it
        /// still clears the 3:1 fill floor on the dark row surface at 3.42:1.
        /// </summary>
        public static readonly Color UnreadAccentWhatsApp = FromHex("#17803F");
    }

    private static ThemeAsset LoadOrDefault(string path)
    {
        var asset = Resources.Load<ThemeAsset>(path);
        if (asset != null) return asset;
        // Code defaults are today's light look — safe on any missing asset.
        return ScriptableObject.CreateInstance<ThemeAsset>();
    }

    private static Color FromHex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : UnityEngine.Color.magenta;

    // ---------------------------------------------------------------- tests
    /// <summary>Inject assets + mode for EditMode tests. Pass nulls to clear.</summary>
    public static void OverrideForTests(ThemeAsset light, ThemeAsset dark, ThemeMode? mode)
    {
        _lightOverride = light;
        _darkOverride = dark;
        _mode = mode;
    }

    /// <summary>Drop caches/overrides so the next access re-reads prefs + Resources.</summary>
    public static void ResetForTests()
    {
        _lightOverride = _darkOverride = null;
        _light = _dark = null;
        _mode = null;
        Changed = null;
    }
}
