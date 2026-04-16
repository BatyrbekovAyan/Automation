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

        private Vector2 thumbOffAnchored;
        private Vector2 thumbOnAnchored;

        private void Awake()
        {
            thumbOffAnchored = thumb.anchoredPosition;
            thumbOnAnchored = thumbOffAnchored + new Vector2(thumbOffsetX, 0f);
            toggle.onValueChanged.AddListener(AnimateTo);
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
            thumb.anchoredPosition = on ? thumbOnAnchored : thumbOffAnchored;
            trackImage.color = on ? onColor : offColor;
        }
    }
}
