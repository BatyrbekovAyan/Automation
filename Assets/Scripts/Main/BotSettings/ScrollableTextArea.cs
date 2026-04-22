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

        private void OnDestroy()
        {
            if (inputField != null)
                inputField.onValueChanged.RemoveListener(OnTextChanged);
        }

        private void OnTextChanged(string text)
        {
            var previous = content.sizeDelta.y;
            ResizeContent(text);
            if (content.sizeDelta.y > previous)
            {
                // Flush pending Canvas rebuilds so ScrollRect has the new
                // content size before we pin the scroll to the bottom —
                // otherwise verticalNormalizedPosition clamps against the
                // stale bounds and the caret lands below the viewport.
                // Same trick as Chat/ExpandableInput.cs.
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void ResizeContent(string text)
        {
            var width = viewport.rect.width;
            var preferred = inputField.textComponent.GetPreferredValues(MeasureText(text), width, 0f).y;
            var target = Mathf.Max(viewport.rect.height, preferred + bottomPadding);
            content.sizeDelta = new Vector2(content.sizeDelta.x, target);
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
