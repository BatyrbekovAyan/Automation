using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ✦ ⇄ ⌨ key inside the composer input field (sketch-003 variant A) — the switch between
/// the two keyboard-slot tenants, placed like WhatsApp/Telegram's in-field icons. Shows ✦ while
/// the suggestions slot is closed (tap → open it, over the keyboard if one is up) and ⌨ while
/// it is open (tap → hand the slot back to the keyboard). Visible only in «Вместе» mode.
/// View only: raises <see cref="Tapped"/>; SuggestionsController owns the swap.
/// </summary>
public class ComposerSlotKey : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private GameObject sparkleGlyph;    // ✦ — slot closed, tap opens suggestions
    [SerializeField] private GameObject keyboardGlyph;   // ⌨ — slot open, tap returns the keyboard

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

    /// <summary>Swap the glyph to match the slot's tenant: ⌨ while the panel holds it, ✦ otherwise.</summary>
    public void SetSlotOpen(bool slotOpen)
    {
        if (sparkleGlyph != null && sparkleGlyph.activeSelf == slotOpen) sparkleGlyph.SetActive(!slotOpen);
        if (keyboardGlyph != null && keyboardGlyph.activeSelf != slotOpen) keyboardGlyph.SetActive(slotOpen);
    }

    /// <summary>The key exists only where suggestions do — «Вместе» mode in an open chat.</summary>
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }
}
