using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ChatSearchBar : MonoBehaviour
{
    [SerializeField] private TMP_InputField input;
    [SerializeField] private Button clearButton;
    [SerializeField] private GameObject clearIcon;

    public event Action<string> OnQueryChanged;
    public string CurrentQuery { get; private set; } = "";

    private void Awake()
    {
        if (input != null)
            input.onValueChanged.AddListener(HandleChanged);

        if (clearButton != null)
            clearButton.onClick.AddListener(Clear);

        if (clearIcon != null)
            clearIcon.SetActive(false);
    }

    private void HandleChanged(string raw)
    {
        var trimmed = string.IsNullOrEmpty(raw) ? "" : raw.Trim();

        if (clearIcon != null)
            clearIcon.SetActive(trimmed.Length > 0);

        if (trimmed == CurrentQuery) return; // dedupe noisy IME events
        CurrentQuery = trimmed;
        OnQueryChanged?.Invoke(trimmed);
    }

    public void Clear()
    {
        if (input != null) input.text = "";
    }

    // Force-release input focus. TMP_InputField.DeactivateInputField stops
    // the caret blink coroutine but leaves the caret graphic at whatever
    // alpha the last blink frame drew — if visible, it stays visible as a
    // static line. Set the caret graphic's render alpha to 0 directly.
    // When the user re-focuses, TMP restarts the blink coroutine which
    // overwrites the alpha, so the caret reappears normally.
    public void ReleaseFocus()
    {
        if (input == null) return;

        if (input.isFocused)
            input.DeactivateInputField();

        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject == input.gameObject)
            es.SetSelectedGameObject(null);

        var graphics = input.GetComponentsInChildren<Graphic>(includeInactive: true);
        for (int i = 0; i < graphics.Length; i++)
        {
            var g = graphics[i];
            if (g.name.IndexOf("caret", StringComparison.OrdinalIgnoreCase) >= 0
                && g.canvasRenderer != null)
            {
                g.canvasRenderer.SetAlpha(0f);
            }
        }
    }

    private void OnDisable() => ReleaseFocus();

    private void OnDestroy()
    {
        if (input != null) input.onValueChanged.RemoveListener(HandleChanged);
        if (clearButton != null) clearButton.onClick.RemoveListener(Clear);
    }
}
