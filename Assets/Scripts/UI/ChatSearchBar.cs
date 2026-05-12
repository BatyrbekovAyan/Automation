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
        {
            input.onValueChanged.AddListener(HandleChanged);
            input.onSelect.AddListener(RestoreCaretAlpha);
        }

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
    // alpha the last blink frame drew — if visible, it stays as a static
    // line. Setting the caret graphic's CanvasRenderer alpha to 0 hides it
    // without disabling the GameObject. The companion RestoreCaretAlpha
    // listener (wired to input.onSelect) puts alpha back to 1 on every
    // re-focus, because TMP only updates the caret geometry on refocus —
    // never the CanvasRenderer alpha — so without the restore the caret
    // would render at alpha 0 forever after the first release.
    public void ReleaseFocus()
    {
        if (input == null) return;

        if (input.isFocused)
            input.DeactivateInputField();

        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject == input.gameObject)
            es.SetSelectedGameObject(null);

        SetCaretAlpha(0f);
    }

    private void RestoreCaretAlpha(string _) => SetCaretAlpha(1f);

    private void SetCaretAlpha(float alpha)
    {
        if (input == null) return;
        var graphics = input.GetComponentsInChildren<Graphic>(includeInactive: true);
        for (int i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic.name.IndexOf("caret", StringComparison.OrdinalIgnoreCase) >= 0
                && graphic.canvasRenderer != null)
            {
                graphic.canvasRenderer.SetAlpha(alpha);
            }
        }
    }

    private void OnDisable() => ReleaseFocus();

    private void OnDestroy()
    {
        if (input != null)
        {
            input.onValueChanged.RemoveListener(HandleChanged);
            input.onSelect.RemoveListener(RestoreCaretAlpha);
        }
        if (clearButton != null) clearButton.onClick.RemoveListener(Clear);
    }
}
