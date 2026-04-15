using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the status pill on a bot card by observing the legacy
/// <see cref="Bot.Status"/> TMP.
///
/// Bot.cs writes English strings and a color (green/blue/red) into that TMP
/// as its state changes. This component mirrors the color to a localized
/// Russian pill label and a rounded background, leaving Bot.cs untouched.
///
/// The source TMP should be disabled (rendered hidden) so it acts purely
/// as a data channel.
/// </summary>
public class BotStatusPill : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TextMeshProUGUI pillLabel;
    [SerializeField] private TextMeshProUGUI statusSource;

    // Source colors written by Bot.cs — must match the literals there exactly.
    private static readonly Color SourceGreen = new(0f, 1f, 0f);
    private static readonly Color SourceBlue  = new(0f, 0.6980392f, 1f);

    // Pill palette (matches mockup).
    private static readonly Color BgActive     = Hex("#E8F8EE");
    private static readonly Color FgActive     = Hex("#34C759");
    private static readonly Color BgConnecting = Hex("#E3F2FF");
    private static readonly Color FgConnecting = Hex("#007AFF");
    private static readonly Color BgInactive   = Hex("#FFECEC");
    private static readonly Color FgInactive   = Hex("#FF3B30");

    private const string LabelActive     = "Активен";
    private const string LabelConnecting = "Подключение";
    private const string LabelInactive   = "Неактивен";

    private const float ColorEpsilon = 0.01f;

    // Sentinel so the first LateUpdate always repaints.
    private Color lastObservedColor = new(-1f, -1f, -1f);

    private void OnEnable() => lastObservedColor = new Color(-1f, -1f, -1f);

    private void LateUpdate()
    {
        if (statusSource == null) return;

        var current = statusSource.color;
        if (ColorsClose(current, lastObservedColor)) return;

        lastObservedColor = current;
        Apply(current);
    }

    private void Apply(Color sourceColor)
    {
        if (ColorsClose(sourceColor, SourceGreen))
            Set(LabelActive, BgActive, FgActive);
        else if (ColorsClose(sourceColor, SourceBlue))
            Set(LabelConnecting, BgConnecting, FgConnecting);
        else
            Set(LabelInactive, BgInactive, FgInactive);
    }

    private void Set(string label, Color bgColor, Color fgColor)
    {
        if (pillLabel != null)
        {
            pillLabel.text = label;
            pillLabel.color = fgColor;
        }
        if (background != null)
            background.color = bgColor;
    }

    private static bool ColorsClose(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < ColorEpsilon &&
        Mathf.Abs(a.g - b.g) < ColorEpsilon &&
        Mathf.Abs(a.b - b.b) < ColorEpsilon;

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
