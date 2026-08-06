using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Per-chat «Авто» chip in the open-chat top bar — the conversation-scoped
/// sibling of the chats-list <see cref="ReplyModeToggleBinder"/> button, sharing
/// its exact visual language via <see cref="ReplyModeToggleBinder.PaintChip"/>
/// (2026-08 restyle): PositiveBg pill + filled lamp when the bot answers THIS
/// client itself, outlined pill + hollow lamp when it only proposes replies.
///
/// View only, same contract as the sliding-knob era: it raises
/// <see cref="OnToggled"/> with the DESIRED semi-auto state on tap;
/// SuggestionsController persists it (SemiAutoStore, explicit per-chat
/// override) and drives the suggestions panel. The header's confirm asymmetry
/// applies per chat too: enabling auto for the chat (desired semi = false)
/// routes through the shared confirm popup with chat-scoped copy; disabling is
/// instant. Rebuilt + wired by ChatsTopBarRestyleBuilder.
/// </summary>
public class SemiAutoToggle : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [SerializeField] private Image ringImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image dotRing;
    [SerializeField] private Image dotCore;

    /// <summary>Fires the desired semi-auto state on tap (after the confirm gate).</summary>
    public event Action<bool> OnToggled;

    private const string EnableTitle = "Включить авто-режим в этом чате?";
    private const string EnableBody =
        "Бот будет сам отвечать этому клиенту. Выключить можно в любой момент — этой же кнопкой.";

    private bool _on;   // true = semi-auto on for this chat (the «Авто» chip reads unlit)

    private void Awake()
    {
        if (toggleButton == null) toggleButton = GetComponent<Button>();
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveAllListeners();
            toggleButton.onClick.AddListener(HandlePressed);
        }
    }

    private void OnEnable()
    {
        Theme.Changed += RepaintForTheme;
        ApplyVisuals(_on, animate: false);
    }

    private void OnDisable()
    {
        Theme.Changed -= RepaintForTheme;
        KillColorTweens();
        transform.DOKill();
        transform.localScale = Vector3.one;
    }

    /// <summary>Sets the visual state. <paramref name="on"/> = semi-auto (chip unlit).</summary>
    public void SetLit(bool on)
    {
        bool animate = _on != on && gameObject.activeInHierarchy;   // animate only a real, visible change
        _on = on;
        ApplyVisuals(on, animate);
    }

    private void RepaintForTheme() => ApplyVisuals(_on, animate: false);

    private void HandlePressed()
    {
        transform.DOKill();
        transform.localScale = Vector3.one;
        transform.DOPunchScale(Vector3.one * -0.04f, 0.18f, 1, 0.5f);

        bool desiredSemi = !_on;
        if (desiredSemi)
        {
            OnToggled?.Invoke(true);   // turning auto OFF for this chat — safe, instant
            return;
        }

        // Turning auto ON for this chat — the bot starts writing this client, so
        // the same confirm gate as the header button, scoped to one conversation.
        if (!ReplyModeToggleBinder.ShowConfirm(EnableTitle, EnableBody, () => OnToggled?.Invoke(false)))
            OnToggled?.Invoke(false);  // no popup wired — commit straight away
    }

    private void ApplyVisuals(bool semi, bool animate)
    {
        KillColorTweens();
        ReplyModeToggleBinder.PaintChip(!semi, ringImage, fillImage, label, dotRing, dotCore, animate);
    }

    private void KillColorTweens()
    {
        if (fillImage != null) fillImage.DOKill();
        if (ringImage != null) ringImage.DOKill();
        if (label != null) label.DOKill();
        if (dotRing != null) dotRing.DOKill();
        if (dotCore != null) dotCore.DOKill();
    }
}
