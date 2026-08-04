using UnityEngine;

/// <summary>
/// One theme's complete colour token set, as a ScriptableObject asset.
/// Two instances live in <c>Assets/Resources/Theme/</c> (Theme_Light / Theme_Dark)
/// so the runtime facade can <c>Resources.Load</c> them; they are created and
/// re-seeded by <c>Tools/Theme/Create Or Update Theme Assets</c> — which touches
/// ONLY these .asset files, never the scene. The scene keeps every hand-tuned
/// value; colours reach it additively via <see cref="ThemedColor"/> bindings.
///
/// Field defaults below are TODAY'S app values (light), so a freshly created
/// instance — or a test's <c>CreateInstance</c> — describes the app as it looks
/// now. The palette flip to «Петроль»/«Чернильный» later is a data edit here,
/// not a code change. v1 scope is colours only; radii/elevation/fonts can be
/// added as fields later without breaking existing assets.
/// </summary>
[CreateAssetMenu(fileName = "Theme", menuName = "Automation/Theme Asset")]
public class ThemeAsset : ScriptableObject
{
    [Header("Surfaces")]
    public Color background   = Hex("#F0F2F5");
    public Color surface      = Hex("#FFFFFF");
    public Color hairline     = Hex("#E4E6EB");
    public Color border       = Hex("#E1E5EC");
    public Color inputBorder  = Hex("#C6CBD3");

    [Header("Ink")]
    public Color inkPrimary   = Hex("#1A1A2E");
    public Color inkSecondary = Hex("#65676B");
    public Color inkTertiary  = Hex("#8E8E93");

    [Header("Accent")]
    public Color accentFill   = Hex("#1B7CEB");
    public Color accentText   = Hex("#1B7CEB");
    public Color accentOnFill = Hex("#FFFFFF");

    [Header("Controls")]
    public Color switchOffTrack = Hex("#E9E9EA"); // BotCardFooterBuilder.TrackOffColor

    [Header("Dashboard statuses (FG dots/pills — see DashboardStatusInfo)")]
    public Color statusOrderCollected = Hex("#34C759");
    public Color statusOwnerNeeded    = Hex("#F57C00");
    public Color statusInDialog       = Hex("#007AFF");
    public Color statusClientSilent   = Hex("#8E8E93");
    public Color statusQuestionClosed = Hex("#65676B");

    [Header("Semantic moments")]
    public Color destructive = Hex("#E53935");
    public Color positiveBg  = Hex("#E8F8EE");
    public Color positiveInk = Hex("#206A2C");

    [Header("Chat thread")]
    public Color chatWallpaper  = Hex("#F5F2EA"); // doodle paper (dark theme needs its own wallpaper asset later)
    public Color bubbleIncoming = Hex("#FFFFFF");
    public Color bubbleOutgoing = Hex("#C5EEB6");

    /// <summary>Resolve a semantic role to this theme's colour.</summary>
    public Color Resolve(ThemeRole role) => role switch
    {
        ThemeRole.Background           => background,
        ThemeRole.Surface              => surface,
        ThemeRole.Hairline             => hairline,
        ThemeRole.Border               => border,
        ThemeRole.InputBorder          => inputBorder,
        ThemeRole.InkPrimary           => inkPrimary,
        ThemeRole.InkSecondary         => inkSecondary,
        ThemeRole.InkTertiary          => inkTertiary,
        ThemeRole.AccentFill           => accentFill,
        ThemeRole.AccentText           => accentText,
        ThemeRole.AccentOnFill         => accentOnFill,
        ThemeRole.SwitchOffTrack       => switchOffTrack,
        ThemeRole.StatusOrderCollected => statusOrderCollected,
        ThemeRole.StatusOwnerNeeded    => statusOwnerNeeded,
        ThemeRole.StatusInDialog       => statusInDialog,
        ThemeRole.StatusClientSilent   => statusClientSilent,
        ThemeRole.StatusQuestionClosed => statusQuestionClosed,
        ThemeRole.Destructive          => destructive,
        ThemeRole.PositiveBg           => positiveBg,
        ThemeRole.PositiveInk          => positiveInk,
        ThemeRole.ChatWallpaper        => chatWallpaper,
        ThemeRole.BubbleIncoming       => bubbleIncoming,
        ThemeRole.BubbleOutgoing       => bubbleOutgoing,
        _                              => Color.magenta, // unmapped role — loud sentinel, tests assert it never happens
    };

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}
