using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Bottom sheet for editing one ProductCardView or ServiceCardView.
    /// Holds its own FocusScrim scoped to the sheet — nested field focus
    /// raises above the sheet, not the main page.
    ///
    /// Delete routes through PopupUI so the existing finger-up confirm
    /// popup continues to work unchanged.
    /// </summary>
    public class ItemEditSheet : MonoBehaviour
    {
        [SerializeField] private RectTransform sheetRoot;
        [SerializeField] private EditableField nameField;
        [SerializeField] private EditableField priceField;
        [SerializeField] private EditableField descField;
        [SerializeField] private Button doneButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private GameObject deleteConfirmPopup;
        [SerializeField] private Button deleteConfirmYes;
        [SerializeField] private Button deleteConfirmNo;
        [SerializeField] private GameObject scrimBehind;  // dim behind sheet (separate from FocusScrim)
        [SerializeField] private CanvasGroup scrimBehindGroup;
        [SerializeField] private DelayedFingerUpAction scrimBehindFinger;  // tap-outside to close (full-press verified)
        [SerializeField] private float slideDuration = 0.25f;
        [SerializeField] private float scrimAlpha = 0.5f;
        [Tooltip("Canvas-unit reduction applied to the measured keyboard height. " +
                 "Raise this (e.g. 80-160) if the lift appears to overshoot on device.")]
        [SerializeField] private float liftReduction = 0f;
        [Tooltip("Canvas-unit height of the iOS accessory bar above the keyboard. " +
                 "Subtracted when the focused field has 'Hide Mobile Input' enabled, " +
                 "because TouchScreenKeyboard.area still reports that invisible bar. " +
                 "~44pt at reference 1x; tune if your canvas scale differs.")]
        [SerializeField] private float hiddenMobileInputBarHeight = 44f;

        [Tooltip("SmoothDamp time (s) used to track the keyboard while the sheet is shown. " +
                 "0.12 matches the Apple native keyboard spring — same value used by KeyboardAwarePanel.")]
        [SerializeField] private float keyboardFollowSmoothTime = 0.12f;

        private enum SheetMode { Hidden, Showing, Shown, Hiding }

        private Canvas canvas;
        private float baselineY;
        private bool isShown;
        private EditableField lastFocusedField;
        private SheetMode mode = SheetMode.Hidden;
        private float currentLiftedY;
        private float liftVelocity;

        private ProductCardView boundProduct;
        private ServiceCardView boundService;
        private Vector2 hiddenAnchored;
        private Vector2 shownAnchored;

        public event Action<ProductCardView> OnProductDeleted;
        public event Action<ServiceCardView> OnServiceDeleted;
        public event Action OnAnyCommitted;

        private void Awake()
        {
            shownAnchored = sheetRoot.anchoredPosition;
            hiddenAnchored = new Vector2(shownAnchored.x, -sheetRoot.rect.height);
            sheetRoot.anchoredPosition = hiddenAnchored;
            baselineY = shownAnchored.y;
            canvas = GetComponentInParent<Canvas>();
            // NOTE: do not SetActive(false) here. The prefab ships with the
            // sheet container already inactive, so Awake only runs the first
            // time Show() activates it — deactivating again inside Awake
            // would cancel that first SlideIn mid-animation.

            doneButton.onClick.AddListener(Commit);
            deleteButton.onClick.AddListener(() => PopupUI.Show(deleteConfirmPopup));
            PopupUI.WireFingerUp(deleteConfirmYes, ConfirmDelete);
            PopupUI.WireFingerUp(deleteConfirmNo, () => PopupUI.Hide(deleteConfirmPopup));
            if (scrimBehindFinger != null)
                scrimBehindFinger.OnRealRelease += Hide;
        }

        private void Update()
        {
            if (!isShown || sheetRoot == null || mode != SheetMode.Shown) return;

            // Track which field is currently focused so the mobile-input-bar
            // subtraction in GetKeyboardHeightCanvas has fresh context.
            lastFocusedField = GetFocusedField();

            float targetKeyboard = GetKeyboardHeightCanvas();
            float effectiveLift = Mathf.Max(0f, targetKeyboard - liftReduction);
            float targetY = baselineY + effectiveLift;

            currentLiftedY = Mathf.SmoothDamp(
                currentLiftedY, targetY,
                ref liftVelocity,
                keyboardFollowSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime
            );

            sheetRoot.anchoredPosition = new Vector2(sheetRoot.anchoredPosition.x, currentLiftedY);
        }

        private EditableField GetFocusedField()
        {
            if (nameField != null && nameField.InputField != null && nameField.InputField.isFocused) return nameField;
            if (priceField != null && priceField.InputField != null && priceField.InputField.isFocused) return priceField;
            if (descField != null && descField.InputField != null && descField.InputField.isFocused) return descField;
            return null;
        }

        private float GetKeyboardHeightCanvas()
        {
            float heightPx = EstimateKeyboardHeightPixels();
            if (heightPx <= 0f) return 0f;

            // Subtract the bottom safe-area inset (iPhone home bar / Android
            // gesture inset). The canvas is inset to the safe area, so we
            // only need to lift by the portion of the keyboard that covers
            // new canvas space. Mirrors Chat/KeyboardAwarePanel.
            float safeBottomPx = Screen.safeArea.y;
            float adjustedPx = Mathf.Max(0f, heightPx - safeBottomPx);

            float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
            float heightCanvas = adjustedPx / scale;

            // When the focused field hides its mobile input accessory bar,
            // iOS still reports the bar's height as part of area.height —
            // producing a gap the size of the (now invisible) bar above the
            // keyboard. Subtract it in canvas space.
            if (FocusedFieldHidesMobileInput())
                heightCanvas = Mathf.Max(0f, heightCanvas - hiddenMobileInputBarHeight);

            return heightCanvas;
        }

        private bool FocusedFieldHidesMobileInput()
        {
            var field = lastFocusedField != null ? lastFocusedField : GetFocusedField();
            return field != null
                && field.InputField != null
                && field.InputField.shouldHideMobileInput;
        }

        private static float EstimateKeyboardHeightPixels()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            float measured = MeasureKeyboardHeightAndroid();
            return measured > 0f ? measured : Screen.height * 0.4f;
#elif UNITY_IOS && !UNITY_EDITOR
            float area = TouchScreenKeyboard.area.height;
            return area > 0f ? area : Screen.height * 0.4f;
#else
            return 0f;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static float MeasureKeyboardHeightAndroid()
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var window = activity.Call<AndroidJavaObject>("getWindow");
                using var decorView = window.Call<AndroidJavaObject>("getDecorView");
                using var rootView = decorView.Call<AndroidJavaObject>("getRootView");

                using var visibleRect = new AndroidJavaObject("android.graphics.Rect");
                decorView.Call("getWindowVisibleDisplayFrame", visibleRect);

                int visibleBottom = visibleRect.Call<int>("bottom");
                int rootHeight = rootView.Call<int>("getHeight");
                int height = rootHeight - visibleBottom;

                return height > 100 ? height : 0f;
            }
            catch
            {
                return 0f;
            }
        }
#endif

        public void Show(ProductCardView card)
        {
            boundProduct = card;
            boundService = null;
            BindFields(card.Name, card.Price, card.Description);
            SlideIn();
        }

        public void Show(ServiceCardView card)
        {
            boundService = card;
            boundProduct = null;
            BindFields(card.Name, card.Price, card.Description);
            SlideIn();
        }

        private void BindFields(string n, string p, string d)
        {
            nameField.Value = n;
            priceField.Value = p;
            descField.Value = d;
        }

        private void SlideIn()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            isShown = true;
            lastFocusedField = null;
            mode = SheetMode.Showing;

            if (scrimBehind != null)
            {
                scrimBehind.SetActive(true);
                scrimBehindGroup.alpha = 0f;
                scrimBehindGroup.DOFade(scrimAlpha, slideDuration).SetEase(Ease.OutQuad);
            }
            sheetRoot.DOKill();
            sheetRoot.DOAnchorPos(shownAnchored, slideDuration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    mode = SheetMode.Shown;
                    currentLiftedY = baselineY;
                    liftVelocity = 0f;
                });
        }

        public void Hide()
        {
            isShown = false;
            lastFocusedField = null;
            mode = SheetMode.Hiding;
            liftVelocity = 0f;

            sheetRoot.DOKill();
            sheetRoot.DOAnchorPos(hiddenAnchored, slideDuration).SetEase(Ease.InCubic);
            if (scrimBehind != null)
            {
                scrimBehindGroup.DOFade(0f, slideDuration).SetEase(Ease.InQuad)
                    .OnComplete(() =>
                    {
                        scrimBehind.SetActive(false);
                        gameObject.SetActive(false);
                        mode = SheetMode.Hidden;
                    });
            }
            else
            {
                Invoke(nameof(FinishHide), slideDuration);
            }
            boundProduct = null;
            boundService = null;
        }

        private void FinishHide()
        {
            gameObject.SetActive(false);
            mode = SheetMode.Hidden;
        }

        private void Commit()
        {
            if (boundProduct != null)
            {
                boundProduct.Name = nameField.Value;
                boundProduct.Price = priceField.Value;
                boundProduct.Description = descField.Value;
            }
            else if (boundService != null)
            {
                boundService.Name = nameField.Value;
                boundService.Price = priceField.Value;
                boundService.Description = descField.Value;
            }
            OnAnyCommitted?.Invoke();
            Hide();
        }

        private void ConfirmDelete()
        {
            PopupUI.Hide(deleteConfirmPopup);
            if (boundProduct != null)
            {
                var card = boundProduct;
                Hide();
                OnProductDeleted?.Invoke(card);
            }
            else if (boundService != null)
            {
                var card = boundService;
                Hide();
                OnServiceDeleted?.Invoke(card);
            }
        }
    }
}
