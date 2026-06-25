using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Semi-auto icon toggle in the open-chat top bar (SEMI-01). Lit green = on (the lit state IS
/// the per-chat mode indicator, D-09); neutral grey = off. View only — it raises
/// <see cref="OnToggled"/> with the desired state on tap; Plan 04 persists it (SemiAutoStore)
/// and drives the panel. Icon is an Image + sprite, never a TMP glyph (UI-SPEC anti-pattern).
/// </summary>
public class SemiAutoToggle : MonoBehaviour
{
    [SerializeField] private Button toggleButton;   // ≥132u hit area
    [SerializeField] private Image iconImage;       // Image + sprite (NOT a TMP glyph)

    private bool _on;
    public event Action<bool> OnToggled;            // fires desired state on tap

    void Awake()
    {
        if (toggleButton != null) toggleButton.onClick.AddListener(() => OnToggled?.Invoke(!_on));
    }

    public void SetLit(bool on)
    {
        _on = on;
        if (iconImage == null) return;
        // Lit green #25D366 on; neutral grey #54656F off — DOColor 0.15s OutQuad (UI-SPEC).
        Color target = on ? new Color32(0x25, 0xD3, 0x66, 0xFF) : new Color32(0x54, 0x65, 0x6F, 0xFF);
        iconImage.DOColor(target, 0.15f).SetEase(Ease.OutQuad);
    }
}
