using UnityEngine;
using TMPro;
using UnityEngine.UI;

[DefaultExecutionOrder(100)]
public class ExpandableInput : MonoBehaviour
{
    [Header("UI References")]
    public RectTransform bottomPanelRect;
    public RectTransform inputFieldRect;
    public ScrollRect scrollRect;
    public TMP_InputField inputField;

    // --- ADD THIS: the RectTransform that wraps your message list ---
    [Header("Layout")]
    public RectTransform messageListRect;

    // When the input is the content of a scroll host that sits under a layout
    // group (attachment preview caption), assign the HOST's LayoutElement here:
    // it tracks the growing input but stops at maxHeight, so the overflow
    // drag-scrolls inside the host via scrollRect — the same structure the
    // messages input uses. Leave null for anchor-driven hosts (messages).
    public LayoutElement inputLayoutElement;

    [Header("Sizing")]
    public float maxHeight = 412;

    private float minHeight;
    private float heightPadding;
    private float baseInputFieldHeight;
    private float singleLineTextHeight;
    private float currentAppliedHeight;
    private float updateThreshold;
    private bool needsFlush = false;

    // --- Suggestions-panel integration (additive; 0 / no subscriber = composer behaviour unchanged) ---
    private float _extraBottomOffset;                        // extra clearance ABOVE the composer (e.g. the suggestions panel height)
    public event System.Action<float> OnPanelHeightChanged;  // emits the clamped composer height whenever it changes
    public float CurrentPanelHeight => bottomPanelRect != null ? bottomPanelRect.rect.height : 0f;
    public float ExtraBottomOffset => _extraBottomOffset;    // current extra clearance (for animating show/hide)

    /// <summary>
    /// Add (or clear with 0) extra bottom clearance above the composer so the message-list floor
    /// rises past a panel that sits on the composer's top edge. Re-pushes the offset immediately.
    /// </summary>
    public void SetExtraBottomOffset(float extra)
    {
        _extraBottomOffset = extra;
        if (bottomPanelRect != null) ApplyScrollRectOffset(bottomPanelRect.rect.height);
    }

    void Start()
    {
        baseInputFieldHeight = inputFieldRect.rect.height;
        minHeight = bottomPanelRect.rect.height;
        heightPadding = minHeight - baseInputFieldHeight;
        singleLineTextHeight = GetAccurateTextHeight("A");
        currentAppliedHeight = baseInputFieldHeight;
        updateThreshold = inputField.textComponent.fontSize * 0.4f;

        inputField.onValueChanged.AddListener(OnTextChanged);

        // Apply correct bottom offset on start so nothing is miscalibrated
        ApplyScrollRectOffset(minHeight);
    }

    void OnTextChanged(string newText)
    {
        float rawTextHeight = GetRenderedTextHeight(newText);
        float textGrowth = Mathf.Max(0f, rawTextHeight - singleLineTextHeight);
        float targetHeight = baseInputFieldHeight + textGrowth;

        if (Mathf.Abs(currentAppliedHeight - targetHeight) > updateThreshold)
        {
            // The input rect always grows with its text — when hosted in a
            // ScrollRect it is the scroll content. In layout-driven mode the
            // host's LayoutElement tracks it but stops at the panel max; past
            // that the overflow drag-scrolls inside the host.
            inputFieldRect.sizeDelta = new Vector2(inputFieldRect.sizeDelta.x, targetHeight);
            if (inputLayoutElement != null)
                inputLayoutElement.preferredHeight = Mathf.Min(targetHeight, maxHeight - heightPadding);

            float targetPanelHeight = targetHeight + heightPadding;
            float clampedHeight = Mathf.Clamp(targetPanelHeight, minHeight, maxHeight);
            bottomPanelRect.sizeDelta = new Vector2(bottomPanelRect.sizeDelta.x, clampedHeight);

            // --- KEEP SCROLL LIST ABOVE THE PANEL ---
            ApplyScrollRectOffset(clampedHeight);

            if (scrollRect != null && targetHeight > currentAppliedHeight && targetPanelHeight > maxHeight)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }

            currentAppliedHeight = targetHeight;
            needsFlush = true;
            OnPanelHeightChanged?.Invoke(clampedHeight);   // notify panels that ride the composer's top edge
        }
    }

    // Pushes the message list's bottom edge up so it never hides behind the input panel
    void ApplyScrollRectOffset(float panelHeight)
    {
        if (messageListRect == null) return;

        messageListRect.offsetMin = new Vector2(
            messageListRect.offsetMin.x,
            panelHeight + _extraBottomOffset
        );
    }

    void LateUpdate()
    {
        if (needsFlush && inputField != null && inputField.textComponent != null)
        {
            RectTransform textRect = inputField.textComponent.rectTransform;
            textRect.offsetMin = new Vector2(textRect.offsetMin.x, 0f);
            textRect.offsetMax = new Vector2(textRect.offsetMax.x, 0f);

            if (inputField.textViewport != null)
            {
                Transform caret = inputField.textViewport.Find("Caret");
                if (caret != null)
                {
                    RectTransform caretRect = caret.GetComponent<RectTransform>();
                    caretRect.offsetMin = new Vector2(caretRect.offsetMin.x, 0f);
                    caretRect.offsetMax = new Vector2(caretRect.offsetMax.x, 0f);
                }
            }

            needsFlush = false;
        }
    }

    private float GetAccurateTextHeight(string textToMeasure)
    {
        if (string.IsNullOrEmpty(textToMeasure)) textToMeasure = "A";
        else if (textToMeasure.EndsWith("\n")) textToMeasure += "A";

        float width = inputFieldRect.rect.width;
        return inputField.textComponent.GetPreferredValues(textToMeasure, width, 0f).y;
    }

    // Measures the composer content height from TMP's ACTUAL line layout: the span from the
    // first line's top (ascender) to the last line's bottom (descender). This equals the real
    // per-line advance summed over the rows, so it matches exactly what TMP renders.
    //
    // NOTE: an earlier version summed per-row glyph-QUAD heights. That was a workaround for
    // when the emoji sprite FaceInfo had ascent/descent = 0 (lines collapsed to ~0). Now the
    // FaceInfo carries real line metrics, so the glyph-quad sum OVER-measures — a glyph quad is
    // a few px TALLER than its line advance (the emoji slightly overflows its line box) — and
    // that error accumulates per row, leaving growing empty padding + top-clipping once the
    // field exceeds maxHeight and scrolls. The line-layout span has no such accumulation.
    private float GetRenderedTextHeight(string newText)
    {
        if (string.IsNullOrEmpty(newText)) return singleLineTextHeight;

        TMP_Text textComponent = inputField.textComponent;
        textComponent.ForceMeshUpdate();
        TMP_TextInfo info = textComponent.textInfo;
        if (info == null || info.lineCount < 1) return singleLineTextHeight;

        float top = info.lineInfo[0].ascender;
        float bottom = info.lineInfo[info.lineCount - 1].descender;
        return Mathf.Max(top - bottom, singleLineTextHeight);
    }

    void OnDestroy()
    {
        if (inputField != null)
            inputField.onValueChanged.RemoveListener(OnTextChanged);
    }
}