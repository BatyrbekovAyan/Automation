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
        // Set true by HandleFieldBlurred when any field blurs. While true,
        // Update() ignores positive keyboard-height readings — iOS keeps
        // reporting the keyboard as visible (and TouchScreenKeyboard.area
        // sometimes flickers to non-zero) during its ~250 ms dismissal
        // animation, which would yo-yo the sheet back up and undo the bypass.
        // Cleared by HandleFieldSelected when any field re-focuses, or by the
        // floor-protected rawKeyboard <= 0 check in Update once at least
        // suppressionFloorSeconds has elapsed since the bypass started.
        private bool dismissingKeyboard;
        private float dismissalStartTime;
        // Minimum time the bypass stays active before rawKeyboard <= 0 is
        // allowed to clear it. Covers the worst-case iOS dismissal animation
        // (~250 ms) plus a margin so flicker readings can't end the bypass
        // mid-animation. Re-focus (Selected event) clears it immediately
        // regardless of this floor, so re-tapping the same field still works.
        private const float suppressionFloorSeconds = 0.3f;
        // Frame in which HandleFieldSelected last fired. Used to skip setting
        // the bypass in HandleFieldBlurred if a Select happened in the same
        // frame — that means a field-switch is in progress and the keyboard
        // stays up, so we must NOT start a dismissal. Handles both event
        // orderings (Blur-then-Select AND Select-then-Blur) symmetrically:
        // if Select fires first, Blur bails via this guard; if Blur fires
        // first, HandleFieldSelected clears dismissingKeyboard after.
        private int lastSelectedFrame = -1;
        // The field most recently selected. Paired with lastSelectedFrame
        // to detect TMP_InputField's iOS quirk where a synthetic OnSubmit
        // fires on the newly focused field one frame after a field-switch
        // (the keyboard's resignFirstResponder/becomeFirstResponder dance
        // gets routed to whoever's currently selected via OnSubmit). Blur
        // on the just-selected field within selectRecencyFramesForBlurSkip
        // frames is treated as this artifact and skipped.
        private EditableField lastSelectedField;
        private const int selectRecencyFramesForBlurSkip = 2;
        // Which field most recently fired Blurred. Used by Update()'s
        // focus-regain fallback to distinguish a real field-switch (another
        // field is focused — clear bypass) from stale input.isFocused reports
        // on the same field after Blur (leave bypass alone).
        private EditableField lastBlurredField;

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

            // Subscribe to field blur events to set the dismissal bypass when
            // the user dismisses the keyboard, and to field selected events to
            // clear the bypass when the user re-focuses any field. The pair
            // gives us a clean state-machine driven entirely by user-action
            // signals — no reliance on iOS's lying TouchScreenKeyboard polling.
            if (nameField != null) { nameField.Blurred += HandleFieldBlurred; nameField.Selected += HandleFieldSelected; }
            if (priceField != null) { priceField.Blurred += HandleFieldBlurred; priceField.Selected += HandleFieldSelected; }
            if (descField != null) { descField.Blurred += HandleFieldBlurred; descField.Selected += HandleFieldSelected; }
        }

        private void OnDestroy()
        {
            if (nameField != null) { nameField.Blurred -= HandleFieldBlurred; nameField.Selected -= HandleFieldSelected; }
            if (priceField != null) { priceField.Blurred -= HandleFieldBlurred; priceField.Selected -= HandleFieldSelected; }
            if (descField != null) { descField.Blurred -= HandleFieldBlurred; descField.Selected -= HandleFieldSelected; }
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

            // Field-switch fallback: if a *different* field is currently
            // focused while dismissingKeyboard is set, the user tapped another
            // input and TMP_InputField on iOS failed to fire onSelect on the
            // new field (known quirk). EventSystem moved the selection so
            // input.isFocused reads true on B, but neither HandleFieldSelected
            // nor the same-frame guard caught it. Clear the bypass before the
            // descent tween gets issued, avoiding the yo-yo.
            // Same-field focused (== lastBlurredField) is ignored here — it's
            // either a stale-true read or a genuine re-tap, both handled by
            // the synthesize-Select path in EditableField.Update.
            if (dismissingKeyboard && focused != null && focused != lastBlurredField)
            {
                Debug.Log($"[KB] dismissingKeyboard CLEARED via Update focus-regain " +
                          $"(focused={focused.name}, lastBlurred={(lastBlurredField != null ? lastBlurredField.name : "null")}) f={Time.frameCount}");
                dismissingKeyboard = false;
            }

            // Debounce the "keyboard is down" signal. During a field-to-field
            // focus switch, the OS briefly reports the keyboard as gone for
            // 1–3 frames even though it stays visible to the user. Hold the
            // last positive height until the OS has reported zero for longer
            // than keyboardDownConfirmSeconds.
            float rawKeyboard = GetKeyboardHeightCanvas();
            if (dismissingKeyboard)
            {
                // HandleFieldBlurred zeroed heldKeyboardHeight already. Do NOT
                // refill from polling — iOS keeps reporting a non-zero height
                // (and on subsequent dismissals, briefly flickers between 0
                // and non-zero) during the ~250 ms dismissal animation, which
                // would yo-yo the sheet right back up. Hold zero until either
                // (a) HandleFieldSelected fires (user re-focused a field —
                // immediate clear) or (b) suppressionFloorSeconds has elapsed
                // AND rawKeyboard reads zero. The floor protects against
                // mid-animation flicker readings ending the bypass early.
                if (rawKeyboard <= 0f &&
                    Time.unscaledTime - dismissalStartTime > suppressionFloorSeconds)
                {
                    dismissingKeyboard = false;
                    Debug.Log($"[KB] dismissingKeyboard CLEARED via floor+rawKB<=0 " +
                              $"f={Time.frameCount} elapsed={Time.unscaledTime - dismissalStartTime:F3}");
                }
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
                Debug.Log($"[KB] TWEEN issued target={target:F1} prev={activeTargetY:F1} " +
                          $"focused={(focused != null ? focused.name : "null")} " +
                          $"dismissingKB={dismissingKeyboard} rawKB={rawKeyboard:F1} " +
                          $"heldKB={heldKeyboardHeight:F1} f={Time.frameCount}");
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
            lastBlurredField = null;
            lastSelectedFrame = -1;
            lastSelectedField = null;
            // Reset the debounce so a freshly-shown sheet doesn't inherit a
            // "held" keyboard height from a previous session.
            heldKeyboardHeight = 0f;
            lastPositiveKeyboardTime = float.NegativeInfinity;
            dismissingKeyboard = false;
            dismissalStartTime = float.NegativeInfinity;
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
        // Sets dismissingKeyboard immediately and zeros the height state so the
        // very next Update() tweens the sheet toward baselineY without waiting
        // for the OS height-debounce or for iOS's lying TouchScreenKeyboard.area
        // to drop. Skips the bypass entirely if a Select happened in the same
        // frame — that's a field-switch, keyboard stays up, sheet must stay.
        private void HandleFieldBlurred(EditableField blurred)
        {
            Debug.Log($"[KB] HandleFieldBlurred({blurred.name}) mode={mode} " +
                      $"f={Time.frameCount} lastSelF={lastSelectedFrame} " +
                      $"dismissingKB={dismissingKeyboard} heldKB={heldKeyboardHeight}");
            if (mode != SheetMode.Shown) return;
            // Same-frame guard: old field's blur during a field-switch.
            if (Time.frameCount == lastSelectedFrame)
            {
                Debug.Log($"[KB] HandleFieldBlurred SKIPPED (same frame as Select — field-switch)");
                return;
            }
            // iOS field-switch-OnSubmit artifact guard: when the user switches
            // from input A to input B on iOS, TMP_InputField fires a synthetic
            // OnSubmit on B one frame after B's Select. OnSubmit calls
            // DeactivateInputField → onEndEdit → Blur, and the spurious Blur
            // on the newly focused field trips the bypass, which starts a
            // descent before EditableField.Update can synthesize a Select back.
            // Suppress Blurs that hit the just-selected field within a few
            // frames — physically impossible for a human to press Done that
            // fast (needs 100+ ms / 6+ frames of reaction time).
            int framesSinceSelect = Time.frameCount - lastSelectedFrame;
            if (blurred == lastSelectedField && framesSinceSelect <= selectRecencyFramesForBlurSkip)
            {
                Debug.Log($"[KB] HandleFieldBlurred SKIPPED (iOS field-switch OnSubmit on just-selected field, " +
                          $"framesSinceSelect={framesSinceSelect})");
                return;
            }
            lastBlurredField = blurred;
            heldKeyboardHeight = 0f;
            lastPositiveKeyboardTime = float.NegativeInfinity;
            dismissingKeyboard = true;
            dismissalStartTime = Time.unscaledTime;
            Debug.Log($"[KB] dismissingKeyboard SET TRUE at t={Time.unscaledTime:F3}");
        }

        // Called when any of the three EditableFields fires its Selected event.
        // The user tapped a field — they want the keyboard back. Clear the
        // dismissal bypass so Update()'s polling can refill heldKeyboardHeight
        // and lift the sheet. Also records the frame + field so
        // HandleFieldBlurred can detect both same-frame field-switches and
        // the iOS OnSubmit-on-newly-selected-field artifact.
        private void HandleFieldSelected(EditableField selected)
        {
            Debug.Log($"[KB] HandleFieldSelected({selected.name}) mode={mode} " +
                      $"f={Time.frameCount} dismissingKB={dismissingKeyboard}");
            if (mode != SheetMode.Shown) return;
            lastSelectedFrame = Time.frameCount;
            lastSelectedField = selected;
            if (dismissingKeyboard)
            {
                dismissingKeyboard = false;
                Debug.Log($"[KB] dismissingKeyboard CLEARED via Select");
            }
        }
    }
}
