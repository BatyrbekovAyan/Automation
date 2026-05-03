using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Full-screen dim + raised-layer reparent. Show(field, onOutsideTap)
    /// lifts the given RectTransform onto raisedLayer so it renders above
    /// the scrim; tapping the scrim invokes onOutsideTap (via finger-up,
    /// not finger-down, matching PopupUI).
    /// </summary>
    public class FocusScrim : MonoBehaviour
    {
        [SerializeField] private GameObject scrimRoot;
        [SerializeField] private CanvasGroup scrimGroup;
        [SerializeField] private Image scrimImage;
        [SerializeField] private RectTransform raisedLayer;
        [SerializeField] private float targetAlpha = 0.5f;
        [SerializeField] private float fadeInDuration = 0.2f;
        [SerializeField] private float fadeOutDuration = 0.15f;

        private RectTransform raisedField;
        private Transform originalParent;
        private int originalSiblingIndex;
        private RectTransform placeholder;
        private DelayedFingerUpAction fingerUp;
        private Action onOutsideTapCached;

        public bool IsShowing { get; private set; }

        private void OnDisable()
        {
            if (IsShowing)
                Hide();
        }

        public void Show(RectTransform field, Action onOutsideTap)
        {
            if (IsShowing) Hide();

            raisedField = field;
            originalParent = field.parent;
            originalSiblingIndex = field.GetSiblingIndex();

            // Insert a spacer that preserves the field's layout slot so
            // siblings under the parent's VerticalLayoutGroup don't reflow
            // when we reparent the field onto the raised layer.
            var placeholderGo = new GameObject(
                "FocusScrimPlaceholder", typeof(RectTransform), typeof(LayoutElement));
            placeholder = (RectTransform)placeholderGo.transform;
            placeholder.SetParent(originalParent, worldPositionStays: false);
            placeholder.SetSiblingIndex(originalSiblingIndex);
            placeholder.localScale = Vector3.one;
            placeholder.anchoredPosition = Vector2.zero;
            placeholder.sizeDelta = new Vector2(0f, field.rect.height);
            var le = placeholderGo.GetComponent<LayoutElement>();
            le.preferredHeight = field.rect.height;
            le.flexibleHeight = 0f;

            field.SetParent(raisedLayer, worldPositionStays: true);
            field.SetAsLastSibling();

            scrimRoot.SetActive(true);
            scrimGroup.DOKill();
            scrimGroup.alpha = 0f;
            scrimGroup.DOFade(targetAlpha, fadeInDuration).SetEase(Ease.OutQuad);

            if (fingerUp == null)
                fingerUp = scrimImage.gameObject.GetComponent<DelayedFingerUpAction>()
                           ?? scrimImage.gameObject.AddComponent<DelayedFingerUpAction>();
            onOutsideTapCached = onOutsideTap;
            fingerUp.OnRealRelease += HandleOutsideTap;

            IsShowing = true;
        }

        public void Hide()
        {
            if (!IsShowing) return;

            if (fingerUp != null)
                fingerUp.OnRealRelease -= HandleOutsideTap;
            onOutsideTapCached = null;

            scrimGroup.DOKill();
            scrimGroup.DOFade(0f, fadeOutDuration).SetEase(Ease.InQuad)
                .OnComplete(() => scrimRoot.SetActive(false));

            if (raisedField != null && originalParent != null)
            {
                raisedField.SetParent(originalParent, worldPositionStays: true);
                raisedField.SetSiblingIndex(originalSiblingIndex);
            }

            if (placeholder != null)
            {
                Destroy(placeholder.gameObject);
                placeholder = null;
            }

            raisedField = null;
            originalParent = null;
            IsShowing = false;
        }

        private void HandleOutsideTap()
        {
            onOutsideTapCached?.Invoke();
        }
    }
}
