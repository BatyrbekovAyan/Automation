/// <summary>Which glyph the composer key wears — the DESTINATION it takes you to, never the current tenant.</summary>
public enum SlotKeyGlyph
{
    /// <summary>✦ — tap raises the suggestions panel.</summary>
    Sparkle,

    /// <summary>⌨ — tap hands the slot back to the native keyboard.</summary>
    Keyboard,
}

/// <summary>
/// Pure decision seam for the ONE morphing key at the composer field's END (sketch 005 winner E),
/// driving the <see cref="ComposerSlotKey"/> view.
///
/// THE GRAMMAR — READ BEFORE WRITING A TEST FROM A STATE NAME: the glyph names the DESTINATION,
/// not the current state. So the ⌨ glyph appears exactly while the PANEL is up (tap → keyboard),
/// and the ✦ glyph appears while the panel is NOT up (tap → panel). Reading it as "state = glyph"
/// inverts every row of the table below.
///
/// Consequences the table encodes: Panel and Expanded are one destination (both are "panel is up"),
/// and Collapsed and Keyboard are the other (both are "panel is not up"). The ✦ destination is the
/// promoted one — it wears the tint circle and PositiveInk; the ⌨ return trip is quiet InkTertiary
/// with no circle. In «Авто» there are no suggestions to reach, so the key does not exist at all.
///
/// No MonoBehaviour, no namespace — flat Assets/Scripts/UI/ pure-seam style (ChannelSwitcherModel
/// precedent), so the whole matrix is EditMode-testable without a scene.
/// </summary>
public static class ComposerSlotKeyModel
{
    /// <summary>
    /// Alpha of the ✦ tint circle, authored ON the Image and pinned here rather than set from code:
    /// a graphic must have exactly ONE colour owner, and that owner is the circle's ThemedColor
    /// binding to PositiveInk (preserveAlpha = true, so this authored alpha survives every repaint).
    /// The soft tint is therefore alpha-on-PositiveInk — never a new ThemeRole (ThemeRole is
    /// append-only because ThemedColor serialises the enum ORDINAL into the scene) and never a
    /// code-set colour fighting the binding.
    /// </summary>
    public const float TintCircleAlpha = 0.13f;

    /// <summary>
    /// The key's appearance for a slot state. «Авто» (semiAutoOn = false) hides it everywhere;
    /// otherwise the glyph is the destination — ⌨ while the panel is up, ✦ while it is not.
    /// An unrecognised state falls to the ✦ branch: offering the panel is the safe defined
    /// result, never an undefined appearance.
    /// </summary>
    public static SlotKeyStyle For(SuggestionSlotState state, bool semiAutoOn)
    {
        // Hidden: values still fixed so the struct is deterministic and comparable in tests.
        if (!semiAutoOn)
            return new SlotKeyStyle(false, SlotKeyGlyph.Sparkle, false, ThemeRole.InkTertiary);

        // The two panel states are listed POSITIVELY on purpose. Phrased the other way round
        // ("anything that is not Collapsed or Keyboard"), an out-of-range cast would inherit the ⌨
        // branch and offer a keyboard the slot may not even have — the whitelist is what makes the
        // ✦ fallback in the doc above true.
        bool panelIsUp = state == SuggestionSlotState.Panel || state == SuggestionSlotState.Expanded;

        return panelIsUp
            ? new SlotKeyStyle(true, SlotKeyGlyph.Keyboard, false, ThemeRole.InkTertiary)
            : new SlotKeyStyle(true, SlotKeyGlyph.Sparkle, true, ThemeRole.PositiveInk);
    }
}

/// <summary>
/// One rendering of the composer key, produced by <see cref="ComposerSlotKeyModel.For"/>:
/// whether it exists at all, which destination glyph it wears, whether the tint circle sits
/// behind that glyph, and the ink role both are painted with.
/// </summary>
public readonly struct SlotKeyStyle
{
    /// <summary>False only in «Авто» — there is no suggestions panel to reach, so no key.</summary>
    public readonly bool Visible;

    /// <summary>The destination a tap goes to, never the current tenant.</summary>
    public readonly SlotKeyGlyph Glyph;

    /// <summary>The soft circle behind ✦ that promotes the panel; the quiet ⌨ return trip never wears it.</summary>
    public readonly bool TintCircle;

    /// <summary>Ink role for the glyph (and, at <see cref="ComposerSlotKeyModel.TintCircleAlpha"/>, the circle).</summary>
    public readonly ThemeRole Ink;

    public SlotKeyStyle(bool visible, SlotKeyGlyph glyph, bool tintCircle, ThemeRole ink)
    {
        Visible = visible;
        Glyph = glyph;
        TintCircle = tintCircle;
        Ink = ink;
    }
}
