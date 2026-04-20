using System;
using System.Collections;
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

        private Canvas canvas;
        private float baselineY;
        private bool isShown;
        private bool isLifted;
        private EditableField lastFocusedField;
        private Coroutine liftCoroutine;
        private Coroutine lowerCoroutine;
        private const float LiftDuration = 0.3f;
        private const float KeyboardOpenWait = 0.35f;
        private const float LowerGrace = 0.15f;

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
            gameObject.SetActive(false);

            doneButton.onClick.AddListener(Commit);
            deleteButton.onClick.AddListener(() => PopupUI.Show(deleteConfirmPopup));
            PopupUI.WireFingerUp(deleteConfirmYes, ConfirmDelete);
            PopupUI.WireFingerUp(deleteConfirmNo, () => PopupUI.Hide(deleteConfirmPopup));
            if (scrimBehindFinger != null)
                scrimBehindFinger.OnRealRelease += Hide;
        }

        private void Update()
        {
            if (!isShown || sheetRoot == null) return;
            var focused = GetFocusedField();
            if (focused == lastFocusedField) return;
            lastFocusedField = focused;

            if (focused != null)
            {
                // New field gained focus. Cancel any pending lower so a
                // rapid field-to-field tap doesn't cause a down-then-up blip.
                if (lowerCoroutine != null) { StopCoroutine(lowerCoroutine); lowerCoroutine = null; }
                if (!isLifted)
                {
                    if (liftCoroutine != null) StopCoroutine(liftCoroutine);
                    liftCoroutine = StartCoroutine(LiftRoutine());
                }
            }
            else
            {
                // Lost focus — wait briefly before lowering, in case another
                // field is about to gain focus on the next frame.
                if (lowerCoroutine != null) StopCoroutine(lowerCoroutine);
                lowerCoroutine = StartCoroutine(LowerRoutine());
            }
        }

        private EditableField GetFocusedField()
        {
            if (nameField != null && nameField.InputField != null && nameField.InputField.isFocused) return nameField;
            if (priceField != null && priceField.InputField != null && priceField.InputField.isFocused) return priceField;
            if (descField != null && descField.InputField != null && descField.InputField.isFocused) return descField;
            return null;
        }

        private IEnumerator LiftRoutine()
        {
            // Wait for the native keyboard to finish its slide-up animation
            // before measuring its height.
            yield return new WaitForSeconds(KeyboardOpenWait);

            float keyboardCanvas = GetKeyboardHeightCanvas();
            float targetY = keyboardCanvas > 0f ? baselineY + keyboardCanvas : baselineY;

            sheetRoot.DOKill();
            sheetRoot.DOAnchorPosY(targetY, LiftDuration).SetEase(Ease.OutCubic);
            isLifted = true;
            liftCoroutine = null;
        }

        private IEnumerator LowerRoutine()
        {
            yield return new WaitForSeconds(LowerGrace);
            sheetRoot.DOKill();
            sheetRoot.DOAnchorPosY(baselineY, LiftDuration).SetEase(Ease.OutCubic);
            isLifted = false;
            lowerCoroutine = null;
        }

        private float GetKeyboardHeightCanvas()
        {
            float heightPx = EstimateKeyboardHeightPixels();
            if (heightPx <= 0f) return 0f;
            float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
            return heightPx / scale;
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
            if (scrimBehind != null)
            {
                scrimBehind.SetActive(true);
                scrimBehindGroup.alpha = 0f;
                scrimBehindGroup.DOFade(scrimAlpha, slideDuration).SetEase(Ease.OutQuad);
            }
            sheetRoot.DOKill();
            sheetRoot.DOAnchorPos(shownAnchored, slideDuration).SetEase(Ease.OutCubic);
        }

        public void Hide()
        {
            isShown = false;
            isLifted = false;
            lastFocusedField = null;
            if (liftCoroutine != null)
            {
                StopCoroutine(liftCoroutine);
                liftCoroutine = null;
            }
            if (lowerCoroutine != null)
            {
                StopCoroutine(lowerCoroutine);
                lowerCoroutine = null;
            }
            sheetRoot.DOKill();
            sheetRoot.DOAnchorPos(hiddenAnchored, slideDuration).SetEase(Ease.InCubic);
            if (scrimBehind != null)
            {
                scrimBehindGroup.DOFade(0f, slideDuration).SetEase(Ease.InQuad)
                    .OnComplete(() =>
                    {
                        scrimBehind.SetActive(false);
                        gameObject.SetActive(false);
                    });
            }
            else
            {
                Invoke(nameof(Deactivate), slideDuration);
            }
            boundProduct = null;
            boundService = null;
        }

        private void Deactivate() => gameObject.SetActive(false);

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
