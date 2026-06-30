using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Per-chat reply-mode switch in the open-chat top bar (SEMI-01). A COMPACT sibling of the
/// chats-list <see cref="ReplyModeToggleBinder"/> — same sliding-knob form and color language so
/// the two read as one control (bot-wide default vs this conversation). "Авто" (green) on the
/// RIGHT, "Вместе" (semi-auto, blue) on the LEFT; the white thumb covers the active word and
/// slides on switch while the track recolours. View only: it raises <see cref="OnToggled"/> with
/// the desired state on tap; SuggestionsController persists it (SemiAutoStore) and drives the panel.
/// </summary>
public class SemiAutoToggle : MonoBehaviour
{
    [SerializeField] private Button toggleButton;          // whole switch is the tap target
    [SerializeField] private Image trackImage;             // recolours green (Auto) ↔ blue (Semi)
    [SerializeField] private RectTransform thumb;          // slides right (Auto) ↔ left (Semi)
    [SerializeField] private TextMeshProUGUI thumbLabel;   // active word, covered by the thumb
    [SerializeField] private TextMeshProUGUI faintAvto;    // recessive "Авто" on the right
    [SerializeField] private TextMeshProUGUI faintVmeste;  // recessive "Вместе" on the left
    [SerializeField] private float thumbX = 54f;           // ± slide distance (set by the builder)

    private bool _on;                                       // true = semi-auto on
    public event Action<bool> OnToggled;                   // fires desired state on tap

    // Match ReplyModeToggleBinder exactly so the two switches share one color language.
    private static readonly Color TrackAuto  = new Color32(0x2F, 0xB3, 0x44, 0xFF);
    private static readonly Color TrackSemi  = new Color32(0x00, 0x7A, 0xFF, 0xFF);
    private static readonly Color InkAuto    = new Color32(0x20, 0x6A, 0x2C, 0xFF);
    private static readonly Color InkSemi    = new Color32(0x00, 0x4C, 0x99, 0xFF);
    private static readonly Color FaintAuto  = new Color32(0xC3, 0xEF, 0xCB, 0xFF);
    private static readonly Color FaintSemi  = new Color32(0xA8, 0xCF, 0xFF, 0xFF);
    private const float AnimDuration = 0.22f;

    void Awake()
    {
        if (toggleButton != null) toggleButton.onClick.AddListener(() => OnToggled?.Invoke(!_on));
    }

    void OnDisable()
    {
        if (thumb != null) thumb.DOKill();
        if (trackImage != null) trackImage.DOKill();
    }

    /// <summary>Sets the visual state. <paramref name="on"/> = semi-auto ("Вместе"/blue/left).</summary>
    public void SetLit(bool on)
    {
        bool animate = _on != on && gameObject.activeInHierarchy;   // slide only on a real, visible change
        _on = on;
        ApplyVisuals(on, animate);
    }

    private void ApplyVisuals(bool semi, bool animate)
    {
        Color track = semi ? TrackSemi : TrackAuto;
        float x = semi ? -thumbX : thumbX;                          // Вместе left, Авто right
        Color faint = semi ? FaintSemi : FaintAuto;

        if (thumbLabel != null)
        {
            thumbLabel.text = semi ? "Вместе" : "Авто";
            thumbLabel.color = semi ? InkSemi : InkAuto;
        }
        if (faintAvto != null) faintAvto.color = faint;
        if (faintVmeste != null) faintVmeste.color = faint;

        if (thumb != null) thumb.DOKill();
        if (trackImage != null) trackImage.DOKill();

        if (animate)
        {
            if (thumb != null) thumb.DOAnchorPosX(x, AnimDuration).SetEase(Ease.OutCubic);
            if (trackImage != null) trackImage.DOColor(track, AnimDuration).SetEase(Ease.OutCubic);
        }
        else
        {
            if (thumb != null) { Vector2 p = thumb.anchoredPosition; p.x = x; thumb.anchoredPosition = p; }
            if (trackImage != null) trackImage.color = track;
        }
    }
}
