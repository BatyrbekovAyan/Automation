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
        [Tooltip("Canvas-unit reduction applied to the measured keyboard height. " +
                 "iOS still reports the (now invisible) accessory bar's height in " +
                 "TouchScreenKeyboard.area.height when fields have 'Hide Mobile Input' " +
                 "enabled, so subtract it here. ~44 at reference 1x; tune if your " +
                 "canvas scale differs or the lift overshoots on device.")]
        [SerializeField] private float liftReduction = 44f;

        [Tooltip("DOTween duration (s) for keyboard-follow moves. Each time the measured " +
                 "keyboard height changes, the sheet retweens to the new target over this " +
                 "duration with an OutQuad ease. Shorter = snappier but potentially jittery; " +
                 "longer = smoother but laggier. 0.10–0.12 matches the native keyboard feel.")]
        [SerializeField] private float keyboardFollowDuration = 0.1f;

        [Tooltip("Seconds the OS must consistently report the keyboard as down before " +
                 "we actually drop the sheet. Absorbs the 1-3 frame blip where " +
                 "TouchScreenKeyboard.visible / the Android IME measurement briefly " +
                 "reports 0 between DeactivateInputField on field A and ActivateInputField " +
                 "on field B. Without this the sheet dips visibly on every field switch.")]
        [SerializeField] private float keyboardDownConfirmSeconds = 0.15f;

        private enum SheetMode { Hidden, Showing, Shown, Hiding }

        private Canvas canvas;
        private float baselineY;
        private bool isShown;
        private EditableField lastFocusedField;
        private SheetMode mode = SheetMode.Hidden;
        private float heldKeyboardHeight;
        private float lastPositiveKeyboardTime;
        // Tracks the target the active sheet-position tween is aiming at, so
        // we only kill-and-reissue a tween when the target actually shifts —
        // otherwise every Update would churn the tween and progress resets.
        private float activeTargetY = float.NaN;
        private Tween activePosTween;
        // Coroutine handle for the one-frame post-blur check. We hold it so a
        // rapid second blur cancels the first check (only the most recent matters).
        private Coroutine pendingDismissCheck;
        // Set true by the dismissal coroutine after it confirms an explicit
        // keyboard dismissal (Done key / outside-tap). While true, Update()
        // ignores positive height readings — iOS reports the keyboard as still
        // visible during its ~250 ms dismissal animation, which would yo-yo the
        // sheet back up and undo the bypass. Cleared when the OS finally agrees
        // the keyboard is down (rawKeyboard <= 0) or when a field re-focuses.
        private bool dismissingKeyboard;

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

            // Subscribe to field blur events so an explicit keyboard-Done dismissal
            // can bypass the 0.15 s height-debounce and descend in sync with the
            // keyboard. See HandleFieldBlurred for the focus-check logic.
            if (nameField != null) nameField.Blurred += HandleFieldBlurred;
            if (priceField != null) priceField.Blurred += HandleFieldBlurred;
            if (descField != null) descField.Blurred += HandleFieldBlurred;
        }

        private void OnDestroy()
        {
            if (nameField != null) nameField.Blurred -= HandleFieldBlurred;
            if (priceField != null) priceField.Blurred -= HandleFieldBlurred;
            if (descField != null) descField.Blurred -= HandleFieldBlurred;
        }

        private void Update()
        {
            if (sheetRoot == null || mode != SheetMode.Shown) return;

            // Sticky focus tracking: only overwrite when a field is actually
            // focused. When the user taps from one input to another, a few
            // frames report no focused field while the OS keyboard is still
            // up; clearing lastFocusedField in that gap would cause a visible
            // dip. Stickiness keeps the lift stable across field switches.
            var focused = GetFocusedField();
            if (focused != null) lastFocusedField = focused;

            // If a field re-focused mid-dismissal (user tapped another input
            // while the keyboard was animating away), abandon the dismissal
            // suppression and let normal polling lift the sheet again.
            if (dismissingKeyboard && focused != null) dismissingKeyboard = false;

            // Debounce the "keyboard is down" signal. During a field-to-field
            // focus switch, the OS briefly reports the keyboard as gone for
            // 1–3 frames even though it stays visible to the user. Hold the
            // last positive height until the OS has reported zero for longer
            // than keyboardDownConfirmSeconds.
            float rawKeyboard = GetKeyboardHeightCanvas();
            if (dismissingKeyboard)
            {
                // The dismissal coroutine zeroed heldKeyboardHeight already.
                // Do NOT re-fill from polling — iOS keeps reporting a non-zero
                // height during the ~250 ms dismissal animation, which would
                // yo-yo the sheet right back up. Hold zero until the OS finally
                // confirms the keyboard is fully down.
                if (rawKeyboard <= 0f) dismissingKeyboard = false;
            }
            else if (rawKeyboard > 0f)
            {
                heldKeyboardHeight = rawKeyboard;
                lastPositiveKeyboardTime = Time.unscaledTime;
            }
            else if (Time.unscaledTime - lastPositiveKeyboardTime > keyboardDownConfirmSeconds)
            {
                heldKeyboardHeight = 0f;
            }

            float effectiveLift = Mathf.Max(0f, heldKeyboardHeight - liftReduction);
            float target = baselineY + effectiveLift;

            // Only reissue the tween when the target has meaningfully shifted.
            // This prevents per-frame kill/restart that would stutter the
            // OutQuad ease into a linear-looking motion.
            if (!Mathf.Approximately(activeTargetY, target))
            {
                activeTargetY = target;
                activePosTween?.Kill();
                activePosTween = sheetRoot.DOAnchorPosY(target, keyboardFollowDuration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(UpdateType.Normal, isIndependentUpdate: true);
            }
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
            // EstimateKeyboardHeightPixels now returns 0 whenever the OS
            // reports the keyboard is down, so no focus-based gate is
            // needed here. The previous gate ("if lastFocusedField == null
            // return 0") caused the sheet to drop during field-to-field
            // focus switches where the keyboard actually stays up.
            float heightPx = EstimateKeyboardHeightPixels();
            if (heightPx <= 0f) return 0f;

            // Subtract the bottom safe-area inset (iPhone home bar / Android
            // gesture inset). The canvas is inset to the safe area, so we
            // only need to lift by the portion of the keyboard that covers
            // new canvas space. Mirrors Chat/KeyboardAwarePanel.
            float safeBottomPx = Screen.safeArea.y;
            float adjustedPx = Mathf.Max(0f, heightPx - safeBottomPx);

            float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
            return adjustedPx / scale;
        }

        private static float EstimateKeyboardHeightPixels()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // JNI measurement returns 0 whenever the keyboard is down, so
            // no fallback is needed. Firing the 0.4 * Screen.height fallback
            // when the keyboard is genuinely down would push the sheet off
            // screen whenever the user tapped outside or pressed "Done".
            return MeasureKeyboardHeightAndroid();
#elif UNITY_IOS && !UNITY_EDITOR
            // Gate the fallback on TouchScreenKeyboard.visible so it only
            // fires while the OS keyboard is actually on screen.
            if (!TouchScreenKeyboard.visible) return 0f;
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
            // Reset the debounce so a freshly-shown sheet doesn't inherit a
            // "held" keyboard height from a previous session.
            heldKeyboardHeight = 0f;
            lastPositiveKeyboardTime = float.NegativeInfinity;
            dismissingKeyboard = false;
            mode = SheetMode.Showing;
            // Clear the Update pipeline's memory of any prior tween target
            // so the first frame after slide-in issues a fresh tween if the
            // keyboard is already (or still) on screen.
            activeTargetY = float.NaN;

            if (scrimBehind != null)
            {
                scrimBehind.SetActive(true);
                scrimBehindGroup.alpha = 0f;
                scrimBehindGroup.DOKill();
                scrimBehindGroup.DOFade(scrimAlpha, slideDuration).SetEase(Ease.OutQuad);
            }

            activePosTween?.Kill();
            activePosTween = sheetRoot.DOAnchorPos(shownAnchored, slideDuration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    mode = SheetMode.Shown;
                    activeTargetY = shownAnchored.y;
                    activePosTween = null;
                });
        }

        public void Hide()
        {
            isShown = false;
            lastFocusedField = null;
            mode = SheetMode.Hiding;
            activeTargetY = float.NaN;

            // Start the slide-off IMMEDIATELY on the same frame as the user's
            // tap. The sheet and keyboard then begin descending together.
            //
            // Trying to drive this via Update + keyboard-height polling
            // doesn't work: iOS's TouchScreenKeyboard.area.height stays at
            // full value during the dismissal animation and only jumps to 0
            // after the keyboard is already off-screen, which is exactly the
            // "sheet starts sliding only after keyboard is gone" bug. On
            // Android the height animates, but the down-confirm debounce
            // window (needed to absorb field-switch blips in Shown mode) adds
            // its own delay. Committing to the off-screen target here and
            // letting DOTween drive it is the only reliable way to sync the
            // start moment with the keyboard dismissal across platforms.
            activePosTween?.Kill();
            activePosTween = sheetRoot.DOAnchorPos(hiddenAnchored, slideDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(UpdateType.Normal, isIndependentUpdate: true)
                .OnComplete(() =>
                {
                    gameObject.SetActive(false);
                    mode = SheetMode.Hidden;
                    activePosTween = null;
                });

            // Scrim fade runs in parallel on its own timeline — purely
            // cosmetic backdrop so it doesn't need to wait on the sheet.
            if (scrimBehind != null)
            {
                scrimBehindGroup.DOKill();
                scrimBehindGroup.DOFade(0f, slideDuration).SetEase(Ease.InQuad)
                    .OnComplete(() => scrimBehind.SetActive(false));
            }

            boundProduct = null;
            boundService = null;
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

        // Called when any of the three EditableFields fires its Blurred event.
        // Schedules a one-frame check: if no field gains focus, the user
        // dismissed the keyboard (Done key, outside-tap), so we drop the sheet
        // immediately instead of waiting for the OS keyboard-height debounce.
        private void HandleFieldBlurred()
        {
            if (mode != SheetMode.Shown) return;
            if (pendingDismissCheck != null) StopCoroutine(pendingDismissCheck);
            pendingDismissCheck = StartCoroutine(CheckDismissalNextFrame());
        }

        private IEnumerator CheckDismissalNextFrame()
        {
            // Yield one frame so any same-frame field-switch select() handlers
            // can mark the next field focused before we read focus state.
            yield return null;
            pendingDismissCheck = null;

            // If we're no longer Shown (e.g., Hide() ran during the wait),
            // skip — the sheet is already animating off.
            if (mode != SheetMode.Shown) yield break;

            // If another field grabbed focus, this was a field-switch, not a
            // dismissal. Leave the existing height-debounce path in charge.
            if (GetFocusedField() != null) yield break;

            // Explicit dismissal: bypass the 0.15 s height-debounce so Update()
            // retweens toward baselineY on the next tick using the existing
            // keyboardFollowDuration / OutQuad path. Also raise the
            // dismissingKeyboard flag so Update() ignores positive height
            // readings during the OS dismissal animation — without this, iOS
            // refills heldKeyboardHeight from its still-positive area.height
            // and the sheet yo-yos back up.
            heldKeyboardHeight = 0f;
            lastPositiveKeyboardTime = float.NegativeInfinity;
            dismissingKeyboard = true;
        }
    }
}
