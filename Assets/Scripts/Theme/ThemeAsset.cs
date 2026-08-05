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
    public Color background   = Hex("#F4F8F8");
    public Color surface      = Hex("#FFFFFF");
    public Color hairline     = Hex("#E3EDED");
    public Color border       = Hex("#C4D6D7");
    public Color inputBorder  = Hex("#6F9B9D");

    [Header("Ink")]
    public Color inkPrimary   = Hex("#08181B");
    public Color inkSecondary = Hex("#4C6265");
    public Color inkTertiary  = Hex("#64797C");

    [Header("Accent")]
    public Color accentFill   = Hex("#243A7A");
    public Color accentText   = Hex("#243A7A");
    public Color accentOnFill = Hex("#FFFFFF");

    [Header("Controls")]
    public Color switchOffTrack = Hex("#6F9B9D");

    [Header("Dashboard statuses (FG dots/pills — see DashboardStatusInfo)")]
    public Color statusOrderCollected = Hex("#3A934C");
    public Color statusOwnerNeeded    = Hex("#E46602");
    public Color statusInDialog       = Hex("#3B72E6");
    public Color statusClientSilent   = Hex("#8E8E93");
    public Color statusQuestionClosed = Hex("#65676B");

    [Header("Semantic moments")]
    public Color destructive = Hex("#A01B12");
    public Color positiveBg  = Hex("#E6F6EE");
    public Color positiveInk = Hex("#0A6B3E");

    [Header("Chat thread")]
    public Color chatWallpaper  = Hex("#F5F2EA");
    public Color bubbleIncoming = Hex("#FFFFFF");
    public Color bubbleOutgoing = Hex("#E0E3EC");
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
