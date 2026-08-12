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
///
/// Slot tenancy (sketch-003): the suggestions panel can occupy the keyboard's
/// slot while the keyboard is away. <see cref="VirtualBottomInset"/> is that
/// tenant's claim in canvas units; the applied rise is max(keyboard, virtual),
/// so during a keyboard ⇄ panel handoff the larger claim holds the panel still
/// (the no-dip invariant — see SuggestionSlotSwap).
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

    /// <summary>Last computed effective KEYBOARD area in canvas-space pixels (safe-adjusted;
    /// excludes any virtual tenant). Updated every frame.</summary>
    public float EffectiveAreaCanvasPx { get; private set; }

    /// <summary>A non-keyboard slot tenant's claim on the bottom inset, canvas px (0 = none).
    /// The applied rise is max(keyboard, this).</summary>
    public float VirtualBottomInset { get; set; }

    /// <summary>The rise actually applied to the panel THIS frame, canvas px — smoothing and
    /// all. Slot tenants glue their top edge to this so they track the composer exactly.</summary>
    public float AppliedBottomInset => _panel != null ? _panel.anchoredPosition.y - _baseY : 0f;

    /// <summary>True while the native keyboard is up (Editor: the simulated one).</summary>
    public bool NativeKeyboardVisible
    {
        get
        {
#if UNITY_EDITOR
            return _editorKbVisible;
#else
            return TouchScreenKeyboard.visible;
#endif
        }
    }

    /// <summary>The bottom safe-area inset (home bar) in canvas px — what a slot tenant adds
    /// below its content so it fills to the true screen bottom like the keyboard does.</summary>
    public float SafeBottomCanvasPx => RawToCanvas(Screen.safeArea.y);

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
        EffectiveAreaCanvasPx = ConvertToCanvasSpace(_editorSimulated);
        ApplyInstant(EffectiveTarget);

#elif UNITY_ANDROID
        float liveAndroid = GetAndroidLiveHeight();
        EffectiveAreaCanvasPx = ConvertToCanvasSpace(liveAndroid);
        ApplyInstant(EffectiveTarget);

#elif UNITY_IOS
        float targetIos = GetIOSTargetHeight();
        EffectiveAreaCanvasPx = ConvertToCanvasSpace(targetIos);
        ApplyIOS(EffectiveTarget);

#endif
    }

    // The one place keyboard and slot-tenant claims merge (no-dip max rule).
    private float EffectiveTarget => Mathf.Max(EffectiveAreaCanvasPx, VirtualBottomInset);

#if UNITY_EDITOR
    /// <summary>Editor-only device parity: lets slot-swap code drive the simulated keyboard the
    /// way ActivateInputField/DeactivateInputField drive the real one. K still toggles manually.</summary>
    public void SetSimulatedKeyboard(bool visible) => _editorKbVisible = visible;
#endif

    // ── platform implementations ───────────────────────────────────

    void ApplyInstant(float offsetCanvasPx)
    {
        _panel.anchoredPosition = new Vector2(
            _panel.anchoredPosition.x,
            _baseY + offsetCanvasPx
        );
    }

    void ApplyIOS(float offsetCanvasPx)
    {
        float targetY = _baseY + offsetCanvasPx;

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
        return TouchScreenKeyboard.visible ? (Screen.height - TouchScreenKeyboard.area.y) : 0f;
#else
        return 0f;
#endif
    }

    float GetIOSTargetHeight()
    {
#if UNITY_IOS
        return TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0f;
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
        return RawToCanvas(Mathf.Max(0f, screenPixels - safeBottom));
    }

    float RawToCanvas(float screenPixels)
    {
        if (screenPixels <= 0f || _canvas == null) return 0f;

        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return screenPixels / _canvas.scaleFactor;
        }
        else
        {
            float screenH = Screen.height;
            float canvasH = _canvas.GetComponent<RectTransform>().rect.height;
            return screenPixels * (canvasH / screenH);
        }
    }
}
