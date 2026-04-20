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
        [SerializeField] private Button scrimBehindButton;  // tap-outside to close
        [SerializeField] private float slideDuration = 0.25f;
        [SerializeField] private float scrimAlpha = 0.5f;

        private Canvas canvas;
        private float baselineY;
        private bool isShown;
        private EditableField lastFocusedField;
        private const float LiftDuration = 0.3f;
        private const float LiftMargin = 24f;

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
            if (scrimBehindButton != null)
                scrimBehindButton.onClick.AddListener(Hide);
        }

        private void Update()
        {
            if (!isShown || sheetRoot == null) return;
            var focused = GetFocusedField();
            if (focused != lastFocusedField)
            {
                lastFocusedField = focused;
                AnimateLiftFor(focused);
            }
        }

        private EditableField GetFocusedField()
        {
            if (nameField != null && nameField.InputField != null && nameField.InputField.isFocused) return nameField;
            if (priceField != null && priceField.InputField != null && priceField.InputField.isFocused) return priceField;
            if (descField != null && descField.InputField != null && descField.InputField.isFocused) return descField;
            return null;
        }

        private void AnimateLiftFor(EditableField field)
        {
            float targetY = baselineY;
            if (field != null)
            {
                float keyboardCanvas = GetKeyboardHeightCanvas();
                if (keyboardCanvas > 0f)
                {
                    var fieldRect = field.GetComponent<RectTransform>();
                    if (fieldRect != null)
                    {
                        var worldCorners = new Vector3[4];
                        fieldRect.GetWorldCorners(worldCorners);
                        var localBottom = sheetRoot.InverseTransformPoint(worldCorners[0]);
                        float fieldBottomInSheet = localBottom.y; // sheet pivot is at bottom
                        float lift = Mathf.Max(0f, keyboardCanvas - fieldBottomInSheet + LiftMargin);
                        targetY = baselineY + lift;
                    }
                }
            }
            sheetRoot.DOKill();
            sheetRoot.DOAnchorPosY(targetY, LiftDuration).SetEase(Ease.OutCubic);
        }

        private float GetKeyboardHeightCanvas()
        {
            if (!TouchScreenKeyboard.isSupported) return 0f;
            float heightPx = TouchScreenKeyboard.area.height;
            if (heightPx <= 0f) return 0f;
            float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
            return heightPx / scale;
        }

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
