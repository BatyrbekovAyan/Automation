using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ONE morphing ✦ ⇄ ⌨ key at the composer input field's end (sketch-005 winner E), placed like
/// WhatsApp/Telegram's in-field icons. The glyph names the DESTINATION a tap goes to, never the
/// current tenant: ✦ while the panel is NOT up (tap → raise it) and ⌨ while it IS up (tap → hand
/// the slot back to the keyboard). Visible only in «Вместе» mode.
///
/// <para>Pure view: it renders a <see cref="SlotKeyStyle"/> through <see cref="Apply"/> and raises
/// <see cref="Tapped"/>. It decides nothing and stores nothing — <see cref="ComposerSlotKeyModel"/>
/// owns the matrix and SuggestionsController owns the swap.</para>
/// </summary>
public class ComposerSlotKey : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private GameObject sparkleGlyph;    // ✦ — slot closed, tap opens suggestions
    [SerializeField] private GameObject keyboardGlyph;   // ⌨ — slot open, tap returns the keyboard

    /// <summary>
    /// The soft PositiveInk circle that promotes the ✦ face — a SEPARATE child, deliberately NOT the
    /// Button's targetGraphic (the root Image is the targetGraphic and the key's only raycast target;
    /// recolouring or disabling it would make the key untappable). Its colour has exactly ONE owner:
    /// a ThemedColor(PositiveInk, preserveAlpha) binding over the
    /// <see cref="ComposerSlotKeyModel.TintCircleAlpha"/> alpha authored on the Image. This class
    /// therefore only ever SetActive()s it and NEVER touches its colour.
    /// </summary>
    [SerializeField] private GameObject tintCircle;      // ✦ only — soft PositiveInk disc behind the glyph

    public event Action Tapped;

    void Awake()
    {
        if (button == null) return;
        // Never steal EventSystem selection from the field on PointerDown — same rule as the
        // composer's own buttons (MessagesBottomPanel.SetNavigationNone).
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        button.onClick.AddListener(HandleClick);
    }

    void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick() => Tapped?.Invoke();

    /// <summary>
    /// Render one <see cref="SlotKeyStyle"/> from <see cref="ComposerSlotKeyModel.For"/> — the single
    /// entry point: does the key exist, which destination glyph it wears, and whether the tint circle
    /// sits behind that glyph.
    ///
    /// <para><see cref="SlotKeyStyle.Ink"/> is deliberately NOT applied here. Each glyph (and the tint
    /// circle) carries its own ThemedColor binding stamped at build time, and a graphic may have
    /// exactly ONE colour owner — painting ink from code would make this class a second owner and
    /// fight the binding on every <c>Theme.Changed</c>. Do not "fix" that by adding a colour write;
    /// if a glyph shows the wrong ink, the binding on that glyph is what is wrong.</para>
    ///
    /// Every field is null-guarded: an older scene may not have a <c>tintCircle</c> child stamped yet.
    /// </summary>
    public void Apply(SlotKeyStyle style)
    {
        // Anything that is not the ⌨ face is the ✦ face: the model documents ✦ as the SAFE defined
        // fallback, so a glyph value this view has not heard of must land there too — matching on
        // Sparkle instead would make an unknown value fall to ⌨ and offer a keyboard the slot may
        // not have, inverting the seam's own guarantee.
        bool wantSparkle = style.Glyph != SlotKeyGlyph.Keyboard;

        // Faces first, visibility last: when the key is coming back on, its glyph is already correct
        // by the time the root activates, so it can never show the previous destination for a frame.
        // (SetActive on a child of an inactive root is legal — activeSelf is stored and takes effect
        // the moment the root activates.)
        SetActiveIfChanged(sparkleGlyph, wantSparkle);
        SetActiveIfChanged(keyboardGlyph, !wantSparkle);
        SetActiveIfChanged(tintCircle, style.TintCircle);

        SetVisible(style.Visible);
    }

    /// <summary>Legacy 2-state entry point — <see cref="Apply"/> is the real one; this only delegates.</summary>
    /// <remarks>
    /// Kept so the pre-005 call sites keep compiling. It maps the old boolean onto the model's states
    /// (slot open = the panel holds the slot ⇒ ⌨ destination) and preserves the CURRENT visibility,
    /// because the boolean API never owned it — <see cref="SetVisible"/> did, and callers still pair
    /// the two calls. <c>semiAutoOn</c> is pinned true for the same reason: this entry point only ever
    /// runs while the key is already on screen, i.e. in «Вместе»; the «Авто» hide stays SetVisible's.
    ///
    /// <para>Migrating a call site means replacing BOTH calls with one
    /// <c>Apply(ComposerSlotKeyModel.For(state, semiAutoOn))</c> — while the pair survives, a
    /// <see cref="SetVisible"/>(true) that is not immediately followed by a SetSlotOpen shows
    /// whatever face the key was wearing when it was last hidden.</para>
    /// </remarks>
    public void SetSlotOpen(bool slotOpen)
    {
        var style = ComposerSlotKeyModel.For(
            slotOpen ? SuggestionSlotState.Panel : SuggestionSlotState.Keyboard,
            semiAutoOn: true);

        Apply(new SlotKeyStyle(gameObject.activeSelf, style.Glyph, style.TintCircle, style.Ink));
    }

    /// <summary>The key exists only where suggestions do — «Вместе» mode in an open chat.</summary>
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    // One guard for all three children: null-tolerant (an older scene may not have every child
    // stamped) and only writes activeSelf when it actually changes, so a per-state re-render costs
    // nothing. Kept as one helper because the three call sites read in opposite polarities and an
    // inverted comparison is exactly the edit that silently shows both faces at once.
    private static void SetActiveIfChanged(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
