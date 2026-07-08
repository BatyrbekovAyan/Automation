using TMPro;
using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Keyboard lift for TALL sheets. Unlike KeyboardAwarePanel — which glues the
/// panel's BOTTOM to the keyboard top and shoves a tall sheet's fields way
/// past it — this raises the panel by a single amount derived from the
/// REFERENCE field (the lowest input): its bottom is placed half-a-field +
/// clearance above the keyboard top. That same lift is used whenever any of
/// the tracked fields is focused, so every field settles at the same height
/// and the lowest one is comfortably clear.
///
/// The host must disable this component while it animates the panel (it
/// writes anchoredPosition.y every frame) and enable it once the panel has
/// settled. OnEnable self-syncs to the panel's current position, so no reset
/// call is needed. Editor: press K in Play Mode to simulate the keyboard.
/// </summary>
public class FocusedFieldKeyboardLift : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [Tooltip("Any of these being focused triggers the lift.")]
    [SerializeField] private TMP_InputField[] fields;
    [Tooltip("Lowest field — the lift is sized so this one clears the keyboard; all fields share its height.")]
    [SerializeField] private TMP_InputField referenceField;
    [Tooltip("Extra gap above the half-field margin, in canvas units.")]
    [SerializeField] private float clearance = 24f;
    [SerializeField] private float smoothTime = 0.12f;
    [Tooltip("Seconds the lift stays held after the LAST frame in which a field was focused " +
             "OR the keyboard was measured up. This absorbs the field-to-field switch: during " +
             "the handoff a field briefly reads unfocused AND/OR TouchScreenKeyboard.visible " +
             "flickers for 1-3 frames, but never both persistently — so as long as either " +
             "signal was seen within this window the panel holds its exact lift and cannot dip. " +
             "Only a genuine dismiss (both signals gone past this window) lowers the panel; the " +
             "host disables this component during the sheet's open/close slide, so that lag " +
             "never fights the tween. Raise it if a slow device still shows a residual dip.")]
    [SerializeField] private float liftHoldSeconds = 0.15f;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private float _baseY;
    private float _currentY;
    private float _velocityY;
    private readonly Vector3[] _corners = new Vector3[4];

    // Field-switch hold. The lift MAGNITUDE is the last positive keyboard height
    // (canvas units), held through any transient zero-reading rather than
    // recomputed to zero. _lastActiveTime is refreshed every frame a field is
    // focused or the keyboard is measured up (and by the onSelect tap pulse), so
    // the hold survives the switch blip and releases only once BOTH signals have
    // been absent for liftHoldSeconds.
    private float _heldKeyboardCanvas;
    private float _lastActiveTime = float.NegativeInfinity;

#if UNITY_EDITOR
    private bool _editorKbVisible;
    private float _editorSimulated;
    private const float EditorKbTargetHeight = 400f;
    private const float EditorKbSpeed = 1400f;
#endif

    private void Awake()
    {
        var localCanvas = GetComponentInParent<Canvas>();
        if (localCanvas != null)
        {
            _canvas = localCanvas.rootCanvas;
            _canvasRect = _canvas.GetComponent<RectTransform>();
        }
        if (panel != null) _baseY = panel.anchoredPosition.y;
        _currentY = _baseY;

        // A field's onSelect fires the instant the user taps it — a more reliable
        // "a field is active" pulse than polling isFocused, which on iOS can read
        // false for a frame or two around the activation handoff during a switch.
        if (fields != null)
            foreach (var field in fields)
                if (field != null) field.onSelect.AddListener(HandleFieldSelected);
    }

    private void OnDestroy()
    {
        if (fields != null)
            foreach (var field in fields)
                if (field != null) field.onSelect.RemoveListener(HandleFieldSelected);
    }

    private void HandleFieldSelected(string _) => _lastActiveTime = Time.unscaledTime;

    private void OnEnable()
    {
        // Self-sync: the host may hand us the panel at any settled position.
        if (panel != null) _currentY = panel.anchoredPosition.y;
        _velocityY = 0f;

        // Fresh start each time the sheet re-enables the lift after its slide-in
        // (the host disables it during the slide, and never destroys it between
        // sheets) — otherwise a stale held height / active-time from the previous
        // open could lift the freshly-settled panel before the keyboard reappears.
        _heldKeyboardCanvas = 0f;
        _lastActiveTime = float.NegativeInfinity;
    }

    private void Update()
    {
        if (panel == null || _canvasRect == null) return;

        // Two independent "the keyboard should be up" signals, EITHER of which
        // keeps the lift alive: a field is focused, or the OS still measures a
        // keyboard. During a field switch both briefly flicker but never together,
        // so refreshing _lastActiveTime from either bridges the whole handoff —
        // and because the lift magnitude is the HELD last-positive height (never
        // overwritten by a zero reading), the panel holds a constant target
        // throughout and cannot dip regardless of how long the blip lasts.
        float rawKeyboard = KeyboardCanvasHeight();
        if (rawKeyboard > 0f)
        {
            _heldKeyboardCanvas = rawKeyboard;
            _lastActiveTime = Time.unscaledTime;
        }
        if (AnyFieldFocused())
            _lastActiveTime = Time.unscaledTime;

        bool holding = Time.unscaledTime - _lastActiveTime <= liftHoldSeconds;
        if (!holding) _heldKeyboardCanvas = 0f;  // genuine dismiss — reset for the next focus

        float targetY = _baseY + ComputeLift(holding ? _heldKeyboardCanvas : 0f);

        _currentY = Mathf.SmoothDamp(
            _currentY, targetY, ref _velocityY, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, _currentY);
    }

    // Sizes the lift for the given keyboard height. Whether the panel SHOULD be
    // lifted (focus/keyboard hold) is decided in Update — this is pure geometry.
    private float ComputeLift(float keyboardCanvas)
    {
        if (keyboardCanvas <= 0f || referenceField == null) return 0f;

        // Place the reference (lowest) field's bottom half-a-field + clearance
        // above the keyboard top. Same lift for every field, so they all
        // settle at the same height and the lowest one is fully visible.
        var refRt = (RectTransform)referenceField.transform;
        float refBottomAtRest = FieldBottomFromCanvasBottomAtRest(refRt);
        float margin = refRt.rect.height * 0.5f + clearance;
        return Mathf.Max(0f, keyboardCanvas + margin - refBottomAtRest);
    }

    private bool AnyFieldFocused()
    {
        if (fields == null) return false;
        foreach (var field in fields)
            if (field != null && field.isFocused) return true;
        return false;
    }

    // The focused field's bottom edge measured from the canvas bottom with the
    // panel at rest — current lift is subtracted out so the target doesn't
    // chase its own movement.
    private float FieldBottomFromCanvasBottomAtRest(RectTransform field)
    {
        field.GetWorldCorners(_corners); // [0] = bottom-left, world space
        Vector3 local = _canvasRect.InverseTransformPoint(_corners[0]);
        float fromBottom = local.y + _canvasRect.rect.height * _canvasRect.pivot.y;
        float currentLift = panel.anchoredPosition.y - _baseY;
        return fromBottom - currentLift;
    }

    private float KeyboardCanvasHeight()
    {
#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            _editorKbVisible = !_editorKbVisible;
        float editorTarget = _editorKbVisible ? EditorKbTargetHeight : 0f;
        _editorSimulated = Mathf.MoveTowards(
            _editorSimulated, editorTarget, EditorKbSpeed * Time.unscaledDeltaTime);
        return ConvertToCanvasSpace(_editorSimulated);
#elif UNITY_ANDROID
        float live = TouchScreenKeyboard.visible ? (Screen.height - TouchScreenKeyboard.area.y) : 0f;
        return ConvertToCanvasSpace(live);
#elif UNITY_IOS
        float target = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0f;
        return ConvertToCanvasSpace(target);
#else
        return 0f;
#endif
    }

    // Mirrors KeyboardAwarePanel: bottom safe-area inset slides under the
    // keyboard instead of padding the lift.
    private float ConvertToCanvasSpace(float screenPixels)
    {
        if (screenPixels <= 0f) return 0f;

        float safeBottom = Screen.safeArea.y;
        float adjusted = Mathf.Max(0f, screenPixels - safeBottom);

        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return adjusted / _canvas.scaleFactor;

        return adjusted * (_canvasRect.rect.height / Screen.height);
    }
}
