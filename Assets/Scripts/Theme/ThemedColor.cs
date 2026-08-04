using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds one Graphic (Image or TMP text — both derive from Graphic) to a
/// semantic <see cref="ThemeRole"/>. Applies the active theme's colour on
/// enable and re-applies on <see cref="Theme.Changed"/>.
///
/// This is the ADDITIVE seam for the restyle: the component is attached to
/// EXISTING, hand-tuned scene objects (by a SerializedObject wirer or by hand)
/// — never via destroy-and-rebuild, so every manual tweak in the scene
/// survives. Until a binding is attached, an element keeps its authored colour;
/// attach bindings screen-by-screen with token values byte-identical to what is
/// already there, and each wiring step is provably a visual no-op.
///
/// <see cref="preserveAlpha"/> defaults ON because the scene's alphas are
/// hand-tuned (pressed states, scrims, disabled looks): the binding repaints
/// the HUE and keeps the authored alpha unless told otherwise.
/// </summary>
[DisallowMultipleComponent]
public class ThemedColor : MonoBehaviour
{
    [SerializeField] private ThemeRole role = ThemeRole.Surface;

    [Tooltip("Defaults to the Graphic on this GameObject.")]
    [SerializeField] private Graphic target;

    [Tooltip("Keep the authored alpha (hand-tuned scrims/disabled looks) and only repaint the hue.")]
    [SerializeField] private bool preserveAlpha = true;

    public ThemeRole Role => role;

    /// <summary>Wire in code (tests, runtime spawners, SerializedObject wirers).</summary>
    public void Configure(ThemeRole newRole, Graphic explicitTarget = null, bool keepAlpha = true)
    {
        role = newRole;
        if (explicitTarget != null) target = explicitTarget;
        preserveAlpha = keepAlpha;
        if (isActiveAndEnabled) Apply();
    }

    private void Awake()
    {
        if (target == null) target = GetComponent<Graphic>();
    }

    private void OnEnable()
    {
        Apply();
        Theme.Changed += Apply;
    }

    private void OnDisable()
    {
        Theme.Changed -= Apply;
    }

    /// <summary>Pull the active theme's colour for this role onto the target.</summary>
    public void Apply()
    {
        if (target == null) target = GetComponent<Graphic>();
        if (target == null) return;

        var c = Theme.Color(role);
        if (preserveAlpha) c.a = target.color.a;
        target.color = c;
    }
}
