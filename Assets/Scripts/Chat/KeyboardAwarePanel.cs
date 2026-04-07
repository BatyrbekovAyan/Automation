using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Attach this to your bottom panel RectTransform.
///
/// Android : panel is glued to the keyboard via live area.y tracking.
/// iOS     : replicates Apple's ~250 ms spring with SmoothDamp.
///
/// Safe area: the bottom safe-zone gap (home bar inset) is subtracted from
/// the rise amount, so it slides under the keyboard and stays invisible.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class KeyboardAwarePanel : MonoBehaviour
{
    [Header("iOS Animation (ignored on Android)")]
    [Tooltip("SmoothDamp time for iOS keyboard spring. 0.12 matches Apple's system keyboard closely.")]
    public float iosSmoothTime = 0.12f;

    // ── private state ──────────────────────────────────────────────
    private RectTransform _panel;
    private Canvas        _canvas;
    private float         _baseY;

    // iOS SmoothDamp state
    private float _currentY;
    private float _velocityY;

    // Editor simulation
#if UNITY_EDITOR
    private bool  _editorKbVisible;
    private float _editorSimulated;
    private const float EditorKbTargetHeight = 400f;
    private const float EditorKbSpeed        = 1400f;
#endif

    // ── lifecycle ──────────────────────────────────────────────────
    void Awake()
    {
        _panel    = GetComponent<RectTransform>();
        _canvas   = GetComponentInParent<Canvas>();
        _baseY    = _panel.anchoredPosition.y;
        _currentY = _baseY;
    }

    void Update()
    {
#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            _editorKbVisible = !_editorKbVisible;
            Debug.Log($"[KeyboardAwarePanel] Simulated keyboard: {(_editorKbVisible ? "visible" : "hidden")}");
        }

        float editorTarget = _editorKbVisible ? EditorKbTargetHeight : 0f;
        _editorSimulated = Mathf.MoveTowards(_editorSimulated, editorTarget,
                                             EditorKbSpeed * Time.unscaledDeltaTime);
        ApplyAndroid(_editorSimulated);

#elif UNITY_ANDROID
        ApplyAndroid(GetAndroidLiveHeight());

#elif UNITY_IOS
        ApplyIOS(GetIOSTargetHeight());

#endif
    }

    // ── platform implementations ───────────────────────────────────

    void ApplyAndroid(float liveKeyboardHeight)
    {
        float offset = ConvertToCanvasSpace(liveKeyboardHeight);
        _panel.anchoredPosition = new Vector2(
            _panel.anchoredPosition.x,
            _baseY + offset
        );
    }

    void ApplyIOS(float targetKeyboardHeight)
    {
        float targetY = _baseY + ConvertToCanvasSpace(targetKeyboardHeight);

        _currentY = Mathf.SmoothDamp(
            _currentY, targetY,
            ref _velocityY,
            iosSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime
        );

        _panel.anchoredPosition = new Vector2(_panel.anchoredPosition.x, _currentY);
    }

    // ── keyboard height readers ────────────────────────────────────

    float GetAndroidLiveHeight()
    {
#if UNITY_ANDROID
        if (!TouchScreenKeyboard.visible) return 0f;
        return Screen.height - TouchScreenKeyboard.area.y;
#else
        return 0f;
#endif
    }

    float GetIOSTargetHeight()
    {
#if UNITY_IOS
        if (!TouchScreenKeyboard.visible) return 0f;
        return TouchScreenKeyboard.area.height;
#else
        return 0f;
#endif
    }

    // ── canvas conversion ──────────────────────────────────────────

    float ConvertToCanvasSpace(float screenPixels)
    {
        if (screenPixels <= 0f) return 0f;

        // Subtract the bottom safe area (home bar / gesture inset) so the panel
        // only rises by the amount that covers NEW screen space.
        // The safe-zone gap slides under the keyboard instead of floating above it.
        float safeBottom = Screen.safeArea.y;           // px — 0 on devices with no home bar
        float adjusted   = Mathf.Max(0f, screenPixels - safeBottom);

        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return adjusted / _canvas.scaleFactor;
        }
        else
        {
            float screenH = Screen.height;
            float canvasH = _canvas.GetComponent<RectTransform>().rect.height;
            return adjusted * (canvasH / screenH);
        }
    }
}
