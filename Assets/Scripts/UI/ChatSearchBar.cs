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

    // Force-release input focus and tear down TMP's caret child. Called both
    // on OnDisable (panel hide) and externally by ChatListView when the user
    // taps a chat — the panel deactivation path alone isn't always enough,
    // since TMP_InputField parents a separate "TMP Input Caret" GameObject
    // that can render independently of the input's focused state.
    public void ReleaseFocus()
    {
        if (input == null) return;

        if (input.isFocused)
            input.DeactivateInputField();

        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject == input.gameObject)
            es.SetSelectedGameObject(null);

        var caret = input.transform.Find("TMP Input Caret");
        if (caret != null) caret.gameObject.SetActive(false);
    }

    private void OnDisable() => ReleaseFocus();

    private void OnDestroy()
    {
        if (input != null) input.onValueChanged.RemoveListener(HandleChanged);
        if (clearButton != null) clearButton.onClick.RemoveListener(Clear);
    }
}
