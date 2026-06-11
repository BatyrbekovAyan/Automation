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
        float rawTextHeight = GetAccurateTextHeight(newText);
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
        }
    }

    // Pushes the message list's bottom edge up so it never hides behind the input panel
    void ApplyScrollRectOffset(float panelHeight)
    {
        if (messageListRect == null) return;

        messageListRect.offsetMin = new Vector2(
            messageListRect.offsetMin.x,
            panelHeight
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

    void OnDestroy()
    {
        if (inputField != null)
            inputField.onValueChanged.RemoveListener(OnTextChanged);
    }
}