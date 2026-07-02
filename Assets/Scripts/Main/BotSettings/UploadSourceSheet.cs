using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Bottom sheet shown when the user taps «Загрузить прайс-лист». Offers two
    /// upload sources — «Файл» (the document picker) and «Фото из галереи»
    /// (NativeGallery multi-select) — plus «Отмена».
    ///
    /// Mirrors <see cref="ItemEditSheet"/>'s slide-up + scrim-behind +
    /// tap-outside-to-close idiom, minus the keyboard-follow machinery (this
    /// sheet has no input fields). The two option buttons raise
    /// <see cref="OnFilePressed"/> / <see cref="OnGalleryPressed"/>; BotSettings
    /// subscribes and resumes the existing upload flow with the pending context.
    /// The sheet closes itself before invoking the source handler, so the picker
    /// / gallery opens over a clean page.
    /// </summary>
    public class UploadSourceSheet : MonoBehaviour
    {
        [SerializeField] private RectTransform sheetRoot;
        [SerializeField] private Button fileButton;
        [SerializeField] private Button galleryButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private GameObject scrimBehind;   // dim behind sheet
        [SerializeField] private CanvasGroup scrimBehindGroup;
        [SerializeField] private DelayedFingerUpAction scrimBehindFinger; // tap-outside to close (full-press verified)
        [SerializeField] private float slideDuration = 0.28f;
        [SerializeField] private float scrimAlpha = 0.5f;

        private enum SheetMode { Hidden, Showing, Shown, Hiding }

        private SheetMode mode = SheetMode.Hidden;
        private Vector2 hiddenAnchored;
        private Vector2 shownAnchored;
        private Tween posTween;

        /// <summary>Raised when the user taps «Файл».</summary>
        public event Action OnFilePressed;

        /// <summary>Raised when the user taps «Фото из галереи».</summary>
        public event Action OnGalleryPressed;

        private void Awake()
        {
            shownAnchored = sheetRoot.anchoredPosition;
            hiddenAnchored = new Vector2(shownAnchored.x, -sheetRoot.rect.height);
            sheetRoot.anchoredPosition = hiddenAnchored;
            // NOTE: do not SetActive(false) here — the prefab ships with the
            // sheet container inactive, so Awake only runs the first time Show()
            // activates it. Deactivating again would cancel that first SlideIn.

            if (fileButton != null) fileButton.onClick.AddListener(HandleFilePressed);
            if (galleryButton != null) galleryButton.onClick.AddListener(HandleGalleryPressed);
            if (cancelButton != null) cancelButton.onClick.AddListener(Hide);
            if (scrimBehindFinger != null) scrimBehindFinger.OnRealRelease += Hide;
        }

        private void OnDestroy()
        {
            if (scrimBehindFinger != null) scrimBehindFinger.OnRealRelease -= Hide;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            mode = SheetMode.Showing;

            if (scrimBehind != null)
            {
                scrimBehind.SetActive(true);
                scrimBehindGroup.alpha = 0f;
                scrimBehindGroup.DOKill();
                scrimBehindGroup.DOFade(scrimAlpha, slideDuration).SetEase(Ease.OutQuad);
            }

            posTween?.Kill();
            posTween = sheetRoot.DOAnchorPos(shownAnchored, slideDuration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    mode = SheetMode.Shown;
                    posTween = null;
                });
        }

        public void Hide()
        {
            if (mode == SheetMode.Hidden || mode == SheetMode.Hiding) return;
            mode = SheetMode.Hiding;

            posTween?.Kill();
            posTween = sheetRoot.DOAnchorPos(hiddenAnchored, slideDuration)
                .SetEase(Ease.InCubic)
                .OnComplete(() =>
                {
                    gameObject.SetActive(false);
                    mode = SheetMode.Hidden;
                    posTween = null;
                });

            if (scrimBehind != null)
            {
                scrimBehindGroup.DOKill();
                scrimBehindGroup.DOFade(0f, slideDuration).SetEase(Ease.InQuad)
                    .OnComplete(() => scrimBehind.SetActive(false));
            }
        }

        private void HandleFilePressed()
        {
            OnFilePressed?.Invoke();
        }

        private void HandleGalleryPressed()
        {
            OnGalleryPressed?.Invoke();
        }
    }
}
