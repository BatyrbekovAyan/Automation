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
                scrollRect.verticalNormalizedPosition = 0f;
        }

        private void ResizeContent(string text)
        {
            var width = viewport.rect.width;
            var preferred = inputField.textComponent.GetPreferredValues(text, width, 0f).y;
            var target = Mathf.Max(viewport.rect.height, preferred + bottomPadding);
            content.sizeDelta = new Vector2(content.sizeDelta.x, target);
        }
    }
}
