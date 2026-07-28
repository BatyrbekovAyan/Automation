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

        // Keyboard lift. Deliberately float-only: a missing float key in the
        // shipped prefab falls back to the initializer here, so this component
        // needs no re-stamp (a new SerializeField OBJECT reference would
        // deserialize null until a builder ran, and the full BotSettings
        // rebuild is destructive — see BusinessContactFieldsBuilder).
        // Settle margin for a covered field (canvas units). Tuned on device
        // over two rounds: 48 let the next card's label peek above the
        // keyboard, 40 was still "too high" by the owner's eye — 32 lands the
        // field right above the keyboard as requested.
        [SerializeField] private float keyboardClearance = 32f;
        [SerializeField] private float liftSmoothTime = 0.10f;

        private RectTransform raisedField;
        private Transform originalParent;
        private int originalSiblingIndex;
        private RectTransform placeholder;
        private DelayedFingerUpAction fingerUp;
        private Action onOutsideTapCached;

        private Canvas rootCanvas;
        private RectTransform canvasRect;
        private readonly Vector3[] corners = new Vector3[4];

        // The tab the raised field came from. When it scrolls, the whole form
        // moves to clear the keyboard and the raised card rides along with its
        // placeholder — so the focused card never floats alone above a frozen
        // page. Null for a non-scrolling tab (Промпт), which falls back to
        // lifting the card on its own.
        private ScrollRect ownerScroll;
        private VerticalLayoutGroup ownerLayout;
        private int originalBottomPadding;
        private bool paddingApplied;
        private bool preservePaddingOnce;

        // Coverage latch. The decision "is this field's rest slot under the
        // keyboard" is made once per raise, and only from a SETTLED keyboard
        // height: the keyboard's rise (and its transient suggestion-strip
        // overshoot) crosses borderline slots mid-animation, which scrolled a
        // visible field a little and dropped it back (Часы работы bounce).
        // The latch may escalate to covered later (a suggestion strip can
        // genuinely cover a borderline field) but never reverts mid-raise.
        private bool coveredLatched;
        private float lastKeyboardCanvas;
        private int keyboardStableFrames;
        private const int CoveredDecisionFrames = 4;

        // Captured once per raise, with the field at rest. The field's own
        // transform is never written (we move raisedLayer instead), so its
        // geometry is constant for the whole raise — no per-frame re-measure,
        // and the lift can't chase its own movement.
        private float restBottomY;
        private float slotStartY;
        private float maxLift;
        private float liftY;
        private float liftVelocity;

        public bool IsShowing { get; private set; }

        /// <summary>The field currently raised, so callers can check ownership.</summary>
        public RectTransform RaisedField => raisedField;

        private void Awake()
        {
            var parentCanvas = GetComponentInParent<Canvas>();
            rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            canvasRect = rootCanvas != null
                ? rootCanvas.GetComponent<RectTransform>()
                : null;
        }

        private void OnDisable()
        {
            if (IsShowing)
                Hide();
        }

        public void Show(RectTransform field, Action onOutsideTap)
        {
            // Handoff (field-to-field switch while already showing): keep the
            // dim up without restarting its fade, and carry the keyboard
            // padding across the Hide/Show pair when the new field lives in
            // the same form — otherwise the content shrinks for a frame and
            // the scroll position jumps between fields.
            var wasShowing = IsShowing;
            preservePaddingOnce = wasShowing && field != null && field.parent == originalParent;

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

            // Zero any residual lift BEFORE reparenting. worldPositionStays
            // preserves the field's world position, so raising onto a still-
            // lifted layer would place the card at rest and then drag it as
            // the layer settles. Reachable whenever a previous raise ended
            // without a layout rebuild (swipe-back closes settings mid-lift:
            // SwipeBack raycasts above the scrim).
            RestoreKeyboardPadding();
            ResetLift();

            field.SetParent(raisedLayer, worldPositionStays: true);
            field.SetAsLastSibling();

            ownerScroll = originalParent != null
                ? originalParent.GetComponentInParent<ScrollRect>()
                : null;
            ownerLayout = originalParent != null
                ? originalParent.GetComponent<VerticalLayoutGroup>()
                : null;

            // Lay the placeholder out into the field's real slot NOW. It was
            // created this frame with default (centered) anchors, so reading
            // it before a layout pass returns the middle of the form — the
            // captured slot start would then be wrong and TrackSlot would
            // teleport the raised card on the next frame, yanking it out from
            // under the finger mid-tap. That cancels the click, so the input
            // never activates and the keyboard never opens.
            if (originalParent is RectTransform formRect && formRect.gameObject.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(formRect);

            CaptureLiftGeometry(field);

            scrimRoot.SetActive(true);
            scrimGroup.DOKill();
            // On a handoff the dim is already up — restarting the fade from 0
            // would flash the whole page bright for a frame.
            if (!wasShowing) scrimGroup.alpha = 0f;
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

            // Must run before the SetParent below, and must live HERE rather
            // than at a call site: Hide() has three entry points (blur, the
            // re-entrant guard in Show, and OnDisable), and the OnDisable path
            // fires with the owning tab already deactivated — no layout
            // rebuild will correct a lift left behind.
            RestoreKeyboardPadding();
            ResetLift();

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
                // Detach BEFORE the deferred Destroy: the dying placeholder
                // stays a layout child until end-of-frame, so a fast re-raise
                // in the same frame would lay the form out with BOTH the old
                // and new placeholder present — capturing a slot one card off
                // and displacing the raised field inside the page.
                placeholder.SetParent(null, worldPositionStays: false);
                Destroy(placeholder.gameObject);
                placeholder = null;
            }

            raisedField = null;
            originalParent = null;
            IsShowing = false;
        }

        // Every tap outside the raised field dismisses — including taps on
        // other cards (the user re-taps to focus the next field). A raycast
        // handoff that transferred focus directly was tried and did not
        // behave on device; the dismiss semantics are the accepted UX, and
        // the IME text-bleed that the dismiss/reopen cycle can cause is
        // guarded at the input layer instead (KeyboardTextBleedGuard).
        private void HandleOutsideTap()
        {
            onOutsideTapCached?.Invoke();
        }

        // Raises the layer just enough to clear the keyboard. No field-switch
        // hold window is needed here (unlike ItemEditSheet / FocusedField-
        // KeyboardLift): while showing, the scrim blocks raycasts to every
        // other card, so a tap elsewhere hits the scrim and blurs first. Only
        // one field is ever tappable, so there is no A→B focus handoff whose
        // keyboard blip would need bridging.
        private void Update()
        {
            if (!IsShowing || raisedLayer == null || canvasRect == null) return;

            var keyboardCanvas = KeyboardLiftMath.ScreenPxToCanvas(
                KeyboardInset.OccludedScreenPixels(),
                safeAreaBottomPx: 0f, // Overlay canvas spans the full screen.
                isOverlay: rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay,
                scaleFactor: rootCanvas.scaleFactor,
                canvasHeight: canvasRect.rect.height,
                screenHeight: Screen.height);

            if (ownerScroll != null)
            {
                UpdateCoveredLatch(keyboardCanvas);
                ApplyKeyboardPadding(coveredLatched ? keyboardCanvas : 0f);
                if (coveredLatched) ScrollSlotClear(keyboardCanvas);
                TrackSlot();
                return;
            }

            // Non-scrolling tab: nothing to scroll, so lift the card itself.
            var target = KeyboardLiftMath.RequiredLift(
                restBottomY, keyboardCanvas, keyboardClearance, maxLift);
            liftY = Mathf.SmoothDamp(
                liftY, target, ref liftVelocity, liftSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
            raisedLayer.anchoredPosition = new Vector2(raisedLayer.anchoredPosition.x, liftY);
        }

        // Grow the form's bottom padding by the keyboard height so the LAST
        // card still has somewhere to scroll to. Restored in Hide().
        private void ApplyKeyboardPadding(float keyboardCanvas)
        {
            if (ownerLayout == null) return;

            var wanted = keyboardCanvas > 0f;
            if (wanted == paddingApplied) return;

            var pad = ownerLayout.padding;
            ownerLayout.padding = new RectOffset(
                pad.left, pad.right, pad.top,
                wanted ? originalBottomPadding + Mathf.CeilToInt(keyboardCanvas)
                       : originalBottomPadding);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)originalParent);
            paddingApplied = wanted;
        }

        // Scrolls the form so the focused field's slot clears the keyboard.
        // Every card moves together — the raised one included, via TrackSlot.
        private void ScrollSlotClear(float keyboardCanvas)
        {
            if (placeholder == null || ownerScroll.content == null) return;

            var viewport = ownerScroll.viewport != null
                ? ownerScroll.viewport
                : (RectTransform)ownerScroll.transform;
            var scrollable = ownerScroll.content.rect.height - viewport.rect.height;

            var delta = KeyboardLiftMath.ScrollDeltaNormalized(
                SlotBottomY(), keyboardCanvas, keyboardClearance, scrollable);
            if (delta <= 0f) return;

            var target = Mathf.Clamp01(ownerScroll.verticalNormalizedPosition - delta);
            ownerScroll.verticalNormalizedPosition = Mathf.MoveTowards(
                ownerScroll.verticalNormalizedPosition, target,
                Mathf.Max(0.0001f, Time.unscaledDeltaTime / Mathf.Max(0.01f, liftSmoothTime)));
        }

        // Keeps the raised card pinned to the slot it left behind, so it
        // travels with the form instead of hanging in mid-air.
        private void TrackSlot()
        {
            liftY = SlotBottomY() - slotStartY;
            raisedLayer.anchoredPosition = new Vector2(raisedLayer.anchoredPosition.x, liftY);
        }

        private float SlotBottomY()
        {
            if (placeholder == null || canvasRect == null) return slotStartY;
            placeholder.GetWorldCorners(corners);
            return canvasRect.InverseTransformPoint(corners[0]).y
                   + canvasRect.rect.height * canvasRect.pivot.y;
        }

        private void RestoreKeyboardPadding()
        {
            // Handoff within the same form: the keyboard is staying up, so
            // the padding must survive the Hide/Show pair — restoring it here
            // would shrink the content, clamp the scroll, and make the form
            // jump between fields.
            if (preservePaddingOnce)
            {
                preservePaddingOnce = false;
                return;
            }

            if (!paddingApplied || ownerLayout == null)
            {
                paddingApplied = false;
                return;
            }

            var pad = ownerLayout.padding;
            ownerLayout.padding = new RectOffset(pad.left, pad.right, pad.top, originalBottomPadding);
            // Rebuild the layout whose padding was just restored — NOT
            // originalParent, which by the Show-path call has already been
            // repointed at the new field's form.
            var layoutRect = (RectTransform)ownerLayout.transform;
            if (layoutRect.gameObject.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRect);
            paddingApplied = false;
        }

        // Measures the raised field at rest, in canvas-bottom-relative units.
        // Safe to read immediately: SetParent(worldPositionStays:true) keeps
        // the field's world position, and on raisedLayer no layout group can
        // re-drive it.
        private void CaptureLiftGeometry(RectTransform field)
        {
            restBottomY = 0f;
            maxLift = 0f;
            coveredLatched = false;
            keyboardStableFrames = 0;
            lastKeyboardCanvas = 0f;
            // While the keyboard padding is applied (handoff), padding.bottom
            // reads the PADDED value — recapturing it as "original" would make
            // the eventual restore bake the keyboard height in permanently.
            if (!paddingApplied)
                originalBottomPadding = ownerLayout != null ? ownerLayout.padding.bottom : 0;
            slotStartY = SlotBottomY();
            if (canvasRect == null) return;

            field.GetWorldCorners(corners);
            var pivotOffset = canvasRect.rect.height * canvasRect.pivot.y;
            restBottomY = canvasRect.InverseTransformPoint(corners[0]).y + pivotOffset;
            var restTopY = canvasRect.InverseTransformPoint(corners[1]).y + pivotOffset;

            // Ceiling: RaisedLayer draws above the header and tab bar, so an
            // unclamped lift would slide a tall card (Описание 360, Промпт 540)
            // over them. Use the top of the tab the field came from.
            var ceilingY = CeilingFromOwningTab();
            maxLift = Mathf.Max(0f, ceilingY - restTopY);
        }

        private float CeilingFromOwningTab()
        {
            var tab = originalParent as RectTransform;
            if (tab == null || canvasRect == null) return canvasRect != null ? canvasRect.rect.height : 0f;

            // originalParent is the tab's Content; the tab itself is its
            // parent (it carries the ScrollRect/mask when the tab scrolls).
            var owner = tab.parent as RectTransform ?? tab;
            owner.GetWorldCorners(corners);
            return canvasRect.InverseTransformPoint(corners[1]).y
                   + canvasRect.rect.height * canvasRect.pivot.y;
        }

        private void UpdateCoveredLatch(float keyboardCanvas)
        {
            if (coveredLatched) return;
            if (keyboardCanvas <= 0f)
            {
                keyboardStableFrames = 0;
                lastKeyboardCanvas = 0f;
                return;
            }

            if (Mathf.Abs(keyboardCanvas - lastKeyboardCanvas) < 1f) keyboardStableFrames++;
            else keyboardStableFrames = 0;
            lastKeyboardCanvas = keyboardCanvas;

            if (keyboardStableFrames >= CoveredDecisionFrames && slotStartY < keyboardCanvas)
                coveredLatched = true;
        }

        private void ResetLift()
        {
            liftY = 0f;
            liftVelocity = 0f;
            if (raisedLayer != null)
                raisedLayer.anchoredPosition = new Vector2(raisedLayer.anchoredPosition.x, 0f);
        }
    }
}
