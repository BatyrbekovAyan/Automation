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
        [SerializeField] private float keyboardClearance = 48f;
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

        // Captured once per raise, with the field at rest. The field's own
        // transform is never written (we move raisedLayer instead), so its
        // geometry is constant for the whole raise — no per-frame re-measure,
        // and the lift can't chase its own movement.
        private float restBottomY;
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
            ResetLift();

            field.SetParent(raisedLayer, worldPositionStays: true);
            field.SetAsLastSibling();

            CaptureLiftGeometry(field);

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

            // Must run before the SetParent below, and must live HERE rather
            // than at a call site: Hide() has three entry points (blur, the
            // re-entrant guard in Show, and OnDisable), and the OnDisable path
            // fires with the owning tab already deactivated — no layout
            // rebuild will correct a lift left behind.
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

            var target = KeyboardLiftMath.RequiredLift(
                restBottomY, keyboardCanvas, keyboardClearance, maxLift);

            liftY = Mathf.SmoothDamp(
                liftY, target, ref liftVelocity, liftSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);

            raisedLayer.anchoredPosition = new Vector2(raisedLayer.anchoredPosition.x, liftY);
        }

        // Measures the raised field at rest, in canvas-bottom-relative units.
        // Safe to read immediately: SetParent(worldPositionStays:true) keeps
        // the field's world position, and on raisedLayer no layout group can
        // re-drive it.
        private void CaptureLiftGeometry(RectTransform field)
        {
            restBottomY = 0f;
            maxLift = 0f;
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

        private void ResetLift()
        {
            liftY = 0f;
            liftVelocity = 0f;
            if (raisedLayer != null)
                raisedLayer.anchoredPosition = new Vector2(raisedLayer.anchoredPosition.x, 0f);
        }
    }
}
