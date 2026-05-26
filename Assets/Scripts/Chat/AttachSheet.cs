using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class AttachSheet : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Sheet height in canvas pixels — used as fallback when no keyboard height has been observed yet.")]
    [SerializeField] private float sheetHeightCanvasPx = 290f;

    [Header("References")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private KeyboardAwarePanel keyboardPanel;
    [SerializeField] private MessagesBottomPanel messagesBottomPanel;
    [SerializeField] private Button cameraButton;
    [SerializeField] private Button galleryButton;
    [SerializeField] private Button documentButton;

    [Header("Tween Timings")]
    [SerializeField] private float openDuration  = 0.30f;
    [SerializeField] private float closeDuration = 0.25f;

    public event Action<AttachmentPick> OnPicked;

    private RectTransform _rt;
    private Canvas        _canvas;
    private bool          _isOpen;
    private bool          _openedOverKeyboard;
    private bool          _isAnimating;
    private bool          _suppressDeselectListener;
    private Tween         _insetTween;

    void Awake()
    {
        _rt     = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();

        // Debug self-subscriber so wiring is observable in editor without a real consumer.
        OnPicked += pick =>
            Debug.Log($"[AttachSheet] OnPicked: kind={pick.Kind} file={pick.FileName} " +
                      $"size={pick.FileSizeBytes} mime={pick.MimeType} path={pick.Path}");
    }

    void OnEnable()
    {
        if (cameraButton   != null) cameraButton.onClick.AddListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.AddListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.AddListener(OnDocumentTapped);
        if (inputField     != null) inputField.onDeselect.AddListener(OnInputFieldDeselected);
    }

    void OnDisable()
    {
        if (cameraButton   != null) cameraButton.onClick.RemoveListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.RemoveListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.RemoveListener(OnDocumentTapped);
        if (inputField     != null) inputField.onDeselect.RemoveListener(OnInputFieldDeselected);

        _insetTween?.Kill();
    }

    void Update()
    {
        if (keyboardPanel == null) return;
        float area = keyboardPanel.EffectiveAreaCanvasPx;
        float y = -sheetHeightCanvasPx + Mathf.Min(sheetHeightCanvasPx, area);
        _rt.anchoredPosition = new Vector2(0, y);
    }

    public void Toggle()
    {
        if (_isAnimating) return;
        if (_isOpen) Close();
        else         Open();
    }

    public void Open()
    {
        if (_isOpen || _isAnimating) return;
        _isOpen = true;
        Debug.Log("[AttachSheet] Open (stub)");
        // Body filled in Task 7.
    }

    public void Close()
    {
        if (!_isOpen || _isAnimating) return;
        _isOpen = false;
        Debug.Log("[AttachSheet] Close (stub)");
        // Body filled in Task 8.
    }

    private void OnInputFieldDeselected(string _)
    {
        // Open() in Case A deactivates the input field intentionally, which fires onDeselect.
        // The suppression flag prevents that synthetic deselect from recursively closing the sheet.
        if (_suppressDeselectListener) return;
        if (_isOpen) Close();
    }

    // Used by Open() in Case A to swallow the immediate onDeselect that follows DeactivateInputField.
    private System.Collections.IEnumerator ClearSuppressNextFrame()
    {
        yield return null;
        _suppressDeselectListener = false;
    }

    private void OnCameraTapped()   { /* Filled in Task 9. */ }
    private void OnGalleryTapped()  { /* Filled in Task 9. */ }
    private void OnDocumentTapped() { /* Filled in Task 9. */ }
}
