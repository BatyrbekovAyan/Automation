using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Fixed-height multiline input with touch-drag scrolling. Attach
    /// alongside EditableTextArea on a card whose TMP_InputField hosts a
    /// ScrollRect over the TMP textViewport / textComponent. Resizes the
    /// scroll content to measured text height and auto-scrolls to the caret
    /// as text grows. Mirrors the GetPreferredValues pattern in
    /// Chat/ExpandableInput.cs.
    /// </summary>
    [RequireComponent(typeof(EditableTextArea))]
    public class ScrollableTextArea : MonoBehaviour
    {
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private RectTransform content;
        [SerializeField] private float bottomPadding = 8f;

        private RectTransform viewport;
        private bool scrollSnapPending;

        private void Awake()
        {
            if (scrollRect == null || inputField == null || content == null)
            {
                Debug.LogError($"[ScrollableTextArea] Missing references on {name}.");
                return;
            }

            viewport = scrollRect.viewport;
            inputField.onValueChanged.AddListener(OnTextChanged);
            ResizeContent(inputField.text);
        }

        private void OnEnable()
        {
            // Re-measure whenever the tab activates: text loaded while the
            // card was inactive never triggered onValueChanged, and the very
            // first activation measures against a ~2px pre-layout width
            // (known ScrollRect measure-timing gotcha) — wait for the real
            // width before sizing, or long text can end up unscrollable.
            if (viewport != null)
                StartCoroutine(ResizeWhenWidthSettles());
        }

        private void OnDisable()
        {
            // The snap coroutine died with the deactivation — reset its latch.
            scrollSnapPending = false;
        }

        private IEnumerator ResizeWhenWidthSettles()
        {
            for (var i = 0; i < 60 && !ScrollableTextAreaMetrics.WidthSettled(TextLayoutWidth()); i++)
                yield return null;
            if (ScrollableTextAreaMetrics.WidthSettled(TextLayoutWidth()))
                ResizeContent(inputField.text);
        }

        // The column the text actually wraps in: TMP's text rect minus its
        // horizontal margins — narrower than the card by Text Area's inset.
        private float TextLayoutWidth()
        {
            var text = inputField.textComponent;
            if (text == null) return 0f;
            var margin = text.margin;
            return text.rectTransform.rect.width - margin.x - margin.z;
        }

        private void OnDestroy()
        {
            if (inputField != null)
                inputField.onValueChanged.RemoveListener(OnTextChanged);
        }

        private void OnTextChanged(string text)
        {
            var previous = content.sizeDelta.y;
            ResizeContent(text);
            if (content.sizeDelta.y > previous && !scrollSnapPending && isActiveAndEnabled)
            {
                // Defer the snap to the next frame. TMP_InputField fires
                // onValueChanged from inside its Rebuild pass; calling
                // Canvas.ForceUpdateCanvases or touching the ScrollRect
                // content here re-enters the rebuild and the caret graphic
                // throws "already inside a graphic rebuild loop".
                scrollSnapPending = true;
                StartCoroutine(SnapToBottomNextFrame());
            }
        }

        private IEnumerator SnapToBottomNextFrame()
        {
            yield return null; // Let TMP finish this frame's rebuild cycle.
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f;
            scrollSnapPending = false;
        }

        private void ResizeContent(string text)
        {
            var width = ScrollableTextAreaMetrics.MeasureWidth(TextLayoutWidth(), viewport.rect.width);
            var preferred = inputField.textComponent.GetPreferredValues(MeasureText(text), width, 0f).y;
            var target = ScrollableTextAreaMetrics.ContentHeight(
                preferred, TextAreaChromeHeight(), bottomPadding, viewport.rect.height);
            content.sizeDelta = new Vector2(content.sizeDelta.x, target);
        }

        // Vertical space the content spends on things that are not the text
        // column: TMP's Text Area is stretch-anchored inside the content and
        // inset from it, so this difference is constant as the content grows.
        // Sizing the content without it leaves the text viewport shorter than
        // the text and hands scrolling to TMP, which strands the first row.
        private float TextAreaChromeHeight()
        {
            var textArea = inputField.textViewport;
            if (textArea == null) return 0f;

            var margin = inputField.textComponent != null ? inputField.textComponent.margin : Vector4.zero;
            return Mathf.Max(0f, content.rect.height - textArea.rect.height) + margin.y + margin.w;
        }

        // TMPro.GetPreferredValues drops a trailing empty line from its
        // measurement. On a filled card, pressing Enter produces "...\n" —
        // same measured height as before — so content stays too short and
        // the ScrollRect elastic-snaps the caret back to the previous line.
        // Appending a stub character forces that last empty line to count.
        // Mirrors Chat/ExpandableInput.GetAccurateTextHeight.
        private static string MeasureText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "A";
            if (text.EndsWith("\n")) return text + "A";
            return text;
        }
    }
}
