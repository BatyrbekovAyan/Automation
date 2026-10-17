using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// iOS-style card toggle. Animates thumb position + track color via
    /// DOTween. Exposes the underlying Toggle so BotSettings keeps typed
    /// references named WhatsappToggle / TelegramToggle for the auth
    /// coroutines that read Toggle.isOn directly.
    /// </summary>
    public class ToggleRow : MonoBehaviour
    {
        [SerializeField] private Toggle toggle;
        [SerializeField] private Image trackImage;
        [SerializeField] private RectTransform thumb;
        [SerializeField] private TextMeshProUGUI labelText;
        [SerializeField] private Color onColor = new Color(0.145f, 0.827f, 0.4f);   // #25D366
        [SerializeField] private Color offColor = new Color(0.878f, 0.878f, 0.878f); // #E0E0E0
        [SerializeField] private float thumbOffsetX = 20f;
        [SerializeField] private float animDuration = 0.2f;

        public Toggle Toggle => toggle;
        public string Label { get => labelText.text; set => labelText.text = value; }

        /// <summary>
        /// Set the toggle state without firing onValueChanged, but still
        /// update the visual (thumb position + track color) immediately.
        /// Use this when code needs to force the toggle off/on without
        /// triggering auth/EnableSave side effects, yet still wants the
        /// control to reflect the new state on screen.
        ///
        /// Safe in every state: a row whose GameObject is inactive is repainted
        /// too (the row has no OnEnable repaint, so a revert behind a hidden tab
        /// must paint now), an in-flight tap animation is cut so it cannot finish
        /// on the old value, and before Awake only the value is taken — Awake
        /// records the thumb's authored OFF position, and painting earlier would
        /// record the wrong one for good.
        /// </summary>
        public void SetIsOnQuiet(bool on)
        {
            if (toggle != null) toggle.SetIsOnWithoutNotify(on);
            if (initialized) ApplyImmediate(on);
        }

        private Vector2 thumbOffAnchored;
        private Vector2 thumbOnAnchored;
        private bool initialized;

        private void Awake()
        {
            thumbOffAnchored = thumb.anchoredPosition;
            thumbOnAnchored = thumbOffAnchored + new Vector2(thumbOffsetX, 0f);
            toggle.onValueChanged.AddListener(AnimateTo);
            initialized = true;
            ApplyImmediate(toggle.isOn);
        }

        private void OnDestroy() => toggle.onValueChanged.RemoveListener(AnimateTo);

        private void AnimateTo(bool on)
        {
            thumb.DOAnchorPos(on ? thumbOnAnchored : thumbOffAnchored, animDuration)
                 .SetEase(Ease.OutCubic);
            trackImage.DOColor(on ? onColor : offColor, animDuration);
        }

        private void ApplyImmediate(bool on)
        {
            thumb.DOKill();
            trackImage.DOKill();
            thumb.anchoredPosition = on ? thumbOnAnchored : thumbOffAnchored;
            trackImage.color = on ? onColor : offColor;
        }
    }
}
