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

    // Release input focus when the chats panel is hidden (e.g. user taps a chat
    // and the SwipeToBack panel-swap deactivates this GameObject). Without
    // this, TMP_InputField stays the EventSystem's selected object and
    // re-spawns its caret child the next time the panel reactivates.
    private void OnDisable()
    {
        if (input != null && input.isFocused)
            input.DeactivateInputField();

        var es = EventSystem.current;
        if (es != null && input != null && es.currentSelectedGameObject == input.gameObject)
            es.SetSelectedGameObject(null);
    }

    private void OnDestroy()
    {
        if (input != null) input.onValueChanged.RemoveListener(HandleChanged);
        if (clearButton != null) clearButton.onClick.RemoveListener(Clear);
    }
}
