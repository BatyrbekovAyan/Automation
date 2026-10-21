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
///
/// B2 fix round 3 (D1): a chip lit ONLY by a fallback (the bot's schedule is
/// closed, or the quota is spent — the chat's own mode is not «Вместе») raises
/// <see cref="OnDismissed"/> instead: no learning gate, no confirm — nothing is
/// being enabled (the server would not answer this client now anyway), and the
/// controller writes nothing; it only hides the panel for this chat-open.
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

    /// <summary>D1: a tap on a chip lit only by a fallback — hide the panel for this chat-open, record nothing.</summary>
    public event Action OnDismissed;

    private const string EnableTitle = "Включить авто-режим в этом чате?";
    private const string EnableBody =
        "Сотрудник будет сам отвечать этому клиенту. Выключить можно в любой момент — этой же кнопкой.";

    private bool _on;   // true = semi-auto on for this chat (the «Авто» chip reads unlit)
    private bool _fallbackOnly;   // D1: lit by a fallback alone — the chat's stored mode is «Авто»

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

    /// <summary>
    /// A2: dim the chip while the tapped state is not yet confirmed by the server (pending desire);
    /// full opacity once the per-profile confirmation lands. The chip stays tappable meanwhile —
    /// a newer tap simply supersedes the pending one.
    /// </summary>
    public void SetPending(bool pending)
    {
        var group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = pending ? ReplyModeToggleBinder.PendingAlpha : 1f;
    }

    /// <summary>
    /// Sets the visual state. <paramref name="on"/> = semi-auto (chip unlit). <paramref name="fallbackOnly"/> (D1): the
    /// lit state comes from a fallback alone. Every paint states it — the default is «the chat's own mode», so a caller
    /// that forgets it gets today's tap (the owner's decision), never a silent no-op.
    /// </summary>
    public void SetLit(bool on, bool fallbackOnly = false)
    {
        bool animate = _on != on && gameObject.activeInHierarchy;   // animate only a real, visible change
        _on = on;
        _fallbackOnly = on && fallbackOnly;
        ApplyVisuals(on, animate);
    }

    /// <summary>D1: restate the flag without a repaint (a live re-evaluation that did not flip the chip).</summary>
    public void SetFallbackOnly(bool fallbackOnly) => _fallbackOnly = _on && fallbackOnly;

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

        // D1: lit only by a fallback — the chat already IS «Авто»; the panel is up because the bot's window is closed or
        // the quota is spent. The tap hides it for this chat-open: no gate, no confirm, nothing written.
        if (_fallbackOnly)
        {
            OnDismissed?.Invoke();
            return;
        }

        // Turning auto ON for this chat — the bot starts writing this client, so
        // the same confirm gate as the header button, scoped to one conversation.
        // learning B: the per-chat enable meets the same soft lock (the sheet says «в этом чате»), then its own confirm —
        // unless the bot itself is already in «Авто»: resuming one paused chat is a move inside an enabled mode (spec §0).
        string botKey = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : null;
        ReplyModeToggleBinder.ReplyMode botMode = string.IsNullOrEmpty(botKey)
            ? AutoButtonModel.DefaultMode
            : ReplyModeToggleBinder.GetMode(botKey);
        AutoLockGate.TryEnableChat(botKey, botMode, label != null ? label.font : null, () =>
        {
            if (!ReplyModeToggleBinder.ShowConfirm(EnableTitle, EnableBody, () => OnToggled?.Invoke(false)))
                OnToggled?.Invoke(false);  // no popup wired — commit straight away
        });
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
