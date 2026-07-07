using TMPro;
using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Keyboard lift for TALL sheets: raises the panel only far enough that the
/// currently focused input field sits just above the keyboard (plus a small
/// clearance). Unlike KeyboardAwarePanel — which glues the panel's BOTTOM to
/// the keyboard top and shoves a tall sheet's fields way past it — the lift
/// here is relative to the focused field, so each field stops right where
/// the user is typing.
///
/// The host must disable this component while it animates the panel (it
/// writes anchoredPosition.y every frame) and enable it once the panel has
/// settled. OnEnable self-syncs to the panel's current position, so no reset
/// call is needed. Editor: press K in Play Mode to simulate the keyboard.
/// </summary>
public class FocusedFieldKeyboardLift : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private TMP_InputField[] fields;
    [Tooltip("Gap kept between the focused field's bottom and the keyboard top, in canvas units.")]
    [SerializeField] private float clearance = 24f;
    [SerializeField] private float smoothTime = 0.12f;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private float _baseY;
    private float _currentY;
    private float _velocityY;
    private readonly Vector3[] _corners = new Vector3[4];

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
    }

    private void OnEnable()
    {
        // Self-sync: the host may hand us the panel at any settled position.
        if (panel != null) _currentY = panel.anchoredPosition.y;
        _velocityY = 0f;
    }

    private void Update()
    {
        if (panel == null || _canvasRect == null) return;

        float targetY = _baseY + ComputeLift(KeyboardCanvasHeight());

        _currentY = Mathf.SmoothDamp(
            _currentY, targetY, ref _velocityY, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, _currentY);
    }

    private float ComputeLift(float keyboardCanvas)
    {
        if (keyboardCanvas <= 0f || fields == null) return 0f;

        foreach (var field in fields)
        {
            if (field == null || !field.isFocused) continue;
            float fieldBottomAtRest = FieldBottomFromCanvasBottomAtRest((RectTransform)field.transform);
            return Mathf.Max(0f, keyboardCanvas + clearance - fieldBottomAtRest);
        }
        return 0f;
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
