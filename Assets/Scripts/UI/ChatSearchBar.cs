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

    // Force-release input focus and tear down TMP's caret children. TMP names
    // the caret GameObject differently across versions ("TMP Input Caret",
    // "Caret", "{InputName} Input Caret") and parents it under either the
    // input field or its text viewport — so we walk every descendant and
    // disable anything matching "caret" by substring. Also clears
    // EventSystem selection so reactivation can't re-focus into us.
    public void ReleaseFocus()
    {
        if (input == null) return;

        if (input.isFocused)
            input.DeactivateInputField();

        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject == input.gameObject)
            es.SetSelectedGameObject(null);

        var descendants = input.GetComponentsInChildren<Transform>(includeInactive: true);
        for (int i = 0; i < descendants.Length; i++)
        {
            var t = descendants[i];
            if (t == input.transform) continue;
            if (t.name.IndexOf("caret", StringComparison.OrdinalIgnoreCase) >= 0)
                t.gameObject.SetActive(false);
        }
    }

    private void OnEnable() => ReleaseFocus();
    private void OnDisable() => ReleaseFocus();

    private void OnDestroy()
    {
        if (input != null) input.onValueChanged.RemoveListener(HandleChanged);
        if (clearButton != null) clearButton.onClick.RemoveListener(Clear);
    }
}
