using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Keyboard handling for a scrollable form whose fields edit INLINE —
    /// the products-sheet model: no scrim, no raise, direct field-to-field
    /// focus switches. DeferredDismissInputField keeps the OS keyboard up
    /// across switches and the unified MultiLineSubmit line types keep the
    /// native input view type stable, so the IME session never restarts —
    /// that restart was the cross-field text-corruption window the scrim's
    /// forced dismiss/reopen cycle kept hitting.
    ///
    /// When the focused field's card is covered by the keyboard (decided
    /// once per focus from a SETTLED keyboard height), the form gains bottom
    /// padding and scrolls the card to clearance above the keyboard — once,
    /// so the user keeps manual scroll control afterwards. Fields already
    /// visible above the keyboard trigger nothing at all.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class FormKeyboardScroll : MonoBehaviour
    {
        [SerializeField] private float clearance = 32f;
        [SerializeField] private float releaseHoldSeconds = 0.25f;
        [SerializeField] private float scrollSeconds = 0.15f;

        // Frames the measured keyboard height must hold steady before the
        // covered/visible decision is made — deciding during the keyboard's
        // rise (or its suggestion-strip overshoot) scrolls borderline fields
        // and drops them back.
        private const int SettleFrames = 4;

        private ScrollRect scroll;
        private RectTransform content;
        private VerticalLayoutGroup layout;
        private Canvas rootCanvas;
        private RectTransform canvasRect;
        private TMP_InputField[] inputs;
        private readonly Vector3[] corners = new Vector3[4];

        private int originalBottomPadding;
        private bool paddingApplied;

        private TMP_InputField activeInput;
        private float lastFocusSeenTime;
        private bool coveredLatched;
        private bool scrollApplied;
        private float lastKeyboard;
        private int stableFrames;
        private Tween scrollTween;

        private void Awake()
        {
            scroll = GetComponent<ScrollRect>();
            content = scroll.content;
            layout = content != null ? content.GetComponent<VerticalLayoutGroup>() : null;
            var parentCanvas = GetComponentInParent<Canvas>();
            rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            canvasRect = rootCanvas != null ? (RectTransform)rootCanvas.transform : null;
        }

        private void OnEnable()
        {
            inputs = GetComponentsInChildren<TMP_InputField>(true);
            if (layout != null) originalBottomPadding = layout.padding.bottom;
            paddingApplied = false;
            ClearActive();
        }

        private void OnDisable()
        {
            scrollTween?.Kill();
            RestorePadding();
            ClearActive();
        }

        private void Update()
        {
            if (content == null || canvasRect == null) return;

            var focused = FindFocusedChildInput();
            if (focused != null)
            {
                if (focused != activeInput) BeginTracking(focused);
                lastFocusSeenTime = Time.unscaledTime;
            }
            else if (activeInput != null
                     && Time.unscaledTime - lastFocusSeenTime > releaseHoldSeconds)
            {
                // Genuine dismiss — the hold bridges the 1-3 frame focus
                // flicker of a direct field-to-field switch.
                RestorePadding();
                ClearActive();
            }

            if (activeInput == null) return;

            var keyboard = KeyboardLiftMath.ScreenPxToCanvas(
                KeyboardInset.OccludedScreenPixels(),
                safeAreaBottomPx: 0f,
                isOverlay: rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay,
                scaleFactor: rootCanvas.scaleFactor,
                canvasHeight: canvasRect.rect.height,
                screenHeight: Screen.height);

            if (!coveredLatched) TryLatch(keyboard);
            if (!coveredLatched) return;

            ApplyPadding(keyboard);
            if (!scrollApplied) ScrollClear(keyboard);
        }

        private void BeginTracking(TMP_InputField input)
        {
            activeInput = input;
            coveredLatched = false;
            scrollApplied = false;
            stableFrames = 0;
            lastKeyboard = 0f;
        }

        private void ClearActive()
        {
            activeInput = null;
            coveredLatched = false;
            scrollApplied = false;
            stableFrames = 0;
            lastKeyboard = 0f;
        }

        private TMP_InputField FindFocusedChildInput()
        {
            if (inputs == null) return null;
            foreach (var input in inputs)
            {
                if (input != null && input.isFocused) return input;
            }
            return null;
        }

        private void TryLatch(float keyboard)
        {
            if (keyboard <= 0f)
            {
                stableFrames = 0;
                lastKeyboard = 0f;
                return;
            }

            if (Mathf.Abs(keyboard - lastKeyboard) < 1f) stableFrames++;
            else stableFrames = 0;
            lastKeyboard = keyboard;

            if (stableFrames < SettleFrames) return;
            if (CardBottomY() < keyboard) coveredLatched = true;
        }

        // The focused field's CARD bottom (the whole EditableField, so the
        // rounded container clears the keyboard, not just the text) in
        // canvas units from the canvas bottom.
        private float CardBottomY()
        {
            var card = activeInput.GetComponentInParent<EditableField>();
            var rect = card != null ? (RectTransform)card.transform
                                    : (RectTransform)activeInput.transform;
            rect.GetWorldCorners(corners);
            return canvasRect.InverseTransformPoint(corners[0]).y
                   + canvasRect.rect.height * canvasRect.pivot.y;
        }

        private void ApplyPadding(float keyboard)
        {
            if (layout == null || paddingApplied) return;

            var pad = layout.padding;
            layout.padding = new RectOffset(
                pad.left, pad.right, pad.top,
                originalBottomPadding + Mathf.CeilToInt(keyboard));
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            paddingApplied = true;
        }

        private void RestorePadding()
        {
            if (!paddingApplied || layout == null)
            {
                paddingApplied = false;
                return;
            }

            var pad = layout.padding;
            layout.padding = new RectOffset(pad.left, pad.right, pad.top, originalBottomPadding);
            if (content.gameObject.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            paddingApplied = false;
        }

        // One-shot: scrolls the covered card to clearance above the keyboard,
        // then leaves the scroll position to the user.
        private void ScrollClear(float keyboard)
        {
            scrollApplied = true;

            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            var scrollable = content.rect.height - viewport.rect.height;
            var delta = KeyboardLiftMath.ScrollDeltaNormalized(
                CardBottomY(), keyboard, clearance, scrollable);
            if (delta <= 0f) return;

            var target = Mathf.Clamp01(scroll.verticalNormalizedPosition - delta);
            scrollTween?.Kill();
            scrollTween = DOTween.To(
                () => scroll.verticalNormalizedPosition,
                value => scroll.verticalNormalizedPosition = value,
                target, scrollSeconds).SetEase(Ease.OutCubic).SetUpdate(true);
        }
    }
}
